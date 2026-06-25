using CommunityToolkit.Mvvm.ComponentModel;

namespace DraftAgent.App.Psd.ViewModels;

public sealed partial class PsdBrowserTabViewModel : ObservableObject
{
    public PsdBrowserTabViewModel(
        int? groupLayerId,
        string displayName,
        string countSummary,
        string matchRoleText = "",
        int depth = 0,
        bool hasChildren = false,
        bool isExpanded = true)
    {
        GroupLayerId = groupLayerId;
        DisplayName = displayName;
        CountSummary = countSummary;
        MatchRoleText = matchRoleText;
        Depth = depth;
        HasChildren = hasChildren;
        IsExpanded = isExpanded;
    }

    public int? GroupLayerId { get; }

    public string DisplayName { get; }

    public string CountSummary { get; }

    public string MatchRoleText { get; }

    public bool HasMatchRole => !string.IsNullOrWhiteSpace(MatchRoleText);

    public int Depth { get; }

    public double IndentWidth => Depth * 16.0;

    public bool HasChildren { get; }

    public bool IsExpanded { get; }

    public string ExpandGlyph => HasChildren ? (IsExpanded ? "▾" : "▸") : string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}

