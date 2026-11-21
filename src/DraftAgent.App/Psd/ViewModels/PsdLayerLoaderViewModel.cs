using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DraftAgent.App.Psd.Services;
using DraftAgent.Core;
using PsdLayerLoader.Core;

namespace DraftAgent.App.Psd.ViewModels;

public sealed partial class PsdLayerLoaderViewModel : ObservableObject
{
    private readonly PsdLayerCore _core;
    private readonly PsdLayerSelectionResolver _selectionResolver;
    private readonly PsdGuiPreviewCache _guiCache = new();
    private readonly PsdLayerManagementUiState _layerUi = new();
    private PsdLayerRenderParameters _renderParameters = new(string.Empty, PsdExportBounds.LayerBounds, null, true);
    private PsdMergedPreview? _currentExportPreview;
    private PsdLayerCoreSnapshot _snapshot = PsdLayerCoreSnapshot.Empty;
    private int _thumbnailLoadVersion;
    private int _previewRenderVersion;

    public PsdLayerLoaderViewModel()
        : this(new PsdLayerCore(), new PsdLayerSelectionResolver())
    {
    }

    internal PsdLayerLoaderViewModel(PsdLayerCore core, PsdLayerSelectionResolver selectionResolver)
    {
        _core = core;
        _selectionResolver = selectionResolver;
    }

    [ObservableProperty]
    private string _statusText = "Open";

    [ObservableProperty]
    private string _documentTitle = "No PSD loaded";

    [ObservableProperty]
    private string _documentDetails = "Waiting";

    [ObservableProperty]
    private string _loadedPath = string.Empty;

    [ObservableProperty]
    private Bitmap? _previewBitmap;

    [ObservableProperty]
    private string _previewMessage = "No PSD loaded";

    [ObservableProperty]
    private bool _isPreviewPlaceholderVisible = true;

    [ObservableProperty]
    private string _breadcrumbText = "ROOT";

    [ObservableProperty]
    private string _selectionSummary = "No layer selected";

    [ObservableProperty]
    private bool _isMultiSelect;

    [ObservableProperty]
    private bool _canExportPreview;

    [ObservableProperty]
    private bool _isComponentRequestMode;

    [ObservableProperty]
    private string _suggestedExportFileName = "psd-preview.png";

    [ObservableProperty]
    private bool _canConfigureLayerMatch;

    [ObservableProperty]
    private string _layerMatchTemplateSummary = "未绑定 PSD 图层匹配模板";

    public string? LastDragExportPath { get; private set; }

    public bool IsSingleSelect => !IsMultiSelect;

    public ObservableCollection<PsdDocumentTabViewModel> DocumentTabs => _layerUi.DocumentTabs;

    public ObservableCollection<PsdBrowserTabViewModel> GroupTabs => _layerUi.GroupTabs;

    public ObservableCollection<PsdLayerThumbnailViewModel> LayerThumbnails => _layerUi.LayerThumbnails;

    public ObservableCollection<PsdLayerRowViewModel> Layers => _layerUi.Layers;

    internal IReadOnlyList<PsdLayerThumbnailViewModel> AllLayerThumbnails => _layerUi.AllThumbnails;

    internal PsdLayerManagementUiState LayerManagementUi => _layerUi;

    public int LayerCount => _layerUi.LayerCount;

    public int VisibleLayerCount => _core.VisibleLayerIds.Count;

    public int SelectedLayerCount => _layerUi.SelectedLayerCount;

    public int CurrentGroupItemCount => _layerUi.CurrentGroupItemCount;

    public string SelectionModeText => IsMultiSelect ? "多选" : "单选";

    partial void OnIsMultiSelectChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSingleSelect));
        OnPropertyChanged(nameof(SelectionModeText));
        StatusText = value ? "Multi" : "Single";
    }

    public void ApplyLayerMatchTemplate(PsdLayerMatchConfiguration? configuration)
    {
        _layerUi.ApplyLayerMatchTemplate(configuration);
        LayerMatchTemplateSummary = _layerUi.LayerMatchTemplateSummary;
        SyncUiProperties();
    }

    public async Task LoadPsdAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await LoadPsdAsync(new PsdLayerRenderParameters(
            Path.GetFullPath(path),
            PsdExportBounds.LayerBounds,
            null,
            LoadMergedPreview: false));
    }

    public async Task LoadPsdAsync(PsdLayerRenderParameters parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters.PsdPath))
        {
            return;
        }

        var path = Path.GetFullPath(parameters.PsdPath);
        _renderParameters = parameters with { PsdPath = path };
        StatusText = "Loading";
        DocumentTitle = Path.GetFileName(path);
        DocumentDetails = "Reading";
        LoadedPath = path;
        PreviewBitmap = null;
        IsPreviewPlaceholderVisible = true;
        PreviewMessage = "Loading";
        BreadcrumbText = "ROOT";
        SelectionSummary = "No layer selected";
        CanExportPreview = false;
        _currentExportPreview = null;
        _snapshot = PsdLayerCoreSnapshot.Empty;
        _previewRenderVersion++;
        _thumbnailLoadVersion++;
        _guiCache.Clear();
        _layerUi.Reset(path);
        OnPropertyChanged(nameof(LayerCount));
        NotifyBrowserCountsChanged();

        try
        {
            _snapshot = await Task.Run(() => _core.Load(path, parameters.LoadMergedPreview));
            var document = _snapshot.Document;
            var fileName = Path.GetFileName(path);
            var displayName = Path.GetFileNameWithoutExtension(path);

            DocumentTitle = fileName;
            DocumentDetails = $"{document.Width} x {document.Height}, {document.BitsPerChannel}-bit";
            SuggestedExportFileName = $"{displayName}_preview.png";
            _layerUi.LoadDocument(_snapshot, displayName, $"{document.Width} x {document.Height}");
            _layerUi.SelectSingleMode();
            _core.SetVisibleLayers(_layerUi.SelectionState.VisibleLayerIds);
            _layerUi.ApplyVisibleLayers(_core.VisibleLayerIds, _layerUi.SelectionState.SelectedLayerIds);

            if (_snapshot.MergedPreview is not null)
            {
                var mergedBitmap = PsdPreviewBitmapFactory.Create(_snapshot.MergedPreview);
                _guiCache.StorePreview(
                    _snapshot.SourceFingerprint,
                    _core.VisibleLayerIds,
                    _renderParameters.Bounds,
                    mergedBitmap,
                    _snapshot.MergedPreview);
            }

            var refreshedUi = false;
            if (_renderParameters.SelectionHint is not null)
            {
                ApplySelectionHint(_renderParameters.SelectionHint);
                refreshedUi = true;
            }
            else
            {
                UpdatePreviewFromVisibleSelection();
            }

            if (PreviewBitmap is null && _snapshot.MergedPreview is null)
            {
                IsPreviewPlaceholderVisible = true;
                PreviewMessage = $"Layers loaded, but no merged preview was available: {_snapshot.MergedPreviewError}";
            }

            if (!refreshedUi)
            {
                RebuildBrowserTabs();
                RefreshLayerThumbnailGrid();
                UpdateSelectionSummary();
                SyncUiProperties();
            }
            StatusText = $"{_snapshot.DisplayLayers.Count} layers";
        }
        catch (Exception ex)
        {
            DocumentTitle = "Load failed";
            DocumentDetails = ex.Message;
            IsPreviewPlaceholderVisible = true;
            PreviewMessage = "PSD load failed.";
            StatusText = $"Failed: {ex.Message}";
        }
    }

    public async Task InitializeComponentRequestAsync(PsdLayerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        IsComponentRequestMode = true;
        await LoadPsdAsync(PsdLayerRenderParameters.FromRequest(
            request,
            ResolvePath(request.ProjectRoot, request.Psd.Path),
            loadMergedPreview: false));
        SuggestedExportFileName = request.Output.FileNameHint;
    }

    public void EnableLayerMatchConfiguration()
    {
        CanConfigureLayerMatch = true;
    }

    public async Task<PsdLayerResponse> BuildComponentResponseAsync(PsdLayerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            if (string.IsNullOrWhiteSpace(_snapshot.PsdPath))
            {
                return PsdLayerResponse.Failed(
                    request.RequestId,
                    "psd_not_loaded",
                    "PSD document is not loaded.");
            }

            _core.SetVisibleLayers(_layerUi.SelectionState.VisibleLayerIds);
            _layerUi.ApplyVisibleLayers(_core.VisibleLayerIds, _layerUi.SelectionState.SelectedLayerIds);
            var outputDirectory = ResolvePath(request.ProjectRoot, request.Output.Directory);
            var outputPath = BuildOutputPath(outputDirectory, request.Output.FileNameHint);
            var export = await _core.ExportVisiblePngAsync(outputPath, request.Output.Bounds);
            var match = _core.BuildMatchFromCurrent(
                request,
                1.0,
                request.SelectionHint?.PresetId);
            return _core.BuildCompletedResponse(request, export, match);
        }
        catch (Exception ex)
        {
            return PsdLayerResponse.Failed(request.RequestId, "interactive_export_failed", ex.Message);
        }
    }

    public void SelectSingleMode()
    {
        _layerUi.SelectSingleMode();
        IsMultiSelect = false;
        ApplyVisibleSelectionAndPreview(_layerUi.SelectionState.VisibleLayerIds, _layerUi.SelectionState.SelectedLayerIds);
    }

    public void SelectMultiMode()
    {
        IsMultiSelect = true;
        _layerUi.SelectMultiMode();
        ApplyVisibleSelectionAndPreview(_layerUi.SelectionState.VisibleLayerIds, _layerUi.SelectionState.SelectedLayerIds);
    }

    public void SelectGroupTab(PsdBrowserTabViewModel tab)
    {
        _layerUi.SelectGroup(tab.GroupLayerId);
        RefreshLayerThumbnailGrid();
        UpdateSelectionSummary();
        SyncUiProperties();
        StatusText = BreadcrumbText;
    }

    public void ToggleGroupExpanded(PsdBrowserTabViewModel tab)
    {
        _layerUi.ToggleGroupExpanded(tab);
        SyncUiProperties();
    }

    public void SelectRootGroup()
    {
        _layerUi.SelectGroup(null);
        RefreshLayerThumbnailGrid();
        UpdateSelectionSummary();
        SyncUiProperties();
        StatusText = BreadcrumbText;
    }

    public void OpenLayerGroup(PsdLayerThumbnailViewModel thumbnail)
    {
        if (thumbnail.IsGroup)
        {
            _layerUi.SelectGroup(thumbnail.LayerId);
            RefreshLayerThumbnailGrid();
            UpdateSelectionSummary();
            SyncUiProperties();
            StatusText = BreadcrumbText;
        }
    }

    public void NavigateUp()
    {
        _layerUi.NavigateUp();
        RefreshLayerThumbnailGrid();
        SyncUiProperties();
    }

    public void ToggleLayer(PsdLayerThumbnailViewModel thumbnail)
    {
        if (thumbnail.IsHideOption)
        {
            var groupIds = PsdLayerOperationMapper.DirectChildLayerIds(_snapshot.DisplayLayers, _layerUi.CurrentGroupLayerId);
            ApplyVisibleSelectionAndPreview(
                PsdLayerOperationMapper.HideGroup(_core.VisibleLayerIds, groupIds),
                []);
            StatusText = $"Hide {BreadcrumbText}";
            return;
        }

        if (thumbnail.IsGroup)
        {
            OpenLayerGroup(thumbnail);
            return;
        }

        var currentGroupLayerId = _layerUi.CurrentGroupLayerId;
        if (!PsdLayerOperationMapper.IsExclusiveGroup(_snapshot.DisplayLayers, currentGroupLayerId))
        {
            StatusText = $"{thumbnail.Name}: use the eye button to toggle visibility.";
            return;
        }

        var exclusiveGroupIds = PsdLayerOperationMapper.DirectChildLayerIds(_snapshot.DisplayLayers, currentGroupLayerId);
        var next = PsdLayerOperationMapper.ReplaceGroupExclusive(
            _core.VisibleLayerIds,
            exclusiveGroupIds,
            thumbnail.LayerId,
            IsMultiSelect);
        ApplyVisibleSelectionAndPreview(next, [thumbnail.LayerId]);
        StatusText = thumbnail.Name;
    }

    public void ToggleLayerVisibility(PsdLayerThumbnailViewModel thumbnail)
    {
        if (thumbnail.IsGroup || thumbnail.IsHideOption || thumbnail.SupportsExclusiveSwitch)
        {
            return;
        }

        var next = PsdLayerOperationMapper.ToggleLayer(_core.VisibleLayerIds, thumbnail.LayerId);
        ApplyVisibleSelectionAndPreview(next, next.Contains(thumbnail.LayerId) ? [thumbnail.LayerId] : []);
        StatusText = next.Contains(thumbnail.LayerId) ? $"Show {thumbnail.Name}" : $"Hide {thumbnail.Name}";
    }

    public async Task ExportCurrentPreviewAsync(string outputPath)
    {
        StatusText = "Exporting";
        var export = await _core.ExportVisiblePngAsync(outputPath, _renderParameters.Bounds);
        if (!export.Success)
        {
            StatusText = export.ErrorMessage ?? "No preview";
            return;
        }

        StatusText = $"Exported {Path.GetFileName(outputPath)}";
    }

    public async Task<string?> PrepareCurrentPreviewDragFileAsync()
    {
        var preview = _currentExportPreview ?? await _core.RenderVisibleAsync(_renderParameters.Bounds);
        if (preview is null)
        {
            StatusText = "No preview";
            return null;
        }

        var exportPath = await PsdPreviewDragExportService.SaveDragPreviewAsync(
            preview,
            SuggestedExportFileName);
        LastDragExportPath = exportPath;
        StatusText = $"Drag {Path.GetFileName(exportPath)}";
        return exportPath;
    }

    private void ApplyVisibleSelectionAndPreview(
        IEnumerable<int> visibleLayerIds,
        IEnumerable<int> selectedLayerIds)
    {
        _core.SetVisibleLayers(visibleLayerIds);
        _layerUi.ApplyVisibleLayers(_core.VisibleLayerIds, selectedLayerIds);
        RebuildBrowserTabs();
        RefreshLayerThumbnailGrid();
        UpdateSelectionSummary();
        UpdatePreviewFromVisibleSelection();
    }

    private void RebuildBrowserTabs()
    {
        _layerUi.RebuildBrowserTabs();
        SyncUiProperties();
    }

    private void RefreshLayerThumbnailGrid()
    {
        var displayedItems = _layerUi.RefreshLayerThumbnailGrid();
        foreach (var thumbnail in displayedItems)
        {
            thumbnail.SupportsExclusiveSwitch = PsdLayerOperationMapper.IsExclusiveGroup(_snapshot.DisplayLayers, thumbnail.ParentLayerId);
        }

        QueueThumbnailLoading(displayedItems, ++_thumbnailLoadVersion);
        SyncUiProperties();
    }

    private async void QueueThumbnailLoading(IReadOnlyList<PsdLayerThumbnailViewModel> thumbnails, int version)
    {
        foreach (var thumbnail in thumbnails)
        {
            if (version != _thumbnailLoadVersion)
            {
                return;
            }

            if (thumbnail.IsGroup)
            {
                continue;
            }

            if (_guiCache.TryGetThumbnail(thumbnail.LayerId, out var cached))
            {
                thumbnail.ThumbnailBitmap = cached;
                continue;
            }

            var pixelData = await _core.GetLayerPixelsAsync(thumbnail.LayerId);
            if (version != _thumbnailLoadVersion || pixelData is null)
            {
                continue;
            }

            var bitmap = TryCreateThumbnailBitmap(pixelData);
            if (bitmap is not null)
            {
                _guiCache.StoreThumbnail(thumbnail.LayerId, bitmap);
                thumbnail.ThumbnailBitmap = bitmap;
            }
        }
    }

    private void UpdatePreviewFromVisibleSelection()
    {
        var version = ++_previewRenderVersion;
        if (string.IsNullOrWhiteSpace(_snapshot.PsdPath) || _snapshot.Document.Width <= 0 || _snapshot.Document.Height <= 0)
        {
            ApplyFallbackPreview();
            return;
        }

        var visibleLayerIds = _core.VisibleLayerIds;
        if (_guiCache.TryGetPreview(_snapshot.SourceFingerprint, visibleLayerIds, _renderParameters.Bounds, out var cachedBitmap, out var cachedPreview))
        {
            PreviewBitmap = cachedBitmap;
            _currentExportPreview = cachedPreview;
            IsPreviewPlaceholderVisible = false;
            PreviewMessage = string.Empty;
            CanExportPreview = true;
            return;
        }

        CanExportPreview = false;
        PreviewMessage = PreviewBitmap is null ? "Rendering preview..." : string.Empty;
        _ = RenderPreviewAsync(version, visibleLayerIds, CancellationToken.None);
    }

    private async Task RenderPreviewAsync(
        int version,
        IReadOnlySet<int> visibleLayerIds,
        CancellationToken cancellationToken)
    {
        try
        {
            var preview = await _core.RenderVisibleAsync(_renderParameters.Bounds, cancellationToken).ConfigureAwait(false);
            var bitmap = preview is null ? null : TryCreatePreviewBitmap(preview);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (version != _previewRenderVersion)
                {
                    return;
                }

                if (preview is not null && bitmap is not null)
                {
                    PreviewBitmap = bitmap;
                    _currentExportPreview = preview;
                    _guiCache.StorePreview(_snapshot.SourceFingerprint, visibleLayerIds, _renderParameters.Bounds, bitmap, preview);
                    IsPreviewPlaceholderVisible = false;
                    PreviewMessage = string.Empty;
                    CanExportPreview = true;
                    return;
                }

                ApplyFallbackPreview();
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (version != _previewRenderVersion)
                {
                    return;
                }

                PreviewBitmap = null;
                _currentExportPreview = null;
                IsPreviewPlaceholderVisible = true;
                PreviewMessage = $"Preview failed: {ex.Message}";
                CanExportPreview = false;
            });
        }
    }

    private void ApplyFallbackPreview()
    {
        if (_snapshot.Document.Width > 0 &&
            _snapshot.Document.Height > 0 &&
            _core.VisibleLayerIds.Count == 0)
        {
            var transparentPreview = CreateTransparentPreview();
            PreviewBitmap = TryCreatePreviewBitmap(transparentPreview);
            _currentExportPreview = transparentPreview;
            IsPreviewPlaceholderVisible = PreviewBitmap is null;
            PreviewMessage = PreviewBitmap is null ? "Preview rendered." : string.Empty;
            CanExportPreview = true;
            return;
        }

        if (_snapshot.MergedPreview is not null)
        {
            var bitmap = PsdPreviewBitmapFactory.Create(_snapshot.MergedPreview);
            PreviewBitmap = bitmap;
            _currentExportPreview = _snapshot.MergedPreview;
            IsPreviewPlaceholderVisible = false;
            PreviewMessage = string.Empty;
            CanExportPreview = true;
            return;
        }

        PreviewBitmap = null;
        _currentExportPreview = null;
        IsPreviewPlaceholderVisible = true;
        PreviewMessage = "No visible raster layer is available.";
        CanExportPreview = false;
    }

    private static Bitmap? TryCreatePreviewBitmap(PsdMergedPreview preview)
    {
        try
        {
            return PsdPreviewBitmapFactory.Create(preview);
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? TryCreateThumbnailBitmap(PsdLayerPixelData pixelData)
    {
        try
        {
            return PsdPreviewBitmapFactory.CreateThumbnail(pixelData);
        }
        catch
        {
            return null;
        }
    }

    private void UpdateSelectionSummary()
    {
        SelectionSummary = $"{VisibleLayerCount} visible, {SelectedLayerCount} selected";
        NotifyBrowserCountsChanged();
    }

    private void ApplySelectionHint(PsdLayerSelectionHint? hint)
    {
        if (hint is null)
        {
            return;
        }

        var selection = _selectionResolver.ResolveSelectionHint(_snapshot, hint);
        IsMultiSelect = selection.SelectedLayerIds.Count > 1;
        ApplyVisibleSelectionAndPreview(selection.VisibleLayerIds, selection.SelectedLayerIds);
        StatusText = $"Restored {selection.SelectedLayerIds.Count} layer selection";
    }

    private void NotifyBrowserCountsChanged()
    {
        OnPropertyChanged(nameof(VisibleLayerCount));
        OnPropertyChanged(nameof(SelectedLayerCount));
        OnPropertyChanged(nameof(CurrentGroupItemCount));
    }

    private void SyncUiProperties()
    {
        BreadcrumbText = _layerUi.BreadcrumbText;
        OnPropertyChanged(nameof(LayerCount));
        NotifyBrowserCountsChanged();
    }

    private PsdMergedPreview CreateTransparentPreview()
    {
        var width = Math.Max(1, _snapshot.Document.Width);
        var height = Math.Max(1, _snapshot.Document.Height);
        return new PsdMergedPreview(width, height, new byte[checked(width * height * 4)]);
    }

    private static string ResolvePath(string projectRoot, string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(Path.Combine(projectRoot, path));
    }

    private static string BuildOutputPath(string directory, string fileNameHint)
    {
        var fileName = Path.GetFileName(fileNameHint);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "psd-export.png";
        }

        if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".png";
        }

        return Path.Combine(directory, fileName);
    }
}
