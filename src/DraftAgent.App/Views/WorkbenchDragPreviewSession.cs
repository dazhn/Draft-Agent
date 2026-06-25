using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using DraftAgent.App.ViewModels;

namespace DraftAgent.App.Views;

internal sealed class WorkbenchDragPreviewSession
{
    private readonly WorkbenchDropIndicatorState _dropIndicator;
    private readonly WorkbenchDragDisplacementState _displacement;

    public WorkbenchDragPreviewSession()
        : this(new WorkbenchDropIndicatorState(), new WorkbenchDragDisplacementState())
    {
    }

    public WorkbenchDragPreviewSession(
        WorkbenchDropIndicatorState dropIndicator,
        WorkbenchDragDisplacementState displacement)
    {
        _dropIndicator = dropIndicator;
        _displacement = displacement;
    }

    public WorkbenchDropIntent? PendingDrop { get; private set; }

    public void AttachDropIndicatorOverlay(Canvas host)
    {
        _dropIndicator.Attach(host);
    }

    public void BeginRow(AnalysisResultRowViewModel row, Visual? sourceVisual, Visual coordinateRoot)
    {
        Clear();
        _displacement.StartRow(row);
    }

    public void BeginScene(SceneTableViewModel scene, Visual? sourceVisual, Visual coordinateRoot)
    {
        Clear();
        _displacement.StartScene(scene);
    }

    public bool PreviewRow(
        AnalysisResultRowViewModel row,
        Visual? hit,
        Visual coordinateRoot)
    {
        if (WorkbenchDropTargetResolver.TryCreateRowDropIntent(
                hit,
                row,
                out var intent,
                out var indicatorVisual))
        {
            UpdatePreview(intent, indicatorVisual, coordinateRoot, row);
            return true;
        }

        return false;
    }

    public bool PreviewScene(
        SceneTableViewModel scene,
        Visual? hit,
        Visual coordinateRoot,
        IReadOnlyList<SceneTableViewModel>? currentScenes)
    {
        if (!WorkbenchDropTargetResolver.TryCreateSceneDropIntent(
                hit,
                scene,
                currentScenes,
                out var intent,
                out var indicatorVisual))
        {
            return WorkbenchDropTargetResolver.IsRowDragTargetSource(hit);
        }

        UpdatePreview(intent, indicatorVisual, coordinateRoot, draggedRow: null, scene);
        return true;
    }

    public void ClearDropIntent()
    {
        PendingDrop = null;
        _dropIndicator.Clear();
        _displacement.ClearTarget();
    }

    public void Clear()
    {
        ClearDropIntent();
        _displacement.Clear();
    }

    private void UpdatePreview(
        WorkbenchDropIntent intent,
        Visual? indicatorVisual,
        Visual coordinateRoot,
        AnalysisResultRowViewModel? draggedRow,
        SceneTableViewModel? draggedScene = null)
    {
        var resolvedIndicator = WorkbenchDropTargetResolver.ResolveDropIndicatorVisual(indicatorVisual, intent);
        if (intent.Equals(PendingDrop) &&
            _dropIndicator.Matches(resolvedIndicator))
        {
            return;
        }

        PendingDrop = intent;
        LogDropIntent("preview", intent);

        _dropIndicator.Set(resolvedIndicator, intent);
    }

    private static void LogDropIntent(string phase, WorkbenchDropIntent intent)
    {
        AppRuntimeLog.Info($"Workbench drag {phase}: {intent.Format()}");
    }
}
