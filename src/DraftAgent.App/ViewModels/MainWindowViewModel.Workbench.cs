using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using DraftAgent.Core;

namespace DraftAgent.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly WorkbenchDraftService _workbenchDraftService = new();
    private readonly WorkbenchDragController _workbenchDragController = new();
    private readonly WorkbenchProjectionGuard _workbenchProjectionGuard = new();
    private readonly WorkbenchDisplayProjectionSynchronizer _workbenchDisplayProjection;
    private readonly HashSet<SceneTableViewModel> _subscribedWorkbenchScenes = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<AnalysisResultRowViewModel> _subscribedWorkbenchRows = new(ReferenceEqualityComparer.Instance);
    private WorkbenchDerivedStateSnapshot? _derivedStateSnapshot;
    private WorkbenchSessionNotificationSnapshot? _workbenchSessionNotificationSnapshot;
    private WorkbenchDocumentSyncMetricsSnapshot _workbenchDocumentSyncMetrics;
    private WorkbenchBindingRefreshMetricsSnapshot _workbenchBindingRefreshMetrics;

    [RelayCommand]
    private void LoadSample()
    {
        SetOriginalScriptDraftText(DraftAnalyzer.SampleScript);
        StatusText = "示例文案已载入，可重新分析生成表格。";
    }

    [RelayCommand]
    private void ClearScript()
    {
        SetOriginalScriptDraftText(string.Empty);
        StatusText = "文案已清空。";
    }

    private void SetOriginalScriptDraftText(string text)
    {
        var originalDraft = ScriptDrafts.FirstOrDefault(draft => !draft.CanClose)
            ?? ScriptDrafts.FirstOrDefault();
        if (originalDraft is null)
        {
            return;
        }

        SelectedScriptDraft = originalDraft;
        originalDraft.Text = text;
    }

    [RelayCommand]
    private async Task SaveWorkbenchEditsAsync()
    {
        if (IsWorkbenchMutationLocked)
        {
            RestoreWorkbenchViewModelsFromSessionDuringMutationLockIfNeeded();
            StatusText = $"{WorkbenchMutationLockReasonText}正在运行。请等待完成或取消后再保存。";
            return;
        }

        try
        {
            ApplyPendingBindingTargetEditsBeforeSave();
            await _externalActionTransaction.RunAsync(async committedProject =>
            {
                await PersistCurrentProjectSnapshotAsync(committedProject);
                return committedProject;
            });
            LlmJsonPreview = _workspaceService.ExportJson(Project);
            StatusText = _workspaceProjectManifest is null
                ? "工作台调整已保存到本机最近项目快照。若要保存到项目文件夹，请先点击“打开”选择项目文件夹。"
                : $"工作台调整已保存到项目：{_workspaceProjectManifest.Name}";
        }
        catch (Exception ex)
        {
            _workbenchSession.Restore(_workbenchSession.Document, isDirty: true);
            NotifyWorkbenchSessionStateChanged();
            HandleExternalError("保存工作台调整失败", ex);
        }
    }

    private void ApplyPendingBindingTargetEditsBeforeSave()
    {
        BindingTargetRowIndex? rowIndex = null;
        foreach (var target in BindingTargets.ToArray())
        {
            if (!target.HasAnyBindingEdit)
            {
                continue;
            }

            rowIndex ??= BindingTargetRowIndex.FromScenes(SceneTables);
            ApplyBindingTargetToWorkspace(target, rowIndex);
        }
    }

    [RelayCommand]
    private async Task RevertWorkbenchAsync()
    {
        if (IsWorkbenchMutationLocked)
        {
            StatusText = $"{WorkbenchMutationLockReasonText}中已锁定重置工作台。请等待完成或取消后再重置。";
            return;
        }

        if (!await ConfirmResetWorkbenchAsync())
        {
            StatusText = "已取消重置工作台。";
            return;
        }

        var previousInteractionId = SelectedRow?.InteractionId;
        Project = await RestoreWorkbenchResetBaselineAsync();
        RestoreWorkbenchSelection(previousInteractionId);
        LlmJsonPreview = _workbenchResetBaselineJson;
        StatusText = "已从原始 AI JSON 重置工作台。";
    }

    [RelayCommand]
    private void ExpandAllWorkbenchScenes()
    {
        SetAllWorkbenchScenesExpanded(true);
    }

    [RelayCommand]
    private void CollapseAllWorkbenchScenes()
    {
        SetAllWorkbenchScenesExpanded(false);
    }

    private void SetAllWorkbenchScenesExpanded(bool isExpanded)
    {
        using var deferDisplayProjectionSync = _workbenchDisplayProjection.DeferSync();
        foreach (var scene in SceneTables)
        {
            if (scene.IsExpanded == isExpanded)
            {
                continue;
            }

            scene.IsExpanded = isExpanded;
        }

        StatusText = isExpanded
            ? "已展开全部工作台场景。"
            : "已折叠全部工作台场景。";
    }

    [RelayCommand]
    private void ToggleSelectedRowSourceType(AnalysisResultRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        if (IsWorkbenchMutationLocked)
        {
            RestoreWorkbenchViewModelsFromSessionDuringMutationLockIfNeeded();
            StatusText = $"{WorkbenchMutationLockReasonText}正在运行。请等待完成或取消后再切换行属性。";
            return;
        }

        if (!row.UsesCharacterVisualPreview)
        {
            StatusText = $"第 {row.OrderLabel} 行不是角色行，不能切换词/念/动属性。";
            return;
        }

        var nextSourceType = NextCharacterSourceItemType(row.SourceItemType);
        var result = _workbenchDraftService.UpdateRowSourceTypeWithResult(
            SceneTables,
            _workbenchSession,
            row,
            UpdateRowSourceTypeCommand.FromRow(row, nextSourceType, Project.Characters));
        ApplyBindingTargetRefresh(result);
        ApplyDerivedStateRefresh(result);
        StatusText = $"第 {row.OrderLabel} 行已切换为{row.SourceTypeText}。";
    }

    private static string NextCharacterSourceItemType(string? sourceItemType)
    {
        return sourceItemType?.Trim().ToLowerInvariant() switch
        {
            "dialogue" => "thought",
            "thought" => "action",
            "action" => "dialogue",
            _ => "dialogue"
        };
    }

    [RelayCommand(CanExecute = nameof(CanUndoWorkbench))]
    private void UndoWorkbench()
    {
        var previousInteractionId = SelectedRow?.InteractionId;
        if (!_workbenchSession.Undo())
        {
            return;
        }

        ApplyWorkbenchSessionDocument(previousInteractionId, "workbench_undo");
        StatusText = "已撤销上一步工作台改动。";
    }

    [RelayCommand(CanExecute = nameof(CanRedoWorkbench))]
    private void RedoWorkbench()
    {
        var previousInteractionId = SelectedRow?.InteractionId;
        if (!_workbenchSession.Redo())
        {
            return;
        }

        ApplyWorkbenchSessionDocument(previousInteractionId, "workbench_redo");
        StatusText = "已重做工作台改动。";
    }

    [RelayCommand]
    private void AddManagedCharacter()
    {
        try
        {
            if (!CanEditWorkbenchStructure)
            {
                StatusText = "语音合成中已锁定角色管理。请等待合成完成，或停止合成后再修改角色。";
                return;
            }

            var name = BuildDefaultManagedCharacterName();
            if (Project.Characters.Any(character => string.Equals(character.Name, name, StringComparison.Ordinal)))
            {
                StatusText = $"角色已存在：{name}。";
                return;
            }

            var character = CreateManagedCharacter(name, Project.Characters);
            Project = Project with
            {
                Characters = Project.Characters
                    .Concat([character])
                    .ToArray()
            };
            Replace(Characters, Project.Characters);
            RefreshWorkbenchCharacterProjection();
            RefreshBindingTargets("add_managed_character");
            var target = BindingTargets.FirstOrDefault(candidate =>
                candidate.Kind == BindingTargetKind.Character &&
                string.Equals(candidate.TargetId, character.Id, StringComparison.Ordinal));
            if (target is not null)
            {
                SelectedBindingTarget = target;
                target.BeginDisplayNameEdit();
            }

            StatusText = $"已添加角色：{name}。";
        }
        catch (Exception ex)
        {
            HandleExternalError("添加角色失败", ex);
        }
    }

    private string BuildDefaultManagedCharacterName()
    {
        const string prefix = "新角色";
        var existingNames = Project.Characters
            .Select(character => character.Name)
            .Concat(CharacterNameOptions)
            .ToHashSet(StringComparer.Ordinal);

        for (var index = 1; index < int.MaxValue; index++)
        {
            var name = $"{prefix} {index}";
            if (!existingNames.Contains(name))
            {
                return name;
            }
        }

        return $"{prefix} {Guid.NewGuid():N}";
    }

    private static Character CreateManagedCharacter(string name, IReadOnlyCollection<Character> existingCharacters)
    {
        return new Character(
            NextManagedCharacterId(existingCharacters.Select(character => character.Id)),
            name,
            existingCharacters.Count == 0 ? "主角" : "角色",
            "待绑定参考形象",
            "待选择音色",
            "克制",
            new AssetBinding());
    }

    private static string NextManagedCharacterId(IEnumerable<string> existingIds)
    {
        var existing = existingIds.ToHashSet(StringComparer.Ordinal);
        for (var index = 1; index < 100_000; index++)
        {
            var id = $"char_manual_{index:000}";
            if (!existing.Contains(id))
            {
                return id;
            }
        }

        return $"char_manual_{Guid.NewGuid():N}";
    }

    [RelayCommand]
    private void AddScene()
    {
        if (!CanEditWorkbenchStructure)
        {
            StatusText = "语音合成中已锁定工作台结构编辑。";
            return;
        }

        WorkbenchSceneMutationResult result;
        using (BeginWorkbenchDocumentApply())
        {
            result = _workbenchDraftService.AddSceneWithResult(SceneTables, _workbenchSession);
        }

        var scene = result.Scene;
        if (!result.CommandResult.Applied || scene is null)
        {
            return;
        }

        AttachWorkbenchSceneSyncHandler(scene);
        ApplyBindingTargetRefresh(result);
        ApplyDerivedStateRefresh(result);
        StatusText = $"{scene.SceneLabel} 已添加，可继续添加台词或旁白。";
    }

    [RelayCommand]
    private void AddDialogueRow(SceneTableViewModel? scene)
    {
        if (!CanEditWorkbenchStructure)
        {
            StatusText = "语音合成中已锁定新增台词行。";
            return;
        }

        if (scene is null)
        {
            return;
        }

        WorkbenchRowMutationResult result;
        using (BeginWorkbenchDocumentApply())
        {
            result = _workbenchDraftService.AddDialogueRowWithResult(
                SceneTables,
                _workbenchSession,
                scene,
                SelectedRow,
                DefaultCharacterName());
        }

        var row = result.Row;
        if (!result.CommandResult.Applied || row is null)
        {
            return;
        }

        AttachWorkbenchRowSyncHandler(row);
        ApplySuggestedSelectedRow(result);
        ApplyNewDialogueRowDefaults(row);
        ApplyBindingTargetRefresh(result);
        ApplyDerivedStateRefresh(result);
        StatusText = $"{scene.SceneLabel} 已添加人物台词行。";
    }

    [RelayCommand]
    private void AddNarrationRow(SceneTableViewModel? scene)
    {
        if (!CanEditWorkbenchStructure)
        {
            StatusText = "语音合成中已锁定新增旁白行。";
            return;
        }

        if (scene is null)
        {
            return;
        }

        WorkbenchRowMutationResult result;
        using (BeginWorkbenchDocumentApply())
        {
            result = _workbenchDraftService.AddNarrationRowWithResult(SceneTables, _workbenchSession, scene, SelectedRow);
        }

        var row = result.Row;
        if (!result.CommandResult.Applied || row is null)
        {
            return;
        }

        AttachWorkbenchRowSyncHandler(row);
        ApplySuggestedSelectedRow(result);
        ApplyBindingTargetRefresh(result);
        ApplyDerivedStateRefresh(result);
        StatusText = $"{scene.SceneLabel} 已添加旁白行。";
    }

    [RelayCommand]
    private void RemoveScene(SceneTableViewModel? scene)
    {
        if (!CanEditWorkbenchStructure)
        {
            StatusText = "语音合成中已锁定删除场景。";
            return;
        }

        if (scene is null)
        {
            return;
        }

        var removedIndex = IndexOfScene(scene);
        var selectedRow = SelectedRow;
        var removedSelection = selectedRow is not null && RowBelongsToScene(scene, selectedRow);
        WorkbenchSceneMutationResult result;
        using (BeginWorkbenchDocumentApply())
        {
            result = _workbenchDraftService.RemoveSceneWithResult(SceneTables, _workbenchSession, scene);
        }

        if (!result.CommandResult.Applied)
        {
            return;
        }

        DetachWorkbenchSceneSyncHandler(scene);
        if (removedSelection)
        {
            result = result with { SuggestedSelectedRow = FindNearestRowAfterSceneRemoval(removedIndex) };
            ApplySuggestedSelectedRow(result);
        }

        ApplyBindingTargetRefresh(result);
        ApplyDerivedStateRefresh(result);
        RefreshWorkbenchCharacterProjection("remove_scene");
        StatusText = $"{scene.SceneLabel} 已删除。";
    }

    [RelayCommand]
    private void RemoveInteractionRow(AnalysisResultRowViewModel? row)
    {
        if (!CanEditWorkbenchStructure)
        {
            StatusText = "语音合成中已锁定删除行。";
            return;
        }

        if (row is null)
        {
            return;
        }

        WorkbenchRowMutationResult result;
        using (BeginWorkbenchDocumentApply())
        {
            result = _workbenchDraftService.RemoveRowWithResult(SceneTables, _workbenchSession, row);
        }

        var scene = result.SourceScene;
        if (!result.CommandResult.Applied || scene is null)
        {
            return;
        }

        DetachWorkbenchRowSyncHandler(row);
        ApplyBindingTargetRefresh(result);
        ApplyDerivedStateRefresh(result);
        RefreshWorkbenchCharacterProjection("remove_row");
        StatusText = $"{scene.SceneLabel} 已移除第 {row.OrderLabel} 行。";
    }

    [RelayCommand]
    private void SelectRow(AnalysisResultRowViewModel? row)
    {
        SelectedRow = row;
    }

    [RelayCommand]
    private void LocateExportPreflightIssue(ExportPreflightIssueViewModel? issue)
    {
        if (issue is null || !issue.CanLocate)
        {
            StatusText = "该导出诊断没有可定位的场景或行。";
            return;
        }

        var targetScene = FindSceneForExportPreflightIssue(issue);
        if (targetScene is null)
        {
            StatusText = $"未找到导出诊断对应的场景：{issue.LocationText}";
            return;
        }

        targetScene.IsExpanded = true;
        var targetRow = targetScene.Rows.FirstOrDefault(row =>
            string.Equals(row.InteractionId, issue.InteractionId, StringComparison.Ordinal));
        if (targetRow is not null)
        {
            SelectedRow = targetRow;
            var target = SelectBindingTargetForExportIssue(issue, targetScene, targetRow);
            StatusText = target is null
                ? $"已定位导出诊断：{targetScene.SceneLabel} 第 {targetRow.OrderLabel} 行，未找到匹配的绑定目标。"
                : $"已定位导出诊断：{targetScene.SceneLabel} 第 {targetRow.OrderLabel} 行，已打开 {target.DisplayName} 绑定。";
            return;
        }

        var sceneTarget = SelectBindingTargetForExportIssue(issue, targetScene, null);
        StatusText = sceneTarget is null
            ? $"已定位导出诊断场景：{targetScene.SceneLabel}，未找到匹配的绑定目标。"
            : $"已定位导出诊断场景：{targetScene.SceneLabel}，已打开 {sceneTarget.DisplayName} 绑定。";
    }

    [RelayCommand]
    private async Task RepairExportPreflightIssueAsync(ExportPreflightIssueViewModel? issue)
    {
        if (issue is null || !issue.CanRepair)
        {
            StatusText = "该导出诊断暂时没有可自动执行的修复动作。";
            return;
        }

        if (issue.Kind != EditingTimelinePreflightIssueKind.PsdReference)
        {
            StatusText = "该导出诊断请先定位后在绑定面板中处理。";
            return;
        }

        var targetScene = FindSceneForExportPreflightIssue(issue);
        if (targetScene is null)
        {
            StatusText = $"未找到导出诊断对应的场景：{issue.LocationText}";
            return;
        }

        var targetRow = targetScene.Rows.FirstOrDefault(row =>
            string.Equals(row.InteractionId, issue.InteractionId, StringComparison.Ordinal));
        targetScene.IsExpanded = true;
        if (targetRow is not null)
        {
            SelectedRow = targetRow;
        }

        var target = SelectBindingTargetForExportIssue(issue, targetScene, targetRow);
        if (target is null || !target.SupportsPsd)
        {
            StatusText = $"未找到可重新渲染 PSD 的绑定目标：{issue.LocationText}";
            return;
        }

        var sourceAssetId = target.VisualBinding.SourceAssetId;
        if (target.Kind == BindingTargetKind.Character &&
            !string.IsNullOrWhiteSpace(issue.ReferenceText) &&
            !string.Equals(issue.ReferenceText, sourceAssetId, StringComparison.Ordinal))
        {
            StatusText = $"{target.DisplayName} 的行级 PSD 源与角色管理 PSD 不一致，请先在角色管理中重新绑定。";
            return;
        }

        sourceAssetId = !string.IsNullOrWhiteSpace(sourceAssetId)
            ? sourceAssetId
            : issue.ReferenceText;
        if (string.IsNullOrWhiteSpace(sourceAssetId))
        {
            StatusText = $"{target.DisplayName} 没有可重新渲染的 PSD 源。";
            return;
        }

        var psdPath = ResolveProjectAssetPath(sourceAssetId);
        if (string.IsNullOrWhiteSpace(psdPath) || !File.Exists(psdPath))
        {
            StatusText = $"{target.DisplayName} 的 PSD 源文件不存在，请重新绑定或重新导入 PSD。";
            return;
        }

        var intent = targetRow is not null && target.Kind == BindingTargetKind.Character
            ? ResolvePsdIntentForRow(targetRow)
            : ResolvePsdIntent(target);
        await GeneratePsdImageBindingAsync(
            target,
            sourceAssetId,
            psdPath,
            intent.Emotion,
            intent.Action,
            intent.SceneId,
            intent.InteractionId,
            intent.Text);
        await RefreshExportPreflightIssuesAsync(commitWorkbench: true);
    }

    private async Task RefreshExportPreflightIssuesAsync(bool commitWorkbench)
    {
        if (_workspaceProjectManifest is null)
        {
            ClearExportPreflightIssues();
            return;
        }

        if (commitWorkbench)
        {
            var updatedProject = await CommitWorkbenchAsync();
            Project = updatedProject;
        }

        var issues = await _workspaceService.PreflightEditingProjectAsync(
            new EditingProjectExportRequest(
                Project,
                CurrentWorkspaceProject(),
                EditingProjectFormat.Fcpxml,
                new TimelineExportOptions(),
                _workspaceProjectManifest));
        ApplyExportPreflightIssues(issues);
    }

    private SceneTableViewModel? FindSceneForExportPreflightIssue(ExportPreflightIssueViewModel issue)
    {
        return SceneTables.FirstOrDefault(scene =>
            string.Equals(scene.SceneId, issue.SceneId, StringComparison.Ordinal) ||
            scene.Rows.Any(row => string.Equals(row.InteractionId, issue.InteractionId, StringComparison.Ordinal)));
    }

    private BindingTargetViewModel? SelectBindingTargetForExportIssue(
        ExportPreflightIssueViewModel issue,
        SceneTableViewModel scene,
        AnalysisResultRowViewModel? row)
    {
        IsBindingDialogOpen = true;
        var target = ResolveBindingTargetForExportIssue(issue, scene, row);
        SelectedBindingTarget = target;
        return target;
    }

    private BindingTargetViewModel? ResolveBindingTargetForExportIssue(
        ExportPreflightIssueViewModel issue,
        SceneTableViewModel scene,
        AnalysisResultRowViewModel? row)
    {
        if (issue.Kind == EditingTimelinePreflightIssueKind.PsdReference &&
            VisualBindingReferences(scene.VisualBinding, issue.ReferenceText))
        {
            return BindingTargets.FirstOrDefault(target =>
                target.Kind == BindingTargetKind.Scene &&
                string.Equals(target.TargetId, scene.SceneId, StringComparison.Ordinal));
        }

        if (row is not null)
        {
            if (!row.UsesCharacterVisualPreview)
            {
                return BindingTargets.FirstOrDefault(target => target.Kind == BindingTargetKind.Narration);
            }

            if (issue.Kind == EditingTimelinePreflightIssueKind.PsdReference)
            {
                var characterId = row.SubjectCharacterId ?? row.SourceRoleCharacterId;
                var characterName = row.SubjectName ?? row.SourceRole;
                var psdTarget = BindingTargets.FirstOrDefault(target =>
                    target.Kind == BindingTargetKind.Character &&
                    (string.Equals(target.TargetId, characterId, StringComparison.Ordinal) ||
                     string.Equals(target.DisplayName, characterName, StringComparison.Ordinal)) &&
                    CharacterTargetReferencesVisualSource(target, issue.ReferenceText));
                if (psdTarget is not null)
                {
                    return psdTarget;
                }
            }

            return BindingTargets.FirstOrDefault(target =>
                target.Kind == BindingTargetKind.Character &&
                (string.Equals(target.TargetId, row.SubjectCharacterId ?? row.SourceRoleCharacterId, StringComparison.Ordinal) ||
                 string.Equals(target.DisplayName, row.SubjectName ?? row.SourceRole, StringComparison.Ordinal)));
        }

        return BindingTargets.FirstOrDefault(target =>
            target.Kind == BindingTargetKind.Scene &&
            string.Equals(target.TargetId, scene.SceneId, StringComparison.Ordinal));
    }

    private bool CharacterTargetReferencesVisualSource(BindingTargetViewModel target, string referenceText)
    {
        if (VisualBindingReferences(target.VisualBinding, referenceText))
        {
            return true;
        }

        var binding = WorkbenchDocument.CharacterBindings.FirstOrDefault(binding =>
            string.Equals(binding.CharacterId, target.TargetId, StringComparison.Ordinal));
        return VisualBindingReferences(binding?.VisualBinding, referenceText);
    }

    private static bool VisualBindingReferences(
        VisualAssetBinding? binding,
        string referenceText)
    {
        if (string.IsNullOrWhiteSpace(referenceText))
        {
            return false;
        }

        return string.Equals(binding?.SourceAssetId, referenceText, StringComparison.Ordinal) ||
            string.Equals(binding?.RenderedAssetId, referenceText, StringComparison.Ordinal);
    }

    private void ApplySuggestedSelectedRow(WorkbenchRowMutationResult result)
    {
        if (result.SuggestedSelectedRow is not null)
        {
            SelectedRow = result.SuggestedSelectedRow;
        }
    }

    private void ApplySuggestedSelectedRow(WorkbenchSceneMutationResult result)
    {
        SelectedRow = result.SuggestedSelectedRow;
    }

    private void ApplyBindingTargetRefresh(WorkbenchRowMutationResult result)
    {
        ApplyBindingTargetRefresh(result.BindingTargetRefresh);
    }

    private void ApplyBindingTargetRefresh(WorkbenchSceneMutationResult result)
    {
        ApplyBindingTargetRefresh(result.BindingTargetRefresh);
    }

    private void ApplyBindingTargetRefresh(WorkbenchSceneUpdateResult result)
    {
        ApplyBindingTargetRefresh(result.BindingTargetRefresh);
    }

    private void ApplyBindingTargetRefresh(WorkbenchRowUpdateResult result)
    {
        ApplyBindingTargetRefresh(result.BindingTargetRefresh);
    }

    private void ApplyBindingTargetRefresh(WorkbenchBindingTargetRefresh? refresh)
    {
        if (refresh is null)
        {
            return;
        }

        switch (refresh.Kind)
        {
            case WorkbenchBindingTargetRefreshKind.Scene:
                if (refresh.Scene is not null)
                {
                    RefreshSceneBindingTarget(refresh.Scene);
                }
                break;
            case WorkbenchBindingTargetRefreshKind.InsertScene:
                if (refresh.Scene is not null)
                {
                    InsertSceneBindingTarget(refresh.Scene);
                }
                break;
            case WorkbenchBindingTargetRefreshKind.RemoveScene:
                if (refresh.Scene is not null)
                {
                    RemoveSceneBindingTarget(refresh.Scene);
                }

                RefreshBindingTargetsForRemovedSceneRows(refresh.RemovedRows ?? []);
                break;
            case WorkbenchBindingTargetRefreshKind.SyncSceneOrder:
                SyncSceneBindingTargetOrder();
                break;
            case WorkbenchBindingTargetRefreshKind.SceneOrderRange:
                SyncSceneBindingTargetOrderRange(refresh.Scenes ?? []);
                break;
            case WorkbenchBindingTargetRefreshKind.Row:
                if (refresh.Row is not null)
                {
                    RefreshBindingTargetForRow(refresh.Row);
                }
                break;
            case WorkbenchBindingTargetRefreshKind.Rows:
                RefreshBindingTargetsForRows(refresh.Rows ?? []);
                break;
            case WorkbenchBindingTargetRefreshKind.RowSubjectChange:
                if (refresh.Row is not null)
                {
                    RefreshBindingTargetsForRowSubjectChange(refresh.Row, refresh.PreviousRowDraft);
                }
                break;
        }
    }

    private void ApplyPreviewAssetRefresh(WorkbenchSceneUpdateResult result)
    {
        ApplyPreviewAssetRefresh(result.PreviewAssetRefresh);
    }

    private void ApplyPreviewAssetRefresh(WorkbenchRowUpdateResult result)
    {
        ApplyPreviewAssetRefresh(result.PreviewAssetRefresh);
    }

    private void ApplyPreviewAssetRefresh(WorkbenchPreviewAssetRefresh? refresh)
    {
        if (refresh is null)
        {
            return;
        }

        switch (refresh.Kind)
        {
            case WorkbenchPreviewAssetRefreshKind.Scene:
                if (refresh.Scene is not null)
                {
                    RefreshResolvedPreviewAsset(refresh.Scene);
                }
                break;
            case WorkbenchPreviewAssetRefreshKind.Row:
                if (refresh.Row is not null)
                {
                    RefreshResolvedPreviewAssets(refresh.Row);
                }
                break;
        }
    }

    private void ApplyDerivedStateRefresh(WorkbenchSceneMutationResult result)
    {
        ApplyDerivedStateRefresh(result.DerivedStateRefresh);
    }

    private void ApplyDerivedStateRefresh(WorkbenchRowMutationResult result)
    {
        ApplyDerivedStateRefresh(result.DerivedStateRefresh);
    }

    private void ApplyDerivedStateRefresh(WorkbenchSceneUpdateResult result)
    {
        ApplyDerivedStateRefresh(result.DerivedStateRefresh);
    }

    private void ApplyDerivedStateRefresh(WorkbenchRowUpdateResult result)
    {
        ApplyDerivedStateRefresh(result.DerivedStateRefresh);
    }

    private void ApplyDerivedStateRefresh(WorkbenchDerivedStateRefresh refresh)
    {
        if (!refresh.RefreshCounters)
        {
            NotifyWorkbenchSessionStateChanged();
            return;
        }

        RefreshWorkspaceDerivedState(
            syncSession: false,
            refreshBindingTargets: false,
            refreshCounters: true);
    }

    public void MoveInteractionRow(AnalysisResultRowViewModel? draggedRow, AnalysisResultRowViewModel? targetRow)
    {
        if (!CanDragWorkbench || draggedRow is null || targetRow is null || ReferenceEquals(draggedRow, targetRow))
        {
            return;
        }

        WorkbenchRowMutationResult result;
        using (BeginWorkbenchDocumentApply())
        {
            result = _workbenchDraftService.MoveRowBeforeTargetWithResult(
                SceneTables,
                _workbenchSession,
                draggedRow,
                targetRow);
        }

        var targetScene = result.TargetScene;
        if (!result.CommandResult.Applied || targetScene is null)
        {
            return;
        }

        ApplySuggestedSelectedRow(result);
        ApplyBindingTargetRefresh(result);
        ApplyDerivedStateRefresh(result);
        StatusText = $"已移动到 {targetScene.SceneLabel} 第 {draggedRow.OrderLabel} 行。";
    }

    public void MoveScene(SceneTableViewModel? draggedScene, SceneTableViewModel? targetScene)
    {
        if (!CanDragWorkbench || draggedScene is null || targetScene is null || ReferenceEquals(draggedScene, targetScene))
        {
            return;
        }

        WorkbenchSceneMutationResult result;
        using (BeginWorkbenchDocumentApply())
        {
            result = _workbenchDraftService.MoveSceneWithResult(SceneTables, _workbenchSession, draggedScene, targetScene);
        }

        if (!result.CommandResult.Applied)
        {
            return;
        }

        ApplyBindingTargetRefresh(result);
        ApplyDerivedStateRefresh(result);
        StatusText = $"{draggedScene.SceneLabel} 已调整顺序。";
    }

    internal void MoveInteractionRow(
        AnalysisResultRowViewModel? draggedRow,
        AnalysisResultRowViewModel? targetRow,
        WorkbenchDropPosition position)
    {
        if (!CanDragWorkbench)
        {
            return;
        }

        if (position == WorkbenchDropPosition.Before)
        {
            MoveInteractionRow(draggedRow, targetRow);
            return;
        }

        if (draggedRow is null || targetRow is null || ReferenceEquals(draggedRow, targetRow))
        {
            return;
        }

        WorkbenchRowMutationResult result;
        using (BeginWorkbenchDocumentApply())
        {
            result = _workbenchDraftService.MoveRowToTargetWithResult(
                SceneTables,
                _workbenchSession,
                draggedRow,
                targetRow,
                position);
        }

        var targetScene = result.TargetScene;
        if (!result.CommandResult.Applied || targetScene is null)
        {
            return;
        }

        ApplySuggestedSelectedRow(result);
        ApplyBindingTargetRefresh(result);
        ApplyDerivedStateRefresh(result);
        StatusText = $"Workbench row moved to {targetScene.SceneLabel} row {draggedRow.OrderLabel}.";
    }

    internal void MoveInteractionRowToSceneEnd(AnalysisResultRowViewModel? draggedRow, SceneTableViewModel? targetScene)
    {
        if (!CanDragWorkbench || draggedRow is null || targetScene is null)
        {
            return;
        }

        WorkbenchRowMutationResult result;
        using (BeginWorkbenchDocumentApply())
        {
            result = _workbenchDraftService.MoveRowToSceneEndWithResult(
                SceneTables,
                _workbenchSession,
                draggedRow,
                targetScene);
        }

        if (!result.CommandResult.Applied)
        {
            return;
        }

        ApplySuggestedSelectedRow(result);
        ApplyBindingTargetRefresh(result);
        ApplyDerivedStateRefresh(result);
        StatusText = $"Workbench row moved to {targetScene.SceneLabel} row {draggedRow.OrderLabel}.";
    }

    internal void MoveScene(
        SceneTableViewModel? draggedScene,
        SceneTableViewModel? targetScene,
        WorkbenchDropPosition position)
    {
        if (!CanDragWorkbench)
        {
            return;
        }

        if (position == WorkbenchDropPosition.Before)
        {
            MoveScene(draggedScene, targetScene);
            return;
        }

        if (draggedScene is null || targetScene is null || ReferenceEquals(draggedScene, targetScene))
        {
            return;
        }

        WorkbenchSceneMutationResult result;
        using (BeginWorkbenchDocumentApply())
        {
            result = _workbenchDraftService.MoveSceneToTargetWithResult(
                SceneTables,
                _workbenchSession,
                draggedScene,
                targetScene,
                position);
        }

        if (!result.CommandResult.Applied)
        {
            return;
        }

        ApplyBindingTargetRefresh(result);
        ApplyDerivedStateRefresh(result);
        StatusText = $"Workbench scene moved: {draggedScene.SceneLabel}.";
    }

    public bool PreviewMoveInteractionRow(AnalysisResultRowViewModel? draggedRow, AnalysisResultRowViewModel? targetRow)
    {
        if (!CanDragWorkbench || draggedRow is null || targetRow is null || ReferenceEquals(draggedRow, targetRow))
        {
            return false;
        }

        WorkbenchRowMutationResult result;
        using (BeginWorkbenchDocumentApply())
        {
            if (!_workbenchDragController.ShouldPreview(new WorkbenchDragPreviewIntent(
                    WorkbenchDragPreviewIntentKind.RowToRow,
                    draggedRow.InteractionId,
                    targetRow.InteractionId,
                    WorkbenchDropPosition.Before)))
            {
                return false;
            }

            _workbenchDragController.EnsureCheckpoint(_workbenchSession);
            result = _workbenchDraftService.MoveRowBeforeTargetWithResult(
                SceneTables,
                _workbenchSession,
                draggedRow,
                targetRow,
                preview: true);
        }

        if (!result.CommandResult.Applied)
        {
            _workbenchDragController.MarkNoOpPreview();
            return false;
        }

        SelectedRow = draggedRow;
        _workbenchDragController.MarkChanged(RowMoveBindingTargetRefresh(result));
        return true;
    }

    internal bool PreviewMoveInteractionRow(
        AnalysisResultRowViewModel? draggedRow,
        AnalysisResultRowViewModel? targetRow,
        WorkbenchDropPosition position)
    {
        if (!CanDragWorkbench || draggedRow is null || targetRow is null || ReferenceEquals(draggedRow, targetRow))
        {
            return false;
        }

        WorkbenchRowMutationResult result;
        using (BeginWorkbenchDocumentApply())
        {
            if (!_workbenchDragController.ShouldPreview(new WorkbenchDragPreviewIntent(
                    WorkbenchDragPreviewIntentKind.RowToRow,
                    draggedRow.InteractionId,
                    targetRow.InteractionId,
                    position)))
            {
                return false;
            }

            _workbenchDragController.EnsureCheckpoint(_workbenchSession);
            result = _workbenchDraftService.MoveRowToTargetWithResult(
                SceneTables,
                _workbenchSession,
                draggedRow,
                targetRow,
                position,
                preview: true);
        }

        if (!result.CommandResult.Applied)
        {
            _workbenchDragController.MarkNoOpPreview();
            return false;
        }

        SelectedRow = draggedRow;
        _workbenchDragController.MarkChanged(RowMoveBindingTargetRefresh(result));
        return true;
    }

    public bool PreviewMoveInteractionRowToSceneEnd(AnalysisResultRowViewModel? draggedRow, SceneTableViewModel? targetScene)
    {
        if (!CanDragWorkbench || draggedRow is null || targetScene is null)
        {
            return false;
        }

        WorkbenchRowMutationResult result;
        using (BeginWorkbenchDocumentApply())
        {
            if (!_workbenchDragController.ShouldPreview(new WorkbenchDragPreviewIntent(
                    WorkbenchDragPreviewIntentKind.RowToSceneEnd,
                    draggedRow.InteractionId,
                    targetScene.SceneId,
                    null)))
            {
                return false;
            }

            _workbenchDragController.EnsureCheckpoint(_workbenchSession);
            result = _workbenchDraftService.MoveRowToSceneEndWithResult(
                SceneTables,
                _workbenchSession,
                draggedRow,
                targetScene,
                preview: true);
        }

        if (!result.CommandResult.Applied)
        {
            _workbenchDragController.MarkNoOpPreview();
            return false;
        }

        SelectedRow = draggedRow;
        _workbenchDragController.MarkChanged(RowMoveBindingTargetRefresh(result));
        return true;
    }

    public bool PreviewMoveScene(SceneTableViewModel? draggedScene, SceneTableViewModel? targetScene)
    {
        if (!CanDragWorkbench || draggedScene is null || targetScene is null || ReferenceEquals(draggedScene, targetScene))
        {
            return false;
        }

        WorkbenchSceneMutationResult result;
        using (BeginWorkbenchDocumentApply())
        {
            if (!_workbenchDragController.ShouldPreview(new WorkbenchDragPreviewIntent(
                    WorkbenchDragPreviewIntentKind.SceneToScene,
                    draggedScene.SceneId,
                    targetScene.SceneId,
                    WorkbenchDropPosition.Before)))
            {
                return false;
            }

            _workbenchDragController.EnsureCheckpoint(_workbenchSession);
            result = _workbenchDraftService.MoveSceneWithResult(
                SceneTables,
                _workbenchSession,
                draggedScene,
                targetScene,
                preview: true);
        }

        if (!result.CommandResult.Applied)
        {
            _workbenchDragController.MarkNoOpPreview();
            return false;
        }

        _workbenchDragController.MarkChanged(SceneMoveBindingTargetRefresh(result));
        return true;
    }

    internal bool PreviewMoveScene(
        SceneTableViewModel? draggedScene,
        SceneTableViewModel? targetScene,
        WorkbenchDropPosition position)
    {
        if (!CanDragWorkbench || draggedScene is null || targetScene is null || ReferenceEquals(draggedScene, targetScene))
        {
            return false;
        }

        WorkbenchSceneMutationResult result;
        using (BeginWorkbenchDocumentApply())
        {
            if (!_workbenchDragController.ShouldPreview(new WorkbenchDragPreviewIntent(
                    WorkbenchDragPreviewIntentKind.SceneToScene,
                    draggedScene.SceneId,
                    targetScene.SceneId,
                    position)))
            {
                return false;
            }

            _workbenchDragController.EnsureCheckpoint(_workbenchSession);
            result = _workbenchDraftService.MoveSceneToTargetWithResult(
                SceneTables,
                _workbenchSession,
                draggedScene,
                targetScene,
                position,
                preview: true);
        }

        if (!result.CommandResult.Applied)
        {
            _workbenchDragController.MarkNoOpPreview();
            return false;
        }

        _workbenchDragController.MarkChanged(SceneMoveBindingTargetRefresh(result));
        return true;
    }

    public void BeginWorkbenchDrag()
    {
        if (!CanDragWorkbench)
        {
            return;
        }

        _workbenchDragController.Begin(SelectedRow);
    }

    private WorkbenchBindingTargetRefresh? RowMoveBindingTargetRefresh(WorkbenchRowMutationResult result)
    {
        var changedRowIds = result.CommandResult.ChangedRowIds;
        if (!result.CommandResult.Applied || changedRowIds.Count == 0)
        {
            return null;
        }

        var changedRows = RowsForChangedRowIds(changedRowIds);
        return changedRows.Count == 0
            ? null
            : WorkbenchBindingTargetRefresh.ForRows(changedRows);
    }

    private WorkbenchBindingTargetRefresh? SceneMoveBindingTargetRefresh(WorkbenchSceneMutationResult result)
    {
        var changedSceneIds = result.CommandResult.ChangedSceneIds;
        if (!result.CommandResult.Applied || changedSceneIds.Count == 0)
        {
            return null;
        }

        var changedScenes = ScenesForChangedSceneIds(changedSceneIds);
        return changedScenes.Count == 0
            ? WorkbenchBindingTargetRefresh.SyncSceneOrder()
            : WorkbenchBindingTargetRefresh.ForSceneOrderRange(changedScenes);
    }

    private IReadOnlyList<AnalysisResultRowViewModel> RowsForChangedRowIds(IReadOnlyList<string> changedRowIds)
    {
        var remainingRowIds = new HashSet<string>(changedRowIds, StringComparer.Ordinal);
        var rowsById = new Dictionary<string, AnalysisResultRowViewModel>(changedRowIds.Count, StringComparer.Ordinal);
        foreach (var scene in SceneTables)
        {
            foreach (var row in scene.Rows)
            {
                if (!remainingRowIds.Remove(row.InteractionId))
                {
                    continue;
                }

                rowsById[row.InteractionId] = row;
                if (remainingRowIds.Count == 0)
                {
                    return SortRowsByChangedIds(rowsById, changedRowIds);
                }
            }
        }

        return rowsById.Count == 0 ? [] : SortRowsByChangedIds(rowsById, changedRowIds);
    }

    private IReadOnlyList<SceneTableViewModel> ScenesForChangedSceneIds(IReadOnlyList<string> changedSceneIds)
    {
        var remainingSceneIds = new HashSet<string>(changedSceneIds, StringComparer.Ordinal);
        var scenesById = new Dictionary<string, SceneTableViewModel>(changedSceneIds.Count, StringComparer.Ordinal);
        foreach (var scene in SceneTables)
        {
            if (!remainingSceneIds.Remove(scene.SceneId))
            {
                continue;
            }

            scenesById[scene.SceneId] = scene;
            if (remainingSceneIds.Count == 0)
            {
                return SortScenesByChangedIds(scenesById, changedSceneIds);
            }
        }

        return scenesById.Count == 0 ? [] : SortScenesByChangedIds(scenesById, changedSceneIds);
    }

    private static AnalysisResultRowViewModel[] SortRowsByChangedIds(
        IReadOnlyDictionary<string, AnalysisResultRowViewModel> rowsById,
        IReadOnlyList<string> changedRowIds)
    {
        var rows = new List<AnalysisResultRowViewModel>(rowsById.Count);
        foreach (var rowId in changedRowIds)
        {
            if (rowsById.TryGetValue(rowId, out var row))
            {
                rows.Add(row);
            }
        }

        return rows.ToArray();
    }

    private static SceneTableViewModel[] SortScenesByChangedIds(
        IReadOnlyDictionary<string, SceneTableViewModel> scenesById,
        IReadOnlyList<string> changedSceneIds)
    {
        var scenes = new List<SceneTableViewModel>(scenesById.Count);
        foreach (var sceneId in changedSceneIds)
        {
            if (scenesById.TryGetValue(sceneId, out var scene))
            {
                scenes.Add(scene);
            }
        }

        return scenes.ToArray();
    }

    public void CompleteWorkbenchDrag()
    {
        if (!CanDragWorkbench)
        {
            CancelWorkbenchDrag();
            return;
        }

        var result = _workbenchDragController.Complete();
        if (result is null)
        {
            return;
        }

        using (BeginWorkbenchDocumentApply())
        {
            _workbenchDraftService.ApplyOrderFieldsFromDocument(SceneTables, _workbenchSession.Document);
        }

        ApplyBindingTargetRefresh(result.BindingTargetRefresh);
        _workbenchSession.SquashHistoryToCheckpoint(
            result.Checkpoint,
            WorkbenchSessionOperationNames.WorkbenchDrag);
        ApplyDerivedStateRefresh(result.DerivedStateRefresh);
        StatusText = "工作台顺序已调整。";
    }

    public void CancelWorkbenchDrag()
    {
        var result = _workbenchDragController.Cancel();
        if (result is null)
        {
            return;
        }

        var rollback = result.Rollback;
        using (BeginWorkbenchDocumentApply())
        {
            _workbenchSession.Restore(rollback.Checkpoint);
            WorkbenchViewModelAdapter.ApplyDocument(SceneTables, _workbenchSession.Document);
        }

        RestoreWorkbenchSelection(rollback.SelectedInteractionId);
        ApplyBindingTargetRefresh(result.BindingTargetRefresh);
        ApplyDerivedStateRefresh(result.DerivedStateRefresh);
        StatusText = "工作台拖动已取消。";
    }

    partial void OnProjectChanged(InteractionProject value)
    {
        _externalActionTransaction.NotifyExternalProjectChange();
        var previousInteractionId = SelectedRow?.InteractionId;
        RefreshCollections();
        RestoreWorkbenchSelection(previousInteractionId);
    }

    private void RestoreWorkbenchSelection(string? previousInteractionId)
    {
        AnalysisResultRowViewModel? firstRow = null;
        AnalysisResultRowViewModel? firstDialogueRow = null;
        foreach (var scene in SceneTables)
        {
            foreach (var row in scene.Rows)
            {
                firstRow ??= row;
                if (firstDialogueRow is null && row.UsesCharacterVisualPreview)
                {
                    firstDialogueRow = row;
                }

                if (!string.IsNullOrWhiteSpace(previousInteractionId) &&
                    string.Equals(row.InteractionId, previousInteractionId, StringComparison.Ordinal))
                {
                    SelectedRow = row;
                    return;
                }
            }
        }

        SelectedRow = firstDialogueRow ?? firstRow;
    }

    partial void OnSelectedRowChanged(AnalysisResultRowViewModel? oldValue, AnalysisResultRowViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }

        ApplyBindingTargetCurrentRowMarker(newValue?.InteractionId);
        OnPropertyChanged(nameof(HasSelectedDialogueRow));
    }

    private void ApplyBindingTargetCurrentRowMarker(string? interactionId)
    {
        foreach (var target in BindingTargets)
        {
            target.ApplyCurrentRowMarker(interactionId);
        }
    }

    private void RefreshCollections()
    {
        var syncMetrics = _workbenchDocumentSyncMetrics;
        var expandedScenes = CaptureExpandedScenes();
        var document = WorkbenchDraftBuilder.FromProject(Project);
        Replace(Segments, Project.Script.Segments);
        Replace(Characters, Project.Characters);
        Replace(GenerationTasks, Project.GenerationTasks);
        Replace(Issues, Project.Issues);
        _workbenchSession.Reset(document);
        using (BeginWorkbenchDocumentApply())
        {
            WorkbenchProjectProjection.ApplyProject(SceneTables, Project, document);
        }

        RestoreExpandedScenes(expandedScenes);
        ApplyTtsSynthesisRunningToRows();
        RefreshResolvedPreviewAssets();
        AttachWorkbenchDocumentSyncHandlers();
        RefreshWorkbenchCharacterProjection();
        RefreshBindingTargets("project_refresh");
        NotifyWorkbenchSessionStateChanged();
        _workbenchDocumentSyncMetrics = syncMetrics;
        NotifyDerivedWorkspaceStateChanged();
    }

    private Dictionary<string, bool> CaptureExpandedScenes()
    {
        var expandedScenes = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var scene in SceneTables)
        {
            expandedScenes[scene.SceneId] = scene.IsExpanded;
        }

        return expandedScenes;
    }

    private void RestoreExpandedScenes(IReadOnlyDictionary<string, bool> expandedScenes)
    {
        if (expandedScenes.Count == 0)
        {
            return;
        }

        foreach (var scene in SceneTables)
        {
            if (expandedScenes.TryGetValue(scene.SceneId, out var isExpanded))
            {
                scene.IsExpanded = isExpanded;
            }
        }
    }

    partial void OnIsTtsSynthesisRunningChanged(bool value)
    {
        OnWorkbenchMutationLockStateChanged();
    }

    partial void OnIsPsdLayerAiMatchingRunningChanged(bool value)
    {
        OnWorkbenchMutationLockStateChanged();
    }

    partial void OnIsProjectExportRunningChanged(bool value)
    {
        OnWorkbenchMutationLockStateChanged();
    }

    private void OnWorkbenchMutationLockStateChanged()
    {
        ApplyWorkbenchMutationLockToRows();
        RefreshCharacterVoiceBindingEditAccess();
        NotifyPsdLayerMatchTargetStateChanged();
    }

    private void ApplyTtsSynthesisRunningToRows()
    {
        ApplyWorkbenchMutationLockToRows();
    }

    private void ApplyWorkbenchMutationLockToRows()
    {
        foreach (var row in SceneTables.SelectMany(scene => scene.Rows))
        {
            row.IsTtsSynthesisRunning = IsTtsSynthesisRunning;
            row.IsWorkbenchMutationLocked = IsWorkbenchMutationLocked;
        }
    }

    private void RefreshWorkspaceDerivedState(
        bool syncSession = true,
        bool refreshBindingTargets = true,
        bool refreshCounters = true,
        string bindingRefreshReason = "workspace_derived_state")
    {
        RefreshWorkbenchCharacterProjection();
        if (refreshBindingTargets)
        {
            RefreshBindingTargets(bindingRefreshReason);
        }

        if (syncSession)
        {
            SyncWorkbenchSessionFromViewModels();
        }

        NotifyWorkbenchSessionStateChanged();
        if (refreshCounters)
        {
            NotifyDerivedWorkspaceStateChanged();
        }
    }

    private async Task<InteractionProject> CommitWorkbenchAsync()
    {
        SyncWorkbenchSessionFromViewModels();
        var document = _workbenchSession.Document;
        var project = PruneUnreferencedWorkbenchCharacters(AttachCurrentScriptDrafts(Project), document);
        var updatedProject = await _workspaceService.ApplyWorkbenchDocumentAsync(project, document);
        updatedProject = await ImportPendingExternalVoiceReferencesAsync(updatedProject);
        updatedProject = PruneUnreferencedWorkbenchCharacters(updatedProject, document);
        if (_workbenchSession.HasSameDocument(document))
        {
            _workbenchSession.Reset(document);
        }
        NotifyWorkbenchSessionStateChanged();
        return updatedProject;
    }

    private void ResetWorkbenchSessionFromProject()
    {
        var document = WorkbenchDraftBuilder.FromProject(Project);
        _workbenchSession.Reset(document);
        WorkbenchViewModelAdapter.MarkAppliedDrafts(SceneTables, document);
        ApplyWorkbenchMutationLockToRows();
        NotifyWorkbenchSessionStateChanged();
    }

    private void SyncWorkbenchSessionFromViewModels()
    {
        var syncReason = GetFullDocumentSyncReasonFromViewModels();
        if (syncReason.Kind == WorkbenchFullDocumentSyncReasonKind.Aligned)
        {
            _workbenchDocumentSyncMetrics = _workbenchDocumentSyncMetrics.RecordSkippedAlignedProjection();
            return;
        }

        if (IsWorkbenchMutationLocked)
        {
            RestoreWorkbenchViewModelsFromSessionDuringMutationLock(syncReason);
            return;
        }

        _workbenchDocumentSyncMetrics = _workbenchDocumentSyncMetrics.RecordAttempt(syncReason);
        var document = BuildCurrentWorkbenchDocument();
        if (_workbenchSession.HasSameDocument(document))
        {
            _workbenchDocumentSyncMetrics = _workbenchDocumentSyncMetrics.RecordSkippedNoChange();
            return;
        }

        _workbenchDocumentSyncMetrics = _workbenchDocumentSyncMetrics.RecordApplied();
        _workbenchSession.Execute(
            _ => document,
            WorkbenchSessionOperationNames.SyncViewModelDocument);
        NotifyWorkbenchSessionStateChanged();
    }

    private void RestoreWorkbenchViewModelsFromSessionDuringMutationLock(WorkbenchFullDocumentSyncReason syncReason)
    {
        _workbenchDocumentSyncMetrics = _workbenchDocumentSyncMetrics.RecordSkippedNoChange();
        var previousInteractionId = SelectedRow?.InteractionId;
        using (BeginWorkbenchDocumentApply())
        {
            WorkbenchViewModelAdapter.ApplyDocument(SceneTables, _workbenchSession.Document);
        }

        AttachWorkbenchDocumentSyncHandlers();
        RestoreWorkbenchSelection(previousInteractionId);
        ApplyWorkbenchMutationLockToRows();
        ApplyDerivedStateRefresh(WorkbenchDerivedStateRefresh.SessionOnly);
        StatusText = $"{WorkbenchMutationLockReasonText}中已锁定工作台数据修改，已恢复未提交更改：{syncReason.Kind}。";
    }

    private void RestoreWorkbenchViewModelsFromSessionDuringMutationLockIfNeeded()
    {
        var syncReason = GetFullDocumentSyncReasonFromViewModels();
        if (syncReason.Kind != WorkbenchFullDocumentSyncReasonKind.Aligned)
        {
            RestoreWorkbenchViewModelsFromSessionDuringMutationLock(syncReason);
        }
    }

    private string WorkbenchMutationLockReasonText
    {
        get
        {
            if (IsTtsSynthesisRunning)
            {
                return "语音合成";
            }

            if (IsPsdLayerAiMatchingRunning)
            {
                return "AI 分析 PSD 图层";
            }

            if (IsProjectExportRunning)
            {
                return "导出";
            }

            return "长任务";
        }
    }

    private bool NeedsFullDocumentSyncFromViewModels()
    {
        return GetFullDocumentSyncReasonFromViewModels().Kind != WorkbenchFullDocumentSyncReasonKind.Aligned;
    }

    private WorkbenchFullDocumentSyncReason GetFullDocumentSyncReasonFromViewModels()
    {
        var document = _workbenchSession.Document;
        if (SceneTables.Count != document.Scenes.Count)
        {
            return WorkbenchFullDocumentSyncReason.SceneCountMismatch(SceneTables.Count, document.Scenes.Count);
        }

        for (var sceneIndex = 0; sceneIndex < SceneTables.Count; sceneIndex++)
        {
            var scene = SceneTables[sceneIndex];
            var sceneDraft = document.Scenes[sceneIndex];
            if (!ReferenceEquals(scene.AppliedDraft, sceneDraft))
            {
                return WorkbenchFullDocumentSyncReason.SceneDraftMismatch(sceneIndex, scene.SceneId, sceneDraft.SceneId);
            }

            if (!string.Equals(scene.SceneId, sceneDraft.SceneId, StringComparison.Ordinal))
            {
                return WorkbenchFullDocumentSyncReason.SceneIdMismatch(sceneIndex, scene.SceneId, sceneDraft.SceneId);
            }

            if (scene.Rows.Count != sceneDraft.Rows.Count)
            {
                return WorkbenchFullDocumentSyncReason.RowCountMismatch(
                    sceneIndex,
                    scene.SceneId,
                    scene.Rows.Count,
                    sceneDraft.Rows.Count);
            }

            for (var rowIndex = 0; rowIndex < scene.Rows.Count; rowIndex++)
            {
                var row = scene.Rows[rowIndex];
                var rowDraft = sceneDraft.Rows[rowIndex];
                if (!ReferenceEquals(row.AppliedDraft, rowDraft))
                {
                    return WorkbenchFullDocumentSyncReason.RowDraftMismatch(
                        sceneIndex,
                        rowIndex,
                        scene.SceneId,
                        row.InteractionId,
                        rowDraft.InteractionId);
                }

                if (!string.Equals(row.InteractionId, rowDraft.InteractionId, StringComparison.Ordinal))
                {
                    return WorkbenchFullDocumentSyncReason.RowIdMismatch(
                        sceneIndex,
                        rowIndex,
                        scene.SceneId,
                        row.InteractionId,
                        rowDraft.InteractionId);
                }
            }
        }

        return WorkbenchFullDocumentSyncReason.Aligned;
    }

    private void ApplyWorkbenchSessionDocument(string? previousInteractionId, string bindingRefreshReason)
    {
        using (BeginWorkbenchDocumentApply())
        {
            WorkbenchViewModelAdapter.ApplyDocument(SceneTables, _workbenchSession.Document);
        }

        AttachWorkbenchDocumentSyncHandlers();
        RestoreWorkbenchSelection(previousInteractionId);
        RefreshWorkspaceDerivedState(syncSession: false, bindingRefreshReason: bindingRefreshReason);
    }

    private void NotifyWorkbenchSessionStateChanged()
    {
        var current = new WorkbenchSessionNotificationSnapshot(
            IsWorkbenchDirty,
            CanUndoWorkbench,
            CanRedoWorkbench,
            NextUndoWorkbenchOperationName,
            NextRedoWorkbenchOperationName);
        var previous = _workbenchSessionNotificationSnapshot;
        _workbenchSessionNotificationSnapshot = current;

        if (previous is null || previous.IsDirty != current.IsDirty)
        {
            OnPropertyChanged(nameof(IsWorkbenchDirty));
        }

        if (previous is null || previous.CanUndo != current.CanUndo)
        {
            OnPropertyChanged(nameof(CanUndoWorkbench));
            UndoWorkbenchCommand.NotifyCanExecuteChanged();
        }

        if (previous is null || previous.CanRedo != current.CanRedo)
        {
            OnPropertyChanged(nameof(CanRedoWorkbench));
            RedoWorkbenchCommand.NotifyCanExecuteChanged();
        }

        if (previous is null || previous.NextUndoOperationName != current.NextUndoOperationName)
        {
            OnPropertyChanged(nameof(NextUndoWorkbenchOperationName));
        }

        if (previous is null || previous.NextRedoOperationName != current.NextRedoOperationName)
        {
            OnPropertyChanged(nameof(NextRedoWorkbenchOperationName));
        }
    }

    private void NotifyDerivedWorkspaceStateChanged()
    {
        var current = CaptureDerivedWorkspaceState();
        var previous = _derivedStateSnapshot;
        _derivedStateSnapshot = current;

        if (previous is null || previous.ProjectMetaSignature != current.ProjectMetaSignature)
        {
            OnPropertyChanged(nameof(ProjectMeta));
        }

        if (previous is null || previous.ResultRowCount != current.ResultRowCount)
        {
            OnPropertyChanged(nameof(ResultRowCount));
        }

        if (previous is null || previous.NarrationCount != current.NarrationCount)
        {
            OnPropertyChanged(nameof(NarrationCount));
        }

        if (previous is null || previous.TtsTaskCount != current.TtsTaskCount)
        {
            OnPropertyChanged(nameof(TtsTaskCount));
        }

        if (previous is null || previous.TtsCompletedCount != current.TtsCompletedCount)
        {
            OnPropertyChanged(nameof(TtsCompletedCount));
        }

        if (previous is null || previous.TtsQueuedCount != current.TtsQueuedCount)
        {
            OnPropertyChanged(nameof(TtsQueuedCount));
        }

        if (previous is null || previous.TtsFailedCount != current.TtsFailedCount)
        {
            OnPropertyChanged(nameof(TtsFailedCount));
        }

        if (previous is null || previous.TtsQueueSignature != current.TtsQueueSignature)
        {
            OnPropertyChanged(nameof(TtsQueueMeta));
        }
    }

    private WorkbenchDerivedStateSnapshot CaptureDerivedWorkspaceState()
    {
        var resultRowCount = 0;
        var narrationCount = 0;
        foreach (var scene in SceneTables)
        {
            resultRowCount += scene.Rows.Count;
            for (var index = 0; index < scene.Rows.Count; index++)
            {
                if (scene.Rows[index].IsNarration)
                {
                    narrationCount++;
                }
            }
        }

        var ttsTaskCount = 0;
        var ttsCompletedCount = 0;
        var ttsQueuedCount = 0;
        var ttsFailedCount = 0;
        foreach (var task in GenerationTasks)
        {
            if (task.Type == GenerationTaskType.Tts)
            {
                ttsTaskCount++;
                if (task.Status == GenerationTaskStatus.Completed)
                {
                    ttsCompletedCount++;
                }
                else if (task.Status is GenerationTaskStatus.Pending or GenerationTaskStatus.Running)
                {
                    ttsQueuedCount++;
                }
                else if (task.Status == GenerationTaskStatus.Failed)
                {
                    ttsFailedCount++;
                }
            }

        }

        return new WorkbenchDerivedStateSnapshot(
            SceneTables.Count,
            CurrentWorkbenchCharactersForBindingTargets().Count,
            resultRowCount,
            narrationCount,
            ttsTaskCount,
            ttsCompletedCount,
            ttsQueuedCount,
            ttsFailedCount);
    }

    private WorkbenchDocument BuildCurrentWorkbenchDocument()
    {
        return WorkbenchViewModelAdapter.BuildDocument(SceneTables) with
        {
            CharacterBindings = _workbenchSession.Document.CharacterBindings
        };
    }

    private void RenumberScenes()
    {
        _workbenchDraftService.RenumberScenes(SceneTables);
    }

    internal WorkbenchDragPreviewMetrics WorkbenchDragPreviewMetrics => _workbenchDragController.Metrics;

    internal WorkbenchDocumentSyncMetricsSnapshot WorkbenchDocumentSyncMetrics => _workbenchDocumentSyncMetrics;

    internal WorkbenchBindingRefreshMetricsSnapshot WorkbenchBindingRefreshMetrics => _workbenchBindingRefreshMetrics;

    private void AttachWorkbenchDocumentSyncHandlers()
    {
        var currentScenes = new HashSet<SceneTableViewModel>(SceneTables, ReferenceEqualityComparer.Instance);
        foreach (var scene in _subscribedWorkbenchScenes.ToArray())
        {
            if (!currentScenes.Contains(scene))
            {
                scene.PropertyChanged -= OnWorkbenchScenePropertyChanged;
                _subscribedWorkbenchScenes.Remove(scene);
            }
        }

        var currentRows = new HashSet<AnalysisResultRowViewModel>(ReferenceEqualityComparer.Instance);
        foreach (var scene in SceneTables)
        {
            if (_subscribedWorkbenchScenes.Add(scene))
            {
                scene.PropertyChanged += OnWorkbenchScenePropertyChanged;
            }

            foreach (var row in scene.Rows)
            {
                currentRows.Add(row);
                if (_subscribedWorkbenchRows.Add(row))
                {
                    row.PropertyChanged += OnWorkbenchRowPropertyChanged;
                }
            }
        }

        foreach (var row in _subscribedWorkbenchRows.ToArray())
        {
            if (!currentRows.Contains(row))
            {
                row.PropertyChanged -= OnWorkbenchRowPropertyChanged;
                _subscribedWorkbenchRows.Remove(row);
            }
        }
    }

    private void AttachWorkbenchSceneSyncHandler(SceneTableViewModel scene)
    {
        if (_subscribedWorkbenchScenes.Add(scene))
        {
            scene.PropertyChanged += OnWorkbenchScenePropertyChanged;
        }

        foreach (var row in scene.Rows)
        {
            AttachWorkbenchRowSyncHandler(row);
        }
    }

    private void DetachWorkbenchSceneSyncHandler(SceneTableViewModel scene)
    {
        if (_subscribedWorkbenchScenes.Remove(scene))
        {
            scene.PropertyChanged -= OnWorkbenchScenePropertyChanged;
        }

        foreach (var row in scene.Rows)
        {
            DetachWorkbenchRowSyncHandler(row);
        }
    }

    private void AttachWorkbenchRowSyncHandler(AnalysisResultRowViewModel row)
    {
        if (_subscribedWorkbenchRows.Add(row))
        {
            row.PropertyChanged += OnWorkbenchRowPropertyChanged;
        }
    }

    private void DetachWorkbenchRowSyncHandler(AnalysisResultRowViewModel row)
    {
        if (_subscribedWorkbenchRows.Remove(row))
        {
            row.PropertyChanged -= OnWorkbenchRowPropertyChanged;
        }
    }

    private void OnWorkbenchScenePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_workbenchProjectionGuard.IsApplying ||
            sender is not SceneTableViewModel scene ||
            e.PropertyName is not (nameof(SceneTableViewModel.SceneName)
                or nameof(SceneTableViewModel.SceneEffect)
                or nameof(SceneTableViewModel.SelectedSceneAsset)))
        {
            return;
        }

        if (!CanEditWorkbenchStructure)
        {
            RestoreSceneFromWorkbenchDocument(scene);
            StatusText = $"{WorkbenchMutationLockReasonText}中已锁定场景编辑。";
            return;
        }

        WorkbenchSceneUpdateResult updateResult;
        using (BeginWorkbenchDocumentApply())
        {
            if (e.PropertyName == nameof(SceneTableViewModel.SelectedSceneAsset))
            {
                updateResult = _workbenchDraftService.UpdateSceneAssetWithResult(
                    SceneTables,
                    _workbenchSession,
                    scene,
                    UpdateSceneBindingCommand.FromScene(scene));
            }
            else
            {
                updateResult = _workbenchDraftService.UpdateSceneTextWithResult(
                    SceneTables,
                    _workbenchSession,
                    scene,
                    UpdateSceneTextCommand.FromScene(scene));
            }
        }

        if (!updateResult.CommandResult.Applied)
        {
            return;
        }

        ApplyPreviewAssetRefresh(updateResult);
        ApplyBindingTargetRefresh(updateResult);
        ApplyDerivedStateRefresh(updateResult);
    }

    private void OnWorkbenchRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_workbenchProjectionGuard.IsApplying ||
            sender is not AnalysisResultRowViewModel row)
        {
            return;
        }

        WorkbenchRowUpdateResult? rowUpdateResult = null;
        using (BeginWorkbenchDocumentApply())
        {
            if (e.PropertyName == nameof(AnalysisResultRowViewModel.LineText))
            {
                if (!row.CanEditTextAndEmotion)
                {
                    RestoreRowFromWorkbenchDocument(row);
                    StatusText = $"{WorkbenchMutationLockReasonText}中已锁定文本编辑；完成或取消后会解锁。";
                    return;
                }

                rowUpdateResult = _workbenchDraftService.UpdateRowTextWithResult(
                    SceneTables,
                    _workbenchSession,
                    row,
                    UpdateRowTextCommand.FromRow(row));
            }
            else if (e.PropertyName == nameof(AnalysisResultRowViewModel.SubjectName))
            {
                if (IsWorkbenchMutationLocked)
                {
                    RestoreRowFromWorkbenchDocument(row);
                    StatusText = $"{WorkbenchMutationLockReasonText}中已锁定角色选择。";
                    return;
                }

                rowUpdateResult = _workbenchDraftService.UpdateRowSubjectWithResult(
                    SceneTables,
                    _workbenchSession,
                    row,
                    UpdateRowSubjectCommand.FromRow(row, Project.Characters));
            }
            else if (e.PropertyName == nameof(AnalysisResultRowViewModel.IsNarration))
            {
                if (IsWorkbenchMutationLocked)
                {
                    RestoreRowFromWorkbenchDocument(row);
                    StatusText = $"{WorkbenchMutationLockReasonText}中已锁定行类型。";
                    return;
                }

                rowUpdateResult = _workbenchDraftService.UpdateRowSubjectWithResult(
                    SceneTables,
                    _workbenchSession,
                    row,
                    UpdateRowSubjectCommand.FromRow(row, Project.Characters));
            }
            else if (e.PropertyName is nameof(AnalysisResultRowViewModel.Emotion)
                or nameof(AnalysisResultRowViewModel.EmotionAlpha)
                or nameof(AnalysisResultRowViewModel.EmotionVectorMap))
            {
                if (!row.CanEditTextAndEmotion)
                {
                    RestoreRowFromWorkbenchDocument(row);
                    StatusText = $"{WorkbenchMutationLockReasonText}中已锁定情绪编辑；完成或取消后会解锁。";
                    return;
                }

                if (row.IsUpdatingSubjectDerivedState)
                {
                    return;
                }

                rowUpdateResult = _workbenchDraftService.UpdateRowEmotionWithResult(
                    SceneTables,
                    _workbenchSession,
                    row,
                    UpdateRowEmotionCommand.FromRow(row));
            }
            else if (e.PropertyName is nameof(AnalysisResultRowViewModel.SelectedCharacterAsset)
                or nameof(AnalysisResultRowViewModel.SelectedVoiceReference)
                or nameof(AnalysisResultRowViewModel.SelectedVoiceReferenceText))
            {
                if (IsWorkbenchMutationLocked)
                {
                    RestoreRowFromWorkbenchDocument(row);
                    StatusText = $"{WorkbenchMutationLockReasonText}中已锁定素材和声音绑定。";
                    return;
                }

                if (row.IsUpdatingSubjectDerivedState)
                {
                    return;
                }

                rowUpdateResult = _workbenchDraftService.UpdateRowBindingWithResult(
                    SceneTables,
                    _workbenchSession,
                    row,
                    UpdateRowBindingCommand.FromRow(row));
            }
            else
            {
                return;
            }
        }

        if (rowUpdateResult is null || !rowUpdateResult.CommandResult.Applied)
        {
            return;
        }

        ApplyPreviewAssetRefresh(rowUpdateResult);
        ApplyBindingTargetRefresh(rowUpdateResult);
        ApplyDerivedStateRefresh(rowUpdateResult);
        if (e.PropertyName == nameof(AnalysisResultRowViewModel.SubjectName))
        {
            ApplyCurrentCharacterDefaultVisualBinding(row);
        }
    }

    private void RestoreRowFromWorkbenchDocument(AnalysisResultRowViewModel row)
    {
        WorkbenchViewModelAdapter.ApplyRowFields(row, _workbenchSession.Document);
    }

    private void RestoreSceneFromWorkbenchDocument(SceneTableViewModel scene)
    {
        if (_workbenchSession.Document.TryGetSceneDraft(scene.SceneId, out var sceneDraft))
        {
            WorkbenchSceneProjection.ApplySceneFields(scene, sceneDraft);
        }
    }

    private void ApplyCurrentCharacterDefaultVisualBinding(AnalysisResultRowViewModel row)
    {
        var characterId = row.SubjectCharacterId ?? row.SourceRoleCharacterId;
        if (!row.UsesCharacterVisualPreview || string.IsNullOrWhiteSpace(characterId))
        {
            return;
        }

        var character = Project.Characters.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, characterId, StringComparison.Ordinal));
        var visualBinding = character?.AssetBinding.PortraitVisualBinding is { SourceKind: not VisualSourceKind.None } defaultBinding
            ? defaultBinding
            : VisualAssetBinding.FromImage(character?.AssetBinding.Portrait);
        if (visualBinding.SourceKind == VisualSourceKind.None)
        {
            return;
        }

        WorkbenchRowUpdateResult result;
        using (BeginWorkbenchDocumentApply())
        {
            result = _workbenchDraftService.UpdateRowCharacterVisualBindingWithResult(
                SceneTables,
                _workbenchSession,
                row,
                visualBinding);
        }

        if (!result.CommandResult.Applied)
        {
            return;
        }

        ApplyPreviewAssetRefresh(result);
        ApplyBindingTargetRefresh(result);
        ApplyDerivedStateRefresh(result);
    }

    private void ApplyNewDialogueRowDefaults(AnalysisResultRowViewModel row)
    {
        if (!row.UsesCharacterVisualPreview)
        {
            return;
        }

        WorkbenchRowUpdateResult subjectResult;
        using (BeginWorkbenchDocumentApply())
        {
            subjectResult = _workbenchDraftService.UpdateRowSubjectWithResult(
                SceneTables,
                _workbenchSession,
                row,
                UpdateRowSubjectCommand.FromRow(row, Project.Characters));
        }

        if (subjectResult.CommandResult.Applied)
        {
            ApplyBindingTargetRefresh(subjectResult);
            ApplyDerivedStateRefresh(subjectResult);
        }

        ApplyCurrentCharacterDefaultVisualBinding(row);
    }

    private WorkbenchDerivedStateSnapshot CurrentDerivedState()
    {
        _derivedStateSnapshot ??= CaptureDerivedWorkspaceState();
        return _derivedStateSnapshot;
    }

    private IDisposable BeginWorkbenchDocumentApply()
    {
        return new WorkbenchDocumentApplyScope(
            _workbenchProjectionGuard.BeginApply(),
            _workbenchDisplayProjection.DeferSync());
    }

    private sealed class WorkbenchDocumentApplyScope : IDisposable
    {
        private IDisposable? _projectionGuardScope;
        private IDisposable? _displayProjectionScope;

        public WorkbenchDocumentApplyScope(
            IDisposable projectionGuardScope,
            IDisposable displayProjectionScope)
        {
            _projectionGuardScope = projectionGuardScope;
            _displayProjectionScope = displayProjectionScope;
        }

        public void Dispose()
        {
            var displayProjectionScope = _displayProjectionScope;
            _displayProjectionScope = null;
            displayProjectionScope?.Dispose();

            var projectionGuardScope = _projectionGuardScope;
            _projectionGuardScope = null;
            projectionGuardScope?.Dispose();
        }
    }

    internal void EndWorkbenchEditCoalescing()
    {
        _workbenchSession.EndCoalescing();
        NotifyWorkbenchSessionStateChanged();
    }

    private sealed record WorkbenchSessionNotificationSnapshot(
        bool IsDirty,
        bool CanUndo,
        bool CanRedo,
        string? NextUndoOperationName,
        string? NextRedoOperationName);

    internal readonly record struct WorkbenchDocumentSyncMetricsSnapshot(
        int FullDocumentBuildAttemptCount,
        int SkippedNoChangeCount,
        int AppliedDocumentCount,
        int SkippedAlignedProjectionCount,
        WorkbenchFullDocumentSyncReason LastFullDocumentSyncReason = default,
        WorkbenchFullDocumentSyncReasonCounts FullDocumentSyncReasons = default)
    {
        public WorkbenchDocumentSyncMetricsSnapshot RecordSkippedAlignedProjection()
        {
            return this with { SkippedAlignedProjectionCount = SkippedAlignedProjectionCount + 1 };
        }

        public WorkbenchDocumentSyncMetricsSnapshot RecordAttempt(WorkbenchFullDocumentSyncReason reason)
        {
            return this with
            {
                FullDocumentBuildAttemptCount = FullDocumentBuildAttemptCount + 1,
                LastFullDocumentSyncReason = reason,
                FullDocumentSyncReasons = FullDocumentSyncReasons.Increment(reason.Kind)
            };
        }

        public WorkbenchDocumentSyncMetricsSnapshot RecordSkippedNoChange()
        {
            return this with { SkippedNoChangeCount = SkippedNoChangeCount + 1 };
        }

        public WorkbenchDocumentSyncMetricsSnapshot RecordApplied()
        {
            return this with { AppliedDocumentCount = AppliedDocumentCount + 1 };
        }
    }

    internal readonly record struct WorkbenchBindingRefreshMetricsSnapshot(
        int FullRefreshCount,
        int FallbackFullRefreshCount,
        string LastFullRefreshReason = "",
        string LastFallbackFullRefreshReason = "",
        WorkbenchBindingRefreshReasonCounts FullRefreshReasons = default,
        WorkbenchBindingRefreshReasonCounts FallbackFullRefreshReasons = default)
    {
        public WorkbenchBindingRefreshMetricsSnapshot RecordFullRefresh(string reason)
        {
            return this with
            {
                FullRefreshCount = FullRefreshCount + 1,
                LastFullRefreshReason = reason,
                FullRefreshReasons = FullRefreshReasons.Increment(reason)
            };
        }

        public WorkbenchBindingRefreshMetricsSnapshot RecordFallbackFullRefresh(string reason)
        {
            return this with
            {
                FullRefreshCount = FullRefreshCount + 1,
                FallbackFullRefreshCount = FallbackFullRefreshCount + 1,
                LastFullRefreshReason = reason,
                LastFallbackFullRefreshReason = reason,
                FullRefreshReasons = FullRefreshReasons.Increment(reason),
                FallbackFullRefreshReasons = FallbackFullRefreshReasons.Increment(reason)
            };
        }
    }

    internal readonly record struct WorkbenchBindingRefreshReasonCounts(
        IReadOnlyDictionary<string, int>? Counts)
    {
        public int CountFor(string reason)
        {
            return Counts is not null && Counts.TryGetValue(reason, out var count)
                ? count
                : 0;
        }

        public WorkbenchBindingRefreshReasonCounts Increment(string reason)
        {
            var key = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason;
            var counts = Counts is null
                ? new Dictionary<string, int>(StringComparer.Ordinal)
                : new Dictionary<string, int>(Counts, StringComparer.Ordinal);
            counts[key] = counts.GetValueOrDefault(key) + 1;
            return new WorkbenchBindingRefreshReasonCounts(counts);
        }
    }

    internal readonly record struct WorkbenchFullDocumentSyncReasonCounts(
        IReadOnlyDictionary<WorkbenchFullDocumentSyncReasonKind, int>? Counts)
    {
        public int CountFor(WorkbenchFullDocumentSyncReasonKind reason)
        {
            return Counts is not null && Counts.TryGetValue(reason, out var count)
                ? count
                : 0;
        }

        public WorkbenchFullDocumentSyncReasonCounts Increment(WorkbenchFullDocumentSyncReasonKind reason)
        {
            var counts = Counts is null
                ? new Dictionary<WorkbenchFullDocumentSyncReasonKind, int>()
                : new Dictionary<WorkbenchFullDocumentSyncReasonKind, int>(Counts);
            counts[reason] = counts.GetValueOrDefault(reason) + 1;
            return new WorkbenchFullDocumentSyncReasonCounts(counts);
        }
    }

    internal readonly record struct WorkbenchFullDocumentSyncReason(
        WorkbenchFullDocumentSyncReasonKind Kind,
        int SceneIndex = -1,
        int RowIndex = -1,
        string SceneId = "",
        string ExpectedId = "",
        string ActualId = "",
        int ExpectedCount = -1,
        int ActualCount = -1)
    {
        public static WorkbenchFullDocumentSyncReason Aligned => new(WorkbenchFullDocumentSyncReasonKind.Aligned);

        public static WorkbenchFullDocumentSyncReason SceneCountMismatch(int actualCount, int expectedCount)
        {
            return new(
                WorkbenchFullDocumentSyncReasonKind.SceneCountMismatch,
                ExpectedCount: expectedCount,
                ActualCount: actualCount);
        }

        public static WorkbenchFullDocumentSyncReason SceneDraftMismatch(int sceneIndex, string actualId, string expectedId)
        {
            return new(
                WorkbenchFullDocumentSyncReasonKind.SceneDraftMismatch,
                sceneIndex,
                SceneId: actualId,
                ExpectedId: expectedId,
                ActualId: actualId);
        }

        public static WorkbenchFullDocumentSyncReason SceneIdMismatch(int sceneIndex, string actualId, string expectedId)
        {
            return new(
                WorkbenchFullDocumentSyncReasonKind.SceneIdMismatch,
                sceneIndex,
                SceneId: actualId,
                ExpectedId: expectedId,
                ActualId: actualId);
        }

        public static WorkbenchFullDocumentSyncReason RowCountMismatch(
            int sceneIndex,
            string sceneId,
            int actualCount,
            int expectedCount)
        {
            return new(
                WorkbenchFullDocumentSyncReasonKind.RowCountMismatch,
                sceneIndex,
                SceneId: sceneId,
                ExpectedCount: expectedCount,
                ActualCount: actualCount);
        }

        public static WorkbenchFullDocumentSyncReason RowDraftMismatch(
            int sceneIndex,
            int rowIndex,
            string sceneId,
            string actualId,
            string expectedId)
        {
            return new(
                WorkbenchFullDocumentSyncReasonKind.RowDraftMismatch,
                sceneIndex,
                rowIndex,
                sceneId,
                expectedId,
                actualId);
        }

        public static WorkbenchFullDocumentSyncReason RowIdMismatch(
            int sceneIndex,
            int rowIndex,
            string sceneId,
            string actualId,
            string expectedId)
        {
            return new(
                WorkbenchFullDocumentSyncReasonKind.RowIdMismatch,
                sceneIndex,
                rowIndex,
                sceneId,
                expectedId,
                actualId);
        }
    }

    internal enum WorkbenchFullDocumentSyncReasonKind
    {
        Aligned,
        SceneCountMismatch,
        SceneDraftMismatch,
        SceneIdMismatch,
        RowCountMismatch,
        RowDraftMismatch,
        RowIdMismatch
    }

    private sealed record WorkbenchDerivedStateSnapshot(
        int SceneCount,
        int CharacterCount,
        int ResultRowCount,
        int NarrationCount,
        int TtsTaskCount,
        int TtsCompletedCount,
        int TtsQueuedCount,
        int TtsFailedCount)
    {
        public (int SceneCount, int CharacterCount, int NarrationCount, int ResultRowCount) ProjectMetaSignature =>
            (SceneCount, CharacterCount, NarrationCount, ResultRowCount);

        public (int TtsCompletedCount, int TtsTaskCount, int TtsQueuedCount, int TtsFailedCount) TtsQueueSignature =>
            (TtsCompletedCount, TtsTaskCount, TtsQueuedCount, TtsFailedCount);
    }

    private string DefaultCharacterName()
    {
        for (var index = 0; index < Characters.Count; index++)
        {
            if (Characters[index].Name != "旁白")
            {
                return Characters[index].Name;
            }
        }

        for (var index = 0; index < Project.Characters.Count; index++)
        {
            if (Project.Characters[index].Name != "旁白")
            {
                return Project.Characters[index].Name;
            }
        }

        return "未命名人物";
    }

    private int IndexOfScene(SceneTableViewModel scene)
    {
        for (var index = 0; index < SceneTables.Count; index++)
        {
            if (ReferenceEquals(SceneTables[index], scene))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool RowBelongsToScene(SceneTableViewModel scene, AnalysisResultRowViewModel row)
    {
        if (ReferenceEquals(row.OwnerScene, scene))
        {
            return true;
        }

        for (var index = 0; index < scene.Rows.Count; index++)
        {
            if (ReferenceEquals(scene.Rows[index], row))
            {
                return true;
            }
        }

        return false;
    }

    private AnalysisResultRowViewModel? FindNearestRowAfterSceneRemoval(int removedIndex)
    {
        if (SceneTables.Count == 0)
        {
            return null;
        }

        var nearestSceneIndex = Math.Clamp(removedIndex, 0, SceneTables.Count - 1);
        for (var sceneIndex = nearestSceneIndex; sceneIndex < SceneTables.Count; sceneIndex++)
        {
            if (SceneTables[sceneIndex].Rows.Count > 0)
            {
                return SceneTables[sceneIndex].Rows[0];
            }
        }

        for (var sceneIndex = nearestSceneIndex - 1; sceneIndex >= 0; sceneIndex--)
        {
            if (SceneTables[sceneIndex].Rows.Count > 0)
            {
                return SceneTables[sceneIndex].Rows[0];
            }
        }

        return null;
    }

    private IReadOnlyList<SceneTableViewModel> BuildSceneTables(IReadOnlyList<InteractionScene> scenes)
    {
        var context = AnalysisResultRowViewModel.InteractionRowBuildContext.FromProject(Project);
        var sceneTables = new SceneTableViewModel[scenes.Count];
        for (var index = 0; index < scenes.Count; index++)
        {
            sceneTables[index] = SceneTableViewModel.FromScene(scenes[index], context);
        }

        return sceneTables;
    }

    private static IReadOnlyList<string> BuildCharacterNameOptions(IReadOnlyList<Character> characters)
    {
        var names = new List<string>(characters.Count + 3)
        {
            "旁白",
            AnalysisResultRowViewModel.EnvironmentEffectsSubjectName,
            AnalysisResultRowViewModel.NarrationStorySubjectName
        };
        var seen = new HashSet<string>(StringComparer.Ordinal)
        {
            "旁白",
            AnalysisResultRowViewModel.EnvironmentEffectsSubjectName,
            AnalysisResultRowViewModel.NarrationStorySubjectName
        };
        for (var index = 0; index < characters.Count; index++)
        {
            var name = characters[index].Name;
            if (name != "旁白" && seen.Add(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private void RefreshWorkbenchCharacterProjection(string reason = "")
    {
        var characters = CurrentWorkbenchCharactersForBindingTargets();
        Replace(Characters, characters);
        Replace(CharacterNameOptions, BuildCharacterNameOptions(characters));
    }

    private InteractionProject PruneUnreferencedWorkbenchCharacters(InteractionProject project, WorkbenchDocument document)
    {
        var characters = FilterCharactersForWorkbench(project.Characters, document);
        if (characters.Count == project.Characters.Count &&
            characters.SequenceEqual(project.Characters))
        {
            return project;
        }

        return project with { Characters = characters };
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }
}
