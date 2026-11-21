namespace DraftAgent.Core;

public interface IProjectAssetResolver
{
    string? ResolvePath(string? reference, WorkspaceProject workspace, WorkspaceProjectManifest? manifest = null);

    ProjectAssetResolution? Resolve(string? reference, WorkspaceProject workspace, WorkspaceProjectManifest? manifest = null);
}

public sealed class ProjectAssetResolver : IProjectAssetResolver
{
    public string? ResolvePath(string? reference, WorkspaceProject workspace, WorkspaceProjectManifest? manifest = null)
    {
        return Resolve(reference, workspace, manifest)?.AbsolutePath;
    }

    public ProjectAssetResolution? Resolve(string? reference, WorkspaceProject workspace, WorkspaceProjectManifest? manifest = null)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var trimmed = reference.Trim();
        var byId = manifest?.Assets.FirstOrDefault(asset => string.Equals(asset.Id, trimmed, StringComparison.Ordinal));
        if (byId is not null)
        {
            return ExistingAsset(trimmed, byId, workspace, "assetId");
        }

        var byRelativePath = manifest?.Assets.FirstOrDefault(asset =>
            string.Equals(asset.ProjectRelativePath, trimmed.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
        if (byRelativePath is not null)
        {
            return ExistingAsset(trimmed, byRelativePath, workspace, "projectRelativePath");
        }

        if (Path.IsPathRooted(trimmed))
        {
            var absolute = Path.GetFullPath(trimmed);
            return File.Exists(absolute)
                ? new ProjectAssetResolution(trimmed, absolute, null, "absolutePath")
                : null;
        }

        var projectPath = Path.GetFullPath(Path.Combine(workspace.RootPath, trimmed));
        if (IsInsideWorkspace(projectPath, workspace.RootPath) && File.Exists(projectPath))
        {
            return new ProjectAssetResolution(trimmed, projectPath, null, "workspaceRelativePath");
        }

        return null;
    }

    private static ProjectAssetResolution? ExistingAsset(string originalReference, ProjectMediaAsset asset, WorkspaceProject workspace, string resolutionKind)
    {
        var absolute = Path.GetFullPath(asset.AbsolutePath);
        if (IsInsideWorkspace(absolute, workspace.RootPath) && File.Exists(absolute))
        {
            return new ProjectAssetResolution(originalReference, absolute, asset, resolutionKind);
        }

        if (!string.IsNullOrWhiteSpace(asset.ProjectRelativePath))
        {
            var rebased = Path.GetFullPath(Path.Combine(
                workspace.RootPath,
                asset.ProjectRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (IsInsideWorkspace(rebased, workspace.RootPath) && File.Exists(rebased))
            {
                return new ProjectAssetResolution(originalReference, rebased, asset, $"{resolutionKind}:rebased");
            }
        }

        return null;
    }

    private static bool IsInsideWorkspace(string path, string rootPath)
    {
        var normalizedPath = EnsureDirectorySeparator(Path.GetFullPath(Path.GetDirectoryName(path) ?? string.Empty));
        var normalizedRoot = EnsureDirectorySeparator(Path.GetFullPath(rootPath));
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureDirectorySeparator(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }
}
