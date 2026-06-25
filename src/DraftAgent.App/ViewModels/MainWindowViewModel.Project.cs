using System;
using System.Threading.Tasks;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using DraftAgent.App;
using DraftAgent.Core;

namespace DraftAgent.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public async Task OpenWorkspaceProjectAsync(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        if (IsWorkbenchMutationLocked)
        {
            StatusText = $"{WorkbenchMutationLockReasonText}中已锁定打开工作台项目。请等待完成或取消后再打开。";
            return;
        }

        try
        {
            await _externalActionTransaction.RunAsync(Task.FromResult);
            var manifest = await _workspaceService.CreateOrOpenWorkspaceProjectAsync(folderPath, Project.Title, AttachCurrentScriptDrafts(Project));
            ApplyWorkspaceProjectManifest(manifest, refreshDerivedViews: false);
            var workspaceProject = await _workspaceService.LoadWorkspaceProjectDraftAsync(WorkspaceProjectManager.ToWorkspace(manifest));
            if (workspaceProject is not null)
            {
                Project = workspaceProject;
                ApplyScriptDraftsFromProject(workspaceProject);
                var originalAiJson = await _workspaceService.LoadWorkspaceOriginalAiJsonAsync(WorkspaceProjectManager.ToWorkspace(manifest));
                LlmJsonPreview = string.IsNullOrWhiteSpace(originalAiJson)
                    ? _workspaceService.ExportJson(workspaceProject)
                    : originalAiJson;
                CaptureWorkbenchResetBaseline(workspaceProject, LlmJsonPreview);
                if (string.IsNullOrWhiteSpace(originalAiJson))
                {
                    await CaptureWorkspaceOriginalAiJsonAsync(LlmJsonPreview);
                }

                ComfySubmitStatusText = BuildTtsQueueSummary();
            }
            else
            {
                RefreshWorkspaceProjectDerivedViews();
            }

            var importedProject = await ImportPendingExternalVoiceReferencesAsync(Project);
            if (!ReferenceEquals(importedProject, Project) && importedProject != Project)
            {
                Project = importedProject;
                var originalAiJson = await _workspaceService.LoadWorkspaceOriginalAiJsonAsync(CurrentWorkspaceProject());
                LlmJsonPreview = string.IsNullOrWhiteSpace(originalAiJson)
                    ? _workspaceService.ExportJson(Project)
                    : originalAiJson;
                CaptureWorkbenchResetBaseline(Project, LlmJsonPreview);
                await CaptureWorkspaceOriginalAiJsonIfMissingAsync(LlmJsonPreview);
                ComfySubmitStatusText = BuildTtsQueueSummary();
            }

            StatusText = $"当前项目已打开：{manifest.Name}";
            AppRuntimeLog.Info($"工作台项目已打开：projectId={manifest.ProjectId}; root={manifest.RootPath}; draft={manifest.DraftPath}");
        }
        catch (Exception ex)
        {
            HandleExternalError("打开工作台项目失败", ex);
        }
    }

    [RelayCommand]
    private async Task ExportProjectAsync()
    {
        if (IsWorkbenchMutationLocked)
        {
            StatusText = $"{WorkbenchMutationLockReasonText}中已锁定导出。请等待完成或取消后再导出。";
            return;
        }

        if (_workspaceProjectManifest is null)
        {
            ClearExportPreflightIssues();
            StatusText = "请先指定或新建一个项目文件夹，再导出剪辑项目。";
            return;
        }

        try
        {
            await _externalActionTransaction.RunAsync(Task.FromResult);
            IsProjectExportRunning = true;
            Project = AttachCurrentScriptDrafts(Project);
            var workspace = CurrentWorkspaceProject();
            SetWorkspaceProjectManifest(await _workspaceService.SaveWorkspaceProjectDraftAsync(workspace, Project));
            workspace = CurrentWorkspaceProject();
            var preflightIssues = await _workspaceService.PreflightEditingProjectAsync(
                new EditingProjectExportRequest(
                    Project,
                    workspace,
                    EditingProjectFormat.Fcpxml,
                    new TimelineExportOptions(),
                    _workspaceProjectManifest));
            ApplyExportPreflightIssues(preflightIssues);
            var allowMissingVisuals = preflightIssues.Count == 0;
            if (preflightIssues.Count > 0)
            {
                var blockingIssues = preflightIssues
                    .Where(issue => !IsMissingVisualExportIssue(issue))
                    .ToArray();
                if (blockingIssues.Length > 0)
                {
                    StatusText = $"导出已取消：{blockingIssues.Length} 个语音问题待处理。";
                    return;
                }

                allowMissingVisuals = await ConfirmExportMissingVisualsAsync(new ExportMissingVisualConfirmation(
                    ExportPreflightIssues.ToArray(),
                    $"有 {preflightIssues.Count} 个图片问题，修复后再导出更稳。",
                    issue => LocateExportPreflightIssueCommand.Execute(issue)));
                if (!allowMissingVisuals)
                {
                    StatusText = $"导出已取消：{preflightIssues.Count} 个图片问题待处理。";
                    return;
                }
            }
            else
            {
                ClearExportPreflightIssues();
            }

            var destination = await SelectExportDestinationAsync(new ExportDestinationRequest(
                WorkbenchExportFormat.Fcpxml,
                SanitizeExportName(Project.Title),
                workspace.Folders.Exports));
            if (destination is null || string.IsNullOrWhiteSpace(destination.OutputPath))
            {
                StatusText = "导出已取消：未选择保存路径。";
                return;
            }

            var result = await ExportEditingProjectFileAsync(
                destination.Format.ToEditingProjectFormat(),
                destination.OutputPath,
                allowMissingVisuals);
            if (preflightIssues.Count == 0)
            {
                ClearExportPreflightIssues();
            }

            StatusText = destination.Format switch
            {
                WorkbenchExportFormat.JianyingDraft => $"剪映草稿已导出：{result.OutputPath}",
                WorkbenchExportFormat.AnimateTimeline => $"Animate 音频字幕已导出：{result.OutputPath}",
                _ => $"剪辑项目已导出：{result.OutputPath}"
            };
            AppRuntimeLog.Info($"剪辑项目导出完成：path={result.OutputPath}; format={result.Format}; assets={result.ReferencedAssetPaths.Count}");
        }
        catch (Exception ex)
        {
            HandleExternalError("导出剪辑项目失败", ex);
        }
        finally
        {
            IsProjectExportRunning = false;
        }
    }

    [RelayCommand]
    private async Task ExportJsonAsync()
    {
        try
        {
            await _externalActionTransaction.RunAsync(Task.FromResult);
            Project = AttachCurrentScriptDrafts(Project);
            LlmJsonPreview = _workspaceService.ExportJson(Project);
            if (_workspaceProjectManifest is null)
            {
                ClearExportPreflightIssues();
                StatusText = "已生成测试 JSON。指定项目文件夹后可使用导出按钮选择 FCPXML、剪映草稿或 Animate 音频字幕。";
                return;
            }

            ClearExportPreflightIssues();
            StatusText = "已生成测试 JSON。剪辑项目请使用导出按钮选择 FCPXML、剪映草稿或 Animate 音频字幕。";
        }
        catch (Exception ex)
        {
            HandleExternalError("导出失败", ex);
        }
    }

    private static bool IsMissingVisualExportIssue(EditingTimelinePreflightIssue issue)
    {
        return issue.Kind is EditingTimelinePreflightIssueKind.PsdReference or EditingTimelinePreflightIssueKind.MissingVisual;
    }

    private async Task<TimelineExportResult> ExportEditingProjectFileAsync(
        EditingProjectFormat format = EditingProjectFormat.Fcpxml,
        string? outputPath = null,
        bool allowMissingVisuals = false)
    {
        await _externalActionTransaction.RunAsync(Task.FromResult);
        var workspace = CurrentWorkspaceProject();
        Project = AttachCurrentScriptDrafts(Project);
        SetWorkspaceProjectManifest(await _workspaceService.SaveWorkspaceProjectDraftAsync(workspace, Project));
        workspace = CurrentWorkspaceProject();
        return await _workspaceService.ExportEditingProjectAsync(
            new EditingProjectExportRequest(
                Project,
                workspace,
                format,
                new TimelineExportOptions(AllowMissingVisuals: allowMissingVisuals),
                _workspaceProjectManifest,
                outputPath,
                allowMissingVisuals));
    }

    private async Task LoadLastProjectAsync()
    {
        try
        {
            var lastProject = await _workspaceService.LoadLastProjectAsync();
            var lastWorkspace = await _workspaceService.LoadLastWorkspaceProjectAsync();
            if (lastWorkspace is not null)
            {
                ApplyWorkspaceProjectManifest(lastWorkspace, refreshDerivedViews: lastProject is null);
            }
            if (lastProject is null)
            {
                return;
            }

            Project = lastProject;
            ApplyScriptDraftsFromProject(lastProject);
            var originalAiJson = lastWorkspace is null
                ? null
                : await _workspaceService.LoadWorkspaceOriginalAiJsonAsync(WorkspaceProjectManager.ToWorkspace(lastWorkspace));
            LlmJsonPreview = string.IsNullOrWhiteSpace(originalAiJson)
                ? _workspaceService.ExportJson(lastProject)
                : originalAiJson;
            CaptureWorkbenchResetBaseline(lastProject, LlmJsonPreview);
            await CaptureWorkspaceOriginalAiJsonIfMissingAsync(LlmJsonPreview);
            ComfySubmitStatusText = BuildTtsQueueSummary();
            StatusText = $"已恢复上次项目：{ProjectMeta}。";
        }
        catch (Exception ex)
        {
            HandleExternalError("恢复上次项目失败", ex);
        }
    }

    private void ApplyWorkspaceProjectManifest(
        WorkspaceProjectManifest manifest,
        bool refreshDerivedViews = true)
    {
        SetWorkspaceProjectManifest(manifest);
        WorkspaceProjectName = manifest.Name;
        WorkspaceProjectPath = manifest.RootPath;
        if (refreshDerivedViews)
        {
            RefreshWorkspaceProjectDerivedViews();
        }

        OnPropertyChanged(nameof(WorkspaceProjectMeta));
        OnPropertyChanged(nameof(HasWorkspaceProject));
    }

    private void RefreshWorkspaceProjectDerivedViews()
    {
        RefreshResolvedPreviewAssets();
        WorkbenchBindingProjection.RefreshPreviewAssets(BindingTargets, ResolveProjectAssetPath);
    }

    private void SetWorkspaceProjectManifest(WorkspaceProjectManifest manifest)
    {
        _workspaceProjectManifest = manifest;
        _workspaceProject = WorkspaceProjectManager.ToWorkspace(manifest);
        _projectAssetPathCache.Clear();
    }

    private WorkspaceProject CurrentWorkspaceProject()
    {
        if (_workspaceProjectManifest is null)
        {
            throw new InvalidOperationException("Workspace project manifest is not available.");
        }

        return _workspaceProject ??= WorkspaceProjectManager.ToWorkspace(_workspaceProjectManifest);
    }

    private static string SanitizeExportName(string value)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string((string.IsNullOrWhiteSpace(value) ? "剪辑项目" : value.Trim())
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "剪辑项目" : cleaned;
    }
}
