using System;
using Avalonia;
using Avalonia.Controls;
using DraftAgent.App.ViewModels;

namespace DraftAgent.App.Views;

internal sealed class WorkbenchDragInteractionCoordinator
{
    private readonly WorkbenchActiveDragState _activeDrag;
    private readonly WorkbenchDragPreviewSession _previewSession;
    private readonly WorkbenchDragTargetRefresh _targetRefresh;
    private bool _hasAppliedPreview;

    public WorkbenchDragInteractionCoordinator()
        : this(new WorkbenchActiveDragState(), new WorkbenchDragPreviewSession(), new WorkbenchDragTargetRefresh())
    {
    }

    public WorkbenchDragInteractionCoordinator(
        WorkbenchActiveDragState activeDrag,
        WorkbenchDragPreviewSession previewSession,
        WorkbenchDragTargetRefresh targetRefresh)
    {
        _activeDrag = activeDrag;
        _previewSession = previewSession;
        _targetRefresh = targetRefresh;
    }

    public bool IsActive => _activeDrag.IsActive;

    public void AttachDropIndicatorOverlay(Canvas host)
    {
        _previewSession.AttachDropIndicatorOverlay(host);
    }

    public void BeginRowDrag(
        MainWindowViewModel viewModel,
        AnalysisResultRowViewModel row,
        Visual? sourceVisual,
        Visual coordinateRoot)
    {
        viewModel.BeginWorkbenchDrag();
        _activeDrag.StartRow(row);
        _hasAppliedPreview = false;
        _targetRefresh.Reset();
        _previewSession.BeginRow(row, sourceVisual, coordinateRoot);
    }

    public void BeginSceneDrag(
        MainWindowViewModel viewModel,
        SceneTableViewModel scene,
        Visual? sourceVisual,
        Visual coordinateRoot)
    {
        viewModel.BeginWorkbenchDrag();
        _activeDrag.StartScene(scene);
        _hasAppliedPreview = false;
        _targetRefresh.Reset();
        _previewSession.BeginScene(scene, sourceVisual, coordinateRoot);
    }

    public void Preview(
        MainWindowViewModel viewModel,
        Visual? hit,
        Visual coordinateRoot)
    {
        if (_activeDrag.Row is not null)
        {
            if (_previewSession.PreviewRow(_activeDrag.Row, hit, coordinateRoot))
            {
                _targetRefresh.AcceptEnteredRowTarget(hit);
                PreviewPending(viewModel);
                return;
            }

            LogRowPreviewMissOnce();
            if (hit is null)
            {
                ClearDropIntent();
            }
        }
        else if (_activeDrag.Scene is not null)
        {
            if (_previewSession.PreviewScene(_activeDrag.Scene, hit, coordinateRoot, viewModel.SceneTables))
            {
                _targetRefresh.AcceptEnteredSceneTarget(hit);
                PreviewPending(viewModel);
                return;
            }

            LogScenePreviewMissOnce();
            ClearDropIntent();
        }
    }

    public bool RefreshTargetFromPointer(
        object? dataContext,
        Visual coordinateRoot,
        Point pointerPosition,
        DateTimeOffset now)
    {
        if (dataContext is not MainWindowViewModel viewModel)
        {
            return false;
        }

        Visual target;
        if (_activeDrag.Row is not null)
        {
            if (!_targetRefresh.TryResolveNextRowTarget(
                    coordinateRoot,
                    pointerPosition,
                    now,
                    _activeDrag.Row,
                    out target))
            {
                return false;
            }

            if (ShouldKeepCurrentRowTarget(target))
            {
                _targetRefresh.AcceptEnteredRowTarget(target);
                return false;
            }
        }
        else if (_activeDrag.Scene is not null)
        {
            if (!_targetRefresh.TryResolveNextSceneTarget(coordinateRoot, pointerPosition, now, out target))
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        Preview(viewModel, target, coordinateRoot);
        return true;
    }

    private bool ShouldKeepCurrentRowTarget(Visual target)
    {
        if (_previewSession.PendingDrop is not
            {
                Kind: WorkbenchDropIntentKind.RowToRow,
                Target: AnalysisResultRowViewModel currentTargetRow
            })
        {
            return false;
        }

        return WorkbenchDropTargetResolver.TryResolveExplicitTableTarget(target, out var targetScene) &&
               ReferenceEquals(currentTargetRow.OwnerScene, targetScene);
    }

    public void CommitPending(MainWindowViewModel viewModel)
    {
        if (_hasAppliedPreview)
        {
            return;
        }

        if (_previewSession.PendingDrop is not { } intent)
        {
            return;
        }

        WorkbenchDropIntentDispatcher.Commit(viewModel, intent, _activeDrag.Row, _activeDrag.Scene);
    }

    public void End(MainWindowViewModel viewModel, bool complete)
    {
        if (complete)
        {
            LogDragEnding("complete");
            CommitPending(viewModel);

            viewModel.CompleteWorkbenchDrag();
        }
        else
        {
            LogDragEnding("cancel");
            viewModel.CancelWorkbenchDrag();
        }
    }

    public void EndAndReset(MainWindowViewModel viewModel, bool complete)
    {
        End(viewModel, complete);
        Reset();
    }

    public void LogDragEnding(string action)
    {
        if (_activeDrag.Row is not null)
        {
            AppRuntimeLog.Info(
                $"Workbench row drag {action}: interactionId={_activeDrag.Row.InteractionId}; pending={WorkbenchDropIntent.Format(_previewSession.PendingDrop)}");
        }
        else if (_activeDrag.Scene is not null)
        {
            AppRuntimeLog.Info(
                $"Workbench scene drag {action}: sceneId={_activeDrag.Scene.SceneId}; pending={WorkbenchDropIntent.Format(_previewSession.PendingDrop)}");
        }
    }

    public void LogRowPreviewMissOnce()
    {
        if (_activeDrag.Row is not null &&
            _activeDrag.TryMarkPreviewMissLogged())
        {
            AppRuntimeLog.Info($"Workbench row drag preview missed: interactionId={_activeDrag.Row.InteractionId}");
        }
    }

    public void LogScenePreviewMissOnce()
    {
        if (_activeDrag.Scene is not null &&
            _activeDrag.TryMarkPreviewMissLogged())
        {
            AppRuntimeLog.Info($"Workbench scene drag preview missed: draggedSceneId={_activeDrag.Scene.SceneId}");
        }
    }

    public void ClearDropIntent()
    {
        _previewSession.ClearDropIntent();
    }

    public void Reset()
    {
        _activeDrag.Clear();
        _previewSession.Clear();
        _targetRefresh.Reset();
        _hasAppliedPreview = false;
    }

    private void PreviewPending(MainWindowViewModel viewModel)
    {
        if (_previewSession.PendingDrop is not { } intent)
        {
            return;
        }

        var appliedPreview = WorkbenchDropIntentDispatcher.Preview(
            viewModel,
            intent,
            _activeDrag.Row,
            _activeDrag.Scene);
        _hasAppliedPreview |= appliedPreview;
    }
}
