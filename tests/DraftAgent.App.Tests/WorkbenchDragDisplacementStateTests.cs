using Avalonia.Controls;
using DraftAgent.App.ViewModels;
using DraftAgent.App.Views;

namespace DraftAgent.App.Tests;

public sealed class WorkbenchDragDisplacementStateTests
{
    [Fact]
    public void RowDragMarksOnlyTheSourceRowState()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var first = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var second = AnalysisResultRowViewModel.CreateManualDialogue(2, "Lin");
        scene.Rows.Add(first);
        scene.Rows.Add(second);
        var tree = BuildTree([scene]);
        var state = new WorkbenchDragDisplacementState();

        state.StartRow(first);
        state.SetRow(first);

        Assert.True(first.IsDragSourceActive);
        Assert.False(second.IsDragSourceActive);
        Assert.DoesNotContain("dragSourceActive", tree.Rows[first].Classes);
        Assert.DoesNotContain("dragSourceActive", tree.Rows[second].Classes);
        AssertNoDisplacementClasses(tree);
    }

    [Fact]
    public void RowDragSourceStateSurvivesRecreatedFlatRowVisual()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var first = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(first);
        var tree = BuildTree([scene]);
        var replacement = new Border();
        replacement.Classes.Add("eventRow");
        WorkbenchDragTarget.SetTarget(replacement, first);
        var state = new WorkbenchDragDisplacementState();

        state.StartRow(first);
        tree.Root.Children.Remove(tree.Rows[first]);
        tree.Root.Children.Insert(1, replacement);
        state.SetRow(first);

        Assert.True(first.IsDragSourceActive);
        Assert.DoesNotContain("dragSourceActive", tree.Rows[first].Classes);
        Assert.DoesNotContain("dragSourceActive", replacement.Classes);
        AssertNoDisplacementClasses(tree);
        AssertNoDisplacementClasses(replacement);
    }

    [Fact]
    public void RowDragStartedFromChildMarksRowStateOnly()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var first = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(first);
        var tree = BuildTree([scene]);
        var child = new TextBlock();
        tree.Rows[first].Child = child;
        var state = new WorkbenchDragDisplacementState();

        state.StartRow(first);

        Assert.True(first.IsDragSourceActive);
        Assert.DoesNotContain("dragSourceActive", tree.Rows[first].Classes);
        Assert.DoesNotContain("dragSourceActive", child.Classes);
        AssertNoDisplacementClasses(tree);
    }

    [Fact]
    public void SceneDragMarksOnlyTheSceneState()
    {
        var first = SceneTableViewModel.CreateManual(1);
        var second = SceneTableViewModel.CreateManual(2);
        first.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(1, "A"));
        second.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(1, "B"));
        var tree = BuildTree([first, second]);
        var state = new WorkbenchDragDisplacementState();

        state.StartScene(first);
        state.SetScene(first);

        Assert.True(first.IsDragSourceActive);
        Assert.False(second.IsDragSourceActive);
        Assert.DoesNotContain("dragSourceActive", tree.Scenes[first].Classes);
        Assert.DoesNotContain("dragSourceActive", tree.Rows[first.Rows[0]].Classes);
        Assert.DoesNotContain("dragSourceActive", tree.SceneEnds[first].Classes);
        Assert.DoesNotContain("dragSourceActive", tree.Scenes[second].Classes);
        AssertNoDisplacementClasses(tree);
    }

    [Fact]
    public void SceneDragSourceStateSurvivesRecreatedFlatSceneHeaderVisual()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var tree = BuildTree([scene]);
        var replacement = new Border();
        replacement.Classes.Add("referenceScene");
        WorkbenchDragTarget.SetTarget(replacement, scene);
        var state = new WorkbenchDragDisplacementState();

        state.StartScene(scene);
        tree.Root.Children.Remove(tree.Scenes[scene]);
        tree.Root.Children.Insert(0, replacement);
        state.SetScene(scene);

        Assert.True(scene.IsDragSourceActive);
        Assert.DoesNotContain("dragSourceActive", tree.Scenes[scene].Classes);
        Assert.DoesNotContain("dragSourceActive", replacement.Classes);
        AssertNoDisplacementClasses(tree);
        AssertNoDisplacementClasses(replacement);
    }

    [Fact]
    public void SceneDragStartedFromChildMarksSceneStateOnly()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var tree = BuildTree([scene]);
        var child = new TextBlock();
        tree.Scenes[scene].Child = child;
        var state = new WorkbenchDragDisplacementState();

        state.StartScene(scene);

        Assert.True(scene.IsDragSourceActive);
        Assert.DoesNotContain("dragSourceActive", tree.Scenes[scene].Classes);
        Assert.DoesNotContain("dragSourceActive", child.Classes);
        AssertNoDisplacementClasses(tree);
    }

    [Fact]
    public void ClearRemovesSourceState()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var first = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(first);
        var tree = BuildTree([scene]);
        var state = new WorkbenchDragDisplacementState();

        state.StartRow(first);
        state.Clear();

        Assert.False(first.IsDragSourceActive);
        Assert.DoesNotContain("dragSourceActive", tree.Rows[first].Classes);
        AssertNoDisplacementClasses(tree);
    }

    private static DragTree BuildTree(IReadOnlyList<SceneTableViewModel> scenes)
    {
        var root = new StackPanel();
        var sceneVisuals = new Dictionary<SceneTableViewModel, Border>(ReferenceEqualityComparer.Instance);
        var rowVisuals = new Dictionary<AnalysisResultRowViewModel, Border>(ReferenceEqualityComparer.Instance);
        var sceneEndVisuals = new Dictionary<SceneTableViewModel, Border>(ReferenceEqualityComparer.Instance);
        foreach (var scene in scenes)
        {
            var sceneBorder = new Border();
            sceneBorder.Classes.Add("referenceScene");
            WorkbenchDragTarget.SetTarget(sceneBorder, scene);
            sceneVisuals[scene] = sceneBorder;
            root.Children.Add(sceneBorder);

            foreach (var row in scene.Rows)
            {
                var rowBorder = new Border();
                rowBorder.Classes.Add("eventRow");
                WorkbenchDragTarget.SetTarget(rowBorder, row);
                rowVisuals[row] = rowBorder;
                root.Children.Add(rowBorder);
            }

            var sceneEndBorder = new Border();
            sceneEndBorder.Classes.Add("eventTable");
            WorkbenchDragTarget.SetTarget(sceneEndBorder, scene);
            sceneEndVisuals[scene] = sceneEndBorder;
            root.Children.Add(sceneEndBorder);
        }

        return new DragTree(root, sceneVisuals, rowVisuals, sceneEndVisuals);
    }

    private static void AssertNoDisplacementClasses(DragTree tree)
    {
        Assert.All(tree.Scenes.Values, AssertNoDisplacementClasses);
        Assert.All(tree.Rows.Values, AssertNoDisplacementClasses);
        Assert.All(tree.SceneEnds.Values, AssertNoDisplacementClasses);
    }

    private static void AssertNoDisplacementClasses(Border visual)
    {
        Assert.DoesNotContain("dragDisplaceUp", visual.Classes);
        Assert.DoesNotContain("dragDisplaceDown", visual.Classes);
        Assert.Null(visual.RenderTransform);
    }

    private sealed record DragTree(
        StackPanel Root,
        IReadOnlyDictionary<SceneTableViewModel, Border> Scenes,
        IReadOnlyDictionary<AnalysisResultRowViewModel, Border> Rows,
        IReadOnlyDictionary<SceneTableViewModel, Border> SceneEnds);
}
