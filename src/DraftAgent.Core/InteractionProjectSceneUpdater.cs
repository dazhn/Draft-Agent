namespace DraftAgent.Core;

public sealed record InteractionProjectSceneUpdate(
    IReadOnlyList<InteractionScene> Scenes,
    IReadOnlyList<Character> Characters);

public sealed class InteractionProjectSceneUpdater
{
    private const string NarrationName = "旁白";
    private readonly WorkbenchTaskUpdater _taskUpdater;

    public InteractionProjectSceneUpdater(WorkbenchTaskUpdater taskUpdater)
    {
        _taskUpdater = taskUpdater;
    }

    public InteractionProjectSceneUpdate ApplyScenes(
        InteractionProject project,
        WorkbenchDocument document,
        IWorkflowLogger logger)
    {
        var existingScenesById = project.Scenes.ToDictionary(scene => scene.Id, StringComparer.Ordinal);
        var existingInteractionsById = project.Scenes
            .SelectMany(scene => scene.Interactions)
            .GroupBy(interaction => interaction.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var existingTasksById = project.GenerationTasks
            .Concat(project.Scenes.SelectMany(scene => scene.Interactions).SelectMany(interaction => interaction.Tasks))
            .GroupBy(task => task.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var usedSceneIds = new HashSet<string>(StringComparer.Ordinal);
        var usedInteractionIds = new HashSet<string>(StringComparer.Ordinal);
        var characters = project.Characters.ToList();
        var characterByName = characters.ToDictionary(character => character.Name, StringComparer.Ordinal);
        var characterById = characters.ToDictionary(character => character.Id, StringComparer.Ordinal);

        var updatedScenes = document.Scenes
            .OrderBy(scene => scene.Order)
            .Select((sceneDraft, sceneIndex) =>
            {
                var sceneId = ResolveId(sceneDraft.SceneId, "scene_manual", usedSceneIds);
                existingScenesById.TryGetValue(sceneId, out var existingScene);
                logger.Info(
                    WorkflowSteps.WorkbenchEdit,
                    "scene_apply_started",
                    "开始应用场景表格修改。",
                    new Dictionary<string, string>
                    {
                        ["sceneId"] = sceneId,
                        ["rowCount"] = sceneDraft.Rows.Count.ToString()
                    });

                var sceneVisualBinding = NormalizeSceneVisualBindingForProject(sceneDraft.VisualBinding);
                var rows = sceneDraft.Rows
                    .OrderBy(row => row.Order)
                    .Select((row, rowIndex) => BuildInteraction(
                        row,
                        sceneId,
                        sceneVisualBinding,
                        rowIndex + 1,
                        ExistingInteraction(row.InteractionId, existingInteractionsById),
                        existingTasksById,
                        characters,
                        characterByName,
                        characterById,
                        usedInteractionIds))
                    .ToArray();

                var scene = existingScene is null
                    ? CreateScene(sceneId, sceneIndex + 1, sceneDraft, rows)
                    : existingScene with
                    {
                        Order = sceneIndex + 1,
                        Name = BlankFallback(sceneDraft.Name, existingScene.Name),
                        Description = sceneDraft.Effect.Trim(),
                        AssetBinding = existingScene.AssetBinding with
                        {
                            BackgroundImage = WorkbenchProjectBindingValue.Normalize(sceneVisualBinding.EffectiveAssetId),
                            BackgroundVisualBinding = sceneVisualBinding
                        },
                        Interactions = rows
                    };

                logger.Info(
                    WorkflowSteps.WorkbenchEdit,
                    "scene_apply_completed",
                    "场景表格修改已应用。",
                    new Dictionary<string, string>
                    {
                        ["sceneId"] = scene.Id,
                        ["rowCount"] = rows.Length.ToString()
                    });

                return scene;
            })
            .ToArray();

        return new InteractionProjectSceneUpdate(updatedScenes, characters);
    }

    private CharacterInteraction BuildInteraction(
        WorkbenchRowDraft row,
        string sceneId,
        VisualAssetBinding sceneVisualBinding,
        int order,
        CharacterInteraction? existingInteraction,
        IReadOnlyDictionary<string, GenerationTask> existingTasksById,
        List<Character> characters,
        Dictionary<string, Character> characterByName,
        Dictionary<string, Character> characterById,
        HashSet<string> usedInteractionIds)
    {
        var interactionId = ResolveId(row.InteractionId, "int_manual", usedInteractionIds);
        var text = BlankFallback(row.Text, "待填写");
        var voiceReferenceId = WorkbenchProjectBindingValue.Normalize(row.VoiceBinding?.EffectiveVoiceReferenceId);
        var voiceReferenceText = WorkbenchProjectBindingValue.Normalize(row.VoiceBinding?.EffectiveReferenceText);
        var isNarration = row.Kind == WorkbenchRowKind.Narration || IsNarration(row.SubjectName);
        var isExistingInnerMonologue = !isNarration && existingInteraction?.Type == InteractionType.InnerMonologue;
        var action = existingInteraction is null
            ? new ActionSpec(isNarration ? text : "台词表演", [], string.Empty, 2)
            : existingInteraction.Action with { MainAction = isNarration ? text : "台词表演" };

        if (isExistingInnerMonologue)
        {
            action = action with { MainAction = text };
        }

        if (isNarration)
        {
            var narrationEmotion = ParseEmotion(row.Emotion, "Calm", row.EmotionVectors);
            var narrationEmotionAlpha = Math.Clamp(row.EmotionAlpha ?? 1, 0, IndexTts2Defaults.MaxEmotionStrength);
            var narrationVoice = new VoiceRenderSettings(voiceReferenceId, narrationEmotion.Emotion, narrationEmotion.Intensity, 1, 0.6, NormalizeEmotionVectorsOrNull(row.EmotionVectors), narrationEmotionAlpha, voiceReferenceText);
            var narration = new NarrationLine(existingInteraction?.Narration?.Id ?? $"nar_{interactionId}", text, narrationVoice);
            var narrationInteraction = new CharacterInteraction(
                interactionId,
                order,
                InteractionType.Narration,
                sceneId,
                null,
                null,
                narrationEmotion,
                action,
                null,
                null,
                narration,
                new InteractionBinding(
                    null,
                    voiceReferenceId,
                    sceneVisualBinding,
                    NormalizeVisualBindingForProject(row.CharacterVisualBinding)),
                [],
                existingInteraction?.SourceSegmentId ?? string.Empty)
            {
                SourceItemType = SourceItemType(row, existingInteraction),
                SourceRole = SourceRole(row, existingInteraction),
                SourceRoleCharacterId = SourceRoleCharacterId(row, existingInteraction),
                VoiceRole = VoiceRole(row, existingInteraction),
                VoiceRoleCharacterId = VoiceRoleCharacterId(row, existingInteraction)
            };

            return narrationInteraction with
            {
                Tasks = _taskUpdater.BuildTasks(narrationInteraction, existingInteraction, row.TaskLinks, existingTasksById, row.VoiceBinding)
            };
        }

        var character = ResolveCharacter(row, characters, characterByName, characterById);
        var emotion = ParseEmotion(row.Emotion, character.DefaultEmotion, row.EmotionVectors);
        var characterVisualBinding = NormalizeVisualBindingForProject(row.CharacterVisualBinding);
        var emotionAlpha = Math.Clamp(row.EmotionAlpha ?? 1, 0, IndexTts2Defaults.MaxEmotionStrength);
        var dialogueVoice = new VoiceRenderSettings(voiceReferenceId, emotion.Emotion, emotion.Intensity, 1, 0.6, NormalizeEmotionVectorsOrNull(row.EmotionVectors), emotionAlpha, voiceReferenceText);
        if (isExistingInnerMonologue)
        {
            var innerMonologue = new InnerMonologue(
                existingInteraction?.InnerMonologue?.Id ?? $"inner_{interactionId}",
                character.Id,
                text,
                dialogueVoice);
            var innerInteraction = new CharacterInteraction(
                interactionId,
                order,
                InteractionType.InnerMonologue,
                sceneId,
                character.Id,
                null,
                emotion,
                action,
                null,
                innerMonologue,
                null,
                new InteractionBinding(voiceReferenceId, null, sceneVisualBinding, characterVisualBinding),
                [],
                existingInteraction?.SourceSegmentId ?? string.Empty)
            {
                SourceItemType = SourceItemType(row, existingInteraction),
                SourceRole = SourceRole(row, existingInteraction),
                SourceRoleCharacterId = SourceRoleCharacterId(row, existingInteraction),
                VoiceRole = VoiceRole(row, existingInteraction),
                VoiceRoleCharacterId = VoiceRoleCharacterId(row, existingInteraction)
            };

            return innerInteraction with
            {
                Tasks = _taskUpdater.BuildTasks(innerInteraction, existingInteraction, row.TaskLinks, existingTasksById, row.VoiceBinding)
            };
        }

        var dialogue = new DialogueLine(existingInteraction?.Dialogue?.Id ?? $"dia_{interactionId}", character.Id, null, text, dialogueVoice);
        var dialogueInteraction = new CharacterInteraction(
            interactionId,
            order,
            InteractionType.Dialogue,
            sceneId,
            character.Id,
            null,
            emotion,
            action,
            dialogue,
            null,
            null,
            new InteractionBinding(voiceReferenceId, null, sceneVisualBinding, characterVisualBinding),
            [],
            existingInteraction?.SourceSegmentId ?? string.Empty)
        {
            SourceItemType = SourceItemType(row, existingInteraction),
            SourceRole = SourceRole(row, existingInteraction),
            SourceRoleCharacterId = SourceRoleCharacterId(row, existingInteraction),
            VoiceRole = VoiceRole(row, existingInteraction),
            VoiceRoleCharacterId = VoiceRoleCharacterId(row, existingInteraction)
        };

        return dialogueInteraction with
        {
            Tasks = _taskUpdater.BuildTasks(dialogueInteraction, existingInteraction, row.TaskLinks, existingTasksById, row.VoiceBinding)
        };
    }

    private static CharacterInteraction? ExistingInteraction(
        string interactionId,
        IReadOnlyDictionary<string, CharacterInteraction> existingInteractionsById)
    {
        return !string.IsNullOrWhiteSpace(interactionId) &&
            existingInteractionsById.TryGetValue(interactionId, out var existingInteraction)
                ? existingInteraction
                : null;
    }

    private static InteractionScene CreateScene(
        string sceneId,
        int order,
        WorkbenchSceneDraft sceneDraft,
        IReadOnlyList<CharacterInteraction> rows)
    {
        return new InteractionScene(
            sceneId,
            order,
            BlankFallback(sceneDraft.Name, $"场景 {order}"),
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            sceneDraft.Effect.Trim(),
            new SceneAssetBinding(
                WorkbenchProjectBindingValue.Normalize(NormalizeSceneVisualBindingForProject(sceneDraft.VisualBinding).EffectiveAssetId),
                BackgroundVisualBinding: NormalizeSceneVisualBindingForProject(sceneDraft.VisualBinding)),
            rows);
    }

    private static VisualAssetBinding NormalizeSceneVisualBindingForProject(VisualAssetBinding? binding)
    {
        if (binding is { SourceKind: VisualSourceKind.Psd })
        {
            return VisualAssetBinding.FromImage(binding.RenderedAssetId);
        }

        return NormalizeVisualBindingForProject(binding);
    }

    private static VisualAssetBinding NormalizeVisualBindingForProject(VisualAssetBinding? binding)
    {
        return binding is { SourceKind: not VisualSourceKind.None }
            ? binding
            : VisualAssetBinding.None;
    }

    private static Character ResolveCharacter(
        WorkbenchRowDraft row,
        List<Character> characters,
        Dictionary<string, Character> characterByName,
        Dictionary<string, Character> characterById)
    {
        var name = CharacterNameGuard.NormalizeOrNull(row.SubjectName) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name) || IsNarration(name))
        {
            name = "未命名人物";
        }

        if (!string.IsNullOrWhiteSpace(row.SubjectCharacterId) &&
            characterById.TryGetValue(row.SubjectCharacterId, out var characterByIdMatch) &&
            string.Equals(characterByIdMatch.Name, name, StringComparison.Ordinal))
        {
            return characterByIdMatch;
        }

        if (characterByName.TryGetValue(name, out var existing))
        {
            return existing;
        }

        var character = CreateCharacter(name, characters);
        characters.Add(character);
        characterByName[name] = character;
        characterById[character.Id] = character;
        return character;
    }

    private static Character CreateCharacter(string name, IReadOnlyCollection<Character> existingCharacters)
    {
        return new Character(
            NextId("char_manual", existingCharacters.Select(character => character.Id)),
            name,
            existingCharacters.Count == 0 ? "主角" : "角色",
            "待绑定参考形象",
            "待选择音色",
            "克制",
            new AssetBinding());
    }

    private static EmotionSpec ParseEmotion(string value, string fallback, IReadOnlyDictionary<string, double>? emotionVectors)
    {
        var normalized = NormalizeName(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = string.IsNullOrWhiteSpace(fallback) ? "克制" : fallback;
        }

        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var emotion = ResolvePrimaryEmotion(parts.Length > 0 ? parts[0] : normalized, emotionVectors);
        var intensity = 0.6;
        if (emotionVectors is not null && emotionVectors.Count > 0)
        {
            intensity = Math.Clamp(emotionVectors.TryGetValue(emotion, out var vectorValue) ? vectorValue : emotionVectors.Values.DefaultIfEmpty(0).Max(), 0, IndexTts2Defaults.MaxEmotionStrength);
        }
        if (parts.Length > 1)
        {
            var numberText = parts[1].TrimEnd('%');
            if (double.TryParse(numberText, out var parsed))
            {
                intensity = parts[1].EndsWith("%", StringComparison.Ordinal) ? parsed / 100 : parsed;
            }
        }

        intensity = Math.Clamp(intensity, 0, IndexTts2Defaults.MaxEmotionStrength);
        return new EmotionSpec(emotion, intensity, string.Empty, emotion, emotion, string.Empty);
    }

    private static string ResolvePrimaryEmotion(string emotion, IReadOnlyDictionary<string, double>? emotionVectors)
    {
        if (emotionVectors is not null && emotionVectors.Count > 0)
        {
            var strongest = emotionVectors
                .Where(pair => IndexTts2Defaults.EmotionKeys.Contains(pair.Key))
                .OrderByDescending(pair => pair.Value)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(strongest.Key) && strongest.Value > 0)
            {
                return strongest.Key;
            }
        }

        return IndexTts2Defaults.NormalizeEmotion(emotion);
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

    private static IReadOnlyDictionary<string, double>? NormalizeEmotionVectorsOrNull(IReadOnlyDictionary<string, double>? emotionVectors)
    {
        if (emotionVectors is null || emotionVectors.Count == 0 || emotionVectors.Values.All(value => value <= 0))
        {
            return null;
        }

        return NormalizeEmotionVectors(emotionVectors);
    }

    private static string ResolveId(string? requestedId, string prefix, HashSet<string> usedIds)
    {
        if (!string.IsNullOrWhiteSpace(requestedId) && usedIds.Add(requestedId.Trim()))
        {
            return requestedId.Trim();
        }

        var id = NextId(prefix, usedIds);
        usedIds.Add(id);
        return id;
    }

    private static string NextId(string prefix, IEnumerable<string> existingIds)
    {
        var existing = existingIds.ToHashSet(StringComparer.Ordinal);
        for (var index = 1; index < 100_000; index++)
        {
            var id = $"{prefix}_{index:000}";
            if (!existing.Contains(id))
            {
                return id;
            }
        }

        return $"{prefix}_{Guid.NewGuid():N}";
    }

    private static string NormalizeName(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    private static string BlankFallback(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static bool IsNarration(string? value)
    {
        return string.Equals(NormalizeName(value), NarrationName, StringComparison.Ordinal);
    }

    private static string SourceItemType(WorkbenchRowDraft row, CharacterInteraction? existingInteraction)
    {
        return string.IsNullOrWhiteSpace(row.SourceItemType)
            ? existingInteraction?.SourceItemType ?? string.Empty
            : row.SourceItemType.Trim();
    }

    private static string SourceRole(WorkbenchRowDraft row, CharacterInteraction? existingInteraction)
    {
        return string.IsNullOrWhiteSpace(row.SourceRole)
            ? existingInteraction?.SourceRole ?? string.Empty
            : row.SourceRole.Trim();
    }

    private static string? SourceRoleCharacterId(WorkbenchRowDraft row, CharacterInteraction? existingInteraction)
    {
        return string.IsNullOrWhiteSpace(row.SourceRoleCharacterId)
            ? existingInteraction?.SourceRoleCharacterId
            : row.SourceRoleCharacterId.Trim();
    }

    private static string? VoiceRole(WorkbenchRowDraft row, CharacterInteraction? existingInteraction)
    {
        return string.IsNullOrWhiteSpace(row.VoiceRole)
            ? existingInteraction?.VoiceRole
            : row.VoiceRole.Trim();
    }

    private static string? VoiceRoleCharacterId(WorkbenchRowDraft row, CharacterInteraction? existingInteraction)
    {
        return string.IsNullOrWhiteSpace(row.VoiceRoleCharacterId)
            ? existingInteraction?.VoiceRoleCharacterId
            : row.VoiceRoleCharacterId.Trim();
    }
}
