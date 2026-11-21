using System.Text.Json.Serialization;

namespace DraftAgent.Core;

public sealed record ScriptDocument(string RawText, string CleanText, IReadOnlyList<ScriptSegment> Segments);

public sealed record ScriptSegment(string Id, int Index, string Text, SegmentType Type);

public enum SegmentType
{
    Dialogue,
    Narration,
    Action
}

public sealed record Character(
    string Id,
    string Name,
    string Role,
    string Appearance,
    string VoiceStyle,
    string DefaultEmotion,
    AssetBinding AssetBinding);

public sealed record AssetBinding(
    string? Portrait = null,
    string? Voice = null,
    string? Costume = null,
    string? ActorClip = null,
    VisualAssetBinding? PortraitVisualBinding = null,
    VoiceAssetBinding? VoiceBinding = null);

public sealed record SceneAssetBinding(
    string? BackgroundImage = null,
    string? VideoClip = null,
    string? AmbientSound = null,
    string? Bgm = null,
    VisualAssetBinding? BackgroundVisualBinding = null);

public sealed record EmotionSpec(
    string Emotion,
    double Intensity,
    string Transition,
    string FacialExpression,
    string Tone,
    string BodySignal);

public sealed record ActionSpec(string MainAction, IReadOnlyList<string> MicroActions, string CameraAction, double Duration);

public sealed record DraftIssue(
    string Id,
    IssueSeverity Severity,
    string TargetType,
    string TargetId,
    string Message,
    string Suggestion,
    string? SourceShotId = null);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IssueSeverity
{
    Info,
    Warning,
    Error
}

public sealed record DraftProject(
    string ProjectId,
    string Title,
    string ContentType,
    ScriptDocument Script,
    DraftAnalysis Analysis,
    IReadOnlyList<Character> Characters,
    IReadOnlyList<Scene> Scenes,
    IReadOnlyList<Shot> Shots,
    Timeline Timeline,
    IReadOnlyList<DraftIssue> Issues);

public sealed record DraftAnalysis(string Summary, string MainConflict, IReadOnlyList<EmotionCurvePoint> EmotionCurve);

public sealed record EmotionCurvePoint(string ShotId, string Emotion, double Intensity);

public sealed record Scene(
    string Id,
    string Name,
    string Location,
    string TimeOfDay,
    string Weather,
    string Mood,
    string VisualStyle,
    IReadOnlyList<string> Props,
    SceneAssetBinding AssetBinding);

public sealed record Shot(
    string Id,
    IReadOnlyList<string> SourceSegmentIds,
    string SceneId,
    IReadOnlyList<string> CharacterIds,
    TimeRange TimeRange,
    VisualSpec Visual,
    IReadOnlyList<PerformanceSpec> Performance,
    DialogueSpec? Dialogue,
    AudioSpec Audio,
    string? Subtitle,
    ShotBinding Binding,
    string GenerationPrompt,
    string NegativePrompt,
    string VoicePrompt,
    string Status);

public sealed record TimeRange(double Start, double Duration);

public sealed record VisualSpec(string ShotType, string CameraMovement, string Composition, string Lighting);

public sealed record PerformanceSpec(string CharacterId, EmotionSpec Emotion, ActionSpec Action, string? DialogueId);

public sealed record DialogueSpec(
    string Id,
    string? CharacterId,
    string? Speaker,
    string Text,
    string Emotion,
    string Tone,
    double PauseBefore,
    double PauseAfter,
    SubtitleSpec Subtitle);

public sealed record SubtitleSpec(string Text, string Style, int MaxCharsPerLine);

public sealed record AudioSpec(string? Voice, string Bgm, IReadOnlyList<string> Sfx);

public sealed record ShotBinding(string? CharacterAsset, string? SceneAsset, string? VoiceAsset, string SubtitleStyle);

public sealed record Timeline(string Id, double Duration, IReadOnlyList<TimelineTrack> Tracks);

public sealed record TimelineTrack(string Id, string Type, IReadOnlyList<TimelineItem> Items);

public sealed record TimelineItem(string ShotId, double Start, double Duration, string? AssetId, string Status);
