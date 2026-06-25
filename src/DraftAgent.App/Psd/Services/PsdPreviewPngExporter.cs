using System.IO;
using System.Runtime.InteropServices;
using PsdLayerLoader.Core;
using SkiaSharp;

namespace DraftAgent.App.Psd.Services;

public static class PsdPreviewPngExporter
{
    public static void SaveMergedPreview(PsdMergedPreview preview, string outputPath)
    {
        SavePreview(preview, outputPath);
    }

    public static void SavePreview(PsdMergedPreview preview, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

        var imageInfo = new SKImageInfo(
            preview.Width,
            preview.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Unpremul);

        using var bitmap = new SKBitmap(imageInfo);
        Marshal.Copy(preview.Rgba, 0, bitmap.GetPixels(), preview.Rgba.Length);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        data.SaveTo(stream);
    }
}

