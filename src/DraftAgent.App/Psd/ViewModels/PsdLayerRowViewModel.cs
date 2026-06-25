namespace DraftAgent.App.Psd.ViewModels;

public sealed record PsdLayerRowViewModel(
    int LayerId,
    string Name,
    string Kind,
    bool IsVisible,
    string Bounds);

