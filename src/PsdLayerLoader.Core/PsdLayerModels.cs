namespace PsdLayerLoader.Core;

public enum PsdLayerKind
{
    Raster,
    Group,
    Text,
    SmartObject,
    Adjustment,
    Unknown
}

public sealed record PsdLayerNode(
    int LayerId,
    int? ParentLayerId,
    string Name,
    PsdLayerKind Kind,
    bool IsGroup,
    bool IsVisibleByDefault,
    int Left,
    int Top,
    int Width,
    int Height,
    IReadOnlyList<PsdLayerNode> Children)
{
    public IEnumerable<PsdLayerNode> Flatten()
    {
        yield return this;
        foreach (var child in Children)
        {
            foreach (var descendant in child.Flatten())
            {
                yield return descendant;
            }
        }
    }
}

public sealed record PsdDocumentInfo(
    string DocumentId,
    string FilePath,
    string DisplayName,
    int Width,
    int Height,
    IReadOnlyList<PsdLayerNode> Layers)
{
    public IEnumerable<PsdLayerNode> FlattenLayers()
    {
        return Layers.SelectMany(layer => layer.Flatten());
    }
}

public sealed record PsdNativeDocumentInfo(
    int Width,
    int Height,
    int BitsPerChannel,
    int ChannelCount,
    int ColorMode,
    IReadOnlyList<PsdNativeLayerInfo> Layers);

public sealed record PsdNativeLayerInfo(
    int LayerId,
    int? ParentLayerId,
    string Name,
    int Type,
    bool IsGroup,
    bool IsVisible,
    int Left,
    int Top,
    int Right,
    int Bottom,
    int Width,
    int Height);

public sealed record PsdMergedPreview(
    int Width,
    int Height,
    byte[] Rgba);

public sealed record PsdLayerPixelData(
    int LayerId,
    int Left,
    int Top,
    int Width,
    int Height,
    byte[] Rgba);

public sealed record PsdLayerPreset(
    string PresetId,
    string DisplayName,
    string Emotion,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<int> LayerIds,
    PsdExportBounds ExportBounds);

public sealed record PsdLayerGroupSelection(
    int GroupLayerId,
    bool IsMultiSelect,
    IReadOnlySet<int> VisibleChildLayerIds);

public sealed record PsdSelectionState(
    string DocumentId,
    IReadOnlyDictionary<int, PsdLayerGroupSelection> GroupSelections,
    IReadOnlySet<int> VisibleLayerIds,
    IReadOnlySet<int> SelectedLayerIds,
    int? CurrentGroupLayerId);
