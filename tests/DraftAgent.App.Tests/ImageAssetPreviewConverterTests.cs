using System.Globalization;
using Avalonia;
using Avalonia.Media;
using DraftAgent.App;

namespace DraftAgent.App.Tests;

public sealed class ImageAssetPreviewConverterTests
{
    [Fact]
    public void ConverterReusesBitmapForSameImageFileVersion()
    {
        ImageAssetPreviewCache.Clear();
        var loadCount = 0;
        using var _ = ImageAssetPreviewCache.UseBitmapLoaderForTests(_ =>
        {
            loadCount++;
            return CreateBitmap();
        });
        var path = CreateTempImageFile();
        var converter = new ImageAssetPreviewConverter();

        var first = converter.Convert(path, typeof(IImage), null, CultureInfo.InvariantCulture);
        var second = converter.Convert(path, typeof(IImage), null, CultureInfo.InvariantCulture);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(1, loadCount);
        Assert.Equal(1, ImageAssetPreviewCache.Count);

        var metrics = ImageAssetPreviewCache.Metrics;
        Assert.Equal(2, metrics.RequestCount);
        Assert.Equal(1, metrics.HitCount);
        Assert.Equal(1, metrics.LoadCount);
        Assert.Equal(1, metrics.MissCount);
        Assert.Equal(0, metrics.InvalidPathCount);
        Assert.Equal(0, metrics.MissingFileCount);
        Assert.Equal(0, metrics.LoadFailureCount);
        Assert.Equal(1, metrics.CacheEntryCount);
    }

    [Fact]
    public void ConverterReloadsBitmapWhenImageFileVersionChanges()
    {
        ImageAssetPreviewCache.Clear();
        var loadCount = 0;
        using var _ = ImageAssetPreviewCache.UseBitmapLoaderForTests(_ =>
        {
            loadCount++;
            return CreateBitmap();
        });
        var path = CreateTempImageFile();
        var converter = new ImageAssetPreviewConverter();
        var first = converter.Convert(path, typeof(IImage), null, CultureInfo.InvariantCulture);
        var nextWrite = File.GetLastWriteTimeUtc(path).AddSeconds(2);
        File.SetLastWriteTimeUtc(path, nextWrite);

        var second = converter.Convert(path, typeof(IImage), null, CultureInfo.InvariantCulture);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Equal(2, loadCount);
        Assert.Equal(2, ImageAssetPreviewCache.Count);

        var metrics = ImageAssetPreviewCache.Metrics;
        Assert.Equal(2, metrics.RequestCount);
        Assert.Equal(0, metrics.HitCount);
        Assert.Equal(2, metrics.LoadCount);
        Assert.Equal(2, metrics.MissCount);
        Assert.Equal(2, metrics.CacheEntryCount);
    }

    [Fact]
    public void ConverterKeepsDecodedThumbnailAndFullPreviewCacheEntriesSeparate()
    {
        ImageAssetPreviewCache.Clear();
        var decodeWidths = new List<int?>();
        using var _ = ImageAssetPreviewCache.UseBitmapLoaderForTests((_, decodePixelWidth) =>
        {
            decodeWidths.Add(decodePixelWidth);
            return CreateBitmap();
        });
        var path = CreateTempImageFile();
        var converter = new ImageAssetPreviewConverter();

        var thumbnail = converter.Convert(path, typeof(IImage), "32", CultureInfo.InvariantCulture);
        var sameThumbnail = converter.Convert(path, typeof(IImage), 32, CultureInfo.InvariantCulture);
        var fullPreview = converter.Convert(path, typeof(IImage), null, CultureInfo.InvariantCulture);

        Assert.NotNull(thumbnail);
        Assert.Same(thumbnail, sameThumbnail);
        Assert.NotNull(fullPreview);
        Assert.NotSame(thumbnail, fullPreview);
        Assert.Equal([32, null], decodeWidths);
        Assert.Equal(2, ImageAssetPreviewCache.Count);

        var metrics = ImageAssetPreviewCache.Metrics;
        Assert.Equal(3, metrics.RequestCount);
        Assert.Equal(1, metrics.HitCount);
        Assert.Equal(2, metrics.LoadCount);
        Assert.Equal(2, metrics.MissCount);
    }

    [Fact]
    public void ConverterIgnoresMissingAndRelativePaths()
    {
        ImageAssetPreviewCache.Clear();
        var converter = new ImageAssetPreviewConverter();

        Assert.Null(converter.Convert("relative.png", typeof(IImage), null, CultureInfo.InvariantCulture));
        Assert.Null(converter.Convert(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.png"), typeof(IImage), null, CultureInfo.InvariantCulture));
        Assert.Equal(0, ImageAssetPreviewCache.Count);

        var metrics = ImageAssetPreviewCache.Metrics;
        Assert.Equal(2, metrics.RequestCount);
        Assert.Equal(0, metrics.HitCount);
        Assert.Equal(0, metrics.LoadCount);
        Assert.Equal(2, metrics.MissCount);
        Assert.Equal(1, metrics.InvalidPathCount);
        Assert.Equal(1, metrics.MissingFileCount);
        Assert.Equal(0, metrics.LoadFailureCount);
        Assert.Equal(0, metrics.CacheEntryCount);
    }

    [Fact]
    public void ConverterIgnoresEmptyPathWithoutRecordingCacheRequest()
    {
        ImageAssetPreviewCache.Clear();
        var converter = new ImageAssetPreviewConverter();

        Assert.Null(converter.Convert(string.Empty, typeof(IImage), null, CultureInfo.InvariantCulture));
        Assert.Null(converter.Convert("   ", typeof(IImage), null, CultureInfo.InvariantCulture));
        Assert.Equal(0, ImageAssetPreviewCache.Count);

        var metrics = ImageAssetPreviewCache.Metrics;
        Assert.Equal(0, metrics.RequestCount);
        Assert.Equal(0, metrics.HitCount);
        Assert.Equal(0, metrics.LoadCount);
        Assert.Equal(0, metrics.MissCount);
        Assert.Equal(0, metrics.InvalidPathCount);
        Assert.Equal(0, metrics.MissingFileCount);
        Assert.Equal(0, metrics.LoadFailureCount);
        Assert.Equal(0, metrics.CacheEntryCount);
    }

    [Fact]
    public void ConverterRecordsLoadFailures()
    {
        ImageAssetPreviewCache.Clear();
        using var _ = ImageAssetPreviewCache.UseBitmapLoaderForTests(_ => null);
        var converter = new ImageAssetPreviewConverter();
        var path = CreateTempImageFile();

        Assert.Null(converter.Convert(path, typeof(IImage), null, CultureInfo.InvariantCulture));

        var metrics = ImageAssetPreviewCache.Metrics;
        Assert.Equal(1, metrics.RequestCount);
        Assert.Equal(0, metrics.HitCount);
        Assert.Equal(0, metrics.LoadCount);
        Assert.Equal(1, metrics.MissCount);
        Assert.Equal(0, metrics.InvalidPathCount);
        Assert.Equal(0, metrics.MissingFileCount);
        Assert.Equal(1, metrics.LoadFailureCount);
        Assert.Equal(0, metrics.CacheEntryCount);
    }

    private static string CreateTempImageFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"draft-agent-preview-cache-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47]);
        return path;
    }

    private static IImage CreateBitmap()
    {
        return new TestImage();
    }

    private sealed class TestImage : IImage
    {
        public Size Size => new(1, 1);

        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
        {
        }
    }
}

