namespace DraftAgent.Core;

public static class WorkbenchDraftBuilder
{
    private const string NarrationName = "旁白";

    public static WorkbenchDocument FromEdits(
        IReadOnlyList<WorkbenchSceneEdit> scenes,
        IReadOnlyList<WorkbenchCharacterBindingEdit>? characterBindings = null)
    {
        return WorkbenchDocument.FromEdits(scenes, characterBindings);
    }

    public static WorkbenchDocument FromProject(InteractionProject project)
    {
        var characterNamesById = BuildCharacterNameIndex(project.Characters);
        var rootTtsTasksByTargetId = BuildRootTtsTaskTargetIndex(project.GenerationTasks);
        var orderedScenes = SortByOrder(project.Scenes);
        var scenes = new WorkbenchSceneDraft[orderedScenes.Count];
        for (var index = 0; index < orderedScenes.Count; index++)
        {
            scenes[index] = BuildSceneDraft(orderedScenes[index], characterNamesById, rootTtsTasksByTargetId);
        }

        var characterBindings = BuildCharacterBindings(project.Characters);

        return new WorkbenchDocument(scenes, characterBindings);
    }

    private static WorkbenchSceneDraft BuildSceneDraft(
        InteractionScene scene,
        IReadOnlyDictionary<string, string> characterNamesById,
        IReadOnlyDictionary<string, GenerationTask> rootTtsTasksByTargetId)
    {
        var orderedInteractions = SortByOrder(scene.Interactions);
        var rows = new WorkbenchRowDraft[orderedInteractions.Count];
        for (var index = 0; index < orderedInteractions.Count; index++)
        {
            rows[index] = BuildRowDraft(orderedInteractions[index], characterNamesById, rootTtsTasksByTargetId);
        }

        var visualBinding = BuildVisualBinding(scene.AssetBinding.BackgroundVisualBinding);

        return new WorkbenchSceneDraft(
            scene.Id,
            scene.Order,
            scene.Name,
            scene.Description,
            rows,
            visualBinding);
    }

    private static WorkbenchRowDraft BuildRowDraft(
        CharacterInteraction interaction,
        IReadOnlyDictionary<string, string> characterNamesById,
        IReadOnlyDictionary<string, GenerationTask> rootTtsTasksByTargetId)
    {
        var isNarration = interaction.Narration is not null;
        ResolveInteractionTasks(interaction, rootTtsTasksByTargetId, out var ttsTask);
        var emotionVectors = ReadEmotionVectors(ttsTask, interaction.Emotion.Emotion, interaction.Emotion.Intensity);

        var visualBinding = BuildVisualBinding(interaction.Binding.SubjectCharacterVisualBinding);
        var voiceReferenceId = isNarration ? interaction.Binding.NarratorVoiceReferenceId : interaction.Binding.SubjectVoiceReferenceId;
        var referenceText = interaction.Narration?.Voice.ReferenceText ??
            interaction.Dialogue?.Voice.ReferenceText ??
            interaction.InnerMonologue?.Voice.ReferenceText;
        var voiceBinding = BuildVoiceBinding(ttsTask, voiceReferenceId, referenceText);

        return new WorkbenchRowDraft(
            interaction.Id,
            interaction.SceneId,
            interaction.Order,
            isNarration ? WorkbenchRowKind.Narration : WorkbenchRowKind.Dialogue,
            SubjectName(interaction, characterNamesById, isNarration),
            isNarration ? null : interaction.SubjectCharacterId,
            BuildEffectiveEmotion(isNarration, emotionVectors, interaction.Emotion),
            interaction.Dialogue?.Text ?? interaction.Narration?.Text ?? interaction.InnerMonologue?.Text ?? interaction.Action.MainAction,
            emotionVectors,
            ReadEmotionAlpha(ttsTask) ?? (isNarration ? null : 1),
            new WorkbenchTaskLinks(ttsTask?.Id),
            visualBinding,
            voiceBinding,
            interaction.SourceItemType,
            interaction.SourceRole,
            interaction.SourceRoleCharacterId,
            interaction.VoiceRole,
            interaction.VoiceRoleCharacterId);
    }

    private static VoiceAssetBinding BuildVoiceBinding(
        GenerationTask? ttsTask,
        string? voiceReferenceId,
        string? referenceText)
    {
        if (ttsTask is null)
        {
            return VoiceAssetBinding.FromProjectReference(voiceReferenceId, referenceText, null, null);
        }

        var taskVoiceReferenceId = ReadParameter(ttsTask, "voiceReferenceId");
        var sameVoiceReference = string.IsNullOrWhiteSpace(voiceReferenceId) ||
            string.Equals(taskVoiceReferenceId, voiceReferenceId, StringComparison.Ordinal);
        if (!sameVoiceReference)
        {
            return VoiceAssetBinding.FromProjectReference(voiceReferenceId, referenceText, null, null);
        }

        return VoiceAssetBinding.FromProjectReference(
            taskVoiceReferenceId ?? voiceReferenceId,
            ReadParameter(ttsTask, "referenceText") ?? referenceText,
            ReadParameter(ttsTask, "voiceSourceAssetId"),
            ReadParameter(ttsTask, "voiceSyncedPath"));
    }

    private static VisualAssetBinding BuildVisualBinding(VisualAssetBinding? projectVisualBinding)
    {
        if (projectVisualBinding is { SourceKind: VisualSourceKind.Image or VisualSourceKind.Psd })
        {
            return projectVisualBinding!;
        }

        return VisualAssetBinding.None;
    }

    private static string? ReadParameter(GenerationTask task, string key)
    {
        return task.Parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    private static string BuildEffectiveEmotion(
        bool isNarration,
        IReadOnlyDictionary<string, double>? emotionVectors,
        EmotionSpec emotion)
    {
        if (emotionVectors is not null)
        {
            string? strongestKey = null;
            var strongestValue = 0d;
            foreach (var vector in emotionVectors)
            {
                if (strongestKey is null || vector.Value > strongestValue)
                {
                    strongestKey = vector.Key;
                    strongestValue = vector.Value;
                }
            }

            if (strongestKey is not null && strongestValue > 0)
            {
                return $"{strongestKey} {strongestValue.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}";
            }
        }

        return string.IsNullOrWhiteSpace(emotion.Emotion)
            ? "Calm 0"
            : $"{emotion.Emotion} {emotion.Intensity.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private static string SubjectName(
        CharacterInteraction interaction,
        IReadOnlyDictionary<string, string> characterNamesById,
        bool isNarration)
    {
        if (isNarration)
        {
            return interaction.SourceItemType.Trim().ToLowerInvariant() switch
            {
                "action" or "thought" when !string.IsNullOrWhiteSpace(interaction.SourceRole) => interaction.SourceRole.Trim(),
                "effects" => "环境场效",
                "narration" => "旁白叙述",
                _ => NarrationName
            };
        }

        return SubjectName(interaction.SubjectCharacterId, characterNamesById);
    }

    private static string SubjectName(string? characterId, IReadOnlyDictionary<string, string> characterNamesById)
    {
        return !string.IsNullOrWhiteSpace(characterId) && characterNamesById.TryGetValue(characterId, out var name)
            ? name
            : characterId ?? string.Empty;
    }

    private static IReadOnlyDictionary<string, double>? ReadEmotionVectors(
        GenerationTask? task,
        string emotion,
        double intensity)
    {
        var vectors = new Dictionary<string, double>(StringComparer.Ordinal);
        if (task is not null)
        {
            foreach (var parameter in task.Parameters)
            {
                if (!parameter.Key.StartsWith("emotionVector.", StringComparison.Ordinal))
                {
                    continue;
                }

                if (double.TryParse(
                    parameter.Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var value))
                {
                    vectors[parameter.Key["emotionVector.".Length..]] = value;
                }
            }
        }

        return vectors.Count == 0 ? null : vectors;
    }

    private static double? ReadEmotionAlpha(GenerationTask? task)
    {
        return task is not null &&
            task.Parameters.TryGetValue("emotionAlpha", out var value) &&
            double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
    }

    private static Dictionary<string, string> BuildCharacterNameIndex(IReadOnlyList<Character> characters)
    {
        var characterNamesById = new Dictionary<string, string>(characters.Count, StringComparer.Ordinal);
        for (var index = 0; index < characters.Count; index++)
        {
            var character = characters[index];
            characterNamesById.TryAdd(character.Id, character.Name);
        }

        return characterNamesById;
    }

    private static IReadOnlyList<WorkbenchCharacterBindingEdit> BuildCharacterBindings(
        IReadOnlyList<Character> characters)
    {
        var bindings = new List<WorkbenchCharacterBindingEdit>(characters.Count);
        for (var index = 0; index < characters.Count; index++)
        {
            var character = characters[index];
            if (string.Equals(character.Name, NarrationName, StringComparison.Ordinal))
            {
                continue;
            }

            var visualBinding = BuildVisualBinding(character.AssetBinding.PortraitVisualBinding);
            var voiceBinding = character.AssetBinding.VoiceBinding ??
                VoiceAssetBinding.FromProjectReference(character.AssetBinding.Voice, null, null, null);

            bindings.Add(new WorkbenchCharacterBindingEdit(
                character.Id,
                visualBinding,
                voiceBinding));
        }

        return bindings.Count == 0 ? [] : bindings.ToArray();
    }

    private static IReadOnlyList<InteractionScene> SortByOrder(IReadOnlyList<InteractionScene> scenes)
    {
        if (IsOrderedByOrder(scenes))
        {
            return scenes;
        }

        var ordered = scenes.ToArray();
        Array.Sort(ordered, static (left, right) => left.Order.CompareTo(right.Order));
        return ordered;
    }

    private static IReadOnlyList<CharacterInteraction> SortByOrder(IReadOnlyList<CharacterInteraction> interactions)
    {
        if (IsOrderedByOrder(interactions))
        {
            return interactions;
        }

        var ordered = interactions.ToArray();
        Array.Sort(ordered, static (left, right) => left.Order.CompareTo(right.Order));
        return ordered;
    }

    private static bool IsOrderedByOrder(IReadOnlyList<InteractionScene> scenes)
    {
        for (var index = 1; index < scenes.Count; index++)
        {
            if (scenes[index - 1].Order > scenes[index].Order)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsOrderedByOrder(IReadOnlyList<CharacterInteraction> interactions)
    {
        for (var index = 1; index < interactions.Count; index++)
        {
            if (interactions[index - 1].Order > interactions[index].Order)
            {
                return false;
            }
        }

        return true;
    }

    private static Dictionary<string, GenerationTask> BuildRootTtsTaskTargetIndex(
        IReadOnlyList<GenerationTask> tasks)
    {
        var index = new Dictionary<string, GenerationTask>(StringComparer.Ordinal);
        for (var taskIndex = 0; taskIndex < tasks.Count; taskIndex++)
        {
            var task = tasks[taskIndex];
            if (task.Type != GenerationTaskType.Tts || string.IsNullOrWhiteSpace(task.TargetId))
            {
                continue;
            }

            index[task.TargetId] = task;
        }

        return index;
    }

    private static void ResolveInteractionTasks(
        CharacterInteraction interaction,
        IReadOnlyDictionary<string, GenerationTask> rootTtsTasksByTargetId,
        out GenerationTask? ttsTask)
    {
        ttsTask = null;
        for (var index = 0; index < interaction.Tasks.Count; index++)
        {
            var task = interaction.Tasks[index];
            if (ttsTask is null && task.Type == GenerationTaskType.Tts)
            {
                ttsTask = task;
            }
            if (ttsTask is not null)
            {
                return;
            }
        }

        if (rootTtsTasksByTargetId.TryGetValue(interaction.Id, out var rootTtsTask))
        {
            ttsTask = rootTtsTask;
        }
    }
}
