using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.IO;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DraftAgent.App;
using DraftAgent.App.Psd.Services;
using DraftAgent.Core;

namespace DraftAgent.App.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    public const string CustomVoiceReferenceOption = "自定义音频...";

    private const string DefaultComfyEndpoint = "http://127.0.0.1:8000";

    private static readonly HttpClient LlmHttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    private static readonly HttpClient ComfyHttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    private readonly DraftWorkspaceService _workspaceService;
    private readonly IAudioPreviewService _audioPreviewService;
    private readonly PsdDefaultPreviewService _psdDefaultPreviewService = new();
    private readonly IProjectAssetResolver _projectAssetResolver;
    private readonly IPsdLayerComponentClient _psdLayerComponentClient;
    private readonly Func<IAiModelClient> _aiModelClientFactory;
    private readonly bool _checkAiConnectionWithModelFactory;
    private readonly AiSettingsStore _aiSettingsStore;
    private readonly WorkbenchExternalActionTransaction _externalActionTransaction;
    private readonly Dictionary<string, string?> _projectAssetPathCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _projectVoiceAssetIdByOption = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _comfyVoiceReferenceTextByOption = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _pendingLiveTtsResubmissions = new(StringComparer.Ordinal);
    private readonly object _pendingLiveTtsResubmissionsGate = new();
    private PsdLayerMatchConfiguration? _lastPsdLayerMatchTemplate;
    private WorkspaceProjectManifest? _workspaceProjectManifest;
    private WorkspaceProject? _workspaceProject;
    private WorkbenchSession _workbenchSession = new(new WorkbenchDocument([], []));
    private VoiceAssetBinding _lastNarratorVoiceBinding = VoiceAssetBinding.None;
    private string _workbenchResetBaselineJson = string.Empty;

    private ScriptDraftTabViewModel? _selectedScriptDraft;

    [ObservableProperty]
    private InteractionProject _project;

    [ObservableProperty]
    private string _llmJsonPreview = string.Empty;

    [ObservableProperty]
    private string _statusText = "真实 AI / ComfyUI 工作流已接入，请先确认服务设置。";

    [ObservableProperty]
    private bool _isBindingDialogOpen;

    [ObservableProperty]
    private BindingTargetViewModel? _selectedBindingTarget;

    [ObservableProperty]
    private BindingTargetViewModel? _selectedSceneBindingTarget;

    [ObservableProperty]
    private BindingTargetViewModel? _selectedCharacterBindingTarget;

    [ObservableProperty]
    private BindingTargetViewModel? _selectedNarrationBindingTarget;

    [ObservableProperty]
    private bool _useRoleVoiceForDialogue;

    [ObservableProperty]
    private bool _useRoleVoiceForThought;

    [ObservableProperty]
    private bool _useRoleVoiceForAction;

    [ObservableProperty]
    private bool _useRoleVoiceForAllCharacterLines;

    [ObservableProperty]
    private AnalysisResultRowViewModel? _selectedRow;

    [ObservableProperty]
    private string _activeAudioTarget = string.Empty;

    [ObservableProperty]
    private bool _isAudioPlayerVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDragWorkbench))]
    [NotifyPropertyChangedFor(nameof(CanEditWorkbenchStructure))]
    [NotifyPropertyChangedFor(nameof(IsWorkbenchMutationLocked))]
    [NotifyPropertyChangedFor(nameof(TtsSynthesisActionText))]
    [NotifyCanExecuteChangedFor(nameof(CancelTtsSynthesisCommand))]
    private bool _isTtsSynthesisRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDragWorkbench))]
    [NotifyPropertyChangedFor(nameof(CanEditWorkbenchStructure))]
    [NotifyPropertyChangedFor(nameof(IsWorkbenchMutationLocked))]
    private bool _isPsdLayerAiMatchingRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDragWorkbench))]
    [NotifyPropertyChangedFor(nameof(CanEditWorkbenchStructure))]
    [NotifyPropertyChangedFor(nameof(IsWorkbenchMutationLocked))]
    private bool _isProjectExportRunning;

    [ObservableProperty]
    private string _selectedAiProvider = AiSettingsPolicy.DefaultProvider;

    [ObservableProperty]
    private string _aiApiKey = string.Empty;

    [ObservableProperty]
    private string _aiBaseUrl = AiSettingsPolicy.DefaultLmStudioBaseUrl;

    [ObservableProperty]
    private string _aiModelName = AiSettingsPolicy.DefaultLmStudioModel;

    [ObservableProperty]
    private int _aiMaxOutputTokens = AiSettingsPolicy.DefaultMaxOutputTokens;

    [ObservableProperty]
    private string _aiMaxOutputTokensText = AiSettingsPolicy.DefaultMaxOutputTokens.ToString();

    [ObservableProperty]
    private string _comfyEndpoint = DefaultComfyEndpoint;

    [ObservableProperty]
    private string _comfyConnectionState = "neutral";

    [ObservableProperty]
    private string _comfyStatusText = "未检测";

    [ObservableProperty]
    private string _comfySubmitStatusText = "未提交";

    [ObservableProperty]
    private string _aiConnectionState = "neutral";

    [ObservableProperty]
    private bool _hasAiApiKeyValidationError;

    [ObservableProperty]
    private string _aiStatusText = "未检测";

    [ObservableProperty]
    private string _aiReplyStatusText = "未分析";

    [ObservableProperty]
    private string _workspaceProjectName = "未指定项目";

    [ObservableProperty]
    private string _workspaceProjectPath = string.Empty;

    private AiSettingsSnapshot? _aiSettingsSnapshot;

    public MainWindowViewModel()
        : this(null, true)
    {
    }

    public MainWindowViewModel(
        DraftWorkspaceService? workspaceService,
        bool loadLastProject = false,
        IPsdLayerComponentClient? psdLayerComponentClient = null,
        IProjectAssetResolver? projectAssetResolver = null,
        IAudioPreviewService? audioPreviewService = null,
        Func<string>? comfyVoicesDirectoryResolver = null,
        Func<IAiModelClient>? aiModelClientFactory = null,
        AiSettingsStore? aiSettingsStore = null)
    {
        _aiModelClientFactory = aiModelClientFactory ?? CreateDefaultAiModelClient;
        _checkAiConnectionWithModelFactory = workspaceService is null || aiModelClientFactory is not null;
        _workspaceService = workspaceService ?? CreateWorkspaceService();
        _audioPreviewService = audioPreviewService ?? new AudioPreviewService(ComfyHttpClient);
        _psdLayerComponentClient = psdLayerComponentClient ?? new PsdLayerComponentClient(new ProjectMediaAssetService());
        _projectAssetResolver = projectAssetResolver ?? new ProjectAssetResolver();
        _aiSettingsStore = aiSettingsStore ?? new AiSettingsStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DraftAgent",
            "ai-settings.json"));
        LoadSavedAiSettings();
        _externalActionTransaction = new WorkbenchExternalActionTransaction(
            CommitWorkbenchAsync,
            () => Project,
            project => Project = project);
        BindingTargets.CollectionChanged += (_, _) =>
        {
            RefreshBindingTargetSections();
            ApplyBindingAssetDisplayNames();
        };
        _workbenchDisplayProjection = new WorkbenchDisplayProjectionSynchronizer(SceneTables);
        SelectedScriptDraft = ScriptDrafts[0];
        Project = new SceneInteractionBuilder().Build(ScriptText);
        CaptureWorkbenchResetBaseline(Project, _workspaceService.ExportJson(Project));
        if (loadLastProject)
        {
            _ = LoadLastProjectAsync();
        }
    }

    public Func<TtsRegenerationConfirmation, Task<bool>> ConfirmRegenerateCompletedTtsAsync { get; set; } =
        _ => Task.FromResult(false);

    public Func<ExportMissingVisualConfirmation, Task<bool>> ConfirmExportMissingVisualsAsync { get; set; } =
        _ => Task.FromResult(false);

    public Func<RoleMergeConfirmation, Task<bool>> ConfirmRoleMergeAsync { get; set; } =
        _ => Task.FromResult(false);

    public Func<PsdLayerMatchSettingsViewModel, Task<bool>> ConfirmPsdLayerMatchSettingsAsync { get; set; } =
        _ => Task.FromResult(false);

    public Func<Task<bool>> ConfirmResetWorkbenchAsync { get; set; } =
        () => Task.FromResult(false);

    public Func<ExportDestinationRequest, Task<ExportDestinationSelection?>> SelectExportDestinationAsync { get; set; } =
        _ => Task.FromResult<ExportDestinationSelection?>(null);

    public ObservableCollection<string> AiProviders { get; } =
    [
        "DeepSeek",
        "OpenAI",
        "Gemini",
        "LM Studio"
    ];

    public ObservableCollection<ScriptSegment> Segments { get; } = [];

    public ObservableCollection<ScriptDraftTabViewModel> ScriptDrafts { get; } =
    [
        new ScriptDraftTabViewModel("原文", DraftAnalyzer.SampleScript, canClose: false)
    ];

    public bool HasMultipleScriptDrafts => ScriptDrafts.Count > 1;

    public ScriptDraftTabViewModel? SelectedScriptDraft
    {
        get => _selectedScriptDraft;
        set
        {
            if (ReferenceEquals(_selectedScriptDraft, value))
            {
                return;
            }

            if (_selectedScriptDraft is not null)
            {
                _selectedScriptDraft.PropertyChanged -= OnSelectedScriptDraftPropertyChanged;
            }

            if (SetProperty(ref _selectedScriptDraft, value))
            {
                if (_selectedScriptDraft is not null)
                {
                    _selectedScriptDraft.PropertyChanged += OnSelectedScriptDraftPropertyChanged;
                }

                OnPropertyChanged(nameof(ScriptText));
            }
        }
    }

    public string ScriptText
    {
        get => SelectedScriptDraft?.Text ?? string.Empty;
        set
        {
            if (SelectedScriptDraft is null)
            {
                return;
            }

            if (SelectedScriptDraft.Text == value)
            {
                return;
            }

            SelectedScriptDraft.Text = value;
            OnPropertyChanged();
        }
    }

    private void OnSelectedScriptDraftPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScriptDraftTabViewModel.Text))
        {
            OnPropertyChanged(nameof(ScriptText));
        }
    }

    private void ApplyScriptDraftsFromProject(InteractionProject project)
    {
        var drafts = (project.ScriptDrafts.Count > 0
            ? project.ScriptDrafts
            : [new ProjectScriptDraft("原文", project.Script.CleanText, false)])
            .ToArray();

        if (drafts.Length > 0 && drafts.All(draft => draft.CanClose))
        {
            drafts[0] = drafts[0] with { CanClose = false };
        }

        if (_selectedScriptDraft is not null)
        {
            _selectedScriptDraft.PropertyChanged -= OnSelectedScriptDraftPropertyChanged;
        }

        _selectedScriptDraft = null;
        ScriptDrafts.Clear();
        foreach (var draft in drafts)
        {
            var title = string.IsNullOrWhiteSpace(draft.Title) ? "原文" : draft.Title.Trim();
            ScriptDrafts.Add(new ScriptDraftTabViewModel(title, draft.Text ?? string.Empty, draft.CanClose));
        }

        if (ScriptDrafts.Count == 0)
        {
            ScriptDrafts.Add(new ScriptDraftTabViewModel("原文", project.Script.CleanText, canClose: false));
        }

        var selectedIndex = project.SelectedScriptDraftIndex >= 0 && project.SelectedScriptDraftIndex < ScriptDrafts.Count
            ? project.SelectedScriptDraftIndex
            : 0;
        SelectedScriptDraft = ScriptDrafts[selectedIndex];
        OnPropertyChanged(nameof(HasMultipleScriptDrafts));
        OnPropertyChanged(nameof(ScriptText));
    }

    private InteractionProject AttachCurrentScriptDrafts(InteractionProject project)
    {
        var selectedIndex = SelectedScriptDraft is null
            ? 0
            : Math.Max(0, ScriptDrafts.IndexOf(SelectedScriptDraft));
        return project with
        {
            ScriptDrafts = ScriptDrafts
                .Select(tab => new ProjectScriptDraft(tab.Title, tab.Text, tab.CanClose))
                .ToArray(),
            SelectedScriptDraftIndex = selectedIndex
        };
    }

    private async Task SaveCurrentProjectSnapshotAsync()
    {
        Project = AttachCurrentScriptDrafts(Project);
        await PersistCurrentProjectSnapshotAsync(Project);
    }

    private async Task PersistCurrentProjectSnapshotAsync(InteractionProject project)
    {
        await _workspaceService.SaveProjectSnapshotAsync(project);
        if (_workspaceProjectManifest is not null)
        {
            var workspace = CurrentWorkspaceProject();
            SetWorkspaceProjectManifest(await _workspaceService.SaveWorkspaceProjectDraftAsync(workspace, project));
        }
    }

    private void CaptureWorkbenchResetBaseline(InteractionProject project, string? rawJson = null)
    {
        _workbenchResetBaselineJson = string.IsNullOrWhiteSpace(rawJson)
            ? _workspaceService.ExportJson(project)
            : rawJson;
    }

    private async Task CaptureWorkspaceOriginalAiJsonAsync(string rawJson)
    {
        if (_workspaceProjectManifest is null || string.IsNullOrWhiteSpace(rawJson))
        {
            return;
        }

        SetWorkspaceProjectManifest(await _workspaceService.SaveWorkspaceOriginalAiJsonAsync(
            CurrentWorkspaceProject(),
            rawJson));
    }

    private async Task CaptureWorkspaceOriginalAiJsonIfMissingAsync(string rawJson)
    {
        if (_workspaceProjectManifest is null || string.IsNullOrWhiteSpace(rawJson))
        {
            return;
        }

        var existing = await _workspaceService.LoadWorkspaceOriginalAiJsonAsync(CurrentWorkspaceProject());
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return;
        }

        await CaptureWorkspaceOriginalAiJsonAsync(rawJson);
    }

    private async Task<string> LoadWorkbenchResetBaselineJsonAsync()
    {
        if (_workspaceProjectManifest is not null)
        {
            var workspaceJson = await _workspaceService.LoadWorkspaceOriginalAiJsonAsync(CurrentWorkspaceProject());
            if (!string.IsNullOrWhiteSpace(workspaceJson))
            {
                _workbenchResetBaselineJson = workspaceJson;
                return workspaceJson;
            }
        }

        return string.IsNullOrWhiteSpace(_workbenchResetBaselineJson)
            ? _workspaceService.ExportJson(Project)
            : _workbenchResetBaselineJson;
    }

    private async Task<InteractionProject> RestoreWorkbenchResetBaselineAsync()
    {
        var json = await LoadWorkbenchResetBaselineJsonAsync();
        return JsonSerializer.Deserialize<InteractionProject>(json, DraftJson.Options)
            ?? throw new InvalidOperationException("无法从原始 AI JSON 重建工作台。");
    }

    private ScriptDraftTabViewModel EnsureScriptDraft(string title)
    {
        var existing = ScriptDrafts.FirstOrDefault(tab => tab.Title == title);
        if (existing is not null)
        {
            return existing;
        }

        var tab = new ScriptDraftTabViewModel(title);
        ScriptDrafts.Add(tab);
        OnPropertyChanged(nameof(HasMultipleScriptDrafts));
        return tab;
    }

    [RelayCommand]
    private void CloseScriptDraft(ScriptDraftTabViewModel? tab)
    {
        if (tab is null || !tab.CanClose || !ScriptDrafts.Contains(tab))
        {
            return;
        }

        var selected = ReferenceEquals(SelectedScriptDraft, tab);
        ScriptDrafts.Remove(tab);
        if (selected)
        {
            SelectedScriptDraft = ScriptDrafts.FirstOrDefault();
        }

        OnPropertyChanged(nameof(HasMultipleScriptDrafts));
        Project = AttachCurrentScriptDrafts(Project);
    }

    public ObservableCollection<Character> Characters { get; } = [];

    public ObservableCollection<SceneTableViewModel> SceneTables { get; } = [];

    public ObservableCollection<WorkbenchDisplayItem> WorkbenchDisplayItems => _workbenchDisplayProjection.Items;

    public ObservableCollection<BindingTargetViewModel> BindingTargets { get; } = [];

    public ObservableCollection<BindingTargetViewModel> SceneBindingTargets { get; } = [];

    public ObservableCollection<BindingTargetViewModel> CharacterBindingTargets { get; } = [];

    public ObservableCollection<BindingTargetViewModel> NarrationBindingTargets { get; } = [];

    public ObservableCollection<GenerationTask> GenerationTasks { get; } = [];

    public ObservableCollection<DraftIssue> Issues { get; } = [];

    public ObservableCollection<ExportPreflightIssueViewModel> ExportPreflightIssues { get; } = [];

    public ObservableCollection<string> CharacterNameOptions { get; } = [];

    public ObservableCollection<string> VoiceReferenceOptions { get; } =
    [
        IndexTts2Defaults.DefaultVoiceReferenceId,
        CustomVoiceReferenceOption
    ];

    public string BindingPanelToggleText => "角色管理";

    public string WorkspaceProjectMeta => string.IsNullOrWhiteSpace(WorkspaceProjectPath)
        ? "项目：未指定"
        : $"项目：{WorkspaceProjectName} / {WorkspaceProjectPath}";

    public bool HasWorkspaceProject => _workspaceProjectManifest is not null;

    public bool HasExportPreflightIssues => ExportPreflightIssues.Count > 0;

    public bool HasSceneBindingTargets => SceneBindingTargets.Count > 0;

    public bool HasCharacterBindingTargets => CharacterBindingTargets.Count > 0;

    public bool HasNarrationBindingTargets => NarrationBindingTargets.Count > 0;

    public BindingTargetViewModel? CharacterPsdLayerMatchTarget =>
        ResolveCharacterPsdLayerMatchTarget(SelectedCharacterBindingTarget);

    public bool CanConfigureSelectedCharacterPsdLayerMatch =>
        CanEditWorkbenchStructure &&
        CharacterPsdLayerMatchTarget is not null;

    public string CharacterPsdLayerMatchSettingsText =>
        CharacterPsdLayerMatchTarget?.PsdLayerMatchSettingsText ?? "请先给任一角色绑定 PSD";

    public string RoleDialogueVoiceRouteText => "台词使用角色参考音";

    public string RoleSceneNarrationVoiceRouteText => "场景/旁白使用默认配音";

    public string RoleThoughtVoiceRouteText => UseRoleVoiceForThought
        ? "内心使用角色参考音"
        : "内心使用默认配音";

    public string RoleActionVoiceRouteText => UseRoleVoiceForAction
        ? "动作使用角色参考音"
        : "动作使用默认配音";

    public string RoleAllVoiceRouteText => UseRoleVoiceForAllCharacterLines
        ? "角色行使用参考音频配音"
        : "角色行使用默认配音";

    public bool ShowRoleVoiceDetailRouteButtons => UseRoleVoiceForAllCharacterLines;

    public bool CanEditCharacterVoiceBindings => UseRoleVoiceForAllCharacterLines;

    public bool HasSelectedDialogueRow => SelectedRow is { IsNarration: false };

    public bool IsWorkbenchMutationLocked => IsTtsSynthesisRunning || IsPsdLayerAiMatchingRunning || IsProjectExportRunning;

    public bool CanDragWorkbench => !IsWorkbenchMutationLocked;

    public bool CanEditWorkbenchStructure => !IsWorkbenchMutationLocked;

    public string TtsSynthesisActionText => IsTtsSynthesisRunning ? "停止合成" : "语音合成";

    public bool IsWorkbenchDirty => _workbenchSession.IsDirty;

    public bool CanUndoWorkbench => _workbenchSession.CanUndo;

    public bool CanRedoWorkbench => _workbenchSession.CanRedo;

    public string? NextUndoWorkbenchOperationName => _workbenchSession.NextUndoOperationName;

    public string? NextRedoWorkbenchOperationName => _workbenchSession.NextRedoOperationName;

    internal WorkbenchDocument WorkbenchDocument => _workbenchSession.Document;

    public string ProjectMeta
    {
        get
        {
            var state = CurrentDerivedState();
            return $"{state.SceneCount} 个场景 / {state.CharacterCount} 个人物 / {state.NarrationCount} 条旁白 / {state.ResultRowCount} 行";
        }
    }

    public int ResultRowCount => CurrentDerivedState().ResultRowCount;

    public int NarrationCount => CurrentDerivedState().NarrationCount;

    public int TtsTaskCount => CurrentDerivedState().TtsTaskCount;

    public int TtsCompletedCount => CurrentDerivedState().TtsCompletedCount;

    public int TtsQueuedCount => CurrentDerivedState().TtsQueuedCount;

    public int TtsFailedCount => CurrentDerivedState().TtsFailedCount;

    public string TtsQueueMeta
    {
        get
        {
            var state = CurrentDerivedState();
            return $"语音 {state.TtsCompletedCount}/{state.TtsTaskCount}，队列 {state.TtsQueuedCount}，失败 {state.TtsFailedCount}";
        }
    }

    public IBrush ComfyEndpointBackground => ComfyConnectionState switch
    {
        "success" => new SolidColorBrush(Color.Parse("#102A1A")),
        "failed" => new SolidColorBrush(Color.Parse("#2A1114")),
        _ => new SolidColorBrush(Color.Parse("#111722"))
    };

    public IBrush ComfyEndpointBorderBrush => ComfyConnectionState switch
    {
        "success" => new SolidColorBrush(Color.Parse("#34C759")),
        "failed" => new SolidColorBrush(Color.Parse("#FF453A")),
        _ => new SolidColorBrush(Color.Parse("#2C3546"))
    };

    public IBrush AiEndpointBackground => AiConnectionState switch
    {
        "success" => new SolidColorBrush(Color.Parse("#102A1A")),
        "failed" => new SolidColorBrush(Color.Parse("#2A1114")),
        _ => new SolidColorBrush(Color.Parse("#111722"))
    };

    public IBrush AiEndpointBorderBrush => AiConnectionState switch
    {
        "success" => new SolidColorBrush(Color.Parse("#34C759")),
        "failed" => new SolidColorBrush(Color.Parse("#FF453A")),
        _ => new SolidColorBrush(Color.Parse("#2C3546"))
    };

    public IBrush AiApiKeyBackground => IsAiApiKeyInvalid
        ? new SolidColorBrush(Color.Parse("#2A1114"))
        : new SolidColorBrush(Color.Parse("#111722"));

    public IBrush AiApiKeyBorderBrush => IsAiApiKeyInvalid
        ? new SolidColorBrush(Color.Parse("#FF453A"))
        : new SolidColorBrush(Color.Parse("#2C3546"));

    private bool IsAiApiKeyInvalid =>
        HasAiApiKeyValidationError ||
        (AiConnectionState == "failed" &&
            AiSettingsPolicy.RequiresApiKey(SelectedAiProvider) &&
            string.IsNullOrWhiteSpace(AiApiKey));

    private DraftWorkspaceService CreateWorkspaceService()
    {
        var llmClient = new HttpLlmAnalysisClient(CreateAiModelClient());
        var comfyClient = new ComfyUiHttpClient(ComfyHttpClient, () => new ComfyUiOptions(ComfyEndpoint));
        var storePath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DraftAgent",
            "last-project.json");
        var workspaceStatePath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DraftAgent",
            "last-workspace-project.json");
        return new DraftWorkspaceService(
            llmClient,
            comfyClient,
            new FileInteractionProjectStore(storePath),
            workspaceProjectStateStore: new FileWorkspaceProjectStateStore(workspaceStatePath));
    }

    private IAiModelClient CreateAiModelClient()
    {
        return _aiModelClientFactory();
    }

    private IAiModelClient CreateDefaultAiModelClient()
    {
        return new HttpAiModelClient(
            LlmHttpClient,
            () => new AiServiceOptions(SelectedAiProvider, AiApiKey, AiBaseUrl, AiModelName, ResolveAiMaxOutputTokens()));
    }

    private void HandleExternalError(string title, Exception exception)
    {
        AppRuntimeLog.Error(title, exception);
        AppRuntimeLog.Info(BuildWorkflowLogSnapshot());
        if (FindAiReplyException(exception) is { } replyException)
        {
            AiReplyStatusText = FormatReplyStatus(replyException.Metadata);
        }

        ApplyExportPreflightIssues(exception);
        StatusText = FormatExternalErrorStatus(title, exception);
    }

    private void ClearExportPreflightIssues()
    {
        if (ExportPreflightIssues.Count == 0)
        {
            return;
        }

        ExportPreflightIssues.Clear();
        OnPropertyChanged(nameof(HasExportPreflightIssues));
    }

    [RelayCommand]
    private void ReturnToWorkbenchFromExportIssues()
    {
        ClearExportPreflightIssues();
        StatusText = "已返回工作台。导出问题列表已关闭，可继续修改绑定或重新导出。";
    }

    private void ApplyExportPreflightIssues(Exception exception)
    {
        if (exception is not EditingTimelinePreflightException { Issues.Count: > 0 } preflightException)
        {
            ClearExportPreflightIssues();
            return;
        }

        ApplyExportPreflightIssues(preflightException.StructuredIssues);
    }

    private void ApplyExportPreflightIssues(IReadOnlyList<EditingTimelinePreflightIssue> structuredIssues)
    {
        if (structuredIssues.Count == 0)
        {
            ClearExportPreflightIssues();
            return;
        }

        ExportPreflightIssues.Clear();
        for (var index = 0; index < structuredIssues.Count; index++)
        {
            var issue = structuredIssues[index];
            ExportPreflightIssues.Add(new ExportPreflightIssueViewModel(
                index + 1,
                issue.Kind,
                issue.Message,
                issue.SceneId,
                issue.InteractionId,
                issue.AssetReference ?? issue.TaskId ?? string.Empty,
                issue.TaskId ?? string.Empty,
                issue.TaskStatus,
                issue.SuggestedAction)
            {
                DisplaySceneLabel = ResolveExportIssueSceneLabel(issue.SceneId),
                DisplayRowLabel = ResolveExportIssueRowLabel(issue.SceneId, issue.InteractionId)
            });
        }

        OnPropertyChanged(nameof(HasExportPreflightIssues));
    }

    private string? ResolveExportIssueSceneLabel(string sceneId)
    {
        return SceneTables.FirstOrDefault(scene =>
            string.Equals(scene.SceneId, sceneId, StringComparison.Ordinal))?.SceneLabel;
    }

    private string? ResolveExportIssueRowLabel(string sceneId, string interactionId)
    {
        var scene = SceneTables.FirstOrDefault(candidate =>
            string.Equals(candidate.SceneId, sceneId, StringComparison.Ordinal));
        return scene?.Rows.FirstOrDefault(row =>
            string.Equals(row.InteractionId, interactionId, StringComparison.Ordinal))?.OrderLabel;
    }

    private static string FormatExternalErrorStatus(string title, Exception exception)
    {
        if (exception is EditingTimelinePreflightException { Issues.Count: > 0 } preflightException)
        {
            return $"{title}：发现 {preflightException.Issues.Count} 个导出前检查问题，请在导出提示中定位处理。";
        }

        if (TryFormatKnownTtsFailure(exception.ToString()) is { } knownTtsFailure)
        {
            return $"{title}：{knownTtsFailure}";
        }

        return $"{title}：{exception.Message}";
    }

    private string BuildWorkflowLogSnapshot()
    {
        var entries = _workspaceService.LogEntries.TakeLast(12);
        var lines = entries.Select(entry =>
        {
            var data = string.Join(", ", entry.Data.Select(pair => $"{pair.Key}={pair.Value}"));
            return $"{entry.Timestamp:O} [{entry.Level}] {entry.Step}/{entry.Operation}: {entry.Message} {data}";
        });
        return $"最近工作流日志：{Environment.NewLine}{string.Join(Environment.NewLine, lines)}";
    }

    private static string NormalizeEndpoint(string value)
    {
        var endpoint = string.IsNullOrWhiteSpace(value) ? DefaultComfyEndpoint : value.Trim();
        return endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? endpoint.TrimEnd('/')
            : $"http://{endpoint.TrimEnd('/')}";
    }

    private static string? BindingValue(string? value)
    {
        return WorkbenchBindingValue.Asset(value);
    }

    private void LoadSavedAiSettings()
    {
        if (_aiSettingsStore.Load() is not { } settings)
        {
            return;
        }

        SelectedAiProvider = string.IsNullOrWhiteSpace(settings.SelectedAiProvider)
            ? AiSettingsPolicy.DefaultProvider
            : settings.SelectedAiProvider;
        AiApiKey = settings.AiApiKey ?? string.Empty;
        AiBaseUrl = string.IsNullOrWhiteSpace(settings.AiBaseUrl)
            ? AiSettingsPolicy.DefaultsFor(SelectedAiProvider).BaseUrl
            : settings.AiBaseUrl;
        AiModelName = string.IsNullOrWhiteSpace(settings.AiModelName)
            ? AiSettingsPolicy.DefaultsFor(SelectedAiProvider).ModelName
            : settings.AiModelName;
        AiMaxOutputTokens = AiSettingsPolicy.ResolveMaxOutputTokens(
            settings.AiMaxOutputTokens.ToString(),
            AiSettingsPolicy.DefaultMaxOutputTokens);
        AiMaxOutputTokensText = AiMaxOutputTokens.ToString();
        ComfyEndpoint = NormalizeEndpoint(settings.ComfyEndpoint);
    }

    private sealed record AiSettingsSnapshot(
        string SelectedAiProvider,
        string AiApiKey,
        string AiBaseUrl,
        string AiModelName,
        int AiMaxOutputTokens,
        string AiMaxOutputTokensText,
        string ComfyEndpoint);

}
