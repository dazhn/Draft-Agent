using System;
using System.IO;
using DraftAgent.Core;

namespace DraftAgent.App.ViewModels;

internal static class VisualPreviewAssetSelector
{
    public static string? ResolvePreviewAsset(
        VisualAssetBinding? visualBinding,
        string? fallbackReference,
        Func<string?, string?> resolve,
        Func<VisualAssetBinding?, string?>? renderDefaultPsdPreview = null)
    {
        ArgumentNullException.ThrowIfNull(resolve);

        if (visualBinding is { SourceKind: VisualSourceKind.Psd })
        {
            return FirstResolvedNonPsd(resolve, visualBinding.RenderedAssetId) ??
                renderDefaultPsdPreview?.Invoke(visualBinding);
        }

        if (visualBinding is { SourceKind: VisualSourceKind.Image })
        {
            return FirstResolvedNonPsd(
                resolve,
                visualBinding.RenderedAssetId,
                visualBinding.SourceAssetId,
                fallbackReference);
        }

        return FirstResolvedNonPsd(resolve, fallbackReference);
    }

    private static string? FirstResolvedNonPsd(
        Func<string?, string?> resolve,
        params string?[] references)
    {
        foreach (var reference in references)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                continue;
            }

            var resolved = resolve(reference);
            if (string.IsNullOrWhiteSpace(resolved) || IsPsdPath(resolved))
            {
                continue;
            }

            return resolved;
        }

        return null;
    }

    private static bool IsPsdPath(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".psd", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".psb", StringComparison.OrdinalIgnoreCase);
    }
}
