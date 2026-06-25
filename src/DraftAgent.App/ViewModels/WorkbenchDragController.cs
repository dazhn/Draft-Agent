using DraftAgent.Core;

namespace DraftAgent.App.ViewModels;

internal sealed class WorkbenchDragController
{
    private WorkbenchSessionCheckpoint? _checkpoint;
    private string? _selectedInteractionId;
    private WorkbenchDragPreviewIntent? _lastPreviewIntent;
    private WorkbenchBindingTargetRefresh? _pendingCompleteBindingTargetRefresh;
    private WorkbenchDragPreviewMetrics _metrics;
    private bool _changed;

    public bool IsActive { get; private set; }

    public bool HasChanged => _changed;

    public WorkbenchDragPreviewMetrics Metrics => _metrics;

    public void Begin(AnalysisResultRowViewModel? selectedRow)
    {
        IsActive = true;
        _checkpoint = null;
        _selectedInteractionId = selectedRow?.InteractionId;
        _lastPreviewIntent = null;
        _pendingCompleteBindingTargetRefresh = null;
        _metrics = WorkbenchDragPreviewMetrics.Empty;
        _changed = false;
    }

    public void EnsureCheckpoint(WorkbenchSession session)
    {
        if (IsActive && _checkpoint is null)
        {
            _checkpoint = session.CreateCheckpoint();
            _metrics = _metrics with { CheckpointCreated = true };
        }
    }

    public void MarkChanged(WorkbenchBindingTargetRefresh? completeBindingTargetRefresh = null)
    {
        if (IsActive)
        {
            _changed = true;
            if (completeBindingTargetRefresh is not null)
            {
                _pendingCompleteBindingTargetRefresh = completeBindingTargetRefresh;
            }

            _metrics = _metrics with { AppliedPreviewCount = _metrics.AppliedPreviewCount + 1 };
        }
    }

    public void MarkNoOpPreview()
    {
        if (IsActive)
        {
            _metrics = _metrics with { SkippedNoOpPreviewCount = _metrics.SkippedNoOpPreviewCount + 1 };
        }
    }

    public bool ShouldPreview(WorkbenchDragPreviewIntent intent)
    {
        if (!IsActive)
        {
            return true;
        }

        _metrics = _metrics with { PreviewAttemptCount = _metrics.PreviewAttemptCount + 1 };
        if (_lastPreviewIntent == intent)
        {
            _metrics = _metrics with { SkippedRepeatedPreviewCount = _metrics.SkippedRepeatedPreviewCount + 1 };
            return false;
        }

        _lastPreviewIntent = intent;
        _metrics = _metrics with { DistinctPreviewIntentCount = _metrics.DistinctPreviewIntentCount + 1 };
        return true;
    }

    public WorkbenchDragCancelResult? Cancel()
    {
        if (!IsActive)
        {
            return null;
        }

        var rollback = _checkpoint is null
            ? null
            : new WorkbenchDragRollback(_checkpoint, _selectedInteractionId);
        Clear();
        return rollback is null
            ? null
            : new WorkbenchDragCancelResult(
                rollback,
                null,
                WorkbenchDerivedStateRefresh.SessionOnly);
    }

    public WorkbenchDragCompleteResult? Complete()
    {
        var checkpoint = _changed ? _checkpoint : null;
        var bindingTargetRefresh = _pendingCompleteBindingTargetRefresh;
        Clear();
        return checkpoint is null
            ? null
            : new WorkbenchDragCompleteResult(
                checkpoint,
                bindingTargetRefresh,
                WorkbenchDerivedStateRefresh.SessionOnly);
    }

    private void Clear()
    {
        IsActive = false;
        _checkpoint = null;
        _selectedInteractionId = null;
        _lastPreviewIntent = null;
        _pendingCompleteBindingTargetRefresh = null;
        _changed = false;
    }
}

internal enum WorkbenchDragPreviewIntentKind
{
    RowToRow,
    RowToSceneEnd,
    SceneToScene
}

internal readonly record struct WorkbenchDragPreviewIntent(
    WorkbenchDragPreviewIntentKind Kind,
    string DraggedId,
    string TargetId,
    WorkbenchDropPosition? Position);

internal readonly record struct WorkbenchDragPreviewMetrics(
    int PreviewAttemptCount,
    int DistinctPreviewIntentCount,
    int SkippedRepeatedPreviewCount,
    int SkippedNoOpPreviewCount,
    int AppliedPreviewCount,
    bool CheckpointCreated)
{
    public static WorkbenchDragPreviewMetrics Empty => new(0, 0, 0, 0, 0, false);
}

internal sealed record WorkbenchDragRollback(
    WorkbenchSessionCheckpoint Checkpoint,
    string? SelectedInteractionId);

internal sealed record WorkbenchDragCompleteResult(
    WorkbenchSessionCheckpoint Checkpoint,
    WorkbenchBindingTargetRefresh? BindingTargetRefresh,
    WorkbenchDerivedStateRefresh DerivedStateRefresh);

internal sealed record WorkbenchDragCancelResult(
    WorkbenchDragRollback Rollback,
    WorkbenchBindingTargetRefresh? BindingTargetRefresh,
    WorkbenchDerivedStateRefresh DerivedStateRefresh);
