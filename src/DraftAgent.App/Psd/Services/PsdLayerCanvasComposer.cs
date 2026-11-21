using System;
using System.Collections.Generic;
using PsdLayerLoader.Core;

namespace DraftAgent.App.Psd.Services;

public static class PsdLayerCanvasComposer
{
    public static PsdMergedPreview Compose(
        int canvasWidth,
        int canvasHeight,
        IReadOnlyList<PsdLayerPixelData> layers,
        PsdExportBounds bounds,
        PsdLayerExportRect? fixedLayerBounds = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(canvasWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(canvasHeight);
        ArgumentNullException.ThrowIfNull(layers);

        var exportRect = bounds == PsdExportBounds.CanvasBounds
            ? new PsdLayerExportRect(0, 0, canvasWidth, canvasHeight)
            : NormalizeFixedBounds(fixedLayerBounds) ??
                GetVisibleLayerBounds(layers) ??
                new PsdLayerExportRect(0, 0, canvasWidth, canvasHeight);

        var output = new byte[checked(exportRect.Width * exportRect.Height * 4)];
        foreach (var layer in layers)
        {
            DrawLayer(output, exportRect, layer);
        }

        return new PsdMergedPreview(exportRect.Width, exportRect.Height, output);
    }

    private static PsdLayerExportRect? NormalizeFixedBounds(PsdLayerExportRect? fixedLayerBounds)
    {
        if (fixedLayerBounds is not { } bounds)
        {
            return null;
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bounds.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bounds.Height);
        return bounds;
    }

    private static PsdLayerExportRect? GetVisibleLayerBounds(IReadOnlyList<PsdLayerPixelData> layers)
    {
        var left = int.MaxValue;
        var top = int.MaxValue;
        var right = int.MinValue;
        var bottom = int.MinValue;

        foreach (var layer in layers)
        {
            for (var y = 0; y < layer.Height; y++)
            {
                for (var x = 0; x < layer.Width; x++)
                {
                    var sourceOffset = ((y * layer.Width) + x) * 4;
                    if (layer.Rgba[sourceOffset + 3] == 0)
                    {
                        continue;
                    }

                    var canvasX = layer.Left + x;
                    var canvasY = layer.Top + y;
                    left = Math.Min(left, canvasX);
                    top = Math.Min(top, canvasY);
                    right = Math.Max(right, canvasX + 1);
                    bottom = Math.Max(bottom, canvasY + 1);
                }
            }
        }

        if (left == int.MaxValue || top == int.MaxValue || right <= left || bottom <= top)
        {
            return null;
        }

        return new PsdLayerExportRect(left, top, right - left, bottom - top);
    }

    private static void DrawLayer(byte[] canvasRgba, PsdLayerExportRect exportRect, PsdLayerPixelData layer)
    {
        for (var y = 0; y < layer.Height; y++)
        {
            var outputY = layer.Top + y - exportRect.Top;
            if (outputY < 0 || outputY >= exportRect.Height)
            {
                continue;
            }

            for (var x = 0; x < layer.Width; x++)
            {
                var outputX = layer.Left + x - exportRect.Left;
                if (outputX < 0 || outputX >= exportRect.Width)
                {
                    continue;
                }

                var sourceOffset = ((y * layer.Width) + x) * 4;
                var sourceA = layer.Rgba[sourceOffset + 3] / 255.0;
                if (sourceA <= 0.0)
                {
                    continue;
                }

                var canvasOffset = ((outputY * exportRect.Width) + outputX) * 4;
                var destinationA = canvasRgba[canvasOffset + 3] / 255.0;
                var outA = sourceA + destinationA * (1.0 - sourceA);
                if (outA <= 0.0)
                {
                    continue;
                }

                var sourceR = layer.Rgba[sourceOffset] / 255.0;
                var sourceG = layer.Rgba[sourceOffset + 1] / 255.0;
                var sourceB = layer.Rgba[sourceOffset + 2] / 255.0;
                var destinationR = canvasRgba[canvasOffset] / 255.0;
                var destinationG = canvasRgba[canvasOffset + 1] / 255.0;
                var destinationB = canvasRgba[canvasOffset + 2] / 255.0;

                canvasRgba[canvasOffset] = ToByte((sourceR * sourceA + destinationR * destinationA * (1.0 - sourceA)) / outA);
                canvasRgba[canvasOffset + 1] = ToByte((sourceG * sourceA + destinationG * destinationA * (1.0 - sourceA)) / outA);
                canvasRgba[canvasOffset + 2] = ToByte((sourceB * sourceA + destinationB * destinationA * (1.0 - sourceA)) / outA);
                canvasRgba[canvasOffset + 3] = ToByte(outA);
            }
        }
    }

    private static byte ToByte(double value)
    {
        return (byte)Math.Clamp((int)Math.Round(value * 255.0), 0, 255);
    }
}

