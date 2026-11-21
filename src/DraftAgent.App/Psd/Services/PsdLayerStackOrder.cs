using System;
using System.Collections.Generic;
using System.Linq;
using PsdLayerLoader.Core;

namespace DraftAgent.App.Psd.Services;

public static class PsdLayerStackOrder
{
    public static IOrderedEnumerable<T> ForPanel<T>(IEnumerable<T> layers, Func<T, int> layerId)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(layerId);
        return layers.OrderByDescending(layerId);
    }

    public static IOrderedEnumerable<PsdLayerPixelData> ForCompositing(IEnumerable<PsdLayerPixelData> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        return layers.OrderBy(layer => layer.LayerId);
    }
}

