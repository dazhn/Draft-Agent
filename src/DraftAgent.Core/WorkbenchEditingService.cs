namespace DraftAgent.Core;

public sealed record WorkbenchSceneEdit(
    string SceneId,
    int Order,
    string Name,
    string Effect,
    IReadOnlyList<WorkbenchInteractionEdit> Rows,
    VisualAssetBinding? VisualBinding = null);

public sealed record WorkbenchInteractionEdit(
    string InteractionId,
    int Order,
    string SubjectName,
    string Emotion,
    string Text,
    VisualAssetBinding? CharacterVisualBinding = null,
    VoiceAssetBinding? VoiceBinding = null,
    IReadOnlyDictionary<string, double>? EmotionVectors = null,
    double? EmotionAlpha = null,
    string SourceItemType = "",
    string SourceRole = "",
    string? SourceRoleCharacterId = null,
    string? VoiceRole = null,
    string? VoiceRoleCharacterId = null);

public sealed record WorkbenchCharacterBindingEdit(
    string CharacterId,
    VisualAssetBinding? VisualBinding,
    VoiceAssetBinding? VoiceBinding = null);

public sealed class WorkbenchEditingService
{
    private const string NarrationName = "旁白";
    private readonly IWorkflowLogger _logger;
    private readonly InteractionProjectCharacterUpdater _characterUpdater = new();
    private readonly WorkbenchTaskUpdater _taskUpdater;
    private readonly WorkbenchIssueUpdater _issueUpdater = new();
    private readonly InteractionProjectSceneUpdater _sceneUpdater;

    public WorkbenchEditingService(IWorkflowLogger logger, IWorkbenchTaskPreservationPolicy? taskPreservationPolicy = null)
    {
        _logger = logger;
        _taskUpdater = new WorkbenchTaskUpdater(taskPreservationPolicy);
        _sceneUpdater = new InteractionProjectSceneUpdater(_taskUpdater);
    }

    public InteractionProject AddCharacter(InteractionProject project, string characterName)
    {
        var normalizedName = CharacterNameGuard.NormalizeOrNull(characterName) ?? string.Empty;
        _logger.Info(
            WorkflowSteps.WorkbenchEdit,
            "character_add_started",
            "开始添加或确认工作台人物。",
            new Dictionary<string, string>
            {
                ["name"] = normalizedName
            });

        if (string.IsNullOrWhiteSpace(normalizedName) || IsNarration(normalizedName))
        {
            _logger.Warning(
                WorkflowSteps.WorkbenchEdit,
                "character_add_ignored",
                "人物名称为空或为旁白，已忽略。",
                new Dictionary<string, string>
                {
                    ["name"] = normalizedName
                });
            return project;
        }

        var characters = project.Characters.ToList();
        if (characters.Any(character => character.Name == normalizedName))
        {
            _logger.Info(
                WorkflowSteps.WorkbenchEdit,
                "character_add_completed",
                "人物已存在，无需重复添加。",
                new Dictionary<string, string>
                {
                    ["name"] = normalizedName,
                    ["characterCount"] = characters.Count.ToString()
                });
            return project;
        }

        characters.Add(CreateCharacter(normalizedName, characters));
        var updatedProject = project with { Characters = characters };

        _logger.Info(
            WorkflowSteps.WorkbenchEdit,
            "character_add_completed",
            "人物已添加到工作台。",
            new Dictionary<string, string>
            {
                ["name"] = normalizedName,
                ["characterCount"] = updatedProject.Characters.Count.ToString()
            });

        return updatedProject;
    }

    public InteractionProject Apply(
        InteractionProject project,
        IReadOnlyList<WorkbenchSceneEdit> sceneEdits,
        IReadOnlyList<WorkbenchCharacterBindingEdit>? characterBindingEdits = null)
    {
        var document = WorkbenchDraftBuilder.FromEdits(sceneEdits, characterBindingEdits);
        return Apply(project, document);
    }

    public InteractionProject Apply(InteractionProject project, WorkbenchDocument document)
    {
        _logger.Info(
            WorkflowSteps.WorkbenchEdit,
            "apply_started",
            "开始应用工作台表格修改。",
            new Dictionary<string, string>
            {
                ["sceneEditCount"] = document.Scenes.Count.ToString(),
                ["currentSceneCount"] = project.Scenes.Count.ToString(),
                ["currentCharacterCount"] = project.Characters.Count.ToString()
            });

        var sceneUpdate = _sceneUpdater.ApplyScenes(project, document, _logger);
        var updatedScenes = sceneUpdate.Scenes;

        var updatedCharacters = PruneUnreferencedCharacters(
            _characterUpdater.ApplyBindings(sceneUpdate.Characters, updatedScenes, document.CharacterBindings),
            updatedScenes);
        var updatedIssues = _issueUpdater.RefreshBindingIssues(project.Issues, updatedScenes, updatedCharacters);
        var rowGenerationTasks = updatedScenes
            .SelectMany(scene => scene.Interactions.SelectMany(interaction => interaction.Tasks))
            .OrderBy(task => task.Id, StringComparer.Ordinal)
            .ToArray();
        var generationTasks = _taskUpdater.MergeProjectTasks(project, rowGenerationTasks);

        var updatedProject = project with
        {
            Characters = updatedCharacters,
            Scenes = updatedScenes,
            GenerationTasks = generationTasks,
            Issues = updatedIssues
        };

        _logger.Info(
            WorkflowSteps.WorkbenchEdit,
            "apply_completed",
            "工作台表格修改已全部应用。",
            new Dictionary<string, string>
            {
                ["sceneCount"] = updatedProject.Scenes.Count.ToString(),
                ["characterCount"] = updatedProject.Characters.Count.ToString(),
                ["taskCount"] = updatedProject.GenerationTasks.Count.ToString(),
                ["issueCount"] = updatedProject.Issues.Count.ToString()
            });

        return updatedProject;
    }

    private static Character CreateCharacter(string name, IReadOnlyCollection<Character> existingCharacters)
    {
        return new Character(
            NextId("char_manual", existingCharacters.Select(character => character.Id)),
            name,
            existingCharacters.Count == 0 ? "主角" : "角色",
            "待绑定参考形象",
            "待选择音色",
            "克制",
            new AssetBinding());
    }

    private static string NextId(string prefix, IEnumerable<string> existingIds)
    {
        var existing = existingIds.ToHashSet(StringComparer.Ordinal);
        for (var index = 1; index < 100_000; index++)
        {
            var id = $"{prefix}_{index:000}";
            if (!existing.Contains(id))
            {
                return id;
            }
        }

        return $"{prefix}_{Guid.NewGuid():N}";
    }

    private static IReadOnlyList<Character> PruneUnreferencedCharacters(
        IReadOnlyList<Character> characters,
        IReadOnlyList<InteractionScene> scenes)
    {
        var referencedIds = new HashSet<string>(StringComparer.Ordinal);
        var referencedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var interaction in scenes.SelectMany(scene => scene.Interactions))
        {
            Add(referencedIds, interaction.SubjectCharacterId);
            Add(referencedIds, interaction.SourceRoleCharacterId);
            Add(referencedIds, interaction.Dialogue?.SpeakerCharacterId);
            Add(referencedIds, interaction.InnerMonologue?.CharacterId);
            Add(referencedIds, interaction.VoiceRoleCharacterId);
            Add(referencedNames, interaction.SourceRole);
            Add(referencedNames, interaction.VoiceRole);
        }

        var filtered = characters
            .Where(character =>
                IsNarration(character.Name) ||
                character.Id.StartsWith("char_manual_", StringComparison.Ordinal) ||
                referencedIds.Contains(character.Id) ||
                referencedNames.Contains(character.Name))
            .ToArray();
        return filtered.Length == characters.Count ? characters : filtered;
    }

    private static void Add(HashSet<string> values, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add(value.Trim());
        }
    }

    private static string NormalizeName(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    private static bool IsNarration(string? value)
    {
        return string.Equals(NormalizeName(value), NarrationName, StringComparison.Ordinal);
    }

}
