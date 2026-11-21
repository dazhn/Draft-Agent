using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DraftAgent.Core;

namespace DraftAgent.App.ViewModels;

internal sealed class PsdLayerAiSelectionService
{
    private readonly IAiModelClient _aiClient;

    public PsdLayerAiSelectionService(IAiModelClient aiClient)
    {
        _aiClient = aiClient;
    }

    public async Task<PsdLayerAiSelectionResult> SelectAsync(
        PsdLayerAiSelectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Configuration is null || !request.Configuration.HasAnyGroup)
        {
            throw new InvalidOperationException("PSD 图层模板为空。");
        }

        var response = await _aiClient.GenerateJsonAsync(
            new AiModelJsonRequest(
                "psd_layer_selection_v1",
                BuildSystemPrompt(request.Configuration),
                BuildUserText(request)),
            cancellationToken);
        return ParseSelection(response.Json, request.Configuration);
    }

    public async Task<IReadOnlyList<PsdLayerAiRowSelectionResult>> SelectRowsAsync(
        PsdLayerAiBatchSelectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Configuration is null || !request.Configuration.HasAnyGroup)
        {
            throw new InvalidOperationException("PSD 图层模板为空。");
        }

        if (request.Rows.Count == 0)
        {
            return [];
        }

        var response = await _aiClient.GenerateJsonAsync(
            new AiModelJsonRequest(
                "psd_layer_selection_v1",
                BuildSystemPrompt(request.Configuration, batchMode: true),
                BuildBatchUserText(request)),
            cancellationToken);
        return ParseBatchSelection(response.Json, request.Configuration, request.Rows);
    }

    internal static string BuildSystemPrompt(PsdLayerMatchConfiguration configuration, bool batchMode = false)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return string.Join(Environment.NewLine,
            "你是固定词汇分类器。只输出 JSON。",
            "任务：只根据输入 text，从固定词汇表中为 brow、eye、mouth、leftHand、rightHand 各选择一个状态词。",
            "硬规则：",
            "1. 输出字段固定，不得新增字段，不得解释，不得输出 Markdown。",
            "2. 每个字段的值只能是该字段候选词中的一个完整词，或空字符串 \"\"。",
            "3. 禁止改写候选词，禁止组合候选词，禁止创造候选词。",
            "4. brow 只表示眉毛状态；eye 只表示眼睛状态；mouth 只表示嘴部状态。",
            "5. leftHand 只表示左手状态；rightHand 只表示右手状态。",
            "6. 字段之间不得互相代替：嘴部状态不能填到 eye，眼部状态不能填到 brow，手部状态不能填到 mouth。",
            batchMode
                ? "7. rows 必须与输入行一一对应；interactionId 必须原样返回；不得遗漏，不得新增。"
                : "7. 返回单行结果。",
            "返回 JSON 格式：",
            batchMode ? BatchReturnShape() : SingleReturnShape(),
            string.Empty,
            BuildCatalogText(configuration));
    }

    private static string SingleReturnShape()
    {
        return """
            {
              "brow": "",
              "eye": "",
              "mouth": "",
              "leftHand": "",
              "rightHand": ""
            }
            """;
    }

    private static string BatchReturnShape()
    {
        return """
            {
              "rows": [
                {
                  "interactionId": "",
                  "brow": "",
                  "eye": "",
                  "mouth": "",
                  "leftHand": "",
                  "rightHand": ""
                }
              ]
            }
            """;
    }

    internal static string BuildUserText(PsdLayerAiSelectionRequest request)
    {
        return $"文本：{request.RowText}";
    }

    internal static string BuildBatchUserText(PsdLayerAiBatchSelectionRequest request)
    {
        var rows = request.Rows.Select(row =>
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["interactionId"] = row.InteractionId,
                ["text"] = row.RowText
            });
        var rowsJson = JsonSerializer.Serialize(rows, JsonOptions);
        return string.Join(Environment.NewLine,
            "rows:",
            rowsJson);
    }

    private static string BuildCatalogText(PsdLayerMatchConfiguration configuration)
    {
        return string.Join(Environment.NewLine,
            "固定词汇表：",
            FormatCatalog("brow", configuration.BrowGroup),
            FormatCatalog("eye", configuration.EyeGroup),
            FormatCatalog("mouth", configuration.MouthGroup),
            FormatCatalog("leftHand", configuration.LeftHandGroup),
            FormatCatalog("rightHand", configuration.RightHandGroup));
    }

    internal static PsdLayerAiSelectionResult ParseSelection(
        string json,
        PsdLayerMatchConfiguration configuration)
    {
        var reply = JsonSerializer.Deserialize<PsdLayerAiSelectionReply>(json, JsonOptions)
            ?? throw new InvalidOperationException("角色状态 AI 返回为空。");
        var selected = new List<string>();
        var brow = AddSelected(selected, reply.Brow, configuration.BrowGroup);
        var eye = AddSelected(selected, reply.Eye, configuration.EyeGroup);
        var mouth = AddSelected(selected, reply.Mouth, configuration.MouthGroup);
        var leftHand = AddSelected(selected, reply.LeftHand, configuration.LeftHandGroup);
        var rightHand = AddSelected(selected, reply.RightHand, configuration.RightHandGroup);
        return new PsdLayerAiSelectionResult(
            selected.Distinct(StringComparer.Ordinal).ToArray(),
            brow,
            eye,
            mouth,
            leftHand,
            rightHand);
    }

    internal static IReadOnlyList<PsdLayerAiRowSelectionResult> ParseBatchSelection(
        string json,
        PsdLayerMatchConfiguration configuration,
        IReadOnlyList<PsdLayerAiSelectionRowRequest> expectedRows)
    {
        var reply = JsonSerializer.Deserialize<PsdLayerAiBatchSelectionReply>(json, JsonOptions)
            ?? throw new InvalidOperationException("角色状态 AI 返回为空。");
        var expectedIds = expectedRows
            .Select(row => row.InteractionId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        var results = new List<PsdLayerAiRowSelectionResult>();
        foreach (var row in reply.Rows)
        {
            var interactionId = row.InteractionId?.Trim() ?? string.Empty;
            if (!expectedIds.Contains(interactionId))
            {
                throw new InvalidOperationException($"角色状态 AI 返回了不在请求范围内的 interactionId: {interactionId}");
            }

            var selection = ParseSelection(JsonSerializer.Serialize(row, JsonOptions), configuration);
            results.Add(new PsdLayerAiRowSelectionResult(interactionId, selection));
        }

        var returnedIds = results.Select(result => result.InteractionId).ToHashSet(StringComparer.Ordinal);
        var missing = expectedIds.Where(id => !returnedIds.Contains(id)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException($"角色状态 AI 缺少行结果: {string.Join(", ", missing)}");
        }

        return results;
    }

    private static string AddSelected(
        ICollection<string> selected,
        string? value,
        PsdLayerGroupCatalog? catalog)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (catalog is null ||
            !catalog.LayerNames.Any(name => string.Equals(name, normalized, StringComparison.Ordinal)))
        {
            return string.Empty;
        }

        selected.Add(normalized);
        return normalized;
    }

    private static string FormatCatalog(string label, PsdLayerGroupCatalog? catalog)
    {
        if (catalog is null || catalog.LayerNames.Count == 0)
        {
            return $"{label}: []";
        }

        return $"{label}: [{string.Join(", ", catalog.LayerNames)}]";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private sealed record PsdLayerAiSelectionReply(
        string? InteractionId,
        string? Brow,
        string? Eye,
        string? Mouth,
        string? LeftHand,
        string? RightHand);

    private sealed record PsdLayerAiBatchSelectionReply(
        IReadOnlyList<PsdLayerAiSelectionReply> Rows);
}

internal sealed record PsdLayerAiSelectionRequest(
    string CharacterName,
    string RowText,
    PsdLayerMatchConfiguration Configuration);

internal sealed record PsdLayerAiBatchSelectionRequest(
    string CharacterName,
    IReadOnlyList<PsdLayerAiSelectionRowRequest> Rows,
    PsdLayerMatchConfiguration Configuration);

internal sealed record PsdLayerAiSelectionRowRequest(
    string InteractionId,
    string RowText);

internal sealed record PsdLayerAiSelectionResult(
    IReadOnlyList<string> LayerNames,
    string Brow,
    string Eye,
    string Mouth,
    string LeftHand,
    string RightHand);

internal sealed record PsdLayerAiRowSelectionResult(
    string InteractionId,
    PsdLayerAiSelectionResult Selection);
