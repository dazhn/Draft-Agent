using Avalonia;
using Avalonia.Controls;
using DraftAgent.App.ViewModels;
using DraftAgent.App.Views;

namespace DraftAgent.App.Tests;

public sealed class WorkbenchDragTargetRefreshTests
{
    [Fact]
    public void ResolvesVisibleWorkbenchTargetUnderPointer()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var tree = BuildTargetTree(row);
        var refresh = new WorkbenchDragTargetRefresh((_, _) => [tree.RowBorder]);

        var resolved = refresh.TryResolveNextRowTarget(
            tree.Root,
            new Point(20, 20),
            DateTimeOffset.UtcNow,
            out var target);

        Assert.True(resolved);
        Assert.Same(tree.RowBorder, target);
    }

    [Fact]
    public void RechecksSameTargetAfterThrottle()
    {
        var first = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var second = AnalysisResultRowViewModel.CreateManualDialogue(2, "Lin");
        var tree = BuildTargetTree(first, second);
        var candidates = new Queue<Visual[]>([
            [tree.RowBorder],
            [tree.RowBorder],
            [tree.SecondRowBorder!]
        ]);
        var refresh = new WorkbenchDragTargetRefresh((_, _) => candidates.Dequeue());
        var now = DateTimeOffset.UtcNow;

        Assert.True(refresh.TryResolveNextRowTarget(tree.Root, new Point(20, 20), now, out var firstTarget));
        Assert.Same(tree.RowBorder, firstTarget);

        Assert.True(refresh.TryResolveNextRowTarget(tree.Root, new Point(24, 24), now.AddMilliseconds(40), out var sameTarget));
        Assert.Same(tree.RowBorder, sameTarget);

        Assert.True(refresh.TryResolveNextRowTarget(tree.Root, new Point(20, 72), now.AddMilliseconds(80), out var secondTarget));
        Assert.Same(tree.SecondRowBorder, secondTarget);
    }

    [Fact]
    public void RechecksRecycledRowVisualAfterTargetDataChanges()
    {
        var first = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var second = AnalysisResultRowViewModel.CreateManualDialogue(2, "Lin");
        var tree = BuildTargetTree(first);
        var refresh = new WorkbenchDragTargetRefresh((_, _) => [tree.RowBorder]);
        var now = DateTimeOffset.UtcNow;

        Assert.True(refresh.TryResolveNextRowTarget(tree.Root, new Point(20, 20), now, out var firstTarget));
        WorkbenchDragTarget.SetTarget(tree.RowBorder, second);

        Assert.True(refresh.TryResolveNextRowTarget(tree.Root, new Point(20, 20), now.AddMilliseconds(40), out var recycledTarget));
        Assert.Same(firstTarget, recycledTarget);
    }

    [Fact]
    public void RechecksRecycledSceneVisualAfterTargetDataChanges()
    {
        var first = SceneTableViewModel.CreateManual(1);
        var second = SceneTableViewModel.CreateManual(2);
        var tree = BuildTargetTree(AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen"));
        WorkbenchDragTarget.SetTarget(tree.SceneBorder, first);
        var refresh = new WorkbenchDragTargetRefresh((_, _) => [tree.SceneBorder]);
        var now = DateTimeOffset.UtcNow;

        Assert.True(refresh.TryResolveNextSceneTarget(tree.Root, new Point(20, 20), now, out var firstTarget));
        WorkbenchDragTarget.SetTarget(tree.SceneBorder, second);

        Assert.True(refresh.TryResolveNextSceneTarget(tree.Root, new Point(20, 20), now.AddMilliseconds(40), out var recycledTarget));
        Assert.Same(firstTarget, recycledTarget);
    }

    [Fact]
    public void ThrottlesPointerRefresh()
    {
        var first = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var second = AnalysisResultRowViewModel.CreateManualDialogue(2, "Lin");
        var tree = BuildTargetTree(first, second);
        var candidates = new Queue<Visual[]>([
            [tree.RowBorder],
            [tree.SecondRowBorder!]
        ]);
        var refresh = new WorkbenchDragTargetRefresh((_, _) => candidates.Dequeue());
        var now = DateTimeOffset.UtcNow;

        Assert.True(refresh.TryResolveNextRowTarget(tree.Root, new Point(20, 20), now, out _));

        Assert.False(refresh.TryResolveNextRowTarget(tree.Root, new Point(20, 72), now.AddMilliseconds(8), out _));
        Assert.True(refresh.TryResolveNextRowTarget(tree.Root, new Point(20, 72), now.AddMilliseconds(40), out var target));
        Assert.Same(tree.SecondRowBorder, target);
    }

    [Fact]
    public void RowTargetRefreshSkipsDraggedSourceRowAndKeepsScanning()
    {
        var dragged = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var targetRow = AnalysisResultRowViewModel.CreateManualDialogue(2, "Lin");
        var tree = BuildTargetTree(dragged, targetRow);
        var refresh = new WorkbenchDragTargetRefresh((_, _) => [tree.RowBorder, tree.SecondRowBorder!]);

        var resolved = refresh.TryResolveNextRowTarget(
            tree.Root,
            new Point(20, 20),
            DateTimeOffset.UtcNow,
            dragged,
            out var target);

        Assert.True(resolved);
        Assert.Same(tree.SecondRowBorder, target);
    }

    [Fact]
    public void RowTargetRefreshReturnsDraggedSourceRowWhenItIsTheOnlyResolvedCandidate()
    {
        var dragged = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var targetRow = AnalysisResultRowViewModel.CreateManualDialogue(2, "Lin");
        var tree = BuildTargetTree(dragged, targetRow);
        var refresh = new WorkbenchDragTargetRefresh((_, _) => [tree.RowBorder]);

        var resolved = refresh.TryResolveNextRowTarget(
            tree.Root,
            new Point(20, 20),
            DateTimeOffset.UtcNow,
            dragged,
            out var target);

        Assert.True(resolved);
        Assert.Same(tree.RowBorder, target);
    }

    [Fact]
    public void RowTargetRefreshIgnoresBareSceneTargets()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var tree = BuildTargetTree(row);
        var refresh = new WorkbenchDragTargetRefresh((_, _) => [tree.SceneBorder]);

        var resolved = refresh.TryResolveNextRowTarget(
            tree.Root,
            new Point(20, 20),
            DateTimeOffset.UtcNow,
            out _);

        Assert.False(resolved);
    }

    [Fact]
    public void SceneTargetRefreshIgnoresRowTargets()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var tree = BuildTargetTree(row);
        var refresh = new WorkbenchDragTargetRefresh((_, _) => [tree.RowBorder]);

        var resolved = refresh.TryResolveNextSceneTarget(
            tree.Root,
            new Point(20, 20),
            DateTimeOffset.UtcNow,
            out _);

        Assert.False(resolved);
    }

    [Fact]
    public void RowDropIntentIgnoresBareSceneTargets()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var tree = BuildTargetTree(row);

        var resolved = WorkbenchDropTargetResolver.TryCreateRowDropIntent(
            tree.SceneBorder,
            row,
            out _,
            out _);

        Assert.False(resolved);
    }

    [Fact]
    public void AcceptEnteredRowTargetDoesNotTreatSceneAsLastRefreshTarget()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var tree = BuildTargetTree(row);
        var refresh = new WorkbenchDragTargetRefresh((_, _) => [tree.RowBorder]);
        var now = DateTimeOffset.UtcNow;

        refresh.AcceptEnteredRowTarget(tree.SceneBorder);

        var resolved = refresh.TryResolveNextRowTarget(
            tree.Root,
            new Point(20, 20),
            now.AddMilliseconds(40),
            out var target);

        Assert.True(resolved);
        Assert.Same(tree.RowBorder, target);
    }

    [Fact]
    public void RowDropIntentRequiresExplicitTableTargetForSceneEnd()
    {
        var draggedRow = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var targetRow = AnalysisResultRowViewModel.CreateManualDialogue(1, "Lin");
        var tree = BuildTargetTree(targetRow);
        var child = new Border();
        tree.Table.Children.Add(child);

        var resolved = WorkbenchDropTargetResolver.TryCreateRowDropIntent(
            child,
            draggedRow,
            out _,
            out _);

        Assert.False(resolved);
    }

    [Fact]
    public void RowDropPositionUsesCurrentRowIndexesAfterLivePreview()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var first = AnalysisResultRowViewModel.CreateManualDialogue(1, "A");
        var second = AnalysisResultRowViewModel.CreateManualDialogue(2, "B");
        var third = AnalysisResultRowViewModel.CreateManualDialogue(3, "C");
        scene.Rows.Add(first);
        scene.Rows.Add(second);
        scene.Rows.Add(third);
        scene.Rows.Move(0, 2);

        var position = WorkbenchDropTargetResolver.ResolveRowDropPosition(first, second);

        Assert.Equal(WorkbenchDropPosition.Before, position);
    }

    [Fact]
    public void SceneDropIntentUsesCurrentSceneIndexesAfterLivePreview()
    {
        var first = SceneTableViewModel.CreateManual(1);
        var second = SceneTableViewModel.CreateManual(2);
        var third = SceneTableViewModel.CreateManual(3);
        var target = new Border();
        target.Classes.Add("referenceScene");
        WorkbenchDragTarget.SetTarget(target, second);

        var resolved = WorkbenchDropTargetResolver.TryCreateSceneDropIntent(
            target,
            first,
            [second, first, third],
            out var intent,
            out var indicatorVisual);

        Assert.True(resolved);
        Assert.Same(second, intent.Target);
        Assert.Equal(WorkbenchDropPosition.Before, intent.Position);
        Assert.Same(target, indicatorVisual);
    }

    private static TargetTree BuildTargetTree(params AnalysisResultRowViewModel[] rows)
    {
        var root = new StackPanel();
        var scene = SceneTableViewModel.CreateManual(1);
        var sceneBorder = new Border
        {
            Width = 300,
            Height = rows.Length * 52
        };
        sceneBorder.Classes.Add("referenceScene");
        WorkbenchDragTarget.SetTarget(sceneBorder, scene);

        var table = new StackPanel
        {
            Width = 300,
            Height = rows.Length * 52
        };
        table.Classes.Add("eventTable");
        WorkbenchDragTarget.SetTarget(table, scene);
        sceneBorder.Child = table;
        root.Children.Add(sceneBorder);

        Border? firstRowBorder = null;
        Border? secondRowBorder = null;
        foreach (var row in rows)
        {
            scene.Rows.Add(row);
            var rowBorder = new Border
            {
                Width = 300,
                Height = 52
            };
            rowBorder.Classes.Add("eventRow");
            WorkbenchDragTarget.SetTarget(rowBorder, row);
            table.Children.Add(rowBorder);
            firstRowBorder ??= rowBorder;
            secondRowBorder = ReferenceEquals(firstRowBorder, rowBorder)
                ? secondRowBorder
                : rowBorder;
        }

        root.Measure(new Size(300, rows.Length * 52));
        root.Arrange(new Rect(0, 0, 300, rows.Length * 52));

        return new TargetTree(root, scene, sceneBorder, table, firstRowBorder!, secondRowBorder);
    }

    private sealed record TargetTree(
        StackPanel Root,
        SceneTableViewModel Scene,
        Border SceneBorder,
        StackPanel Table,
        Border RowBorder,
        Border? SecondRowBorder);
}

