#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <algorithm>
#include <cstddef>
#include <cmath>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

#include "../third_party/psd_sdk/src/Psd/Psd.h"
#include "../third_party/psd_sdk/src/Psd/PsdMallocAllocator.h"
#include "../third_party/psd_sdk/src/Psd/PsdNativeFile.h"
#include "../third_party/psd_sdk/src/Psd/PsdDocument.h"
#include "../third_party/psd_sdk/src/Psd/PsdLayer.h"
#include "../third_party/psd_sdk/src/Psd/PsdChannel.h"
#include "../third_party/psd_sdk/src/Psd/PsdChannelType.h"
#include "../third_party/psd_sdk/src/Psd/PsdLayerMaskSection.h"
#include "../third_party/psd_sdk/src/Psd/PsdLayerType.h"
#include "../third_party/psd_sdk/src/Psd/PsdColorMode.h"
#include "../third_party/psd_sdk/src/Psd/PsdImageDataSection.h"
#include "../third_party/psd_sdk/src/Psd/PsdPlanarImage.h"
#include "../third_party/psd_sdk/src/Psd/PsdParseDocument.h"
#include "../third_party/psd_sdk/src/Psd/PsdParseImageDataSection.h"
#include "../third_party/psd_sdk/src/Psd/PsdParseLayerMaskSection.h"

#if defined(_WIN32)
#define PSD_LAYER_API extern "C" __declspec(dllexport)
#else
#define PSD_LAYER_API extern "C"
#endif

namespace
{
    constexpr int PSD_LAYER_OK = 0;
    constexpr int PSD_LAYER_OPEN_FAILED = 1;
    constexpr int PSD_LAYER_PARSE_FAILED = 2;
    constexpr int PSD_LAYER_NO_PREVIEW = 3;
    constexpr int PSD_LAYER_UNSUPPORTED = 4;
    constexpr int PSD_LAYER_EXCEPTION = 100;

    std::string EscapeJson(const char* value)
    {
        if (!value)
        {
            return "";
        }

        std::ostringstream output;
        for (const unsigned char ch : std::string(value))
        {
            switch (ch)
            {
            case '\\':
                output << "\\\\";
                break;
            case '"':
                output << "\\\"";
                break;
            case '\b':
                output << "\\b";
                break;
            case '\f':
                output << "\\f";
                break;
            case '\n':
                output << "\\n";
                break;
            case '\r':
                output << "\\r";
                break;
            case '\t':
                output << "\\t";
                break;
            default:
                if (ch < 0x20)
                {
                    output << "\\u";
                    const char* digits = "0123456789abcdef";
                    output << '0' << '0' << digits[(ch >> 4) & 0xF] << digits[ch & 0xF];
                }
                else
                {
                    output << ch;
                }
                break;
            }
        }

        return output.str();
    }

    std::string Utf16ToUtf8(const uint16_t* value)
    {
        if (!value)
        {
            return "";
        }

        std::string output;
        for (size_t i = 0u; value[i] != 0u; ++i)
        {
            uint32_t codePoint = value[i];
            if (codePoint >= 0xD800u && codePoint <= 0xDBFFu)
            {
                const uint16_t lowSurrogate = value[i + 1u];
                if (lowSurrogate >= 0xDC00u && lowSurrogate <= 0xDFFFu)
                {
                    codePoint = 0x10000u + (((codePoint - 0xD800u) << 10u) | (lowSurrogate - 0xDC00u));
                    ++i;
                }
                else
                {
                    codePoint = 0xFFFDu;
                }
            }
            else if (codePoint >= 0xDC00u && codePoint <= 0xDFFFu)
            {
                codePoint = 0xFFFDu;
            }

            if (codePoint <= 0x7Fu)
            {
                output.push_back(static_cast<char>(codePoint));
            }
            else if (codePoint <= 0x7FFu)
            {
                output.push_back(static_cast<char>(0xC0u | (codePoint >> 6u)));
                output.push_back(static_cast<char>(0x80u | (codePoint & 0x3Fu)));
            }
            else if (codePoint <= 0xFFFFu)
            {
                output.push_back(static_cast<char>(0xE0u | (codePoint >> 12u)));
                output.push_back(static_cast<char>(0x80u | ((codePoint >> 6u) & 0x3Fu)));
                output.push_back(static_cast<char>(0x80u | (codePoint & 0x3Fu)));
            }
            else
            {
                output.push_back(static_cast<char>(0xF0u | (codePoint >> 18u)));
                output.push_back(static_cast<char>(0x80u | ((codePoint >> 12u) & 0x3Fu)));
                output.push_back(static_cast<char>(0x80u | ((codePoint >> 6u) & 0x3Fu)));
                output.push_back(static_cast<char>(0x80u | (codePoint & 0x3Fu)));
            }
        }

        return output;
    }

    std::string LayerNameUtf8(const psd::Layer& layer)
    {
        const std::string utf16Name = Utf16ToUtf8(layer.utf16Name);
        if (!utf16Name.empty())
        {
            return EscapeJson(utf16Name.c_str());
        }

        return EscapeJson(layer.name.c_str());
    }

    bool IsGroupLayer(const psd::Layer& layer)
    {
        return layer.type == psd::layerType::OPEN_FOLDER ||
            layer.type == psd::layerType::CLOSED_FOLDER;
    }

    unsigned int FindExtractedChannel(const psd::Layer& layer, int16_t channelType)
    {
        for (unsigned int i = 0; i < layer.channelCount; ++i)
        {
            const psd::Channel& channel = layer.channels[i];
            if (channel.data && channel.type == channelType)
            {
                return i;
            }
        }

        return UINT_MAX;
    }

    char* CopyToHeap(const std::string& text)
    {
        char* buffer = static_cast<char*>(std::malloc(text.size() + 1u));
        if (!buffer)
        {
            return nullptr;
        }

        std::memcpy(buffer, text.c_str(), text.size() + 1u);
        return buffer;
    }

    void SetError(char** errorUtf8, const std::string& message)
    {
        if (errorUtf8)
        {
            *errorUtf8 = CopyToHeap(message);
        }
    }

    std::string BuildDocumentJson(const psd::Document* document, const psd::LayerMaskSection* layerMaskSection)
    {
        std::ostringstream json;
        json << "{";
        json << "\"width\":" << document->width << ",";
        json << "\"height\":" << document->height << ",";
        json << "\"bitsPerChannel\":" << document->bitsPerChannel << ",";
        json << "\"channelCount\":" << document->channelCount << ",";
        json << "\"colorMode\":" << document->colorMode << ",";
        json << "\"layers\":[";

        if (layerMaskSection)
        {
            for (unsigned int i = 0; i < layerMaskSection->layerCount; ++i)
            {
                const psd::Layer& layer = layerMaskSection->layers[i];
                if (i != 0u)
                {
                    json << ",";
                }

                json << "{";
                json << "\"layerId\":" << i << ",";
                json << "\"parentLayerId\":";
                if (layer.parent)
                {
                    const ptrdiff_t parentIndex = layer.parent - layerMaskSection->layers;
                    if (parentIndex >= 0 && parentIndex < static_cast<ptrdiff_t>(layerMaskSection->layerCount))
                    {
                        json << parentIndex;
                    }
                    else
                    {
                        json << "null";
                    }
                }
                else
                {
                    json << "null";
                }
                json << ",";
                json << "\"name\":\"" << LayerNameUtf8(layer) << "\",";
                json << "\"type\":" << layer.type << ",";
                json << "\"isGroup\":" << (IsGroupLayer(layer) ? "true" : "false") << ",";
                json << "\"isVisible\":" << (layer.isVisible ? "true" : "false") << ",";
                json << "\"left\":" << layer.left << ",";
                json << "\"top\":" << layer.top << ",";
                json << "\"right\":" << layer.right << ",";
                json << "\"bottom\":" << layer.bottom << ",";
                json << "\"width\":" << (layer.right - layer.left) << ",";
                json << "\"height\":" << (layer.bottom - layer.top);
                json << "}";
            }
        }

        json << "]}";
        return json.str();
    }

    uint8_t ToPreviewByte(uint8_t value)
    {
        return value;
    }

    uint8_t ToPreviewByte(uint16_t value)
    {
        const uint32_t clamped = std::min<uint32_t>(value, 32768u);
        return static_cast<uint8_t>((clamped * 255u + 16384u) / 32768u);
    }

    uint8_t ToPreviewByte(float32_t value)
    {
        if (!std::isfinite(value))
        {
            return 0u;
        }

        const float32_t clamped = std::clamp(value, 0.0f, 1.0f);
        return static_cast<uint8_t>(clamped * 255.0f + 0.5f);
    }

    template <typename T>
    void ConvertPlanarRgbToRgba8(const psd::ImageDataSection* imageData, bool hasAlpha, unsigned int width, unsigned int height, uint8_t* rgba)
    {
        const T* r = static_cast<const T*>(imageData->images[0].data);
        const T* g = static_cast<const T*>(imageData->images[1].data);
        const T* b = static_cast<const T*>(imageData->images[2].data);
        const T* a = hasAlpha ? static_cast<const T*>(imageData->images[3].data) : nullptr;
        const size_t pixelCount = static_cast<size_t>(width) * static_cast<size_t>(height);

        for (size_t i = 0; i < pixelCount; ++i)
        {
            const size_t offset = i * 4u;
            rgba[offset] = ToPreviewByte(r[i]);
            rgba[offset + 1u] = ToPreviewByte(g[i]);
            rgba[offset + 2u] = ToPreviewByte(b[i]);
            rgba[offset + 3u] = a ? ToPreviewByte(a[i]) : 255u;
        }
    }

    template <typename T>
    void ConvertLayerChannelsToRgba8(
        const psd::Layer& layer,
        unsigned int indexR,
        unsigned int indexG,
        unsigned int indexB,
        unsigned int indexA,
        unsigned int width,
        unsigned int height,
        uint8_t* rgba)
    {
        const T* r = static_cast<const T*>(layer.channels[indexR].data);
        const T* g = static_cast<const T*>(layer.channels[indexG].data);
        const T* b = static_cast<const T*>(layer.channels[indexB].data);
        const T* a = indexA != UINT_MAX ? static_cast<const T*>(layer.channels[indexA].data) : nullptr;
        const size_t pixelCount = static_cast<size_t>(width) * static_cast<size_t>(height);

        for (size_t i = 0; i < pixelCount; ++i)
        {
            const uint8_t maskAlpha = a ? ToPreviewByte(a[i]) : 255u;
            const uint8_t opacityAlpha = static_cast<uint8_t>((static_cast<unsigned int>(maskAlpha) * layer.opacity + 127u) / 255u);
            const size_t offset = i * 4u;
            rgba[offset] = ToPreviewByte(r[i]);
            rgba[offset + 1u] = ToPreviewByte(g[i]);
            rgba[offset + 2u] = ToPreviewByte(b[i]);
            rgba[offset + 3u] = opacityAlpha;
        }
    }

    uint8_t* CropToVisiblePixels(
        uint8_t* source,
        unsigned int sourceWidth,
        unsigned int sourceHeight,
        int& left,
        int& top,
        int& width,
        int& height)
    {
        unsigned int minX = sourceWidth;
        unsigned int minY = sourceHeight;
        unsigned int maxX = 0u;
        unsigned int maxY = 0u;
        bool hasVisiblePixel = false;

        for (unsigned int y = 0u; y < sourceHeight; ++y)
        {
            for (unsigned int x = 0u; x < sourceWidth; ++x)
            {
                const size_t offset = (static_cast<size_t>(y) * sourceWidth + x) * 4u;
                if (source[offset + 3u] == 0u)
                {
                    continue;
                }

                hasVisiblePixel = true;
                minX = std::min(minX, x);
                minY = std::min(minY, y);
                maxX = std::max(maxX, x);
                maxY = std::max(maxY, y);
            }
        }

        if (!hasVisiblePixel)
        {
            return nullptr;
        }

        const unsigned int croppedWidth = maxX - minX + 1u;
        const unsigned int croppedHeight = maxY - minY + 1u;
        if (croppedWidth == sourceWidth && croppedHeight == sourceHeight)
        {
            width = static_cast<int>(sourceWidth);
            height = static_cast<int>(sourceHeight);
            return source;
        }

        const size_t croppedByteCount = static_cast<size_t>(croppedWidth) * static_cast<size_t>(croppedHeight) * 4u;
        uint8_t* cropped = static_cast<uint8_t*>(std::malloc(croppedByteCount));
        if (!cropped)
        {
            return nullptr;
        }

        for (unsigned int y = 0u; y < croppedHeight; ++y)
        {
            const size_t sourceOffset = (static_cast<size_t>(y + minY) * sourceWidth + minX) * 4u;
            const size_t targetOffset = static_cast<size_t>(y) * croppedWidth * 4u;
            std::memcpy(cropped + targetOffset, source + sourceOffset, static_cast<size_t>(croppedWidth) * 4u);
        }

        std::free(source);
        left += static_cast<int>(minX);
        top += static_cast<int>(minY);
        width = static_cast<int>(croppedWidth);
        height = static_cast<int>(croppedHeight);
        return cropped;
    }

    void ValidatePreviewPlanes(const psd::ImageDataSection* imageData, bool hasAlpha)
    {
        if (!imageData || imageData->imageCount < 3u || !imageData->images)
        {
            throw std::runtime_error("PSD merged preview does not contain RGB channels.");
        }

        const unsigned int requiredChannelCount = hasAlpha ? 4u : 3u;
        if (imageData->imageCount < requiredChannelCount)
        {
            throw std::runtime_error("PSD merged preview alpha channel is missing.");
        }

        for (unsigned int i = 0; i < requiredChannelCount; ++i)
        {
            if (!imageData->images[i].data)
            {
                throw std::runtime_error("PSD merged preview contains an empty channel.");
            }
        }
    }
}

PSD_LAYER_API int psd_layer_load_document_json(const wchar_t* path, char** jsonUtf8, char** errorUtf8)
{
    if (jsonUtf8)
    {
        *jsonUtf8 = nullptr;
    }

    if (errorUtf8)
    {
        *errorUtf8 = nullptr;
    }

    if (!path || !jsonUtf8)
    {
        SetError(errorUtf8, "Invalid arguments.");
        return PSD_LAYER_EXCEPTION;
    }

    try
    {
        psd::MallocAllocator allocator;
        psd::NativeFile file(&allocator);
        if (!file.OpenRead(path))
        {
            SetError(errorUtf8, "Cannot open PSD file.");
            return PSD_LAYER_OPEN_FAILED;
        }

        psd::Document* document = psd::CreateDocument(&file, &allocator);
        if (!document)
        {
            file.Close();
            SetError(errorUtf8, "Cannot parse PSD document header.");
            return PSD_LAYER_PARSE_FAILED;
        }

        psd::LayerMaskSection* layerMaskSection = psd::ParseLayerMaskSection(document, &file, &allocator);
        const std::string json = BuildDocumentJson(document, layerMaskSection);

        if (layerMaskSection)
        {
            psd::DestroyLayerMaskSection(layerMaskSection, &allocator);
        }

        psd::DestroyDocument(document, &allocator);
        file.Close();

        *jsonUtf8 = CopyToHeap(json);
        return PSD_LAYER_OK;
    }
    catch (const std::exception& ex)
    {
        SetError(errorUtf8, ex.what());
        return PSD_LAYER_EXCEPTION;
    }
    catch (...)
    {
        SetError(errorUtf8, "Unknown native PSD exception.");
        return PSD_LAYER_EXCEPTION;
    }
}

PSD_LAYER_API void psd_layer_free(void* ptr)
{
    std::free(ptr);
}

PSD_LAYER_API int psd_layer_load_merged_preview_rgba(
    const wchar_t* path,
    uint8_t** rgba,
    int* width,
    int* height,
    char** errorUtf8)
{
    if (rgba)
    {
        *rgba = nullptr;
    }

    if (width)
    {
        *width = 0;
    }

    if (height)
    {
        *height = 0;
    }

    if (errorUtf8)
    {
        *errorUtf8 = nullptr;
    }

    if (!path || !rgba || !width || !height)
    {
        SetError(errorUtf8, "Invalid arguments.");
        return PSD_LAYER_EXCEPTION;
    }

    try
    {
        psd::MallocAllocator allocator;
        psd::NativeFile file(&allocator);
        if (!file.OpenRead(path))
        {
            SetError(errorUtf8, "Cannot open PSD file.");
            return PSD_LAYER_OPEN_FAILED;
        }

        psd::Document* document = psd::CreateDocument(&file, &allocator);
        if (!document)
        {
            file.Close();
            SetError(errorUtf8, "Cannot parse PSD document header.");
            return PSD_LAYER_PARSE_FAILED;
        }

        if (document->colorMode != psd::colorMode::RGB)
        {
            psd::DestroyDocument(document, &allocator);
            file.Close();
            SetError(errorUtf8, "Merged preview currently supports RGB PSD documents.");
            return PSD_LAYER_UNSUPPORTED;
        }

        if (document->imageDataSection.length == 0)
        {
            psd::DestroyDocument(document, &allocator);
            file.Close();
            SetError(errorUtf8, "PSD file does not contain a merged compatibility preview.");
            return PSD_LAYER_NO_PREVIEW;
        }

        bool hasTransparencyMask = false;
        psd::LayerMaskSection* layerMaskSection = psd::ParseLayerMaskSection(document, &file, &allocator);
        if (layerMaskSection)
        {
            hasTransparencyMask = layerMaskSection->hasTransparencyMask;
        }

        psd::ImageDataSection* imageData = psd::ParseImageDataSection(document, &file, &allocator);
        if (!imageData)
        {
            if (layerMaskSection)
            {
                psd::DestroyLayerMaskSection(layerMaskSection, &allocator);
            }

            psd::DestroyDocument(document, &allocator);
            file.Close();
            SetError(errorUtf8, "Cannot parse PSD merged image data.");
            return PSD_LAYER_PARSE_FAILED;
        }

        const bool hasAlpha = imageData->imageCount >= 4u && hasTransparencyMask;
        ValidatePreviewPlanes(imageData, hasAlpha);

        const unsigned int documentWidth = document->width;
        const unsigned int documentHeight = document->height;
        const size_t byteCount = static_cast<size_t>(documentWidth) * static_cast<size_t>(documentHeight) * 4u;
        uint8_t* preview = static_cast<uint8_t*>(std::malloc(byteCount));
        if (!preview)
        {
            psd::DestroyImageDataSection(imageData, &allocator);
            if (layerMaskSection)
            {
                psd::DestroyLayerMaskSection(layerMaskSection, &allocator);
            }

            psd::DestroyDocument(document, &allocator);
            file.Close();
            SetError(errorUtf8, "Cannot allocate PSD preview buffer.");
            return PSD_LAYER_EXCEPTION;
        }

        switch (document->bitsPerChannel)
        {
        case 8:
            ConvertPlanarRgbToRgba8<uint8_t>(imageData, hasAlpha, documentWidth, documentHeight, preview);
            break;
        case 16:
            ConvertPlanarRgbToRgba8<uint16_t>(imageData, hasAlpha, documentWidth, documentHeight, preview);
            break;
        case 32:
            ConvertPlanarRgbToRgba8<float32_t>(imageData, hasAlpha, documentWidth, documentHeight, preview);
            break;
        default:
            std::free(preview);
            psd::DestroyImageDataSection(imageData, &allocator);
            if (layerMaskSection)
            {
                psd::DestroyLayerMaskSection(layerMaskSection, &allocator);
            }

            psd::DestroyDocument(document, &allocator);
            file.Close();
            SetError(errorUtf8, "Unsupported PSD bits per channel.");
            return PSD_LAYER_UNSUPPORTED;
        }

        psd::DestroyImageDataSection(imageData, &allocator);
        if (layerMaskSection)
        {
            psd::DestroyLayerMaskSection(layerMaskSection, &allocator);
        }

        *rgba = preview;
        *width = static_cast<int>(documentWidth);
        *height = static_cast<int>(documentHeight);

        psd::DestroyDocument(document, &allocator);
        file.Close();
        return PSD_LAYER_OK;
    }
    catch (const std::exception& ex)
    {
        SetError(errorUtf8, ex.what());
        return PSD_LAYER_EXCEPTION;
    }
    catch (...)
    {
        SetError(errorUtf8, "Unknown native PSD preview exception.");
        return PSD_LAYER_EXCEPTION;
    }
}

PSD_LAYER_API int psd_layer_load_layer_rgba(
    const wchar_t* path,
    int layerId,
    uint8_t** rgba,
    int* left,
    int* top,
    int* width,
    int* height,
    char** errorUtf8)
{
    if (rgba)
    {
        *rgba = nullptr;
    }

    if (left)
    {
        *left = 0;
    }

    if (top)
    {
        *top = 0;
    }

    if (width)
    {
        *width = 0;
    }

    if (height)
    {
        *height = 0;
    }

    if (errorUtf8)
    {
        *errorUtf8 = nullptr;
    }

    if (!path || !rgba || !left || !top || !width || !height || layerId < 0)
    {
        SetError(errorUtf8, "Invalid arguments.");
        return PSD_LAYER_EXCEPTION;
    }

    try
    {
        psd::MallocAllocator allocator;
        psd::NativeFile file(&allocator);
        if (!file.OpenRead(path))
        {
            SetError(errorUtf8, "Cannot open PSD file.");
            return PSD_LAYER_OPEN_FAILED;
        }

        psd::Document* document = psd::CreateDocument(&file, &allocator);
        if (!document)
        {
            file.Close();
            SetError(errorUtf8, "Cannot parse PSD document header.");
            return PSD_LAYER_PARSE_FAILED;
        }

        if (document->colorMode != psd::colorMode::RGB)
        {
            psd::DestroyDocument(document, &allocator);
            file.Close();
            SetError(errorUtf8, "Layer preview currently supports RGB PSD documents.");
            return PSD_LAYER_UNSUPPORTED;
        }

        psd::LayerMaskSection* layerMaskSection = psd::ParseLayerMaskSection(document, &file, &allocator);
        if (!layerMaskSection || static_cast<unsigned int>(layerId) >= layerMaskSection->layerCount)
        {
            if (layerMaskSection)
            {
                psd::DestroyLayerMaskSection(layerMaskSection, &allocator);
            }

            psd::DestroyDocument(document, &allocator);
            file.Close();
            SetError(errorUtf8, "PSD layer id is out of range.");
            return PSD_LAYER_PARSE_FAILED;
        }

        psd::Layer& layer = layerMaskSection->layers[layerId];
        if (IsGroupLayer(layer) || layer.right <= layer.left || layer.bottom <= layer.top)
        {
            psd::DestroyLayerMaskSection(layerMaskSection, &allocator);
            psd::DestroyDocument(document, &allocator);
            file.Close();
            SetError(errorUtf8, "PSD layer does not contain raster pixels.");
            return PSD_LAYER_UNSUPPORTED;
        }

        psd::ExtractLayer(document, &file, &allocator, &layer);
        const unsigned int indexR = FindExtractedChannel(layer, psd::channelType::R);
        const unsigned int indexG = FindExtractedChannel(layer, psd::channelType::G);
        const unsigned int indexB = FindExtractedChannel(layer, psd::channelType::B);
        const unsigned int indexA = FindExtractedChannel(layer, psd::channelType::TRANSPARENCY_MASK);

        if (indexR == UINT_MAX || indexG == UINT_MAX || indexB == UINT_MAX)
        {
            psd::DestroyLayerMaskSection(layerMaskSection, &allocator);
            psd::DestroyDocument(document, &allocator);
            file.Close();
            SetError(errorUtf8, "PSD layer does not expose RGB channels.");
            return PSD_LAYER_UNSUPPORTED;
        }

        const unsigned int layerWidth = static_cast<unsigned int>(layer.right - layer.left);
        const unsigned int layerHeight = static_cast<unsigned int>(layer.bottom - layer.top);
        const size_t byteCount = static_cast<size_t>(layerWidth) * static_cast<size_t>(layerHeight) * 4u;
        uint8_t* layerRgba = static_cast<uint8_t*>(std::malloc(byteCount));
        if (!layerRgba)
        {
            psd::DestroyLayerMaskSection(layerMaskSection, &allocator);
            psd::DestroyDocument(document, &allocator);
            file.Close();
            SetError(errorUtf8, "Cannot allocate PSD layer buffer.");
            return PSD_LAYER_EXCEPTION;
        }

        switch (document->bitsPerChannel)
        {
        case 8:
            ConvertLayerChannelsToRgba8<uint8_t>(layer, indexR, indexG, indexB, indexA, layerWidth, layerHeight, layerRgba);
            break;
        case 16:
            ConvertLayerChannelsToRgba8<uint16_t>(layer, indexR, indexG, indexB, indexA, layerWidth, layerHeight, layerRgba);
            break;
        case 32:
            ConvertLayerChannelsToRgba8<float32_t>(layer, indexR, indexG, indexB, indexA, layerWidth, layerHeight, layerRgba);
            break;
        default:
            std::free(layerRgba);
            psd::DestroyLayerMaskSection(layerMaskSection, &allocator);
            psd::DestroyDocument(document, &allocator);
            file.Close();
            SetError(errorUtf8, "Unsupported PSD bits per channel.");
            return PSD_LAYER_UNSUPPORTED;
        }

        int croppedLeft = layer.left;
        int croppedTop = layer.top;
        int croppedWidth = static_cast<int>(layerWidth);
        int croppedHeight = static_cast<int>(layerHeight);
        uint8_t* croppedLayerRgba = CropToVisiblePixels(layerRgba, layerWidth, layerHeight, croppedLeft, croppedTop, croppedWidth, croppedHeight);
        if (!croppedLayerRgba)
        {
            std::free(layerRgba);
            psd::DestroyLayerMaskSection(layerMaskSection, &allocator);
            psd::DestroyDocument(document, &allocator);
            file.Close();
            SetError(errorUtf8, "PSD layer does not contain visible pixels.");
            return PSD_LAYER_UNSUPPORTED;
        }

        *rgba = croppedLayerRgba;
        *left = croppedLeft;
        *top = croppedTop;
        *width = croppedWidth;
        *height = croppedHeight;

        psd::DestroyLayerMaskSection(layerMaskSection, &allocator);
        psd::DestroyDocument(document, &allocator);
        file.Close();
        return PSD_LAYER_OK;
    }
    catch (const std::exception& ex)
    {
        SetError(errorUtf8, ex.what());
        return PSD_LAYER_EXCEPTION;
    }
    catch (...)
    {
        SetError(errorUtf8, "Unknown native PSD layer exception.");
        return PSD_LAYER_EXCEPTION;
    }
}
