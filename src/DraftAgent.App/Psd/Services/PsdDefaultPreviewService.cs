using System;
using System.IO;
using System.Linq;
using PsdLayerLoader.Core;

namespace DraftAgent.App.Psd.Services;

internal sealed class PsdDefaultPreviewService
{
    private const string CacheVersion = "v5";
    private const int SectionDividerLayerType = 3;

    private readonly NativePsdDocumentReader _reader;

    public PsdDefaultPreviewService()
        : this(new NativePsdDocumentReader())
    {
    }

    internal PsdDefaultPreviewService(NativePsdDocumentReader reader)
    {
        _reader = reader;
    }

    public string RenderDefaultPreview(string psdAssetId, string psdPath, string cacheDirectory)
    {
        if (string.IsNullOrWhiteSpace(psdAssetId))
        {
            throw new ArgumentException("PSD asset id is required.", nameof(psdAssetId));
        }

        if (string.IsNullOrWhiteSpace(psdPath))
        {
            throw new ArgumentException("PSD path is required.", nameof(psdPath));
        }

        var fullPath = Path.GetFullPath(psdPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("PSD file does not exist.", fullPath);
        }

        Directory.CreateDirectory(cacheDirectory);
        var info = new FileInfo(fullPath);
        var outputPath = Path.Combine(
            cacheDirectory,
            $"{SanitizeFileName(psdAssetId)}_{CacheVersion}_{info.LastWriteTimeUtc.Ticks:x}_{info.Length:x}.png");
        if (File.Exists(outputPath))
        {
            return outputPath;
        }

        var preview = RenderDefaultLayerVisibilityPreview(fullPath);
        PsdPreviewPngExporter.SavePreview(preview, outputPath);
        return outputPath;
    }

    private PsdMergedPreview RenderDefaultLayerVisibilityPreview(string fullPath)
    {
        var document = _reader.Load(fullPath);
        var visiblePixels = PsdLayerStackOrder.ForCompositing(document.Layers
                .Where(layer =>
                    !layer.IsGroup &&
                    layer.Type != SectionDividerLayerType &&
                    layer.IsVisible &&
                    layer.Width > 0 &&
                    layer.Height > 0)
                .Select(layer => TryLoadLayerPixels(fullPath, layer.LayerId))
                .Where(pixelData => pixelData is not null)
                .Cast<PsdLayerPixelData>())
            .Where(PsdRenderableLayerFilter.IsRenderableLayer)
            .ToArray();

        if (visiblePixels.Length == 0)
        {
            return _reader.LoadMergedPreview(fullPath);
        }

        var fixedBounds = PsdRenderableLayerFilter.TryGetRenderableUnionBounds(visiblePixels, out var renderBounds)
            ? renderBounds
            : (PsdLayerExportRect?)null;

        return PsdLayerCanvasComposer.Compose(
            document.Width,
            document.Height,
            visiblePixels,
            PsdExportBounds.LayerBounds,
            fixedBounds);
    }

    private PsdLayerPixelData? TryLoadLayerPixels(string fullPath, int layerId)
    {
        try
        {
            return _reader.LoadLayerPixels(fullPath, layerId);
        }
        catch
        {
            return null;
        }
    }

    internal static bool IsIgnorableSolidLayer(PsdLayerPixelData pixelData)
    {
        return PsdRenderableLayerFilter.IsIgnorableSolidLayer(pixelData);
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim().ToCharArray();
        for (var index = 0; index < chars.Length; index++)
        {
            if (Array.IndexOf(invalid, chars[index]) >= 0)
            {
                chars[index] = '_';
            }
        }

        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "psd" : sanitized;
    }
}
