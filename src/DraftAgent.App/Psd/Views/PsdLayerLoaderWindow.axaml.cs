using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using System;
using System.Threading.Tasks;
using DraftAgent.App.Psd.Services;
using DraftAgent.App.Psd.ViewModels;

namespace DraftAgent.App.Psd.Views;

public partial class PsdLayerLoaderWindow : Window
{
    private bool _isLoadPsdPickerOpen;
    private bool _isExportPngPickerOpen;

    public PsdLayerLoaderWindow()
    {
        InitializeComponent();
    }

    public PsdInteractiveRequestSession? ComponentRequestSession { get; set; }

    public event EventHandler? ComponentRequestConfirmed;

    public event EventHandler? ComponentRequestCancelled;

    public Func<Task>? ConfigureLayerMatchAsync { get; set; }

    private async void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            DataContext is not PsdLayerLoaderViewModel viewModel)
        {
            return;
        }

        var exportPath = await viewModel.PrepareCurrentPreviewDragFileAsync();
        if (string.IsNullOrWhiteSpace(exportPath))
        {
            return;
        }

        var storageFile = await StorageProvider.TryGetFileFromPathAsync(new Uri(exportPath));
        if (storageFile is null)
        {
            return;
        }

        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateFile(storageFile));
        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);
        e.Handled = true;
    }

    private async void OnLoadPsdClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isLoadPsdPickerOpen)
        {
            return;
        }

        _isLoadPsdPickerOpen = true;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose PSD file",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Photoshop PSD")
                    {
                        Patterns = ["*.psd", "*.psb"]
                    }
                ]
            });

            if (files.Count == 0 || DataContext is not PsdLayerLoaderViewModel viewModel)
            {
                return;
            }

            var path = files[0].TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path))
            {
                await viewModel.LoadPsdAsync(path);
            }
        }
        finally
        {
            _isLoadPsdPickerOpen = false;
        }
    }

    private async void OnExportPngClicked(object? sender, RoutedEventArgs e)
    {
        if (_isExportPngPickerOpen)
        {
            return;
        }

        if (DataContext is not PsdLayerLoaderViewModel viewModel)
        {
            return;
        }

        _isExportPngPickerOpen = true;
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export PNG",
                SuggestedFileName = viewModel.SuggestedExportFileName,
                FileTypeChoices =
                [
                    new FilePickerFileType("PNG image")
                    {
                        Patterns = ["*.png"],
                        MimeTypes = ["image/png"]
                    }
                ]
            });

            var path = file?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path))
            {
                await viewModel.ExportCurrentPreviewAsync(path);
            }
        }
        finally
        {
            _isExportPngPickerOpen = false;
        }
    }

    private void OnSingleModeClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PsdLayerLoaderViewModel viewModel)
        {
            viewModel.SelectSingleMode();
        }
    }

    private void OnMultiModeClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PsdLayerLoaderViewModel viewModel)
        {
            viewModel.SelectMultiMode();
        }
    }

    private void OnGroupTabClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: PsdBrowserTabViewModel tab } &&
            DataContext is PsdLayerLoaderViewModel viewModel)
        {
            viewModel.SelectGroupTab(tab);
        }
    }

    private void OnGroupExpandClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: PsdBrowserTabViewModel tab } &&
            DataContext is PsdLayerLoaderViewModel viewModel)
        {
            viewModel.ToggleGroupExpanded(tab);
            e.Handled = true;
        }
    }

    private void OnNavigateUpClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PsdLayerLoaderViewModel viewModel)
        {
            viewModel.NavigateUp();
        }
    }

    private void OnLayerThumbnailClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: PsdLayerThumbnailViewModel thumbnail } &&
            DataContext is PsdLayerLoaderViewModel viewModel)
        {
            viewModel.ToggleLayer(thumbnail);
        }
    }

    private void OnLayerVisibilityClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: PsdLayerThumbnailViewModel thumbnail } &&
            DataContext is PsdLayerLoaderViewModel viewModel)
        {
            viewModel.ToggleLayerVisibility(thumbnail);
            e.Handled = true;
        }
    }

    private void OnLayerThumbnailDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: PsdLayerThumbnailViewModel thumbnail } &&
            DataContext is PsdLayerLoaderViewModel viewModel)
        {
            viewModel.OpenLayerGroup(thumbnail);
        }
    }

    private async void OnConfigureLayerMatchClicked(object? sender, RoutedEventArgs e)
    {
        if (ConfigureLayerMatchAsync is not null)
        {
            await ConfigureLayerMatchAsync();
        }
    }

    private async void OnConfirmComponentRequestClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PsdLayerLoaderViewModel viewModel)
        {
            return;
        }

        if (ComponentRequestSession is not null)
        {
            await ComponentRequestSession.ConfirmAsync(viewModel);
        }
        else
        {
            ComponentRequestConfirmed?.Invoke(this, EventArgs.Empty);
        }

        Close(true);
    }

    private async void OnCancelComponentRequestClicked(object? sender, RoutedEventArgs e)
    {
        if (ComponentRequestSession is not null)
        {
            await ComponentRequestSession.CancelAsync();
        }
        else
        {
            ComponentRequestCancelled?.Invoke(this, EventArgs.Empty);
        }

        Close(false);
    }
}

