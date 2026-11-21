using System.Collections.ObjectModel;
using DraftAgent.App.ViewModels;
using DraftAgent.Core;

namespace DraftAgent.App.Tests;

public sealed class WorkbenchDraftServiceTests
{
    [Fact]
    public void AddCommandDtosResolveTargetPositionsAndRows()
    {
        var scene = SceneTableViewModel.CreateManual(3);
        var first = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var second = AnalysisResultRowViewModel.CreateManualDialogue(2, "Lin");
        scene.Rows.Add(first);
        scene.Rows.Add(second);

        var sceneCommand = AddSceneCommand.FromSceneCollection([scene]);
        var dialogueCommand = AddRowCommand.ForDialogue(scene, 4, second, "Mo");
        var narrationCommand = AddRowCommand.ForNarration(scene, 4, null);

        Assert.Equal(1, sceneCommand.TargetIndex);
        Assert.Equal(2, sceneCommand.Scene.Order);
        Assert.Equal(scene.SceneId, dialogueCommand.SceneId);
        Assert.Equal(4, dialogueCommand.SceneIndex);
        Assert.Equal(1, dialogueCommand.TargetIndex);
        Assert.Equal("Mo", dialogueCommand.Row.SubjectName);
        Assert.False(dialogueCommand.Row.IsNarration);
        Assert.Equal(scene.Rows.Count, narrationCommand.TargetIndex);
        Assert.True(narrationCommand.Row.IsNarration);
    }

    [Fact]
    public void RemoveCommandDtosCarryResolvedLocations()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(3, "Chen");

        var sceneCommand = RemoveSceneCommand.FromResolvedLocation(scene, 5);
        var rowCommand = RemoveRowCommand.FromResolvedLocation(row, scene, 5, 2);

        Assert.Equal(scene.SceneId, sceneCommand.SceneId);
        Assert.Equal(5, sceneCommand.SourceIndex);
        Assert.Equal(row.InteractionId, rowCommand.InteractionId);
        Assert.Equal(scene.SceneId, rowCommand.SourceSceneId);
        Assert.Equal(5, rowCommand.SourceSceneIndex);
        Assert.Equal(2, rowCommand.SourceRowIndex);
    }

    [Fact]
    public void UpdateRowTextUsesAppliedDraftSceneWhenRowIsStillInThatScene()
    {
        var service = new WorkbenchDraftService();
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(row);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);

        row.LineText = "Updated line";
        var updated = service.UpdateRowText(scenes, session, row);

        Assert.True(updated);
        Assert.Equal("Updated line", session.Document.RowsById[row.InteractionId].Text);
    }

    [Fact]
    public void UpdateRowTextUsesOwnerSceneAfterAppliedDraftIsInvalidated()
    {
        var service = new WorkbenchDraftService();
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(row);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);

        row.LineText = "First edit invalidates draft";
        Assert.Null(row.AppliedDraft);
        Assert.Same(scene, row.OwnerScene);

        var updated = service.UpdateRowText(scenes, session, row);

        Assert.True(updated);
        Assert.Equal("First edit invalidates draft", session.Document.RowsById[row.InteractionId].Text);
        Assert.Same(scene, row.OwnerScene);
    }

    [Fact]
    public void UpdateRowTextUsesKnownLocationAndLeavesUnrelatedRowsUntouched()
    {
        var service = new WorkbenchDraftService();
        var firstScene = SceneTableViewModel.CreateManual(1);
        var secondScene = SceneTableViewModel.CreateManual(2);
        var editedRow = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var sameSceneUnchanged = AnalysisResultRowViewModel.CreateManualDialogue(2, "Lin");
        var otherSceneRow = AnalysisResultRowViewModel.CreateManualDialogue(1, "Mo");
        firstScene.Rows.Add(editedRow);
        firstScene.Rows.Add(sameSceneUnchanged);
        secondScene.Rows.Add(otherSceneRow);
        var scenes = new ObservableCollection<SceneTableViewModel>([firstScene, secondScene]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);
        var sameSceneDraft = sameSceneUnchanged.AppliedDraft;
        var otherSceneDraft = secondScene.AppliedDraft;
        var sameSceneNotifications = 0;
        var otherSceneNotifications = 0;
        sameSceneUnchanged.PropertyChanged += (_, _) => sameSceneNotifications++;
        secondScene.PropertyChanged += (_, _) => otherSceneNotifications++;

        editedRow.LineText = "Updated line";
        var updated = service.UpdateRowText(scenes, session, editedRow);

        Assert.True(updated);
        Assert.Equal("Updated line", session.Document.Scenes[0].Rows[0].Text);
        Assert.Same(session.Document.Scenes[0].Rows[0], editedRow.AppliedDraft);
        Assert.Same(sameSceneDraft, sameSceneUnchanged.AppliedDraft);
        Assert.Same(otherSceneDraft, secondScene.AppliedDraft);
        Assert.Equal(0, sameSceneNotifications);
        Assert.Equal(0, otherSceneNotifications);
    }

    [Fact]
    public void UpdateRowTextWithResultReportsChangedRowWithoutDerivedRefresh()
    {
        var service = new WorkbenchDraftService();
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(row);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);

        row.LineText = "Updated line";
        var result = service.UpdateRowTextWithResult(
            scenes,
            session,
            row,
            UpdateRowTextCommand.FromRow(row));

        Assert.True(result.CommandResult.Applied);
        Assert.Equal([scene.SceneId], result.CommandResult.ChangedSceneIds);
        Assert.Equal([row.InteractionId], result.CommandResult.ChangedRowIds);
        Assert.Null(result.BindingTargetRefresh);
        Assert.Null(result.PreviewAssetRefresh);
    }

    [Fact]
    public void UpdateRowTextRejectsRowWhenAppliedDraftSceneNoLongerContainsIt()
    {
        var service = new WorkbenchDraftService();
        var scene = SceneTableViewModel.CreateManual(1);
        var detachedScene = SceneTableViewModel.CreateManual(2);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(row);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene, detachedScene]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);
        scene.Rows.Remove(row);

        row.LineText = "Detached line";
        var updated = service.UpdateRowText(scenes, session, row);

        Assert.False(updated);
        Assert.NotEqual("Detached line", session.Document.RowsById[row.InteractionId].Text);
        Assert.Null(row.OwnerScene);
    }

    [Fact]
    public void MoveRowAcrossScenesUpdatesOwnerScene()
    {
        var service = new WorkbenchDraftService();
        var sourceScene = SceneTableViewModel.CreateManual(1);
        var targetScene = SceneTableViewModel.CreateManual(2);
        var dragged = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var target = AnalysisResultRowViewModel.CreateManualDialogue(1, "Lin");
        sourceScene.Rows.Add(dragged);
        targetScene.Rows.Add(target);
        var scenes = new ObservableCollection<SceneTableViewModel>([sourceScene, targetScene]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);

        var moved = service.MoveRowBeforeTarget(scenes, session, dragged, target, out var movedTargetScene);

        Assert.True(moved);
        Assert.Same(targetScene, movedTargetScene);
        Assert.Same(targetScene, dragged.OwnerScene);
        Assert.Same(targetScene, target.OwnerScene);
        Assert.DoesNotContain(dragged, sourceScene.Rows);
        Assert.Same(dragged, targetScene.Rows[0]);
    }

    [Fact]
    public void MoveRowAcrossScenesAppliesOnlyAffectedSceneCollections()
    {
        var service = new WorkbenchDraftService();
        var sourceScene = SceneTableViewModel.CreateManual(1);
        var targetScene = SceneTableViewModel.CreateManual(2);
        var unaffectedScene = SceneTableViewModel.CreateManual(3);
        var dragged = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var target = AnalysisResultRowViewModel.CreateManualDialogue(1, "Lin");
        var unaffected = AnalysisResultRowViewModel.CreateManualDialogue(1, "Mo");
        sourceScene.Rows.Add(dragged);
        targetScene.Rows.Add(target);
        unaffectedScene.Rows.Add(unaffected);
        var scenes = new ObservableCollection<SceneTableViewModel>([sourceScene, targetScene, unaffectedScene]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);
        var sourceCollectionNotifications = 0;
        var targetCollectionNotifications = 0;
        var unaffectedNotifications = 0;
        sourceScene.Rows.CollectionChanged += (_, _) => sourceCollectionNotifications++;
        targetScene.Rows.CollectionChanged += (_, _) => targetCollectionNotifications++;
        unaffectedScene.PropertyChanged += (_, _) => unaffectedNotifications++;

        var moved = service.MoveRowBeforeTarget(scenes, session, dragged, target, out var movedTargetScene);

        Assert.True(moved);
        Assert.Same(targetScene, movedTargetScene);
        Assert.Same(dragged, targetScene.Rows[0]);
        Assert.Same(unaffectedScene, scenes[2]);
        Assert.Same(session.Document.Scenes.Single(scene => scene.SceneId == sourceScene.SceneId), sourceScene.AppliedDraft);
        Assert.Same(session.Document.Scenes.Single(scene => scene.SceneId == targetScene.SceneId), targetScene.AppliedDraft);
        Assert.Same(session.Document.Scenes.Single(scene => scene.SceneId == unaffectedScene.SceneId), unaffectedScene.AppliedDraft);
        Assert.True(sourceCollectionNotifications > 0);
        Assert.True(targetCollectionNotifications > 0);
        Assert.Equal(0, unaffectedNotifications);
    }

    [Fact]
    public void MoveRowAcrossLargeWorkbenchLeavesUnrelatedRowsAndScenesUntouched()
    {
        var service = new WorkbenchDraftService();
        var scenes = new ObservableCollection<SceneTableViewModel>();
        for (var sceneIndex = 0; sceneIndex < 8; sceneIndex++)
        {
            var scene = SceneTableViewModel.CreateManual(sceneIndex + 1);
            for (var rowIndex = 0; rowIndex < 25; rowIndex++)
            {
                scene.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(rowIndex + 1, $"Character {sceneIndex}-{rowIndex}"));
            }

            scenes.Add(scene);
        }

        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);
        var sourceScene = scenes[2];
        var targetScene = scenes[5];
        var unrelatedScene = scenes[7];
        var dragged = sourceScene.Rows[10];
        var target = targetScene.Rows[12];
        var sourceUnchangedBeforeRange = sourceScene.Rows[0];
        var sourceUnchangedAfterRange = sourceScene.Rows[24];
        var targetUnchangedBeforeRange = targetScene.Rows[0];
        var targetUnchangedAfterRange = targetScene.Rows[24];
        var unrelatedRows = unrelatedScene.Rows.ToArray();
        var unrelatedSceneNotifications = 0;
        var unrelatedRowNotifications = 0;
        var sourceBeforeNotifications = 0;
        var targetBeforeNotifications = 0;
        var sourceAfterNotifications = new List<string>();
        var targetAfterNotifications = new List<string>();
        unrelatedScene.PropertyChanged += (_, _) => unrelatedSceneNotifications++;
        foreach (var row in unrelatedRows)
        {
            row.PropertyChanged += (_, _) => unrelatedRowNotifications++;
        }

        sourceUnchangedBeforeRange.PropertyChanged += (_, _) => sourceBeforeNotifications++;
        sourceUnchangedAfterRange.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                sourceAfterNotifications.Add(args.PropertyName);
            }
        };
        targetUnchangedBeforeRange.PropertyChanged += (_, _) => targetBeforeNotifications++;
        targetUnchangedAfterRange.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                targetAfterNotifications.Add(args.PropertyName);
            }
        };

        var moved = service.MoveRowToTarget(
            scenes,
            session,
            dragged,
            target,
            WorkbenchDropPosition.After,
            out var movedTargetScene);

        Assert.True(moved);
        Assert.Same(targetScene, movedTargetScene);
        Assert.DoesNotContain(dragged, sourceScene.Rows);
        Assert.Same(dragged, targetScene.Rows[13]);
        Assert.Same(sourceUnchangedBeforeRange, sourceScene.Rows[0]);
        Assert.Same(sourceUnchangedAfterRange, sourceScene.Rows[^1]);
        Assert.Same(targetUnchangedBeforeRange, targetScene.Rows[0]);
        Assert.Same(targetUnchangedAfterRange, targetScene.Rows[^1]);
        Assert.Equal(unrelatedRows, unrelatedScene.Rows);
        Assert.Equal(0, unrelatedSceneNotifications);
        Assert.Equal(0, unrelatedRowNotifications);
        Assert.Equal(0, sourceBeforeNotifications);
        Assert.Equal(0, targetBeforeNotifications);
        Assert.Equal(2, sourceAfterNotifications.Count);
        Assert.Contains(nameof(AnalysisResultRowViewModel.Order), sourceAfterNotifications);
        Assert.Contains(nameof(AnalysisResultRowViewModel.OrderLabel), sourceAfterNotifications);
        Assert.Equal(2, targetAfterNotifications.Count);
        Assert.Contains(nameof(AnalysisResultRowViewModel.Order), targetAfterNotifications);
        Assert.Contains(nameof(AnalysisResultRowViewModel.OrderLabel), targetAfterNotifications);
    }

    [Fact]
    public void PreviewMoveRowAcrossScenesMovesCollectionsWithoutOrderNotifications()
    {
        var service = new WorkbenchDraftService();
        var scenes = new ObservableCollection<SceneTableViewModel>();
        for (var sceneIndex = 0; sceneIndex < 4; sceneIndex++)
        {
            var scene = SceneTableViewModel.CreateManual(sceneIndex + 1);
            for (var rowIndex = 0; rowIndex < 8; rowIndex++)
            {
                scene.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(rowIndex + 1, $"Character {sceneIndex}-{rowIndex}"));
            }

            scenes.Add(scene);
        }

        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);
        var sourceScene = scenes[1];
        var targetScene = scenes[2];
        var unrelatedScene = scenes[3];
        var dragged = sourceScene.Rows[3];
        var target = targetScene.Rows[4];
        var affectedRows = sourceScene.Rows.Concat(targetScene.Rows).ToArray();
        var unrelatedRows = unrelatedScene.Rows.ToArray();
        var affectedOrderNotifications = new List<string>();
        var unrelatedNotifications = 0;
        foreach (var row in affectedRows)
        {
            row.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(AnalysisResultRowViewModel.Order)
                    or nameof(AnalysisResultRowViewModel.OrderLabel))
                {
                    affectedOrderNotifications.Add($"{row.InteractionId}:{args.PropertyName}");
                }
            };
        }

        unrelatedScene.PropertyChanged += (_, _) => unrelatedNotifications++;
        foreach (var row in unrelatedRows)
        {
            row.PropertyChanged += (_, _) => unrelatedNotifications++;
        }

        var moved = service.MoveRowToTarget(
            scenes,
            session,
            dragged,
            target,
            WorkbenchDropPosition.After,
            out var movedTargetScene,
            preview: true);

        Assert.True(moved);
        Assert.Same(targetScene, movedTargetScene);
        Assert.DoesNotContain(dragged, sourceScene.Rows);
        Assert.Same(dragged, targetScene.Rows[5]);
        Assert.Empty(affectedOrderNotifications);
        Assert.Equal(0, unrelatedNotifications);
    }

    [Fact]
    public void PreviewMoveRowDoesNotReturnBindingRefresh()
    {
        var service = new WorkbenchDraftService();
        var sourceScene = SceneTableViewModel.CreateManual(1);
        var targetScene = SceneTableViewModel.CreateManual(2);
        var dragged = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var target = AnalysisResultRowViewModel.CreateManualDialogue(1, "Lin");
        sourceScene.Rows.Add(dragged);
        targetScene.Rows.Add(target);
        var scenes = new ObservableCollection<SceneTableViewModel>([sourceScene, targetScene]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);

        var result = service.MoveRowToTargetWithResult(
            scenes,
            session,
            dragged,
            target,
            WorkbenchDropPosition.After,
            preview: true);

        Assert.True(result.CommandResult.Applied);
        Assert.Null(result.BindingTargetRefresh);
        Assert.Equal(WorkbenchDerivedStateRefresh.SessionOnly, result.DerivedStateRefresh);
        Assert.Same(targetScene, result.TargetScene);
        Assert.DoesNotContain(dragged, sourceScene.Rows);
        Assert.Same(dragged, targetScene.Rows[1]);
        Assert.Equal(1, dragged.Order);
        Assert.Equal(1, target.Order);
    }

    [Fact]
    public void CompleteStyleMoveRowReturnsAffectedRowsBindingRefresh()
    {
        var service = new WorkbenchDraftService();
        var scene = SceneTableViewModel.CreateManual(1);
        var rows = Enumerable.Range(1, 4)
            .Select(index => AnalysisResultRowViewModel.CreateManualDialogue(index, $"Character {index}"))
            .ToArray();
        foreach (var row in rows)
        {
            scene.Rows.Add(row);
        }

        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);

        var result = service.MoveRowToTargetWithResult(
            scenes,
            session,
            rows[0],
            rows[2],
            WorkbenchDropPosition.After);

        Assert.True(result.CommandResult.Applied);
        Assert.NotNull(result.BindingTargetRefresh);
        var refresh = result.BindingTargetRefresh!;
        Assert.Equal(WorkbenchBindingTargetRefreshKind.Rows, refresh.Kind);
        var refreshRows = Assert.IsAssignableFrom<IReadOnlyList<AnalysisResultRowViewModel>>(refresh.Rows);
        Assert.Equal(result.CommandResult.ChangedRowIds, refreshRows.Select(row => row.InteractionId).ToArray());
        Assert.Equal([rows[1], rows[2], rows[0]], refreshRows);
        Assert.Equal(WorkbenchDerivedStateRefresh.SessionOnly, result.DerivedStateRefresh);
    }

    [Fact]
    public void PreviewMoveSceneDoesNotReturnCompleteOnlyBindingRefresh()
    {
        var service = new WorkbenchDraftService();
        var first = SceneTableViewModel.CreateManual(1);
        var second = SceneTableViewModel.CreateManual(2);
        var scenes = new ObservableCollection<SceneTableViewModel>([first, second]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);

        var result = service.MoveSceneWithResult(
            scenes,
            session,
            first,
            second,
            preview: true);

        Assert.True(result.CommandResult.Applied);
        Assert.Null(result.BindingTargetRefresh);
        Assert.Equal(WorkbenchDerivedStateRefresh.SessionOnly, result.DerivedStateRefresh);
        Assert.Same(first, scenes[1]);
        Assert.Equal(1, first.Order);
        Assert.Equal(2, second.Order);
    }

    [Fact]
    public void CompleteStyleMoveSceneReturnsSceneOrderRangeBindingRefresh()
    {
        var service = new WorkbenchDraftService();
        var first = SceneTableViewModel.CreateManual(1);
        var second = SceneTableViewModel.CreateManual(2);
        var scenes = new ObservableCollection<SceneTableViewModel>([first, second]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);

        var result = service.MoveSceneWithResult(
            scenes,
            session,
            first,
            second);

        Assert.True(result.CommandResult.Applied);
        Assert.Equal(WorkbenchBindingTargetRefreshKind.SceneOrderRange, result.BindingTargetRefresh?.Kind);
        Assert.Equal([second, first], result.BindingTargetRefresh?.Scenes);
        Assert.Equal(WorkbenchDerivedStateRefresh.SessionOnly, result.DerivedStateRefresh);
        Assert.Same(first, scenes[1]);
        Assert.Equal(2, first.Order);
        Assert.Equal(1, second.Order);
    }

    [Fact]
    public void AddRowInLargeWorkbenchLeavesUnrelatedRowsAndScenesUntouched()
    {
        var service = new WorkbenchDraftService();
        var scenes = new ObservableCollection<SceneTableViewModel>();
        for (var sceneIndex = 0; sceneIndex < 6; sceneIndex++)
        {
            var scene = SceneTableViewModel.CreateManual(sceneIndex + 1);
            for (var rowIndex = 0; rowIndex < 20; rowIndex++)
            {
                scene.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(rowIndex + 1, $"Character {sceneIndex}-{rowIndex}"));
            }

            scenes.Add(scene);
        }

        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);
        var targetScene = scenes[2];
        var selectedRow = targetScene.Rows[10];
        var unrelatedScene = scenes[5];
        var unrelatedRows = unrelatedScene.Rows.ToArray();
        var sceneCollectionNotifications = 0;
        var unrelatedSceneNotifications = 0;
        var unrelatedRowNotifications = 0;
        scenes.CollectionChanged += (_, _) => sceneCollectionNotifications++;
        unrelatedScene.PropertyChanged += (_, _) => unrelatedSceneNotifications++;
        foreach (var unrelatedRow in unrelatedRows)
        {
            unrelatedRow.PropertyChanged += (_, _) => unrelatedRowNotifications++;
        }

        var addedRow = service.AddDialogueRow(scenes, session, targetScene, selectedRow, "New Character");

        Assert.Same(targetScene, addedRow.OwnerScene);
        Assert.Same(addedRow, targetScene.Rows[10]);
        Assert.Equal(21, targetScene.Rows.Count);
        Assert.Equal(Enumerable.Range(1, targetScene.Rows.Count), targetScene.Rows.Select(item => item.Order));
        Assert.Equal(addedRow.InteractionId, session.Document.Scenes[2].Rows[10].InteractionId);
        Assert.Equal(unrelatedRows, unrelatedScene.Rows);
        Assert.Equal(0, sceneCollectionNotifications);
        Assert.Equal(0, unrelatedSceneNotifications);
        Assert.Equal(0, unrelatedRowNotifications);
    }

    [Fact]
    public void AddRowWithResultReportsChangedSceneAndRow()
    {
        var service = new WorkbenchDraftService();
        var scene = SceneTableViewModel.CreateManual(1);
        var existing = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(existing);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);

        var result = service.AddDialogueRowWithResult(scenes, session, scene, existing, "Lin");

        Assert.True(result.CommandResult.Applied);
        Assert.Same(result.CommandResult.Document, session.Document);
        Assert.Same(result.Row, scene.Rows[0]);
        Assert.Same(result.Row, result.SuggestedSelectedRow);
        Assert.Same(result.Row, result.CommandResult.SuggestedSelectedRow);
        Assert.Equal(WorkbenchBindingTargetRefreshKind.Row, result.BindingTargetRefresh?.Kind);
        Assert.Same(result.Row, result.BindingTargetRefresh?.Row);
        Assert.Equal([scene.SceneId], result.CommandResult.ChangedSceneIds);
        Assert.Equal([result.Row!.InteractionId], result.CommandResult.ChangedRowIds);
        Assert.Empty(result.CommandResult.RemovedSceneIds);
        Assert.Empty(result.CommandResult.RemovedRowIds);
    }

    [Fact]
    public void RemoveRowInLargeWorkbenchLeavesUnrelatedRowsAndScenesUntouched()
    {
        var service = new WorkbenchDraftService();
        var scenes = new ObservableCollection<SceneTableViewModel>();
        for (var sceneIndex = 0; sceneIndex < 6; sceneIndex++)
        {
            var scene = SceneTableViewModel.CreateManual(sceneIndex + 1);
            for (var rowIndex = 0; rowIndex < 20; rowIndex++)
            {
                scene.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(rowIndex + 1, $"Character {sceneIndex}-{rowIndex}"));
            }

            scenes.Add(scene);
        }

        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);
        var sourceScene = scenes[2];
        var removedRow = sourceScene.Rows[10];
        var unrelatedScene = scenes[5];
        var unrelatedRows = unrelatedScene.Rows.ToArray();
        var sceneCollectionNotifications = 0;
        var unrelatedSceneNotifications = 0;
        var unrelatedRowNotifications = 0;
        scenes.CollectionChanged += (_, _) => sceneCollectionNotifications++;
        unrelatedScene.PropertyChanged += (_, _) => unrelatedSceneNotifications++;
        foreach (var row in unrelatedRows)
        {
            row.PropertyChanged += (_, _) => unrelatedRowNotifications++;
        }

        var removed = service.RemoveRow(scenes, session, removedRow, out var source);

        Assert.True(removed);
        Assert.Same(sourceScene, source);
        Assert.Null(removedRow.OwnerScene);
        Assert.DoesNotContain(removedRow, sourceScene.Rows);
        Assert.Equal(19, sourceScene.Rows.Count);
        Assert.Equal(Enumerable.Range(1, sourceScene.Rows.Count), sourceScene.Rows.Select(item => item.Order));
        Assert.False(session.Document.RowsById.ContainsKey(removedRow.InteractionId));
        Assert.Equal(unrelatedRows, unrelatedScene.Rows);
        Assert.Equal(0, sceneCollectionNotifications);
        Assert.Equal(0, unrelatedSceneNotifications);
        Assert.Equal(0, unrelatedRowNotifications);
    }

    [Fact]
    public void RemoveRowWithResultReportsRemovedRowAndChangedScene()
    {
        var service = new WorkbenchDraftService();
        var scene = SceneTableViewModel.CreateManual(1);
        var removedRow = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var remainingRow = AnalysisResultRowViewModel.CreateManualDialogue(2, "Lin");
        scene.Rows.Add(removedRow);
        scene.Rows.Add(remainingRow);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);

        var result = service.RemoveRowWithResult(scenes, session, removedRow);

        Assert.True(result.CommandResult.Applied);
        Assert.Same(scene, result.SourceScene);
        Assert.Equal([scene.SceneId], result.CommandResult.ChangedSceneIds);
        Assert.Empty(result.CommandResult.ChangedRowIds);
        Assert.Empty(result.CommandResult.RemovedSceneIds);
        Assert.Equal([removedRow.InteractionId], result.CommandResult.RemovedRowIds);
        Assert.NotNull(result.RemovedRow);
        Assert.Equal(removedRow.InteractionId, result.RemovedRow.InteractionId);
        Assert.Equal("Chen", result.RemovedRow.SubjectName);
        Assert.Equal(WorkbenchBindingTargetRefreshKind.RowSubjectChange, result.BindingTargetRefresh?.Kind);
        Assert.Same(removedRow, result.BindingTargetRefresh?.Row);
        Assert.Same(result.RemovedRow, result.BindingTargetRefresh?.PreviousRowDraft);
        Assert.False(session.Document.RowsById.ContainsKey(removedRow.InteractionId));
    }

    [Fact]
    public void AddSceneKeepsExistingRowsUntouched()
    {
        var service = new WorkbenchDraftService();
        var first = SceneTableViewModel.CreateManual(1);
        var second = SceneTableViewModel.CreateManual(2);
        first.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen"));
        second.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(1, "Lin"));
        var scenes = new ObservableCollection<SceneTableViewModel>([first, second]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);
        var firstSceneDraft = first.AppliedDraft;
        var secondSceneDraft = second.AppliedDraft;
        var firstRowDraft = first.Rows[0].AppliedDraft;
        var secondRowDraft = second.Rows[0].AppliedDraft;
        var firstSceneNotifications = 0;
        var secondSceneNotifications = 0;
        var firstRowNotifications = 0;
        var secondRowNotifications = 0;
        first.PropertyChanged += (_, _) => firstSceneNotifications++;
        second.PropertyChanged += (_, _) => secondSceneNotifications++;
        first.Rows[0].PropertyChanged += (_, _) => firstRowNotifications++;
        second.Rows[0].PropertyChanged += (_, _) => secondRowNotifications++;

        var scene = service.AddScene(scenes, session);

        Assert.Same(scene, scenes[^1]);
        Assert.Equal(3, scenes.Count);
        Assert.Equal([1, 2, 3], scenes.Select(item => item.Order));
        Assert.Same(session.Document.Scenes.Single(draft => draft.SceneId == scene.SceneId), scene.AppliedDraft);
        Assert.Same(firstSceneDraft, first.AppliedDraft);
        Assert.Same(secondSceneDraft, second.AppliedDraft);
        Assert.Equal(0, firstSceneNotifications);
        Assert.Equal(0, secondSceneNotifications);
        Assert.Same(firstRowDraft, first.Rows[0].AppliedDraft);
        Assert.Same(secondRowDraft, second.Rows[0].AppliedDraft);
        Assert.Equal(0, firstRowNotifications);
        Assert.Equal(0, secondRowNotifications);
    }

    [Fact]
    public void RemoveSceneKeepsRemainingRowsUntouched()
    {
        var service = new WorkbenchDraftService();
        var first = SceneTableViewModel.CreateManual(1);
        var second = SceneTableViewModel.CreateManual(2);
        var third = SceneTableViewModel.CreateManual(3);
        first.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen"));
        second.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(1, "Lin"));
        third.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(1, "Mo"));
        var scenes = new ObservableCollection<SceneTableViewModel>([first, second, third]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);
        var firstRowDraft = first.Rows[0].AppliedDraft;
        var thirdRowDraft = third.Rows[0].AppliedDraft;
        var firstRowNotifications = 0;
        var thirdRowNotifications = 0;
        first.Rows[0].PropertyChanged += (_, _) => firstRowNotifications++;
        third.Rows[0].PropertyChanged += (_, _) => thirdRowNotifications++;

        var removed = service.RemoveScene(scenes, session, second);

        Assert.True(removed);
        Assert.Equal([first.SceneId, third.SceneId], scenes.Select(item => item.SceneId));
        Assert.Equal([1, 2], scenes.Select(item => item.Order));
        Assert.Same(firstRowDraft, first.Rows[0].AppliedDraft);
        Assert.Same(thirdRowDraft, third.Rows[0].AppliedDraft);
        Assert.Equal(0, firstRowNotifications);
        Assert.Equal(0, thirdRowNotifications);
        Assert.False(session.Document.TryGetSceneDraft(second.SceneId, out _));
    }

    [Fact]
    public void RemoveSceneWithResultReportsRemovedSceneAndRows()
    {
        var service = new WorkbenchDraftService();
        var first = SceneTableViewModel.CreateManual(1);
        var second = SceneTableViewModel.CreateManual(2);
        var firstRow = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var secondRow = AnalysisResultRowViewModel.CreateManualDialogue(2, "Lin");
        first.Rows.Add(firstRow);
        first.Rows.Add(secondRow);
        var scenes = new ObservableCollection<SceneTableViewModel>([first, second]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);

        var result = service.RemoveSceneWithResult(scenes, session, first);

        Assert.True(result.CommandResult.Applied);
        Assert.Equal([first.SceneId], result.CommandResult.RemovedSceneIds);
        Assert.Equal([firstRow.InteractionId, secondRow.InteractionId], result.CommandResult.RemovedRowIds);
        Assert.Equal([firstRow.InteractionId, secondRow.InteractionId], result.RemovedRows.Select(row => row.InteractionId));
        Assert.Equal(["Chen", "Lin"], result.RemovedRows.Select(row => row.SubjectName));
        Assert.Equal(WorkbenchBindingTargetRefreshKind.RemoveScene, result.BindingTargetRefresh?.Kind);
        Assert.Same(first, result.BindingTargetRefresh?.Scene);
        Assert.Same(result.RemovedRows, result.BindingTargetRefresh?.RemovedRows);
        Assert.Empty(result.CommandResult.ChangedSceneIds);
        Assert.Empty(result.CommandResult.ChangedRowIds);
        Assert.DoesNotContain(first, scenes);
        Assert.DoesNotContain(session.Document.Scenes, scene => scene.SceneId == first.SceneId);
    }

    [Fact]
    public void RemoveSceneOnlyMarksAndRenumbersFollowingScenes()
    {
        var service = new WorkbenchDraftService();
        var scenes = new ObservableCollection<SceneTableViewModel>();
        for (var index = 0; index < 6; index++)
        {
            var scene = SceneTableViewModel.CreateManual(index + 1);
            scene.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(1, $"Character {index}"));
            scenes.Add(scene);
        }

        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);
        var orderedScenes = scenes.ToArray();
        var beforeRemoved = orderedScenes[0];
        var afterRemoved = orderedScenes[5];
        var beforeRemovedAppliedDraft = beforeRemoved.AppliedDraft;
        var beforeRemovedNotifications = 0;
        var afterRemovedNotifications = new List<string>();
        beforeRemoved.PropertyChanged += (_, _) => beforeRemovedNotifications++;
        afterRemoved.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                afterRemovedNotifications.Add(args.PropertyName);
            }
        };

        var removed = service.RemoveScene(scenes, session, orderedScenes[2]);

        Assert.True(removed);
        Assert.Equal(
            [
                orderedScenes[0].SceneId,
                orderedScenes[1].SceneId,
                orderedScenes[3].SceneId,
                orderedScenes[4].SceneId,
                orderedScenes[5].SceneId
            ],
            scenes.Select(scene => scene.SceneId));
        Assert.Same(beforeRemovedAppliedDraft, beforeRemoved.AppliedDraft);
        Assert.Same(session.Document.Scenes[4], afterRemoved.AppliedDraft);
        Assert.Equal(0, beforeRemovedNotifications);
        Assert.Contains(nameof(SceneTableViewModel.Order), afterRemovedNotifications);
        Assert.Equal(Enumerable.Range(1, scenes.Count), scenes.Select(scene => scene.Order));
    }

    [Fact]
    public void UpdateSceneTextUsesKnownLocationAndLeavesRowsUntouched()
    {
        var service = new WorkbenchDraftService();
        var first = SceneTableViewModel.CreateManual(1);
        var second = SceneTableViewModel.CreateManual(2);
        var firstRow = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var secondRow = AnalysisResultRowViewModel.CreateManualDialogue(1, "Lin");
        first.Rows.Add(firstRow);
        second.Rows.Add(secondRow);
        var scenes = new ObservableCollection<SceneTableViewModel>([first, second]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);
        var firstRowDraft = firstRow.AppliedDraft;
        var secondSceneDraft = second.AppliedDraft;
        var firstRowNotifications = 0;
        var secondSceneNotifications = 0;
        firstRow.PropertyChanged += (_, _) => firstRowNotifications++;
        second.PropertyChanged += (_, _) => secondSceneNotifications++;

        first.SceneName = "Updated scene";
        first.SceneEffect = "Updated effect";
        var updated = service.UpdateSceneText(scenes, session, first);

        Assert.True(updated);
        Assert.Equal("Updated scene", session.Document.Scenes[0].Name);
        Assert.Equal("Updated effect", session.Document.Scenes[0].Effect);
        Assert.Same(firstRowDraft, firstRow.AppliedDraft);
        Assert.Same(secondSceneDraft, second.AppliedDraft);
        Assert.Equal(0, firstRowNotifications);
        Assert.Equal(0, secondSceneNotifications);
    }

    [Fact]
    public void UpdateRowSubjectWithResultReportsSubjectBindingRefreshWithPreviousDraft()
    {
        var service = new WorkbenchDraftService();
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(row);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);
        var previousDraft = session.Document.RowsById[row.InteractionId];

        row.SubjectName = "Lin";
        var result = service.UpdateRowSubjectWithResult(
            scenes,
            session,
            row,
            UpdateRowSubjectCommand.FromRow(row));

        Assert.True(result.CommandResult.Applied);
        Assert.Equal([scene.SceneId], result.CommandResult.ChangedSceneIds);
        Assert.Equal([row.InteractionId], result.CommandResult.ChangedRowIds);
        Assert.Equal(WorkbenchBindingTargetRefreshKind.RowSubjectChange, result.BindingTargetRefresh?.Kind);
        Assert.Same(row, result.BindingTargetRefresh?.Row);
        Assert.Same(previousDraft, result.BindingTargetRefresh?.PreviousRowDraft);
        Assert.Null(result.PreviewAssetRefresh);
        Assert.False(result.DerivedStateRefresh.RefreshCounters);
    }

    [Fact]
    public void UpdateRowSubjectCompatibilityOverloadResolvesProjectCharacterId()
    {
        var service = new WorkbenchDraftService();
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(row);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);
        var characters = new[]
        {
            new Character("char_chen", "Chen", "角色", string.Empty, string.Empty, string.Empty, new AssetBinding()),
            new Character("char_lin", "Lin", "角色", string.Empty, string.Empty, string.Empty, new AssetBinding())
        };

        row.SubjectName = "Lin";
        var applied = service.UpdateRowSubject(scenes, session, row, characters);

        Assert.True(applied);
        Assert.Equal("char_lin", session.Document.RowsById[row.InteractionId].SubjectCharacterId);
        Assert.Equal("char_lin", row.SubjectCharacterId);
    }

    [Fact]
    public void UpdateRowSubjectWithResultRequestsCounterRefreshWhenNarrationStateChanges()
    {
        var service = new WorkbenchDraftService();
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(row);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);

        row.SubjectName = "旁白";
        var result = service.UpdateRowSubjectWithResult(
            scenes,
            session,
            row,
            UpdateRowSubjectCommand.FromRow(row));

        Assert.True(result.CommandResult.Applied);
        Assert.True(result.DerivedStateRefresh.RefreshCounters);
    }

    [Fact]
    public void UpdateSceneTextWithResultReportsSceneBindingRefreshWithoutPreviewRefresh()
    {
        var service = new WorkbenchDraftService();
        var scene = SceneTableViewModel.CreateManual(1);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);

        scene.SceneName = "Updated scene";
        var result = service.UpdateSceneTextWithResult(
            scenes,
            session,
            scene,
            UpdateSceneTextCommand.FromScene(scene));

        Assert.True(result.CommandResult.Applied);
        Assert.Equal([scene.SceneId], result.CommandResult.ChangedSceneIds);
        Assert.Empty(result.CommandResult.ChangedRowIds);
        Assert.Equal(WorkbenchBindingTargetRefreshKind.Scene, result.BindingTargetRefresh?.Kind);
        Assert.Same(scene, result.BindingTargetRefresh?.Scene);
        Assert.Null(result.PreviewAssetRefresh);
        Assert.False(result.DerivedStateRefresh.RefreshCounters);
    }

    [Fact]
    public void UpdateSceneAssetUsesKnownLocationAndLeavesOtherScenesUntouched()
    {
        var service = new WorkbenchDraftService();
        var first = SceneTableViewModel.CreateManual(1);
        var second = SceneTableViewModel.CreateManual(2);
        second.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(1, "Lin"));
        var scenes = new ObservableCollection<SceneTableViewModel>([first, second]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);
        var secondDraft = second.AppliedDraft;
        var secondNotifications = 0;
        second.PropertyChanged += (_, _) => secondNotifications++;

        first.SelectedSceneAsset = "asset_scene_001";
        var updated = service.UpdateSceneAsset(scenes, session, first);

        Assert.True(updated);
        Assert.Equal("asset_scene_001", SceneVisualId(session.Document.Scenes[0]));
        Assert.Same(secondDraft, second.AppliedDraft);
        Assert.Equal(0, secondNotifications);
    }

    [Fact]
    public void UpdateSceneAssetWithResultReportsSceneBindingAndPreviewRefresh()
    {
        var service = new WorkbenchDraftService();
        var scene = SceneTableViewModel.CreateManual(1);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);

        scene.SelectedSceneAsset = "asset_scene_001";
        var result = service.UpdateSceneAssetWithResult(
            scenes,
            session,
            scene,
            UpdateSceneBindingCommand.FromScene(scene));

        Assert.True(result.CommandResult.Applied);
        Assert.Equal([scene.SceneId], result.CommandResult.ChangedSceneIds);
        Assert.Equal(WorkbenchBindingTargetRefreshKind.Scene, result.BindingTargetRefresh?.Kind);
        Assert.Same(scene, result.BindingTargetRefresh?.Scene);
        Assert.Equal(WorkbenchPreviewAssetRefreshKind.Scene, result.PreviewAssetRefresh?.Kind);
        Assert.Same(scene, result.PreviewAssetRefresh?.Scene);
        Assert.False(result.DerivedStateRefresh.RefreshCounters);
    }

    [Fact]
    public void UpdateSceneVisualBindingWithResultRejectsPsdSourceAndKeepsOnlyRenderedImage()
    {
        var service = new WorkbenchDraftService();
        var scene = SceneTableViewModel.CreateManual(1);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);

        var result = service.UpdateSceneVisualBindingWithResult(
            scenes,
            session,
            scene,
            VisualAssetBinding.FromPsd("asset_psd_scene", "asset_png_scene_rendered"));

        Assert.True(result.CommandResult.Applied);
        Assert.Equal([scene.SceneId], result.CommandResult.ChangedSceneIds);
        Assert.Equal(WorkbenchBindingTargetRefreshKind.Scene, result.BindingTargetRefresh?.Kind);
        Assert.Equal(WorkbenchPreviewAssetRefreshKind.Scene, result.PreviewAssetRefresh?.Kind);
        Assert.False(result.DerivedStateRefresh.RefreshCounters);

        var sceneDraft = Assert.Single(session.Document.Scenes);
        Assert.Equal("asset_png_scene_rendered", SceneVisualId(sceneDraft));
        Assert.Equal(VisualSourceKind.Image, sceneDraft.VisualBinding?.SourceKind);
        Assert.Equal("asset_png_scene_rendered", sceneDraft.VisualBinding?.SourceAssetId);
        Assert.Equal("asset_png_scene_rendered", sceneDraft.VisualBinding?.RenderedAssetId);
        Assert.Equal("asset_png_scene_rendered", scene.SelectedSceneAsset);
    }

    [Fact]
    public void EquivalentSceneAssetUpdateReturnsFalseAndDoesNotAddUndoHistory()
    {
        var service = new WorkbenchDraftService();
        var scene = SceneTableViewModel.CreateManual(1);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);

        scene.SelectedSceneAsset = "未绑定图片";
        var updated = service.UpdateSceneAsset(scenes, session, scene);

        Assert.False(updated);
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.Null(SceneVisualId(session.Document.Scenes[0]));
    }

    [Fact]
    public void EquivalentRowBindingUpdateReturnsFalseAndDoesNotAddUndoHistory()
    {
        var service = new WorkbenchDraftService();
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(row);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);

        row.SelectedCharacterAsset = "未绑定图片";
        row.SelectedVoiceReference = "未绑定声音";
        var updated = service.UpdateRowBinding(scenes, session, row);

        Assert.False(updated);
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.Null(RowVisualId(session.Document.RowsById[row.InteractionId]));
        Assert.Null(RowVoiceId(session.Document.RowsById[row.InteractionId]));
    }

    [Fact]
    public void UpdateRowBindingWithResultReportsPreviewRefreshOnlyWhenCharacterAssetChanges()
    {
        var service = new WorkbenchDraftService();
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        row.SelectedCharacterAsset = "asset_character_001";
        row.SelectedVoiceReference = "asset_voice_001";
        row.SelectedVoiceReferenceText = "old reference";
        scene.Rows.Add(row);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);

        row.SelectedVoiceReferenceText = "new reference";
        var voiceResult = service.UpdateRowBindingWithResult(
            scenes,
            session,
            row,
            UpdateRowBindingCommand.FromRow(row));

        Assert.True(voiceResult.CommandResult.Applied);
        Assert.Equal(WorkbenchBindingTargetRefreshKind.Row, voiceResult.BindingTargetRefresh?.Kind);
        Assert.Same(row, voiceResult.BindingTargetRefresh?.Row);
        Assert.Null(voiceResult.PreviewAssetRefresh);

        row.SelectedCharacterAsset = "asset_character_002";
        var characterResult = service.UpdateRowBindingWithResult(
            scenes,
            session,
            row,
            UpdateRowBindingCommand.FromRow(row));

        Assert.True(characterResult.CommandResult.Applied);
        Assert.Equal(WorkbenchBindingTargetRefreshKind.Row, characterResult.BindingTargetRefresh?.Kind);
        Assert.Same(row, characterResult.BindingTargetRefresh?.Row);
        Assert.Equal(WorkbenchPreviewAssetRefreshKind.Row, characterResult.PreviewAssetRefresh?.Kind);
        Assert.Same(row, characterResult.PreviewAssetRefresh?.Row);
        Assert.False(characterResult.DerivedStateRefresh.RefreshCounters);
    }

    [Fact]
    public void UpdateRowEmotionWithResultReportsChangedRowWithoutDerivedRefresh()
    {
        var service = new WorkbenchDraftService();
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(row);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);

        row.EmotionVectors.Single(vector => vector.Key == "Calm").Value = 0.2;
        var result = service.UpdateRowEmotionWithResult(
            scenes,
            session,
            row,
            UpdateRowEmotionCommand.FromRow(row));

        Assert.True(result.CommandResult.Applied);
        Assert.Equal([scene.SceneId], result.CommandResult.ChangedSceneIds);
        Assert.Equal([row.InteractionId], result.CommandResult.ChangedRowIds);
        Assert.Null(result.BindingTargetRefresh);
        Assert.Null(result.PreviewAssetRefresh);
        Assert.False(result.DerivedStateRefresh.RefreshCounters);
    }

    [Fact]
    public void EquivalentRowEmotionUpdateReturnsFalseAndDoesNotAddUndoHistory()
    {
        var service = new WorkbenchDraftService();
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(row);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);

        row.EmotionVectors.Single(vector => vector.Key == "Calm").Value = 0.6;
        var updated = service.UpdateRowEmotion(scenes, session, row);

        Assert.False(updated);
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void UpdateSceneBindingCommandFiltersDisplayPlaceholdersBeforeCoreDocument()
    {
        var service = new WorkbenchDraftService();
        var scene = SceneTableViewModel.CreateManual(1);
        scene.SelectedSceneAsset = "asset_scene_001";
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);

        Assert.Equal("asset_scene_001", SceneVisualId(session.Document.Scenes[0]));

        scene.SelectedSceneAsset = "场景预览";
        var command = UpdateSceneBindingCommand.FromScene(scene);
        var updated = service.UpdateSceneAsset(scenes, session, scene, command);

        Assert.True(updated);
        Assert.Null(command.SceneImageAssetId);
        Assert.Null(SceneVisualId(session.Document.Scenes[0]));
    }

    [Fact]
    public void UpdateRowBindingCommandFiltersDisplayPlaceholdersBeforeCoreDocument()
    {
        var service = new WorkbenchDraftService();
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        row.SelectedCharacterAsset = "asset_character_001";
        row.SelectedVoiceReference = "asset_voice_001";
        row.SelectedVoiceReferenceText = "reference line";
        scene.Rows.Add(row);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);

        var initialDraft = session.Document.RowsById[row.InteractionId];
        Assert.Equal("asset_character_001", RowVisualId(initialDraft));
        Assert.Equal("asset_voice_001", RowVoiceId(initialDraft));
        Assert.Equal("reference line", RowVoiceText(initialDraft));

        row.SelectedCharacterAsset = "人物预览";
        row.SelectedVoiceReference = "角色声音";
        row.SelectedVoiceReferenceText = "未绑定";
        var command = UpdateRowBindingCommand.FromRow(row);
        var updated = service.UpdateRowBinding(scenes, session, row, command);

        var updatedDraft = session.Document.RowsById[row.InteractionId];
        Assert.True(updated);
        Assert.Null(command.CharacterImageAssetId);
        Assert.Null(command.VoiceReferenceId);
        Assert.Null(command.VoiceReferenceText);
        Assert.Null(RowVisualId(updatedDraft));
        Assert.Null(RowVoiceId(updatedDraft));
        Assert.Null(RowVoiceText(updatedDraft));
    }

    [Fact]
    public void MoveRowWithinSceneRenumbersOnlyMovedRange()
    {
        var service = new WorkbenchDraftService();
        var scene = SceneTableViewModel.CreateManual(1);
        var rows = Enumerable.Range(1, 5)
            .Select(index => AnalysisResultRowViewModel.CreateManualDialogue(index, $"Character {index}"))
            .ToArray();
        foreach (var row in rows)
        {
            scene.Rows.Add(row);
        }

        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);
        var firstDraft = rows[0].AppliedDraft;
        var fifthDraft = rows[4].AppliedDraft;
        var notificationsByRow = rows.ToDictionary(row => row, _ => 0);
        foreach (var row in rows)
        {
            row.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(AnalysisResultRowViewModel.Order))
                {
                    notificationsByRow[row]++;
                }
            };
        }

        var moved = service.MoveRowToTarget(
            scenes,
            session,
            rows[1],
            rows[3],
            WorkbenchDropPosition.After,
            out var targetScene);

        Assert.True(moved);
        Assert.Same(scene, targetScene);
        Assert.Equal(
            [rows[0].InteractionId, rows[2].InteractionId, rows[3].InteractionId, rows[1].InteractionId, rows[4].InteractionId],
            scene.Rows.Select(row => row.InteractionId));
        Assert.Equal([1, 2, 3, 4, 5], scene.Rows.Select(row => row.Order));
        Assert.Equal(0, notificationsByRow[rows[0]]);
        Assert.True(notificationsByRow[rows[1]] > 0);
        Assert.True(notificationsByRow[rows[2]] > 0);
        Assert.True(notificationsByRow[rows[3]] > 0);
        Assert.Equal(0, notificationsByRow[rows[4]]);
        Assert.Same(firstDraft, rows[0].AppliedDraft);
        Assert.Same(fifthDraft, rows[4].AppliedDraft);
        Assert.Same(session.Document.Scenes.Single().Rows[3], rows[1].AppliedDraft);
    }

    [Fact]
    public void MoveRowWithinSceneWithResultReportsSingleChangedSceneAndAffectedRows()
    {
        var service = new WorkbenchDraftService();
        var scene = SceneTableViewModel.CreateManual(1);
        var rows = Enumerable.Range(1, 3)
            .Select(index => AnalysisResultRowViewModel.CreateManualDialogue(index, $"Character {index}"))
            .ToArray();
        foreach (var row in rows)
        {
            scene.Rows.Add(row);
        }

        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);

        var result = service.MoveRowToTargetWithResult(
            scenes,
            session,
            rows[0],
            rows[2],
            WorkbenchDropPosition.After);

        Assert.True(result.CommandResult.Applied);
        Assert.Same(scene, result.SourceScene);
        Assert.Same(scene, result.TargetScene);
        Assert.Same(rows[0], result.SuggestedSelectedRow);
        Assert.Same(rows[0], result.CommandResult.SuggestedSelectedRow);
        Assert.Equal([scene.SceneId], result.CommandResult.ChangedSceneIds);
        Assert.Equal([rows[1].InteractionId, rows[2].InteractionId, rows[0].InteractionId], result.CommandResult.ChangedRowIds);
        Assert.Empty(result.CommandResult.RemovedSceneIds);
        Assert.Empty(result.CommandResult.RemovedRowIds);
        Assert.False(result.DerivedStateRefresh.RefreshCounters);
    }

    [Fact]
    public void MoveRowAcrossScenesWithResultReportsSourceTargetScenesAndAffectedRows()
    {
        var service = new WorkbenchDraftService();
        var sourceScene = SceneTableViewModel.CreateManual(1);
        var targetScene = SceneTableViewModel.CreateManual(2);
        var sourceBefore = AnalysisResultRowViewModel.CreateManualDialogue(1, "Mo");
        var dragged = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var sourceAfter = AnalysisResultRowViewModel.CreateManualDialogue(2, "Zhao");
        var targetBefore = AnalysisResultRowViewModel.CreateManualDialogue(1, "Qin");
        var target = AnalysisResultRowViewModel.CreateManualDialogue(1, "Lin");
        var targetAfter = AnalysisResultRowViewModel.CreateManualDialogue(2, "Wang");
        sourceScene.Rows.Add(sourceBefore);
        sourceScene.Rows.Add(dragged);
        sourceScene.Rows.Add(sourceAfter);
        targetScene.Rows.Add(targetBefore);
        targetScene.Rows.Add(target);
        targetScene.Rows.Add(targetAfter);
        var scenes = new ObservableCollection<SceneTableViewModel>([sourceScene, targetScene]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);

        var result = service.MoveRowToTargetWithResult(
            scenes,
            session,
            dragged,
            target,
            WorkbenchDropPosition.After);

        Assert.True(result.CommandResult.Applied);
        Assert.Same(sourceScene, result.SourceScene);
        Assert.Same(targetScene, result.TargetScene);
        Assert.Same(dragged, result.SuggestedSelectedRow);
        Assert.Same(dragged, result.CommandResult.SuggestedSelectedRow);
        Assert.Equal([sourceScene.SceneId, targetScene.SceneId], result.CommandResult.ChangedSceneIds);
        Assert.Equal(
            [sourceAfter.InteractionId, dragged.InteractionId, targetAfter.InteractionId],
            result.CommandResult.ChangedRowIds);
        Assert.Empty(result.CommandResult.RemovedSceneIds);
        Assert.Empty(result.CommandResult.RemovedRowIds);
        Assert.False(result.DerivedStateRefresh.RefreshCounters);
    }

    [Fact]
    public void MoveRowUsesOwnerSceneWhenAppliedDraftIsInvalidated()
    {
        var service = new WorkbenchDraftService();
        var scene = SceneTableViewModel.CreateManual(1);
        var first = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var second = AnalysisResultRowViewModel.CreateManualDialogue(2, "Lin");
        scene.Rows.Add(first);
        scene.Rows.Add(second);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);

        first.LineText = "invalidate applied draft before dragging";
        Assert.Null(first.AppliedDraft);
        Assert.Same(scene, first.OwnerScene);

        var moved = service.MoveRowToTarget(
            scenes,
            session,
            first,
            second,
            WorkbenchDropPosition.After,
            out var targetScene);

        Assert.True(moved);
        Assert.Same(scene, targetScene);
        Assert.Equal([second.InteractionId, first.InteractionId], scene.Rows.Select(row => row.InteractionId));
        Assert.Same(session.Document.Scenes.Single().Rows[1], first.AppliedDraft);
    }

    [Fact]
    public void MoveRowAcrossScenesRefreshesFirstCharacterPreviewOnlyWhenItChanges()
    {
        var service = new WorkbenchDraftService();
        var sourceImage = CreateTempPng();
        var targetImage = CreateTempPng();
        var sourceScene = SceneTableViewModel.CreateManual(1);
        var targetScene = SceneTableViewModel.CreateManual(2);
        var dragged = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var sourceRemaining = AnalysisResultRowViewModel.CreateManualDialogue(2, "Lin");
        var targetExisting = AnalysisResultRowViewModel.CreateManualDialogue(1, "Mo");
        dragged.SelectedCharacterPreviewAsset = sourceImage;
        targetExisting.SelectedCharacterPreviewAsset = targetImage;
        sourceScene.Rows.Add(dragged);
        sourceScene.Rows.Add(sourceRemaining);
        targetScene.Rows.Add(targetExisting);
        var scenes = new ObservableCollection<SceneTableViewModel>([sourceScene, targetScene]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);
        _ = sourceScene.FirstCharacterPreviewAsset;
        _ = targetScene.FirstCharacterPreviewAsset;
        var sourceNotifications = new List<string>();
        var targetNotifications = new List<string>();
        sourceScene.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                sourceNotifications.Add(args.PropertyName);
            }
        };
        targetScene.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                targetNotifications.Add(args.PropertyName);
            }
        };

        var moved = service.MoveRowToTarget(
            scenes,
            session,
            dragged,
            targetExisting,
            WorkbenchDropPosition.After,
            out var target);

        Assert.True(moved);
        Assert.Same(targetScene, target);
        Assert.NotEqual(sourceImage, sourceScene.FirstCharacterPreviewAsset);
        Assert.Equal(targetImage, targetScene.FirstCharacterPreviewAsset);
        Assert.DoesNotContain(nameof(SceneTableViewModel.FirstCharacterPreviewAsset), sourceNotifications);
        Assert.DoesNotContain(nameof(SceneTableViewModel.FirstCharacterPreviewAsset), targetNotifications);
    }

    [Fact]
    public void MoveSceneAppliesLocalCollectionMoveWithoutRefreshingRows()
    {
        var service = new WorkbenchDraftService();
        var first = SceneTableViewModel.CreateManual(1);
        var second = SceneTableViewModel.CreateManual(2);
        var third = SceneTableViewModel.CreateManual(3);
        first.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen"));
        second.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(1, "Lin"));
        third.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(1, "Mo"));
        var scenes = new ObservableCollection<SceneTableViewModel>([first, second, third]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);
        var firstRowDraft = first.Rows[0].AppliedDraft;
        var secondRowDraft = second.Rows[0].AppliedDraft;
        var thirdRowDraft = third.Rows[0].AppliedDraft;
        var firstRowNotifications = 0;
        first.Rows[0].PropertyChanged += (_, _) => firstRowNotifications++;

        var moved = service.MoveSceneToTarget(
            scenes,
            session,
            first,
            second,
            WorkbenchDropPosition.After);

        Assert.True(moved);
        Assert.Equal([second.SceneId, first.SceneId, third.SceneId], scenes.Select(scene => scene.SceneId));
        Assert.Equal([1, 2, 3], scenes.Select(scene => scene.Order));
        Assert.Equal(0, firstRowNotifications);
        Assert.Same(firstRowDraft, first.Rows[0].AppliedDraft);
        Assert.Same(secondRowDraft, second.Rows[0].AppliedDraft);
        Assert.Same(thirdRowDraft, third.Rows[0].AppliedDraft);
        Assert.Same(session.Document.Scenes.Single(scene => scene.SceneId == first.SceneId), first.AppliedDraft);
        Assert.Same(session.Document.Scenes.Single(scene => scene.SceneId == second.SceneId), second.AppliedDraft);
    }

    [Fact]
    public void MoveSceneOnlyMarksAndRenumbersAffectedRange()
    {
        var service = new WorkbenchDraftService();
        var scenes = new ObservableCollection<SceneTableViewModel>();
        for (var index = 0; index < 6; index++)
        {
            var scene = SceneTableViewModel.CreateManual(index + 1);
            scene.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(1, $"Character {index}"));
            scenes.Add(scene);
        }

        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);
        var orderedScenes = scenes.ToArray();
        var outsideBefore = scenes[0];
        var outsideAfter = scenes[5];
        var outsideBeforeAppliedDraft = outsideBefore.AppliedDraft;
        var outsideAfterAppliedDraft = outsideAfter.AppliedDraft;
        var outsideBeforeNotifications = 0;
        var outsideAfterNotifications = 0;
        outsideBefore.PropertyChanged += (_, _) => outsideBeforeNotifications++;
        outsideAfter.PropertyChanged += (_, _) => outsideAfterNotifications++;

        var moved = service.MoveSceneToTarget(
            scenes,
            session,
            scenes[2],
            scenes[4],
            WorkbenchDropPosition.After);

        Assert.True(moved);
        Assert.Equal(
            [
                orderedScenes[0].SceneId,
                orderedScenes[1].SceneId,
                orderedScenes[3].SceneId,
                orderedScenes[4].SceneId,
                orderedScenes[2].SceneId,
                orderedScenes[5].SceneId
            ],
            scenes.Select(scene => scene.SceneId));
        Assert.Same(outsideBeforeAppliedDraft, outsideBefore.AppliedDraft);
        Assert.Same(outsideAfterAppliedDraft, outsideAfter.AppliedDraft);
        Assert.Equal(0, outsideBeforeNotifications);
        Assert.Equal(0, outsideAfterNotifications);
        Assert.Equal(Enumerable.Range(1, scenes.Count), scenes.Select(scene => scene.Order));
    }

    [Fact]
    public void MoveSceneWithResultReportsMovedAndTargetScenes()
    {
        var service = new WorkbenchDraftService();
        var first = SceneTableViewModel.CreateManual(1);
        var second = SceneTableViewModel.CreateManual(2);
        var third = SceneTableViewModel.CreateManual(3);
        var scenes = new ObservableCollection<SceneTableViewModel>([first, second, third]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);

        var result = service.MoveSceneToTargetWithResult(
            scenes,
            session,
            first,
            third,
            WorkbenchDropPosition.After);

        Assert.True(result.CommandResult.Applied);
        Assert.Equal([second.SceneId, third.SceneId, first.SceneId], result.CommandResult.ChangedSceneIds);
        Assert.Empty(result.CommandResult.ChangedRowIds);
        Assert.Empty(result.CommandResult.RemovedSceneIds);
        Assert.Empty(result.CommandResult.RemovedRowIds);
        Assert.Equal(WorkbenchBindingTargetRefreshKind.SceneOrderRange, result.BindingTargetRefresh?.Kind);
        Assert.Equal([second, third, first], result.BindingTargetRefresh?.Scenes);
        Assert.False(result.DerivedStateRefresh.RefreshCounters);
        Assert.Equal([second.SceneId, third.SceneId, first.SceneId], scenes.Select(scene => scene.SceneId));
    }

    [Fact]
    public void MoveRowFallsBackToCollectionIndexWhenOrderIsStale()
    {
        var service = new WorkbenchDraftService();
        var scene = SceneTableViewModel.CreateManual(1);
        var first = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var second = AnalysisResultRowViewModel.CreateManualDialogue(2, "Lin");
        scene.Rows.Add(first);
        scene.Rows.Add(second);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);
        first.Order = 2;
        second.Order = 1;

        var moved = service.MoveRowToTarget(
            scenes,
            session,
            first,
            second,
            WorkbenchDropPosition.After,
            out var targetScene);

        Assert.True(moved);
        Assert.Same(scene, targetScene);
        Assert.Equal([second.InteractionId, first.InteractionId], scene.Rows.Select(row => row.InteractionId));
        Assert.Equal([1, 2], scene.Rows.Select(row => row.Order));
    }

    [Fact]
    public void UpdateRowTextRejectsDetachedRowWithSameIdsAsExistingRow()
    {
        var service = new WorkbenchDraftService();
        var sceneDraft = new WorkbenchSceneDraft(
            "scene_same_id",
            1,
            "Scene",
            "Effect",
            [CreateRowDraft("row_same_id", 1, "Chen", "original")]);
        var scene = SceneTableViewModel.FromDraft(sceneDraft);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);
        var detachedRow = AnalysisResultRowViewModel.FromDraft(CreateRowDraft("row_same_id", 1, "Chen", "detached"), null);
        WorkbenchViewModelAdapter.ApplyRowFields(detachedRow, session.Document);

        detachedRow.LineText = "detached edit";
        var updated = service.UpdateRowText(scenes, session, detachedRow);

        Assert.False(updated);
        Assert.Equal("original", session.Document.RowsById["row_same_id"].Text);
        Assert.Equal("original", scene.Rows[0].LineText);
    }

    [Fact]
    public void RemoveSceneRejectsDetachedSceneWithSameIdAsExistingScene()
    {
        var service = new WorkbenchDraftService();
        var sceneDraft = new WorkbenchSceneDraft(
            "scene_same_id",
            1,
            "Scene",
            "Effect",
            [CreateRowDraft("row_same_id", 1, "Chen", "original")]);
        var scene = SceneTableViewModel.FromDraft(sceneDraft);
        var detachedScene = SceneTableViewModel.FromDraft(sceneDraft);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);

        var removed = service.RemoveScene(scenes, session, detachedScene);

        Assert.False(removed);
        Assert.Same(scene, Assert.Single(scenes));
        Assert.True(session.Document.TryGetSceneDraft("scene_same_id", out _));
    }

    [Fact]
    public void MoveSceneFallsBackToCollectionIndexWhenOrderIsStale()
    {
        var service = new WorkbenchDraftService();
        var first = SceneTableViewModel.CreateManual(1);
        var second = SceneTableViewModel.CreateManual(2);
        var scenes = new ObservableCollection<SceneTableViewModel>([first, second]);
        var session = new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
        WorkbenchViewModelAdapter.ApplyDocument(scenes, session.Document);
        first.Order = 2;
        second.Order = 1;

        var moved = service.MoveSceneToTarget(
            scenes,
            session,
            first,
            second,
            WorkbenchDropPosition.After);

        Assert.True(moved);
        Assert.Equal([second.SceneId, first.SceneId], scenes.Select(scene => scene.SceneId));
        Assert.Equal([1, 2], scenes.Select(scene => scene.Order));
    }

    private static string CreateTempPng()
    {
        var path = Path.Combine(Path.GetTempPath(), $"draft-agent-workbench-service-preview-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, [137, 80, 78, 71]);
        return path;
    }

    private static WorkbenchRowDraft CreateRowDraft(
        string interactionId,
        int order,
        string subjectName,
        string text)
    {
        return new WorkbenchRowDraft(
            interactionId,
            "scene",
            order,
            WorkbenchRowKind.Dialogue,
            subjectName,
            null,
            "Calm",
            text,
            null,
            null,
            new WorkbenchTaskLinks(null));
    }

    private static string? SceneVisualId(WorkbenchSceneDraft scene)
    {
        return scene.VisualBinding?.EffectiveAssetId;
    }

    private static string? RowVisualId(WorkbenchRowDraft row)
    {
        return row.CharacterVisualBinding?.EffectiveAssetId;
    }

    private static string? RowVoiceId(WorkbenchRowDraft row)
    {
        return row.VoiceBinding?.EffectiveVoiceReferenceId;
    }

    private static string? RowVoiceText(WorkbenchRowDraft row)
    {
        return row.VoiceBinding?.EffectiveReferenceText;
    }
}



