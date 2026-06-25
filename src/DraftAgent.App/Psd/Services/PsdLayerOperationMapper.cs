using System;
using System.Collections.Generic;
using System.Linq;
using PsdLayerLoader.Core;

namespace DraftAgent.App.Psd.Services;

internal static class PsdLayerOperationMapper
{
    public const int RootGroupLayerId = -1;

    public static IReadOnlySet<int> ToggleLayer(
        IReadOnlySet<int> currentVisibleLayerIds,
        int layerId)
    {
        var next = currentVisibleLayerIds.ToHashSet();
        if (!next.Add(layerId))
        {
            next.Remove(layerId);
        }

        return next;
    }

    public static IReadOnlySet<int> ReplaceGroupExclusive(
        IReadOnlySet<int> currentVisibleLayerIds,
        IEnumerable<int> groupLayerIds,
        int selectedLayerId,
        bool isMultiSelect)
    {
        var groupSet = groupLayerIds.ToHashSet();
        var next = currentVisibleLayerIds
            .Where(layerId => !groupSet.Contains(layerId))
            .ToHashSet();
        if (isMultiSelect)
        {
            foreach (var layerId in currentVisibleLayerIds.Where(groupSet.Contains))
            {
                next.Add(layerId);
            }

            if (!next.Add(selectedLayerId))
            {
                next.Remove(selectedLayerId);
            }
        }
        else
        {
            next.Add(selectedLayerId);
        }

        return next;
    }

    public static IReadOnlySet<int> HideGroup(
        IReadOnlySet<int> currentVisibleLayerIds,
        IEnumerable<int> groupLayerIds)
    {
        var groupSet = groupLayerIds.ToHashSet();
        return currentVisibleLayerIds
            .Where(layerId => !groupSet.Contains(layerId))
            .ToHashSet();
    }

    public static IReadOnlySet<int> NormalizeExclusiveGroups(
        IReadOnlySet<int> currentVisibleLayerIds,
        IReadOnlyList<PsdNativeLayerInfo> layers)
    {
        var next = currentVisibleLayerIds.ToHashSet();
        foreach (var group in layers.Where(layer => !layer.IsGroup).GroupBy(layer => layer.ParentLayerId))
        {
            if (!IsExclusiveGroup(layers, group.Key))
            {
                continue;
            }

            var firstVisible = group.FirstOrDefault(layer => next.Contains(layer.LayerId));
            var keep = firstVisible?.LayerId;
            foreach (var layer in group)
            {
                next.Remove(layer.LayerId);
            }

            if (keep is not null)
            {
                next.Add(keep.Value);
            }
        }

        return next;
    }

    public static IReadOnlySet<int> ReplaceControlled(
        IReadOnlySet<int> currentVisibleLayerIds,
        IEnumerable<int> controlledLayerIds,
        IEnumerable<int> selectedControlledLayerIds)
    {
        var controlled = controlledLayerIds.ToHashSet();
        return currentVisibleLayerIds
            .Where(layerId => !controlled.Contains(layerId))
            .Concat(selectedControlledLayerIds)
            .ToHashSet();
    }

    public static IReadOnlyList<PsdNativeLayerInfo> DirectChildLayers(
        IReadOnlyList<PsdNativeLayerInfo> layers,
        int? parentLayerId)
    {
        return layers
            .Where(layer => !layer.IsGroup && layer.ParentLayerId == parentLayerId)
            .OrderByDescending(layer => layer.LayerId)
            .ToArray();
    }

    public static IReadOnlyList<int> DirectChildLayerIds(
        IReadOnlyList<PsdNativeLayerInfo> layers,
        int? parentLayerId)
    {
        return DirectChildLayers(layers, parentLayerId)
            .Select(layer => layer.LayerId)
            .ToArray();
    }

    public static bool IsExclusiveGroup(
        IReadOnlyList<PsdNativeLayerInfo> layers,
        int? groupLayerId)
    {
        var directLayers = layers
            .Where(layer => !layer.IsGroup && layer.ParentLayerId == groupLayerId)
            .ToArray();
        return directLayers.Length > 0 &&
            directLayers.Count(layer => layer.IsVisible) <= 1;
    }

    public static IReadOnlyDictionary<int, PsdLayerGroupSelection> BuildGroupSelections(
        IReadOnlyList<PsdNativeLayerInfo> layers,
        IReadOnlySet<int> visibleLayerIds)
    {
        return layers
            .Where(layer => !layer.IsGroup && visibleLayerIds.Contains(layer.LayerId))
            .GroupBy(layer => layer.ParentLayerId ?? RootGroupLayerId)
            .ToDictionary(
                group => group.Key,
                group => new PsdLayerGroupSelection(
                    group.Key,
                    !IsExclusiveGroup(layers, group.Key == RootGroupLayerId ? null : group.Key),
                    group.Select(layer => layer.LayerId).ToHashSet()));
    }
}
