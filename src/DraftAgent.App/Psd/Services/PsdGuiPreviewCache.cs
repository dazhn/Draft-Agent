using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media.Imaging;
using PsdLayerLoader.Core;

namespace DraftAgent.App.Psd.Services;

internal sealed class PsdGuiPreviewCache
{
    private const int MaxPreviewCount = 12;

    private readonly Dictionary<int, Bitmap> _thumbnailByLayerId = [];
    private readonly Dictionary<string, PreviewEntry> _previewByKey = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _previewLru = [];

    public void Clear()
    {
        _thumbnailByLayerId.Clear();
        _previewByKey.Clear();
        _previewLru.Clear();
    }

    public bool TryGetThumbnail(int layerId, out Bitmap bitmap)
    {
        return _thumbnailByLayerId.TryGetValue(layerId, out bitmap!);
    }

    public void StoreThumbnail(int layerId, Bitmap bitmap)
    {
        _thumbnailByLayerId[layerId] = bitmap;
    }

    public bool TryGetPreview(
        string fingerprint,
        IReadOnlySet<int> visibleLayerIds,
        PsdExportBounds bounds,
        out Bitmap bitmap,
        out PsdMergedPreview preview)
    {
        var key = BuildPreviewKey(fingerprint, visibleLayerIds, bounds);
        if (!_previewByKey.TryGetValue(key, out var entry))
        {
            bitmap = null!;
            preview = null!;
            return false;
        }

        _previewLru.Remove(entry.Node);
        _previewLru.AddFirst(entry.Node);
        bitmap = entry.Bitmap;
        preview = entry.Preview;
        return true;
    }

    public void StorePreview(
        string fingerprint,
        IReadOnlySet<int> visibleLayerIds,
        PsdExportBounds bounds,
        Bitmap bitmap,
        PsdMergedPreview preview)
    {
        var key = BuildPreviewKey(fingerprint, visibleLayerIds, bounds);
        if (_previewByKey.TryGetValue(key, out var existing))
        {
            _previewLru.Remove(existing.Node);
        }

        var node = new LinkedListNode<string>(key);
        _previewLru.AddFirst(node);
        _previewByKey[key] = new PreviewEntry(bitmap, preview, node);
        while (_previewByKey.Count > MaxPreviewCount && _previewLru.Last is not null)
        {
            var removeKey = _previewLru.Last.Value;
            _previewLru.RemoveLast();
            _previewByKey.Remove(removeKey);
        }
    }

    private static string BuildPreviewKey(
        string fingerprint,
        IReadOnlySet<int> visibleLayerIds,
        PsdExportBounds bounds)
    {
        return string.Join(
            "|",
            fingerprint,
            bounds.ToString(),
            string.Join(",", visibleLayerIds.OrderBy(id => id)));
    }

    private sealed record PreviewEntry(Bitmap Bitmap, PsdMergedPreview Preview, LinkedListNode<string> Node);
}
