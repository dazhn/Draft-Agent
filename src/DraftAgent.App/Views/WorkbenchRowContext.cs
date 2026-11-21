using DraftAgent.App.ViewModels;

namespace DraftAgent.App.Views;

internal static class WorkbenchRowContext
{
    public static AnalysisResultRowViewModel? Resolve(object? context)
    {
        return context switch
        {
            AnalysisResultRowViewModel row => row,
            WorkbenchRowDisplayItem rowItem => rowItem.Row,
            WorkbenchDisplayItem { Row: { } row } => row,
            _ => null
        };
    }
}
