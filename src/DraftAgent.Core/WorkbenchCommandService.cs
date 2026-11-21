using System;
using System.Linq;

namespace DraftAgent.Core;

public sealed class WorkbenchCommandService
{
    public WorkbenchDocument AddScene(
        WorkbenchDocument document,
        string sceneId,
        string name,
        string effect = "",
        string? sceneAssetId = null)
    {
        if (IndexOfScene(document, sceneId) >= 0)
        {
            return document;
        }

        return AppendScene(document, sceneId, name, effect, sceneAssetId);
    }

    public WorkbenchDocument AddSceneUnchecked(
        WorkbenchDocument document,
        string sceneId,
        string name,
        string effect = "",
        string? sceneAssetId = null)
    {
        return AppendScene(document, sceneId, name, effect, sceneAssetId);
    }

    private static WorkbenchDocument AppendScene(
        WorkbenchDocument document,
        string sceneId,
        string name,
        string effect,
        string? sceneAssetId)
    {
        var scenes = new WorkbenchSceneDraft[document.Scenes.Count + 1];
        CopyScenes(document.Scenes, 0, scenes, 0, document.Scenes.Count);
        scenes[^1] = new WorkbenchSceneDraft(
            sceneId,
            document.Scenes.Count + 1,
            name,
            effect,
            [],
            VisualAssetBinding.FromImage(sceneAssetId));

        RenumberScenesInPlace(scenes, 0, scenes.Length - 1);
        return document with { Scenes = scenes };
    }

    public WorkbenchDocument RemoveScene(WorkbenchDocument document, string sceneId)
    {
        var removedIndex = IndexOfScene(document, sceneId);
        if (removedIndex < 0)
        {
            return document;
        }

        return RemoveSceneFromKnownIndex(document, sceneId, removedIndex);
    }

    public WorkbenchDocument RemoveSceneFromKnownLocation(
        WorkbenchDocument document,
        string sceneId,
        int sceneIndex)
    {
        if (!IsValidSceneLocation(document, sceneId, sceneIndex))
        {
            return document;
        }

        return RemoveSceneFromKnownIndex(document, sceneId, sceneIndex);
    }

    private static WorkbenchDocument RemoveSceneFromKnownIndex(
        WorkbenchDocument document,
        string sceneId,
        int removedIndex)
    {
        var scenes = new WorkbenchSceneDraft[document.Scenes.Count - 1];
        if (removedIndex > 0)
        {
            CopyScenes(document.Scenes, 0, scenes, 0, removedIndex);
        }

        if (removedIndex + 1 < document.Scenes.Count)
        {
            CopyScenes(
                document.Scenes,
                removedIndex + 1,
                scenes,
                removedIndex,
                document.Scenes.Count - removedIndex - 1);
        }

        RenumberScenesInPlace(scenes, removedIndex, scenes.Length - 1);
        return document with { Scenes = scenes };
    }

    public WorkbenchDocument AddRow(
        WorkbenchDocument document,
        string sceneId,
        WorkbenchRowDraft row,
        int? insertIndex = null)
    {
        var sceneIndex = IndexOfScene(document, sceneId);
        if (sceneIndex < 0)
        {
            return document;
        }

        if (document.RowsById.ContainsKey(row.InteractionId))
        {
            return document;
        }

        var scene = document.Scenes[sceneIndex];
        return AddRowToSceneAtIndex(document, sceneIndex, sceneId, row, insertIndex, checkOnlyTargetScene: false);
    }

    public WorkbenchDocument AddRowToKnownScene(
        WorkbenchDocument document,
        string sceneId,
        int sceneIndex,
        WorkbenchRowDraft row,
        int? insertIndex = null)
    {
        if (!IsValidSceneLocation(document, sceneId, sceneIndex))
        {
            return document;
        }

        return AddRowToSceneAtIndex(document, sceneIndex, sceneId, row, insertIndex, checkOnlyTargetScene: true);
    }

    private static WorkbenchDocument AddRowToSceneAtIndex(
        WorkbenchDocument document,
        int sceneIndex,
        string sceneId,
        WorkbenchRowDraft row,
        int? insertIndex,
        bool checkOnlyTargetScene)
    {
        var scene = document.Scenes[sceneIndex];
        if (checkOnlyTargetScene && SceneContainsRow(scene, row.InteractionId))
        {
            return document;
        }

        var index = Math.Clamp(insertIndex ?? scene.Rows.Count, 0, scene.Rows.Count);
        var rows = new WorkbenchRowDraft[scene.Rows.Count + 1];
        if (index > 0)
        {
            CopyRows(scene.Rows, 0, rows, 0, index);
        }

        rows[index] = row with { SceneId = sceneId };
        if (index < scene.Rows.Count)
        {
            CopyRows(scene.Rows, index, rows, index + 1, scene.Rows.Count - index);
        }

        RenumberRowsInPlace(rows, sceneId, index, rows.Length - 1);
        return ReplaceScene(document, sceneIndex, scene with { Rows = rows }, renumberScenes: false);
    }

    public WorkbenchDocument RemoveRow(WorkbenchDocument document, string interactionId)
    {
        if (!document.RowLocationsById.TryGetValue(interactionId, out var location))
        {
            return document;
        }

        var sceneIndex = location.SceneIndex;
        var rowIndex = location.RowIndex;
        return RemoveRowFromKnownIndex(document, interactionId, document.Scenes[sceneIndex].SceneId, sceneIndex, rowIndex);
    }

    public WorkbenchDocument RemoveRowFromKnownLocation(
        WorkbenchDocument document,
        string interactionId,
        string sceneId,
        int sceneIndex,
        int rowIndex)
    {
        if (!IsValidRowLocation(document, interactionId, sceneId, sceneIndex, rowIndex))
        {
            return document;
        }

        return RemoveRowFromKnownIndex(document, interactionId, sceneId, sceneIndex, rowIndex);
    }

    private static WorkbenchDocument RemoveRowFromKnownIndex(
        WorkbenchDocument document,
        string interactionId,
        string sceneId,
        int sceneIndex,
        int rowIndex)
    {
        var scene = document.Scenes[sceneIndex];
        var rows = new WorkbenchRowDraft[scene.Rows.Count - 1];
        if (rowIndex > 0)
        {
            CopyRows(scene.Rows, 0, rows, 0, rowIndex);
        }

        if (rowIndex + 1 < scene.Rows.Count)
        {
            CopyRows(scene.Rows, rowIndex + 1, rows, rowIndex, scene.Rows.Count - rowIndex - 1);
        }

        RenumberRowsInPlace(rows, scene.SceneId, rowIndex, rows.Length - 1);
        return ReplaceScene(document, sceneIndex, scene with { Rows = rows }, renumberScenes: false);
    }

    public WorkbenchDocument UpdateRowText(WorkbenchDocument document, string interactionId, string text)
    {
        return UpdateRow(document, interactionId, row => row with { Text = text });
    }

    public WorkbenchDocument UpdateRowTextFromKnownLocation(
        WorkbenchDocument document,
        string interactionId,
        string sceneId,
        int sceneIndex,
        int rowIndex,
        string text)
    {
        return UpdateRowFromKnownLocation(
            document,
            interactionId,
            sceneId,
            sceneIndex,
            rowIndex,
            row => row with { Text = text });
    }

    public WorkbenchDocument UpdateRowSubject(
        WorkbenchDocument document,
        string interactionId,
        string subjectName,
        string? subjectCharacterId)
    {
        return UpdateRow(document, interactionId, row => ApplyRowSubject(row, subjectName, subjectCharacterId));
    }

    public WorkbenchDocument UpdateRowSubjectFromKnownLocation(
        WorkbenchDocument document,
        string interactionId,
        string sceneId,
        int sceneIndex,
        int rowIndex,
        string subjectName,
        string? subjectCharacterId)
    {
        return UpdateRowFromKnownLocation(
            document,
            interactionId,
            sceneId,
            sceneIndex,
            rowIndex,
            row => ApplyRowSubject(row, subjectName, subjectCharacterId));
    }

    public WorkbenchDocument UpdateRowSourceTypeFromKnownLocation(
        WorkbenchDocument document,
        string interactionId,
        string sceneId,
        int sceneIndex,
        int rowIndex,
        string sourceItemType,
        string subjectName,
        string? subjectCharacterId)
    {
        return UpdateRowFromKnownLocation(
            document,
            interactionId,
            sceneId,
            sceneIndex,
            rowIndex,
            row => ApplyRowSourceType(row, sourceItemType, subjectName, subjectCharacterId));
    }

    private static WorkbenchRowDraft ApplyRowSubject(
        WorkbenchRowDraft row,
        string subjectName,
        string? subjectCharacterId)
    {
        var sourceItemType = ResolveSourceItemType(row, subjectName);
        var isNarrationSource = SourceTypeIsNarration(sourceItemType);
        return row with
        {
            SubjectName = subjectName,
            SubjectCharacterId = subjectCharacterId,
            Kind = ResolveRowKind(row, subjectName),
            SourceItemType = sourceItemType,
            SourceRole = isNarrationSource ? string.Empty : subjectName?.Trim() ?? string.Empty,
            SourceRoleCharacterId = isNarrationSource ? null : subjectCharacterId,
            CharacterVisualBinding = ClearVisualBindingWhenVisualOwnershipChanges(row, isNarrationSource)
        };
    }

    private static WorkbenchRowDraft ApplyRowSourceType(
        WorkbenchRowDraft row,
        string sourceItemType,
        string subjectName,
        string? subjectCharacterId)
    {
        var isNarrationSource = SourceTypeIsNarration(sourceItemType);
        return row with
        {
            SubjectName = subjectName,
            SubjectCharacterId = subjectCharacterId,
            Kind = isNarrationSource ? WorkbenchRowKind.Narration : WorkbenchRowKind.Dialogue,
            SourceItemType = sourceItemType,
            SourceRole = isNarrationSource ? string.Empty : subjectName,
            SourceRoleCharacterId = isNarrationSource ? null : subjectCharacterId,
            CharacterVisualBinding = ClearVisualBindingWhenVisualOwnershipChanges(row, isNarrationSource)
        };
    }

    private static VisualAssetBinding? ClearVisualBindingWhenVisualOwnershipChanges(
        WorkbenchRowDraft row,
        bool isNarrationSource)
    {
        if (SourceTypeIsNarration(row.SourceItemType) == isNarrationSource)
        {
            return row.CharacterVisualBinding;
        }

        return VisualAssetBinding.None;
    }

    public WorkbenchDocument UpdateRowEmotion(
        WorkbenchDocument document,
        string interactionId,
        string emotion,
        IReadOnlyDictionary<string, double>? emotionVectors,
        double? emotionAlpha)
    {
        return UpdateRow(document, interactionId, row =>
            row.Emotion == emotion &&
            DictionariesEqual(row.EmotionVectors, emotionVectors) &&
            row.EmotionAlpha == emotionAlpha
                ? row
                : row with
                {
                    Emotion = emotion,
                    EmotionVectors = emotionVectors,
                    EmotionAlpha = emotionAlpha
                });
    }

    public WorkbenchDocument UpdateRowEmotionFromKnownLocation(
        WorkbenchDocument document,
        string interactionId,
        string sceneId,
        int sceneIndex,
        int rowIndex,
        string emotion,
        IReadOnlyDictionary<string, double>? emotionVectors,
        double? emotionAlpha)
    {
        return UpdateRowFromKnownLocation(
            document,
            interactionId,
            sceneId,
            sceneIndex,
            rowIndex,
            row =>
                row.Emotion == emotion &&
                DictionariesEqual(row.EmotionVectors, emotionVectors) &&
                row.EmotionAlpha == emotionAlpha
                    ? row
                    : row with
                    {
                        Emotion = emotion,
                        EmotionVectors = emotionVectors,
                        EmotionAlpha = emotionAlpha
                    });
    }

    public WorkbenchDocument UpdateScene(
        WorkbenchDocument document,
        string sceneId,
        string name,
        string effect)
    {
        var sceneIndex = IndexOfScene(document, sceneId);
        if (sceneIndex < 0)
        {
            return document;
        }

        return UpdateSceneFromKnownIndex(document, sceneId, sceneIndex, scene => scene with { Name = name, Effect = effect });
    }

    public WorkbenchDocument UpdateSceneFromKnownLocation(
        WorkbenchDocument document,
        string sceneId,
        int sceneIndex,
        string name,
        string effect)
    {
        return UpdateSceneFromKnownLocation(
            document,
            sceneId,
            sceneIndex,
            scene => scene with { Name = name, Effect = effect });
    }

    private static WorkbenchDocument UpdateSceneFromKnownLocation(
        WorkbenchDocument document,
        string sceneId,
        int sceneIndex,
        Func<WorkbenchSceneDraft, WorkbenchSceneDraft> update)
    {
        if (!IsValidSceneLocation(document, sceneId, sceneIndex))
        {
            return document;
        }

        return UpdateSceneFromKnownIndex(document, sceneId, sceneIndex, update);
    }

    private static WorkbenchDocument UpdateSceneFromKnownIndex(
        WorkbenchDocument document,
        string sceneId,
        int sceneIndex,
        Func<WorkbenchSceneDraft, WorkbenchSceneDraft> update)
    {
        var scene = document.Scenes[sceneIndex];
        var updatedScene = update(scene);
        if (updatedScene == scene)
        {
            return document;
        }

        return ReplaceScene(document, sceneIndex, updatedScene, renumberScenes: false);
    }

    public WorkbenchDocument UpdateSceneAsset(
        WorkbenchDocument document,
        string sceneId,
        string? sceneAssetId)
    {
        var sceneIndex = IndexOfScene(document, sceneId);
        if (sceneIndex < 0)
        {
            return document;
        }

        return UpdateSceneAssetFromKnownIndex(document, sceneId, sceneIndex, sceneAssetId);
    }

    public WorkbenchDocument UpdateSceneAssetFromKnownLocation(
        WorkbenchDocument document,
        string sceneId,
        int sceneIndex,
        string? sceneAssetId)
    {
        if (!IsValidSceneLocation(document, sceneId, sceneIndex))
        {
            return document;
        }

        return UpdateSceneAssetFromKnownIndex(document, sceneId, sceneIndex, sceneAssetId);
    }

    public WorkbenchDocument UpdateSceneVisualBinding(
        WorkbenchDocument document,
        string sceneId,
        VisualAssetBinding? visualBinding)
    {
        var sceneIndex = IndexOfScene(document, sceneId);
        if (sceneIndex < 0)
        {
            return document;
        }

        return UpdateSceneVisualBindingFromKnownIndex(document, sceneId, sceneIndex, visualBinding);
    }

    public WorkbenchDocument UpdateSceneVisualBindingFromKnownLocation(
        WorkbenchDocument document,
        string sceneId,
        int sceneIndex,
        VisualAssetBinding? visualBinding)
    {
        if (!IsValidSceneLocation(document, sceneId, sceneIndex))
        {
            return document;
        }

        return UpdateSceneVisualBindingFromKnownIndex(document, sceneId, sceneIndex, visualBinding);
    }

    private static WorkbenchDocument UpdateSceneAssetFromKnownIndex(
        WorkbenchDocument document,
        string sceneId,
        int sceneIndex,
        string? sceneAssetId)
    {
        return UpdateSceneVisualBindingFromKnownIndex(document, sceneId, sceneIndex, VisualAssetBinding.FromImage(sceneAssetId));
    }

    private static WorkbenchDocument UpdateSceneVisualBindingFromKnownIndex(
        WorkbenchDocument document,
        string sceneId,
        int sceneIndex,
        VisualAssetBinding? visualBinding)
    {
        var scene = document.Scenes[sceneIndex];
        var normalizedBinding = NormalizeSceneVisualBinding(visualBinding);
        if (VisualAssetBinding.SemanticEquals(scene.VisualBinding, normalizedBinding))
        {
            return document;
        }

        return ReplaceScene(
            document,
            sceneIndex,
            scene with
            {
                VisualBinding = normalizedBinding
            },
            renumberScenes: false);
    }

    public WorkbenchDocument UpdateRowCharacterAsset(
        WorkbenchDocument document,
        string interactionId,
        string? characterAssetId)
    {
        return UpdateRow(document, interactionId, row => UpdateRowCharacterVisualBinding(row, VisualAssetBinding.FromImage(characterAssetId)));
    }

    public WorkbenchDocument UpdateRowCharacterVisualBinding(
        WorkbenchDocument document,
        string interactionId,
        VisualAssetBinding? visualBinding)
    {
        return UpdateRow(document, interactionId, row => UpdateRowCharacterVisualBinding(row, visualBinding));
    }

    public WorkbenchDocument UpdateRowVoiceReference(
        WorkbenchDocument document,
        string interactionId,
        string? voiceReferenceId,
        string? voiceReferenceText)
    {
        return UpdateRow(document, interactionId, row => UpdateRowVoiceBinding(row, VoiceAssetBinding.FromProjectReference(voiceReferenceId, voiceReferenceText, null, null)));
    }

    public WorkbenchDocument UpdateRowVoiceBinding(
        WorkbenchDocument document,
        string interactionId,
        VoiceAssetBinding? voiceBinding)
    {
        return UpdateRow(document, interactionId, row => UpdateRowVoiceBinding(row, voiceBinding));
    }

    public WorkbenchDocument UpdateRowBinding(
        WorkbenchDocument document,
        string interactionId,
        string? characterAssetId,
        string? voiceReferenceId,
        string? voiceReferenceText)
    {
        return UpdateRow(document, interactionId, row => row with
        {
            CharacterVisualBinding = NormalizeVisualBinding(VisualAssetBinding.FromImage(characterAssetId)),
            VoiceBinding = NormalizeVoiceBinding(MergeVoiceBinding(row.VoiceBinding, voiceReferenceId, voiceReferenceText))
        });
    }

    public WorkbenchDocument UpdateRowBindingFromKnownLocation(
        WorkbenchDocument document,
        string interactionId,
        string sceneId,
        int sceneIndex,
        int rowIndex,
        string? characterAssetId,
        string? voiceReferenceId,
        string? voiceReferenceText)
    {
        return UpdateRowFromKnownLocation(
            document,
            interactionId,
            sceneId,
            sceneIndex,
            rowIndex,
            row => row with
            {
                CharacterVisualBinding = NormalizeVisualBinding(VisualAssetBinding.FromImage(characterAssetId)),
                VoiceBinding = NormalizeVoiceBinding(MergeVoiceBinding(row.VoiceBinding, voiceReferenceId, voiceReferenceText))
            });
    }

    public WorkbenchDocument UpdateRowCharacterVisualBindingFromKnownLocation(
        WorkbenchDocument document,
        string interactionId,
        string sceneId,
        int sceneIndex,
        int rowIndex,
        VisualAssetBinding? visualBinding)
    {
        return UpdateRowFromKnownLocation(
            document,
            interactionId,
            sceneId,
            sceneIndex,
            rowIndex,
            row => UpdateRowCharacterVisualBinding(row, visualBinding));
    }

    public WorkbenchDocument MoveRow(
        WorkbenchDocument document,
        string interactionId,
        string targetSceneId,
        int targetIndex)
    {
        if (!document.RowLocationsById.TryGetValue(interactionId, out var sourceLocation))
        {
            return document;
        }

        var targetSceneIndex = IndexOfScene(document, targetSceneId);
        if (targetSceneIndex < 0)
        {
            return document;
        }

        var sourceSceneIndex = sourceLocation.SceneIndex;
        var sourceRowIndex = sourceLocation.RowIndex;
        return MoveRowFromKnownIndexes(document, sourceSceneIndex, sourceRowIndex, targetSceneIndex, targetIndex);
    }

    public WorkbenchDocument MoveRowFromKnownLocation(
        WorkbenchDocument document,
        string interactionId,
        string sourceSceneId,
        int sourceSceneIndex,
        int sourceRowIndex,
        string targetSceneId,
        int targetSceneIndex,
        int targetIndex)
    {
        if (!IsValidRowMoveLocation(
                document,
                interactionId,
                sourceSceneId,
                sourceSceneIndex,
                sourceRowIndex,
                targetSceneId,
                targetSceneIndex))
        {
            return document;
        }

        return MoveRowFromKnownIndexes(document, sourceSceneIndex, sourceRowIndex, targetSceneIndex, targetIndex);
    }

    private static WorkbenchDocument MoveRowFromKnownIndexes(
        WorkbenchDocument document,
        int sourceSceneIndex,
        int sourceRowIndex,
        int targetSceneIndex,
        int targetIndex)
    {
        var sourceScene = document.Scenes[sourceSceneIndex];
        var targetScene = document.Scenes[targetSceneIndex];
        var movingRow = sourceScene.Rows[sourceRowIndex];
        if (sourceSceneIndex == targetSceneIndex)
        {
            var insertIndex = Math.Clamp(targetIndex, 0, sourceScene.Rows.Count - 1);
            if (insertIndex == sourceRowIndex)
            {
                return document;
            }

            var rows = CopySceneRows(sourceScene.Rows);
            MoveItem(rows, sourceRowIndex, insertIndex);
            RenumberRowsInPlace(rows, sourceScene.SceneId, Math.Min(sourceRowIndex, insertIndex), Math.Max(sourceRowIndex, insertIndex));
            return ReplaceScene(document, sourceSceneIndex, sourceScene with { Rows = rows }, renumberScenes: false);
        }

        var sourceRows = new WorkbenchRowDraft[sourceScene.Rows.Count - 1];
        if (sourceRowIndex > 0)
        {
            CopyRows(sourceScene.Rows, 0, sourceRows, 0, sourceRowIndex);
        }

        if (sourceRowIndex + 1 < sourceScene.Rows.Count)
        {
            CopyRows(sourceScene.Rows, sourceRowIndex + 1, sourceRows, sourceRowIndex, sourceScene.Rows.Count - sourceRowIndex - 1);
        }

        var targetRows = new WorkbenchRowDraft[targetScene.Rows.Count + 1];
        var targetInsertIndex = Math.Clamp(targetIndex, 0, targetScene.Rows.Count);
        if (targetInsertIndex > 0)
        {
            CopyRows(targetScene.Rows, 0, targetRows, 0, targetInsertIndex);
        }

        targetRows[targetInsertIndex] = movingRow with { SceneId = targetScene.SceneId };
        if (targetInsertIndex < targetScene.Rows.Count)
        {
            CopyRows(targetScene.Rows, targetInsertIndex, targetRows, targetInsertIndex + 1, targetScene.Rows.Count - targetInsertIndex);
        }

        RenumberRowsInPlace(sourceRows, sourceScene.SceneId, sourceRowIndex, sourceRows.Length - 1);
        RenumberRowsInPlace(targetRows, targetScene.SceneId, targetInsertIndex, targetRows.Length - 1);

        var scenes = CopyDocumentScenes(document);
        scenes[sourceSceneIndex] = sourceScene with { Rows = sourceRows };
        scenes[targetSceneIndex] = targetScene with { Rows = targetRows };
        return document with { Scenes = scenes };
    }

    public WorkbenchDocument MoveScene(WorkbenchDocument document, string sceneId, int targetIndex)
    {
        if (!document.SceneIndexesById.TryGetValue(sceneId, out var oldIndex))
        {
            return document;
        }

        return MoveSceneFromKnownIndex(document, sceneId, oldIndex, targetIndex);
    }

    public WorkbenchDocument MoveSceneFromKnownLocation(
        WorkbenchDocument document,
        string sceneId,
        int oldIndex,
        int targetIndex)
    {
        if (!IsValidSceneMoveLocation(document, sceneId, oldIndex))
        {
            return document;
        }

        return MoveSceneFromKnownIndex(document, sceneId, oldIndex, targetIndex);
    }

    private static WorkbenchDocument MoveSceneFromKnownIndex(
        WorkbenchDocument document,
        string sceneId,
        int oldIndex,
        int targetIndex)
    {
        var insertIndex = Math.Clamp(targetIndex, 0, document.Scenes.Count - 1);
        if (insertIndex == oldIndex)
        {
            return document;
        }

        var scenes = CopyDocumentScenes(document);
        MoveItem(scenes, oldIndex, insertIndex);
        RenumberScenesInPlace(scenes, Math.Min(oldIndex, insertIndex), Math.Max(oldIndex, insertIndex));
        return document with { Scenes = scenes };
    }

    public WorkbenchDocument ApplyCharacterBinding(
        WorkbenchDocument document,
        string characterId,
        string characterName,
        string? portraitAssetId,
        string? voiceReferenceId,
        string? voiceReferenceText = null)
    {
        return ApplyCharacterBinding(
            document,
            characterId,
            characterName,
            portraitAssetId,
            voiceReferenceId,
            voiceReferenceText,
            VoiceAssetBinding.FromProjectReference(voiceReferenceId, voiceReferenceText, null, null));
    }

    public WorkbenchDocument ApplyCharacterVoiceBinding(
        WorkbenchDocument document,
        string characterId,
        string characterName,
        VoiceAssetBinding? voiceBinding)
    {
        var existing = ExistingCharacterBinding(document.CharacterBindings, characterId);
        var normalizedVoiceBinding = NormalizeVoiceBinding(voiceBinding);
        var bindings = ApplyCharacterBindingEdit(
            document.CharacterBindings,
            new WorkbenchCharacterBindingEdit(
                characterId,
                existing?.VisualBinding,
                normalizedVoiceBinding));

        return !ReferenceEquals(bindings, document.CharacterBindings)
            ? document with { CharacterBindings = bindings }
            : document;
    }

    public WorkbenchDocument ApplyCharacterBinding(
        WorkbenchDocument document,
        string characterId,
        string characterName,
        string? portraitAssetId,
        string? voiceReferenceId,
        string? voiceReferenceText,
        VoiceAssetBinding? voiceBinding)
    {
        var changed = false;
        var normalizedVoiceBinding = NormalizeVoiceBinding(voiceBinding);
        var normalizedVisualBinding = NormalizeVisualBinding(VisualAssetBinding.FromImage(portraitAssetId));
        WorkbenchSceneDraft[]? scenes = null;
        var currentSceneIndex = -1;
        WorkbenchRowDraft[]? currentRows = null;
        foreach (var location in document.BindingRowIndex.RowsForCharacter(characterId, characterName))
        {
            if (location.SceneIndex != currentSceneIndex)
            {
                CommitChangedBindingScene(document, scenes, currentSceneIndex, currentRows);
                currentSceneIndex = location.SceneIndex;
                currentRows = null;
            }

            var scene = document.Scenes[location.SceneIndex];
            var rows = currentRows ?? scene.Rows;
            var row = rows[location.RowIndex];
            if (VisualAssetBinding.SemanticEquals(row.CharacterVisualBinding, normalizedVisualBinding) &&
                VoiceAssetBinding.SemanticEquals(row.VoiceBinding, normalizedVoiceBinding))
            {
                continue;
            }

            if (!changed)
            {
                scenes = CopyDocumentScenes(document);
                changed = true;
            }

            currentRows ??= CopySceneRows(scene.Rows);
            currentRows[location.RowIndex] = row with
            {
                CharacterVisualBinding = normalizedVisualBinding,
                VoiceBinding = normalizedVoiceBinding
            };
        }

        CommitChangedBindingScene(document, scenes, currentSceneIndex, currentRows);

        var bindings = ApplyCharacterBindingEdit(
            document.CharacterBindings,
            new WorkbenchCharacterBindingEdit(
                characterId,
                normalizedVisualBinding,
                normalizedVoiceBinding));

        return changed || !ReferenceEquals(bindings, document.CharacterBindings)
            ? document with { Scenes = changed ? scenes! : document.Scenes, CharacterBindings = bindings }
            : document;
    }

    public WorkbenchDocument ApplyBinding(
        WorkbenchDocument document,
        string characterId,
        string? portraitAssetId,
        string? voiceReferenceId)
    {
        return ApplyCharacterBinding(document, characterId, string.Empty, portraitAssetId, voiceReferenceId);
    }

    public WorkbenchDocument ApplyCharacterVisualSourceBinding(
        WorkbenchDocument document,
        string characterId,
        VisualAssetBinding? visualBinding)
    {
        if (string.IsNullOrWhiteSpace(characterId))
        {
            return document;
        }

        var normalizedBinding = NormalizeVisualBinding(visualBinding);
        var existing = ExistingCharacterBinding(document.CharacterBindings, characterId);
        var bindings = ApplyCharacterBindingEdit(
            document.CharacterBindings,
            new WorkbenchCharacterBindingEdit(
                characterId,
                normalizedBinding,
                existing?.VoiceBinding));

        return ReferenceEquals(bindings, document.CharacterBindings)
            ? document
            : document with { CharacterBindings = bindings };
    }

    public WorkbenchDocument ApplyCharacterVisualBinding(
        WorkbenchDocument document,
        string characterId,
        string characterName,
        VisualAssetBinding? visualBinding)
    {
        if (string.IsNullOrWhiteSpace(characterId))
        {
            return document;
        }

        var normalizedBinding = NormalizeVisualBinding(visualBinding);
        var changed = false;
        WorkbenchSceneDraft[]? scenes = null;
        var currentSceneIndex = -1;
        WorkbenchRowDraft[]? currentRows = null;
        foreach (var location in document.BindingRowIndex.RowsForCharacter(characterId, characterName))
        {
            if (location.SceneIndex != currentSceneIndex)
            {
                CommitChangedBindingScene(document, scenes, currentSceneIndex, currentRows);
                currentSceneIndex = location.SceneIndex;
                currentRows = null;
            }

            var scene = document.Scenes[location.SceneIndex];
            var rows = currentRows ?? scene.Rows;
            var row = rows[location.RowIndex];
            if (VisualAssetBinding.SemanticEquals(row.CharacterVisualBinding, normalizedBinding))
            {
                continue;
            }

            if (!changed)
            {
                scenes = CopyDocumentScenes(document);
                changed = true;
            }

            currentRows ??= CopySceneRows(scene.Rows);
            currentRows[location.RowIndex] = row with
            {
                CharacterVisualBinding = normalizedBinding
            };
        }

        CommitChangedBindingScene(document, scenes, currentSceneIndex, currentRows);

        var existing = ExistingCharacterBinding(document.CharacterBindings, characterId);
        var bindings = ApplyCharacterBindingEdit(
            document.CharacterBindings,
            new WorkbenchCharacterBindingEdit(
                characterId,
                normalizedBinding,
                existing?.VoiceBinding));

        return changed || !ReferenceEquals(bindings, document.CharacterBindings)
            ? document with { Scenes = changed ? scenes! : document.Scenes, CharacterBindings = bindings }
            : document;
    }

    public WorkbenchDocument ApplySceneBinding(
        WorkbenchDocument document,
        string sceneId,
        string? sceneAssetId)
    {
        return UpdateSceneAsset(document, sceneId, sceneAssetId);
    }

    public WorkbenchDocument ApplyNarrationBinding(
        WorkbenchDocument document,
        string? voiceReferenceId,
        string? voiceReferenceText = null)
    {
        return ApplyNarrationVoiceBinding(document, VoiceAssetBinding.FromProjectReference(voiceReferenceId, voiceReferenceText, null, null));
    }

    public WorkbenchDocument ApplyNarrationBinding(
        WorkbenchDocument document,
        string? voiceReferenceId,
        string? voiceReferenceText,
        VoiceAssetBinding? voiceBinding)
    {
        return ApplyNarrationVoiceBinding(
            document,
            voiceBinding ?? VoiceAssetBinding.FromProjectReference(voiceReferenceId, voiceReferenceText, null, null));
    }

    public WorkbenchDocument ApplyNarrationVoiceBinding(
        WorkbenchDocument document,
        VoiceAssetBinding? voiceBinding)
    {
        var changed = false;
        var normalizedVoiceBinding = NormalizeVoiceBinding(voiceBinding);
        WorkbenchSceneDraft[]? scenes = null;
        var currentSceneIndex = -1;
        WorkbenchRowDraft[]? currentRows = null;
        foreach (var location in document.BindingRowIndex.NarratorVoiceRows)
        {
            if (location.SceneIndex != currentSceneIndex)
            {
                CommitChangedBindingScene(document, scenes, currentSceneIndex, currentRows);
                currentSceneIndex = location.SceneIndex;
                currentRows = null;
            }

            var scene = document.Scenes[location.SceneIndex];
            var rows = currentRows ?? scene.Rows;
            var row = rows[location.RowIndex];
            if (VoiceAssetBinding.SemanticEquals(row.VoiceBinding, normalizedVoiceBinding))
            {
                continue;
            }

            if (!changed)
            {
                scenes = CopyDocumentScenes(document);
                changed = true;
            }

            currentRows ??= CopySceneRows(scene.Rows);
            currentRows[location.RowIndex] = row with
            {
                VoiceBinding = normalizedVoiceBinding
            };
        }

        CommitChangedBindingScene(document, scenes, currentSceneIndex, currentRows);

        return changed ? document with { Scenes = scenes! } : document;
    }

    public WorkbenchDocument BindThoughtNarrationVoicesToSourceRoles(
        WorkbenchDocument document,
        IReadOnlyList<Character> characters)
    {
        return RouteSourceRoleVoices(
            document,
            characters,
            static row => string.Equals(row.SourceItemType?.Trim(), "thought", StringComparison.OrdinalIgnoreCase),
            routeToSourceRole: true);
    }

    public WorkbenchDocument RouteSourceRoleVoices(
        WorkbenchDocument document,
        IReadOnlyList<Character> characters,
        string sourceItemType,
        bool routeToSourceRole)
    {
        if (string.IsNullOrWhiteSpace(sourceItemType))
        {
            return document;
        }

        var normalizedSourceItemType = sourceItemType.Trim();
        return RouteSourceRoleVoices(
            document,
            characters,
            row => string.Equals(row.SourceItemType?.Trim(), normalizedSourceItemType, StringComparison.OrdinalIgnoreCase),
            routeToSourceRole);
    }

    public WorkbenchDocument RouteDialogueVoicesToSourceRoles(
        WorkbenchDocument document,
        IReadOnlyList<Character> characters,
        bool routeToSourceRole)
    {
        return RouteSourceRoleVoices(
            document,
            characters,
            static row => row.Kind == WorkbenchRowKind.Dialogue ||
                string.Equals(row.SourceItemType?.Trim(), "dialogue", StringComparison.OrdinalIgnoreCase),
            routeToSourceRole);
    }

    public WorkbenchDocument RouteCharacterSourceVoicesToSourceRoles(
        WorkbenchDocument document,
        IReadOnlyList<Character> characters,
        bool routeToSourceRole)
    {
        return RouteSourceRoleVoices(
            document,
            characters,
            static row =>
            {
                var sourceItemType = row.SourceItemType?.Trim();
                return row.Kind == WorkbenchRowKind.Dialogue ||
                    string.Equals(sourceItemType, "dialogue", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(sourceItemType, "thought", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(sourceItemType, "action", StringComparison.OrdinalIgnoreCase);
            },
            routeToSourceRole);
    }

    private WorkbenchDocument RouteSourceRoleVoices(
        WorkbenchDocument document,
        IReadOnlyList<Character> characters,
        Func<WorkbenchRowDraft, bool> matchesRow,
        bool routeToSourceRole)
    {
        var charactersById = characters
            .Where(character => !string.IsNullOrWhiteSpace(character.Id))
            .ToDictionary(character => character.Id, StringComparer.Ordinal);
        var charactersByName = characters
            .Where(character => !string.IsNullOrWhiteSpace(character.Name))
            .GroupBy(character => character.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var changed = false;
        WorkbenchSceneDraft[]? scenes = null;

        for (var sceneIndex = 0; sceneIndex < document.Scenes.Count; sceneIndex++)
        {
            var scene = document.Scenes[sceneIndex];
            WorkbenchRowDraft[]? rows = null;
            for (var rowIndex = 0; rowIndex < scene.Rows.Count; rowIndex++)
            {
                var row = scene.Rows[rowIndex];
                if (!matchesRow(row))
                {
                    continue;
                }

                var character = ResolveSourceRoleCharacter(row, charactersById, charactersByName);
                if (character is null)
                {
                    continue;
                }

                var updatedRow = routeToSourceRole
                    ? ApplySourceRoleVoice(row, character)
                    : row with
                    {
                        VoiceRole = null,
                        VoiceRoleCharacterId = null
                    };

                if (updatedRow == row)
                {
                    continue;
                }

                if (!changed)
                {
                    scenes = CopyDocumentScenes(document);
                    changed = true;
                }

                rows ??= CopySceneRows(scene.Rows);
                rows[rowIndex] = updatedRow;
            }

            if (rows is not null)
            {
                scenes![sceneIndex] = scene with { Rows = rows };
            }
        }

        return changed ? document with { Scenes = scenes! } : document;
    }

    private static WorkbenchRowDraft ApplySourceRoleVoice(WorkbenchRowDraft row, Character character)
    {
        var roleVoiceBinding = NormalizeVoiceBinding(character.AssetBinding.VoiceBinding);
        if (VoiceAssetBinding.SemanticEquals(roleVoiceBinding, VoiceAssetBinding.None) ||
            string.IsNullOrWhiteSpace(roleVoiceBinding.EffectiveVoiceReferenceId))
        {
            return row with
            {
                VoiceRole = null,
                VoiceRoleCharacterId = null
            };
        }

        return row with
        {
            VoiceBinding = roleVoiceBinding,
            VoiceRole = character.Name,
            VoiceRoleCharacterId = character.Id
        };
    }

    private static bool IsNarrationSubject(string? subjectName)
    {
        var normalized = subjectName?.Trim();
        return string.Equals(normalized, "旁白", StringComparison.Ordinal) ||
            string.Equals(normalized, "环境场效", StringComparison.Ordinal) ||
            string.Equals(normalized, "旁白叙述", StringComparison.Ordinal);
    }

    private static WorkbenchRowKind ResolveRowKind(WorkbenchRowDraft row, string? subjectName)
    {
        return IsNarrationSubject(subjectName) || IsNarrationSourceItemType(ResolveSourceItemType(row, subjectName))
            ? WorkbenchRowKind.Narration
            : WorkbenchRowKind.Dialogue;
    }

    private static string ResolveSourceItemType(WorkbenchRowDraft row, string? subjectName)
    {
        var normalized = subjectName?.Trim();
        if (string.Equals(normalized, "环境场效", StringComparison.Ordinal))
        {
            return "effects";
        }

        if (string.Equals(normalized, "旁白", StringComparison.Ordinal) ||
            string.Equals(normalized, "旁白叙述", StringComparison.Ordinal))
        {
            return "narration";
        }

        if (!string.IsNullOrWhiteSpace(normalized))
        {
            var existing = row.SourceItemType?.Trim().ToLowerInvariant();
            return existing is "action" or "thought" ? existing : "dialogue";
        }

        if (string.IsNullOrWhiteSpace(row.SourceItemType))
        {
            return row.Kind == WorkbenchRowKind.Narration ? "narration" : "dialogue";
        }

        return row.SourceItemType;
    }

    private static string ResolveSourceRole(WorkbenchRowDraft row, string? subjectName)
    {
        return ResolveSourceItemType(row, subjectName) is "effects" or "narration"
            ? string.Empty
            : subjectName?.Trim() ?? string.Empty;
    }

    private static string? ResolveSourceRoleCharacterId(
        WorkbenchRowDraft row,
        string? subjectName,
        string? subjectCharacterId)
    {
        return ResolveSourceItemType(row, subjectName) is "effects" or "narration"
            ? null
            : subjectCharacterId;
    }

    private static bool IsNarrationSourceItemType(string? sourceItemType)
    {
        return sourceItemType?.Trim().ToLowerInvariant() is "action" or "thought" or "effects" or "narration";
    }

    private static bool SourceTypeIsNarration(string? sourceItemType)
    {
        return sourceItemType?.Trim().ToLowerInvariant() is "effects" or "narration";
    }

    private static Character? ResolveSourceRoleCharacter(
        WorkbenchRowDraft row,
        IReadOnlyDictionary<string, Character> charactersById,
        IReadOnlyDictionary<string, Character> charactersByName)
    {
        if (!string.IsNullOrWhiteSpace(row.SourceRoleCharacterId) &&
            charactersById.TryGetValue(row.SourceRoleCharacterId, out var byId))
        {
            return byId;
        }

        if (!string.IsNullOrWhiteSpace(row.SourceRole) &&
            charactersByName.TryGetValue(row.SourceRole, out var byName))
        {
            return byName;
        }

        if (row.Kind == WorkbenchRowKind.Dialogue)
        {
            if (!string.IsNullOrWhiteSpace(row.SubjectCharacterId) &&
                charactersById.TryGetValue(row.SubjectCharacterId, out var bySubjectId))
            {
                return bySubjectId;
            }

            if (!string.IsNullOrWhiteSpace(row.SubjectName) &&
                charactersByName.TryGetValue(row.SubjectName, out var bySubjectName))
            {
                return bySubjectName;
            }
        }

        return null;
    }

    private static IReadOnlyList<WorkbenchCharacterBindingEdit> ApplyCharacterBindingEdit(
        IReadOnlyList<WorkbenchCharacterBindingEdit> bindings,
        WorkbenchCharacterBindingEdit edit)
    {
        for (var index = 0; index < bindings.Count; index++)
        {
            if (!string.Equals(bindings[index].CharacterId, edit.CharacterId, StringComparison.Ordinal))
            {
                continue;
            }

            if (BindingEditEquivalent(bindings[index], edit))
            {
                return bindings;
            }

            var updated = CopyCharacterBindings(bindings);
            updated[index] = edit;
            return updated;
        }

        var added = new WorkbenchCharacterBindingEdit[bindings.Count + 1];
        for (var index = 0; index < bindings.Count; index++)
        {
            added[index] = bindings[index];
        }

        added[^1] = edit;
        return added;
    }

    private static WorkbenchCharacterBindingEdit? ExistingCharacterBinding(
        IReadOnlyList<WorkbenchCharacterBindingEdit> bindings,
        string characterId)
    {
        for (var index = 0; index < bindings.Count; index++)
        {
            if (string.Equals(bindings[index].CharacterId, characterId, StringComparison.Ordinal))
            {
                return bindings[index];
            }
        }

        return null;
    }

    private static bool BindingEditEquivalent(
        WorkbenchCharacterBindingEdit existing,
        WorkbenchCharacterBindingEdit edit)
    {
        return string.Equals(existing.CharacterId, edit.CharacterId, StringComparison.Ordinal) &&
            VisualAssetBinding.SemanticEquals(existing.VisualBinding, edit.VisualBinding) &&
            VoiceAssetBinding.SemanticEquals(existing.VoiceBinding, edit.VoiceBinding);
    }

    private static WorkbenchRowDraft UpdateRowCharacterVisualBinding(
        WorkbenchRowDraft row,
        VisualAssetBinding? visualBinding)
    {
        var normalizedBinding = NormalizeVisualBinding(visualBinding);
        if (VisualAssetBinding.SemanticEquals(row.CharacterVisualBinding, normalizedBinding))
        {
            return row;
        }

        return row with
        {
            CharacterVisualBinding = normalizedBinding
        };
    }

    private static WorkbenchRowDraft UpdateRowVoiceBinding(
        WorkbenchRowDraft row,
        VoiceAssetBinding? voiceBinding)
    {
        var normalizedBinding = NormalizeVoiceBinding(voiceBinding);
        if (VoiceAssetBinding.SemanticEquals(row.VoiceBinding, normalizedBinding))
        {
            return row;
        }

        return row with
        {
            VoiceBinding = normalizedBinding
        };
    }

    private static VisualAssetBinding NormalizeVisualBinding(VisualAssetBinding? visualBinding)
    {
        if (visualBinding is null)
        {
            return VisualAssetBinding.None;
        }

        return visualBinding.SourceKind switch
        {
            VisualSourceKind.Image => VisualAssetBinding.FromImage(visualBinding.EffectiveAssetId),
            VisualSourceKind.Psd => VisualAssetBinding.FromPsd(
                visualBinding.SourceAssetId,
                visualBinding.RenderedAssetId,
                visualBinding.PsdSelection,
                visualBinding.PsdLayerMatch),
            _ => VisualAssetBinding.None
        };
    }

    private static VisualAssetBinding NormalizeSceneVisualBinding(VisualAssetBinding? visualBinding)
    {
        var normalizedBinding = NormalizeVisualBinding(visualBinding);
        return normalizedBinding.SourceKind == VisualSourceKind.Psd
            ? VisualAssetBinding.FromImage(normalizedBinding.RenderedAssetId)
            : normalizedBinding;
    }

    private static VoiceAssetBinding NormalizeVoiceBinding(VoiceAssetBinding? voiceBinding)
    {
        if (voiceBinding is null)
        {
            return VoiceAssetBinding.None;
        }

        return VoiceAssetBinding.FromProjectReference(
            voiceBinding.VoiceReferenceId,
            voiceBinding.ReferenceText,
            voiceBinding.SourceAssetId,
            voiceBinding.SyncedVoicePath);
    }

    private static VoiceAssetBinding MergeVoiceBinding(
        VoiceAssetBinding? existing,
        string? voiceReferenceId,
        string? referenceText)
    {
        var normalizedExisting = NormalizeVoiceBinding(existing);
        return VoiceAssetBinding.FromProjectReference(
            voiceReferenceId,
            referenceText,
            string.Equals(normalizedExisting.EffectiveVoiceReferenceId, VoiceAssetBinding.FromProjectReference(voiceReferenceId, referenceText, null, null).EffectiveVoiceReferenceId, StringComparison.Ordinal)
                ? normalizedExisting.SourceAssetId
                : null,
            string.Equals(normalizedExisting.EffectiveVoiceReferenceId, VoiceAssetBinding.FromProjectReference(voiceReferenceId, referenceText, null, null).EffectiveVoiceReferenceId, StringComparison.Ordinal)
                ? normalizedExisting.SyncedVoicePath
                : null);
    }

    private static void CommitChangedBindingScene(
        WorkbenchDocument document,
        WorkbenchSceneDraft[]? scenes,
        int sceneIndex,
        WorkbenchRowDraft[]? rows)
    {
        if (scenes is null || rows is null || sceneIndex < 0)
        {
            return;
        }

        scenes[sceneIndex] = document.Scenes[sceneIndex] with { Rows = rows };
    }

    private static WorkbenchDocument UpdateRow(
        WorkbenchDocument document,
        string interactionId,
        Func<WorkbenchRowDraft, WorkbenchRowDraft> update)
    {
        if (!document.RowLocationsById.TryGetValue(interactionId, out var location))
        {
            return document;
        }

        return UpdateRowFromKnownLocation(
            document,
            interactionId,
            document.Scenes[location.SceneIndex].SceneId,
            location.SceneIndex,
            location.RowIndex,
            update);
    }

    private static WorkbenchDocument UpdateRowFromKnownLocation(
        WorkbenchDocument document,
        string interactionId,
        string sceneId,
        int sceneIndex,
        int rowIndex,
        Func<WorkbenchRowDraft, WorkbenchRowDraft> update)
    {
        if (!IsValidRowLocation(document, interactionId, sceneId, sceneIndex, rowIndex))
        {
            return document;
        }

        var scene = document.Scenes[sceneIndex];
        var row = scene.Rows[rowIndex];
        var updatedRow = update(row);
        if (updatedRow == row)
        {
            return document;
        }

        var rows = new WorkbenchRowDraft[scene.Rows.Count];
        CopyRows(scene.Rows, 0, rows, 0, scene.Rows.Count);
        rows[rowIndex] = updatedRow;
        var scenes = new WorkbenchSceneDraft[document.Scenes.Count];
        CopyScenes(document.Scenes, 0, scenes, 0, document.Scenes.Count);
        scenes[sceneIndex] = scene with { Rows = rows };
        return document with { Scenes = scenes };
    }

    private static WorkbenchDocument ReplaceScene(
        WorkbenchDocument document,
        int sceneIndex,
        WorkbenchSceneDraft scene,
        bool renumberScenes)
    {
        var scenes = CopyDocumentScenes(document);
        scenes[sceneIndex] = scene;
        if (renumberScenes)
        {
            RenumberScenesInPlace(scenes, 0, scenes.Length - 1);
        }

        return document with { Scenes = scenes };
    }

    private static WorkbenchSceneDraft[] CopyDocumentScenes(WorkbenchDocument document)
    {
        var scenes = new WorkbenchSceneDraft[document.Scenes.Count];
        CopyScenes(document.Scenes, 0, scenes, 0, document.Scenes.Count);
        return scenes;
    }

    private static WorkbenchRowDraft[] CopySceneRows(IReadOnlyList<WorkbenchRowDraft> source)
    {
        var rows = new WorkbenchRowDraft[source.Count];
        CopyRows(source, 0, rows, 0, source.Count);
        return rows;
    }

    private static WorkbenchCharacterBindingEdit[] CopyCharacterBindings(
        IReadOnlyList<WorkbenchCharacterBindingEdit> source)
    {
        var bindings = new WorkbenchCharacterBindingEdit[source.Count];
        for (var index = 0; index < source.Count; index++)
        {
            bindings[index] = source[index];
        }

        return bindings;
    }

    private static int IndexOfScene(WorkbenchDocument document, string sceneId)
    {
        return document.SceneIndexesById.TryGetValue(sceneId, out var index) ? index : -1;
    }

    private static bool IsValidRowMoveLocation(
        WorkbenchDocument document,
        string interactionId,
        string sourceSceneId,
        int sourceSceneIndex,
        int sourceRowIndex,
        string targetSceneId,
        int targetSceneIndex)
    {
        return IsValidRowLocation(document, interactionId, sourceSceneId, sourceSceneIndex, sourceRowIndex) &&
            IsValidSceneLocation(document, targetSceneId, targetSceneIndex);
    }

    private static bool IsValidRowLocation(
        WorkbenchDocument document,
        string interactionId,
        string sceneId,
        int sceneIndex,
        int rowIndex)
    {
        if ((uint)sceneIndex >= (uint)document.Scenes.Count)
        {
            return false;
        }

        var scene = document.Scenes[sceneIndex];
        if (!string.Equals(scene.SceneId, sceneId, StringComparison.Ordinal) ||
            (uint)rowIndex >= (uint)scene.Rows.Count)
        {
            return false;
        }

        return string.Equals(scene.Rows[rowIndex].InteractionId, interactionId, StringComparison.Ordinal);
    }

    private static bool SceneContainsRow(WorkbenchSceneDraft scene, string interactionId)
    {
        for (var index = 0; index < scene.Rows.Count; index++)
        {
            if (string.Equals(scene.Rows[index].InteractionId, interactionId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsValidSceneLocation(
        WorkbenchDocument document,
        string sceneId,
        int sceneIndex)
    {
        return (uint)sceneIndex < (uint)document.Scenes.Count &&
            string.Equals(document.Scenes[sceneIndex].SceneId, sceneId, StringComparison.Ordinal);
    }

    private static bool IsValidSceneMoveLocation(
        WorkbenchDocument document,
        string sceneId,
        int sceneIndex)
    {
        return IsValidSceneLocation(document, sceneId, sceneIndex);
    }

    private static bool DictionariesEqual(
        IReadOnlyDictionary<string, double>? left,
        IReadOnlyDictionary<string, double>? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.Count == right.Count &&
            left.All(pair => right.TryGetValue(pair.Key, out var value) && value.Equals(pair.Value));
    }

    private static void RenumberScenesInPlace(
        WorkbenchSceneDraft[] scenes,
        int startIndex,
        int endIndexInclusive)
    {
        if (scenes.Length == 0)
        {
            return;
        }

        var start = Math.Clamp(startIndex, 0, scenes.Length - 1);
        var end = Math.Clamp(endIndexInclusive, 0, scenes.Length - 1);
        if (start > end)
        {
            return;
        }

        for (var index = start; index <= end; index++)
        {
            var order = index + 1;
            var scene = scenes[index];
            if (scene.Order != order)
            {
                scenes[index] = scene with { Order = order };
            }
        }
    }

    private static void RenumberRowsInPlace(
        WorkbenchRowDraft[] rows,
        string sceneId,
        int startIndex,
        int endIndexInclusive)
    {
        if (rows.Length == 0)
        {
            return;
        }

        var start = Math.Clamp(startIndex, 0, rows.Length - 1);
        var end = Math.Clamp(endIndexInclusive, 0, rows.Length - 1);
        if (start > end)
        {
            return;
        }

        for (var index = start; index <= end; index++)
        {
            var order = index + 1;
            var row = rows[index];
            if (row.SceneId != sceneId || row.Order != order)
            {
                rows[index] = row with
                {
                    SceneId = sceneId,
                    Order = order
                };
            }
        }
    }

    private static void MoveItem<T>(T[] items, int sourceIndex, int targetIndex)
    {
        var item = items[sourceIndex];
        if (sourceIndex < targetIndex)
        {
            Array.Copy(items, sourceIndex + 1, items, sourceIndex, targetIndex - sourceIndex);
        }
        else
        {
            Array.Copy(items, targetIndex, items, targetIndex + 1, sourceIndex - targetIndex);
        }

        items[targetIndex] = item;
    }

    private static void CopyScenes(
        IReadOnlyList<WorkbenchSceneDraft> source,
        int sourceIndex,
        WorkbenchSceneDraft[] target,
        int targetIndex,
        int length)
    {
        if (length <= 0)
        {
            return;
        }

        if (source is WorkbenchSceneDraft[] array)
        {
            Array.Copy(array, sourceIndex, target, targetIndex, length);
            return;
        }

        for (var index = 0; index < length; index++)
        {
            target[targetIndex + index] = source[sourceIndex + index];
        }
    }

    private static void CopyRows(
        IReadOnlyList<WorkbenchRowDraft> source,
        int sourceIndex,
        WorkbenchRowDraft[] target,
        int targetIndex,
        int length)
    {
        if (length <= 0)
        {
            return;
        }

        if (source is WorkbenchRowDraft[] array)
        {
            Array.Copy(array, sourceIndex, target, targetIndex, length);
            return;
        }

        for (var index = 0; index < length; index++)
        {
            target[targetIndex + index] = source[sourceIndex + index];
        }
    }
}
