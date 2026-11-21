using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using DraftAgent.App;
using DraftAgent.Core;

namespace DraftAgent.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private const int DefaultLiveTtsQueueMaxPasses = 120;
    private const int DefaultLiveTtsQueueDelayMilliseconds = 2000;
    private const int DefaultLiveTtsQueueMaxInFlightPrompts = 2;

    private CancellationTokenSource? _ttsSynthesisCancellation;
    private bool _ttsSynthesisCancelStatusHandled;

    internal int LiveTtsQueueMaxPasses { get; set; } = DefaultLiveTtsQueueMaxPasses;

    internal int LiveTtsQueueDelayMilliseconds { get; set; } = DefaultLiveTtsQueueDelayMilliseconds;

    internal int LiveTtsQueueMaxInFlightPrompts { get; set; } = DefaultLiveTtsQueueMaxInFlightPrompts;

    [RelayCommand]
    private async Task SynthesizeAllVoicesAsync()
    {
        await RunTtsSynthesisOperationAsync(async () =>
        {
            try
            {
                var referenceTextSourceByRowId = new Dictionary<string, string>(StringComparer.Ordinal);
                var blockedByMissingVoice = false;
                var stoppedAfterDecliningRegeneration = false;
                await _externalActionTransaction.RunAsync(async project =>
                {
                    project = await EnsureWorkbenchVoiceReferencesSyncedAsync(project, referenceTextSourceByRowId);
                    project = await ApplyTtsSubmissionVoiceRoutesAsync(project, referenceTextSourceByRowId);
                    var pendingRegenerationDecision = ResolveTtsRegenerationDecision(project);
                    if (pendingRegenerationDecision.RequiresConfirmation)
                    {
                        var shouldRegenerate = await ConfirmRegenerateCompletedTtsAsync(pendingRegenerationDecision.Confirmation);
                        if (!shouldRegenerate)
                        {
                            ComfySubmitStatusText = BuildTtsQueueSummary();
                            StatusText = "语音合成已完成，未重新生成。";
                            stoppedAfterDecliningRegeneration = true;
                            return project;
                        }

                        pendingRegenerationDecision = pendingRegenerationDecision with { RegenerateCompleted = shouldRegenerate };
                    }

                    StatusText = pendingRegenerationDecision.RegenerateCompleted
                        ? "正在重新生成已完成语音并继续处理队列..."
                        : "正在继续处理语音合成队列...";

                    if (pendingRegenerationDecision.ResubmitFailed && !pendingRegenerationDecision.RegenerateCompleted)
                    {
                        project = await _externalActionTransaction.PublishAsync(
                            _workspaceService.ResetFailedTtsTasksToDraftAsync(project));
                    }

                    if (!EnsureAllProcessableTtsTasksHaveVoiceBindings(project, pendingRegenerationDecision.ResubmitFailed, pendingRegenerationDecision.RegenerateCompleted) ||
                        !await EnsureComfyConnectionReadyAsync("语音合成"))
                    {
                        blockedByMissingVoice = true;
                        return project;
                    }

                    var currentDecision = ResolveTtsRegenerationDecision(project) with
                    {
                        RegenerateCompleted = pendingRegenerationDecision.RegenerateCompleted,
                        ResubmitFailed = pendingRegenerationDecision.ResubmitFailed
                    };
                    return await ProcessTtsQueueWithLiveImportAsync(project, currentDecision.RegenerateCompleted, currentDecision.ResubmitFailed);
                });
                ApplyVoiceReferenceTextSourcesForRows(referenceTextSourceByRowId);
                if (blockedByMissingVoice || stoppedAfterDecliningRegeneration)
                {
                    return;
                }

                ComfySubmitStatusText = BuildTtsQueueSummary();
                StatusText = ComfySubmitStatusText;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                HandleExternalError("一键合成语音失败", ex);
            }
        });
    }

    [RelayCommand]
    private async Task ContinueTtsQueueAsync()
    {
        await RunTtsSynthesisOperationAsync(async () =>
        {
            try
            {
                StatusText = "正在继续未完成的语音合成队列...";
                var blockedByMissingVoice = false;
                await _externalActionTransaction.RunAsync(async project =>
                {
                    project = await EnsureWorkbenchVoiceReferencesSyncedAsync(project);
                    project = await ApplyTtsSubmissionVoiceRoutesAsync(project);
                    if (!EnsureAllProcessableTtsTasksHaveVoiceBindings(project, includeFailed: false) ||
                        !await EnsureComfyConnectionReadyAsync("继续语音队列"))
                    {
                        blockedByMissingVoice = true;
                        return project;
                    }

                    return await ProcessTtsQueueWithLiveImportAsync(project, regenerateCompleted: false);
                });
                if (blockedByMissingVoice)
                {
                    return;
                }

                ComfySubmitStatusText = BuildTtsQueueSummary();
                StatusText = ComfySubmitStatusText;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                HandleExternalError("继续语音队列失败", ex);
            }
        });
    }

    [RelayCommand]
    private async Task TestComfyConnectionAsync()
    {
        var endpoint = NormalizeEndpoint(ComfyEndpoint);
        ComfyEndpoint = endpoint;
        ComfyConnectionState = "neutral";
        ComfyStatusText = "检测中...";
        OnPropertyChanged(nameof(ComfyEndpointBackground));
        OnPropertyChanged(nameof(ComfyEndpointBorderBrush));

        try
        {
            using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var result = await _workspaceService.CheckComfyConnectionAsync(cancellationTokenSource.Token);
            ComfyConnectionState = result.State == DraftAgent.Core.ComfyConnectionState.Connected ? "success" : "failed";
            ComfyStatusText = result.Message;
        }
        catch (Exception ex)
        {
            ComfyConnectionState = "failed";
            ComfyStatusText = $"ComfyUI 检测失败：{ex.Message}";
        }

        await RefreshVoiceCatalogSafeAsync();
        OnPropertyChanged(nameof(ComfyEndpointBackground));
        OnPropertyChanged(nameof(ComfyEndpointBorderBrush));
    }

    [RelayCommand]
    private async Task PreviewVoiceAsync(BindingTargetViewModel? target)
    {
        if (target is null || !target.SupportsVoice) return;

        if (string.IsNullOrWhiteSpace(target.VoiceAsset) ||
            target.VoiceAsset.StartsWith("未绑定", StringComparison.Ordinal))
        {
            target.StatusText = "未绑定参考音频";
            StatusText = $"{target.DisplayName} 还没有绑定参考音频。";
            return;
        }

        try
        {
            var projectSourcePreviewTarget = !string.IsNullOrWhiteSpace(target.VoiceSourceAsset) &&
                string.IsNullOrWhiteSpace(target.VoiceSyncedPath) &&
                ResolveProjectAssetPath(target.VoiceSourceAsset) is { } projectVoicePath
                    ? projectVoicePath
                    : null;
            var currentBinding = VoiceAssetBinding.FromProjectReference(
                WorkbenchBindingValue.Asset(target.VoiceAsset),
                WorkbenchBindingValue.Text(target.EffectiveVoiceReferenceTextForBinding()),
                WorkbenchBindingValue.Asset(target.VoiceSourceAsset),
                WorkbenchBindingValue.Asset(target.VoiceSyncedPath));
            var synced = await EnsureVoiceBindingSyncedAsync(currentBinding);
            if (synced != currentBinding)
            {
                target.VoiceAsset = synced.EffectiveVoiceReferenceId ?? target.VoiceAsset;
                target.VoiceSourceAsset = synced.SourceAssetId ?? target.VoiceSourceAsset;
                target.VoiceSyncedPath = synced.SyncedVoicePath ?? string.Empty;
                target.ApplyVoiceReferenceText(
                    synced.EffectiveReferenceText ?? target.EffectiveVoiceReferenceTextForBinding(),
                    VoiceReferenceTextSourceFromBinding(synced));
                target.ApplyVoiceBindingSummary(synced);
                ApplyBindingTargetToWorkspace(target);
            }

            var previewTarget = projectSourcePreviewTarget ?? ResolveVoiceReferencePreviewTarget(target.VoiceAsset);
            ActiveAudioTarget = previewTarget;
            IsAudioPlayerVisible = true;
            await _audioPreviewService.PlayAsync(previewTarget);
            target.StatusText = $"试听参考音频：{target.VoiceAsset}";
            StatusText = $"正在内置试听参考音频：{target.DisplayName}";
        }
        catch (Exception ex)
        {
            ClearAudioPreviewState();
            target.StatusText = "参考音频试听失败";
            HandleExternalError("参考音频试听失败", ex);
        }
    }

    [RelayCommand]
    private async Task RegenerateVoiceAsync(BindingTargetViewModel? target)
    {
        if (target is null || !target.SupportsVoice) return;

        await RunTtsSynthesisOperationAsync(async () =>
        {
            try
            {
                var submittedCount = 0;
                await _externalActionTransaction.RunAsync(async project =>
                {
                    project = await EnsureWorkbenchVoiceReferencesSyncedAsync(project);
                    project = await ApplyTtsSubmissionVoiceRoutesAsync(project);
                    var taskIds = TtsTaskIdsForBindingTarget(target);
                    submittedCount = taskIds.Count;
                    if (taskIds.Count == 0)
                    {
                        target.StatusText = "没有可生成的语音行";
                        return project;
                    }

                    if (!EnsureSelectedTtsTasksHaveVoiceBindings(project, taskIds))
                    {
                        submittedCount = 0;
                        return project;
                    }

                    if (!await EnsureComfyConnectionReadyAsync("重新生成语音"))
                    {
                        submittedCount = 0;
                        return project;
                    }

                    var updatedProject = project;
                    foreach (var taskId in taskIds)
                    {
                        updatedProject = await _workspaceService.ResubmitTtsTaskAsync(updatedProject, taskId);
                    }

                    return await _externalActionTransaction.PublishAsync(
                        ImportCompletedGeneratedAssetsIntoWorkspaceAsync(updatedProject));
                });

                if (submittedCount == 0)
                {
                    return;
                }

                target.StatusText = $"已重新提交 {submittedCount} 条";
                ComfySubmitStatusText = BuildTtsQueueSummary();
                StatusText = $"{target.DisplayName} 已重新提交语音合成。";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                HandleExternalError("重新生成语音失败", ex);
            }
        });
    }

    [RelayCommand]
    private async Task PreviewRowVoiceAsync(AnalysisResultRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(row.SynthesizedAudioAsset))
        {
            StatusText = "待合成：该行还没有可试听音频。";
            return;
        }

        try
        {
            var target = ResolveAudioPreviewTarget(row.SynthesizedAudioAsset);
            ActiveAudioTarget = target;
            IsAudioPlayerVisible = true;
            await _audioPreviewService.PlayAsync(target);
            StatusText = $"正在内置试听：{row.SubjectName} / {row.OrderLabel}";
        }
        catch (Exception ex)
        {
            ClearAudioPreviewState();
            HandleExternalError("试听音频失败", ex);
        }
    }

    [RelayCommand]
    private void StopVoicePreview()
    {
        _audioPreviewService.Stop();
        ClearAudioPreviewState();
        StatusText = "试听已停止。";
    }

    private void ClearAudioPreviewState()
    {
        IsAudioPlayerVisible = false;
        ActiveAudioTarget = string.Empty;
    }

    [RelayCommand]
    private async Task RegenerateRowVoiceAsync(AnalysisResultRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        if (IsTtsSynthesisRunning)
        {
            if (row.TtsStatus != GenerationTaskStatus.Completed)
            {
                StatusText = "语音合成中仅允许已完成行插队重新生成。";
                return;
            }

            QueueLiveTtsRowRegeneration(row);
            return;
        }

        await RunTtsSynthesisOperationAsync(async () =>
        {
            try
            {
                StatusText = $"正在重新提交第 {row.OrderLabel} 行语音...";
                var taskId = row.TtsTaskId ?? $"tts_{row.InteractionId}";
                var overrides = TtsWorkbenchPolicy.BuildRegenerateRowOverrides(row);
                var submitted = false;
                await _externalActionTransaction.RunAsync(async project =>
                {
                    project = await EnsureWorkbenchVoiceReferencesSyncedAsync(project);
                    if (!EnsureSelectedTtsTasksHaveVoiceBindings(project, [taskId]) ||
                        !await EnsureComfyConnectionReadyAsync("重新生成语音"))
                    {
                        return project;
                    }

                    var updatedProject = await _externalActionTransaction.PublishAsync(
                        _workspaceService.ResubmitTtsTaskAsync(
                            project,
                            taskId,
                            overrides,
                            _ttsSynthesisCancellation?.Token ?? CancellationToken.None));
                    submitted = true;
                    return await _externalActionTransaction.PublishAsync(
                        ImportCompletedGeneratedAssetsIntoWorkspaceAsync(updatedProject));
                });
                if (!submitted)
                {
                    return;
                }

                ComfySubmitStatusText = BuildTtsQueueSummary();
                StatusText = $"第 {row.OrderLabel} 行语音已重新提交。";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                HandleExternalError("重新生成语音失败", ex);
            }
        });
    }

    private void QueueLiveTtsRowRegeneration(AnalysisResultRowViewModel row)
    {
        RestoreWorkbenchViewModelsFromSessionDuringMutationLockIfNeeded();
        row = SceneTables
            .SelectMany(scene => scene.Rows)
            .FirstOrDefault(candidate => string.Equals(candidate.InteractionId, row.InteractionId, StringComparison.Ordinal)) ?? row;
        var taskId = row.TtsTaskId ?? $"tts_{row.InteractionId}";
        var overrides = TtsWorkbenchPolicy.BuildRegenerateRowOverrides(row);
        lock (_pendingLiveTtsResubmissionsGate)
        {
            _pendingLiveTtsResubmissions[taskId] = overrides;
        }

        ComfySubmitStatusText = BuildTtsQueueSummary();
        StatusText = $"第 {row.OrderLabel} 行已加入当前语音队列，稍后按当前行文本和情绪重新生成。";
    }

    private async Task RunTtsSynthesisOperationAsync(Func<Task> operation)
    {
        if (IsTtsSynthesisRunning)
        {
            return;
        }

        if (!EnsureWorkspaceProjectBeforeTtsSynthesis())
        {
            return;
        }

        SyncWorkbenchSessionFromViewModels();
        IsTtsSynthesisRunning = true;
        _ttsSynthesisCancellation = new CancellationTokenSource();
        _ttsSynthesisCancelStatusHandled = false;
        try
        {
            await operation();
        }
        catch (OperationCanceledException)
        {
            if (!_ttsSynthesisCancelStatusHandled)
            {
                ComfySubmitStatusText = BuildTtsQueueSummary();
                StatusText = "语音合成已取消。";
            }
        }
        finally
        {
            _ttsSynthesisCancellation.Dispose();
            _ttsSynthesisCancellation = null;
            _ttsSynthesisCancelStatusHandled = false;
            IsTtsSynthesisRunning = false;
        }
    }

    private bool EnsureWorkspaceProjectBeforeTtsSynthesis()
    {
        if (_workspaceProjectManifest is not null)
        {
            return true;
        }

        ComfySubmitStatusText = "未提交：请先选择项目保存目录。";
        StatusText = "语音合成前请先点击“打开”选择或新建项目文件夹，保存项目后再开始合成。";
        return false;
    }

    [RelayCommand(CanExecute = nameof(IsTtsSynthesisRunning))]
    private async Task CancelTtsSynthesis()
    {
        StatusText = "正在取消语音合成...";
        _ttsSynthesisCancellation?.Cancel();
        try
        {
            var cancellation = await _workspaceService.CancelSubmittedTtsPromptsAsync(Project, CancellationToken.None);
            Project = cancellation.Project;
            ComfySubmitStatusText = cancellation.Message;
            StatusText = cancellation.RunningPromptCount > 0
                ? $"语音合成已取消本地等待；{cancellation.Message}"
                : $"语音合成已取消；{cancellation.Message}";
            _ttsSynthesisCancelStatusHandled = true;
        }
        catch (Exception ex)
        {
            ComfySubmitStatusText = $"取消合成：ComfyUI 队列取消失败：{ex.Message}";
            StatusText = "语音合成已取消本地等待，但 ComfyUI 远端队列取消失败。";
            _ttsSynthesisCancelStatusHandled = true;
            AppRuntimeLog.Error("取消 ComfyUI 语音队列失败", ex);
        }
    }

    private async Task RefreshVoiceCatalogSafeAsync()
    {
        _projectVoiceAssetIdByOption.Clear();
        _comfyVoiceReferenceTextByOption.Clear();

        IReadOnlyList<string> comfyVoices = [];
        try
        {
            using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var catalog = await _workspaceService.GetComfyVoiceCatalogAsync(cancellationTokenSource.Token);
            foreach (var voice in catalog.Voices)
            {
                if (!string.IsNullOrWhiteSpace(voice.ReferenceText))
                {
                    _comfyVoiceReferenceTextByOption[voice.VoiceName] = voice.ReferenceText.Trim();
                }
            }

            comfyVoices = catalog.Voices
                .Select(voice => voice.VoiceName)
                .ToArray();
        }
        catch (Exception ex)
        {
            AppRuntimeLog.Warning($"ComfyUI 参考音频目录读取失败：{ex.Message}");
        }

        var voices = TtsWorkbenchPolicy.BuildVoiceCatalogOptions(comfyVoices);
        Replace(
            VoiceReferenceOptions,
            AppendCustomVoiceOption(voices.Count == 0 ? [IndexTts2Defaults.DefaultVoiceReferenceId] : voices));
    }

    private void AddVoiceReferenceOption(string voiceReferenceId)
    {
        if (string.IsNullOrWhiteSpace(voiceReferenceId) ||
            string.Equals(voiceReferenceId, CustomVoiceReferenceOption, StringComparison.Ordinal) ||
            VoiceReferenceOptions.Contains(voiceReferenceId, StringComparer.Ordinal))
        {
            return;
        }

        var customIndex = VoiceReferenceOptions.IndexOf(CustomVoiceReferenceOption);
        if (customIndex >= 0)
        {
            VoiceReferenceOptions.Insert(customIndex, voiceReferenceId);
            return;
        }

        VoiceReferenceOptions.Add(voiceReferenceId);
        VoiceReferenceOptions.Add(CustomVoiceReferenceOption);
    }

    private static IReadOnlyList<string> AppendCustomVoiceOption(IReadOnlyList<string> voiceReferenceIds)
    {
        return voiceReferenceIds
            .Where(id => !string.Equals(id, CustomVoiceReferenceOption, StringComparison.Ordinal))
            .Append(CustomVoiceReferenceOption)
            .ToArray();
    }

    private static string ResolveProjectVoiceReferenceOption(ProjectMediaAsset asset)
    {
        if (!string.IsNullOrWhiteSpace(asset.ProjectRelativePath))
        {
            var relative = asset.ProjectRelativePath.Trim().Replace('\\', '/');
            var fileName = Path.GetFileName(relative);
            return string.IsNullOrWhiteSpace(fileName) ? relative : fileName;
        }

        if (!string.IsNullOrWhiteSpace(asset.DisplayName))
        {
            return asset.DisplayName.Trim();
        }

        return !string.IsNullOrWhiteSpace(asset.AbsolutePath)
            ? Path.GetFileName(asset.AbsolutePath.Trim())
            : asset.Id;
    }

    private static string FormatComfySubmitStatus(ComfyPromptMetadata? metadata)
    {
        if (metadata is null)
        {
            return "未提交";
        }

        var state = metadata.State switch
        {
            ComfySubmitState.Submitted => "提交成功",
            ComfySubmitState.MissingPromptId => "缺少 prompt_id",
            ComfySubmitState.Failed => "提交失败",
            _ => "未知"
        };
        return $"{state} / prompt={metadata.PromptId ?? "null"} / queue={metadata.QueueNumber?.ToString() ?? "null"} / {metadata.Message}";
    }

    private static ComfyPromptException? FindComfyPromptException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is ComfyPromptException comfyPromptException)
            {
                return comfyPromptException;
            }
        }

        return null;
    }

    private string BuildTtsQueueSummary()
    {
        var summary = $"语音合成：已完成 {TtsCompletedCount}/{TtsTaskCount}，队列中 {TtsQueuedCount}，失败 {TtsFailedCount}。";
        return TtsFailedCount > 0 && TryGetFirstTtsFailure(Project) is { } failure
            ? $"{summary} 首个失败：{FormatKnownTtsFailure(failure)}"
            : summary;
    }

    private static string? TryGetBlockingTtsFailure(InteractionProject project)
    {
        return project.GenerationTasks
            .Where(task => task.Type == GenerationTaskType.Tts && task.Status == GenerationTaskStatus.Failed)
            .Select(TryReadTtsFailureMessage)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .FirstOrDefault(IsKnownBlockingTtsFailure);
    }

    private static string? TryGetFirstTtsFailure(InteractionProject project)
    {
        return project.GenerationTasks
            .Where(task => task.Type == GenerationTaskType.Tts && task.Status == GenerationTaskStatus.Failed)
            .Select(TryReadTtsFailureMessage)
            .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message));
    }

    private static string? TryReadTtsFailureMessage(GenerationTask task)
    {
        return task.Parameters.TryGetValue(TtsSynthesisQueueService.FailureMessageParameter, out var failureMessage)
            ? failureMessage
            : null;
    }

    private static string? TryFormatKnownTtsFailure(string message)
    {
        return IsKnownBlockingTtsFailure(message) ? FormatKnownTtsFailure(message) : null;
    }

    private static string FormatKnownTtsFailure(string message)
    {
        if (IsIndexTtsMemoryFailure(message))
        {
            return "IndexTTS-2 模型加载失败：系统内存/页面文件不足（ComfyUI 报 not enough memory / 页面文件太小）。请重启 ComfyUI，关闭占内存程序或调大 Windows 虚拟内存后重试；Draft Agent 已改用低内存 TTS 参数。";
        }

        var normalized = string.Join(" ", message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 300 ? normalized : $"{normalized[..300]}...";
    }

    private static bool IsKnownBlockingTtsFailure(string? message)
    {
        return IsIndexTtsMemoryFailure(message);
    }

    private static bool IsIndexTtsMemoryFailure(string? message)
    {
        return !string.IsNullOrWhiteSpace(message) &&
            message.Contains("IndexTTS-2", StringComparison.OrdinalIgnoreCase) &&
            (message.Contains("DefaultCPUAllocator", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("not enough memory", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("页面文件太小", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("os error 1455", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("CUDA out of memory", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("access violation", StringComparison.OrdinalIgnoreCase));
    }

    private TtsRegenerationDecision ResolveTtsRegenerationDecision(InteractionProject project)
    {
        var ttsTasks = project.GenerationTasks.Where(task => task.Type == GenerationTaskType.Tts).ToArray();
        var completedCount = ttsTasks.Count(task => task.Status == GenerationTaskStatus.Completed);
        var unfinishedCount = ttsTasks.Count(task => IsPendingTtsTask(task));
        var failedCount = ttsTasks.Count(task => task.Status == GenerationTaskStatus.Failed);
        var shouldConfirm = completedCount > 0 && unfinishedCount == 0 && failedCount == 0;
        return new TtsRegenerationDecision(
            shouldConfirm,
            false,
            failedCount > 0,
            completedCount > 0,
            unfinishedCount > 0 || failedCount > 0,
            new TtsRegenerationConfirmation(ttsTasks.Length, completedCount, unfinishedCount, failedCount));
    }

    private static bool IsPendingTtsTask(GenerationTask task)
    {
        return task.Status is GenerationTaskStatus.Draft or GenerationTaskStatus.Pending or GenerationTaskStatus.Running;
    }

    private IReadOnlyList<string> TtsTaskIdsForBindingTarget(BindingTargetViewModel target)
    {
        var rows = RowsForBindingTarget(target);
        var taskIds = new List<string>(rows.Count);
        for (var index = 0; index < rows.Count; index++)
        {
            var taskId = rows[index].TtsTaskId;
            if (!string.IsNullOrWhiteSpace(taskId))
            {
                taskIds.Add(taskId);
            }
        }

        return taskIds.Count == 0 ? [] : taskIds;
    }

    private async Task<InteractionProject> ImportCompletedGeneratedAssetsIntoWorkspaceAsync(InteractionProject project)
    {
        var import = await ImportCompletedGeneratedAssetsIntoWorkspaceDetailedAsync(project);
        return import.Project;
    }

    private async Task<GeneratedAssetImportResult> ImportCompletedGeneratedAssetsIntoWorkspaceDetailedAsync(InteractionProject project)
    {
        if (_workspaceProjectManifest is null)
        {
            return new GeneratedAssetImportResult(project, EmptyWorkspaceManifest(project), 0, []);
        }

        var import = await _workspaceService.ImportCompletedGeneratedAssetsAsync(project, _workspaceProjectManifest);
        SetWorkspaceProjectManifest(import.Manifest);
        if (import.ImportedCount > 0)
        {
            SetWorkspaceProjectManifest(await _workspaceService.SaveWorkspaceProjectDraftAsync(
                WorkspaceProjectManager.ToWorkspace(import.Manifest),
                import.Project));
            AppRuntimeLog.Info($"Generated assets imported into project: count={import.ImportedCount}; project={import.Manifest.RootPath}");
        }

        return import;
    }

    private async Task<InteractionProject> ProcessTtsQueueWithLiveImportAsync(
        InteractionProject project,
        bool regenerateCompleted,
        bool resubmitFailedTasks = false)
    {
        var updatedProject = project;
        var pass = 0;
        var cancellationToken = _ttsSynthesisCancellation?.Token ?? CancellationToken.None;
        if (regenerateCompleted)
        {
            updatedProject = await _externalActionTransaction.PublishAsync(
                _workspaceService.ResetAllTtsTasksAsync(updatedProject, cancellationToken));
            resubmitFailedTasks = true;
        }

        while (HasProcessableTtsTask(updatedProject, regenerateCompleted || resubmitFailedTasks) &&
            pass < LiveTtsQueueMaxPasses)
        {
            pass++;
            if (pass > 1 && LiveTtsQueueDelayMilliseconds > 0)
            {
                await Task.Delay(LiveTtsQueueDelayMilliseconds, cancellationToken);
            }

            updatedProject = await ApplyPendingLiveTtsResubmissionsAsync(updatedProject, cancellationToken);

            var beforeCompletedAssetIds = CompletedTtsAssetIds(updatedProject);
            updatedProject = await _externalActionTransaction.PublishAsync(
                _workspaceService.ProcessNextTtsQueueStepAsync(
                    updatedProject,
                    regenerateCompleted || resubmitFailedTasks,
                    LiveTtsQueueMaxInFlightPrompts,
                    cancellationToken));
            var import = await ImportCompletedGeneratedAssetsIntoWorkspaceDetailedAsync(updatedProject);
            updatedProject = await _externalActionTransaction.PublishAsync(Task.FromResult(import.Project));
            NotifyNewlySyncedTtsAudio(import, beforeCompletedAssetIds);
            ComfySubmitStatusText = BuildTtsQueueSummary();
            StatusText = BuildTtsQueueSummary();
            if (TryGetBlockingTtsFailure(updatedProject) is { } blockingFailure)
            {
                var message = FormatKnownTtsFailure(blockingFailure);
                ComfySubmitStatusText = $"{BuildTtsQueueSummary()} 已暂停后续提交。";
                StatusText = $"{message} 已暂停后续语音队列，避免继续向 ComfyUI 提交必然失败的任务。";
                break;
            }
        }

        if (HasProcessableTtsTask(updatedProject, regenerateCompleted || resubmitFailedTasks))
        {
            StatusText = TryGetBlockingTtsFailure(updatedProject) is { } blockingFailure
                ? $"{FormatKnownTtsFailure(blockingFailure)} 已暂停后续语音队列。"
                : $"语音队列已暂停：达到本次最多 {LiveTtsQueueMaxPasses} 轮处理上限，可稍后继续队列。";
        }

        return updatedProject;
    }

    private async Task<InteractionProject> ApplyPendingLiveTtsResubmissionsAsync(
        InteractionProject project,
        CancellationToken cancellationToken)
    {
        KeyValuePair<string, IReadOnlyDictionary<string, string>>[] pending;
        lock (_pendingLiveTtsResubmissionsGate)
        {
            if (_pendingLiveTtsResubmissions.Count == 0)
            {
                return project;
            }

            pending = _pendingLiveTtsResubmissions.ToArray();
            _pendingLiveTtsResubmissions.Clear();
        }

        var committedProject = await CommitWorkbenchAsync();
        var updatedProject = await _externalActionTransaction.PublishAsync(Task.FromResult(committedProject));
        foreach (var pair in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            updatedProject = await _externalActionTransaction.PublishAsync(
                _workspaceService.MarkTtsTaskForResubmissionAsync(
                    updatedProject,
                    pair.Key,
                    pair.Value,
                    cancellationToken));
        }

        StatusText = pending.Length == 1
            ? "已把 1 条重新生成请求插入当前语音队列。"
            : $"已把 {pending.Length} 条重新生成请求插入当前语音队列。";
        return updatedProject;
    }

    private static bool HasProcessableTtsTask(InteractionProject project, bool includeFailed)
    {
        return project.GenerationTasks.Any(task =>
            task.Type == GenerationTaskType.Tts &&
            (task.Status is GenerationTaskStatus.Draft or GenerationTaskStatus.Pending or GenerationTaskStatus.Running ||
                includeFailed && task.Status == GenerationTaskStatus.Failed));
    }

    private bool EnsureAllProcessableTtsTasksHaveVoiceBindings(
        InteractionProject project,
        bool includeFailed,
        bool includeCompleted = false)
    {
        var missingTasks = project.GenerationTasks
            .Where(task => task.Type == GenerationTaskType.Tts &&
                (task.Status is GenerationTaskStatus.Draft or GenerationTaskStatus.Pending or GenerationTaskStatus.Running ||
                    includeFailed && task.Status == GenerationTaskStatus.Failed ||
                    includeCompleted && task.Status == GenerationTaskStatus.Completed))
            .Where(task => !TtsTaskValidation.HasVoiceReference(task))
            .ToArray();
        return ReportMissingVoiceBindings(missingTasks);
    }

    private bool EnsureSelectedTtsTasksHaveVoiceBindings(InteractionProject project, IReadOnlyCollection<string> taskIds)
    {
        if (taskIds.Count == 0)
        {
            return true;
        }

        var selectedIds = taskIds.ToHashSet(StringComparer.Ordinal);
        var missingTasks = project.GenerationTasks
            .Where(task => task.Type == GenerationTaskType.Tts && selectedIds.Contains(task.Id))
            .Where(task => !TtsTaskValidation.HasVoiceReference(task))
            .ToArray();
        return ReportMissingVoiceBindings(missingTasks);
    }

    private bool ReportMissingVoiceBindings(IReadOnlyList<GenerationTask> missingTasks)
    {
        if (missingTasks.Count == 0)
        {
            return true;
        }

        var firstRow = FindWorkbenchRow(missingTasks[0].TargetId);
        var location = firstRow is null ? "工作台行" : $"{firstRow.SubjectName} / {firstRow.OrderLabel}";
        StatusText = missingTasks.Count == 1
            ? $"{location} 未绑定参考音频，请先在声音绑定中选择参考音频。"
            : $"有 {missingTasks.Count} 行未绑定参考音频，请先完成声音绑定。";
        ComfySubmitStatusText = "未提交：缺少参考音频绑定。";
        return false;
    }

    private async Task<bool> EnsureComfyConnectionReadyAsync(string operationName)
    {
        var endpoint = NormalizeEndpoint(ComfyEndpoint);
        ComfyEndpoint = endpoint;
        ComfyConnectionState = "neutral";
        ComfyStatusText = $"{operationName}前检测中...";
        OnPropertyChanged(nameof(ComfyEndpointBackground));
        OnPropertyChanged(nameof(ComfyEndpointBorderBrush));

        try
        {
            using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var result = await _workspaceService.CheckComfyConnectionAsync(cancellationTokenSource.Token);
            var connected = result.State == DraftAgent.Core.ComfyConnectionState.Connected;
            ComfyConnectionState = connected ? "success" : "failed";
            ComfyStatusText = result.Message;
            OnPropertyChanged(nameof(ComfyEndpointBackground));
            OnPropertyChanged(nameof(ComfyEndpointBorderBrush));

            if (connected)
            {
                return true;
            }

            ComfySubmitStatusText = "未提交：ComfyUI 未连接。";
            StatusText = "ComfyUI 未连接，请先在 AI 服务设置中检测并修复 ComfyUI 地址。";
            return false;
        }
        catch (Exception ex)
        {
            ComfyConnectionState = "failed";
            ComfyStatusText = $"ComfyUI 检测失败：{ex.Message}";
            OnPropertyChanged(nameof(ComfyEndpointBackground));
            OnPropertyChanged(nameof(ComfyEndpointBorderBrush));
            ComfySubmitStatusText = "未提交：ComfyUI 检测失败。";
            StatusText = $"ComfyUI 检测失败：{ex.Message}";
            return false;
        }
    }

    private static HashSet<string> CompletedTtsAssetIds(InteractionProject project)
    {
        return project.GenerationTasks
            .Where(task => task.Type == GenerationTaskType.Tts && task.Status == GenerationTaskStatus.Completed)
            .Select(GeneratedTaskAudioReference)
            .Where(asset => !string.IsNullOrWhiteSpace(asset))
            .Select(asset => asset!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private void NotifyNewlySyncedTtsAudio(
        GeneratedAssetImportResult import,
        IReadOnlySet<string> beforeCompletedAssetIds)
    {
        var importedTtsAssets = import.ImportedAssets
            .Where(asset => asset.Source == MediaAssetSource.TtsGenerated && asset.Kind == MediaAssetKind.Audio)
            .ToArray();
        if (importedTtsAssets.Length > 0)
        {
            StatusText = importedTtsAssets.Length == 1
                ? BuildSyncedTtsStatus(importedTtsAssets[0])
                : $"已同步 {importedTtsAssets.Length} 条语音，可点击对应行试听。";
            return;
        }

        var newlyCompletedCount = import.Project.GenerationTasks
            .Where(task => task.Type == GenerationTaskType.Tts && task.Status == GenerationTaskStatus.Completed)
            .Count(task => GeneratedTaskAudioReference(task) is { } asset &&
                !string.IsNullOrWhiteSpace(asset) &&
                !beforeCompletedAssetIds.Contains(asset));
        if (newlyCompletedCount > 0)
        {
            StatusText = newlyCompletedCount == 1
                ? "已同步 1 条语音，可点击对应行试听。"
                : $"已同步 {newlyCompletedCount} 条语音，可点击对应行试听。";
        }
    }

    private string BuildSyncedTtsStatus(ProjectMediaAsset asset)
    {
        if (!string.IsNullOrWhiteSpace(asset.TargetId) && FindWorkbenchRow(asset.TargetId) is { } row)
        {
            return $"已同步语音：{row.SubjectName} / {row.OrderLabel}，可点击试听。";
        }

        return $"已同步语音：{asset.DisplayName}，可点击对应行试听。";
    }

    private AnalysisResultRowViewModel? FindWorkbenchRow(string interactionId)
    {
        if (string.IsNullOrWhiteSpace(interactionId))
        {
            return null;
        }

        return SceneTables
            .SelectMany(scene => scene.Rows)
            .FirstOrDefault(row => string.Equals(row.InteractionId, interactionId, StringComparison.Ordinal));
    }

    private static string? GeneratedTaskAudioReference(GenerationTask task)
    {
        return TtsGeneratedAudioReference.Resolve(task);
    }

    private static WorkspaceProjectManifest EmptyWorkspaceManifest(InteractionProject project)
    {
        var emptyFolders = new ProjectFolderLayout(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        return new WorkspaceProjectManifest(
            WorkspaceProjectManager.ManifestSchema,
            project.ProjectId,
            project.Title,
            string.Empty,
            string.Empty,
            string.Empty,
            emptyFolders,
            [],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
    }

    private string ResolveAudioPreviewTarget(string asset)
    {
        if (ResolveProjectAssetPath(asset) is { } projectAssetPath)
        {
            return projectAssetPath;
        }

        if (_workspaceProjectManifest is not null && IsProjectAssetReference(asset))
        {
            throw new FileNotFoundException("Generated TTS project asset does not exist.", asset);
        }

        return TtsWorkbenchPolicy.ResolvePlayableAudioTarget(asset, ComfyEndpoint);
    }

    private string ResolveVoiceReferencePreviewTarget(string voiceReferenceId)
    {
        if (ResolveProjectAssetPath(voiceReferenceId) is { } projectAssetPath)
        {
            return projectAssetPath;
        }

        if (_workspaceProjectManifest is not null && IsProjectAssetReference(voiceReferenceId))
        {
            throw new FileNotFoundException("Voice reference project asset does not exist.", voiceReferenceId);
        }

        return ResolveComfyInputAudioPreviewTarget(voiceReferenceId);
    }

    private string ResolveComfyInputAudioPreviewTarget(string voiceReferenceId)
    {
        if (!TtsGeneratedAudioReference.IsSupportedAudioReference(voiceReferenceId))
        {
            throw new InvalidOperationException("参考音频不是支持的音频格式。");
        }

        var normalized = voiceReferenceId.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Contains(':', StringComparison.Ordinal) ||
            normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment is "." or ".."))
        {
            throw new InvalidOperationException("参考音频路径不是安全的 ComfyUI 相对路径。");
        }

        var query = $"voice_name={Uri.EscapeDataString(normalized)}";
        return $"{NormalizeEndpoint(ComfyEndpoint)}/api/tts-audio-suite/voice-preview?{query}";
    }

    private static bool IsProjectAssetReference(string reference)
    {
        return reference.StartsWith("asset_", StringComparison.OrdinalIgnoreCase);
    }
}
