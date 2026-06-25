using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DraftAgent.Core;

public interface IWorkspaceProjectManager
{
    Task<WorkspaceProjectManifest> CreateOrOpenAsync(string rootPath, string projectName, CancellationToken cancellationToken = default);

    Task<WorkspaceProjectManifest> LoadAsync(string rootPath, CancellationToken cancellationToken = default);

    Task<InteractionProject?> LoadDraftAsync(WorkspaceProject workspace, CancellationToken cancellationToken = default);

    Task<WorkspaceProjectManifest> SaveDraftAsync(WorkspaceProject workspace, InteractionProject project, CancellationToken cancellationToken = default);

    Task<string?> LoadOriginalAiJsonAsync(WorkspaceProject workspace, CancellationToken cancellationToken = default);

    Task<WorkspaceProjectManifest> SaveOriginalAiJsonAsync(WorkspaceProject workspace, string rawJson, CancellationToken cancellationToken = default);
}

public interface IProjectMediaAssetService
{
    Task<MediaImportResult> ImportAsync(WorkspaceProject workspace, MediaImportRequest request, CancellationToken cancellationToken = default);
}

public sealed class WorkspaceProjectManager : IWorkspaceProjectManager
{
    public const string ManifestFileName = "draft-agent-project.json";
    public const string DraftFileName = "draft-agent-workspace.json";
    public const string OriginalAiJsonFileName = "draft-agent-original-ai.json";
    public const string ManifestSchema = "draft_agent_project_v1";

    private readonly IWorkflowLogger _logger;

    public WorkspaceProjectManager(IWorkflowLogger? logger = null)
    {
        _logger = logger ?? new InMemoryWorkflowLogger();
    }

    public async Task<WorkspaceProjectManifest> CreateOrOpenAsync(string rootPath, string projectName, CancellationToken cancellationToken = default)
    {
        try
        {
            var normalizedRoot = NormalizeRoot(rootPath);
            _logger.Info(
                WorkflowSteps.ProjectManagement,
                "create_or_open_started",
                "开始创建或打开工作台项目目录。",
                new Dictionary<string, string>
                {
                    ["rootPath"] = normalizedRoot,
                    ["projectName"] = projectName
                });

            Directory.CreateDirectory(normalizedRoot);
            var manifestPath = Path.Combine(normalizedRoot, ManifestFileName);
            if (File.Exists(manifestPath))
            {
                var existing = RebaseManifest(await ReadManifestAsync(manifestPath, cancellationToken), normalizedRoot, out var rebased);
                EnsureFolders(existing.Folders);
                if (rebased)
                {
                    existing = Touch(existing);
                    await WriteManifestAsync(existing, cancellationToken);
                }

                _logger.Info(
                    WorkflowSteps.ProjectManagement,
                    "open_completed",
                    "已有工作台项目已打开。",
                    new Dictionary<string, string>
                    {
                        ["projectId"] = existing.ProjectId,
                        ["assetCount"] = existing.Assets.Count.ToString(CultureInfo.InvariantCulture)
                    });
                return existing;
            }

            var now = DateTimeOffset.UtcNow;
            var layout = BuildLayout(normalizedRoot);
            EnsureFolders(layout);
            var name = string.IsNullOrWhiteSpace(projectName) ? new DirectoryInfo(normalizedRoot).Name : projectName.Trim();
            var manifest = new WorkspaceProjectManifest(
                ManifestSchema,
                StableProjectId(normalizedRoot),
                name,
                normalizedRoot,
                Path.Combine(normalizedRoot, DraftFileName),
                Path.Combine(normalizedRoot, OriginalAiJsonFileName),
                layout,
                [],
                now,
                now);
            await WriteManifestAsync(manifest, cancellationToken);
            _logger.Info(
                WorkflowSteps.ProjectManagement,
                "create_completed",
                "工作台项目目录已创建。",
                new Dictionary<string, string>
                {
                    ["projectId"] = manifest.ProjectId,
                    ["rootPath"] = manifest.RootPath
                });
            return manifest;
        }
        catch (Exception ex) when (ExternalBoundary.ShouldWrap(ex))
        {
            _logger.Error(
                WorkflowSteps.ProjectManagement,
                "create_or_open_failed",
                "创建或打开工作台项目失败。",
                new Dictionary<string, string> { ["error"] = ex.Message });
            throw new ExternalIntegrationException("WorkspaceProject", "create_or_open", $"工作台项目目录创建/打开失败：{ex.Message}", ex);
        }
    }

    public async Task<WorkspaceProjectManifest> LoadAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var normalizedRoot = NormalizeRoot(rootPath);
            var manifestPath = Path.Combine(normalizedRoot, ManifestFileName);
            _logger.Info(
                WorkflowSteps.ProjectManagement,
                "load_started",
                "开始读取工作台项目 manifest。",
                new Dictionary<string, string> { ["manifestPath"] = manifestPath });

            if (!File.Exists(manifestPath))
            {
                throw new FileNotFoundException("工作台项目 manifest 不存在。", manifestPath);
            }

            var manifest = await ReadManifestAsync(manifestPath, cancellationToken);
            manifest = RebaseManifest(manifest, normalizedRoot, out var rebased);
            EnsureFolders(manifest.Folders);
            if (rebased)
            {
                manifest = Touch(manifest);
                await WriteManifestAsync(manifest, cancellationToken);
            }

            _logger.Info(
                WorkflowSteps.ProjectManagement,
                "load_completed",
                "工作台项目 manifest 已读取。",
                new Dictionary<string, string>
                {
                    ["projectId"] = manifest.ProjectId,
                    ["assetCount"] = manifest.Assets.Count.ToString(CultureInfo.InvariantCulture)
                });
            return manifest;
        }
        catch (Exception ex) when (ExternalBoundary.ShouldWrap(ex))
        {
            _logger.Error(
                WorkflowSteps.ProjectManagement,
                "load_failed",
                "读取工作台项目失败。",
                new Dictionary<string, string> { ["error"] = ex.Message });
            throw new ExternalIntegrationException("WorkspaceProject", "load", $"工作台项目读取失败：{ex.Message}", ex);
        }
    }

    public async Task<InteractionProject?> LoadDraftAsync(WorkspaceProject workspace, CancellationToken cancellationToken = default)
    {
        try
        {
            var manifest = await LoadAsync(workspace.RootPath, cancellationToken);
            if (!File.Exists(manifest.DraftPath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(manifest.DraftPath, cancellationToken);
            return JsonSerializer.Deserialize<InteractionProject>(json, DraftJson.Options);
        }
        catch (Exception ex) when (ExternalBoundary.ShouldWrap(ex))
        {
            _logger.Error(
                WorkflowSteps.ProjectManagement,
                "draft_load_failed",
                "读取工作台结构失败。",
                new Dictionary<string, string> { ["error"] = ex.Message });
            throw new ExternalIntegrationException("WorkspaceProject", "load_draft", $"工作台项目草稿读取失败：{ex.Message}", ex);
        }
    }

    public async Task<WorkspaceProjectManifest> SaveDraftAsync(WorkspaceProject workspace, InteractionProject project, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.Info(
                WorkflowSteps.ProjectManagement,
                "draft_save_started",
                "开始保存当前工作台结构到项目目录。",
                new Dictionary<string, string>
                {
                    ["projectId"] = workspace.ProjectId,
                    ["draftPath"] = workspace.DraftPath,
                    ["sceneCount"] = project.Scenes.Count.ToString(CultureInfo.InvariantCulture)
                });
            var manifest = await LoadAsync(workspace.RootPath, cancellationToken);
            Directory.CreateDirectory(Path.GetDirectoryName(manifest.DraftPath)!);
            await AtomicFileWriter.WriteUtf8TextAsync(manifest.DraftPath, JsonSerializer.Serialize(project, DraftJson.Options), cancellationToken);
            manifest = Touch(manifest with { DraftPath = manifest.DraftPath });
            await WriteManifestAsync(manifest, cancellationToken);
            _logger.Info(
                WorkflowSteps.ProjectManagement,
                "draft_save_completed",
                "当前工作台结构已保存到项目目录。",
                new Dictionary<string, string> { ["draftPath"] = manifest.DraftPath });
            return manifest;
        }
        catch (Exception ex) when (ExternalBoundary.ShouldWrap(ex))
        {
            _logger.Error(
                WorkflowSteps.ProjectManagement,
                "draft_save_failed",
                "保存工作台结构到项目目录失败。",
                new Dictionary<string, string> { ["error"] = ex.Message });
            throw new ExternalIntegrationException("WorkspaceProject", "save_draft", $"工作台项目保存失败：{ex.Message}", ex);
        }
    }

    public async Task<string?> LoadOriginalAiJsonAsync(WorkspaceProject workspace, CancellationToken cancellationToken = default)
    {
        try
        {
            var manifest = await LoadAsync(workspace.RootPath, cancellationToken);
            if (!File.Exists(manifest.OriginalAiJsonPath))
            {
                return null;
            }

            return await File.ReadAllTextAsync(manifest.OriginalAiJsonPath, cancellationToken);
        }
        catch (Exception ex) when (ExternalBoundary.ShouldWrap(ex))
        {
            _logger.Error(
                WorkflowSteps.ProjectManagement,
                "original_ai_json_load_failed",
                "读取原始 AI JSON 失败。",
                new Dictionary<string, string> { ["error"] = ex.Message });
            throw new ExternalIntegrationException("WorkspaceProject", "load_original_ai_json", $"原始 AI JSON 读取失败：{ex.Message}", ex);
        }
    }

    public async Task<WorkspaceProjectManifest> SaveOriginalAiJsonAsync(WorkspaceProject workspace, string rawJson, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                throw new InvalidOperationException("原始 AI JSON 不能为空。");
            }

            using var _ = JsonDocument.Parse(rawJson);
            var manifest = await LoadAsync(workspace.RootPath, cancellationToken);
            Directory.CreateDirectory(Path.GetDirectoryName(manifest.OriginalAiJsonPath)!);
            await AtomicFileWriter.WriteUtf8TextAsync(manifest.OriginalAiJsonPath, rawJson, cancellationToken);
            manifest = Touch(manifest with { OriginalAiJsonPath = manifest.OriginalAiJsonPath });
            await WriteManifestAsync(manifest, cancellationToken);
            _logger.Info(
                WorkflowSteps.ProjectManagement,
                "original_ai_json_save_completed",
                "原始 AI JSON 已保存到项目目录。",
                new Dictionary<string, string> { ["path"] = manifest.OriginalAiJsonPath });
            return manifest;
        }
        catch (Exception ex) when (ExternalBoundary.ShouldWrap(ex))
        {
            _logger.Error(
                WorkflowSteps.ProjectManagement,
                "original_ai_json_save_failed",
                "保存原始 AI JSON 失败。",
                new Dictionary<string, string> { ["error"] = ex.Message });
            throw new ExternalIntegrationException("WorkspaceProject", "save_original_ai_json", $"原始 AI JSON 保存失败：{ex.Message}", ex);
        }
    }

    public static WorkspaceProject ToWorkspace(WorkspaceProjectManifest manifest)
    {
        return new WorkspaceProject(
            manifest.ProjectId,
            manifest.Name,
            manifest.RootPath,
            Path.Combine(manifest.RootPath, ManifestFileName),
            manifest.DraftPath,
            manifest.OriginalAiJsonPath,
            manifest.Folders,
            manifest.CreatedAt,
            manifest.UpdatedAt);
    }

    internal static ProjectFolderLayout BuildLayout(string rootPath)
    {
        var assets = Path.Combine(rootPath, "assets");
        var images = Path.Combine(assets, "images");
        var audio = Path.Combine(assets, "audio");
        return new ProjectFolderLayout(
            assets,
            images,
            Path.Combine(images, "psd"),
            Path.Combine(images, "imported"),
            audio,
            Path.Combine(audio, "voice-references"),
            Path.Combine(audio, "generated"),
            Path.Combine(rootPath, "timeline"),
            Path.Combine(rootPath, "exports"),
            Path.Combine(rootPath, "logs"));
    }

    internal static async Task<WorkspaceProjectManifest> ReadManifestAsync(string manifestPath, CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
        var manifest = JsonSerializer.Deserialize<WorkspaceProjectManifest>(json, DraftJson.Options)
            ?? throw new InvalidOperationException("工作台项目 manifest 无法解析。");
        return manifest;
    }

    internal static Task WriteManifestAsync(WorkspaceProjectManifest manifest, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(manifest.RootPath);
        return AtomicFileWriter.WriteUtf8TextAsync(
            Path.Combine(manifest.RootPath, ManifestFileName),
            JsonSerializer.Serialize(manifest, DraftJson.Options),
            cancellationToken);
    }

    internal static string NormalizeRoot(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new InvalidOperationException("项目目录不能为空。");
        }

        return Path.GetFullPath(rootPath.Trim());
    }

    internal static void EnsureFolders(ProjectFolderLayout layout)
    {
        foreach (var folder in new[]
        {
            layout.Assets,
            layout.Images,
            layout.ImagesPsd,
            layout.ImagesImported,
            layout.Audio,
            layout.AudioVoiceReferences,
            layout.AudioGenerated,
            layout.Otio,
            layout.Exports,
            layout.Logs
        })
        {
            Directory.CreateDirectory(folder);
        }
    }

    internal static WorkspaceProjectManifest Touch(WorkspaceProjectManifest manifest)
    {
        return manifest with { UpdatedAt = DateTimeOffset.UtcNow };
    }

    internal static WorkspaceProjectManifest RebaseManifest(WorkspaceProjectManifest manifest, string rootPath, out bool rebased)
    {
        var normalizedRoot = NormalizeRoot(rootPath);
        var layout = BuildLayout(normalizedRoot);
        var draftPath = Path.Combine(normalizedRoot, DraftFileName);
        var originalAiJsonPath = Path.Combine(normalizedRoot, OriginalAiJsonFileName);
        var assets = manifest.Assets
            .Select(asset => RebaseAsset(asset, normalizedRoot))
            .ToArray();
        rebased =
            !string.Equals(manifest.RootPath, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(manifest.DraftPath, draftPath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(manifest.OriginalAiJsonPath, originalAiJsonPath, StringComparison.OrdinalIgnoreCase) ||
            !ProjectFolderLayoutEquals(manifest.Folders, layout) ||
            !manifest.Assets.SequenceEqual(assets);
        return manifest with
        {
            RootPath = normalizedRoot,
            DraftPath = draftPath,
            OriginalAiJsonPath = originalAiJsonPath,
            Folders = layout,
            Assets = assets
        };
    }

    private static string StableProjectId(string rootPath)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rootPath.ToUpperInvariant()));
        return $"project_{Convert.ToHexString(hash)[..12].ToLowerInvariant()}";
    }

    private static ProjectMediaAsset RebaseAsset(ProjectMediaAsset asset, string rootPath)
    {
        var relativePath = asset.ProjectRelativePath.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return asset;
        }

        var absolutePath = Path.GetFullPath(Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsInsideRoot(absolutePath, rootPath))
        {
            return asset with { ProjectRelativePath = relativePath };
        }

        return asset with
        {
            AbsolutePath = absolutePath,
            ProjectRelativePath = relativePath
        };
    }

    private static bool IsInsideRoot(string path, string rootPath)
    {
        var normalizedPath = EnsureDirectorySeparator(Path.GetFullPath(Path.GetDirectoryName(path) ?? string.Empty));
        var normalizedRoot = EnsureDirectorySeparator(Path.GetFullPath(rootPath));
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureDirectorySeparator(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    private static bool ProjectFolderLayoutEquals(ProjectFolderLayout left, ProjectFolderLayout right)
    {
        return string.Equals(left.Assets, right.Assets, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.Images, right.Images, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.ImagesPsd, right.ImagesPsd, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.ImagesImported, right.ImagesImported, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.Audio, right.Audio, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.AudioVoiceReferences, right.AudioVoiceReferences, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.AudioGenerated, right.AudioGenerated, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.Otio, right.Otio, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.Exports, right.Exports, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.Logs, right.Logs, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class ProjectMediaAssetService : IProjectMediaAssetService
{
    private readonly IWorkflowLogger _logger;

    public ProjectMediaAssetService(IWorkflowLogger? logger = null)
    {
        _logger = logger ?? new InMemoryWorkflowLogger();
    }

    public async Task<MediaImportResult> ImportAsync(WorkspaceProject workspace, MediaImportRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var sourcePath = Path.GetFullPath(request.SourcePath);
            _logger.Info(
                WorkflowSteps.ProjectAsset,
                "import_started",
                "开始导入项目媒体资产。",
                new Dictionary<string, string>
                {
                    ["sourcePath"] = sourcePath,
                    ["kind"] = request.Kind.ToString(),
                    ["source"] = request.Source.ToString(),
                    ["targetId"] = request.TargetId ?? string.Empty
                });

            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("媒体文件不存在。", sourcePath);
            }

            ValidateImportKind(sourcePath, request.Kind, request.Source);
            var manifest = await WorkspaceProjectManager.ReadManifestAsync(workspace.ManifestPath, cancellationToken);
            manifest = WorkspaceProjectManager.RebaseManifest(manifest, workspace.RootPath, out _);
            WorkspaceProjectManager.EnsureFolders(manifest.Folders);
            if (TryFindExistingReusableAsset(manifest, sourcePath, request) is { } existingAsset)
            {
                await CopyVoiceReferenceTextIfPresentAsync(sourcePath, existingAsset.AbsolutePath, request, cancellationToken);
                _logger.Info(
                    WorkflowSteps.ProjectAsset,
                    "import_reused_existing",
                    "项目媒体资产已存在，复用现有文件。",
                    new Dictionary<string, string>
                    {
                        ["assetId"] = existingAsset.Id,
                        ["relativePath"] = existingAsset.ProjectRelativePath
                    });
                return new MediaImportResult(existingAsset, manifest);
            }

            var destinationDirectory = ResolveDestinationDirectory(manifest.Folders, request.Kind, request.Source);
            Directory.CreateDirectory(destinationDirectory);
            var destinationPath = AllocateDestinationPath(destinationDirectory, sourcePath, request.TargetId);
            await CopyFileAsync(sourcePath, destinationPath, cancellationToken);
            await CopyVoiceReferenceTextIfPresentAsync(sourcePath, destinationPath, request, cancellationToken);
            var asset = CreateAsset(workspace.RootPath, sourcePath, destinationPath, request);
            var updatedAssets = manifest.Assets
                .Where(existing => !string.Equals(existing.AbsolutePath, asset.AbsolutePath, StringComparison.OrdinalIgnoreCase))
                .Append(asset)
                .OrderBy(existing => existing.CreatedAt)
                .ToArray();
            var updatedManifest = WorkspaceProjectManager.Touch(manifest with { Assets = updatedAssets });
            await WorkspaceProjectManager.WriteManifestAsync(updatedManifest, cancellationToken);
            _logger.Info(
                WorkflowSteps.ProjectAsset,
                "import_completed",
                "项目媒体资产导入完成。",
                new Dictionary<string, string>
                {
                    ["assetId"] = asset.Id,
                    ["relativePath"] = asset.ProjectRelativePath,
                    ["assetCount"] = updatedManifest.Assets.Count.ToString(CultureInfo.InvariantCulture)
                });
            return new MediaImportResult(asset, updatedManifest);
        }
        catch (Exception ex) when (ExternalBoundary.ShouldWrap(ex))
        {
            _logger.Error(
                WorkflowSteps.ProjectAsset,
                "import_failed",
                "导入项目媒体资产失败。",
                new Dictionary<string, string> { ["error"] = ex.Message });
            throw new ExternalIntegrationException("ProjectAsset", "import", $"项目媒体资产导入失败：{ex.Message}", ex);
        }
    }

    private static void ValidateImportKind(string sourcePath, MediaAssetKind kind, MediaAssetSource source)
    {
        ValidateImportKindSourcePair(kind, source);
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (kind == MediaAssetKind.Psd || source == MediaAssetSource.ImportedPsd)
        {
            if (extension != ".psd" && extension != ".psb")
            {
                throw new InvalidOperationException("PSD 导入只接受 .psd 或 .psb 文件。");
            }

            return;
        }

        if (kind == MediaAssetKind.Image)
        {
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };
            if (!allowed.Contains(extension))
            {
                throw new InvalidOperationException("图片导入只接受 PNG/JPG/WEBP/BMP 文件；PSD 请走 PSD 导入。");
            }
        }

        if (kind == MediaAssetKind.Audio)
        {
            var allowedAudio = source == MediaAssetSource.TtsGenerated
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".wav", ".flac" }
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".wav", ".flac", ".mp3", ".m4a", ".ogg" };
            if (!allowedAudio.Contains(extension))
            {
                throw new InvalidOperationException(source == MediaAssetSource.TtsGenerated
                    ? "TTS 生成音频导入只接受 WAV/FLAC 文件，其他格式需先补真实时长读取器。"
                    : "参考音频导入只接受 WAV/FLAC/MP3/M4A/OGG 文件。");
            }
        }
    }

    private static void ValidateImportKindSourcePair(MediaAssetKind kind, MediaAssetSource source)
    {
        var isValid = source switch
        {
            MediaAssetSource.ImportedPng => kind == MediaAssetKind.Image,
            MediaAssetSource.ImportedPsd => kind == MediaAssetKind.Psd,
            MediaAssetSource.VoiceReference => kind == MediaAssetKind.Audio,
            MediaAssetSource.TtsGenerated => kind == MediaAssetKind.Audio,
            MediaAssetSource.Manual => true,
            _ => false
        };

        if (!isValid)
        {
            throw new InvalidOperationException($"项目媒体资产 kind/source 不匹配：{kind} / {source}。");
        }
    }

    private static string ResolveDestinationDirectory(ProjectFolderLayout folders, MediaAssetKind kind, MediaAssetSource source)
    {
        return source switch
        {
            MediaAssetSource.ImportedPsd => folders.ImagesPsd,
            MediaAssetSource.ImportedPng => folders.ImagesImported,
            MediaAssetSource.VoiceReference => folders.AudioVoiceReferences,
            MediaAssetSource.TtsGenerated => folders.AudioGenerated,
            _ => kind switch
            {
                MediaAssetKind.Psd => folders.ImagesPsd,
                MediaAssetKind.Image => folders.ImagesImported,
                MediaAssetKind.Audio => folders.Audio,
                MediaAssetKind.Video => folders.Assets,
                _ => folders.Assets
            }
        };
    }

    private static string AllocateDestinationPath(string destinationDirectory, string sourcePath, string? targetId)
    {
        _ = targetId;
        var candidate = Path.Combine(destinationDirectory, MediaFileNamePolicy.PublicFileNameFromSource(sourcePath));
        var extension = Path.GetExtension(candidate);
        var name = Path.GetFileNameWithoutExtension(candidate);
        var index = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(destinationDirectory, $"{name}_{index}{extension}");
            index++;
        }

        return candidate;
    }

    private static ProjectMediaAsset? TryFindExistingReusableAsset(
        WorkspaceProjectManifest manifest,
        string sourcePath,
        MediaImportRequest request)
    {
        if (request.Source != MediaAssetSource.VoiceReference || request.Kind != MediaAssetKind.Audio)
        {
            return null;
        }

        var sourceFullPath = Path.GetFullPath(sourcePath);
        var sourceLength = new FileInfo(sourceFullPath).Length;
        foreach (var asset in manifest.Assets)
        {
            if (asset.Kind != request.Kind || asset.Source != request.Source)
            {
                continue;
            }

            if (string.Equals(Path.GetFullPath(asset.SourcePath ?? string.Empty), sourceFullPath, StringComparison.OrdinalIgnoreCase) ||
                (File.Exists(asset.AbsolutePath) &&
                    Path.GetExtension(asset.AbsolutePath).Equals(Path.GetExtension(sourceFullPath), StringComparison.OrdinalIgnoreCase) &&
                    new FileInfo(asset.AbsolutePath).Length == sourceLength &&
                    FilesHaveSameContent(asset.AbsolutePath, sourceFullPath)))
            {
                return asset;
            }
        }

        return null;
    }

    private static ProjectMediaAsset CreateAsset(string rootPath, string sourcePath, string destinationPath, MediaImportRequest request)
    {
        var relativePath = Path.GetRelativePath(rootPath, destinationPath).Replace('\\', '/');
        var displayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? Path.GetFileName(destinationPath)
            : request.DisplayName.Trim();
        return new ProjectMediaAsset(
            $"asset_{Guid.NewGuid():N}",
            request.Kind,
            request.Source,
            displayName,
            destinationPath,
            relativePath,
            request.TargetId,
            sourcePath,
            null,
            request.Metadata ?? new Dictionary<string, string>(),
            DateTimeOffset.UtcNow);
    }

    private static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken,
        bool overwrite = false)
    {
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var mode = overwrite ? FileMode.Create : FileMode.CreateNew;
        await using var destination = new FileStream(destinationPath, mode, FileAccess.Write, FileShare.None);
        await source.CopyToAsync(destination, cancellationToken);
    }

    private static bool FilesHaveSameContent(string left, string right)
    {
        using var leftStream = File.OpenRead(left);
        using var rightStream = File.OpenRead(right);
        return SHA256.HashData(leftStream).SequenceEqual(SHA256.HashData(rightStream));
    }

    private static async Task CopyVoiceReferenceTextIfPresentAsync(
        string sourcePath,
        string destinationPath,
        MediaImportRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Kind != MediaAssetKind.Audio || request.Source != MediaAssetSource.VoiceReference)
        {
            return;
        }

        var sourceTextPath = Path.ChangeExtension(sourcePath, ".txt");
        if (!File.Exists(sourceTextPath))
        {
            return;
        }

        var destinationTextPath = Path.ChangeExtension(destinationPath, ".txt");
        await CopyFileAsync(sourceTextPath, destinationTextPath, cancellationToken, overwrite: true);
    }
}
