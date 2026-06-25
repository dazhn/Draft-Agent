using System.Globalization;
using System.Text;

namespace DraftAgent.Core;

public sealed class WorkbenchTimelineAssembler
{
    private const string SubtitleTrackId = "track_subtitle";
    private const string CharacterVideoTrackId = "track_character_visual";
    private const string CharacterDialogueVoiceTrackId = "track_character_dialogue_voice";
    private const string CharacterThoughtVoiceTrackId = "track_character_thought_voice";
    private const string CharacterActionVoiceTrackId = "track_character_action_voice";
    private const string NarrationSceneEffectVideoTrackId = "track_narration_scene_effect_visual";
    private const string NarrationSceneEffectAudioTrackId = "track_narration_scene_effect_audio";
    private const string SceneVideoTrackId = "track_scene_visual";

    private readonly IWorkflowLogger _logger;
    private readonly IProjectAssetResolver _assetResolver;

    public WorkbenchTimelineAssembler(IWorkflowLogger? logger = null, IProjectAssetResolver? assetResolver = null)
    {
        _logger = logger ?? new InMemoryWorkflowLogger();
        _assetResolver = assetResolver ?? new ProjectAssetResolver();
    }

    public EditingTimeline Assemble(InteractionProject project, WorkspaceProject workspace, TimelineExportOptions options, WorkspaceProjectManifest? manifest = null)
    {
        _logger.Info(
            WorkflowSteps.TimelineExport,
            "timeline_assemble_started",
            "开始把工作台投影为剪辑时间线。",
            new Dictionary<string, string>
            {
                ["projectId"] = project.ProjectId,
                ["sceneCount"] = project.Scenes.Count.ToString(CultureInfo.InvariantCulture),
                ["fps"] = options.FramesPerSecond.ToString("0.###", CultureInfo.InvariantCulture)
            });

        var characterVisualClips = new List<EditingClip>();
        var characterDialogueVoiceClips = new List<EditingClip>();
        var characterThoughtVoiceClips = new List<EditingClip>();
        var characterActionVoiceClips = new List<EditingClip>();
        var narrationSceneEffectVisualClips = new List<EditingClip>();
        var narrationSceneEffectAudioClips = new List<EditingClip>();
        var sceneVisualClips = new List<EditingClip>();
        var subtitleClips = new List<EditingClip>();
        var markers = new List<EditingMarker>();
        var characterNames = project.Characters.ToDictionary(character => character.Id, character => character.Name, StringComparer.Ordinal);
        var visualPreflightIssues = new List<EditingTimelinePreflightIssue>();
        var audioPreflightIssues = new List<EditingTimelinePreflightIssue>();
        var currentSeconds = 0d;

        foreach (var scene in project.Scenes.OrderBy(scene => scene.Order))
        {
            var sceneStartSeconds = currentSeconds;

            foreach (var interaction in scene.Interactions.OrderBy(interaction => interaction.Order))
            {
                var audioAsset = ResolveAudioAsset(project, scene, interaction, workspace, manifest, options.RequireAudioForTextRows, audioPreflightIssues);
                var duration = ResolveDuration(interaction, options.DefaultStillDurationSeconds, audioAsset?.AbsolutePath);
                var metadata = BuildInteractionMetadata(scene, interaction, characterNames);
                var characterVisualAsset = ResolveCharacterVisualAsset(project, scene, interaction, workspace, manifest, visualPreflightIssues);
                if (ShouldCreateCharacterVisualClip(interaction, characterVisualAsset))
                {
                    characterVisualClips.Add(new EditingClip(
                        $"character_visual_{interaction.Id}",
                        $"{scene.Order:00}-{interaction.Order:000} {ResolveCharacterVisualClipName(interaction, characterNames)}",
                        currentSeconds,
                        duration,
                        null,
                        characterVisualAsset,
                        metadata));
                }

                var narrationSceneEffectVisualAsset = ResolveNarrationSceneEffectVisualAsset(scene, interaction, workspace, manifest, visualPreflightIssues);
                if (ShouldCreateNarrationSceneEffectVisualClip(interaction, narrationSceneEffectVisualAsset))
                {
                    narrationSceneEffectVisualClips.Add(new EditingClip(
                        $"narration_scene_effect_visual_{interaction.Id}",
                        $"{scene.Order:00}-{interaction.Order:000} {ResolveNarrationSceneEffectVisualClipName(interaction)}",
                        currentSeconds,
                        duration,
                        null,
                        narrationSceneEffectVisualAsset,
                        metadata));
                }

                if (audioAsset is not null)
                {
                    var audioClip = new EditingClip(
                        $"audio_{interaction.Id}",
                        $"{scene.Order:00}-{interaction.Order:000} {ResolveAudioClipName(interaction, characterNames)}",
                        currentSeconds,
                        duration,
                        null,
                        audioAsset.AbsolutePath,
                        WithAudioTaskMetadata(metadata, audioAsset.TaskId, interaction));
                    AddAudioClipToTrack(
                        interaction,
                        audioClip,
                        characterDialogueVoiceClips,
                        characterThoughtVoiceClips,
                        characterActionVoiceClips,
                        narrationSceneEffectAudioClips);
                    subtitleClips.AddRange(BuildSubtitleClips(scene, interaction, audioClip, metadata));
                }

                currentSeconds += duration;
            }

            var sceneDuration = currentSeconds - sceneStartSeconds;
            if (sceneDuration <= 0)
            {
                sceneDuration = Math.Clamp(options.DefaultStillDurationSeconds, 1, 30);
                currentSeconds += sceneDuration;
            }

            var sceneMetadata = BuildSceneMetadata(scene);
            var visualAsset = ResolveSceneVisualAsset(project, scene, workspace, manifest, visualPreflightIssues);
            sceneVisualClips.Add(new EditingClip(
                $"scene_visual_{scene.Id}",
                $"{scene.Order:00} {ResolveSceneVisualClipName(scene)}",
                sceneStartSeconds,
                sceneDuration,
                null,
                visualAsset,
                sceneMetadata));

            markers.Add(new EditingMarker(
                $"marker_{scene.Id}",
                scene.Name,
                sceneStartSeconds,
                sceneDuration,
                "BLUE",
                sceneMetadata));
        }

        if (audioPreflightIssues.Count > 0)
        {
            throw new EditingTimelinePreflightException(audioPreflightIssues.ToArray());
        }

        if (visualPreflightIssues.Count > 0 && !options.AllowMissingVisuals)
        {
            throw new EditingTimelinePreflightException(visualPreflightIssues);
        }

        var timeline = new EditingTimeline(
            project.ProjectId,
            project.Title,
            options.FramesPerSecond,
            [
                new EditingTrack(SubtitleTrackId, EditingTrackKind.Subtitle, "Subtitles", subtitleClips, new Dictionary<string, string> { ["trackRole"] = "subtitle" }),
                new EditingTrack(CharacterVideoTrackId, EditingTrackKind.Video, "Character Visual", characterVisualClips, new Dictionary<string, string> { ["visualRole"] = "character" }),
                new EditingTrack(CharacterDialogueVoiceTrackId, EditingTrackKind.Audio, "Character Dialogue Voice", characterDialogueVoiceClips, new Dictionary<string, string> { ["trackRole"] = "character_dialogue_voice" }),
                new EditingTrack(CharacterThoughtVoiceTrackId, EditingTrackKind.Audio, "Character Thought Voice", characterThoughtVoiceClips, new Dictionary<string, string> { ["trackRole"] = "character_thought_voice" }),
                new EditingTrack(CharacterActionVoiceTrackId, EditingTrackKind.Audio, "Character Action Voice", characterActionVoiceClips, new Dictionary<string, string> { ["trackRole"] = "character_action_voice" }),
                new EditingTrack(NarrationSceneEffectVideoTrackId, EditingTrackKind.Video, "Narration Scene Effect Visual", narrationSceneEffectVisualClips, new Dictionary<string, string> { ["visualRole"] = "narration_scene_effect" }),
                new EditingTrack(NarrationSceneEffectAudioTrackId, EditingTrackKind.Audio, "Narration Scene Effect Voice", narrationSceneEffectAudioClips, new Dictionary<string, string> { ["trackRole"] = "narration_scene_effect_voice" }),
                new EditingTrack(SceneVideoTrackId, EditingTrackKind.Video, "Scene Visual", sceneVisualClips, new Dictionary<string, string> { ["visualRole"] = "scene" }),
            ],
            markers,
            new Dictionary<string, string>
            {
                ["schema"] = DraftWorkspaceService.SceneInteractionSchemaName,
                ["projectId"] = project.ProjectId,
                ["workspaceProjectId"] = workspace.ProjectId,
                ["sceneCount"] = project.Scenes.Count.ToString(CultureInfo.InvariantCulture),
                ["durationSeconds"] = Math.Round(currentSeconds, 3).ToString("0.###", CultureInfo.InvariantCulture)
            });

        _logger.Info(
            WorkflowSteps.TimelineExport,
            "timeline_assemble_completed",
            "工作台剪辑时间线投影完成。",
            new Dictionary<string, string>
            {
                ["clipCount"] = timeline.Tracks.Sum(track => track.Clips.Count).ToString(CultureInfo.InvariantCulture),
                ["markerCount"] = timeline.Markers.Count.ToString(CultureInfo.InvariantCulture),
                ["durationSeconds"] = timeline.Metadata["durationSeconds"]
            });

        return timeline;
    }

    private static IReadOnlyDictionary<string, string> BuildSceneMetadata(InteractionScene scene)
    {
        return new Dictionary<string, string>
        {
            ["sceneId"] = scene.Id,
            ["sceneOrder"] = scene.Order.ToString(CultureInfo.InvariantCulture),
            ["sceneName"] = scene.Name,
            ["sceneLocation"] = scene.Location,
            ["sceneTimeOfDay"] = scene.TimeOfDay,
            ["sceneWeather"] = scene.Weather,
            ["sceneMood"] = scene.Mood,
            ["sceneEffect"] = scene.Description,
            ["interactionId"] = string.Empty,
            ["text"] = string.Empty
        };
    }

    private static IReadOnlyDictionary<string, string> BuildInteractionMetadata(
        InteractionScene scene,
        CharacterInteraction interaction,
        IReadOnlyDictionary<string, string> characterNames)
    {
        return new Dictionary<string, string>
        {
            ["sceneId"] = scene.Id,
            ["sceneOrder"] = scene.Order.ToString(CultureInfo.InvariantCulture),
            ["sceneName"] = scene.Name,
            ["sceneEffect"] = scene.Description,
            ["interactionId"] = interaction.Id,
            ["interactionOrder"] = interaction.Order.ToString(CultureInfo.InvariantCulture),
            ["interactionType"] = interaction.Type.ToString(),
            ["subjectCharacterId"] = interaction.SubjectCharacterId ?? string.Empty,
            ["subjectCharacterName"] = interaction.SubjectCharacterId is not null && characterNames.TryGetValue(interaction.SubjectCharacterId, out var name) ? name : string.Empty,
            ["emotion"] = interaction.Emotion.Emotion,
            ["text"] = InteractionText(interaction),
            ["rowTaskIds"] = string.Join(",", interaction.Tasks.Select(task => task.Id)),
            ["sourceSegmentId"] = interaction.SourceSegmentId
        };
    }

    private static IReadOnlyDictionary<string, string> WithAudioTaskMetadata(
        IReadOnlyDictionary<string, string> metadata,
        string? taskId,
        CharacterInteraction interaction)
    {
        var values = new Dictionary<string, string>(metadata, StringComparer.Ordinal)
        {
            ["audioRole"] = AudioRole(interaction)
        };

        if (!string.IsNullOrWhiteSpace(taskId))
        {
            values["ttsTaskId"] = taskId;
        }

        return values;
    }

    private static IReadOnlyList<EditingClip> BuildSubtitleClips(
        InteractionScene scene,
        CharacterInteraction interaction,
        EditingClip audioClip,
        IReadOnlyDictionary<string, string> metadata)
    {
        var text = InteractionText(interaction);
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var cues = EditingTimelineSubtitleBuilder.BuildCues(text, audioClip.DurationSeconds);
        return cues
            .Select(cue => new EditingClip(
                $"subtitle_{interaction.Id}_{cue.Index:000}",
                $"{scene.Order:00}-{interaction.Order:000} 字幕 {cue.Index}",
                audioClip.StartSeconds + cue.StartSeconds,
                cue.DurationSeconds,
                null,
                null,
                WithSubtitleMetadata(metadata, audioClip, cue, text)))
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string> WithSubtitleMetadata(
        IReadOnlyDictionary<string, string> metadata,
        EditingClip audioClip,
        SubtitleCue cue,
        string originalText)
    {
        var values = new Dictionary<string, string>(metadata, StringComparer.Ordinal)
        {
            ["text"] = cue.Text,
            ["originalText"] = originalText,
            ["subtitleIndex"] = cue.Index.ToString(CultureInfo.InvariantCulture),
            ["sourceClipId"] = audioClip.Id,
            ["sourceClipName"] = audioClip.Name
        };
        return values;
    }

    private static string InteractionText(CharacterInteraction interaction)
    {
        return interaction.Dialogue?.Text
            ?? interaction.Narration?.Text
            ?? interaction.InnerMonologue?.Text
            ?? interaction.Action.MainAction
            ?? string.Empty;
    }

    private static string AudioRole(CharacterInteraction interaction)
    {
        return interaction.Narration is not null ||
            interaction.InnerMonologue is not null ||
            interaction.Type is InteractionType.Narration or InteractionType.InnerMonologue
            ? "voiceover"
            : "dialogue";
    }

    private static void AddAudioClipToTrack(
        CharacterInteraction interaction,
        EditingClip audioClip,
        ICollection<EditingClip> dialogueClips,
        ICollection<EditingClip> thoughtClips,
        ICollection<EditingClip> actionClips,
        ICollection<EditingClip> narrationSceneEffectClips)
    {
        if (IsNarrationSceneEffectInteraction(interaction))
        {
            narrationSceneEffectClips.Add(audioClip);
            return;
        }

        switch (ResolveCharacterAudioTrackRole(interaction))
        {
            case "character_thought_voice":
                thoughtClips.Add(audioClip);
                break;
            case "character_action_voice":
                actionClips.Add(audioClip);
                break;
            default:
                dialogueClips.Add(audioClip);
                break;
        }
    }

    private static string ResolveCharacterAudioTrackRole(CharacterInteraction interaction)
    {
        var sourceItemType = interaction.SourceItemType.Trim().ToLowerInvariant();
        if (sourceItemType == "thought" ||
            interaction.InnerMonologue is not null ||
            interaction.Type == InteractionType.InnerMonologue)
        {
            return "character_thought_voice";
        }

        if (sourceItemType == "action")
        {
            return "character_action_voice";
        }

        return "character_dialogue_voice";
    }

    private static bool IsNarrationSceneEffectInteraction(CharacterInteraction interaction)
    {
        var sourceItemType = interaction.SourceItemType.Trim().ToLowerInvariant();
        if (sourceItemType is "effects" or "narration")
        {
            return true;
        }

        if (sourceItemType is "dialogue" or "action" or "thought")
        {
            return false;
        }

        return !HasCharacterVisualIdentity(interaction) &&
            interaction.Type == InteractionType.Narration;
    }

    private static double ResolveDuration(CharacterInteraction interaction, double defaultStillDurationSeconds, string? audioPath)
    {
        if (AudioDurationReader.TryReadSeconds(audioPath, out var audioDurationSeconds))
        {
            return Math.Max(audioDurationSeconds, 0.001);
        }

        var pause = interaction.Dialogue?.Voice.PauseAfterSeconds
            ?? interaction.Narration?.Voice.PauseAfterSeconds
            ?? interaction.Action.Duration;
        return Math.Clamp(Math.Max(interaction.Action.Duration, defaultStillDurationSeconds + pause), 1, 30);
    }

    private string? ResolveSceneVisualAsset(
        InteractionProject project,
        InteractionScene scene,
        WorkspaceProject workspace,
        WorkspaceProjectManifest? manifest,
        ICollection<EditingTimelinePreflightIssue> preflightIssues)
    {
        var hasExplicitSceneBinding = IsExplicitVisualBinding(scene.AssetBinding.BackgroundVisualBinding);
        var references = (hasExplicitSceneBinding
                ? VisualReferences(scene.AssetBinding.BackgroundVisualBinding)
                : VisualReferences(null, scene.AssetBinding.BackgroundImage))
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var resolution = FirstExportableResolution(
            references,
            workspace,
            manifest,
            out var blockedPsd);

        var issues = new List<EditingTimelinePreflightIssue>();
        if (blockedPsd is not null && resolution is null)
        {
            issues.Add(EditingTimelinePreflightIssue.PsdReference(
                scene.Id,
                string.Empty,
                blockedPsd.OriginalReference));
        }
        else if (resolution is null && references.Length > 0)
        {
            issues.Add(EditingTimelinePreflightIssue.MissingVisual(
                scene.Id,
                string.Empty,
                string.Join(" | ", references)));
        }

        foreach (var issue in issues)
        {
            preflightIssues.Add(issue);
        }

        return resolution?.AbsolutePath;
    }

    private string? ResolveCharacterVisualAsset(
        InteractionProject project,
        InteractionScene scene,
        CharacterInteraction interaction,
        WorkspaceProject workspace,
        WorkspaceProjectManifest? manifest,
        ICollection<EditingTimelinePreflightIssue> preflightIssues)
    {
        if (!IsCharacterVisualInteraction(interaction))
        {
            return null;
        }

        var subjectCharacter = SubjectCharacter(project, interaction);
        var hasExplicitRowBinding = IsExplicitVisualBinding(interaction.Binding.SubjectCharacterVisualBinding);
        var references = (hasExplicitRowBinding
                ? VisualReferences(interaction.Binding.SubjectCharacterVisualBinding)
                : VisualReferences(subjectCharacter?.AssetBinding.PortraitVisualBinding, subjectCharacter?.AssetBinding.Portrait))
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var resolution = FirstExportableResolution(
            references,
            workspace,
            manifest,
            out var blockedPsd);

        var issues = new List<EditingTimelinePreflightIssue>();
        if (blockedPsd is not null && resolution is null)
        {
            issues.Add(EditingTimelinePreflightIssue.PsdReference(scene.Id, interaction.Id, blockedPsd.OriginalReference));
        }
        else if (resolution is null && references.Length > 0)
        {
            issues.Add(EditingTimelinePreflightIssue.MissingVisual(
                scene.Id,
                interaction.Id,
                string.Join(" | ", references)));
        }

        foreach (var issue in issues)
        {
            preflightIssues.Add(issue);
        }

        return resolution?.AbsolutePath;
    }

    private string? ResolveNarrationSceneEffectVisualAsset(
        InteractionScene scene,
        CharacterInteraction interaction,
        WorkspaceProject workspace,
        WorkspaceProjectManifest? manifest,
        ICollection<EditingTimelinePreflightIssue> preflightIssues)
    {
        if (!IsNarrationSceneEffectInteraction(interaction))
        {
            return null;
        }

        var references = VisualReferences(interaction.Binding.SubjectCharacterVisualBinding)
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var resolution = FirstExportableResolution(
            references,
            workspace,
            manifest,
            out var blockedPsd);

        var issues = new List<EditingTimelinePreflightIssue>();
        if (blockedPsd is not null && resolution is null)
        {
            issues.Add(EditingTimelinePreflightIssue.PsdReference(scene.Id, interaction.Id, blockedPsd.OriginalReference));
        }
        else if (resolution is null && references.Length > 0)
        {
            issues.Add(EditingTimelinePreflightIssue.MissingVisual(
                scene.Id,
                interaction.Id,
                string.Join(" | ", references)));
        }

        foreach (var issue in issues)
        {
            preflightIssues.Add(issue);
        }

        return resolution?.AbsolutePath;
    }

    private AudioAssetResolution? ResolveAudioAsset(
        InteractionProject project,
        InteractionScene scene,
        CharacterInteraction interaction,
        WorkspaceProject workspace,
        WorkspaceProjectManifest? manifest,
        bool requireAudioForTextRows,
        ICollection<EditingTimelinePreflightIssue> preflightIssues)
    {
        var issueCountBefore = preflightIssues.Count;
        var foundCompletedTask = false;
        foreach (var task in CurrentAudioTasks(project, interaction))
        {
            if (task.Status != GenerationTaskStatus.Completed)
            {
                continue;
            }

            foundCompletedTask = true;
            foreach (var taskAsset in AudioAssetReferences(task))
            {
                var audioResolution = FirstExportableAudioResolution(taskAsset, workspace, manifest);
                if (audioResolution is not null)
                {
                    if (!IsSupportedAudioDurationFormat(audioResolution.AbsolutePath))
                    {
                        preflightIssues.Add(EditingTimelinePreflightIssue.UnsupportedCompletedAudioDuration(
                            scene.Id,
                            interaction.Id,
                            task.Id,
                            taskAsset,
                            task.Status));
                        return null;
                    }

                    if (requireAudioForTextRows &&
                        !AudioDurationReader.TryReadSeconds(audioResolution.AbsolutePath, out _))
                    {
                        preflightIssues.Add(EditingTimelinePreflightIssue.UnsupportedCompletedAudioDuration(
                            scene.Id,
                            interaction.Id,
                            task.Id,
                            taskAsset,
                            task.Status));
                        return null;
                    }

                    return new AudioAssetResolution(audioResolution.AbsolutePath, task.Id);
                }
            }

            preflightIssues.Add(EditingTimelinePreflightIssue.MissingCompletedAudio(
                scene.Id,
                interaction.Id,
                task.Id,
                string.Join(" | ", AudioAssetReferences(task)),
                task.Status));
        }

        if (requireAudioForTextRows &&
            preflightIssues.Count == issueCountBefore &&
            !foundCompletedTask &&
            InteractionNeedsAudio(interaction))
        {
            preflightIssues.Add(EditingTimelinePreflightIssue.MissingRequiredAudio(
                scene.Id,
                interaction.Id,
                InteractionText(interaction)));
        }

        return null;
    }

    private static bool InteractionNeedsAudio(CharacterInteraction interaction)
    {
        return !string.IsNullOrWhiteSpace(InteractionText(interaction));
    }

    private static IReadOnlyList<string> AudioAssetReferences(GenerationTask task)
    {
        return TtsGeneratedAudioReference.ResolveCandidates(task);
    }

    private string? FirstExistingPath(IEnumerable<string?> values, WorkspaceProject workspace, WorkspaceProjectManifest? manifest)
    {
        return values
            .Select(value => _assetResolver.ResolvePath(value, workspace, manifest))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private string? FirstExistingPath(string? first, WorkspaceProject workspace, WorkspaceProjectManifest? manifest)
    {
        return FirstExistingPath([first], workspace, manifest);
    }

    private ProjectAssetResolution? FirstExportableAudioResolution(
        string? reference,
        WorkspaceProject workspace,
        WorkspaceProjectManifest? manifest)
    {
        var resolution = _assetResolver.Resolve(reference, workspace, manifest);
        if (resolution is null)
        {
            return null;
        }

        return IsProjectOwnedResolution(resolution, workspace) ? resolution : null;
    }

    private static bool IsProjectOwnedResolution(ProjectAssetResolution resolution, WorkspaceProject workspace)
    {
        return resolution.Asset is not null || IsInsideWorkspacePath(resolution.AbsolutePath, workspace.RootPath);
    }

    private ProjectAssetResolution? FirstExportableResolution(
        IEnumerable<string?> references,
        WorkspaceProject workspace,
        WorkspaceProjectManifest? manifest,
        out ProjectAssetResolution? blockedPsd)
    {
        blockedPsd = null;
        foreach (var reference in references)
        {
            var resolution = _assetResolver.Resolve(reference, workspace, manifest);
            if (resolution is null)
            {
                continue;
            }

            if (IsPsdVisualAsset(resolution))
            {
                blockedPsd ??= resolution;
                continue;
            }

            return resolution;
        }

        return null;
    }

    private static IEnumerable<GenerationTask> CurrentAudioTasks(InteractionProject project, CharacterInteraction interaction)
    {
        var yieldedTaskIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var task in interaction.Tasks.Where(task => task.Type == GenerationTaskType.Tts))
        {
            if (yieldedTaskIds.Add(task.Id))
            {
                yield return task;
            }
        }

        foreach (var task in project.GenerationTasks.Where(task =>
            task.Type == GenerationTaskType.Tts &&
            string.Equals(task.TargetId, interaction.Id, StringComparison.Ordinal)))
        {
            if (yieldedTaskIds.Add(task.Id))
            {
                yield return task;
            }
        }
    }

    private static Character? SubjectCharacter(InteractionProject project, CharacterInteraction interaction)
    {
        foreach (var characterId in CharacterVisualIdentityIds(interaction))
        {
            var character = project.Characters.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, characterId, StringComparison.Ordinal));
            if (character is not null)
            {
                return character;
            }
        }

        if (!string.IsNullOrWhiteSpace(interaction.SourceRole))
        {
            return project.Characters.FirstOrDefault(character =>
                string.Equals(character.Name, interaction.SourceRole.Trim(), StringComparison.Ordinal));
        }

        return null;
    }

    private static bool ShouldCreateCharacterVisualClip(CharacterInteraction interaction, string? characterVisualAsset)
    {
        return !string.IsNullOrWhiteSpace(characterVisualAsset) &&
            IsCharacterVisualInteraction(interaction);
    }

    private static bool ShouldCreateNarrationSceneEffectVisualClip(CharacterInteraction interaction, string? visualAsset)
    {
        return !string.IsNullOrWhiteSpace(visualAsset) &&
            IsNarrationSceneEffectInteraction(interaction);
    }

    private static bool IsCharacterVisualInteraction(CharacterInteraction interaction)
    {
        var sourceItemType = interaction.SourceItemType.Trim().ToLowerInvariant();
        return sourceItemType switch
        {
            "dialogue" or "action" or "thought" => HasCharacterVisualIdentity(interaction),
            "effects" or "narration" => false,
            _ => HasCharacterVisualIdentity(interaction) && !IsNarrationSceneEffectInteraction(interaction)
        };
    }

    private static bool HasCharacterVisualIdentity(CharacterInteraction interaction)
    {
        return CharacterVisualIdentityIds(interaction).Any() ||
            !string.IsNullOrWhiteSpace(interaction.SourceRole);
    }

    private static IEnumerable<string> CharacterVisualIdentityIds(CharacterInteraction interaction)
    {
        if (!string.IsNullOrWhiteSpace(interaction.SubjectCharacterId))
        {
            yield return interaction.SubjectCharacterId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(interaction.SourceRoleCharacterId) &&
            !string.Equals(interaction.SourceRoleCharacterId, interaction.SubjectCharacterId, StringComparison.Ordinal))
        {
            yield return interaction.SourceRoleCharacterId.Trim();
        }
    }

    private static IEnumerable<string?> VisualReferences(VisualAssetBinding? binding, string? fallbackImageAssetId = null)
    {
        if (binding is null || binding == VisualAssetBinding.None)
        {
            yield return fallbackImageAssetId;
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(binding.RenderedAssetId))
        {
            yield return binding.RenderedAssetId;
        }

        if (!string.IsNullOrWhiteSpace(binding.SourceAssetId) &&
            !string.Equals(binding.SourceAssetId, binding.RenderedAssetId, StringComparison.Ordinal))
        {
            yield return binding.SourceAssetId;
        }

        yield return fallbackImageAssetId;
    }

    private static bool IsExplicitVisualBinding(VisualAssetBinding? binding)
    {
        return binding is { SourceKind: not VisualSourceKind.None } &&
            (!string.IsNullOrWhiteSpace(binding.SourceAssetId) ||
                !string.IsNullOrWhiteSpace(binding.RenderedAssetId));
    }

    private static string ResolveSceneVisualClipName(InteractionScene scene)
    {
        return TrimName(string.IsNullOrWhiteSpace(scene.Name) ? scene.Description : scene.Name);
    }

    private static string ResolveCharacterVisualClipName(CharacterInteraction interaction, IReadOnlyDictionary<string, string> characterNames)
    {
        var characterName = interaction.SubjectCharacterId is not null && characterNames.TryGetValue(interaction.SubjectCharacterId, out var name)
            ? name
            : "角色";
        return $"{characterName} {TrimName(interaction.Dialogue?.Text ?? interaction.Action.MainAction)}";
    }

    private static string ResolveAudioClipName(CharacterInteraction interaction, IReadOnlyDictionary<string, string> characterNames)
    {
        if (interaction.Narration is not null)
        {
            return $"旁白 {TrimName(interaction.Narration.Text)}";
        }

        var characterName = interaction.SubjectCharacterId is not null && characterNames.TryGetValue(interaction.SubjectCharacterId, out var name)
            ? name
            : "角色";
        return $"{characterName} 台词";
    }

    private static string ResolveNarrationSceneEffectVisualClipName(CharacterInteraction interaction)
    {
        return interaction.SourceItemType.Trim().ToLowerInvariant() == "effects"
            ? $"环境场效 {TrimName(InteractionText(interaction))}"
            : $"旁白叙述 {TrimName(InteractionText(interaction))}";
    }

    private static string TrimName(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "未命名" : value.Trim();
        return normalized.Length <= 24 ? normalized : $"{normalized[..24]}...";
    }

    private static bool IsPsdVisualAsset(ProjectAssetResolution resolution)
    {
        return resolution.Asset?.Kind == MediaAssetKind.Psd || IsPsdPath(resolution.AbsolutePath);
    }

    private static bool IsPsdPath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".psd", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".psb", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInsideWorkspacePath(string path, string rootPath)
    {
        var normalizedPath = EnsureDirectorySeparator(Path.GetFullPath(Path.GetDirectoryName(path) ?? string.Empty));
        var normalizedRoot = EnsureDirectorySeparator(Path.GetFullPath(rootPath));
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureDirectorySeparator(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    private static bool IsSupportedAudioDurationFormat(string audioPath)
    {
        var extension = Path.GetExtension(audioPath);
        return extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".flac", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record AudioAssetResolution(string AbsolutePath, string TaskId);
}

public enum EditingTimelinePreflightIssueKind
{
    PsdReference,
    MissingVisual,
    MissingAudio,
    UnsupportedAudio
}

public sealed record EditingTimelinePreflightIssue(
    EditingTimelinePreflightIssueKind Kind,
    string SceneId,
    string InteractionId,
    string Message,
    string SuggestedAction,
    string? AssetReference = null,
    string? TaskId = null,
    GenerationTaskStatus? TaskStatus = null)
{
    public static EditingTimelinePreflightIssue PsdReference(
        string sceneId,
        string interactionId,
        string assetReference)
    {
        return new EditingTimelinePreflightIssue(
            EditingTimelinePreflightIssueKind.PsdReference,
            sceneId,
            interactionId,
            $"剪辑项目导出不能直接引用 PSD/PSB：scene={sceneId}; interaction={interactionId}; asset={assetReference}",
            "请先从 PSD 选择图层并渲染为 PNG/JPG，再导出剪辑项目文件。",
            AssetReference: assetReference);
    }

    public static EditingTimelinePreflightIssue MissingVisual(
        string sceneId,
        string interactionId,
        string assetReference)
    {
        var normalizedReference = string.IsNullOrWhiteSpace(assetReference) ? string.Empty : assetReference.Trim();
        return new EditingTimelinePreflightIssue(
            EditingTimelinePreflightIssueKind.MissingVisual,
            sceneId,
            interactionId,
            $"已绑定的视觉素材找不到可导出的图片：scene={sceneId}; interaction={interactionId}; asset={normalizedReference}",
            "请重新绑定图片，或确认项目 assets 中的图片文件仍然存在。",
            AssetReference: normalizedReference);
    }

    public static EditingTimelinePreflightIssue MissingCompletedAudio(
        string sceneId,
        string interactionId,
        string taskId,
        string? assetReference,
        GenerationTaskStatus taskStatus)
    {
        var normalizedReference = string.IsNullOrWhiteSpace(assetReference) ? string.Empty : assetReference.Trim();
        return new EditingTimelinePreflightIssue(
            EditingTimelinePreflightIssueKind.MissingAudio,
            sceneId,
            interactionId,
            $"已完成的 TTS 任务找不到可导出的音频：scene={sceneId}; interaction={interactionId}; task={taskId}; asset={normalizedReference}",
            "请重新同步或重新生成该行语音，再导出剪辑项目文件。",
            AssetReference: normalizedReference,
            TaskId: taskId,
            TaskStatus: taskStatus);
    }

    public static EditingTimelinePreflightIssue MissingRequiredAudio(
        string sceneId,
        string interactionId,
        string? rowText)
    {
        var normalizedText = string.IsNullOrWhiteSpace(rowText) ? string.Empty : rowText.Trim();
        return new EditingTimelinePreflightIssue(
            EditingTimelinePreflightIssueKind.MissingAudio,
            sceneId,
            interactionId,
            $"工作台台词行缺少可导出的语音：scene={sceneId}; interaction={interactionId}; text={normalizedText}",
            "请先为该行生成语音，确认音频已保存到项目目录后再导出 FCPXML。",
            AssetReference: normalizedText);
    }

    public static EditingTimelinePreflightIssue UnsupportedCompletedAudioDuration(
        string sceneId,
        string interactionId,
        string taskId,
        string? assetReference,
        GenerationTaskStatus taskStatus)
    {
        var normalizedReference = string.IsNullOrWhiteSpace(assetReference) ? string.Empty : assetReference.Trim();
        var extension = Path.GetExtension(normalizedReference);
        var formatText = string.IsNullOrWhiteSpace(extension) ? "未知格式" : extension.TrimStart('.').ToUpperInvariant();
        return new EditingTimelinePreflightIssue(
            EditingTimelinePreflightIssueKind.UnsupportedAudio,
            sceneId,
            interactionId,
            $"已完成的 TTS 任务音频格式无法读取真实时长：scene={sceneId}; interaction={interactionId}; task={taskId}; asset={normalizedReference}; format={formatText}",
            "请转换为 WAV/FLAC，或补齐该格式真实时长读取器后再导出剪辑项目文件。",
            AssetReference: normalizedReference,
            TaskId: taskId,
            TaskStatus: taskStatus);
    }
}

public sealed class EditingTimelinePreflightException : InvalidOperationException
{
    public EditingTimelinePreflightException(IReadOnlyList<string> issues)
        : this(issues.Select(issue => new EditingTimelinePreflightIssue(
            EditingTimelinePreflightIssueKind.PsdReference,
            string.Empty,
            string.Empty,
            issue,
            "请修复该导出前检查问题后重试。")).ToArray())
    {
    }

    public EditingTimelinePreflightException(IReadOnlyList<EditingTimelinePreflightIssue> issues)
        : base(BuildMessage(issues.Select(issue => issue.Message).ToArray()))
    {
        StructuredIssues = issues;
        Issues = issues.Select(issue => issue.Message).ToArray();
    }

    public IReadOnlyList<string> Issues { get; }

    public IReadOnlyList<EditingTimelinePreflightIssue> StructuredIssues { get; }

    private static string BuildMessage(IReadOnlyList<string> issues)
    {
        return issues.Count == 0
            ? "\u526a\u8f91\u9879\u76ee\u5bfc\u51fa\u524d\u68c0\u67e5\u5931\u8d25\u3002"
            : $"\u526a\u8f91\u9879\u76ee\u5bfc\u51fa\u524d\u68c0\u67e5\u53d1\u73b0 {issues.Count} \u4e2a\u95ee\u9898\uff1a{string.Join(" | ", issues)}";
    }
}
