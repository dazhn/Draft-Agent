using DraftAgent.App.ViewModels;
using DraftAgent.Core;

namespace DraftAgent.App.Tests;

public sealed class PsdLayerAiSelectionServiceTests
{
    [Fact]
    public void BuildSystemPromptAppendsFixedCatalogAfterJsonShape()
    {
        var prompt = PsdLayerAiSelectionService.BuildSystemPrompt(TestWholeExpressionConfiguration());

        Assert.StartsWith("你是固定词汇分类器", prompt, StringComparison.Ordinal);
        Assert.Contains("固定词汇分类器", prompt, StringComparison.Ordinal);
        Assert.Contains("只根据输入 text", prompt, StringComparison.Ordinal);
        Assert.Contains("不得新增字段", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("不得使用角色名、场景、情绪、上一行、下一行或常识脑补", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("text 没有明确证据时填空字符串", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("否则填空字符串", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("左右手必须有明确手部/手势/手臂文本证据才可选择", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("普通说话文本可选择 mouth", prompt, StringComparison.Ordinal);
        Assert.Contains("7. 返回单行结果", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("8. 返回单行结果", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("10. 返回单行结果", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("11. 返回单行结果", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("expression", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("整体表情", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("开心", prompt, StringComparison.Ordinal);
        Assert.Contains("brow: [", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("brow(", prompt, StringComparison.Ordinal);
        Assert.Contains("皱眉", prompt, StringComparison.Ordinal);
        Assert.Contains("eye: [", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("eye(", prompt, StringComparison.Ordinal);
        Assert.Contains("闭眼", prompt, StringComparison.Ordinal);
        Assert.Contains("mouth: [", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("mouth(", prompt, StringComparison.Ordinal);
        Assert.Contains("张嘴", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("动作(动作)", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("抬手", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("\"action\"", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("leftHand: [", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("leftHand(", prompt, StringComparison.Ordinal);
        Assert.Contains("伸手", prompt, StringComparison.Ordinal);
        Assert.Contains("rightHand: [", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("rightHand(", prompt, StringComparison.Ordinal);
        Assert.Contains("握拳", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("foot", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("脚(", prompt, StringComparison.Ordinal);
        Assert.Contains("固定词汇表：", prompt, StringComparison.Ordinal);
        Assert.True(
            prompt.IndexOf("返回 JSON 格式：", StringComparison.Ordinal) <
            prompt.IndexOf("固定词汇表：", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildSystemPromptUsesFacialPartCandidatesExclusively()
    {
        var prompt = PsdLayerAiSelectionService.BuildSystemPrompt(TestFacialPartsConfiguration());

        Assert.StartsWith("你是固定词汇分类器", prompt, StringComparison.Ordinal);
        Assert.Contains("只根据输入 text", prompt, StringComparison.Ordinal);
        Assert.Contains("brow: [", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("brow(", prompt, StringComparison.Ordinal);
        Assert.Contains("皱眉", prompt, StringComparison.Ordinal);
        Assert.Contains("eye: [", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("eye(", prompt, StringComparison.Ordinal);
        Assert.Contains("闭眼", prompt, StringComparison.Ordinal);
        Assert.Contains("mouth: [", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("mouth(", prompt, StringComparison.Ordinal);
        Assert.Contains("张嘴", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("expression", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("整体表情", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseSelectionReturnsStructuredLayerNames()
    {
        var result = PsdLayerAiSelectionService.ParseSelection(
            """
            {"brow":"皱眉","eye":"闭眼","mouth":"张嘴","leftHand":"伸手","rightHand":"握拳"}
            """,
            TestFacialPartsConfiguration());

        Assert.Equal(["皱眉", "闭眼", "张嘴", "伸手", "握拳"], result.LayerNames);
        Assert.Equal("皱眉", result.Brow);
        Assert.Equal("闭眼", result.Eye);
        Assert.Equal("张嘴", result.Mouth);
        Assert.Equal("伸手", result.LeftHand);
        Assert.Equal("握拳", result.RightHand);
    }

    [Fact]
    public void ParseSelectionIgnoresLayerOutsideConfiguredCandidates()
    {
        var result = PsdLayerAiSelectionService.ParseSelection(
            """
            {"brow":"不存在的眉毛","eye":"闭眼","mouth":""}
            """,
            TestFacialPartsConfiguration());

        Assert.Equal(["闭眼"], result.LayerNames);
        Assert.Equal(string.Empty, result.Brow);
        Assert.Equal("闭眼", result.Eye);
        Assert.Equal(string.Empty, result.Mouth);
    }

    [Fact]
    public async Task SelectAsyncUsesJsonSchemaAndConfiguredPrompt()
    {
        var client = new FakeAiModelClient(
            """
            {"brow":"皱眉","eye":"闭眼","mouth":"张嘴","leftHand":"伸手","rightHand":""}
            """);
        var service = new PsdLayerAiSelectionService(client);

        var result = await service.SelectAsync(new PsdLayerAiSelectionRequest(
            "陈",
            "我来晚了",
            TestFacialPartsConfiguration()));

        var request = Assert.Single(client.JsonRequests);
        Assert.Equal("psd_layer_selection_v1", request.SchemaName);
        Assert.Contains("皱眉", request.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("expression", request.SystemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("整体表情", request.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("动作(动作)", request.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("\"action\"", request.SystemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("固定词汇表：", request.SystemPrompt, StringComparison.Ordinal);
        Assert.StartsWith("文本：", request.UserText, StringComparison.Ordinal);
        Assert.DoesNotContain("固定词汇表：", request.UserText, StringComparison.Ordinal);
        Assert.DoesNotContain("皱眉", request.UserText, StringComparison.Ordinal);
        Assert.Contains("我来晚了", request.UserText, StringComparison.Ordinal);
        Assert.True(
            request.SystemPrompt.IndexOf("返回 JSON 格式：", StringComparison.Ordinal) <
            request.SystemPrompt.IndexOf("固定词汇表：", StringComparison.Ordinal));
        Assert.Equal(["皱眉", "闭眼", "张嘴", "伸手"], result.LayerNames);
    }

    [Fact]
    public async Task SelectRowsAsyncSendsAllRowsForRoleAndParsesRowsJson()
    {
        var client = new FakeAiModelClient(
            """
            {"rows":[
              {"interactionId":"int_001","brow":"皱眉","eye":"闭眼","mouth":"张嘴","leftHand":"伸手","rightHand":""},
              {"interactionId":"int_002","brow":"挑眉","eye":"睁眼","mouth":"闭嘴","leftHand":"","rightHand":"摊手"}
            ]}
            """);
        var service = new PsdLayerAiSelectionService(client);

        var result = await service.SelectRowsAsync(new PsdLayerAiBatchSelectionRequest(
            "陈",
            [
                new PsdLayerAiSelectionRowRequest("int_001", "我来晚了"),
                new PsdLayerAiSelectionRowRequest("int_002", "别走")
            ],
            TestFacialPartsConfiguration()));

        var request = Assert.Single(client.JsonRequests);
        Assert.Equal("psd_layer_selection_v1", request.SchemaName);
        Assert.StartsWith("你是固定词汇分类器", request.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("固定词汇表：", request.SystemPrompt, StringComparison.Ordinal);
        Assert.StartsWith("rows:", request.UserText, StringComparison.Ordinal);
        Assert.Contains("\"interactionId\":\"int_001\"", request.UserText, StringComparison.Ordinal);
        Assert.Contains("\"interactionId\":\"int_002\"", request.UserText, StringComparison.Ordinal);
        Assert.Contains("7. rows 必须与输入行一一对应", request.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("8. rows 必须与输入行一一对应", request.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("10. rows 必须与输入行一一对应", request.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("11. rows 必须与输入行一一对应", request.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("普通说话文本可选择 mouth", request.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("brow: [", request.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("mouth: [", request.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("brow(", request.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("mouth(", request.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("rows:", request.UserText, StringComparison.Ordinal);
        Assert.True(
            request.SystemPrompt.IndexOf("返回 JSON 格式：", StringComparison.Ordinal) <
            request.SystemPrompt.IndexOf("固定词汇表：", StringComparison.Ordinal));
        Assert.DoesNotContain("固定词汇表：", request.UserText, StringComparison.Ordinal);
        Assert.DoesNotContain("角色：", request.UserText, StringComparison.Ordinal);
        Assert.DoesNotContain("说明：", request.UserText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"scene\"", request.UserText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"sourceItemType\"", request.UserText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"emotion\"", request.UserText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"action\"", request.UserText, StringComparison.Ordinal);
        Assert.DoesNotContain("雨夜", request.UserText, StringComparison.Ordinal);
        Assert.DoesNotContain("愧疚", request.UserText, StringComparison.Ordinal);
        Assert.DoesNotContain("悲伤", request.UserText, StringComparison.Ordinal);
        Assert.Equal(2, result.Count);
        Assert.Equal("int_001", result[0].InteractionId);
        Assert.Equal(["皱眉", "闭眼", "张嘴", "伸手"], result[0].Selection.LayerNames);
        Assert.Equal("int_002", result[1].InteractionId);
        Assert.Equal(["挑眉", "睁眼", "闭嘴", "摊手"], result[1].Selection.LayerNames);
    }

    private static PsdLayerMatchConfiguration TestWholeExpressionConfiguration()
    {
        return new PsdLayerMatchConfiguration(
            ExpressionGroup: new PsdLayerGroupCatalog("表情", ["开心", "悲伤"]),
            BrowGroup: new PsdLayerGroupCatalog("眉毛", ["皱眉", "挑眉"]),
            EyeGroup: new PsdLayerGroupCatalog("眼睛", ["闭眼", "睁眼"]),
            MouthGroup: new PsdLayerGroupCatalog("嘴巴", ["张嘴", "闭嘴"]),
            ActionGroup: new PsdLayerGroupCatalog("动作", ["抬手", "放手"]),
            LeftHandGroup: new PsdLayerGroupCatalog("左手", ["伸手", "垂手"]),
            RightHandGroup: new PsdLayerGroupCatalog("右手", ["握拳", "摊手"]));
    }

    private static PsdLayerMatchConfiguration TestFacialPartsConfiguration()
    {
        return new PsdLayerMatchConfiguration(
            BrowGroup: new PsdLayerGroupCatalog("眉毛", ["皱眉", "挑眉"]),
            EyeGroup: new PsdLayerGroupCatalog("眼睛", ["闭眼", "睁眼"]),
            MouthGroup: new PsdLayerGroupCatalog("嘴巴", ["张嘴", "闭嘴"]),
            ActionGroup: new PsdLayerGroupCatalog("动作", ["抬手", "放手"]),
            LeftHandGroup: new PsdLayerGroupCatalog("左手", ["伸手", "垂手"]),
            RightHandGroup: new PsdLayerGroupCatalog("右手", ["握拳", "摊手"]));
    }

    private sealed class FakeAiModelClient : IAiModelClient
    {
        private readonly string _json;

        public FakeAiModelClient(string json)
        {
            _json = json;
        }

        public List<AiModelJsonRequest> JsonRequests { get; } = [];

        public Task<AiModelJsonResponse> GenerateJsonAsync(
            AiModelJsonRequest request,
            CancellationToken cancellationToken = default)
        {
            JsonRequests.Add(request);
            return Task.FromResult(new AiModelJsonResponse(
                _json,
                _json,
                new AiModelReplyMetadata("Fake", "fake-model", "stop", AiReplyState.Completed, "ok")));
        }

        public Task<AiModelTextResponse> GenerateTextAsync(
            AiModelTextRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<AiConnectionCheckResult> CheckConnectionAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AiConnectionCheckResult(
                "Fake",
                "fake-model",
                "mock://ai",
                AiConnectionState.Connected,
                200,
                "ok"));
        }
    }
}
