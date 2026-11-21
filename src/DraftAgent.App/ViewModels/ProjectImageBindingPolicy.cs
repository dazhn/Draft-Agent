using System;
using System.Collections.Generic;
using System.IO;
using DraftAgent.Core;

namespace DraftAgent.App.ViewModels;

public static class ProjectImageBindingPolicy
{
    public static MediaImportRequest BuildImportRequest(BindingTargetViewModel target, string imagePath)
    {
        var extension = Path.GetExtension(imagePath);
        var isPsd = IsPsd(extension);
        if (isPsd && !target.SupportsPsd)
        {
            throw new InvalidOperationException("PSD/PSB imports are only supported for character binding targets.");
        }

        return new MediaImportRequest(
            imagePath,
            isPsd ? MediaAssetKind.Psd : MediaAssetKind.Image,
            isPsd ? MediaAssetSource.ImportedPsd : MediaAssetSource.ImportedPng,
            target.TargetId,
            target.DisplayName,
            new Dictionary<string, string>
            {
                ["bindingKind"] = target.Kind.ToString(),
                ["originalFileName"] = Path.GetFileName(imagePath)
            });
    }

    public static MediaImportRequest BuildRowImageImportRequest(string interactionId, string displayName, string imagePath)
    {
        var extension = Path.GetExtension(imagePath);
        if (IsPsd(extension))
        {
            throw new InvalidOperationException("PSD/PSB imports are not supported for narration or effects rows.");
        }

        return new MediaImportRequest(
            imagePath,
            MediaAssetKind.Image,
            MediaAssetSource.ImportedPng,
            interactionId,
            displayName,
            new Dictionary<string, string>
            {
                ["bindingKind"] = BindingTargetKind.Narration.ToString(),
                ["bindingScope"] = "RowImage",
                ["originalFileName"] = Path.GetFileName(imagePath)
            });
    }

    public static MediaImportRequest BuildPsdDefaultPreviewImportRequest(
        BindingTargetViewModel target,
        string previewPath,
        string sourcePsdAssetId,
        string sourcePsdPath)
    {
        return new MediaImportRequest(
            previewPath,
            MediaAssetKind.Image,
            MediaAssetSource.ImportedPng,
            target.TargetId,
            $"{target.DisplayName} PSD 默认预览",
            new Dictionary<string, string>
            {
                ["bindingKind"] = target.Kind.ToString(),
                ["source"] = "InternalPsdDefaultPreview",
                ["sourcePsdAssetId"] = sourcePsdAssetId,
                ["sourcePsdPath"] = sourcePsdPath,
                ["sourcePsdFileName"] = Path.GetFileName(sourcePsdPath),
                ["previewFileName"] = Path.GetFileName(previewPath)
            });
    }

    private static bool IsPsd(string extension)
    {
        return extension.Equals(".psd", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".psb", StringComparison.OrdinalIgnoreCase);
    }
}
