using System.Text.Json.Serialization;

namespace DraftAgent.Core;

public sealed record WorkspaceProject(
    string ProjectId,
    string Name,
    string RootPath,
    string ManifestPath,
    string DraftPath,
    string OriginalAiJsonPath,
    ProjectFolderLayout Folders,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ProjectFolderLayout(
    string Assets,
    string Images,
    string ImagesPsd,
    string ImagesImported,
    string Audio,
    string AudioVoiceReferences,
    string AudioGenerated,
    string Otio,
    string Exports,
    string Logs);

public sealed record WorkspaceProjectManifest(
    string Schema,
    string ProjectId,
    string Name,
    string RootPath,
    string DraftPath,
    string OriginalAiJsonPath,
    ProjectFolderLayout Folders,
    IReadOnlyList<ProjectMediaAsset> Assets,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ProjectMediaAsset(
    string Id,
    MediaAssetKind Kind,
    MediaAssetSource Source,
    string DisplayName,
    string AbsolutePath,
    string ProjectRelativePath,
    string? TargetId,
    string? SourcePath,
    string? Provider,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset CreatedAt);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MediaAssetKind
{
    Image,
    Psd,
    Audio,
    Video,
    Otio,
    Other
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MediaAssetSource
{
    ImportedPng,
    ImportedPsd,
    VoiceReference,
    TtsGenerated,
    Manual
}

public sealed record MediaImportRequest(
    string SourcePath,
    MediaAssetKind Kind,
    MediaAssetSource Source,
    string? TargetId = null,
    string? DisplayName = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record MediaImportResult(ProjectMediaAsset Asset, WorkspaceProjectManifest Manifest);

public sealed record GeneratedAssetImportResult(
    InteractionProject Project,
    WorkspaceProjectManifest Manifest,
    int ImportedCount,
    IReadOnlyList<ProjectMediaAsset> ImportedAssets);

public sealed record TimelineExportOptions(
    double FramesPerSecond = 24,
    double DefaultStillDurationSeconds = 4,
    bool UseRelativeMediaReferences = true,
    bool AllowMissingVisuals = false,
    bool RequireAudioForTextRows = false);

public sealed record TimelineExportResult(
    string Format,
    string OutputPath,
    IReadOnlyList<string> ReferencedAssetPaths,
    string Message);

public sealed record EditingProjectExportRequest(
    InteractionProject Project,
    WorkspaceProject Workspace,
    EditingProjectFormat Format,
    TimelineExportOptions Options,
    WorkspaceProjectManifest? Manifest = null,
    string? OutputPath = null,
    bool AllowMissingVisuals = false);

public sealed record ClipSubtitleExport(
    string ClipId,
    string ClipName,
    string Text,
    string OutputPath,
    IReadOnlyList<SubtitleCue> Cues);

public sealed record SubtitleCue(
    int Index,
    double StartSeconds,
    double DurationSeconds,
    string Text);

public sealed record ProjectAssetResolution(
    string OriginalReference,
    string AbsolutePath,
    ProjectMediaAsset? Asset,
    string ResolutionKind);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EditingProjectFormat
{
    Otio,
    Fcpxml,
    JianyingDraft,
    AnimateTimeline,
    DaVinciResolve,
    PremierePro,
    FinalCutPro
}
