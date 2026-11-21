namespace DraftAgent.Core;

public sealed class WorkbenchProjectApplier
{
    private readonly WorkbenchEditingService _editingService;

    public WorkbenchProjectApplier(WorkbenchEditingService editingService)
    {
        _editingService = editingService;
    }

    public InteractionProject Apply(InteractionProject project, WorkbenchDocument document)
    {
        return _editingService.Apply(project, document);
    }
}
