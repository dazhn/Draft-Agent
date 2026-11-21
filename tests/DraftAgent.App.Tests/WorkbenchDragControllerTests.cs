using DraftAgent.App.ViewModels;
using DraftAgent.Core;

namespace DraftAgent.App.Tests;

public sealed class WorkbenchDragControllerTests
{
    [Fact]
    public void EnsureCheckpointCreatesCheckpointOnlyOncePerDrag()
    {
        var controller = new WorkbenchDragController();
        var initialDocument = new WorkbenchDocument([], []);
        var session = new WorkbenchSession(initialDocument);
        var nextDocument = new WorkbenchDocument(
            [new WorkbenchSceneDraft("scene", 1, "Scene", string.Empty, [])],
            []);
        controller.Begin(null);

        controller.EnsureCheckpoint(session);
        session.Preview(_ => nextDocument);
        controller.EnsureCheckpoint(session);
        controller.MarkChanged();

        var completed = controller.Complete();

        Assert.NotNull(completed);
        Assert.Same(initialDocument, completed.Checkpoint.Current.Document);
        Assert.Null(completed.BindingTargetRefresh);
        Assert.False(completed.DerivedStateRefresh.RefreshCounters);
    }

    [Fact]
    public void CompleteReturnsLastPendingBindingTargetRefresh()
    {
        var controller = new WorkbenchDragController();
        var session = new WorkbenchSession(new WorkbenchDocument([], []));
        var scene = SceneTableViewModel.CreateManual(1);
        var refresh = WorkbenchBindingTargetRefresh.ForSceneOrderRange([scene]);
        controller.Begin(null);
        controller.EnsureCheckpoint(session);

        controller.MarkChanged(refresh);
        var completed = controller.Complete();

        Assert.NotNull(completed);
        Assert.Same(refresh, completed.BindingTargetRefresh);
        Assert.Equal(WorkbenchBindingTargetRefreshKind.SceneOrderRange, completed.BindingTargetRefresh?.Kind);
    }

    [Fact]
    public void EnsureCheckpointDoesNotCreateCheckpointWhenDragIsInactive()
    {
        var controller = new WorkbenchDragController();
        var session = new WorkbenchSession(new WorkbenchDocument([], []));

        controller.EnsureCheckpoint(session);

        Assert.Null(controller.Complete());
    }

    [Fact]
    public void ShouldPreviewSkipsRepeatedIntentDuringActiveDrag()
    {
        var controller = new WorkbenchDragController();
        var intent = new WorkbenchDragPreviewIntent(
            WorkbenchDragPreviewIntentKind.RowToRow,
            "row_1",
            "row_2",
            WorkbenchDropPosition.After);

        Assert.True(controller.ShouldPreview(intent));

        controller.Begin(null);

        Assert.True(controller.ShouldPreview(intent));
        Assert.False(controller.ShouldPreview(intent));
        Assert.True(controller.ShouldPreview(intent with { Position = WorkbenchDropPosition.Before }));

        Assert.Equal(3, controller.Metrics.PreviewAttemptCount);
        Assert.Equal(2, controller.Metrics.DistinctPreviewIntentCount);
        Assert.Equal(1, controller.Metrics.SkippedRepeatedPreviewCount);
        Assert.Equal(0, controller.Metrics.SkippedNoOpPreviewCount);
    }

    [Fact]
    public void ShouldPreviewResetsAfterDragCompletes()
    {
        var controller = new WorkbenchDragController();
        var intent = new WorkbenchDragPreviewIntent(
            WorkbenchDragPreviewIntentKind.SceneToScene,
            "scene_1",
            "scene_2",
            WorkbenchDropPosition.After);

        controller.Begin(null);
        Assert.True(controller.ShouldPreview(intent));
        Assert.False(controller.ShouldPreview(intent));

        controller.Complete();
        controller.Begin(null);

        Assert.True(controller.ShouldPreview(intent));
    }

    [Fact]
    public void MetricsTrackAppliedPreviewsAndCheckpointCreation()
    {
        var controller = new WorkbenchDragController();
        var session = new WorkbenchSession(new WorkbenchDocument([], []));
        var intent = new WorkbenchDragPreviewIntent(
            WorkbenchDragPreviewIntentKind.RowToSceneEnd,
            "row_1",
            "scene_2",
            null);

        controller.Begin(null);
        Assert.True(controller.ShouldPreview(intent));
        controller.EnsureCheckpoint(session);
        controller.MarkChanged();

        Assert.True(controller.Metrics.CheckpointCreated);
        Assert.Equal(1, controller.Metrics.PreviewAttemptCount);
        Assert.Equal(1, controller.Metrics.DistinctPreviewIntentCount);
        Assert.Equal(1, controller.Metrics.AppliedPreviewCount);
        Assert.Equal(0, controller.Metrics.SkippedNoOpPreviewCount);
    }

    [Fact]
    public void MetricsTrackNoOpPreviewAfterDistinctIntent()
    {
        var controller = new WorkbenchDragController();
        var intent = new WorkbenchDragPreviewIntent(
            WorkbenchDragPreviewIntentKind.RowToRow,
            "row_1",
            "row_2",
            WorkbenchDropPosition.Before);

        controller.Begin(null);
        Assert.True(controller.ShouldPreview(intent));
        controller.MarkNoOpPreview();

        Assert.Equal(1, controller.Metrics.PreviewAttemptCount);
        Assert.Equal(1, controller.Metrics.DistinctPreviewIntentCount);
        Assert.Equal(1, controller.Metrics.SkippedNoOpPreviewCount);
        Assert.Equal(0, controller.Metrics.SkippedRepeatedPreviewCount);
        Assert.Equal(0, controller.Metrics.AppliedPreviewCount);
    }

    [Fact]
    public void CancelReturnsRollbackWithSessionOnlyDerivedRefresh()
    {
        var controller = new WorkbenchDragController();
        var session = new WorkbenchSession(new WorkbenchDocument([], []));
        controller.Begin(null);
        controller.EnsureCheckpoint(session);

        var canceled = controller.Cancel();

        Assert.NotNull(canceled);
        Assert.NotNull(canceled.Rollback);
        Assert.Null(canceled.BindingTargetRefresh);
        Assert.False(canceled.DerivedStateRefresh.RefreshCounters);
    }
}

