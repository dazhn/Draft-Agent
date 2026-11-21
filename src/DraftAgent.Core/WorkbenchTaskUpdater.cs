namespace DraftAgent.Core;

public sealed class WorkbenchTaskUpdater
{
    private readonly IWorkbenchTaskPreservationPolicy _taskPreservationPolicy;

    public WorkbenchTaskUpdater(IWorkbenchTaskPreservationPolicy? taskPreservationPolicy = null)
    {
        _taskPreservationPolicy = taskPreservationPolicy ?? new WorkbenchTaskPreservationPolicy();
    }

    public IReadOnlyList<GenerationTask> BuildTasks(
        CharacterInteraction interaction,
        CharacterInteraction? existingInteraction,
        WorkbenchTaskLinks taskLinks,
        IReadOnlyDictionary<string, GenerationTask> existingTasksById,
        VoiceAssetBinding? voiceBinding = null)
    {
        var tasks = new List<GenerationTask>
        {
            BuildTtsTask(interaction, existingInteraction, taskLinks.TtsTaskId, existingTasksById, voiceBinding)
        };

        return tasks.OrderBy(task => task.Id, StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<GenerationTask> MergeProjectTasks(
        InteractionProject project,
        IReadOnlyList<GenerationTask> rowGenerationTasks)
    {
        var previousRowTaskIds = project.Scenes
            .SelectMany(scene => scene.Interactions)
            .SelectMany(interaction => interaction.Tasks)
            .Select(task => task.Id)
            .ToHashSet(StringComparer.Ordinal);
        var rowTaskIds = rowGenerationTasks
            .Select(task => task.Id)
            .ToHashSet(StringComparer.Ordinal);
        var rootOnlyTasks = project.GenerationTasks
            .Where(task => !previousRowTaskIds.Contains(task.Id) && !rowTaskIds.Contains(task.Id));

        return rootOnlyTasks
            .Concat(rowGenerationTasks)
            .OrderBy(task => task.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private GenerationTask BuildTtsTask(
        CharacterInteraction interaction,
        CharacterInteraction? existingInteraction,
        string? linkedTaskId,
        IReadOnlyDictionary<string, GenerationTask> existingTasksById,
        VoiceAssetBinding? voiceBinding)
    {
        var id = ResolveTaskId(linkedTaskId, "tts", interaction.Id);
        var voice = InteractionVoice(interaction);
        var text = interaction.Dialogue?.Text ?? interaction.Narration?.Text ?? interaction.InnerMonologue?.Text ?? string.Empty;
        var emotion = voice?.Emotion ?? interaction.Emotion.Emotion;
        var voiceReferenceId = interaction.Narration is not null
            ? interaction.Binding.NarratorVoiceReferenceId
            : interaction.Binding.SubjectVoiceReferenceId;
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["text"] = text,
            ["emotion"] = string.IsNullOrWhiteSpace(emotion) ? string.Empty : IndexTts2Defaults.NormalizeEmotion(emotion),
            ["emotionStrength"] = IndexTts2Defaults.ResolveEmotionStrength(emotion, voice?.EmotionIntensity.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
            ["voiceReferenceId"] = voiceReferenceId ?? string.Empty
        };
        var referenceText = voice?.ReferenceText;
        if (!string.IsNullOrWhiteSpace(referenceText))
        {
            parameters["referenceText"] = referenceText;
        }
        if (!string.IsNullOrWhiteSpace(voiceBinding?.SourceAssetId))
        {
            parameters["voiceSourceAssetId"] = voiceBinding.SourceAssetId.Trim();
        }
        if (!string.IsNullOrWhiteSpace(voiceBinding?.SyncedVoicePath))
        {
            parameters["voiceSyncedPath"] = voiceBinding.SyncedVoicePath.Trim();
        }

        var hasEmotionVectors = voice?.EmotionVectors is { Count: > 0 };
        if (interaction.Dialogue is not null || hasEmotionVectors)
        {
            parameters["emotionAlpha"] = voice?.EmotionAlpha.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? "1";
            foreach (var pair in NormalizeEmotionVectors(hasEmotionVectors ? voice?.EmotionVectors : null))
            {
                parameters[$"emotionVector.{pair.Key}"] = pair.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        var existingTask = ExistingTask(id, GenerationTaskType.Tts, interaction, existingInteraction, existingTasksById);
        if (_taskPreservationPolicy.PreserveTtsTask(existingTask, parameters) is { } preservedTask)
        {
            return preservedTask;
        }

        return new GenerationTask(
            id,
            GenerationTaskType.Tts,
            interaction.Id,
            interaction.Narration is not null ? "TTS 旁白" : $"TTS {interaction.SubjectCharacterId ?? "角色"} 台词",
            "comfyui",
            GenerationTaskStatus.Draft,
            parameters);
    }

    private static VoiceRenderSettings? InteractionVoice(CharacterInteraction interaction)
    {
        return interaction.Narration?.Voice ??
            interaction.Dialogue?.Voice ??
            interaction.InnerMonologue?.Voice;
    }

    private static GenerationTask? ExistingTask(
        string taskId,
        GenerationTaskType type,
        CharacterInteraction interaction,
        CharacterInteraction? existingInteraction,
        IReadOnlyDictionary<string, GenerationTask> existingTasksById)
    {
        if (existingTasksById.TryGetValue(taskId, out var linkedTask) &&
            linkedTask.Type == type &&
            string.Equals(linkedTask.TargetId, interaction.Id, StringComparison.Ordinal))
        {
            return linkedTask;
        }

        return existingInteraction?.Tasks.FirstOrDefault(task => task.Id == taskId && task.Type == type);
    }

    private static string ResolveTaskId(string? linkedTaskId, string prefix, string interactionId)
    {
        return string.IsNullOrWhiteSpace(linkedTaskId)
            ? $"{prefix}_{interactionId}"
            : linkedTaskId.Trim();
    }

    private static IReadOnlyDictionary<string, double> NormalizeEmotionVectors(IReadOnlyDictionary<string, double>? emotionVectors)
    {
        var normalized = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var key in IndexTts2Defaults.EmotionKeys)
        {
            var value = emotionVectors is not null && emotionVectors.TryGetValue(key, out var configured)
                ? configured
                : 0;
            normalized[key] = Math.Clamp(value, 0, IndexTts2Defaults.MaxEmotionStrength);
        }

        return normalized;
    }
}
