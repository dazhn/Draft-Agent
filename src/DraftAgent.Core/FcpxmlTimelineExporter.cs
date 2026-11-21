using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace DraftAgent.Core;

public sealed class FcpxmlTimelineExporter : ITimelineExporter
{
    private const int SubtitleLane = 1;
    private const int CharacterLane = 2;
    private const int CharacterDialogueVoiceLane = 3;
    private const int CharacterThoughtVoiceLane = 4;
    private const int CharacterActionVoiceLane = 5;
    private const int NarrationSceneEffectLane = 6;
    private const int NarrationSceneEffectVoiceLane = 7;
    private const int SceneLane = 8;

    private readonly IWorkflowLogger _logger;

    public FcpxmlTimelineExporter(IWorkflowLogger? logger = null)
    {
        _logger = logger ?? new InMemoryWorkflowLogger();
    }

    public async Task<TimelineExportResult> ExportAsync(TimelineExportContext context, CancellationToken cancellationToken = default)
    {
        if (context.Format != EditingProjectFormat.Fcpxml)
        {
            throw new NotSupportedException($"{context.Format} 导出适配器不支持 FCPXML。");
        }

        var outputPath = ResolveOutputPath(context, ".fcpxml");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var subtitleDirectory = Path.Combine(Path.GetDirectoryName(outputPath)!, "srt");
        var subtitleExports = TimelineSubtitleExporter.ExportSubtitleTrack(context.Timeline, subtitleDirectory);
        var referencedAssets = BuildAssetResources(context.Timeline);
        var xml = BuildFcpxml(context, referencedAssets, subtitleExports);
        await File.WriteAllTextAsync(outputPath, xml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);

        _logger.Info(
            WorkflowSteps.TimelineExport,
            "fcpxml_export_completed",
            "FCPXML 时间线导出完成。",
            new Dictionary<string, string>
            {
                ["outputPath"] = outputPath,
                ["assetCount"] = referencedAssets.Count.ToString(CultureInfo.InvariantCulture),
                ["subtitleCount"] = subtitleExports.Count.ToString(CultureInfo.InvariantCulture)
            });

        return new TimelineExportResult("fcpxml", outputPath, referencedAssets.Select(asset => asset.Path).Concat(subtitleExports.Select(item => item.OutputPath)).ToArray(), "FCPXML 与逐段 SRT 已导出。");
    }

    private static string BuildFcpxml(
        TimelineExportContext context,
        IReadOnlyList<FcpxmlAssetResource> referencedAssets,
        IReadOnlyList<ClipSubtitleExport> subtitleExports)
    {
        var anchoredItems = new List<XElement>();
        var visualTracks = OrderVideoTracks(context.Timeline.Tracks).ToArray();
        foreach (var visualTrack in visualTracks)
        {
            var lane = VideoLane(visualTrack);
            foreach (var clip in visualTrack.Clips.OrderBy(clip => clip.StartSeconds))
            {
                var refId = AssetId(referencedAssets, clip.MediaPath);
                var laneAttribute = LaneAttribute(lane);
                if (refId == MissingReferenceId)
                {
                    anchoredItems.Add(new XElement(
                        "gap",
                        new XAttribute("name", clip.Name),
                        laneAttribute,
                        new XAttribute("offset", Time(clip.StartSeconds)),
                        new XAttribute("start", "0s"),
                        new XAttribute("duration", Time(clip.DurationSeconds))));
                }
                else
                {
                    anchoredItems.Add(new XElement(
                        "asset-clip",
                        new XAttribute("name", clip.Name),
                        new XAttribute("ref", refId),
                        laneAttribute,
                        new XAttribute("offset", Time(clip.StartSeconds)),
                        new XAttribute("duration", Time(clip.DurationSeconds)),
                        new XAttribute("start", "0s")));
                }
            }
        }

        foreach (var track in context.Timeline.Tracks.Where(track => track.Kind == EditingTrackKind.Audio))
        {
            var audioLaneAttribute = LaneAttribute(AudioLane(track));
            foreach (var clip in track.Clips.OrderBy(clip => clip.StartSeconds).ThenBy(clip => clip.Id, StringComparer.Ordinal))
            {
                var refId = AssetId(referencedAssets, clip.MediaPath);
                if (refId != MissingReferenceId)
                {
                    anchoredItems.Add(new XElement(
                        "asset-clip",
                        new XAttribute("name", clip.Name),
                        new XAttribute("ref", refId),
                        audioLaneAttribute,
                        new XAttribute("offset", Time(clip.StartSeconds)),
                        new XAttribute("duration", Time(clip.DurationSeconds)),
                        new XAttribute("start", "0s"),
                        new XAttribute("audioRole", AudioRole(clip))));
                }
            }
        }

        foreach (var subtitle in subtitleExports)
        {
            var clip = context.Timeline.Tracks.SelectMany(track => track.Clips).First(candidate => candidate.Id == subtitle.ClipId);
            foreach (var cue in subtitle.Cues)
            {
                anchoredItems.Add(new XElement(
                    "caption",
                    new XAttribute("name", cue.Text),
                    new XAttribute("lane", SubtitleLane.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("offset", Time(clip.StartSeconds + cue.StartSeconds)),
                    new XAttribute("duration", Time(cue.DurationSeconds)),
                    new XAttribute("start", "0s"),
                    new XAttribute("role", "ITT?captionFormat=ITT.zh-CN"),
                    new XElement(
                        "text",
                        new XAttribute("placement", "bottom"),
                        new XElement(
                            "text-style",
                            new XAttribute("ref", "ts_subtitle"),
                            cue.Text)),
                    new XElement(
                        "text-style-def",
                        new XAttribute("id", "ts_subtitle"),
                        new XElement(
                            "text-style",
                            new XAttribute("font", ".AppleSystemUIFont"),
                            new XAttribute("fontSize", "13"),
                            new XAttribute("fontFace", "Regular"),
                            new XAttribute("fontColor", "1 1 1 1"),
                            new XAttribute("backgroundColor", "0 0 0 1")))));
            }
        }

        var resources = new XElement(
            "resources",
            new XElement(
                "format",
                new XAttribute("id", TimelineFormatId),
                new XAttribute("name", "FFVideoFormat1080p2398"),
                new XAttribute("frameDuration", FcpFrameDuration),
                new XAttribute("fieldOrder", "progressive"),
                new XAttribute("width", "1920"),
                new XAttribute("height", "1080"),
                new XAttribute("colorSpace", "1-1-1 (Rec. 709)")),
            new XElement(
                "format",
                new XAttribute("id", ImageFormatId),
                new XAttribute("name", "DraftAgentImageFormat1080p"),
                new XAttribute("fieldOrder", "progressive"),
                new XAttribute("width", "1920"),
                new XAttribute("height", "1080"),
                new XAttribute("colorSpace", "1-1-1 (Rec. 709)")),
            referencedAssets.Select(BuildAssetElement));

        var sequenceDuration = context.Timeline.Tracks
            .SelectMany(track => track.Clips)
            .Select(clip => clip.StartSeconds + clip.DurationSeconds)
            .DefaultIfEmpty(0)
            .Max();
        var primaryStoryline = new XElement(
            "gap",
            new XAttribute("name", "Draft Agent Timeline"),
            new XAttribute("offset", "0s"),
            new XAttribute("start", "0s"),
            new XAttribute("duration", Time(sequenceDuration)),
            anchoredItems);
        var spine = new XElement("spine", primaryStoryline);
        var root = new XElement(
            "fcpxml",
            new XAttribute("version", "1.10"),
            resources,
            new XElement(
                "library",
                new XElement(
                    "event",
                    new XAttribute("name", context.Timeline.Title),
                    new XElement(
                        "project",
                        new XAttribute("name", context.Timeline.Title),
                        new XElement(
                            "sequence",
                            new XAttribute("format", TimelineFormatId),
                            new XAttribute("duration", Time(sequenceDuration)),
                            new XAttribute("tcStart", "0s"),
                            new XAttribute("tcFormat", "NDF"),
                            new XAttribute("audioLayout", "stereo"),
                            new XAttribute("audioRate", "48k"),
                            spine)))));

        return string.Join(
            Environment.NewLine,
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>",
            "<!DOCTYPE fcpxml>",
            root.ToString()) + Environment.NewLine;
    }

    private static IEnumerable<EditingTrack> OrderVideoTracks(IEnumerable<EditingTrack> tracks)
    {
        var videoTracks = tracks.Where(track => track.Kind == EditingTrackKind.Video).ToArray();
        var primary = videoTracks.FirstOrDefault(IsSceneVisualTrack) ?? videoTracks.FirstOrDefault();
        if (primary is null)
        {
            yield break;
        }

        yield return primary;
        foreach (var track in videoTracks.Where(track => !ReferenceEquals(track, primary)))
        {
            yield return track;
        }
    }

    private static bool IsSceneVisualTrack(EditingTrack track)
    {
        return track.Metadata?.TryGetValue("visualRole", out var role) == true &&
            string.Equals(role, "scene", StringComparison.OrdinalIgnoreCase);
    }

    private static int VideoLane(EditingTrack track)
    {
        if (IsSceneVisualTrack(track))
        {
            return SceneLane;
        }

        return IsNarrationSceneEffectVisualTrack(track) ? NarrationSceneEffectLane : CharacterLane;
    }

    private static bool IsNarrationSceneEffectVisualTrack(EditingTrack track)
    {
        return track.Metadata?.TryGetValue("visualRole", out var role) == true &&
            string.Equals(role, "narration_scene_effect", StringComparison.OrdinalIgnoreCase);
    }

    private static int AudioLane(EditingTrack track)
    {
        if (track.Metadata is not null && track.Metadata.TryGetValue("trackRole", out var role))
        {
            return role.Trim().ToLowerInvariant() switch
            {
                "character_dialogue_voice" => CharacterDialogueVoiceLane,
                "character_thought_voice" => CharacterThoughtVoiceLane,
                "character_action_voice" => CharacterActionVoiceLane,
                "narration_scene_effect_voice" => NarrationSceneEffectVoiceLane,
                _ => CharacterDialogueVoiceLane
            };
        }

        return CharacterDialogueVoiceLane;
    }

    private static XAttribute? LaneAttribute(int? lane)
    {
        return lane is null
            ? null
            : new XAttribute("lane", lane.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static string ResolveOutputPath(TimelineExportContext context, string extension)
    {
        var outputPath = context.OutputPath;
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            outputPath = Path.Combine(context.Workspace.Folders.Exports, $"{SanitizeFileName(context.Timeline.Title)}{extension}");
        }

        if (!Path.HasExtension(outputPath))
        {
            outputPath = Path.Combine(outputPath, $"{SanitizeFileName(context.Timeline.Title)}{extension}");
        }

        return Path.ChangeExtension(outputPath, extension);
    }

    private const string MissingReferenceId = "missing";

    private static IReadOnlyList<FcpxmlAssetResource> BuildAssetResources(EditingTimeline timeline)
    {
        var assets = new List<FcpxmlAssetResource>();
        var index = 1;
        foreach (var item in timeline.Tracks
                     .SelectMany(track => track.Clips.Select(clip => new { track.Kind, clip.MediaPath, clip.DurationSeconds }))
                     .Where(item => !string.IsNullOrWhiteSpace(item.MediaPath))
                     .GroupBy(item => item.MediaPath!, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Min(item => item.MediaPath), StringComparer.OrdinalIgnoreCase))
        {
            var kind = item.Any(clip => clip.Kind == EditingTrackKind.Audio)
                ? EditingTrackKind.Audio
                : EditingTrackKind.Video;
            var durationSeconds = item
                .Where(clip => clip.Kind == kind)
                .Select(clip => clip.DurationSeconds)
                .DefaultIfEmpty(0)
                .Max();
            assets.Add(new FcpxmlAssetResource($"r_asset_{index}", item.Key, kind, durationSeconds));
            index++;
        }

        return assets;
    }

    private static string AssetId(IReadOnlyList<FcpxmlAssetResource> assets, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return MissingReferenceId;
        }

        return assets.FirstOrDefault(asset => string.Equals(asset.Path, path, StringComparison.OrdinalIgnoreCase))?.Id ?? MissingReferenceId;
    }

    private static string AudioRole(EditingClip clip)
    {
        if (clip.Metadata.TryGetValue("audioRole", out var role) &&
            !string.IsNullOrWhiteSpace(role))
        {
            return role;
        }

        return clip.Metadata.TryGetValue("interactionType", out var interactionType) &&
            (interactionType.Contains("Narration", StringComparison.OrdinalIgnoreCase) ||
                interactionType.Contains("InnerMonologue", StringComparison.OrdinalIgnoreCase))
            ? "voiceover"
            : "dialogue";
    }

    private const string TimelineFormatId = "r_format_timeline";
    private const string ImageFormatId = "r_format_image";
    private const int FcpTimebase = 24000;
    private const int FcpFrameDurationNumerator = 1001;
    private const double FcpFrameRate = 24000d / 1001d;
    private const string FcpFrameDuration = "1001/24000s";

    private static XElement BuildAssetElement(FcpxmlAssetResource asset)
    {
        var fileName = Path.GetFileName(asset.Path);
        var uid = AssetUid(asset.Path);
        if (asset.Kind == EditingTrackKind.Audio)
        {
            var audioProperties = ReadAudioProperties(asset.Path);
            return new XElement(
                "asset",
                new XAttribute("id", asset.Id),
                new XAttribute("name", fileName),
                new XAttribute("uid", uid),
                new XAttribute("start", "0s"),
                new XAttribute("duration", Time(asset.DurationSeconds)),
                new XAttribute("hasAudio", "1"),
                new XAttribute("audioSources", "1"),
                new XAttribute("audioChannels", audioProperties.Channels.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("audioRate", audioProperties.SampleRate.ToString(CultureInfo.InvariantCulture)),
                new XElement(
                    "media-rep",
                    new XAttribute("kind", "original-media"),
                    new XAttribute("sig", uid),
                    new XAttribute("src", ToFileUri(asset.Path))));
        }

        return new XElement(
            "asset",
            new XAttribute("id", asset.Id),
            new XAttribute("name", fileName),
            new XAttribute("uid", uid),
            new XAttribute("start", "0s"),
            new XAttribute("duration", "0s"),
            new XAttribute("hasVideo", "1"),
            new XAttribute("format", ImageFormatId),
            new XAttribute("videoSources", "1"),
            new XElement(
                "media-rep",
                new XAttribute("kind", "original-media"),
                new XAttribute("sig", uid),
                new XAttribute("src", ToFileUri(asset.Path))));
    }

    private static string Time(double value)
    {
        if (value <= 0)
        {
            return "0s";
        }

        var frames = (long)Math.Round(value * FcpFrameRate, MidpointRounding.AwayFromZero);
        return $"{(frames * FcpFrameDurationNumerator).ToString(CultureInfo.InvariantCulture)}/{FcpTimebase.ToString(CultureInfo.InvariantCulture)}s";
    }

    private static string ToFileUri(string path)
    {
        return new Uri(Path.GetFullPath(path)).AbsoluteUri;
    }

    private static string AssetUid(string path)
    {
        var normalizedPath = Path.GetFullPath(path).ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));
        return Convert.ToHexString(hash, 0, 16);
    }

    private static AudioProperties ReadAudioProperties(string path)
    {
        if (!TryReadWavAudioProperties(path, out var properties))
        {
            return new AudioProperties(2, 48000);
        }

        return properties;
    }

    private static bool TryReadWavAudioProperties(string path, out AudioProperties properties)
    {
        properties = default;
        if (!string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
            if (ReadAscii(reader, 4) != "RIFF")
            {
                return false;
            }

            reader.ReadUInt32();
            if (ReadAscii(reader, 4) != "WAVE")
            {
                return false;
            }

            while (stream.Position + 8 <= stream.Length)
            {
                var chunkId = ReadAscii(reader, 4);
                var chunkSize = reader.ReadUInt32();
                if (chunkId == "fmt " && chunkSize >= 16)
                {
                    _ = reader.ReadUInt16();
                    var channels = reader.ReadUInt16();
                    var sampleRate = reader.ReadUInt32();
                    properties = new AudioProperties(channels, sampleRate);
                    return true;
                }

                stream.Seek(chunkSize + (chunkSize % 2), SeekOrigin.Current);
            }
        }
        catch (EndOfStreamException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }

        return false;
    }

    private static string ReadAscii(BinaryReader reader, int length)
    {
        return Encoding.ASCII.GetString(reader.ReadBytes(length));
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "draft-agent-timeline" : cleaned;
    }

    private readonly record struct AudioProperties(uint Channels, uint SampleRate);

    private sealed record FcpxmlAssetResource(string Id, string Path, EditingTrackKind Kind, double DurationSeconds);
}
