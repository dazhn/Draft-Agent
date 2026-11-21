using System;
using System.Collections.Generic;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using DraftAgent.Core;

namespace DraftAgent.App.ViewModels;

public enum BindingTargetKind
{
    Scene,
    Character,
    Narration
}

public sealed partial class BindingTargetViewModel : ObservableObject
{
    private const string VoiceReferenceTextSourceSavedBinding = "Saved binding";
    private const string VoiceReferenceTextSourceManualEdit = "Manual edit";

    private string _category;
    private string _displayName;
    private string _editableDisplayName;
    private string _detail;
    private VisualAssetBinding _visualBinding = VisualAssetBinding.None;
    private VoiceAssetBinding _voiceBinding = VoiceAssetBinding.None;
    private IReadOnlyList<BindingTargetRowVisualDetailViewModel> _rowVisualDetails = [];
    private ImagePreviewSnapshot? _imagePreview;
    private string? _visualSourceDisplayName;
    private string? _visualRenderedDisplayName;
    private bool _suppressVoiceReferenceTextSourceUpdate;
    private bool _suppressBindingChangeTracking;
    private string _lockedVoiceReferenceText = string.Empty;
    private string _baselineImageAsset = "未绑定图片";
    private string _baselineVoiceAsset = "未绑定声音";
    private string _baselineVoiceReferenceText = string.Empty;
    private string _baselineEffectiveVoiceReferenceText = string.Empty;
    private string _baselineVoiceSourceAsset = string.Empty;
    private string _baselineVoiceSyncedPath = string.Empty;
    private bool _canEditVoiceBinding = true;
    private bool _canEditVoiceReferenceText = true;

    [ObservableProperty]
    private string _imageAsset = "未绑定图片";

    [ObservableProperty]
    private string _imagePreviewAsset = string.Empty;

    [ObservableProperty]
    private string _voiceAsset = "未绑定声音";

    [ObservableProperty]
    private string _voiceReferenceText = string.Empty;

    [ObservableProperty]
    private string _voiceReferenceTextSourceText = VoiceReferenceTextSourceSavedBinding;

    [ObservableProperty]
    private string _voiceSourceAsset = string.Empty;

    [ObservableProperty]
    private string _voiceSyncedPath = string.Empty;

    [ObservableProperty]
    private string _statusText = "待绑定";

    [ObservableProperty]
    private bool _isEditingDisplayName;

    private BindingTargetViewModel(BindingTargetKind kind, string targetId, string category, string displayName, string detail, bool supportsImage, bool supportsVoice)
    {
        Kind = kind;
        TargetId = targetId;
        _category = category;
        _displayName = displayName;
        _editableDisplayName = displayName;
        _detail = detail;
        SupportsImage = supportsImage;
        SupportsVoice = supportsVoice;
    }

    public BindingTargetKind Kind { get; }

    public string TargetId { get; }

    public string Category
    {
        get => _category;
        private set => SetProperty(ref _category, value);
    }

    public string DisplayName
    {
        get => _displayName;
        private set
        {
            if (SetProperty(ref _displayName, value))
            {
                _editableDisplayName = value;
                OnPropertyChanged(nameof(EditableDisplayName));
            }
        }
    }

    public string EditableDisplayName
    {
        get => _editableDisplayName;
        set => SetProperty(ref _editableDisplayName, value ?? string.Empty);
    }

    public string Detail
    {
        get => _detail;
        private set => SetProperty(ref _detail, value);
    }

    public bool SupportsImage { get; }

    public bool SupportsVoice { get; }

    public bool CanEditVoiceBinding
    {
        get => SupportsVoice && _canEditVoiceBinding;
        private set
        {
            if (SetProperty(ref _canEditVoiceBinding, value))
            {
                OnPropertyChanged(nameof(VoiceBindingAccessText));
            }
        }
    }

    public string VoiceBindingAccessText => !SupportsVoice
        ? "无需声音"
        : CanEditVoiceBinding
            ? "角色参考音频已启用"
            : "未启用角色参考音频时使用默认配音";

    public bool CanPreviewVoiceBinding => SupportsVoice;

    public bool CanEditVoiceReferenceText
    {
        get => SupportsVoice && CanEditVoiceBinding && _canEditVoiceReferenceText;
        private set
        {
            var oldEffective = CanEditVoiceReferenceText;
            if (_canEditVoiceReferenceText == value)
            {
                return;
            }

            _canEditVoiceReferenceText = value;
            if (oldEffective != CanEditVoiceReferenceText)
            {
                OnPropertyChanged();
            }
        }
    }

    public bool ShowVoiceBindingControls => SupportsVoice;

    public bool CanEditDisplayName => Kind == BindingTargetKind.Character;

    public string RenameButtonText => IsEditingDisplayName ? "保存" : "重命名";

    partial void OnIsEditingDisplayNameChanged(bool value)
    {
        if (value)
        {
            EditableDisplayName = DisplayName;
        }

        OnPropertyChanged(nameof(RenameButtonText));
    }

    internal void BeginDisplayNameEdit()
    {
        if (!CanEditDisplayName)
        {
            return;
        }

        IsEditingDisplayName = true;
    }

    internal void CancelDisplayNameEdit()
    {
        EditableDisplayName = DisplayName;
        IsEditingDisplayName = false;
    }

    internal void AcceptDisplayNameEdit(string displayName)
    {
        DisplayName = displayName;
        EditableDisplayName = displayName;
        IsEditingDisplayName = false;
    }

    public bool SupportsPsd => SupportsImage && Kind == BindingTargetKind.Character;

    public bool HasPsdLayerMatchSettings => _visualBinding.PsdLayerMatch?.HasAnyGroup == true;

    public bool CanConfigurePsdLayerMatch => SupportsPsd && _visualBinding.SourceKind == VisualSourceKind.Psd;

    public string PsdLayerMatchSettingsText => _visualBinding.PsdLayerMatch is { HasAnyGroup: true } settings
        ? FormatPsdLayerMatchSettings(settings)
        : "未设置 PSD 图层匹配";

    public bool UsesLargeBindingPreview => SupportsImage && !SupportsVoice;

    internal VisualAssetBinding VisualBinding => _visualBinding;

    internal VoiceAssetBinding VoiceBinding => _voiceBinding;

    internal bool HasImageBindingEdit => SupportsImage &&
        !string.Equals(ImageAsset, _baselineImageAsset, StringComparison.Ordinal);

    internal bool HasVoiceBindingEdit => SupportsVoice &&
        (!string.Equals(VoiceAsset, _baselineVoiceAsset, StringComparison.Ordinal) ||
         !string.Equals(EffectiveVoiceReferenceTextForBinding(), _baselineEffectiveVoiceReferenceText, StringComparison.Ordinal) ||
         !string.Equals(VoiceSourceAsset, _baselineVoiceSourceAsset, StringComparison.Ordinal) ||
         !string.Equals(VoiceSyncedPath, _baselineVoiceSyncedPath, StringComparison.Ordinal));

    internal bool HasAnyBindingEdit => HasImageBindingEdit || HasVoiceBindingEdit;

    internal string EffectiveVoiceReferenceTextForBinding()
    {
        return _canEditVoiceReferenceText
            ? VoiceReferenceText
            : _lockedVoiceReferenceText;
    }

    public string ImagePreviewText => SupportsImage ? ImageAsset : "无需图片";

    public string ImagePreviewLabel => ImagePreview.Label;

    public bool HasImagePreview => ImagePreview.HasImage;

    public bool HasNoImagePreview => !HasImagePreview;

    public string ImagePreviewSource => ImagePreview.Source;

    public bool BindingPreviewShowsImage => HasImagePreview;

    public bool BindingPreviewShowsText => !BindingPreviewShowsImage;

    public string BindingPreviewTitle => BindingPreviewShowsImage
        ? ImagePreviewLabel
        : _visualBinding.SourceKind == VisualSourceKind.Psd
        ? VisualSourceText
        : ImagePreviewLabel;

    public string BindingPreviewSubtitle => _visualBinding.SourceKind == VisualSourceKind.Psd
        ? "PSD"
        : VisualSourceKindText;

    private ImagePreviewSnapshot ImagePreview =>
        _imagePreview ??= CaptureImagePreview(ImagePreviewAsset, ImagePreviewText);

    public string VoicePreviewText => SupportsVoice ? VoiceAsset : "无需声音";

    public bool HasVisualSummary => SupportsImage;

    public string VisualSourceKindText => SupportsImage ? FormatVisualSourceKind(_visualBinding.SourceKind) : "无需图片";

    public string VisualSourceText => SupportsImage
        ? FirstDisplayText(
            _visualSourceDisplayName,
            FormatAssetSummary(_visualBinding.SourceAssetId ?? WorkbenchBindingValue.Asset(ImageAsset), "未绑定源"))
        : "无需图片";

    public string VisualRenderedText => SupportsImage
        ? FirstDisplayText(
            _visualRenderedDisplayName,
            FormatAssetSummary(VisualRenderedAssetReference(), "未生成预览"))
        : "无需图片";

    public string VisualRenderedLabel => _visualBinding.SourceKind == VisualSourceKind.Psd
        ? "PSD 输出"
        : "当前图";

    public bool HasVoiceSummary => ShowVoiceBindingControls;

    public IReadOnlyList<BindingTargetRowVisualDetailViewModel> RowVisualDetails => _rowVisualDetails;

    public bool HasRowVisualDetails => _rowVisualDetails.Count > 0;

    public string VoiceSourceText => SupportsVoice
        ? FormatAssetSummary(_voiceBinding.SourceAssetId, "未登记项目源")
        : "无需声音";

    public string VoiceSourceStateText => SupportsVoice
        ? FormatVoiceSourceState(_voiceBinding)
        : "No voice required";

    public string VoiceSyncedText => SupportsVoice
        ? FormatAssetSummary(_voiceBinding.SyncedVoicePath ?? WorkbenchBindingValue.Asset(VoiceAsset), "未同步 voices")
        : "无需声音";

    public string VoiceSyncedStateText => SupportsVoice
        ? FormatVoiceSyncedState(_voiceBinding, VoiceAsset)
        : "No voice required";

    public string VoiceReferenceTextPreview => SupportsVoice
        ? FormatReferenceTextPreview(VoiceReferenceText)
        : "无需声音";

    public string VoiceReferenceStateText => SupportsVoice
        ? FormatVoiceReferenceState(_voiceBinding, VoiceReferenceTextSourceText)
        : "No voice required";

    public string VoiceDiagnosticText => SupportsVoice
        ? FormatVoiceDiagnostic(_voiceBinding, VoiceAsset, VoiceReferenceTextSourceText)
        : "No voice required";

    internal void ApplyRefreshedStateFrom(BindingTargetViewModel source)
    {
        if (Kind != source.Kind || TargetId != source.TargetId)
        {
            throw new InvalidOperationException("Cannot apply binding target state from a different target.");
        }

        var voiceReferenceTextSourceText = ShouldPreserveVoiceReferenceTextSource(source)
            ? VoiceReferenceTextSourceText
            : source.VoiceReferenceTextSourceText;
        ApplyState(
            source.Category,
            source.DisplayName,
            source.Detail,
            source.ImageAsset,
            source.ImagePreviewAsset,
            source.VoiceAsset,
            source.VoiceReferenceText,
            voiceReferenceTextSourceText,
            source.VoiceSourceAsset,
            source.VoiceSyncedPath,
            source._rowVisualDetails,
            source._visualBinding,
            source._visualSourceDisplayName,
            source._visualRenderedDisplayName,
            source._voiceBinding,
            source.CanEditVoiceBinding,
            resetBaseline: true);
    }

    private bool ShouldPreserveVoiceReferenceTextSource(BindingTargetViewModel source)
    {
        return SupportsVoice &&
            string.Equals(source.VoiceReferenceTextSourceText, VoiceReferenceTextSourceSavedBinding, StringComparison.Ordinal) &&
            !string.Equals(VoiceReferenceTextSourceText, VoiceReferenceTextSourceSavedBinding, StringComparison.Ordinal) &&
            string.Equals(VoiceAsset, source.VoiceAsset, StringComparison.Ordinal) &&
            string.Equals(VoiceReferenceText, source.VoiceReferenceText, StringComparison.Ordinal) &&
            string.Equals(VoiceSourceAsset, source.VoiceSourceAsset, StringComparison.Ordinal) &&
            string.Equals(VoiceSyncedPath, source.VoiceSyncedPath, StringComparison.Ordinal);
    }

    internal void ApplyVisualBindingSummary(VisualAssetBinding? visualBinding)
    {
        SetVisualBinding(visualBinding);
    }

    internal void ApplyVisualAssetDisplayNames(string? sourceDisplayName, string? renderedDisplayName)
    {
        var normalizedSource = NormalizeDisplayName(sourceDisplayName);
        var normalizedRendered = NormalizeDisplayName(renderedDisplayName);
        if (string.Equals(_visualSourceDisplayName, normalizedSource, StringComparison.Ordinal) &&
            string.Equals(_visualRenderedDisplayName, normalizedRendered, StringComparison.Ordinal))
        {
            return;
        }

        _visualSourceDisplayName = normalizedSource;
        _visualRenderedDisplayName = normalizedRendered;
        OnPropertyChanged(nameof(VisualSourceText));
        OnPropertyChanged(nameof(VisualRenderedText));
        NotifyBindingPreviewChanged();
    }

    internal void ApplyPsdSourceBinding(string psdAssetId, VisualAssetBinding visualBinding)
    {
        _suppressBindingChangeTracking = true;
        try
        {
            ImageAsset = DisplayImageBinding(psdAssetId);
            ImagePreviewAsset = string.Empty;
            SetVisualBinding(visualBinding);
        }
        finally
        {
            _suppressBindingChangeTracking = false;
        }

        ResetBindingEditBaseline();
    }

    internal void ApplyVoiceBindingSummary(VoiceAssetBinding? voiceBinding)
    {
        SetVoiceBinding(voiceBinding);
    }

    internal void ApplyVoiceReferenceText(string referenceText, string sourceText)
    {
        _suppressVoiceReferenceTextSourceUpdate = true;
        try
        {
            VoiceReferenceText = referenceText;
        }
        finally
        {
            _suppressVoiceReferenceTextSourceUpdate = false;
        }

        SetVoiceReferenceTextSource(sourceText);
        var hasSidecarSource = IsVoiceReferenceTextFileSource(sourceText);
        _lockedVoiceReferenceText = hasSidecarSource ? referenceText : string.Empty;
        ApplyVoiceReferenceTextEditAccess(!hasSidecarSource);
    }

    private static bool IsVoiceReferenceTextFileSource(string? sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return false;
        }

        return sourceText.Contains("sidecar", StringComparison.OrdinalIgnoreCase);
    }

    internal void ApplyVoiceReferenceTextEditAccess(bool canEdit)
    {
        if (canEdit)
        {
            _lockedVoiceReferenceText = string.Empty;
        }

        CanEditVoiceReferenceText = canEdit;
    }

    internal void ApplyCurrentRowMarker(string? interactionId)
    {
        if (_rowVisualDetails.Count == 0)
        {
            return;
        }

        var details = new BindingTargetRowVisualDetailViewModel[_rowVisualDetails.Count];
        var changed = false;
        for (var index = 0; index < _rowVisualDetails.Count; index++)
        {
            var detail = _rowVisualDetails[index];
            var isCurrentRow = !string.IsNullOrWhiteSpace(interactionId) &&
                string.Equals(detail.InteractionId, interactionId, StringComparison.Ordinal);
            changed |= detail.IsCurrentRow != isCurrentRow;
            details[index] = detail with { IsCurrentRow = isCurrentRow };
        }

        if (changed)
        {
            SetRowVisualDetails(details);
        }
    }

    public static BindingTargetViewModel ForScene(SceneTableViewModel scene)
    {
        var visualBinding = scene.VisualBinding ?? VisualAssetBinding.FromImage(WorkbenchBindingValue.Asset(scene.SelectedSceneAsset));
        var effectiveAsset = VisualPreviewReference(visualBinding, scene.SelectedSceneAsset);
        var target = new BindingTargetViewModel(BindingTargetKind.Scene, scene.SceneId, "场景", scene.SceneName, scene.SceneEffect, true, false)
        {
            ImageAsset = DisplayImageBinding(effectiveAsset)
        };
        target.SetVisualBinding(visualBinding);
        target.ResetBindingEditBaseline();
        return target;
    }

    public static BindingTargetViewModel ForCharacter(Character character, IEnumerable<SceneTableViewModel> scenes)
    {
        return ForCharacterRows(character, CharacterRows(character, scenes));
    }

    internal static BindingTargetViewModel ForCharacterRows(
        Character character,
        IReadOnlyList<AnalysisResultRowViewModel> rows,
        Func<string?, string?>? resolveProjectAssetPath = null,
        IReadOnlyList<GenerationTask>? generationTasks = null)
    {
        var firstRow = rows.Count > 0 ? rows[0] : null;
        var visualBinding = CharacterTargetVisualBinding(firstRow, character);
        var voiceBinding = CharacterTargetVoiceBinding(character);
        var target = new BindingTargetViewModel(BindingTargetKind.Character, character.Id, "人物", character.Name, $"{rows.Count} 条台词", true, true)
        {
            ImageAsset = DisplayImageBinding(CharacterTargetPreviewReference(visualBinding, firstRow, character)),
            VoiceAsset = DisplayVoiceBinding(voiceBinding.EffectiveVoiceReferenceId),
            VoiceReferenceText = voiceBinding.EffectiveReferenceText ?? string.Empty,
            VoiceSourceAsset = voiceBinding.SourceAssetId ?? string.Empty,
            VoiceSyncedPath = voiceBinding.SyncedVoicePath ?? string.Empty
        };
        target.SetVisualBinding(visualBinding);
        target.SetVoiceBinding(voiceBinding);
        target.SetVoiceReferenceTextSource(VoiceReferenceTextSourceSavedBinding);
        target.SetRowVisualDetails(BuildRowVisualDetails(rows, resolveProjectAssetPath, generationTasks));
        target.ResetBindingEditBaseline();
        return target;
    }

    private static string? CharacterTargetPreviewReference(
        VisualAssetBinding visualBinding,
        AnalysisResultRowViewModel? firstRow,
        Character character)
    {
        if (character.AssetBinding.PortraitVisualBinding is { SourceKind: not VisualSourceKind.None } defaultBinding)
        {
            return VisualPreviewReference(defaultBinding, character.AssetBinding.Portrait);
        }

        if (visualBinding.SourceKind == VisualSourceKind.Psd)
        {
            return FirstBoundValue(character.AssetBinding.Portrait);
        }

        return visualBinding.EffectiveAssetId ?? FirstBoundValue(firstRow?.SelectedCharacterAsset, character.AssetBinding.Portrait);
    }

    private static VisualAssetBinding CharacterTargetVisualBinding(
        AnalysisResultRowViewModel? firstRow,
        Character character)
    {
        if (character.AssetBinding.PortraitVisualBinding is { SourceKind: not VisualSourceKind.None } defaultBinding)
        {
            return defaultBinding;
        }

        if (firstRow?.CharacterVisualBinding is { SourceKind: not VisualSourceKind.None } rowBinding)
        {
            return rowBinding;
        }

        return VisualAssetBinding.FromImage(FirstBoundValue(firstRow?.SelectedCharacterAsset, character.AssetBinding.Portrait));
    }

    private static VoiceAssetBinding CharacterTargetVoiceBinding(Character character)
    {
        if (HasReferenceVoiceBinding(character.AssetBinding.VoiceBinding))
        {
            return character.AssetBinding.VoiceBinding!;
        }

        return VoiceAssetBinding.None;
    }

    private static bool HasReferenceVoiceBinding(VoiceAssetBinding? binding)
    {
        return binding is not null &&
            !VoiceAssetBinding.SemanticEquals(binding, VoiceAssetBinding.None) &&
            !string.IsNullOrWhiteSpace(binding.EffectiveVoiceReferenceId);
    }

    private static string? VisualPreviewReference(VisualAssetBinding? visualBinding, string? fallbackReference)
    {
        if (visualBinding is { SourceKind: VisualSourceKind.Psd })
        {
            return visualBinding.RenderedAssetId;
        }

        return visualBinding?.EffectiveAssetId ?? fallbackReference;
    }

    public static BindingTargetViewModel ForNarration(IEnumerable<SceneTableViewModel> scenes)
    {
        return ForNarrationRows(NarratorVoiceRows(scenes));
    }

    internal static BindingTargetViewModel ForNarrationRows(IReadOnlyList<AnalysisResultRowViewModel> rows)
    {
        var firstRow = rows.Count > 0 ? rows[0] : null;
        var voiceBinding = firstRow?.VoiceBinding ?? VoiceAssetBinding.None;
        var target = new BindingTargetViewModel(BindingTargetKind.Narration, "narration", "默认配音", "默认", $"{rows.Count} 条默认配音行", false, true)
        {
            VoiceAsset = DisplayVoiceBinding(voiceBinding.EffectiveVoiceReferenceId ?? WorkbenchBindingValue.Asset(firstRow?.SelectedVoiceReference)),
            VoiceReferenceText = voiceBinding.EffectiveReferenceText ?? firstRow?.SelectedVoiceReferenceText ?? string.Empty,
            VoiceSourceAsset = voiceBinding.SourceAssetId ?? string.Empty,
            VoiceSyncedPath = voiceBinding.SyncedVoicePath ?? string.Empty
        };
        target.SetVoiceBinding(voiceBinding);
        target.SetVoiceReferenceTextSource(VoiceReferenceTextSourceSavedBinding);
        target.ResetBindingEditBaseline();
        return target;
    }

    partial void OnImageAssetChanged(string value)
    {
        if (!_suppressBindingChangeTracking)
        {
            OnPropertyChanged(nameof(HasImageBindingEdit));
            OnPropertyChanged(nameof(HasAnyBindingEdit));
        }

        InvalidateImagePreview();
        SetVisualBinding(VisualAssetBinding.FromImage(WorkbenchBindingValue.Asset(value)));
        if (IsExistingImagePath(value))
        {
            ImagePreviewAsset = value;
        }
        else if (string.IsNullOrWhiteSpace(value) || value.StartsWith("未绑定", StringComparison.Ordinal))
        {
            ImagePreviewAsset = string.Empty;
        }

        OnPropertyChanged(nameof(ImagePreviewText));
        OnPropertyChanged(nameof(ImagePreviewLabel));
        OnPropertyChanged(nameof(HasImagePreview));
        OnPropertyChanged(nameof(HasNoImagePreview));
        OnPropertyChanged(nameof(ImagePreviewSource));
        NotifyBindingPreviewChanged();
    }

    partial void OnVoiceAssetChanged(string value)
    {
        if (string.Equals(value, MainWindowViewModel.CustomVoiceReferenceOption, StringComparison.Ordinal))
        {
            VoiceAsset = DisplayVoiceBinding(null);
            return;
        }

        NotifyVoiceBindingEditChanged();
        var voiceReferenceId = WorkbenchBindingValue.Asset(value);
        var previousVoiceReferenceId = _voiceBinding.EffectiveVoiceReferenceId;
        var keepSource = string.Equals(previousVoiceReferenceId, voiceReferenceId, StringComparison.Ordinal);
        if (!keepSource)
        {
            VoiceSourceAsset = string.Empty;
            VoiceSyncedPath = string.Empty;
        }

        SetVoiceBinding(VoiceAssetBinding.FromProjectReference(
            voiceReferenceId,
            WorkbenchBindingValue.Text(EffectiveVoiceReferenceTextForBinding()),
            keepSource ? WorkbenchBindingValue.Asset(VoiceSourceAsset) : null,
            keepSource ? WorkbenchBindingValue.Asset(VoiceSyncedPath) : null));
        OnPropertyChanged(nameof(VoicePreviewText));
    }

    partial void OnVoiceReferenceTextChanged(string value)
    {
        NotifyVoiceBindingEditChanged();
        SetVoiceBinding(VoiceAssetBinding.FromProjectReference(
            WorkbenchBindingValue.Asset(VoiceAsset),
            WorkbenchBindingValue.Text(EffectiveVoiceReferenceTextForBinding()),
            WorkbenchBindingValue.Asset(VoiceSourceAsset),
            WorkbenchBindingValue.Asset(VoiceSyncedPath)));
        if (!_suppressVoiceReferenceTextSourceUpdate)
        {
            SetVoiceReferenceTextSource(VoiceReferenceTextSourceManualEdit);
        }
    }

    partial void OnVoiceSourceAssetChanged(string value)
    {
        NotifyVoiceBindingEditChanged();
        SetVoiceBinding(VoiceAssetBinding.FromProjectReference(
            WorkbenchBindingValue.Asset(VoiceAsset),
            WorkbenchBindingValue.Text(EffectiveVoiceReferenceTextForBinding()),
            WorkbenchBindingValue.Asset(value),
            WorkbenchBindingValue.Asset(VoiceSyncedPath)));
    }

    partial void OnVoiceSyncedPathChanged(string value)
    {
        NotifyVoiceBindingEditChanged();
        SetVoiceBinding(VoiceAssetBinding.FromProjectReference(
            WorkbenchBindingValue.Asset(VoiceAsset),
            WorkbenchBindingValue.Text(EffectiveVoiceReferenceTextForBinding()),
            WorkbenchBindingValue.Asset(VoiceSourceAsset),
            WorkbenchBindingValue.Asset(value)));
    }

    partial void OnImagePreviewAssetChanged(string value)
    {
        InvalidateImagePreview();
        OnPropertyChanged(nameof(ImagePreviewLabel));
        OnPropertyChanged(nameof(HasImagePreview));
        OnPropertyChanged(nameof(HasNoImagePreview));
        OnPropertyChanged(nameof(ImagePreviewSource));
        NotifyBindingPreviewChanged();
    }

    private void InvalidateImagePreview()
    {
        _imagePreview = null;
    }

    private void NotifyBindingPreviewChanged()
    {
        OnPropertyChanged(nameof(BindingPreviewShowsImage));
        OnPropertyChanged(nameof(BindingPreviewShowsText));
        OnPropertyChanged(nameof(BindingPreviewTitle));
        OnPropertyChanged(nameof(BindingPreviewSubtitle));
    }

    internal void AcceptBindingEdits()
    {
        ResetBindingEditBaseline();
    }

    internal void ApplyVoiceBindingEditAccess(bool canEdit)
    {
        var oldCanEditReferenceText = CanEditVoiceReferenceText;
        CanEditVoiceBinding = canEdit;
        if (oldCanEditReferenceText != CanEditVoiceReferenceText)
        {
            OnPropertyChanged(nameof(CanEditVoiceReferenceText));
        }
    }

    private void ApplyState(
        string category,
        string displayName,
        string detail,
        string imageAsset,
        string imagePreviewAsset,
        string voiceAsset,
        string voiceReferenceText,
        string voiceReferenceTextSourceText,
        string voiceSourceAsset,
        string voiceSyncedPath,
        IReadOnlyList<BindingTargetRowVisualDetailViewModel> rowVisualDetails,
        VisualAssetBinding visualBinding,
        string? visualSourceDisplayName,
        string? visualRenderedDisplayName,
        VoiceAssetBinding voiceBinding,
        bool canEditVoiceBinding,
        bool resetBaseline)
    {
        _suppressBindingChangeTracking = true;
        try
        {
            Category = category;
            DisplayName = displayName;
            Detail = detail;
            ImageAsset = imageAsset;
            ImagePreviewAsset = imagePreviewAsset;
            VoiceAsset = voiceAsset;
            VoiceReferenceText = voiceReferenceText;
            VoiceReferenceTextSourceText = voiceReferenceTextSourceText;
            VoiceSourceAsset = voiceSourceAsset;
            VoiceSyncedPath = voiceSyncedPath;
            SetRowVisualDetails(rowVisualDetails);
            SetVisualBinding(visualBinding);
            ApplyVisualAssetDisplayNames(visualSourceDisplayName, visualRenderedDisplayName);
            SetVoiceBinding(voiceBinding);
            ApplyVoiceBindingEditAccess(canEditVoiceBinding);

            if (resetBaseline)
            {
                ResetBindingEditBaseline();
            }
        }
        finally
        {
            _suppressBindingChangeTracking = false;
        }
    }

    private void ResetBindingEditBaseline()
    {
        var hadImageEdit = HasImageBindingEdit;
        var hadVoiceEdit = HasVoiceBindingEdit;
        var hadAnyEdit = HasAnyBindingEdit;
        _baselineImageAsset = ImageAsset;
        _baselineVoiceAsset = VoiceAsset;
        _baselineVoiceReferenceText = VoiceReferenceText;
        _baselineEffectiveVoiceReferenceText = EffectiveVoiceReferenceTextForBinding();
        _baselineVoiceSourceAsset = VoiceSourceAsset;
        _baselineVoiceSyncedPath = VoiceSyncedPath;
        if (hadImageEdit != HasImageBindingEdit)
        {
            OnPropertyChanged(nameof(HasImageBindingEdit));
        }

        if (hadVoiceEdit != HasVoiceBindingEdit)
        {
            OnPropertyChanged(nameof(HasVoiceBindingEdit));
        }

        if (hadAnyEdit != HasAnyBindingEdit)
        {
            OnPropertyChanged(nameof(HasAnyBindingEdit));
        }
    }

    private void NotifyVoiceBindingEditChanged()
    {
        if (_suppressBindingChangeTracking)
        {
            return;
        }

        OnPropertyChanged(nameof(HasVoiceBindingEdit));
        OnPropertyChanged(nameof(HasAnyBindingEdit));
    }

    private void SetVisualBinding(VisualAssetBinding? visualBinding)
    {
        var next = visualBinding ?? VisualAssetBinding.None;
        if (_visualBinding == next)
        {
            return;
        }

        _visualBinding = next;
        _visualSourceDisplayName = null;
        _visualRenderedDisplayName = null;
        OnPropertyChanged(nameof(VisualSourceKindText));
        OnPropertyChanged(nameof(VisualSourceText));
        OnPropertyChanged(nameof(VisualRenderedText));
        OnPropertyChanged(nameof(VisualRenderedLabel));
        OnPropertyChanged(nameof(HasPsdLayerMatchSettings));
        OnPropertyChanged(nameof(CanConfigurePsdLayerMatch));
        OnPropertyChanged(nameof(PsdLayerMatchSettingsText));
        NotifyBindingPreviewChanged();
    }

    private void SetVoiceBinding(VoiceAssetBinding? voiceBinding)
    {
        var next = voiceBinding ?? VoiceAssetBinding.None;
        if (_voiceBinding == next)
        {
            return;
        }

        _voiceBinding = next;
        OnPropertyChanged(nameof(VoiceSourceText));
        OnPropertyChanged(nameof(VoiceSourceStateText));
        OnPropertyChanged(nameof(VoiceSyncedText));
        OnPropertyChanged(nameof(VoiceSyncedStateText));
        OnPropertyChanged(nameof(VoiceReferenceTextPreview));
        OnPropertyChanged(nameof(VoiceReferenceStateText));
        OnPropertyChanged(nameof(VoiceDiagnosticText));
    }

    private void SetVoiceReferenceTextSource(string? sourceText)
    {
        VoiceReferenceTextSourceText = string.IsNullOrWhiteSpace(sourceText)
            ? VoiceReferenceTextSourceSavedBinding
            : sourceText.Trim();
        OnPropertyChanged(nameof(VoiceReferenceStateText));
        OnPropertyChanged(nameof(VoiceDiagnosticText));
    }

    private void SetRowVisualDetails(IReadOnlyList<BindingTargetRowVisualDetailViewModel> details)
    {
        if (ReferenceEquals(_rowVisualDetails, details) ||
            RowVisualDetailsEqual(_rowVisualDetails, details))
        {
            return;
        }

        _rowVisualDetails = details.Count == 0 ? [] : CopyRowVisualDetails(details);
        OnPropertyChanged(nameof(RowVisualDetails));
        OnPropertyChanged(nameof(HasRowVisualDetails));
    }

    private static IReadOnlyList<BindingTargetRowVisualDetailViewModel> CopyRowVisualDetails(
        IReadOnlyList<BindingTargetRowVisualDetailViewModel> details)
    {
        var copy = new BindingTargetRowVisualDetailViewModel[details.Count];
        for (var index = 0; index < details.Count; index++)
        {
            copy[index] = details[index];
        }

        return copy;
    }

    private static bool IsExistingImagePath(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            Path.IsPathRooted(value) &&
            File.Exists(value);
    }

    private static string FormatVisualSourceKind(VisualSourceKind sourceKind)
    {
        return sourceKind switch
        {
            VisualSourceKind.Image => "PNG/JPG",
            VisualSourceKind.Psd => "PSD",
            _ => "未绑定"
        };
    }

    private static string FormatAssetSummary(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmed = value.Trim();
        if (Path.IsPathRooted(trimmed))
        {
            return Path.GetFileName(trimmed);
        }

        return trimmed;
    }

    private string? VisualRenderedAssetReference()
    {
        return _visualBinding.SourceKind == VisualSourceKind.Image
            ? _visualBinding.RenderedAssetId ?? WorkbenchBindingValue.Asset(ImageAsset)
            : _visualBinding.RenderedAssetId;
    }

    private static string FirstDisplayText(string? displayName, string fallback)
    {
        var normalized = NormalizeDisplayName(displayName);
        return normalized ?? fallback;
    }

    private static string? NormalizeDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static string FormatReferenceTextPreview(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "未绑定参考文本";
        }

        return trimmed.Length <= 48 ? trimmed : $"{trimmed[..48]}...";
    }

    private static string FormatVoiceSourceState(VoiceAssetBinding voiceBinding)
    {
        return string.IsNullOrWhiteSpace(voiceBinding.SourceAssetId)
            ? "Missing project source"
            : "Project source registered";
    }

    private static string FormatVoiceSyncedState(VoiceAssetBinding voiceBinding, string voiceAsset)
    {
        if (!string.IsNullOrWhiteSpace(voiceBinding.SyncedVoicePath))
        {
            return "Legacy voices path tracked";
        }

        return string.IsNullOrWhiteSpace(voiceBinding.EffectiveVoiceReferenceId ?? WorkbenchBindingValue.Asset(voiceAsset))
            ? "Missing voice reference"
            : "No ComfyUI voices copy";
    }

    private static string FormatVoiceReferenceState(VoiceAssetBinding voiceBinding, string sourceText)
    {
        if (string.IsNullOrWhiteSpace(voiceBinding.EffectiveReferenceText))
        {
            return "Missing reference text";
        }

        return string.IsNullOrWhiteSpace(sourceText)
            ? "Reference text bound"
            : $"Reference text: {sourceText.Trim()}";
    }

    private static string FormatVoiceDiagnostic(
        VoiceAssetBinding voiceBinding,
        string voiceAsset,
        string sourceText)
    {
        if (string.IsNullOrWhiteSpace(voiceBinding.EffectiveVoiceReferenceId ?? WorkbenchBindingValue.Asset(voiceAsset)))
        {
            return "Voice reference is not bound";
        }

        if (string.IsNullOrWhiteSpace(voiceBinding.SourceAssetId))
        {
            return "Project source is not required for ComfyUI voices references";
        }

        if (string.IsNullOrWhiteSpace(voiceBinding.SyncedVoicePath))
        {
            return "ComfyUI voices copy is managed by the user, not by Draft Agent";
        }

        if (string.IsNullOrWhiteSpace(voiceBinding.EffectiveReferenceText))
        {
            return "Reference text is missing; reload sidecar or enter text before TTS";
        }

        return string.IsNullOrWhiteSpace(sourceText)
            ? "Voice binding is complete"
            : "Voice binding is complete with reference text source";
    }

    private static string FormatLayerOrPromptSummary(VisualAssetBinding visualBinding)
    {
        return visualBinding.SourceKind switch
        {
            VisualSourceKind.Psd => FormatPsdLayerSummary(visualBinding.PsdSelection, visualBinding.PsdLayerMatch),
            _ => string.Empty
        };
    }

    private static string FormatPsdLayerSummary(
        PsdRenderSelection? selection,
        PsdLayerMatchConfiguration? layerMatch)
    {
        if (layerMatch?.HasAnyGroup == true)
        {
            return FormatPsdLayerMatchSettings(layerMatch);
        }

        if (selection is null || selection.LayerNames.Count == 0)
        {
            return string.Empty;
        }

        var joinedLayers = new List<string>(selection.LayerNames.Count);
        for (var index = 0; index < selection.LayerNames.Count; index++)
        {
            var name = selection.LayerNames[index].Trim();
            if (name.Length > 0)
            {
                joinedLayers.Add(name);
            }
        }

        var joined = string.Join(", ", joinedLayers);
        return joined.Length <= 48 ? joined : $"{joined[..48]}...";
    }

    private static string FormatPsdLayerMatchSettings(PsdLayerMatchConfiguration settings)
    {
        var parts = new List<string>(8);
        if (settings.ExpressionGroup is { } expressionGroup &&
            (!string.IsNullOrWhiteSpace(expressionGroup.GroupName) || expressionGroup.LayerNames.Count > 0))
        {
            parts.Add($"表情:{FormatPsdLayerGroupCatalog(expressionGroup)}");
        }

        if (settings.BrowGroup is { } browGroup &&
            (!string.IsNullOrWhiteSpace(browGroup.GroupName) || browGroup.LayerNames.Count > 0))
        {
            parts.Add($"眉:{FormatPsdLayerGroupCatalog(browGroup)}");
        }

        if (settings.EyeGroup is { } eyeGroup &&
            (!string.IsNullOrWhiteSpace(eyeGroup.GroupName) || eyeGroup.LayerNames.Count > 0))
        {
            parts.Add($"眼:{FormatPsdLayerGroupCatalog(eyeGroup)}");
        }

        if (settings.MouthGroup is { } mouthGroup &&
            (!string.IsNullOrWhiteSpace(mouthGroup.GroupName) || mouthGroup.LayerNames.Count > 0))
        {
            parts.Add($"嘴:{FormatPsdLayerGroupCatalog(mouthGroup)}");
        }

        if (settings.ActionGroup is { } actionGroup &&
            (!string.IsNullOrWhiteSpace(actionGroup.GroupName) || actionGroup.LayerNames.Count > 0))
        {
            parts.Add($"动作:{FormatPsdLayerGroupCatalog(actionGroup)}");
        }

        if (settings.LeftHandGroup is { } leftHandGroup &&
            (!string.IsNullOrWhiteSpace(leftHandGroup.GroupName) || leftHandGroup.LayerNames.Count > 0))
        {
            parts.Add($"左手:{FormatPsdLayerGroupCatalog(leftHandGroup)}");
        }

        if (settings.RightHandGroup is { } rightHandGroup &&
            (!string.IsNullOrWhiteSpace(rightHandGroup.GroupName) || rightHandGroup.LayerNames.Count > 0))
        {
            parts.Add($"右手:{FormatPsdLayerGroupCatalog(rightHandGroup)}");
        }

        var text = string.Join(" / ", parts);
        return text.Length <= 60 ? text : $"{text[..60]}...";
    }

    private static string FormatPsdLayerGroupCatalog(PsdLayerGroupCatalog group)
    {
        var groupName = string.IsNullOrWhiteSpace(group.GroupName) ? "未命名组" : group.GroupName.Trim();
        return group.LayerNames.Count == 0
            ? groupName
            : $"{groupName}({group.LayerNames.Count})";
    }

    private static bool RowVisualDetailsEqual(
        IReadOnlyList<BindingTargetRowVisualDetailViewModel> left,
        IReadOnlyList<BindingTargetRowVisualDetailViewModel> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatPsdFingerprintSummary(PsdRenderSelection? selection)
    {
        var fingerprint = selection?.SourceFingerprint.Trim();
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return "PSD fingerprint: 未记录";
        }

        return fingerprint.Length <= 24
            ? $"PSD fingerprint: {fingerprint}"
            : $"PSD fingerprint: {fingerprint[..24]}...";
    }

    private static string FormatVisualDiagnosticText(VisualAssetBinding visualBinding)
    {
        if (visualBinding.SourceKind == VisualSourceKind.None)
        {
            return "未绑定视觉源";
        }

        var hasSource = !string.IsNullOrWhiteSpace(visualBinding.SourceAssetId);
        var hasRendered = !string.IsNullOrWhiteSpace(visualBinding.RenderedAssetId);
        return visualBinding.SourceKind switch
        {
            VisualSourceKind.Psd when !hasSource => "PSD 源缺失",
            VisualSourceKind.Psd when !hasRendered => "PSD 尚未渲染 PNG",
            VisualSourceKind.Psd => "PSD rendered 就绪",
            VisualSourceKind.Image when !hasRendered && !hasSource => "图片缺失",
            VisualSourceKind.Image => "图片就绪",
            _ => "视觉状态未知"
        };
    }

    private static string FormatSourceStateText(VisualAssetBinding visualBinding)
    {
        if (visualBinding.SourceKind == VisualSourceKind.None)
        {
            return "源缺失";
        }

        return string.IsNullOrWhiteSpace(visualBinding.SourceAssetId)
            ? "源缺失"
            : "源已绑定";
    }

    private static string FormatRenderedStateText(VisualAssetBinding visualBinding)
    {
        return visualBinding.SourceKind switch
        {
            VisualSourceKind.Psd => string.IsNullOrWhiteSpace(visualBinding.RenderedAssetId)
                ? "缺 PNG 输出"
                : "PNG 输出就绪",
            VisualSourceKind.Image => string.IsNullOrWhiteSpace(visualBinding.RenderedAssetId)
                ? "图片缺失"
                : "图片就绪",
            _ => "无输出"
        };
    }

    private static IReadOnlyList<BindingTargetRowVisualDetailViewModel> BuildRowVisualDetails(
        IReadOnlyList<AnalysisResultRowViewModel> rows,
        Func<string?, string?>? resolveProjectAssetPath,
        IReadOnlyList<GenerationTask>? generationTasks)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var details = new List<BindingTargetRowVisualDetailViewModel>(rows.Count);
        foreach (var row in rows)
        {
            var visualBinding = row.CharacterVisualBinding ?? VisualAssetBinding.FromImage(WorkbenchBindingValue.Asset(row.SelectedCharacterAsset));
            var linePreview = FormatReferenceTextPreview(row.LineText);
            var sourceAssetId = visualBinding.SourceAssetId ??
                (visualBinding.SourceKind == VisualSourceKind.None ? WorkbenchBindingValue.Asset(row.SelectedCharacterAsset) : null);
            var renderedAssetId = visualBinding.RenderedAssetId ??
                (visualBinding.SourceKind is VisualSourceKind.Image or VisualSourceKind.None
                    ? WorkbenchBindingValue.Asset(row.SelectedCharacterAsset)
                    : null);
            details.Add(new BindingTargetRowVisualDetailViewModel(
                row.InteractionId,
                row.IsSelected,
                row.OwnerScene?.SceneName ?? row.OwnerScene?.SceneId ?? string.Empty,
                row.OrderLabel,
                FormatVisualSourceKind(visualBinding.SourceKind),
                FormatAssetSummary(sourceAssetId, "未绑定源"),
                FormatAssetSummary(renderedAssetId, "未生成预览"),
                FormatSourceStateText(visualBinding),
                FormatRenderedStateText(visualBinding),
                FormatVisualDiagnosticText(visualBinding),
                FormatPsdFingerprintSummary(visualBinding.PsdSelection),
                PsdVisualBindingDiagnostics.FormatMismatchText(visualBinding, resolveProjectAssetPath),
                FormatLayerOrPromptSummary(visualBinding),
                linePreview));
        }

        return details;
    }

    private static ImagePreviewSnapshot CaptureImagePreview(string? source, string fallback)
    {
        var normalizedSource = source ?? string.Empty;
        var hasImage = IsExistingImagePath(normalizedSource);
        return new ImagePreviewSnapshot(
            hasImage ? normalizedSource : string.Empty,
            hasImage,
            hasImage ? Path.GetFileName(normalizedSource) : fallback);
    }

    private static string DisplayImageBinding(string? value)
    {
        return IsUnbound(value) || value is "旁白无需人物" or "未绑定行图" or "行图预览"
            ? "未绑定图片"
            : value!;
    }

    private static string DisplayVoiceBinding(string? value)
    {
        return IsUnbound(value) ||
               value == "角色声音"
            ? "未绑定声音"
            : value!;
    }

    private static string? FirstBoundValue(params string?[] values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            var value = values[index];
            if (!IsUnbound(value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool IsUnbound(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ||
               value == "未绑定" ||
               value.StartsWith("未绑定", StringComparison.Ordinal) ||
               value == "默认配音" ||
               value == "旁白声音" ||
               value == "角色声音";
    }

    private static IReadOnlyList<AnalysisResultRowViewModel> CharacterRows(
        Character character,
        IEnumerable<SceneTableViewModel> scenes)
    {
        var rows = new List<AnalysisResultRowViewModel>();
        foreach (var scene in scenes)
        {
            foreach (var row in scene.Rows)
            {
                if (string.Equals(row.SubjectCharacterId, character.Id, StringComparison.Ordinal) ||
                    string.Equals(row.SubjectName, character.Name, StringComparison.Ordinal))
                {
                    rows.Add(row);
                }
            }
        }

        return rows.Count == 0 ? [] : rows.ToArray();
    }

    private static IReadOnlyList<AnalysisResultRowViewModel> NarrationRows(IEnumerable<SceneTableViewModel> scenes)
    {
        var rows = new List<AnalysisResultRowViewModel>();
        foreach (var scene in scenes)
        {
            foreach (var row in scene.Rows)
            {
                if (row.IsNarration &&
                    string.IsNullOrWhiteSpace(row.AppliedDraft?.VoiceRole) &&
                    string.IsNullOrWhiteSpace(row.AppliedDraft?.VoiceRoleCharacterId))
                {
                    rows.Add(row);
                }
            }
        }

        return rows.Count == 0 ? [] : rows.ToArray();
    }

    private static IReadOnlyList<AnalysisResultRowViewModel> NarratorVoiceRows(IEnumerable<SceneTableViewModel> scenes)
    {
        var rows = new List<AnalysisResultRowViewModel>();
        foreach (var scene in scenes)
        {
            foreach (var row in scene.Rows)
            {
                if (string.IsNullOrWhiteSpace(row.AppliedDraft?.VoiceRole) &&
                    string.IsNullOrWhiteSpace(row.AppliedDraft?.VoiceRoleCharacterId))
                {
                    rows.Add(row);
                }
            }
        }

        return rows.Count == 0 ? [] : rows.ToArray();
    }

    private sealed record ImagePreviewSnapshot(string Source, bool HasImage, string Label);
}

public sealed record BindingTargetRowVisualDetailViewModel(
    string InteractionId,
    bool IsCurrentRow,
    string SceneName,
    string RowLabel,
    string SourceKindText,
    string SourceText,
    string RenderedText,
    string SourceStateText,
    string RenderedStateText,
    string DiagnosticText,
    string PsdFingerprintText,
    string PsdSourceStateText,
    string SelectionText,
    string LinePreview);
