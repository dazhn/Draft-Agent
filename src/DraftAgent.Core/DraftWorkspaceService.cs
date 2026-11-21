using System.Text.Json;

namespace DraftAgent.Core;

public sealed record DraftWorkspaceServiceDependencies(
    ScriptInputService? ScriptInputService = null,
    LlmPromptService? PromptService = null,
    LlmAnalysisStep? LlmAnalysisStep = null,
    InteractionProjectNormalizer? Normalizer = null,
    TtsSynthesisQueueService? TtsSynthesisQueueService = null,
    WorkbenchEditingService? WorkbenchEditingService = null,
    WorkbenchProjectApplier? WorkbenchProjectApplier = null,
    IWorkspaceProjectManager? WorkspaceProjectManager = null,
    IProjectMediaAssetService? ProjectMediaAssetService = null,
    EditingProjectExportService? EditingProjectExportService = null,
    GeneratedAssetImportService? GeneratedAssetImportService = null);

public sealed class DraftWorkspaceService
{
    public const string SceneInteractionSchemaName = "scene_interaction_flow_v1";

    private readonly ILlmAnalysisClient _llmAnalysisClient;
    private readonly IComfyUiClient _comfyUiClient;
    private readonly IInteractionProjectStore _store;
    private readonly IWorkflowLogger _logger;
    private readonly ScriptInputService _scriptInputService;
    private readonly LlmPromptService _promptService;
    private readonly LlmAnalysisStep _llmAnalysisStep;
    private readonly InteractionProjectNormalizer _normalizer;
    private readonly TtsSynthesisQueueService _ttsSynthesisQueueService;
    private readonly WorkbenchEditingService _workbenchEditingService;
    private readonly WorkbenchProjectApplier _workbenchProjectApplier;
    private readonly IWorkspaceProjectManager _workspaceProjectManager;
    private readonly IProjectMediaAssetService _projectMediaAssetService;
    private readonly EditingProjectExportService _editingProjectExportService;
    private readonly GeneratedAssetImportService _generatedAssetImportService;
    private readonly IWorkspaceProjectStateStore? _workspaceProjectStateStore;

    public DraftWorkspaceService(
        ILlmAnalysisClient llmAnalysisClient,
        IComfyUiClient comfyUiClient,
        IInteractionProjectStore store,
        IWorkflowLogger? logger = null,
        IWorkspaceProjectStateStore? workspaceProjectStateStore = null,
        DraftWorkspaceServiceDependencies? dependencies = null)
    {
        _llmAnalysisClient = llmAnalysisClient;
        _comfyUiClient = comfyUiClient;
        _store = store;
        _logger = logger ?? new InMemoryWorkflowLogger();
        _workspaceProjectStateStore = workspaceProjectStateStore;
        dependencies ??= new DraftWorkspaceServiceDependencies();
        _scriptInputService = dependencies.ScriptInputService ?? new ScriptInputService(new DraftAnalyzer(), _logger);
        _promptService = dependencies.PromptService ?? new LlmPromptService(_logger);
        _llmAnalysisStep = dependencies.LlmAnalysisStep ?? new LlmAnalysisStep(_llmAnalysisClient, _logger);
        _normalizer = dependencies.Normalizer ?? new InteractionProjectNormalizer(_logger);
        _ttsSynthesisQueueService = dependencies.TtsSynthesisQueueService ?? new TtsSynthesisQueueService(_comfyUiClient, _logger);
        _workbenchEditingService = dependencies.WorkbenchEditingService ?? new WorkbenchEditingService(_logger);
        _workbenchProjectApplier = dependencies.WorkbenchProjectApplier ?? new WorkbenchProjectApplier(_workbenchEditingService);
        _workspaceProjectManager = dependencies.WorkspaceProjectManager ?? new WorkspaceProjectManager(_logger);
        _projectMediaAssetService = dependencies.ProjectMediaAssetService ?? new ProjectMediaAssetService(_logger);
        _editingProjectExportService = dependencies.EditingProjectExportService ?? new EditingProjectExportService(logger: _logger);
        _generatedAssetImportService = dependencies.GeneratedAssetImportService ?? new GeneratedAssetImportService(_comfyUiClient, _projectMediaAssetService, _logger);
    }

    public IReadOnlyList<WorkflowLogEntry> LogEntries => _logger.Entries;

    public async Task<LlmAnalysisResponse> AnalyzeWithLlmAsync(string rawText, CancellationToken cancellationToken = default)
    {
        var script = _scriptInputService.Prepare(rawText);
        var request = _promptService.CreateRequest(script);
        var response = await _llmAnalysisStep.AnalyzeAsync(request, cancellationToken);
        var normalizedResponse = _normalizer.Normalize(response);
        await SaveProjectAsync(normalizedResponse.Project, cancellationToken);
        return normalizedResponse;
    }

    public async Task<AiModelTextResponse> OptimizeNarrationScriptAsync(string rawText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            throw new InvalidOperationException("文案不能为空。");
        }

        if (_llmAnalysisClient is not IAiTextGenerationClient textGenerationClient)
        {
            throw new InvalidOperationException("当前 AI 客户端不支持文案优化。");
        }

        return await textGenerationClient.GenerateTextAsync(
            new AiModelTextRequest(LlmPromptService.BuildNarrationOptimizationInstructions(), rawText.Trim()),
            cancellationToken);
    }

    public async Task<AiConnectionCheckResult> CheckAiConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_llmAnalysisClient is not IAiConnectionClient connectionClient)
        {
            return new AiConnectionCheckResult("Unknown", string.Empty, string.Empty, AiConnectionState.Unknown, null, "当前 AI 客户端不支持连接检测。");
        }

        return await connectionClient.CheckConnectionAsync(cancellationToken);
    }

    public async Task<ComfyConnectionCheckResult> CheckComfyConnectionAsync(CancellationToken cancellationToken = default)
    {
        _logger.Info(
            WorkflowSteps.ComfyConnection,
            "check_started",
            "开始检测 ComfyUI 服务。",
            new Dictionary<string, string>());

        if (_comfyUiClient is not IComfyConnectionClient connectionClient)
        {
            var unsupported = new ComfyConnectionCheckResult(string.Empty, ComfyConnectionState.Unknown, null, "当前 ComfyUI 客户端不支持连接检测。");
            _logger.Warning(
                WorkflowSteps.ComfyConnection,
                "check_unsupported",
                unsupported.Message);
            return unsupported;
        }

        var result = await connectionClient.CheckComfyConnectionAsync(cancellationToken);
        _logger.Info(
            WorkflowSteps.ComfyConnection,
            "check_completed",
            "ComfyUI 服务检测完成。",
            new Dictionary<string, string>
            {
                ["endpoint"] = result.Endpoint,
                ["state"] = result.State.ToString(),
                ["httpStatusCode"] = result.HttpStatusCode?.ToString() ?? string.Empty,
                ["message"] = result.Message
            });
        return result;
    }

    public async Task<ComfyVoiceCatalogResult> GetComfyVoiceCatalogAsync(CancellationToken cancellationToken = default)
    {
        if (_comfyUiClient is not IComfyVoiceCatalogClient catalogClient)
        {
            return new ComfyVoiceCatalogResult([]);
        }

        return await catalogClient.GetVoiceCatalogAsync(cancellationToken);
    }

    public async Task<ComfyPromptStatusResult> GetComfyPromptStatusAsync(string promptId, CancellationToken cancellationToken = default)
    {
        _logger.Info(
            WorkflowSteps.ComfyStatus,
            "status_started",
            "开始查询 ComfyUI prompt 状态。",
            new Dictionary<string, string>
            {
                ["promptId"] = promptId
            });

        if (_comfyUiClient is not IComfyPromptStatusClient statusClient)
        {
            throw new InvalidOperationException("当前 ComfyUI 客户端不支持 prompt 状态查询。");
        }

        var result = await statusClient.GetPromptStatusAsync(promptId, cancellationToken);
        _logger.Info(
            WorkflowSteps.ComfyStatus,
            "status_completed",
            "ComfyUI prompt 状态查询完成。",
            new Dictionary<string, string>
            {
                ["promptId"] = result.PromptId,
                ["state"] = result.State.ToString(),
                ["taskStatus"] = result.TaskStatus.ToString(),
                ["queuePosition"] = result.QueuePosition?.ToString() ?? string.Empty,
                ["progress"] = result.Progress?.ToString("0.##") ?? string.Empty,
                ["outputCount"] = result.OutputAssetIds.Count.ToString(),
                ["message"] = result.Message
            });
        return result;
    }

    public async Task<InteractionProject> EnqueueAndProcessAllTtsAsync(InteractionProject project, CancellationToken cancellationToken = default)
    {
        var updatedProject = await _ttsSynthesisQueueService.EnqueueAllAndDrainAsync(project, new TtsQueueDrainOptions(), SaveTtsQueueProgressAsync, cancellationToken);
        await SaveProjectAsync(updatedProject, cancellationToken);
        return updatedProject;
    }

    public async Task<InteractionProject> ResubmitAndProcessAllTtsAsync(InteractionProject project, CancellationToken cancellationToken = default)
    {
        var updatedProject = await _ttsSynthesisQueueService.ResubmitAllAndDrainAsync(project, new TtsQueueDrainOptions(ResubmitFailedTasks: true), SaveTtsQueueProgressAsync, cancellationToken);
        await SaveProjectAsync(updatedProject, cancellationToken);
        return updatedProject;
    }

    public async Task<InteractionProject> ResetAllTtsTasksAsync(InteractionProject project, CancellationToken cancellationToken = default)
    {
        var resetTasks = project.GenerationTasks
            .Where(task => task.Type == GenerationTaskType.Tts)
            .Select(ResetTtsTask)
            .ToArray();
        var updatedProject = TtsSynthesisQueueService.ReplaceTasks(project, resetTasks);
        await SaveProjectAsync(updatedProject, cancellationToken);
        return updatedProject;
    }

    public async Task<InteractionProject> ResetFailedTtsTasksToDraftAsync(InteractionProject project, CancellationToken cancellationToken = default)
    {
        var resetTasks = project.GenerationTasks
            .Where(task => task.Type == GenerationTaskType.Tts && task.Status == GenerationTaskStatus.Failed)
            .Select(ResetFailedTtsTask)
            .ToArray();
        var updatedProject = TtsSynthesisQueueService.ReplaceTasks(project, resetTasks);
        await SaveProjectAsync(updatedProject, cancellationToken);
        return updatedProject;
    }

    public async Task<InteractionProject> ProcessNextTtsQueueStepAsync(
        InteractionProject project,
        bool resubmitFailedTasks = false,
        int maxInFlightPrompts = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        var updatedProject = await _ttsSynthesisQueueService.ContinueAsync(
            project,
            new TtsQueueOptions(MaxTasksToProcess: Math.Max(1, maxInFlightPrompts), ResubmitFailedTasks: resubmitFailedTasks, MaxInFlightPrompts: maxInFlightPrompts),
            SaveTtsQueueProgressAsync,
            cancellationToken);
        await SaveProjectAsync(updatedProject, cancellationToken);
        return updatedProject;
    }

    public async Task<TtsPromptCancellationResult> CancelSubmittedTtsPromptsAsync(InteractionProject project, CancellationToken cancellationToken = default)
    {
        project = await LoadFreshProjectForTtsCancellationAsync(project, cancellationToken);
        var candidates = project.GenerationTasks
            .Where(task => task.Type == GenerationTaskType.Tts &&
                task.Status is GenerationTaskStatus.Pending or GenerationTaskStatus.Running)
            .Select(task => new TtsPromptCancellationCandidate(task, ResolveTtsPromptId(task)))
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.PromptId))
            .ToArray();

        if (candidates.Length == 0)
        {
            return new TtsPromptCancellationResult(project, 0, 0, 0, 0, "没有已提交到 ComfyUI 的语音队列需要取消。");
        }

        if (_comfyUiClient is not IComfyQueueControlClient queueControlClient)
        {
            return new TtsPromptCancellationResult(project, candidates.Length, 0, 0, 0, "当前 ComfyUI 客户端不支持远端队列取消。");
        }

        var runningPromptIds = new HashSet<string>(StringComparer.Ordinal);
        var completedPromptIds = new HashSet<string>(StringComparer.Ordinal);
        var cancellablePromptIds = new HashSet<string>(StringComparer.Ordinal);
        var statusFailures = new HashSet<string>(StringComparer.Ordinal);
        if (_comfyUiClient is IComfyPromptStatusClient statusClient)
        {
            foreach (var promptId in candidates.Select(candidate => candidate.PromptId!).Distinct(StringComparer.Ordinal))
            {
                try
                {
                    var status = await statusClient.GetPromptStatusAsync(promptId, cancellationToken);
                    if (status.State == ComfyPromptState.Running)
                    {
                        runningPromptIds.Add(promptId);
                    }
                    else if (status.State == ComfyPromptState.Completed)
                    {
                        completedPromptIds.Add(promptId);
                    }
                    else
                    {
                        cancellablePromptIds.Add(promptId);
                    }
                }
                catch (Exception ex) when (ExternalBoundary.ShouldWrap(ex))
                {
                    _logger.Warning(
                        WorkflowSteps.TtsQueue,
                        "cancel_status_unknown",
                        "取消语音合成前查询 ComfyUI prompt 状态失败，将尝试按等待队列删除。",
                        new Dictionary<string, string>
                        {
                            ["promptId"] = promptId,
                            ["error"] = ex.Message
                        });
                    statusFailures.Add(promptId);
                    cancellablePromptIds.Add(promptId);
                }
            }
        }
        else
        {
            foreach (var promptId in candidates.Select(candidate => candidate.PromptId!).Distinct(StringComparer.Ordinal))
            {
                cancellablePromptIds.Add(promptId);
            }
        }

        var deleteRequestedCount = 0;
        if (cancellablePromptIds.Count > 0)
        {
            var deleteResult = await queueControlClient.DeleteQueuedPromptsAsync(cancellablePromptIds.ToArray(), cancellationToken);
            deleteRequestedCount = deleteResult.PromptIds.Count;
        }

        var resetTasks = candidates
            .Where(candidate => candidate.PromptId is not null && cancellablePromptIds.Contains(candidate.PromptId))
            .Select(candidate => MarkTtsPromptCanceled(candidate.Task))
            .ToArray();
        var updatedProject = resetTasks.Length == 0 ? project : TtsSynthesisQueueService.ReplaceTasks(project, resetTasks);
        if (resetTasks.Length > 0)
        {
            await SaveProjectAsync(updatedProject, cancellationToken);
        }

        var message = BuildTtsPromptCancellationMessage(deleteRequestedCount, runningPromptIds.Count, completedPromptIds.Count, statusFailures.Count);
        return new TtsPromptCancellationResult(
            updatedProject,
            candidates.Length,
            deleteRequestedCount,
            runningPromptIds.Count,
            resetTasks.Length,
            message);
    }

    public async Task<InteractionProject> ContinueTtsQueueAsync(InteractionProject project, CancellationToken cancellationToken = default)
    {
        var updatedProject = await _ttsSynthesisQueueService.ContinueAsync(project, new TtsQueueOptions(), SaveTtsQueueProgressAsync, cancellationToken);
        await SaveProjectAsync(updatedProject, cancellationToken);
        return updatedProject;
    }

    public async Task<InteractionProject> ResubmitTtsTaskAsync(InteractionProject project, string taskId, IReadOnlyDictionary<string, string>? parameterOverrides = null, CancellationToken cancellationToken = default)
    {
        var updatedProject = await _ttsSynthesisQueueService.ResubmitAsync(project, taskId, parameterOverrides, SaveTtsQueueProgressAsync, cancellationToken);
        await SaveProjectAsync(updatedProject, cancellationToken);
        return updatedProject;
    }

    public async Task<InteractionProject> MarkTtsTaskForResubmissionAsync(InteractionProject project, string taskId, IReadOnlyDictionary<string, string>? parameterOverrides = null, CancellationToken cancellationToken = default)
    {
        var updatedProject = _ttsSynthesisQueueService.MarkForResubmission(project, taskId, parameterOverrides);
        await SaveProjectAsync(updatedProject, cancellationToken);
        return updatedProject;
    }

    public async Task<InteractionProject> ApplyWorkbenchEditsAsync(
        InteractionProject project,
        IReadOnlyList<WorkbenchSceneEdit> sceneEdits,
        IReadOnlyList<WorkbenchCharacterBindingEdit>? characterBindingEdits = null,
        CancellationToken cancellationToken = default)
    {
        var document = WorkbenchDraftBuilder.FromEdits(sceneEdits, characterBindingEdits);
        return await ApplyWorkbenchDocumentAsync(project, document, cancellationToken);
    }

    public async Task<InteractionProject> ApplyWorkbenchDocumentAsync(
        InteractionProject project,
        WorkbenchDocument document,
        CancellationToken cancellationToken = default)
    {
        var updatedProject = _workbenchProjectApplier.Apply(project, document);
        await SaveProjectAsync(updatedProject, cancellationToken);
        return updatedProject;
    }

    public async Task<InteractionProject> AddCharacterAsync(InteractionProject project, string characterName, CancellationToken cancellationToken = default)
    {
        var updatedProject = _workbenchEditingService.AddCharacter(project, characterName);
        await SaveProjectAsync(updatedProject, cancellationToken);
        return updatedProject;
    }

    public async Task<WorkspaceProjectManifest> CreateOrOpenWorkspaceProjectAsync(string rootPath, string projectName, InteractionProject? currentProject = null, CancellationToken cancellationToken = default)
    {
        var normalizedRoot = WorkspaceProjectManager.NormalizeRoot(rootPath);
        var existingDraftPath = Path.Combine(normalizedRoot, WorkspaceProjectManager.DraftFileName);
        var manifest = await _workspaceProjectManager.CreateOrOpenAsync(rootPath, projectName, cancellationToken);
        if (currentProject is not null && !File.Exists(existingDraftPath))
        {
            manifest = await _workspaceProjectManager.SaveDraftAsync(WorkspaceProjectManager.ToWorkspace(manifest), currentProject, cancellationToken);
        }

        await SaveWorkspaceProjectStateAsync(manifest, cancellationToken);
        return manifest;
    }

    public async Task<InteractionProject?> LoadWorkspaceProjectDraftAsync(WorkspaceProject workspace, CancellationToken cancellationToken = default)
    {
        _logger.Info(
            WorkflowSteps.ProjectStore,
            "workspace_draft_load_started",
            "开始读取工作台项目草稿。",
            new Dictionary<string, string>
            {
                ["projectId"] = workspace.ProjectId,
                ["draftPath"] = workspace.DraftPath
            });

        var project = await _workspaceProjectManager.LoadDraftAsync(workspace, cancellationToken);
        _logger.Info(
            WorkflowSteps.ProjectStore,
            "workspace_draft_load_completed",
            project is null ? "工作台项目草稿不存在。" : "工作台项目草稿读取完成。",
            new Dictionary<string, string>
            {
                ["found"] = (project is not null).ToString(),
                ["projectId"] = project?.ProjectId ?? string.Empty
            });
        return project;
    }

    public async Task<WorkspaceProjectManifest> LoadWorkspaceProjectAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        var manifest = await _workspaceProjectManager.LoadAsync(rootPath, cancellationToken);
        await SaveWorkspaceProjectStateAsync(manifest, cancellationToken);
        return manifest;
    }

    public async Task<WorkspaceProjectManifest> SaveWorkspaceProjectDraftAsync(WorkspaceProject workspace, InteractionProject project, CancellationToken cancellationToken = default)
    {
        return await _workspaceProjectManager.SaveDraftAsync(workspace, project, cancellationToken);
    }

    public async Task<string?> LoadWorkspaceOriginalAiJsonAsync(WorkspaceProject workspace, CancellationToken cancellationToken = default)
    {
        return await _workspaceProjectManager.LoadOriginalAiJsonAsync(workspace, cancellationToken);
    }

    public async Task<WorkspaceProjectManifest> SaveWorkspaceOriginalAiJsonAsync(WorkspaceProject workspace, string rawJson, CancellationToken cancellationToken = default)
    {
        return await _workspaceProjectManager.SaveOriginalAiJsonAsync(workspace, rawJson, cancellationToken);
    }

    public Task SaveProjectSnapshotAsync(InteractionProject project, CancellationToken cancellationToken = default)
    {
        return SaveProjectAsync(project, cancellationToken);
    }

    public async Task<MediaImportResult> ImportProjectMediaAsync(WorkspaceProject workspace, MediaImportRequest request, CancellationToken cancellationToken = default)
    {
        return await _projectMediaAssetService.ImportAsync(workspace, request, cancellationToken);
    }

    public async Task<GeneratedAssetImportResult> ImportCompletedGeneratedAssetsAsync(
        InteractionProject project,
        WorkspaceProjectManifest manifest,
        CancellationToken cancellationToken = default)
    {
        var result = await _generatedAssetImportService.ImportCompletedOutputsAsync(project, manifest, cancellationToken);
        if (result.ImportedCount > 0)
        {
            await SaveProjectAsync(result.Project, cancellationToken);
        }

        return result;
    }

    public Task<TimelineExportResult> ExportEditingProjectAsync(EditingProjectExportRequest request, CancellationToken cancellationToken = default)
    {
        return _editingProjectExportService.ExportAsync(request, cancellationToken);
    }

    public Task<IReadOnlyList<EditingTimelinePreflightIssue>> PreflightEditingProjectAsync(
        EditingProjectExportRequest request,
        CancellationToken cancellationToken = default)
    {
        return _editingProjectExportService.PreflightAsync(request, cancellationToken);
    }

    private Task SaveTtsQueueProgressAsync(TtsQueueProgress progress, CancellationToken cancellationToken)
    {
        _logger.Info(
            WorkflowSteps.ProjectStore,
            "tts_progress_save_started",
            "保存 TTS 队列中间状态。",
            new Dictionary<string, string>
            {
                ["taskId"] = progress.Task.Id,
                ["status"] = progress.Task.Status.ToString(),
                ["resultAssetId"] = progress.Task.ResultAssetId ?? string.Empty
            });

        return _store.SaveAsync(progress.Project, cancellationToken);
    }

    public string ExportJson(InteractionProject project)
    {
        _logger.Info(
            WorkflowSteps.Export,
            "json_export_started",
            "开始导出场景交互流 JSON。",
            new Dictionary<string, string>
            {
                ["sceneCount"] = project.Scenes.Count.ToString(),
                ["taskCount"] = project.GenerationTasks.Count.ToString()
            });

        var json = JsonSerializer.Serialize(project, DraftJson.Options);
        _logger.Info(
            WorkflowSteps.Export,
            "json_export_completed",
            "场景交互流 JSON 导出完成。",
            new Dictionary<string, string>
            {
                ["jsonLength"] = json.Length.ToString()
            });

        return json;
    }

    public string BuildLlmInstructions()
    {
        return _promptService.BuildInstructions();
    }

    public async Task<InteractionProject?> LoadLastProjectAsync(CancellationToken cancellationToken = default)
    {
        _logger.Info(
            WorkflowSteps.ProjectStore,
            "load_started",
            "开始读取上次项目状态。");

        var project = await _store.LoadLastAsync(cancellationToken);
        _logger.Info(
            WorkflowSteps.ProjectStore,
            "load_completed",
            project is null ? "没有找到上次项目状态。" : "上次项目状态读取完成。",
            new Dictionary<string, string>
            {
                ["found"] = (project is not null).ToString(),
                ["projectId"] = project?.ProjectId ?? string.Empty,
                ["taskCount"] = project?.GenerationTasks.Count.ToString() ?? "0"
            });

        return project;
    }

    public async Task<WorkspaceProjectManifest?> LoadLastWorkspaceProjectAsync(CancellationToken cancellationToken = default)
    {
        _logger.Info(
            WorkflowSteps.ProjectStore,
            "workspace_project_state_load_started",
            "Loading last workspace project state.");

        if (_workspaceProjectStateStore is null)
        {
            _logger.Warning(
                WorkflowSteps.ProjectStore,
                "workspace_project_state_store_missing",
                "Workspace project state store is not configured.");
            return null;
        }

        var state = await _workspaceProjectStateStore.LoadAsync(cancellationToken);
        if (state is null || string.IsNullOrWhiteSpace(state.RootPath))
        {
            _logger.Info(
                WorkflowSteps.ProjectStore,
                "workspace_project_state_load_completed",
                "No last workspace project state was found.",
                new Dictionary<string, string> { ["found"] = "False" });
            return null;
        }

        try
        {
            var manifest = await _workspaceProjectManager.LoadAsync(state.RootPath, cancellationToken);
            _logger.Info(
                WorkflowSteps.ProjectStore,
                "workspace_project_state_load_completed",
                "Last workspace project manifest restored.",
                new Dictionary<string, string>
                {
                    ["found"] = "True",
                    ["rootPath"] = manifest.RootPath,
                    ["projectId"] = manifest.ProjectId
                });
            return manifest;
        }
        catch (Exception ex) when (ExternalBoundary.ShouldWrap(ex))
        {
            _logger.Warning(
                WorkflowSteps.ProjectStore,
                "workspace_project_state_load_failed",
                "Last workspace project manifest could not be restored; draft restore will continue.",
                new Dictionary<string, string>
                {
                    ["rootPath"] = state.RootPath,
                    ["error"] = ex.Message
                });
            return null;
        }
    }

    private async Task SaveProjectAsync(InteractionProject project, CancellationToken cancellationToken)
    {
        _logger.Info(
            WorkflowSteps.ProjectStore,
            "save_started",
            "开始保存当前项目状态。",
            new Dictionary<string, string>
            {
                ["projectId"] = project.ProjectId,
                ["sceneCount"] = project.Scenes.Count.ToString()
            });

        await _store.SaveAsync(project, cancellationToken);

        _logger.Info(
            WorkflowSteps.ProjectStore,
            "save_completed",
            "项目状态保存完成。",
            new Dictionary<string, string>
            {
                ["projectId"] = project.ProjectId,
                ["taskCount"] = project.GenerationTasks.Count.ToString()
            });
    }

    private async Task<InteractionProject> LoadFreshProjectForTtsCancellationAsync(InteractionProject project, CancellationToken cancellationToken)
    {
        var storedProject = await _store.LoadLastAsync(cancellationToken);
        if (storedProject is null ||
            !string.Equals(storedProject.ProjectId, project.ProjectId, StringComparison.Ordinal) ||
            storedProject.GenerationTasks.Count == 0)
        {
            return project;
        }

        var currentPromptCount = CountSubmittedTtsPrompts(project);
        var storedPromptCount = CountSubmittedTtsPrompts(storedProject);
        return storedPromptCount > currentPromptCount ? storedProject : project;
    }

    private static int CountSubmittedTtsPrompts(InteractionProject project)
    {
        return project.GenerationTasks.Count(task =>
            task.Type == GenerationTaskType.Tts &&
            task.Status is GenerationTaskStatus.Pending or GenerationTaskStatus.Running &&
            ResolveTtsPromptId(task) is not null);
    }

    private static GenerationTask ResetTtsTask(GenerationTask task)
    {
        var parameters = task.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        parameters.Remove(TtsSynthesisQueueService.PromptIdParameter);
        parameters.Remove(TtsSynthesisQueueService.QueueMessageParameter);
        parameters.Remove(TtsSynthesisQueueService.QueuePositionParameter);
        parameters.Remove(TtsSynthesisQueueService.FailureMessageParameter);
        parameters.Remove("comfyOutputReference");
        return task with
        {
            Status = GenerationTaskStatus.Pending,
            Parameters = parameters,
            ResultAssetId = null
        };
    }

    private static GenerationTask ResetFailedTtsTask(GenerationTask task)
    {
        var parameters = task.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        parameters.Remove(TtsSynthesisQueueService.PromptIdParameter);
        parameters.Remove(TtsSynthesisQueueService.QueueMessageParameter);
        parameters.Remove(TtsSynthesisQueueService.QueuePositionParameter);
        parameters.Remove(TtsSynthesisQueueService.FailureMessageParameter);
        parameters.Remove("comfyOutputReference");
        return task with
        {
            Status = GenerationTaskStatus.Draft,
            Parameters = parameters,
            ResultAssetId = null
        };
    }

    private static string? ResolveTtsPromptId(GenerationTask task)
    {
        if (task.Parameters.TryGetValue(TtsSynthesisQueueService.PromptIdParameter, out var promptId) &&
            !string.IsNullOrWhiteSpace(promptId))
        {
            return promptId.Trim();
        }

        if (task.Status is GenerationTaskStatus.Pending or GenerationTaskStatus.Running &&
            task.ResultAssetId is { } resultAssetId &&
            !string.IsNullOrWhiteSpace(resultAssetId) &&
            !LooksLikeComfyOutputReference(resultAssetId))
        {
            return resultAssetId.Trim();
        }

        return null;
    }

    private static bool LooksLikeComfyOutputReference(string value)
    {
        var parts = value.Split('|', 3);
        return parts.Length == 3 && !string.IsNullOrWhiteSpace(parts[2]);
    }

    private static GenerationTask MarkTtsPromptCanceled(GenerationTask task)
    {
        var parameters = task.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        parameters.Remove(TtsSynthesisQueueService.PromptIdParameter);
        parameters.Remove(TtsSynthesisQueueService.QueuePositionParameter);
        parameters.Remove(TtsSynthesisQueueService.FailureMessageParameter);
        parameters.Remove("comfyOutputReference");
        parameters[TtsSynthesisQueueService.QueueMessageParameter] = "已取消：已请求从 ComfyUI 等待队列删除。";
        return task with
        {
            Status = GenerationTaskStatus.Draft,
            Parameters = parameters,
            ResultAssetId = null
        };
    }

    private static string BuildTtsPromptCancellationMessage(
        int deleteRequestedCount,
        int runningCount,
        int completedCount,
        int statusFailureCount)
    {
        var parts = new List<string>
        {
            deleteRequestedCount > 0
                ? $"已请求取消 ComfyUI 等待队列 {deleteRequestedCount} 个 prompt"
                : "没有可从 ComfyUI 等待队列删除的 prompt"
        };
        if (runningCount > 0)
        {
            parts.Add($"{runningCount} 个 prompt 已在运行，普通取消不会强制中断");
        }

        if (completedCount > 0)
        {
            parts.Add($"{completedCount} 个 prompt 已完成");
        }

        if (statusFailureCount > 0)
        {
            parts.Add($"{statusFailureCount} 个 prompt 状态未知，已尝试删除等待队列项");
        }

        return string.Join("；", parts) + "。";
    }

    private sealed record TtsPromptCancellationCandidate(GenerationTask Task, string? PromptId);

    private async Task SaveWorkspaceProjectStateAsync(WorkspaceProjectManifest manifest, CancellationToken cancellationToken)
    {
        if (_workspaceProjectStateStore is null)
        {
            return;
        }

        _logger.Info(
            WorkflowSteps.ProjectStore,
            "workspace_project_state_save_started",
            "Saving current workspace project state.",
            new Dictionary<string, string>
            {
                ["projectId"] = manifest.ProjectId,
                ["rootPath"] = manifest.RootPath
            });

        await _workspaceProjectStateStore.SaveAsync(new WorkspaceProjectState(manifest.RootPath), cancellationToken);

        _logger.Info(
            WorkflowSteps.ProjectStore,
            "workspace_project_state_save_completed",
            "Current workspace project state saved.",
            new Dictionary<string, string>
            {
                ["projectId"] = manifest.ProjectId,
                ["rootPath"] = manifest.RootPath
            });
    }
}
