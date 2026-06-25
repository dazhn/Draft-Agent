using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace DraftAgent.Core;

public sealed class AnimateTimelineExporter : ITimelineExporter
{
    private const int StageWidth = 1920;
    private const int StageHeight = 1080;
    private const double AnimateFrameRate = 30;
    private const string ManifestFileName = "draft-agent-animate-subtitles.json";
    private const string JsflImporterFileName = "import-draft-agent-audio-subtitles.jsfl";
    private const string PowerShellLauncherFileName = "run-import-in-animate.ps1";
    private const string CommandLauncherFileName = "run-import-in-animate.cmd";

    private readonly IWorkflowLogger _logger;

    public AnimateTimelineExporter(IWorkflowLogger? logger = null)
    {
        _logger = logger ?? new InMemoryWorkflowLogger();
    }

    public async Task<TimelineExportResult> ExportAsync(TimelineExportContext context, CancellationToken cancellationToken = default)
    {
        if (context.Format != EditingProjectFormat.AnimateTimeline)
        {
            throw new NotSupportedException($"{context.Format} 导出适配器不支持 Adobe Animate 音频字幕导入。");
        }

        var outputDirectory = ResolveOutputDirectory(context);
        Directory.CreateDirectory(outputDirectory);
        var imageDirectory = Path.Combine(outputDirectory, "images");
        var audioDirectory = Path.Combine(outputDirectory, "audio");
        Directory.CreateDirectory(imageDirectory);
        Directory.CreateDirectory(audioDirectory);

        var subtitleDirectory = Path.Combine(outputDirectory, "srt");
        var subtitleExports = TimelineSubtitleExporter.ExportSubtitleTrack(context.Timeline, subtitleDirectory);
        var tracks = await BuildTracksAsync(context.Timeline, imageDirectory, audioDirectory, subtitleExports, cancellationToken);
        var manifestPath = Path.Combine(outputDirectory, ManifestFileName);
        var jsflPath = Path.Combine(outputDirectory, JsflImporterFileName);
        var powerShellLauncherPath = Path.Combine(outputDirectory, PowerShellLauncherFileName);
        var commandLauncherPath = Path.Combine(outputDirectory, CommandLauncherFileName);
        var manifestJson = BuildManifest(context, tracks).ToJsonString(DraftJson.Options);

        await File.WriteAllTextAsync(manifestPath, manifestJson, new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(jsflPath, BuildJsflImporter(manifestPath, manifestJson), new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(powerShellLauncherPath, BuildPowerShellLauncher(), new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(commandLauncherPath, BuildCommandLauncher(), new UTF8Encoding(false), cancellationToken);

        var imageCount = tracks.Sum(track => track.Items.Count(item => item.Kind == AnimateTimelineItemKind.Image));
        var audioCount = tracks.Sum(track => track.Items.Count(item => item.Kind == AnimateTimelineItemKind.Audio));
        var subtitleCount = tracks.Sum(track => track.Items.Count(item => item.Kind == AnimateTimelineItemKind.Text));

        _logger.Info(
            WorkflowSteps.TimelineExport,
            "animate_timeline_export_completed",
            "Adobe Animate 时间线导入包导出完成。",
            new Dictionary<string, string>
            {
                ["outputPath"] = outputDirectory,
                ["imageCount"] = imageCount.ToString(CultureInfo.InvariantCulture),
                ["audioCount"] = audioCount.ToString(CultureInfo.InvariantCulture),
                ["textCount"] = subtitleCount.ToString(CultureInfo.InvariantCulture)
            });

        return new TimelineExportResult(
            "animate_timeline",
            outputDirectory,
            tracks.SelectMany(track => track.Items).Select(item => item.SourcePath).Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => path!).Concat(subtitleExports.Select(item => item.OutputPath)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            "Adobe Animate 时间线导入包已导出。可运行 run-import-in-animate.ps1 或 run-import-in-animate.cmd，一键把文本、角色、场景、音频固定轨道导入 Animate。");
    }

    private static async Task<IReadOnlyList<AnimateTrack>> BuildTracksAsync(
        EditingTimeline timeline,
        string imageDirectory,
        string audioDirectory,
        IReadOnlyList<ClipSubtitleExport> subtitleExports,
        CancellationToken cancellationToken)
    {
        var itemsByTrackId = timeline.Tracks.ToDictionary(track => track.Id, _ => new List<AnimateTimelineItem>(), StringComparer.Ordinal);
        var usedImageNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedAudioNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var track in timeline.Tracks)
        {
            foreach (var clip in track.Clips.OrderBy(clip => clip.StartSeconds).ThenBy(clip => clip.Id, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(clip.MediaPath) || !File.Exists(clip.MediaPath))
                {
                    continue;
                }

                var sourcePath = Path.GetFullPath(clip.MediaPath);
                var destinationDirectory = track.Kind == EditingTrackKind.Audio ? audioDirectory : imageDirectory;
                var usedNames = track.Kind == EditingTrackKind.Audio ? usedAudioNames : usedImageNames;
                var fileName = UniqueFileName(SanitizeFileName(Path.GetFileName(sourcePath)), usedNames);
                var destinationPath = Path.Combine(destinationDirectory, fileName);
                await CopyFileAsync(sourcePath, destinationPath, cancellationToken);
                var startFrame = Frame(clip.StartSeconds);
                var endFrame = Math.Max(startFrame + 1, Frame(clip.StartSeconds + clip.DurationSeconds));
                var itemMetadata = track.Kind == EditingTrackKind.Audio
                    ? clip.Metadata
                    : WithVisualRoleMetadata(clip.Metadata, VisualRole(track));
                var item = new AnimateTimelineItem(
                    track.Kind == EditingTrackKind.Audio ? AnimateTimelineItemKind.Audio : AnimateTimelineItemKind.Image,
                    clip.Id,
                    clip.Name,
                    Path.GetFileName(destinationPath),
                    sourcePath,
                    destinationPath,
                    null,
                    startFrame,
                    endFrame,
                    endFrame - startFrame,
                    clip.StartSeconds,
                    clip.DurationSeconds,
                    itemMetadata);

                itemsByTrackId[track.Id].Add(item);
            }
        }

        var subtitleTrack = timeline.Tracks.FirstOrDefault(track => track.Kind == EditingTrackKind.Subtitle);
        if (subtitleTrack is not null)
        {
            itemsByTrackId[subtitleTrack.Id].AddRange(BuildSubtitleItems(timeline, subtitleExports));
        }

        return timeline.Tracks
            .Select((track, index) => new AnimateTrack(
                track.Id,
                $"{index + 1:00} {AnimateTrackDisplayName(track)}",
                AnimateTrackKindFromTimeline(track),
                index + 1,
                AnimateTrackMetadata(track),
                Ordered(itemsByTrackId[track.Id])))
            .ToArray();
    }

    private static async Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await source.CopyToAsync(destination, cancellationToken);
    }

    private static IReadOnlyList<AnimateTimelineItem> BuildSubtitleItems(
        EditingTimeline timeline,
        IReadOnlyList<ClipSubtitleExport> subtitleExports)
    {
        var clipsById = timeline.Tracks.SelectMany(track => track.Clips).ToDictionary(clip => clip.Id, StringComparer.Ordinal);
        var result = new List<AnimateTimelineItem>();
        foreach (var subtitle in subtitleExports)
        {
            if (!clipsById.TryGetValue(subtitle.ClipId, out var clip))
            {
                continue;
            }

            foreach (var cue in subtitle.Cues)
            {
                var startSeconds = clip.StartSeconds + cue.StartSeconds;
                var endSeconds = startSeconds + cue.DurationSeconds;
                var startFrame = Frame(startSeconds);
                var endFrame = Math.Max(startFrame + 1, Frame(endSeconds));
                result.Add(new AnimateTimelineItem(
                    AnimateTimelineItemKind.Text,
                    subtitle.ClipId,
                    $"{subtitle.ClipName} 字幕 {cue.Index}",
                    null,
                    null,
                    null,
                    cue.Text,
                    startFrame,
                    endFrame,
                    endFrame - startFrame,
                    startSeconds,
                    cue.DurationSeconds,
                    new Dictionary<string, string>
                    {
                        ["subtitleIndex"] = cue.Index.ToString(CultureInfo.InvariantCulture),
                        ["sourceClipId"] = subtitle.ClipId
                    }));
            }
        }

        return result
            .OrderBy(item => item.StartFrame)
            .ThenBy(item => item.ClipId, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<AnimateTimelineItem> Ordered(IEnumerable<AnimateTimelineItem> items)
    {
        return items
            .OrderBy(item => item.StartFrame)
            .ThenBy(item => item.ClipId, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsCharacterVisualTrack(EditingTrack track)
    {
        return track.Metadata is not null &&
            track.Metadata.TryGetValue("visualRole", out var role) &&
            string.Equals(role, "character", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNarrationSceneEffectVisualTrack(EditingTrack track)
    {
        return track.Metadata is not null &&
            track.Metadata.TryGetValue("visualRole", out var role) &&
            string.Equals(role, "narration_scene_effect", StringComparison.OrdinalIgnoreCase);
    }

    private static string VisualRole(EditingTrack track)
    {
        if (track.Metadata is not null &&
            track.Metadata.TryGetValue("visualRole", out var role) &&
            !string.IsNullOrWhiteSpace(role))
        {
            return role;
        }

        return "scene";
    }

    private static IReadOnlyDictionary<string, string> WithVisualRoleMetadata(
        IReadOnlyDictionary<string, string> metadata,
        string visualRole)
    {
        var values = new Dictionary<string, string>(metadata, StringComparer.Ordinal)
        {
            ["visualRole"] = visualRole
        };
        return values;
    }

    private static AnimateTrackKind AnimateTrackKindFromTimeline(EditingTrack track)
    {
        return track.Kind switch
        {
            EditingTrackKind.Subtitle => AnimateTrackKind.Text,
            EditingTrackKind.Audio => AnimateTrackKind.Audio,
            _ => AnimateTrackKind.Image
        };
    }

    private static IReadOnlyDictionary<string, string> AnimateTrackMetadata(EditingTrack track)
    {
        var metadata = new Dictionary<string, string>(track.Metadata ?? new Dictionary<string, string>(), StringComparer.Ordinal)
        {
            ["timelineTrackId"] = track.Id,
            ["timelineTrackName"] = track.Name
        };

        if (track.Kind == EditingTrackKind.Subtitle)
        {
            metadata["animateRole"] = "text";
        }
        else if (track.Kind == EditingTrackKind.Audio)
        {
            metadata["animateRole"] = track.Metadata is not null && track.Metadata.TryGetValue("trackRole", out var role) ? role : "audio";
        }
        else
        {
            metadata["animateRole"] = VisualRole(track);
        }

        return metadata;
    }

    private static string AnimateTrackDisplayName(EditingTrack track)
    {
        if (track.Kind == EditingTrackKind.Subtitle)
        {
            return "字幕";
        }

        if (track.Metadata is not null && track.Metadata.TryGetValue("visualRole", out var visualRole))
        {
            return visualRole.Trim().ToLowerInvariant() switch
            {
                "character" => "角色图",
                "narration_scene_effect" => "旁白场效图",
                "scene" => "场景图",
                _ => track.Name
            };
        }

        if (track.Metadata is not null && track.Metadata.TryGetValue("trackRole", out var trackRole))
        {
            return trackRole.Trim().ToLowerInvariant() switch
            {
                "character_dialogue_voice" => "角色台词配音",
                "character_thought_voice" => "角色内心配音",
                "character_action_voice" => "角色动作配音",
                "narration_scene_effect_voice" => "旁白场效配音",
                _ => track.Name
            };
        }

        return track.Name;
    }

    private static JsonObject BuildManifest(
        TimelineExportContext context,
        IReadOnlyList<AnimateTrack> tracks)
    {
        var durationFrames = Math.Max(
            1,
            tracks
                .SelectMany(track => track.Items)
                .Select(item => item.EndFrame)
                .DefaultIfEmpty(1)
                .Max());

        return new JsonObject
        {
            ["format"] = "animate_timeline",
            ["title"] = context.Timeline.Title,
            ["frameRate"] = AnimateFrameRate,
            ["width"] = StageWidth,
            ["height"] = StageHeight,
            ["durationFrames"] = durationFrames,
            ["trackOrder"] = "editing_timeline_8_tracks",
            ["sourceFrameRate"] = context.Timeline.FramesPerSecond,
            ["tracks"] = new JsonArray(tracks.Select(track => new JsonObject
            {
                ["id"] = track.Id,
                ["name"] = track.Name,
                ["kind"] = track.Kind.ToString().ToLowerInvariant(),
                ["order"] = track.Order,
                ["metadata"] = Metadata(track.Metadata),
                ["items"] = new JsonArray(track.Items.Select(item => new JsonObject
                {
                    ["kind"] = item.Kind.ToString().ToLowerInvariant(),
                    ["clipId"] = item.ClipId,
                    ["name"] = item.Name,
                    ["libraryName"] = item.LibraryName,
                    ["sourcePath"] = item.SourcePath,
                    ["path"] = item.CopiedPath,
                    ["text"] = item.Text,
                    ["startFrame"] = item.StartFrame,
                    ["endFrame"] = item.EndFrame,
                    ["durationFrames"] = item.DurationFrames,
                    ["startSeconds"] = item.StartSeconds,
                    ["durationSeconds"] = item.DurationSeconds,
                    ["metadata"] = Metadata(item.Metadata)
                }).ToArray<JsonNode?>())
            }).ToArray<JsonNode?>())
        };
    }

    private static string BuildJsflImporter(string manifestPath, string manifestJson)
    {
        var manifestUri = ToJsflFileUri(manifestPath);
        var escapedManifestJson = EscapeJavaScriptString(manifestJson);
        return $$"""
        /*
         * Draft Agent -> Adobe Animate timeline importer.
         * Run this JSFL from Animate. The script creates a fresh Animate document
         * on every run, then adds editable timeline layers in fixed Draft Agent
         * order: text, character, scene, audio.
         * It does not create or convert a FLA.
         */
        var manifestUri = "{{manifestUri}}";
        var reportUri = manifestUri.replace(/draft-agent-animate-subtitles\.json$/, "draft-agent-animate-import-report.txt");
        var manifestText = "{{escapedManifestJson}}";

        function readText(uri) {
            var text = FLfile.read(uri);
            if (!text) {
                return manifestText;
            }
            return text;
        }

        function fileUri(path) {
            return "file:///" + path.replace(/\\/g, "/").replace(/^([A-Za-z]):/, "$1|");
        }

        function uniqueLayerName(timeline, name) {
            var existing = {};
            for (var i = 0; i < timeline.layers.length; i++) {
                existing[timeline.layers[i].name] = true;
            }
            if (!existing[name]) {
                return name;
            }

            var index = 2;
            var candidate = name + " Import " + index;
            while (existing[candidate]) {
                index++;
                candidate = name + " Import " + index;
            }

            return candidate;
        }

        function findLayerIndexByName(timeline, name) {
            for (var i = 0; i < timeline.layers.length; i++) {
                if (timeline.layers[i].name === name) {
                    return i;
                }
            }
            return -1;
        }

        function ensureCurrentLayerFrameCount(timeline, requiredFrameCount) {
            requiredFrameCount = Math.max(1, intFrame(requiredFrameCount, 1));
            var layer = timeline.layers[timeline.currentLayer];
            if (layer.frames.length < requiredFrameCount) {
                timeline.insertFrames(requiredFrameCount - layer.frames.length, false, Math.max(0, layer.frames.length - 1));
            }
        }

        function requireImportLayer(timeline, layersById, trackId) {
            var layer = findLayerIndexByName(timeline, layersById.__names[trackId]);
            layersById[trackId] = layer;
            if (layer === undefined || layer < 0 || layer >= timeline.layers.length) {
                alert("找不到 Animate 导入图层: " + trackId + " / " + layersById.__names[trackId]);
                return -1;
            }
            return layer;
        }

        function addImportLayer(timeline, name, requiredFrameCount) {
            var layerName = uniqueLayerName(timeline, name);
            timeline.addNewLayer("Draft Agent Import", "normal", true);
            timeline.layers[timeline.currentLayer].name = layerName;
            return layerName;
        }

        function createImportLayers(timeline, tracks, requiredFrameCount) {
            var layerNamesById = {};
            for (var i = tracks.length - 1; i >= 0; i--) {
                layerNamesById[tracks[i].id] = addImportLayer(timeline, tracks[i].name, requiredFrameCount);
            }

            var layersById = {};
            for (var j = 0; j < tracks.length; j++) {
                layersById[tracks[j].id] = findLayerIndexByName(timeline, layerNamesById[tracks[j].id]);
            }
            layersById.__names = layerNamesById;
            return layersById;
        }

        function numberOrDefault(value, fallback) {
            var parsed = Number(value);
            return isNaN(parsed) ? fallback : parsed;
        }

        function intFrame(value, fallback) {
            return Math.max(0, Math.round(numberOrDefault(value, fallback || 0)));
        }

        function itemStartFrame(item) {
            return intFrame(item.startFrame, 0);
        }

        function itemDurationFrames(item) {
            if (item.endFrame !== undefined && item.endFrame !== null) {
                return Math.max(1, itemEndFrame(item) - itemStartFrame(item));
            }
            return Math.max(1, intFrame(item.durationFrames, 1));
        }

        function itemEndFrame(item) {
            var startFrame = itemStartFrame(item);
            if (item.endFrame !== undefined && item.endFrame !== null) {
                return Math.max(startFrame + 1, intFrame(item.endFrame, startFrame + 1));
            }
            return startFrame + itemDurationFrames(item);
        }

        function ensureFrameCount(timeline, requiredFrameCount) {
            requiredFrameCount = Math.max(1, intFrame(requiredFrameCount, 1));
            if (timeline.frameCount < requiredFrameCount) {
                var previousFrame = timeline.currentFrame;
                timeline.currentFrame = Math.max(0, timeline.frameCount - 1);
                timeline.insertFrames(requiredFrameCount - timeline.frameCount, true, timeline.currentFrame);
                timeline.currentFrame = previousFrame;
            }
        }

        function prepareContentSpan(timeline, item) {
            var startFrame = itemStartFrame(item);
            ensureCurrentLayerFrameCount(timeline, startFrame + 1);
            timeline.currentFrame = startFrame;
            return timeline.layers[timeline.currentLayer].frames[startFrame];
        }

        function prepareTrackSpans(timeline, track, layer) {
            timeline.currentLayer = layer;
            var boundaries = {};
            var maxEndFrame = 1;
            for (var i = 0; i < track.items.length; i++) {
                var item = track.items[i];
                var startFrame = itemStartFrame(item);
                var endFrame = itemEndFrame(item);
                boundaries[startFrame] = true;
                boundaries[endFrame] = true;
                maxEndFrame = Math.max(maxEndFrame, endFrame + 1);
            }
            ensureCurrentLayerFrameCount(timeline, maxEndFrame);
            var ordered = [];
            for (var key in boundaries) {
                if (boundaries.hasOwnProperty(key)) {
                    ordered.push(intFrame(key, 0));
                }
            }
            ordered.sort(function (a, b) { return b - a; });
            for (var b = 0; b < ordered.length; b++) {
                var frameIndex = ordered[b];
                if (frameIndex > 0 && frameIndex < timeline.layers[layer].frames.length) {
                    timeline.currentFrame = frameIndex;
                    timeline.convertToBlankKeyframes(frameIndex, frameIndex + 1);
                }
            }
        }

        function findLibraryItem(dom, libraryName, sourceUri) {
            var items = dom.library.items;
            for (var i = 0; i < items.length; i++) {
                if (items[i].name === libraryName || items[i].sourceFilePath === sourceUri) {
                    return items[i];
                }
            }
            return null;
        }

        function setSelectionBounds(dom, bounds) {
            try {
                if (dom.selection && dom.selection.length > 0) {
                    dom.setSelectionBounds(bounds);
                    return true;
                }
            } catch (ex) {
            }
            return false;
        }

        function fitSelectionToBounds(dom, bounds, contain) {
            var current = 0;
            try {
                current = dom.getSelectionRect();
            } catch (ex) {
                current = 0;
            }
            if (!current || current.right <= current.left || current.bottom <= current.top) {
                setSelectionBounds(dom, bounds);
                return;
            }

            var currentWidth = current.right - current.left;
            var currentHeight = current.bottom - current.top;
            var targetWidth = bounds.right - bounds.left;
            var targetHeight = bounds.bottom - bounds.top;
            var scaleX = targetWidth / currentWidth;
            var scaleY = targetHeight / currentHeight;
            var scale = contain ? Math.min(scaleX, scaleY) : Math.max(scaleX, scaleY);
            var fittedWidth = currentWidth * scale;
            var fittedHeight = currentHeight * scale;
            var fitted = {
                left: bounds.left + ((targetWidth - fittedWidth) / 2),
                top: bounds.top + ((targetHeight - fittedHeight) / 2),
                right: bounds.left + ((targetWidth - fittedWidth) / 2) + fittedWidth,
                bottom: bounds.top + ((targetHeight - fittedHeight) / 2) + fittedHeight
            };
            setSelectionBounds(dom, fitted);
        }

        function importImage(dom, timeline, item) {
            prepareContentSpan(timeline, item);
            dom.importFile(fileUri(item.path), false, false, false);
            var role = item.metadata && item.metadata.visualRole ? item.metadata.visualRole : "";
            if (role === "character") {
                fitSelectionToBounds(dom, {
                    left: Math.round(dom.width * 0.25),
                    top: Math.round(dom.height * 0.08),
                    right: Math.round(dom.width * 0.75),
                    bottom: Math.round(dom.height * 0.96)
                }, true);
            } else {
                fitSelectionToBounds(dom, {
                    left: 0,
                    top: 0,
                    right: dom.width,
                    bottom: dom.height
                }, false);
            }
        }

        function importAudio(dom, timeline, item) {
            var frame = prepareContentSpan(timeline, item);
            var sourceUri = fileUri(item.path);
            dom.importFile(sourceUri, true, false, false);
            var libraryItem = findLibraryItem(dom, item.libraryName, sourceUri);
            if (frame) {
                if (libraryItem) {
                    frame.soundLibraryItem = libraryItem;
                }
                frame.soundName = item.libraryName;
                frame.soundSync = "stream";
                frame.soundLoopMode = "repeat";
                frame.soundLoop = 1;
            }
        }

        function addSubtitle(dom, timeline, item, stageWidth, stageHeight) {
            var frame = prepareContentSpan(timeline, item);
            var textValue = item.text || "";
            var bounds = {
                left: Math.round(stageWidth * 0.08),
                top: Math.round(stageHeight * 0.78),
                right: Math.round(stageWidth * 0.92),
                bottom: Math.round(stageHeight * 0.92)
            };
            dom.addNewText(bounds, textValue);
            frame = timeline.layers[timeline.currentLayer].frames[itemStartFrame(item)];
            var textElement = frame.elements[frame.elements.length - 1];
            try {
                textElement.textType = "static";
                textElement.orientation = "horizontal";
                textElement.useDeviceFonts = true;
                textElement.setTextString(textValue);
                textElement.setTextAttr("face", "Microsoft YaHei", 0, textValue.length);
                textElement.setTextAttr("size", 48, 0, textValue.length);
                textElement.setTextAttr("alignment", "center", 0, textValue.length);
                textElement.setTextAttr("fillColor", "#000000", 0, textValue.length);
                textElement.setTextAttr("lineSpacing", 8, 0, textValue.length);
            } catch (textTypeEx) {
            }
        }

        function requiredFrameCount(data) {
            var required = intFrame(data.durationFrames, 1);
            for (var t = 0; t < data.tracks.length; t++) {
                var track = data.tracks[t];
                for (var i = 0; i < track.items.length; i++) {
                    required = Math.max(required, itemEndFrame(track.items[i]) + 1);
                }
            }
            return required;
        }

        function traceFirstSpan(timeline, data, layersById, kind) {
            for (var t = 0; t < data.tracks.length; t++) {
                var track = data.tracks[t];
                var layer = layersById[track.id];
                for (var i = 0; i < track.items.length; i++) {
                    var item = track.items[i];
                    if (item.kind !== kind) {
                        continue;
                    }

                    var startFrame = itemStartFrame(item);
                    var frame = timeline.layers[layer].frames[startFrame];
                    fl.trace("Draft Agent span check " + kind + ": start=" + startFrame + ", expected=" + itemDurationFrames(item) + ", animateDuration=" + (frame ? frame.duration : "missing"));
                    return;
                }
            }
        }

        function writeImportReport(timeline, data, layersById) {
            var lines = [];
            lines.push("title=" + (data.title || ""));
            lines.push("frameRate=" + (data.frameRate || "") + ",width=" + (data.width || "") + ",height=" + (data.height || "") + ",timelineFrameCount=" + timeline.frameCount);
            lines.push("tracks=" + data.tracks.length);
            for (var t = 0; t < data.tracks.length; t++) {
                var track = data.tracks[t];
                var layerIndex = layersById[track.id];
                var layer = timeline.layers[layerIndex];
                lines.push("track=" + track.name + ",layer=" + layerIndex + ",items=" + track.items.length + ",frames=" + (layer ? layer.frames.length : "missing"));
                for (var i = 0; i < track.items.length; i++) {
                    var item = track.items[i];
                    var startFrame = itemStartFrame(item);
                    var frame = layer && layer.frames.length > startFrame ? layer.frames[startFrame] : null;
                    var elementCount = frame && frame.elements ? frame.elements.length : 0;
                    lines.push("  item=" + i +
                        ",kind=" + item.kind +
                        ",start=" + startFrame +
                        ",expected=" + itemDurationFrames(item) +
                        ",actual=" + (frame ? frame.duration : "missing") +
                        ",sound=" + (frame && frame.soundName ? frame.soundName : "") +
                        ",elements=" + elementCount +
                        ",text=" + ((item.text || "").replace(/\r/g, " ").replace(/\n/g, " ")));
                }
            }
            FLfile.write(reportUri, lines.join("\n"));
        }

        fl.createDocument();
        var dom = fl.getDocumentDOM();

        if (!dom) {
            alert("无法创建或打开 Animate 文档，Draft Agent 导入已取消。");
        } else {
            var data = eval("(" + readText(manifestUri) + ")");
            dom.frameRate = data.frameRate || dom.frameRate;
            dom.width = data.width || dom.width;
            dom.height = data.height || dom.height;
            var timeline = dom.getTimeline();
            var totalFrameCount = requiredFrameCount(data);
            ensureFrameCount(timeline, totalFrameCount);
            var layersById = createImportLayers(timeline, data.tracks, totalFrameCount);
            var importedImages = 0;
            var importedAudio = 0;
            var importedText = 0;
            for (var t = 0; t < data.tracks.length; t++) {
                var track = data.tracks[t];
                var layer = requireImportLayer(timeline, layersById, track.id);
                if (layer < 0) {
                    break;
                }
                prepareTrackSpans(timeline, track, layer);
                for (var i = 0; i < track.items.length; i++) {
                    var item = track.items[i];
                    layer = requireImportLayer(timeline, layersById, track.id);
                    if (layer < 0) {
                        break;
                    }
                    timeline.currentLayer = layer;
                    if (item.kind === "image") {
                        importImage(dom, timeline, item);
                        importedImages++;
                    } else if (item.kind === "audio") {
                        importAudio(dom, timeline, item);
                        importedAudio++;
                    } else if (item.kind === "text") {
                        addSubtitle(dom, timeline, item, data.width || dom.width, data.height || dom.height);
                        importedText++;
                    }
                }
            }
            if (data.tracks.length > 0 && layersById[data.tracks[0].id] !== undefined) {
                timeline.currentLayer = layersById[data.tracks[0].id];
            }
            timeline.currentFrame = 0;
            traceFirstSpan(timeline, data, layersById, "text");
            traceFirstSpan(timeline, data, layersById, "audio");
            traceFirstSpan(timeline, data, layersById, "image");
            writeImportReport(timeline, data, layersById);
            fl.trace("Draft Agent timeline import completed in fixed order: text, character, scene, audio. " + importedImages + " images, " + importedAudio + " audio clips, " + importedText + " text cues.");
        }
        """;
    }

    private static string BuildPowerShellLauncher()
    {
        return $$"""
        param(
            [string]$AnimateExe
        )

        $ErrorActionPreference = 'Stop'
        $packageDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
        $jsflPath = Join-Path $packageDirectory '{{JsflImporterFileName}}'

        if (-not (Test-Path -LiteralPath $jsflPath)) {
            throw "找不到 Animate 导入脚本：$jsflPath"
        }

        $candidatePaths = @(
            $AnimateExe,
            "${env:ProgramFiles}\Adobe\Adobe Animate 2026\Animate.exe",
            "${env:ProgramFiles}\Adobe\Adobe Animate 2025\Animate.exe",
            "${env:ProgramFiles}\Adobe\Adobe Animate 2024\Animate.exe",
            "${env:ProgramFiles}\Adobe\Adobe Animate 2023\Animate.exe",
            "${env:ProgramFiles}\Adobe\Adobe Animate 2022\Animate.exe",
            "${env:ProgramFiles(x86)}\Adobe\Adobe Animate 2026\Animate.exe",
            "${env:ProgramFiles(x86)}\Adobe\Adobe Animate 2025\Animate.exe",
            "${env:ProgramFiles(x86)}\Adobe\Adobe Animate 2024\Animate.exe",
            "${env:ProgramFiles(x86)}\Adobe\Adobe Animate 2023\Animate.exe",
            "${env:ProgramFiles(x86)}\Adobe\Adobe Animate 2022\Animate.exe"
        ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

        $resolvedAnimate = $candidatePaths | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if (-not $resolvedAnimate) {
            throw "找不到 Animate.exe。请用 -AnimateExe 指定路径，例如：.\{{PowerShellLauncherFileName}} -AnimateExe 'C:\Program Files\Adobe\Adobe Animate 2022\Animate.exe'"
        }

        Write-Host "Animate: $resolvedAnimate"
        Write-Host "JSFL: $jsflPath"
        & $resolvedAnimate $jsflPath -AlwaysRunJSFL
        """;
    }

    private static string BuildCommandLauncher()
    {
        return $$"""
        @echo off
        setlocal
        powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0{{PowerShellLauncherFileName}}" %*
        if errorlevel 1 (
          echo.
          echo Animate import failed.
          pause
          exit /b 1
        )
        endlocal
        """;
    }

    private static JsonObject Metadata(IReadOnlyDictionary<string, string> metadata)
    {
        var json = new JsonObject();
        foreach (var pair in metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            json[pair.Key] = pair.Value;
        }

        return json;
    }

    private static int Frame(double seconds)
    {
        return (int)Math.Round(Math.Max(0, seconds) * AnimateFrameRate, MidpointRounding.AwayFromZero);
    }

    private static string ToJsflFileUri(string path)
    {
        var fullPath = Path.GetFullPath(path).Replace('\\', '/');
        if (fullPath.Length >= 2 && fullPath[1] == ':')
        {
            return $"file:///{fullPath[0]}|{fullPath[2..]}";
        }

        return new Uri(fullPath).AbsoluteUri;
    }

    private static string EscapeJavaScriptString(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private static string ResolveOutputDirectory(TimelineExportContext context)
    {
        var outputPath = context.OutputPath;
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.Combine(context.Workspace.Folders.Exports, $"{SanitizeFileName(context.Timeline.Title)}_Animate音频字幕");
        }

        return Path.HasExtension(outputPath)
            ? Path.Combine(Path.GetDirectoryName(outputPath)!, Path.GetFileNameWithoutExtension(outputPath))
            : outputPath;
    }

    private static string UniqueFileName(string fileName, HashSet<string> usedNames)
    {
        var extension = Path.GetExtension(fileName);
        var name = Path.GetFileNameWithoutExtension(fileName);
        var candidate = fileName;
        var index = 2;
        while (!usedNames.Add(candidate))
        {
            candidate = $"{name}_{index}{extension}";
            index++;
        }

        return candidate;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string((string.IsNullOrWhiteSpace(value) ? "draft-agent-timeline" : value.Trim())
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "draft-agent-timeline" : cleaned;
    }

    private sealed record AnimateTrack(
        string Id,
        string Name,
        AnimateTrackKind Kind,
        int Order,
        IReadOnlyDictionary<string, string> Metadata,
        IReadOnlyList<AnimateTimelineItem> Items);

    private sealed record AnimateTimelineItem(
        AnimateTimelineItemKind Kind,
        string ClipId,
        string Name,
        string? LibraryName,
        string? SourcePath,
        string? CopiedPath,
        string? Text,
        int StartFrame,
        int EndFrame,
        int DurationFrames,
        double StartSeconds,
        double DurationSeconds,
        IReadOnlyDictionary<string, string> Metadata);

    private enum AnimateTrackKind
    {
        Image,
        Audio,
        Text
    }

    private enum AnimateTimelineItemKind
    {
        Image,
        Audio,
        Text
    }
}
