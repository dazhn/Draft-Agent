namespace DraftAgent.Core;

public sealed class SceneInteractionBuilder
{
    private readonly DraftAnalyzer _analyzer = new();

    public InteractionProject Build(string? rawText)
    {
        var draft = _analyzer.Analyze(rawText);
        var scenes = draft.Scenes
            .Select((scene, sceneIndex) => BuildScene(scene, sceneIndex, draft))
            .ToArray();
        var tasks = scenes.SelectMany(scene => scene.Interactions.SelectMany(interaction => interaction.Tasks)).ToArray();

        return new InteractionProject(
            draft.ProjectId,
            draft.Title,
            draft.Script,
            draft.Characters,
            scenes,
            tasks,
            BuildIssues(draft, scenes));
    }

    private static InteractionScene BuildScene(Scene scene, int sceneIndex, DraftProject draft)
    {
        var interactionDrafts = draft.Shots
            .Where(shot => shot.SceneId == scene.Id)
            .Select((shot, interactionIndex) => new InteractionDraft(BuildInteraction(shot, interactionIndex, scene.Id, draft), shot))
            .ToArray();
        var interactions = interactionDrafts
            .Select(item => item.Interaction with { Tasks = BuildTasks(item.Interaction, draft, item.Shot).ToArray() })
            .ToArray();

        return new InteractionScene(
            scene.Id,
            sceneIndex + 1,
            scene.Name,
            scene.Location,
            scene.TimeOfDay,
            scene.Weather,
            scene.Mood,
            scene.VisualStyle,
            scene.AssetBinding,
            interactions);
    }

    private static CharacterInteraction BuildInteraction(Shot shot, int interactionIndex, string sceneId, DraftProject draft)
    {
        var performance = shot.Performance.FirstOrDefault();
        var sourceText = SourceText(shot, draft);
        var isNarrationRow = shot.Dialogue is null || shot.Dialogue.Speaker == "旁白";
        var subjectId = ResolveSubjectCharacterId(shot, isNarrationRow);
        var targetId = shot.CharacterIds.FirstOrDefault(id => id != subjectId);
        var type = InferInteractionType(shot, isNarrationRow, subjectId, targetId);
        var voice = new VoiceRenderSettings(
            null,
            performance?.Emotion.Emotion ?? shot.Dialogue?.Emotion ?? "Calm",
            performance?.Emotion.Intensity ?? 0.6,
            1.0,
            shot.Dialogue?.PauseAfter ?? 0.6);
        var dialogue = !isNarrationRow && shot.Dialogue is not null && subjectId is not null
            ? new DialogueLine(shot.Dialogue.Id, subjectId, targetId, shot.Dialogue.Text, voice)
            : null;
        var narration = isNarrationRow
            ? new NarrationLine(shot.Dialogue?.Id ?? $"nar_{shot.Id}", shot.Dialogue?.Text ?? sourceText, voice)
            : null;
        var innerMonologue = type == InteractionType.InnerMonologue && subjectId is not null
            ? new InnerMonologue($"inner_{shot.Id}", subjectId, shot.Subtitle ?? string.Empty, voice)
            : null;
        var emotion = isNarrationRow
            ? new EmotionSpec(voice.Emotion, voice.EmotionIntensity, voice.Emotion, voice.Emotion, voice.Emotion, sourceText)
            : performance?.Emotion ?? new EmotionSpec("克制", 0.5, "保持克制", "表情克制", "自然、平稳", "动作幅度适中");

        return new CharacterInteraction(
            shot.Id.Replace("shot_", "int_"),
            interactionIndex + 1,
            type,
            sceneId,
            subjectId,
            targetId,
            emotion,
            performance?.Action ?? new ActionSpec(sourceText, ["观察环境"], "稳定中景", shot.TimeRange.Duration),
            dialogue,
            innerMonologue,
            narration,
            new InteractionBinding(null, null),
            [],
            shot.SourceSegmentIds.FirstOrDefault() ?? string.Empty);
    }

    private static string? ResolveSubjectCharacterId(Shot shot, bool isNarrationRow)
    {
        if (isNarrationRow)
        {
            return null;
        }

        return shot.Dialogue?.CharacterId is { } dialogueCharacterId && dialogueCharacterId != "narrator"
            ? dialogueCharacterId
            : shot.CharacterIds.FirstOrDefault();
    }

    private static InteractionType InferInteractionType(Shot shot, bool isNarrationRow, string? subjectId, string? targetId)
    {
        if (isNarrationRow) return InteractionType.Narration;
        if (shot.Dialogue is not null && subjectId is not null && targetId is not null) return InteractionType.Dialogue;
        if (shot.Dialogue is not null) return InteractionType.Dialogue;
        return InteractionType.SoloAction;
    }

    private static IEnumerable<GenerationTask> BuildTasks(CharacterInteraction interaction, DraftProject draft, Shot shot)
    {
        if (interaction.Dialogue is not null)
        {
            yield return NewTask("tts", interaction.Id, GenerationTaskType.Tts, interaction.Id, $"TTS {GetCharacterName(draft, interaction.SubjectCharacterId)} 台词", new Dictionary<string, string>
            {
                ["text"] = interaction.Dialogue.Text,
                ["emotion"] = IndexTts2Defaults.NormalizeEmotion(interaction.Dialogue.Voice.Emotion),
                ["emotionStrength"] = IndexTts2Defaults.ResolveEmotionStrength(interaction.Dialogue.Voice.Emotion, interaction.Dialogue.Voice.EmotionIntensity.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                ["voiceReferenceId"] = interaction.Dialogue.Voice.VoiceReferenceId ?? string.Empty
            });
        }

        if (interaction.Narration is not null)
        {
            yield return NewTask("tts", interaction.Id, GenerationTaskType.Tts, interaction.Id, "TTS 旁白", new Dictionary<string, string>
            {
                ["text"] = interaction.Narration.Text,
                ["emotion"] = IndexTts2Defaults.NormalizeEmotion(interaction.Narration.Voice.Emotion),
                ["emotionStrength"] = IndexTts2Defaults.ResolveEmotionStrength(interaction.Narration.Voice.Emotion, interaction.Narration.Voice.EmotionIntensity.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                ["voiceReferenceId"] = interaction.Narration.Voice.VoiceReferenceId ?? string.Empty
            });
        }

    }

    private static IReadOnlyList<DraftIssue> BuildIssues(DraftProject draft, IReadOnlyList<InteractionScene> scenes)
    {
        var issues = draft.Issues.ToList();
        foreach (var scene in scenes.Where(scene => scene.AssetBinding.BackgroundImage is null && scene.AssetBinding.VideoClip is null))
        {
            issues.Add(new DraftIssue($"issue_{issues.Count + 1}", IssueSeverity.Info, "scene", scene.Id, $"{scene.Name} 未绑定场景素材", "选择现有图片素材"));
        }

        return issues;
    }

    private static GenerationTask NewTask(string prefix, string id, GenerationTaskType type, string targetId, string displayName, IReadOnlyDictionary<string, string> parameters)
    {
        return new GenerationTask($"{prefix}_{id}", type, targetId, displayName, "comfyui", GenerationTaskStatus.Draft, parameters);
    }

    private static string GetCharacterName(DraftProject draft, string? characterId)
    {
        return draft.Characters.FirstOrDefault(character => character.Id == characterId)?.Name ?? "角色";
    }

    private static string SourceText(Shot shot, DraftProject draft)
    {
        var sourceSegmentId = shot.SourceSegmentIds.FirstOrDefault();
        return draft.Script.Segments.FirstOrDefault(segment => segment.Id == sourceSegmentId)?.Text
            ?? shot.Subtitle
            ?? shot.Dialogue?.Text
            ?? string.Empty;
    }

    private sealed record InteractionDraft(CharacterInteraction Interaction, Shot Shot);
}
