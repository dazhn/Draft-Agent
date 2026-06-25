using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using DraftAgent.Core;

namespace DraftAgent.App.ViewModels;

internal static class WorkbenchViewModelAdapter
{
    public static IReadOnlyList<WorkbenchSceneEdit> BuildSceneEdits(IEnumerable<SceneTableViewModel> scenes)
    {
        if (scenes is IReadOnlyCollection<SceneTableViewModel> sceneCollection)
        {
            var edits = new WorkbenchSceneEdit[sceneCollection.Count];
            var index = 0;
            foreach (var scene in sceneCollection)
            {
                edits[index++] = BuildSceneEdit(scene);
            }

            return edits;
        }

        var sceneEdits = new List<WorkbenchSceneEdit>();
        foreach (var scene in scenes)
        {
            sceneEdits.Add(BuildSceneEdit(scene));
        }

        return sceneEdits.ToArray();
    }

    public static WorkbenchDocument BuildDocument(IEnumerable<SceneTableViewModel> scenes)
    {
        return new WorkbenchDocument(BuildSceneDrafts(scenes), []);
    }

    public static IReadOnlyList<WorkbenchCharacterBindingEdit> BuildCharacterBindings(IEnumerable<BindingTargetViewModel> targets)
    {
        var bindings = targets is IReadOnlyCollection<BindingTargetViewModel> targetCollection
            ? new List<WorkbenchCharacterBindingEdit>(targetCollection.Count)
            : [];
        foreach (var target in targets)
        {
            if (target.Kind != BindingTargetKind.Character)
            {
                continue;
            }

            bindings.Add(new WorkbenchCharacterBindingEdit(
                target.TargetId,
                target.VisualBinding,
                target.VoiceBinding));
        }

        return bindings.Count == 0 ? [] : bindings.ToArray();
    }

    public static WorkbenchRowDraft BuildRowDraft(AnalysisResultRowViewModel row, string sceneId)
    {
        var appliedDraft = row.AppliedDraft;
        var emotionVectors = appliedDraft is not null ? appliedDraft.EmotionVectors : row.EmotionVectorMap;
        var emotionAlpha = appliedDraft is not null ? appliedDraft.EmotionAlpha : (double?)row.EmotionAlpha;

        var characterVisualBinding = row.CharacterVisualBinding;
        if (characterVisualBinding is null || characterVisualBinding.SourceKind == VisualSourceKind.None)
        {
            characterVisualBinding = VisualAssetBinding.FromImage(WorkbenchBindingValue.Asset(row.SelectedCharacterAsset));
        }

        var voiceReferenceId = WorkbenchBindingValue.Asset(row.SelectedVoiceReference);
        var voiceReferenceText = WorkbenchBindingValue.Text(row.SelectedVoiceReferenceText);
        var voiceBinding = BuildVoiceBinding(row.VoiceBinding, voiceReferenceId, voiceReferenceText);

        return new WorkbenchRowDraft(
            row.InteractionId,
            sceneId,
            row.Order,
            row.IsNarration ? WorkbenchRowKind.Narration : WorkbenchRowKind.Dialogue,
            row.SubjectName ?? string.Empty,
            row.SubjectCharacterId,
            row.EffectiveEmotionText,
            row.LineText,
            emotionVectors,
            emotionAlpha,
            new WorkbenchTaskLinks(row.TtsTaskId),
            characterVisualBinding,
            voiceBinding,
            appliedDraft?.SourceItemType ?? string.Empty,
            appliedDraft?.SourceRole ?? string.Empty,
            appliedDraft?.SourceRoleCharacterId,
            appliedDraft?.VoiceRole,
            appliedDraft?.VoiceRoleCharacterId);
    }

    private static VoiceAssetBinding BuildVoiceBinding(
        VoiceAssetBinding? currentBinding,
        string? voiceReferenceId,
        string? voiceReferenceText)
    {
        if (currentBinding is null)
        {
            return VoiceAssetBinding.FromProjectReference(voiceReferenceId, voiceReferenceText, null, null);
        }

        var currentVoiceReferenceId = currentBinding.EffectiveVoiceReferenceId;
        var currentReferenceText = currentBinding.EffectiveReferenceText;
        var sameVoiceReference = string.Equals(currentVoiceReferenceId, voiceReferenceId, StringComparison.Ordinal);
        if (sameVoiceReference &&
            string.Equals(currentReferenceText, voiceReferenceText, StringComparison.Ordinal))
        {
            return currentBinding;
        }

        return VoiceAssetBinding.FromProjectReference(
            voiceReferenceId,
            voiceReferenceText,
            sameVoiceReference ? currentBinding.SourceAssetId : null,
            sameVoiceReference ? currentBinding.SyncedVoicePath : null);
    }

    public static void ApplyDocument(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchDocument document)
    {
        WorkbenchSceneProjection.ApplyDocument(scenes, document);
    }

    public static void ApplyDocument(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchDocument document,
        IEnumerable<SceneTableViewModel> newScenes,
        IEnumerable<AnalysisResultRowViewModel> newRows)
    {
        WorkbenchSceneProjection.ApplyDocument(scenes, document, newScenes, newRows);
    }

    public static void ApplySceneFields(SceneTableViewModel scene, WorkbenchDocument document)
    {
        if (!document.TryGetSceneDraft(scene.SceneId, out var sceneDraft))
        {
            return;
        }

        WorkbenchSceneProjection.ApplySceneFields(scene, sceneDraft);
    }

    public static void ApplyRowFields(AnalysisResultRowViewModel row, WorkbenchDocument document)
    {
        if (!document.RowsById.TryGetValue(row.InteractionId, out var rowDraft))
        {
            return;
        }

        ApplyRowFields(row, rowDraft);
    }

    public static void ApplyRowFields(AnalysisResultRowViewModel row, WorkbenchRowDraft rowDraft)
    {
        WorkbenchRowProjection.ApplyRowFields(row, rowDraft);
    }

    public static void MarkAppliedDrafts(SceneTableViewModel scene, WorkbenchDocument document)
    {
        if (!document.TryGetSceneDraft(scene.SceneId, out var sceneDraft))
        {
            return;
        }

        MarkAppliedDrafts(scene, sceneDraft);
    }

    public static void MarkAppliedDrafts(SceneTableViewModel scene, WorkbenchSceneDraft sceneDraft)
    {
        scene.MarkAppliedDraft(sceneDraft);
        if (WorkbenchRowProjection.TryMarkAppliedRowsByPosition(scene, sceneDraft))
        {
            return;
        }

        WorkbenchRowProjection.MarkAppliedRowsById(scene, sceneDraft);
    }

    public static void MarkAppliedDrafts(IEnumerable<SceneTableViewModel> scenes, WorkbenchDocument document)
    {
        foreach (var scene in scenes)
        {
            MarkAppliedDrafts(scene, document);
        }
    }

    public static void MarkAppliedDrafts(
        SceneTableViewModel scene,
        WorkbenchDocument document,
        int startRowIndex,
        int endRowIndexInclusive)
    {
        if (!document.TryGetSceneDraft(scene.SceneId, out var sceneDraft))
        {
            return;
        }

        MarkAppliedDrafts(scene, sceneDraft, startRowIndex, endRowIndexInclusive);
    }

    public static void MarkAppliedDrafts(
        SceneTableViewModel scene,
        WorkbenchSceneDraft sceneDraft,
        int startRowIndex,
        int endRowIndexInclusive)
    {
        scene.MarkAppliedDraft(sceneDraft);
        WorkbenchRowProjection.MarkAppliedRowsByPositionRange(
            scene,
            sceneDraft,
            startRowIndex,
            endRowIndexInclusive);
    }

    public static void MarkAppliedSceneDrafts(IEnumerable<SceneTableViewModel> scenes, WorkbenchDocument document)
    {
        foreach (var scene in scenes)
        {
            if (document.TryGetSceneDraft(scene.SceneId, out var sceneDraft))
            {
                WorkbenchSceneProjection.MarkAppliedSceneDraft(scene, sceneDraft);
            }
        }
    }

    public static void MarkAppliedSceneDraft(SceneTableViewModel scene, WorkbenchDocument document)
    {
        if (document.TryGetSceneDraft(scene.SceneId, out var sceneDraft))
        {
            WorkbenchSceneProjection.MarkAppliedSceneDraft(scene, sceneDraft);
        }
    }

    public static void MarkAppliedSceneDraft(SceneTableViewModel scene, WorkbenchSceneDraft sceneDraft)
    {
        WorkbenchSceneProjection.MarkAppliedSceneDraft(scene, sceneDraft);
    }

    private static WorkbenchSceneEdit BuildSceneEdit(SceneTableViewModel scene)
    {
        return new WorkbenchSceneEdit(
            scene.SceneId,
            scene.Order,
            scene.SceneName,
            scene.SceneEffect,
            BuildInteractionEdits(scene),
            BuildSceneVisualBinding(scene));
    }

    private static WorkbenchSceneDraft BuildSceneDraft(SceneTableViewModel scene)
    {
        var visualBinding = BuildSceneVisualBinding(scene);

        return new WorkbenchSceneDraft(
            scene.SceneId,
            scene.Order,
            scene.SceneName,
            scene.SceneEffect,
            BuildRowDrafts(scene),
            visualBinding);
    }

    private static VisualAssetBinding BuildSceneVisualBinding(SceneTableViewModel scene)
    {
        var visualBinding = scene.VisualBinding;
        if (visualBinding is null || visualBinding.SourceKind == VisualSourceKind.None || visualBinding.SourceKind == VisualSourceKind.Psd)
        {
            return VisualAssetBinding.FromImage(WorkbenchBindingValue.Asset(scene.SelectedSceneAsset));
        }

        return visualBinding;
    }

    private static IReadOnlyList<WorkbenchSceneDraft> BuildSceneDrafts(IEnumerable<SceneTableViewModel> scenes)
    {
        if (scenes is IReadOnlyCollection<SceneTableViewModel> sceneCollection)
        {
            var drafts = new WorkbenchSceneDraft[sceneCollection.Count];
            var index = 0;
            foreach (var scene in sceneCollection)
            {
                drafts[index++] = BuildSceneDraft(scene);
            }

            return drafts;
        }

        var sceneDrafts = new List<WorkbenchSceneDraft>();
        foreach (var scene in scenes)
        {
            sceneDrafts.Add(BuildSceneDraft(scene));
        }

        return sceneDrafts.ToArray();
    }

    private static IReadOnlyList<WorkbenchRowDraft> BuildRowDrafts(SceneTableViewModel scene)
    {
        var rows = new WorkbenchRowDraft[scene.Rows.Count];
        for (var index = 0; index < scene.Rows.Count; index++)
        {
            rows[index] = BuildRowDraft(scene.Rows[index], scene.SceneId);
        }

        return rows;
    }

    private static WorkbenchInteractionEdit BuildInteractionEdit(AnalysisResultRowViewModel row)
    {
        return new WorkbenchInteractionEdit(
            row.InteractionId,
            row.Order,
            row.SubjectName ?? string.Empty,
            row.EffectiveEmotionText,
            row.LineText,
            row.CharacterVisualBinding is { SourceKind: not VisualSourceKind.None }
                ? row.CharacterVisualBinding
                : VisualAssetBinding.FromImage(WorkbenchBindingValue.Asset(row.SelectedCharacterAsset)),
            BuildVoiceBinding(row.VoiceBinding, WorkbenchBindingValue.Asset(row.SelectedVoiceReference), WorkbenchBindingValue.Text(row.SelectedVoiceReferenceText)),
            row.EmotionVectorMap,
            row.EmotionAlpha);
    }

    private static IReadOnlyList<WorkbenchInteractionEdit> BuildInteractionEdits(SceneTableViewModel scene)
    {
        var edits = new WorkbenchInteractionEdit[scene.Rows.Count];
        for (var index = 0; index < scene.Rows.Count; index++)
        {
            edits[index] = BuildInteractionEdit(scene.Rows[index]);
        }

        return edits;
    }
}
