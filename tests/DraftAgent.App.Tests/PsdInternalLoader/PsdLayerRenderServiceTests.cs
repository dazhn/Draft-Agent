using DraftAgent.App.Psd.Services;
using PsdLayerLoader.Core;

namespace DraftAgent.App.Tests.PsdInternalLoader;

public sealed class PsdLayerRenderServiceTests
{
    [Fact]
    public void TryApplySelectionHintKeepsDefaultVisibleLayersOutsideControlledNames()
    {
        var service = new PsdLayerRenderService();
        var layers = new[]
        {
            Layer(1, null, "身体", isVisible: true),
            Group(10, null, "眉毛"),
            Layer(11, 10, "平眉", isVisible: true),
            Layer(12, 10, "皱眉", isVisible: false),
            Layer(20, null, "装饰", isVisible: true)
        };
        var hint = new PsdLayerSelectionHint(
            [],
            ["皱眉"],
            "ai_structured",
            "fp",
            "cache",
            ["平眉", "皱眉"]);

        var document = RenderDocument(
            PsdExportBounds.LayerBounds,
            layers,
            new Dictionary<int, PsdLayerPixelData>(),
            null);
        var state = service.CreateState(document);

        var applied = service.TryApplySelectionHint(state, hint, out var nextState);

        Assert.True(applied);
        Assert.Equal([12], nextState.SelectedLayerIds.OrderBy(id => id));
        Assert.Contains(1, nextState.VisibleLayerIds);
        Assert.DoesNotContain(11, nextState.VisibleLayerIds);
        Assert.Contains(12, nextState.VisibleLayerIds);
        Assert.Contains(20, nextState.VisibleLayerIds);
        Assert.Equal([11, 12], nextState.ControlledLayerIds.OrderBy(id => id));
    }

    [Fact]
    public void TryApplySelectionHintPrefersLayerIdsWhenLayerNamesAreDuplicated()
    {
        var service = new PsdLayerRenderService();
        var layers = new[]
        {
            Group(10, null, "左手"),
            Group(20, 10, "伸手"),
            Layer(21, 20, "手掌", isVisible: false),
            Layer(22, 20, "袖口", isVisible: false),
            Group(30, 10, "默认"),
            Layer(31, 30, "手掌", isVisible: true),
            Layer(32, 30, "袖口", isVisible: true)
        };
        var hint = new PsdLayerSelectionHint(
            [21, 22],
            ["手掌", "袖口"],
            "ai_structured",
            "fp",
            "cache",
            ["手掌", "袖口"],
            [21, 22, 31, 32]);

        var document = RenderDocument(
            PsdExportBounds.LayerBounds,
            layers,
            new Dictionary<int, PsdLayerPixelData>(),
            null);
        var state = service.CreateState(document);

        var applied = service.TryApplySelectionHint(state, hint, out var nextState);

        Assert.True(applied);
        Assert.Equal([21, 22], nextState.SelectedLayerIds.OrderBy(id => id));
        Assert.Equal([21, 22], nextState.VisibleLayerIds.OrderBy(id => id));
        Assert.Equal([21, 22, 31, 32], nextState.ControlledLayerIds.OrderBy(id => id));
    }

    [Fact]
    public void CreateStateUsesDefaultVisibleLayersAsAuthority()
    {
        var service = new PsdLayerRenderService();
        var layers = new[]
        {
            Layer(1, null, "身体", isVisible: true),
            Layer(2, null, "隐藏备用", isVisible: false),
            Group(10, null, "手"),
            Layer(11, 10, "默认手", isVisible: true)
        };
        var document = RenderDocument(
            PsdExportBounds.LayerBounds,
            layers,
            new Dictionary<int, PsdLayerPixelData>(),
            null);

        var state = service.CreateState(document);

        Assert.Equal([1, 11], state.VisibleLayerIds.OrderBy(id => id));
        Assert.Empty(state.SelectedLayerIds);
        Assert.Contains(10, state.GroupSelections.Keys);
    }

    [Fact]
    public void LoadStateReadsStructureWithoutPreloadingLayerPixels()
    {
        var service = new PsdLayerRenderService();

        var state = service.LoadState(new PsdLayerRenderParameters(
            GetSamplePsdPath(),
            PsdExportBounds.LayerBounds));

        Assert.NotEmpty(state.Document.DisplayLayers);
        Assert.Empty(state.Document.LayerPixels);
    }

    [Fact]
    public void TryApplySelectionHintUpdatesStateAndKeepsUncontrolledDefaults()
    {
        var service = new PsdLayerRenderService();
        var layers = new[]
        {
            Layer(1, null, "身体", isVisible: true),
            Group(10, null, "眉毛"),
            Layer(11, 10, "平眉", isVisible: true),
            Layer(12, 10, "皱眉", isVisible: false),
            Layer(20, null, "装饰", isVisible: true)
        };
        var document = RenderDocument(
            PsdExportBounds.LayerBounds,
            layers,
            new Dictionary<int, PsdLayerPixelData>(),
            null);
        var state = service.CreateState(document);
        var hint = new PsdLayerSelectionHint(
            [],
            ["皱眉"],
            "ai_structured",
            "fp",
            "cache",
            ["平眉", "皱眉"]);

        var applied = service.TryApplySelectionHint(state, hint, out var nextState);

        Assert.True(applied);
        Assert.Equal([12], nextState.SelectedLayerIds.OrderBy(id => id));
        Assert.Equal([1, 12, 20], nextState.VisibleLayerIds.OrderBy(id => id));
        Assert.Equal([11, 12], nextState.ControlledLayerIds.OrderBy(id => id));
    }

    [Fact]
    public void ComposePreviewUsesSharedRenderBoundsParameter()
    {
        var service = new PsdLayerRenderService();
        var layer = Layer(1, null, "身体", isVisible: true) with
        {
            Left = 10,
            Top = 20,
            Right = 30,
            Bottom = 60,
            Width = 20,
            Height = 40
        };
        var pixels = new PsdLayerPixelData(1, 10, 20, 20, 40, FilledRgba(20, 40));
        var canvasDocument = RenderDocument(
            PsdExportBounds.CanvasBounds,
            [layer],
            new Dictionary<int, PsdLayerPixelData> { [1] = pixels },
            new PsdLayerExportRect(10, 20, 20, 40));
        var layerBoundsDocument = canvasDocument with
        {
            Parameters = canvasDocument.Parameters with { Bounds = PsdExportBounds.LayerBounds }
        };

        var canvasPreview = service.ComposePreview(service.CreateState(canvasDocument));
        var layerBoundsPreview = service.ComposePreview(service.CreateState(layerBoundsDocument));

        Assert.NotNull(canvasPreview);
        Assert.NotNull(layerBoundsPreview);
        Assert.Equal(100, canvasPreview.Width);
        Assert.Equal(100, canvasPreview.Height);
        Assert.Equal(20, layerBoundsPreview.Width);
        Assert.Equal(40, layerBoundsPreview.Height);
    }

    private static PsdNativeLayerInfo Group(int id, int? parentId, string name)
    {
        return new PsdNativeLayerInfo(id, parentId, name, 0, true, true, 0, 0, 0, 0, 0, 0);
    }

    private static PsdNativeLayerInfo Layer(int id, int? parentId, string name, bool isVisible)
    {
        return new PsdNativeLayerInfo(id, parentId, name, 0, false, isVisible, 0, 0, 1, 1, 1, 1);
    }

    private static PsdLayerRenderDocument RenderDocument(
        PsdExportBounds bounds,
        IReadOnlyList<PsdNativeLayerInfo> layers,
        IReadOnlyDictionary<int, PsdLayerPixelData> pixels,
        PsdLayerExportRect? renderableBounds)
    {
        return new PsdLayerRenderDocument(
            new PsdLayerRenderParameters("test.psd", bounds),
            "test.psd",
            new PsdNativeDocumentInfo(100, 100, 8, 4, 3, layers),
            layers,
            pixels,
            renderableBounds);
    }

    private static byte[] FilledRgba(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        for (var index = 0; index < rgba.Length; index += 4)
        {
            rgba[index] = 255;
            rgba[index + 1] = 255;
            rgba[index + 2] = 255;
            rgba[index + 3] = 255;
        }

        return rgba;
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
}
