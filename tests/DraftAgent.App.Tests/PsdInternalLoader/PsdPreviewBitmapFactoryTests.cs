using DraftAgent.App.Psd.Services;

namespace DraftAgent.App.Tests.PsdInternalLoader;

public sealed class PsdPreviewBitmapFactoryTests
{
    [Fact]
    public void CalculateThumbnailPlacementPreservesLayerAspectRatio()
    {
        var placement = PsdPreviewBitmapFactory.CalculateThumbnailPlacement(
            sourceWidth: 4,
            sourceHeight: 1,
            targetWidth: 40,
            targetHeight: 40);

        Assert.Equal(0, placement.OffsetX);
        Assert.Equal(15, placement.OffsetY);
        Assert.Equal(40, placement.Width);
        Assert.Equal(10, placement.Height);
    }

    [Fact]
    public void CalculateThumbnailRasterSizeUsesFixedSquareFrame()
    {
        var wide = PsdPreviewBitmapFactory.CalculateThumbnailRasterSize(1024, 256);
        var tall = PsdPreviewBitmapFactory.CalculateThumbnailRasterSize(256, 1024);

        Assert.Equal(256, wide.Width);
        Assert.Equal(256, wide.Height);
        Assert.Equal(256, tall.Width);
        Assert.Equal(256, tall.Height);
    }

    [Fact]
    public void SampleBilinearRgbaBlendsNeighboringPixels()
    {
        var sample = PsdPreviewBitmapFactory.SampleBilinearRgba(
            [
                0, 0, 0, 255,
                255, 255, 255, 255
            ],
            width: 2,
            height: 1,
            x: 0.5,
            y: 0);

        Assert.InRange(sample.R, 127, 128);
        Assert.InRange(sample.G, 127, 128);
        Assert.InRange(sample.B, 127, 128);
        Assert.Equal(255, sample.A);
    }
}


