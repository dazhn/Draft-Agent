using System;
using System.Collections.Generic;
using DraftAgent.Core;

namespace DraftAgent.App.ViewModels;

internal static class WorkbenchRowProjection
{
    public static bool ApplyRows(
        SceneTableViewModel scene,
        WorkbenchSceneDraft sceneDraft,
        WorkbenchRowProjectionIndex rowsById,
        out PreviewRefreshChanges previewChanges)
    {
        if (TryApplyRowsByPosition(scene, sceneDraft, out previewChanges, out var positionChanged))
        {
            return positionChanged;
        }

        var changed = false;
        var previewChangesAggregate = PreviewRefreshChanges.None;
        var desiredRows = new List<AnalysisResultRowViewModel>(sceneDraft.Rows.Count);
        var desiredRowIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rowDraft in sceneDraft.Rows)
        {
            if (!rowsById.TryGetValue(rowDraft.InteractionId, out var row))
            {
                row = AnalysisResultRowViewModel.FromDraft(rowDraft, sceneDraft.VisualBinding?.EffectiveAssetId);
                rowsById.AddOrUpdate(row);
                changed = true;
                previewChangesAggregate = previewChangesAggregate with
                {
                    ScenePreviewChanged = true,
                    FirstCharacterPreviewChanged = true
                };
            }

            if (!ReferenceEquals(row.AppliedDraft, rowDraft))
            {
                changed |= ApplyRowFieldsIfChanged(row, rowDraft, out var rowPreviewChanges);
                previewChangesAggregate = previewChangesAggregate.Merge(rowPreviewChanges);
                row.MarkAppliedDraft(rowDraft);
            }

            desiredRows.Add(row);
            desiredRowIds.Add(row.InteractionId);
        }

        for (var index = scene.Rows.Count - 1; index >= 0; index--)
        {
            if (!desiredRowIds.Contains(scene.Rows[index].InteractionId))
            {
                rowsById.AddOrUpdate(scene.Rows[index]);
                scene.Rows.RemoveAt(index);
                changed = true;
            }
        }

        var currentRowIndexById = BuildRowIndex(scene.Rows);
        for (var targetIndex = 0; targetIndex < desiredRows.Count; targetIndex++)
        {
            var row = desiredRows[targetIndex];
            if (!currentRowIndexById.TryGetValue(row.InteractionId, out var currentIndex))
            {
                scene.Rows.Insert(targetIndex, row);
                RefreshRowIndexes(scene.Rows, currentRowIndexById, targetIndex);
                changed = true;
            }
            else if (currentIndex != targetIndex)
            {
                scene.Rows.Move(currentIndex, targetIndex);
                RefreshRowIndexes(scene.Rows, currentRowIndexById, Math.Min(currentIndex, targetIndex));
                changed = true;
            }
        }

        scene.MarkAppliedDraft(sceneDraft);
        previewChanges = previewChangesAggregate;
        return changed;
    }

    public static bool TryApplyRowsByPosition(
        SceneTableViewModel scene,
        WorkbenchSceneDraft sceneDraft,
        out PreviewRefreshChanges previewChanges,
        out bool changed)
    {
        previewChanges = PreviewRefreshChanges.None;
        changed = false;
        if (!RowsAreAligned(scene, sceneDraft))
        {
            return false;
        }

        for (var index = 0; index < scene.Rows.Count; index++)
        {
            var row = scene.Rows[index];
            var rowDraft = sceneDraft.Rows[index];
            if (!ReferenceEquals(row.AppliedDraft, rowDraft))
            {
                changed |= ApplyRowFieldsIfChanged(row, rowDraft, out var rowPreviewChanges);
                previewChanges = previewChanges.Merge(rowPreviewChanges);
                row.MarkAppliedDraft(rowDraft);
            }
        }

        scene.MarkAppliedDraft(sceneDraft);
        return true;
    }

    public static bool RowsAreAligned(SceneTableViewModel scene, WorkbenchSceneDraft sceneDraft)
    {
        return RowsAreAligned(scene, sceneDraft, out _);
    }

    public static bool RowsAreAligned(
        SceneTableViewModel scene,
        WorkbenchSceneDraft sceneDraft,
        out string failureReason)
    {
        if (scene.Rows.Count != sceneDraft.Rows.Count)
        {
            failureReason = $"row_count:{scene.Rows.Count}!={sceneDraft.Rows.Count}";
            return false;
        }

        for (var rowIndex = 0; rowIndex < scene.Rows.Count; rowIndex++)
        {
            var rowId = scene.Rows[rowIndex].InteractionId;
            var draftRowId = sceneDraft.Rows[rowIndex].InteractionId;
            if (!string.Equals(rowId, draftRowId, StringComparison.Ordinal))
            {
                failureReason = $"row_id:{rowIndex}:{rowId}!={draftRowId}";
                return false;
            }
        }

        failureReason = string.Empty;
        return true;
    }

    public static void ApplyRowFields(AnalysisResultRowViewModel row, WorkbenchRowDraft rowDraft)
    {
        _ = ApplyRowFieldsIfChanged(row, rowDraft, out _);
        row.MarkAppliedDraft(rowDraft);
    }

    public static bool TryMarkAppliedRowsByPosition(SceneTableViewModel scene, WorkbenchSceneDraft sceneDraft)
    {
        if (!RowsAreAligned(scene, sceneDraft))
        {
            return false;
        }

        for (var index = 0; index < scene.Rows.Count; index++)
        {
            scene.Rows[index].MarkAppliedDraft(sceneDraft.Rows[index]);
        }

        return true;
    }

    public static void MarkAppliedRowsById(SceneTableViewModel scene, WorkbenchSceneDraft sceneDraft)
    {
        var rowsById = new Dictionary<string, WorkbenchRowDraft>(sceneDraft.Rows.Count, StringComparer.Ordinal);
        foreach (var rowDraft in sceneDraft.Rows)
        {
            rowsById[rowDraft.InteractionId] = rowDraft;
        }

        foreach (var row in scene.Rows)
        {
            if (rowsById.TryGetValue(row.InteractionId, out var rowDraft))
            {
                row.MarkAppliedDraft(rowDraft);
            }
        }
    }

    public static void MarkAppliedRowsByPositionRange(
        SceneTableViewModel scene,
        WorkbenchSceneDraft sceneDraft,
        int startRowIndex,
        int endRowIndexInclusive)
    {
        if (scene.Rows.Count == 0)
        {
            return;
        }

        var start = Math.Clamp(startRowIndex, 0, scene.Rows.Count - 1);
        var end = Math.Clamp(endRowIndexInclusive, 0, scene.Rows.Count - 1);
        if (start > end)
        {
            (start, end) = (end, start);
        }

        for (var index = start; index <= end; index++)
        {
            var row = scene.Rows[index];
            if (index < sceneDraft.Rows.Count &&
                string.Equals(sceneDraft.Rows[index].InteractionId, row.InteractionId, StringComparison.Ordinal))
            {
                row.MarkAppliedDraft(sceneDraft.Rows[index]);
            }
        }
    }

    private static Dictionary<string, int> BuildRowIndex(IReadOnlyList<AnalysisResultRowViewModel> rows)
    {
        var indexById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < rows.Count; index++)
        {
            indexById[rows[index].InteractionId] = index;
        }

        return indexById;
    }

    private static void RefreshRowIndexes(
        IReadOnlyList<AnalysisResultRowViewModel> rows,
        Dictionary<string, int> indexById,
        int startIndex)
    {
        for (var index = Math.Max(startIndex, 0); index < rows.Count; index++)
        {
            indexById[rows[index].InteractionId] = index;
        }
    }

    private static bool ApplyRowFieldsIfChanged(
        AnalysisResultRowViewModel row,
        WorkbenchRowDraft rowDraft,
        out PreviewRefreshChanges previewChanges)
    {
        var changed = false;
        previewChanges = PreviewRefreshChanges.None;
        changed |= SetIfDifferent(row.Order, rowDraft.Order, value => row.Order = value);
        changed |= row.ApplySubjectProjection(rowDraft.SubjectName, rowDraft.SubjectCharacterId);
        changed |= SetIfDifferent(row.LineText, rowDraft.Text, value => row.LineText = value);
        changed |= SetIfDifferent(row.Emotion, rowDraft.Emotion, value => row.Emotion = value);
        var characterAssetChanged = SetIfDifferent(
            row.SelectedCharacterAsset,
            CharacterAssetDisplayValue(row, rowDraft),
            value => row.SelectedCharacterAsset = value);
        changed |= characterAssetChanged;
        if (characterAssetChanged)
        {
            previewChanges = previewChanges with
            {
                ScenePreviewChanged = true,
                FirstCharacterPreviewChanged = true
            };
        }

        changed |= SetIfDifferent(
            row.SelectedVoiceReference,
            VoiceReferenceDisplayValue(row, rowDraft),
            value => row.SelectedVoiceReference = value);
        changed |= SetIfDifferent(row.SelectedVoiceReferenceText, rowDraft.VoiceBinding?.EffectiveReferenceText ?? string.Empty, value => row.SelectedVoiceReferenceText = value);
        changed |= SetIfDifferent(row.EmotionAlpha, rowDraft.EmotionAlpha ?? row.EmotionAlpha, value => row.EmotionAlpha = value);
        changed |= row.ApplyTaskLinks(rowDraft.TaskLinks);
        changed |= ApplyEmotionVectorsIfChanged(row, rowDraft.EmotionVectors);
        return changed;
    }

    private static bool ApplyEmotionVectorsIfChanged(
        AnalysisResultRowViewModel row,
        IReadOnlyDictionary<string, double>? emotionVectors)
    {
        if (emotionVectors is null)
        {
            return false;
        }

        var changed = false;
        foreach (var vector in row.EmotionVectors)
        {
            if (emotionVectors.TryGetValue(vector.Key, out var value))
            {
                changed |= SetIfDifferent(vector.Value, value, next => vector.Value = next);
            }
        }

        return changed;
    }

    private static string CharacterAssetDisplayValue(AnalysisResultRowViewModel row, WorkbenchRowDraft rowDraft)
    {
        var effectiveAssetId = VisualPreviewReference(rowDraft.CharacterVisualBinding);
        if (!string.IsNullOrWhiteSpace(effectiveAssetId))
        {
            return effectiveAssetId;
        }

        return AnalysisResultRowViewModel.FromDraft(rowDraft, null).SelectedCharacterAsset;
    }

    private static string? VisualPreviewReference(VisualAssetBinding? visualBinding)
    {
        if (visualBinding is { SourceKind: VisualSourceKind.Psd })
        {
            return visualBinding.RenderedAssetId;
        }

        return visualBinding?.EffectiveAssetId;
    }

    private static string VoiceReferenceDisplayValue(AnalysisResultRowViewModel row, WorkbenchRowDraft rowDraft)
    {
        var effectiveVoiceReferenceId = rowDraft.VoiceBinding?.EffectiveVoiceReferenceId;
        if (!string.IsNullOrWhiteSpace(effectiveVoiceReferenceId))
        {
            return effectiveVoiceReferenceId;
        }

        return AnalysisResultRowViewModel.FromDraft(rowDraft, null).SelectedVoiceReference;
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

internal readonly record struct PreviewRefreshChanges(
    bool ScenePreviewChanged,
    bool FirstCharacterPreviewChanged)
{
    public static PreviewRefreshChanges None => new(false, false);

    public bool Any => ScenePreviewChanged || FirstCharacterPreviewChanged;

    public PreviewRefreshChanges Merge(PreviewRefreshChanges other)
    {
        return new PreviewRefreshChanges(
            ScenePreviewChanged || other.ScenePreviewChanged,
            FirstCharacterPreviewChanged || other.FirstCharacterPreviewChanged);
    }
}
