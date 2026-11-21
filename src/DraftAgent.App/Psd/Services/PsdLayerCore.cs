using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DraftAgent.Core;
using PsdLayerLoader.Core;

namespace DraftAgent.App.Psd.Services;

internal interface IPsdLayerCore
{
    PsdLayerCoreSnapshot Load(string psdPath, bool loadMergedPreview = false);

    PsdLayerCoreSnapshot Snapshot { get; }

    IReadOnlySet<int> VisibleLayerIds { get; }

    void SetVisibleLayers(IEnumerable<int> layerIds);

    Task<PsdLayerPixelData?> GetLayerPixelsAsync(int layerId, CancellationToken cancellationToken = default);

    Task PreloadLayersAsync(IEnumerable<int> layerIds, CancellationToken cancellationToken = default);

    Task<PsdMergedPreview?> RenderVisibleAsync(PsdExportBounds bounds, CancellationToken cancellationToken = default);

    Task<PsdLayerExportResult> ExportVisiblePngAsync(
        string outputPath,
        PsdExportBounds bounds,
        CancellationToken cancellationToken = default);

    PsdRenderSelection BuildSelectionMetadata(string psdAssetId, PsdExportBounds bounds, string? presetId = null, string? emotion = null, string? action = null);
}

internal sealed class PsdLayerCore : IPsdLayerCore
{
    private const int RootParentKey = -1;
    private const int PsdSectionDividerLayerType = 3;
    private const long DefaultMaxCacheBytes = 512L * 1024L * 1024L;

    private readonly NativePsdDocumentReader _reader;
    private readonly PsdLayerPixelCache _pixelCache;
    private readonly object _readerLock = new();
    private readonly object _stateLock = new();

    private PsdLayerCoreSnapshot _snapshot = PsdLayerCoreSnapshot.Empty;
    private HashSet<int> _visibleLayerIds = [];
    private HashSet<int> _validRenderableLayerIds = [];
    private PsdLayerExportRect? _metadataBounds;

    public PsdLayerCore()
        : this(new NativePsdDocumentReader(), new PsdLayerPixelCache(DefaultMaxCacheBytes))
    {
    }

    public PsdLayerCore(NativePsdDocumentReader reader)
        : this(reader, new PsdLayerPixelCache(DefaultMaxCacheBytes))
    {
    }

    internal PsdLayerCore(NativePsdDocumentReader reader, PsdLayerPixelCache pixelCache)
    {
        _reader = reader;
        _pixelCache = pixelCache;
    }

    public PsdLayerCoreSnapshot Snapshot
    {
        get
        {
            lock (_stateLock)
            {
                return _snapshot with { VisibleLayerIds = _visibleLayerIds.ToHashSet() };
            }
        }
    }

    public IReadOnlySet<int> VisibleLayerIds
    {
        get
        {
            lock (_stateLock)
            {
                return _visibleLayerIds.ToHashSet();
            }
        }
    }

    public PsdLayerCoreSnapshot Load(string psdPath, bool loadMergedPreview = false)
    {
        if (string.IsNullOrWhiteSpace(psdPath))
        {
            throw new ArgumentException("PSD path is required.", nameof(psdPath));
        }

        var fullPath = Path.GetFullPath(psdPath);
        PsdNativeDocumentInfo nativeDocument;
        lock (_readerLock)
        {
            nativeDocument = _reader.Load(fullPath);
        }

        var candidateLayerIds = nativeDocument.Layers
            .Where(CanLoadLayerPixels)
            .Select(layer => layer.LayerId)
            .ToHashSet();
        var displayLayers = BuildDisplayLayers(nativeDocument.Layers, candidateLayerIds);
        _validRenderableLayerIds = displayLayers
            .Where(layer => !layer.IsGroup && candidateLayerIds.Contains(layer.LayerId))
            .Select(layer => layer.LayerId)
            .ToHashSet();
        _metadataBounds = TryGetMetadataUnionBounds(displayLayers, out var bounds)
            ? bounds
            : null;
        var visibleLayerIds = displayLayers
            .Where(layer => !layer.IsGroup && layer.IsVisible && _validRenderableLayerIds.Contains(layer.LayerId))
            .Select(layer => layer.LayerId)
            .ToHashSet();

        PsdMergedPreview? mergedPreview = null;
        string? mergedPreviewError = null;
        if (loadMergedPreview)
        {
            try
            {
                lock (_readerLock)
                {
                    mergedPreview = _reader.LoadMergedPreview(fullPath);
                }
            }
            catch (Exception ex)
            {
                mergedPreviewError = ex.Message;
            }
        }

        var fingerprint = PsdVisualSourceMetadata.BuildSourceFingerprint(null, fullPath);
        _pixelCache.SetDocument(fingerprint);
        lock (_stateLock)
        {
            _visibleLayerIds = visibleLayerIds;
            _snapshot = new PsdLayerCoreSnapshot(
                fullPath,
                fingerprint,
                nativeDocument,
                displayLayers,
                _visibleLayerIds.ToHashSet(),
                _metadataBounds,
                mergedPreview,
                mergedPreviewError);
            return _snapshot;
        }
    }

    public void SetVisibleLayers(IEnumerable<int> layerIds)
    {
        ArgumentNullException.ThrowIfNull(layerIds);
        EnsureLoaded();
        var normalized = layerIds
            .Where(_validRenderableLayerIds.Contains)
            .Distinct()
            .ToHashSet();
        lock (_stateLock)
        {
            _visibleLayerIds = normalized;
            _snapshot = _snapshot with { VisibleLayerIds = _visibleLayerIds.ToHashSet() };
        }
    }

    public async Task<PsdLayerPixelData?> GetLayerPixelsAsync(int layerId, CancellationToken cancellationToken = default)
    {
        EnsureLoaded();
        PsdLayerCoreSnapshot snapshot;
        lock (_stateLock)
        {
            snapshot = _snapshot;
        }

        if (!_validRenderableLayerIds.Contains(layerId))
        {
            return null;
        }

        if (_pixelCache.TryGet(snapshot.SourceFingerprint, layerId, out var cached))
        {
            return cached;
        }

        var pixelData = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_readerLock)
            {
                return _reader.LoadLayerPixels(snapshot.PsdPath, layerId);
            }
        }, cancellationToken);

        if (!PsdRenderableLayerFilter.IsRenderableLayer(pixelData))
        {
            return null;
        }

        _pixelCache.Add(snapshot.SourceFingerprint, pixelData);
        return pixelData;
    }

    public async Task PreloadLayersAsync(IEnumerable<int> layerIds, CancellationToken cancellationToken = default)
    {
        foreach (var layerId in layerIds.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = await GetLayerPixelsAsync(layerId, cancellationToken);
        }
    }

    public async Task<PsdMergedPreview?> RenderVisibleAsync(PsdExportBounds bounds, CancellationToken cancellationToken = default)
    {
        EnsureLoaded();
        var snapshot = Snapshot;
        var orderedLayerIds = LayerIdsInDocumentOrder(snapshot.DisplayLayers, snapshot.VisibleLayerIds);
        if (orderedLayerIds.Count == 0)
        {
            return null;
        }

        var pixels = new List<PsdLayerPixelData>();
        foreach (var layerId in orderedLayerIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pixel = await GetLayerPixelsAsync(layerId, cancellationToken);
            if (pixel is not null)
            {
                pixels.Add(pixel);
            }
        }

        var orderedPixels = PsdLayerStackOrder.ForCompositing(pixels).ToArray();
        if (orderedPixels.Length == 0)
        {
            return null;
        }

        return PsdLayerCanvasComposer.Compose(
            snapshot.Document.Width,
            snapshot.Document.Height,
            orderedPixels,
            bounds,
            PsdRenderableLayerFilter.TryGetRenderableUnionBounds(orderedPixels, out var renderBounds)
                ? renderBounds
                : snapshot.RenderableLayerBounds);
    }

    public async Task<PsdLayerExportResult> ExportVisiblePngAsync(
        string outputPath,
        PsdExportBounds bounds,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path is required.", nameof(outputPath));
        }

        var preview = await RenderVisibleAsync(bounds, cancellationToken);
        if (preview is null)
        {
            return PsdLayerExportResult.Failed("no_exportable_layers", "No visible PSD layers contain exportable pixels.");
        }

        PsdPreviewPngExporter.SavePreview(preview, outputPath);
        return PsdLayerExportResult.Completed(outputPath, preview.Width, preview.Height, bounds);
    }

    public PsdRenderSelection BuildSelectionMetadata(
        string psdAssetId,
        PsdExportBounds bounds,
        string? presetId = null,
        string? emotion = null,
        string? action = null)
    {
        EnsureLoaded();
        var snapshot = Snapshot;
        var orderedLayerIds = LayerIdsInDocumentOrder(snapshot.DisplayLayers, snapshot.VisibleLayerIds);
        var layerNames = LayerNamesForIds(snapshot.DisplayLayers, orderedLayerIds);
        var sourceFingerprint = PsdVisualSourceMetadata.BuildSourceFingerprint(psdAssetId, snapshot.PsdPath);
        var renderCacheKey = PsdVisualSourceMetadata.BuildRenderCacheKey(
            psdAssetId,
            sourceFingerprint,
            orderedLayerIds,
            bounds);
        return new PsdRenderSelection(
            orderedLayerIds,
            layerNames,
            presetId,
            emotion,
            action,
            ToVisualExportBounds(bounds),
            sourceFingerprint,
            renderCacheKey);
    }

    public PsdLayerMatchResult BuildMatchFromCurrent(
        PsdLayerRequest request,
        double confidence,
        string? matchedPresetId)
    {
        EnsureLoaded();
        var snapshot = Snapshot;
        var orderedLayerIds = LayerIdsInDocumentOrder(snapshot.DisplayLayers, snapshot.VisibleLayerIds);
        return new PsdLayerMatchResult(
            confidence,
            request.Intent.Emotion,
            request.Intent.Action,
            matchedPresetId,
            orderedLayerIds,
            LayerNamesForIds(snapshot.DisplayLayers, orderedLayerIds));
    }

    public PsdLayerResponse BuildCompletedResponse(
        PsdLayerRequest request,
        PsdLayerExportResult export,
        PsdLayerMatchResult match)
    {
        EnsureLoaded();
        if (!export.Success || string.IsNullOrWhiteSpace(export.Path))
        {
            return PsdLayerResponse.Failed(
                request.RequestId,
                export.ErrorCode ?? "psd_export_failed",
                export.ErrorMessage ?? "PSD export failed.");
        }

        var snapshot = Snapshot;
        var orderedLayerIds = LayerIdsInDocumentOrder(snapshot.DisplayLayers, snapshot.VisibleLayerIds);
        var sourceFingerprint = PsdVisualSourceMetadata.BuildSourceFingerprint(request.Psd.AssetId, snapshot.PsdPath);
        var renderCacheKey = PsdVisualSourceMetadata.BuildRenderCacheKey(
            request.Psd.AssetId,
            sourceFingerprint,
            orderedLayerIds,
            request.Output.Bounds);

        return PsdLayerResponse.Completed(
            request.RequestId,
            new PsdPngResult(export.Path, export.Width, export.Height, request.Output.Bounds),
            match with
            {
                LayerIds = orderedLayerIds,
                LayerNames = LayerNamesForIds(snapshot.DisplayLayers, orderedLayerIds)
            },
            new PsdResponseMetadata(request.Psd.AssetId, snapshot.PsdPath, sourceFingerprint, renderCacheKey));
    }

    private void EnsureLoaded()
    {
        if (string.IsNullOrWhiteSpace(_snapshot.PsdPath))
        {
            throw new InvalidOperationException("PSD document is not loaded.");
        }
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

    private static bool CanLoadLayerPixels(PsdNativeLayerInfo layer)
    {
        return !layer.IsGroup &&
            layer.Type != PsdSectionDividerLayerType &&
            layer.Width > 0 &&
            layer.Height > 0;
    }

    private static IReadOnlyList<int> LayerIdsInDocumentOrder(
        IReadOnlyList<PsdNativeLayerInfo> layers,
        IReadOnlySet<int> layerIds)
    {
        return layers
            .Where(layer => !layer.IsGroup && layerIds.Contains(layer.LayerId))
            .Select(layer => layer.LayerId)
            .ToArray();
    }

    private static IReadOnlyList<string> LayerNamesForIds(
        IReadOnlyList<PsdNativeLayerInfo> layers,
        IReadOnlyList<int> layerIds)
    {
        var namesById = layers
            .Where(layer => !layer.IsGroup)
            .GroupBy(layer => layer.LayerId)
            .ToDictionary(group => group.Key, group => group.First().Name);
        return layerIds
            .Select(layerId => namesById.TryGetValue(layerId, out var name) ? name : layerId.ToString(CultureInfo.InvariantCulture))
            .ToArray();
    }

    private static VisualExportBounds ToVisualExportBounds(PsdExportBounds bounds)
    {
        return bounds switch
        {
            PsdExportBounds.LayerBounds => VisualExportBounds.LayerBounds,
            PsdExportBounds.CanvasBounds => VisualExportBounds.CanvasBounds,
            PsdExportBounds.PreviewBounds => VisualExportBounds.PreviewBounds,
            _ => VisualExportBounds.LayerBounds
        };
    }
}

internal sealed record PsdLayerCoreSnapshot(
    string PsdPath,
    string SourceFingerprint,
    PsdNativeDocumentInfo Document,
    IReadOnlyList<PsdNativeLayerInfo> DisplayLayers,
    IReadOnlySet<int> VisibleLayerIds,
    PsdLayerExportRect? RenderableLayerBounds,
    PsdMergedPreview? MergedPreview,
    string? MergedPreviewError)
{
    public static PsdLayerCoreSnapshot Empty { get; } = new(
        string.Empty,
        string.Empty,
        new PsdNativeDocumentInfo(0, 0, 0, 0, 0, []),
        [],
        new HashSet<int>(),
        null,
        null,
        null);
}

internal sealed record PsdLayerExportResult(
    bool Success,
    string? Path,
    int Width,
    int Height,
    PsdExportBounds Bounds,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static PsdLayerExportResult Completed(string path, int width, int height, PsdExportBounds bounds)
    {
        return new PsdLayerExportResult(true, path, width, height, bounds, null, null);
    }

    public static PsdLayerExportResult Failed(string code, string message)
    {
        return new PsdLayerExportResult(false, null, 0, 0, PsdExportBounds.LayerBounds, code, message);
    }
}

internal sealed class PsdLayerPixelCache
{
    private readonly long _maxBytes;
    private readonly Dictionary<PsdLayerCacheKey, CacheEntry> _entries = [];
    private readonly LinkedList<PsdLayerCacheKey> _lru = [];
    private readonly object _sync = new();
    private long _currentBytes;
    private string _currentFingerprint = string.Empty;

    public PsdLayerPixelCache(long maxBytes)
    {
        _maxBytes = Math.Max(1, maxBytes);
    }

    public void SetDocument(string fingerprint)
    {
        lock (_sync)
        {
            if (string.Equals(_currentFingerprint, fingerprint, StringComparison.Ordinal))
            {
                return;
            }

            _currentFingerprint = fingerprint;
            Clear();
        }
    }

    public bool TryGet(string fingerprint, int layerId, out PsdLayerPixelData pixelData)
    {
        var key = new PsdLayerCacheKey(fingerprint, layerId);
        lock (_sync)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                pixelData = null!;
                return false;
            }

            _lru.Remove(entry.Node);
            _lru.AddFirst(entry.Node);
            pixelData = entry.PixelData;
            return true;
        }
    }

    public void Add(string fingerprint, PsdLayerPixelData pixelData)
    {
        var key = new PsdLayerCacheKey(fingerprint, pixelData.LayerId);
        var byteCount = pixelData.Rgba.LongLength;
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                _currentBytes -= existing.ByteCount;
                _lru.Remove(existing.Node);
            }

            var node = new LinkedListNode<PsdLayerCacheKey>(key);
            _lru.AddFirst(node);
            _entries[key] = new CacheEntry(pixelData, byteCount, node);
            _currentBytes += byteCount;
            Trim();
        }
    }

    private void Trim()
    {
        while (_currentBytes > _maxBytes && _lru.Last is not null)
        {
            var key = _lru.Last.Value;
            _lru.RemoveLast();
            if (_entries.Remove(key, out var entry))
            {
                _currentBytes -= entry.ByteCount;
            }
        }
    }

    private void Clear()
    {
        _entries.Clear();
        _lru.Clear();
        _currentBytes = 0;
    }

    private readonly record struct PsdLayerCacheKey(string Fingerprint, int LayerId);

    private sealed record CacheEntry(PsdLayerPixelData PixelData, long ByteCount, LinkedListNode<PsdLayerCacheKey> Node);
}
