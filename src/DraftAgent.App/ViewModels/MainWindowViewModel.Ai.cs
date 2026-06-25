using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using DraftAgent.Core;

namespace DraftAgent.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task AnalyzeScriptAsync()
    {
        try
        {
            if (IsWorkbenchMutationLocked)
            {
                StatusText = $"{WorkbenchMutationLockReasonText}中已锁定 AI 分析。请等待完成或取消后再分析。";
                return;
            }

            if (string.IsNullOrWhiteSpace(ScriptText))
            {
                StatusText = "文稿为空，无法分析。";
                return;
            }

            if (!await EnsureAiConnectionReadyAsync("AI 分析"))
            {
                return;
            }

            if (IsWorkbenchDirty)
            {
                await _externalActionTransaction.RunAsync(async committedProject =>
                {
                    await PersistCurrentProjectSnapshotAsync(committedProject);
                    return committedProject;
                });
            }

            var inputRevision = _externalActionTransaction.CaptureRevision();
            var sourceScript = ScriptText;
            StatusText = $"正在调用 {SelectedAiProvider} 分析接口，等待结构化 JSON...";
            var response = await _workspaceService.AnalyzeWithLlmAsync(sourceScript);
            if (IsWorkbenchDirty ||
                !_externalActionTransaction.IsCurrentRevision(inputRevision) ||
                !string.Equals(sourceScript, ScriptText, StringComparison.Ordinal))
            {
                StatusText = "分析结果已返回，但当前工作台或文稿已更新；为避免覆盖新编辑，已保留当前项目。";
                return;
            }

            Project = AttachCurrentScriptDrafts(response.Project);
            await SaveCurrentProjectSnapshotAsync();
            LlmJsonPreview = response.RawJson;
            CaptureWorkbenchResetBaseline(Project, response.RawJson);
            await CaptureWorkspaceOriginalAiJsonAsync(response.RawJson);
            AiReplyStatusText = FormatReplyStatus(response.ReplyMetadata);
            StatusText = $"分析完成：已解析 {Project.Scenes.Count} 个场景，{ResultRowCount} 行工作台数据。";
        }
        catch (Exception ex)
        {
            HandleExternalError("AI 分析失败", ex);
        }
    }

    [RelayCommand]
    private async Task OptimizeScriptNarrationAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(ScriptText))
            {
                StatusText = "文稿为空，无法优化。";
                return;
            }

            if (!await EnsureAiConnectionReadyAsync("解说优化"))
            {
                return;
            }

            var sourceText = ScriptText;
            StatusText = $"正在调用 {SelectedAiProvider} 优化文稿...";
            var response = await _workspaceService.OptimizeNarrationScriptAsync(sourceText);
            var targetDraft = EnsureScriptDraft("改编1");
            targetDraft.Text = response.Text;
            SelectedScriptDraft = targetDraft;
            await PersistCurrentProjectSnapshotAsync(AttachCurrentScriptDrafts(Project));
            AiReplyStatusText = FormatReplyStatus(response.ReplyMetadata);
            StatusText = "解说优化完成，已写入改编1。可以继续分析到工作台。";
        }
        catch (Exception ex)
        {
            HandleExternalError("解说优化失败", ex);
        }
    }

    [RelayCommand]
    private async Task TestAiConnectionAsync()
    {
        HasAiApiKeyValidationError = false;
        AiConnectionState = "neutral";
        AiStatusText = "检测中...";
        RefreshAiConnectionBrushes();

        try
        {
            var result = await CheckCurrentAiConnectionAsync();
            HasAiApiKeyValidationError = IsAiApiKeyFailure(result);
            AiConnectionState = result.State == DraftAgent.Core.AiConnectionState.Connected ? "success" : "failed";
            AiStatusText = result.Message;
        }
        catch (Exception ex)
        {
            HasAiApiKeyValidationError = false;
            AiConnectionState = "failed";
            AiStatusText = $"AI 检测失败：{ex.Message}";
        }

        RefreshAiConnectionBrushes();
    }

    private async Task<bool> EnsureAiConnectionReadyAsync(string operationName)
    {
        HasAiApiKeyValidationError = false;
        AiConnectionState = "neutral";
        AiStatusText = $"{operationName}前检测中...";
        RefreshAiConnectionBrushes();

        try
        {
            var result = await CheckCurrentAiConnectionAsync();
            HasAiApiKeyValidationError = IsAiApiKeyFailure(result);
            var connected = result.State == DraftAgent.Core.AiConnectionState.Connected;
            AiConnectionState = connected ? "success" : "failed";
            AiStatusText = result.Message;
            RefreshAiConnectionBrushes();

            if (connected)
            {
                return true;
            }

            StatusText = $"{operationName}已停止：AI 服务未连接，请先检查服务设置。";
            return false;
        }
        catch (Exception ex)
        {
            HasAiApiKeyValidationError = false;
            AiConnectionState = "failed";
            AiStatusText = $"AI 检测失败：{ex.Message}";
            RefreshAiConnectionBrushes();
            StatusText = $"{operationName}已停止：AI 服务检测失败。";
            return false;
        }
    }

    [RelayCommand]
    private void SaveAiSettings()
    {
        CommitAiSettingsEdit();
    }

    private Task<AiConnectionCheckResult> CheckCurrentAiConnectionAsync()
    {
        return _checkAiConnectionWithModelFactory
            ? CreateAiModelClient().CheckConnectionAsync()
            : _workspaceService.CheckAiConnectionAsync();
    }

    public void BeginAiSettingsEdit()
    {
        _aiSettingsSnapshot = new AiSettingsSnapshot(
            SelectedAiProvider,
            AiApiKey,
            AiBaseUrl,
            AiModelName,
            AiMaxOutputTokens,
            AiMaxOutputTokensText,
            ComfyEndpoint);
    }

    public void CommitAiSettingsEdit()
    {
        NormalizeAiMaxOutputTokensText();
        ComfyEndpoint = NormalizeEndpoint(ComfyEndpoint);
        _aiSettingsStore.Save(new AiSettingsState(
            SelectedAiProvider,
            AiApiKey,
            AiBaseUrl,
            AiModelName,
            AiMaxOutputTokens,
            ComfyEndpoint));
        _aiSettingsSnapshot = null;
        StatusText = $"AI 服务设置已保存：{SelectedAiProvider} / {AiModelName}";
    }

    public void CancelAiSettingsEdit()
    {
        if (_aiSettingsSnapshot is not { } snapshot)
        {
            return;
        }

        SelectedAiProvider = snapshot.SelectedAiProvider;
        AiApiKey = snapshot.AiApiKey;
        AiBaseUrl = snapshot.AiBaseUrl;
        AiModelName = snapshot.AiModelName;
        AiMaxOutputTokens = snapshot.AiMaxOutputTokens;
        AiMaxOutputTokensText = snapshot.AiMaxOutputTokensText;
        ComfyEndpoint = snapshot.ComfyEndpoint;
        _aiSettingsSnapshot = null;
        StatusText = "AI 服务设置更改已取消。";
    }

    partial void OnAiConnectionStateChanged(string value)
    {
        RefreshAiConnectionBrushes();
    }

    partial void OnAiApiKeyChanged(string value)
    {
        HasAiApiKeyValidationError = false;
        RefreshAiApiKeyBrushes();
    }

    partial void OnSelectedAiProviderChanged(string value)
    {
        HasAiApiKeyValidationError = false;
        var defaults = AiSettingsPolicy.DefaultsFor(value);
        AiBaseUrl = defaults.BaseUrl;
        AiModelName = defaults.ModelName;
        RefreshAiApiKeyBrushes();
    }

    partial void OnHasAiApiKeyValidationErrorChanged(bool value)
    {
        RefreshAiApiKeyBrushes();
    }

    private void RefreshAiConnectionBrushes()
    {
        OnPropertyChanged(nameof(AiEndpointBackground));
        OnPropertyChanged(nameof(AiEndpointBorderBrush));
        RefreshAiApiKeyBrushes();
    }

    private void RefreshAiApiKeyBrushes()
    {
        OnPropertyChanged(nameof(AiApiKeyBackground));
        OnPropertyChanged(nameof(AiApiKeyBorderBrush));
    }

    private bool IsAiApiKeyFailure(AiConnectionCheckResult result)
    {
        if (!AiSettingsPolicy.RequiresApiKey(SelectedAiProvider))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(AiApiKey) && result.State != DraftAgent.Core.AiConnectionState.Connected)
        {
            return true;
        }

        if (result.State == DraftAgent.Core.AiConnectionState.Unauthorized ||
            result.HttpStatusCode is 401 or 403)
        {
            return true;
        }

        return result.Message.Contains("api key", StringComparison.OrdinalIgnoreCase) ||
            result.Message.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
            result.Message.Contains("invalid key", StringComparison.OrdinalIgnoreCase) ||
            result.Message.Contains("invalid token", StringComparison.OrdinalIgnoreCase) ||
            result.Message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
            result.Message.Contains("forbidden", StringComparison.OrdinalIgnoreCase);
    }

    private void NormalizeAiMaxOutputTokensText()
    {
        AiMaxOutputTokensText = AiSettingsPolicy.NormalizeMaxOutputTokensText(AiMaxOutputTokensText, AiMaxOutputTokens);
        AiMaxOutputTokens = int.Parse(AiMaxOutputTokensText);
    }

    private int ResolveAiMaxOutputTokens()
    {
        var tokens = AiSettingsPolicy.ResolveMaxOutputTokens(AiMaxOutputTokensText, AiMaxOutputTokens);
        AiMaxOutputTokens = tokens;
        return tokens;
    }

    private static string FormatReplyStatus(AiModelReplyMetadata? metadata)
    {
        return metadata is null
            ? "未分析"
            : $"{metadata.Provider} / {metadata.ModelName} / {FormatAiReplyState(metadata.State)} / finish={metadata.FinishReason ?? "null"} / content={metadata.ContentLength} / reasoning={metadata.ReasoningLength} / {metadata.Message}";
    }

    private static string FormatAiReplyState(AiReplyState state)
    {
        return state switch
        {
            AiReplyState.Completed => "回复完成",
            AiReplyState.LengthLimited => "长度截断",
            AiReplyState.ContentFiltered => "内容过滤",
            AiReplyState.ToolCallsOnly => "只返回工具调用",
            AiReplyState.ReasoningOnly => "只返回思考内容",
            AiReplyState.ReasoningLengthLimited => "思考内容被截断",
            AiReplyState.EmptyContent => "内容为空",
            AiReplyState.InvalidJson => "JSON 不合法",
            AiReplyState.RecoveredFromReasoning => "已从思考内容恢复 JSON",
            AiReplyState.Failed => "回复失败",
            _ => "未知"
        };
    }

    private static AiReplyException? FindAiReplyException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is AiReplyException aiReplyException)
            {
                return aiReplyException;
            }
        }

        return null;
    }
}
