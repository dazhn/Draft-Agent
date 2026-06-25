using CommunityToolkit.Mvvm.ComponentModel;

namespace DraftAgent.App.Psd.ViewModels;

public sealed partial class PsdDocumentTabViewModel : ObservableObject
{
    public PsdDocumentTabViewModel(string displayName, string subtitle, bool isPlaceholder = false)
    {
        DisplayName = displayName;
        Subtitle = subtitle;
        IsPlaceholder = isPlaceholder;
        IsEnabled = !isPlaceholder;
    }

    public string DisplayName { get; }

    public string Subtitle { get; }

    public bool IsPlaceholder { get; }

    public bool IsEnabled { get; }

    [ObservableProperty]
    private bool _isActive;
}

