using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DraftAgent.Core;

namespace DraftAgent.App.ViewModels;

public sealed partial class SceneTableViewModel : ObservableObject
{
    [ObservableProperty]
    private int _order;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isDragSourceActive;

    [ObservableProperty]
    private string _selectedSceneAsset = "未绑定";

    [ObservableProperty]
    private string _selectedScenePreviewAsset = string.Empty;

    [ObservableProperty]
    private string _sceneName;

    [ObservableProperty]
    private string _sceneEffect;

    private ImagePreviewSnapshot? _scenePreview;
    private ImagePreviewSnapshot? _firstCharacterPreview;
    private readonly HashSet<AnalysisResultRowViewModel> _rowsWithCharacterPreviewImages = new(ReferenceEqualityComparer.Instance);
    private string _sceneMeta;

    private SceneTableViewModel(
        string sceneId,
        int order,
        string sceneName,
        string sceneEffect,
        string sceneMeta,
        string selectedSceneAsset,
        IEnumerable<AnalysisResultRowViewModel> rows)
    {
        SceneId = sceneId;
        Order = order;
        SceneName = sceneName;
        SceneEffect = sceneEffect;
        _sceneMeta = sceneMeta;
        _selectedSceneAsset = selectedSceneAsset;
        _selectedScenePreviewAsset = IsExistingImagePath(selectedSceneAsset) ? selectedSceneAsset : string.Empty;
        Rows = new ObservableCollection<AnalysisResultRowViewModel>(rows);
        Rows.CollectionChanged += OnRowsChanged;
        foreach (var row in Rows)
        {
            AttachRow(row);
        }
    }

    public string CollapseGlyph => IsExpanded ? "⌃" : "⌄";

    public string SceneId { get; }

    public string SceneLabel => $"场景 {Order}";

    public string SceneMeta => _sceneMeta;

    public ObservableCollection<AnalysisResultRowViewModel> Rows { get; }

    internal WorkbenchSceneDraft? AppliedDraft { get; private set; }

    internal VisualAssetBinding? VisualBinding { get; private set; }

    public bool HasSceneImagePreview => ScenePreview.HasImage;

    public bool HasNoSceneImagePreview => !HasSceneImagePreview;

    public string SceneImagePreviewLabel => ScenePreview.Label;

    public string FirstCharacterPreviewAsset => FirstCharacterPreview.Asset;

    public bool HasFirstCharacterPreview => FirstCharacterPreview.HasImage;

    public bool HasNoFirstCharacterPreview => !HasFirstCharacterPreview;

    public string FirstCharacterPreviewLabel => FirstCharacterPreview.Label;

    private ImagePreviewSnapshot ScenePreview =>
        _scenePreview ??= CaptureImagePreview(SelectedScenePreviewAsset, SelectedSceneAsset);

    private ImagePreviewSnapshot FirstCharacterPreview =>
        _firstCharacterPreview ??= CaptureFirstCharacterPreview();

    public static SceneTableViewModel FromScene(InteractionScene scene, InteractionProject project)
    {
        return FromScene(scene, AnalysisResultRowViewModel.InteractionRowBuildContext.FromProject(project));
    }

    internal static SceneTableViewModel FromScene(
        InteractionScene scene,
        AnalysisResultRowViewModel.InteractionRowBuildContext context)
    {
        return new SceneTableViewModel(
            scene.Id,
            scene.Order,
            scene.Name,
            scene.Description,
            BuildSceneMeta(scene),
            DisplayBinding(scene.AssetBinding.BackgroundImage ?? FirstInteractionSceneAsset(scene.Interactions)),
            BuildInteractionRows(scene.Interactions, context, scene.AssetBinding.BackgroundImage));
    }

    public static SceneTableViewModel CreateManual(int order)
    {
        return new SceneTableViewModel(
            $"scene_manual_{Guid.NewGuid():N}",
            order,
            $"场景 {order}",
            "待填写场景效果",
            "自定义",
            "未绑定",
            []);
    }

    internal static SceneTableViewModel FromDraft(WorkbenchSceneDraft scene)
    {
        var scenePreviewReference = VisualPreviewReference(scene.VisualBinding);
        var viewModel = new SceneTableViewModel(
            scene.SceneId,
            scene.Order,
            scene.Name,
            scene.Effect,
            "工作台",
            scenePreviewReference ?? "未绑定",
            BuildDraftRows(scene.Rows, scenePreviewReference));
        viewModel.MarkAppliedDraft(scene);
        for (var index = 0; index < viewModel.Rows.Count && index < scene.Rows.Count; index++)
        {
            viewModel.Rows[index].MarkAppliedDraft(scene.Rows[index]);
        }

        return viewModel;
    }

    private static string? VisualPreviewReference(VisualAssetBinding? visualBinding)
    {
        if (visualBinding is { SourceKind: VisualSourceKind.Psd })
        {
            return visualBinding.RenderedAssetId;
        }

        return visualBinding?.EffectiveAssetId;
    }

    public int NextRowOrder()
    {
        if (Rows.Count == 0)
        {
            return 1;
        }

        var lastRow = Rows[^1];
        if (lastRow.Order == Rows.Count)
        {
            return Rows.Count + 1;
        }

        var maxOrder = 0;
        for (var index = 0; index < Rows.Count; index++)
        {
            maxOrder = Math.Max(maxOrder, Rows[index].Order);
        }

        return maxOrder + 1;
    }

    public void RenumberRows()
    {
        for (var index = 0; index < Rows.Count; index++)
        {
            Rows[index].Order = index + 1;
        }
    }

    public void RenumberRows(int startIndex, int endIndexInclusive)
    {
        if (Rows.Count == 0)
        {
            return;
        }

        var start = Math.Clamp(startIndex, 0, Rows.Count - 1);
        var end = Math.Clamp(endIndexInclusive, 0, Rows.Count - 1);
        if (start > end)
        {
            (start, end) = (end, start);
        }

        for (var index = start; index <= end; index++)
        {
            Rows[index].Order = index + 1;
        }
    }

    public void RefreshPreviewAssets()
    {
        var previousScenePreview = ScenePreview;
        InvalidateScenePreview();
        InvalidateFirstCharacterPreview();
        var currentScenePreview = ScenePreview;

        NotifyScenePreviewChanges(previousScenePreview, currentScenePreview, force: false);
    }

    public void RefreshPreviewAssets(bool scenePreviewChanged, bool firstCharacterPreviewChanged, bool forceScenePreviewNotifications = true)
    {
        ImagePreviewSnapshot? previousScenePreview = null;
        ImagePreviewSnapshot? currentScenePreview = null;
        if (scenePreviewChanged)
        {
            previousScenePreview = ScenePreview;
            InvalidateScenePreview();
            currentScenePreview = ScenePreview;
        }

        if (firstCharacterPreviewChanged)
        {
            InvalidateFirstCharacterPreview();
        }

        if (previousScenePreview is not null && currentScenePreview is not null)
        {
            NotifyScenePreviewChanges(previousScenePreview, currentScenePreview, force: forceScenePreviewNotifications);
        }
    }

    internal void MarkAppliedDraft(WorkbenchSceneDraft draft)
    {
        AppliedDraft = draft;
        VisualBinding = draft.VisualBinding ?? VisualAssetBinding.None;
    }

#pragma warning disable MVVMTK0034
    internal void ApplySceneVisualBindingProjection(
        VisualAssetBinding visualBinding,
        string selectedSceneAsset,
        string selectedScenePreviewAsset)
    {
        VisualBinding = visualBinding;
        if (!string.Equals(SelectedSceneAsset, selectedSceneAsset, StringComparison.Ordinal))
        {
            _selectedSceneAsset = selectedSceneAsset;
            InvalidateAppliedDraft();
            OnPropertyChanged(nameof(SelectedSceneAsset));
        }

        if (!string.Equals(SelectedScenePreviewAsset, selectedScenePreviewAsset, StringComparison.Ordinal))
        {
            _selectedScenePreviewAsset = selectedScenePreviewAsset;
            OnPropertyChanged(nameof(SelectedScenePreviewAsset));
        }

        RefreshPreviewAssets(scenePreviewChanged: true, firstCharacterPreviewChanged: false);
    }
#pragma warning restore MVVMTK0034

    internal void ApplyProjectDerivedState(InteractionScene scene)
    {
        var sceneMeta = BuildSceneMeta(scene);
        if (!string.Equals(_sceneMeta, sceneMeta, StringComparison.Ordinal))
        {
            _sceneMeta = sceneMeta;
        }
    }

    internal void RefreshFirstCharacterPreviewAfterResolvedAssetChange(AnalysisResultRowViewModel row)
    {
        RefreshFirstCharacterPreviewAfterRowPreviewChange(row);
    }

    private void InvalidateAppliedDraft()
    {
        AppliedDraft = null;
    }

    private void InvalidateScenePreview()
    {
        _scenePreview = null;
    }

    private void InvalidateFirstCharacterPreview()
    {
        _firstCharacterPreview = null;
    }

    [RelayCommand]
    private void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
    }

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(CollapseGlyph));
    }

    partial void OnOrderChanged(int value)
    {
        InvalidateAppliedDraft();
        OnPropertyChanged(nameof(SceneLabel));
    }

    partial void OnSelectedSceneAssetChanged(string value)
    {
        InvalidateAppliedDraft();
        VisualBinding = VisualAssetBinding.FromImage(WorkbenchBindingValue.Asset(value));
        SelectedScenePreviewAsset = IsExistingImagePath(value) ? value : string.Empty;
        if (string.IsNullOrEmpty(SelectedScenePreviewAsset))
        {
            RefreshPreviewAssets(scenePreviewChanged: true, firstCharacterPreviewChanged: false);
        }
    }

    partial void OnSelectedScenePreviewAssetChanged(string value)
    {
        RefreshPreviewAssets(scenePreviewChanged: true, firstCharacterPreviewChanged: false);
    }

    partial void OnSceneNameChanged(string value)
    {
        InvalidateAppliedDraft();
    }

    partial void OnSceneEffectChanged(string value)
    {
        InvalidateAppliedDraft();
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var refreshFirstCharacterPreview = ShouldRefreshFirstCharacterPreview(e);
        if (e.Action == NotifyCollectionChangedAction.Move)
        {
            InvalidateAppliedDraft();
            if (refreshFirstCharacterPreview)
            {
                InvalidateFirstCharacterPreview();
            }

            return;
        }

        if (e.OldItems is not null)
        {
            foreach (AnalysisResultRowViewModel row in e.OldItems)
            {
                DetachRow(row);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (AnalysisResultRowViewModel row in e.NewItems)
            {
                AttachRow(row);
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            RebuildCharacterPreviewRowIndex();
        }

        InvalidateAppliedDraft();
        if (refreshFirstCharacterPreview)
        {
            InvalidateFirstCharacterPreview();
        }
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AnalysisResultRowViewModel.Order)
            or nameof(AnalysisResultRowViewModel.SubjectName)
            or nameof(AnalysisResultRowViewModel.IsNarration)
            or nameof(AnalysisResultRowViewModel.LineText)
            or nameof(AnalysisResultRowViewModel.Emotion)
            or nameof(AnalysisResultRowViewModel.EmotionAlpha)
            or nameof(AnalysisResultRowViewModel.EmotionVectorMap)
            or nameof(AnalysisResultRowViewModel.SelectedCharacterAsset)
            or nameof(AnalysisResultRowViewModel.SelectedVoiceReference)
            or nameof(AnalysisResultRowViewModel.SelectedVoiceReferenceText))
        {
            InvalidateAppliedDraft();
        }

        if (e.PropertyName == nameof(AnalysisResultRowViewModel.SelectedCharacterPreviewAsset) &&
            sender is AnalysisResultRowViewModel row)
        {
            RefreshFirstCharacterPreviewAfterRowPreviewChange(row);
        }
    }

    private void AttachRow(AnalysisResultRowViewModel row)
    {
        row.SetOwnerScene(this);
        row.PropertyChanged += OnRowPropertyChanged;
        TrackCharacterPreviewRow(row);
    }

    private void DetachRow(AnalysisResultRowViewModel row)
    {
        row.PropertyChanged -= OnRowPropertyChanged;
        UntrackCharacterPreviewRow(row);
        if (ReferenceEquals(row.OwnerScene, this))
        {
            row.SetOwnerScene(null);
        }
    }

    private bool ShouldRefreshFirstCharacterPreview(NotifyCollectionChangedEventArgs e)
    {
        return e.Action switch
        {
            NotifyCollectionChangedAction.Add => ContainsCharacterPreviewRow(e.NewItems),
            NotifyCollectionChangedAction.Remove => ContainsTrackedCharacterPreviewRow(e.OldItems),
            NotifyCollectionChangedAction.Replace => ContainsTrackedCharacterPreviewRow(e.OldItems) ||
                ContainsCharacterPreviewRow(e.NewItems),
            NotifyCollectionChangedAction.Move => ContainsTrackedCharacterPreviewRow(e.NewItems),
            NotifyCollectionChangedAction.Reset => true,
            _ => true
        };
    }

    private bool ContainsTrackedCharacterPreviewRow(System.Collections.IList? rows)
    {
        if (rows is null)
        {
            return false;
        }

        foreach (AnalysisResultRowViewModel row in rows)
        {
            if (_rowsWithCharacterPreviewImages.Contains(row))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsCharacterPreviewRow(System.Collections.IList? rows)
    {
        if (rows is null)
        {
            return false;
        }

        foreach (AnalysisResultRowViewModel row in rows)
        {
            if (RowHasCharacterPreviewImage(row))
            {
                return true;
            }
        }

        return false;
    }

    private void RefreshFirstCharacterPreviewAfterRowPreviewChange(AnalysisResultRowViewModel row)
    {
        var hadPreview = _rowsWithCharacterPreviewImages.Contains(row);
        var hasPreview = RowHasCharacterPreviewImage(row);
        if (!hadPreview && !hasPreview)
        {
            return;
        }

        if (hasPreview)
        {
            _rowsWithCharacterPreviewImages.Add(row);
        }
        else
        {
            _rowsWithCharacterPreviewImages.Remove(row);
        }

        InvalidateFirstCharacterPreview();
    }

    private void TrackCharacterPreviewRow(AnalysisResultRowViewModel row)
    {
        if (RowHasCharacterPreviewImage(row))
        {
            _rowsWithCharacterPreviewImages.Add(row);
        }
    }

    private void UntrackCharacterPreviewRow(AnalysisResultRowViewModel row)
    {
        _rowsWithCharacterPreviewImages.Remove(row);
    }

    private void RebuildCharacterPreviewRowIndex()
    {
        _rowsWithCharacterPreviewImages.Clear();
        foreach (var row in Rows)
        {
            TrackCharacterPreviewRow(row);
        }
    }

    private ImagePreviewSnapshot CaptureFirstCharacterPreview()
    {
        foreach (var row in Rows)
        {
            var previewAsset = row.SelectedCharacterPreviewAsset;
            if (IsExistingImagePath(previewAsset))
            {
                return new ImagePreviewSnapshot(previewAsset, true, Path.GetFileName(previewAsset));
            }
        }

        return new ImagePreviewSnapshot("随行显示", false, "随行显示");
    }

    private static string DisplayBinding(string? value, string fallback = "未绑定")
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static string BuildSceneMeta(InteractionScene scene)
    {
        var parts = new List<string>(4);
        AddMetaPart(parts, scene.Location);
        AddMetaPart(parts, scene.TimeOfDay);
        AddMetaPart(parts, scene.Weather);
        AddMetaPart(parts, scene.Mood);
        return string.Join(" / ", parts);
    }

    private static void AddMetaPart(List<string> parts, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add(value);
        }
    }

    private static string? FirstInteractionSceneAsset(IReadOnlyList<CharacterInteraction> interactions)
    {
        for (var index = 0; index < interactions.Count; index++)
        {
            var sceneAssetId = interactions[index].Binding.SceneVisualBinding?.EffectiveAssetId;
            if (!string.IsNullOrWhiteSpace(sceneAssetId))
            {
                return sceneAssetId;
            }
        }

        return null;
    }

    private static IReadOnlyList<AnalysisResultRowViewModel> BuildInteractionRows(
        IReadOnlyList<CharacterInteraction> interactions,
        AnalysisResultRowViewModel.InteractionRowBuildContext context,
        string? sceneAssetId)
    {
        var rows = new AnalysisResultRowViewModel[interactions.Count];
        for (var index = 0; index < interactions.Count; index++)
        {
            rows[index] = AnalysisResultRowViewModel.FromInteraction(interactions[index], context, sceneAssetId);
        }

        return rows;
    }

    private static IReadOnlyList<AnalysisResultRowViewModel> BuildDraftRows(
        IReadOnlyList<WorkbenchRowDraft> drafts,
        string? sceneAssetId)
    {
        var rows = new AnalysisResultRowViewModel[drafts.Count];
        for (var index = 0; index < drafts.Count; index++)
        {
            rows[index] = AnalysisResultRowViewModel.FromDraft(drafts[index], sceneAssetId);
        }

        return rows;
    }

    private static bool IsExistingImagePath(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            Path.IsPathRooted(value) &&
            File.Exists(value);
    }

    private static bool RowHasCharacterPreviewImage(AnalysisResultRowViewModel row)
    {
        return IsExistingImagePath(row.SelectedCharacterPreviewAsset);
    }

    private static ImagePreviewSnapshot CaptureImagePreview(string? source, string fallback)
    {
        var normalizedSource = source ?? string.Empty;
        var hasImage = IsExistingImagePath(normalizedSource);
        return new ImagePreviewSnapshot(
            hasImage ? normalizedSource : fallback,
            hasImage,
            hasImage ? Path.GetFileName(normalizedSource) : fallback);
    }

    private void NotifyScenePreviewChanges(ImagePreviewSnapshot previous, ImagePreviewSnapshot current, bool force)
    {
        if (force || previous.HasImage != current.HasImage)
        {
            OnPropertyChanged(nameof(HasSceneImagePreview));
            OnPropertyChanged(nameof(HasNoSceneImagePreview));
        }

        if (force || !string.Equals(previous.Label, current.Label, StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(SceneImagePreviewLabel));
        }
    }

    private sealed record ImagePreviewSnapshot(string Asset, bool HasImage, string Label);
}
