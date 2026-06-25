using Avalonia.Controls;
using Avalonia.Interactivity;
using DraftAgent.App.ViewModels;

namespace DraftAgent.App.Views;

public partial class AiSettingsWindow : Window
{
    private bool _saved;

    public AiSettingsWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_saved && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CancelAiSettingsEdit();
        }

        base.OnClosing(e);
    }

    private void SaveAndCloseWindow(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CommitAiSettingsEdit();
            _saved = true;
        }

        Close();
    }

    private void CloseWindow(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
