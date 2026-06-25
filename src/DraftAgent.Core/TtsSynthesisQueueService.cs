namespace DraftAgent.Core;

public sealed record TtsQueueOptions(
    int MaxTasksToProcess = int.MaxValue,
    bool ResubmitFailedTasks = false,
    int MaxInFlightPrompts = int.MaxValue);

public sealed record TtsQueueDrainOptions(
    int MaxPasses = 120,
    int DelayMilliseconds = 2000,
    int MaxTasksPerPass = int.MaxValue,
    bool ResubmitFailedTasks = false,
    int MaxInFlightPrompts = int.MaxValue);

public sealed record TtsQueueProgress(GenerationTask Task, InteractionProject Project);

public sealed class TtsSynthesisQueueService
{
    public const string PromptIdParameter = "promptId";
    public const string QueueMessageParameter = "queueMessage";
    public const string QueuePositionParameter = "queuePosition";
    public const string FailureMessageParameter = "failureMessage";
    public const string PromptNotFoundCountParameter = "promptNotFoundCount";

    private const int MaxPromptNotFoundStatusChecks = 2;

    private readonly IComfyUiClient _client;
    private readonly IWorkflowLogger _logger;

    public TtsSynthesisQueueService(IComfyUiClient client, IWorkflowLogger logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<InteractionProject> EnqueueAllAsync(
        InteractionProject project,
        Func<TtsQueueProgress, CancellationToken, Task>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var ttsTasks = project.GenerationTasks.Where(task => task.Type == GenerationTaskType.Tts).ToArray();
        _logger.Info(
            WorkflowSteps.TtsQueue,
            "enqueue_all_started",
            "开始把工作台 TTS 条目加入合成队列。",
            new Dictionary<string, string>
            {
                ["ttsTaskCount"] = ttsTasks.Length.ToString()
            });

        var queued = ttsTasks
            .Select(task => task.Status is GenerationTaskStatus.Completed or GenerationTaskStatus.Running or GenerationTaskStatus.Pending
                ? task
                : MarkQueued(task))
            .ToArray();

        var updatedProject = ReplaceTasks(project, queued);
        _logger.Info(
            WorkflowSteps.TtsQueue,
            "enqueue_all_completed",
            "TTS 条目已加入合成队列。",
            new Dictionary<string, string>
            {
                ["queuedCount"] = queued.Count(task => task.Status == GenerationTaskStatus.Pending).ToString(),
                ["completedCount"] = queued.Count(task => task.Status == GenerationTaskStatus.Completed).ToString()
            });

        return await ContinueAsync(updatedProject, new TtsQueueOptions(), onProgress, cancellationToken);
    }

    public async Task<InteractionProject> EnqueueAllAndDrainAsync(
        InteractionProject project,
        TtsQueueDrainOptions? options = null,
        Func<TtsQueueProgress, CancellationToken, Task>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new TtsQueueDrainOptions();
        var updatedProject = await EnqueueAllAsync(project, onProgress, cancellationToken);
        return await DrainAsync(updatedProject, options, onProgress, cancellationToken);
    }

    public async Task<InteractionProject> ResubmitAsync(
        InteractionProject project,
        string taskId,
        IReadOnlyDictionary<string, string>? parameterOverrides = null,
        Func<TtsQueueProgress, CancellationToken, Task>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.Info(
            WorkflowSteps.TtsQueue,
            "resubmit_started",
            "开始重新提交 TTS 任务。",
            new Dictionary<string, string>
            {
                ["taskId"] = taskId
            });

        var task = project.GenerationTasks.FirstOrDefault(task => task.Id == taskId)
            ?? throw new InvalidOperationException($"找不到 TTS 任务：{taskId}");
        if (task.Type != GenerationTaskType.Tts)
        {
            throw new InvalidOperationException($"任务不是 TTS 类型：{taskId}");
        }

        var parameters = task.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (parameterOverrides is not null)
        {
            foreach (var pair in parameterOverrides)
            {
                parameters[pair.Key] = pair.Value;
            }
        }

        RemoveRuntimeParameters(parameters);
        var resetTask = task with
        {
            Status = GenerationTaskStatus.Pending,
            Parameters = parameters,
            ResultAssetId = null
        };

        var updatedProject = ReplaceTasks(project, [resetTask]);
        _logger.Info(
            WorkflowSteps.TtsQueue,
            "resubmit_completed",
            "TTS 任务已重置并加入队列。",
            new Dictionary<string, string>
            {
                ["taskId"] = taskId,
                ["textLength"] = Parameter(resetTask, "text").Length.ToString(),
                ["emotion"] = Parameter(resetTask, "emotion"),
                ["voiceReferenceId"] = Parameter(resetTask, "voiceReferenceId")
            });

        return await ContinueAsync(updatedProject, new TtsQueueOptions(ResubmitFailedTasks: true), onProgress, cancellationToken);
    }

    public InteractionProject MarkForResubmission(
        InteractionProject project,
        string taskId,
        IReadOnlyDictionary<string, string>? parameterOverrides = null)
    {
        var task = project.GenerationTasks.FirstOrDefault(task => task.Id == taskId)
            ?? throw new InvalidOperationException($"找不到 TTS 任务：{taskId}");
        if (task.Type != GenerationTaskType.Tts)
        {
            throw new InvalidOperationException($"任务不是 TTS 类型：{taskId}");
        }

        var parameters = task.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (parameterOverrides is not null)
        {
            foreach (var pair in parameterOverrides)
            {
                parameters[pair.Key] = pair.Value;
            }
        }

        RemoveRuntimeParameters(parameters);
        var resetTask = task with
        {
            Status = GenerationTaskStatus.Pending,
            Parameters = parameters,
            ResultAssetId = null
        };

        return ReplaceTasks(project, [resetTask]);
    }

    public async Task<InteractionProject> ResubmitAllAndDrainAsync(
        InteractionProject project,
        TtsQueueDrainOptions? options = null,
        Func<TtsQueueProgress, CancellationToken, Task>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new TtsQueueDrainOptions(ResubmitFailedTasks: true);
        var resetTasks = project.GenerationTasks
            .Where(task => task.Type == GenerationTaskType.Tts)
            .Select(ClearRuntimeState)
            .ToArray();

        _logger.Info(
            WorkflowSteps.TtsQueue,
            "resubmit_all_started",
            "开始重新提交全部 TTS 任务。",
            new Dictionary<string, string>
            {
                ["taskCount"] = resetTasks.Length.ToString()
            });

        var updatedProject = ReplaceTasks(project, resetTasks);
        return await DrainAsync(updatedProject, options with { ResubmitFailedTasks = true }, onProgress, cancellationToken);
    }

    public async Task<InteractionProject> ContinueAsync(
        InteractionProject project,
        TtsQueueOptions? options = null,
        Func<TtsQueueProgress, CancellationToken, Task>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new TtsQueueOptions();
        var processedCount = 0;
        var updatedProject = project;
        var maxInFlightPrompts = Math.Max(1, options.MaxInFlightPrompts);
        var inFlightPromptCount = CountInFlightSubmittedTts(updatedProject);
        var submittedNewCount = 0;
        var candidateTasks = updatedProject.GenerationTasks
            .Where(task => task.Type == GenerationTaskType.Tts && IsUnfinished(task, options.ResubmitFailedTasks))
            .OrderBy(task => HasPromptId(task) ? 0 : 1)
            .ThenBy(task => task.Id, StringComparer.Ordinal)
            .ToArray();

        _logger.Info(
            WorkflowSteps.TtsQueue,
            "continue_started",
            "开始继续处理未完成 TTS 队列。",
            new Dictionary<string, string>
            {
                ["candidateCount"] = candidateTasks.Length.ToString(),
                ["maxTasksToProcess"] = options.MaxTasksToProcess.ToString(),
                ["maxInFlightPrompts"] = maxInFlightPrompts.ToString(),
                ["inFlightPromptCount"] = inFlightPromptCount.ToString(),
                ["resubmitFailedTasks"] = options.ResubmitFailedTasks.ToString()
            });

        foreach (var candidate in candidateTasks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentTask = updatedProject.GenerationTasks.First(task => task.Id == candidate.Id);
            var wasInFlight = IsInFlightSubmittedTts(currentTask);
            if (!wasInFlight && submittedNewCount >= options.MaxTasksToProcess)
            {
                continue;
            }

            if (!wasInFlight && inFlightPromptCount >= maxInFlightPrompts)
            {
                continue;
            }

            var updatedTask = await ProcessTaskAsync(currentTask, options, SaveTaskProgressAsync, cancellationToken);
            updatedProject = ReplaceTasks(updatedProject, [updatedTask]);
            var isInFlight = IsInFlightSubmittedTts(updatedTask);
            if (!wasInFlight && isInFlight)
            {
                inFlightPromptCount++;
                submittedNewCount++;
            }
            else if (wasInFlight && !isInFlight)
            {
                inFlightPromptCount = Math.Max(0, inFlightPromptCount - 1);
            }

            processedCount++;
            if (updatedTask.Status == GenerationTaskStatus.Failed)
            {
                _logger.Warning(
                    WorkflowSteps.TtsQueue,
                    "continue_stopped_after_failed_task",
                    "TTS 任务失败，本轮停止补交新任务，等待上层处理失败原因。",
                    new Dictionary<string, string>
                    {
                        ["taskId"] = updatedTask.Id,
                        ["message"] = Parameter(updatedTask, FailureMessageParameter)
                    });
                break;
            }
        }

        _logger.Info(
            WorkflowSteps.TtsQueue,
            "continue_completed",
            "TTS 队列处理完成。",
            new Dictionary<string, string>
            {
                ["processedCount"] = processedCount.ToString(),
                ["pendingCount"] = updatedProject.GenerationTasks.Count(task => task.Type == GenerationTaskType.Tts && task.Status == GenerationTaskStatus.Pending).ToString(),
                ["runningCount"] = updatedProject.GenerationTasks.Count(task => task.Type == GenerationTaskType.Tts && task.Status == GenerationTaskStatus.Running).ToString(),
                ["completedCount"] = updatedProject.GenerationTasks.Count(task => task.Type == GenerationTaskType.Tts && task.Status == GenerationTaskStatus.Completed).ToString(),
                ["failedCount"] = updatedProject.GenerationTasks.Count(task => task.Type == GenerationTaskType.Tts && task.Status == GenerationTaskStatus.Failed).ToString()
            });

        return updatedProject;

        async Task SaveTaskProgressAsync(GenerationTask task, CancellationToken token)
        {
            updatedProject = ReplaceTasks(updatedProject, [task]);
            if (onProgress is not null)
            {
                await onProgress(new TtsQueueProgress(task, updatedProject), token);
            }
        }
    }

    public async Task<InteractionProject> DrainAsync(
        InteractionProject project,
        TtsQueueDrainOptions? options = null,
        Func<TtsQueueProgress, CancellationToken, Task>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new TtsQueueDrainOptions();
        var updatedProject = project;
        var pass = 0;

        _logger.Info(
            WorkflowSteps.TtsQueue,
            "drain_started",
            "开始轮询 TTS 队列直到完成或达到上限。",
            new Dictionary<string, string>
            {
                ["maxPasses"] = options.MaxPasses.ToString(),
                ["delayMilliseconds"] = options.DelayMilliseconds.ToString(),
                ["maxTasksPerPass"] = options.MaxTasksPerPass.ToString()
            });

        while (HasUnfinishedTts(updatedProject, options.ResubmitFailedTasks) && pass < options.MaxPasses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            pass++;
            if (pass > 1 && options.DelayMilliseconds > 0)
            {
                await Task.Delay(options.DelayMilliseconds, cancellationToken);
            }

            updatedProject = await ContinueAsync(
                updatedProject,
                new TtsQueueOptions(options.MaxTasksPerPass, options.ResubmitFailedTasks, options.MaxInFlightPrompts),
                onProgress,
                cancellationToken);
        }

        _logger.Info(
            WorkflowSteps.TtsQueue,
            "drain_completed",
            "TTS 队列轮询结束。",
            new Dictionary<string, string>
            {
                ["passCount"] = pass.ToString(),
                ["pendingCount"] = updatedProject.GenerationTasks.Count(task => task.Type == GenerationTaskType.Tts && task.Status == GenerationTaskStatus.Pending).ToString(),
                ["runningCount"] = updatedProject.GenerationTasks.Count(task => task.Type == GenerationTaskType.Tts && task.Status == GenerationTaskStatus.Running).ToString(),
                ["completedCount"] = updatedProject.GenerationTasks.Count(task => task.Type == GenerationTaskType.Tts && task.Status == GenerationTaskStatus.Completed).ToString(),
                ["failedCount"] = updatedProject.GenerationTasks.Count(task => task.Type == GenerationTaskType.Tts && task.Status == GenerationTaskStatus.Failed).ToString()
            });

        return updatedProject;
    }

    private async Task<GenerationTask> ProcessTaskAsync(
        GenerationTask task,
        TtsQueueOptions options,
        Func<GenerationTask, CancellationToken, Task>? onTaskUpdated,
        CancellationToken cancellationToken)
    {
        if (task.Status == GenerationTaskStatus.Completed)
        {
            return task;
        }

        if (task.Status == GenerationTaskStatus.Failed && !options.ResubmitFailedTasks)
        {
            _logger.Warning(
                WorkflowSteps.TtsQueue,
                "failed_task_skipped",
                "跳过失败任务，等待用户选择重新生成。",
                new Dictionary<string, string>
                {
                    ["taskId"] = task.Id
                });
            return task;
        }

        var promptId = Parameter(task, PromptIdParameter);
        if (string.IsNullOrWhiteSpace(promptId))
        {
            task = await SubmitTaskAsync(task, cancellationToken);
            if (onTaskUpdated is not null)
            {
                await onTaskUpdated(task, cancellationToken);
            }

            if (task.Status == GenerationTaskStatus.Completed || task.Status == GenerationTaskStatus.Failed)
            {
                return task;
            }

            promptId = Parameter(task, PromptIdParameter);
        }

        if (string.IsNullOrWhiteSpace(promptId))
        {
            return task;
        }

        task = await RefreshTaskStatusAsync(task, promptId, cancellationToken);
        if (onTaskUpdated is not null)
        {
            await onTaskUpdated(task, cancellationToken);
        }

        return task;
    }

    private async Task<GenerationTask> SubmitTaskAsync(GenerationTask task, CancellationToken cancellationToken)
    {
        _logger.Info(
            WorkflowSteps.TtsQueue,
            "task_submit_started",
            "提交 TTS 任务到 ComfyUI。",
            new Dictionary<string, string>
            {
                ["taskId"] = task.Id,
                ["targetId"] = task.TargetId,
                ["textLength"] = Parameter(task, "text").Length.ToString(),
                ["emotion"] = Parameter(task, "emotion"),
                ["voiceReferenceId"] = Parameter(task, "voiceReferenceId")
            });

        var submitTask = ClearRuntimeState(task);
        TtsTaskValidation.RequireVoiceReference(submitTask);

        try
        {
            var result = await _client.SubmitAsync(new ComfyTaskRequest(submitTask), cancellationToken);
            var promptId = result.PromptMetadata?.PromptId ?? result.ResultAssetId;
            var parameters = submitTask.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(promptId))
            {
                parameters[PromptIdParameter] = promptId;
            }

            if (result.PromptMetadata?.QueueNumber is { } queueNumber)
            {
                parameters[QueuePositionParameter] = queueNumber.ToString();
            }

            parameters[QueueMessageParameter] = result.PromptMetadata?.Message ?? result.Message ?? string.Empty;
            parameters.Remove(FailureMessageParameter);

            _logger.Info(
                WorkflowSteps.TtsQueue,
                "task_submit_completed",
                "TTS 任务已提交到 ComfyUI。",
                new Dictionary<string, string>
                {
                    ["taskId"] = task.Id,
                    ["promptId"] = promptId ?? string.Empty,
                    ["status"] = result.Status.ToString(),
                    ["message"] = result.Message ?? string.Empty
                });

            return submitTask with
            {
                Status = result.Status,
                Parameters = parameters,
                ResultAssetId = promptId
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(
                WorkflowSteps.TtsQueue,
                "task_submit_failed",
                "TTS 任务提交失败。",
                new Dictionary<string, string>
                {
                    ["taskId"] = task.Id,
                    ["error"] = ex.Message
                });
            return MarkFailed(submitTask, ex.Message);
        }
    }

    private async Task<GenerationTask> RefreshTaskStatusAsync(GenerationTask task, string promptId, CancellationToken cancellationToken)
    {
        if (_client is not IComfyPromptStatusClient statusClient)
        {
            _logger.Warning(
                WorkflowSteps.TtsQueue,
                "status_unsupported",
                "当前 ComfyUI 客户端不支持状态查询，任务保持等待状态。",
                new Dictionary<string, string>
                {
                    ["taskId"] = task.Id,
                    ["promptId"] = promptId
                });
            return task.Status is GenerationTaskStatus.Completed or GenerationTaskStatus.Running ? task : task with { Status = GenerationTaskStatus.Pending };
        }

        _logger.Info(
            WorkflowSteps.TtsQueue,
            "task_status_started",
            "开始查询 TTS ComfyUI prompt 状态。",
            new Dictionary<string, string>
            {
                ["taskId"] = task.Id,
                ["promptId"] = promptId
            });

        try
        {
            var status = await statusClient.GetPromptStatusAsync(promptId, cancellationToken);
            var parameters = task.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            parameters[PromptIdParameter] = promptId;
            parameters[QueueMessageParameter] = status.Message;
            if (status.QueuePosition is { } queuePosition)
            {
                parameters[QueuePositionParameter] = queuePosition.ToString();
            }
            else
            {
                parameters.Remove(QueuePositionParameter);
            }

            if (status.TaskStatus != GenerationTaskStatus.Failed)
            {
                parameters.Remove(FailureMessageParameter);
            }

            if (status.State == ComfyPromptState.NotFound)
            {
                var notFoundCount = ReadIntegerParameter(task, PromptNotFoundCountParameter) + 1;
                parameters[PromptNotFoundCountParameter] = notFoundCount.ToString();
                if (notFoundCount >= MaxPromptNotFoundStatusChecks)
                {
                    parameters.Remove(PromptIdParameter);
                    parameters.Remove(QueuePositionParameter);
                    parameters[QueueMessageParameter] = "ComfyUI prompt 已不在 history/queue 中，已释放队列占用并等待重新提交。";
                    _logger.Warning(
                        WorkflowSteps.TtsQueue,
                        "task_status_prompt_not_found_requeued",
                        "TTS prompt 连续查询不到，释放本地队列占用并等待重新提交。",
                        new Dictionary<string, string>
                        {
                            ["taskId"] = task.Id,
                            ["promptId"] = promptId,
                            ["notFoundCount"] = notFoundCount.ToString()
                        });
                    return task with
                    {
                        Status = GenerationTaskStatus.Pending,
                        Parameters = parameters,
                        ResultAssetId = null
                    };
                }
            }
            else
            {
                parameters.Remove(PromptNotFoundCountParameter);
            }

            var resultAssetId = status.OutputAssetIds.FirstOrDefault()
                ?? (status.TaskStatus == GenerationTaskStatus.Completed ? task.ResultAssetId : promptId);

            _logger.Info(
                WorkflowSteps.TtsQueue,
                "task_status_completed",
                "TTS ComfyUI prompt 状态查询完成。",
                new Dictionary<string, string>
                {
                    ["taskId"] = task.Id,
                    ["promptId"] = promptId,
                    ["state"] = status.State.ToString(),
                    ["taskStatus"] = status.TaskStatus.ToString(),
                    ["outputCount"] = status.OutputAssetIds.Count.ToString(),
                    ["message"] = status.Message
                });

            return task with
            {
                Status = status.TaskStatus,
                Parameters = parameters,
                ResultAssetId = resultAssetId
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(
                WorkflowSteps.TtsQueue,
                "task_status_failed",
                "TTS ComfyUI prompt 状态查询失败。",
                new Dictionary<string, string>
                {
                    ["taskId"] = task.Id,
                    ["promptId"] = promptId,
                    ["error"] = ex.Message
                });
            return MarkFailed(task, ex.Message);
        }
    }

    private static bool IsUnfinished(GenerationTask task, bool includeFailed)
    {
        return task.Status is GenerationTaskStatus.Draft or GenerationTaskStatus.Pending or GenerationTaskStatus.Running ||
            includeFailed && task.Status == GenerationTaskStatus.Failed;
    }

    private static bool HasUnfinishedTts(InteractionProject project, bool includeFailed)
    {
        return project.GenerationTasks.Any(task => task.Type == GenerationTaskType.Tts && IsUnfinished(task, includeFailed));
    }

    private static int CountInFlightSubmittedTts(InteractionProject project)
    {
        return project.GenerationTasks.Count(IsInFlightSubmittedTts);
    }

    private static bool IsInFlightSubmittedTts(GenerationTask task)
    {
        return task.Type == GenerationTaskType.Tts &&
            task.Status is GenerationTaskStatus.Pending or GenerationTaskStatus.Running &&
            HasPromptId(task);
    }

    private static bool HasPromptId(GenerationTask task)
    {
        return task.Parameters.TryGetValue(PromptIdParameter, out var promptId) &&
            !string.IsNullOrWhiteSpace(promptId);
    }

    private static GenerationTask MarkQueued(GenerationTask task)
    {
        return task with
        {
            Status = GenerationTaskStatus.Pending,
            Parameters = WithoutFailure(task.Parameters)
        };
    }

    private static GenerationTask MarkFailed(GenerationTask task, string message)
    {
        var parameters = task.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        parameters[FailureMessageParameter] = message;
        parameters[QueueMessageParameter] = message;
        return task with
        {
            Status = GenerationTaskStatus.Failed,
            Parameters = parameters
        };
    }

    private static GenerationTask ClearRuntimeState(GenerationTask task)
    {
        var parameters = task.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        RemoveRuntimeParameters(parameters);
        return task with
        {
            Status = GenerationTaskStatus.Pending,
            Parameters = parameters,
            ResultAssetId = null
        };
    }

    private static IReadOnlyDictionary<string, string> WithoutFailure(IReadOnlyDictionary<string, string> parameters)
    {
        var copy = parameters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        copy.Remove(FailureMessageParameter);
        return copy;
    }

    private static void RemoveRuntimeParameters(Dictionary<string, string> parameters)
    {
        parameters.Remove(PromptIdParameter);
        parameters.Remove(QueueMessageParameter);
        parameters.Remove(QueuePositionParameter);
        parameters.Remove(FailureMessageParameter);
        parameters.Remove("comfyOutputReference");
        parameters.Remove(PromptNotFoundCountParameter);
    }

    public static InteractionProject ReplaceTasks(InteractionProject project, IReadOnlyList<GenerationTask> updatedTasks)
    {
        if (updatedTasks.Count == 0)
        {
            return project;
        }

        var updatedById = updatedTasks.ToDictionary(task => task.Id, StringComparer.Ordinal);
        var rootTasks = project.GenerationTasks
            .Select(task => updatedById.TryGetValue(task.Id, out var updatedTask) ? updatedTask : task)
            .ToArray();
        var scenes = project.Scenes.Select(scene => scene with
        {
            Interactions = scene.Interactions.Select(interaction => interaction with
            {
                Tasks = interaction.Tasks
                    .Select(task => updatedById.TryGetValue(task.Id, out var updatedTask) ? updatedTask : task)
                    .ToArray()
            }).ToArray()
        }).ToArray();

        return project with
        {
            Scenes = scenes,
            GenerationTasks = rootTasks
        };
    }

    private static string Parameter(GenerationTask task, string key)
    {
        return task.Parameters.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private static int ReadIntegerParameter(GenerationTask task, string key)
    {
        return task.Parameters.TryGetValue(key, out var value) &&
            int.TryParse(value, out var parsed)
                ? parsed
                : 0;
    }
}
