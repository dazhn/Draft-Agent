using PsdLayerLoader.Core;

namespace PsdLayerLoader.Core.Tests;

public sealed class PsdLayerSelectionServiceTests
{
    [Fact]
    public void SingleSelectKeepsOnlyOneVisibleLayerInsideTheSameGroup()
    {
        var state = PsdLayerSelectionService.Empty("doc");

        state = PsdLayerSelectionService.ToggleLayer(state, groupLayerId: 10, childLayerId: 101, isMultiSelect: false);
        state = PsdLayerSelectionService.ToggleLayer(state, groupLayerId: 10, childLayerId: 102, isMultiSelect: false);

        Assert.Equal([102], state.GroupSelections[10].VisibleChildLayerIds);
        Assert.Equal([102], state.VisibleLayerIds);
        Assert.Equal([102], state.SelectedLayerIds);
    }

    [Fact]
    public void SingleSelectDoesNotClearOtherGroups()
    {
        var state = PsdLayerSelectionService.Empty("doc");

        state = PsdLayerSelectionService.ToggleLayer(state, groupLayerId: 10, childLayerId: 101, isMultiSelect: false);
        state = PsdLayerSelectionService.ToggleLayer(state, groupLayerId: 20, childLayerId: 201, isMultiSelect: false);
        state = PsdLayerSelectionService.ToggleLayer(state, groupLayerId: 10, childLayerId: 102, isMultiSelect: false);

        Assert.Equal([102], state.GroupSelections[10].VisibleChildLayerIds);
        Assert.Equal([201], state.GroupSelections[20].VisibleChildLayerIds);
        Assert.Equal([102, 201], state.VisibleLayerIds.Order());
    }

    [Fact]
    public void MultiSelectTogglesLayerWithoutHidingPreviouslyVisibleLayers()
    {
        var state = PsdLayerSelectionService.Empty("doc");

        state = PsdLayerSelectionService.ToggleLayer(state, groupLayerId: 10, childLayerId: 101, isMultiSelect: true);
        state = PsdLayerSelectionService.ToggleLayer(state, groupLayerId: 10, childLayerId: 102, isMultiSelect: true);

        Assert.Equal([101, 102], state.GroupSelections[10].VisibleChildLayerIds.Order());
        Assert.Equal([101, 102], state.VisibleLayerIds.Order());
    }

    [Fact]
    public void MultiSelectClickingVisibleLayerAgainHidesOnlyThatLayer()
    {
        var state = PsdLayerSelectionService.Empty("doc");

        state = PsdLayerSelectionService.ToggleLayer(state, groupLayerId: 10, childLayerId: 101, isMultiSelect: true);
        state = PsdLayerSelectionService.ToggleLayer(state, groupLayerId: 10, childLayerId: 102, isMultiSelect: true);
        state = PsdLayerSelectionService.ToggleLayer(state, groupLayerId: 10, childLayerId: 101, isMultiSelect: true);

        Assert.Equal([102], state.GroupSelections[10].VisibleChildLayerIds);
        Assert.Equal([102], state.VisibleLayerIds);
    }

    [Fact]
    public void HideGroupClearsOnlyThatGroup()
    {
        var state = PsdLayerSelectionService.Empty("doc");

        state = PsdLayerSelectionService.ToggleLayer(state, groupLayerId: 10, childLayerId: 101, isMultiSelect: true);
        state = PsdLayerSelectionService.ToggleLayer(state, groupLayerId: 10, childLayerId: 102, isMultiSelect: true);
        state = PsdLayerSelectionService.ToggleLayer(state, groupLayerId: 20, childLayerId: 201, isMultiSelect: true);
        state = PsdLayerSelectionService.HideGroup(state, groupLayerId: 10);

        Assert.Empty(state.GroupSelections[10].VisibleChildLayerIds);
        Assert.Equal([201], state.GroupSelections[20].VisibleChildLayerIds);
        Assert.Equal([201], state.VisibleLayerIds);
        Assert.Equal([201], state.SelectedLayerIds);
    }
}

