namespace DraftAgent.Core;

public enum VisualSourceKind
{
    None,
    Image,
    Psd
}

public enum VisualExportBounds
{
    LayerBounds,
    CanvasBounds,
    PreviewBounds
}

public sealed record VisualAssetBinding(
    string? SourceAssetId,
    string? RenderedAssetId,
    VisualSourceKind SourceKind,
    PsdRenderSelection? PsdSelection = null,
    PsdLayerMatchConfiguration? PsdLayerMatch = null)
{
    public static VisualAssetBinding None { get; } = new(null, null, VisualSourceKind.None);

    public static VisualAssetBinding FromImage(string? assetId)
    {
        var normalized = Normalize(assetId);
        return string.IsNullOrWhiteSpace(normalized)
            ? None
            : new VisualAssetBinding(normalized, normalized, VisualSourceKind.Image);
    }

    public static VisualAssetBinding FromPsd(
        string? psdAssetId,
        string? renderedAssetId = null,
        PsdRenderSelection? selection = null,
        PsdLayerMatchConfiguration? layerMatch = null)
    {
        var normalizedSource = Normalize(psdAssetId);
        return string.IsNullOrWhiteSpace(normalizedSource)
            ? None
            : new VisualAssetBinding(normalizedSource, Normalize(renderedAssetId), VisualSourceKind.Psd, selection, layerMatch);
    }

    public string? EffectiveAssetId => Normalize(RenderedAssetId) ?? Normalize(SourceAssetId);

    public bool HasRenderedAsset => !string.IsNullOrWhiteSpace(RenderedAssetId);

    public bool IsPsd => SourceKind == VisualSourceKind.Psd;

    public bool IsImage => SourceKind == VisualSourceKind.Image;

    public VisualAssetBinding WithRenderedAsset(string? renderedAssetId, PsdRenderSelection? selection = null)
    {
        return this with
        {
            RenderedAssetId = Normalize(renderedAssetId),
            PsdSelection = selection ?? PsdSelection
        };
    }

    public static bool SemanticEquals(
        VisualAssetBinding? left,
        VisualAssetBinding? right)
    {
        var leftNormalized = NormalizeForComparison(left);
        var rightNormalized = NormalizeForComparison(right);
        return string.Equals(leftNormalized.SourceAssetId, rightNormalized.SourceAssetId, StringComparison.Ordinal) &&
            string.Equals(leftNormalized.RenderedAssetId, rightNormalized.RenderedAssetId, StringComparison.Ordinal) &&
            leftNormalized.SourceKind == rightNormalized.SourceKind &&
            PsdSelectionsEqual(leftNormalized.PsdSelection, rightNormalized.PsdSelection) &&
            leftNormalized.PsdLayerMatch == rightNormalized.PsdLayerMatch;
    }

    private static VisualAssetBinding NormalizeForComparison(VisualAssetBinding? binding)
    {
        if (binding is not null)
        {
            return binding with
            {
                SourceAssetId = Normalize(binding.SourceAssetId),
                RenderedAssetId = Normalize(binding.RenderedAssetId)
            };
        }

        return None;
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool PsdSelectionsEqual(PsdRenderSelection? left, PsdRenderSelection? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.PresetId == right.PresetId &&
            left.Emotion == right.Emotion &&
            left.Action == right.Action &&
            left.Bounds == right.Bounds &&
            left.SourceFingerprint == right.SourceFingerprint &&
            left.RenderCacheKey == right.RenderCacheKey &&
            SequenceEqual(left.LayerIds, right.LayerIds) &&
            SequenceEqual(left.LayerNames, right.LayerNames) &&
            SequenceEqual(left.ControlledLayerNames ?? [], right.ControlledLayerNames ?? []);
    }

    private static bool SequenceEqual<T>(IReadOnlyList<T> left, IReadOnlyList<T> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        var comparer = EqualityComparer<T>.Default;
        for (var index = 0; index < left.Count; index++)
        {
            if (!comparer.Equals(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

}

public sealed record PsdRenderSelection(
    IReadOnlyList<int> LayerIds,
    IReadOnlyList<string> LayerNames,
    string? PresetId,
    string? Emotion,
    string? Action,
    VisualExportBounds Bounds,
    string SourceFingerprint,
    string RenderCacheKey,
    IReadOnlyList<string>? ControlledLayerNames = null,
    IReadOnlyList<int>? ControlledLayerIds = null);

public sealed record PsdLayerMatchConfiguration(
    PsdLayerGroupCatalog? ExpressionGroup = null,
    PsdLayerGroupCatalog? BrowGroup = null,
    PsdLayerGroupCatalog? EyeGroup = null,
    PsdLayerGroupCatalog? MouthGroup = null,
    PsdLayerGroupCatalog? ActionGroup = null,
    PsdLayerGroupCatalog? LeftHandGroup = null,
    PsdLayerGroupCatalog? RightHandGroup = null)
{
    public bool HasAnyGroup =>
        HasGroup(ExpressionGroup) ||
        HasGroup(BrowGroup) ||
        HasGroup(EyeGroup) ||
        HasGroup(MouthGroup) ||
        HasGroup(ActionGroup) ||
        HasGroup(LeftHandGroup) ||
        HasGroup(RightHandGroup);

    private static bool HasGroup(PsdLayerGroupCatalog? group)
    {
        return group is not null &&
            (!string.IsNullOrWhiteSpace(group.GroupName) || group.LayerNames.Count > 0);
    }
}

public sealed record PsdLayerGroupCatalog(
    string GroupName,
    IReadOnlyList<string> LayerNames);

public sealed record VoiceAssetBinding(
    string? VoiceReferenceId,
    string? ReferenceText,
    string? SourceAssetId = null,
    string? SyncedVoicePath = null)
{
    public static VoiceAssetBinding None { get; } = new(null, null);

    public static VoiceAssetBinding FromProjectReference(
        string? voiceReferenceId,
        string? referenceText,
        string? sourceAssetId,
        string? syncedVoicePath)
    {
        var normalizedVoiceReferenceId = Normalize(voiceReferenceId);
        var normalizedReferenceText = Normalize(referenceText);
        var normalizedSourceAssetId = Normalize(sourceAssetId);
        var normalizedSyncedVoicePath = Normalize(syncedVoicePath);
        return string.IsNullOrWhiteSpace(normalizedVoiceReferenceId) &&
            string.IsNullOrWhiteSpace(normalizedReferenceText) &&
            string.IsNullOrWhiteSpace(normalizedSourceAssetId) &&
            string.IsNullOrWhiteSpace(normalizedSyncedVoicePath)
                ? None
                : new VoiceAssetBinding(
                    normalizedVoiceReferenceId,
                    normalizedReferenceText,
                    normalizedSourceAssetId,
                    normalizedSyncedVoicePath);
    }

    public string? EffectiveVoiceReferenceId => Normalize(VoiceReferenceId);

    public string? EffectiveReferenceText => Normalize(ReferenceText);

    public static bool SemanticEquals(
        VoiceAssetBinding? left,
        VoiceAssetBinding? right)
    {
        var leftNormalized = NormalizeForComparison(left);
        var rightNormalized = NormalizeForComparison(right);
        return leftNormalized == rightNormalized;
    }

    private static VoiceAssetBinding NormalizeForComparison(VoiceAssetBinding? binding)
    {
        if (binding is not null)
        {
            return binding with
            {
                VoiceReferenceId = Normalize(binding.VoiceReferenceId),
                ReferenceText = Normalize(binding.ReferenceText),
                SourceAssetId = Normalize(binding.SourceAssetId),
                SyncedVoicePath = Normalize(binding.SyncedVoicePath)
            };
        }

        return None;
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
