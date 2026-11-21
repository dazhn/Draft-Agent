using System.Runtime.InteropServices;
using System.Text.Json;

namespace PsdLayerLoader.Core;

public sealed class NativePsdDocumentReader
{
    public PsdNativeDocumentInfo Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("PSD path is required.", nameof(path));
        }

        var result = NativeMethods.psd_layer_load_document_json(path, out var jsonPtr, out var errorPtr);
        try
        {
            if (result != 0)
            {
                var error = PtrToString(errorPtr);
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                    ? $"Native PSD load failed with code {result}."
                    : error);
            }

            var json = PtrToString(jsonPtr);
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException("Native PSD loader returned an empty document.");
            }

            return JsonSerializer.Deserialize<PsdNativeDocumentInfo>(json, PsdComponentProtocol.JsonOptions)
                ?? throw new InvalidOperationException("Native PSD document JSON could not be parsed.");
        }
        finally
        {
            if (jsonPtr != IntPtr.Zero)
            {
                NativeMethods.psd_layer_free(jsonPtr);
            }

            if (errorPtr != IntPtr.Zero)
            {
                NativeMethods.psd_layer_free(errorPtr);
            }
        }
    }

    public PsdMergedPreview LoadMergedPreview(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("PSD path is required.", nameof(path));
        }

        var result = NativeMethods.psd_layer_load_merged_preview_rgba(
            path,
            out var rgbaPtr,
            out var width,
            out var height,
            out var errorPtr);

        try
        {
            if (result != 0)
            {
                var error = PtrToString(errorPtr);
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                    ? $"Native PSD preview load failed with code {result}."
                    : error);
            }

            if (rgbaPtr == IntPtr.Zero || width <= 0 || height <= 0)
            {
                throw new InvalidOperationException("Native PSD preview returned an empty image.");
            }

            var byteCount = checked(width * height * 4);
            var rgba = new byte[byteCount];
            Marshal.Copy(rgbaPtr, rgba, 0, rgba.Length);
            return new PsdMergedPreview(width, height, rgba);
        }
        finally
        {
            if (rgbaPtr != IntPtr.Zero)
            {
                NativeMethods.psd_layer_free(rgbaPtr);
            }

            if (errorPtr != IntPtr.Zero)
            {
                NativeMethods.psd_layer_free(errorPtr);
            }
        }
    }

    public PsdLayerPixelData LoadLayerPixels(string path, int layerId)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("PSD path is required.", nameof(path));
        }

        var result = NativeMethods.psd_layer_load_layer_rgba(
            path,
            layerId,
            out var rgbaPtr,
            out var left,
            out var top,
            out var width,
            out var height,
            out var errorPtr);

        try
        {
            if (result != 0)
            {
                var error = PtrToString(errorPtr);
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                    ? $"Native PSD layer load failed with code {result}."
                    : error);
            }

            if (rgbaPtr == IntPtr.Zero || width <= 0 || height <= 0)
            {
                throw new InvalidOperationException("Native PSD layer returned an empty image.");
            }

            var byteCount = checked(width * height * 4);
            var rgba = new byte[byteCount];
            Marshal.Copy(rgbaPtr, rgba, 0, rgba.Length);
            return new PsdLayerPixelData(layerId, left, top, width, height, rgba);
        }
        finally
        {
            if (rgbaPtr != IntPtr.Zero)
            {
                NativeMethods.psd_layer_free(rgbaPtr);
            }

            if (errorPtr != IntPtr.Zero)
            {
                NativeMethods.psd_layer_free(errorPtr);
            }
        }
    }

    private static string PtrToString(IntPtr ptr)
    {
        return ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
    }

    private static class NativeMethods
    {
        [DllImport("PsdLayerLoader.Native", CharSet = CharSet.Unicode)]
        public static extern int psd_layer_load_document_json(
            string path,
            out IntPtr jsonUtf8,
            out IntPtr errorUtf8);

        [DllImport("PsdLayerLoader.Native", CharSet = CharSet.Unicode)]
        public static extern int psd_layer_load_merged_preview_rgba(
            string path,
            out IntPtr rgba,
            out int width,
            out int height,
            out IntPtr errorUtf8);

        [DllImport("PsdLayerLoader.Native", CharSet = CharSet.Unicode)]
        public static extern int psd_layer_load_layer_rgba(
            string path,
            int layerId,
            out IntPtr rgba,
            out int left,
            out int top,
            out int width,
            out int height,
            out IntPtr errorUtf8);

        [DllImport("PsdLayerLoader.Native")]
        public static extern void psd_layer_free(IntPtr ptr);
    }
}
