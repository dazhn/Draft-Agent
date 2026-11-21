using DraftAgent.App.ViewModels;
using DraftAgent.App.Views;

namespace DraftAgent.App.Tests;

public sealed class WorkbenchRowContextTests
{
    [Fact]
    public void ResolveReturnsRowFromFlatDisplayItem()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var item = new WorkbenchRowDisplayItem(scene, row);

        var resolved = WorkbenchRowContext.Resolve(item);

        Assert.Same(row, resolved);
    }

    [Fact]
    public void ResolveKeepsExistingRowContextSupported()
    {
        var row = AnalysisResultRowViewModel.CreateManualNarration(1);

        var resolved = WorkbenchRowContext.Resolve(row);

        Assert.Same(row, resolved);
    }

    [Fact]
    public void ResolveReturnsNullForSceneAndEmptyItems()
    {
        var scene = SceneTableViewModel.CreateManual(1);

        Assert.Null(WorkbenchRowContext.Resolve(new WorkbenchSceneHeaderDisplayItem(scene)));
        Assert.Null(WorkbenchRowContext.Resolve(new WorkbenchEmptySceneDisplayItem(scene)));
        Assert.Null(WorkbenchRowContext.Resolve(null));
    }
}
