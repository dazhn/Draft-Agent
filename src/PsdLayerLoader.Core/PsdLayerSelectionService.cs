namespace PsdLayerLoader.Core;

public static class PsdLayerSelectionService
{
    public static PsdSelectionState Empty(string documentId)
    {
        return new PsdSelectionState(
            documentId,
            new Dictionary<int, PsdLayerGroupSelection>(),
            new HashSet<int>(),
            new HashSet<int>(),
            null);
    }

    public static PsdSelectionState ToggleLayer(
        PsdSelectionState state,
        int groupLayerId,
        int childLayerId,
        bool isMultiSelect)
    {
        var groups = state.GroupSelections.ToDictionary(pair => pair.Key, pair => pair.Value);
        var existing = groups.TryGetValue(groupLayerId, out var group)
            ? group.VisibleChildLayerIds
            : new HashSet<int>();

        HashSet<int> nextVisibleChildren;
        if (isMultiSelect)
        {
            nextVisibleChildren = new HashSet<int>(existing);
            if (!nextVisibleChildren.Add(childLayerId))
            {
                nextVisibleChildren.Remove(childLayerId);
            }
        }
        else
        {
            nextVisibleChildren = new HashSet<int> { childLayerId };
        }

        groups[groupLayerId] = new PsdLayerGroupSelection(groupLayerId, isMultiSelect, nextVisibleChildren);
        var visibleLayerIds = groups.Values
            .SelectMany(selection => selection.VisibleChildLayerIds)
            .ToHashSet();

        return state with
        {
            GroupSelections = groups,
            VisibleLayerIds = visibleLayerIds,
            SelectedLayerIds = new HashSet<int> { childLayerId },
            CurrentGroupLayerId = groupLayerId
        };
    }

    public static PsdSelectionState HideGroup(
        PsdSelectionState state,
        int groupLayerId)
    {
        var groups = state.GroupSelections.ToDictionary(pair => pair.Key, pair => pair.Value);
        groups[groupLayerId] = new PsdLayerGroupSelection(
            groupLayerId,
            groups.TryGetValue(groupLayerId, out var existing) && existing.IsMultiSelect,
            new HashSet<int>());

        var visibleLayerIds = groups.Values
            .SelectMany(selection => selection.VisibleChildLayerIds)
            .ToHashSet();
        var selectedLayerIds = state.SelectedLayerIds
            .Where(visibleLayerIds.Contains)
            .ToHashSet();

        return state with
        {
            GroupSelections = groups,
            VisibleLayerIds = visibleLayerIds,
            SelectedLayerIds = selectedLayerIds,
            CurrentGroupLayerId = groupLayerId
        };
    }
}
