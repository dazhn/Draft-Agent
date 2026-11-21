using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using DraftAgent.Core;

namespace DraftAgent.App.ViewModels;

public sealed class WorkbenchDraftService
{
    private readonly WorkbenchCommandService _commandService = new();

    public SceneTableViewModel AddScene(ObservableCollection<SceneTableViewModel> scenes, WorkbenchSession session)
    {
        return AddSceneWithResult(scenes, session).Scene!;
    }

    internal WorkbenchSceneMutationResult AddSceneWithResult(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session)
    {
        var command = AddSceneCommand.FromSceneCollection(scenes);
        var scene = command.Scene;
        var updated = session.Execute(document => _commandService.AddSceneUnchecked(
            document,
            scene.SceneId,
            scene.SceneName,
            scene.SceneEffect,
            WorkbenchBindingValue.Asset(scene.SelectedSceneAsset)),
            WorkbenchSessionOperationNames.AddScene);
        ApplyLocalSceneAdd(scenes, updated, scene, command.TargetIndex);
        return new WorkbenchSceneMutationResult(
            WorkbenchCommandResult.AppliedResult(updated, changedSceneIds: [scene.SceneId]),
            scene,
            bindingTargetRefresh: WorkbenchBindingTargetRefresh.InsertScene(scene));
    }

    public AnalysisResultRowViewModel AddDialogueRow(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        SceneTableViewModel scene,
        AnalysisResultRowViewModel? selectedRow,
        string characterName)
    {
        return AddDialogueRowWithResult(scenes, session, scene, selectedRow, characterName).Row!;
    }

    internal WorkbenchRowMutationResult AddDialogueRowWithResult(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        SceneTableViewModel scene,
        AnalysisResultRowViewModel? selectedRow,
        string characterName)
    {
        var sceneIndex = IndexOfScene(scenes, scene);
        if (sceneIndex < 0)
        {
            return new WorkbenchRowMutationResult(
                WorkbenchCommandResult.NoOp(session.Document),
                AnalysisResultRowViewModel.CreateManualDialogue(1, characterName));
        }

        var command = AddRowCommand.ForDialogue(scene, sceneIndex, selectedRow, characterName);
        var row = command.Row;
        var updated = session.Execute(document => _commandService.AddRowToKnownScene(
            document,
            command.SceneId,
            command.SceneIndex,
            WorkbenchViewModelAdapter.BuildRowDraft(row, command.SceneId),
            command.TargetIndex),
            WorkbenchSessionOperationNames.AddRow);
        ApplyLocalRowAdd(scenes, updated, scene, command.SceneIndex, row, command.TargetIndex);
        return new WorkbenchRowMutationResult(
            WorkbenchCommandResult.AppliedResult(
                updated,
                changedSceneIds: [command.SceneId],
                changedRowIds: [row.InteractionId],
                suggestedSelectedRow: row),
            row,
            TargetScene: scene,
            suggestedSelectedRow: row,
            bindingTargetRefresh: WorkbenchBindingTargetRefresh.ForRow(row));
    }

    public AnalysisResultRowViewModel AddNarrationRow(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        SceneTableViewModel scene,
        AnalysisResultRowViewModel? selectedRow)
    {
        return AddNarrationRowWithResult(scenes, session, scene, selectedRow).Row!;
    }

    internal WorkbenchRowMutationResult AddNarrationRowWithResult(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        SceneTableViewModel scene,
        AnalysisResultRowViewModel? selectedRow)
    {
        var sceneIndex = IndexOfScene(scenes, scene);
        if (sceneIndex < 0)
        {
            return new WorkbenchRowMutationResult(
                WorkbenchCommandResult.NoOp(session.Document),
                AnalysisResultRowViewModel.CreateManualNarration(1));
        }

        var command = AddRowCommand.ForNarration(scene, sceneIndex, selectedRow);
        var row = command.Row;
        var updated = session.Execute(document => _commandService.AddRowToKnownScene(
            document,
            command.SceneId,
            command.SceneIndex,
            WorkbenchViewModelAdapter.BuildRowDraft(row, command.SceneId),
            command.TargetIndex),
            WorkbenchSessionOperationNames.AddRow);
        ApplyLocalRowAdd(scenes, updated, scene, command.SceneIndex, row, command.TargetIndex);
        return new WorkbenchRowMutationResult(
            WorkbenchCommandResult.AppliedResult(
                updated,
                changedSceneIds: [command.SceneId],
                changedRowIds: [row.InteractionId],
                suggestedSelectedRow: row),
            row,
            TargetScene: scene,
            suggestedSelectedRow: row,
            bindingTargetRefresh: WorkbenchBindingTargetRefresh.ForRow(row));
    }

    public bool RemoveScene(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        SceneTableViewModel scene)
    {
        return RemoveSceneWithResult(scenes, session, scene).CommandResult.Applied;
    }

    internal WorkbenchSceneMutationResult RemoveSceneWithResult(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        SceneTableViewModel scene)
    {
        var sourceIndex = IndexOfScene(scenes, scene);
        if (sourceIndex < 0)
        {
            return new WorkbenchSceneMutationResult(WorkbenchCommandResult.NoOp(session.Document), scene);
        }

        var command = RemoveSceneCommand.FromResolvedLocation(scene, sourceIndex);
        var removedRows = session.Document.TryGetSceneDraft(command.SceneId, out var removedSceneDraft)
            ? removedSceneDraft.Rows.ToArray()
            : scene.Rows
                .Select(row => WorkbenchViewModelAdapter.BuildRowDraft(row, command.SceneId))
                .ToArray();
        var removedRowIds = removedRows.Select(row => row.InteractionId).ToArray();
        var updated = session.Execute(document => _commandService.RemoveSceneFromKnownLocation(
            document,
            command.SceneId,
            command.SourceIndex),
            WorkbenchSessionOperationNames.RemoveScene);
        ApplyLocalSceneRemove(scenes, updated, scene, command.SourceIndex);
        return new WorkbenchSceneMutationResult(
            WorkbenchCommandResult.AppliedResult(
                updated,
                removedSceneIds: [command.SceneId],
                removedRowIds: removedRowIds),
            scene,
            removedRows: removedRows,
            bindingTargetRefresh: WorkbenchBindingTargetRefresh.RemoveScene(scene, removedRows));
    }

    public bool RemoveRow(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        AnalysisResultRowViewModel row,
        out SceneTableViewModel? sourceScene)
    {
        var result = RemoveRowWithResult(scenes, session, row);
        sourceScene = result.SourceScene;
        return result.CommandResult.Applied;
    }

    internal WorkbenchRowMutationResult RemoveRowWithResult(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        AnalysisResultRowViewModel row)
    {
        if (!TryFindRowLocation(scenes, row, out var sourceScene, out var sceneIndex, out var rowIndex))
        {
            return new WorkbenchRowMutationResult(
                WorkbenchCommandResult.NoOp(session.Document),
                row);
        }

        var command = RemoveRowCommand.FromResolvedLocation(row, sourceScene, sceneIndex, rowIndex);
        var removedRowDraft = session.Document.RowsById.TryGetValue(command.InteractionId, out var draft)
            ? draft
            : WorkbenchViewModelAdapter.BuildRowDraft(row, command.SourceSceneId);
        var updated = session.Execute(document => _commandService.RemoveRowFromKnownLocation(
            document,
            command.InteractionId,
            command.SourceSceneId,
            command.SourceSceneIndex,
            command.SourceRowIndex),
            WorkbenchSessionOperationNames.RemoveRow);
        ApplyLocalRowRemove(scenes, updated, row, sourceScene, command.SourceSceneIndex, command.SourceRowIndex);
        return new WorkbenchRowMutationResult(
            WorkbenchCommandResult.AppliedResult(
                updated,
                changedSceneIds: [command.SourceSceneId],
                removedRowIds: [command.InteractionId]),
            row,
            SourceScene: sourceScene,
            removedRow: removedRowDraft,
            bindingTargetRefresh: WorkbenchBindingTargetRefresh.ForRowSubjectChange(row, removedRowDraft));
    }

    public bool MoveRowBeforeTarget(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        AnalysisResultRowViewModel draggedRow,
        AnalysisResultRowViewModel targetRow,
        out SceneTableViewModel? targetScene,
        bool preview = false)
    {
        var result = MoveRowBeforeTargetWithResult(scenes, session, draggedRow, targetRow, preview);
        targetScene = result.TargetScene;
        return result.CommandResult.Applied;
    }

    internal WorkbenchRowMutationResult MoveRowBeforeTargetWithResult(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        AnalysisResultRowViewModel draggedRow,
        AnalysisResultRowViewModel targetRow,
        bool preview = false)
    {
        if (ReferenceEquals(draggedRow, targetRow))
        {
            return new WorkbenchRowMutationResult(WorkbenchCommandResult.NoOp(session.Document), draggedRow);
        }

        if (!TryFindRowLocation(scenes, draggedRow, out var sourceScene, out var sourceSceneIndex, out var sourceIndex) ||
            !TryFindRowLocation(scenes, targetRow, out var resolvedTargetScene, out var targetSceneIndex, out var targetIndex))
        {
            return new WorkbenchRowMutationResult(WorkbenchCommandResult.NoOp(session.Document), draggedRow);
        }

        var command = MoveRowCommand.FromResolvedLocation(
            draggedRow,
            sourceScene,
            sourceSceneIndex,
            sourceIndex,
            resolvedTargetScene,
            targetSceneIndex,
            targetIndex);
        var updated = ExecuteMove(session, document => _commandService.MoveRowFromKnownLocation(
            document,
            command.InteractionId,
            command.SourceSceneId,
            command.SourceSceneIndex,
            command.SourceRowIndex,
            command.TargetSceneId,
            command.TargetSceneIndex,
            command.TargetIndex),
            preview,
            WorkbenchSessionOperationNames.MoveRow);
        var applied = ApplyLocalRowMove(
            scenes,
            updated,
            draggedRow,
            sourceScene,
            sourceSceneIndex,
            resolvedTargetScene,
            targetSceneIndex,
            sourceIndex,
            targetIndex,
            updateOrderFields: !preview);
        var commandResult = applied
            ? WorkbenchCommandResult.AppliedResult(
                updated,
                changedSceneIds: ChangedSceneIdsForMove(command.SourceSceneId, command.TargetSceneId),
                changedRowIds: ChangedRowIdsForRowMove(sourceScene, resolvedTargetScene, sourceIndex, command.TargetIndex),
                suggestedSelectedRow: draggedRow)
            : WorkbenchCommandResult.NoOp(updated);
        return new WorkbenchRowMutationResult(
            commandResult,
            draggedRow,
            sourceScene,
            resolvedTargetScene,
            suggestedSelectedRow: applied ? draggedRow : null,
            bindingTargetRefresh: RowMoveBindingTargetRefresh(scenes, commandResult, preview),
            derivedStateRefresh: WorkbenchDerivedStateRefresh.SessionOnly);
    }

    public bool MoveRowToTarget(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        AnalysisResultRowViewModel draggedRow,
        AnalysisResultRowViewModel targetRow,
        WorkbenchDropPosition position,
        out SceneTableViewModel? targetScene,
        bool preview = false)
    {
        var result = MoveRowToTargetWithResult(scenes, session, draggedRow, targetRow, position, preview);
        targetScene = result.TargetScene;
        return result.CommandResult.Applied;
    }

    internal WorkbenchRowMutationResult MoveRowToTargetWithResult(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        AnalysisResultRowViewModel draggedRow,
        AnalysisResultRowViewModel targetRow,
        WorkbenchDropPosition position,
        bool preview = false)
    {
        if (ReferenceEquals(draggedRow, targetRow))
        {
            return new WorkbenchRowMutationResult(WorkbenchCommandResult.NoOp(session.Document), draggedRow);
        }

        if (!TryFindRowLocation(scenes, draggedRow, out var sourceScene, out var sourceSceneIndex, out var sourceIndex) ||
            !TryFindRowLocation(scenes, targetRow, out var resolvedTargetScene, out var targetSceneIndex, out var targetIndex))
        {
            return new WorkbenchRowMutationResult(WorkbenchCommandResult.NoOp(session.Document), draggedRow);
        }
        if (IsSameRowDropPosition(sourceScene, resolvedTargetScene, sourceIndex, targetIndex, position))
        {
            return new WorkbenchRowMutationResult(WorkbenchCommandResult.NoOp(session.Document), draggedRow);
        }

        var insertIndex = ResolveRowInsertIndex(sourceScene, resolvedTargetScene, sourceIndex, targetIndex, position);
        var command = MoveRowCommand.FromResolvedLocation(
            draggedRow,
            sourceScene,
            sourceSceneIndex,
            sourceIndex,
            resolvedTargetScene,
            targetSceneIndex,
            insertIndex);
        var updated = ExecuteMove(session, document => _commandService.MoveRowFromKnownLocation(
            document,
            command.InteractionId,
            command.SourceSceneId,
            command.SourceSceneIndex,
            command.SourceRowIndex,
            command.TargetSceneId,
            command.TargetSceneIndex,
            command.TargetIndex),
            preview,
            WorkbenchSessionOperationNames.MoveRow);
        var applied = ApplyLocalRowMove(
            scenes,
            updated,
            draggedRow,
            sourceScene,
            sourceSceneIndex,
            resolvedTargetScene,
            targetSceneIndex,
            sourceIndex,
            insertIndex,
            updateOrderFields: !preview);
        var commandResult = applied
            ? WorkbenchCommandResult.AppliedResult(
                updated,
                changedSceneIds: ChangedSceneIdsForMove(command.SourceSceneId, command.TargetSceneId),
                changedRowIds: ChangedRowIdsForRowMove(sourceScene, resolvedTargetScene, sourceIndex, command.TargetIndex),
                suggestedSelectedRow: draggedRow)
            : WorkbenchCommandResult.NoOp(updated);
        return new WorkbenchRowMutationResult(
            commandResult,
            draggedRow,
            sourceScene,
            resolvedTargetScene,
            suggestedSelectedRow: applied ? draggedRow : null,
            bindingTargetRefresh: RowMoveBindingTargetRefresh(scenes, commandResult, preview),
            derivedStateRefresh: WorkbenchDerivedStateRefresh.SessionOnly);
    }

    public bool MoveRowToSceneEnd(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        AnalysisResultRowViewModel draggedRow,
        SceneTableViewModel targetScene,
        bool preview = false)
    {
        return MoveRowToSceneEndWithResult(scenes, session, draggedRow, targetScene, preview).CommandResult.Applied;
    }

    internal WorkbenchRowMutationResult MoveRowToSceneEndWithResult(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        AnalysisResultRowViewModel draggedRow,
        SceneTableViewModel targetScene,
        bool preview = false)
    {
        if (!TryFindRowLocation(scenes, draggedRow, out var sourceScene, out var sourceSceneIndex, out var sourceIndex))
        {
            return new WorkbenchRowMutationResult(WorkbenchCommandResult.NoOp(session.Document), draggedRow);
        }

        var targetSceneIndex = IndexOfScene(scenes, targetScene);
        if (targetSceneIndex < 0)
        {
            return new WorkbenchRowMutationResult(WorkbenchCommandResult.NoOp(session.Document), draggedRow);
        }

        if (ReferenceEquals(sourceScene, targetScene) &&
            sourceIndex == targetScene.Rows.Count - 1)
        {
            return new WorkbenchRowMutationResult(WorkbenchCommandResult.NoOp(session.Document), draggedRow);
        }

        var command = MoveRowCommand.FromResolvedLocation(
            draggedRow,
            sourceScene,
            sourceSceneIndex,
            sourceIndex,
            targetScene,
            targetSceneIndex,
            targetScene.Rows.Count);
        var updated = ExecuteMove(session, document => _commandService.MoveRowFromKnownLocation(
            document,
            command.InteractionId,
            command.SourceSceneId,
            command.SourceSceneIndex,
            command.SourceRowIndex,
            command.TargetSceneId,
            command.TargetSceneIndex,
            command.TargetIndex),
            preview,
            WorkbenchSessionOperationNames.MoveRow);
        var applied = ApplyLocalRowMove(
            scenes,
            updated,
            draggedRow,
            sourceScene,
            sourceSceneIndex,
            targetScene,
            targetSceneIndex,
            sourceIndex,
            targetScene.Rows.Count,
            updateOrderFields: !preview);
        var commandResult = applied
            ? WorkbenchCommandResult.AppliedResult(
                updated,
                changedSceneIds: ChangedSceneIdsForMove(command.SourceSceneId, command.TargetSceneId),
                changedRowIds: ChangedRowIdsForRowMove(sourceScene, targetScene, sourceIndex, command.TargetIndex),
                suggestedSelectedRow: draggedRow)
            : WorkbenchCommandResult.NoOp(updated);
        return new WorkbenchRowMutationResult(
            commandResult,
            draggedRow,
            sourceScene,
            targetScene,
            suggestedSelectedRow: applied ? draggedRow : null,
            bindingTargetRefresh: RowMoveBindingTargetRefresh(scenes, commandResult, preview),
            derivedStateRefresh: WorkbenchDerivedStateRefresh.SessionOnly);
    }

    public bool MoveScene(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        SceneTableViewModel draggedScene,
        SceneTableViewModel targetScene,
        bool preview = false)
    {
        return MoveSceneWithResult(scenes, session, draggedScene, targetScene, preview).CommandResult.Applied;
    }

    internal WorkbenchSceneMutationResult MoveSceneWithResult(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        SceneTableViewModel draggedScene,
        SceneTableViewModel targetScene,
        bool preview = false)
    {
        if (ReferenceEquals(draggedScene, targetScene))
        {
            return new WorkbenchSceneMutationResult(WorkbenchCommandResult.NoOp(session.Document), draggedScene);
        }

        var oldIndex = IndexOfScene(scenes, draggedScene);
        var targetIndex = IndexOfScene(scenes, targetScene);
        if (oldIndex < 0 || targetIndex < 0 || oldIndex == targetIndex)
        {
            return new WorkbenchSceneMutationResult(WorkbenchCommandResult.NoOp(session.Document), draggedScene);
        }

        var command = MoveSceneCommand.FromResolvedLocation(draggedScene, oldIndex, targetIndex);
        var updated = ExecuteMove(
            session,
            document => _commandService.MoveSceneFromKnownLocation(
                document,
                command.SceneId,
                command.SourceIndex,
                command.TargetIndex),
            preview,
            WorkbenchSessionOperationNames.MoveScene);
        var applied = ApplyLocalSceneMove(scenes, updated, draggedScene, targetIndex, updateOrderFields: !preview);
        var changedSceneIds = applied
            ? ChangedSceneIdsForSceneMove(scenes, oldIndex, targetIndex)
            : [];
        return new WorkbenchSceneMutationResult(
            applied
                ? WorkbenchCommandResult.AppliedResult(
                    updated,
                    changedSceneIds: changedSceneIds)
                : WorkbenchCommandResult.NoOp(updated),
            draggedScene,
            bindingTargetRefresh: SceneMoveBindingTargetRefresh(scenes, changedSceneIds, applied, preview),
            derivedStateRefresh: WorkbenchDerivedStateRefresh.SessionOnly);
    }

    public bool MoveSceneToTarget(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        SceneTableViewModel draggedScene,
        SceneTableViewModel targetScene,
        WorkbenchDropPosition position,
        bool preview = false)
    {
        return MoveSceneToTargetWithResult(scenes, session, draggedScene, targetScene, position, preview).CommandResult.Applied;
    }

    internal WorkbenchSceneMutationResult MoveSceneToTargetWithResult(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        SceneTableViewModel draggedScene,
        SceneTableViewModel targetScene,
        WorkbenchDropPosition position,
        bool preview = false)
    {
        if (ReferenceEquals(draggedScene, targetScene))
        {
            return new WorkbenchSceneMutationResult(WorkbenchCommandResult.NoOp(session.Document), draggedScene);
        }

        var oldIndex = IndexOfScene(scenes, draggedScene);
        var targetIndex = IndexOfScene(scenes, targetScene);
        if (oldIndex < 0 || targetIndex < 0 ||
            IsSameListDropPosition(oldIndex, targetIndex, position))
        {
            return new WorkbenchSceneMutationResult(WorkbenchCommandResult.NoOp(session.Document), draggedScene);
        }

        var insertIndex = ResolveSameListInsertIndex(oldIndex, targetIndex, position);
        var command = MoveSceneCommand.FromResolvedLocation(draggedScene, oldIndex, insertIndex);
        var updated = ExecuteMove(
            session,
            document => _commandService.MoveSceneFromKnownLocation(
                document,
                command.SceneId,
                command.SourceIndex,
                command.TargetIndex),
            preview,
            WorkbenchSessionOperationNames.MoveScene);
        var applied = ApplyLocalSceneMove(scenes, updated, draggedScene, insertIndex, updateOrderFields: !preview);
        var changedSceneIds = applied
            ? ChangedSceneIdsForSceneMove(scenes, oldIndex, insertIndex)
            : [];
        return new WorkbenchSceneMutationResult(
            applied
                ? WorkbenchCommandResult.AppliedResult(
                    updated,
                    changedSceneIds: changedSceneIds)
                : WorkbenchCommandResult.NoOp(updated),
            draggedScene,
            bindingTargetRefresh: SceneMoveBindingTargetRefresh(scenes, changedSceneIds, applied, preview),
            derivedStateRefresh: WorkbenchDerivedStateRefresh.SessionOnly);
    }

    public bool UpdateSceneText(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        SceneTableViewModel scene)
    {
        return UpdateSceneTextWithResult(scenes, session, scene, UpdateSceneTextCommand.FromScene(scene)).CommandResult.Applied;
    }

    internal bool UpdateSceneText(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        SceneTableViewModel scene,
        UpdateSceneTextCommand command)
    {
        return UpdateSceneTextWithResult(scenes, session, scene, command).CommandResult.Applied;
    }

    internal WorkbenchSceneUpdateResult UpdateSceneTextWithResult(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        SceneTableViewModel scene,
        UpdateSceneTextCommand command)
    {
        var sceneIndex = IndexOfScene(scenes, scene);
        if (sceneIndex < 0)
        {
            return new WorkbenchSceneUpdateResult(WorkbenchCommandResult.NoOp(session.Document), scene);
        }

        if (!string.Equals(command.SceneId, scene.SceneId, StringComparison.Ordinal))
        {
            return new WorkbenchSceneUpdateResult(WorkbenchCommandResult.NoOp(session.Document), scene);
        }

        var previous = session.Document;
        var updated = session.Execute(document => _commandService.UpdateSceneFromKnownLocation(
            document,
            command.SceneId,
            sceneIndex,
            command.Name,
            command.Effect),
            WorkbenchSessionOperationNames.UpdateSceneText);
        WorkbenchViewModelAdapter.ApplySceneFields(scene, updated);
        WorkbenchViewModelAdapter.MarkAppliedSceneDraft(scene, updated);
        var changed = !ReferenceEquals(updated, previous);
        return new WorkbenchSceneUpdateResult(
            changed
                ? WorkbenchCommandResult.AppliedResult(updated, changedSceneIds: [scene.SceneId])
                : WorkbenchCommandResult.NoOp(updated),
            scene,
            BindingTargetRefresh: changed ? WorkbenchBindingTargetRefresh.ForScene(scene) : null,
            DerivedStateRefresh: WorkbenchDerivedStateRefresh.SessionOnly);
    }

    public bool UpdateSceneAsset(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        SceneTableViewModel scene)
    {
        return UpdateSceneAssetWithResult(scenes, session, scene, UpdateSceneBindingCommand.FromScene(scene)).CommandResult.Applied;
    }

    internal bool UpdateSceneAsset(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        SceneTableViewModel scene,
        UpdateSceneBindingCommand command)
    {
        return UpdateSceneAssetWithResult(scenes, session, scene, command).CommandResult.Applied;
    }

    internal WorkbenchSceneUpdateResult UpdateSceneAssetWithResult(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        SceneTableViewModel scene,
        UpdateSceneBindingCommand command)
    {
        var sceneIndex = IndexOfScene(scenes, scene);
        if (sceneIndex < 0)
        {
            return new WorkbenchSceneUpdateResult(WorkbenchCommandResult.NoOp(session.Document), scene);
        }

        if (!string.Equals(command.SceneId, scene.SceneId, StringComparison.Ordinal))
        {
            return new WorkbenchSceneUpdateResult(WorkbenchCommandResult.NoOp(session.Document), scene);
        }

        var previous = session.Document;
        var updated = session.Execute(document => _commandService.UpdateSceneAssetFromKnownLocation(
            document,
            command.SceneId,
            sceneIndex,
            command.SceneImageAssetId),
            WorkbenchSessionOperationNames.UpdateSceneAsset);
        WorkbenchViewModelAdapter.ApplySceneFields(scene, updated);
        WorkbenchViewModelAdapter.MarkAppliedSceneDraft(scene, updated);
        var changed = !ReferenceEquals(updated, previous);
        return new WorkbenchSceneUpdateResult(
            changed
                ? WorkbenchCommandResult.AppliedResult(updated, changedSceneIds: [scene.SceneId])
                : WorkbenchCommandResult.NoOp(updated),
            scene,
            BindingTargetRefresh: changed ? WorkbenchBindingTargetRefresh.ForScene(scene) : null,
            PreviewAssetRefresh: changed ? WorkbenchPreviewAssetRefresh.ForScene(scene) : null,
            DerivedStateRefresh: WorkbenchDerivedStateRefresh.SessionOnly);
    }

    internal WorkbenchSceneUpdateResult UpdateSceneVisualBindingWithResult(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        SceneTableViewModel scene,
        VisualAssetBinding visualBinding)
    {
        var sceneIndex = IndexOfScene(scenes, scene);
        if (sceneIndex < 0)
        {
            return new WorkbenchSceneUpdateResult(WorkbenchCommandResult.NoOp(session.Document), scene);
        }

        var previous = session.Document;
        var updated = session.Execute(document => _commandService.UpdateSceneVisualBindingFromKnownLocation(
            document,
            scene.SceneId,
            sceneIndex,
            visualBinding),
            WorkbenchSessionOperationNames.UpdateSceneAsset);
        var changed = !ReferenceEquals(updated, previous);
        WorkbenchViewModelAdapter.ApplySceneFields(scene, updated);
        WorkbenchViewModelAdapter.MarkAppliedSceneDraft(scene, updated);
        return new WorkbenchSceneUpdateResult(
            changed
                ? WorkbenchCommandResult.AppliedResult(updated, changedSceneIds: [scene.SceneId])
                : WorkbenchCommandResult.NoOp(updated),
            scene,
            BindingTargetRefresh: changed ? WorkbenchBindingTargetRefresh.ForScene(scene) : null,
            PreviewAssetRefresh: changed ? WorkbenchPreviewAssetRefresh.ForScene(scene) : null,
            DerivedStateRefresh: WorkbenchDerivedStateRefresh.SessionOnly);
    }

    public bool UpdateRowText(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        AnalysisResultRowViewModel row)
    {
        return UpdateRowTextWithResult(scenes, session, row, UpdateRowTextCommand.FromRow(row)).CommandResult.Applied;
    }

    internal bool UpdateRowText(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        AnalysisResultRowViewModel row,
        UpdateRowTextCommand command)
    {
        return UpdateRowTextWithResult(scenes, session, row, command).CommandResult.Applied;
    }

    internal WorkbenchRowUpdateResult UpdateRowTextWithResult(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        AnalysisResultRowViewModel row,
        UpdateRowTextCommand command)
    {
        if (!TryFindRowLocation(scenes, row, out var scene, out var sceneIndex, out var rowIndex))
        {
            return new WorkbenchRowUpdateResult(WorkbenchCommandResult.NoOp(session.Document), row);
        }

        if (!string.Equals(command.InteractionId, row.InteractionId, StringComparison.Ordinal))
        {
            return new WorkbenchRowUpdateResult(WorkbenchCommandResult.NoOp(session.Document), row);
        }

        var previous = session.Document;
        var updated = session.ExecuteCoalesced(document => _commandService.UpdateRowTextFromKnownLocation(
            document,
            command.InteractionId,
            scene.SceneId,
            sceneIndex,
            rowIndex,
            command.Text),
            WorkbenchSessionOperationNames.UpdateRowText,
            WorkbenchSessionCoalescingKeys.RowText(command.InteractionId));
        var changed = !ReferenceEquals(updated, previous);
        _ = ApplyUpdatedRowFields(scene, row, updated, scene.SceneId, sceneIndex, rowIndex);
        return new WorkbenchRowUpdateResult(
            changed
                ? WorkbenchCommandResult.AppliedResult(
                    updated,
                    changedSceneIds: [scene.SceneId],
                    changedRowIds: [row.InteractionId])
                : WorkbenchCommandResult.NoOp(updated),
            row,
            DerivedStateRefresh: WorkbenchDerivedStateRefresh.SessionOnly);
    }

    public bool UpdateRowSubject(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        AnalysisResultRowViewModel row)
    {
        return UpdateRowSubjectWithResult(scenes, session, row, UpdateRowSubjectCommand.FromRow(row)).CommandResult.Applied;
    }

    public bool UpdateRowSubject(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        AnalysisResultRowViewModel row,
        IReadOnlyList<Character> characters)
    {
        return UpdateRowSubjectWithResult(
            scenes,
            session,
            row,
            UpdateRowSubjectCommand.FromRow(row, characters)).CommandResult.Applied;
    }

    internal bool UpdateRowSubject(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        AnalysisResultRowViewModel row,
        UpdateRowSubjectCommand command)
    {
        return UpdateRowSubjectWithResult(scenes, session, row, command).CommandResult.Applied;
    }

    internal WorkbenchRowUpdateResult UpdateRowSubjectWithResult(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        AnalysisResultRowViewModel row,
        UpdateRowSubjectCommand command)
    {
        if (!TryFindRowLocation(scenes, row, out var scene, out var sceneIndex, out var rowIndex))
        {
            return new WorkbenchRowUpdateResult(WorkbenchCommandResult.NoOp(session.Document), row);
        }

        if (!string.Equals(command.InteractionId, row.InteractionId, StringComparison.Ordinal))
        {
            return new WorkbenchRowUpdateResult(WorkbenchCommandResult.NoOp(session.Document), row);
        }

        var previous = session.Document;
        previous.RowsById.TryGetValue(command.InteractionId, out var previousRowDraft);
        var updated = session.Execute(document => _commandService.UpdateRowSubjectFromKnownLocation(
            document,
            command.InteractionId,
            scene.SceneId,
            sceneIndex,
            rowIndex,
            command.SubjectName,
            command.SubjectCharacterId),
            WorkbenchSessionOperationNames.UpdateRowSubject);
        var changed = !ReferenceEquals(updated, previous);
        _ = ApplyUpdatedRowFields(scene, row, updated, scene.SceneId, sceneIndex, rowIndex);
        return new WorkbenchRowUpdateResult(
            changed
                ? WorkbenchCommandResult.AppliedResult(
                    updated,
                    changedSceneIds: [scene.SceneId],
                    changedRowIds: [row.InteractionId])
                : WorkbenchCommandResult.NoOp(updated),
            row,
            BindingTargetRefresh: changed
                ? WorkbenchBindingTargetRefresh.ForRowSubjectChange(row, previousRowDraft)
                : null,
            PreviewAssetRefresh: changed && RowVisualBindingChanged(previousRowDraft, updated.RowsById.GetValueOrDefault(row.InteractionId))
                ? WorkbenchPreviewAssetRefresh.ForRow(row)
                : null,
            DerivedStateRefresh: changed && NarrationStateChanged(previousRowDraft, command.SubjectName)
                ? WorkbenchDerivedStateRefresh.Counters
                : WorkbenchDerivedStateRefresh.SessionOnly);
    }

    internal WorkbenchRowUpdateResult UpdateRowSourceTypeWithResult(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        AnalysisResultRowViewModel row,
        UpdateRowSourceTypeCommand command)
    {
        if (!TryFindRowLocation(scenes, row, out var scene, out var sceneIndex, out var rowIndex))
        {
            return new WorkbenchRowUpdateResult(WorkbenchCommandResult.NoOp(session.Document), row);
        }

        if (!string.Equals(command.InteractionId, row.InteractionId, StringComparison.Ordinal))
        {
            return new WorkbenchRowUpdateResult(WorkbenchCommandResult.NoOp(session.Document), row);
        }

        var previous = session.Document;
        previous.RowsById.TryGetValue(command.InteractionId, out var previousRowDraft);
        var updated = session.Execute(document => _commandService.UpdateRowSourceTypeFromKnownLocation(
            document,
            command.InteractionId,
            scene.SceneId,
            sceneIndex,
            rowIndex,
            command.SourceItemType,
            command.SubjectName,
            command.SubjectCharacterId),
            WorkbenchSessionOperationNames.UpdateRowSubject);
        var changed = !ReferenceEquals(updated, previous);
        row.ApplySourceTypeProjection(
            command.SourceItemType,
            command.SubjectName,
            command.SubjectCharacterId,
            command.SourceItemType is "effects" or "narration");
        _ = ApplyUpdatedRowFields(scene, row, updated, scene.SceneId, sceneIndex, rowIndex);
        return new WorkbenchRowUpdateResult(
            changed
                ? WorkbenchCommandResult.AppliedResult(
                    updated,
                    changedSceneIds: [scene.SceneId],
                    changedRowIds: [row.InteractionId])
                : WorkbenchCommandResult.NoOp(updated),
            row,
            BindingTargetRefresh: changed
                ? WorkbenchBindingTargetRefresh.ForRowSubjectChange(row, previousRowDraft)
                : null,
            PreviewAssetRefresh: changed && RowVisualBindingChanged(previousRowDraft, updated.RowsById.GetValueOrDefault(row.InteractionId))
                ? WorkbenchPreviewAssetRefresh.ForRow(row)
                : null,
            DerivedStateRefresh: changed
                ? WorkbenchDerivedStateRefresh.Counters
                : WorkbenchDerivedStateRefresh.SessionOnly);
    }

    private static bool RowVisualBindingChanged(WorkbenchRowDraft? previous, WorkbenchRowDraft? current)
    {
        return previous is not null &&
            current is not null &&
            !VisualAssetBinding.SemanticEquals(previous.CharacterVisualBinding, current.CharacterVisualBinding);
    }

    public bool UpdateRowEmotion(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        AnalysisResultRowViewModel row)
    {
        return UpdateRowEmotionWithResult(scenes, session, row, UpdateRowEmotionCommand.FromRow(row)).CommandResult.Applied;
    }

    internal bool UpdateRowEmotion(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        AnalysisResultRowViewModel row,
        UpdateRowEmotionCommand command)
    {
        return UpdateRowEmotionWithResult(scenes, session, row, command).CommandResult.Applied;
    }

    internal WorkbenchRowUpdateResult UpdateRowEmotionWithResult(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        AnalysisResultRowViewModel row,
        UpdateRowEmotionCommand command)
    {
        if (!TryFindRowLocation(scenes, row, out var scene, out var sceneIndex, out var rowIndex))
        {
            return new WorkbenchRowUpdateResult(WorkbenchCommandResult.NoOp(session.Document), row);
        }

        if (!string.Equals(command.InteractionId, row.InteractionId, StringComparison.Ordinal))
        {
            return new WorkbenchRowUpdateResult(WorkbenchCommandResult.NoOp(session.Document), row);
        }

        var previous = session.Document;
        var updated = session.ExecuteCoalesced(document => _commandService.UpdateRowEmotionFromKnownLocation(
            document,
            command.InteractionId,
            scene.SceneId,
            sceneIndex,
            rowIndex,
            command.Emotion,
            command.EmotionVectors,
            command.EmotionAlpha),
            WorkbenchSessionOperationNames.UpdateRowEmotion,
            WorkbenchSessionCoalescingKeys.RowEmotion(command.InteractionId));
        var changed = !ReferenceEquals(updated, previous);
        _ = ApplyUpdatedRowFields(scene, row, updated, scene.SceneId, sceneIndex, rowIndex);
        return new WorkbenchRowUpdateResult(
            changed
                ? WorkbenchCommandResult.AppliedResult(
                    updated,
                    changedSceneIds: [scene.SceneId],
                    changedRowIds: [row.InteractionId])
                : WorkbenchCommandResult.NoOp(updated),
            row,
            DerivedStateRefresh: WorkbenchDerivedStateRefresh.SessionOnly);
    }

    public bool UpdateRowBinding(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        AnalysisResultRowViewModel row)
    {
        return UpdateRowBindingWithResult(scenes, session, row, UpdateRowBindingCommand.FromRow(row)).CommandResult.Applied;
    }

    internal bool UpdateRowBinding(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        AnalysisResultRowViewModel row,
        UpdateRowBindingCommand command)
    {
        return UpdateRowBindingWithResult(scenes, session, row, command).CommandResult.Applied;
    }

    internal WorkbenchRowUpdateResult UpdateRowBindingWithResult(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        AnalysisResultRowViewModel row,
        UpdateRowBindingCommand command)
    {
        if (!TryFindRowLocation(scenes, row, out var scene, out var sceneIndex, out var rowIndex))
        {
            return new WorkbenchRowUpdateResult(WorkbenchCommandResult.NoOp(session.Document), row);
        }

        if (!string.Equals(command.InteractionId, row.InteractionId, StringComparison.Ordinal))
        {
            return new WorkbenchRowUpdateResult(WorkbenchCommandResult.NoOp(session.Document), row);
        }

        var previous = session.Document;
        previous.RowsById.TryGetValue(command.InteractionId, out var previousRowDraft);
        var updated = session.Execute(document => _commandService.UpdateRowBindingFromKnownLocation(
            document,
            command.InteractionId,
            scene.SceneId,
            sceneIndex,
            rowIndex,
            command.CharacterImageAssetId,
            command.VoiceReferenceId,
            command.VoiceReferenceText),
            WorkbenchSessionOperationNames.UpdateRowBinding);
        var changed = !ReferenceEquals(updated, previous);
        _ = ApplyUpdatedRowFields(scene, row, updated, scene.SceneId, sceneIndex, rowIndex);
        return new WorkbenchRowUpdateResult(
            changed
                ? WorkbenchCommandResult.AppliedResult(
                    updated,
                    changedSceneIds: [scene.SceneId],
                    changedRowIds: [row.InteractionId])
                : WorkbenchCommandResult.NoOp(updated),
            row,
            BindingTargetRefresh: changed ? WorkbenchBindingTargetRefresh.ForRow(row) : null,
            PreviewAssetRefresh: changed &&
                !string.Equals(previousRowDraft?.CharacterVisualBinding?.EffectiveAssetId, command.CharacterImageAssetId, StringComparison.Ordinal)
                    ? WorkbenchPreviewAssetRefresh.ForRow(row)
                    : null,
            DerivedStateRefresh: WorkbenchDerivedStateRefresh.SessionOnly);
    }

    internal WorkbenchRowUpdateResult UpdateRowCharacterVisualBindingWithResult(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchSession session,
        AnalysisResultRowViewModel row,
        VisualAssetBinding visualBinding)
    {
        if (!TryFindRowLocation(scenes, row, out var scene, out var sceneIndex, out var rowIndex))
        {
            return new WorkbenchRowUpdateResult(WorkbenchCommandResult.NoOp(session.Document), row);
        }

        var previous = session.Document;
        previous.RowsById.TryGetValue(row.InteractionId, out var previousRowDraft);
        var updated = session.Execute(document => _commandService.UpdateRowCharacterVisualBindingFromKnownLocation(
            document,
            row.InteractionId,
            scene.SceneId,
            sceneIndex,
            rowIndex,
            visualBinding),
            WorkbenchSessionOperationNames.UpdateRowBinding);
        var changed = !ReferenceEquals(updated, previous);
        _ = ApplyUpdatedRowFields(scene, row, updated, scene.SceneId, sceneIndex, rowIndex);
        var currentAssetId = visualBinding?.EffectiveAssetId;
        var previousAssetId = previousRowDraft is null
            ? null
            : previousRowDraft.CharacterVisualBinding?.EffectiveAssetId;
        return new WorkbenchRowUpdateResult(
            changed
                ? WorkbenchCommandResult.AppliedResult(
                    updated,
                    changedSceneIds: [scene.SceneId],
                    changedRowIds: [row.InteractionId])
                : WorkbenchCommandResult.NoOp(updated),
            row,
            BindingTargetRefresh: changed ? WorkbenchBindingTargetRefresh.ForRow(row) : null,
            PreviewAssetRefresh: changed &&
                !string.Equals(previousAssetId, currentAssetId, StringComparison.Ordinal)
                    ? WorkbenchPreviewAssetRefresh.ForRow(row)
                    : null,
            DerivedStateRefresh: WorkbenchDerivedStateRefresh.SessionOnly);
    }

    public void RenumberScenes(ObservableCollection<SceneTableViewModel> scenes)
    {
        for (var index = 0; index < scenes.Count; index++)
        {
            scenes[index].Order = index + 1;
            scenes[index].RenumberRows();
        }
    }

    public void ApplyOrderFieldsFromDocument(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchDocument document)
    {
        if (scenes.Count != document.Scenes.Count)
        {
            WorkbenchViewModelAdapter.ApplyDocument(scenes, document);
            return;
        }

        for (var sceneIndex = 0; sceneIndex < scenes.Count; sceneIndex++)
        {
            var scene = scenes[sceneIndex];
            var sceneDraft = document.Scenes[sceneIndex];
            if (!string.Equals(scene.SceneId, sceneDraft.SceneId, StringComparison.Ordinal) ||
                scene.Rows.Count != sceneDraft.Rows.Count)
            {
                WorkbenchViewModelAdapter.ApplyDocument(scenes, document);
                return;
            }

            for (var rowIndex = 0; rowIndex < scene.Rows.Count; rowIndex++)
            {
                if (!string.Equals(scene.Rows[rowIndex].InteractionId, sceneDraft.Rows[rowIndex].InteractionId, StringComparison.Ordinal))
                {
                    WorkbenchViewModelAdapter.ApplyDocument(scenes, document);
                    return;
                }
            }
        }

        for (var sceneIndex = 0; sceneIndex < scenes.Count; sceneIndex++)
        {
            var scene = scenes[sceneIndex];
            var sceneDraft = document.Scenes[sceneIndex];
            scene.Order = sceneDraft.Order;
            for (var rowIndex = 0; rowIndex < scene.Rows.Count; rowIndex++)
            {
                scene.Rows[rowIndex].Order = sceneDraft.Rows[rowIndex].Order;
            }

            WorkbenchViewModelAdapter.MarkAppliedDrafts(scene, sceneDraft);
        }
    }

    private static bool TryFindRowLocation(
        ObservableCollection<SceneTableViewModel> scenes,
        AnalysisResultRowViewModel row,
        out SceneTableViewModel scene,
        out int rowIndex)
    {
        return TryFindRowLocation(scenes, row, out scene, out _, out rowIndex);
    }

    private static bool TryFindRowLocation(
        ObservableCollection<SceneTableViewModel> scenes,
        AnalysisResultRowViewModel row,
        out SceneTableViewModel scene,
        out int sceneIndex,
        out int rowIndex)
    {
        scene = null!;
        sceneIndex = -1;
        rowIndex = -1;
        if (TryResolveOwnerScene(row, out var ownerScene, out var ownerRowIndex) &&
            IndexOfScene(scenes, ownerScene) is var ownerSceneIndex &&
            ownerSceneIndex >= 0)
        {
            scene = ownerScene;
            sceneIndex = ownerSceneIndex;
            rowIndex = ownerRowIndex;
            return true;
        }

        if (row.AppliedDraft is { SceneId: { Length: > 0 } sceneId } &&
            TryFindSceneById(scenes, sceneId, out var draftScene, out var draftSceneIndex))
        {
            var draftRowIndex = IndexOfRow(draftScene, row);
            if (draftRowIndex < 0)
            {
                return false;
            }

            scene = draftScene;
            sceneIndex = draftSceneIndex;
            rowIndex = draftRowIndex;
            return true;
        }

        return TryFindRowByReference(scenes, row, out scene, out sceneIndex, out rowIndex);
    }

    private static bool TryResolveOwnerScene(
        AnalysisResultRowViewModel row,
        out SceneTableViewModel scene,
        out int rowIndex)
    {
        scene = null!;
        rowIndex = -1;
        if (row.OwnerScene is not { } ownerScene)
        {
            return false;
        }

        var expectedIndex = row.Order - 1;
        if ((uint)expectedIndex < (uint)ownerScene.Rows.Count &&
            ReferenceEquals(ownerScene.Rows[expectedIndex], row))
        {
            scene = ownerScene;
            rowIndex = expectedIndex;
            return true;
        }

        var fallbackIndex = IndexOfRow(ownerScene, row);
        if (fallbackIndex >= 0)
        {
            scene = ownerScene;
            rowIndex = fallbackIndex;
            return true;
        }

        return false;
    }

    private static bool TryFindRowByReference(
        ObservableCollection<SceneTableViewModel> scenes,
        AnalysisResultRowViewModel row,
        out SceneTableViewModel scene,
        out int sceneIndex,
        out int rowIndex)
    {
        scene = null!;
        sceneIndex = -1;
        rowIndex = -1;
        for (var index = 0; index < scenes.Count; index++)
        {
            var candidate = scenes[index];
            for (var candidateRowIndex = 0; candidateRowIndex < candidate.Rows.Count; candidateRowIndex++)
            {
                if (ReferenceEquals(candidate.Rows[candidateRowIndex], row))
                {
                    scene = candidate;
                    sceneIndex = index;
                    rowIndex = candidateRowIndex;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryFindSceneById(
        ObservableCollection<SceneTableViewModel> scenes,
        string sceneId,
        out SceneTableViewModel scene,
        out int sceneIndex)
    {
        scene = null!;
        sceneIndex = -1;
        for (var index = 0; index < scenes.Count; index++)
        {
            if (string.Equals(scenes[index].SceneId, sceneId, StringComparison.Ordinal))
            {
                scene = scenes[index];
                sceneIndex = index;
                return true;
            }
        }

        return false;
    }

    private static int IndexOfScene(
        ObservableCollection<SceneTableViewModel> scenes,
        SceneTableViewModel scene)
    {
        var expectedIndex = scene.Order - 1;
        if ((uint)expectedIndex < (uint)scenes.Count && ReferenceEquals(scenes[expectedIndex], scene))
        {
            return expectedIndex;
        }

        for (var index = 0; index < scenes.Count; index++)
        {
            if (ReferenceEquals(scenes[index], scene))
            {
                return index;
            }
        }

        return -1;
    }

    private static int IndexOfRow(
        SceneTableViewModel scene,
        AnalysisResultRowViewModel row)
    {
        var expectedIndex = row.Order - 1;
        if ((uint)expectedIndex < (uint)scene.Rows.Count && ReferenceEquals(scene.Rows[expectedIndex], row))
        {
            return expectedIndex;
        }

        for (var index = 0; index < scene.Rows.Count; index++)
        {
            if (ReferenceEquals(scene.Rows[index], row))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsSameRowDropPosition(
        SceneTableViewModel sourceScene,
        SceneTableViewModel targetScene,
        int sourceIndex,
        int targetIndex,
        WorkbenchDropPosition position)
    {
        return ReferenceEquals(sourceScene, targetScene) &&
            IsSameListDropPosition(sourceIndex, targetIndex, position);
    }

    private static bool IsSameListDropPosition(
        int sourceIndex,
        int targetIndex,
        WorkbenchDropPosition position)
    {
        return position == WorkbenchDropPosition.Before
            ? sourceIndex + 1 == targetIndex
            : sourceIndex == targetIndex + 1;
    }

    private static int ResolveRowInsertIndex(
        SceneTableViewModel sourceScene,
        SceneTableViewModel targetScene,
        int sourceIndex,
        int targetIndex,
        WorkbenchDropPosition position)
    {
        if (!ReferenceEquals(sourceScene, targetScene))
        {
            return position == WorkbenchDropPosition.Before ? targetIndex : targetIndex + 1;
        }

        return ResolveSameListInsertIndex(sourceIndex, targetIndex, position);
    }

    private static int ResolveSameListInsertIndex(
        int sourceIndex,
        int targetIndex,
        WorkbenchDropPosition position)
    {
        return position switch
        {
            WorkbenchDropPosition.Before when sourceIndex < targetIndex => targetIndex - 1,
            WorkbenchDropPosition.Before => targetIndex,
            WorkbenchDropPosition.After when sourceIndex < targetIndex => targetIndex,
            _ => targetIndex + 1
        };
    }

    private static string[] ChangedSceneIdsForMove(string sourceSceneId, string targetSceneId)
    {
        return string.Equals(sourceSceneId, targetSceneId, StringComparison.Ordinal)
            ? [sourceSceneId]
            : [sourceSceneId, targetSceneId];
    }

    private static WorkbenchBindingTargetRefresh? RowMoveBindingTargetRefresh(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchCommandResult result,
        bool preview)
    {
        if (preview || !result.Applied || result.ChangedRowIds.Count == 0)
        {
            return null;
        }

        var remainingRowIds = new HashSet<string>(result.ChangedRowIds, StringComparer.Ordinal);
        var changedRows = new List<AnalysisResultRowViewModel>(result.ChangedRowIds.Count);
        foreach (var scene in scenes)
        {
            foreach (var row in scene.Rows)
            {
                if (!remainingRowIds.Remove(row.InteractionId))
                {
                    continue;
                }

                changedRows.Add(row);
                if (remainingRowIds.Count == 0)
                {
                    return WorkbenchBindingTargetRefresh.ForRows(SortRowsByChangedRowIds(changedRows, result.ChangedRowIds));
                }
            }
        }

        return changedRows.Count == 0
            ? null
            : WorkbenchBindingTargetRefresh.ForRows(SortRowsByChangedRowIds(changedRows, result.ChangedRowIds));
    }

    private static WorkbenchBindingTargetRefresh? SceneMoveBindingTargetRefresh(
        ObservableCollection<SceneTableViewModel> scenes,
        IReadOnlyList<string> changedSceneIds,
        bool applied,
        bool preview)
    {
        if (preview || !applied || changedSceneIds.Count == 0)
        {
            return null;
        }

        var changedScenes = ScenesForChangedSceneIds(scenes, changedSceneIds);
        return changedScenes.Length == 0
            ? WorkbenchBindingTargetRefresh.SyncSceneOrder()
            : WorkbenchBindingTargetRefresh.ForSceneOrderRange(changedScenes);
    }

    private static SceneTableViewModel[] ScenesForChangedSceneIds(
        IReadOnlyList<SceneTableViewModel> scenes,
        IReadOnlyList<string> changedSceneIds)
    {
        var remainingSceneIds = new HashSet<string>(changedSceneIds, StringComparer.Ordinal);
        var changedScenes = new List<SceneTableViewModel>(changedSceneIds.Count);
        foreach (var scene in scenes)
        {
            if (!remainingSceneIds.Remove(scene.SceneId))
            {
                continue;
            }

            changedScenes.Add(scene);
            if (remainingSceneIds.Count == 0)
            {
                return SortScenesByChangedSceneIds(changedScenes, changedSceneIds);
            }
        }

        return changedScenes.Count == 0
            ? []
            : SortScenesByChangedSceneIds(changedScenes, changedSceneIds);
    }

    private static SceneTableViewModel[] SortScenesByChangedSceneIds(
        IReadOnlyList<SceneTableViewModel> scenes,
        IReadOnlyList<string> changedSceneIds)
    {
        if (scenes.Count <= 1)
        {
            return scenes.ToArray();
        }

        var scenesById = new Dictionary<string, SceneTableViewModel>(scenes.Count, StringComparer.Ordinal);
        foreach (var scene in scenes)
        {
            scenesById[scene.SceneId] = scene;
        }

        var sortedScenes = new List<SceneTableViewModel>(scenes.Count);
        foreach (var sceneId in changedSceneIds)
        {
            if (scenesById.TryGetValue(sceneId, out var scene))
            {
                sortedScenes.Add(scene);
            }
        }

        return sortedScenes.ToArray();
    }

    private static AnalysisResultRowViewModel[] SortRowsByChangedRowIds(
        IReadOnlyList<AnalysisResultRowViewModel> rows,
        IReadOnlyList<string> changedRowIds)
    {
        if (rows.Count <= 1)
        {
            return rows.ToArray();
        }

        var rowsById = new Dictionary<string, AnalysisResultRowViewModel>(rows.Count, StringComparer.Ordinal);
        foreach (var row in rows)
        {
            rowsById[row.InteractionId] = row;
        }

        var sortedRows = new List<AnalysisResultRowViewModel>(rows.Count);
        foreach (var rowId in changedRowIds)
        {
            if (rowsById.TryGetValue(rowId, out var row))
            {
                sortedRows.Add(row);
            }
        }

        return sortedRows.ToArray();
    }

    private static string[] ChangedRowIdsForRowMove(
        SceneTableViewModel sourceScene,
        SceneTableViewModel targetScene,
        int sourceIndex,
        int targetIndex)
    {
        if (ReferenceEquals(sourceScene, targetScene))
        {
            if (sourceScene.Rows.Count == 0)
            {
                return [];
            }

            var insertIndex = Math.Clamp(targetIndex, 0, sourceScene.Rows.Count - 1);
            return ChangedRowIdsForRange(
                sourceScene,
                Math.Min(sourceIndex, insertIndex),
                Math.Max(sourceIndex, insertIndex));
        }

        var changedRowIds = new List<string>();
        AddChangedRowIdsForRange(changedRowIds, sourceScene, sourceIndex, sourceScene.Rows.Count - 1);

        var targetInsertIndex = Math.Clamp(targetIndex, 0, targetScene.Rows.Count - 1);
        AddChangedRowIdsForRange(changedRowIds, targetScene, targetInsertIndex, targetScene.Rows.Count - 1);

        return changedRowIds.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string[] ChangedRowIdsForRange(
        SceneTableViewModel scene,
        int startIndex,
        int endIndexInclusive)
    {
        var changedRowIds = new List<string>();
        AddChangedRowIdsForRange(changedRowIds, scene, startIndex, endIndexInclusive);
        return changedRowIds.ToArray();
    }

    private static void AddChangedRowIdsForRange(
        List<string> changedRowIds,
        SceneTableViewModel scene,
        int startIndex,
        int endIndexInclusive)
    {
        if (scene.Rows.Count == 0 || startIndex > endIndexInclusive)
        {
            return;
        }

        var start = Math.Max(startIndex, 0);
        var end = Math.Min(endIndexInclusive, scene.Rows.Count - 1);
        if (start > end)
        {
            return;
        }

        for (var index = start; index <= end; index++)
        {
            changedRowIds.Add(scene.Rows[index].InteractionId);
        }
    }

    private static string[] ChangedSceneIdsForSceneMove(
        ObservableCollection<SceneTableViewModel> scenes,
        int sourceIndex,
        int targetIndex)
    {
        var startIndex = Math.Min(sourceIndex, targetIndex);
        var endIndex = Math.Max(sourceIndex, targetIndex);
        if (startIndex < 0 || endIndex >= scenes.Count)
        {
            return [];
        }

        var changedSceneIds = new string[endIndex - startIndex + 1];
        for (var index = startIndex; index <= endIndex; index++)
        {
            changedSceneIds[index - startIndex] = scenes[index].SceneId;
        }

        return changedSceneIds;
    }

    private static bool NarrationStateChanged(WorkbenchRowDraft? previousRowDraft, string subjectName)
    {
        return previousRowDraft is not null &&
            (previousRowDraft.Kind == WorkbenchRowKind.Narration) != IsNarrationSubject(subjectName);
    }

    private static bool IsNarrationSubject(string? subjectName)
    {
        return string.Equals(subjectName?.Trim(), "旁白", StringComparison.Ordinal);
    }

    private static WorkbenchDocument ExecuteMove(
        WorkbenchSession session,
        System.Func<WorkbenchDocument, WorkbenchDocument> command,
        bool preview,
        string operationName)
    {
        return preview ? session.Preview(command) : session.Execute(command, operationName);
    }

    private static bool ApplyUpdatedRowFields(
        SceneTableViewModel scene,
        AnalysisResultRowViewModel row,
        WorkbenchDocument document,
        string sceneId,
        int sceneIndex,
        int rowIndex)
    {
        if ((uint)sceneIndex >= (uint)document.Scenes.Count)
        {
            return false;
        }

        var sceneDraft = document.Scenes[sceneIndex];
        if (!string.Equals(sceneDraft.SceneId, sceneId, StringComparison.Ordinal) ||
            (uint)rowIndex >= (uint)sceneDraft.Rows.Count)
        {
            return false;
        }

        var rowDraft = sceneDraft.Rows[rowIndex];
        if (!string.Equals(rowDraft.InteractionId, row.InteractionId, StringComparison.Ordinal))
        {
            return false;
        }

        WorkbenchViewModelAdapter.ApplyRowFields(row, rowDraft);
        WorkbenchViewModelAdapter.MarkAppliedSceneDraft(scene, sceneDraft);
        return true;
    }

    private static void ApplyLocalSceneAdd(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchDocument document,
        SceneTableViewModel scene,
        int targetIndex)
    {
        if ((uint)targetIndex >= (uint)document.Scenes.Count ||
            !string.Equals(document.Scenes[targetIndex].SceneId, scene.SceneId, StringComparison.Ordinal))
        {
            WorkbenchViewModelAdapter.ApplyDocument(scenes, document, [scene], []);
            return;
        }

        var currentIndex = IndexOfScene(scenes, scene);
        if (currentIndex < 0)
        {
            scenes.Insert(Math.Clamp(targetIndex, 0, scenes.Count), scene);
        }
        else if (currentIndex != targetIndex)
        {
            scenes.Move(currentIndex, targetIndex);
        }

        RenumberSceneOrdersToDocument(scenes, document, Math.Clamp(targetIndex, 0, scenes.Count - 1), scenes.Count - 1);
        WorkbenchViewModelAdapter.MarkAppliedSceneDraft(scene, document);
    }

    private static void ApplyLocalSceneRemove(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchDocument document,
        SceneTableViewModel scene,
        int sourceIndex)
    {
        if (document.TryGetSceneDraft(scene.SceneId, out _) ||
            (uint)sourceIndex >= (uint)scenes.Count ||
            !ReferenceEquals(scenes[sourceIndex], scene))
        {
            WorkbenchViewModelAdapter.ApplyDocument(scenes, document);
            return;
        }

        scenes.RemoveAt(sourceIndex);
        if (sourceIndex < scenes.Count)
        {
            RenumberSceneOrdersToDocument(scenes, document, sourceIndex, scenes.Count - 1);
            if (!MarkAppliedSceneDraftsByPosition(scenes, document, sourceIndex, scenes.Count - 1))
            {
                WorkbenchViewModelAdapter.ApplyDocument(scenes, document);
            }
        }
    }

    private static void ApplyLocalRowAdd(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchDocument document,
        SceneTableViewModel scene,
        int sceneIndex,
        AnalysisResultRowViewModel row,
        int targetIndex)
    {
        if (!TryGetSceneDraftAtKnownIndex(document, scene.SceneId, sceneIndex, out var sceneDraft) ||
            IndexOfScene(scenes, scene) != sceneIndex ||
            sceneDraft.Rows.Count != scene.Rows.Count + 1)
        {
            WorkbenchViewModelAdapter.ApplyDocument(scenes, document, [], [row]);
            return;
        }

        var insertIndex = Math.Clamp(targetIndex, 0, scene.Rows.Count);
        if (insertIndex >= sceneDraft.Rows.Count ||
            !string.Equals(sceneDraft.Rows[insertIndex].InteractionId, row.InteractionId, StringComparison.Ordinal))
        {
            WorkbenchViewModelAdapter.ApplyDocument(scenes, document, [], [row]);
            return;
        }

        scene.Rows.Insert(insertIndex, row);
        scene.RenumberRows(insertIndex, scene.Rows.Count - 1);
        WorkbenchViewModelAdapter.MarkAppliedDrafts(scene, sceneDraft, insertIndex, scene.Rows.Count - 1);
    }

    private static void ApplyLocalRowRemove(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchDocument document,
        AnalysisResultRowViewModel row,
        SceneTableViewModel sourceScene,
        int sourceSceneIndex,
        int sourceIndex)
    {
        if (!TryGetSceneDraftAtKnownIndex(document, sourceScene.SceneId, sourceSceneIndex, out var sceneDraft) ||
            IndexOfScene(scenes, sourceScene) != sourceSceneIndex ||
            sceneDraft.Rows.Count != sourceScene.Rows.Count - 1 ||
            (uint)sourceIndex >= (uint)sourceScene.Rows.Count ||
            !ReferenceEquals(sourceScene.Rows[sourceIndex], row))
        {
            WorkbenchViewModelAdapter.ApplyDocument(scenes, document);
            return;
        }

        sourceScene.Rows.RemoveAt(sourceIndex);
        if (sourceScene.Rows.Count > 0 && sourceIndex < sourceScene.Rows.Count)
        {
            sourceScene.RenumberRows(sourceIndex, sourceScene.Rows.Count - 1);
            WorkbenchViewModelAdapter.MarkAppliedDrafts(sourceScene, sceneDraft, sourceIndex, sourceScene.Rows.Count - 1);
            return;
        }

        WorkbenchViewModelAdapter.MarkAppliedSceneDraft(sourceScene, sceneDraft);
    }

    private static bool ApplyLocalRowMove(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchDocument document,
        AnalysisResultRowViewModel draggedRow,
        SceneTableViewModel sourceScene,
        int sourceSceneIndex,
        SceneTableViewModel targetScene,
        int targetSceneIndex,
        int sourceIndex,
        int targetIndex,
        bool updateOrderFields)
    {
        if (!TryGetSceneDraftAtKnownIndex(document, sourceScene.SceneId, sourceSceneIndex, out var sourceSceneDraft) ||
            !TryGetSceneDraftAtKnownIndex(document, targetScene.SceneId, targetSceneIndex, out var targetSceneDraft) ||
            IndexOfScene(scenes, sourceScene) != sourceSceneIndex ||
            IndexOfScene(scenes, targetScene) != targetSceneIndex ||
            (uint)sourceIndex >= (uint)sourceScene.Rows.Count ||
            !ReferenceEquals(sourceScene.Rows[sourceIndex], draggedRow))
        {
            WorkbenchViewModelAdapter.ApplyDocument(scenes, document);
            return true;
        }

        if (ReferenceEquals(sourceScene, targetScene))
        {
            var insertIndex = Math.Clamp(targetIndex, 0, sourceScene.Rows.Count - 1);
            if (sourceIndex != insertIndex)
            {
                sourceScene.Rows.Move(sourceIndex, insertIndex);
            }

            if (updateOrderFields)
            {
                sourceScene.RenumberRows(Math.Min(sourceIndex, insertIndex), Math.Max(sourceIndex, insertIndex));
            }

            WorkbenchViewModelAdapter.MarkAppliedDrafts(
                sourceScene,
                sourceSceneDraft,
                Math.Min(sourceIndex, insertIndex),
                Math.Max(sourceIndex, insertIndex));
            return true;
        }

        sourceScene.Rows.RemoveAt(sourceIndex);
        var crossSceneInsertIndex = Math.Clamp(targetIndex, 0, targetScene.Rows.Count);
        targetScene.Rows.Insert(crossSceneInsertIndex, draggedRow);
        if (sourceScene.Rows.Count > 0 && sourceIndex < sourceScene.Rows.Count)
        {
            if (updateOrderFields)
            {
                sourceScene.RenumberRows(sourceIndex, sourceScene.Rows.Count - 1);
            }

            WorkbenchViewModelAdapter.MarkAppliedDrafts(sourceScene, sourceSceneDraft, sourceIndex, sourceScene.Rows.Count - 1);
        }
        else
        {
            WorkbenchViewModelAdapter.MarkAppliedSceneDraft(sourceScene, sourceSceneDraft);
        }

        if (updateOrderFields)
        {
            targetScene.RenumberRows(crossSceneInsertIndex, targetScene.Rows.Count - 1);
        }

        WorkbenchViewModelAdapter.MarkAppliedDrafts(targetScene, targetSceneDraft, crossSceneInsertIndex, targetScene.Rows.Count - 1);
        return true;
    }

    private static bool ApplyLocalSceneMove(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchDocument document,
        SceneTableViewModel draggedScene,
        int targetIndex,
        bool updateOrderFields)
    {
        if ((uint)targetIndex >= (uint)document.Scenes.Count ||
            !string.Equals(document.Scenes[targetIndex].SceneId, draggedScene.SceneId, StringComparison.Ordinal))
        {
            WorkbenchViewModelAdapter.ApplyDocument(scenes, document);
            return false;
        }

        var sourceIndex = IndexOfScene(scenes, draggedScene);
        if (sourceIndex < 0)
        {
            WorkbenchViewModelAdapter.ApplyDocument(scenes, document);
            return false;
        }

        var startIndex = Math.Min(sourceIndex, targetIndex);
        var endIndex = Math.Max(sourceIndex, targetIndex);
        if (sourceIndex != targetIndex)
        {
            scenes.Move(sourceIndex, targetIndex);
        }

        if (updateOrderFields)
        {
            RenumberSceneOrdersToDocument(scenes, document, startIndex, endIndex);
        }

        if (!MarkAppliedSceneDraftsByPosition(scenes, document, startIndex, endIndex))
        {
            WorkbenchViewModelAdapter.ApplyDocument(scenes, document);
        }

        return true;
    }

    private static void RenumberSceneOrdersToDocument(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchDocument document)
    {
        for (var index = 0; index < scenes.Count; index++)
        {
            var order = index < document.Scenes.Count ? document.Scenes[index].Order : index + 1;
            scenes[index].Order = order;
        }
    }

    private static void RenumberSceneOrdersToDocument(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchDocument document,
        int startIndex,
        int endIndexInclusive)
    {
        if (scenes.Count == 0)
        {
            return;
        }

        var start = Math.Clamp(startIndex, 0, scenes.Count - 1);
        var end = Math.Clamp(endIndexInclusive, 0, scenes.Count - 1);
        if (start > end)
        {
            return;
        }

        for (var index = start; index <= end; index++)
        {
            var order = index < document.Scenes.Count ? document.Scenes[index].Order : index + 1;
            scenes[index].Order = order;
        }
    }

    private static bool MarkAppliedSceneDraftsByPosition(
        ObservableCollection<SceneTableViewModel> scenes,
        WorkbenchDocument document,
        int startIndex,
        int endIndexInclusive)
    {
        if (scenes.Count == 0)
        {
            return true;
        }

        var start = Math.Clamp(startIndex, 0, scenes.Count - 1);
        var end = Math.Clamp(endIndexInclusive, 0, scenes.Count - 1);
        if (start > end)
        {
            return true;
        }

        if (end >= document.Scenes.Count)
        {
            return false;
        }

        for (var index = start; index <= end; index++)
        {
            if (!string.Equals(scenes[index].SceneId, document.Scenes[index].SceneId, StringComparison.Ordinal))
            {
                return false;
            }
        }

        for (var index = start; index <= end; index++)
        {
            scenes[index].MarkAppliedDraft(document.Scenes[index]);
        }

        return true;
    }

    private static bool TryGetSceneDraftAtKnownIndex(
        WorkbenchDocument document,
        string sceneId,
        int sceneIndex,
        out WorkbenchSceneDraft sceneDraft)
    {
        if ((uint)sceneIndex < (uint)document.Scenes.Count &&
            string.Equals(document.Scenes[sceneIndex].SceneId, sceneId, StringComparison.Ordinal))
        {
            sceneDraft = document.Scenes[sceneIndex];
            return true;
        }

        sceneDraft = null!;
        return false;
    }

}
