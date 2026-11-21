using System;
using DraftAgent.Core;
using PsdLayerLoader.Core;

namespace DraftAgent.App.ViewModels;

internal static class PsdVisualBindingDiagnostics
{
    public static string BuildSourceFingerprint(string? psdAssetId, string? psdPath)
    {
        return PsdVisualSourceMetadata.BuildSourceFingerprint(psdAssetId, psdPath);
    }

    public static bool SourceFingerprintMatches(PsdRenderSelection selection, string currentFingerprint)
    {
        return string.Equals(selection.SourceFingerprint, currentFingerprint, StringComparison.Ordinal);
    }

    public static string FormatMismatchText(
        VisualAssetBinding visualBinding,
        Func<string?, string?>? resolveProjectAssetPath)
    {
        if (visualBinding.SourceKind != VisualSourceKind.Psd)
        {
            return string.Empty;
        }

        if (visualBinding.PsdSelection is null ||
            string.IsNullOrWhiteSpace(visualBinding.PsdSelection.SourceFingerprint))
        {
            return "PSD source state: not recorded";
        }

        if (string.IsNullOrWhiteSpace(visualBinding.SourceAssetId))
        {
            return "PSD source state: missing source";
        }

        var psdPath = resolveProjectAssetPath?.Invoke(visualBinding.SourceAssetId);
        if (string.IsNullOrWhiteSpace(psdPath))
        {
            return "PSD source state: source path unavailable";
        }

        var currentFingerprint = BuildSourceFingerprint(visualBinding.SourceAssetId, psdPath);
        return SourceFingerprintMatches(visualBinding.PsdSelection, currentFingerprint)
            ? "PSD source state: current"
            : "PSD source state: changed, reselect layers";
    }
}
