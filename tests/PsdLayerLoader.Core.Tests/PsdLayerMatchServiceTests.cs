using PsdLayerLoader.Core;

namespace PsdLayerLoader.Core.Tests;

public sealed class PsdLayerMatchServiceTests
{
    private readonly PsdLayerMatchService _service = new();

    [Fact]
    public void MatchUsesExactPresetWithFullConfidence()
    {
        var document = BuildDocument();
        var preset = new PsdLayerPreset(
            "preset_calm_turn_left",
            "平静-左转",
            "Calm",
            ["turn_left", "look_left"],
            ["平静侧身"],
            [1, 2, 4],
            PsdExportBounds.CanvasBounds);

        var result = _service.Match(document, "Calm", "turn_left", [preset]);

        Assert.Equal(1.0, result.Confidence);
        Assert.Equal("preset_calm_turn_left", result.MatchedPresetId);
        Assert.Equal([1, 2, 4], result.LayerIds);
        Assert.Equal(["Base", "Eyes_Calm", "Body_TurnLeft"], result.LayerNames);
    }

    [Fact]
    public void MatchFallsBackToEmotionPreset()
    {
        var document = BuildDocument();
        var preset = new PsdLayerPreset(
            "preset_calm",
            "平静",
            "Calm",
            ["idle"],
            [],
            [1, 2],
            PsdExportBounds.CanvasBounds);

        var result = _service.Match(document, "Calm", "turn_left", [preset]);

        Assert.Equal(0.85, result.Confidence);
        Assert.Equal("preset_calm", result.MatchedPresetId);
        Assert.Equal([1, 2], result.LayerIds);
    }

    [Fact]
    public void MatchFallsBackToLayerNameRules()
    {
        var result = _service.Match(BuildDocument(), "Calm", "turn_left", []);

        Assert.Equal(0.72, result.Confidence);
        Assert.Contains(1, result.LayerIds);
        Assert.Contains(2, result.LayerIds);
        Assert.Contains(4, result.LayerIds);
        Assert.Contains("Eyes_Calm", result.LayerNames);
        Assert.Contains("Body_TurnLeft", result.LayerNames);
    }

    [Fact]
    public void MatchSelectsOneLayerPerSemanticGroupFromEmotionActionAndText()
    {
        var result = _service.Match(BuildSemanticDocument(), "开心 0.8", "角色动作：陈默抬手示意", "我准备好了。", []);

        Assert.Equal(0.78, result.Confidence);
        Assert.Equal([1, 3, 5, 7], result.LayerIds);
        Assert.Equal(["Base", "开心", "说话", "抬手"], result.LayerNames);
        Assert.DoesNotContain(2, result.LayerIds);
        Assert.DoesNotContain(4, result.LayerIds);
        Assert.DoesNotContain(6, result.LayerIds);
    }

    [Fact]
    public void MatchConfiguredPsdLayerNamesBeforeGenericSemanticFallback()
    {
        var text = """
            角色状态匹配：根据文本描述选择眉眼嘴左右手状态，状态只能从对应固定词汇中选择。
            动作可选状态：抬手，默认手。表情可选状态：悲伤，开心。
            """;

        var result = _service.Match(BuildSemanticDocument(), "悲伤", "角色动作：抬手", text, []);

        Assert.Equal(0.9, result.Confidence);
        Assert.Contains(7, result.LayerIds);
        Assert.Contains("抬手", result.LayerNames);
        Assert.Contains(8, result.LayerIds);
        Assert.Contains("悲伤", result.LayerNames);
    }

    [Fact]
    public void MatchKeepsDefaultSemanticLayersWhenOnlyEmotionMatches()
    {
        var result = _service.Match(BuildSemanticDocument(), "悲伤", "Idle", []);

        Assert.Equal(0.78, result.Confidence);
        Assert.Equal([1, 8, 4, 6], result.LayerIds);
        Assert.Equal(["Base", "悲伤", "闭嘴", "默认手"], result.LayerNames);
    }

    [Fact]
    public void MatchSelectsAllRenderableLayersInsideNestedExpressionGroup()
    {
        var result = _service.Match(BuildNestedExpressionDocument(), "开心", "Idle", []);

        Assert.Equal(0.78, result.Confidence);
        Assert.Equal([1, 3, 4], result.LayerIds);
        Assert.Equal(["Base", "开心_眼睛", "开心_嘴"], result.LayerNames);
        Assert.DoesNotContain(2, result.LayerIds);
    }

    [Fact]
    public void MatchFallsBackToDefaultVisibleLayersWhenNoRuleMatches()
    {
        var result = _service.Match(BuildDocument(), "Afraid", "jump", []);

        Assert.Equal(0.5, result.Confidence);
        Assert.Equal([1], result.LayerIds);
        Assert.Equal(["Base"], result.LayerNames);
    }

    [Fact]
    public void MatchReturnsZeroConfidenceWhenNothingCanBeMatched()
    {
        var document = new PsdDocumentInfo("doc", "empty.psd", "empty", 100, 100, []);

        var result = _service.Match(document, "Calm", "idle", []);

        Assert.Equal(0.0, result.Confidence);
        Assert.Empty(result.LayerIds);
    }

    private static PsdDocumentInfo BuildDocument()
    {
        return new PsdDocumentInfo(
            "doc_chen",
            "chen.psd",
            "chen",
            2048,
            2048,
            [
                new PsdLayerNode(
                    10,
                    null,
                    "ROOT",
                    PsdLayerKind.Group,
                    true,
                    true,
                    0,
                    0,
                    2048,
                    2048,
                    [
                        new PsdLayerNode(1, 10, "Base", PsdLayerKind.Raster, false, true, 0, 0, 2048, 2048, []),
                        new PsdLayerNode(2, 10, "Eyes_Calm", PsdLayerKind.Raster, false, false, 200, 200, 500, 200, []),
                        new PsdLayerNode(3, 10, "Mouth_Neutral", PsdLayerKind.Raster, false, false, 240, 520, 260, 120, []),
                        new PsdLayerNode(4, 10, "Body_TurnLeft", PsdLayerKind.Raster, false, false, 0, 0, 2048, 2048, [])
                    ])
            ]);
    }

    private static PsdDocumentInfo BuildSemanticDocument()
    {
        return new PsdDocumentInfo(
            "doc_semantic",
            "semantic.psd",
            "semantic",
            100,
            100,
            [
                new PsdLayerNode(
                    10,
                    null,
                    "ROOT",
                    PsdLayerKind.Group,
                    true,
                    true,
                    0,
                    0,
                    100,
                    100,
                    [
                        new PsdLayerNode(1, 10, "Base", PsdLayerKind.Raster, false, true, 0, 0, 100, 100, []),
                        new PsdLayerNode(
                            20,
                            10,
                            "表情",
                            PsdLayerKind.Group,
                            true,
                            true,
                            0,
                            0,
                            100,
                            100,
                            [
                                new PsdLayerNode(2, 20, "平静", PsdLayerKind.Raster, false, true, 0, 0, 20, 20, []),
                                new PsdLayerNode(3, 20, "开心", PsdLayerKind.Raster, false, false, 0, 0, 20, 20, []),
                                new PsdLayerNode(8, 20, "悲伤", PsdLayerKind.Raster, false, false, 0, 0, 20, 20, [])
                            ]),
                        new PsdLayerNode(
                            30,
                            10,
                            "嘴",
                            PsdLayerKind.Group,
                            true,
                            true,
                            0,
                            0,
                            100,
                            100,
                            [
                                new PsdLayerNode(4, 30, "闭嘴", PsdLayerKind.Raster, false, true, 0, 0, 20, 20, []),
                                new PsdLayerNode(5, 30, "说话", PsdLayerKind.Raster, false, false, 0, 0, 20, 20, [])
                            ]),
                        new PsdLayerNode(
                            40,
                            10,
                            "手",
                            PsdLayerKind.Group,
                            true,
                            true,
                            0,
                            0,
                            100,
                            100,
                            [
                                new PsdLayerNode(6, 40, "默认手", PsdLayerKind.Raster, false, true, 0, 0, 20, 20, []),
                                new PsdLayerNode(7, 40, "抬手", PsdLayerKind.Raster, false, false, 0, 0, 20, 20, [])
                            ])
                    ])
            ]);
    }

    private static PsdDocumentInfo BuildNestedExpressionDocument()
    {
        return new PsdDocumentInfo(
            "doc_nested_expression",
            "nested-expression.psd",
            "nested-expression",
            100,
            100,
            [
                new PsdLayerNode(
                    10,
                    null,
                    "ROOT",
                    PsdLayerKind.Group,
                    true,
                    true,
                    0,
                    0,
                    100,
                    100,
                    [
                        new PsdLayerNode(1, 10, "Base", PsdLayerKind.Raster, false, true, 0, 0, 100, 100, []),
                        new PsdLayerNode(
                            20,
                            10,
                            "表情",
                            PsdLayerKind.Group,
                            true,
                            true,
                            0,
                            0,
                            100,
                            100,
                            [
                                new PsdLayerNode(2, 20, "平静", PsdLayerKind.Raster, false, true, 0, 0, 20, 20, []),
                                new PsdLayerNode(
                                    30,
                                    20,
                                    "开心",
                                    PsdLayerKind.Group,
                                    true,
                                    false,
                                    0,
                                    0,
                                    20,
                                    20,
                                    [
                                        new PsdLayerNode(3, 30, "开心_眼睛", PsdLayerKind.Raster, false, false, 0, 0, 20, 20, []),
                                        new PsdLayerNode(4, 30, "开心_嘴", PsdLayerKind.Raster, false, false, 0, 0, 20, 20, [])
                                    ])
                            ])
                    ])
            ]);
    }
}

