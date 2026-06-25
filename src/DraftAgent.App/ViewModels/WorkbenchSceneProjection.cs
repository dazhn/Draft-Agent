using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using DraftAgent.Core;

namespace DraftAgent.App.ViewModels;

internal static class WorkbenchSceneProjection
{
    private static readonly AsyncLocal<WorkbenchSceneProjectionMetrics?> CurrentMetrics = new();

    public static WorkbenchSceneProjectionMetrics? Metrics
    {
        get => CurrentMetrics.Value;
        set => CurrentMetrics.Value = value;
    }

    public static void ApplyDocument(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchDocument document)
    {
        if (TryApplyScenesByPosition(scenes, document))
        {
            return;
        }

        ApplyDocument(scenes, document, [], []);
    }

    public static void ApplyDocument(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchDocument document,
        IEnumerable<SceneTableViewModel> newScenes,
        IEnumerable<AnalysisResultRowViewModel> newRows)
    {
        Metrics?.RecordFallbackApplyDocument("direct_apply_document");
        var scenesById = new Dictionary<string, SceneTableViewModel>(scenes.Count + 1, StringComparer.Ordinal);
        for (var index = 0; index < scenes.Count; index++)
        {
            scenesById[scenes[index].SceneId] = scenes[index];
        }

        foreach (var scene in newScenes)
        {
            scenesById[scene.SceneId] = scene;
        }

        var rowsById = new WorkbenchRowProjectionIndex(scenes);
        foreach (var row in newRows)
        {
            rowsById.AddOrUpdate(row);
        }

        var desiredSceneIds = new HashSet<string>(document.Scenes.Count, StringComparer.Ordinal);
        for (var index = 0; index < document.Scenes.Count; index++)
        {
            desiredSceneIds.Add(document.Scenes[index].SceneId);
        }

        for (var index = scenes.Count - 1; index >= 0; index--)
        {
            if (!desiredSceneIds.Contains(scenes[index].SceneId))
            {
                foreach (var row in scenes[index].Rows)
                {
                    rowsById.AddOrUpdate(row);
                }

                scenes.RemoveAt(index);
            }
        }

        var currentSceneIndexById = BuildSceneIndex(scenes);
        for (var targetIndex = 0; targetIndex < document.Scenes.Count; targetIndex++)
        {
            var sceneDraft = document.Scenes[targetIndex];
            if (!scenesById.TryGetValue(sceneDraft.SceneId, out var scene))
            {
                scene = SceneTableViewModel.FromDraft(sceneDraft);
                scenesById[scene.SceneId] = scene;
                scene.MarkAppliedDraft(sceneDraft);
                WorkbenchRowProjection.TryMarkAppliedRowsByPosition(scene, sceneDraft);
            }

            var changed = false;
            var previewChanges = PreviewRefreshChanges.None;
            if (!currentSceneIndexById.TryGetValue(scene.SceneId, out var currentIndex))
            {
                scenes.Insert(targetIndex, scene);
                RefreshSceneIndexes(scenes, currentSceneIndexById, targetIndex);
                changed = true;
            }
            else if (currentIndex != targetIndex)
            {
                scenes.Move(currentIndex, targetIndex);
                RefreshSceneIndexes(scenes, currentSceneIndexById, Math.Min(currentIndex, targetIndex));
                changed = true;
            }

            changed |= SetIfDifferent(scene.Order, sceneDraft.Order, value => scene.Order = value);
            if (!ReferenceEquals(scene.AppliedDraft, sceneDraft))
            {
                changed |= ApplySceneFieldsIfChanged(scene, sceneDraft, out var scenePreviewChanged);
                previewChanges = previewChanges with { ScenePreviewChanged = previewChanges.ScenePreviewChanged || scenePreviewChanged };
                changed |= WorkbenchRowProjection.ApplyRows(scene, sceneDraft, rowsById, out var rowsPreviewChanges);
                previewChanges = previewChanges.Merge(rowsPreviewChanges);
            }

            if (previewChanges.Any)
            {
                scene.RefreshPreviewAssets(
                    previewChanges.ScenePreviewChanged,
                    previewChanges.FirstCharacterPreviewChanged,
                    forceScenePreviewNotifications: false);
            }
        }
    }

    public static void ApplySceneFields(SceneTableViewModel scene, WorkbenchSceneDraft sceneDraft)
    {
        _ = ApplySceneFieldsIfChanged(scene, sceneDraft, out _);
    }

    public static void MarkAppliedSceneDrafts(IEnumerable<SceneTableViewModel> scenes, WorkbenchDocument document)
    {
        foreach (var scene in scenes)
        {
            if (document.TryGetSceneDraft(scene.SceneId, out var sceneDraft))
            {
                scene.MarkAppliedDraft(sceneDraft);
            }
        }
    }

    public static void MarkAppliedSceneDraft(SceneTableViewModel scene, WorkbenchDocument document)
    {
        if (document.TryGetSceneDraft(scene.SceneId, out var sceneDraft))
        {
            MarkAppliedSceneDraft(scene, sceneDraft);
        }
    }

    public static void MarkAppliedSceneDraft(SceneTableViewModel scene, WorkbenchSceneDraft sceneDraft)
    {
        scene.MarkAppliedDraft(sceneDraft);
    }

    public static bool TryApplyScenesByPosition(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchDocument document)
    {
        Metrics?.RecordAlignedApplyAttempt();
        if (scenes.Count != document.Scenes.Count)
        {
            Metrics?.RecordAlignmentFailure($"scene_count:{scenes.Count}!={document.Scenes.Count}");
            return false;
        }

        for (var sceneIndex = 0; sceneIndex < scenes.Count; sceneIndex++)
        {
            var scene = scenes[sceneIndex];
            var sceneDraft = document.Scenes[sceneIndex];
            if (!string.Equals(scene.SceneId, sceneDraft.SceneId, StringComparison.Ordinal))
            {
                Metrics?.RecordAlignmentFailure($"scene_id:{sceneIndex}:{scene.SceneId}!={sceneDraft.SceneId}");
                return false;
            }

            if (!WorkbenchRowProjection.RowsAreAligned(scene, sceneDraft, out var rowAlignmentFailure))
            {
                Metrics?.RecordAlignmentFailure($"row:{sceneIndex}:{scene.SceneId}:{rowAlignmentFailure}");
                return false;
            }
        }

        for (var sceneIndex = 0; sceneIndex < scenes.Count; sceneIndex++)
        {
            var scene = scenes[sceneIndex];
            var sceneDraft = document.Scenes[sceneIndex];
            var changed = SetIfDifferent(scene.Order, sceneDraft.Order, value => scene.Order = value);
            var previewChanges = PreviewRefreshChanges.None;
            if (!ReferenceEquals(scene.AppliedDraft, sceneDraft))
            {
                changed |= ApplySceneFieldsIfChanged(scene, sceneDraft, out var scenePreviewChanged);
                previewChanges = previewChanges with { ScenePreviewChanged = scenePreviewChanged };
                _ = WorkbenchRowProjection.TryApplyRowsByPosition(scene, sceneDraft, out var rowsPreviewChanges, out var rowsChanged);
                changed |= rowsChanged;
                previewChanges = previewChanges.Merge(rowsPreviewChanges);
            }

            if (changed && previewChanges.Any)
            {
                scene.RefreshPreviewAssets(
                    previewChanges.ScenePreviewChanged,
                    previewChanges.FirstCharacterPreviewChanged,
                    forceScenePreviewNotifications: false);
            }
        }

        Metrics?.RecordAlignedApplySuccess();
        return true;
    }

    private static Dictionary<string, int> BuildSceneIndex(IReadOnlyList<SceneTableViewModel> scenes)
    {
        var indexById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < scenes.Count; index++)
        {
            indexById[scenes[index].SceneId] = index;
        }

        return indexById;
    }

    private static void RefreshSceneIndexes(
        IReadOnlyList<SceneTableViewModel> scenes,
        Dictionary<string, int> indexById,
        int startIndex)
    {
        for (var index = Math.Max(startIndex, 0); index < scenes.Count; index++)
        {
            indexById[scenes[index].SceneId] = index;
        }
    }

    private static bool ApplySceneFieldsIfChanged(
        SceneTableViewModel scene,
        WorkbenchSceneDraft sceneDraft,
        out bool previewChanged)
    {
        var changed = false;
        previewChanged = false;
        changed |= SetIfDifferent(scene.SceneName, sceneDraft.Name, value => scene.SceneName = value);
        changed |= SetIfDifferent(scene.SceneEffect, sceneDraft.Effect, value => scene.SceneEffect = value);
        previewChanged = SetIfDifferent(
            scene.SelectedSceneAsset,
            SceneAssetDisplayValue(sceneDraft),
            value => scene.SelectedSceneAsset = value);
        changed |= previewChanged;
        return changed;
    }

    private static string SceneAssetDisplayValue(WorkbenchSceneDraft sceneDraft)
    {
        var effectiveAssetId = VisualPreviewReference(sceneDraft.VisualBinding);
        if (!string.IsNullOrWhiteSpace(effectiveAssetId))
        {
            return effectiveAssetId;
        }

        return SceneTableViewModel.FromDraft(sceneDraft with { Rows = [] }).SelectedSceneAsset;
    }

    private static string? VisualPreviewReference(VisualAssetBinding? visualBinding)
    {
        if (visualBinding is { SourceKind: VisualSourceKind.Psd })
        {
            return visualBinding.RenderedAssetId;
        }

        return visualBinding?.EffectiveAssetId;
    }

    private static bool SetIfDifferent<T>(T current, T next, Action<T> apply)
    {
        if (EqualityComparer<T>.Default.Equals(current, next))
        {
            return false;
        }

        apply(next);
        return true;
    }
}

internal sealed class WorkbenchSceneProjectionMetrics
{
    public int AlignedApplyAttemptCount { get; private set; }

    public int AlignedApplySuccessCount { get; private set; }

    public int FallbackApplyDocumentCount { get; private set; }

    public string? LastAlignmentFailureReason { get; private set; }

    public string? LastFallbackReason { get; private set; }

    public void RecordAlignedApplyAttempt()
    {
        AlignedApplyAttemptCount++;
        LastAlignmentFailureReason = null;
    }

    public void RecordAlignedApplySuccess()
    {
        AlignedApplySuccessCount++;
        LastAlignmentFailureReason = null;
    }

    public void RecordFallbackApplyDocument(string? reason = null)
    {
        FallbackApplyDocumentCount++;
        LastFallbackReason = LastAlignmentFailureReason ?? reason;
    }

    public void RecordAlignmentFailure(string reason)
    {
        LastAlignmentFailureReason = reason;
    }
}
