using Avalonia.Controls;
using Avalonia.Interactivity;
using DraftAgent.App.ViewModels;

namespace DraftAgent.App.Views;

public partial class EmotionEditorWindow : Window
{
    public EmotionEditorWindow()
    {
        InitializeComponent();
    }

    private void CancelWindow(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SaveWindow(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EmotionEditorSessionViewModel session)
        {
            session.Save();
        }

        Close();
    }
}
