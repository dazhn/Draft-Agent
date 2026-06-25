using CommunityToolkit.Mvvm.ComponentModel;

namespace DraftAgent.App.ViewModels;

public sealed partial class ScriptDraftTabViewModel : ObservableObject
{
    public ScriptDraftTabViewModel(string title, string text = "", bool canClose = true)
    {
        Title = title;
        Text = text;
        CanClose = canClose;
    }

    public string Title { get; }

    public bool CanClose { get; }

    [ObservableProperty]
    private string _text;
}
