using System;
using System.Collections.Generic;
using PsdLayerLoader.Core;

namespace DraftAgent.App.Psd.Services;

internal static class PsdRenderableLayerFilter
{
    private const byte SolidWhiteThreshold = 248;
    private const byte SolidBlackThreshold = 7;

    public static bool IsIgnorableSolidLayer(PsdLayerPixelData pixelData)
    {
        ArgumentNullException.ThrowIfNull(pixelData);
        ValidateRgbaBuffer(pixelData);

        var isSolidWhite = true;
        var isSolidBlack = true;
        var pixelBytes = checked(pixelData.Width * pixelData.Height * 4);
        for (var offset = 0; offset < pixelBytes; offset += 4)
        {
            var alpha = pixelData.Rgba[offset + 3];
            if (alpha != byte.MaxValue)
            {
                return false;
            }

            var red = pixelData.Rgba[offset];
            var green = pixelData.Rgba[offset + 1];
            var blue = pixelData.Rgba[offset + 2];
            isSolidWhite &= red >= SolidWhiteThreshold && green >= SolidWhiteThreshold && blue >= SolidWhiteThreshold;
            isSolidBlack &= red <= SolidBlackThreshold && green <= SolidBlackThreshold && blue <= SolidBlackThreshold;

            if (!isSolidWhite && !isSolidBlack)
            {
                return false;
            }
        }

        return isSolidWhite || isSolidBlack;
    }

    public static bool IsRenderableLayer(PsdLayerPixelData pixelData)
    {
        return TryGetFilledBounds(pixelData, out _) && !IsIgnorableSolidLayer(pixelData);
    }

    public static bool TryGetFilledBounds(PsdLayerPixelData pixelData, out PsdLayerExportRect bounds)
    {
        ArgumentNullException.ThrowIfNull(pixelData);
        ValidateRgbaBuffer(pixelData);

        var left = int.MaxValue;
        var top = int.MaxValue;
        var right = int.MinValue;
        var bottom = int.MinValue;

        for (var y = 0; y < pixelData.Height; y++)
        {
            for (var x = 0; x < pixelData.Width; x++)
            {
                var sourceOffset = ((y * pixelData.Width) + x) * 4;
                if (pixelData.Rgba[sourceOffset + 3] == 0)
                {
                    continue;
                }

                var canvasX = pixelData.Left + x;
                var canvasY = pixelData.Top + y;
                left = Math.Min(left, canvasX);
                top = Math.Min(top, canvasY);
                right = Math.Max(right, canvasX + 1);
                bottom = Math.Max(bottom, canvasY + 1);
            }
        }

        if (left == int.MaxValue || top == int.MaxValue || right <= left || bottom <= top)
        {
            bounds = default;
            return false;
        }

        bounds = new PsdLayerExportRect(left, top, right - left, bottom - top);
        return true;
    }

    public static bool TryGetRenderableUnionBounds(
        IEnumerable<PsdLayerPixelData> layers,
        out PsdLayerExportRect bounds)
    {
        ArgumentNullException.ThrowIfNull(layers);

        var left = int.MaxValue;
        var top = int.MaxValue;
        var right = int.MinValue;
        var bottom = int.MinValue;

        foreach (var layer in layers)
        {
            if (!IsRenderableLayer(layer) || !TryGetFilledBounds(layer, out var layerBounds))
            {
                continue;
            }

            left = Math.Min(left, layerBounds.Left);
            top = Math.Min(top, layerBounds.Top);
            right = Math.Max(right, layerBounds.Left + layerBounds.Width);
            bottom = Math.Max(bottom, layerBounds.Top + layerBounds.Height);
        }

        if (left == int.MaxValue || top == int.MaxValue || right <= left || bottom <= top)
        {
            bounds = default;
            return false;
        }

        bounds = new PsdLayerExportRect(left, top, right - left, bottom - top);
        return true;
    }

    private static void ValidateRgbaBuffer(PsdLayerPixelData pixelData)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelData.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelData.Height);
        ArgumentNullException.ThrowIfNull(pixelData.Rgba);
        var expectedLength = checked(pixelData.Width * pixelData.Height * 4);
        if (pixelData.Rgba.Length < expectedLength)
        {
            throw new ArgumentException("Layer RGBA buffer is smaller than the supplied dimensions.", nameof(pixelData));
        }
    }
}
