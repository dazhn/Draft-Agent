using DraftAgent.App.ViewModels;

namespace DraftAgent.App.Views;

internal static class WorkbenchDropIntentDispatcher
{
    public static bool Preview(
        MainWindowViewModel viewModel,
        WorkbenchDropIntent intent,
        AnalysisResultRowViewModel? draggedRow,
        SceneTableViewModel? draggedScene)
    {
        return intent.Kind switch
        {
            WorkbenchDropIntentKind.RowToRow when
                draggedRow is not null &&
                intent.Target is AnalysisResultRowViewModel targetRow &&
                intent.Position is { } position =>
                    viewModel.PreviewMoveInteractionRow(draggedRow, targetRow, position),
            WorkbenchDropIntentKind.RowToSceneEnd when
                draggedRow is not null &&
                intent.Target is SceneTableViewModel targetScene =>
                    viewModel.PreviewMoveInteractionRowToSceneEnd(draggedRow, targetScene),
            WorkbenchDropIntentKind.SceneToScene when
                draggedScene is not null &&
                intent.Target is SceneTableViewModel targetScene =>
                    viewModel.PreviewMoveScene(
                        draggedScene,
                        targetScene,
                        ResolveCurrentSceneDropPosition(viewModel, draggedScene, targetScene, intent.Position)),
            _ => false
        };
    }

    public static void Commit(
        MainWindowViewModel viewModel,
        WorkbenchDropIntent intent,
        AnalysisResultRowViewModel? draggedRow,
        SceneTableViewModel? draggedScene)
    {
        switch (intent.Kind)
        {
            case WorkbenchDropIntentKind.RowToRow when draggedRow is not null && intent.Position is not null:
                viewModel.MoveInteractionRow(
                    draggedRow,
                    intent.Target as AnalysisResultRowViewModel,
                    intent.Position.Value);
                break;
            case WorkbenchDropIntentKind.RowToSceneEnd when draggedRow is not null:
                viewModel.MoveInteractionRowToSceneEnd(
                    draggedRow,
                    intent.Target as SceneTableViewModel);
                break;
            case WorkbenchDropIntentKind.SceneToScene when draggedScene is not null && intent.Target is SceneTableViewModel targetScene:
                viewModel.MoveScene(
                    draggedScene,
                    targetScene,
                    ResolveCurrentSceneDropPosition(viewModel, draggedScene, targetScene, intent.Position));
                break;
        }
    }

    private static WorkbenchDropPosition ResolveCurrentSceneDropPosition(
        MainWindowViewModel viewModel,
        SceneTableViewModel draggedScene,
        SceneTableViewModel targetScene,
        WorkbenchDropPosition? fallback)
    {
        var draggedIndex = viewModel.SceneTables.IndexOf(draggedScene);
        var targetIndex = viewModel.SceneTables.IndexOf(targetScene);
        if (draggedIndex >= 0 &&
            targetIndex >= 0 &&
            draggedIndex != targetIndex)
        {
            return draggedIndex < targetIndex
                ? WorkbenchDropPosition.After
                : WorkbenchDropPosition.Before;
        }

        return fallback ?? WorkbenchDropPosition.Before;
    }
}
