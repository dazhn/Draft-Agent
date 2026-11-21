using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DraftAgent.Core;

public sealed partial class DraftAnalyzer
{
    public const string SampleScript = """
雨夜街口，只剩路灯亮着。
林安心想：他怎么还没来？
林安低头看手机。
手指攥紧雨伞。
林安说：雨这么大，你知道我等了多久吗？
陈默从街角跑来。
陈默抬手想解释。
又停在半空。
陈默说：对不起，我来晚了。
陈默心里一沉。
这句道歉太轻了。
林安摸摸自己的头。
苦笑了一下。
林安说：这场雨里，你从来没有相信过我。
雨声盖住两个人的沉默。
办公室只剩电脑屏幕亮着。
林安把文件放到桌上。
林安说：明天的会议，我不会再替你解释。
陈默看着那份文件。
陈默心想：这次真的麻烦了。
""";

    private static readonly string[] ActionWords =
    [
        "站", "坐", "等", "低头", "看", "盯", "攥紧", "跑来", "喘", "抬头", "抬手", "举手", "挥手", "摸", "摸摸", "苦笑", "停", "伸手", "收回", "转身", "离开", "沉默", "推门", "进来", "开门", "关门", "拿", "放", "走", "皱眉"
    ];

    private static readonly HashSet<string> NonCharacterNameTerms =
    [
        "旁白", "旁白叙述", "用户", "镜头", "场景", "场景描述", "画面", "字幕", "角色动作", "角色内心",
        "雨夜", "街口", "街角", "办公室", "房间",
        "低头", "抬头", "伸手", "收回", "转身", "离开", "沉默", "皱眉", "低声", "轻声",
        "手机", "屏幕", "手指", "雨伞", "眼眶", "肩膀", "嘴角", "眉头", "呼吸", "雨声", "合同", "电脑"
    ];

    private static readonly EmotionRule[] EmotionRules =
    [
        new(["委屈", "眼眶", "发红", "不相信"], "委屈", 0.82, "低声但用力"),
        new(["愤怒", "吼", "喊", "怒"], "愤怒", 0.9, "急促、有爆发力"),
        new(["压抑", "沉默", "低头", "攥紧"], "压抑", 0.68, "低声、克制"),
        new(["紧张", "喘", "跑来", "迟到"], "紧张", 0.7, "急促、带歉意"),
        new(["开心", "笑", "轻松"], "轻松", 0.56, "明亮、自然"),
        new(["害怕", "恐惧", "颤抖"], "害怕", 0.78, "发颤、压低")
    ];

    private static readonly SceneRule[] SceneRules =
    [
        new(["雨", "街", "路灯", "街口", "车"], "雨夜街口", "城市街道", "夜晚", "雨", "压抑、孤独", "冷色调，霓虹反光，浅景深", ["雨伞", "路灯", "街角"]),
        new(["办公室", "会议", "工位"], "办公室", "室内办公区", "白天", "室内", "紧绷、克制", "冷白光，干净线条，中景构图", ["电脑", "桌面文件"]),
        new(["家", "房间", "客厅", "卧室"], "室内房间", "居住空间", "夜晚", "室内", "私密、安静", "暖色台灯，浅景深", ["沙发", "台灯"]),
        new(["咖啡", "餐厅", "桌"], "咖啡馆", "商业室内", "傍晚", "室内", "克制、疏离", "柔和暖光，玻璃反射", ["咖啡杯", "桌椅"])
    ];

    public ScriptDocument Preprocess(string rawText)
    {
        var cleanText = NormalizeText(rawText);
        var segments = SplitIntoSentences(cleanText)
            .Select((text, index) => new ScriptSegment(Uid("seg", index), index + 1, text, DetectSegmentType(text)))
            .ToArray();

        return new ScriptDocument(rawText, cleanText, segments);
    }

    public DraftProject Analyze(string? rawText = null)
    {
        var script = Preprocess(string.IsNullOrWhiteSpace(rawText) ? SampleScript : rawText);
        var dialogues = script.Segments.Select(segment => ExtractDialogue(segment.Text)).Where(dialogue => dialogue is not null).Cast<DialogueCandidate>().ToArray();
        var names = ExtractCharacterNames(script.CleanText, dialogues);
        var characters = names.Select((name, index) => BuildCharacter(name, index)).ToArray();
        var characterByName = characters.ToDictionary(character => character.Name, StringComparer.Ordinal);
        var sceneByName = new Dictionary<string, Scene>(StringComparer.Ordinal);

        var shots = script.Segments.Select((segment, index) =>
        {
            var dialogue = ExtractDialogue(segment.Text);
            var sceneInfo = InferScene(segment.Text);
            if (!sceneByName.TryGetValue(sceneInfo.Name, out var scene))
            {
                scene = BuildScene(sceneInfo, sceneByName.Count);
                sceneByName[sceneInfo.Name] = scene;
            }

            var candidateNames = dialogue?.Speaker is { } speaker && speaker != "旁白"
                ? [speaker]
                : names.Where(name => name != "旁白" && segment.Text.Contains(name, StringComparison.Ordinal)).ToArray();
            var resolvedNames = candidateNames.Length > 0 ? candidateNames : names.Where(name => name != "旁白").Take(1).ToArray();
            var emotion = InferEmotion(segment.Text);
            var action = InferAction(segment.Text);
            var dialogueId = dialogue is null ? null : Uid("dia", index);
            var dialogueCharacter = dialogue?.Speaker is null ? null : characterByName.GetValueOrDefault(dialogue.Speaker);
            var characterIds = resolvedNames.Select(name => characterByName.GetValueOrDefault(name)?.Id).Where(id => id is not null).Cast<string>().ToArray();
            var dialogueSpec = dialogue is null
                ? null
                : new DialogueSpec(
                    dialogueId!,
                    dialogueCharacter?.Id ?? (dialogue.Speaker == "旁白" ? "narrator" : null),
                    dialogue.Speaker,
                    dialogue.Text,
                    emotion.Emotion,
                    emotion.Tone,
                    0.4,
                    0.8,
                    new SubtitleSpec(dialogue.Text, "standard", 14));
            var prompt = BuildGenerationPrompt(scene, resolvedNames, emotion, action, dialogue?.Text);

            return new Shot(
                Uid("shot", index),
                [segment.Id],
                scene.Id,
                characterIds,
                new TimeRange(0, action.Duration),
                new VisualSpec(index % 3 == 0 ? "中近景" : index % 3 == 1 ? "近景" : "中景", action.CameraAction, "主体清晰，背景提供情绪信息", scene.VisualStyle),
                characterIds.Select(id => new PerformanceSpec(id, emotion, action, dialogueId)).ToArray(),
                dialogueSpec,
                new AudioSpec(dialogue?.Speaker, scene.Mood.Contains("压抑", StringComparison.Ordinal) ? "低沉钢琴" : "轻环境氛围", scene.Weather == "雨" ? ["雨声", "远处车声"] : ["环境底噪"]),
                dialogue?.Text,
                new ShotBinding(null, null, null, "standard"),
                prompt,
                "不要出现多余人物，不要字幕水印，不要改变角色外貌，不要肢体畸形，不要风格突变",
                BuildVoicePrompt(dialogue?.Speaker, emotion),
                "needs_binding");
        }).ToArray();

        var draft = BuildTimeline(new DraftProject(
            "proj_001",
            "未命名视频草稿",
            InferContentType(script.CleanText),
            script,
            new DraftAnalysis(
                SummarizeText(script.CleanText),
                script.CleanText.Contains("不相信", StringComparison.Ordinal) ? "信任关系破裂" : "核心冲突待确认",
                shots.Select(shot => new EmotionCurvePoint(shot.Id, shot.Performance.FirstOrDefault()?.Emotion.Emotion ?? "克制", shot.Performance.FirstOrDefault()?.Emotion.Intensity ?? 0.5)).ToArray()),
            characters,
            sceneByName.Values.ToArray(),
            shots,
            new Timeline("timeline_001", 0, []),
            []));

        return draft with { Issues = CheckIssues(draft) };
    }

    public DraftProject BuildTimeline(DraftProject draft)
    {
        var cursor = 0.0;
        var shots = draft.Shots.Select(shot =>
        {
            var next = shot with { TimeRange = shot.TimeRange with { Start = Math.Round(cursor, 1) } };
            cursor += next.TimeRange.Duration;
            return next;
        }).ToArray();

        var tracks = new[] { "video", "character", "voice", "subtitle", "sfx", "bgm" }
            .Select(type => new TimelineTrack($"{type}_track_001", type, shots.Select(shot => new TimelineItem(
                shot.Id,
                shot.TimeRange.Start,
                shot.TimeRange.Duration,
                type == "video" ? shot.Binding.SceneAsset : type == "voice" ? shot.Binding.VoiceAsset : null,
                shot.Status)).ToArray()))
            .ToArray();

        return draft with { Shots = shots, Timeline = new Timeline("timeline_001", Math.Round(cursor, 1), tracks) };
    }

    public IReadOnlyList<DraftIssue> CheckIssues(DraftProject draft)
    {
        var issues = new List<DraftIssue>();
        var charactersById = draft.Characters.ToDictionary(character => character.Id, StringComparer.Ordinal);
        var scenesById = draft.Scenes.ToDictionary(scene => scene.Id, StringComparer.Ordinal);

        foreach (var shot in draft.Shots)
        {
            if (!scenesById.ContainsKey(shot.SceneId))
            {
                issues.Add(NewIssue(issues, IssueSeverity.Error, "shot", shot.Id, "镜头缺少有效场景", "选择或创建场景，并同步生成 prompt"));
            }

            if (shot.GenerationPrompt.Length < 18)
            {
                issues.Add(NewIssue(issues, IssueSeverity.Warning, "shot", shot.Id, "镜头 prompt 信息不足", "补充主体、场景、动作、镜头和风格"));
            }

            if (shot.CharacterIds.Count == 0 && shot.Dialogue?.Speaker?.Contains("旁白", StringComparison.Ordinal) != true)
            {
                issues.Add(NewIssue(issues, IssueSeverity.Warning, "shot", shot.Id, "镜头没有绑定出场人物", "从人物库选择角色或标记为纯场景镜头"));
            }

            foreach (var characterId in shot.CharacterIds)
            {
                if (!charactersById.TryGetValue(characterId, out var character)) continue;

                if (character.AssetBinding.Portrait is null && character.Name != "旁白")
                {
                    issues.Add(NewIssue(issues, IssueSeverity.Warning, "character", characterId, $"{character.Name} 缺少参考形象绑定", "为角色绑定参考图或演员素材", shot.Id));
                }

                if (character.AssetBinding.Voice is null && shot.Dialogue?.CharacterId == characterId)
                {
                    issues.Add(NewIssue(issues, IssueSeverity.Warning, "character", characterId, $"{character.Name} 缺少音色绑定", "选择角色默认音色", shot.Id));
                }
            }

            if (scenesById.TryGetValue(shot.SceneId, out var scene) && scene.AssetBinding.BackgroundImage is null && scene.AssetBinding.VideoClip is null)
            {
                issues.Add(NewIssue(issues, IssueSeverity.Info, "scene", scene.Id, $"{scene.Name} 未绑定背景素材", "绑定背景图、视频片段或保留生成式场景", shot.Id));
            }

            if (shot.Dialogue is { CharacterId: null } dialogue && dialogue.Speaker != "旁白")
            {
                issues.Add(NewIssue(issues, IssueSeverity.Error, "dialogue", dialogue.Id, "台词缺少有效说话人", "选择说话人，并将角色加入镜头人物列表", shot.Id));
            }
        }

        return issues;
    }

    public string ExportJson(DraftProject draft)
    {
        return JsonSerializer.Serialize(draft, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    public string ExportMarkdown(DraftProject draft)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {draft.Title}");
        builder.AppendLine();
        builder.AppendLine($"- 内容类型：{draft.ContentType}");
        builder.AppendLine($"- 总时长：{draft.Timeline.Duration}s");
        builder.AppendLine($"- 摘要：{draft.Analysis.Summary}");
        builder.AppendLine();
        builder.AppendLine("## 镜头表");
        builder.AppendLine();
        builder.AppendLine("| 镜头 | 场景 | 人物 | 情绪 | 动作 | 台词/旁白 | 时长 |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");

        foreach (var shot in draft.Shots)
        {
            var scene = draft.Scenes.FirstOrDefault(item => item.Id == shot.SceneId);
            var names = string.Join("、", shot.CharacterIds.Select(id => draft.Characters.FirstOrDefault(character => character.Id == id)?.Name).Where(name => !string.IsNullOrWhiteSpace(name)));
            var emotion = shot.Performance.FirstOrDefault()?.Emotion.Emotion ?? "未识别";
            builder.AppendLine($"| {shot.Id} | {scene?.Name ?? "未设定"} | {(string.IsNullOrWhiteSpace(names) ? "无" : names)} | {emotion} | {shot.Performance.FirstOrDefault()?.Action.MainAction ?? "无"} | {shot.Dialogue?.Text ?? "无"} | {shot.TimeRange.Duration}s |");
        }

        return builder.ToString();
    }

    private static DraftIssue NewIssue(List<DraftIssue> issues, IssueSeverity severity, string targetType, string targetId, string message, string suggestion, string? sourceShotId = null)
    {
        return new DraftIssue($"issue_{issues.Count + 1}", severity, targetType, targetId, message, suggestion, sourceShotId);
    }

    private static Character BuildCharacter(string name, int index)
    {
        var isNarrator = name == "旁白";
        return new Character(
            Uid("char", index),
            name,
            isNarrator ? "旁白" : index == 0 ? "主角" : "角色",
            isNarrator ? "不出镜，仅声音出现" : "待绑定参考形象",
            isNarrator ? "沉稳旁白音" : "待选择音色",
            isNarrator ? "平稳" : "克制",
            new AssetBinding());
    }

    private static Scene BuildScene(SceneInfo sceneInfo, int index)
    {
        return new Scene(
            Uid("scene", index),
            sceneInfo.Name,
            sceneInfo.Location,
            sceneInfo.TimeOfDay,
            sceneInfo.Weather,
            sceneInfo.Mood,
            sceneInfo.VisualStyle,
            sceneInfo.Props,
            new SceneAssetBinding());
    }

    private static string NormalizeText(string? text)
    {
        var value = (text ?? string.Empty).Replace("\r\n", "\n");
        value = MultiSpaceRegex().Replace(value, " ");
        value = MultiNewLineRegex().Replace(value, "\n\n");
        value = SentenceSpaceRegex().Replace(value, "$1\n");
        return value.Trim();
    }

    private static IReadOnlyList<string> SplitIntoSentences(string text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .SelectMany(line => SentenceBoundaryRegex().Split(line).Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part.Trim()))
                .ToArray();
    }

    private static SegmentType DetectSegmentType(string text)
    {
        if (IsStructuredActionText(text)) return SegmentType.Action;
        if (IsNaturalInnerThoughtText(text)) return SegmentType.Narration;
        if (IsStructuredNarrationText(text)) return SegmentType.Narration;
        if (NarrationRegex().IsMatch(text)) return SegmentType.Narration;
        if (DialoguePrefixRegex().IsMatch(text)) return SegmentType.Dialogue;
        if (ExtractLeadingSpeaker(text) is not null && text.Contains('：')) return SegmentType.Dialogue;
        if (SpeechVerbRegex().IsMatch(text)) return SegmentType.Dialogue;
        return ActionWords.Any(text.Contains) ? SegmentType.Action : SegmentType.Narration;
    }

    private static DialogueCandidate? ExtractDialogue(string text)
    {
        if (IsStructuredActionText(text) ||
            IsStructuredSceneDescriptionText(text) ||
            IsStructuredInnerMonologueText(text) ||
            IsNaturalInnerThoughtText(text))
        {
            return null;
        }

        var narratorMatch = NarrationRegex().Match(text);
        if (narratorMatch.Success)
        {
            return new DialogueCandidate("旁白", narratorMatch.Groups[2].Value.Trim(), "narration");
        }

        var leadingSpeaker = ExtractLeadingSpeaker(text);
        var speechMatch = SpeechAfterVerbRegex().Match(text);
        if (leadingSpeaker is not null && speechMatch.Success)
        {
            return new DialogueCandidate(leadingSpeaker, CleanQuote(speechMatch.Groups[1].Value), "dialogue");
        }

        var colonMatch = ColonDialogueRegex().Match(text);
        if (leadingSpeaker is not null && colonMatch.Success)
        {
            return new DialogueCandidate(leadingSpeaker, CleanQuote(colonMatch.Groups[1].Value), "dialogue");
        }

        var prefixMatch = DialoguePrefixRegex().Match(text);
        if (prefixMatch.Success)
        {
            return new DialogueCandidate(prefixMatch.Groups[1].Value.Trim(), CleanQuote(prefixMatch.Groups[2].Value), "dialogue");
        }

        return null;
    }

    private static string? ExtractLeadingSpeaker(string text)
    {
        var match = LeadingSpeakerRegex().Match(text);
        var speaker = match.Success ? match.Groups[1].Value.Trim() : null;
        return CharacterNameGuard.NormalizeOrNull(speaker);
    }

    private static string[] ExtractCharacterNames(string text, IReadOnlyList<DialogueCandidate> dialogues)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dialogue in dialogues)
        {
            if (!string.IsNullOrWhiteSpace(dialogue.Speaker) && dialogue.Speaker != "旁白") names.Add(dialogue.Speaker);
        }

        foreach (Match match in CharacterActionRegex().Matches(text))
        {
            var name = PronounPrefixRegex().Replace(match.Groups[1].Value, string.Empty).Trim();
            if (IsLikelyCharacterName(name))
            {
                names.Add(name);
            }
        }

        foreach (var name in ExtractStructuredCharacterNames(text))
        {
            names.Add(name);
        }

        foreach (var name in ExtractNaturalInnerThoughtCharacterNames(text))
        {
            names.Add(name);
        }

        if (names.Count == 0) names.Add("主角");
        return names.ToArray();
    }

    private static bool IsLikelyCharacterName(string name)
    {
        return CharacterNameGuard.IsLikelyName(name);
    }

    private static bool IsStructuredActionText(string text)
    {
        return text.StartsWith("角色动作：", StringComparison.Ordinal) ||
            text.StartsWith("角色动作:", StringComparison.Ordinal) ||
            text.StartsWith("动作：", StringComparison.Ordinal) ||
            text.StartsWith("动作:", StringComparison.Ordinal);
    }

    private static bool IsStructuredNarrationText(string text)
    {
        return IsStructuredSceneDescriptionText(text) ||
            IsStructuredInnerMonologueText(text) ||
            text.StartsWith("旁白叙述：", StringComparison.Ordinal) ||
            text.StartsWith("旁白叙述:", StringComparison.Ordinal);
    }

    private static bool IsStructuredInnerMonologueText(string text)
    {
        return text.StartsWith("角色内心：", StringComparison.Ordinal) ||
            text.StartsWith("角色内心:", StringComparison.Ordinal);
    }

    private static bool IsStructuredSceneDescriptionText(string text)
    {
        return text.StartsWith("场景描述：", StringComparison.Ordinal) ||
            text.StartsWith("场景描述:", StringComparison.Ordinal);
    }

    private static bool IsNaturalInnerThoughtText(string text)
    {
        return NaturalInnerThoughtRegex().IsMatch(text);
    }

    private static IEnumerable<string> ExtractStructuredCharacterNames(string text)
    {
        foreach (Match match in StructuredCharacterNameRegex().Matches(text))
        {
            var name = CharacterNameGuard.NormalizeOrNull(match.Groups[1].Value);
            if (!string.IsNullOrWhiteSpace(name))
            {
                yield return name;
            }
        }

        foreach (Match match in StructuredReferencedCharacterNameRegex().Matches(text))
        {
            var name = CharacterNameGuard.NormalizeOrNull(match.Groups[1].Value);
            if (!string.IsNullOrWhiteSpace(name))
            {
                yield return name;
            }
        }
    }

    private static IEnumerable<string> ExtractNaturalInnerThoughtCharacterNames(string text)
    {
        foreach (Match match in NaturalInnerThoughtRegex().Matches(text))
        {
            var name = CharacterNameGuard.NormalizeOrNull(match.Groups[1].Value);
            if (!string.IsNullOrWhiteSpace(name))
            {
                yield return name;
            }
        }
    }

    private static string InferContentType(string text)
    {
        if (Regex.IsMatch(text, "产品|购买|优惠|品牌|用户痛点|转化")) return "advertisement";
        if (Regex.IsMatch(text, "知识|步骤|方法|为什么|如何")) return "knowledge";
        if (Regex.IsMatch(text, "旁白|说|镜头|街口|办公室|房间|眼眶|沉默")) return "short_drama";
        return "short_video";
    }

    private static SceneInfo InferScene(string text)
    {
        var rule = SceneRules.FirstOrDefault(item => item.Keywords.Any(text.Contains));
        return rule is null
            ? new SceneInfo("默认室内场景", "未明确地点", "未明确时间", "未明确", "中性", "自然光，纪实风格，中景构图", [])
            : new SceneInfo(rule.Name, rule.Location, rule.TimeOfDay, rule.Weather, rule.Mood, rule.VisualStyle, rule.Props);
    }

    private static EmotionSpec InferEmotion(string text)
    {
        var rule = EmotionRules.FirstOrDefault(item => item.Keywords.Any(text.Contains)) ?? new EmotionRule([], "克制", 0.52, "自然、平稳");
        return new EmotionSpec(
            rule.Emotion,
            rule.Intensity,
            rule.Intensity > 0.75 ? $"进入{rule.Emotion}" : $"保持{rule.Emotion}",
            rule.Emotion == "委屈" ? "眼神发红，嘴角紧绷" : rule.Emotion == "紧张" ? "眉头微皱，呼吸急促" : "表情克制，眼神集中",
            rule.Tone,
            rule.Emotion == "压抑" ? "肩膀收紧，手部用力" : "动作幅度适中");
    }

    private static ActionSpec InferAction(string text)
    {
        var microActions = ActionWords.Where(text.Contains).Take(4).ToArray();
        var mainAction = microActions.Length > 0 ? $"{microActions[0]}，{TrimSentence(text, 26)}" : "保持当前状态";
        return new ActionSpec(mainAction, microActions.Length > 0 ? microActions : ["观察环境", "保持情绪"], text.Length > 28 ? "缓慢推近到中近景" : "稳定中景", EstimateDuration(text));
    }

    private static double EstimateDuration(string text)
    {
        var dialogue = ExtractDialogue(text);
        var baseline = dialogue is not null ? dialogue.Text.Length / 5.5 + 1.2 : text.Length / 8.0 + 1.5;
        return Math.Max(3, Math.Min(8, Math.Round(baseline, 1)));
    }

    private static string BuildGenerationPrompt(Scene scene, IReadOnlyList<string> characterNames, EmotionSpec emotion, ActionSpec action, string? dialogueText)
    {
        var subject = characterNames.Count > 0 ? string.Join("、", characterNames) : "画面主体";
        var dialogueHint = string.IsNullOrWhiteSpace(dialogueText) ? null : $"台词情绪：{TrimSentence(dialogueText, 20)}";
        return string.Join("，", new[] { scene.Name, scene.Location, subject, action.MainAction, $"{emotion.Emotion}{Math.Round(emotion.Intensity * 100)}%", scene.VisualStyle, action.CameraAction, "电影感，画面信息清晰", dialogueHint }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string BuildVoicePrompt(string? characterName, EmotionSpec emotion)
    {
        return string.IsNullOrWhiteSpace(characterName) || characterName == "旁白"
            ? "旁白，沉稳清晰，语速适中，停顿自然"
            : $"{characterName}，{emotion.Tone}，情绪为{emotion.Emotion}，强度{Math.Round(emotion.Intensity * 100)}%，句尾保留自然停顿";
    }

    private static string SummarizeText(string text) => text.Length > 72 ? $"{text[..72]}..." : string.IsNullOrWhiteSpace(text) ? "暂无文案" : text;

    private static string Uid(string prefix, int index) => $"{prefix}_{index + 1:000}";

    private static string CleanQuote(string value) => value.Replace("“", string.Empty, StringComparison.Ordinal).Replace("”", string.Empty, StringComparison.Ordinal).Replace("\"", string.Empty, StringComparison.Ordinal).Trim();

    private static string TrimSentence(string value, int length)
    {
        var clean = Regex.Replace(value, "[。！？!?]$", string.Empty);
        return clean.Length <= length ? clean : clean[..length];
    }

    private sealed record EmotionRule(IReadOnlyList<string> Keywords, string Emotion, double Intensity, string Tone);

    private sealed record SceneRule(IReadOnlyList<string> Keywords, string Name, string Location, string TimeOfDay, string Weather, string Mood, string VisualStyle, IReadOnlyList<string> Props);

    private sealed record SceneInfo(string Name, string Location, string TimeOfDay, string Weather, string Mood, string VisualStyle, IReadOnlyList<string> Props);

    private sealed record DialogueCandidate(string? Speaker, string Text, string Kind);

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex MultiSpaceRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultiNewLineRegex();

    [GeneratedRegex(@"([。！？!?])\s+")]
    private static partial Regex SentenceSpaceRegex();

    [GeneratedRegex(@"(?<=[。！？!?])\s*")]
    private static partial Regex SentenceBoundaryRegex();

    [GeneratedRegex(@"^(旁白|旁白叙述|画外音|VO|Narration)\s*[：:]\s*(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex NarrationRegex();

    [GeneratedRegex(@"^([\u4e00-\u9fa5A-Za-z0-9_]{1,8})\s*[：:]\s*(.+)$")]
    private static partial Regex DialoguePrefixRegex();

    [GeneratedRegex(@"(?:说|问|喊|低声|回答|喃喃|道)[：:]")]
    private static partial Regex SpeechVerbRegex();

    [GeneratedRegex(@"(?:说|问|喊|回答|喃喃|道)\s*[：:]\s*(.+)$")]
    private static partial Regex SpeechAfterVerbRegex();

    [GeneratedRegex(@"[：:]\s*(.+)$")]
    private static partial Regex ColonDialogueRegex();

    [GeneratedRegex(@"^([\u4e00-\u9fa5A-Za-z0-9_]{2,8}?)(?=终于|立刻|突然|缓缓|低声|喘|抬头|看|盯|停|从|跑|坐|站|推门|进来|沉默|皱眉|说|问|喊|回答|喃喃|道|[:：])")]
    private static partial Regex LeadingSpeakerRegex();

    [GeneratedRegex(@"(?:(?<=^)|(?<=[。！？!?；;，,\n]))\s*([\u4e00-\u9fa5]{2,4})(?=(?:终于|立刻|突然|缓缓)?(?:站|坐|等|低头|看|盯|攥紧|跑来|喘|抬头|停|伸手|收回|转身|离开|沉默|推门|进来|开门|关门|拿|放|走|皱眉|抬手|举手|挥手|别|说|问|喊|回答|道))")]
    private static partial Regex CharacterActionRegex();

    [GeneratedRegex(@"(?:角色动作|角色内心)\s*[：:]\s*([\u4e00-\u9fa5]{2,4})(?=(?:终于|立刻|突然|缓缓)?(?:站|坐|等|低头|看|盯|攥紧|跑来|喘|抬头|停|伸手|收回|转身|离开|沉默|推门|进来|开门|关门|拿|放|走|皱眉|抬手|举手|挥手|心里|意识|想|说|问|喊|回答|道))")]
    private static partial Regex StructuredCharacterNameRegex();

    [GeneratedRegex(@"(?:示意|看向|望向|对着|拉住|扶住|挡住|提醒)([\u4e00-\u9fa5]{2,4})(?=(?:别|不要|先|站|坐|看|走|说|问|喊|回答|道|。|，|,|；|;))")]
    private static partial Regex StructuredReferencedCharacterNameRegex();

    [GeneratedRegex(@"(?:(?<=^)|(?<=[。！？!?；;，,\n]))\s*([\u4e00-\u9fa5]{2,4})(?:心想|心里|心中|暗想)(?=[：:，,。！？!?]|[^。！？!?；;，,\n]{1,8}[，,。！？!?])")]
    private static partial Regex NaturalInnerThoughtRegex();

    [GeneratedRegex(@"^(她|他|它|他们|她们)")]
    private static partial Regex PronounPrefixRegex();
}
