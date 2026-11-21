using System;
using System.Collections.Generic;
using System.Linq;
using PsdLayerLoader.Core;

namespace DraftAgent.App.Psd.Services;

internal sealed class PsdLayerSelectionResolver
{
    private readonly PsdLayerMatchService _matcher;

    public PsdLayerSelectionResolver()
        : this(new PsdLayerMatchService())
    {
    }

    public PsdLayerSelectionResolver(PsdLayerMatchService matcher)
    {
        _matcher = matcher;
    }

    public PsdLayerResolvedSelection ResolveForRequest(
        PsdLayerCoreSnapshot snapshot,
        PsdLayerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.SelectionHint is not null)
        {
            return ResolveSelectionHint(snapshot, request.SelectionHint);
        }

        var document = CreateDocumentInfo(snapshot);
        var match = _matcher.Match(
            document,
            request.Intent.Emotion,
            request.Intent.Action,
            request.Intent.Text,
            Array.Empty<PsdLayerPreset>());
        if (match.LayerIds.Count > 0)
        {
            return new PsdLayerResolvedSelection(
                match.LayerIds.ToHashSet(),
                match.LayerIds.ToHashSet(),
                new HashSet<int>(),
                match);
        }

        return new PsdLayerResolvedSelection(
            snapshot.VisibleLayerIds.ToHashSet(),
            snapshot.VisibleLayerIds.ToHashSet(),
            new HashSet<int>(),
            match);
    }

    public PsdLayerResolvedSelection ResolveSelectionHint(
        PsdLayerCoreSnapshot snapshot,
        PsdLayerSelectionHint hint)
    {
        var layers = snapshot.DisplayLayers
            .Where(layer => !layer.IsGroup)
            .ToArray();
        var selectedLayerIds = new List<int>();
        var ids = layers.Select(layer => layer.LayerId).ToHashSet();
        foreach (var layerId in hint.LayerIds)
        {
            if (ids.Contains(layerId))
            {
                selectedLayerIds.Add(layerId);
            }
        }

        var layersByName = layers
            .Where(layer => !string.IsNullOrWhiteSpace(layer.Name))
            .GroupBy(layer => layer.Name.Trim(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        foreach (var layerName in hint.LayerNames)
        {
            if (string.IsNullOrWhiteSpace(layerName) ||
                !layersByName.TryGetValue(layerName.Trim(), out var matches) ||
                matches.Length != 1)
            {
                continue;
            }

            if (!selectedLayerIds.Contains(matches[0].LayerId))
            {
                selectedLayerIds.Add(matches[0].LayerId);
            }
        }

        var controlledLayerIds = ResolveControlledLayerIds(layers, hint);
        var selectedSet = selectedLayerIds.Distinct().ToHashSet();
        var visibleLayerIds = controlledLayerIds.Count == 0
            ? selectedSet
            : snapshot.VisibleLayerIds
                .Where(layerId => !controlledLayerIds.Contains(layerId))
                .Concat(selectedSet)
                .ToHashSet();
        return new PsdLayerResolvedSelection(
            selectedSet,
            visibleLayerIds,
            controlledLayerIds,
            null);
    }

    public PsdDocumentInfo CreateDocumentInfo(PsdLayerCoreSnapshot snapshot)
    {
        var childrenByParent = snapshot.DisplayLayers
            .GroupBy(layer => layer.ParentLayerId ?? PsdLayerOperationMapper.RootGroupLayerId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var roots = BuildChildren(PsdLayerOperationMapper.RootGroupLayerId, childrenByParent);
        return new PsdDocumentInfo(
            snapshot.PsdPath,
            snapshot.PsdPath,
            System.IO.Path.GetFileNameWithoutExtension(snapshot.PsdPath),
            snapshot.Document.Width,
            snapshot.Document.Height,
            roots);
    }

    private static HashSet<int> ResolveControlledLayerIds(
        IReadOnlyList<PsdNativeLayerInfo> layers,
        PsdLayerSelectionHint hint)
    {
        var controlledLayerIds = new HashSet<int>();
        var layerIds = layers.Select(layer => layer.LayerId).ToHashSet();
        if (hint.ControlledLayerIds is not null)
        {
            foreach (var layerId in hint.ControlledLayerIds)
            {
                if (layerIds.Contains(layerId))
                {
                    controlledLayerIds.Add(layerId);
                }
            }
        }

        if (controlledLayerIds.Count > 0)
        {
            return controlledLayerIds;
        }

        if (hint.ControlledLayerNames is null || hint.ControlledLayerNames.Count == 0)
        {
            return controlledLayerIds;
        }

        var controlledNames = hint.ControlledLayerNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToHashSet(StringComparer.Ordinal);
        foreach (var layerId in layers
            .Where(layer => controlledNames.Contains(layer.Name))
            .Select(layer => layer.LayerId))
        {
            controlledLayerIds.Add(layerId);
        }

        return controlledLayerIds;
    }

    private static IReadOnlyList<PsdLayerNode> BuildChildren(
        int parentLayerKey,
        IReadOnlyDictionary<int, PsdNativeLayerInfo[]> childrenByParent)
    {
        if (!childrenByParent.TryGetValue(parentLayerKey, out var children))
        {
            return Array.Empty<PsdLayerNode>();
        }

        return children
            .Select(layer => new PsdLayerNode(
                layer.LayerId,
                layer.ParentLayerId,
                string.IsNullOrWhiteSpace(layer.Name) ? $"Layer {layer.LayerId}" : layer.Name,
                layer.IsGroup ? PsdLayerKind.Group : PsdLayerKind.Raster,
                layer.IsGroup,
                layer.IsVisible,
                layer.Left,
                layer.Top,
                layer.Width,
                layer.Height,
                BuildChildren(layer.LayerId, childrenByParent)))
            .ToArray();
    }
}

internal sealed record PsdLayerResolvedSelection(
    IReadOnlySet<int> SelectedLayerIds,
    IReadOnlySet<int> VisibleLayerIds,
    IReadOnlySet<int> ControlledLayerIds,
    PsdLayerMatchResult? Match);

