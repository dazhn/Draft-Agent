using System.Text.Json;

namespace DraftAgent.Core;

public sealed class FileInteractionProjectStore : IInteractionProjectStore
{
    private readonly string _path;

    public FileInteractionProjectStore(string path)
    {
        _path = path;
    }

    public async Task SaveAsync(InteractionProject project, CancellationToken cancellationToken = default)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await AtomicFileWriter.WriteUtf8TextAsync(_path, JsonSerializer.Serialize(project, DraftJson.Options), cancellationToken);
        }
        catch (Exception ex) when (ExternalBoundary.ShouldWrap(ex))
        {
            throw new ExternalIntegrationException("FileStore", "save", $"Project save failed: {ex.Message}", ex);
        }
    }

    public async Task<InteractionProject?> LoadLastAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(_path, cancellationToken);
            return JsonSerializer.Deserialize<InteractionProject>(json, DraftJson.Options);
        }
        catch (Exception ex) when (ExternalBoundary.ShouldWrap(ex))
        {
            throw new ExternalIntegrationException("FileStore", "load", $"Project load failed: {ex.Message}", ex);
        }
    }
}

public sealed class FileWorkspaceProjectStateStore : IWorkspaceProjectStateStore
{
    private readonly string _path;

    public FileWorkspaceProjectStateStore(string path)
    {
        _path = path;
    }

    public async Task SaveAsync(WorkspaceProjectState state, CancellationToken cancellationToken = default)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await AtomicFileWriter.WriteUtf8TextAsync(_path, JsonSerializer.Serialize(state, DraftJson.Options), cancellationToken);
        }
        catch (Exception ex) when (ExternalBoundary.ShouldWrap(ex))
        {
            throw new ExternalIntegrationException("FileStore", "save_workspace_project_state", $"Workspace project state save failed: {ex.Message}", ex);
        }
    }

    public async Task<WorkspaceProjectState?> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(_path, cancellationToken);
            return JsonSerializer.Deserialize<WorkspaceProjectState>(json, DraftJson.Options);
        }
        catch (Exception ex) when (ExternalBoundary.ShouldWrap(ex))
        {
            throw new ExternalIntegrationException("FileStore", "load_workspace_project_state", $"Workspace project state load failed: {ex.Message}", ex);
        }
    }
}
