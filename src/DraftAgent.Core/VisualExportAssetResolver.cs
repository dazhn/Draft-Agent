namespace DraftAgent.Core;

public sealed class VisualExportAssetResolver
{
    private readonly IProjectAssetResolver _assetResolver;

    public VisualExportAssetResolver(IProjectAssetResolver? assetResolver = null)
    {
        _assetResolver = assetResolver ?? new ProjectAssetResolver();
    }

    public VisualExportAssetResolution Resolve(
        InteractionProject project,
        InteractionScene scene,
        CharacterInteraction interaction,
        WorkspaceProject workspace,
        WorkspaceProjectManifest? manifest = null)
    {
        var subjectCharacter = SubjectCharacter(project, interaction);
        var hasExplicitRowBinding = IsExplicitVisualBinding(interaction.Binding.SubjectCharacterVisualBinding);
        var references = (hasExplicitRowBinding
                ? VisualReferences(interaction.Binding.SubjectCharacterVisualBinding)
                : VisualReferences(subjectCharacter?.AssetBinding.PortraitVisualBinding, subjectCharacter?.AssetBinding.Portrait))
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var visualAsset = FirstExportableResolution(
            references,
            workspace,
            manifest,
            out var blockedPsd);
        var issues = new List<EditingTimelinePreflightIssue>();
        if (blockedPsd is not null && visualAsset is null)
        {
            issues.Add(EditingTimelinePreflightIssue.PsdReference(
                scene.Id,
                interaction.Id,
                blockedPsd.OriginalReference));
        }
        else if (visualAsset is null && references.Length > 0)
        {
            issues.Add(EditingTimelinePreflightIssue.MissingVisual(
                scene.Id,
                interaction.Id,
                string.Join(" | ", references)));
        }

        return new VisualExportAssetResolution(visualAsset, issues);
    }

    private ProjectAssetResolution? FirstExportableResolution(
        IEnumerable<string?> references,
        WorkspaceProject workspace,
        WorkspaceProjectManifest? manifest,
        out ProjectAssetResolution? blockedPsd)
    {
        blockedPsd = null;
        foreach (var reference in references)
        {
            var resolution = _assetResolver.Resolve(reference, workspace, manifest);
            if (resolution is null)
            {
                continue;
            }

            if (IsPsdVisualAsset(resolution))
            {
                blockedPsd ??= resolution;
                continue;
            }

            return resolution;
        }

        return null;
    }

    private static Character? SubjectCharacter(InteractionProject project, CharacterInteraction interaction)
    {
        return string.IsNullOrWhiteSpace(interaction.SubjectCharacterId)
            ? null
            : project.Characters.FirstOrDefault(character =>
                string.Equals(character.Id, interaction.SubjectCharacterId, StringComparison.Ordinal));
    }

    private static IEnumerable<string?> VisualReferences(VisualAssetBinding? binding, string? fallbackImageAssetId = null)
    {
        if (binding is null || binding == VisualAssetBinding.None)
        {
            yield return fallbackImageAssetId;
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(binding.RenderedAssetId))
        {
            yield return binding.RenderedAssetId;
        }

        if (!string.IsNullOrWhiteSpace(binding.SourceAssetId) &&
            !string.Equals(binding.SourceAssetId, binding.RenderedAssetId, StringComparison.Ordinal))
        {
            yield return binding.SourceAssetId;
        }

        yield return fallbackImageAssetId;
    }

    private static bool IsPsdVisualAsset(ProjectAssetResolution resolution)
    {
        return resolution.Asset?.Kind == MediaAssetKind.Psd || IsPsdPath(resolution.AbsolutePath);
    }

    private static bool IsPsdPath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".psd", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".psb", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExplicitVisualBinding(VisualAssetBinding? binding)
    {
        return binding is { SourceKind: not VisualSourceKind.None } &&
            (!string.IsNullOrWhiteSpace(binding.SourceAssetId) ||
                !string.IsNullOrWhiteSpace(binding.RenderedAssetId));
    }
}

public sealed record VisualExportAssetResolution(
    ProjectAssetResolution? Asset,
    IReadOnlyList<EditingTimelinePreflightIssue> Issues);
