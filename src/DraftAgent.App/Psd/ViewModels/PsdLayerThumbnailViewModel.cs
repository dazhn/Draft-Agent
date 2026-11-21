using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DraftAgent.App.Psd.ViewModels;

public sealed partial class PsdLayerThumbnailViewModel : ObservableObject
{
    private const int HideOptionLayerId = int.MinValue;

    public PsdLayerThumbnailViewModel(
        int layerId,
        int? parentLayerId,
        string name,
        string kind,
        string bounds,
        int left,
        int top,
        int right,
        int bottom,
        bool isGroup,
        bool isDefaultVisible,
        bool isHideOption = false)
    {
        LayerId = layerId;
        ParentLayerId = parentLayerId;
        Name = name;
        Kind = kind;
        Bounds = bounds;
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
        IsGroup = isGroup;
        IsDefaultVisible = isDefaultVisible;
        IsHideOption = isHideOption;
        IsVisible = isDefaultVisible;
    }

    public static PsdLayerThumbnailViewModel CreateHideOption(int? parentLayerId)
    {
        return new PsdLayerThumbnailViewModel(
            HideOptionLayerId,
            parentLayerId,
            "\u9690\u85cf",
            "Hide",
            "\u4e0d\u663e\u793a\u56fe\u5c42",
            0,
            0,
            0,
            0,
            isGroup: false,
            isDefaultVisible: false,
            isHideOption: true);
    }

    public int LayerId { get; }

    public int? ParentLayerId { get; }

    public string Name { get; }

    public string Kind { get; }

    public string Bounds { get; }

    public int Left { get; }

    public int Top { get; }

    public int Right { get; }

    public int Bottom { get; }

    public bool IsGroup { get; }

    public bool IsDefaultVisible { get; }

    public bool IsHideOption { get; }

    public string LayerBadge => IsHideOption ? string.Empty : $"#{LayerId}";

    public string TypeBadge => IsHideOption ? "\u9690\u85cf" : IsGroup ? "\u7ec4" : "\u5c42";

    private bool _supportsExclusiveSwitch = true;

    public bool SupportsExclusiveSwitch
    {
        get => _supportsExclusiveSwitch;
        internal set
        {
            if (SetProperty(ref _supportsExclusiveSwitch, value))
            {
                OnPropertyChanged(nameof(CanToggleVisibility));
                OnPropertyChanged(nameof(ShowsVisibleDot));
            }
        }
    }

    public bool CanToggleVisibility => !IsGroup && !IsHideOption && !SupportsExclusiveSwitch;

    public bool ShowsVisibleDot => IsVisible && !CanToggleVisibility;

    public string VisibilityGlyph => IsVisible ? "👁" : "◌";

    [ObservableProperty]
    private bool _isVisible;

    partial void OnIsVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(VisibilityGlyph));
        OnPropertyChanged(nameof(ShowsVisibleDot));
    }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private Bitmap? _thumbnailBitmap;
}

