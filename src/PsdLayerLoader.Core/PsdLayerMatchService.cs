namespace PsdLayerLoader.Core;

public sealed class PsdLayerMatchService
{
    private static readonly IReadOnlyList<SemanticLayerGroupRule> SemanticGroupRules =
    [
        new(
            SemanticLayerGroupKind.Expression,
            ["表情", "表情组", "expression", "emotion", "face", "脸", "顔"],
            ["开心", "高兴", "快乐", "笑", "happy", "smile", "sad", "悲伤", "难过", "angry", "愤怒", "生气", "calm", "平静", "普通", "默认", "normal", "surprised", "惊讶", "吃惊", "fear", "害怕", "disgust", "厌恶", "melancholy", "忧郁"]),
        new(
            SemanticLayerGroupKind.Eyes,
            ["眼", "眼睛", "eyes", "eye", "目"],
            ["左看", "右看", "看左", "看右", "闭眼", "睁眼", "平静", "开心", "悲伤", "愤怒", "left", "right", "closed", "open", "calm", "happy", "sad", "angry"]),
        new(
            SemanticLayerGroupKind.Mouth,
            ["嘴", "嘴巴", "口", "mouth", "lip", "lips"],
            ["说话", "张嘴", "开口", "闭嘴", "默认", "普通", "talk", "speak", "open", "close", "closed", "normal"]),
        new(
            SemanticLayerGroupKind.Hand,
            ["手", "手势", "hand", "gesture", "arm", "arms", "胳膊"],
            ["指向", "指着", "抬手", "举手", "挥手", "招手", "握拳", "攥紧", "摊手", "抱臂", "默认", "普通", "point", "raise", "wave", "fist", "open", "cross", "idle", "normal"]),
        new(
            SemanticLayerGroupKind.Action,
            ["动作", "姿势", "身体动作", "pose", "action", "body"],
            ["转身", "回头", "低头", "抬头", "靠近", "上前", "后退", "左", "右", "默认", "普通", "turn", "look", "down", "up", "near", "back", "left", "right", "idle", "normal"])
    ];

    public PsdLayerMatchResult Match(
        PsdDocumentInfo document,
        string emotion,
        string action,
        IReadOnlyList<PsdLayerPreset> presets)
    {
        return Match(document, emotion, action, null, presets);
    }

    public PsdLayerMatchResult Match(
        PsdDocumentInfo document,
        string emotion,
        string action,
        string? text,
        IReadOnlyList<PsdLayerPreset> presets)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(emotion))
        {
            throw new ArgumentException("Emotion is required.", nameof(emotion));
        }

        if (string.IsNullOrWhiteSpace(action))
        {
            throw new ArgumentException("Action is required.", nameof(action));
        }

        var exactPreset = presets.FirstOrDefault(preset =>
            EqualsIgnoreCase(preset.Emotion, emotion) &&
            preset.Actions.Any(candidate => EqualsIgnoreCase(candidate, action)));
        if (exactPreset is not null)
        {
            return BuildPresetResult(document, exactPreset, emotion, action, 1.0);
        }

        var emotionPreset = presets.FirstOrDefault(preset => EqualsIgnoreCase(preset.Emotion, emotion));
        if (emotionPreset is not null)
        {
            return BuildPresetResult(document, emotionPreset, emotion, action, 0.85);
        }

        var actionPreset = presets.FirstOrDefault(preset =>
            preset.Actions.Any(candidate => EqualsIgnoreCase(candidate, action)));
        if (actionPreset is not null)
        {
            return BuildPresetResult(document, actionPreset, emotion, action, 0.75);
        }

        var configuredLayerMatch = MatchConfiguredLayerNames(document, emotion, action, text);
        if (configuredLayerMatch is not null)
        {
            return configuredLayerMatch;
        }

        var semanticMatch = MatchBySemanticGroups(document, emotion, action, text);
        if (semanticMatch.Confidence >= 0.78)
        {
            return semanticMatch;
        }

        var layerNameMatch = MatchByLayerNames(document, emotion, action);
        if (layerNameMatch.LayerIds.Count > 0)
        {
            return layerNameMatch;
        }

        var defaults = document.FlattenLayers()
            .Where(layer => !layer.IsGroup && layer.IsVisibleByDefault)
            .ToList();
        return new PsdLayerMatchResult(
            defaults.Count == 0 ? 0.0 : 0.5,
            emotion,
            action,
            null,
            defaults.Select(layer => layer.LayerId).ToArray(),
            defaults.Select(layer => layer.Name).ToArray());
    }

    private static PsdLayerMatchResult? MatchConfiguredLayerNames(
        PsdDocumentInfo document,
        string emotion,
        string action,
        string? text)
    {
        if (string.IsNullOrWhiteSpace(text) ||
            (!text.Contains("PSD", StringComparison.OrdinalIgnoreCase) &&
             !text.Contains("角色状态", StringComparison.Ordinal) &&
             !text.Contains("固定词汇", StringComparison.Ordinal) &&
             !text.Contains("可选状态", StringComparison.Ordinal)))
        {
            return null;
        }

        var configuredNames = ExtractConfiguredLayerNames(text);
        if (configuredNames.Count == 0)
        {
            return null;
        }

        var renderLayers = BuildLayerContexts(document)
            .Where(layer => !layer.Node.IsGroup)
            .ToArray();
        var intentTokens = BuildIntentTokens($"{emotion} {action} {text}", IntentTokenKind.Action)
            .Concat(BuildIntentTokens(emotion, IntentTokenKind.Emotion))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var selected = renderLayers
            .Where(layer => configuredNames.Any(name => ConfiguredLayerNameMatches(layer, name)))
            .Where(layer => LayerMatchesAny(layer, intentTokens) || layer.Node.IsVisibleByDefault)
            .ToList();
        if (selected.Count == 0)
        {
            selected = renderLayers
                .Where(layer => configuredNames.Any(name => ConfiguredLayerNameMatches(layer, name)))
                .Take(1)
                .ToList();
        }

        if (selected.Count == 0)
        {
            return null;
        }

        foreach (var defaultLayer in renderLayers.Where(layer => layer.Node.IsVisibleByDefault))
        {
            if (selected.All(layer => layer.Node.LayerId != defaultLayer.Node.LayerId))
            {
                selected.Add(defaultLayer);
            }
        }

        var ordered = selected
            .OrderBy(layer => layer.Node.LayerId)
            .ToArray();
        return new PsdLayerMatchResult(
            0.9,
            emotion,
            action,
            null,
            ordered.Select(layer => layer.Node.LayerId).ToArray(),
            ordered.Select(layer => layer.Node.Name).ToArray());
    }

    private static IReadOnlyList<string> ExtractConfiguredLayerNames(string text)
    {
        var names = new List<string>();
        foreach (var marker in new[]
        {
            "只能使用这些动作图层名描述：",
            "状态只能从这些图层名中选择：",
            "只能从这些固定词汇中选择：",
            "动作可选状态：",
            "表情可选状态："
        })
        {
            var start = text.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
            {
                continue;
            }

            start += marker.Length;
            var end = text.IndexOf('。', start);
            var segment = end < 0 ? text[start..] : text[start..end];
            names.AddRange(segment.Split(['，', ',', '、', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool EqualsConfiguredLayerName(string layerName, string configuredName)
    {
        return string.Equals(Normalize(layerName), Normalize(configuredName), StringComparison.OrdinalIgnoreCase);
    }

    private static bool ConfiguredLayerNameMatches(PsdLayerContext layer, string configuredName)
    {
        if (EqualsConfiguredLayerName(layer.Node.Name, configuredName))
        {
            return true;
        }

        var normalizedSearchText = Normalize(layer.SearchText);
        var normalizedConfiguredName = Normalize(configuredName);
        return !string.IsNullOrWhiteSpace(normalizedConfiguredName) &&
            normalizedSearchText.Contains(normalizedConfiguredName, StringComparison.OrdinalIgnoreCase);
    }

    private static PsdLayerMatchResult BuildPresetResult(
        PsdDocumentInfo document,
        PsdLayerPreset preset,
        string emotion,
        string action,
        double confidence)
    {
        var layerById = document.FlattenLayers().ToDictionary(layer => layer.LayerId);
        var names = preset.LayerIds
            .Select(id => layerById.TryGetValue(id, out var layer) ? layer.Name : id.ToStringInvariant())
            .ToArray();
        return new PsdLayerMatchResult(
            confidence,
            emotion,
            action,
            preset.PresetId,
            preset.LayerIds.ToArray(),
            names);
    }

    private static PsdLayerMatchResult MatchBySemanticGroups(
        PsdDocumentInfo document,
        string emotion,
        string action,
        string? text)
    {
        var layers = BuildLayerContexts(document);
        var renderLayers = layers
            .Where(layer => !layer.Node.IsGroup)
            .ToArray();
        var selectedIds = new HashSet<int>();
        var selectedSemanticGroupIds = new HashSet<int>();
        var matchedSemanticGroup = false;
        var emotionTokens = BuildIntentTokens(emotion, IntentTokenKind.Emotion);
        var actionTokens = BuildIntentTokens(action, IntentTokenKind.Action);
        var mouthTokens = HasSpeechIntent(action, text)
            ? BuildIntentTokens("说话 张嘴 talk speak open", IntentTokenKind.Action)
            : BuildIntentTokens("闭嘴 默认 normal closed", IntentTokenKind.Action);

        foreach (var group in layers.Where(layer => layer.Node.IsGroup))
        {
            var groupKind = ClassifyGroup(group);
            if (groupKind == SemanticLayerGroupKind.None)
            {
                continue;
            }

            var descendantRenderLayers = renderLayers
                .Where(layer => IsDescendantOf(layer, group.Node.LayerId))
                .ToArray();
            if (descendantRenderLayers.Length == 0)
            {
                continue;
            }

            var tokens = groupKind switch
            {
                SemanticLayerGroupKind.Expression or SemanticLayerGroupKind.Eyes => emotionTokens,
                SemanticLayerGroupKind.Mouth => mouthTokens,
                _ => actionTokens
            };
            var candidateLayers = layers
                .Where(layer => IsDescendantOf(layer, group.Node.LayerId))
                .Where(layer => !layer.Node.IsGroup || layer.Node.LayerId != group.Node.LayerId)
                .ToArray();
            var best = BestSemanticLayer(candidateLayers, tokens);
            best ??= descendantRenderLayers.FirstOrDefault(layer => layer.Node.IsVisibleByDefault);
            if (best is null)
            {
                continue;
            }

            foreach (var selectedLayer in ExpandToRenderableLayers(best, descendantRenderLayers))
            {
                selectedIds.Add(selectedLayer.Node.LayerId);
            }

            selectedSemanticGroupIds.Add(group.Node.LayerId);
            matchedSemanticGroup = matchedSemanticGroup || LayerMatchesAny(best, tokens);
        }

        foreach (var layer in renderLayers)
        {
            if (!layer.Node.IsVisibleByDefault ||
                HasSemanticAncestor(layer, selectedSemanticGroupIds))
            {
                continue;
            }

            selectedIds.Add(layer.Node.LayerId);
        }

        var selected = renderLayers
            .Where(layer => selectedIds.Contains(layer.Node.LayerId))
            .ToArray();
        return new PsdLayerMatchResult(
            selected.Length == 0 ? 0.0 : matchedSemanticGroup ? 0.78 : 0.5,
            emotion,
            action,
            null,
            selected.Select(layer => layer.Node.LayerId).ToArray(),
            selected.Select(layer => layer.Node.Name).ToArray());
    }

    private static IReadOnlyList<PsdLayerContext> ExpandToRenderableLayers(
        PsdLayerContext selected,
        IReadOnlyList<PsdLayerContext> groupRenderLayers)
    {
        if (!selected.Node.IsGroup)
        {
            return [selected];
        }

        var descendants = groupRenderLayers
            .Where(layer => IsDescendantOf(layer, selected.Node.LayerId))
            .ToArray();
        return descendants.Length > 0 ? descendants : [];
    }

    private static PsdLayerMatchResult MatchByLayerNames(PsdDocumentInfo document, string emotion, string action)
    {
        var layers = BuildLayerContexts(document)
            .Where(layer => !layer.Node.IsGroup)
            .ToArray();
        var emotionTokens = BuildIntentTokens(emotion, IntentTokenKind.Emotion);
        var actionTokens = BuildIntentTokens(action, IntentTokenKind.Action);
        var selected = layers
            .Where(layer =>
                layer.Node.IsVisibleByDefault ||
                LayerMatchesAny(layer, emotionTokens) ||
                LayerMatchesAny(layer, actionTokens))
            .OrderBy(layer => layer.Node.LayerId)
            .ToArray();
        var matchedIntentLayer = selected.Any(layer =>
            !layer.Node.IsVisibleByDefault &&
            (LayerMatchesAny(layer, emotionTokens) || LayerMatchesAny(layer, actionTokens)));

        return new PsdLayerMatchResult(
            selected.Length == 0 ? 0.0 : matchedIntentLayer ? 0.72 : 0.5,
            emotion,
            action,
            null,
            selected.Select(layer => layer.Node.LayerId).ToArray(),
            selected.Select(layer => layer.Node.Name).ToArray());
    }

    private static PsdLayerContext[] BuildLayerContexts(PsdDocumentInfo document)
    {
        var contexts = new List<PsdLayerContext>();
        foreach (var root in document.Layers)
        {
            AddLayerContext(contexts, root, []);
        }

        return contexts.ToArray();
    }

    private static void AddLayerContext(
        List<PsdLayerContext> contexts,
        PsdLayerNode node,
        IReadOnlyList<PsdLayerNode> ancestors)
    {
        contexts.Add(new PsdLayerContext(node, ancestors));
        var nextAncestors = ancestors.Concat([node]).ToArray();
        foreach (var child in node.Children)
        {
            AddLayerContext(contexts, child, nextAncestors);
        }
    }

    private static PsdLayerContext? BestSemanticLayer(
        IReadOnlyList<PsdLayerContext> layers,
        IReadOnlyList<string> tokens)
    {
        PsdLayerContext? best = null;
        var bestScore = 0;
        foreach (var layer in layers)
        {
            var score = MatchScore(layer, tokens);
            if (score <= bestScore)
            {
                continue;
            }

            best = layer;
            bestScore = score;
        }

        return bestScore == 0 ? null : best;
    }

    private static int MatchScore(PsdLayerContext layer, IReadOnlyList<string> tokens)
    {
        var normalized = Normalize(layer.SearchText);
        var score = 0;
        foreach (var token in tokens)
        {
            var normalizedToken = Normalize(token);
            if (string.IsNullOrWhiteSpace(normalizedToken))
            {
                continue;
            }

            if (string.Equals(normalized, normalizedToken, StringComparison.OrdinalIgnoreCase))
            {
                score = Math.Max(score, 100);
            }
            else if (normalized.Contains(normalizedToken, StringComparison.OrdinalIgnoreCase))
            {
                score = Math.Max(score, Math.Max(10, normalizedToken.Length));
            }
        }

        return score;
    }

    private static bool LayerMatchesAny(PsdLayerContext layer, IReadOnlyList<string> tokens)
    {
        return MatchScore(layer, tokens) > 0;
    }

    private static SemanticLayerGroupKind ClassifyGroup(PsdLayerContext group)
    {
        var normalized = Normalize(group.SearchText);
        foreach (var rule in SemanticGroupRules)
        {
            if (rule.GroupTokens.Any(token => normalized.Contains(Normalize(token), StringComparison.OrdinalIgnoreCase)))
            {
                return rule.Kind;
            }
        }

        return SemanticLayerGroupKind.None;
    }

    private static IReadOnlyList<string> BuildIntentTokens(string value, IntentTokenKind kind)
    {
        var normalized = Normalize(value);
        var tokens = value
            .Split(['_', '-', '/', ' ', '\t', '，', ',', '。', '：', ':', '；', ';', '、', '(', ')', '（', '）'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Append(value)
            .Append(normalized)
            .ToList();

        if (kind == IntentTokenKind.Emotion)
        {
            AddEmotionAliases(normalized, tokens);
        }
        else
        {
            AddActionAliases(normalized, tokens);
        }

        return tokens
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddEmotionAliases(string normalized, List<string> tokens)
    {
        AddAliasesIfContains(normalized, tokens, ["happy", "开心", "高兴", "快乐", "笑"], ["happy", "smile", "开心", "高兴", "笑"]);
        AddAliasesIfContains(normalized, tokens, ["sad", "悲伤", "难过"], ["sad", "悲伤", "难过"]);
        AddAliasesIfContains(normalized, tokens, ["angry", "愤怒", "生气"], ["angry", "愤怒", "生气"]);
        AddAliasesIfContains(normalized, tokens, ["calm", "平静", "克制", "普通", "默认"], ["calm", "normal", "平静", "普通", "默认"]);
        AddAliasesIfContains(normalized, tokens, ["surprise", "surprised", "惊讶", "吃惊"], ["surprise", "surprised", "惊讶", "吃惊"]);
        AddAliasesIfContains(normalized, tokens, ["fear", "害怕", "惊恐"], ["fear", "害怕", "惊恐"]);
        AddAliasesIfContains(normalized, tokens, ["disgust", "厌恶"], ["disgust", "厌恶"]);
        AddAliasesIfContains(normalized, tokens, ["melancholy", "忧郁"], ["melancholy", "忧郁"]);
    }

    private static void AddActionAliases(string normalized, List<string> tokens)
    {
        AddAliasesIfContains(normalized, tokens, ["指向", "指着", "手指", "point"], ["指向", "指着", "point"]);
        AddAliasesIfContains(normalized, tokens, ["抬手", "举手", "raise"], ["抬手", "举手", "raise"]);
        AddAliasesIfContains(normalized, tokens, ["挥手", "招手", "wave"], ["挥手", "招手", "wave"]);
        AddAliasesIfContains(normalized, tokens, ["握拳", "攥紧", "fist"], ["握拳", "攥紧", "fist"]);
        AddAliasesIfContains(normalized, tokens, ["摊手", "openhand"], ["摊手", "open"]);
        AddAliasesIfContains(normalized, tokens, ["抱臂", "交叉", "cross"], ["抱臂", "交叉", "cross"]);
        AddAliasesIfContains(normalized, tokens, ["转身", "回头", "turn"], ["转身", "回头", "turn"]);
        AddAliasesIfContains(normalized, tokens, ["低头", "down"], ["低头", "down"]);
        AddAliasesIfContains(normalized, tokens, ["抬头", "up"], ["抬头", "up"]);
        AddAliasesIfContains(normalized, tokens, ["左", "left"], ["左", "left"]);
        AddAliasesIfContains(normalized, tokens, ["右", "right"], ["右", "right"]);
        AddAliasesIfContains(normalized, tokens, ["默认", "普通", "idle", "normal"], ["默认", "普通", "idle", "normal"]);
    }

    private static bool HasSpeechIntent(string action, string? text)
    {
        var normalizedAction = Normalize(action);
        var normalizedText = Normalize(text ?? string.Empty);
        return normalizedAction.Contains("说", StringComparison.OrdinalIgnoreCase) ||
            normalizedAction.Contains("台词", StringComparison.OrdinalIgnoreCase) ||
            normalizedAction.Contains("dialogue", StringComparison.OrdinalIgnoreCase) ||
            normalizedAction.Contains("speak", StringComparison.OrdinalIgnoreCase) ||
            normalizedAction.Contains("talk", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(normalizedText);
    }

    private static void AddAliasesIfContains(
        string normalized,
        List<string> tokens,
        IReadOnlyList<string> triggers,
        IReadOnlyList<string> aliases)
    {
        if (!triggers.Any(trigger => normalized.Contains(Normalize(trigger), StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        tokens.AddRange(aliases);
    }

    private static bool IsDescendantOf(PsdLayerContext layer, int groupLayerId)
    {
        return layer.Ancestors.Any(ancestor => ancestor.LayerId == groupLayerId);
    }

    private static bool HasSemanticAncestor(PsdLayerContext layer, IReadOnlySet<int> selectedSemanticGroupIds)
    {
        return layer.Ancestors.Any(ancestor => selectedSemanticGroupIds.Contains(ancestor.LayerId));
    }

    private static string Normalize(string value)
    {
        return string.Concat(value
            .Where(character => !char.IsWhiteSpace(character) && !IsIgnoredSeparator(character)))
            .Trim()
            .ToLowerInvariant();
    }

    private static bool IsIgnoredSeparator(char character)
    {
        return character is '_' or '-' or '/' or '\\' or '：' or ':' or '，' or ',' or '。' or '；' or ';' or '、' or '(' or ')' or '（' or '）';
    }

    private static bool EqualsIgnoreCase(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record SemanticLayerGroupRule(
        SemanticLayerGroupKind Kind,
        IReadOnlyList<string> GroupTokens,
        IReadOnlyList<string> LayerTokens);

    private sealed record PsdLayerContext(PsdLayerNode Node, IReadOnlyList<PsdLayerNode> Ancestors)
    {
        public string SearchText => string.Join("/", Ancestors.Select(ancestor => ancestor.Name).Append(Node.Name));
    }

    private enum IntentTokenKind
    {
        Emotion,
        Action
    }

    private enum SemanticLayerGroupKind
    {
        None,
        Expression,
        Eyes,
        Mouth,
        Hand,
        Action
    }
}

internal static class PsdLayerInvariantExtensions
{
    public static string ToStringInvariant(this int value)
    {
        return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
