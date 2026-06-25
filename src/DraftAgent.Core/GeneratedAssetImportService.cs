namespace DraftAgent.Core;

public sealed class GeneratedAssetImportService
{
    private readonly IComfyUiClient _comfyClient;
    private readonly IProjectMediaAssetService _assetService;
    private readonly IProjectAssetResolver _assetResolver;
    private readonly IWorkflowLogger _logger;

    public GeneratedAssetImportService(
        IComfyUiClient comfyClient,
        IProjectMediaAssetService assetService,
        IWorkflowLogger logger,
        IProjectAssetResolver? assetResolver = null)
    {
        _comfyClient = comfyClient;
        _assetService = assetService;
        _logger = logger;
        _assetResolver = assetResolver ?? new ProjectAssetResolver();
    }

    public async Task<GeneratedAssetImportResult> ImportCompletedOutputsAsync(
        InteractionProject project,
        WorkspaceProjectManifest manifest,
        CancellationToken cancellationToken = default)
    {
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var updatedProject = project;
        var currentManifest = manifest;
        var importedAssets = new List<ProjectMediaAsset>();
        var completedTasks = project.GenerationTasks
            .Where(task => task.Status == GenerationTaskStatus.Completed && task.Type == GenerationTaskType.Tts)
            .ToArray();

        _logger.Info(
            WorkflowSteps.ProjectAsset,
            "generated_import_started",
            "Importing completed generated assets into workspace project.",
            new Dictionary<string, string>
            {
                ["projectId"] = project.ProjectId,
                ["completedTaskCount"] = completedTasks.Length.ToString(),
                ["workspaceProjectId"] = manifest.ProjectId
            });

        foreach (var task in completedTasks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryGetImportCandidate(task, workspace, currentManifest, out var outputReference))
            {
                continue;
            }

            if (_comfyClient is not IComfyOutputFileClient outputClient)
            {
                _logger.Warning(
                    WorkflowSteps.ProjectAsset,
                    "generated_import_skipped",
                    "Current ComfyUI client cannot download generated outputs.",
                    new Dictionary<string, string>
                    {
                        ["taskId"] = task.Id,
                        ["outputReference"] = outputReference
                    });
                continue;
            }

            var kind = ResolveAssetKind(outputReference, task.Type);
            var source = ResolveAssetSource(task.Type);
            var downloadDirectory = Path.Combine(workspace.Folders.Logs, "comfy-downloads");
            var downloadedPath = await outputClient.DownloadOutputAsync(outputReference, downloadDirectory, cancellationToken);
            var import = await _assetService.ImportAsync(
                workspace,
                new MediaImportRequest(
                    downloadedPath,
                    kind,
                    source,
                    task.TargetId,
                    Path.GetFileName(downloadedPath),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["taskId"] = task.Id,
                        ["taskType"] = task.Type.ToString(),
                        ["comfyOutputReference"] = outputReference
                    }),
                cancellationToken);

            currentManifest = import.Manifest;
            DeleteTemporaryDownload(downloadedPath, import.Asset.AbsolutePath);
            importedAssets.Add(import.Asset);
            updatedProject = TtsSynthesisQueueService.ReplaceTasks(
                updatedProject,
                [RewriteTaskWithImportedAsset(task, import.Asset.Id, outputReference)]);

            _logger.Info(
                WorkflowSteps.ProjectAsset,
                "generated_import_completed",
                "Generated asset imported and task result was rewritten to assetId.",
                new Dictionary<string, string>
                {
                    ["taskId"] = task.Id,
                    ["assetId"] = import.Asset.Id,
                    ["outputReference"] = outputReference
                });
        }

        _logger.Info(
            WorkflowSteps.ProjectAsset,
            "generated_import_finished",
            "Generated asset import pass completed.",
            new Dictionary<string, string>
            {
                ["importedCount"] = importedAssets.Count.ToString(),
                ["assetCount"] = currentManifest.Assets.Count.ToString()
            });

        return new GeneratedAssetImportResult(updatedProject, currentManifest, importedAssets.Count, importedAssets);
    }

    private bool TryGetImportCandidate(
        GenerationTask task,
        WorkspaceProject workspace,
        WorkspaceProjectManifest manifest,
        out string outputReference)
    {
        outputReference = string.Empty;
        if (task.Type != GenerationTaskType.Tts)
        {
            return false;
        }

        if (IsExistingManifestAssetReference(task.ResultAssetId, workspace, manifest))
        {
            return false;
        }

        var reference = TtsGeneratedAudioReference.Resolve(task);

        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        if (IsExistingManifestAssetReference(reference, workspace, manifest))
        {
            return false;
        }

        if (IsMissingManifestAssetReference(reference, workspace, manifest))
        {
            return false;
        }

        if (!TtsGeneratedAudioReference.IsSupportedGeneratedAudioReference(reference))
        {
            _logger.Warning(
                WorkflowSteps.ProjectAsset,
                "generated_import_skipped_unsupported_audio",
                "Completed TTS output is not importable as a generated project audio asset.",
                new Dictionary<string, string>
                {
                    ["taskId"] = task.Id,
                    ["outputReference"] = reference
                });
            return false;
        }

        outputReference = reference;
        return true;
    }

    private bool IsExistingManifestAssetReference(
        string? reference,
        WorkspaceProject workspace,
        WorkspaceProjectManifest manifest)
    {
        return TryFindManifestAsset(reference, manifest, out var asset) &&
            _assetResolver.Resolve(asset.Id, workspace, manifest) is not null;
    }

    private bool IsMissingManifestAssetReference(
        string? reference,
        WorkspaceProject workspace,
        WorkspaceProjectManifest manifest)
    {
        return TryFindManifestAsset(reference, manifest, out var asset) &&
            _assetResolver.Resolve(asset.Id, workspace, manifest) is null;
    }

    private static bool TryFindManifestAsset(
        string? reference,
        WorkspaceProjectManifest manifest,
        out ProjectMediaAsset asset)
    {
        asset = null!;
        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        var trimmed = reference.Trim();
        var found = manifest.Assets.FirstOrDefault(asset =>
            string.Equals(asset.Id, trimmed, StringComparison.Ordinal) ||
            string.Equals(asset.ProjectRelativePath, trimmed.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(asset.AbsolutePath, trimmed, StringComparison.OrdinalIgnoreCase));
        if (found is not null)
        {
            asset = found;
            return true;
        }

        return false;
    }

    private static GenerationTask RewriteTaskWithImportedAsset(GenerationTask task, string assetId, string originalOutputReference)
    {
        var parameters = task.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        parameters["comfyOutputReference"] = originalOutputReference;
        return task with
        {
            ResultAssetId = assetId,
            Parameters = parameters
        };
    }

    private static MediaAssetKind ResolveAssetKind(string reference, GenerationTaskType taskType)
    {
        return taskType switch
        {
            GenerationTaskType.Tts => MediaAssetKind.Audio,
            _ => throw new NotSupportedException("Generated asset import only supports completed TTS tasks.")
        };
    }

    private static MediaAssetSource ResolveAssetSource(GenerationTaskType taskType)
    {
        return taskType switch
        {
            GenerationTaskType.Tts => MediaAssetSource.TtsGenerated,
            _ => throw new NotSupportedException("Generated asset import only supports completed TTS tasks.")
        };
    }

    private static void DeleteTemporaryDownload(string downloadedPath, string importedPath)
    {
        try
        {
            if (!string.Equals(Path.GetFullPath(downloadedPath), Path.GetFullPath(importedPath), StringComparison.OrdinalIgnoreCase) &&
                File.Exists(downloadedPath))
            {
                File.Delete(downloadedPath);
            }
        }
        catch
        {
            // A leftover temp download is harmless; the manifest points at the imported project asset.
        }
    }
}
