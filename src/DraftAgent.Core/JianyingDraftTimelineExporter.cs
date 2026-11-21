using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace DraftAgent.Core;

public sealed class JianyingDraftTimelineExporter : ITimelineExporter
{
    private readonly IWorkflowLogger _logger;

    public JianyingDraftTimelineExporter(IWorkflowLogger? logger = null)
    {
        _logger = logger ?? new InMemoryWorkflowLogger();
    }

    public async Task<TimelineExportResult> ExportAsync(TimelineExportContext context, CancellationToken cancellationToken = default)
    {
        if (context.Format != EditingProjectFormat.JianyingDraft)
        {
            throw new NotSupportedException($"{context.Format} 导出适配器不支持剪映草稿。");
        }

        var outputDirectory = ResolveOutputDirectory(context);
        Directory.CreateDirectory(outputDirectory);
        var subtitleDirectory = Path.Combine(outputDirectory, "srt");
        var subtitleExports = TimelineSubtitleExporter.ExportSubtitleTrack(context.Timeline, subtitleDirectory);
        var referencedAssets = context.Timeline.Tracks
            .SelectMany(track => track.Clips)
            .Select(clip => clip.MediaPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var draftContentPath = Path.Combine(outputDirectory, "draft_content.json");
        var draftMetaPath = Path.Combine(outputDirectory, "draft_meta_info.json");
        await File.WriteAllTextAsync(draftContentPath, BuildDraftContent(context, subtitleExports, outputDirectory).ToJsonString(DraftJson.Options), new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(draftMetaPath, BuildDraftMeta(context, outputDirectory).ToJsonString(DraftJson.Options), new UTF8Encoding(false), cancellationToken);

        _logger.Info(
            WorkflowSteps.TimelineExport,
            "jianying_draft_export_completed",
            "剪映草稿导出完成。",
            new Dictionary<string, string>
            {
                ["outputPath"] = outputDirectory,
                ["assetCount"] = referencedAssets.Length.ToString(CultureInfo.InvariantCulture),
                ["subtitleCount"] = subtitleExports.Count.ToString(CultureInfo.InvariantCulture)
            });

        return new TimelineExportResult("jianying_draft", outputDirectory, referencedAssets.Concat(subtitleExports.Select(item => item.OutputPath)).ToArray(), "剪映草稿 JSON 与逐段 SRT 已导出。");
    }

    private static JsonObject BuildDraftContent(
        TimelineExportContext context,
        IReadOnlyList<ClipSubtitleExport> subtitleExports,
        string outputDirectory)
    {
        var materialsByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var videoMaterials = new JsonArray();
        var audioMaterials = new JsonArray();
        var speedMaterials = new JsonArray();
        var textMaterials = new JsonArray();
        var tracks = new JsonArray();
        var subtitleSegments = new JsonArray();
        foreach (var subtitle in subtitleExports)
        {
            var clip = context.Timeline.Tracks.SelectMany(track => track.Clips).First(candidate => candidate.Id == subtitle.ClipId);
            foreach (var cue in subtitle.Cues)
            {
                var materialId = Id("text_material", $"{subtitle.ClipId}_{cue.Index}");
                textMaterials.Add(TextMaterial(materialId, cue.Text, subtitle.OutputPath, outputDirectory, context));
                subtitleSegments.Add(TextSegment(materialId, subtitle.ClipId, cue.Index, clip.StartSeconds + cue.StartSeconds, cue.DurationSeconds));
            }
        }

        foreach (var track in context.Timeline.Tracks)
        {
            if (track.Kind == EditingTrackKind.Subtitle)
            {
                if (subtitleSegments.Count > 0)
                {
                    tracks.Add(new JsonObject
                    {
                        ["attribute"] = 0,
                        ["flag"] = 0,
                        ["id"] = Id("track", track.Id),
                        ["is_default_name"] = false,
                        ["type"] = "text",
                        ["name"] = "字幕",
                        ["segments"] = subtitleSegments
                    });
                }

                continue;
            }

            var segments = new JsonArray();
            foreach (var clip in track.Clips.OrderBy(clip => clip.StartSeconds))
            {
                if (string.IsNullOrWhiteSpace(clip.MediaPath))
                {
                    continue;
                }

                var materialId = MaterialId(materialsByPath, clip.MediaPath);
                if (track.Kind == EditingTrackKind.Video)
                {
                    if (!ContainsMaterial(videoMaterials, materialId))
                    {
                        videoMaterials.Add(VideoMaterial(materialId, clip.MediaPath!, clip.DurationSeconds));
                    }
                }
                else if (track.Kind == EditingTrackKind.Audio)
                {
                    if (!ContainsMaterial(audioMaterials, materialId))
                    {
                        audioMaterials.Add(AudioMaterial(materialId, clip.MediaPath!, clip.DurationSeconds));
                    }
                }
                else
                {
                    continue;
                }

                var speedId = Id("speed", clip.Id);
                speedMaterials.Add(SpeedMaterial(speedId));

                segments.Add(track.Kind == EditingTrackKind.Video
                    ? VideoSegment(clip, materialId, speedId)
                    : AudioSegment(clip, materialId, speedId));
            }

            tracks.Add(new JsonObject
            {
                ["attribute"] = 0,
                ["flag"] = 0,
                ["id"] = Id("track", track.Id),
                ["is_default_name"] = false,
                ["type"] = track.Kind.ToString().ToLowerInvariant(),
                ["name"] = track.Name,
                ["segments"] = segments
            });
        }

        var durationSeconds = context.Timeline.Tracks
            .SelectMany(track => track.Clips)
            .Select(clip => clip.StartSeconds + clip.DurationSeconds)
            .DefaultIfEmpty(0)
            .Max();
        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return new JsonObject
        {
            ["canvas_config"] = new JsonObject { ["height"] = 1080, ["ratio"] = "original", ["width"] = 1920 },
            ["color_space"] = 0,
            ["config"] = DraftConfig(),
            ["cover"] = null,
            ["create_time"] = nowSeconds,
            ["duration"] = ToMicroseconds(durationSeconds),
            ["extra_info"] = null,
            ["fps"] = context.Timeline.FramesPerSecond,
            ["free_render_index_mode_on"] = false,
            ["group_container"] = null,
            ["id"] = context.Timeline.Id,
            ["name"] = context.Timeline.Title,
            ["keyframe_graph_list"] = new JsonArray(),
            ["keyframes"] = EmptyKeyframes(),
            ["last_modified_platform"] = Platform(),
            ["platform"] = Platform(),
            ["materials"] = new JsonObject
            {
                ["ai_translates"] = new JsonArray(),
                ["audio_balances"] = new JsonArray(),
                ["audio_effects"] = new JsonArray(),
                ["audio_fades"] = new JsonArray(),
                ["audio_track_indexes"] = new JsonArray(),
                ["audios"] = audioMaterials,
                ["beats"] = new JsonArray(),
                ["canvases"] = new JsonArray(),
                ["chromas"] = new JsonArray(),
                ["color_curves"] = new JsonArray(),
                ["digital_humans"] = new JsonArray(),
                ["drafts"] = new JsonArray(),
                ["effects"] = new JsonArray(),
                ["flowers"] = new JsonArray(),
                ["green_screens"] = new JsonArray(),
                ["handwrites"] = new JsonArray(),
                ["hsl"] = new JsonArray(),
                ["images"] = new JsonArray(),
                ["log_color_wheels"] = new JsonArray(),
                ["loudnesses"] = new JsonArray(),
                ["manual_deformations"] = new JsonArray(),
                ["masks"] = new JsonArray(),
                ["material_animations"] = new JsonArray(),
                ["material_colors"] = new JsonArray(),
                ["multi_language_refs"] = new JsonArray(),
                ["placeholders"] = new JsonArray(),
                ["plugin_effects"] = new JsonArray(),
                ["primary_color_wheels"] = new JsonArray(),
                ["realtime_denoises"] = new JsonArray(),
                ["shapes"] = new JsonArray(),
                ["smart_crops"] = new JsonArray(),
                ["smart_relights"] = new JsonArray(),
                ["sound_channel_mappings"] = new JsonArray(),
                ["speeds"] = speedMaterials,
                ["stickers"] = new JsonArray(),
                ["tail_leaders"] = new JsonArray(),
                ["text_templates"] = new JsonArray(),
                ["texts"] = textMaterials,
                ["time_marks"] = new JsonArray(),
                ["transitions"] = new JsonArray(),
                ["video_effects"] = new JsonArray(),
                ["video_trackings"] = new JsonArray(),
                ["videos"] = videoMaterials,
                ["vocal_beautifys"] = new JsonArray(),
                ["vocal_separations"] = new JsonArray()
            },
            ["mutable_config"] = null,
            ["new_version"] = "110.0.0",
            ["relationships"] = new JsonArray(),
            ["render_index_track_mode_on"] = false,
            ["retouch_cover"] = null,
            ["source"] = "default",
            ["static_cover_image_path"] = "",
            ["time_marks"] = null,
            ["tracks"] = tracks,
            ["update_time"] = nowSeconds,
            ["version"] = 360000
        };
    }

    private static JsonObject BuildDraftMeta(TimelineExportContext context, string outputDirectory)
    {
        var durationSeconds = context.Timeline.Tracks
            .SelectMany(track => track.Clips)
            .Select(clip => clip.StartSeconds + clip.DurationSeconds)
            .DefaultIfEmpty(0)
            .Max();
        return new JsonObject
        {
            ["cloud_package_completed_time"] = "",
            ["draft_cloud_capcut_purchase_info"] = "",
            ["draft_cloud_last_action_download"] = false,
            ["draft_cloud_materials"] = new JsonArray(),
            ["draft_cloud_purchase_info"] = "",
            ["draft_cloud_template_id"] = "",
            ["draft_cloud_tutorial_info"] = "",
            ["draft_cloud_videocut_purchase_info"] = "",
            ["draft_cover"] = "",
            ["draft_deeplink_url"] = "",
            ["draft_enterprise_info"] = new JsonObject
            {
                ["draft_enterprise_extra"] = "",
                ["draft_enterprise_id"] = "",
                ["draft_enterprise_name"] = "",
                ["enterprise_material"] = new JsonArray()
            },
            ["draft_fold_path"] = outputDirectory,
            ["draft_id"] = context.Timeline.Id,
            ["draft_is_ai_packaging_used"] = false,
            ["draft_is_ai_shorts"] = false,
            ["draft_is_ai_translate"] = false,
            ["draft_is_article_video_draft"] = false,
            ["draft_is_from_deeplink"] = "false",
            ["draft_is_invisible"] = false,
            ["draft_materials"] = DraftMetaMaterials(),
            ["draft_materials_copied_info"] = new JsonArray(),
            ["draft_name"] = context.Timeline.Title,
            ["draft_new_version"] = "",
            ["draft_removable_storage_device"] = "",
            ["draft_root_path"] = outputDirectory,
            ["draft_segment_extra_info"] = new JsonArray(),
            ["draft_type"] = "",
            ["tm_draft_cloud_completed"] = "",
            ["tm_draft_cloud_modified"] = 0,
            ["tm_draft_removed"] = 0,
            ["tm_duration"] = ToMicroseconds(durationSeconds)
        };
    }

    private static JsonObject Timerange(double startSeconds, double durationSeconds)
    {
        return new JsonObject
        {
            ["start"] = ToMicroseconds(startSeconds),
            ["duration"] = ToMicroseconds(durationSeconds)
        };
    }

    private static long ToMicroseconds(double seconds)
    {
        return (long)Math.Round(Math.Max(0, seconds) * 1_000_000d);
    }

    private static JsonObject VideoMaterial(string id, string path, double durationSeconds)
    {
        return new JsonObject
        {
            ["audio_fade"] = null,
            ["category_id"] = "",
            ["category_name"] = "local",
            ["check_flag"] = 63487,
            ["crop"] = Crop(),
            ["crop_ratio"] = "free",
            ["crop_scale"] = 1.0,
            ["duration"] = IsImagePath(path) ? 10_800_000_000L : ToMicroseconds(durationSeconds),
            ["height"] = 1080,
            ["id"] = id,
            ["local_material_id"] = "",
            ["material_id"] = id,
            ["material_name"] = Path.GetFileName(path),
            ["media_path"] = "",
            ["path"] = Path.GetFullPath(path),
            ["type"] = IsImagePath(path) ? "photo" : "video",
            ["width"] = 1920
        };
    }

    private static JsonObject AudioMaterial(string id, string path, double durationSeconds)
    {
        return new JsonObject
        {
            ["app_id"] = 0,
            ["category_id"] = "",
            ["category_name"] = "local",
            ["check_flag"] = 3,
            ["copyright_limit_type"] = "none",
            ["duration"] = ToMicroseconds(durationSeconds),
            ["effect_id"] = "",
            ["formula_id"] = "",
            ["id"] = id,
            ["local_material_id"] = id,
            ["music_id"] = id,
            ["name"] = Path.GetFileName(path),
            ["path"] = Path.GetFullPath(path),
            ["source_platform"] = 0,
            ["type"] = "extract_music",
            ["wave_points"] = new JsonArray()
        };
    }

    private static JsonObject SpeedMaterial(string id)
    {
        return new JsonObject
        {
            ["curve_speed"] = null,
            ["id"] = id,
            ["mode"] = 0,
            ["speed"] = 1.0,
            ["type"] = "speed"
        };
    }

    private static JsonObject VideoSegment(EditingClip clip, string materialId, string speedId)
    {
        var json = MediaSegment(clip, materialId, speedId);
        json["clip"] = ClipSettings();
        json["uniform_scale"] = new JsonObject { ["on"] = true, ["value"] = 1.0 };
        json["render_index"] = clip.Metadata.TryGetValue("visualRole", out var role)
            ? role.Trim().ToLowerInvariant() switch
            {
                "scene" => 1,
                "narration_scene_effect" => 2,
                _ => 3
            }
            : 3;
        return json;
    }

    private static JsonObject AudioSegment(EditingClip clip, string materialId, string speedId)
    {
        var json = MediaSegment(clip, materialId, speedId);
        json["clip"] = null;
        json["hdr_settings"] = null;
        json["render_index"] = 0;
        return json;
    }

    private static JsonObject MediaSegment(EditingClip clip, string materialId, string speedId)
    {
        return new JsonObject
        {
            ["enable_adjust"] = true,
            ["enable_color_correct_adjust"] = false,
            ["enable_color_curves"] = true,
            ["enable_color_match_adjust"] = false,
            ["enable_color_wheels"] = true,
            ["enable_lut"] = true,
            ["enable_smart_color_adjust"] = false,
            ["last_nonzero_volume"] = 1.0,
            ["reverse"] = false,
            ["track_attribute"] = 0,
            ["track_render_index"] = 0,
            ["visible"] = true,
            ["id"] = Id("segment", clip.Id),
            ["material_id"] = materialId,
            ["target_timerange"] = Timerange(clip.StartSeconds, clip.DurationSeconds),
            ["common_keyframes"] = new JsonArray(),
            ["keyframe_refs"] = new JsonArray(),
            ["source_timerange"] = Timerange(0, clip.DurationSeconds),
            ["speed"] = 1.0,
            ["volume"] = 1.0,
            ["extra_material_refs"] = new JsonArray(speedId),
            ["is_tone_modify"] = false
        };
    }

    private static JsonObject TextMaterial(string id, string text, string srtPath, string outputDirectory, TimelineExportContext context)
    {
        var content = new JsonObject
        {
            ["styles"] = new JsonArray(
                new JsonObject
                {
                    ["fill"] = new JsonObject
                    {
                        ["alpha"] = 1.0,
                        ["content"] = new JsonObject
                        {
                            ["render_type"] = "solid",
                            ["solid"] = new JsonObject
                            {
                                ["alpha"] = 1.0,
                                ["color"] = new JsonArray(1.0, 1.0, 1.0)
                            }
                        }
                    },
                    ["range"] = new JsonArray(0, text.Length),
                    ["size"] = 5.0,
                    ["bold"] = false,
                    ["italic"] = false,
                    ["underline"] = false,
                    ["strokes"] = new JsonArray()
                }),
            ["text"] = text
        };

        return new JsonObject
        {
            ["id"] = id,
            ["content"] = content.ToJsonString(DraftJson.Options),
            ["typesetting"] = 0,
            ["alignment"] = 1,
            ["letter_spacing"] = 0.0,
            ["line_spacing"] = 0.02,
            ["line_feed"] = 1,
            ["line_max_width"] = 0.82,
            ["force_apply_line_max_width"] = false,
            ["check_flag"] = 7,
            ["type"] = "subtitle",
            ["global_alpha"] = 1.0,
            ["draft_agent_srt_path"] = FormatDescriptionPath(context, outputDirectory, srtPath)
        };
    }

    private static JsonObject TextSegment(string materialId, string clipId, int cueIndex, double startSeconds, double durationSeconds)
    {
        return new JsonObject
        {
            ["enable_adjust"] = true,
            ["enable_color_correct_adjust"] = false,
            ["enable_color_curves"] = true,
            ["enable_color_match_adjust"] = false,
            ["enable_color_wheels"] = true,
            ["enable_lut"] = true,
            ["enable_smart_color_adjust"] = false,
            ["last_nonzero_volume"] = 1.0,
            ["reverse"] = false,
            ["track_attribute"] = 0,
            ["track_render_index"] = 0,
            ["visible"] = true,
            ["id"] = Id("text_segment", $"{clipId}_{cueIndex}"),
            ["material_id"] = materialId,
            ["target_timerange"] = Timerange(startSeconds, durationSeconds),
            ["common_keyframes"] = new JsonArray(),
            ["keyframe_refs"] = new JsonArray(),
            ["source_timerange"] = null,
            ["speed"] = 1.0,
            ["volume"] = 1.0,
            ["extra_material_refs"] = new JsonArray(),
            ["is_tone_modify"] = false,
            ["clip"] = new JsonObject
            {
                ["alpha"] = 1.0,
                ["flip"] = new JsonObject { ["horizontal"] = false, ["vertical"] = false },
                ["rotation"] = 0.0,
                ["scale"] = new JsonObject { ["x"] = 1.0, ["y"] = 1.0 },
                ["transform"] = new JsonObject { ["x"] = 0.0, ["y"] = -0.8 }
            },
            ["uniform_scale"] = new JsonObject { ["on"] = true, ["value"] = 1.0 },
            ["render_index"] = 15000
        };
    }

    private static JsonObject DraftConfig()
    {
        return new JsonObject
        {
            ["adjust_max_index"] = 1,
            ["attachment_info"] = new JsonArray(),
            ["combination_max_index"] = 1,
            ["export_range"] = null,
            ["extract_audio_last_index"] = 1,
            ["lyrics_recognition_id"] = "",
            ["lyrics_sync"] = true,
            ["lyrics_taskinfo"] = new JsonArray(),
            ["maintrack_adsorb"] = true,
            ["material_save_mode"] = 0,
            ["multi_language_current"] = "none",
            ["multi_language_list"] = new JsonArray(),
            ["multi_language_main"] = "none",
            ["multi_language_mode"] = "none",
            ["original_sound_last_index"] = 1,
            ["record_audio_last_index"] = 1,
            ["sticker_max_index"] = 1,
            ["subtitle_keywords_config"] = null,
            ["subtitle_recognition_id"] = "",
            ["subtitle_sync"] = true,
            ["subtitle_taskinfo"] = new JsonArray(),
            ["system_font_list"] = new JsonArray(),
            ["video_mute"] = false,
            ["zoom_info_params"] = null
        };
    }

    private static JsonObject EmptyKeyframes()
    {
        return new JsonObject
        {
            ["adjusts"] = new JsonArray(),
            ["audios"] = new JsonArray(),
            ["effects"] = new JsonArray(),
            ["filters"] = new JsonArray(),
            ["handwrites"] = new JsonArray(),
            ["stickers"] = new JsonArray(),
            ["texts"] = new JsonArray(),
            ["videos"] = new JsonArray()
        };
    }

    private static JsonObject Platform()
    {
        return new JsonObject
        {
            ["app_id"] = 3704,
            ["app_source"] = "lv",
            ["app_version"] = "5.9.0",
            ["os"] = "windows"
        };
    }

    private static JsonArray DraftMetaMaterials()
    {
        return new JsonArray(
            DraftMetaMaterial(0),
            DraftMetaMaterial(1),
            DraftMetaMaterial(2),
            DraftMetaMaterial(3),
            DraftMetaMaterial(6),
            DraftMetaMaterial(7),
            DraftMetaMaterial(8));
    }

    private static JsonObject DraftMetaMaterial(int type)
    {
        return new JsonObject { ["type"] = type, ["value"] = new JsonArray() };
    }

    private static JsonObject Crop()
    {
        return new JsonObject
        {
            ["upper_left_x"] = 0.0,
            ["upper_left_y"] = 0.0,
            ["upper_right_x"] = 1.0,
            ["upper_right_y"] = 0.0,
            ["lower_left_x"] = 0.0,
            ["lower_left_y"] = 1.0,
            ["lower_right_x"] = 1.0,
            ["lower_right_y"] = 1.0
        };
    }

    private static JsonObject ClipSettings()
    {
        return new JsonObject
        {
            ["alpha"] = 1.0,
            ["flip"] = new JsonObject { ["horizontal"] = false, ["vertical"] = false },
            ["rotation"] = 0.0,
            ["scale"] = new JsonObject { ["x"] = 1.0, ["y"] = 1.0 },
            ["transform"] = new JsonObject { ["x"] = 0.0, ["y"] = 0.0 }
        };
    }

    private static string MaterialId(IDictionary<string, string> materialsByPath, string? path)
    {
        var fullPath = Path.GetFullPath(path ?? string.Empty);
        if (materialsByPath.TryGetValue(fullPath, out var existing))
        {
            return existing;
        }

        var id = Id("material", fullPath);
        materialsByPath[fullPath] = id;
        return id;
    }

    private static bool ContainsMaterial(JsonArray materials, string materialId)
    {
        return materials
            .OfType<JsonObject>()
            .Any(material => string.Equals(material["id"]?.GetValue<string>(), materialId, StringComparison.Ordinal));
    }

    private static string Id(string prefix, string value)
    {
        var sanitized = new string(value.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray()).Trim('_');
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = Guid.NewGuid().ToString("N");
        }

        return $"{prefix}_{sanitized}";
    }

    private static bool IsImagePath(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp";
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

    private static string FormatDescriptionPath(
        TimelineExportContext context,
        string outputDirectory,
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var fullPath = Path.GetFullPath(path);
        if (context.Options.UseRelativeMediaReferences && IsInside(context.Workspace.RootPath, fullPath))
        {
            return Path.GetRelativePath(outputDirectory, fullPath).Replace('\\', '/');
        }

        return fullPath;
    }

    private static bool IsInside(string rootPath, string candidatePath)
    {
        var root = Path.GetFullPath(rootPath);
        var candidate = Path.GetFullPath(candidatePath);
        var relative = Path.GetRelativePath(root, candidate);
        return relative == "." ||
            (!relative.StartsWith("..", StringComparison.Ordinal) &&
             !Path.IsPathRooted(relative));
    }

    private static string ResolveOutputDirectory(TimelineExportContext context)
    {
        var outputPath = context.OutputPath;
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.Combine(context.Workspace.Folders.Exports, $"{SanitizeFileName(context.Timeline.Title)}_剪映草稿");
        }

        return Path.HasExtension(outputPath)
            ? Path.Combine(Path.GetDirectoryName(outputPath)!, Path.GetFileNameWithoutExtension(outputPath))
            : outputPath;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "draft-agent-timeline" : cleaned;
    }
}
