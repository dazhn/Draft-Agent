using System.Text.Json;

namespace DraftAgent.Core;

public static class WorkflowSteps
{
    public const string ScriptInput = "script_input";
    public const string PromptBuild = "prompt_build";
    public const string LlmAnalysis = "llm_analysis";
    public const string JsonNormalize = "json_normalize";
    public const string ProjectStore = "project_store";
    public const string ComfyConnection = "comfy_connection";
    public const string ComfyStatus = "comfy_status";
    public const string TtsQueue = "tts_queue";
    public const string WorkbenchEdit = "workbench_edit";
    public const string ProjectManagement = "project_management";
    public const string ProjectAsset = "project_asset";
    public const string TimelineExport = "timeline_export";
    public const string Export = "export";
}

public sealed class ScriptInputService
{
    private readonly DraftAnalyzer _analyzer;
    private readonly IWorkflowLogger _logger;

    public ScriptInputService(DraftAnalyzer analyzer, IWorkflowLogger logger)
    {
        _analyzer = analyzer;
        _logger = logger;
    }

    public ScriptDocument Prepare(string? rawText)
    {
        _logger.Info(
            WorkflowSteps.ScriptInput,
            "prepare_started",
            "开始清洗并切分用户文案。",
            new Dictionary<string, string>
            {
                ["rawLength"] = (rawText ?? string.Empty).Length.ToString()
            });

        var script = _analyzer.Preprocess(rawText ?? string.Empty);
        if (string.IsNullOrWhiteSpace(script.CleanText))
        {
            _logger.Warning(
                WorkflowSteps.ScriptInput,
                "empty_script",
                "文案为空，后续会使用本地样例或由调用方阻止分析。",
                new Dictionary<string, string>
                {
                    ["segmentCount"] = script.Segments.Count.ToString()
                });
        }

        _logger.Info(
            WorkflowSteps.ScriptInput,
            "prepare_completed",
            "文案清洗完成。",
            new Dictionary<string, string>
            {
                ["cleanLength"] = script.CleanText.Length.ToString(),
                ["segmentCount"] = script.Segments.Count.ToString()
            });

        return script;
    }
}

public sealed class LlmPromptService
{
    private readonly IWorkflowLogger _logger;

    public LlmPromptService(IWorkflowLogger logger)
    {
        _logger = logger;
    }

    public LlmAnalysisRequest CreateRequest(ScriptDocument script)
    {
        _logger.Info(
            WorkflowSteps.PromptBuild,
            "request_started",
            "开始构建 LLM 分析请求。",
            new Dictionary<string, string>
            {
                ["schema"] = DraftWorkspaceService.SceneInteractionSchemaName,
                ["segmentCount"] = script.Segments.Count.ToString()
            });

        var instructions = BuildInstructions();
        var request = new LlmAnalysisRequest(script.CleanText, DraftWorkspaceService.SceneInteractionSchemaName, instructions);

        _logger.Info(
            WorkflowSteps.PromptBuild,
            "request_completed",
            "LLM 分析请求构建完成。",
            new Dictionary<string, string>
            {
                ["schema"] = request.JsonSchemaName,
                ["promptLength"] = request.Instructions.Length.ToString(),
                ["textLength"] = request.RawText.Length.ToString()
            });

        return request;
    }

    public string BuildInstructions()
    {
        _logger.Info(
            WorkflowSteps.PromptBuild,
            "instructions_built",
            "生成 scene_interaction_flow_v1 提示词规则。",
            new Dictionary<string, string>
            {
                ["schema"] = DraftWorkspaceService.SceneInteractionSchemaName
            });

        return DefaultInstructions;
    }

    public static string BuildNarrationOptimizationInstructions()
    {
        return NarrationOptimizationInstructions;
    }

    private const string DefaultInstructions = """
你是中文文案结构化分析器。把用户文案按原文顺序拆成 JSON，只输出 JSON。

参考 JSON：
{
  "scenes": [
    {
      "description": "雨夜街口，雨势加重，气氛压抑。",
      "items": [
        {
          "type": "effects",
          "role": "",
          "text": "雨声突然变大。",
          "emotion": "Calm",
          "intensity": 0.6
        },
        {
          "type": "action",
          "role": "林安",
          "text": "林安攥紧手机。",
          "emotion": "Angry",
          "intensity": 0.7
        },
        {
          "type": "thought",
          "role": "林安",
          "text": "她终于明白自己被利用了。",
          "emotion": "Sad",
          "intensity": 0.75
        },
        {
          "type": "dialogue",
          "role": "林安",
          "text": "你为什么骗我？",
          "emotion": "Angry",
          "intensity": 0.8
        },
        {
          "type": "narration",
          "role": "",
          "text": "三年前的秘密被重新揭开。",
          "emotion": "Melancholic",
          "intensity": 0.65
        }
      ]
    }
  ]
}

字段说明：
- description：当前 scene 的主要地点、环境和氛围。
- items：当前场景内按原文顺序排列的事件。
- type：dialogue、action、thought、effects、narration。
- role：dialogue、action、thought 写角色名；effects、narration 留空。
- text：当前事件的内容。
- emotion：Happy、Angry、Sad、Surprised、Afraid、Disgusted、Calm、Melancholic。
- intensity：0 到 1 的小数。

类型说明：
- dialogue：角色说出口的话。
- action：角色动作、表情或反应。
- thought：角色内心想法。
- effects：同一地点内的天气、灯光、声音、气氛变化。
- narration：旁白或剧情推进。

场景切换规则：
- 原文主要地点变化时，新建一个 scene。
- 原文明确跳到另一段时间时，新建一个 scene。
- 原文切到回忆、梦境或想象时，新建一个 scene。

拆分规则：
- 一条 item 只写一个事件。
- 动作和台词在同一句时拆成两条。
- 内心和动作在同一句时拆成两条。
- effects 和人物反应在同一句时拆成两条。
- 情绪不明确时使用 Calm 和 0.6。
""";

    private const string NarrationOptimizationInstructions = """
请改写下面的中文文案。

最高优先级：
- 保留原文核心剧情。
- 保留原文人物关系。
- 保留原文事件顺序。
- 保留原文核心设定。
- 保留原文人物动机。
- 保留原文结局。
- 主要人物只使用原文已有人物。

开头约束：
- 第 1 句写最大矛盾、危险、身份落差或反常处境。
- 前 3 句内写清主角是谁。
- 前 3 句内写清主要人物关系。
- 前 3 句内写清当前最大麻烦。

句子约束：
- 每句控制在 8 到 24 个中文字符。
- 一句只写一种内容。
- 动作单独成句。
- 心理单独成句。
- 场景状态单独成句。
- 台词单独成句。
- 人物第一次出场写清姓名或身份。
- 同一人物连续动作或心理时，第一句写姓名。
- 同一人物连续动作或心理时，后续连续句可以省略姓名。
- 中间切换人物、插入台词或插入场景状态后，再写动作或心理时重新写姓名。

节奏约束：
- 每 3 到 6 句安排一次转折、误会、危险、秘密、人物反应或情绪落点。
- 关键转折前使用 1 个 8 到 14 个中文字符的短句。
- 每段包含 3 到 6 句。
- 每段最后一句留下下一步期待。
- 情绪推进按压住、激化、爆点、反转、回落排列。

台词约束：
- 台词使用日常口语。
- 台词展示人物冲突。
- 台词保留原文人物立场。
- 台词长度控制在 6 到 24 个中文字符。

输出约束：
- 只输出改写后的正文。
- 输出使用中文。
- 输出不包含解释。
- 输出不包含标题。
- 输出不包含编号列表。
""";
}

public sealed class LlmAnalysisStep{
    private readonly ILlmAnalysisClient _client;
    private readonly IWorkflowLogger _logger;

    public LlmAnalysisStep(ILlmAnalysisClient client, IWorkflowLogger logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<LlmAnalysisResponse> AnalyzeAsync(LlmAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        _logger.Info(
            WorkflowSteps.LlmAnalysis,
            "call_started",
            "开始调用 LLM 分析接口。",
            new Dictionary<string, string>
            {
                ["schema"] = request.JsonSchemaName,
                ["textLength"] = request.RawText.Length.ToString()
            });

        try
        {
            var response = await _client.AnalyzeAsync(request, cancellationToken);
            _logger.Info(
                WorkflowSteps.LlmAnalysis,
                "call_completed",
                "LLM 分析接口返回完成。",
                MergeReplyData(response.ReplyMetadata, new Dictionary<string, string>
                {
                    ["rawJsonLength"] = response.RawJson.Length.ToString(),
                    ["sceneCount"] = response.Project.Scenes.Count.ToString()
                }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.Error(
                WorkflowSteps.LlmAnalysis,
                "call_failed",
                "LLM 分析接口调用失败。",
                MergeReplyData(FindAiReplyMetadata(ex), new Dictionary<string, string>
                {
                    ["error"] = ex.Message
                }));
            throw;
        }
    }

    private static IReadOnlyDictionary<string, string> MergeReplyData(AiModelReplyMetadata? metadata, Dictionary<string, string> data)
    {
        if (metadata is null)
        {
            return data;
        }

        data["provider"] = metadata.Provider;
        data["model"] = metadata.ModelName;
        data["replyState"] = metadata.State.ToString();
        data["finishReason"] = metadata.FinishReason ?? string.Empty;
        data["replyMessage"] = metadata.Message;
        data["contentLength"] = metadata.ContentLength.ToString();
        data["reasoningLength"] = metadata.ReasoningLength.ToString();
        return data;
    }

    private static AiModelReplyMetadata? FindAiReplyMetadata(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is AiReplyException aiReplyException)
            {
                return aiReplyException.Metadata;
            }
        }

        return null;
    }
}

public sealed class InteractionProjectNormalizer
{
    private readonly IWorkflowLogger _logger;

    public InteractionProjectNormalizer(IWorkflowLogger logger)
    {
        _logger = logger;
    }

    public LlmAnalysisResponse Normalize(LlmAnalysisResponse response)
    {
        _logger.Info(
            WorkflowSteps.JsonNormalize,
            "normalize_started",
            "开始校验并归一化 LLM JSON。",
            new Dictionary<string, string>
            {
                ["rawJsonLength"] = response.RawJson.Length.ToString()
            });

        if (string.IsNullOrWhiteSpace(response.RawJson))
        {
            _logger.Error(WorkflowSteps.JsonNormalize, "empty_json", "LLM 返回 JSON 为空。");
            throw new InvalidOperationException("LLM 返回 JSON 为空。");
        }

        try
        {
            using var _ = JsonDocument.Parse(response.RawJson);
        }
        catch (JsonException ex)
        {
            _logger.Error(
                WorkflowSteps.JsonNormalize,
                "invalid_json",
                "LLM 返回内容不是合法 JSON。",
                new Dictionary<string, string>
                {
                    ["error"] = ex.Message
                });
            throw;
        }

        InteractionProject parsedProject;
        try
        {
            parsedProject = JsonSerializer.Deserialize<InteractionProject>(response.RawJson, DraftJson.Options)
                ?? throw new InvalidOperationException("LLM JSON 无法解析为 InteractionProject。");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            _logger.Error(
                WorkflowSteps.JsonNormalize,
                "project_parse_failed",
                "LLM JSON 无法转换为场景交互项目。",
                new Dictionary<string, string>
                {
                    ["error"] = ex.Message
                });
            throw;
        }

        var normalizedProject = NormalizeProject(parsedProject);
        var normalizedRawJson = JsonSerializer.Serialize(normalizedProject, DraftJson.Options);

        _logger.Info(
            WorkflowSteps.JsonNormalize,
            "normalize_completed",
            "LLM JSON 归一化完成。",
            new Dictionary<string, string>
            {
                ["sceneCount"] = normalizedProject.Scenes.Count.ToString(),
                ["interactionCount"] = normalizedProject.Scenes.Sum(scene => scene.Interactions.Count).ToString(),
                ["taskCount"] = normalizedProject.GenerationTasks.Count.ToString()
            });

        return new LlmAnalysisResponse(normalizedRawJson, normalizedProject, response.ModelRawJson ?? response.RawJson, response.ReplyMetadata);
    }

    private InteractionProject NormalizeProject(InteractionProject project)
    {
        var characters = NormalizeCharacters(project.Characters);
        var validCharacterIds = characters
            .Select(character => character.Id)
            .ToHashSet(StringComparer.Ordinal);
        var usedSceneIds = new HashSet<string>(StringComparer.Ordinal);
        var usedInteractionIds = new HashSet<string>(StringComparer.Ordinal);
        var scenes = project.Scenes
            .Select((scene, sceneIndex) => NormalizeScene(scene, sceneIndex, usedSceneIds, usedInteractionIds, validCharacterIds))
            .ToArray();
        var generatedTasks = scenes.SelectMany(scene => scene.Interactions.SelectMany(interaction => interaction.Tasks)).ToArray();
        var issues = NormalizeIssues(project.Issues, scenes, characters);

        return project with
        {
            Characters = characters,
            Scenes = scenes,
            GenerationTasks = generatedTasks.OrderBy(task => task.Id, StringComparer.Ordinal).ToArray(),
            Issues = issues
        };
    }

    private static IReadOnlyList<Character> NormalizeCharacters(IReadOnlyList<Character> characters)
    {
        var normalized = new List<Character>(characters.Count);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var character in characters)
        {
            var name = CharacterNameGuard.NormalizeOrNull(character.Name);
            if (name is null ||
                string.Equals(name, "旁白", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(character.Id) ||
                !seenIds.Add(character.Id) ||
                !seenNames.Add(name))
            {
                continue;
            }

            normalized.Add(character with { Name = name, AssetBinding = new AssetBinding() });
        }

        return normalized;
    }

    private static InteractionScene NormalizeScene(
        InteractionScene scene,
        int sceneIndex,
        HashSet<string> usedSceneIds,
        HashSet<string> usedInteractionIds,
        IReadOnlySet<string> validCharacterIds)
    {
        var sceneId = ResolveUniqueId(scene.Id, "scene_manual", usedSceneIds);
        var interactions = scene.Interactions
            .Select((interaction, interactionIndex) => NormalizeInteraction(interaction, sceneId, interactionIndex, usedInteractionIds, validCharacterIds))
            .ToArray();

        return scene with
        {
            Id = sceneId,
            Order = scene.Order > 0 ? scene.Order : sceneIndex + 1,
            AssetBinding = new SceneAssetBinding(),
            Interactions = interactions
        };
    }

    private static CharacterInteraction NormalizeInteraction(
        CharacterInteraction interaction,
        string sceneId,
        int interactionIndex,
        HashSet<string> usedInteractionIds,
        IReadOnlySet<string> validCharacterIds)
    {
        var interactionId = ResolveUniqueId(interaction.Id, "int_manual", usedInteractionIds);
        var hasValidSubject = !string.IsNullOrWhiteSpace(interaction.SubjectCharacterId) &&
            validCharacterIds.Contains(interaction.SubjectCharacterId);
        var requiresValidSubject = interaction.Type is InteractionType.Dialogue
            or InteractionType.SoloAction
            or InteractionType.CharacterEnter
            or InteractionType.Reaction
            or InteractionType.InnerMonologue
            or InteractionType.EmotionShift;
        var isNarration = interaction.Narration is not null ||
            interaction.Type == InteractionType.Narration ||
            (requiresValidSubject && !hasValidSubject) ||
            (interaction.Dialogue is not null && !hasValidSubject);
        var normalizedEmotion = isNarration
            ? NormalizeNarrationEmotion(interaction)
            : interaction.Emotion;
        var narration = ResolveNarration(interaction, isNarration) is not { } narrationLine
            ? null
            : narrationLine with
            {
                Voice = NormalizeNarrationVoice(narrationLine.Voice, normalizedEmotion)
            };
        var dialogue = isNarration || interaction.Dialogue is null
            ? null
            : interaction.Dialogue with
            {
                Voice = NormalizeDialogueVoice(interaction.Dialogue.Voice, normalizedEmotion)
            };
        var innerMonologue = isNarration || interaction.Type != InteractionType.InnerMonologue || interaction.InnerMonologue is null
            ? null
            : interaction.InnerMonologue with
            {
                Voice = NormalizeDialogueVoice(interaction.InnerMonologue.Voice, normalizedEmotion)
            };
        var normalized = interaction with
        {
            Id = interactionId,
            Order = interaction.Order > 0 ? interaction.Order : interactionIndex + 1,
            Type = isNarration ? InteractionType.Narration : interaction.Type,
            SceneId = sceneId,
            SubjectCharacterId = isNarration || interaction.Type == InteractionType.SceneEstablish ? null : interaction.SubjectCharacterId,
            TargetCharacterId = isNarration || string.IsNullOrWhiteSpace(interaction.TargetCharacterId) || !validCharacterIds.Contains(interaction.TargetCharacterId)
                ? null
                : interaction.TargetCharacterId,
            Emotion = normalizedEmotion,
            Action = interaction.Action with { CameraAction = "no camera language" },
            Dialogue = dialogue,
            InnerMonologue = innerMonologue,
            Narration = narration
        };

        return normalized with
        {
            Binding = new InteractionBinding(null, null),
            Tasks = EnsureInteractionTasks(normalized)
        };
    }

    private static NarrationLine? ResolveNarration(CharacterInteraction interaction, bool isNarration)
    {
        if (!isNarration)
        {
            return null;
        }

        if (interaction.Narration is not null)
        {
            return interaction.Narration;
        }

        if (interaction.Dialogue is not null)
        {
            return new NarrationLine($"nar_{interaction.Id}", interaction.Dialogue.Text, interaction.Dialogue.Voice);
        }

        if (interaction.InnerMonologue is not null)
        {
            return new NarrationLine($"nar_{interaction.Id}", interaction.InnerMonologue.Text, interaction.InnerMonologue.Voice);
        }

        return new NarrationLine($"nar_{interaction.Id}", interaction.Action.MainAction, new VoiceRenderSettings(null, interaction.Emotion.Emotion, interaction.Emotion.Intensity, 1, 0.6));
    }

    private static EmotionSpec NormalizeNarrationEmotion(CharacterInteraction interaction)
    {
        var emotion = !string.IsNullOrWhiteSpace(interaction.Narration?.Voice.Emotion)
            ? interaction.Narration.Voice.Emotion
            : interaction.Emotion.Emotion;
        var intensity = interaction.Narration?.Voice.EmotionIntensity > 0
            ? interaction.Narration.Voice.EmotionIntensity
            : interaction.Emotion.Intensity;

        if (string.IsNullOrWhiteSpace(emotion))
        {
            emotion = "Calm";
        }

        if (intensity <= 0)
        {
            intensity = 0.6;
        }

        intensity = Math.Clamp(intensity, 0, IndexTts2Defaults.MaxEmotionStrength);
        return new EmotionSpec(emotion, intensity, emotion, emotion, emotion, interaction.Action.MainAction);
    }

    private static VoiceRenderSettings NormalizeNarrationVoice(VoiceRenderSettings voice, EmotionSpec emotion)
    {
        return string.IsNullOrWhiteSpace(voice.Emotion) || voice.EmotionIntensity <= 0
            ? voice with { Emotion = emotion.Emotion, EmotionIntensity = emotion.Intensity }
            : voice with { EmotionIntensity = Math.Clamp(voice.EmotionIntensity, 0, IndexTts2Defaults.MaxEmotionStrength) };
    }

    private static VoiceRenderSettings NormalizeDialogueVoice(VoiceRenderSettings voice, EmotionSpec emotion)
    {
        return string.IsNullOrWhiteSpace(voice.Emotion)
            ? voice with { Emotion = emotion.Emotion, EmotionIntensity = emotion.Intensity }
            : voice;
    }

    private static IReadOnlyList<GenerationTask> EnsureInteractionTasks(CharacterInteraction interaction)
    {
        var tasks = new Dictionary<string, GenerationTask>(StringComparer.Ordinal);
        if (interaction.Dialogue is not null)
        {
            var task = NewTtsTask(
                $"tts_{interaction.Id}",
                interaction.Id,
                $"TTS {interaction.SubjectCharacterId ?? "角色"} 台词",
                interaction.Dialogue.Text,
                interaction.Dialogue.Voice.Emotion,
                interaction.Dialogue.Voice.VoiceReferenceId);
            tasks.TryAdd(task.Id, task);
        }

        if (interaction.Narration is not null)
        {
            var task = NewTtsTask(
                $"tts_{interaction.Id}",
                interaction.Id,
                "TTS 旁白",
                interaction.Narration.Text,
                interaction.Narration.Voice.Emotion,
                interaction.Narration.Voice.VoiceReferenceId);
            tasks.TryAdd(task.Id, task);
        }

        if (interaction.InnerMonologue is not null)
        {
            var task = NewTtsTask(
                $"tts_{interaction.Id}",
                interaction.Id,
                $"TTS {interaction.SubjectCharacterId ?? "角色"} 内心",
                interaction.InnerMonologue.Text,
                interaction.InnerMonologue.Voice.Emotion,
                interaction.InnerMonologue.Voice.VoiceReferenceId);
            tasks.TryAdd(task.Id, task);
        }

        return tasks.Values.OrderBy(task => task.Id, StringComparer.Ordinal).ToArray();
    }

    private static GenerationTask NewTtsTask(string id, string targetId, string displayName, string text, string emotion, string? voiceReferenceId)
    {
        var normalizedEmotion = IndexTts2Defaults.NormalizeEmotion(emotion);
        return new GenerationTask(
            id,
            GenerationTaskType.Tts,
            targetId,
            displayName,
            "comfyui",
            GenerationTaskStatus.Draft,
            new Dictionary<string, string>
            {
                ["text"] = text,
                ["emotion"] = string.IsNullOrWhiteSpace(emotion) ? string.Empty : normalizedEmotion,
                ["emotionStrength"] = IndexTts2Defaults.ResolveEmotionStrength(emotion, null).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                ["voiceReferenceId"] = voiceReferenceId ?? string.Empty
            });
    }

    private static string ResolveUniqueId(string? requestedId, string prefix, HashSet<string> usedIds)
    {
        if (!string.IsNullOrWhiteSpace(requestedId) && usedIds.Add(requestedId.Trim()))
        {
            return requestedId.Trim();
        }

        for (var index = 1; index < 100_000; index++)
        {
            var id = $"{prefix}_{index:000}";
            if (usedIds.Add(id))
            {
                return id;
            }
        }

        var fallback = $"{prefix}_{Guid.NewGuid():N}";
        usedIds.Add(fallback);
        return fallback;
    }

    private static IReadOnlyList<DraftIssue> NormalizeIssues(IReadOnlyList<DraftIssue> existingIssues, IReadOnlyList<InteractionScene> scenes, IReadOnlyList<Character> characters)
    {
        var issues = existingIssues.ToList();
        var existingKeys = new HashSet<string>(issues.Select(issue => $"{issue.TargetType}:{issue.TargetId}:{issue.Message}"), StringComparer.Ordinal);

        foreach (var scene in scenes.Where(scene => scene.AssetBinding.BackgroundImage is null && scene.AssetBinding.VideoClip is null))
        {
            AddIssueIfMissing(issues, existingKeys, IssueSeverity.Info, "scene", scene.Id, $"{scene.Name} 未绑定场景素材", "选择现有图片素材");
        }

        foreach (var character in characters.Where(character => character.Name != "旁白"))
        {
            if (character.AssetBinding.Portrait is null)
            {
                AddIssueIfMissing(issues, existingKeys, IssueSeverity.Warning, "character", character.Id, $"{character.Name} 缺少参考形象绑定", "为角色绑定参考图或演员素材");
            }

            if (character.AssetBinding.Voice is null)
            {
                AddIssueIfMissing(issues, existingKeys, IssueSeverity.Warning, "character", character.Id, $"{character.Name} 缺少音色绑定", "选择角色默认音色");
            }
        }

        return issues.ToArray();
    }

    private static void AddIssueIfMissing(List<DraftIssue> issues, HashSet<string> existingKeys, IssueSeverity severity, string scope, string targetId, string message, string suggestion)
    {
        var key = $"{scope}:{targetId}:{message}";
        if (!existingKeys.Add(key))
        {
            return;
        }

        issues.Add(new DraftIssue($"issue_{issues.Count + 1}", severity, scope, targetId, message, suggestion));
    }
}
