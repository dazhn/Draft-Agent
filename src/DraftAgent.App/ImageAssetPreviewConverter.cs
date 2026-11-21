using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace DraftAgent.App;

public sealed class ImageAssetPreviewConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return ImageAssetPreviewCache.GetOrLoad(path, ParseDecodePixelWidth(parameter));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static int? ParseDecodePixelWidth(object? parameter)
    {
        return parameter switch
        {
            int value when value > 0 => value,
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0 => value,
            _ => null
        };
    }
}

internal static class ImageAssetPreviewCache
{
    private const int MaxEntries = 512;
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<ImageAssetPreviewCacheKey, WeakReference<IImage>> Cache = new();
    private static Func<string, int?, IImage?> BitmapLoader = static (path, decodePixelWidth) =>
    {
        if (decodePixelWidth is > 0)
        {
            using var stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, decodePixelWidth.Value, BitmapInterpolationMode.LowQuality);
        }

        return new Bitmap(path);
    };

    public static IImage? GetOrLoad(string path, int? decodePixelWidth = null)
    {
        RecordRequest();
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
        {
            RecordInvalidPath();
            return null;
        }

        ImageAssetPreviewCacheKey key;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                RecordMissingFile();
                return null;
            }

            key = new ImageAssetPreviewCacheKey(
                info.FullName,
                info.LastWriteTimeUtc.Ticks,
                info.Length,
                NormalizeDecodePixelWidth(decodePixelWidth));
        }
        catch
        {
            RecordInvalidPath();
            return null;
        }

        lock (SyncRoot)
        {
            if (Cache.TryGetValue(key, out var cached) &&
                cached.TryGetTarget(out var bitmap))
            {
                RecordHitLocked();
                return bitmap;
            }
        }

        Func<string, int?, IImage?> loader;
        lock (SyncRoot)
        {
            loader = BitmapLoader;
        }

        IImage? loaded;
        try
        {
            loaded = loader(key.Path, key.DecodePixelWidth);
        }
        catch
        {
            RecordLoadFailure();
            return null;
        }

        if (loaded is null)
        {
            RecordLoadFailure();
            return null;
        }

        lock (SyncRoot)
        {
            PruneDeadEntries();
            Cache[key] = new WeakReference<IImage>(loaded);
            if (Cache.Count > MaxEntries)
            {
                PruneOldestEntries(Cache.Count - MaxEntries);
            }

            LoadCount++;
        }

        return loaded;
    }

    internal static void Clear()
    {
        lock (SyncRoot)
        {
            Cache.Clear();
            ResetMetricsLocked();
        }
    }

    internal static IDisposable UseBitmapLoaderForTests(Func<string, IImage?> loader)
    {
        return UseBitmapLoaderForTests((path, _) => loader(path));
    }

    internal static IDisposable UseBitmapLoaderForTests(Func<string, int?, IImage?> loader)
    {
        lock (SyncRoot)
        {
            var previous = BitmapLoader;
            BitmapLoader = loader;
            Cache.Clear();
            ResetMetricsLocked();
            return new BitmapLoaderRestoreScope(previous);
        }
    }

    internal static int Count
    {
        get
        {
            lock (SyncRoot)
            {
                return Cache.Count;
            }
        }
    }

    internal static ImageAssetPreviewCacheMetrics Metrics
    {
        get
        {
            lock (SyncRoot)
            {
                return new ImageAssetPreviewCacheMetrics(
                    RequestCount,
                    HitCount,
                    LoadCount,
                    InvalidPathCount,
                    MissingFileCount,
                    LoadFailureCount,
                    Cache.Count);
            }
        }
    }

    private static long RequestCount { get; set; }

    private static long HitCount { get; set; }

    private static long LoadCount { get; set; }

    private static long InvalidPathCount { get; set; }

    private static long MissingFileCount { get; set; }

    private static long LoadFailureCount { get; set; }

    private static void PruneDeadEntries()
    {
        foreach (var entry in Cache.ToArray())
        {
            if (!entry.Value.TryGetTarget(out _))
            {
                Cache.Remove(entry.Key);
            }
        }
    }

    private static void PruneOldestEntries(int count)
    {
        if (count <= 0)
        {
            return;
        }

        foreach (var key in Cache.Keys.OrderBy(key => key.LastWriteTimeUtcTicks).Take(count).ToArray())
        {
            Cache.Remove(key);
        }
    }

    private static void RecordRequest()
    {
        lock (SyncRoot)
        {
            RequestCount++;
        }
    }

    private static void RecordInvalidPath()
    {
        lock (SyncRoot)
        {
            InvalidPathCount++;
        }
    }

    private static void RecordMissingFile()
    {
        lock (SyncRoot)
        {
            MissingFileCount++;
        }
    }

    private static void RecordHitLocked()
    {
        HitCount++;
    }

    private static void RecordLoadFailure()
    {
        lock (SyncRoot)
        {
            LoadFailureCount++;
        }
    }

    private static void ResetMetricsLocked()
    {
        RequestCount = 0;
        HitCount = 0;
        LoadCount = 0;
        InvalidPathCount = 0;
        MissingFileCount = 0;
        LoadFailureCount = 0;
    }

    private static int? NormalizeDecodePixelWidth(int? decodePixelWidth)
    {
        return decodePixelWidth is > 0 ? decodePixelWidth.Value : null;
    }

    private readonly record struct ImageAssetPreviewCacheKey(
        string Path,
        long LastWriteTimeUtcTicks,
        long Length,
        int? DecodePixelWidth);

    private sealed class BitmapLoaderRestoreScope(Func<string, int?, IImage?> previous) : IDisposable
    {
        public void Dispose()
        {
            lock (SyncRoot)
            {
                BitmapLoader = previous;
                Cache.Clear();
                ResetMetricsLocked();
            }
        }
    }
}

internal readonly record struct ImageAssetPreviewCacheMetrics(
    long RequestCount,
    long HitCount,
    long LoadCount,
    long InvalidPathCount,
    long MissingFileCount,
    long LoadFailureCount,
    int CacheEntryCount)
{
    public long MissCount => Math.Max(0, RequestCount - HitCount);
}
