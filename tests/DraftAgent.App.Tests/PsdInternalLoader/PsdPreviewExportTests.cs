using DraftAgent.App.Psd.Services;
using PsdLayerLoader.Core;
using SkiaSharp;

namespace DraftAgent.App.Tests.PsdInternalLoader;

public sealed class PsdPreviewExportTests
{
    [Fact]
    public void DefaultPsdPreviewComposesVisibleLayersInsteadOfUsingWhiteMergedPreviewOnly()
    {
        var samplePath = GetSamplePsdPath();
        Assert.True(File.Exists(samplePath), samplePath);
        var root = Path.Combine(Path.GetTempPath(), "DraftAgent", "PsdDefaultPreview.Tests", Guid.NewGuid().ToString("N"));
        var service = new PsdDefaultPreviewService();

        var path = service.RenderDefaultPreview("asset_sample_psd", samplePath, root);

        Assert.True(File.Exists(path), path);
        Assert.Contains("_v5_", Path.GetFileName(path), StringComparison.Ordinal);
        using var stream = File.OpenRead(path);
        using var bitmap = SKBitmap.Decode(stream);
        Assert.NotNull(bitmap);
        Assert.True(bitmap.Width > 0);
        Assert.True(bitmap.Height > 0);
        Assert.Contains(Enumerable.Range(0, bitmap.Width * bitmap.Height), index =>
        {
            var color = bitmap.GetPixel(index % bitmap.Width, index / bitmap.Width);
            return color.Alpha != 0 &&
                (color.Red != 255 || color.Green != 255 || color.Blue != 255);
        });
    }

    [Fact]
    public void DefaultPsdPreviewIgnoresPureWhiteAndPureBlackLayers()
    {
        var white = SolidLayer(1, 255, 255, 255, 255);
        var black = SolidLayer(2, 0, 0, 0, 255);
        var red = SolidLayer(3, 240, 20, 20, 255);
        var whiteOnTransparent = LayerWithPixels(
            5,
            2,
            2,
            [
                255, 255, 255, 255,
                0, 0, 0, 0,
                0, 0, 0, 0,
                0, 0, 0, 0
            ]);
        var blackOnTransparent = LayerWithPixels(
            6,
            2,
            2,
            [
                0, 0, 0, 0,
                0, 0, 0, 255,
                0, 0, 0, 0,
                0, 0, 0, 0
            ]);
        var translucentWhite = SolidLayer(7, 255, 255, 255, 128);

        Assert.True(PsdDefaultPreviewService.IsIgnorableSolidLayer(white));
        Assert.True(PsdDefaultPreviewService.IsIgnorableSolidLayer(black));
        Assert.False(PsdDefaultPreviewService.IsIgnorableSolidLayer(red));
        Assert.False(PsdDefaultPreviewService.IsIgnorableSolidLayer(whiteOnTransparent));
        Assert.False(PsdDefaultPreviewService.IsIgnorableSolidLayer(blackOnTransparent));
        Assert.False(PsdDefaultPreviewService.IsIgnorableSolidLayer(translucentWhite));
        Assert.False(PsdRenderableLayerFilter.IsRenderableLayer(white));
        Assert.False(PsdRenderableLayerFilter.IsRenderableLayer(black));
        Assert.True(PsdRenderableLayerFilter.IsRenderableLayer(red));
        Assert.True(PsdRenderableLayerFilter.IsRenderableLayer(whiteOnTransparent));
        Assert.True(PsdRenderableLayerFilter.IsRenderableLayer(blackOnTransparent));
        Assert.True(PsdRenderableLayerFilter.IsRenderableLayer(translucentWhite));
        Assert.False(PsdRenderableLayerFilter.IsRenderableLayer(TransparentLayer(4, 0, 0, 2, 2)));
    }

    [Fact]
    public void DefaultPsdPreviewBoundsIgnoreTransparentAndPureWhiteOrBlackLayers()
    {
        var visiblePixels = PsdLayerStackOrder.ForCompositing(
            new[]
            {
                SolidLayer(1, 255, 255, 255, 255, left: 0, top: 0, width: 120, height: 80),
                TransparentLayer(2, left: 40, top: 40, width: 60, height: 60),
                SolidLayer(3, 240, 20, 20, 255, left: 10, top: 12, width: 18, height: 30)
            }.Where(layer => !PsdDefaultPreviewService.IsIgnorableSolidLayer(layer)))
            .ToArray();

        var preview = PsdLayerCanvasComposer.Compose(
            120,
            100,
            visiblePixels,
            PsdExportBounds.LayerBounds);

        Assert.Equal(18, preview.Width);
        Assert.Equal(30, preview.Height);
    }

    [Fact]
    public void RenderableUnionBoundsIgnoreTransparentAndPureWhiteOrBlackLayers()
    {
        var layers = new[]
        {
            SolidLayer(1, 255, 255, 255, 255, left: -20, top: -20, width: 200, height: 200),
            SolidLayer(2, 0, 0, 0, 255, left: -10, top: -10, width: 180, height: 180),
            TransparentLayer(3, left: 0, top: 0, width: 160, height: 160),
            SolidLayer(4, 12, 80, 220, 255, left: 10, top: 20, width: 30, height: 40),
            SolidLayer(5, 220, 70, 12, 255, left: 70, top: 90, width: 15, height: 25)
        };

        var hasBounds = PsdRenderableLayerFilter.TryGetRenderableUnionBounds(layers, out var bounds);

        Assert.True(hasBounds);
        Assert.Equal(10, bounds.Left);
        Assert.Equal(20, bounds.Top);
        Assert.Equal(75, bounds.Width);
        Assert.Equal(95, bounds.Height);
    }

    [Fact]
    public void ComposerCanUseAllRenderableLayerBoundsWhileRenderingSelectedLayer()
    {
        var smallSelectedLayer = SolidLayer(1, 12, 80, 220, 255, left: 10, top: 20, width: 30, height: 40);
        var distantLayer = SolidLayer(2, 220, 70, 12, 255, left: 70, top: 90, width: 15, height: 25);
        Assert.True(PsdRenderableLayerFilter.TryGetRenderableUnionBounds(
            [smallSelectedLayer, distantLayer],
            out var allLayerBounds));

        var preview = PsdLayerCanvasComposer.Compose(
            140,
            140,
            [smallSelectedLayer],
            PsdExportBounds.LayerBounds,
            allLayerBounds);

        Assert.Equal(75, preview.Width);
        Assert.Equal(95, preview.Height);
        Assert.Contains(Enumerable.Range(0, preview.Width * preview.Height), index =>
            preview.Rgba[(index * 4) + 3] != 0);
    }

    [Fact]
    public async Task DragPreviewExportWritesUsablePngFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "DraftAgent", "PsdLayerLoader.Tests", Guid.NewGuid().ToString("N"));
        var preview = new PsdMergedPreview(
            2,
            2,
            [
                255, 0, 0, 255,
                0, 255, 0, 255,
                0, 0, 255, 255,
                255, 255, 255, 255
            ]);

        var path = await PsdPreviewDragExportService.SaveDragPreviewAsync(
            preview,
            "drag-preview.psd",
            root);

        Assert.True(File.Exists(path), path);
        Assert.EndsWith(".png", path, StringComparison.OrdinalIgnoreCase);

        using var stream = File.OpenRead(path);
        using var bitmap = SKBitmap.Decode(stream);
        Assert.NotNull(bitmap);
        Assert.Equal(2, bitmap.Width);
        Assert.Equal(2, bitmap.Height);
    }

    private static string GetSamplePsdPath()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "PsdLayerLoader.Native",
            "third_party",
            "psd_sdk",
            "bin",
            "Sample.psd"));
    }

    private static PsdLayerPixelData SolidLayer(
        int layerId,
        byte red,
        byte green,
        byte blue,
        byte alpha,
        int left = 0,
        int top = 0,
        int width = 2,
        int height = 2)
    {
        var rgba = new byte[checked(width * height * 4)];
        for (var offset = 0; offset < rgba.Length; offset += 4)
        {
            rgba[offset] = red;
            rgba[offset + 1] = green;
            rgba[offset + 2] = blue;
            rgba[offset + 3] = alpha;
        }

        return new PsdLayerPixelData(
            layerId,
            left,
            top,
            width,
            height,
            rgba);
    }

    private static PsdLayerPixelData TransparentLayer(int layerId, int left, int top, int width, int height)
    {
        return new PsdLayerPixelData(
            layerId,
            left,
            top,
            width,
            height,
            new byte[checked(width * height * 4)]);
    }

    private static PsdLayerPixelData LayerWithPixels(int layerId, int width, int height, byte[] rgba)
    {
        Assert.Equal(checked(width * height * 4), rgba.Length);
        return new PsdLayerPixelData(layerId, 0, 0, width, height, rgba);
    }
}


