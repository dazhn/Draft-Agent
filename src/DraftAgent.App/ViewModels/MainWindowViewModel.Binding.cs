using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using DraftAgent.App;
using DraftAgent.App.Psd.Services;
using DraftAgent.Core;
using PsdLayerLoader.Core;

namespace DraftAgent.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private const int PsdSectionDividerLayerType = 3;
    private const int PsdLayerAiRowsPerRequest = 12;

    [RelayCommand]
    private void ConvertThoughtNarrationVoicesToRoles()
    {
        if (!UseRoleVoiceForAllCharacterLines)
        {
            EnableRoleReferenceVoiceRoute();
        }

        UseRoleVoiceForThought = true;
        ApplyRoleVoiceRoute(
            document => new WorkbenchCommandService().BindThoughtNarrationVoicesToSourceRoles(
                document,
                CurrentWorkbenchCharactersForVoiceRouting(document)),
            "thought_voice_roles",
            "已将内心台词改用对应角色参考音；原始 JSON 类型与角色信息已保留。",
            "没有可转换的 thought 旁白，或对应角色尚未在角色管理中存在。");
    }

    [RelayCommand]
    private void ToggleRoleDialogueVoiceRoute()
    {
        if (!CanEditWorkbenchStructure)
        {
            ReportRoleManagementLockedByLongOperation();
            return;
        }

        if (!UseRoleVoiceForAllCharacterLines)
        {
            EnableRoleReferenceVoiceRoute();
        }
    }

    [RelayCommand]
    private void ToggleRoleThoughtVoiceRoute()
    {
        if (!CanEditWorkbenchStructure)
        {
            ReportRoleManagementLockedByLongOperation();
            return;
        }

        if (!UseRoleVoiceForAllCharacterLines)
        {
            return;
        }

        UseRoleVoiceForThought = !UseRoleVoiceForThought;
        ApplyRoleVoiceRoute(
            document => new WorkbenchCommandService().RouteSourceRoleVoices(
                document,
                CurrentWorkbenchCharactersForVoiceRouting(document),
                "thought",
                UseRoleVoiceForThought),
            "thought_voice_route",
            UseRoleVoiceForThought
                ? "内心台词已改用角色参考音；原始 JSON 字段不变。"
                : "内心台词已改回默认配音；原始 JSON 字段不变。",
            "没有可切换的 thought 内心行，或对应角色尚未在角色管理中存在。");
    }

    [RelayCommand]
    private void ToggleRoleActionVoiceRoute()
    {
        if (!CanEditWorkbenchStructure)
        {
            ReportRoleManagementLockedByLongOperation();
            return;
        }

        if (!UseRoleVoiceForAllCharacterLines)
        {
            return;
        }

        UseRoleVoiceForAction = !UseRoleVoiceForAction;
        ApplyRoleVoiceRoute(
            document => new WorkbenchCommandService().RouteSourceRoleVoices(
                document,
                CurrentWorkbenchCharactersForVoiceRouting(document),
                "action",
                UseRoleVoiceForAction),
            "action_voice_route",
            UseRoleVoiceForAction
                ? "动作台词已改用角色参考音；原始 JSON 字段不变。"
                : "动作台词已改回默认配音；原始 JSON 字段不变。",
            "没有可切换的 action 动作行，或对应角色尚未在角色管理中存在。");
    }

    [RelayCommand]
    private void ToggleRoleAllVoiceRoute()
    {
        if (!CanEditWorkbenchStructure)
        {
            ReportRoleManagementLockedByLongOperation();
            return;
        }

        if (!UseRoleVoiceForAllCharacterLines)
        {
            EnableRoleReferenceVoiceRoute();
            return;
        }

        DisableRoleReferenceVoiceRoute();
    }

    private void ReportRoleManagementLockedByLongOperation()
    {
        StatusText = $"{WorkbenchMutationLockReasonText}中已锁定角色管理。请等待完成或取消后再修改角色。";
    }

    private void EnableRoleReferenceVoiceRoute()
    {
        var document = _workbenchSession.Document;
        if (!HasRoutableRoleRows(document, includeThought: true, includeAction: true))
        {
            UseRoleVoiceForAllCharacterLines = false;
            UseRoleVoiceForDialogue = false;
            UseRoleVoiceForThought = false;
            UseRoleVoiceForAction = false;
            StatusText = "没有可使用角色参考音频的角色行：请先确认工作台存在角色台词、内心或动作行，且角色已在角色管理中存在。";
            return;
        }

        var routeApplied = ApplyRoleVoiceRoute(
            document => new WorkbenchCommandService().RouteDialogueVoicesToSourceRoles(
                document,
                CurrentWorkbenchCharactersForVoiceRouting(document),
                routeToSourceRole: true),
            "role_reference_voice_route",
            "角色台词已锁定使用角色参考音；未绑定参考音频的角色仍使用默认配音。",
            "没有可切换的角色台词，或对应角色尚未在角色管理中存在。");
        UseRoleVoiceForAllCharacterLines = true;
        UseRoleVoiceForDialogue = true;
        UseRoleVoiceForThought = false;
        UseRoleVoiceForAction = false;
        if (!routeApplied)
        {
            StatusText = "已开启角色参考音频配音；当前台词角色尚未绑定参考音频，所以暂时继续使用默认配音。";
        }
    }

    private void DisableRoleReferenceVoiceRoute()
    {
        UseRoleVoiceForAllCharacterLines = false;
        UseRoleVoiceForDialogue = false;
        UseRoleVoiceForThought = false;
        UseRoleVoiceForAction = false;
        _ = ApplyRoleVoiceRoute(
            document => new WorkbenchCommandService().RouteCharacterSourceVoicesToSourceRoles(
                document,
                CurrentWorkbenchCharactersForVoiceRouting(document),
                routeToSourceRole: false),
            "role_narrator_voice_route",
            "角色行已改回默认配音；角色参考音频绑定已禁用。",
            "没有可切换的角色行。");
    }

    private bool ApplyRoleVoiceRoute(
        Func<WorkbenchDocument, WorkbenchDocument> route,
        string refreshReason,
        string appliedStatus,
        string noOpStatus)
    {
        var previousDocument = _workbenchSession.Document;
        var previousNarratorVoiceBinding = CaptureNarratorVoiceBinding(previousDocument);
        WorkbenchDocument updatedDocument;
        using (BeginWorkbenchDocumentApply())
        {
            updatedDocument = _workbenchSession.Execute(
                document =>
                {
                    var routed = route(document);
                    return new WorkbenchCommandService().ApplyNarrationVoiceBinding(routed, previousNarratorVoiceBinding);
                },
                WorkbenchSessionOperationNames.ApplyBindingTarget);
            if (!ReferenceEquals(updatedDocument, previousDocument))
            {
                WorkbenchViewModelAdapter.ApplyDocument(SceneTables, updatedDocument);
            }
        }

        RefreshCharacterVoiceBindingEditAccess();
        OnRoleVoiceRouteStateChanged();

        if (ReferenceEquals(updatedDocument, previousDocument))
        {
            StatusText = noOpStatus;
            return false;
        }

        RefreshBindingTargets(refreshReason);
        RefreshCharacterVoiceBindingEditAccess();
        ApplyDerivedStateRefresh(WorkbenchDerivedStateRefresh.SessionOnly);
        NotifyWorkbenchSessionStateChanged();
        StatusText = appliedStatus;
        return true;
    }

    private bool HasRoutableRoleRows(WorkbenchDocument document, bool includeThought, bool includeAction)
    {
        var characters = CurrentWorkbenchCharactersForVoiceRouting(document);
        var characterIds = characters
            .Where(character => !string.IsNullOrWhiteSpace(character.Id))
            .Select(character => character.Id)
            .ToHashSet(StringComparer.Ordinal);
        var characterNames = characters
            .Where(character => !string.IsNullOrWhiteSpace(character.Name))
            .Select(character => character.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var scene in document.Scenes)
        {
            foreach (var row in scene.Rows)
            {
                if (!IsRoleVoiceRoutableRow(row, includeThought, includeAction))
                {
                    continue;
                }

                if (RowMatchesKnownCharacter(row, characterIds, characterNames))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsRoleVoiceRoutableRow(WorkbenchRowDraft row, bool includeThought, bool includeAction)
    {
        var sourceItemType = row.SourceItemType?.Trim();
        return row.Kind == WorkbenchRowKind.Dialogue ||
            string.Equals(sourceItemType, "dialogue", StringComparison.OrdinalIgnoreCase) ||
            (includeThought && string.Equals(sourceItemType, "thought", StringComparison.OrdinalIgnoreCase)) ||
            (includeAction && string.Equals(sourceItemType, "action", StringComparison.OrdinalIgnoreCase));
    }

    private static bool RowMatchesKnownCharacter(
        WorkbenchRowDraft row,
        IReadOnlySet<string> characterIds,
        IReadOnlySet<string> characterNames)
    {
        return (!string.IsNullOrWhiteSpace(row.SourceRoleCharacterId) &&
                characterIds.Contains(row.SourceRoleCharacterId)) ||
            (!string.IsNullOrWhiteSpace(row.SourceRole) &&
             characterNames.Contains(row.SourceRole)) ||
            (row.Kind == WorkbenchRowKind.Dialogue &&
             !string.IsNullOrWhiteSpace(row.SubjectCharacterId) &&
             characterIds.Contains(row.SubjectCharacterId)) ||
            (row.Kind == WorkbenchRowKind.Dialogue &&
             !string.IsNullOrWhiteSpace(row.SubjectName) &&
             characterNames.Contains(row.SubjectName));
    }

    private WorkbenchDocument BuildTtsSubmissionDocument(WorkbenchDocument document)
    {
        var characterVoiceBindingsById = document.CharacterBindings
            .Where(binding => !string.IsNullOrWhiteSpace(binding.CharacterId) &&
                HasEffectiveVoiceBinding(binding.VoiceBinding))
            .ToDictionary(binding => binding.CharacterId, binding => binding.VoiceBinding!, StringComparer.Ordinal);
        var charactersByName = Project.Characters
            .Where(character => !string.IsNullOrWhiteSpace(character.Name))
            .GroupBy(character => character.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var changed = false;
        var scenes = new WorkbenchSceneDraft[document.Scenes.Count];
        for (var sceneIndex = 0; sceneIndex < document.Scenes.Count; sceneIndex++)
        {
            var scene = document.Scenes[sceneIndex];
            WorkbenchRowDraft[]? rows = null;
            for (var rowIndex = 0; rowIndex < scene.Rows.Count; rowIndex++)
            {
                var row = scene.Rows[rowIndex];
                var effectiveVoiceBinding = ResolveTtsSubmissionVoiceBinding(
                    row,
                    characterVoiceBindingsById,
                    charactersByName);
                if (VoiceAssetBinding.SemanticEquals(row.VoiceBinding, effectiveVoiceBinding))
                {
                    continue;
                }

                rows ??= scene.Rows.ToArray();
                rows[rowIndex] = row with { VoiceBinding = effectiveVoiceBinding };
                changed = true;
            }

            scenes[sceneIndex] = rows is null ? scene : scene with { Rows = rows };
        }

        return changed ? document with { Scenes = scenes } : document;
    }

    private async Task<InteractionProject> ApplyTtsSubmissionVoiceRoutesAsync(
        InteractionProject project,
        IDictionary<string, string>? referenceTextSourceByRowId = null)
    {
        var submissionDocument = BuildTtsSubmissionDocument(_workbenchSession.Document);
        var updatedProject = await _workspaceService.ApplyWorkbenchDocumentAsync(project, submissionDocument);
        if (referenceTextSourceByRowId is not null)
        {
            foreach (var row in submissionDocument.Scenes.SelectMany(scene => scene.Rows))
            {
                if (row.VoiceBinding is null)
                {
                    continue;
                }

                referenceTextSourceByRowId[row.InteractionId] = VoiceReferenceTextSourceFromBinding(row.VoiceBinding);
            }
        }

        return updatedProject;
    }

    private VoiceAssetBinding ResolveTtsSubmissionVoiceBinding(
        WorkbenchRowDraft row,
        IReadOnlyDictionary<string, VoiceAssetBinding> characterVoiceBindingsById,
        IReadOnlyDictionary<string, Character> charactersByName)
    {
        if (ShouldUseCharacterVoiceForTts(row) &&
            ResolveTtsSubmissionCharacterId(row, charactersByName) is { } characterId &&
            characterVoiceBindingsById.TryGetValue(characterId, out var characterVoiceBinding))
        {
            return characterVoiceBinding;
        }

        return row.VoiceBinding ?? VoiceAssetBinding.None;
    }

    private bool ShouldUseCharacterVoiceForTts(WorkbenchRowDraft row)
    {
        if (!UseRoleVoiceForAllCharacterLines)
        {
            return false;
        }

        var sourceItemType = row.SourceItemType?.Trim();
        if (row.Kind == WorkbenchRowKind.Dialogue ||
            string.Equals(sourceItemType, "dialogue", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return (UseRoleVoiceForThought && string.Equals(sourceItemType, "thought", StringComparison.OrdinalIgnoreCase)) ||
            (UseRoleVoiceForAction && string.Equals(sourceItemType, "action", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveTtsSubmissionCharacterId(
        WorkbenchRowDraft row,
        IReadOnlyDictionary<string, Character> charactersByName)
    {
        if (!string.IsNullOrWhiteSpace(row.SourceRoleCharacterId))
        {
            return row.SourceRoleCharacterId;
        }

        if (!string.IsNullOrWhiteSpace(row.SubjectCharacterId))
        {
            return row.SubjectCharacterId;
        }

        if (!string.IsNullOrWhiteSpace(row.SourceRole) &&
            charactersByName.TryGetValue(row.SourceRole, out var sourceRoleCharacter))
        {
            return sourceRoleCharacter.Id;
        }

        if (!string.IsNullOrWhiteSpace(row.SubjectName) &&
            charactersByName.TryGetValue(row.SubjectName, out var subjectCharacter))
        {
            return subjectCharacter.Id;
        }

        return null;
    }

    private static bool HasEffectiveVoiceBinding(VoiceAssetBinding? binding)
    {
        return binding is not null &&
            !VoiceAssetBinding.SemanticEquals(binding, VoiceAssetBinding.None) &&
            !string.IsNullOrWhiteSpace(binding.EffectiveVoiceReferenceId);
    }

    private VoiceAssetBinding CaptureNarratorVoiceBinding(WorkbenchDocument document)
    {
        foreach (var scene in document.Scenes)
        {
            foreach (var row in scene.Rows)
            {
                if (!string.IsNullOrWhiteSpace(row.VoiceRole) ||
                    !string.IsNullOrWhiteSpace(row.VoiceRoleCharacterId))
                {
                    continue;
                }

                if (row.VoiceBinding is not null &&
                    !VoiceAssetBinding.SemanticEquals(row.VoiceBinding, VoiceAssetBinding.None))
                {
                    _lastNarratorVoiceBinding = row.VoiceBinding;
                    return row.VoiceBinding;
                }
            }
        }

        return _lastNarratorVoiceBinding;
    }

    private IReadOnlyList<Character> CurrentWorkbenchCharactersForVoiceRouting(WorkbenchDocument document)
    {
        var bindingsById = document.CharacterBindings
            .Where(binding => !string.IsNullOrWhiteSpace(binding.CharacterId))
            .ToDictionary(binding => binding.CharacterId, StringComparer.Ordinal);

        return FilterCharactersForWorkbench(Project.Characters, document)
            .Select(character =>
            {
                bindingsById.TryGetValue(character.Id, out var binding);

                return character with
                {
                    AssetBinding = character.AssetBinding with
                    {
                        PortraitVisualBinding = binding?.VisualBinding ?? character.AssetBinding.PortraitVisualBinding,
                        VoiceBinding = binding?.VoiceBinding
                    }
                };
            })
            .ToArray();
    }

    [RelayCommand]
    private void ToggleBindingPanel()
    {
        IsBindingDialogOpen = !IsBindingDialogOpen;
    }

    [RelayCommand]
    private void CloseBindingDialog()
    {
        IsBindingDialogOpen = false;
    }

    [RelayCommand]
    private void ChooseImageBinding(BindingTargetViewModel? target)
    {
        if (target is null || !target.SupportsImage) return;

        StatusText = $"Choose an image file for {target.DisplayName} in the binding panel.";
    }

    [RelayCommand]
    private void ChooseVoiceBinding(BindingTargetViewModel? target)
    {
        if (target is null || !target.SupportsVoice) return;
        if (RejectBindingEditsWhileWorkbenchMutationLocked(target)) return;

        ShowCustomVoiceReferenceInstruction(target);
    }

    [RelayCommand]
    private void ApplyBindingTarget(BindingTargetViewModel? target)
    {
        if (target is null)
        {
            return;
        }
        if (RejectBindingEditsWhileWorkbenchMutationLocked(target)) return;

        ApplyBindingTargetToWorkspace(target);
        StatusText = $"{target.DisplayName} binding applied to workbench.";
    }

    [RelayCommand]
    private async Task RenameBindingTargetAsync(BindingTargetViewModel? target)
    {
        if (target is null || target.Kind != BindingTargetKind.Character)
        {
            return;
        }
        if (RejectBindingEditsWhileWorkbenchMutationLocked(target)) return;

        var nextName = NormalizeRoleNameForSave(target.EditableDisplayName);
        if (string.IsNullOrWhiteSpace(nextName))
        {
            target.EditableDisplayName = string.Empty;
            StatusText = "角色名称不能为空。";
            return;
        }

        var character = Project.Characters.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, target.TargetId, StringComparison.Ordinal));
        if (character is null)
        {
            target.CancelDisplayNameEdit();
            StatusText = $"未找到角色：{target.DisplayName}";
            return;
        }

        if (string.Equals(character.Name, nextName, StringComparison.Ordinal))
        {
            target.AcceptDisplayNameEdit(character.Name);
            return;
        }

        if (string.Equals(nextName, "旁白", StringComparison.Ordinal))
        {
            target.EditableDisplayName = nextName;
            StatusText = $"角色名称已存在：{nextName}";
            return;
        }

        var mergeTarget = Project.Characters.FirstOrDefault(candidate =>
            !string.Equals(candidate.Id, target.TargetId, StringComparison.Ordinal) &&
            string.Equals(candidate.Name, nextName, StringComparison.Ordinal));
        if (mergeTarget is not null)
        {
            target.EditableDisplayName = nextName;
            var affectedCount = CountRowsForCharacter(target.TargetId, character.Name);
            var shouldMerge = await ConfirmRoleMergeAsync(new RoleMergeConfirmation(
                character.Name,
                mergeTarget.Name,
                affectedCount));
            if (!shouldMerge)
            {
                StatusText = $"已取消合并：{character.Name} -> {mergeTarget.Name}";
                return;
            }

            MergeCharacterBindingTarget(target, character, mergeTarget);
            return;
        }

        RenameCharacterBindingTarget(target, character, nextName);
    }

    private static string NormalizeRoleNameForSave(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value.Where(character => !char.IsWhiteSpace(character)).ToArray();
        return chars.Length == 0 ? string.Empty : new string(chars);
    }

    [RelayCommand]
    private async Task ResyncVoiceReferenceAsync(BindingTargetViewModel? target)
    {
        if (target is null || !target.SupportsVoice)
        {
            return;
        }
        if (RejectBindingEditsWhileWorkbenchMutationLocked(target)) return;

        try
        {
            target.StatusText = "Checking voice reference";
            var binding = VoiceAssetBinding.FromProjectReference(
                WorkbenchBindingValue.Asset(target.VoiceAsset),
                WorkbenchBindingValue.Text(target.EffectiveVoiceReferenceTextForBinding()),
                WorkbenchBindingValue.Asset(target.VoiceSourceAsset),
                null);
            var synced = await EnsureVoiceBindingSyncedAsync(binding);
            target.VoiceAsset = synced.EffectiveVoiceReferenceId ?? target.VoiceAsset;
            target.VoiceSourceAsset = synced.SourceAssetId ?? target.VoiceSourceAsset;
            target.VoiceSyncedPath = string.Empty;
            target.ApplyVoiceReferenceText(
                synced.EffectiveReferenceText ?? target.EffectiveVoiceReferenceTextForBinding(),
                VoiceReferenceTextSourceFromBinding(synced));
            target.ApplyVoiceBindingSummary(synced);
            ApplyBindingTargetToWorkspace(target);
            target.StatusText = "参考音频已确认";
            StatusText = $"{target.DisplayName} 参考音频已确认；软件不会复制到 ComfyUI voices。";
        }
        catch (Exception ex)
        {
            target.StatusText = "Voice reference sync failed";
            HandleExternalError("Voice reference sync failed", ex);
        }
    }

    [RelayCommand]
    private async Task ReloadVoiceReferenceTextAsync(BindingTargetViewModel? target)
    {
        if (target is null || !target.SupportsVoice)
        {
            return;
        }
        if (RejectBindingEditsWhileWorkbenchMutationLocked(target)) return;

        try
        {
            target.StatusText = "Reading reference text";
            var referenceText = await ReadVoiceReferenceTextForTargetAsync(target);
            if (string.IsNullOrWhiteSpace(referenceText.Text))
            {
                target.StatusText = "No reference text found";
                return;
            }

            target.ApplyVoiceReferenceText(referenceText.Text, referenceText.SourceText);
            var binding = VoiceAssetBinding.FromProjectReference(
                WorkbenchBindingValue.Asset(target.VoiceAsset),
                WorkbenchBindingValue.Text(referenceText.Text),
                WorkbenchBindingValue.Asset(target.VoiceSourceAsset),
                WorkbenchBindingValue.Asset(target.VoiceSyncedPath));
            target.ApplyVoiceBindingSummary(binding);
            ApplyBindingTargetToWorkspace(target);
            target.StatusText = "Reference text updated";
            StatusText = $"{target.DisplayName} reference text updated";
        }
        catch (Exception ex)
        {
            target.StatusText = "Reference text reload failed";
            HandleExternalError("Reference text reload failed", ex);
        }
    }

    [RelayCommand]
    private async Task SaveVoiceReferenceTextAsync(BindingTargetViewModel? target)
    {
        await Task.CompletedTask;
        if (target is null || !target.SupportsVoice)
        {
            return;
        }
        if (RejectBindingEditsWhileWorkbenchMutationLocked(target)) return;

        const string message = "Draft Agent 不写 ComfyUI voices 文本；要持久保存参考文本，请在音频旁边建立同名 .reference.txt 或 .txt。";
        target.StatusText = message;
        StatusText = message;
    }

    public async Task SetVoiceBindingAsync(BindingTargetViewModel? target, string audioPath)
    {
        await Task.CompletedTask;
        ShowCustomVoiceReferenceInstruction(target);
    }

    public void ShowCustomVoiceReferenceInstruction(BindingTargetViewModel? target)
    {
        const string message = "自定义音频已取消自动导入：请把音频和同名 .reference.txt 或 .txt 手动放到 ComfyUI\\models\\voices，然后在参考音频下拉框刷新后选择该文件。";
        if (target is not null && target.SupportsVoice)
        {
            target.StatusText = message;
        }

        StatusText = message;
    }

    public async Task SetVoiceReferenceSelectionAsync(BindingTargetViewModel? target, string? voiceReferenceId)
    {
        if (target is null ||
            !target.SupportsVoice ||
            string.IsNullOrWhiteSpace(voiceReferenceId) ||
            string.Equals(voiceReferenceId, CustomVoiceReferenceOption, StringComparison.Ordinal))
        {
            return;
        }
        if (RejectBindingEditsWhileWorkbenchMutationLocked(target)) return;

        try
        {
            var previousVoiceAsset = target.VoiceAsset;
            var voiceChanged = !string.Equals(previousVoiceAsset, voiceReferenceId, StringComparison.Ordinal);
            var projectVoiceSourceAsset = _projectVoiceAssetIdByOption.GetValueOrDefault(voiceReferenceId);
            target.VoiceAsset = voiceReferenceId;
            if (!string.IsNullOrWhiteSpace(projectVoiceSourceAsset))
            {
                target.VoiceSourceAsset = projectVoiceSourceAsset;
                target.VoiceSyncedPath = string.Empty;
            }
            else if (voiceChanged)
            {
                target.VoiceSourceAsset = string.Empty;
                target.VoiceSyncedPath = string.Empty;
            }

            var referenceText = await ReadVoiceReferenceTextForTargetAsync(target);
            if (!string.IsNullOrWhiteSpace(referenceText.Text))
            {
                target.ApplyVoiceReferenceText(referenceText.Text, referenceText.SourceText);
            }
            else if (voiceChanged && !IsUnboundVoiceReference(previousVoiceAsset))
            {
                target.ApplyVoiceReferenceText(string.Empty, string.Empty);
            }
            else
            {
                target.ApplyVoiceReferenceText(target.VoiceReferenceText, string.Empty);
            }

            ApplyBindingTargetToWorkspace(target);
            target.AcceptBindingEdits();
            target.StatusText = $"参考音频已选择：{voiceReferenceId}";
            StatusText = $"{target.DisplayName} 已选择参考音频：{voiceReferenceId}";
        }
        catch (Exception ex)
        {
            target.StatusText = "参考音频选择失败";
            HandleExternalError("参考音频选择失败", ex);
        }
    }

    private static bool IsUnboundVoiceReference(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ||
            value.Trim().StartsWith("未绑定", StringComparison.Ordinal);
    }

    public async Task SetImageBindingAsync(BindingTargetViewModel? target, string imagePath)
    {
        if (target is null || !target.SupportsImage || string.IsNullOrWhiteSpace(imagePath))
        {
            return;
        }
        if (RejectBindingEditsWhileWorkbenchMutationLocked(target)) return;

        try
        {
            var fullPath = Path.GetFullPath(imagePath);
            if (IsPsdFile(fullPath))
            {
                if (target.SupportsPsd)
                {
                    await GeneratePsdImageBindingFromFileAsync(target, fullPath);
                }
                else
                {
                    target.StatusText = "场景不支持 PSD";
                    StatusText = "场景图片请使用 PNG/JPG/WebP/BMP；PSD 仅支持人物绑定。";
                }

                return;
            }

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("Image file does not exist.", fullPath);
            }

            if (_workspaceProjectManifest is not null)
            {
                var import = await _workspaceService.ImportProjectMediaAsync(
                    CurrentWorkspaceProject(),
                    ProjectImageBindingPolicy.BuildImportRequest(target, fullPath));
                SetWorkspaceProjectManifest(import.Manifest);
                target.ImageAsset = import.Asset.Id;
                target.ImagePreviewAsset = import.Asset.AbsolutePath;
            }
            else
            {
                target.ImageAsset = fullPath;
                target.ImagePreviewAsset = fullPath;
            }
            target.StatusText = "Image bound";
            ApplyBindingTargetToWorkspace(target);
            StatusText = $"{target.DisplayName} 已绑定图片：{Path.GetFileName(fullPath)}";
        }
        catch (Exception ex)
        {
            target.StatusText = "图片绑定失败";
            HandleExternalError("图片绑定失败", ex);
        }
    }

    public async Task GeneratePsdImageBindingFromFileAsync(BindingTargetViewModel? target, string psdPath)
    {
        if (target is null || !target.SupportsPsd || string.IsNullOrWhiteSpace(psdPath))
        {
            return;
        }
        if (RejectBindingEditsWhileWorkbenchMutationLocked(target)) return;

        if (_workspaceProjectManifest is null)
        {
            StatusText = "Open or create a workspace project before using PSD.";
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(psdPath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("PSD file does not exist.", fullPath);
            }

            var previousImageAssetId = PreviousCharacterImageAssetId(target);
            target.StatusText = "Importing PSD";
            var workspace = CurrentWorkspaceProject();
            var import = await _workspaceService.ImportProjectMediaAsync(
                workspace,
                ProjectImageBindingPolicy.BuildImportRequest(target, fullPath));
            SetWorkspaceProjectManifest(import.Manifest);
            var defaultPreviewPath = TryRenderDefaultPsdPreview(import.Asset.Id, import.Asset.AbsolutePath, workspace);
            var defaultPreviewImport = await TryImportPsdDefaultPreviewAsync(
                target,
                defaultPreviewPath,
                import.Asset,
                workspace);
            var previousLayerMatch = ResolveCharacterPsdVisualSourceBinding(target)?.PsdLayerMatch ??
                target.VisualBinding.PsdLayerMatch ??
                _lastPsdLayerMatchTemplate;
            var migratedLayerMatch = TryMigratePsdLayerMatchConfiguration(previousLayerMatch, import.Asset.AbsolutePath);
            var sourceBinding = VisualAssetBinding.FromPsd(
                import.Asset.Id,
                defaultPreviewImport?.Asset.Id,
                layerMatch: migratedLayerMatch);
            target.ApplyPsdSourceBinding(import.Asset.Id, sourceBinding);
            target.ImagePreviewAsset = defaultPreviewImport?.Asset.AbsolutePath ?? defaultPreviewPath ?? string.Empty;
            target.ApplyVisualAssetDisplayNames(
                ResolveProjectAssetDisplayName(import.Asset),
                defaultPreviewImport is null ? null : ResolveProjectAssetDisplayName(defaultPreviewImport.Asset));
            ApplyPsdVisualSourceBinding(target, sourceBinding);
            ClearInheritedCharacterImageBindingsForPsdSource(target, previousImageAssetId);
            Project = await _workspaceService.ApplyWorkbenchDocumentAsync(
                Project,
                _workbenchSession.Document);
            _workbenchSession.Reset(_workbenchSession.Document);
            NotifyWorkbenchSessionStateChanged();
            RefreshBindingTarget(target);
            NotifyPsdLayerMatchTargetStateChanged();

            target.StatusText = "PSD source bound";
            StatusText = $"{target.DisplayName} 已绑定 PSD。点击工作台人物缩略图或图层预览来切换图层并生成 PNG。";
        }
        catch (Exception ex)
        {
            target.StatusText = "PSD 图片生成失败";
            HandleExternalError("PSD 图片生成失败", ex);
        }
    }

    private static string? PreviousCharacterImageAssetId(BindingTargetViewModel target)
    {
        return target.VisualBinding.SourceKind == VisualSourceKind.Image
            ? target.VisualBinding.EffectiveAssetId
            : null;
    }

    private void ClearInheritedCharacterImageBindingsForPsdSource(
        BindingTargetViewModel target,
        string? previousImageAssetId)
    {
        if (target.Kind != BindingTargetKind.Character ||
            string.IsNullOrWhiteSpace(previousImageAssetId))
        {
            return;
        }

        var rowIndex = BindingTargetRowIndex.FromScenes(SceneTables);
        var clearedAny = false;
        foreach (var row in RowsForBindingTarget(target, rowIndex))
        {
            if (row.CharacterVisualBinding is not { SourceKind: VisualSourceKind.Image } rowBinding ||
                !string.Equals(rowBinding.EffectiveAssetId, previousImageAssetId, StringComparison.Ordinal))
            {
                continue;
            }

            WorkbenchRowUpdateResult result;
            using (BeginWorkbenchDocumentApply())
            {
                result = _workbenchDraftService.UpdateRowCharacterVisualBindingWithResult(
                    SceneTables,
                    _workbenchSession,
                    row,
                    VisualAssetBinding.None);
            }

            clearedAny |= result.CommandResult.Applied;
        }

        if (!clearedAny)
        {
            return;
        }

        RefreshResolvedPreviewAssets();
        RefreshBindingTarget(target, rowIndex);
        ApplyDerivedStateRefresh(WorkbenchDerivedStateRefresh.SessionOnly);
    }

    public async Task SetWorkbenchRowImageBindingAsync(AnalysisResultRowViewModel? row, string imagePath)
    {
        if (row is null || string.IsNullOrWhiteSpace(imagePath))
        {
            return;
        }

        if (IsWorkbenchMutationLocked)
        {
            StatusText = $"{WorkbenchMutationLockReasonText}正在运行。请等待完成或取消后再绑定行图片。";
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(imagePath);
            if (IsPsdFile(fullPath))
            {
                StatusText = "旁白叙述和环境场效行只支持 PNG/JPG/WebP/BMP 图片，不支持 PSD。";
                return;
            }

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("Image file does not exist.", fullPath);
            }

            string imageAssetId;
            if (_workspaceProjectManifest is not null)
            {
                var import = await _workspaceService.ImportProjectMediaAsync(
                    CurrentWorkspaceProject(),
                    ProjectImageBindingPolicy.BuildRowImageImportRequest(
                        row.InteractionId,
                        $"{row.SubjectName} {row.OrderLabel}".Trim(),
                        fullPath));
                SetWorkspaceProjectManifest(import.Manifest);
                imageAssetId = import.Asset.Id;
            }
            else
            {
                imageAssetId = fullPath;
            }

            WorkbenchRowUpdateResult result;
            using (BeginWorkbenchDocumentApply())
            {
                result = _workbenchDraftService.UpdateRowCharacterVisualBindingWithResult(
                    SceneTables,
                    _workbenchSession,
                    row,
                    VisualAssetBinding.FromImage(imageAssetId));
            }

            if (!result.CommandResult.Applied)
            {
                return;
            }

            ApplyPreviewAssetRefresh(result);
            ApplyBindingTargetRefresh(result);
            ApplyDerivedStateRefresh(result);
            StatusText = $"{row.SubjectName} 第 {row.OrderLabel} 行已绑定图片：{Path.GetFileName(fullPath)}";
        }
        catch (Exception ex)
        {
            HandleExternalError("行图片绑定失败", ex);
        }
    }

    private PsdLayerMatchConfiguration? TryMigratePsdLayerMatchConfiguration(
        PsdLayerMatchConfiguration? previous,
        string psdPath)
    {
        if (previous is null || !previous.HasAnyGroup)
        {
            return null;
        }

        try
        {
            var groups = BuildPsdLayerGroupOptions(psdPath);
            return MigratePsdLayerMatchConfiguration(previous, groups);
        }
        catch (Exception ex)
        {
            AppRuntimeLog.Error("PSD 图层模板迁移失败", ex);
            return null;
        }
    }

    internal PsdLayerMatchConfiguration? TestMigratePsdLayerMatchConfiguration(
        PsdLayerMatchConfiguration? previous,
        IReadOnlyList<PsdLayerGroupOptionViewModel> groups)
    {
        if (previous is null || !previous.HasAnyGroup)
        {
            return null;
        }

        return MigratePsdLayerMatchConfiguration(previous, groups);
    }

    internal static PsdRenderSelection? TestBuildAiPsdRenderSelection(
        PsdLayerAiSelectionResult selection,
        PsdLayerMatchConfiguration layerMatchSettings,
        IReadOnlyList<PsdNativeLayerInfo> layers)
    {
        var renderGroups = BuildPsdLayerRenderGroups(layers, layerMatchSettings);
        return BuildAiPsdRenderSelection(
            selection,
            "asset_test_psd",
            "test.psd",
            "Calm",
            layerMatchSettings,
            renderGroups);
    }

    private static PsdLayerMatchConfiguration MigratePsdLayerMatchConfiguration(
        PsdLayerMatchConfiguration previous,
        IReadOnlyList<PsdLayerGroupOptionViewModel> groups)
    {
        return new PsdLayerMatchConfiguration(
            ExpressionGroup: MatchPsdLayerGroupCatalog(previous.ExpressionGroup, groups),
            BrowGroup: MatchPsdLayerGroupCatalog(previous.BrowGroup, groups),
            EyeGroup: MatchPsdLayerGroupCatalog(previous.EyeGroup, groups),
            MouthGroup: MatchPsdLayerGroupCatalog(previous.MouthGroup, groups),
            ActionGroup: MatchPsdLayerGroupCatalog(previous.ActionGroup, groups),
            LeftHandGroup: MatchPsdLayerGroupCatalog(previous.LeftHandGroup, groups),
            RightHandGroup: MatchPsdLayerGroupCatalog(previous.RightHandGroup, groups));
    }

    private static PsdLayerGroupCatalog? MatchPsdLayerGroupCatalog(
        PsdLayerGroupCatalog? previous,
        IReadOnlyList<PsdLayerGroupOptionViewModel> groups)
    {
        if (previous is null || string.IsNullOrWhiteSpace(previous.GroupName))
        {
            return null;
        }

        return groups
            .FirstOrDefault(group => string.Equals(group.Name, previous.GroupName, StringComparison.Ordinal))
            ?.ToCatalog();
    }

    private async Task<MediaImportResult?> TryImportPsdDefaultPreviewAsync(
        BindingTargetViewModel target,
        string? defaultPreviewPath,
        ProjectMediaAsset psdAsset,
        WorkspaceProject workspace)
    {
        if (string.IsNullOrWhiteSpace(defaultPreviewPath) ||
            !File.Exists(defaultPreviewPath))
        {
            return null;
        }

        try
        {
            var import = await _workspaceService.ImportProjectMediaAsync(
                workspace,
                ProjectImageBindingPolicy.BuildPsdDefaultPreviewImportRequest(
                    target,
                    defaultPreviewPath,
                    psdAsset.Id,
                    psdAsset.AbsolutePath));
            SetWorkspaceProjectManifest(import.Manifest);
            return import;
        }
        catch (Exception ex)
        {
            StatusText = $"PSD 默认预览导入失败：{Path.GetFileName(defaultPreviewPath)} ({ex.Message})";
            AppRuntimeLog.Error("PSD 默认预览导入失败", ex);
            return null;
        }
    }

    public async Task GeneratePsdImageBindingFromExistingSourceAsync(BindingTargetViewModel? target)
    {
        if (target is null || !target.SupportsPsd)
        {
            return;
        }
        if (RejectBindingEditsWhileWorkbenchMutationLocked(target)) return;

        if (_workspaceProjectManifest is null)
        {
            StatusText = "Open or create a workspace project before using PSD.";
            return;
        }

        try
        {
            var rowIndex = BindingTargetRowIndex.FromScenes(SceneTables);
            var source = ResolvePsdVisualSource(target, rowIndex);
            if (source is null)
            {
                target.StatusText = "PSD source missing";
                StatusText = $"{target.DisplayName} has no PSD source. Bind a PSD first.";
                return;
            }

            var intent = ResolvePsdIntent(target, rowIndex);
            await GeneratePsdImageBindingAsync(
                target,
                source.AssetId,
                source.Path,
                intent.Emotion,
                intent.Action,
                intent.SceneId,
                string.Empty,
                intent.Text,
                PsdComponentMode.Interactive);
        }
        catch (Exception ex)
        {
            target.StatusText = "PSD layer reselection failed";
            HandleExternalError("PSD layer reselection failed", ex);
        }
    }

    public PsdPreviewOpenRequest? BuildPsdLayerPreviewRequest(BindingTargetViewModel? target)
    {
        if (target is null || !target.SupportsPsd)
        {
            return null;
        }

        var source = ResolveCharacterPsdSource(target);
        if (source is null)
        {
            target.StatusText = "PSD source missing";
            StatusText = $"{target.DisplayName} 尚未绑定可预览的 PSD。";
            return null;
        }

        return new PsdPreviewOpenRequest(source.Path, $"{target.DisplayName} PSD 图层预览");
    }

    public BindingTargetViewModel? ResolveCharacterPsdLayerMatchTarget(BindingTargetViewModel? preferredTarget = null)
    {
        if (preferredTarget is { SupportsPsd: true } &&
            ResolveCharacterPsdSource(preferredTarget) is not null)
        {
            return preferredTarget;
        }

        return CharacterBindingTargets
            .FirstOrDefault(target => target.SupportsPsd && ResolveCharacterPsdSource(target) is not null) ??
            BindingTargets
                .FirstOrDefault(target =>
                    target.Kind == BindingTargetKind.Character &&
                    target.SupportsPsd &&
                    ResolveCharacterPsdSource(target) is not null);
    }

    public PsdLayerMatchConfiguration? ResolvePsdLayerMatchConfiguration(BindingTargetViewModel? target)
    {
        if (target is null || !target.SupportsPsd)
        {
            return null;
        }

        return ResolveCharacterPsdVisualSourceBinding(target)?.PsdLayerMatch ??
            target.VisualBinding.PsdLayerMatch;
    }

    public async Task ConfigurePsdLayerMatchSettingsAsync(BindingTargetViewModel? target)
    {
        await ConfigurePsdLayerMatchSettingsCoreAsync(target);
    }

    public async Task<bool> ConfigurePsdLayerMatchSettingsAndReportAsync(BindingTargetViewModel? target)
    {
        return await ConfigurePsdLayerMatchSettingsCoreAsync(target);
    }

    private async Task<bool> ConfigurePsdLayerMatchSettingsCoreAsync(BindingTargetViewModel? target)
    {
        if (target is null || !target.SupportsPsd)
        {
            return false;
        }
        if (RejectBindingEditsWhileWorkbenchMutationLocked(target)) return false;

        var source = ResolveCharacterPsdSource(target);
        if (source is null)
        {
            target.StatusText = "PSD source missing";
            StatusText = $"{target.DisplayName} 尚未绑定可读取的 PSD，无法设置图层匹配。";
            return false;
        }

        try
        {
            var groups = BuildPsdLayerGroupOptions(source.Path);
            if (groups.Count == 0)
            {
                target.StatusText = "PSD groups missing";
                StatusText = $"{target.DisplayName} 的 PSD 中没有可配置的图层组。";
                return false;
            }

            var currentBinding = ResolveCharacterPsdVisualSourceBinding(target) ?? target.VisualBinding;
            var session = new PsdLayerMatchSettingsViewModel(
                target.DisplayName,
                groups,
                currentBinding.PsdLayerMatch);
            if (!await ConfirmPsdLayerMatchSettingsAsync(session))
            {
                target.StatusText = "PSD 图层模板设置已取消";
                StatusText = "已取消 PSD 图层模板设置。";
                return false;
            }

            ApplyPsdLayerMatchConfiguration(target, session.BuildConfiguration());
            target.StatusText = "PSD 图层模板设置已保存";
            StatusText = $"{target.DisplayName} 的 PSD 图层模板设置已保存。";
            return true;
        }
        catch (Exception ex)
        {
            target.StatusText = "PSD 图层模板设置失败";
            HandleExternalError("PSD 图层模板设置失败", ex);
            return false;
        }
    }

    internal bool ApplyPsdLayerMatchConfiguration(
        BindingTargetViewModel target,
        PsdLayerMatchConfiguration configuration)
    {
        configuration = NormalizePsdLayerMatchConfiguration(configuration);
        if (!ApplyPsdLayerMatchConfigurationToTarget(target, configuration))
        {
            return false;
        }

        PropagatePsdLayerMatchConfigurationToRelatedPsdTargets(target, configuration);
        return true;
    }

    private bool ApplyPsdLayerMatchConfigurationToTarget(
        BindingTargetViewModel target,
        PsdLayerMatchConfiguration configuration)
    {
        if (target.Kind != BindingTargetKind.Character ||
            target.VisualBinding.SourceKind != VisualSourceKind.Psd)
        {
            return false;
        }

        var currentBinding = ResolveCharacterPsdVisualSourceBinding(target) ?? target.VisualBinding;
        if (currentBinding.SourceKind != VisualSourceKind.Psd)
        {
            return false;
        }

        var updatedBinding = currentBinding with
        {
            PsdLayerMatch = configuration
        };
        if (configuration.HasAnyGroup)
        {
            _lastPsdLayerMatchTemplate = configuration;
        }

        target.ApplyVisualBindingSummary(updatedBinding);
        ApplyCharacterVisualSourceBinding(target, updatedBinding);
        return true;
    }

    private int PropagatePsdLayerMatchConfigurationToRelatedPsdTargets(
        BindingTargetViewModel sourceTarget,
        PsdLayerMatchConfiguration configuration)
    {
        return PropagatePsdLayerMatchConfigurationToRelatedPsdTargets(
            sourceTarget,
            configuration,
            BuildPsdLayerGroupOptions);
    }

    private int PropagatePsdLayerMatchConfigurationToRelatedPsdTargets(
        BindingTargetViewModel sourceTarget,
        PsdLayerMatchConfiguration configuration,
        Func<string, IReadOnlyList<PsdLayerGroupOptionViewModel>> groupOptionsProvider)
    {
        if (!configuration.HasAnyGroup)
        {
            return 0;
        }

        var sourceTargetId = sourceTarget.TargetId;
        var relatedTargets = BindingTargets
            .Where(target => target.Kind == BindingTargetKind.Character && target.SupportsPsd)
            .Where(target => !string.Equals(target.TargetId, sourceTargetId, StringComparison.Ordinal))
            .Where(target =>
            {
                var currentBinding = ResolveCharacterPsdVisualSourceBinding(target) ?? target.VisualBinding;
                return currentBinding.SourceKind == VisualSourceKind.Psd &&
                    currentBinding.PsdLayerMatch is not { HasAnyGroup: true };
            })
            .GroupBy(target => target.TargetId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

        var appliedCount = 0;
        foreach (var relatedTarget in relatedTargets)
        {
            var source = ResolveCharacterPsdSource(relatedTarget);
            if (source is null)
            {
                continue;
            }

            PsdLayerMatchConfiguration? migrated;
            try
            {
                migrated = TestMigratePsdLayerMatchConfiguration(configuration, groupOptionsProvider(source.Path));
            }
            catch (Exception ex)
            {
                AppRuntimeLog.Error("PSD 图层模板自动同步失败", ex);
                continue;
            }

            if (migrated is not { HasAnyGroup: true })
            {
                continue;
            }

            if (ApplyPsdLayerMatchConfigurationToTarget(relatedTarget, NormalizePsdLayerMatchConfiguration(migrated)))
            {
                appliedCount++;
            }
        }

        return appliedCount;
    }

    internal int TestPropagatePsdLayerMatchConfigurationToRelatedPsdTargets(
        BindingTargetViewModel sourceTarget,
        PsdLayerMatchConfiguration configuration,
        Func<string, IReadOnlyList<PsdLayerGroupOptionViewModel>> groupOptionsProvider)
    {
        return PropagatePsdLayerMatchConfigurationToRelatedPsdTargets(
            sourceTarget,
            NormalizePsdLayerMatchConfiguration(configuration),
            groupOptionsProvider);
    }

    private static PsdLayerMatchConfiguration NormalizePsdLayerMatchConfiguration(PsdLayerMatchConfiguration configuration)
    {
        return configuration.ExpressionGroup is null
            ? configuration
            : configuration with
            {
                BrowGroup = null,
                EyeGroup = null,
                MouthGroup = null
            };
    }

    public async Task<bool> GeneratePsdImageBindingAsync(
        BindingTargetViewModel? target,
        string psdAssetId,
        string psdPath,
        string emotion,
        string action,
        string sceneId,
        string interactionId,
        string? text = null,
        PsdComponentMode mode = PsdComponentMode.Headless,
        bool reusePreviousSelection = true,
        PsdRenderSelection? selectionOverride = null)
    {
        if (target is null || !target.SupportsImage)
        {
            return false;
        }

        if (_workspaceProjectManifest is null)
        {
            StatusText = "Open or create a workspace project before using PSD.";
            return false;
        }

        try
        {
            target.StatusText = "Generating PSD PNG";
            var workspace = CurrentWorkspaceProject();
            var rowIndex = BindingTargetRowIndex.FromScenes(SceneTables);
            var targetRow = target.Kind == BindingTargetKind.Character && !string.IsNullOrWhiteSpace(interactionId)
                ? ResolvePsdTargetRow(target, rowIndex, interactionId)
                : null;
            if (target.Kind == BindingTargetKind.Character &&
                !string.IsNullOrWhiteSpace(interactionId) &&
                targetRow is null)
            {
                target.StatusText = "PSD target row missing";
                StatusText = $"{target.DisplayName} 没有找到 PSD 目标行：{interactionId}";
                return false;
            }

            if (targetRow is not null && !ValidateRowPsdSourceMatchesCharacterDefault(target, psdAssetId, out var startValidationError))
            {
                target.StatusText = "PSD source mismatch";
                StatusText = startValidationError;
                return false;
            }

            var previousSelection = reusePreviousSelection
                ? ResolvePreviousPsdSelection(target, rowIndex, targetRow)
                : null;
            var currentSourceFingerprint = PsdVisualBindingDiagnostics.BuildSourceFingerprint(psdAssetId, psdPath);
            var ignoredPreviousSelection = previousSelection is not null &&
                !PsdVisualBindingDiagnostics.SourceFingerprintMatches(previousSelection, currentSourceFingerprint);
            if (ignoredPreviousSelection)
            {
                previousSelection = null;
            }

            var enrichedText = target.Kind == BindingTargetKind.Character
                ? BuildPsdLayerMatchPromptText(target, text)
                : text;
            var result = await _psdLayerComponentClient.GenerateAndImportAsync(
                workspace,
                _workspaceProjectManifest,
                new PsdLayerComponentBindingRequest(
                    target.Kind,
                    target.TargetId,
                    target.DisplayName,
                    target.TargetId,
                    target.DisplayName,
                    null,
                    psdAssetId,
                    psdPath,
                    emotion,
                    action,
                    enrichedText,
                    sceneId,
                    interactionId,
                    PsdExportBounds.LayerBounds,
                    previousSelection,
                    selectionOverride,
                    mode),
                new PsdLayerComponentOptions(
                    PsdLayerComponentClient.ResolveDefaultComponentExecutablePath(),
                    TimeSpan.FromMinutes(2)));

            SetWorkspaceProjectManifest(result.Manifest);
            var renderedBinding = VisualAssetBinding.FromPsd(
                psdAssetId,
                result.ImportedAsset.Id,
                BuildPsdRenderSelection(result),
                ResolveCharacterPsdVisualSourceBinding(target)?.PsdLayerMatch ?? target.VisualBinding.PsdLayerMatch);
            var rowSpecificCharacterRender = target.Kind == BindingTargetKind.Character && targetRow is not null;
            var aggregatePreviewBeforeRowRender = rowSpecificCharacterRender ? target.ImagePreviewAsset : null;
            if (rowSpecificCharacterRender &&
                !ValidateRowPsdSourceMatchesCharacterDefault(target, renderedBinding.SourceAssetId, out var saveValidationError))
            {
                target.StatusText = "PSD source mismatch";
                StatusText = saveValidationError;
                return false;
            }

            if (rowSpecificCharacterRender)
            {
                target.StatusText = "PSD row image bound";
            }
            else
            {
                target.ImageAsset = psdAssetId;
                target.ImagePreviewAsset = result.ImportedAsset.AbsolutePath;
                target.StatusText = "PSD image bound";
                target.ApplyVisualBindingSummary(renderedBinding);
                ApplyBindingAssetDisplayNames(target);
            }

            var applied = ApplyPsdRenderedImageToWorkspace(target, rowIndex, renderedBinding, targetRow);
            if (rowSpecificCharacterRender &&
                !string.IsNullOrWhiteSpace(aggregatePreviewBeforeRowRender) &&
                string.Equals(target.ImagePreviewAsset, result.ImportedAsset.AbsolutePath, StringComparison.Ordinal))
            {
                target.ImagePreviewAsset = aggregatePreviewBeforeRowRender;
            }

            if (applied)
            {
                StatusText = ignoredPreviousSelection
                    ? $"{target.DisplayName} PSD changed; previous layer selection ignored and rematched: {Path.GetFileName(result.ImportedAsset.AbsolutePath)}"
                    : $"{target.DisplayName} PSD exported PNG bound: {Path.GetFileName(result.ImportedAsset.AbsolutePath)}";
            }

            return applied;
        }
        catch (PsdLayerSelectionCancelledException)
        {
            target.StatusText = "PSD 图层选择已取消";
            StatusText = "PSD 图层选择已取消或切换到新的目标。";
            return false;
        }
        catch (Exception ex)
        {
            target.StatusText = "PSD 图片生成失败";
            HandleExternalError("PSD 图片生成失败", ex);
            return false;
        }
    }

    public async Task<bool> GenerateSelectedRowPsdImageFromCharacterSourceAsync(AnalysisResultRowViewModel? row)
    {
        if (IsPsdLayerAiMatchingRunning || IsProjectExportRunning)
        {
            StatusText = $"{WorkbenchMutationLockReasonText}中已锁定行缩略图 PSD 图层重选。请等待完成或取消后再操作。";
            return false;
        }

        if (row is null || !row.UsesCharacterVisualPreview)
        {
            return false;
        }

        var target = ResolveCharacterBindingTargetForRow(row);
        if (target is null)
        {
            return false;
        }

        var source = ResolveCharacterPsdSource(target);
        if (source is null)
        {
            return false;
        }

        SelectedRow = row;
        var intent = ResolvePsdIntentForRow(row);
        return await GeneratePsdImageBindingAsync(
            target,
            source.AssetId,
            source.Path,
            intent.Emotion,
            intent.Action,
            intent.SceneId,
            intent.InteractionId,
            intent.Text,
            PsdComponentMode.Interactive);
    }

    [RelayCommand]
    private async Task MatchPsdExpressionLayersAsync()
    {
        if (IsWorkbenchMutationLocked)
        {
            StatusText = $"{WorkbenchMutationLockReasonText}中已锁定工作台 PSD 图层匹配。";
            return;
        }

        if (_workspaceProjectManifest is null)
        {
            StatusText = "Open or create a workspace project before using PSD.";
            return;
        }

        var plans = BuildPsdExpressionMatchPlans();
        if (plans.Count == 0)
        {
            StatusText = "没有找到可匹配的角色 PSD。请先在角色管理中绑定 PSD。";
            return;
        }

        var missingSettings = plans
            .FirstOrDefault(plan => plan.LayerMatchSettings is null || !plan.LayerMatchSettings.HasAnyGroup);
        if (missingSettings is not null)
        {
            StatusText = $"{missingSettings.Target.DisplayName} 尚未设置 PSD 图层模板。请先在角色素材后点击“PSD图层模板设置”。";
            return;
        }

        SyncWorkbenchSessionFromViewModels();
        IsPsdLayerAiMatchingRunning = true;
        try
        {
            if (!await EnsureAiConnectionReadyAsync("角色状态 AI 匹配"))
            {
                return;
            }

            var aiSelector = new PsdLayerAiSelectionService(CreateAiModelClient());
            var appliedCount = 0;
            foreach (var group in GroupPsdExpressionMatchPlans(plans))
            {
                var renderGroups = BuildPsdLayerRenderGroups(group.Source.Path, group.LayerMatchSettings);
                var processedInGroup = 0;
                foreach (var batchPlans in group.Plans.Chunk(PsdLayerAiRowsPerRequest))
                {
                    StatusText = $"正在进行角色状态 AI 匹配：{group.Target.DisplayName} / {Math.Min(processedInGroup + batchPlans.Length, group.Plans.Count)}/{group.Plans.Count} 行";
                    var batchGroup = group with { Plans = batchPlans };
                    var selections = await aiSelector.SelectRowsAsync(BuildPsdLayerAiBatchSelectionRequest(batchGroup));
                    var selectionsByInteractionId = selections.ToDictionary(selection => selection.InteractionId, StringComparer.Ordinal);
                    foreach (var plan in batchPlans)
                    {
                        if (!selectionsByInteractionId.TryGetValue(plan.Intent.InteractionId, out var rowSelection))
                        {
                            throw new InvalidOperationException($"角色状态 AI 缺少行结果：{plan.Intent.InteractionId}");
                        }

                        var aiSelection = rowSelection.Selection;
                        var selectionOverride = BuildAiPsdRenderSelection(
                            aiSelection,
                            plan.Source.AssetId,
                            plan.Source.Path,
                            plan.Intent.Emotion,
                            batchGroup.LayerMatchSettings,
                            renderGroups);
                        if (selectionOverride is null)
                        {
                            continue;
                        }

                        if (await GeneratePsdImageBindingAsync(
                                plan.Target,
                                plan.Source.AssetId,
                                plan.Source.Path,
                                plan.Intent.Emotion,
                                plan.Intent.Action,
                                plan.Intent.SceneId,
                                plan.Intent.InteractionId,
                                BuildPsdAiSelectionSummaryText(plan.Intent.Text, aiSelection),
                                PsdComponentMode.Headless,
                                reusePreviousSelection: false,
                                selectionOverride))
                        {
                            appliedCount++;
                        }
                    }

                    processedInGroup += batchPlans.Length;
                }
            }

            StatusText = appliedCount == 0
                ? "未生成角色状态匹配结果。"
                : $"已根据 AI 角色状态结果切换 {appliedCount} 行 PSD 图层。";
        }
        finally
        {
            IsPsdLayerAiMatchingRunning = false;
        }
    }

    private static IReadOnlyList<PsdExpressionMatchGroup> GroupPsdExpressionMatchPlans(
        IReadOnlyList<PsdExpressionMatchPlan> plans)
    {
        return plans
            .Where(plan => plan.LayerMatchSettings is not null)
            .GroupBy(
                plan => new PsdExpressionMatchGroupKey(
                    plan.Target.TargetId,
                    plan.Source.AssetId,
                    Path.GetFullPath(plan.Source.Path)),
                (key, groupPlans) =>
                {
                    var orderedPlans = groupPlans
                        .OrderBy(plan => plan.Row.OwnerScene?.Order ?? 0)
                        .ThenBy(plan => plan.Row.Order)
                        .ToArray();
                    var first = orderedPlans[0];
                    return new PsdExpressionMatchGroup(
                        key,
                        first.Target,
                        first.Source,
                        first.LayerMatchSettings!,
                        orderedPlans);
                })
            .ToArray();
    }

    private List<PsdExpressionMatchPlan> BuildPsdExpressionMatchPlans()
    {
        var plans = new List<PsdExpressionMatchPlan>();
        var targetsById = BindingTargets
            .Where(target => target.Kind == BindingTargetKind.Character)
            .Where(target => !string.IsNullOrWhiteSpace(target.TargetId))
            .ToDictionary(target => target.TargetId, StringComparer.Ordinal);
        var targetsByName = BindingTargets
            .Where(target => target.Kind == BindingTargetKind.Character)
            .Where(target => !string.IsNullOrWhiteSpace(target.DisplayName))
            .GroupBy(target => target.DisplayName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var row in SceneTables.SelectMany(scene => scene.Rows).Where(row => row.UsesCharacterVisualPreview))
        {
            var characterId = row.SubjectCharacterId ?? row.SourceRoleCharacterId;
            var characterName = row.SubjectName ?? row.SourceRole;
            var target = !string.IsNullOrWhiteSpace(characterId) &&
                targetsById.TryGetValue(characterId, out var byId)
                    ? byId
                    : characterName is not null && targetsByName.TryGetValue(characterName, out var byName)
                        ? byName
                        : ResolveCharacterBindingTargetForRow(row);
            if (target is null)
            {
                continue;
            }

            var source = ResolveCharacterPsdSource(target);
            if (source is null)
            {
                continue;
            }

            var layerMatchSettings = ResolveCharacterPsdVisualSourceBinding(target)?.PsdLayerMatch ?? target.VisualBinding.PsdLayerMatch;
            plans.Add(new PsdExpressionMatchPlan(row, target, source, ResolvePsdIntentForRow(row), layerMatchSettings));
        }

        return plans;
    }

    private IReadOnlyList<PsdLayerGroupOptionViewModel> BuildPsdLayerGroupOptions(string psdPath)
    {
        var document = new NativePsdDocumentReader().Load(psdPath);
        var childrenByParent = document.Layers
            .GroupBy(layer => layer.ParentLayerId ?? int.MinValue)
            .ToDictionary(group => group.Key, group => group.ToArray());
        return document.Layers
            .Where(layer => layer.IsGroup)
            .Select(layer => BuildPsdLayerGroupOption(layer, childrenByParent))
            .Where(option => option.LayerNames.Count > 0)
            .OrderByDescending(option => option.IsExpressionCandidate || option.IsActionCandidate)
            .ThenBy(option => option.Path, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, PsdLayerRenderGroup> BuildPsdLayerRenderGroups(
        string psdPath,
        PsdLayerMatchConfiguration configuration)
    {
        try
        {
            var document = new NativePsdDocumentReader().Load(psdPath);
            return BuildPsdLayerRenderGroups(document.Layers, configuration);
        }
        catch (Exception ex)
        {
            AppRuntimeLog.Error("PSD 渲染图层读取失败", ex);
            return new Dictionary<string, PsdLayerRenderGroup>(StringComparer.Ordinal);
        }
    }

    private static IReadOnlyDictionary<string, PsdLayerRenderGroup> BuildPsdLayerRenderGroups(
        IReadOnlyList<PsdNativeLayerInfo> layers,
        PsdLayerMatchConfiguration configuration)
    {
        var groupNames = new[]
            {
                configuration.BrowGroup,
                configuration.EyeGroup,
                configuration.MouthGroup,
                configuration.LeftHandGroup,
                configuration.RightHandGroup
            }
            .Where(group => !string.IsNullOrWhiteSpace(group?.GroupName))
            .Select(group => group!.GroupName.Trim())
            .ToHashSet(StringComparer.Ordinal);
        if (groupNames.Count == 0)
        {
            return new Dictionary<string, PsdLayerRenderGroup>(StringComparer.Ordinal);
        }

        var childrenByParent = layers
            .GroupBy(layer => layer.ParentLayerId ?? int.MinValue)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var layersById = layers
            .GroupBy(layer => layer.LayerId)
            .ToDictionary(group => group.Key, group => group.First());
        var renderGroups = new Dictionary<string, PsdLayerRenderGroup>(StringComparer.Ordinal);

        foreach (var group in layers.Where(layer => layer.IsGroup && groupNames.Contains(layer.Name.Trim())))
        {
            var groupName = group.Name.Trim();
            if (renderGroups.ContainsKey(groupName))
            {
                continue;
            }

            var descendants = PsdDescendantLayers(group, childrenByParent)
                .Where(IsNamedPsdLeafLayer)
                .ToArray();
            var controlledLayerNames = UniquePsdLayerNames(descendants);
            var controlledLayerIds = UniquePsdLayerIds(descendants);
            var candidateLayerNames = BuildPsdCandidateLayerNameMap(group, childrenByParent);
            var candidateLayerIds = BuildPsdCandidateLayerIdMap(group, childrenByParent);
            var defaultLayerNames = UniquePsdLayerNames(
                descendants.Where(layer => IsPsdLayerEffectivelyVisible(layer, layersById)));
            var defaultLayerIds = UniquePsdLayerIds(
                descendants.Where(layer => IsPsdLayerEffectivelyVisible(layer, layersById)));
            if (defaultLayerNames.Count == 0 && candidateLayerNames.Count > 0)
            {
                defaultLayerNames = candidateLayerNames.Values.First();
            }
            if (defaultLayerIds.Count == 0 && candidateLayerIds.Count > 0)
            {
                defaultLayerIds = candidateLayerIds.Values.First();
            }

            renderGroups[groupName] = new PsdLayerRenderGroup(
                groupName,
                candidateLayerNames,
                candidateLayerIds,
                defaultLayerNames,
                defaultLayerIds,
                controlledLayerNames,
                controlledLayerIds);
        }

        return renderGroups;
    }

    private static PsdLayerGroupOptionViewModel BuildPsdLayerGroupOption(
        PsdNativeLayerInfo group,
        IReadOnlyDictionary<int, PsdNativeLayerInfo[]> childrenByParent)
    {
        var layerNames = BuildPsdSwitchCandidateNames(group, childrenByParent);
        return new PsdLayerGroupOptionViewModel(
            group.LayerId,
            group.Name,
            BuildPsdGroupPath(group, childrenByParent),
            layerNames);
    }

    private static IReadOnlyList<string> BuildPsdSwitchCandidateNames(
        PsdNativeLayerInfo group,
        IReadOnlyDictionary<int, PsdNativeLayerInfo[]> childrenByParent)
    {
        var directCandidates = new List<string>();
        if (childrenByParent.TryGetValue(group.LayerId, out var children))
        {
            foreach (var child in children)
            {
                if (child.IsGroup)
                {
                    if (PsdDescendantLayers(child, childrenByParent).Any(IsNamedPsdLeafLayer))
                    {
                        AddPsdCandidateName(directCandidates, child.Name);
                    }
                }
                else
                {
                    AddPsdCandidateName(directCandidates, child.Name);
                }
            }
        }

        if (directCandidates.Count > 0)
        {
            return directCandidates
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        return PsdDescendantLayers(group, childrenByParent)
            .Where(IsNamedPsdLeafLayer)
            .Select(layer => layer.Name.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildPsdCandidateLayerNameMap(
        PsdNativeLayerInfo group,
        IReadOnlyDictionary<int, PsdNativeLayerInfo[]> childrenByParent)
    {
        var candidates = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (childrenByParent.TryGetValue(group.LayerId, out var children))
        {
            foreach (var child in children)
            {
                if (child.IsGroup)
                {
                    var childLayerNames = UniquePsdLayerNames(
                        PsdDescendantLayers(child, childrenByParent).Where(IsNamedPsdLeafLayer));
                    if (childLayerNames.Count == 0)
                    {
                        continue;
                    }

                    AddPsdCandidateLayerNames(candidates, child.Name, childLayerNames);
                    foreach (var childLayerName in childLayerNames)
                    {
                        AddPsdCandidateLayerNames(candidates, childLayerName, childLayerNames);
                    }
                }
                else if (TryNormalizePsdLayerName(child.Name, out var layerName))
                {
                    AddPsdCandidateLayerNames(candidates, layerName, [layerName]);
                }
            }
        }

        foreach (var layerName in UniquePsdLayerNames(PsdDescendantLayers(group, childrenByParent).Where(IsNamedPsdLeafLayer)))
        {
            AddPsdCandidateLayerNames(candidates, layerName, [layerName]);
        }

        return candidates;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<int>> BuildPsdCandidateLayerIdMap(
        PsdNativeLayerInfo group,
        IReadOnlyDictionary<int, PsdNativeLayerInfo[]> childrenByParent)
    {
        var candidates = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
        if (childrenByParent.TryGetValue(group.LayerId, out var children))
        {
            foreach (var child in children)
            {
                if (child.IsGroup)
                {
                    var childLayerIds = UniquePsdLayerIds(
                        PsdDescendantLayers(child, childrenByParent).Where(IsNamedPsdLeafLayer));
                    if (childLayerIds.Count == 0)
                    {
                        continue;
                    }

                    AddPsdCandidateLayerIds(candidates, child.Name, childLayerIds);
                    foreach (var childLayerName in UniquePsdLayerNames(
                        PsdDescendantLayers(child, childrenByParent).Where(IsNamedPsdLeafLayer)))
                    {
                        AddPsdCandidateLayerIds(candidates, childLayerName, childLayerIds);
                    }
                }
                else if (TryNormalizePsdLayerName(child.Name, out var layerName))
                {
                    AddPsdCandidateLayerIds(candidates, layerName, [child.LayerId]);
                }
            }
        }

        foreach (var leafLayer in PsdDescendantLayers(group, childrenByParent).Where(IsNamedPsdLeafLayer))
        {
            AddPsdCandidateLayerIds(candidates, leafLayer.Name, [leafLayer.LayerId]);
        }

        return candidates;
    }

    private static void AddPsdCandidateName(ICollection<string> names, string? name)
    {
        if (TryNormalizePsdLayerName(name, out var normalized))
        {
            names.Add(normalized);
        }
    }

    private static void AddPsdCandidateLayerNames(
        IDictionary<string, IReadOnlyList<string>> candidates,
        string? candidateName,
        IReadOnlyList<string> layerNames)
    {
        if (!TryNormalizePsdLayerName(candidateName, out var normalized) || layerNames.Count == 0)
        {
            return;
        }

        if (!candidates.ContainsKey(normalized))
        {
            candidates[normalized] = layerNames;
        }
    }

    private static void AddPsdCandidateLayerIds(
        IDictionary<string, IReadOnlyList<int>> candidates,
        string? candidateName,
        IReadOnlyList<int> layerIds)
    {
        if (!TryNormalizePsdLayerName(candidateName, out var normalized) || layerIds.Count == 0)
        {
            return;
        }

        if (!candidates.ContainsKey(normalized))
        {
            candidates[normalized] = layerIds;
        }
    }

    private static IReadOnlyList<string> UniquePsdLayerNames(IEnumerable<PsdNativeLayerInfo> layers)
    {
        return layers
            .Select(layer => layer.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<int> UniquePsdLayerIds(IEnumerable<PsdNativeLayerInfo> layers)
    {
        return layers
            .Select(layer => layer.LayerId)
            .Distinct()
            .ToArray();
    }

    private static bool IsNamedPsdLeafLayer(PsdNativeLayerInfo layer)
    {
        return !layer.IsGroup &&
            layer.Type != PsdSectionDividerLayerType &&
            layer.Width > 0 &&
            layer.Height > 0 &&
            !string.IsNullOrWhiteSpace(layer.Name);
    }

    private static bool TryNormalizePsdLayerName(string? name, out string normalized)
    {
        normalized = string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
        return normalized.Length > 0;
    }

    private static bool IsPsdLayerEffectivelyVisible(
        PsdNativeLayerInfo layer,
        IReadOnlyDictionary<int, PsdNativeLayerInfo> layersById)
    {
        var current = layer;
        while (true)
        {
            if (!current.IsVisible)
            {
                return false;
            }

            if (current.ParentLayerId is null)
            {
                return true;
            }

            if (!layersById.TryGetValue(current.ParentLayerId.Value, out var parent))
            {
                return true;
            }

            current = parent;
        }
    }

    private static IEnumerable<PsdNativeLayerInfo> PsdDescendantLayers(
        PsdNativeLayerInfo group,
        IReadOnlyDictionary<int, PsdNativeLayerInfo[]> childrenByParent)
    {
        if (!childrenByParent.TryGetValue(group.LayerId, out var children))
        {
            yield break;
        }

        foreach (var child in children)
        {
            yield return child;
            foreach (var descendant in PsdDescendantLayers(child, childrenByParent))
            {
                yield return descendant;
            }
        }
    }

    private static string BuildPsdGroupPath(
        PsdNativeLayerInfo group,
        IReadOnlyDictionary<int, PsdNativeLayerInfo[]> childrenByParent)
    {
        var parentById = childrenByParent
            .SelectMany(pair => pair.Value)
            .Where(layer => layer.ParentLayerId is not null)
            .ToDictionary(layer => layer.LayerId, layer => layer.ParentLayerId!.Value);
        var layersById = childrenByParent
            .SelectMany(pair => pair.Value)
            .GroupBy(layer => layer.LayerId)
            .ToDictionary(grouping => grouping.Key, grouping => grouping.First());
        var names = new List<string>();
        var current = group;
        while (true)
        {
            if (!string.IsNullOrWhiteSpace(current.Name))
            {
                names.Add(current.Name);
            }

            if (!parentById.TryGetValue(current.LayerId, out var parentId) ||
                !layersById.TryGetValue(parentId, out var parent))
            {
                break;
            }

            current = parent;
        }

        names.Reverse();
        return string.Join("/", names);
    }

    private string? BuildPsdLayerMatchPromptText(BindingTargetViewModel target, string? rowText)
    {
        var settings = ResolveCharacterPsdVisualSourceBinding(target)?.PsdLayerMatch ?? target.VisualBinding.PsdLayerMatch;
        if (settings is null || !settings.HasAnyGroup)
        {
            return rowText;
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(rowText))
        {
            parts.Add(rowText.Trim());
        }

        if (settings.ActionGroup is { } actionGroup)
        {
            parts.Add($"整体动作状态：根据文本分析整体动作，只能从这些固定词汇中选择：{string.Join("，", actionGroup.LayerNames)}。");
        }

        var actionParts = new List<string>();
        if (settings.LeftHandGroup is { } leftHandGroup)
        {
            actionParts.Add($"左手[{leftHandGroup.GroupName}]只能选择：{string.Join("，", leftHandGroup.LayerNames)}");
        }

        if (settings.RightHandGroup is { } rightHandGroup)
        {
            actionParts.Add($"右手[{rightHandGroup.GroupName}]只能选择：{string.Join("，", rightHandGroup.LayerNames)}");
        }

        if (actionParts.Count > 0)
        {
            parts.Add($"手部状态：根据文本分别描述[左手][右手]状态，{string.Join("；", actionParts)}。");
        }

        var facialGroups = new List<string>();
        if (settings.BrowGroup is { } browGroup)
        {
            facialGroups.Add($"眉[{browGroup.GroupName}]只能选择：{string.Join("，", browGroup.LayerNames)}");
        }

        if (settings.EyeGroup is { } eyeGroup)
        {
            facialGroups.Add($"眼[{eyeGroup.GroupName}]只能选择：{string.Join("，", eyeGroup.LayerNames)}");
        }

        if (settings.MouthGroup is { } mouthGroup)
        {
            facialGroups.Add($"嘴[{mouthGroup.GroupName}]只能选择：{string.Join("，", mouthGroup.LayerNames)}");
        }

        if (facialGroups.Count > 0)
        {
            parts.Add($"眉眼嘴状态：根据文本和情绪分别描述[眉][眼][嘴]状态，{string.Join("；", facialGroups)}。");
        }

        return string.Join(Environment.NewLine, parts);
    }

    private PsdLayerAiBatchSelectionRequest BuildPsdLayerAiBatchSelectionRequest(PsdExpressionMatchGroup group)
    {
        return new PsdLayerAiBatchSelectionRequest(
            group.Target.DisplayName,
            group.Plans.Select(BuildPsdLayerAiSelectionRowRequest).ToArray(),
            group.LayerMatchSettings);
    }

    private PsdLayerAiSelectionRowRequest BuildPsdLayerAiSelectionRowRequest(PsdExpressionMatchPlan plan)
    {
        return new PsdLayerAiSelectionRowRequest(
            plan.Intent.InteractionId,
            plan.Intent.Text ?? plan.Row.LineText);
    }

    private static PsdRenderSelection? BuildAiPsdRenderSelection(
        PsdLayerAiSelectionResult selection,
        string psdAssetId,
        string psdPath,
        string fallbackEmotion,
        PsdLayerMatchConfiguration layerMatchSettings,
        IReadOnlyDictionary<string, PsdLayerRenderGroup> renderGroups)
    {
        var slots = BuildAiResolvedLayerSlots(selection, layerMatchSettings, renderGroups);
        var selectedLayerIds = slots
            .SelectMany(slot => slot.LayerIds)
            .Distinct()
            .ToArray();
        var selectedLayerNames = slots
            .SelectMany(slot => slot.LayerNames)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var controlledLayerIds = slots
            .SelectMany(slot => slot.ControlledLayerIds)
            .Distinct()
            .ToArray();
        var controlledLayerNames = slots
            .SelectMany(slot => slot.ControlledLayerNames)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if ((selectedLayerNames.Length == 0 && selectedLayerIds.Length == 0) ||
            (controlledLayerNames.Length == 0 && controlledLayerIds.Length == 0))
        {
            return null;
        }

        var fingerprint = PsdVisualBindingDiagnostics.BuildSourceFingerprint(psdAssetId, psdPath);
        var renderCacheKey = string.Join("|", selectedLayerIds) + "|" + string.Join("|", selectedLayerNames) +
            "|controlled=" + string.Join(",", controlledLayerIds) + "|" + string.Join(",", controlledLayerNames);
        return new PsdRenderSelection(
            selectedLayerIds,
            selectedLayerNames,
            "ai_structured",
            fallbackEmotion,
            string.Empty,
            VisualExportBounds.LayerBounds,
            fingerprint,
            $"psd-ai-v1|{fingerprint}|{renderCacheKey}",
            controlledLayerNames,
            controlledLayerIds);
    }

    private static IReadOnlyList<PsdAiResolvedLayerSlot> BuildAiResolvedLayerSlots(
        PsdLayerAiSelectionResult selection,
        PsdLayerMatchConfiguration configuration,
        IReadOnlyDictionary<string, PsdLayerRenderGroup> renderGroups)
    {
        var slots = new List<PsdAiResolvedLayerSlot>();
        AddAiResolvedLayerSlot(slots, selection.Brow, configuration.BrowGroup, renderGroups);
        AddAiResolvedLayerSlot(slots, selection.Eye, configuration.EyeGroup, renderGroups);
        AddAiResolvedLayerSlot(slots, selection.Mouth, configuration.MouthGroup, renderGroups);
        AddAiResolvedLayerSlot(slots, selection.LeftHand, configuration.LeftHandGroup, renderGroups);
        AddAiResolvedLayerSlot(slots, selection.RightHand, configuration.RightHandGroup, renderGroups);
        return slots;
    }

    private static void AddAiResolvedLayerSlot(
        ICollection<PsdAiResolvedLayerSlot> slots,
        string selectedValue,
        PsdLayerGroupCatalog? group,
        IReadOnlyDictionary<string, PsdLayerRenderGroup> renderGroups)
    {
        if (group is null || group.LayerNames.Count == 0)
        {
            return;
        }

        var renderGroup = ResolvePsdLayerRenderGroup(group, renderGroups);
        if (renderGroup is null && renderGroups.Count > 0)
        {
            return;
        }

        var selectedLayerNames = new List<string>();
        var selectedLayerIds = new List<int>();
        AddSelectedOrDefaultLayerName(selectedLayerNames, selectedValue, group, renderGroups);
        AddSelectedOrDefaultLayerId(selectedLayerIds, selectedValue, group, renderGroups);
        if (selectedLayerNames.Count == 0 && selectedLayerIds.Count == 0)
        {
            return;
        }

        if (renderGroups.Count > 0 && selectedLayerIds.Count == 0)
        {
            return;
        }

        var controlledLayerNames = new List<string>();
        var controlledLayerIds = new List<int>();
        AddControlledLayerNames(controlledLayerNames, group, renderGroups);
        AddControlledLayerIds(controlledLayerIds, group, renderGroups);
        if (controlledLayerNames.Count == 0 && controlledLayerIds.Count == 0)
        {
            return;
        }

        if (renderGroups.Count > 0 && controlledLayerIds.Count == 0)
        {
            return;
        }

        slots.Add(new PsdAiResolvedLayerSlot(
            selectedLayerIds.Distinct().ToArray(),
            selectedLayerNames.Distinct(StringComparer.Ordinal).ToArray(),
            controlledLayerIds.Distinct().ToArray(),
            controlledLayerNames.Distinct(StringComparer.Ordinal).ToArray()));
    }

    private static IReadOnlyList<string> BuildAiSelectedLayerNames(
        PsdLayerAiSelectionResult selection,
        PsdLayerMatchConfiguration configuration,
        IReadOnlyDictionary<string, PsdLayerRenderGroup> renderGroups)
    {
        var layerNames = new List<string>();
        AddSelectedOrDefaultLayerName(layerNames, selection.Brow, configuration.BrowGroup, renderGroups);
        AddSelectedOrDefaultLayerName(layerNames, selection.Eye, configuration.EyeGroup, renderGroups);
        AddSelectedOrDefaultLayerName(layerNames, selection.Mouth, configuration.MouthGroup, renderGroups);
        AddSelectedOrDefaultLayerName(layerNames, selection.LeftHand, configuration.LeftHandGroup, renderGroups);
        AddSelectedOrDefaultLayerName(layerNames, selection.RightHand, configuration.RightHandGroup, renderGroups);
        return layerNames.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<int> BuildAiSelectedLayerIds(
        PsdLayerAiSelectionResult selection,
        PsdLayerMatchConfiguration configuration,
        IReadOnlyDictionary<string, PsdLayerRenderGroup> renderGroups)
    {
        var layerIds = new List<int>();
        AddSelectedOrDefaultLayerId(layerIds, selection.Brow, configuration.BrowGroup, renderGroups);
        AddSelectedOrDefaultLayerId(layerIds, selection.Eye, configuration.EyeGroup, renderGroups);
        AddSelectedOrDefaultLayerId(layerIds, selection.Mouth, configuration.MouthGroup, renderGroups);
        AddSelectedOrDefaultLayerId(layerIds, selection.LeftHand, configuration.LeftHandGroup, renderGroups);
        AddSelectedOrDefaultLayerId(layerIds, selection.RightHand, configuration.RightHandGroup, renderGroups);
        return layerIds.Distinct().ToArray();
    }

    private static IReadOnlyList<string> BuildAiControlledLayerNames(
        PsdLayerMatchConfiguration configuration,
        IReadOnlyDictionary<string, PsdLayerRenderGroup> renderGroups)
    {
        var layerNames = new List<string>();
        AddControlledLayerNames(layerNames, configuration.BrowGroup, renderGroups);
        AddControlledLayerNames(layerNames, configuration.EyeGroup, renderGroups);
        AddControlledLayerNames(layerNames, configuration.MouthGroup, renderGroups);
        AddControlledLayerNames(layerNames, configuration.LeftHandGroup, renderGroups);
        AddControlledLayerNames(layerNames, configuration.RightHandGroup, renderGroups);
        return layerNames.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<int> BuildAiControlledLayerIds(
        PsdLayerMatchConfiguration configuration,
        IReadOnlyDictionary<string, PsdLayerRenderGroup> renderGroups)
    {
        var layerIds = new List<int>();
        AddControlledLayerIds(layerIds, configuration.BrowGroup, renderGroups);
        AddControlledLayerIds(layerIds, configuration.EyeGroup, renderGroups);
        AddControlledLayerIds(layerIds, configuration.MouthGroup, renderGroups);
        AddControlledLayerIds(layerIds, configuration.LeftHandGroup, renderGroups);
        AddControlledLayerIds(layerIds, configuration.RightHandGroup, renderGroups);
        return layerIds.Distinct().ToArray();
    }

    private static void AddSelectedOrDefaultLayerName(
        ICollection<string> layerNames,
        string selectedValue,
        PsdLayerGroupCatalog? group,
        IReadOnlyDictionary<string, PsdLayerRenderGroup> renderGroups)
    {
        if (group is null || group.LayerNames.Count == 0)
        {
            return;
        }

        var renderGroup = ResolvePsdLayerRenderGroup(group, renderGroups);
        if (!string.IsNullOrWhiteSpace(selectedValue))
        {
            var normalized = selectedValue.Trim();
            if (group.LayerNames.Any(name => string.Equals(name, normalized, StringComparison.Ordinal)) ||
                renderGroup?.CandidateLayerNames.ContainsKey(normalized) == true)
            {
                AddResolvedPsdLayerNames(layerNames, normalized, group, renderGroup);
                return;
            }
        }

        foreach (var defaultLayerName in DefaultLayerNames(group, renderGroup))
        {
            if (!string.IsNullOrWhiteSpace(defaultLayerName))
            {
                layerNames.Add(defaultLayerName);
            }
        }
    }

    private static void AddSelectedOrDefaultLayerId(
        ICollection<int> layerIds,
        string selectedValue,
        PsdLayerGroupCatalog? group,
        IReadOnlyDictionary<string, PsdLayerRenderGroup> renderGroups)
    {
        if (group is null || group.LayerNames.Count == 0)
        {
            return;
        }

        var renderGroup = ResolvePsdLayerRenderGroup(group, renderGroups);
        if (!string.IsNullOrWhiteSpace(selectedValue))
        {
            var normalized = selectedValue.Trim();
            if (group.LayerNames.Any(name => string.Equals(name, normalized, StringComparison.Ordinal)) ||
                renderGroup?.CandidateLayerIds.ContainsKey(normalized) == true)
            {
                AddResolvedPsdLayerIds(layerIds, normalized, renderGroup);
                return;
            }
        }

        foreach (var defaultLayerId in DefaultLayerIds(renderGroup))
        {
            layerIds.Add(defaultLayerId);
        }
    }

    private static IReadOnlyList<string> DefaultLayerNames(
        PsdLayerGroupCatalog group,
        PsdLayerRenderGroup? renderGroup)
    {
        if (renderGroup?.DefaultLayerNames.Count > 0)
        {
            return renderGroup.DefaultLayerNames;
        }

        var firstLayerName = group.LayerNames.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
        if (string.IsNullOrWhiteSpace(firstLayerName))
        {
            return [];
        }

        var normalized = firstLayerName.Trim();
        return renderGroup?.CandidateLayerNames.TryGetValue(normalized, out var candidateLayerNames) == true &&
            candidateLayerNames.Count > 0
                ? candidateLayerNames
                : [normalized];
    }

    private static IReadOnlyList<int> DefaultLayerIds(PsdLayerRenderGroup? renderGroup)
    {
        return renderGroup?.DefaultLayerIds.Count > 0 ? renderGroup.DefaultLayerIds : [];
    }

    private static void AddControlledLayerNames(
        ICollection<string> layerNames,
        PsdLayerGroupCatalog? group,
        IReadOnlyDictionary<string, PsdLayerRenderGroup> renderGroups)
    {
        if (group is null)
        {
            return;
        }

        var renderGroup = ResolvePsdLayerRenderGroup(group, renderGroups);
        if (renderGroup?.ControlledLayerNames.Count > 0)
        {
            foreach (var layerName in renderGroup.ControlledLayerNames)
            {
                if (!string.IsNullOrWhiteSpace(layerName))
                {
                    layerNames.Add(layerName);
                }
            }

            return;
        }

        foreach (var layerName in group.LayerNames)
        {
            if (!string.IsNullOrWhiteSpace(layerName))
            {
                layerNames.Add(layerName);
            }
        }
    }

    private static void AddControlledLayerIds(
        ICollection<int> layerIds,
        PsdLayerGroupCatalog? group,
        IReadOnlyDictionary<string, PsdLayerRenderGroup> renderGroups)
    {
        if (group is null)
        {
            return;
        }

        var renderGroup = ResolvePsdLayerRenderGroup(group, renderGroups);
        if (renderGroup?.ControlledLayerIds.Count > 0)
        {
            foreach (var layerId in renderGroup.ControlledLayerIds)
            {
                layerIds.Add(layerId);
            }
        }
    }

    private static PsdLayerRenderGroup? ResolvePsdLayerRenderGroup(
        PsdLayerGroupCatalog group,
        IReadOnlyDictionary<string, PsdLayerRenderGroup> renderGroups)
    {
        return !string.IsNullOrWhiteSpace(group.GroupName) &&
            renderGroups.TryGetValue(group.GroupName.Trim(), out var renderGroup)
                ? renderGroup
                : null;
    }

    private static void AddResolvedPsdLayerNames(
        ICollection<string> layerNames,
        string selectedValue,
        PsdLayerGroupCatalog group,
        PsdLayerRenderGroup? renderGroup)
    {
        if (renderGroup?.CandidateLayerNames.TryGetValue(selectedValue, out var resolvedLayerNames) == true &&
            resolvedLayerNames.Count > 0)
        {
            foreach (var layerName in resolvedLayerNames)
            {
                layerNames.Add(layerName);
            }

            return;
        }

        if (group.LayerNames.Any(name => string.Equals(name, selectedValue, StringComparison.Ordinal)))
        {
            layerNames.Add(selectedValue);
        }
    }

    private static void AddResolvedPsdLayerIds(
        ICollection<int> layerIds,
        string selectedValue,
        PsdLayerRenderGroup? renderGroup)
    {
        if (renderGroup?.CandidateLayerIds.TryGetValue(selectedValue, out var resolvedLayerIds) == true &&
            resolvedLayerIds.Count > 0)
        {
            foreach (var layerId in resolvedLayerIds)
            {
                layerIds.Add(layerId);
            }
        }
    }

    private static string BuildPsdAiSelectionSummaryText(string? rowText, PsdLayerAiSelectionResult selection)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(rowText))
        {
            parts.Add(rowText.Trim());
        }

        parts.Add($"角色状态选择：眉={selection.Brow}; 眼={selection.Eye}; 嘴={selection.Mouth}; 左手={selection.LeftHand}; 右手={selection.RightHand}");
        return string.Join(Environment.NewLine, parts);
    }

    private BindingTargetViewModel? ResolveCharacterBindingTargetForRow(AnalysisResultRowViewModel row)
    {
        if (!row.UsesCharacterVisualPreview)
        {
            return null;
        }

        return BindingTargets.FirstOrDefault(target =>
                target.Kind == BindingTargetKind.Character &&
                !string.IsNullOrWhiteSpace(row.SubjectCharacterId ?? row.SourceRoleCharacterId) &&
                string.Equals(target.TargetId, row.SubjectCharacterId ?? row.SourceRoleCharacterId, StringComparison.Ordinal)) ??
            BindingTargets.FirstOrDefault(target =>
                target.Kind == BindingTargetKind.Character &&
                (string.Equals(target.DisplayName, row.SubjectName, StringComparison.Ordinal) ||
                 string.Equals(target.DisplayName, row.SourceRole, StringComparison.Ordinal)));
    }

    private PsdBindingIntent ResolvePsdIntent(BindingTargetViewModel target)
    {
        return ResolvePsdIntent(target, BindingTargetRowIndex.FromScenes(SceneTables));
    }

    private PsdBindingIntent ResolvePsdIntent(
        BindingTargetViewModel target,
        BindingTargetRowIndex rowIndex)
    {
        if (target.Kind == BindingTargetKind.Scene)
        {
            var targetScene = SceneTables.FirstOrDefault(scene => string.Equals(scene.SceneId, target.TargetId, StringComparison.Ordinal));
            return new PsdBindingIntent(
                "Calm",
                NormalizePsdIntentText(targetScene?.SceneEffect, "Scene"),
                target.TargetId,
                target.TargetId,
                targetScene is null ? target.DisplayName : $"{targetScene.SceneName} {targetScene.SceneEffect}".Trim());
        }

        var targetRows = RowsForBindingTarget(target, rowIndex);
        AnalysisResultRowViewModel? firstTargetRow = null;
        var selectedRowMatches = false;
        foreach (var row in targetRows)
        {
            firstTargetRow ??= row;
            if (ReferenceEquals(row, SelectedRow))
            {
                selectedRowMatches = true;
                break;
            }
        }

        var selectedRow = SelectedRow is not null && selectedRowMatches
            ? SelectedRow
            : firstTargetRow;
        var scene = selectedRow is null
            ? SceneTables.Count > 0 ? SceneTables[0] : null
            : selectedRow.OwnerScene ?? FindSceneForRow(selectedRow);

        return new PsdBindingIntent(
            NormalizePsdIntentText(selectedRow?.EffectiveEmotionText, "Calm"),
            ResolvePsdActionIntentForRow(selectedRow),
            scene?.SceneId ?? "scene",
            selectedRow?.InteractionId ?? "interaction",
            selectedRow?.LineText);
    }

    private PsdBindingIntent ResolvePsdIntentForRow(AnalysisResultRowViewModel row)
    {
        var scene = row.OwnerScene ?? FindSceneForRow(row);
        return new PsdBindingIntent(
            NormalizePsdIntentText(row.EffectiveEmotionText, "Calm"),
            ResolvePsdActionIntentForRow(row),
            scene?.SceneId ?? "scene",
            row.InteractionId,
            row.LineText);
    }

    private string ResolvePsdActionIntentForRow(AnalysisResultRowViewModel? row)
    {
        if (row is null)
        {
            return "Idle";
        }

        var parts = new List<string>();
        var contextAction = ResolveNearbyPsdActionText(row);
        if (!string.IsNullOrWhiteSpace(contextAction))
        {
            parts.Add(contextAction);
        }

        if (!string.IsNullOrWhiteSpace(row.ActionText))
        {
            parts.Add(row.ActionText.Trim());
        }

        if (!string.IsNullOrWhiteSpace(row.LineText))
        {
            parts.Add("台词");
        }

        return NormalizePsdIntentText(string.Join(" ", parts.Distinct(StringComparer.Ordinal)), "Idle");
    }

    private string? ResolveNearbyPsdActionText(AnalysisResultRowViewModel row)
    {
        var scene = row.OwnerScene ?? FindSceneForRow(row);
        if (scene is null)
        {
            return null;
        }

        var rowIndex = scene.Rows.IndexOf(row);
        if (rowIndex < 0)
        {
            return null;
        }

        var characterNames = ResolvePsdCharacterNames(row);
        if (characterNames.Count == 0)
        {
            return null;
        }

        var candidates = scene.Rows
            .Select((candidate, index) => new
            {
                Row = candidate,
                Index = index,
                Distance = Math.Abs(index - rowIndex)
            })
            .Where(candidate => !ReferenceEquals(candidate.Row, row) && candidate.Distance <= 2)
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Index)
            .ToArray();
        foreach (var candidate in candidates)
        {
            var actionText = ExtractRoleActionText(candidate.Row, characterNames);
            if (!string.IsNullOrWhiteSpace(actionText))
            {
                return actionText;
            }
        }

        return null;
    }

    private IReadOnlyList<string> ResolvePsdCharacterNames(AnalysisResultRowViewModel row)
    {
        var names = new List<string>();
        AddPsdCharacterName(names, row.SubjectName);
        AddPsdCharacterName(names, row.SourceRole);
        if (!string.IsNullOrWhiteSpace(row.SubjectCharacterId))
        {
            var character = Project.Characters.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, row.SubjectCharacterId, StringComparison.Ordinal));
            AddPsdCharacterName(names, character?.Name);
        }

        if (!string.IsNullOrWhiteSpace(row.SourceRoleCharacterId))
        {
            var character = Project.Characters.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, row.SourceRoleCharacterId, StringComparison.Ordinal));
            AddPsdCharacterName(names, character?.Name);
        }

        return names.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string? ExtractRoleActionText(
        AnalysisResultRowViewModel row,
        IReadOnlyList<string> characterNames)
    {
        if (!row.IsNarration)
        {
            return null;
        }

        var text = FirstBound(row.LineText, row.ActionText);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = text.Trim();
        var isRoleAction = normalized.StartsWith("角色动作", StringComparison.Ordinal) ||
            normalized.StartsWith("动作", StringComparison.Ordinal) ||
            row.ActionText.Trim().StartsWith("角色动作", StringComparison.Ordinal) ||
            row.ActionText.Trim().StartsWith("动作", StringComparison.Ordinal) ||
            ContainsNaturalRoleAction(normalized);
        if (!isRoleAction)
        {
            return null;
        }

        if (!characterNames.Any(name => normalized.Contains(name, StringComparison.Ordinal)))
        {
            return null;
        }

        return normalized;
    }

    private static bool ContainsNaturalRoleAction(string text)
    {
        return NaturalPsdActionTokens.Any(token => text.Contains(token, StringComparison.Ordinal));
    }

    private static readonly string[] NaturalPsdActionTokens =
    [
        "抬手", "举手", "挥手", "招手", "伸手", "收回", "指向", "指着", "摸头", "摸摸", "低头",
        "抬头", "转身", "回头", "握拳", "攥紧", "摊手", "抱臂", "跑来", "走来", "停住", "停在"
    ];

    private static void AddPsdCharacterName(ICollection<string> names, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value.Trim(), "旁白", StringComparison.Ordinal))
        {
            return;
        }

        names.Add(value.Trim());
    }

    private void ApplyPsdVisualSourceBinding(
        BindingTargetViewModel target,
        VisualAssetBinding visualBinding)
    {
        if (target.Kind == BindingTargetKind.Character)
        {
            ApplyCharacterVisualSourceBinding(target, visualBinding);
            return;
        }

        if (target.Kind == BindingTargetKind.Scene)
        {
            ApplySceneVisualBinding(target, visualBinding);
        }
    }

    private void ApplyCharacterVisualSourceBinding(
        BindingTargetViewModel target,
        VisualAssetBinding visualBinding)
    {
        if (target.Kind != BindingTargetKind.Character)
        {
            return;
        }

        WorkbenchDocument updatedDocument;
        using (BeginWorkbenchDocumentApply())
        {
            updatedDocument = _workbenchSession.Execute(
                document => new WorkbenchCommandService().ApplyCharacterVisualSourceBinding(
                    document,
                    target.TargetId,
                    visualBinding),
                WorkbenchSessionOperationNames.ApplyBindingTarget);
        }

        if (!ReferenceEquals(updatedDocument, _workbenchSession.Document))
        {
            return;
        }

        RefreshResolvedPreviewAssets();
        RefreshBindingTarget(target);
        NotifyPsdLayerMatchTargetStateChanged();
        ApplyDerivedStateRefresh(WorkbenchDerivedStateRefresh.SessionOnly);
    }

    private void ApplySceneVisualBinding(
        BindingTargetViewModel target,
        VisualAssetBinding visualBinding)
    {
        var scene = SceneTables.FirstOrDefault(scene => string.Equals(scene.SceneId, target.TargetId, StringComparison.Ordinal));
        if (scene is null)
        {
            return;
        }

        WorkbenchSceneUpdateResult result;
        using (BeginWorkbenchDocumentApply())
        {
            result = _workbenchDraftService.UpdateSceneVisualBindingWithResult(
                SceneTables,
                _workbenchSession,
                scene,
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

    private bool ApplyPsdRenderedImageToWorkspace(
        BindingTargetViewModel target,
        BindingTargetRowIndex rowIndex,
        VisualAssetBinding visualBinding,
        AnalysisResultRowViewModel? targetRow)
    {
        if (target.Kind == BindingTargetKind.Scene)
        {
            return ApplySceneVisualBindingWithResult(target, visualBinding);
        }

        return targetRow is null
            ? ApplyCharacterPsdVisualBindingWithResult(target, visualBinding)
            : ApplyPsdRenderedImageToCurrentRow(target, rowIndex, visualBinding, targetRow);
    }

    private bool ApplySceneVisualBindingWithResult(
        BindingTargetViewModel target,
        VisualAssetBinding visualBinding)
    {
        var scene = SceneTables.FirstOrDefault(scene => string.Equals(scene.SceneId, target.TargetId, StringComparison.Ordinal));
        if (scene is null)
        {
            return false;
        }

        WorkbenchSceneUpdateResult result;
        using (BeginWorkbenchDocumentApply())
        {
            result = _workbenchDraftService.UpdateSceneVisualBindingWithResult(
                SceneTables,
                _workbenchSession,
                scene,
                visualBinding);
        }

        if (!result.CommandResult.Applied)
        {
            return false;
        }

        ApplyPreviewAssetRefresh(result);
        ApplyBindingTargetRefresh(result);
        ApplyDerivedStateRefresh(result);
        return true;
    }

    private bool ApplyCharacterPsdVisualBindingWithResult(
        BindingTargetViewModel target,
        VisualAssetBinding visualBinding)
    {
        if (target.Kind != BindingTargetKind.Character)
        {
            return false;
        }

        var previousDocument = _workbenchSession.Document;
        WorkbenchDocument updatedDocument;
        using (BeginWorkbenchDocumentApply())
        {
            updatedDocument = _workbenchSession.Execute(
                document => new WorkbenchCommandService().ApplyCharacterVisualSourceBinding(
                    document,
                    target.TargetId,
                    visualBinding),
                WorkbenchSessionOperationNames.ApplyBindingTarget);
        }

        if (ReferenceEquals(updatedDocument, previousDocument))
        {
            return false;
        }

        RefreshResolvedPreviewAssets();
        RefreshBindingTarget(target);
        NotifyPsdLayerMatchTargetStateChanged();
        ApplyDerivedStateRefresh(WorkbenchDerivedStateRefresh.SessionOnly);
        return true;
    }

    private bool ApplyPsdRenderedImageToCurrentRow(
        BindingTargetViewModel target,
        BindingTargetRowIndex rowIndex,
        VisualAssetBinding visualBinding,
        AnalysisResultRowViewModel? targetRow)
    {
        if (target.Kind == BindingTargetKind.Scene)
        {
            return false;
        }

        var row = targetRow ?? ResolvePsdTargetRow(target, rowIndex);
        if (row is null)
        {
            StatusText = $"{target.DisplayName} 的 PSD PNG 已生成，但没有找到可绑定的工作台行。";
            return false;
        }

        if (!ContainsWorkbenchRow(row) || !TargetMatchesRow(target, row))
        {
            StatusText = $"{target.DisplayName} 的 PSD PNG 已生成，但目标行已变化，请重新选择该行。";
            return false;
        }

        if (!ValidateRowPsdSourceMatchesCharacterDefault(target, visualBinding.SourceAssetId, out var validationError))
        {
            StatusText = validationError;
            return false;
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
            return false;
        }

        ApplyPreviewAssetRefresh(result);
        ApplyBindingTargetRefresh(result);
        ApplyDerivedStateRefresh(result);
        return true;
    }

    private AnalysisResultRowViewModel? ResolvePsdTargetRow(
        BindingTargetViewModel target,
        BindingTargetRowIndex rowIndex,
        string? interactionId = null)
    {
        var targetRows = RowsForBindingTarget(target, rowIndex);
        AnalysisResultRowViewModel? firstTargetRow = null;
        var requiresInteractionMatch = !string.IsNullOrWhiteSpace(interactionId);
        foreach (var row in targetRows)
        {
            firstTargetRow ??= row;
            if (requiresInteractionMatch &&
                string.Equals(row.InteractionId, interactionId, StringComparison.Ordinal))
            {
                return row;
            }

            if (!requiresInteractionMatch && ReferenceEquals(row, SelectedRow))
            {
                return row;
            }
        }

        if (requiresInteractionMatch &&
            SelectedRow is not null &&
            TargetMatchesRow(target, SelectedRow))
        {
            return SelectedRow;
        }

        return requiresInteractionMatch ? null : firstTargetRow;
    }

    private PsdRenderSelection? ResolvePreviousPsdSelection(
        BindingTargetViewModel target,
        BindingTargetRowIndex rowIndex,
        AnalysisResultRowViewModel? targetRow = null)
    {
        if (target.Kind == BindingTargetKind.Scene)
        {
            var scene = SceneTables.FirstOrDefault(scene => string.Equals(scene.SceneId, target.TargetId, StringComparison.Ordinal));
            return scene?.VisualBinding?.PsdSelection;
        }

        if (targetRow is not null)
        {
            return targetRow.CharacterVisualBinding?.PsdSelection;
        }

        var documentBinding = _workbenchSession.Document.CharacterBindings.FirstOrDefault(binding =>
            string.Equals(binding.CharacterId, target.TargetId, StringComparison.Ordinal));
        return documentBinding?.VisualBinding?.PsdSelection ?? target.VisualBinding.PsdSelection;
    }

    private bool ValidateRowPsdSourceMatchesCharacterDefault(
        BindingTargetViewModel target,
        string? psdAssetId,
        out string errorMessage)
    {
        errorMessage = string.Empty;
        if (target.Kind != BindingTargetKind.Character || string.IsNullOrWhiteSpace(psdAssetId))
        {
            errorMessage = $"{target.DisplayName} 的行级图层必须使用角色管理中绑定的 PSD。";
            return false;
        }

        var defaultBinding = ResolveCharacterPsdVisualSourceBinding(target);
        if (defaultBinding is not { SourceKind: VisualSourceKind.Psd } ||
            string.IsNullOrWhiteSpace(defaultBinding.SourceAssetId))
        {
            errorMessage = $"{target.DisplayName} 尚未在角色管理中绑定 PSD。";
            return false;
        }

        if (!string.Equals(defaultBinding.SourceAssetId, psdAssetId, StringComparison.Ordinal))
        {
            errorMessage = $"{target.DisplayName} 的行级图层只能使用角色管理中绑定的同一个 PSD。";
            return false;
        }

        return true;
    }

    private PsdVisualSource? ResolvePsdVisualSource(
        BindingTargetViewModel target,
        BindingTargetRowIndex rowIndex)
    {
        var binding = target.Kind == BindingTargetKind.Character
            ? ResolveCharacterPsdVisualSourceBinding(target) ?? ResolvePsdVisualBinding(target, rowIndex)
            : ResolvePsdVisualBinding(target, rowIndex);
        if (binding is not { SourceKind: VisualSourceKind.Psd } ||
            string.IsNullOrWhiteSpace(binding.SourceAssetId))
        {
            return null;
        }

        var workspace = CurrentWorkspaceProject();
        var resolution = _projectAssetResolver.Resolve(binding.SourceAssetId, workspace, _workspaceProjectManifest);
        var path = resolution?.AbsolutePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = binding.SourceAssetId;
        }

        return File.Exists(path)
            ? new PsdVisualSource(binding.SourceAssetId, path)
            : null;
    }

    private PsdVisualSource? ResolveCharacterPsdSource(BindingTargetViewModel target)
    {
        var binding = ResolveCharacterPsdVisualSourceBinding(target);
        if (binding is not { SourceKind: VisualSourceKind.Psd } ||
            string.IsNullOrWhiteSpace(binding.SourceAssetId))
        {
            return null;
        }

        var workspace = CurrentWorkspaceProject();
        var resolution = _projectAssetResolver.Resolve(binding.SourceAssetId, workspace, _workspaceProjectManifest);
        var path = resolution?.AbsolutePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = binding.SourceAssetId;
        }

        return File.Exists(path)
            ? new PsdVisualSource(binding.SourceAssetId, path)
            : null;
    }

    private VisualAssetBinding? ResolveCharacterPsdVisualSourceBinding(BindingTargetViewModel target)
    {
        if (target.Kind != BindingTargetKind.Character)
        {
            return null;
        }

        var documentBinding = _workbenchSession.Document.CharacterBindings.FirstOrDefault(binding =>
            string.Equals(binding.CharacterId, target.TargetId, StringComparison.Ordinal));
        if (documentBinding?.VisualBinding is { SourceKind: VisualSourceKind.Psd } defaultBinding)
        {
            return defaultBinding;
        }

        return target.VisualBinding.SourceKind == VisualSourceKind.Psd ? target.VisualBinding : null;
    }

    private VisualAssetBinding? ResolvePsdVisualBinding(
        BindingTargetViewModel target,
        BindingTargetRowIndex rowIndex)
    {
        if (target.Kind == BindingTargetKind.Scene)
        {
            var scene = SceneTables.FirstOrDefault(scene => string.Equals(scene.SceneId, target.TargetId, StringComparison.Ordinal));
            return scene?.VisualBinding ?? target.VisualBinding;
        }

        var row = ResolvePsdTargetRow(target, rowIndex);
        return row?.CharacterVisualBinding ?? target.VisualBinding;
    }

    private static PsdRenderSelection BuildPsdRenderSelection(PsdLayerComponentBindingResult result)
    {
        var match = result.Response.Match;
        var metadata = result.Response.Metadata;
        var sourcePsdAssetId = metadata?.SourcePsdAssetId ?? result.ImportedAsset.Metadata.GetValueOrDefault("sourcePsdAssetId") ?? string.Empty;
        var sourcePsdPath = metadata?.SourcePsdPath ?? result.ImportedAsset.Metadata.GetValueOrDefault("sourcePsdPath") ?? string.Empty;
        var sourceFingerprint = FirstBound(
            metadata?.SourceFingerprint,
            result.ImportedAsset.Metadata.GetValueOrDefault("sourceFingerprint"),
            PsdVisualBindingDiagnostics.BuildSourceFingerprint(sourcePsdAssetId, sourcePsdPath))!;
        var cacheKey = FirstBound(
            metadata?.RenderCacheKey,
            result.ImportedAsset.Metadata.GetValueOrDefault("renderCacheKey"),
            PsdVisualSourceMetadata.BuildRenderCacheKey(
                sourcePsdAssetId,
                sourceFingerprint,
                match?.LayerIds ?? [],
                result.Response.Png?.Bounds ?? PsdExportBounds.CanvasBounds))!;
        return new PsdRenderSelection(
            match?.LayerIds ?? [],
            match?.LayerNames ?? [],
            match?.MatchedPresetId,
            match?.Emotion,
            match?.Action,
            ToVisualExportBounds(result.Response.Png?.Bounds ?? PsdExportBounds.CanvasBounds),
            sourceFingerprint,
            cacheKey);
    }

    private static VisualExportBounds ToVisualExportBounds(PsdExportBounds bounds)
    {
        return bounds switch
        {
            PsdExportBounds.LayerBounds => VisualExportBounds.LayerBounds,
            PsdExportBounds.PreviewBounds => VisualExportBounds.PreviewBounds,
            _ => VisualExportBounds.CanvasBounds
        };
    }

    private static string? FirstBound(params string?[] values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            var value = values[index];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private SceneTableViewModel? FindSceneForRow(AnalysisResultRowViewModel row)
    {
        foreach (var scene in SceneTables)
        {
            for (var index = 0; index < scene.Rows.Count; index++)
            {
                if (ReferenceEquals(scene.Rows[index], row))
                {
                    return scene;
                }
            }
        }

        return null;
    }

    partial void OnIsBindingDialogOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(BindingPanelToggleText));
        if (value)
        {
            _ = RefreshVoiceCatalogSafeAsync();
        }
    }

    partial void OnSelectedBindingTargetChanged(BindingTargetViewModel? value)
    {
        SyncSelectedBindingTargetSections(value);
    }

    partial void OnSelectedSceneBindingTargetChanged(BindingTargetViewModel? value)
    {
        if (value is not null)
        {
            SelectedBindingTarget = value;
        }
    }

    partial void OnSelectedCharacterBindingTargetChanged(BindingTargetViewModel? value)
    {
        if (value is not null)
        {
            SelectedBindingTarget = value;
        }

        NotifyPsdLayerMatchTargetStateChanged();
    }

    private void NotifyPsdLayerMatchTargetStateChanged()
    {
        OnPropertyChanged(nameof(CharacterPsdLayerMatchTarget));
        OnPropertyChanged(nameof(CanConfigureSelectedCharacterPsdLayerMatch));
        OnPropertyChanged(nameof(CharacterPsdLayerMatchSettingsText));
    }

    partial void OnSelectedNarrationBindingTargetChanged(BindingTargetViewModel? value)
    {
        if (value is not null)
        {
            SelectedBindingTarget = value;
        }
    }

    partial void OnUseRoleVoiceForDialogueChanged(bool value)
    {
        OnRoleVoiceRouteStateChanged();
    }

    partial void OnUseRoleVoiceForThoughtChanged(bool value)
    {
        OnRoleVoiceRouteStateChanged();
    }

    partial void OnUseRoleVoiceForActionChanged(bool value)
    {
        OnRoleVoiceRouteStateChanged();
    }

    partial void OnUseRoleVoiceForAllCharacterLinesChanged(bool value)
    {
        OnRoleVoiceRouteStateChanged();
    }

    private void OnRoleVoiceRouteStateChanged()
    {
        OnPropertyChanged(nameof(RoleDialogueVoiceRouteText));
        OnPropertyChanged(nameof(RoleSceneNarrationVoiceRouteText));
        OnPropertyChanged(nameof(RoleThoughtVoiceRouteText));
        OnPropertyChanged(nameof(RoleActionVoiceRouteText));
        OnPropertyChanged(nameof(RoleAllVoiceRouteText));
        OnPropertyChanged(nameof(ShowRoleVoiceDetailRouteButtons));
        OnPropertyChanged(nameof(CanEditCharacterVoiceBindings));
        RefreshCharacterVoiceBindingEditAccess();
    }

    private void RefreshCharacterVoiceBindingEditAccess()
    {
        var canEdit = CanEditCharacterVoiceBindings && CanEditWorkbenchStructure;
        foreach (var target in CharacterBindingTargets)
        {
            target.ApplyVoiceBindingEditAccess(canEdit);
        }

        var canEditNarrationVoice = CanEditWorkbenchStructure;
        foreach (var target in NarrationBindingTargets)
        {
            target.ApplyVoiceBindingEditAccess(canEditNarrationVoice);
        }
    }

    private void RefreshBindingTargets(string reason)
    {
        _workbenchBindingRefreshMetrics = _workbenchBindingRefreshMetrics.RecordFullRefresh(reason);
        WorkbenchBindingProjection.RefreshTargets(
            BindingTargets,
            SceneTables,
            CurrentWorkbenchCharactersForBindingTargets(),
            ResolveProjectAssetPath,
            Project.GenerationTasks);
        ApplyBindingAssetDisplayNames();
        RefreshCharacterVoiceBindingEditAccess();
    }

    private IReadOnlyList<Character> CurrentWorkbenchCharactersForBindingTargets()
    {
        return CurrentWorkbenchCharactersForVoiceRouting(_workbenchSession.Document);
    }

    private static IReadOnlyList<Character> FilterCharactersForWorkbench(
        IReadOnlyList<Character> characters,
        WorkbenchDocument document)
    {
        var filtered = new List<Character>(characters.Count);
        for (var index = 0; index < characters.Count; index++)
        {
            var character = characters[index];
            if (string.Equals(character.Name, "旁白", StringComparison.Ordinal) ||
                IsManualWorkbenchCharacter(character) ||
                DocumentHasCharacterVisualRows(document, character))
            {
                filtered.Add(character);
            }
        }

        return filtered.Count == characters.Count ? characters : filtered.ToArray();
    }

    private static bool DocumentHasCharacterVisualRows(WorkbenchDocument document, Character character)
    {
        foreach (var scene in document.Scenes)
        {
            foreach (var row in scene.Rows)
            {
                if (!AnalysisResultRowViewModel.UsesCharacterVisualPreviewForDraft(row))
                {
                    continue;
                }

                if (string.Equals(character.Id, row.SubjectCharacterId, StringComparison.Ordinal) ||
                    string.Equals(character.Id, row.SourceRoleCharacterId, StringComparison.Ordinal) ||
                    string.Equals(character.Name, row.SubjectName, StringComparison.Ordinal) ||
                    string.Equals(character.Name, row.SourceRole, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsManualWorkbenchCharacter(Character character)
    {
        return character.Id.StartsWith("char_manual_", StringComparison.Ordinal);
    }

    private void RenameCharacterBindingTarget(
        BindingTargetViewModel target,
        Character character,
        string nextName)
    {
        var previousName = character.Name;
        var renamedCharacter = character with { Name = nextName };
        Project = Project with
        {
            Characters = Project.Characters
                .Select(candidate => string.Equals(candidate.Id, target.TargetId, StringComparison.Ordinal)
                    ? renamedCharacter
                    : candidate)
                .ToArray()
        };
        Replace(Characters, Project.Characters);
        RefreshWorkbenchCharacterProjection();

        var changedRows = RenameRowsForCharacter(target.TargetId, previousName, nextName);

        RefreshBindingTargets("rename_character_target");
        SelectedBindingTarget = BindingTargets.FirstOrDefault(candidate =>
            candidate.Kind == BindingTargetKind.Character &&
            string.Equals(candidate.TargetId, target.TargetId, StringComparison.Ordinal));
        if (SelectedBindingTarget is not null)
        {
            SelectedBindingTarget.AcceptDisplayNameEdit(nextName);
        }

        StatusText = $"角色已重命名：{previousName} -> {nextName}";
    }

    private void MergeCharacterBindingTarget(
        BindingTargetViewModel target,
        Character sourceCharacter,
        Character targetCharacter)
    {
        Project = Project with
        {
            Characters = Project.Characters
                .Where(candidate => !string.Equals(candidate.Id, sourceCharacter.Id, StringComparison.Ordinal))
                .ToArray(),
            Scenes = Project.Scenes
                .Select(scene => scene with
                {
                    Interactions = scene.Interactions
                        .Select(interaction => ReassignInteractionCharacter(
                            interaction,
                            sourceCharacter.Id,
                            targetCharacter.Id))
                        .ToArray()
                })
                .ToArray()
        };
        Replace(Characters, Project.Characters);
        RefreshWorkbenchCharacterProjection();

        var changedRows = ReassignRowsForCharacter(
            sourceCharacter.Id,
            sourceCharacter.Name,
            targetCharacter.Id,
            targetCharacter.Name);

        SyncWorkbenchSessionFromViewModels();
        RefreshBindingTargets("merge_character_target");
        SelectedBindingTarget = BindingTargets.FirstOrDefault(candidate =>
            candidate.Kind == BindingTargetKind.Character &&
            string.Equals(candidate.TargetId, targetCharacter.Id, StringComparison.Ordinal));
        target.CancelDisplayNameEdit();
        StatusText = $"角色已合并：{sourceCharacter.Name} -> {targetCharacter.Name}，已更新 {changedRows.Count} 行。";
    }

    private static CharacterInteraction ReassignInteractionCharacter(
        CharacterInteraction interaction,
        string sourceCharacterId,
        string targetCharacterId)
    {
        var subjectCharacterId = string.Equals(interaction.SubjectCharacterId, sourceCharacterId, StringComparison.Ordinal)
            ? targetCharacterId
            : interaction.SubjectCharacterId;
        var targetInteractionCharacterId = string.Equals(interaction.TargetCharacterId, sourceCharacterId, StringComparison.Ordinal)
            ? targetCharacterId
            : interaction.TargetCharacterId;
        var dialogue = interaction.Dialogue;
        if (dialogue is not null)
        {
            dialogue = dialogue with
            {
                SpeakerCharacterId = string.Equals(dialogue.SpeakerCharacterId, sourceCharacterId, StringComparison.Ordinal)
                    ? targetCharacterId
                    : dialogue.SpeakerCharacterId,
                TargetCharacterId = string.Equals(dialogue.TargetCharacterId, sourceCharacterId, StringComparison.Ordinal)
                    ? targetCharacterId
                    : dialogue.TargetCharacterId
            };
        }

        var innerMonologue = interaction.InnerMonologue;
        if (innerMonologue is not null &&
            string.Equals(innerMonologue.CharacterId, sourceCharacterId, StringComparison.Ordinal))
        {
            innerMonologue = innerMonologue with { CharacterId = targetCharacterId };
        }

        return interaction with
        {
            SubjectCharacterId = subjectCharacterId,
            TargetCharacterId = targetInteractionCharacterId,
            Dialogue = dialogue,
            InnerMonologue = innerMonologue
        };
    }

    private int CountRowsForCharacter(string characterId, string characterName)
    {
        var count = 0;
        foreach (var scene in SceneTables)
        {
            foreach (var row in scene.Rows)
            {
                if (row.UsesCharacterVisualPreview &&
                    (string.Equals(row.SubjectCharacterId, characterId, StringComparison.Ordinal) ||
                     string.Equals(row.SourceRoleCharacterId, characterId, StringComparison.Ordinal) ||
                     string.Equals(row.SubjectName, characterName, StringComparison.Ordinal) ||
                     string.Equals(row.SourceRole, characterName, StringComparison.Ordinal)))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private IReadOnlyList<AnalysisResultRowViewModel> RenameRowsForCharacter(
        string characterId,
        string previousName,
        string nextName)
    {
        var changedRows = new List<AnalysisResultRowViewModel>();
        foreach (var scene in SceneTables)
        {
            foreach (var row in scene.Rows)
            {
                if (!row.UsesCharacterVisualPreview)
                {
                    continue;
                }

                if (!string.Equals(row.SubjectCharacterId, characterId, StringComparison.Ordinal) &&
                    !string.Equals(row.SourceRoleCharacterId, characterId, StringComparison.Ordinal) &&
                    !string.Equals(row.SubjectName, previousName, StringComparison.Ordinal) &&
                    !string.Equals(row.SourceRole, previousName, StringComparison.Ordinal))
                {
                    continue;
                }

                row.SubjectName = nextName;
                row.ApplySubjectProjection(nextName, characterId);
                changedRows.Add(row);
            }
        }

        return changedRows;
    }

    private IReadOnlyList<AnalysisResultRowViewModel> ReassignRowsForCharacter(
        string sourceCharacterId,
        string sourceCharacterName,
        string targetCharacterId,
        string targetCharacterName)
    {
        var changedRows = new List<AnalysisResultRowViewModel>();
        foreach (var scene in SceneTables)
        {
            foreach (var row in scene.Rows)
            {
                if (!row.UsesCharacterVisualPreview)
                {
                    continue;
                }

                if (!string.Equals(row.SubjectCharacterId, sourceCharacterId, StringComparison.Ordinal) &&
                    !string.Equals(row.SourceRoleCharacterId, sourceCharacterId, StringComparison.Ordinal) &&
                    !string.Equals(row.SubjectName, sourceCharacterName, StringComparison.Ordinal) &&
                    !string.Equals(row.SourceRole, sourceCharacterName, StringComparison.Ordinal))
                {
                    continue;
                }

                row.SubjectName = targetCharacterName;
                row.ApplySubjectProjection(targetCharacterName, targetCharacterId);
                changedRows.Add(row);
            }
        }

        return changedRows;
    }

    private void RefreshBindingTargetsAfterLocalFallback(string reason)
    {
        _workbenchBindingRefreshMetrics = _workbenchBindingRefreshMetrics.RecordFallbackFullRefresh(reason);
        WorkbenchBindingProjection.RefreshTargets(
            BindingTargets,
            SceneTables,
            CurrentWorkbenchCharactersForBindingTargets(),
            ResolveProjectAssetPath,
            Project.GenerationTasks);
        ApplyBindingAssetDisplayNames();
        RefreshCharacterVoiceBindingEditAccess();
    }

    private void RefreshBindingTargetSections()
    {
        Replace(
            SceneBindingTargets,
            BindingTargets.Where(target => target.Kind == BindingTargetKind.Scene));
        Replace(
            CharacterBindingTargets,
            BindingTargets.Where(target => target.Kind == BindingTargetKind.Character));
        Replace(
            NarrationBindingTargets,
            BindingTargets.Where(target => target.Kind == BindingTargetKind.Narration));
        RefreshCharacterVoiceBindingEditAccess();
        OnPropertyChanged(nameof(HasSceneBindingTargets));
        OnPropertyChanged(nameof(HasCharacterBindingTargets));
        OnPropertyChanged(nameof(HasNarrationBindingTargets));
        SyncSelectedBindingTargetSections(SelectedBindingTarget);
        NotifyPsdLayerMatchTargetStateChanged();
    }

    private void SyncSelectedBindingTargetSections(BindingTargetViewModel? selectedTarget)
    {
        SelectedSceneBindingTarget = selectedTarget?.Kind == BindingTargetKind.Scene ? selectedTarget : null;
        SelectedCharacterBindingTarget = selectedTarget?.Kind == BindingTargetKind.Character ? selectedTarget : null;
        SelectedNarrationBindingTarget = selectedTarget?.Kind == BindingTargetKind.Narration ? selectedTarget : null;
    }

    private void RefreshSceneBindingTarget(SceneTableViewModel scene)
    {
        if (!WorkbenchBindingProjection.RefreshSceneTarget(BindingTargets, scene, ResolveProjectAssetPath))
        {
            if (WorkbenchBindingProjection.InsertSceneTarget(
                    BindingTargets,
                    scene,
                    IndexOfScene(scene),
                    ResolveProjectAssetPath))
            {
                return;
            }

            RefreshBindingTargetsAfterLocalFallback("scene_target");
        }
    }

    public void RefreshBindingTargetForSceneImageSelection(SceneTableViewModel? scene)
    {
        if (scene is null)
        {
            return;
        }

        RefreshSceneBindingTarget(scene);
        RefreshBindingTargetSections();
    }

    private void InsertSceneBindingTarget(SceneTableViewModel scene)
    {
        var sceneIndex = IndexOfScene(scene);
        if (!WorkbenchBindingProjection.InsertSceneTarget(BindingTargets, scene, sceneIndex, ResolveProjectAssetPath))
        {
            RefreshBindingTargetsAfterLocalFallback("insert_scene_target");
        }
    }

    private void RemoveSceneBindingTarget(SceneTableViewModel scene)
    {
        if (!WorkbenchBindingProjection.RemoveSceneTarget(BindingTargets, scene.SceneId))
        {
            if (SceneTables.All(candidate => !string.Equals(candidate.SceneId, scene.SceneId, StringComparison.Ordinal)))
            {
                return;
            }

            RefreshBindingTargetsAfterLocalFallback("remove_scene_target");
        }
    }

    private void SyncSceneBindingTargetOrder()
    {
        if (!WorkbenchBindingProjection.SyncSceneTargetOrder(BindingTargets, SceneTables))
        {
            if (WorkbenchBindingProjection.SyncSceneTargets(BindingTargets, SceneTables, ResolveProjectAssetPath))
            {
                return;
            }

            RefreshBindingTargetsAfterLocalFallback("sync_scene_target_order");
        }
    }

    private void SyncSceneBindingTargetOrderRange(IReadOnlyList<SceneTableViewModel> scenes)
    {
        if (WorkbenchBindingProjection.SyncSceneTargetOrderRange(BindingTargets, scenes))
        {
            return;
        }

        if (WorkbenchBindingProjection.SyncSceneTargets(BindingTargets, SceneTables, ResolveProjectAssetPath))
        {
            return;
        }

        RefreshBindingTargetsAfterLocalFallback("sync_scene_target_order_range");
    }

    private void RefreshBindingTarget(BindingTargetViewModel target)
    {
        RefreshBindingTarget(target, BindingTargetRowIndex.FromScenes(SceneTables));
    }

    private void RefreshBindingTarget(BindingTargetViewModel target, BindingTargetRowIndex rowIndex)
    {
        if (!WorkbenchBindingProjection.RefreshTarget(
                BindingTargets,
                target,
                rowIndex,
                CurrentWorkbenchCharactersForBindingTargets(),
                ResolveProjectAssetPath,
                Project.GenerationTasks))
        {
            RefreshBindingTargetsAfterLocalFallback("single_target");
            return;
        }

        ApplyBindingAssetDisplayNames(target);
    }

    private void RefreshBindingTargetForRow(AnalysisResultRowViewModel row)
    {
        if (!HasExpectedBindingTarget(row))
        {
            return;
        }

        BindingTargetRowIndex? rowIndex = null;
        var refreshedAny = RefreshBindingTargetForRow(row, ref rowIndex);
        if (!refreshedAny)
        {
            if (WorkbenchBindingProjection.UpsertRowTarget(
                    BindingTargets,
                    row,
                    GetBindingTargetRowIndex(ref rowIndex),
                    CurrentWorkbenchCharactersForBindingTargets(),
                    ResolveProjectAssetPath,
                    Project.GenerationTasks))
            {
                return;
            }

            RefreshBindingTargetsAfterLocalFallback("row_target");
        }
    }

    private void RefreshBindingTargetsForRows(IReadOnlyList<AnalysisResultRowViewModel> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        BindingTargetRowIndex? rowIndex = null;
        var refreshedTargets = new HashSet<BindingTargetViewModel>(ReferenceEqualityComparer.Instance);
        var handledTargetKeys = new HashSet<string>(StringComparer.Ordinal);
        var handledAll = true;
        foreach (var row in rows)
        {
            if (!ContainsWorkbenchRow(row) || !HasExpectedBindingTarget(row))
            {
                continue;
            }

            if (!handledTargetKeys.Add(ExpectedBindingTargetKey(row)))
            {
                continue;
            }

            handledAll &= RefreshBindingTargetForRow(row, ref rowIndex, refreshedTargets) ||
                WorkbenchBindingProjection.UpsertRowTarget(
                    BindingTargets,
                    row,
                    GetBindingTargetRowIndex(ref rowIndex),
                    CurrentWorkbenchCharactersForBindingTargets(),
                    ResolveProjectAssetPath,
                    Project.GenerationTasks);
        }

        if (!handledAll)
        {
            RefreshBindingTargetsAfterLocalFallback("row_range");
        }
    }

    private void RefreshBindingTargetsForRowSubjectChange(
        AnalysisResultRowViewModel row,
        WorkbenchRowDraft? previousDraft)
    {
        var hasExpectedPreviousTarget = previousDraft is not null && HasExpectedBindingTarget(previousDraft);
        var rowIsCurrent = ContainsWorkbenchRow(row);
        var hasExpectedCurrentTarget = rowIsCurrent && HasExpectedBindingTarget(row);
        if (!hasExpectedPreviousTarget && !hasExpectedCurrentTarget)
        {
            return;
        }

        BindingTargetRowIndex? rowIndex = null;
        var refreshedTargets = new HashSet<BindingTargetViewModel>(ReferenceEqualityComparer.Instance);
        var previousHandled = !hasExpectedPreviousTarget;
        if (previousDraft is not null && hasExpectedPreviousTarget)
        {
            previousHandled = RefreshBindingTargetForDraft(previousDraft, ref rowIndex, refreshedTargets) ||
                WorkbenchBindingProjection.UpsertRowTarget(
                    BindingTargets,
                    previousDraft,
                    GetBindingTargetRowIndex(ref rowIndex),
                    CurrentWorkbenchCharactersForBindingTargets(),
                    ResolveProjectAssetPath,
                    Project.GenerationTasks);
        }

        var currentHandled = !hasExpectedCurrentTarget;
        if (hasExpectedCurrentTarget)
        {
            currentHandled = RefreshCurrentSubjectBindingTargetForRow(row, ref rowIndex, refreshedTargets) ||
                WorkbenchBindingProjection.UpsertRowTarget(
                    BindingTargets,
                    row,
                    GetBindingTargetRowIndex(ref rowIndex),
                    CurrentWorkbenchCharactersForBindingTargets(),
                    ResolveProjectAssetPath,
                    Project.GenerationTasks);
        }

        if (!previousHandled || !currentHandled)
        {
            RefreshBindingTargetsAfterLocalFallback("row_subject_change");
        }
    }

    private bool ContainsWorkbenchRow(AnalysisResultRowViewModel row)
    {
        foreach (var scene in SceneTables)
        {
            foreach (var candidate in scene.Rows)
            {
                if (ReferenceEquals(candidate, row))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool RefreshBindingTargetForRow(
        AnalysisResultRowViewModel row,
        ref BindingTargetRowIndex? rowIndex,
        HashSet<BindingTargetViewModel>? refreshedTargets = null)
    {
        var refreshedAny = false;
        foreach (var target in BindingTargets.ToArray())
        {
            if (target.Kind == BindingTargetKind.Scene)
            {
                continue;
            }

            if (TargetMatchesRow(target, row))
            {
                if (refreshedTargets is null || refreshedTargets.Add(target))
                {
                    RefreshBindingTarget(target, GetBindingTargetRowIndex(ref rowIndex));
                }

                refreshedAny = true;
            }
        }

        return refreshedAny;
    }

    private bool RefreshCurrentSubjectBindingTargetForRow(
        AnalysisResultRowViewModel row,
        ref BindingTargetRowIndex? rowIndex,
        HashSet<BindingTargetViewModel> refreshedTargets)
    {
        var refreshedAny = false;
        foreach (var target in BindingTargets.ToArray())
        {
            if (!TargetMatchesCurrentRowSubject(target, row))
            {
                continue;
            }

            if (refreshedTargets.Add(target))
            {
                RefreshBindingTarget(target, GetBindingTargetRowIndex(ref rowIndex));
            }

            refreshedAny = true;
        }

        return refreshedAny;
    }

    private static bool TargetMatchesRow(BindingTargetViewModel target, AnalysisResultRowViewModel row)
    {
        return target.Kind switch
        {
            BindingTargetKind.Narration => IsNarrationRowForBinding(row),
            BindingTargetKind.Character => !IsNarrationRowForBinding(row) &&
                (string.Equals(target.TargetId, row.SubjectCharacterId, StringComparison.Ordinal) ||
                 string.Equals(target.DisplayName, row.SubjectName, StringComparison.Ordinal)),
            _ => false
        };
    }

    private void ApplyVoiceReferenceTextSourcesForRows(IReadOnlyDictionary<string, string> sourceByRowId)
    {
        if (sourceByRowId.Count == 0)
        {
            return;
        }

        foreach (var target in BindingTargets)
        {
            if (!target.SupportsVoice)
            {
                continue;
            }

            var sources = SceneTables
                .SelectMany(scene => scene.Rows)
                .Where(row => sourceByRowId.ContainsKey(row.InteractionId) && TargetMatchesRow(target, row))
                .Select(row => sourceByRowId[row.InteractionId])
                .Where(source => !string.IsNullOrWhiteSpace(source))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (sources.Length == 0)
            {
                continue;
            }

            var sourceText = sources.Length == 1 ? sources[0] : $"Reference sidecars: {sources.Length}";
            target.ApplyVoiceReferenceText(target.VoiceReferenceText, sourceText);
        }
    }

    private bool RefreshBindingTargetForDraft(
        WorkbenchRowDraft draft,
        ref BindingTargetRowIndex? rowIndex,
        HashSet<BindingTargetViewModel> refreshedTargets)
    {
        if (IsNarratorVoiceDraft(draft) && !HasNarratorVoiceRows())
        {
            return WorkbenchBindingProjection.RemoveNarrationTarget(BindingTargets);
        }

        var refreshedAny = false;
        foreach (var target in BindingTargets.ToArray())
        {
            if (TargetMatchesDraft(target, draft))
            {
                if (refreshedTargets.Add(target))
                {
                    RefreshBindingTarget(target, GetBindingTargetRowIndex(ref rowIndex));
                }

                refreshedAny = true;
            }
        }

        return refreshedAny;
    }

    private static bool TargetMatchesCurrentRowSubject(BindingTargetViewModel target, AnalysisResultRowViewModel row)
    {
        return target.Kind switch
        {
            BindingTargetKind.Narration => IsNarrationRowForBinding(row),
            BindingTargetKind.Character => !IsNarrationRowForBinding(row) &&
                (string.Equals(target.DisplayName, row.SubjectName, StringComparison.Ordinal) ||
                 (string.IsNullOrWhiteSpace(row.SubjectName) &&
                  string.Equals(target.TargetId, row.SubjectCharacterId, StringComparison.Ordinal))),
            _ => false
        };
    }

    private void RefreshBindingTargetsForRemovedSceneRows(IReadOnlyList<WorkbenchRowDraft> removedRows)
    {
        if (removedRows.Count == 0)
        {
            return;
        }

        if (!HasExpectedBindingTarget(removedRows))
        {
            return;
        }

        BindingTargetRowIndex? rowIndex = null;
        var refreshedTargets = new HashSet<BindingTargetViewModel>(ReferenceEqualityComparer.Instance);
        var handledTargetKeys = new HashSet<string>(StringComparer.Ordinal);
        var handledAll = true;
        foreach (var row in removedRows)
        {
            if (!HasExpectedBindingTarget(row))
            {
                continue;
            }

            if (!handledTargetKeys.Add(ExpectedBindingTargetKey(row)))
            {
                continue;
            }

            handledAll &= RefreshBindingTargetForDraft(row, ref rowIndex, refreshedTargets) ||
                WorkbenchBindingProjection.UpsertRowTarget(
                    BindingTargets,
                    row,
                    GetBindingTargetRowIndex(ref rowIndex),
                    CurrentWorkbenchCharactersForBindingTargets(),
                    ResolveProjectAssetPath,
                    Project.GenerationTasks);
        }

        if (!handledAll)
        {
            RefreshBindingTargetsAfterLocalFallback("removed_scene_rows");
        }
    }

    private BindingTargetRowIndex GetBindingTargetRowIndex(ref BindingTargetRowIndex? rowIndex)
    {
        return rowIndex ??= BindingTargetRowIndex.FromScenes(SceneTables);
    }

    private bool HasExpectedBindingTarget(AnalysisResultRowViewModel row)
    {
        if (IsNarrationRowForBinding(row))
        {
            return HasNarratorVoiceRows() ||
                BindingTargets.Any(target => target.Kind == BindingTargetKind.Narration);
        }

        return Project.Characters.Any(character =>
            character.Name != "旁白" &&
            (string.Equals(character.Id, row.SubjectCharacterId, StringComparison.Ordinal) ||
             string.Equals(character.Id, row.SourceRoleCharacterId, StringComparison.Ordinal) ||
             string.Equals(character.Name, row.SubjectName, StringComparison.Ordinal) ||
             string.Equals(character.Name, row.SourceRole, StringComparison.Ordinal))) ||
            BindingTargets.Any(target =>
                target.Kind == BindingTargetKind.Character &&
                (string.Equals(target.TargetId, row.SubjectCharacterId, StringComparison.Ordinal) ||
                 string.Equals(target.TargetId, row.SourceRoleCharacterId, StringComparison.Ordinal) ||
                 string.Equals(target.DisplayName, row.SubjectName, StringComparison.Ordinal) ||
                 string.Equals(target.DisplayName, row.SourceRole, StringComparison.Ordinal)));
    }

    private bool HasExpectedBindingTarget(WorkbenchRowDraft draft)
    {
        if (!AnalysisResultRowViewModel.UsesCharacterVisualPreviewForDraft(draft))
        {
            return HasNarratorVoiceRows() ||
                BindingTargets.Any(target => target.Kind == BindingTargetKind.Narration);
        }

        return Project.Characters.Any(character =>
            character.Name != "旁白" &&
            (string.Equals(character.Id, draft.SubjectCharacterId, StringComparison.Ordinal) ||
             string.Equals(character.Id, draft.SourceRoleCharacterId, StringComparison.Ordinal) ||
             string.Equals(character.Name, draft.SubjectName, StringComparison.Ordinal) ||
             string.Equals(character.Name, draft.SourceRole, StringComparison.Ordinal))) ||
            BindingTargets.Any(target =>
                target.Kind == BindingTargetKind.Character &&
                (string.Equals(target.TargetId, draft.SubjectCharacterId, StringComparison.Ordinal) ||
                 string.Equals(target.TargetId, draft.SourceRoleCharacterId, StringComparison.Ordinal) ||
                 string.Equals(target.DisplayName, draft.SubjectName, StringComparison.Ordinal) ||
                 string.Equals(target.DisplayName, draft.SourceRole, StringComparison.Ordinal)));
    }

    private bool HasExpectedBindingTarget(IEnumerable<WorkbenchRowDraft> drafts)
    {
        foreach (var draft in drafts)
        {
            if (HasExpectedBindingTarget(draft))
            {
                return true;
            }
        }

        return false;
    }

    private string ExpectedBindingTargetKey(WorkbenchRowDraft draft)
    {
        if (!AnalysisResultRowViewModel.UsesCharacterVisualPreviewForDraft(draft))
        {
            return "narration:narration";
        }

        for (var index = 0; index < Project.Characters.Count; index++)
        {
            var character = Project.Characters[index];
            if (character.Name == "旁白")
            {
                continue;
            }

            if (string.Equals(character.Id, draft.SubjectCharacterId, StringComparison.Ordinal) ||
                string.Equals(character.Id, draft.SourceRoleCharacterId, StringComparison.Ordinal) ||
                string.Equals(character.Name, draft.SubjectName, StringComparison.Ordinal) ||
                string.Equals(character.Name, draft.SourceRole, StringComparison.Ordinal))
            {
                return $"character:id:{character.Id}";
            }
        }

        if (!string.IsNullOrWhiteSpace(draft.SubjectCharacterId))
        {
            return $"character:id:{draft.SubjectCharacterId}";
        }

        return $"character:name:{draft.SubjectName}";
    }

    private string ExpectedBindingTargetKey(AnalysisResultRowViewModel row)
    {
        if (IsNarrationRowForBinding(row))
        {
            return "narration:narration";
        }

        for (var index = 0; index < Project.Characters.Count; index++)
        {
            var character = Project.Characters[index];
            if (character.Name == "旁白")
            {
                continue;
            }

            if (string.Equals(character.Id, row.SubjectCharacterId, StringComparison.Ordinal) ||
                string.Equals(character.Id, row.SourceRoleCharacterId, StringComparison.Ordinal) ||
                string.Equals(character.Name, row.SubjectName, StringComparison.Ordinal) ||
                string.Equals(character.Name, row.SourceRole, StringComparison.Ordinal))
            {
                return $"character:id:{character.Id}";
            }
        }

        if (!string.IsNullOrWhiteSpace(row.SubjectCharacterId))
        {
            return $"character:id:{row.SubjectCharacterId}";
        }

        if (!string.IsNullOrWhiteSpace(row.SourceRoleCharacterId))
        {
            return $"character:id:{row.SourceRoleCharacterId}";
        }

        if (!string.IsNullOrWhiteSpace(row.SourceRole))
        {
            return $"character:name:{row.SourceRole}";
        }

        return $"character:name:{row.SubjectName}";
    }

    private bool HasNarrationRows()
    {
        foreach (var scene in SceneTables)
        {
            foreach (var row in scene.Rows)
            {
                if (row.IsNarration ||
                    IsNarrationSubjectName(row.SubjectName))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool HasNarratorVoiceRows()
    {
        foreach (var scene in SceneTables)
        {
            foreach (var row in scene.Rows)
            {
                if (string.IsNullOrWhiteSpace(row.AppliedDraft?.VoiceRole) &&
                    string.IsNullOrWhiteSpace(row.AppliedDraft?.VoiceRoleCharacterId))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsNarratorVoiceDraft(WorkbenchRowDraft draft)
    {
        return string.IsNullOrWhiteSpace(draft.VoiceRole) &&
            string.IsNullOrWhiteSpace(draft.VoiceRoleCharacterId);
    }

    private static bool TargetMatchesDraft(BindingTargetViewModel target, WorkbenchRowDraft draft)
    {
        return target.Kind switch
        {
            BindingTargetKind.Narration => draft.Kind == WorkbenchRowKind.Narration,
            BindingTargetKind.Character => AnalysisResultRowViewModel.UsesCharacterVisualPreviewForDraft(draft) &&
                (string.Equals(target.TargetId, draft.SubjectCharacterId, StringComparison.Ordinal) ||
                 string.Equals(target.TargetId, draft.SourceRoleCharacterId, StringComparison.Ordinal) ||
                 string.Equals(target.DisplayName, draft.SubjectName, StringComparison.Ordinal) ||
                 string.Equals(target.DisplayName, draft.SourceRole, StringComparison.Ordinal)),
            _ => false
        };
    }

    private static bool IsNarrationRowForBinding(AnalysisResultRowViewModel row)
    {
        return !row.UsesCharacterVisualPreview;
    }

    private static bool IsNarrationSubjectName(string? subjectName)
    {
        var normalized = subjectName?.Trim();
        return string.Equals(normalized, "旁白", StringComparison.Ordinal) ||
            string.Equals(normalized, AnalysisResultRowViewModel.EnvironmentEffectsSubjectName, StringComparison.Ordinal) ||
            string.Equals(normalized, AnalysisResultRowViewModel.NarrationStorySubjectName, StringComparison.Ordinal);
    }

    private void RefreshResolvedPreviewAssets()
    {
        var resolver = new PreviewAssetRefreshResolver(ResolveProjectAssetPath);
        foreach (var scene in SceneTables)
        {
            var scenePreviewChanged = RefreshResolvedPreviewAsset(scene, resolver);

            foreach (var row in scene.Rows)
            {
                _ = RefreshResolvedPreviewAssets(row, resolver, scene, includePsdDefaultPreview: true);
            }

            if (scenePreviewChanged)
            {
                scene.RefreshPreviewAssets(scenePreviewChanged, firstCharacterPreviewChanged: false);
            }
        }
    }

    private bool RefreshResolvedPreviewAsset(SceneTableViewModel scene)
    {
        return RefreshResolvedPreviewAsset(scene, new PreviewAssetRefreshResolver(ResolveProjectAssetPath));
    }

    private static bool RefreshResolvedPreviewAsset(
        SceneTableViewModel scene,
        PreviewAssetRefreshResolver resolver)
    {
        var scenePreviewAsset = VisualPreviewAssetSelector.ResolvePreviewAsset(
            scene.VisualBinding,
            scene.SelectedSceneAsset,
            resolver.Resolve) ?? string.Empty;
        var scenePreviewChanged = !string.Equals(scene.SelectedScenePreviewAsset, scenePreviewAsset, StringComparison.Ordinal);
        if (scenePreviewChanged)
        {
            scene.SelectedScenePreviewAsset = scenePreviewAsset;
        }

        return scenePreviewChanged;
    }

    private ResolvedPreviewAssetChanges RefreshResolvedPreviewAssets(AnalysisResultRowViewModel row)
    {
        return RefreshResolvedPreviewAssets(
            row,
            new PreviewAssetRefreshResolver(ResolveProjectAssetPath),
            row.OwnerScene ?? FindSceneForRow(row),
            includePsdDefaultPreview: true);
    }

    private ResolvedPreviewAssetChanges RefreshResolvedPreviewAssets(
        AnalysisResultRowViewModel row,
        PreviewAssetRefreshResolver resolver,
        SceneTableViewModel? scene,
        bool includePsdDefaultPreview)
    {
        var characterPreviewAsset = ResolveCharacterPreviewAsset(row, resolver, includePsdDefaultPreview);
        var rowPreviewChanges = row.ApplyResolvedPreviewAssets(
            characterPreviewAsset,
            row.UsesCharacterVisualPreview ? resolver.Resolve(row.PreviewAssetText) ?? string.Empty : string.Empty);
        _ = row.ApplyResolvedVoiceDuration(ResolveProjectAssetPath(row.SynthesizedAudioAsset) ?? row.SynthesizedAudioAsset);
        if (rowPreviewChanges.CharacterPreviewChanged && scene is not null)
        {
            scene.RefreshFirstCharacterPreviewAfterResolvedAssetChange(row);
        }

        return rowPreviewChanges;
    }

    private string ResolveCharacterPreviewAsset(
        AnalysisResultRowViewModel row,
        PreviewAssetRefreshResolver resolver,
        bool includePsdDefaultPreview)
    {
        var visualBinding = row.CharacterVisualBinding;
        var defaultBinding = ResolveDefaultCharacterVisualBinding(row);
        return VisualPreviewAssetSelector.ResolvePreviewAsset(
                visualBinding,
                row.SelectedCharacterAsset,
                resolver.Resolve,
                includePsdDefaultPreview ? TryResolveDefaultPsdPreview : null) ??
            VisualPreviewAssetSelector.ResolvePreviewAsset(
                defaultBinding,
                null,
                resolver.Resolve,
                includePsdDefaultPreview ? TryResolveDefaultPsdPreview : null) ??
            string.Empty;
    }

    private sealed class PreviewAssetRefreshResolver
    {
        private readonly Func<string?, string?> _resolve;
        private readonly Dictionary<string, string?> _resolvedByReference = new(StringComparer.Ordinal);

        public PreviewAssetRefreshResolver(Func<string?, string?> resolve)
        {
            _resolve = resolve;
        }

        public string? Resolve(string? reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                return null;
            }

            var key = reference.Trim();
            if (_resolvedByReference.TryGetValue(key, out var resolved))
            {
                return resolved;
            }

            resolved = _resolve(reference);
            _resolvedByReference[key] = resolved;
            return resolved;
        }
    }

    private string? TryResolveDefaultPsdPreview(VisualAssetBinding? visualBinding)
    {
        if (visualBinding is not { SourceKind: VisualSourceKind.Psd } ||
            !string.IsNullOrWhiteSpace(visualBinding.RenderedAssetId) ||
            string.IsNullOrWhiteSpace(visualBinding.SourceAssetId) ||
            _workspaceProjectManifest is null)
        {
            return null;
        }

        var psdPath = ResolveProjectAssetPath(visualBinding.SourceAssetId);
        return string.IsNullOrWhiteSpace(psdPath)
            ? null
            : TryRenderDefaultPsdPreview(visualBinding.SourceAssetId, psdPath, CurrentWorkspaceProject());
    }

    private VisualAssetBinding? ResolveDefaultCharacterVisualBinding(AnalysisResultRowViewModel row)
    {
        if (!row.UsesCharacterVisualPreview)
        {
            return null;
        }

        var documentBinding = _workbenchSession.Document.CharacterBindings.FirstOrDefault(binding =>
            !string.IsNullOrWhiteSpace(row.SubjectCharacterId ?? row.SourceRoleCharacterId) &&
            string.Equals(binding.CharacterId, row.SubjectCharacterId ?? row.SourceRoleCharacterId, StringComparison.Ordinal));
        if (documentBinding?.VisualBinding is { SourceKind: VisualSourceKind.Image or VisualSourceKind.Psd } byId)
        {
            return byId;
        }

        var target = ResolveCharacterBindingTargetForRow(row);
        return target?.VisualBinding.SourceKind is VisualSourceKind.Image or VisualSourceKind.Psd ? target.VisualBinding : null;
    }

    private string? TryRenderDefaultPsdPreview(string psdAssetId, string psdPath, WorkspaceProject workspace)
    {
        try
        {
            var cacheDirectory = Path.Combine(workspace.Folders.Logs, "psd-default-previews");
            return _psdDefaultPreviewService.RenderDefaultPreview(psdAssetId, psdPath, cacheDirectory);
        }
        catch (Exception ex)
        {
            StatusText = $"PSD 默认预览生成失败：{Path.GetFileName(psdPath)} ({ex.Message})";
            return null;
        }
    }

    private static bool IsPsdFile(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".psd", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".psb", StringComparison.OrdinalIgnoreCase);
    }

    private bool RejectBindingEditsWhileWorkbenchMutationLocked(BindingTargetViewModel target)
    {
        if (!IsWorkbenchMutationLocked)
        {
            return false;
        }

        target.StatusText = $"{WorkbenchMutationLockReasonText}中已锁定绑定编辑";
        StatusText = $"{WorkbenchMutationLockReasonText}中工作台绑定已锁定；请等待完成或取消后再修改。";
        return true;
    }

    private void ApplyBindingTargetToWorkspace(BindingTargetViewModel target)
    {
        if (RejectBindingEditsWhileWorkbenchMutationLocked(target)) return;

        var rowIndex = BindingTargetRowIndex.FromScenes(SceneTables);
        ApplyBindingTargetToWorkspace(target, rowIndex);
    }

    private void ApplyBindingTargetToWorkspace(
        BindingTargetViewModel target,
        BindingTargetRowIndex rowIndex)
    {
        if (RejectBindingEditsWhileWorkbenchMutationLocked(target)) return;

        var preservedVoiceReferenceSourceText = target.SupportsVoice
            ? target.VoiceReferenceTextSourceText
            : string.Empty;
        var shouldRefreshRoleVoiceRoutes = target.Kind == BindingTargetKind.Character &&
            target.HasVoiceBindingEdit &&
            UseRoleVoiceForAllCharacterLines;
        WorkbenchBindingTargetApplyResult result;
        using (BeginWorkbenchDocumentApply())
        {
            result = BindingTargetWorkspaceApplier.ApplyWithResult(target, SceneTables, rowIndex, _workbenchSession);
        }

        if (!result.CommandResult.Applied)
        {
            return;
        }

        ApplyDerivedStateRefresh(result.DerivedStateRefresh);
        RestoreBindingTargetVoiceReferenceSourceText(target, preservedVoiceReferenceSourceText);
        if (shouldRefreshRoleVoiceRoutes)
        {
            RefreshEnabledRoleVoiceRoutesAfterCharacterBindingChange();
        }
    }

    private void RestoreBindingTargetVoiceReferenceSourceText(
        BindingTargetViewModel target,
        string sourceText)
    {
        if (!target.SupportsVoice || string.IsNullOrWhiteSpace(sourceText))
        {
            return;
        }

        var currentTarget = BindingTargets.FirstOrDefault(candidate =>
            candidate.Kind == target.Kind &&
            string.Equals(candidate.TargetId, target.TargetId, StringComparison.Ordinal));
        currentTarget?.ApplyVoiceReferenceText(currentTarget.VoiceReferenceText, sourceText);
    }

    private void RefreshEnabledRoleVoiceRoutesAfterCharacterBindingChange()
    {
        ApplyRoleVoiceRoute(
            document =>
            {
                var service = new WorkbenchCommandService();
                var characters = CurrentWorkbenchCharactersForVoiceRouting(document);
                var routed = service.RouteDialogueVoicesToSourceRoles(document, characters, routeToSourceRole: true);
                routed = service.RouteSourceRoleVoices(routed, characters, "thought", UseRoleVoiceForThought);
                routed = service.RouteSourceRoleVoices(routed, characters, "action", UseRoleVoiceForAction);
                return routed;
            },
            "role_reference_voice_binding_refresh",
            "角色参考音频已按当前路由规则刷新；未绑定参考音频的角色继续使用默认配音。",
            "角色参考音频已保存；当前没有需要刷新的角色行。");
    }

    private IReadOnlyList<AnalysisResultRowViewModel> RowsForBindingTarget(BindingTargetViewModel target)
    {
        return RowsForBindingTarget(target, BindingTargetRowIndex.FromScenes(SceneTables));
    }

    private static IReadOnlyList<AnalysisResultRowViewModel> RowsForBindingTarget(
        BindingTargetViewModel target,
        BindingTargetRowIndex rowIndex)
    {
        return rowIndex.RowsForTarget(target);
    }

    private string? ResolveProjectAssetPath(string? reference)
    {
        var projectReference = BindingValue(reference);
        if (projectReference is null)
        {
            return null;
        }

        if (_workspaceProjectManifest is not null)
        {
            var workspace = CurrentWorkspaceProject();
            if (CanCacheProjectAssetReference(projectReference))
            {
                if (_projectAssetPathCache.TryGetValue(projectReference, out var cachedPath))
                {
                    return cachedPath;
                }

                var resolvedPath = _projectAssetResolver.ResolvePath(projectReference, workspace, _workspaceProjectManifest);
                _projectAssetPathCache[projectReference] = resolvedPath;
                return resolvedPath;
            }

            return _projectAssetResolver.ResolvePath(projectReference, workspace, _workspaceProjectManifest);
        }

        if (Path.IsPathRooted(reference))
        {
            var fullPath = Path.GetFullPath(reference);
            return File.Exists(fullPath) ? fullPath : null;
        }

        return null;
    }

    private void ApplyBindingAssetDisplayNames()
    {
        foreach (var target in BindingTargets)
        {
            ApplyBindingAssetDisplayNames(target);
        }
    }

    private void ApplyBindingAssetDisplayNames(BindingTargetViewModel target)
    {
        if (!target.SupportsImage)
        {
            return;
        }

        target.ApplyVisualAssetDisplayNames(
            ResolveProjectAssetDisplayName(target.VisualBinding.SourceAssetId),
            ResolveProjectAssetDisplayName(target.VisualBinding.RenderedAssetId));
    }

    private string? ResolveProjectAssetDisplayName(string? reference)
    {
        var projectReference = BindingValue(reference);
        if (projectReference is null)
        {
            return null;
        }

        if (_workspaceProjectManifest is not null)
        {
            var normalizedProjectReference = projectReference.Replace('\\', '/');
            var asset = _workspaceProjectManifest.Assets.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, projectReference, StringComparison.Ordinal) ||
                string.Equals(candidate.ProjectRelativePath, normalizedProjectReference, StringComparison.OrdinalIgnoreCase));
            if (asset is not null)
            {
                return ResolveProjectAssetDisplayName(asset);
            }
        }

        return Path.IsPathRooted(projectReference) || projectReference.Contains('/') || projectReference.Contains('\\')
            ? Path.GetFileName(projectReference)
            : null;
    }

    private static string? ResolveProjectAssetDisplayName(ProjectMediaAsset asset)
    {
        if (asset.Metadata.TryGetValue("originalFileName", out var originalFileName) &&
            !string.IsNullOrWhiteSpace(originalFileName))
        {
            return originalFileName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(asset.DisplayName))
        {
            return asset.DisplayName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(asset.SourcePath))
        {
            return Path.GetFileName(asset.SourcePath.Trim());
        }

        return !string.IsNullOrWhiteSpace(asset.AbsolutePath)
            ? Path.GetFileName(asset.AbsolutePath.Trim())
            : null;
    }

    private static bool CanCacheProjectAssetReference(string reference)
    {
        return !Path.IsPathRooted(reference);
    }

    private static async Task<string> ReadVoiceReferenceTextAsync(string? referenceTextPath)
    {
        if (string.IsNullOrWhiteSpace(referenceTextPath) || !File.Exists(referenceTextPath))
        {
            return string.Empty;
        }

        return (await File.ReadAllTextAsync(referenceTextPath)).Trim();
    }

    private static string? ResolveExistingVoiceReferenceTextPath(string? audioPath)
    {
        if (string.IsNullOrWhiteSpace(audioPath))
        {
            return null;
        }

        var referencePath = Path.Combine(
            Path.GetDirectoryName(audioPath) ?? string.Empty,
            $"{Path.GetFileNameWithoutExtension(audioPath)}.reference.txt");
        if (File.Exists(referencePath))
        {
            return referencePath;
        }

        var textPath = Path.ChangeExtension(audioPath, ".txt");
        return File.Exists(textPath) ? textPath : null;
    }

    private async Task<VoiceReferenceTextReadResult> ReadVoiceReferenceTextForTargetAsync(BindingTargetViewModel target)
    {
        var sourceAssetId = WorkbenchBindingValue.Asset(target.VoiceSourceAsset);
        if (!string.IsNullOrWhiteSpace(sourceAssetId))
        {
            var sourcePath = ResolveProjectAssetPath(sourceAssetId);
            var sourceReferenceTextPath = ResolveExistingVoiceReferenceTextPath(sourcePath);
            var sourceText = await ReadVoiceReferenceTextAsync(sourceReferenceTextPath);
            if (!string.IsNullOrWhiteSpace(sourceText))
            {
                return new VoiceReferenceTextReadResult(
                    sourceText,
                    VoiceReferenceTextSourceFromPath(sourceReferenceTextPath, "Project sidecar"));
            }
        }

        var voiceReferenceId = WorkbenchBindingValue.Asset(target.VoiceAsset);
        if (!string.IsNullOrWhiteSpace(voiceReferenceId))
        {
            if (_comfyVoiceReferenceTextByOption.TryGetValue(voiceReferenceId, out var catalogReferenceText) &&
                !string.IsNullOrWhiteSpace(catalogReferenceText))
            {
                return new VoiceReferenceTextReadResult(
                    catalogReferenceText,
                    "CharacterVoicesNode catalog");
            }

            await Task.CompletedTask;
        }

        return VoiceReferenceTextReadResult.Empty;
    }

    private string VoiceReferenceTextSourceFromBinding(VoiceAssetBinding binding)
    {
        var sourceAssetId = WorkbenchBindingValue.Asset(binding.SourceAssetId);
        if (!string.IsNullOrWhiteSpace(sourceAssetId) &&
            ResolveProjectAssetPath(sourceAssetId) is { } sourcePath)
        {
            return VoiceReferenceTextSourceFromPath(Path.ChangeExtension(sourcePath, ".txt"), "Project sidecar");
        }

        return "Saved binding";
    }

    private static string VoiceReferenceTextSourceFromPath(string? path, string fallback)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return fallback;
        }

        var fileName = Path.GetFileName(path);
        return string.IsNullOrWhiteSpace(fileName)
            ? fallback
            : $"{fallback}: {fileName}";
    }

    private sealed record VoiceReferenceTextReadResult(string Text, string SourceText)
    {
        public static VoiceReferenceTextReadResult Empty { get; } = new(string.Empty, string.Empty);
    }

    private async Task<InteractionProject> ImportPendingExternalVoiceReferencesAsync(InteractionProject project)
    {
        await Task.CompletedTask;
        return project;
    }

    private async Task<InteractionProject> EnsureWorkbenchVoiceReferencesSyncedAsync(
        InteractionProject project,
        IDictionary<string, string>? referenceTextSourceByRowId = null)
    {
        if (_workspaceProjectManifest is null)
        {
            return project;
        }

        var syncedFileSourcesByRowId = new Dictionary<string, string>(StringComparer.Ordinal);
        var document = _workbenchSession.Document;
        var projectDocument = WorkbenchDraftBuilder.FromProject(project);
        var commandService = new WorkbenchCommandService();
        var changedRowIds = new List<string>();
        var updatedDocument = document;
        foreach (var scene in document.Scenes)
        {
            foreach (var row in scene.Rows)
            {
                projectDocument.RowsById.TryGetValue(row.InteractionId, out var projectRow);
                var voiceBinding = MergeVoiceBinding(row.VoiceBinding, projectRow?.VoiceBinding);
                var synced = await EnsureVoiceBindingSyncedAsync(voiceBinding);
                if (ReferenceEquals(synced, row.VoiceBinding) || synced == row.VoiceBinding)
                {
                    continue;
                }

                updatedDocument = commandService.UpdateRowVoiceBinding(updatedDocument, row.InteractionId, synced);
                changedRowIds.Add(row.InteractionId);
                syncedFileSourcesByRowId[row.InteractionId] = VoiceReferenceTextSourceFromBinding(synced);
                referenceTextSourceByRowId?.Remove(row.InteractionId);
                referenceTextSourceByRowId?.Add(row.InteractionId, syncedFileSourcesByRowId[row.InteractionId]);
            }
        }

        if (changedRowIds.Count == 0 || ReferenceEquals(updatedDocument, document))
        {
            ApplyVoiceReferenceTextSourcesForRows(syncedFileSourcesByRowId);
            return project;
        }

        using (BeginWorkbenchDocumentApply())
        {
            _workbenchSession.Execute(_ => updatedDocument, WorkbenchSessionOperationNames.ApplyBindingTarget);
            WorkbenchViewModelAdapter.ApplyDocument(SceneTables, updatedDocument);
        }

        RefreshBindingTargetsForRows(
            SceneTables
                .SelectMany(scene => scene.Rows)
                .Where(row => changedRowIds.Contains(row.InteractionId, StringComparer.Ordinal))
                .ToArray());
        ApplyDerivedStateRefresh(WorkbenchDerivedStateRefresh.SessionOnly);
        ApplyVoiceReferenceTextSourcesForRows(syncedFileSourcesByRowId);
        project = await _workspaceService.ApplyWorkbenchDocumentAsync(project, updatedDocument);
        _workbenchSession.Reset(updatedDocument);
        NotifyWorkbenchSessionStateChanged();
        return project;
    }

    private static VoiceAssetBinding MergeVoiceBinding(
        VoiceAssetBinding? workbenchBinding,
        VoiceAssetBinding? projectBinding)
    {
        if (workbenchBinding is null)
        {
            return projectBinding ?? VoiceAssetBinding.None;
        }

        if (projectBinding is null)
        {
            return workbenchBinding;
        }

        var sameVoiceReference = string.Equals(
            workbenchBinding.EffectiveVoiceReferenceId,
            projectBinding.EffectiveVoiceReferenceId,
            StringComparison.Ordinal);

        return VoiceAssetBinding.FromProjectReference(
            workbenchBinding.EffectiveVoiceReferenceId ?? projectBinding.EffectiveVoiceReferenceId,
            workbenchBinding.EffectiveReferenceText ?? (sameVoiceReference ? projectBinding.EffectiveReferenceText : null),
            workbenchBinding.SourceAssetId ?? (sameVoiceReference ? projectBinding.SourceAssetId : null),
            workbenchBinding.SyncedVoicePath ?? (sameVoiceReference ? projectBinding.SyncedVoicePath : null));
    }

    private async Task<VoiceAssetBinding> EnsureVoiceBindingSyncedAsync(VoiceAssetBinding? voiceBinding)
    {
        if (_workspaceProjectManifest is null || voiceBinding is null)
        {
            return voiceBinding ?? VoiceAssetBinding.None;
        }

        var sourceAssetId = WorkbenchBindingValue.Asset(voiceBinding.SourceAssetId);
        if (string.IsNullOrWhiteSpace(sourceAssetId))
        {
            return voiceBinding;
        }

        var sourcePath = ResolveProjectAssetPath(sourceAssetId);
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Voice reference project source does not exist; cannot resync.", sourceAssetId);
        }

        var referenceText = await ReadVoiceReferenceTextAsync(Path.ChangeExtension(sourcePath, ".txt"));
        if (string.IsNullOrWhiteSpace(referenceText))
        {
            referenceText = voiceBinding.EffectiveReferenceText ?? string.Empty;
        }

        return VoiceAssetBinding.FromProjectReference(
            sourceAssetId,
            referenceText,
            sourceAssetId,
            null);
    }

    private static string NormalizePsdIntentText(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private sealed record PsdBindingIntent(
        string Emotion,
        string Action,
        string SceneId,
        string InteractionId,
        string? Text);

    private sealed record PsdVisualSource(string AssetId, string Path);

    private sealed record PsdExpressionMatchPlan(
        AnalysisResultRowViewModel Row,
        BindingTargetViewModel Target,
        PsdVisualSource Source,
        PsdBindingIntent Intent,
        PsdLayerMatchConfiguration? LayerMatchSettings);

    private sealed record PsdExpressionMatchGroupKey(
        string TargetId,
        string PsdAssetId,
        string PsdPath);

    private sealed record PsdExpressionMatchGroup(
        PsdExpressionMatchGroupKey Key,
        BindingTargetViewModel Target,
        PsdVisualSource Source,
        PsdLayerMatchConfiguration LayerMatchSettings,
        IReadOnlyList<PsdExpressionMatchPlan> Plans);

    private sealed record PsdLayerRenderGroup(
        string GroupName,
        IReadOnlyDictionary<string, IReadOnlyList<string>> CandidateLayerNames,
        IReadOnlyDictionary<string, IReadOnlyList<int>> CandidateLayerIds,
        IReadOnlyList<string> DefaultLayerNames,
        IReadOnlyList<int> DefaultLayerIds,
        IReadOnlyList<string> ControlledLayerNames,
        IReadOnlyList<int> ControlledLayerIds);

    private sealed record PsdAiResolvedLayerSlot(
        IReadOnlyList<int> LayerIds,
        IReadOnlyList<string> LayerNames,
        IReadOnlyList<int> ControlledLayerIds,
        IReadOnlyList<string> ControlledLayerNames);

}

public sealed record PsdPreviewOpenRequest(string PsdPath, string Title);


