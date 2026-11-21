using System.Globalization;
using System.Text;

namespace DraftAgent.Core;

public static class TimelineSubtitleExporter
{
    public static IReadOnlyList<ClipSubtitleExport> ExportAudioClipSubtitles(
        EditingTimeline timeline,
        string outputDirectory)
    {
        return ExportSubtitleTrack(timeline, outputDirectory);
    }

    public static IReadOnlyList<ClipSubtitleExport> ExportSubtitleTrack(
        EditingTimeline timeline,
        string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var subtitleClips = timeline.Tracks
            .Where(track => track.Kind == EditingTrackKind.Subtitle)
            .SelectMany(track => track.Clips)
            .OrderBy(clip => clip.StartSeconds)
            .ThenBy(clip => clip.Id, StringComparer.Ordinal)
            .ToArray();
        return ExportSubtitleTrack(subtitleClips, outputDirectory);
    }

    private static IReadOnlyList<ClipSubtitleExport> ExportSubtitleTrack(
        IReadOnlyList<EditingClip> subtitleClips,
        string outputDirectory)
    {
        var exports = new List<ClipSubtitleExport>();
        foreach (var group in subtitleClips.GroupBy(SourceClipId, StringComparer.Ordinal))
        {
            var ordered = group
                .OrderBy(clip => clip.StartSeconds)
                .ThenBy(clip => clip.Id, StringComparer.Ordinal)
                .ToArray();
            if (ordered.Length == 0)
            {
                continue;
            }

            var first = ordered[0];
            var sourceClipId = SourceClipId(first);
            var sourceClipName = MetadataValue(first, "sourceClipName", sourceClipId);
            var originalText = MetadataValue(first, "originalText", string.Join(string.Empty, ordered.Select(SubtitleText)));
            var sourceStartSeconds = ordered.Min(clip => clip.StartSeconds);
            var cues = ordered
                .Select((clip, index) => new SubtitleCue(
                    index + 1,
                    Math.Round(clip.StartSeconds - sourceStartSeconds, 3),
                    Math.Round(Math.Max(0.001, clip.DurationSeconds), 3),
                    SubtitleText(clip)))
                .ToArray();
            var outputPath = Path.Combine(outputDirectory, $"{SanitizeFileName(sourceClipId)}.srt");
            File.WriteAllText(outputPath, ToSrt(cues), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            exports.Add(new ClipSubtitleExport(sourceClipId, sourceClipName, originalText, outputPath, cues));
        }

        return exports;
    }

    private static string SourceClipId(EditingClip clip)
    {
        return MetadataValue(clip, "sourceClipId", clip.Id);
    }

    private static string SubtitleText(EditingClip clip)
    {
        return MetadataValue(clip, "text", string.Empty);
    }

    private static string MetadataValue(EditingClip clip, string key, string fallback)
    {
        return clip.Metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }

    public static string ToSrt(IReadOnlyList<SubtitleCue> cues)
    {
        var builder = new StringBuilder();
        foreach (var cue in cues)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.AppendLine(cue.Index.ToString(CultureInfo.InvariantCulture));
            builder.Append(FormatTimestamp(cue.StartSeconds));
            builder.Append(" --> ");
            builder.AppendLine(FormatTimestamp(cue.StartSeconds + cue.DurationSeconds));
            builder.AppendLine(cue.Text);
        }

        return builder.ToString();
    }

    private static string FormatTimestamp(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00},{span.Milliseconds:000}");
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "subtitle" : cleaned;
    }
}
