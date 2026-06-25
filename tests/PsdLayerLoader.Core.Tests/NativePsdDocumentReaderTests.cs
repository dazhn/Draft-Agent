using PsdLayerLoader.Core;
using System.Text;

namespace PsdLayerLoader.Core.Tests;

public sealed class NativePsdDocumentReaderTests
{
    [Fact]
    public void LoadReadsPsdSdkSampleDocumentAndLayers()
    {
        var samplePath = GetSamplePsdPath();

        Assert.True(File.Exists(samplePath), samplePath);

        var document = new NativePsdDocumentReader().Load(samplePath);

        Assert.True(document.Width > 0);
        Assert.True(document.Height > 0);
        Assert.True(document.BitsPerChannel is 8 or 16 or 32);
        Assert.NotEmpty(document.Layers);
        Assert.Contains(document.Layers, layer => !string.IsNullOrWhiteSpace(layer.Name));
    }

    [Fact]
    public void LoadReadsUnicodeLayerAndGroupNames()
    {
        var samplePath = CreateUnicodeLayerNamePsd();

        var document = new NativePsdDocumentReader().Load(samplePath);

        Assert.Contains(document.Layers, layer => layer.Name == "表情组" && layer.IsGroup);
        Assert.Contains(document.Layers, layer => layer.Name == "开心");
    }

    [Fact]
    public void LoadMergedPreviewReadsVisiblePsdPixels()
    {
        var samplePath = GetSamplePsdPath();

        Assert.True(File.Exists(samplePath), samplePath);

        var preview = new NativePsdDocumentReader().LoadMergedPreview(samplePath);

        Assert.True(preview.Width > 0);
        Assert.True(preview.Height > 0);
        Assert.Equal(preview.Width * preview.Height * 4, preview.Rgba.Length);
        Assert.Contains(preview.Rgba, value => value != 0);
    }

    [Fact]
    public void LoadLayerPixelsReadsVisibleMinimumRasterBounds()
    {
        var samplePath = GetSamplePsdPath();

        Assert.True(File.Exists(samplePath), samplePath);

        var reader = new NativePsdDocumentReader();
        var document = reader.Load(samplePath);
        var rasterLayer = document.Layers.First(layer => !layer.IsGroup && layer.Width > 0 && layer.Height > 0);

        var pixels = reader.LoadLayerPixels(samplePath, rasterLayer.LayerId);

        Assert.True(pixels.Width > 0);
        Assert.True(pixels.Height > 0);
        Assert.Equal(pixels.Width * pixels.Height * 4, pixels.Rgba.Length);
        Assert.InRange(pixels.Left, rasterLayer.Left, rasterLayer.Right - 1);
        Assert.InRange(pixels.Top, rasterLayer.Top, rasterLayer.Bottom - 1);
        Assert.InRange(pixels.Left + pixels.Width, rasterLayer.Left + 1, rasterLayer.Right);
        Assert.InRange(pixels.Top + pixels.Height, rasterLayer.Top + 1, rasterLayer.Bottom);
        Assert.True(pixels.Width <= rasterLayer.Width);
        Assert.True(pixels.Height <= rasterLayer.Height);
        Assert.Contains(Enumerable.Range(0, pixels.Rgba.Length / 4), index => pixels.Rgba[index * 4 + 3] != 0);
        Assert.Contains(Enumerable.Range(0, pixels.Width), x => AlphaAt(pixels, x, 0) != 0);
        Assert.Contains(Enumerable.Range(0, pixels.Width), x => AlphaAt(pixels, x, pixels.Height - 1) != 0);
        Assert.Contains(Enumerable.Range(0, pixels.Height), y => AlphaAt(pixels, 0, y) != 0);
        Assert.Contains(Enumerable.Range(0, pixels.Height), y => AlphaAt(pixels, pixels.Width - 1, y) != 0);
    }

    private static string GetSamplePsdPath()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "PsdLayerLoader.Native",
            "third_party",
            "psd_sdk",
            "bin",
            "Sample.psd"));
    }

    private static string CreateUnicodeLayerNamePsd()
    {
        var path = Path.Combine(Path.GetTempPath(), $"unicode-layer-names-{Guid.NewGuid():N}.psd");
        using var stream = File.Create(path);

        WriteAscii(stream, "8BPS");
        WriteUInt16BE(stream, 1);
        stream.Write(new byte[6]);
        WriteUInt16BE(stream, 3);
        WriteUInt32BE(stream, 1);
        WriteUInt32BE(stream, 1);
        WriteUInt16BE(stream, 8);
        WriteUInt16BE(stream, 3);

        WriteUInt32BE(stream, 0);
        WriteUInt32BE(stream, 0);

        using var layerInfo = new MemoryStream();
        WriteUInt16BE(layerInfo, 2);
        WriteLayerRecord(layerInfo, "Group", "表情组", 1);
        WriteLayerRecord(layerInfo, "Smile", "开心", null);

        using var layerMaskInfo = new MemoryStream();
        WriteUInt32BE(layerMaskInfo, checked((uint)layerInfo.Length));
        layerInfo.Position = 0;
        layerInfo.CopyTo(layerMaskInfo);

        WriteUInt32BE(stream, checked((uint)layerMaskInfo.Length));
        layerMaskInfo.Position = 0;
        layerMaskInfo.CopyTo(stream);

        WriteUInt16BE(stream, 0);
        stream.Write(new byte[] { 0, 0, 0 });
        return path;
    }

    private static void WriteLayerRecord(Stream stream, string pascalName, string unicodeName, uint? sectionDividerType)
    {
        WriteInt32BE(stream, 0);
        WriteInt32BE(stream, 0);
        WriteInt32BE(stream, 1);
        WriteInt32BE(stream, 1);
        WriteUInt16BE(stream, 0);
        WriteAscii(stream, "8BIM");
        WriteAscii(stream, "norm");
        stream.WriteByte(255);
        stream.WriteByte(0);
        stream.WriteByte(0);
        stream.WriteByte(0);

        using var extraData = new MemoryStream();
        WriteUInt32BE(extraData, 0);
        WriteUInt32BE(extraData, 0);
        WritePascalName(extraData, pascalName);
        if (sectionDividerType is { } type)
        {
            WriteAdditionalLayerInfo(extraData, "lsct", inner => WriteUInt32BE(inner, type));
        }

        WriteAdditionalLayerInfo(extraData, "luni", inner =>
        {
            var utf16 = Encoding.BigEndianUnicode.GetBytes(unicodeName);
            WriteUInt32BE(inner, checked((uint)(utf16.Length / 2)));
            inner.Write(utf16);
        });

        WriteUInt32BE(stream, checked((uint)extraData.Length));
        extraData.Position = 0;
        extraData.CopyTo(stream);
    }

    private static void WriteAdditionalLayerInfo(Stream stream, string key, Action<Stream> writeData)
    {
        using var data = new MemoryStream();
        writeData(data);

        WriteAscii(stream, "8BIM");
        WriteAscii(stream, key);
        WriteUInt32BE(stream, checked((uint)data.Length));
        data.Position = 0;
        data.CopyTo(stream);
        if ((data.Length % 2) != 0)
        {
            stream.WriteByte(0);
        }
    }

    private static void WritePascalName(Stream stream, string name)
    {
        var bytes = Encoding.ASCII.GetBytes(name);
        stream.WriteByte(checked((byte)bytes.Length));
        stream.Write(bytes);
        while ((stream.Position % 4) != 0)
        {
            stream.WriteByte(0);
        }
    }

    private static void WriteAscii(Stream stream, string value)
    {
        stream.Write(Encoding.ASCII.GetBytes(value));
    }

    private static void WriteInt32BE(Stream stream, int value)
    {
        WriteUInt32BE(stream, unchecked((uint)value));
    }

    private static void WriteUInt16BE(Stream stream, ushort value)
    {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static void WriteUInt32BE(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static byte AlphaAt(PsdLayerPixelData pixels, int x, int y)
    {
        return pixels.Rgba[((y * pixels.Width) + x) * 4 + 3];
    }
}

