using DraftAgent.App.ViewModels;

namespace DraftAgent.App.Views;

internal sealed class WorkbenchDragDisplacementState
{
    private AnalysisResultRowViewModel? _activeRow;
    private SceneTableViewModel? _activeScene;

    public void StartRow(AnalysisResultRowViewModel sourceRow)
    {
        Clear();
        _activeRow = sourceRow;
        sourceRow.IsDragSourceActive = true;
    }

    public void StartScene(SceneTableViewModel sourceScene)
    {
        Clear();
        _activeScene = sourceScene;
        sourceScene.IsDragSourceActive = true;
    }

    public void SetRow(AnalysisResultRowViewModel draggedRow)
    {
    }

    public void SetScene(SceneTableViewModel draggedScene)
    {
    }

    public void ClearTarget()
    {
    }

    public void Clear()
    {
        if (_activeRow is not null)
        {
            _activeRow.IsDragSourceActive = false;
        }

        if (_activeScene is not null)
        {
            _activeScene.IsDragSourceActive = false;
        }

        _activeRow = null;
        _activeScene = null;
    }
}
