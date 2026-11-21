using System.Collections.Generic;
using System;
using System.Linq;
using DraftAgent.Core;

namespace DraftAgent.App.ViewModels;

public static class BindingTargetWorkspaceApplier
{
    private static readonly WorkbenchCommandService CommandService = new();

    public static WorkbenchDocument Apply(
        BindingTargetViewModel target,
        IEnumerable<SceneTableViewModel> scenes,
        WorkbenchSession session)
    {
        return ApplyWithResult(target, scenes, BindingTargetRowIndex.FromScenes(scenes), session).CommandResult.Document;
    }

    internal static WorkbenchDocument Apply(
        BindingTargetViewModel target,
        IEnumerable<SceneTableViewModel> scenes,
        BindingTargetRowIndex rowIndex,
        WorkbenchSession session)
    {
        return ApplyWithResult(target, scenes, rowIndex, session).CommandResult.Document;
    }

    internal static WorkbenchBindingTargetApplyResult ApplyWithResult(
        BindingTargetViewModel target,
        IEnumerable<SceneTableViewModel> scenes,
        BindingTargetRowIndex rowIndex,
        WorkbenchSession session)
    {
        if (!target.HasAnyBindingEdit)
        {
            return new WorkbenchBindingTargetApplyResult(
                WorkbenchCommandResult.NoOp(session.Document),
                WorkbenchDerivedStateRefresh.SessionOnly);
        }

        var previousDocument = session.Document;
        var updatedDocument = session.Execute(document => target.Kind switch
        {
            BindingTargetKind.Scene => ApplySceneBindingCommand(document, target),
            BindingTargetKind.Character => ApplyCharacterBindingCommand(document, target),
            BindingTargetKind.Narration => ApplyNarrationBindingCommand(document, target),
            _ => document
        },
        WorkbenchSessionOperationNames.ApplyBindingTarget);

        var changed = !ReferenceEquals(updatedDocument, previousDocument);
        if (!changed)
        {
            return new WorkbenchBindingTargetApplyResult(
                WorkbenchCommandResult.NoOp(updatedDocument),
                WorkbenchDerivedStateRefresh.SessionOnly);
        }

        IReadOnlyList<string> changedSceneIds = [];
        IReadOnlyList<string> changedRowIds = [];
        switch (target.Kind)
        {
            case BindingTargetKind.Scene:
                ApplySceneBinding(target, scenes, updatedDocument);
                changedSceneIds = [target.TargetId];
                break;

            case BindingTargetKind.Character:
                var characterRows = CharacterRowsToRefresh(target, rowIndex);
                ApplyCharacterBinding(target, characterRows, updatedDocument);
                changedSceneIds = ChangedSceneIdsForRows(characterRows);
                changedRowIds = ChangedRowIdsForRows(characterRows);
                break;

            case BindingTargetKind.Narration:
                var narrationRows = rowIndex.RowsForTarget(target);
                ApplyNarrationBinding(target, narrationRows, updatedDocument);
                changedSceneIds = ChangedSceneIdsForRows(narrationRows);
                changedRowIds = ChangedRowIdsForRows(narrationRows);
                break;
        }

        target.AcceptBindingEdits();

        return new WorkbenchBindingTargetApplyResult(
            WorkbenchCommandResult.AppliedResult(
                updatedDocument,
                changedSceneIds: changedSceneIds,
                changedRowIds: changedRowIds),
            WorkbenchDerivedStateRefresh.SessionOnly);
    }

    public static IReadOnlyList<AnalysisResultRowViewModel> RowsForTarget(
        BindingTargetViewModel target,
        IEnumerable<SceneTableViewModel> scenes)
    {
        return BindingTargetRowIndex.FromScenes(scenes).RowsForTarget(target);
    }

    private static VoiceAssetBinding BuildVoiceBinding(BindingTargetViewModel target)
    {
        return VoiceAssetBinding.FromProjectReference(
            WorkbenchBindingValue.Asset(target.VoiceAsset),
            WorkbenchBindingValue.Text(target.EffectiveVoiceReferenceTextForBinding()),
            WorkbenchBindingValue.Asset(target.VoiceSourceAsset),
            WorkbenchBindingValue.Asset(target.VoiceSyncedPath));
    }

    private static WorkbenchDocument ApplySceneBindingCommand(
        WorkbenchDocument document,
        BindingTargetViewModel target)
    {
        return target.HasImageBindingEdit
            ? CommandService.ApplySceneBinding(
                document,
                target.TargetId,
                WorkbenchBindingValue.Asset(target.ImageAsset))
            : document;
    }

    private static WorkbenchDocument ApplyCharacterBindingCommand(
        WorkbenchDocument document,
        BindingTargetViewModel target)
    {
        if (target.HasImageBindingEdit && target.HasVoiceBindingEdit)
        {
            var withVisual = CommandService.ApplyCharacterVisualBinding(
                document,
                target.TargetId,
                target.DisplayName,
                VisualAssetBinding.FromImage(WorkbenchBindingValue.Asset(target.ImageAsset)));
            return CommandService.ApplyCharacterVoiceBinding(
                withVisual,
                target.TargetId,
                target.DisplayName,
                BuildVoiceBinding(target));
        }

        if (target.HasImageBindingEdit)
        {
            return CommandService.ApplyCharacterVisualBinding(
                document,
                target.TargetId,
                target.DisplayName,
                VisualAssetBinding.FromImage(WorkbenchBindingValue.Asset(target.ImageAsset)));
        }

        if (target.HasVoiceBindingEdit)
        {
            return CommandService.ApplyCharacterVoiceBinding(
                document,
                target.TargetId,
                target.DisplayName,
                BuildVoiceBinding(target));
        }

        return document;
    }

    private static WorkbenchDocument ApplyNarrationBindingCommand(
        WorkbenchDocument document,
        BindingTargetViewModel target)
    {
        return target.HasVoiceBindingEdit
            ? CommandService.ApplyNarrationBinding(
                document,
                WorkbenchBindingValue.Asset(target.VoiceAsset),
                WorkbenchBindingValue.Text(target.EffectiveVoiceReferenceTextForBinding()),
                BuildVoiceBinding(target))
            : document;
    }

    private static void ApplySceneBinding(
        BindingTargetViewModel target,
        IEnumerable<SceneTableViewModel> scenes,
        WorkbenchDocument document)
    {
        foreach (var scene in scenes)
        {
            if (!string.Equals(scene.SceneId, target.TargetId, System.StringComparison.Ordinal))
            {
                continue;
            }

            var sceneDraft = document.TryGetSceneDraft(scene.SceneId, out var draft) ? draft : null;
            scene.SelectedSceneAsset = sceneDraft?.VisualBinding?.EffectiveAssetId ?? target.ImageAsset;
            scene.SelectedScenePreviewAsset = target.ImagePreviewSource;
            foreach (var row in scene.Rows)
            {
                row.ApplyScenePreview(target.ImagePreviewText, target.ImagePreviewSource);
            }

            scene.RefreshPreviewAssets(scenePreviewChanged: true, firstCharacterPreviewChanged: false);
            if (sceneDraft is not null)
            {
                WorkbenchViewModelAdapter.MarkAppliedDrafts(scene, sceneDraft);
            }
        }
    }

    private static void ApplyCharacterBinding(
        BindingTargetViewModel target,
        IReadOnlyList<AnalysisResultRowViewModel> rows,
        WorkbenchDocument document)
    {
        var affectedScenes = new HashSet<SceneTableViewModel>();
        foreach (var row in rows)
        {
            var rowDraft = document.RowsById.TryGetValue(row.InteractionId, out var draft) ? draft : null;
            if (rowDraft is { Kind: WorkbenchRowKind.Narration } &&
                (!string.IsNullOrWhiteSpace(rowDraft.VoiceRole) ||
                 !string.IsNullOrWhiteSpace(rowDraft.VoiceRoleCharacterId)))
            {
                row.ApplyVoiceBindingProjection(
                    rowDraft.VoiceBinding,
                    rowDraft.VoiceBinding?.EffectiveVoiceReferenceId ?? target.VoiceAsset,
                    rowDraft.VoiceBinding?.EffectiveReferenceText ?? target.EffectiveVoiceReferenceTextForBinding(),
                    target.VoicePreviewText);
            }
            else
            {
                row.ApplyCharacterBindingProjection(
                    rowDraft?.CharacterVisualBinding?.EffectiveAssetId ?? target.ImageAsset,
                    target.ImagePreviewSource,
                    rowDraft?.VoiceBinding?.EffectiveVoiceReferenceId ?? row.SelectedVoiceReference,
                    rowDraft?.VoiceBinding?.EffectiveReferenceText ?? row.SelectedVoiceReferenceText,
                    target.ImagePreviewText,
                    target.ImagePreviewSource,
                    rowDraft?.VoiceBinding?.EffectiveVoiceReferenceId ?? row.VoiceAssetText);
            }
            if (rowDraft is not null)
            {
                row.MarkAppliedDraft(rowDraft);
            }

            if (row.OwnerScene is { } scene)
            {
                affectedScenes.Add(scene);
            }
        }

        foreach (var scene in affectedScenes)
        {
            scene.RefreshPreviewAssets(scenePreviewChanged: false, firstCharacterPreviewChanged: true);
            WorkbenchViewModelAdapter.MarkAppliedDrafts(scene, document);
        }
    }

    private static void ApplyNarrationBinding(
        BindingTargetViewModel target,
        IReadOnlyList<AnalysisResultRowViewModel> rows,
        WorkbenchDocument document)
    {
        var affectedScenes = new HashSet<SceneTableViewModel>();
        foreach (var row in rows)
        {
            var rowDraft = document.RowsById.TryGetValue(row.InteractionId, out var draft) ? draft : null;
            row.SelectedVoiceReference = rowDraft?.VoiceBinding?.EffectiveVoiceReferenceId ?? target.VoiceAsset;
            row.SelectedVoiceReferenceText = rowDraft?.VoiceBinding?.EffectiveReferenceText ?? target.EffectiveVoiceReferenceTextForBinding();
            row.VoiceAssetText = target.VoicePreviewText;
            if (rowDraft is not null)
            {
                row.MarkAppliedDraft(rowDraft);
            }

            if (row.OwnerScene is { } scene)
            {
                affectedScenes.Add(scene);
            }
        }

        foreach (var scene in affectedScenes)
        {
            WorkbenchViewModelAdapter.MarkAppliedDrafts(scene, document);
        }
    }

    private static IReadOnlyList<string> ChangedSceneIdsForRows(IReadOnlyList<AnalysisResultRowViewModel> rows)
    {
        return rows
            .Select(row => row.OwnerScene?.SceneId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Cast<string>()
            .ToArray();
    }

    private static IReadOnlyList<AnalysisResultRowViewModel> CharacterRowsToRefresh(
        BindingTargetViewModel target,
        BindingTargetRowIndex rowIndex)
    {
        var rows = new List<AnalysisResultRowViewModel>();
        var seenRows = new HashSet<AnalysisResultRowViewModel>(ReferenceEqualityComparer.Instance);
        if (target.HasImageBindingEdit)
        {
            foreach (var row in rowIndex.RowsForTarget(target))
            {
                if (seenRows.Add(row))
                {
                    rows.Add(row);
                }
            }
        }

        if (target.HasVoiceBindingEdit)
        {
            foreach (var row in rowIndex.VoiceRowsForTarget(target))
            {
                if (seenRows.Add(row))
                {
                    rows.Add(row);
                }
            }
        }

        return rows.Count == 0 ? [] : rows.ToArray();
    }

    private static IReadOnlyList<string> ChangedRowIdsForRows(IReadOnlyList<AnalysisResultRowViewModel> rows)
    {
        return rows
            .Select(row => row.InteractionId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

}
