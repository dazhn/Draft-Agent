using System.Globalization;

namespace PsdLayerLoader.Core;

public static class PsdVisualSourceMetadata
{
    public static string BuildSourceFingerprint(string? psdAssetId, string? psdPath)
    {
        var normalizedAssetId = NormalizeAssetId(psdAssetId) ?? "asset:none";
        var normalizedPath = string.IsNullOrWhiteSpace(psdPath)
            ? "path:none"
            : Path.GetFullPath(psdPath.Trim());
        var length = -1L;
        var mtimeTicks = 0L;
        if (!string.IsNullOrWhiteSpace(psdPath) && File.Exists(normalizedPath))
        {
            var fileInfo = new FileInfo(normalizedPath);
            length = fileInfo.Length;
            mtimeTicks = fileInfo.LastWriteTimeUtc.Ticks;
        }

        return string.Join(
            "|",
            "psd-fp-v2",
            PsdComponentProtocol.RequestSchemaVersion,
            normalizedAssetId,
            normalizedPath,
            length.ToString(CultureInfo.InvariantCulture),
            mtimeTicks.ToString(CultureInfo.InvariantCulture));
    }

    public static string BuildRenderCacheKey(
        string? psdAssetId,
        string sourceFingerprint,
        IReadOnlyList<int> layerIds,
        PsdExportBounds bounds)
    {
        var normalizedAssetId = NormalizeAssetId(psdAssetId) ?? "asset:none";
        var layers = layerIds.Count == 0
            ? "layers:none"
            : string.Join(",", layerIds);
        return string.Join(
            "|",
            "psd-render-v2",
            normalizedAssetId,
            sourceFingerprint,
            bounds.ToString(),
            layers);
    }

    private static string? NormalizeAssetId(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
