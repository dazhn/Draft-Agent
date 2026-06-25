using System.Text.Json.Serialization;

namespace DraftAgent.Core;

public sealed record EditingTimeline(
    string Id,
    string Title,
    double FramesPerSecond,
    IReadOnlyList<EditingTrack> Tracks,
    IReadOnlyList<EditingMarker> Markers,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record EditingTrack(
    string Id,
    EditingTrackKind Kind,
    string Name,
    IReadOnlyList<EditingClip> Clips,
    IReadOnlyDictionary<string, string>? Metadata = null);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EditingTrackKind
{
    Video,
    Audio,
    Subtitle
}

public sealed record EditingClip(
    string Id,
    string Name,
    double StartSeconds,
    double DurationSeconds,
    string? MediaAssetId,
    string? MediaPath,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record EditingMarker(
    string Id,
    string Name,
    double StartSeconds,
    double DurationSeconds,
    string Color,
    IReadOnlyDictionary<string, string> Metadata);
