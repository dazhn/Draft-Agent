using DraftAgent.App.Psd.Services;
using PsdLayerLoader.Core;

namespace DraftAgent.App.Tests.PsdInternalLoader;

public sealed class PsdLayerStackOrderTests
{
    [Fact]
    public void PanelOrderShowsHigherLayerIdsFirst()
    {
        var ordered = PsdLayerStackOrder
            .ForPanel([1, 3, 2], layerId => layerId)
            .ToArray();

        Assert.Equal([3, 2, 1], ordered);
    }

    [Fact]
    public void CompositingOrderDrawsLowerLayerIdsFirst()
    {
        var ordered = PsdLayerStackOrder
            .ForCompositing(
            [
                SolidLayer(layerId: 3, r: 0, g: 0, b: 255),
                SolidLayer(layerId: 1, r: 255, g: 0, b: 0)
            ])
            .ToArray();

        Assert.Equal([1, 3], ordered.Select(layer => layer.LayerId).ToArray());
    }

    [Fact]
    public void ComposerDrawsTopLayerOverBottomLayer()
    {
        var preview = PsdLayerCanvasComposer.Compose(
            1,
            1,
            PsdLayerStackOrder.ForCompositing(
            [
                SolidLayer(layerId: 2, r: 0, g: 0, b: 255),
                SolidLayer(layerId: 1, r: 255, g: 0, b: 0)
            ]).ToArray(),
            PsdExportBounds.CanvasBounds);

        Assert.Equal(0, preview.Rgba[0]);
        Assert.Equal(0, preview.Rgba[1]);
        Assert.Equal(255, preview.Rgba[2]);
        Assert.Equal(255, preview.Rgba[3]);
    }

    private static PsdLayerPixelData SolidLayer(int layerId, byte r, byte g, byte b)
    {
        return new PsdLayerPixelData(layerId, 0, 0, 1, 1, [r, g, b, 255]);
    }
}


