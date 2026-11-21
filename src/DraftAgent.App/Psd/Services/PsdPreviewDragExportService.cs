using System;
using System.IO;
using System.Threading.Tasks;
using PsdLayerLoader.Core;

namespace DraftAgent.App.Psd.Services;

public static class PsdPreviewDragExportService
{
    public static async Task<string> SaveDragPreviewAsync(
        PsdMergedPreview preview,
        string suggestedFileName,
        string? rootDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(preview);

        var exportDirectory = rootDirectory ?? Path.Combine(
            Path.GetTempPath(),
            "DraftAgent",
            "PsdLayerLoader",
            "drag");
        var exportPath = Path.Combine(exportDirectory, BuildSafePngFileName(suggestedFileName));

        await Task.Run(() => PsdPreviewPngExporter.SavePreview(preview, exportPath));
        return exportPath;
    }

    private static string BuildSafePngFileName(string fileName)
    {
        var candidate = string.IsNullOrWhiteSpace(fileName)
            ? "psd-preview.png"
            : fileName.Trim();
        if (!candidate.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            candidate += ".png";
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            candidate = candidate.Replace(invalid, '_');
        }

        return candidate;
    }
}

