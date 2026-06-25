using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace DraftAgent.App.ViewModels;

public enum WorkbenchDisplayItemKind
{
    SceneHeader,
    Row,
    EmptyScene
}

public abstract record WorkbenchDisplayItem : INotifyPropertyChanged
{
    protected WorkbenchDisplayItem(SceneTableViewModel scene)
    {
        Scene = scene;
    }

    public SceneTableViewModel Scene { get; private set; }

    public string SceneId => Scene.SceneId;

    public abstract WorkbenchDisplayItemKind Kind { get; }

    public bool IsSceneHeader => Kind == WorkbenchDisplayItemKind.SceneHeader;

    public bool IsRow => Kind == WorkbenchDisplayItemKind.Row;

    public bool IsEmptyScene => Kind == WorkbenchDisplayItemKind.EmptyScene;

    public virtual AnalysisResultRowViewModel? Row => null;

    public virtual bool IsLastRowInScene => false;

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void UpdateScene(SceneTableViewModel scene)
    {
        if (ReferenceEquals(Scene, scene))
        {
            return;
        }

        Scene = scene;
        OnPropertyChanged(nameof(Scene));
        OnPropertyChanged(nameof(SceneId));
        OnPropertyChanged(nameof(IsLastRowInScene));
    }

    internal void RefreshRowPlacement()
    {
        OnPropertyChanged(nameof(IsLastRowInScene));
    }

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record WorkbenchSceneHeaderDisplayItem(SceneTableViewModel Scene) : WorkbenchDisplayItem(Scene)
{
    public override WorkbenchDisplayItemKind Kind => WorkbenchDisplayItemKind.SceneHeader;
}

public sealed record WorkbenchRowDisplayItem : WorkbenchDisplayItem
{
    public WorkbenchRowDisplayItem(
        SceneTableViewModel scene,
        AnalysisResultRowViewModel row)
        : base(scene)
    {
        Row = row;
    }

    public override WorkbenchDisplayItemKind Kind => WorkbenchDisplayItemKind.Row;

    public override AnalysisResultRowViewModel Row { get; }

    public string InteractionId => Row.InteractionId;

    public override bool IsLastRowInScene => Scene.Rows.Count > 0 && ReferenceEquals(Scene.Rows[^1], Row);
}

public sealed record WorkbenchEmptySceneDisplayItem(SceneTableViewModel Scene) : WorkbenchDisplayItem(Scene)
{
    public override WorkbenchDisplayItemKind Kind => WorkbenchDisplayItemKind.EmptyScene;
}

internal static class WorkbenchDisplayProjection
{
    public static IReadOnlyList<WorkbenchDisplayItem> Build(IReadOnlyList<SceneTableViewModel> scenes)
    {
        var itemCount = 0;
        for (var sceneIndex = 0; sceneIndex < scenes.Count; sceneIndex++)
        {
            itemCount += CountSceneItems(scenes[sceneIndex]);
        }

        var items = new List<WorkbenchDisplayItem>(itemCount);
        for (var sceneIndex = 0; sceneIndex < scenes.Count; sceneIndex++)
        {
            var scene = scenes[sceneIndex];
            AddSceneItems(items, scene);
        }

        return items;
    }

    public static IReadOnlyList<WorkbenchDisplayItem> BuildScene(SceneTableViewModel scene)
    {
        var items = new List<WorkbenchDisplayItem>(CountSceneItems(scene));
        AddSceneItems(items, scene);
        return items;
    }

    public static int CountSceneItems(SceneTableViewModel scene)
    {
        if (!scene.IsExpanded)
        {
            return 1;
        }

        return scene.Rows.Count == 0 ? 2 : scene.Rows.Count + 1;
    }

    private static void AddSceneItems(ICollection<WorkbenchDisplayItem> items, SceneTableViewModel scene)
    {
        items.Add(new WorkbenchSceneHeaderDisplayItem(scene));

        if (!scene.IsExpanded)
        {
            return;
        }

        if (scene.Rows.Count == 0)
        {
            items.Add(new WorkbenchEmptySceneDisplayItem(scene));
            return;
        }

        for (var rowIndex = 0; rowIndex < scene.Rows.Count; rowIndex++)
        {
            items.Add(new WorkbenchRowDisplayItem(scene, scene.Rows[rowIndex]));
        }
    }

    public static void Sync(ObservableCollection<WorkbenchDisplayItem> target, IReadOnlyList<SceneTableViewModel> scenes)
    {
        var desiredItems = Build(scenes);
        if (ItemsAreAligned(target, desiredItems))
        {
            return;
        }

        RemoveObsoleteItems(target, desiredItems);
        for (var index = 0; index < desiredItems.Count; index++)
        {
            var desired = desiredItems[index];
            if (index < target.Count && ItemsMatch(target[index], desired))
            {
                target[index].UpdateScene(desired.Scene);
                continue;
            }

            if (TryApplySingleMove(target, desiredItems, index))
            {
                continue;
            }

            var existingIndex = IndexOfMatchingItem(target, desired, index + 1);
            if (existingIndex >= 0)
            {
                target[existingIndex].UpdateScene(desired.Scene);
                target.Move(existingIndex, index);
            }
            else
            {
                target.Insert(index, desired);
            }
        }

        while (target.Count > desiredItems.Count)
        {
            target.RemoveAt(target.Count - 1);
        }

        RefreshRowPlacement(target);
    }

    private static bool ItemsAreAligned(
        IReadOnlyList<WorkbenchDisplayItem> target,
        IReadOnlyList<WorkbenchDisplayItem> desiredItems)
    {
        if (target.Count != desiredItems.Count)
        {
            return false;
        }

        for (var index = 0; index < desiredItems.Count; index++)
        {
            if (!ItemsMatch(target[index], desiredItems[index]))
            {
                return false;
            }

            target[index].UpdateScene(desiredItems[index].Scene);
        }

        return true;
    }

    private static void RemoveObsoleteItems(
        ObservableCollection<WorkbenchDisplayItem> target,
        IReadOnlyList<WorkbenchDisplayItem> desiredItems)
    {
        var desiredKeys = new HashSet<WorkbenchDisplayItemKey>();
        foreach (var desired in desiredItems)
        {
            desiredKeys.Add(WorkbenchDisplayItemKey.FromItem(desired));
        }

        for (var index = target.Count - 1; index >= 0; index--)
        {
            if (!desiredKeys.Contains(WorkbenchDisplayItemKey.FromItem(target[index])))
            {
                target.RemoveAt(index);
            }
        }
    }

    private static void RefreshRowPlacement(IEnumerable<WorkbenchDisplayItem> items)
    {
        foreach (var item in items)
        {
            item.RefreshRowPlacement();
        }
    }

    private static bool TryApplySingleMove(
        ObservableCollection<WorkbenchDisplayItem> target,
        IReadOnlyList<WorkbenchDisplayItem> desiredItems,
        int mismatchIndex)
    {
        if (target.Count != desiredItems.Count ||
            mismatchIndex >= target.Count ||
            mismatchIndex >= desiredItems.Count)
        {
            return false;
        }

        var currentDesiredIndex = IndexOfMatchingItem(desiredItems, target[mismatchIndex], mismatchIndex + 1);
        if (currentDesiredIndex >= 0 &&
            MoveWouldAlign(target, desiredItems, mismatchIndex, currentDesiredIndex))
        {
            target[mismatchIndex].UpdateScene(desiredItems[currentDesiredIndex].Scene);
            target.Move(mismatchIndex, currentDesiredIndex);
            return true;
        }

        var desiredExistingIndex = IndexOfMatchingItem(target, desiredItems[mismatchIndex], mismatchIndex + 1);
        if (desiredExistingIndex >= 0 &&
            MoveWouldAlign(target, desiredItems, desiredExistingIndex, mismatchIndex))
        {
            target[desiredExistingIndex].UpdateScene(desiredItems[mismatchIndex].Scene);
            target.Move(desiredExistingIndex, mismatchIndex);
            return true;
        }

        return false;
    }

    private static bool MoveWouldAlign(
        IReadOnlyList<WorkbenchDisplayItem> currentItems,
        IReadOnlyList<WorkbenchDisplayItem> desiredItems,
        int sourceIndex,
        int targetIndex)
    {
        for (var index = 0; index < desiredItems.Count; index++)
        {
            var currentIndex = MapIndexAfterMove(index, sourceIndex, targetIndex);
            if (!ItemsMatch(currentItems[currentIndex], desiredItems[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static int MapIndexAfterMove(int index, int sourceIndex, int targetIndex)
    {
        if (index == targetIndex)
        {
            return sourceIndex;
        }

        if (sourceIndex < targetIndex)
        {
            return index >= sourceIndex && index < targetIndex
                ? index + 1
                : index;
        }

        return index > targetIndex && index <= sourceIndex
            ? index - 1
            : index;
    }

    private static int IndexOfMatchingItem(
        IReadOnlyList<WorkbenchDisplayItem> items,
        WorkbenchDisplayItem desired,
        int startIndex)
    {
        for (var index = startIndex; index < items.Count; index++)
        {
            if (ItemsMatch(items[index], desired))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool ItemsMatch(WorkbenchDisplayItem current, WorkbenchDisplayItem desired)
    {
        if (current.Kind != desired.Kind)
        {
            return false;
        }

        if (current is WorkbenchRowDisplayItem currentRow && desired is WorkbenchRowDisplayItem desiredRow)
        {
            return ReferenceEquals(currentRow.Row, desiredRow.Row);
        }

        return ReferenceEquals(current.Scene, desired.Scene) &&
            current.Kind is WorkbenchDisplayItemKind.SceneHeader or WorkbenchDisplayItemKind.EmptyScene;
    }

    private readonly struct WorkbenchDisplayItemKey : IEquatable<WorkbenchDisplayItemKey>
    {
        private readonly WorkbenchDisplayItemKind _kind;
        private readonly SceneTableViewModel? _scene;
        private readonly AnalysisResultRowViewModel? _row;

        private WorkbenchDisplayItemKey(
            WorkbenchDisplayItemKind kind,
            SceneTableViewModel? scene,
            AnalysisResultRowViewModel? row)
        {
            _kind = kind;
            _scene = scene;
            _row = row;
        }

        public static WorkbenchDisplayItemKey FromItem(WorkbenchDisplayItem item)
        {
            return item is WorkbenchRowDisplayItem rowItem
                ? new WorkbenchDisplayItemKey(item.Kind, null, rowItem.Row)
                : new WorkbenchDisplayItemKey(item.Kind, item.Scene, null);
        }

        public bool Equals(WorkbenchDisplayItemKey other)
        {
            return _kind == other._kind &&
                ReferenceEquals(_scene, other._scene) &&
                ReferenceEquals(_row, other._row);
        }

        public override bool Equals(object? obj)
        {
            return obj is WorkbenchDisplayItemKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                _kind,
                RuntimeHelpers.GetHashCode(_scene),
                _row is null ? 0 : RuntimeHelpers.GetHashCode(_row));
        }
    }
}

internal sealed class WorkbenchDisplayProjectionSynchronizer : IDisposable
{
    private readonly ObservableCollection<SceneTableViewModel> _scenes;
    private readonly HashSet<SceneTableViewModel> _trackedScenes = new(ReferenceEqualityComparer.Instance);
    private int _deferSyncDepth;
    private bool _hasDeferredSync;

    public WorkbenchDisplayProjectionSynchronizer(ObservableCollection<SceneTableViewModel> scenes)
    {
        _scenes = scenes;
        _scenes.CollectionChanged += OnScenesChanged;
        TrackScenes(_scenes);
        SyncNow();
    }

    public ObservableCollection<WorkbenchDisplayItem> Items { get; } = [];

    public IDisposable DeferSync()
    {
        _deferSyncDepth++;
        return new DeferredSyncScope(this);
    }

    public void Dispose()
    {
        _scenes.CollectionChanged -= OnScenesChanged;
        foreach (var scene in _trackedScenes.ToArray())
        {
            UntrackScene(scene);
        }
    }

    private void OnScenesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (TryDeferSync(syncTrackedScenes: true))
        {
            return;
        }

        if (TryApplySceneCollectionChange(e))
        {
            return;
        }

        SyncTrackedScenes();
        RequestSync();
    }

    private bool TryApplySceneCollectionChange(NotifyCollectionChangedEventArgs e)
    {
        return e.Action switch
        {
            NotifyCollectionChangedAction.Add => TryApplySceneAdd(e),
            NotifyCollectionChangedAction.Remove => TryApplySceneRemove(e),
            NotifyCollectionChangedAction.Move => TryApplySceneMove(e),
            NotifyCollectionChangedAction.Replace => TryApplySceneReplace(e),
            NotifyCollectionChangedAction.Reset => false,
            _ => false
        };
    }

    private bool TryApplySceneAdd(NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is null || e.NewStartingIndex < 0)
        {
            return false;
        }

        var insertIndex = DisplayItemIndexForSceneIndex(e.NewStartingIndex);
        foreach (SceneTableViewModel scene in e.NewItems)
        {
            TrackScene(scene);
            insertIndex = InsertSceneItems(insertIndex, scene);
        }

        return true;
    }

    private bool TryApplySceneRemove(NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is null)
        {
            return false;
        }

        foreach (SceneTableViewModel scene in e.OldItems)
        {
            UntrackScene(scene);
            if (!TryRemoveSceneItems(scene, out _))
            {
                return false;
            }
        }

        return true;
    }

    private bool TryApplySceneMove(NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is null ||
            e.NewItems.Count != 1 ||
            e.NewStartingIndex < 0 ||
            e.NewItems[0] is not SceneTableViewModel scene)
        {
            return false;
        }

        var sourceIndex = IndexOfSceneItems(scene);
        if (sourceIndex < 0)
        {
            return false;
        }

        var itemCount = CountSceneItemsAt(sourceIndex, scene);
        if (itemCount <= 0)
        {
            return false;
        }

        var insertIndex = Math.Clamp(DisplayItemIndexForSceneIndex(e.NewStartingIndex), 0, Items.Count);
        MoveItemRange(sourceIndex, itemCount, insertIndex);
        return true;
    }

    private bool TryApplySceneReplace(NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is null ||
            e.NewItems is null ||
            e.NewStartingIndex < 0)
        {
            return false;
        }

        foreach (SceneTableViewModel scene in e.OldItems)
        {
            UntrackScene(scene);
            if (!TryRemoveSceneItems(scene, out _))
            {
                return false;
            }
        }

        var insertIndex = DisplayItemIndexForSceneIndex(e.NewStartingIndex);
        foreach (SceneTableViewModel scene in e.NewItems)
        {
            TrackScene(scene);
            insertIndex = InsertSceneItems(insertIndex, scene);
        }

        return true;
    }

    private int DisplayItemIndexForSceneIndex(int sceneIndex)
    {
        var end = Math.Clamp(sceneIndex, 0, _scenes.Count);
        var itemIndex = 0;
        for (var index = 0; index < end; index++)
        {
            itemIndex += WorkbenchDisplayProjection.CountSceneItems(_scenes[index]);
        }

        return itemIndex;
    }

    private int InsertSceneItems(int index, SceneTableViewModel scene)
    {
        return InsertItems(index, WorkbenchDisplayProjection.BuildScene(scene));
    }

    private int InsertItems(int index, IEnumerable<WorkbenchDisplayItem> items)
    {
        var insertIndex = Math.Clamp(index, 0, Items.Count);
        foreach (var item in items)
        {
            Items.Insert(insertIndex, item);
            insertIndex++;
        }

        return insertIndex;
    }

    private bool TryRemoveSceneItems(
        SceneTableViewModel scene,
        out List<WorkbenchDisplayItem> removedItems)
    {
        removedItems = [];
        var startIndex = IndexOfSceneItems(scene);
        if (startIndex < 0)
        {
            return false;
        }

        while (startIndex < Items.Count && ReferenceEquals(Items[startIndex].Scene, scene))
        {
            removedItems.Add(Items[startIndex]);
            Items.RemoveAt(startIndex);
        }

        return removedItems.Count > 0;
    }

    private int IndexOfSceneItems(SceneTableViewModel scene)
    {
        for (var index = 0; index < Items.Count; index++)
        {
            if (ReferenceEquals(Items[index].Scene, scene))
            {
                return index;
            }
        }

        return -1;
    }

    private int CountSceneItemsAt(int startIndex, SceneTableViewModel scene)
    {
        var count = 0;
        for (var index = startIndex; index < Items.Count; index++)
        {
            if (!ReferenceEquals(Items[index].Scene, scene))
            {
                break;
            }

            count++;
        }

        return count;
    }

    private void MoveItemRange(int sourceIndex, int count, int targetIndex)
    {
        if (count <= 0 || sourceIndex == targetIndex)
        {
            return;
        }

        var insertIndex = Math.Clamp(targetIndex, 0, Items.Count - count);
        if (insertIndex == sourceIndex)
        {
            return;
        }

        if (sourceIndex < insertIndex)
        {
            for (var offset = count - 1; offset >= 0; offset--)
            {
                Items.Move(sourceIndex + offset, insertIndex + offset);
            }

            return;
        }

        for (var offset = 0; offset < count; offset++)
        {
            Items.Move(sourceIndex + offset, insertIndex + offset);
        }
    }

    private void OnScenePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SceneTableViewModel.IsExpanded))
        {
            RequestSync();
        }
    }

    private void OnSceneRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (TryDeferSync(syncTrackedScenes: false))
        {
            return;
        }

        if (TryApplyRowCollectionChange(sender, e))
        {
            return;
        }

        RequestSync();
    }

    private bool TryApplyRowCollectionChange(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (sender is not ObservableCollection<AnalysisResultRowViewModel> rows ||
            !TryFindSceneForRows(rows, out var scene))
        {
            return false;
        }

        if (!scene.IsExpanded)
        {
            return true;
        }

        return e.Action switch
        {
            NotifyCollectionChangedAction.Add => TryApplyRowAdd(scene, e),
            NotifyCollectionChangedAction.Remove => TryApplyRowRemove(scene, e),
            NotifyCollectionChangedAction.Move => TryApplyRowMove(scene, e),
            NotifyCollectionChangedAction.Replace => TryApplyRowReplace(scene, e),
            NotifyCollectionChangedAction.Reset => TryRebuildSceneItems(scene),
            _ => false
        };
    }

    private bool TryApplyRowAdd(SceneTableViewModel scene, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is null || e.NewStartingIndex < 0)
        {
            return false;
        }

        if (TryReplaceEmptySceneItemWithRows(scene, e.NewItems))
        {
            return true;
        }

        var insertIndex = DisplayItemIndexForRowIndex(scene, e.NewStartingIndex);
        InsertRowItems(insertIndex, scene, e.NewItems);
        RefreshSceneRowPlacement(scene);
        return true;
    }

    private bool TryApplyRowRemove(SceneTableViewModel scene, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is null)
        {
            return false;
        }

        if (scene.Rows.Count == 0 &&
            e.OldItems.Count == 1 &&
            e.OldItems[0] is AnalysisResultRowViewModel removedRow)
        {
            return TryReplaceRowItemWithEmptySceneItem(scene, removedRow);
        }

        foreach (AnalysisResultRowViewModel row in e.OldItems)
        {
            if (!TryRemoveRowItem(scene, row, out _))
            {
                return false;
            }
        }

        if (scene.Rows.Count == 0)
        {
            var insertIndex = IndexOfSceneHeader(scene) + 1;
            if (insertIndex <= 0)
            {
                return false;
            }

            Items.Insert(insertIndex, new WorkbenchEmptySceneDisplayItem(scene));
            RefreshSceneRowPlacement(scene);
            return true;
        }

        RefreshSceneRowPlacement(scene);
        return true;
    }

    private bool TryApplyRowMove(SceneTableViewModel scene, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is null ||
            e.NewItems.Count != 1 ||
            e.NewStartingIndex < 0 ||
            e.NewItems[0] is not AnalysisResultRowViewModel row)
        {
            return false;
        }

        var sourceItemIndex = IndexOfRowItem(scene, row);
        if (sourceItemIndex < 0)
        {
            return false;
        }

        var targetItemIndex = DisplayItemIndexForRowIndex(scene, e.NewStartingIndex);
        if (sourceItemIndex < targetItemIndex)
        {
            targetItemIndex--;
        }

        if (sourceItemIndex != targetItemIndex)
        {
            Items.Move(sourceItemIndex, targetItemIndex);
        }

        RefreshSceneRowPlacement(scene);
        return true;
    }

    private bool TryApplyRowReplace(SceneTableViewModel scene, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is null ||
            e.NewItems is null ||
            e.NewStartingIndex < 0)
        {
            return false;
        }

        var insertIndex = -1;
        foreach (AnalysisResultRowViewModel row in e.OldItems)
        {
            if (!TryRemoveRowItem(scene, row, out var removedIndex))
            {
                return false;
            }

            if (insertIndex < 0 || removedIndex < insertIndex)
            {
                insertIndex = removedIndex;
            }
        }

        InsertRowItems(insertIndex < 0 ? DisplayItemIndexForRowIndex(scene, e.NewStartingIndex) : insertIndex, scene, e.NewItems);
        RefreshSceneRowPlacement(scene);
        return true;
    }

    private bool TryRebuildSceneItems(SceneTableViewModel scene)
    {
        if (!TryRemoveSceneItems(scene, out _))
        {
            return false;
        }

        var insertIndex = DisplayItemIndexForSceneIndex(IndexOfScene(scene));
        InsertSceneItems(insertIndex, scene);
        return true;
    }

    private bool TryFindSceneForRows(
        ObservableCollection<AnalysisResultRowViewModel> rows,
        out SceneTableViewModel scene)
    {
        foreach (var trackedScene in _trackedScenes)
        {
            if (ReferenceEquals(trackedScene.Rows, rows))
            {
                scene = trackedScene;
                return true;
            }
        }

        scene = null!;
        return false;
    }

    private int DisplayItemIndexForRowIndex(SceneTableViewModel scene, int rowIndex)
    {
        var headerIndex = IndexOfSceneHeader(scene);
        if (headerIndex < 0)
        {
            return Items.Count;
        }

        return Math.Clamp(headerIndex + 1 + rowIndex, headerIndex + 1, Items.Count);
    }

    private void InsertRowItems(
        int index,
        SceneTableViewModel scene,
        System.Collections.IList rows)
    {
        var insertIndex = Math.Clamp(index, 0, Items.Count);
        foreach (AnalysisResultRowViewModel row in rows)
        {
            Items.Insert(insertIndex, new WorkbenchRowDisplayItem(scene, row));
            insertIndex++;
        }
    }

    private bool TryReplaceEmptySceneItemWithRows(
        SceneTableViewModel scene,
        System.Collections.IList rows)
    {
        for (var index = 0; index < Items.Count; index++)
        {
            if (Items[index] is WorkbenchEmptySceneDisplayItem emptyItem &&
                ReferenceEquals(emptyItem.Scene, scene))
            {
                if (rows.Count == 0 ||
                    rows[0] is not AnalysisResultRowViewModel firstRow)
                {
                    return false;
                }

                Items[index] = new WorkbenchRowDisplayItem(scene, firstRow);
                InsertRowItems(index + 1, scene, rows.Cast<AnalysisResultRowViewModel>().Skip(1).ToList());
                RefreshSceneRowPlacement(scene);
                return true;
            }
        }

        return false;
    }

    private bool TryReplaceRowItemWithEmptySceneItem(
        SceneTableViewModel scene,
        AnalysisResultRowViewModel row)
    {
        var rowItemIndex = IndexOfRowItem(scene, row);
        if (rowItemIndex < 0)
        {
            return false;
        }

        Items[rowItemIndex] = new WorkbenchEmptySceneDisplayItem(scene);
        RefreshSceneRowPlacement(scene);
        return true;
    }

    private bool TryRemoveRowItem(
        SceneTableViewModel scene,
        AnalysisResultRowViewModel row,
        out int removedIndex)
    {
        removedIndex = IndexOfRowItem(scene, row);
        if (removedIndex < 0)
        {
            return false;
        }

        Items.RemoveAt(removedIndex);
        return true;
    }

    private void RefreshSceneRowPlacement(SceneTableViewModel scene)
    {
        foreach (var item in Items)
        {
            if (ReferenceEquals(item.Scene, scene))
            {
                item.RefreshRowPlacement();
            }
        }
    }

    private int IndexOfScene(SceneTableViewModel scene)
    {
        for (var index = 0; index < _scenes.Count; index++)
        {
            if (ReferenceEquals(_scenes[index], scene))
            {
                return index;
            }
        }

        return -1;
    }

    private int IndexOfSceneHeader(SceneTableViewModel scene)
    {
        for (var index = 0; index < Items.Count; index++)
        {
            if (Items[index] is WorkbenchSceneHeaderDisplayItem headerItem &&
                ReferenceEquals(headerItem.Scene, scene))
            {
                return index;
            }
        }

        return -1;
    }

    private int IndexOfRowItem(SceneTableViewModel scene, AnalysisResultRowViewModel row)
    {
        for (var index = 0; index < Items.Count; index++)
        {
            if (Items[index] is WorkbenchRowDisplayItem rowItem &&
                ReferenceEquals(rowItem.Scene, scene) &&
                ReferenceEquals(rowItem.Row, row))
            {
                return index;
            }
        }

        return -1;
    }

    private void TrackScenes(IEnumerable<SceneTableViewModel> scenes)
    {
        foreach (var scene in scenes)
        {
            TrackScene(scene);
        }
    }

    private void SyncTrackedScenes()
    {
        var currentScenes = new HashSet<SceneTableViewModel>(_scenes, ReferenceEqualityComparer.Instance);
        foreach (var scene in _trackedScenes.ToArray())
        {
            if (!currentScenes.Contains(scene))
            {
                UntrackScene(scene);
            }
        }

        TrackScenes(_scenes);
    }

    private void TrackScene(SceneTableViewModel scene)
    {
        if (!_trackedScenes.Add(scene))
        {
            return;
        }

        scene.PropertyChanged += OnScenePropertyChanged;
        scene.Rows.CollectionChanged += OnSceneRowsChanged;
    }

    private void UntrackScene(SceneTableViewModel scene)
    {
        if (!_trackedScenes.Remove(scene))
        {
            return;
        }

        scene.PropertyChanged -= OnScenePropertyChanged;
        scene.Rows.CollectionChanged -= OnSceneRowsChanged;
    }

    private bool TryDeferSync(bool syncTrackedScenes)
    {
        if (_deferSyncDepth <= 0)
        {
            return false;
        }

        if (syncTrackedScenes)
        {
            SyncTrackedScenes();
        }

        _hasDeferredSync = true;
        return true;
    }

    private void RequestSync()
    {
        if (TryDeferSync(syncTrackedScenes: false))
        {
            return;
        }

        SyncNow();
    }

    private void SyncNow()
    {
        WorkbenchDisplayProjection.Sync(Items, _scenes);
    }

    private sealed class DeferredSyncScope : IDisposable
    {
        private WorkbenchDisplayProjectionSynchronizer? _owner;

        public DeferredSyncScope(WorkbenchDisplayProjectionSynchronizer owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            var owner = _owner;
            if (owner is null)
            {
                return;
            }

            _owner = null;
            owner._deferSyncDepth--;
            if (owner._deferSyncDepth != 0 || !owner._hasDeferredSync)
            {
                return;
            }

            owner._hasDeferredSync = false;
            owner.SyncNow();
        }
    }
}
