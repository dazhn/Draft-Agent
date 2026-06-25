using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media.Imaging;
using PsdLayerLoader.Core;

namespace DraftAgent.App.Psd.Services;

public static class PsdPreviewBitmapFactory
{
    public static WriteableBitmap Create(PsdMergedPreview preview)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(preview.Width, preview.Height),
            new Vector(96, 96));

        using var framebuffer = bitmap.Lock();
        unsafe
        {
            var destinationBase = (byte*)framebuffer.Address;
            var source = preview.Rgba.AsSpan();

            for (var y = 0; y < preview.Height; y++)
            {
                var destinationRow = destinationBase + y * framebuffer.RowBytes;
                var sourceRow = source.Slice(y * preview.Width * 4, preview.Width * 4);

                for (var x = 0; x < preview.Width; x++)
                {
                    var sourceOffset = x * 4;
                    var destinationOffset = x * 4;
                    WriteDisplayPixel(
                        destinationRow,
                        destinationOffset,
                        x,
                        y,
                        sourceRow[sourceOffset],
                        sourceRow[sourceOffset + 1],
                        sourceRow[sourceOffset + 2],
                        sourceRow[sourceOffset + 3]);
                }
            }
        }

        return bitmap;
    }

    public static WriteableBitmap CreateThumbnail(PsdLayerPixelData layer)
    {
        var size = CalculateThumbnailRasterSize(layer.Width, layer.Height);
        return CreateThumbnail(layer, size.Width, size.Height);
    }

    public static WriteableBitmap CreateThumbnail(
        PsdLayerPixelData layer,
        int targetWidth,
        int targetHeight)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(targetWidth, targetHeight),
            new Vector(96, 96));

        using var framebuffer = bitmap.Lock();
        unsafe
        {
            var destinationBase = (byte*)framebuffer.Address;
            var placement = CalculateThumbnailPlacement(layer.Width, layer.Height, targetWidth, targetHeight);

            for (var y = 0; y < targetHeight; y++)
            {
                var destinationRow = destinationBase + y * framebuffer.RowBytes;
                for (var x = 0; x < targetWidth; x++)
                {
                    var destinationOffset = x * 4;
                    var relativeX = x - placement.OffsetX;
                    var relativeY = y - placement.OffsetY;
                    if (relativeX < 0 || relativeY < 0 || relativeX >= placement.Width || relativeY >= placement.Height)
                    {
                        WriteCheckerboardPixel(destinationRow, destinationOffset, x, y);
                        continue;
                    }

                    var sampleX = ((relativeX + 0.5) * layer.Width / placement.Width) - 0.5;
                    var sampleY = ((relativeY + 0.5) * layer.Height / placement.Height) - 0.5;
                    var sample = SampleBilinearRgba(layer.Rgba, layer.Width, layer.Height, sampleX, sampleY);

                    WriteDisplayPixel(
                        destinationRow,
                        destinationOffset,
                        x,
                        y,
                        sample.R,
                        sample.G,
                        sample.B,
                        sample.A);
                }
            }
        }

        return bitmap;
    }

    public static ThumbnailPlacement CalculateThumbnailPlacement(
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetHeight);

        var scale = Math.Min((double)targetWidth / sourceWidth, (double)targetHeight / sourceHeight);
        var width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        var height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        return new ThumbnailPlacement(
            (targetWidth - width) / 2,
            (targetHeight - height) / 2,
            width,
            height);
    }

    public static ThumbnailRasterSize CalculateThumbnailRasterSize(int sourceWidth, int sourceHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);

        const int side = 256;
        return new ThumbnailRasterSize(side, side);
    }

    public static RgbaSample SampleBilinearRgba(byte[] rgba, int width, int height, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (rgba.Length < checked(width * height * 4))
        {
            throw new ArgumentException("RGBA buffer is smaller than the supplied dimensions.", nameof(rgba));
        }

        var clampedX = Math.Clamp(x, 0.0, width - 1.0);
        var clampedY = Math.Clamp(y, 0.0, height - 1.0);
        var x0 = (int)Math.Floor(clampedX);
        var y0 = (int)Math.Floor(clampedY);
        var x1 = Math.Min(width - 1, x0 + 1);
        var y1 = Math.Min(height - 1, y0 + 1);
        var tx = clampedX - x0;
        var ty = clampedY - y0;

        var topLeft = ReadRgba(rgba, width, x0, y0);
        var topRight = ReadRgba(rgba, width, x1, y0);
        var bottomLeft = ReadRgba(rgba, width, x0, y1);
        var bottomRight = ReadRgba(rgba, width, x1, y1);

        return new RgbaSample(
            Blend(topLeft.R, topRight.R, bottomLeft.R, bottomRight.R, tx, ty),
            Blend(topLeft.G, topRight.G, bottomLeft.G, bottomRight.G, tx, ty),
            Blend(topLeft.B, topRight.B, bottomLeft.B, bottomRight.B, tx, ty),
            Blend(topLeft.A, topRight.A, bottomLeft.A, bottomRight.A, tx, ty));
    }

    public static WriteableBitmap CreateLayerCanvas(int canvasWidth, int canvasHeight, IReadOnlyList<PsdLayerPixelData> layers)
    {
        return CreateLayerCanvasBitmap(canvasWidth, canvasHeight, layers).Bitmap;
    }

    public static PsdMergedPreview CreateLayerCanvasPreview(int canvasWidth, int canvasHeight, IReadOnlyList<PsdLayerPixelData> layers)
    {
        return PsdLayerCanvasComposer.Compose(canvasWidth, canvasHeight, layers, PsdExportBounds.CanvasBounds);
    }

    public static (WriteableBitmap Bitmap, PsdMergedPreview Preview) CreateLayerCanvasBitmap(
        int canvasWidth,
        int canvasHeight,
        IReadOnlyList<PsdLayerPixelData> layers,
        PsdLayerExportRect? fixedLayerBounds = null)
    {
        var preview = fixedLayerBounds is null
            ? PsdLayerCanvasComposer.Compose(canvasWidth, canvasHeight, layers, PsdExportBounds.CanvasBounds)
            : PsdLayerCanvasComposer.Compose(canvasWidth, canvasHeight, layers, PsdExportBounds.LayerBounds, fixedLayerBounds);
        return (Create(preview), preview);
    }

    private static unsafe void WriteDisplayPixel(
        byte* destinationRow,
        int destinationOffset,
        int x,
        int y,
        byte sourceR,
        byte sourceG,
        byte sourceB,
        byte sourceA)
    {
        var background = GetCheckerboardColor(x, y);
        var alpha = sourceA / 255.0;
        var inverseAlpha = 1.0 - alpha;

        destinationRow[destinationOffset] = ToByte(sourceB / 255.0 * alpha + background.B / 255.0 * inverseAlpha);
        destinationRow[destinationOffset + 1] = ToByte(sourceG / 255.0 * alpha + background.G / 255.0 * inverseAlpha);
        destinationRow[destinationOffset + 2] = ToByte(sourceR / 255.0 * alpha + background.R / 255.0 * inverseAlpha);
        destinationRow[destinationOffset + 3] = 255;
    }

    private static unsafe void WriteCheckerboardPixel(byte* destinationRow, int destinationOffset, int x, int y)
    {
        var background = GetCheckerboardColor(x, y);
        destinationRow[destinationOffset] = background.B;
        destinationRow[destinationOffset + 1] = background.G;
        destinationRow[destinationOffset + 2] = background.R;
        destinationRow[destinationOffset + 3] = 255;
    }

    private static (byte R, byte G, byte B) GetCheckerboardColor(int x, int y)
    {
        return ((x / 24) + (y / 24)) % 2 == 0
            ? ((byte)18, (byte)24, (byte)34)
            : ((byte)13, (byte)18, (byte)27);
    }

    private static byte ToByte(double value)
    {
        return (byte)Math.Clamp((int)Math.Round(value * 255.0), 0, 255);
    }

    private static RgbaSample ReadRgba(byte[] rgba, int width, int x, int y)
    {
        var offset = ((y * width) + x) * 4;
        return new RgbaSample(rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3]);
    }

    private static byte Blend(byte topLeft, byte topRight, byte bottomLeft, byte bottomRight, double tx, double ty)
    {
        var top = topLeft + (topRight - topLeft) * tx;
        var bottom = bottomLeft + (bottomRight - bottomLeft) * tx;
        return (byte)Math.Clamp((int)Math.Round(top + (bottom - top) * ty), 0, 255);
    }

}

public readonly record struct ThumbnailPlacement(int OffsetX, int OffsetY, int Width, int Height);

public readonly record struct ThumbnailRasterSize(int Width, int Height);

public readonly record struct RgbaSample(byte R, byte G, byte B, byte A);

