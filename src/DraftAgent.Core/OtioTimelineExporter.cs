using System.Globalization;
using System.Text.Json.Nodes;

namespace DraftAgent.Core;

public interface ITimelineExporter
{
    Task<TimelineExportResult> ExportAsync(TimelineExportContext context, CancellationToken cancellationToken = default);
}

public sealed record TimelineExportContext(
    EditingTimeline Timeline,
    WorkspaceProject Workspace,
    EditingProjectFormat Format,
    TimelineExportOptions Options,
    string? OutputPath = null);

public sealed class OtioTimelineExporter : ITimelineExporter
{
    private readonly IWorkflowLogger _logger;

    public OtioTimelineExporter(IWorkflowLogger? logger = null)
    {
        _logger = logger ?? new InMemoryWorkflowLogger();
    }

    public async Task<TimelineExportResult> ExportAsync(TimelineExportContext context, CancellationToken cancellationToken = default)
    {
        if (context.Format != EditingProjectFormat.Otio)
        {
            throw new NotSupportedException($"{context.Format} 导出适配器尚未实现；请导出 FCPXML、剪映草稿，或使用 Core 内部 OTIO 兼容路径。");
        }

        try
        {
            _logger.Info(
                WorkflowSteps.TimelineExport,
                "otio_export_started",
                "开始导出 OTIO 时间线。",
                new Dictionary<string, string>
                {
                    ["timelineId"] = context.Timeline.Id,
                    ["workspaceRoot"] = context.Workspace.RootPath,
                    ["fps"] = context.Timeline.FramesPerSecond.ToString(CultureInfo.InvariantCulture),
                    ["trackCount"] = context.Timeline.Tracks.Count.ToString(CultureInfo.InvariantCulture)
                });

            WorkspaceProjectManager.EnsureFolders(context.Workspace.Folders);
            var timelineJson = BuildTimeline(context, out var referencedAssetPaths);
            var outputPath = ResolveOutputPath(context);
            await File.WriteAllTextAsync(outputPath, timelineJson.ToJsonString(DraftJson.Options), cancellationToken);
            _logger.Info(
                WorkflowSteps.TimelineExport,
                "otio_export_completed",
                "OTIO 时间线导出完成。",
                new Dictionary<string, string>
                {
                    ["outputPath"] = outputPath,
                    ["assetCount"] = referencedAssetPaths.Count.ToString(CultureInfo.InvariantCulture)
                });
            return new TimelineExportResult("otio", outputPath, referencedAssetPaths, "OTIO 时间线已导出，可作为 DaVinci Resolve / Premiere Pro / Final Cut Pro 后续适配的标准中间格式。");
        }
        catch (Exception ex) when (ExternalBoundary.ShouldWrap(ex))
        {
            _logger.Error(
                WorkflowSteps.TimelineExport,
                "otio_export_failed",
                "OTIO 时间线导出失败。",
                new Dictionary<string, string> { ["error"] = ex.Message });
            throw new ExternalIntegrationException("TimelineExport", "otio", $"OTIO 导出失败：{ex.Message}", ex);
        }
    }

    private static JsonObject BuildTimeline(TimelineExportContext context, out IReadOnlyList<string> referencedAssetPaths)
    {
        var assetPaths = new List<string>();
        var tracks = new JsonArray();
        foreach (var track in context.Timeline.Tracks)
        {
            tracks.Add(NewTrack(track, context, assetPaths));
        }

        referencedAssetPaths = assetPaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new JsonObject
        {
            ["OTIO_SCHEMA"] = "Timeline.1",
            ["name"] = context.Timeline.Title,
            ["global_start_time"] = NewTime(0, context.Timeline.FramesPerSecond),
            ["tracks"] = new JsonObject
            {
                ["OTIO_SCHEMA"] = "Stack.1",
                ["name"] = "Tracks",
                ["children"] = tracks,
                ["source_range"] = null,
                ["markers"] = NewMarkers(context.Timeline.Markers, context.Timeline.FramesPerSecond),
                ["effects"] = new JsonArray(),
                ["metadata"] = new JsonObject()
            },
            ["metadata"] = NewMetadata(context.Timeline.Metadata)
        };
    }

    private static JsonObject NewTrack(EditingTrack track, TimelineExportContext context, List<string> assetPaths)
    {
        var children = new JsonArray();
        var cursorSeconds = 0d;
        foreach (var clip in track.Clips.OrderBy(clip => clip.StartSeconds))
        {
            if (clip.StartSeconds > cursorSeconds)
            {
                children.Add(NewGap(clip.StartSeconds - cursorSeconds, context.Timeline.FramesPerSecond));
            }

            children.Add(NewClip(clip, context, assetPaths));
            cursorSeconds = Math.Max(cursorSeconds, clip.StartSeconds + clip.DurationSeconds);
        }

        return new JsonObject
        {
            ["OTIO_SCHEMA"] = "Track.1",
            ["name"] = track.Name,
            ["kind"] = OtioTrackKind(track.Kind),
            ["children"] = children,
            ["source_range"] = null,
            ["markers"] = new JsonArray(),
            ["effects"] = new JsonArray(),
            ["metadata"] = NewMetadata(track.Metadata ?? new Dictionary<string, string>())
        };
    }

    private static string OtioTrackKind(EditingTrackKind kind)
    {
        return kind switch
        {
            EditingTrackKind.Audio => "Audio",
            EditingTrackKind.Subtitle => "Text",
            _ => "Video"
        };
    }

    private static JsonObject NewClip(EditingClip clip, TimelineExportContext context, List<string> assetPaths)
    {
        if (!string.IsNullOrWhiteSpace(clip.MediaPath))
        {
            assetPaths.Add(clip.MediaPath);
        }

        return new JsonObject
        {
            ["OTIO_SCHEMA"] = "Clip.2",
            ["name"] = clip.Name,
            ["media_reference"] = string.IsNullOrWhiteSpace(clip.MediaPath)
                ? NewMissingReference()
                : NewExternalReference(clip.MediaPath, clip.DurationSeconds, context),
            ["source_range"] = NewTimeRange(0, clip.DurationSeconds, context.Timeline.FramesPerSecond),
            ["markers"] = new JsonArray(),
            ["effects"] = new JsonArray(),
            ["metadata"] = NewMetadata(WithStartTime(clip.Metadata, clip.StartSeconds))
        };
    }

    private static JsonObject NewGap(double durationSeconds, double fps)
    {
        return new JsonObject
        {
            ["OTIO_SCHEMA"] = "Gap.1",
            ["name"] = "Gap",
            ["source_range"] = NewTimeRange(0, durationSeconds, fps),
            ["markers"] = new JsonArray(),
            ["effects"] = new JsonArray(),
            ["metadata"] = new JsonObject()
        };
    }

    private static JsonObject NewExternalReference(string assetPath, double durationSeconds, TimelineExportContext context)
    {
        var targetUrl = context.Options.UseRelativeMediaReferences && IsInside(context.Workspace.RootPath, assetPath)
            ? Path.GetRelativePath(context.Workspace.Folders.Otio, assetPath).Replace('\\', '/')
            : new Uri(Path.GetFullPath(assetPath)).AbsoluteUri;
        return new JsonObject
        {
            ["OTIO_SCHEMA"] = "ExternalReference.1",
            ["target_url"] = targetUrl,
            ["available_range"] = NewTimeRange(0, durationSeconds, context.Timeline.FramesPerSecond),
            ["metadata"] = new JsonObject()
        };
    }

    private static JsonObject NewMissingReference()
    {
        return new JsonObject
        {
            ["OTIO_SCHEMA"] = "MissingReference.1",
            ["name"] = "未绑定素材",
            ["available_range"] = null,
            ["metadata"] = new JsonObject()
        };
    }

    private static JsonArray NewMarkers(IReadOnlyList<EditingMarker> markers, double fps)
    {
        var result = new JsonArray();
        foreach (var marker in markers.OrderBy(marker => marker.StartSeconds))
        {
            result.Add(new JsonObject
            {
                ["OTIO_SCHEMA"] = "Marker.2",
                ["name"] = marker.Name,
                ["marked_range"] = NewTimeRange(marker.StartSeconds, marker.DurationSeconds, fps),
                ["color"] = marker.Color,
                ["metadata"] = NewMetadata(marker.Metadata)
            });
        }

        return result;
    }

    private static JsonObject NewTimeRange(double startSeconds, double durationSeconds, double fps)
    {
        return new JsonObject
        {
            ["OTIO_SCHEMA"] = "TimeRange.1",
            ["start_time"] = NewTime(startSeconds, fps),
            ["duration"] = NewTime(durationSeconds, fps)
        };
    }

    private static JsonObject NewTime(double seconds, double fps)
    {
        return new JsonObject
        {
            ["OTIO_SCHEMA"] = "RationalTime.1",
            ["value"] = Math.Round(seconds * fps, 3),
            ["rate"] = fps
        };
    }

    private static JsonObject NewMetadata(IReadOnlyDictionary<string, string> metadata)
    {
        var draftAgent = new JsonObject();
        foreach (var pair in metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            draftAgent[pair.Key] = pair.Value;
        }

        return new JsonObject
        {
            ["draft_agent"] = draftAgent
        };
    }

    private static IReadOnlyDictionary<string, string> WithStartTime(IReadOnlyDictionary<string, string> metadata, double startSeconds)
    {
        var values = new Dictionary<string, string>(metadata, StringComparer.Ordinal)
        {
            ["timelineStartSeconds"] = Math.Round(startSeconds, 3).ToString("0.###", CultureInfo.InvariantCulture)
        };
        return values;
    }

    private static bool IsInside(string rootPath, string assetPath)
    {
        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var asset = Path.GetFullPath(assetPath);
        return asset.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "draft-agent-timeline" : cleaned;
    }

    private static string ResolveOutputPath(TimelineExportContext context)
    {
        if (string.IsNullOrWhiteSpace(context.OutputPath))
        {
            return Path.Combine(context.Workspace.Folders.Otio, $"{SanitizeFileName(context.Timeline.Title)}.otio");
        }

        var outputPath = context.OutputPath;
        if (!Path.HasExtension(outputPath))
        {
            outputPath = Path.Combine(outputPath, $"{SanitizeFileName(context.Timeline.Title)}.otio");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        return Path.ChangeExtension(outputPath, ".otio");
    }
}

public sealed class EditingProjectExportService
{
    private readonly WorkbenchTimelineAssembler _timelineAssembler;
    private readonly ITimelineExporter _otioExporter;
    private readonly ITimelineExporter _fcpxmlExporter;
    private readonly ITimelineExporter _jianyingDraftExporter;
    private readonly ITimelineExporter _animateTimelineExporter;
    private readonly IWorkflowLogger _logger;

    public EditingProjectExportService(
        WorkbenchTimelineAssembler? timelineAssembler = null,
        ITimelineExporter? otioExporter = null,
        ITimelineExporter? fcpxmlExporter = null,
        ITimelineExporter? jianyingDraftExporter = null,
        ITimelineExporter? animateTimelineExporter = null,
        IWorkflowLogger? logger = null)
    {
        _logger = logger ?? new InMemoryWorkflowLogger();
        _timelineAssembler = timelineAssembler ?? new WorkbenchTimelineAssembler(_logger);
        _otioExporter = otioExporter ?? new OtioTimelineExporter(_logger);
        _fcpxmlExporter = fcpxmlExporter ?? new FcpxmlTimelineExporter(_logger);
        _jianyingDraftExporter = jianyingDraftExporter ?? new JianyingDraftTimelineExporter(_logger);
        _animateTimelineExporter = animateTimelineExporter ?? new AnimateTimelineExporter(_logger);
    }

    public Task<TimelineExportResult> ExportAsync(EditingProjectExportRequest request, CancellationToken cancellationToken = default)
    {
        return request.Format switch
        {
            EditingProjectFormat.Otio => ExportOtioAsync(request, cancellationToken),
            EditingProjectFormat.Fcpxml => ExportFcpxmlAsync(request, cancellationToken),
            EditingProjectFormat.JianyingDraft => ExportTimelineAsync(request, _jianyingDraftExporter, cancellationToken),
            EditingProjectFormat.AnimateTimeline => ExportAnimateTimelineAsync(request, cancellationToken),
            EditingProjectFormat.DaVinciResolve => ThrowPlannedAdapter(request.Format),
            EditingProjectFormat.PremierePro => ThrowPlannedAdapter(request.Format),
            EditingProjectFormat.FinalCutPro => ThrowPlannedAdapter(request.Format),
            _ => ThrowPlannedAdapter(request.Format)
        };
    }

    public async Task<IReadOnlyList<EditingTimelinePreflightIssue>> PreflightAsync(
        EditingProjectExportRequest request,
        CancellationToken cancellationToken = default)
    {
        return request.Format switch
        {
            EditingProjectFormat.Otio => await PreflightOtioAsync(request, cancellationToken),
            EditingProjectFormat.Fcpxml => await PreflightFcpxmlAsync(request, cancellationToken),
            EditingProjectFormat.JianyingDraft => await PreflightOtioAsync(request, cancellationToken),
            EditingProjectFormat.AnimateTimeline => await PreflightFcpxmlAsync(request, cancellationToken),
            EditingProjectFormat.DaVinciResolve => await ThrowPlannedAdapterPreflight(request.Format),
            EditingProjectFormat.PremierePro => await ThrowPlannedAdapterPreflight(request.Format),
            EditingProjectFormat.FinalCutPro => await ThrowPlannedAdapterPreflight(request.Format),
            _ => await ThrowPlannedAdapterPreflight(request.Format)
        };
    }

    private async Task<TimelineExportResult> ExportOtioAsync(EditingProjectExportRequest request, CancellationToken cancellationToken)
    {
        var manifest = request.Manifest ?? await TryLoadManifestAsync(request.Workspace, cancellationToken);
        var timeline = _timelineAssembler.Assemble(request.Project, request.Workspace, WithMissingVisualPolicy(request), manifest);
        return await _otioExporter.ExportAsync(new TimelineExportContext(timeline, request.Workspace, request.Format, WithMissingVisualPolicy(request), request.OutputPath), cancellationToken);
    }

    private async Task<TimelineExportResult> ExportTimelineAsync(
        EditingProjectExportRequest request,
        ITimelineExporter exporter,
        CancellationToken cancellationToken)
    {
        var manifest = request.Manifest ?? await TryLoadManifestAsync(request.Workspace, cancellationToken);
        var options = WithMissingVisualPolicy(request);
        var timeline = _timelineAssembler.Assemble(request.Project, request.Workspace, options, manifest);
        return await exporter.ExportAsync(new TimelineExportContext(timeline, request.Workspace, request.Format, options, request.OutputPath), cancellationToken);
    }

    private async Task<TimelineExportResult> ExportFcpxmlAsync(
        EditingProjectExportRequest request,
        CancellationToken cancellationToken)
    {
        var manifest = request.Manifest ?? await TryLoadManifestAsync(request.Workspace, cancellationToken);
        var options = FcpxmlExportOptions(request);
        var timeline = _timelineAssembler.Assemble(request.Project, request.Workspace, options, manifest);
        return await _fcpxmlExporter.ExportAsync(new TimelineExportContext(timeline, request.Workspace, request.Format, options, request.OutputPath), cancellationToken);
    }

    private async Task<TimelineExportResult> ExportAnimateTimelineAsync(
        EditingProjectExportRequest request,
        CancellationToken cancellationToken)
    {
        var manifest = request.Manifest ?? await TryLoadManifestAsync(request.Workspace, cancellationToken);
        var options = FcpxmlExportOptions(request);
        var timeline = _timelineAssembler.Assemble(request.Project, request.Workspace, options, manifest);
        return await _animateTimelineExporter.ExportAsync(new TimelineExportContext(timeline, request.Workspace, request.Format, options, request.OutputPath), cancellationToken);
    }

    private async Task<IReadOnlyList<EditingTimelinePreflightIssue>> PreflightOtioAsync(
        EditingProjectExportRequest request,
        CancellationToken cancellationToken)
    {
        var manifest = request.Manifest ?? await TryLoadManifestAsync(request.Workspace, cancellationToken);
        try
        {
            _ = _timelineAssembler.Assemble(request.Project, request.Workspace, request.Options with { AllowMissingVisuals = false }, manifest);
            return [];
        }
        catch (EditingTimelinePreflightException ex)
        {
            return ex.StructuredIssues;
        }
    }

    private async Task<IReadOnlyList<EditingTimelinePreflightIssue>> PreflightFcpxmlAsync(
        EditingProjectExportRequest request,
        CancellationToken cancellationToken)
    {
        var manifest = request.Manifest ?? await TryLoadManifestAsync(request.Workspace, cancellationToken);
        try
        {
            _ = _timelineAssembler.Assemble(request.Project, request.Workspace, FcpxmlExportOptions(request), manifest);
            return [];
        }
        catch (EditingTimelinePreflightException ex)
        {
            return ex.StructuredIssues;
        }
    }

    private async Task<WorkspaceProjectManifest?> TryLoadManifestAsync(WorkspaceProject workspace, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(workspace.ManifestPath))
            {
                return null;
            }

            return await WorkspaceProjectManager.ReadManifestAsync(workspace.ManifestPath, cancellationToken);
        }
        catch (Exception ex) when (ExternalBoundary.ShouldWrap(ex))
        {
            _logger.Warning(
                WorkflowSteps.TimelineExport,
                "manifest_load_skipped",
                "Timeline export could not load project manifest; path-only asset resolution will be used.",
                new Dictionary<string, string> { ["error"] = ex.Message });
            return null;
        }
    }

    private Task<TimelineExportResult> ThrowPlannedAdapter(EditingProjectFormat format)
    {
        _logger.Warning(
            WorkflowSteps.TimelineExport,
            "adapter_not_implemented",
            "剪辑软件专用项目导出适配器尚未实现。",
            new Dictionary<string, string> { ["format"] = format.ToString() });
        throw new NotSupportedException($"{format} 专用项目导出尚未实现；请导出 FCPXML、剪映草稿、Adobe Animate 音频字幕，或使用 Core 内部 OTIO 兼容路径。");
    }

    private Task<IReadOnlyList<EditingTimelinePreflightIssue>> ThrowPlannedAdapterPreflight(EditingProjectFormat format)
    {
        _logger.Warning(
            WorkflowSteps.TimelineExport,
            "adapter_preflight_not_implemented",
            "剪辑软件专用项目导出预检适配器尚未实现。",
            new Dictionary<string, string> { ["format"] = format.ToString() });
        throw new NotSupportedException($"{format} 专用项目导出预检尚未实现；请导出 FCPXML、剪映草稿、Adobe Animate 音频字幕，或使用 Core 内部 OTIO 兼容路径。");
    }

    private static TimelineExportOptions WithMissingVisualPolicy(EditingProjectExportRequest request)
    {
        return request.Options with { AllowMissingVisuals = request.AllowMissingVisuals || request.Options.AllowMissingVisuals };
    }

    private static TimelineExportOptions FcpxmlExportOptions(EditingProjectExportRequest request)
    {
        return request.Options with
        {
            AllowMissingVisuals = true,
            RequireAudioForTextRows = true
        };
    }
}
