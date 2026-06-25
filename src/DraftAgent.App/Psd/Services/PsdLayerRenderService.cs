using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using PsdLayerLoader.Core;

namespace DraftAgent.App.Psd.Services;

internal interface IPsdLayerRenderCore
{
    PsdLayerRenderState LoadState(PsdLayerRenderParameters parameters);

    PsdLayerRenderState WithLayerPixels(
        PsdLayerRenderState state,
        IReadOnlyDictionary<int, PsdLayerPixelData> layerPixels);

    PsdLayerRenderState WithSelectionState(
        PsdLayerRenderState state,
        PsdSelectionState selectionState,
        IReadOnlySet<int>? controlledLayerIds = null);

    PsdDocumentInfo CreateDocumentInfo(PsdLayerRenderState state);

    bool TryApplySelectionHint(
        PsdLayerRenderState state,
        PsdLayerSelectionHint? hint,
        out PsdLayerRenderState nextState);

    PsdLayerMatchResult BuildMatchFromState(
        PsdLayerRequest request,
        PsdLayerRenderState state,
        double confidence,
        string? matchedPresetId);

    PsdMergedPreview? ComposePreview(PsdLayerRenderState state, PsdExportBounds? bounds = null);

    PsdLayerResponse ExportToResponse(
        PsdLayerRequest request,
        PsdLayerRenderState state,
        PsdLayerMatchResult match);
}

internal sealed class PsdLayerRenderService : IPsdLayerRenderCore
{
    private const int RootParentKey = -1;
    private const int PsdSectionDividerLayerType = 3;

    private readonly NativePsdDocumentReader _reader;

    public PsdLayerRenderService()
        : this(new NativePsdDocumentReader())
    {
    }

    public PsdLayerRenderService(NativePsdDocumentReader reader)
    {
        _reader = reader;
    }

    public PsdLayerRenderState LoadState(PsdLayerRenderParameters parameters)
    {
        return CreateState(ReadDocument(parameters));
    }

    private PsdLayerRenderDocument ReadDocument(PsdLayerRenderParameters parameters)
    {
        var psdPath = parameters.PsdPath;
        var nativeDocument = _reader.Load(psdPath);
        var candidateLayerIds = nativeDocument.Layers
            .Where(CanLoadLayerPixels)
            .Select(layer => layer.LayerId)
            .ToHashSet();
        var displayLayers = BuildDisplayLayers(nativeDocument.Layers, candidateLayerIds);
        var renderableLayerBounds = TryGetMetadataUnionBounds(displayLayers, out var unionBounds)
            ? unionBounds
            : (PsdLayerExportRect?)null;
        PsdMergedPreview? mergedPreview = null;
        string? mergedPreviewError = null;

        if (parameters.LoadMergedPreview)
        {
            try
            {
                mergedPreview = _reader.LoadMergedPreview(psdPath);
            }
            catch (Exception ex)
            {
                mergedPreviewError = ex.Message;
            }
        }

        return new PsdLayerRenderDocument(
            parameters with { PsdPath = Path.GetFullPath(psdPath) },
            Path.GetFullPath(psdPath),
            nativeDocument,
            displayLayers,
            new Dictionary<int, PsdLayerPixelData>(),
            renderableLayerBounds,
            mergedPreview,
            mergedPreviewError);
    }

    public PsdLayerRenderState CreateState(PsdLayerRenderDocument document)
    {
        var visibleLayerIds = DefaultVisibleLayerIds(document).ToHashSet();
        var selectionState = new PsdSelectionState(
            document.PsdPath,
            BuildGroupSelections(document, visibleLayerIds),
            visibleLayerIds,
            new HashSet<int>(),
            null);

        return new PsdLayerRenderState(
            document,
            selectionState,
            new HashSet<int>());
    }

    public PsdLayerRenderState WithLayerPixels(
        PsdLayerRenderState state,
        IReadOnlyDictionary<int, PsdLayerPixelData> layerPixels)
    {
        return state with
        {
            Document = state.Document with
            {
                LayerPixels = layerPixels,
                RenderableLayerBounds = ResolveRenderableBounds(layerPixels.Values) ??
                    state.Document.RenderableLayerBounds
            }
        };
    }

    public PsdLayerRenderState WithSelectionState(
        PsdLayerRenderState state,
        PsdSelectionState selectionState,
        IReadOnlySet<int>? controlledLayerIds = null)
    {
        var layerIds = RenderableDisplayLayerIds(state.Document);
        var visibleLayerIds = selectionState.VisibleLayerIds
            .Where(layerIds.Contains)
            .ToHashSet();
        var selectedLayerIds = selectionState.SelectedLayerIds
            .Where(visibleLayerIds.Contains)
            .ToHashSet();
        var groupedVisibleLayerIds = selectionState.GroupSelections.Values
            .SelectMany(selection => selection.VisibleChildLayerIds)
            .ToHashSet();
        var groupSelections = selectionState.GroupSelections.Count == 0 || !groupedVisibleLayerIds.SetEquals(visibleLayerIds)
            ? BuildGroupSelections(state.Document, visibleLayerIds)
            : NormalizeGroupSelections(selectionState.GroupSelections, visibleLayerIds);
        var normalizedSelection = selectionState with
        {
            GroupSelections = groupSelections,
            VisibleLayerIds = visibleLayerIds,
            SelectedLayerIds = selectedLayerIds
        };
        var normalizedControlledLayerIds = controlledLayerIds is null
            ? state.ControlledLayerIds
            : controlledLayerIds.Where(layerIds.Contains).ToHashSet();

        return state with
        {
            Selection = normalizedSelection,
            ControlledLayerIds = normalizedControlledLayerIds
        };
    }

    public PsdDocumentInfo CreateDocumentInfo(PsdLayerRenderState state)
    {
        var document = state.Document;
        var childrenByParent = document.DisplayLayers
            .GroupBy(layer => layer.ParentLayerId ?? RootParentKey)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var roots = BuildChildren(RootParentKey, childrenByParent);

        return new PsdDocumentInfo(
            document.PsdPath,
            document.PsdPath,
            Path.GetFileNameWithoutExtension(document.PsdPath),
            document.NativeDocument.Width,
            document.NativeDocument.Height,
            roots);
    }

    public bool TryApplySelectionHint(
        PsdLayerRenderState state,
        PsdLayerSelectionHint? hint,
        out PsdLayerRenderState nextState)
    {
        var resolution = ResolveSelectionHint(state.Document.DisplayLayers, hint);
        if (resolution is null)
        {
            nextState = state;
            return false;
        }

        var selectionState = new PsdSelectionState(
            state.Document.PsdPath,
            BuildGroupSelections(state.Document, resolution.VisibleLayerIds),
            resolution.VisibleLayerIds,
            resolution.SelectedLayerIds,
            state.Selection.CurrentGroupLayerId);
        nextState = WithSelectionState(state, selectionState, resolution.ControlledLayerIds);
        return true;
    }

    public PsdLayerPixelData? LoadLayerPixelsOrNull(string psdPath, int layerId)
    {
        if (string.IsNullOrWhiteSpace(psdPath))
        {
            return null;
        }

        try
        {
            var pixelData = _reader.LoadLayerPixels(psdPath, layerId);
            return PsdRenderableLayerFilter.IsRenderableLayer(pixelData) ? pixelData : null;
        }
        catch
        {
            return null;
        }
    }

    private PsdLayerSelectionResolution? ResolveSelectionHint(
        IReadOnlyList<PsdNativeLayerInfo> displayLayers,
        PsdLayerSelectionHint? hint)
    {
        if (hint is null)
        {
            return null;
        }

        var layers = displayLayers
            .Where(layer => !layer.IsGroup)
            .ToArray();
        var selectedLayerIds = new List<int>();
        foreach (var layerId in hint.LayerIds)
        {
            if (layers.Any(layer => layer.LayerId == layerId))
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

            var match = matches[0];
            if (!selectedLayerIds.Contains(match.LayerId))
            {
                selectedLayerIds.Add(match.LayerId);
            }
        }

        if (selectedLayerIds.Count == 0)
        {
            return null;
        }

        var controlledLayerIds = ResolveControlledLayerIds(layers, hint);
        var selectedLayerIdSet = selectedLayerIds.Distinct().ToHashSet();
        var visibleLayerIds = controlledLayerIds.Count == 0
            ? selectedLayerIdSet
            : layers
                .Where(layer => layer.IsVisible && !controlledLayerIds.Contains(layer.LayerId))
                .Select(layer => layer.LayerId)
                .Concat(selectedLayerIdSet)
                .Distinct()
                .ToHashSet();

        return new PsdLayerSelectionResolution(
            selectedLayerIdSet,
            visibleLayerIds,
            controlledLayerIds);
    }

    private static IReadOnlyList<int> DefaultVisibleLayerIds(PsdLayerRenderDocument document)
    {
        return document.DisplayLayers
            .Where(layer => !layer.IsGroup && layer.IsVisible)
            .Select(layer => layer.LayerId)
            .ToArray();
    }

    private IReadOnlyList<string> LayerNamesForIds(
        PsdLayerRenderDocument document,
        IReadOnlyList<int> layerIds)
    {
        var namesById = document.DisplayLayers
            .Where(layer => !layer.IsGroup)
            .GroupBy(layer => layer.LayerId)
            .ToDictionary(group => group.Key, group => group.First().Name);
        return layerIds
            .Select(layerId => namesById.TryGetValue(layerId, out var name) ? name : layerId.ToString(CultureInfo.InvariantCulture))
            .ToArray();
    }

    public PsdLayerMatchResult BuildMatchFromState(
        PsdLayerRequest request,
        PsdLayerRenderState state,
        double confidence,
        string? matchedPresetId)
    {
        var visibleLayerIds = LayerIdsInDocumentOrder(state.Document, state.VisibleLayerIds);
        return new PsdLayerMatchResult(
            confidence,
            request.Intent.Emotion,
            request.Intent.Action,
            matchedPresetId,
            visibleLayerIds,
            LayerNamesForIds(state.Document, visibleLayerIds));
    }

    public PsdLayerResponse ExportToResponse(
        PsdLayerRequest request,
        PsdLayerRenderState state,
        PsdLayerMatchResult match)
    {
        var layerIds = LayerIdsInDocumentOrder(state.Document, state.VisibleLayerIds);
        return ExportLayerIdsToResponse(request, state.Document, layerIds, match);
    }

    private PsdLayerResponse ExportLayerIdsToResponse(
        PsdLayerRequest request,
        PsdLayerRenderDocument document,
        IReadOnlyList<int> layerIds,
        PsdLayerMatchResult match)
    {
        if (layerIds.Count == 0)
        {
            return PsdLayerResponse.Failed(
                request.RequestId,
                "no_matching_layers",
                $"No matching layers for {request.Intent.Emotion} / {request.Intent.Action}.");
        }

        document = WithLoadedLayerPixels(document, layerIds);
        var selectedPixels = ResolveOrderedPixels(document, layerIds);

        if (selectedPixels.Length == 0)
        {
            return PsdLayerResponse.Failed(
                request.RequestId,
                "no_exportable_pixels",
                $"Matched layers do not contain exportable pixels for {request.Intent.Emotion} / {request.Intent.Action}.");
        }

        var outputDirectory = ResolvePath(request.ProjectRoot, request.Output.Directory);
        var outputPath = BuildOutputPath(outputDirectory, request.Output.FileNameHint);
        var preview = PsdLayerCanvasComposer.Compose(
            document.NativeDocument.Width,
            document.NativeDocument.Height,
            selectedPixels,
            request.Output.Bounds,
            ResolveRenderableBounds(selectedPixels) ?? document.RenderableLayerBounds);
        PsdPreviewPngExporter.SavePreview(preview, outputPath);

        var orderedLayerIds = selectedPixels.Select(layer => layer.LayerId).ToArray();
        var sourceFingerprint = PsdVisualSourceMetadata.BuildSourceFingerprint(request.Psd.AssetId, document.PsdPath);
        var renderCacheKey = PsdVisualSourceMetadata.BuildRenderCacheKey(
            request.Psd.AssetId,
            sourceFingerprint,
            orderedLayerIds,
            request.Output.Bounds);

        return PsdLayerResponse.Completed(
            request.RequestId,
            new PsdPngResult(outputPath, preview.Width, preview.Height, request.Output.Bounds),
            match with
            {
                LayerIds = orderedLayerIds,
                LayerNames = LayerNamesForIds(document, orderedLayerIds)
            },
            new PsdResponseMetadata(request.Psd.AssetId, document.PsdPath, sourceFingerprint, renderCacheKey));
    }

    public PsdMergedPreview? ComposePreview(PsdLayerRenderState state, PsdExportBounds? bounds = null)
    {
        var layerIds = LayerIdsInDocumentOrder(state.Document, state.VisibleLayerIds);
        return ComposeLayerIds(state.Document, layerIds, bounds);
    }

    private PsdMergedPreview? ComposeLayerIds(
        PsdLayerRenderDocument document,
        IReadOnlyList<int> layerIds,
        PsdExportBounds? bounds = null)
    {
        document = WithLoadedLayerPixels(document, layerIds);
        var selectedPixels = ResolveOrderedPixels(document, layerIds);
        if (selectedPixels.Length == 0)
        {
            return null;
        }

        return PsdLayerCanvasComposer.Compose(
            document.NativeDocument.Width,
            document.NativeDocument.Height,
            selectedPixels,
            bounds ?? document.Parameters.Bounds,
            ResolveRenderableBounds(selectedPixels) ?? document.RenderableLayerBounds);
    }

    private PsdLayerRenderDocument WithLoadedLayerPixels(
        PsdLayerRenderDocument document,
        IReadOnlyList<int> layerIds)
    {
        var pixelsById = document.LayerPixels.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var layerId in layerIds)
        {
            if (pixelsById.ContainsKey(layerId))
            {
                continue;
            }

            var pixelData = LoadLayerPixelsOrNull(document.PsdPath, layerId);
            if (pixelData is not null)
            {
                pixelsById[layerId] = pixelData;
            }
        }

        return document with
        {
            LayerPixels = pixelsById,
            RenderableLayerBounds = ResolveRenderableBounds(pixelsById.Values) ?? document.RenderableLayerBounds
        };
    }

    private static PsdLayerPixelData[] ResolveOrderedPixels(
        PsdLayerRenderDocument document,
        IReadOnlyList<int> layerIds)
    {
        var selectedPixels = new List<PsdLayerPixelData>();
        foreach (var layerId in layerIds)
        {
            if (document.LayerPixels.TryGetValue(layerId, out var pixelData))
            {
                selectedPixels.Add(pixelData);
            }
        }

        return PsdLayerStackOrder.ForCompositing(selectedPixels).ToArray();
    }

    private static HashSet<int> RenderableDisplayLayerIds(PsdLayerRenderDocument document)
    {
        return document.DisplayLayers
            .Where(layer => !layer.IsGroup)
            .Select(layer => layer.LayerId)
            .ToHashSet();
    }

    private static IReadOnlyList<int> LayerIdsInDocumentOrder(
        PsdLayerRenderDocument document,
        IReadOnlySet<int> layerIds)
    {
        return document.DisplayLayers
            .Where(layer => !layer.IsGroup && layerIds.Contains(layer.LayerId))
            .Select(layer => layer.LayerId)
            .ToArray();
    }

    private static IReadOnlyDictionary<int, PsdLayerGroupSelection> BuildGroupSelections(
        PsdLayerRenderDocument document,
        IReadOnlySet<int> visibleLayerIds)
    {
        return document.DisplayLayers
            .Where(layer => !layer.IsGroup && visibleLayerIds.Contains(layer.LayerId))
            .GroupBy(layer => layer.ParentLayerId ?? RootParentKey)
            .ToDictionary(
                group => group.Key,
                group => new PsdLayerGroupSelection(
                    group.Key,
                    group.Count() > 1,
                    group.Select(layer => layer.LayerId).ToHashSet()));
    }

    private static IReadOnlyDictionary<int, PsdLayerGroupSelection> NormalizeGroupSelections(
        IReadOnlyDictionary<int, PsdLayerGroupSelection> groupSelections,
        IReadOnlySet<int> visibleLayerIds)
    {
        return groupSelections
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value with
                {
                    VisibleChildLayerIds = pair.Value.VisibleChildLayerIds
                        .Where(visibleLayerIds.Contains)
                        .ToHashSet()
                });
    }

    private static PsdLayerExportRect? ResolveRenderableBounds(IEnumerable<PsdLayerPixelData> layers)
    {
        return PsdRenderableLayerFilter.TryGetRenderableUnionBounds(layers, out var bounds)
            ? bounds
            : null;
    }

    private static PsdNativeLayerInfo[] BuildDisplayLayers(
        IReadOnlyList<PsdNativeLayerInfo> layers,
        IReadOnlySet<int> renderableLayerIds)
    {
        var candidates = layers
            .Where(layer => layer.Type != PsdSectionDividerLayerType)
            .ToArray();
        var childrenByParent = candidates
            .GroupBy(layer => layer.ParentLayerId ?? RootParentKey)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var descendantCache = new Dictionary<int, bool>();

        return candidates
            .Where(layer => renderableLayerIds.Contains(layer.LayerId) ||
                (layer.IsGroup && HasRenderableDescendant(layer.LayerId, childrenByParent, renderableLayerIds, descendantCache)))
            .ToArray();
    }

    private static bool HasRenderableDescendant(
        int groupLayerId,
        IReadOnlyDictionary<int, PsdNativeLayerInfo[]> childrenByParent,
        IReadOnlySet<int> renderableLayerIds,
        Dictionary<int, bool> descendantCache)
    {
        if (descendantCache.TryGetValue(groupLayerId, out var cached))
        {
            return cached;
        }

        if (!childrenByParent.TryGetValue(groupLayerId, out var children))
        {
            descendantCache[groupLayerId] = false;
            return false;
        }

        var hasRenderableDescendant = children.Any(child =>
            renderableLayerIds.Contains(child.LayerId) ||
            (child.IsGroup && HasRenderableDescendant(child.LayerId, childrenByParent, renderableLayerIds, descendantCache)));
        descendantCache[groupLayerId] = hasRenderableDescendant;
        return hasRenderableDescendant;
    }

    private static bool TryGetMetadataUnionBounds(
        IReadOnlyList<PsdNativeLayerInfo> layers,
        out PsdLayerExportRect bounds)
    {
        var left = int.MaxValue;
        var top = int.MaxValue;
        var right = int.MinValue;
        var bottom = int.MinValue;

        foreach (var layer in layers.Where(CanLoadLayerPixels))
        {
            left = Math.Min(left, layer.Left);
            top = Math.Min(top, layer.Top);
            right = Math.Max(right, layer.Right);
            bottom = Math.Max(bottom, layer.Bottom);
        }

        if (left == int.MaxValue || top == int.MaxValue || right <= left || bottom <= top)
        {
            bounds = default;
            return false;
        }

        bounds = new PsdLayerExportRect(left, top, right - left, bottom - top);
        return true;
    }

    private static HashSet<int> ResolveControlledLayerIds(
        IReadOnlyList<PsdNativeLayerInfo> layers,
        PsdLayerSelectionHint hint)
    {
        var controlledLayerIds = new HashSet<int>();
        if (hint.ControlledLayerIds is not null)
        {
            foreach (var layerId in hint.ControlledLayerIds)
            {
                if (layers.Any(layer => layer.LayerId == layerId))
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

    private static bool CanLoadLayerPixels(PsdNativeLayerInfo layer)
    {
        return !layer.IsGroup &&
            layer.Type != PsdSectionDividerLayerType &&
            layer.Width > 0 &&
            layer.Height > 0;
    }

    private static string ResolvePath(string projectRoot, string path)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(projectRoot, path));
    }

    private static string BuildOutputPath(string directory, string fileNameHint)
    {
        var fileName = Path.GetFileName(fileNameHint);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "psd-export.png";
        }

        if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".png";
        }

        return Path.Combine(directory, fileName);
    }
}

public sealed record PsdLayerRenderParameters(
    string PsdPath,
    PsdExportBounds Bounds,
    PsdLayerSelectionHint? SelectionHint = null,
    bool LoadMergedPreview = false)
{
    public static PsdLayerRenderParameters FromRequest(
        PsdLayerRequest request,
        string psdPath,
        bool loadMergedPreview = false)
    {
        return new PsdLayerRenderParameters(
            Path.GetFullPath(psdPath),
            request.Output.Bounds,
            request.SelectionHint,
            loadMergedPreview);
    }
}

internal sealed record PsdLayerRenderDocument(
    PsdLayerRenderParameters Parameters,
    string PsdPath,
    PsdNativeDocumentInfo NativeDocument,
    IReadOnlyList<PsdNativeLayerInfo> DisplayLayers,
    IReadOnlyDictionary<int, PsdLayerPixelData> LayerPixels,
    PsdLayerExportRect? RenderableLayerBounds,
    PsdMergedPreview? MergedPreview = null,
    string? MergedPreviewError = null);

internal sealed record PsdLayerRenderState(
    PsdLayerRenderDocument Document,
    PsdSelectionState Selection,
    IReadOnlySet<int> ControlledLayerIds)
{
    public IReadOnlySet<int> VisibleLayerIds => Selection.VisibleLayerIds;

    public IReadOnlySet<int> SelectedLayerIds => Selection.SelectedLayerIds;

    public IReadOnlyDictionary<int, PsdLayerGroupSelection> GroupSelections => Selection.GroupSelections;
}

internal sealed record PsdLayerSelectionResolution(
    IReadOnlySet<int> SelectedLayerIds,
    IReadOnlySet<int> VisibleLayerIds,
    IReadOnlySet<int> ControlledLayerIds);
