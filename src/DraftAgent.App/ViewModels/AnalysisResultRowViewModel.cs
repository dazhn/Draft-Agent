using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using DraftAgent.Core;

namespace DraftAgent.App.ViewModels;

public sealed partial class AnalysisResultRowViewModel : ObservableObject
{
    private const string NarrationSubjectName = "旁白";

    [ObservableProperty]
    private int _order;

    private string? _subjectName;

    [ObservableProperty]
    private string? _emotion;

    [ObservableProperty]
    private string _lineText;

    [ObservableProperty]
    private string _selectedCharacterAsset;

    [ObservableProperty]
    private string _selectedCharacterPreviewAsset = string.Empty;

    [ObservableProperty]
    private string _selectedVoiceReference = "未绑定";

    [ObservableProperty]
    private string _selectedVoiceReferenceText = string.Empty;

    [ObservableProperty]
    private string _previewAssetText;

    [ObservableProperty]
    private string _previewAssetPath = string.Empty;

    [ObservableProperty]
    private string _voiceAssetText;

    private string _ttsStatusText = string.Empty;

    private string _ttsDetailText;

    private string _synthesizedAudioAsset;

    private string _voiceDurationText = string.Empty;

    private bool _canRegenerateVoice;
    private string _sourceItemType = string.Empty;
    private string _sourceRole = string.Empty;
    private string? _sourceRoleCharacterId;

    [ObservableProperty]
    private double _emotionAlpha = 1;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isDragSourceActive;

    [ObservableProperty]
    private bool _isTtsSynthesisRunning;

    [ObservableProperty]
    private bool _isWorkbenchMutationLocked;

    private GenerationTaskStatus? _ttsStatus;

    private ProjectDerivedSourceSnapshot? _projectDerivedSourceSnapshot;

    private AnalysisResultRowViewModel(
        string interactionId,
        int order,
        string rowType,
        string subjectName,
        string? subjectCharacterId,
        string emotion,
        string lineText,
        string actionText,
        bool isNarration,
        string selectedCharacterAsset,
        string selectedVoiceReference,
        string selectedVoiceReferenceText,
        string previewAssetText,
        string voiceAssetText,
        string? ttsTaskId,
        GenerationTaskStatus? ttsStatus,
        string ttsStatusText,
        string ttsDetailText,
        string synthesizedAudioAsset,
        IReadOnlyDictionary<string, double>? emotionVectors = null,
        double emotionAlpha = 1)
    {
        InteractionId = interactionId;
        Order = order;
        RowType = rowType;
        SubjectName = subjectName;
        SubjectCharacterId = subjectCharacterId;
        Emotion = emotion;
        LineText = lineText;
        ActionText = actionText;
        IsNarration = isNarration;
        SelectedCharacterAsset = selectedCharacterAsset;
        SelectedVoiceReference = selectedVoiceReference;
        SelectedVoiceReferenceText = selectedVoiceReferenceText;
        PreviewAssetText = previewAssetText;
        VoiceAssetText = voiceAssetText;
        TtsTaskId = ttsTaskId;
        _ttsStatus = ttsStatus;
        TtsStatusText = ttsStatusText;
        _ttsDetailText = ttsDetailText;
        _synthesizedAudioAsset = synthesizedAudioAsset;
        _canRegenerateVoice = HasRegeneratableLineText(lineText);
        EmotionAlpha = emotionAlpha;
        EmotionVectors = BuildEmotionVectorViewModels(emotionVectors, emotion);
        EmotionVectors.CollectionChanged += OnEmotionVectorsChanged;
        foreach (var vector in EmotionVectors)
        {
            vector.PropertyChanged += OnEmotionVectorPropertyChanged;
        }
    }

    public const string EnvironmentEffectsSubjectName = "环境场效";

    public const string NarrationStorySubjectName = "旁白叙述";

    public string InteractionId { get; }

    public string? SubjectName
    {
        get => _subjectName;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                InvalidateAppliedDraft();
                OnPropertyChanged();
                return;
            }

            var next = value;
            if (SetProperty(ref _subjectName, next))
            {
                InvalidateAppliedDraft();
                OnSubjectNameChanged(next);
            }
        }
    }

    public string OrderLabel => Order.ToString("00");

    public string RowType { get; }

    public string? SubjectCharacterId { get; private set; }

    public string ActionText { get; }

    public bool IsNarration { get; private set; }

    public string SourceItemType
    {
        get => _sourceItemType;
        private set
        {
            var next = value?.Trim() ?? string.Empty;
            if (SetProperty(ref _sourceItemType, next))
            {
                OnPropertyChanged(nameof(SourceTypeGlyph));
                OnPropertyChanged(nameof(SourceTypeText));
            }
        }
    }

    public string SourceRole
    {
        get => _sourceRole;
        private set => SetProperty(ref _sourceRole, value?.Trim() ?? string.Empty);
    }

    public string? SourceRoleCharacterId
    {
        get => _sourceRoleCharacterId;
        private set => SetProperty(ref _sourceRoleCharacterId, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    public string SourceTypeGlyph => SourceTypeGlyphFor(SourceItemType, IsNarration);

    public string SourceTypeText => SourceTypeTextFor(SourceItemType, IsNarration);

    internal void ApplySourceTypeProjection(
        string sourceItemType,
        string subjectName,
        string? subjectCharacterId,
        bool isNarration)
    {
        IsUpdatingSubjectDerivedState = true;
        try
        {
            SourceItemType = sourceItemType;
            SourceRole = isNarration ? string.Empty : subjectName;
            SourceRoleCharacterId = isNarration ? null : subjectCharacterId;
            IsNarration = isNarration;
            SubjectCharacterId = subjectCharacterId;
            SubjectName = subjectName;
            InvalidateObjectPreviewInitial();
            InvalidateScenePreview();
            _selectedCharacterPreview = null;
            OnPropertyChanged(nameof(IsNarration));
            OnPropertyChanged(nameof(UsesCharacterVisualPreview));
            OnPropertyChanged(nameof(ObjectPreviewInitial));
            RefreshObjectPreview();
        }
        finally
        {
            IsUpdatingSubjectDerivedState = false;
        }
    }

    public bool IsUpdatingSubjectDerivedState { get; private set; }

    public string? TtsTaskId { get; private set; }

    public GenerationTaskStatus? TtsStatus => _ttsStatus;

    public string TtsStatusText
    {
        get => _ttsStatusText;
        set => SetProperty(ref _ttsStatusText, value);
    }

    public bool HasTtsTask => !string.IsNullOrWhiteSpace(TtsTaskId);

    public string TtsDetailText => _ttsDetailText;

    public string SynthesizedAudioAsset => _synthesizedAudioAsset;

    public bool HasSynthesizedVoice => TtsStatus == GenerationTaskStatus.Completed && !string.IsNullOrWhiteSpace(SynthesizedAudioAsset);

    public bool CanPreviewVoice => HasSynthesizedVoice;

    public bool CanRegenerateVoice => _canRegenerateVoice;

    public string VoiceDurationText => _voiceDurationText;

    public bool IsEmotionEditable => true;

    public bool CanEditTextAndEmotion =>
        !IsWorkbenchMutationLocked ||
        IsTtsSynthesisRunning && TtsStatus == GenerationTaskStatus.Completed;

    public bool IsLineEditorActive => IsSelected && CanEditTextAndEmotion;

    public AnalysisResultRowViewModel? SelectedEditingContext => IsSelected ? this : null;

    public ObservableCollection<EmotionVectorViewModel> EmotionVectors { get; }

    internal WorkbenchRowDraft? AppliedDraft { get; private set; }

    internal SceneTableViewModel? OwnerScene { get; private set; }

    internal VisualAssetBinding? CharacterVisualBinding { get; private set; }

    internal VoiceAssetBinding? VoiceBinding { get; private set; }

    internal bool ApplySubjectProjection(string subjectName, string? subjectCharacterId)
    {
        var changed = false;
        if (!string.Equals(SubjectName, subjectName, StringComparison.Ordinal))
        {
            SubjectName = subjectName;
            changed = true;
        }

        if (!string.Equals(SubjectCharacterId, subjectCharacterId, StringComparison.Ordinal))
        {
            SubjectCharacterId = subjectCharacterId;
            InvalidateAppliedDraft();
            changed = true;
        }

        return changed;
    }

    private IReadOnlyDictionary<string, double>? _emotionVectorMap;
    private EmotionVectorViewModel? _strongestEmotionVector;
    private bool _hasStrongestEmotionVector;
    private string? _effectiveEmotionText;
    private string? _effectiveEmotionDisplayText;
    private string? _objectPreviewInitial;
    private ImagePreviewSnapshot? _selectedCharacterPreview;
    private ImagePreviewSnapshot? _scenePreview;
    private ImagePreviewSnapshot? _emptyObjectPreview;

    public IReadOnlyDictionary<string, double> EmotionVectorMap =>
        _emotionVectorMap ??= EmotionVectors.ToDictionary(vector => vector.Key, vector => vector.Value, StringComparer.Ordinal);

    public bool HasSelectedCharacterImage => SelectedCharacterPreview.HasImage;

    public bool HasNoSelectedCharacterImage => !HasSelectedCharacterImage;

    public string SelectedCharacterAssetLabel => SelectedCharacterPreview.Label;

    public bool HasPreviewImage => ScenePreview.HasImage;

    public bool HasNoPreviewImage => !HasPreviewImage;

    public string PreviewAssetLabel => ScenePreview.Label;

    public string ObjectPreviewAsset => ObjectPreview.Asset;

    public string ObjectPreviewSource => ObjectPreview.Source;

    public bool HasObjectPreviewImage => ObjectPreview.HasImage;

    public bool HasNoObjectPreviewImage => !HasObjectPreviewImage;

    public string ObjectPreviewLabel => ObjectPreview.Label;

    public string InlineObjectPreviewSource => ObjectPreviewSource;

    public bool HasInlineObjectPreviewImage => HasObjectPreviewImage;

    public bool HasNoInlineObjectPreviewImage => !HasInlineObjectPreviewImage;

    public string InlineObjectPreviewLabel => ObjectPreviewLabel;

    public AnalysisResultRowViewModel? InlineObjectPreviewContext => HasInlineObjectPreviewImage ? this : null;

    public double InlineObjectPreviewWidth => InlineObjectPreviewSide;

    public double InlineObjectPreviewHeight => InlineObjectPreviewSide;

    private const double InlineObjectPreviewSide = 24;

    private ImagePreviewSnapshot SelectedCharacterPreview =>
        _selectedCharacterPreview ??= CaptureImagePreview(SelectedCharacterPreviewAsset, SelectedCharacterAsset);

    private ImagePreviewSnapshot ScenePreview =>
        _scenePreview ??= CaptureImagePreview(PreviewAssetPath, PreviewAssetText);

    private ImagePreviewSnapshot ObjectPreview =>
        UsesCharacterVisualPreview
            ? SelectedCharacterPreview
            : SelectedCharacterPreview;

    private ImagePreviewSnapshot EmptyObjectPreview =>
        _emptyObjectPreview ??= new ImagePreviewSnapshot(string.Empty, string.Empty, false, string.Empty, InlineObjectPreviewSide, InlineObjectPreviewSide);

    public bool UsesCharacterVisualPreview => IsCharacterVisualSource(SourceItemType, SourceRole, SourceRoleCharacterId, SubjectName, SubjectCharacterId, IsNarration);

    public string ObjectPreviewInitial => _objectPreviewInitial ??= BuildObjectPreviewInitial();

    public string EffectiveEmotionText => _effectiveEmotionText ??= BuildEffectiveEmotionText();

    public string EffectiveEmotionDisplayText => _effectiveEmotionDisplayText ??= BuildEffectiveEmotionDisplayText();

    public static AnalysisResultRowViewModel FromInteraction(CharacterInteraction interaction, InteractionProject project, string? sceneAssetId)
    {
        return FromInteraction(interaction, InteractionRowBuildContext.FromProject(project), sceneAssetId);
    }

    internal static AnalysisResultRowViewModel FromInteraction(
        CharacterInteraction interaction,
        InteractionRowBuildContext context,
        string? sceneAssetId)
    {
        var isNarration = interaction.Narration is not null;
        var subjectName = DisplaySubjectName(interaction, context, isNarration);
        var lineText = interaction.Dialogue?.Text
            ?? interaction.InnerMonologue?.Text
            ?? interaction.Narration?.Text
            ?? "无台词";
        var actionText = isNarration
            ? $"旁白描述：{interaction.Action.MainAction}"
            : interaction.Action.MainAction;
        var ttsTask = context.TtsTask(interaction);
        var emotionVectors = ReadEmotionVectors(ttsTask, interaction.Emotion.Emotion, interaction.Emotion.Intensity);
        var emotion = BuildEffectiveEmotion(isNarration, emotionVectors, interaction.Emotion);
        var usesCharacterVisualPreview = UsesCharacterVisualPreviewForInteraction(interaction);
        var rowVisualAsset = interaction.Binding.SubjectCharacterVisualBinding?.EffectiveAssetId;
        var selectedCharacterAsset = usesCharacterVisualPreview
            ? DisplayBinding(rowVisualAsset)
            : DisplayBinding(rowVisualAsset, "未绑定行图");
        var selectedVoiceReference = isNarration
            ? DisplayBinding(interaction.Binding.NarratorVoiceReferenceId ?? interaction.Narration?.Voice.VoiceReferenceId)
            : DisplayBinding(interaction.Binding.SubjectVoiceReferenceId ?? interaction.Dialogue?.Voice.VoiceReferenceId ?? interaction.InnerMonologue?.Voice.VoiceReferenceId);
        var selectedVoiceReferenceText = isNarration
            ? interaction.Narration?.Voice.ReferenceText
            : interaction.Dialogue?.Voice.ReferenceText ?? interaction.InnerMonologue?.Voice.ReferenceText;
        if (string.IsNullOrWhiteSpace(selectedVoiceReferenceText) &&
            ttsTask?.Parameters.TryGetValue("referenceText", out var taskReferenceText) == true)
        {
            selectedVoiceReferenceText = taskReferenceText;
        }
        var previewAsset = usesCharacterVisualPreview
            ? DisplayBinding(rowVisualAsset ?? sceneAssetId, "人物预览")
            : DisplayBinding(rowVisualAsset, "行图预览");
        var synthesizedAudioAsset = TtsGeneratedAudioReference.Resolve(ttsTask) ?? string.Empty;
        var voiceAsset = DisplayBinding(selectedVoiceReference, isNarration ? "默认配音" : "角色声音");
        var ttsStatusText = FormatTtsStatus(ttsTask);
        var ttsDetailText = FormatTtsDetail(ttsTask, synthesizedAudioAsset);

        var row = new AnalysisResultRowViewModel(
            interaction.Id,
            interaction.Order,
            isNarration ? "旁白" : "人物",
            subjectName,
            interaction.SubjectCharacterId,
            emotion,
            lineText,
            actionText,
            isNarration,
            selectedCharacterAsset,
            selectedVoiceReference,
            selectedVoiceReferenceText ?? string.Empty,
            previewAsset,
            voiceAsset,
            ttsTask?.Id,
            ttsTask?.Status,
            ttsStatusText,
            ttsDetailText,
            synthesizedAudioAsset,
            emotionVectors,
            ReadEmotionAlpha(ttsTask));
        row.CharacterVisualBinding = interaction.Binding.SubjectCharacterVisualBinding ?? VisualAssetBinding.None;
        row.VoiceBinding = VoiceAssetBinding.FromProjectReference(selectedVoiceReference, selectedVoiceReferenceText, null, null);
        row.ApplySourceMetadata(
            interaction.SourceItemType,
            interaction.SourceRole,
            interaction.SourceRoleCharacterId);
        row._projectDerivedSourceSnapshot = CaptureProjectDerivedSourceSnapshot(interaction, context);
        return row;
    }

    internal sealed class InteractionRowBuildContext
    {
        private readonly IReadOnlyDictionary<string, string> _characterNamesById;
        private readonly IReadOnlyDictionary<string, GenerationTask> _rootTtsTasksByTargetId;

        private InteractionRowBuildContext(
            IReadOnlyDictionary<string, string> characterNamesById,
            IReadOnlyDictionary<string, GenerationTask> rootTtsTasksByTargetId)
        {
            _characterNamesById = characterNamesById;
            _rootTtsTasksByTargetId = rootTtsTasksByTargetId;
        }

        public static InteractionRowBuildContext FromProject(InteractionProject project)
        {
            var characterNamesById = new Dictionary<string, string>(project.Characters.Count, StringComparer.Ordinal);
            for (var index = 0; index < project.Characters.Count; index++)
            {
                var character = project.Characters[index];
                characterNamesById.TryAdd(character.Id, character.Name);
            }

            var rootTtsTasksByTargetId = new Dictionary<string, GenerationTask>(StringComparer.Ordinal);
            for (var taskIndex = 0; taskIndex < project.GenerationTasks.Count; taskIndex++)
            {
                var task = project.GenerationTasks[taskIndex];
                if (task.Type != GenerationTaskType.Tts || string.IsNullOrWhiteSpace(task.TargetId))
                {
                    continue;
                }

                rootTtsTasksByTargetId[task.TargetId] = task;
            }

            return new InteractionRowBuildContext(characterNamesById, rootTtsTasksByTargetId);
        }

        public string CharacterName(string? characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
            {
                return "未识别人物";
            }

            return _characterNamesById.TryGetValue(characterId, out var characterName) ? characterName : characterId;
        }

        public GenerationTask? TtsTask(CharacterInteraction interaction)
        {
            ResolveInteractionTasks(interaction.Tasks, out var ttsTask);
            if (ttsTask is not null)
            {
                return ttsTask;
            }

            return _rootTtsTasksByTargetId.TryGetValue(interaction.Id, out var rootTtsTask)
                ? rootTtsTask
                : null;
        }
    }

    public static AnalysisResultRowViewModel CreateManualDialogue(int order, string characterName)
    {
        return new AnalysisResultRowViewModel(
            $"int_manual_{Guid.NewGuid():N}",
            order,
            "人物",
            string.IsNullOrWhiteSpace(characterName) ? "未命名人物" : characterName,
            null,
            "克制 60%",
            "待填写台词",
            "台词表演",
            false,
            "未绑定",
            "未绑定",
            string.Empty,
            "人物预览",
            "角色声音",
            null,
            null,
            "待合成",
            "保存后生成语音任务",
            string.Empty,
            new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["Calm"] = 0.6
            },
            1);
    }

    public static AnalysisResultRowViewModel CreateManualNarration(int order)
    {
        var row = new AnalysisResultRowViewModel(
            $"int_manual_{Guid.NewGuid():N}",
            order,
            "旁白",
            NarrationSubjectName,
            null,
            string.Empty,
            "待填写旁白",
            "旁白描述",
            true,
            "未绑定行图",
            "未绑定",
            string.Empty,
            "行图预览",
            "默认配音",
            null,
            null,
            "待合成",
            "保存后生成语音任务",
            string.Empty);
        row.ApplySourceMetadata("narration", string.Empty, null);
        return row;
    }

    internal static AnalysisResultRowViewModel FromDraft(WorkbenchRowDraft row, string? sceneAssetId)
    {
        var isNarration = row.Kind == WorkbenchRowKind.Narration;
        var usesCharacterVisualPreview = UsesCharacterVisualPreviewForDraft(row);
        var viewModel = new AnalysisResultRowViewModel(
            row.InteractionId,
            row.Order,
            isNarration ? "旁白" : "人物",
            DisplaySubjectName(row),
            row.SubjectCharacterId,
            row.Emotion,
            row.Text,
            isNarration ? "旁白描述" : "台词表演",
            isNarration,
            usesCharacterVisualPreview ? DisplayBinding(row.CharacterVisualBinding?.EffectiveAssetId) : DisplayBinding(row.CharacterVisualBinding?.EffectiveAssetId, "未绑定行图"),
            DisplayBinding(row.VoiceBinding?.EffectiveVoiceReferenceId),
            row.VoiceBinding?.EffectiveReferenceText ?? string.Empty,
            usesCharacterVisualPreview
                ? DisplayBinding(row.CharacterVisualBinding?.EffectiveAssetId ?? sceneAssetId, "人物预览")
                : DisplayBinding(row.CharacterVisualBinding?.EffectiveAssetId, "行图预览"),
            DisplayBinding(row.VoiceBinding?.EffectiveVoiceReferenceId, isNarration ? "默认配音" : "角色声音"),
            row.TaskLinks.TtsTaskId,
            null,
            "待合成",
            "保存后生成语音任务",
            string.Empty,
            row.EmotionVectors,
            row.EmotionAlpha ?? 1);
        viewModel.CharacterVisualBinding = row.CharacterVisualBinding ?? VisualAssetBinding.None;
        viewModel.VoiceBinding = row.VoiceBinding ?? VoiceAssetBinding.None;
        viewModel.ApplySourceMetadata(row.SourceItemType, row.SourceRole, row.SourceRoleCharacterId);
        return viewModel;
    }

    internal bool ApplyTaskLinks(WorkbenchTaskLinks taskLinks)
    {
        var ttsChanged = !string.Equals(TtsTaskId, taskLinks.TtsTaskId, StringComparison.Ordinal);
        TtsTaskId = taskLinks.TtsTaskId;
        if (ttsChanged)
        {
            OnPropertyChanged(nameof(HasTtsTask));
        }

        return ttsChanged;
    }

    internal void ApplyProjectDerivedState(CharacterInteraction interaction, InteractionRowBuildContext context)
    {
        var sourceSnapshot = CaptureProjectDerivedSourceSnapshot(interaction, context);
        if (_projectDerivedSourceSnapshot == sourceSnapshot)
        {
            return;
        }

        _projectDerivedSourceSnapshot = sourceSnapshot;
        var derived = CaptureProjectDerivedState(sourceSnapshot);
        var taskLinksChanged = ApplyTaskLinks(new WorkbenchTaskLinks(derived.TtsTaskId));
        var synthesizedVoiceAvailabilityChanged =
            HasSynthesizedVoice != (derived.TtsStatus == GenerationTaskStatus.Completed &&
                !string.IsNullOrWhiteSpace(derived.SynthesizedAudioAsset));

        var canEditTextAndEmotion = CanEditTextAndEmotion;
        if (!EqualityComparer<GenerationTaskStatus?>.Default.Equals(_ttsStatus, derived.TtsStatus))
        {
            _ttsStatus = derived.TtsStatus;
            OnPropertyChanged(nameof(TtsStatus));
        }

        if (!string.Equals(TtsStatusText, derived.TtsStatusText, StringComparison.Ordinal))
        {
            _ttsStatusText = derived.TtsStatusText;
            OnPropertyChanged(nameof(TtsStatusText));
        }

        if (!string.Equals(TtsDetailText, derived.TtsDetailText, StringComparison.Ordinal))
        {
            _ttsDetailText = derived.TtsDetailText;
        }

        if (!string.Equals(SynthesizedAudioAsset, derived.SynthesizedAudioAsset, StringComparison.Ordinal))
        {
            _synthesizedAudioAsset = derived.SynthesizedAudioAsset;
        }

        if (!string.Equals(VoiceDurationText, derived.VoiceDurationText, StringComparison.Ordinal))
        {
            _voiceDurationText = derived.VoiceDurationText;
            OnPropertyChanged(nameof(VoiceDurationText));
        }

        if (synthesizedVoiceAvailabilityChanged)
        {
            OnPropertyChanged(nameof(HasSynthesizedVoice));
            OnPropertyChanged(nameof(CanPreviewVoice));
        }

        if (canEditTextAndEmotion != CanEditTextAndEmotion)
        {
            OnPropertyChanged(nameof(CanEditTextAndEmotion));
            OnPropertyChanged(nameof(IsLineEditorActive));
        }
    }

    internal void MarkAppliedDraft(WorkbenchRowDraft draft)
    {
        AppliedDraft = draft;
        CharacterVisualBinding = draft.CharacterVisualBinding ?? VisualAssetBinding.None;
        VoiceBinding = draft.VoiceBinding ?? VoiceAssetBinding.None;
        ApplySourceMetadata(draft.SourceItemType, draft.SourceRole, draft.SourceRoleCharacterId);
    }

    internal void RefreshTtsDetailTextBinding()
    {
        OnPropertyChanged(nameof(TtsDetailText));
    }

#pragma warning disable MVVMTK0034
    internal void ApplyScenePreview(string previewAssetText, string previewAssetPath)
    {
        var previewChanged = false;
        if (!string.Equals(PreviewAssetText, previewAssetText, StringComparison.Ordinal))
        {
            _previewAssetText = previewAssetText;
            OnPropertyChanged(nameof(PreviewAssetText));
            previewChanged = true;
        }

        if (!string.Equals(PreviewAssetPath, previewAssetPath, StringComparison.Ordinal))
        {
            _previewAssetPath = previewAssetPath;
            OnPropertyChanged(nameof(PreviewAssetPath));
            previewChanged = true;
        }

        if (previewChanged)
        {
            InvalidateScenePreview();
            RefreshObjectPreview();
        }
    }

    internal ResolvedPreviewAssetChanges ApplyResolvedPreviewAssets(
        string selectedCharacterPreviewAsset,
        string previewAssetPath)
    {
        var characterPreviewChanged = false;
        var scenePreviewChanged = false;
        if (!string.Equals(SelectedCharacterPreviewAsset, selectedCharacterPreviewAsset, StringComparison.Ordinal))
        {
            _selectedCharacterPreviewAsset = selectedCharacterPreviewAsset;
            OnPropertyChanged(nameof(SelectedCharacterPreviewAsset));
            characterPreviewChanged = true;
        }

        if (!string.Equals(PreviewAssetPath, previewAssetPath, StringComparison.Ordinal))
        {
            _previewAssetPath = previewAssetPath;
            OnPropertyChanged(nameof(PreviewAssetPath));
            scenePreviewChanged = true;
        }

        if (characterPreviewChanged)
        {
            InvalidateSelectedCharacterPreview();
        }

        if (scenePreviewChanged)
        {
            InvalidateScenePreview();
        }

        if (characterPreviewChanged || scenePreviewChanged)
        {
            RefreshObjectPreview();
        }

        return new ResolvedPreviewAssetChanges(characterPreviewChanged, scenePreviewChanged);
    }

    internal bool ApplyResolvedVoiceDuration(string? audioPath)
    {
        var next = FormatVoiceDurationText(audioPath);
        if (string.Equals(VoiceDurationText, next, StringComparison.Ordinal))
        {
            return false;
        }

        _voiceDurationText = next;
        OnPropertyChanged(nameof(VoiceDurationText));
        return true;
    }

    internal void ApplyCharacterBindingProjection(
        string selectedCharacterAsset,
        string selectedCharacterPreviewAsset,
        string selectedVoiceReference,
        string selectedVoiceReferenceText,
        string previewAssetText,
        string previewAssetPath,
        string voiceAssetText)
    {
        var characterPreviewChanged = false;
        var scenePreviewChanged = false;
        if (!string.Equals(SelectedCharacterAsset, selectedCharacterAsset, StringComparison.Ordinal))
        {
            _selectedCharacterAsset = selectedCharacterAsset;
            InvalidateAppliedDraft();
            OnPropertyChanged(nameof(SelectedCharacterAsset));
            characterPreviewChanged = true;
        }

        if (!string.Equals(SelectedCharacterPreviewAsset, selectedCharacterPreviewAsset, StringComparison.Ordinal))
        {
            _selectedCharacterPreviewAsset = selectedCharacterPreviewAsset;
            OnPropertyChanged(nameof(SelectedCharacterPreviewAsset));
            characterPreviewChanged = true;
        }

        if (!string.Equals(SelectedVoiceReference, selectedVoiceReference, StringComparison.Ordinal))
        {
            _selectedVoiceReference = selectedVoiceReference;
            InvalidateAppliedDraft();
            OnPropertyChanged(nameof(SelectedVoiceReference));
        }

        if (!string.Equals(SelectedVoiceReferenceText, selectedVoiceReferenceText, StringComparison.Ordinal))
        {
            _selectedVoiceReferenceText = selectedVoiceReferenceText;
            InvalidateAppliedDraft();
            OnPropertyChanged(nameof(SelectedVoiceReferenceText));
        }

        if (!string.Equals(PreviewAssetText, previewAssetText, StringComparison.Ordinal))
        {
            _previewAssetText = previewAssetText;
            OnPropertyChanged(nameof(PreviewAssetText));
            scenePreviewChanged = true;
        }

        if (!string.Equals(PreviewAssetPath, previewAssetPath, StringComparison.Ordinal))
        {
            _previewAssetPath = previewAssetPath;
            OnPropertyChanged(nameof(PreviewAssetPath));
            scenePreviewChanged = true;
        }

        if (!string.Equals(VoiceAssetText, voiceAssetText, StringComparison.Ordinal))
        {
            _voiceAssetText = voiceAssetText;
            OnPropertyChanged(nameof(VoiceAssetText));
        }

        if (characterPreviewChanged)
        {
            InvalidateSelectedCharacterPreview();
        }

        if (scenePreviewChanged)
        {
            InvalidateScenePreview();
        }

        if (characterPreviewChanged || scenePreviewChanged)
        {
            RefreshObjectPreview();
        }
    }

#pragma warning disable MVVMTK0034
    internal void ApplyVoiceBindingProjection(
        VoiceAssetBinding? voiceBinding,
        string selectedVoiceReference,
        string selectedVoiceReferenceText,
        string voiceAssetText)
    {
        VoiceBinding = voiceBinding ?? VoiceAssetBinding.None;
        if (!string.Equals(SelectedVoiceReference, selectedVoiceReference, StringComparison.Ordinal))
        {
            _selectedVoiceReference = selectedVoiceReference;
            InvalidateAppliedDraft();
            OnPropertyChanged(nameof(SelectedVoiceReference));
        }

        if (!string.Equals(SelectedVoiceReferenceText, selectedVoiceReferenceText, StringComparison.Ordinal))
        {
            _selectedVoiceReferenceText = selectedVoiceReferenceText;
            InvalidateAppliedDraft();
            OnPropertyChanged(nameof(SelectedVoiceReferenceText));
        }

        if (!string.Equals(VoiceAssetText, voiceAssetText, StringComparison.Ordinal))
        {
            _voiceAssetText = voiceAssetText;
            OnPropertyChanged(nameof(VoiceAssetText));
        }
    }
#pragma warning restore MVVMTK0034

    internal void ApplyCharacterVisualBindingProjection(
        VisualAssetBinding visualBinding,
        string selectedCharacterAsset,
        string selectedCharacterPreviewAsset,
        string previewAssetText,
        string previewAssetPath)
    {
        CharacterVisualBinding = visualBinding;
        ApplyCharacterBindingProjection(
            selectedCharacterAsset,
            selectedCharacterPreviewAsset,
            SelectedVoiceReference,
            SelectedVoiceReferenceText,
            previewAssetText,
            previewAssetPath,
            VoiceAssetText);
    }
#pragma warning restore MVVMTK0034

    internal void SetOwnerScene(SceneTableViewModel? scene)
    {
        OwnerScene = scene;
    }

    private void InvalidateAppliedDraft()
    {
        AppliedDraft = null;
    }

    private void InvalidateEmotionVectorCaches()
    {
        _emotionVectorMap = null;
        _strongestEmotionVector = null;
        _hasStrongestEmotionVector = false;
        InvalidateEffectiveEmotionTextCaches();
    }

    private void InvalidateEffectiveEmotionTextCaches()
    {
        _effectiveEmotionText = null;
        _effectiveEmotionDisplayText = null;
    }

    private void InvalidateObjectPreviewInitial()
    {
        _objectPreviewInitial = null;
    }

    private void InvalidateSelectedCharacterPreview()
    {
        _selectedCharacterPreview = null;
    }

    private void InvalidateScenePreview()
    {
        _scenePreview = null;
    }

    private EmotionVectorViewModel? StrongestEmotionVector()
    {
        if (_hasStrongestEmotionVector)
        {
            return _strongestEmotionVector;
        }

        EmotionVectorViewModel? strongest = null;
        foreach (var vector in EmotionVectors)
        {
            if (strongest is null || vector.Value > strongest.Value)
            {
                strongest = vector;
            }
        }

        _strongestEmotionVector = strongest;
        _hasStrongestEmotionVector = true;
        return strongest;
    }

    private string BuildEffectiveEmotionText()
    {
        var strongest = StrongestEmotionVector();
        if (strongest is not null && strongest.Value > 0)
        {
            return $"{strongest.Key} {strongest.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}";
        }

        return string.IsNullOrWhiteSpace(Emotion) ? "Calm 0" : Emotion;
    }

    private string BuildEffectiveEmotionDisplayText()
    {
        var displayVectors = StrongestEmotionVectorsForDisplay();
        if (displayVectors.Count > 0)
        {
            return string.Join(Environment.NewLine, displayVectors.Select(vector =>
                $"{vector.DisplayName} {vector.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}"));
        }

        return string.IsNullOrWhiteSpace(Emotion)
            ? $"{EmotionVectorViewModel.DisplayNameFor("Calm")} 0"
            : DisplayEmotionText(Emotion);
    }

    private IReadOnlyList<EmotionVectorViewModel> StrongestEmotionVectorsForDisplay()
    {
        return EmotionVectors
            .Select((vector, index) => new { Vector = vector, Index = index })
            .Where(item => item.Vector.Value > 0)
            .OrderByDescending(item => item.Vector.Value)
            .ThenBy(item => item.Index)
            .Take(2)
            .Select(item => item.Vector)
            .ToArray();
    }

    private string BuildObjectPreviewInitial()
    {
        if (!UsesCharacterVisualPreview)
        {
            return SourceItemType?.Trim().ToLowerInvariant() == "effects" ? "场" : "旁";
        }

        var name = SubjectName?.Trim();
        return string.IsNullOrEmpty(name) ? "人" : name[..1].ToUpperInvariant();
    }

    partial void OnSelectedVoiceReferenceChanged(string value)
    {
        InvalidateAppliedDraft();
        var voiceReferenceId = WorkbenchBindingValue.Asset(value);
        var previousVoiceReferenceId = VoiceBinding?.EffectiveVoiceReferenceId;
        var keepSource = string.Equals(previousVoiceReferenceId, voiceReferenceId, StringComparison.Ordinal);
        VoiceBinding = VoiceAssetBinding.FromProjectReference(
            voiceReferenceId,
            WorkbenchBindingValue.Text(SelectedVoiceReferenceText),
            keepSource ? VoiceBinding?.SourceAssetId : null,
            keepSource ? VoiceBinding?.SyncedVoicePath : null);
        VoiceAssetText = DisplayBinding(value, IsNarration ? "默认配音" : "角色声音");
    }

    partial void OnLineTextChanged(string value)
    {
        InvalidateAppliedDraft();
        UpdateCanRegenerateVoice(value);
    }

    partial void OnSelectedCharacterAssetChanged(string value)
    {
        InvalidateAppliedDraft();
        CharacterVisualBinding = VisualAssetBinding.FromImage(WorkbenchBindingValue.Asset(value));
        InvalidateSelectedCharacterPreview();
        SelectedCharacterPreviewAsset = IsExistingImagePath(value) ? value : string.Empty;
        RefreshObjectPreview();
    }

    partial void OnSelectedVoiceReferenceTextChanged(string value)
    {
        InvalidateAppliedDraft();
        VoiceBinding = VoiceAssetBinding.FromProjectReference(
            WorkbenchBindingValue.Asset(SelectedVoiceReference),
            WorkbenchBindingValue.Text(value),
            VoiceBinding?.SourceAssetId,
            VoiceBinding?.SyncedVoicePath);
    }

    partial void OnSelectedCharacterPreviewAssetChanged(string value)
    {
        InvalidateSelectedCharacterPreview();
        RefreshObjectPreview();
    }

    partial void OnPreviewAssetTextChanged(string value)
    {
        InvalidateScenePreview();
        PreviewAssetPath = IsExistingImagePath(value) ? value : string.Empty;
        RefreshObjectPreview();
    }

    partial void OnPreviewAssetPathChanged(string value)
    {
        InvalidateScenePreview();
        RefreshObjectPreview();
    }

    private void OnSubjectNameChanged(string? value)
    {
        var previousUsesCharacterVisualPreview = UsesCharacterVisualPreview;
        InvalidateObjectPreviewInitial();
        OnPropertyChanged(nameof(ObjectPreviewInitial));
        var narration = IsNarrationName(value) || IsNarrationSourceType(SourceItemType);
        if (narration == IsNarration)
        {
            if (previousUsesCharacterVisualPreview != UsesCharacterVisualPreview)
            {
                OnPropertyChanged(nameof(UsesCharacterVisualPreview));
                RefreshObjectPreview();
            }

            return;
        }

        IsUpdatingSubjectDerivedState = true;
        try
        {
            IsNarration = narration;
            if (narration)
            {
                if (string.IsNullOrWhiteSpace(Emotion))
                {
                    Emotion = "Calm 0.6";
                }

                SelectedCharacterAsset = "未绑定行图";
                PreviewAssetText = "行图预览";
            }
            else
            {
                if (string.IsNullOrWhiteSpace(Emotion))
                {
                    Emotion = "克制 60%";
                }

                if (SelectedCharacterAsset == "旁白无需人物" || SelectedCharacterAsset == "未绑定行图")
                {
                    SelectedCharacterAsset = "未绑定";
                }

                if (PreviewAssetText == "场景预览" || PreviewAssetText == "行图预览")
                {
                    PreviewAssetText = "人物预览";
                }
            }
        }
        finally
        {
            IsUpdatingSubjectDerivedState = false;
        }

        VoiceAssetText = DisplayBinding(SelectedVoiceReference, narration ? "默认配音" : "角色声音");
        InvalidateEffectiveEmotionTextCaches();
        OnPropertyChanged(nameof(IsNarration));
        OnPropertyChanged(nameof(UsesCharacterVisualPreview));
        OnPropertyChanged(nameof(IsEmotionEditable));
        OnPropertyChanged(nameof(SourceTypeGlyph));
        OnPropertyChanged(nameof(SourceTypeText));
        OnPropertyChanged(nameof(EffectiveEmotionDisplayText));
        RefreshObjectPreview();
    }

    partial void OnEmotionChanged(string? value)
    {
        SyncEmotionVectorsFromEmotion(value);
        InvalidateAppliedDraft();
        InvalidateEffectiveEmotionTextCaches();
        OnPropertyChanged(nameof(EmotionVectorMap));
        OnPropertyChanged(nameof(EffectiveEmotionDisplayText));
    }

    private void SyncEmotionVectorsFromEmotion(string? value)
    {
        if (EmotionVectors is not { Count: > 0 } emotionVectors || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var key = IndexTts2Defaults.NormalizeEmotion(value);
        var strength = IndexTts2Defaults.ResolveEmotionStrength(value, null);
        foreach (var vector in emotionVectors)
        {
            var next = string.Equals(vector.Key, key, StringComparison.Ordinal)
                ? strength
                : 0;
            if (Math.Abs(vector.Value - next) > 0.0001)
            {
                vector.Value = next;
            }
        }

        InvalidateEmotionVectorCaches();
    }

    partial void OnEmotionAlphaChanged(double value)
    {
        InvalidateAppliedDraft();
    }

    partial void OnOrderChanged(int value)
    {
        InvalidateAppliedDraft();
        OnPropertyChanged(nameof(OrderLabel));
    }

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanPreviewVoice));
        OnPropertyChanged(nameof(IsLineEditorActive));
        OnPropertyChanged(nameof(SelectedEditingContext));
    }

    partial void OnIsTtsSynthesisRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditTextAndEmotion));
        OnPropertyChanged(nameof(IsLineEditorActive));
    }

    partial void OnIsWorkbenchMutationLockedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditTextAndEmotion));
        OnPropertyChanged(nameof(IsLineEditorActive));
    }

    private static string FormatTtsStatus(GenerationTask? task)
    {
        return FormatTtsStatus(task?.Status);
    }

    private void UpdateCanRegenerateVoice(string lineText)
    {
        var next = HasRegeneratableLineText(lineText);
        if (_canRegenerateVoice == next)
        {
            return;
        }

        _canRegenerateVoice = next;
        OnPropertyChanged(nameof(CanRegenerateVoice));
    }

    private static bool HasRegeneratableLineText(string? lineText)
    {
        return !string.IsNullOrWhiteSpace(lineText);
    }

    private static string FormatTtsStatus(GenerationTaskStatus? status)
    {
        return status switch
        {
            GenerationTaskStatus.Pending => "队列中",
            GenerationTaskStatus.Running => "合成中",
            GenerationTaskStatus.Completed => string.Empty,
            GenerationTaskStatus.Failed => "失败",
            GenerationTaskStatus.Draft => "待合成",
            _ => "待合成"
        };
    }

    private static string FormatTtsDetail(GenerationTask? task, string synthesizedAudioAsset)
    {
        if (task is null)
        {
            return "保存后生成语音任务";
        }

        if (task.Status == GenerationTaskStatus.Completed)
        {
            return string.IsNullOrWhiteSpace(synthesizedAudioAsset) ? "已完成，未找到输出文件" : synthesizedAudioAsset;
        }

        if (task.Parameters.TryGetValue(TtsSynthesisQueueService.FailureMessageParameter, out var failureMessage) && !string.IsNullOrWhiteSpace(failureMessage))
        {
            return failureMessage;
        }

        if (task.Parameters.TryGetValue(TtsSynthesisQueueService.PromptIdParameter, out var promptId) && !string.IsNullOrWhiteSpace(promptId))
        {
            return $"prompt: {promptId}";
        }

        return DisplayBinding(task.Parameters.TryGetValue("voiceReferenceId", out var voiceReferenceId) ? voiceReferenceId : null, "等待参考音频");
    }

    private static string FormatTtsDetail(
        GenerationTaskStatus? status,
        string synthesizedAudioAsset,
        string? failureMessage,
        string? promptId,
        string? voiceReferenceId)
    {
        if (status is null)
        {
            return "保存后生成语音任务";
        }

        if (status == GenerationTaskStatus.Completed)
        {
            return string.IsNullOrWhiteSpace(synthesizedAudioAsset) ? "已完成，未找到输出文件" : synthesizedAudioAsset;
        }

        if (!string.IsNullOrWhiteSpace(failureMessage))
        {
            return failureMessage;
        }

        if (!string.IsNullOrWhiteSpace(promptId))
        {
            return $"prompt: {promptId}";
        }

        return DisplayBinding(voiceReferenceId, "等待参考音频");
    }

    private static string? ReadTaskParameter(GenerationTask? task, string key)
    {
        return task?.Parameters.TryGetValue(key, out var value) == true ? value : null;
    }

    private static ObservableCollection<EmotionVectorViewModel> BuildEmotionVectorViewModels(
        IReadOnlyDictionary<string, double>? emotionVectors,
        string emotion)
    {
        var vectors = new ObservableCollection<EmotionVectorViewModel>();
        foreach (var key in IndexTts2Defaults.EmotionKeys)
        {
            vectors.Add(new EmotionVectorViewModel(key, ResolveVectorValue(key, emotionVectors, emotion)));
        }

        return vectors;
    }

    private static void ResolveInteractionTasks(
        IReadOnlyList<GenerationTask> tasks,
        out GenerationTask? ttsTask)
    {
        ttsTask = null;
        for (var index = 0; index < tasks.Count; index++)
        {
            var task = tasks[index];
            if (ttsTask is null && task.Type == GenerationTaskType.Tts)
            {
                ttsTask = task;
            }

            if (ttsTask is not null)
            {
                return;
            }
        }
    }

    private static ProjectDerivedRowState CaptureProjectDerivedState(
        CharacterInteraction interaction,
        InteractionRowBuildContext context)
    {
        return CaptureProjectDerivedState(CaptureProjectDerivedSourceSnapshot(interaction, context));
    }

    private static ProjectDerivedRowState CaptureProjectDerivedState(ProjectDerivedSourceSnapshot source)
    {
        return new ProjectDerivedRowState(
            source.TtsTaskId,
            source.TtsStatus,
            FormatTtsStatus(source.TtsStatus),
            FormatTtsDetail(source.TtsStatus, source.SynthesizedAudioAsset, source.FailureMessage, source.PromptId, source.VoiceReferenceId),
            source.SynthesizedAudioAsset,
            FormatVoiceDurationText(source.SynthesizedAudioAsset));
    }

    private static ProjectDerivedSourceSnapshot CaptureProjectDerivedSourceSnapshot(
        CharacterInteraction interaction,
        InteractionRowBuildContext context)
    {
        var ttsTask = context.TtsTask(interaction);
        var synthesizedAudioAsset = TtsGeneratedAudioReference.Resolve(ttsTask) ?? string.Empty;

        return new ProjectDerivedSourceSnapshot(
            ttsTask?.Id,
            ttsTask?.Status,
            synthesizedAudioAsset,
            ReadTaskParameter(ttsTask, TtsSynthesisQueueService.FailureMessageParameter),
            ReadTaskParameter(ttsTask, TtsSynthesisQueueService.PromptIdParameter),
            ReadTaskParameter(ttsTask, "voiceReferenceId"));
    }

    private readonly record struct ProjectDerivedSourceSnapshot(
        string? TtsTaskId,
        GenerationTaskStatus? TtsStatus,
        string SynthesizedAudioAsset,
        string? FailureMessage,
        string? PromptId,
        string? VoiceReferenceId);

    private readonly record struct ProjectDerivedRowState(
        string? TtsTaskId,
        GenerationTaskStatus? TtsStatus,
        string TtsStatusText,
        string TtsDetailText,
        string SynthesizedAudioAsset,
        string VoiceDurationText);

    private static string FormatVoiceDurationText(string? audioPath)
    {
        if (!AudioDurationReader.TryReadSeconds(audioPath, out var seconds))
        {
            return string.Empty;
        }

        var wholeSeconds = Math.Clamp((int)Math.Round(seconds, MidpointRounding.AwayFromZero), 0, 99);
        return $"{wholeSeconds:00}s";
    }

    private static string DisplayBinding(string? value, string fallback = "未绑定")
    {
        return string.IsNullOrWhiteSpace(value) || value == "未绑定" ? fallback : value;
    }

    private static string DisplayEmotionText(string emotion)
    {
        var text = emotion.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return $"{EmotionVectorViewModel.DisplayNameFor("Calm")} 0";
        }

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var key = IndexTts2Defaults.NormalizeEmotion(parts.Length > 0 ? parts[0] : text);
        if (parts.Length > 1)
        {
            return $"{EmotionVectorViewModel.DisplayNameFor(key)} {string.Join(' ', parts.Skip(1))}";
        }

        return EmotionVectorViewModel.DisplayNameFor(key);
    }

    private static IReadOnlyDictionary<string, double> ReadEmotionVectors(GenerationTask? task, string emotion, double intensity)
    {
        var vectors = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var key in IndexTts2Defaults.EmotionKeys)
        {
            if (task?.Parameters.TryGetValue($"emotionVector.{key}", out var value) == true &&
                double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                vectors[key] = parsed;
            }
            else
            {
                vectors[key] = 0;
            }
        }

        if (vectors.Values.All(value => value == 0) && !string.IsNullOrWhiteSpace(emotion))
        {
            vectors[IndexTts2Defaults.NormalizeEmotion(emotion)] = Math.Clamp(intensity, 0, IndexTts2Defaults.MaxEmotionStrength);
        }

        return vectors;
    }

    private static string BuildEffectiveEmotion(
        bool isNarration,
        IReadOnlyDictionary<string, double>? emotionVectors,
        EmotionSpec emotion)
    {
        if (isNarration)
        {
            return string.Empty;
        }

        if (emotionVectors is not null)
        {
            string? strongestKey = null;
            var strongestValue = 0d;
            foreach (var vector in emotionVectors)
            {
                if (strongestKey is null || vector.Value > strongestValue)
                {
                    strongestKey = vector.Key;
                    strongestValue = vector.Value;
                }
            }

            if (strongestKey is not null && strongestValue > 0)
            {
                return $"{strongestKey} {strongestValue.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}";
            }
        }

        return string.IsNullOrWhiteSpace(emotion.Emotion)
            ? "Calm 0"
            : $"{emotion.Emotion} {emotion.Intensity.ToString("P0", System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private static double ReadEmotionAlpha(GenerationTask? task)
    {
        if (task?.Parameters.TryGetValue("emotionAlpha", out var value) == true &&
            double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return Math.Clamp(parsed, 0, IndexTts2Defaults.MaxEmotionStrength);
        }

        return 1;
    }

    private static double ResolveVectorValue(string key, IReadOnlyDictionary<string, double>? emotionVectors, string emotion)
    {
        if (emotionVectors is not null && emotionVectors.TryGetValue(key, out var value))
        {
            return Math.Clamp(value, 0, IndexTts2Defaults.MaxEmotionStrength);
        }

        return IndexTts2Defaults.NormalizeEmotion(emotion) == key ? 0.6 : 0;
    }

    private static bool IsNarrationName(string? value)
    {
        var normalized = value?.Trim();
        return string.Equals(normalized, NarrationSubjectName, StringComparison.Ordinal) ||
            string.Equals(normalized, EnvironmentEffectsSubjectName, StringComparison.Ordinal) ||
            string.Equals(normalized, NarrationStorySubjectName, StringComparison.Ordinal);
    }

    private void ApplySourceMetadata(string? sourceItemType, string? sourceRole, string? sourceRoleCharacterId)
    {
        var previousUsesCharacterVisualPreview = UsesCharacterVisualPreview;
        SourceItemType = sourceItemType ?? string.Empty;
        SourceRole = sourceRole ?? string.Empty;
        SourceRoleCharacterId = sourceRoleCharacterId;
        if (previousUsesCharacterVisualPreview != UsesCharacterVisualPreview)
        {
            InvalidateObjectPreviewInitial();
            OnPropertyChanged(nameof(UsesCharacterVisualPreview));
            OnPropertyChanged(nameof(ObjectPreviewInitial));
            RefreshObjectPreview();
        }
    }

    private static string DisplaySubjectName(
        CharacterInteraction interaction,
        InteractionRowBuildContext context,
        bool isNarration)
    {
        var sourceType = interaction.SourceItemType.Trim();
        if (isNarration)
        {
            if (string.Equals(sourceType, "effects", StringComparison.OrdinalIgnoreCase))
            {
                return EnvironmentEffectsSubjectName;
            }

            if (string.Equals(sourceType, "narration", StringComparison.OrdinalIgnoreCase))
            {
                return NarrationStorySubjectName;
            }

            if (!string.IsNullOrWhiteSpace(interaction.SourceRole))
            {
                return interaction.SourceRole;
            }

            if (!string.IsNullOrWhiteSpace(interaction.SourceRoleCharacterId))
            {
                return context.CharacterName(interaction.SourceRoleCharacterId);
            }

            return NarrationSubjectName;
        }

        return context.CharacterName(interaction.SubjectCharacterId);
    }

    private static string DisplaySubjectName(WorkbenchRowDraft row)
    {
        return row.SourceItemType.Trim().ToLowerInvariant() switch
        {
            "effects" => EnvironmentEffectsSubjectName,
            "narration" => NarrationStorySubjectName,
            _ => row.SubjectName
        };
    }

    internal static bool UsesCharacterVisualPreviewForDraft(WorkbenchRowDraft row)
    {
        return IsCharacterVisualSource(
            row.SourceItemType,
            row.SourceRole,
            row.SourceRoleCharacterId,
            row.SubjectName,
            row.SubjectCharacterId,
            row.Kind == WorkbenchRowKind.Narration);
    }

    internal static bool UsesCharacterVisualPreviewForInteraction(CharacterInteraction interaction)
    {
        return IsCharacterVisualSource(
            interaction.SourceItemType,
            interaction.SourceRole,
            interaction.SourceRoleCharacterId,
            interaction.SourceRole,
            interaction.SubjectCharacterId,
            interaction.Narration is not null);
    }

    private static bool IsCharacterVisualSource(
        string? sourceItemType,
        string? sourceRole,
        string? sourceRoleCharacterId,
        string? subjectName,
        string? subjectCharacterId,
        bool isNarration)
    {
        return sourceItemType?.Trim().ToLowerInvariant() switch
        {
            "dialogue" or "action" or "thought" => true,
            "effects" or "narration" => false,
            _ => !isNarration ||
                !string.IsNullOrWhiteSpace(sourceRoleCharacterId) ||
                !string.IsNullOrWhiteSpace(sourceRole) ||
                (!IsNarrationName(subjectName) && !string.IsNullOrWhiteSpace(subjectCharacterId))
        };
    }

    private static bool IsNarrationSourceType(string? sourceItemType)
    {
        return sourceItemType?.Trim().ToLowerInvariant() is "action" or "thought" or "effects" or "narration";
    }

    private static string SourceTypeGlyphFor(string? sourceItemType, bool isNarration)
    {
        return sourceItemType?.Trim().ToLowerInvariant() switch
        {
            "dialogue" => "词",
            "action" => "动",
            "thought" => "念",
            "effects" => "场",
            "narration" => "旁",
            _ => isNarration ? "旁" : "词"
        };
    }

    private static string SourceTypeTextFor(string? sourceItemType, bool isNarration)
    {
        return sourceItemType?.Trim().ToLowerInvariant() switch
        {
            "dialogue" => "对白台词",
            "action" => "动作反应",
            "thought" => "内心独白",
            "effects" => "环境场效",
            "narration" => "旁白叙述",
            _ => isNarration ? "旁白叙述" : "对白台词"
        };
    }

    private static bool IsExistingImagePath(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            Path.IsPathRooted(value) &&
            File.Exists(value);
    }

    private static ImagePreviewSnapshot CaptureImagePreview(string? source, string asset)
    {
        var normalizedSource = source ?? string.Empty;
        var hasImage = IsExistingImagePath(normalizedSource);
        return new ImagePreviewSnapshot(
            asset,
            normalizedSource,
            hasImage,
            hasImage ? Path.GetFileName(normalizedSource) : asset,
            InlineObjectPreviewSide,
            InlineObjectPreviewSide);
    }

    private void RefreshObjectPreview()
    {
        RefreshInlineObjectPreview();
    }

    private void RefreshInlineObjectPreview()
    {
        OnPropertyChanged(nameof(InlineObjectPreviewSource));
        OnPropertyChanged(nameof(InlineObjectPreviewWidth));
        OnPropertyChanged(nameof(InlineObjectPreviewHeight));
        OnPropertyChanged(nameof(HasInlineObjectPreviewImage));
        OnPropertyChanged(nameof(HasNoInlineObjectPreviewImage));
        OnPropertyChanged(nameof(InlineObjectPreviewLabel));
        OnPropertyChanged(nameof(InlineObjectPreviewContext));
    }

    private void OnEmotionVectorsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateAppliedDraft();
        InvalidateEmotionVectorCaches();
        if (e.OldItems is not null)
        {
            foreach (EmotionVectorViewModel vector in e.OldItems)
            {
                vector.PropertyChanged -= OnEmotionVectorPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (EmotionVectorViewModel vector in e.NewItems)
            {
                vector.PropertyChanged += OnEmotionVectorPropertyChanged;
            }
        }

        OnPropertyChanged(nameof(EmotionVectorMap));
        OnPropertyChanged(nameof(EffectiveEmotionDisplayText));
    }

    private void OnEmotionVectorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EmotionVectorViewModel.Value))
        {
            InvalidateAppliedDraft();
            InvalidateEmotionVectorCaches();
            OnPropertyChanged(nameof(EmotionVectorMap));
            OnPropertyChanged(nameof(EffectiveEmotionDisplayText));
        }
    }

    private sealed record ImagePreviewSnapshot(
        string Asset,
        string Source,
        bool HasImage,
        string Label,
        double DisplayWidth,
        double DisplayHeight);

}

internal readonly record struct ResolvedPreviewAssetChanges(
    bool CharacterPreviewChanged,
    bool ScenePreviewChanged)
{
    public bool Any => CharacterPreviewChanged || ScenePreviewChanged;
}
