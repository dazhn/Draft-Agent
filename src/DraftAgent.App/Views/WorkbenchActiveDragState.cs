using DraftAgent.App.ViewModels;

namespace DraftAgent.App.Views;

internal sealed class WorkbenchActiveDragState
{
    public AnalysisResultRowViewModel? Row { get; private set; }

    public SceneTableViewModel? Scene { get; private set; }

    public bool IsActive => Row is not null || Scene is not null;

    public void StartRow(AnalysisResultRowViewModel row)
    {
        Row = row;
        Scene = null;
        ResetInteractionState();
    }

    public void StartScene(SceneTableViewModel scene)
    {
        Row = null;
        Scene = scene;
        ResetInteractionState();
    }

    public bool TryMarkPreviewMissLogged()
    {
        if (_previewMissLogged)
        {
            return false;
        }

        _previewMissLogged = true;
        return true;
    }

    public void Clear()
    {
        Row = null;
        Scene = null;
        ResetInteractionState();
    }

    private bool _previewMissLogged;

    private void ResetInteractionState()
    {
        _previewMissLogged = false;
    }
}
