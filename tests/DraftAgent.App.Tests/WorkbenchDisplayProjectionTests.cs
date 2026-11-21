using DraftAgent.App.ViewModels;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace DraftAgent.App.Tests;

public sealed class WorkbenchDisplayProjectionTests
{
    [Fact]
    public void BuildEmitsOnlySceneHeaderForCollapsedScene()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        scene.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen"));
        scene.IsExpanded = false;

        var items = WorkbenchDisplayProjection.Build([scene]);

        var header = Assert.Single(items);
        Assert.Equal(WorkbenchDisplayItemKind.SceneHeader, header.Kind);
        Assert.Same(scene, header.Scene);
    }

    [Fact]
    public void BuildEmitsSceneHeaderAndRowsForExpandedScene()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var first = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var second = AnalysisResultRowViewModel.CreateManualNarration(2);
        scene.Rows.Add(first);
        scene.Rows.Add(second);

        var items = WorkbenchDisplayProjection.Build([scene]);

        Assert.Collection(
            items,
            item =>
            {
                Assert.Equal(WorkbenchDisplayItemKind.SceneHeader, item.Kind);
                Assert.Same(scene, item.Scene);
            },
            item =>
            {
                var rowItem = Assert.IsType<WorkbenchRowDisplayItem>(item);
                Assert.Same(scene, rowItem.Scene);
                Assert.Same(first, rowItem.Row);
                Assert.Equal(first.InteractionId, rowItem.InteractionId);
                Assert.False(rowItem.IsLastRowInScene);
            },
            item =>
            {
                var rowItem = Assert.IsType<WorkbenchRowDisplayItem>(item);
                Assert.Same(scene, rowItem.Scene);
                Assert.Same(second, rowItem.Row);
                Assert.Equal(second.InteractionId, rowItem.InteractionId);
                Assert.True(rowItem.IsLastRowInScene);
            });
    }

    [Fact]
    public void BuildEmitsEmptyItemForExpandedEmptyScene()
    {
        var scene = SceneTableViewModel.CreateManual(1);

        var items = WorkbenchDisplayProjection.Build([scene]);

        Assert.Collection(
            items,
            item =>
            {
                Assert.Equal(WorkbenchDisplayItemKind.SceneHeader, item.Kind);
                Assert.Same(scene, item.Scene);
            },
            item =>
            {
                Assert.Equal(WorkbenchDisplayItemKind.EmptyScene, item.Kind);
                Assert.Same(scene, item.Scene);
            });
    }

    [Fact]
    public void BuildPreservesSceneAndRowReferencesAcrossMultipleScenes()
    {
        var firstScene = SceneTableViewModel.CreateManual(1);
        var firstRow = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        firstScene.Rows.Add(firstRow);
        var secondScene = SceneTableViewModel.CreateManual(2);
        secondScene.IsExpanded = false;

        var items = WorkbenchDisplayProjection.Build([firstScene, secondScene]);

        Assert.Collection(
            items,
            item => Assert.Same(firstScene, Assert.IsType<WorkbenchSceneHeaderDisplayItem>(item).Scene),
            item => Assert.Same(firstRow, Assert.IsType<WorkbenchRowDisplayItem>(item).Row),
            item => Assert.Same(secondScene, Assert.IsType<WorkbenchSceneHeaderDisplayItem>(item).Scene));
    }

    [Fact]
    public void BuildRespondsToExpandedStateWithoutChangingBusinessProjection()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(row);
        var originalSceneDraft = scene.AppliedDraft;
        var originalRowDraft = row.AppliedDraft;
        scene.IsExpanded = false;

        var collapsed = WorkbenchDisplayProjection.Build([scene]);
        scene.IsExpanded = true;
        var expanded = WorkbenchDisplayProjection.Build([scene]);

        Assert.Single(collapsed);
        Assert.Equal(2, expanded.Count);
        Assert.Same(originalSceneDraft, scene.AppliedDraft);
        Assert.Same(originalRowDraft, row.AppliedDraft);
    }

    [Fact]
    public void SyncPopulatesEmptyTargetWithDisplayItems()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(row);
        var target = new ObservableCollection<WorkbenchDisplayItem>();

        WorkbenchDisplayProjection.Sync(target, [scene]);

        Assert.Collection(
            target,
            item => Assert.Same(scene, Assert.IsType<WorkbenchSceneHeaderDisplayItem>(item).Scene),
            item => Assert.Same(row, Assert.IsType<WorkbenchRowDisplayItem>(item).Row));
    }

    [Fact]
    public void SyncAlignedItemsDoesNotRaiseRedundantPropertyChanges()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(row);
        var target = new ObservableCollection<WorkbenchDisplayItem>();
        WorkbenchDisplayProjection.Sync(target, [scene]);
        var notifications = new List<string?>();
        foreach (var item in target)
        {
            item.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        }

        WorkbenchDisplayProjection.Sync(target, [scene]);

        Assert.Empty(notifications);
    }

    [Fact]
    public void SyncCollapsingSceneRemovesRowItemsAndReusesHeader()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(row);
        var target = new ObservableCollection<WorkbenchDisplayItem>();
        WorkbenchDisplayProjection.Sync(target, [scene]);
        var header = target[0];
        scene.IsExpanded = false;

        WorkbenchDisplayProjection.Sync(target, [scene]);

        var remaining = Assert.Single(target);
        Assert.Same(header, remaining);
    }

    [Fact]
    public void SyncReexpandingSceneReusesHeaderAndCreatesRowItem()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(row);
        scene.IsExpanded = false;
        var target = new ObservableCollection<WorkbenchDisplayItem>();
        WorkbenchDisplayProjection.Sync(target, [scene]);
        var header = target[0];
        scene.IsExpanded = true;

        WorkbenchDisplayProjection.Sync(target, [scene]);

        Assert.Same(header, target[0]);
        Assert.Same(row, Assert.IsType<WorkbenchRowDisplayItem>(target[1]).Row);
    }

    [Fact]
    public void SyncMovingRowsUsesCollectionMoveAndReusesDisplayItems()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var first = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var second = AnalysisResultRowViewModel.CreateManualDialogue(2, "Lin");
        var third = AnalysisResultRowViewModel.CreateManualDialogue(3, "Mo");
        scene.Rows.Add(first);
        scene.Rows.Add(second);
        scene.Rows.Add(third);
        var target = new ObservableCollection<WorkbenchDisplayItem>();
        WorkbenchDisplayProjection.Sync(target, [scene]);
        var firstItem = target[1];
        var secondItem = target[2];
        var thirdItem = target[3];
        var actions = new List<NotifyCollectionChangedAction>();
        target.CollectionChanged += (_, args) => actions.Add(args.Action);
        scene.Rows.Move(2, 0);

        WorkbenchDisplayProjection.Sync(target, [scene]);

        Assert.Equal(
            [third.InteractionId, first.InteractionId, second.InteractionId],
            target.OfType<WorkbenchRowDisplayItem>().Select(item => item.InteractionId));
        Assert.Same(thirdItem, target[1]);
        Assert.Same(firstItem, target[2]);
        Assert.Same(secondItem, target[3]);
        Assert.Contains(NotifyCollectionChangedAction.Move, actions);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
    }

    [Fact]
    public void SyncRemovingMiddleRowRemovesOnlyObsoleteDisplayItem()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var first = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var second = AnalysisResultRowViewModel.CreateManualDialogue(2, "Lin");
        var third = AnalysisResultRowViewModel.CreateManualDialogue(3, "Mo");
        scene.Rows.Add(first);
        scene.Rows.Add(second);
        scene.Rows.Add(third);
        var target = new ObservableCollection<WorkbenchDisplayItem>();
        WorkbenchDisplayProjection.Sync(target, [scene]);
        var firstItem = target[1];
        var thirdItem = target[3];
        var actions = new List<NotifyCollectionChangedAction>();
        target.CollectionChanged += (_, args) => actions.Add(args.Action);
        scene.Rows.Remove(second);

        WorkbenchDisplayProjection.Sync(target, [scene]);

        Assert.Equal([NotifyCollectionChangedAction.Remove], actions);
        Assert.Same(firstItem, target[1]);
        Assert.Same(thirdItem, target[2]);
        Assert.Equal(
            [first.InteractionId, third.InteractionId],
            target.OfType<WorkbenchRowDisplayItem>().Select(item => item.InteractionId));
    }

    [Fact]
    public void SyncCrossSceneMovedRowReusesMovedDisplayItem()
    {
        var firstScene = SceneTableViewModel.CreateManual(1);
        var first = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var moved = AnalysisResultRowViewModel.CreateManualDialogue(2, "Lin");
        firstScene.Rows.Add(first);
        firstScene.Rows.Add(moved);
        var secondScene = SceneTableViewModel.CreateManual(2);
        var targetRow = AnalysisResultRowViewModel.CreateManualDialogue(1, "Mo");
        secondScene.Rows.Add(targetRow);
        var target = new ObservableCollection<WorkbenchDisplayItem>();
        WorkbenchDisplayProjection.Sync(target, [firstScene, secondScene]);
        var actions = new List<NotifyCollectionChangedAction>();
        target.CollectionChanged += (_, args) => actions.Add(args.Action);
        firstScene.Rows.Remove(moved);
        secondScene.Rows.Insert(1, moved);

        WorkbenchDisplayProjection.Sync(target, [firstScene, secondScene]);

        Assert.Equal([NotifyCollectionChangedAction.Move], actions);
        var movedItem = Assert.IsType<WorkbenchRowDisplayItem>(target[4]);
        Assert.Same(secondScene, movedItem.Scene);
        Assert.Same(moved, movedItem.Row);
        Assert.Equal(
            [first.InteractionId, targetRow.InteractionId, moved.InteractionId],
            target.OfType<WorkbenchRowDisplayItem>().Select(item => item.InteractionId));
    }

    [Fact]
    public void SyncCrossSceneMovedRowNotifiesRowPlacement()
    {
        var firstScene = SceneTableViewModel.CreateManual(1);
        var first = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var moved = AnalysisResultRowViewModel.CreateManualDialogue(2, "Lin");
        firstScene.Rows.Add(first);
        firstScene.Rows.Add(moved);
        var secondScene = SceneTableViewModel.CreateManual(2);
        var targetRow = AnalysisResultRowViewModel.CreateManualDialogue(1, "Mo");
        secondScene.Rows.Add(targetRow);
        var target = new ObservableCollection<WorkbenchDisplayItem>();
        WorkbenchDisplayProjection.Sync(target, [firstScene, secondScene]);
        var movedItem = target.OfType<WorkbenchRowDisplayItem>().Single(item => ReferenceEquals(item.Row, moved));
        var targetRowItem = target.OfType<WorkbenchRowDisplayItem>().Single(item => ReferenceEquals(item.Row, targetRow));
        var movedNotifications = new List<string?>();
        var targetNotifications = new List<string?>();
        movedItem.PropertyChanged += (_, args) => movedNotifications.Add(args.PropertyName);
        targetRowItem.PropertyChanged += (_, args) => targetNotifications.Add(args.PropertyName);
        firstScene.Rows.Remove(moved);
        secondScene.Rows.Insert(1, moved);

        WorkbenchDisplayProjection.Sync(target, [firstScene, secondScene]);

        Assert.True(movedItem.IsLastRowInScene);
        Assert.False(targetRowItem.IsLastRowInScene);
        Assert.Contains(nameof(WorkbenchDisplayItem.IsLastRowInScene), movedNotifications);
        Assert.Contains(nameof(WorkbenchDisplayItem.IsLastRowInScene), targetNotifications);
    }

    [Fact]
    public void SyncSwitchesBetweenEmptyItemAndRows()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var target = new ObservableCollection<WorkbenchDisplayItem>();
        WorkbenchDisplayProjection.Sync(target, [scene]);
        var header = target[0];
        Assert.IsType<WorkbenchEmptySceneDisplayItem>(target[1]);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(row);

        WorkbenchDisplayProjection.Sync(target, [scene]);

        Assert.Same(header, target[0]);
        Assert.Same(row, Assert.IsType<WorkbenchRowDisplayItem>(target[1]).Row);

        scene.Rows.Clear();
        WorkbenchDisplayProjection.Sync(target, [scene]);

        Assert.Same(header, target[0]);
        Assert.IsType<WorkbenchEmptySceneDisplayItem>(target[1]);
    }

    [Fact]
    public void SynchronizerInitializesItemsFromScenes()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(row);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        using var synchronizer = new WorkbenchDisplayProjectionSynchronizer(scenes);

        Assert.Collection(
            synchronizer.Items,
            item => Assert.Same(scene, Assert.IsType<WorkbenchSceneHeaderDisplayItem>(item).Scene),
            item => Assert.Same(row, Assert.IsType<WorkbenchRowDisplayItem>(item).Row));
    }

    [Fact]
    public void SynchronizerUpdatesWhenSceneExpansionChanges()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(row);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        using var synchronizer = new WorkbenchDisplayProjectionSynchronizer(scenes);
        var header = synchronizer.Items[0];

        scene.IsExpanded = false;

        Assert.Single(synchronizer.Items);
        Assert.Same(header, synchronizer.Items[0]);

        scene.IsExpanded = true;

        Assert.Same(header, synchronizer.Items[0]);
        Assert.Same(row, Assert.IsType<WorkbenchRowDisplayItem>(synchronizer.Items[1]).Row);
    }

    [Fact]
    public void SynchronizerUpdatesWhenRowsChange()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        using var synchronizer = new WorkbenchDisplayProjectionSynchronizer(scenes);
        var header = synchronizer.Items[0];
        Assert.IsType<WorkbenchEmptySceneDisplayItem>(synchronizer.Items[1]);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");

        scene.Rows.Add(row);

        Assert.Same(header, synchronizer.Items[0]);
        Assert.Same(row, Assert.IsType<WorkbenchRowDisplayItem>(synchronizer.Items[1]).Row);
    }

    [Fact]
    public void SynchronizerRowAddSwitchesEmptySceneToRowItemsLocally()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        using var synchronizer = new WorkbenchDisplayProjectionSynchronizer(scenes);
        var header = synchronizer.Items[0];
        var actions = new List<NotifyCollectionChangedAction>();
        synchronizer.Items.CollectionChanged += (_, args) => actions.Add(args.Action);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");

        scene.Rows.Add(row);

        Assert.Equal([NotifyCollectionChangedAction.Replace], actions);
        Assert.Same(header, synchronizer.Items[0]);
        Assert.Same(row, Assert.IsType<WorkbenchRowDisplayItem>(synchronizer.Items[1]).Row);
        AssertDisplayItemsMatchScenes(synchronizer.Items, scenes);
    }

    [Fact]
    public void SynchronizerRowRemoveSwitchesLastRowToEmptySceneLocally()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(row);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        using var synchronizer = new WorkbenchDisplayProjectionSynchronizer(scenes);
        var header = synchronizer.Items[0];
        var actions = new List<NotifyCollectionChangedAction>();
        synchronizer.Items.CollectionChanged += (_, args) => actions.Add(args.Action);

        scene.Rows.Remove(row);

        Assert.Equal([NotifyCollectionChangedAction.Replace], actions);
        Assert.Same(header, synchronizer.Items[0]);
        Assert.IsType<WorkbenchEmptySceneDisplayItem>(synchronizer.Items[1]);
        AssertDisplayItemsMatchScenes(synchronizer.Items, scenes);
    }

    [Fact]
    public void SynchronizerRowMoveUsesSingleDisplayItemMove()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var first = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var second = AnalysisResultRowViewModel.CreateManualDialogue(2, "Lin");
        var third = AnalysisResultRowViewModel.CreateManualDialogue(3, "Mo");
        scene.Rows.Add(first);
        scene.Rows.Add(second);
        scene.Rows.Add(third);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        using var synchronizer = new WorkbenchDisplayProjectionSynchronizer(scenes);
        var thirdItem = synchronizer.Items[3];
        var actions = new List<NotifyCollectionChangedAction>();
        synchronizer.Items.CollectionChanged += (_, args) => actions.Add(args.Action);

        scene.Rows.Move(2, 0);

        Assert.Equal([NotifyCollectionChangedAction.Move], actions);
        Assert.Same(thirdItem, synchronizer.Items[1]);
        AssertDisplayItemsMatchScenes(synchronizer.Items, scenes);
    }

    [Fact]
    public void SynchronizerNotifiesRowPlacementWhenLastRowChanges()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var first = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var second = AnalysisResultRowViewModel.CreateManualDialogue(2, "Lin");
        scene.Rows.Add(first);
        scene.Rows.Add(second);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        using var synchronizer = new WorkbenchDisplayProjectionSynchronizer(scenes);
        var firstItem = Assert.IsType<WorkbenchRowDisplayItem>(synchronizer.Items[1]);
        var secondItem = Assert.IsType<WorkbenchRowDisplayItem>(synchronizer.Items[2]);
        var firstNotifications = new List<string?>();
        var secondNotifications = new List<string?>();
        firstItem.PropertyChanged += (_, args) => firstNotifications.Add(args.PropertyName);
        secondItem.PropertyChanged += (_, args) => secondNotifications.Add(args.PropertyName);

        scene.Rows.Move(0, 1);

        Assert.True(firstItem.IsLastRowInScene);
        Assert.False(secondItem.IsLastRowInScene);
        Assert.Contains(nameof(WorkbenchDisplayItem.IsLastRowInScene), firstNotifications);
        Assert.Contains(nameof(WorkbenchDisplayItem.IsLastRowInScene), secondNotifications);
    }

    [Fact]
    public void SynchronizerCollapsedSceneRowChangesDoNotChangeDisplayItems()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        scene.IsExpanded = false;
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        using var synchronizer = new WorkbenchDisplayProjectionSynchronizer(scenes);
        var header = synchronizer.Items[0];
        var actions = new List<NotifyCollectionChangedAction>();
        synchronizer.Items.CollectionChanged += (_, args) => actions.Add(args.Action);

        scene.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen"));

        Assert.Empty(actions);
        Assert.Single(synchronizer.Items);
        Assert.Same(header, synchronizer.Items[0]);
        AssertDisplayItemsMatchScenes(synchronizer.Items, scenes);
    }

    [Fact]
    public void SynchronizerDefersExpansionChangesUntilScopeEnds()
    {
        var first = SceneTableViewModel.CreateManual(1);
        first.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen"));
        var second = SceneTableViewModel.CreateManual(2);
        second.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(2, "Lin"));
        var scenes = new ObservableCollection<SceneTableViewModel>([first, second]);
        using var synchronizer = new WorkbenchDisplayProjectionSynchronizer(scenes);
        var firstHeader = synchronizer.Items[0];
        var secondHeader = synchronizer.Items[2];
        var actions = new List<NotifyCollectionChangedAction>();
        synchronizer.Items.CollectionChanged += (_, args) => actions.Add(args.Action);

        using (synchronizer.DeferSync())
        {
            first.IsExpanded = false;
            second.IsExpanded = false;

            Assert.Empty(actions);
            Assert.Equal(4, synchronizer.Items.Count);
        }

        Assert.Equal([NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Remove], actions);
        Assert.Collection(
            synchronizer.Items,
            item => Assert.Same(firstHeader, item),
            item => Assert.Same(secondHeader, item));
        AssertDisplayItemsMatchScenes(synchronizer.Items, scenes);
    }

    [Fact]
    public void SynchronizerUpdatesWhenScenesChange()
    {
        var first = SceneTableViewModel.CreateManual(1);
        var second = SceneTableViewModel.CreateManual(2);
        var scenes = new ObservableCollection<SceneTableViewModel>([first]);
        using var synchronizer = new WorkbenchDisplayProjectionSynchronizer(scenes);

        scenes.Add(second);

        Assert.Collection(
            synchronizer.Items,
            item => Assert.Same(first, Assert.IsType<WorkbenchSceneHeaderDisplayItem>(item).Scene),
            item => Assert.Same(first, Assert.IsType<WorkbenchEmptySceneDisplayItem>(item).Scene),
            item => Assert.Same(second, Assert.IsType<WorkbenchSceneHeaderDisplayItem>(item).Scene),
            item => Assert.Same(second, Assert.IsType<WorkbenchEmptySceneDisplayItem>(item).Scene));

        scenes.Remove(first);

        Assert.Collection(
            synchronizer.Items,
            item => Assert.Same(second, Assert.IsType<WorkbenchSceneHeaderDisplayItem>(item).Scene),
            item => Assert.Same(second, Assert.IsType<WorkbenchEmptySceneDisplayItem>(item).Scene));
    }

    [Fact]
    public void SynchronizerSceneAddInsertsOnlyNewSceneItems()
    {
        var first = SceneTableViewModel.CreateManual(1);
        var second = SceneTableViewModel.CreateManual(2);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        second.Rows.Add(row);
        var scenes = new ObservableCollection<SceneTableViewModel>([first]);
        using var synchronizer = new WorkbenchDisplayProjectionSynchronizer(scenes);
        var firstHeader = synchronizer.Items[0];
        var firstEmpty = synchronizer.Items[1];
        var actions = new List<NotifyCollectionChangedAction>();
        synchronizer.Items.CollectionChanged += (_, args) => actions.Add(args.Action);

        scenes.Add(second);

        Assert.Equal([NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add], actions);
        Assert.Same(firstHeader, synchronizer.Items[0]);
        Assert.Same(firstEmpty, synchronizer.Items[1]);
        Assert.Same(second, Assert.IsType<WorkbenchSceneHeaderDisplayItem>(synchronizer.Items[2]).Scene);
        Assert.Same(row, Assert.IsType<WorkbenchRowDisplayItem>(synchronizer.Items[3]).Row);
    }

    [Fact]
    public void SynchronizerSceneRemoveRemovesOnlySceneBlock()
    {
        var first = SceneTableViewModel.CreateManual(1);
        var firstRow = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        first.Rows.Add(firstRow);
        var second = SceneTableViewModel.CreateManual(2);
        var scenes = new ObservableCollection<SceneTableViewModel>([first, second]);
        using var synchronizer = new WorkbenchDisplayProjectionSynchronizer(scenes);
        var secondHeader = synchronizer.Items[2];
        var secondEmpty = synchronizer.Items[3];
        var actions = new List<NotifyCollectionChangedAction>();
        synchronizer.Items.CollectionChanged += (_, args) => actions.Add(args.Action);

        scenes.Remove(first);

        Assert.Equal([NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Remove], actions);
        Assert.Collection(
            synchronizer.Items,
            item => Assert.Same(secondHeader, item),
            item => Assert.Same(secondEmpty, item));
    }

    [Fact]
    public void SynchronizerSceneMoveReusesMovedSceneItems()
    {
        var first = SceneTableViewModel.CreateManual(1);
        var firstRow = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        first.Rows.Add(firstRow);
        var second = SceneTableViewModel.CreateManual(2);
        var scenes = new ObservableCollection<SceneTableViewModel>([first, second]);
        using var synchronizer = new WorkbenchDisplayProjectionSynchronizer(scenes);
        var firstHeader = synchronizer.Items[0];
        var firstRowItem = synchronizer.Items[1];
        var actions = new List<NotifyCollectionChangedAction>();
        synchronizer.Items.CollectionChanged += (_, args) => actions.Add(args.Action);

        scenes.Move(0, 1);

        Assert.Equal(
            [
                NotifyCollectionChangedAction.Move,
                NotifyCollectionChangedAction.Move
            ],
            actions);
        Assert.Same(firstHeader, synchronizer.Items[2]);
        Assert.Same(firstRowItem, synchronizer.Items[3]);
        AssertDisplayItemsMatchScenes(synchronizer.Items, scenes);
    }

    [Fact]
    public void SynchronizerDisposeStopsRespondingToSceneAndRowChanges()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var synchronizer = new WorkbenchDisplayProjectionSynchronizer(scenes);
        Assert.Equal(2, synchronizer.Items.Count);

        synchronizer.Dispose();
        scene.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen"));
        scene.IsExpanded = false;
        scenes.Add(SceneTableViewModel.CreateManual(2));

        Assert.Equal(2, synchronizer.Items.Count);
        Assert.IsType<WorkbenchEmptySceneDisplayItem>(synchronizer.Items[1]);
    }

    private static void AssertDisplayItemsMatchScenes(
        IReadOnlyList<WorkbenchDisplayItem> actual,
        IReadOnlyList<SceneTableViewModel> scenes)
    {
        var expected = WorkbenchDisplayProjection.Build(scenes);
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].Kind, actual[index].Kind);
            Assert.Same(expected[index].Scene, actual[index].Scene);
            if (expected[index] is WorkbenchRowDisplayItem expectedRow)
            {
                var actualRow = Assert.IsType<WorkbenchRowDisplayItem>(actual[index]);
                Assert.Same(expectedRow.Row, actualRow.Row);
            }
        }
    }
}

