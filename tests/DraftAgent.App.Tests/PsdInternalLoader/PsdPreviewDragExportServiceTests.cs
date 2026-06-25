using DraftAgent.App.Psd.Services;
using PsdLayerLoader.Core;
using SkiaSharp;

namespace DraftAgent.App.Tests.PsdInternalLoader;

public sealed class PsdPreviewDragExportServiceTests
{
    [Fact]
    public async Task SaveDragPreviewCreatesPngFileWithSafeNameAndCanvasSize()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), $"psd-drag-export-{Guid.NewGuid():N}");
        var preview = new PsdMergedPreview(
            2,
            1,
            [
                255, 0, 0, 255,
                0, 255, 0, 128
            ]);

        var outputPath = await PsdPreviewDragExportService.SaveDragPreviewAsync(
            preview,
            "bad:name",
            outputRoot);

        Assert.True(File.Exists(outputPath), outputPath);
        Assert.EndsWith("bad_name.png", outputPath);

        using var data = SKData.Create(outputPath);
        using var image = SKImage.FromEncodedData(data);
        Assert.NotNull(image);
        Assert.Equal(2, image.Width);
        Assert.Equal(1, image.Height);
    }
}


