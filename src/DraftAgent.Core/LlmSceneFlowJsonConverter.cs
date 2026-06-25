using System.Globalization;
using System.Text.Json;

namespace DraftAgent.Core;

public static class LlmSceneFlowJsonConverter
{
    public static string ToInteractionProjectJson(string json, string rawText, SceneInteractionBuilder fallbackBuilder)
    {
        using var document = JsonDocument.Parse(json);
        var draft = fallbackBuilder.Build(rawText);
        var scenesElement = GetScenesElement(document.RootElement);
        if (scenesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("AI JSON must contain a scenes array.");
        }

        var characters = ExtractCharacters(scenesElement)
            .Select((name, index) => new Character(
                $"char_{index + 1:000}",
                name,
                "AI detected character",
                string.Empty,
                string.Empty,
                string.Empty,
                new AssetBinding()))
            .ToArray();
        var characterMap = characters.ToDictionary(character => character.Name, character => character.Id, StringComparer.Ordinal);
        var interactionScenes = scenesElement.EnumerateArray()
            .Select((scene, sceneIndex) => BuildScene(scene, sceneIndex, characterMap))
            .ToArray();
        var tasks = interactionScenes.SelectMany(scene => scene.Interactions.SelectMany(interaction => interaction.Tasks)).ToArray();
        var project = new InteractionProject(
            string.IsNullOrWhiteSpace(draft.ProjectId) ? "proj_001" : draft.ProjectId,
            draft.Title,
            draft.Script,
            characters,
            interactionScenes,
            tasks,
            []);

        return JsonSerializer.Serialize(project, DraftJson.Options);
    }

    private static InteractionScene BuildScene(JsonElement scene, int sceneIndex, IReadOnlyDictionary<string, string> characterMap)
    {
        var sceneId = GetString(scene, "id") ?? $"scene_{sceneIndex + 1:000}";
        var description = GetString(scene, "description") ?? $"Scene {sceneIndex + 1}";
        var itemsElement = scene.TryGetProperty("items", out var itemsProperty) && itemsProperty.ValueKind == JsonValueKind.Array
            ? itemsProperty
            : default;
        var interactions = itemsElement.ValueKind == JsonValueKind.Array
            ? itemsElement.EnumerateArray().Select((interaction, index) => BuildInteraction(interaction, index, sceneId, characterMap)).ToArray()
            : [];

        return new InteractionScene(
            sceneId,
            GetInt(scene, "order") ?? sceneIndex + 1,
            description,
            description,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            new SceneAssetBinding(),
            interactions);
    }

    private static CharacterInteraction BuildInteraction(JsonElement interaction, int index, string sceneId, IReadOnlyDictionary<string, string> characterMap)
    {
        var type = (GetString(interaction, "type") ?? string.Empty).Trim();
        var id = GetString(interaction, "id") ?? $"int_{sceneId}_{index + 1:000}";
        var text = GetString(interaction, "text") ?? string.Empty;
        var emotionText = GetString(interaction, "emotion") ?? "Calm";
        var emotionIntensity = GetDouble(interaction, "intensity") ?? 0.6;
        var voice = new VoiceRenderSettings(null, emotionText, emotionIntensity, 1.0, 0.6);
        var sourceRole = CharacterNameGuard.NormalizeField(GetString(interaction, "role"));
        var speaker = CharacterNameGuard.NormalizeOrNull(sourceRole);
        var subjectId = speaker is not null && characterMap.TryGetValue(speaker, out var characterId) ? characterId : null;
        var isDialogue = type == "dialogue" && subjectId is not null;
        var dialogueSubjectId = isDialogue ? subjectId! : null;
        var dialogue = !isDialogue
            ? null
            : new DialogueLine($"dia_{id}", dialogueSubjectId!, null, text, voice);
        var narration = !isDialogue
            ? new NarrationLine($"nar_{id}", text, voice)
            : null;
        var normalized = new CharacterInteraction(
            id,
            GetInt(interaction, "order") ?? index + 1,
            isDialogue ? InteractionType.Dialogue : InteractionType.Narration,
            sceneId,
            dialogueSubjectId,
            null,
            new EmotionSpec(emotionText, emotionIntensity, emotionText, emotionText, emotionText, text),
            new ActionSpec(text, string.IsNullOrWhiteSpace(text) ? [] : [text], "no camera language", EstimateDuration(text)),
            dialogue,
            null,
            narration,
            new InteractionBinding(null, null),
            [],
            string.Empty);

        return normalized with
        {
            SourceItemType = type,
            SourceRole = sourceRole,
            SourceRoleCharacterId = subjectId,
            Tasks = BuildTasks(normalized)
        };
    }

    private static IReadOnlyList<GenerationTask> BuildTasks(CharacterInteraction interaction)
    {
        var tasks = new List<GenerationTask>();
        var line = interaction.Dialogue?.Text ?? interaction.Narration?.Text;
        if (!string.IsNullOrWhiteSpace(line))
        {
            var voice = interaction.Dialogue?.Voice ?? interaction.Narration?.Voice;
            var emotion = voice?.Emotion ?? string.Empty;
            var emotionStrength = voice is null
                ? "0"
                : IndexTts2Defaults.ResolveEmotionStrength(
                    emotion,
                    voice.EmotionIntensity.ToString("0.##", CultureInfo.InvariantCulture)).ToString("0.##", CultureInfo.InvariantCulture);
            tasks.Add(new GenerationTask(
                $"tts_{interaction.Id}",
                GenerationTaskType.Tts,
                interaction.Id,
                interaction.Type switch
                {
                    InteractionType.Dialogue => $"TTS {interaction.SubjectCharacterId ?? "character"} dialogue",
                    _ => "TTS narration"
                },
                "comfyui",
                GenerationTaskStatus.Draft,
                new Dictionary<string, string>
                {
                    ["text"] = line,
                    ["emotion"] = string.IsNullOrWhiteSpace(emotion) ? string.Empty : IndexTts2Defaults.NormalizeEmotion(emotion),
                    ["emotionStrength"] = emotionStrength,
                    ["voiceReferenceId"] = string.Empty
                }));
        }

        return tasks;
    }

    private static IReadOnlyList<string> ExtractCharacters(JsonElement scenesElement)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scene in scenesElement.EnumerateArray())
        {
            var interactions = scene.TryGetProperty("items", out var itemsProperty) && itemsProperty.ValueKind == JsonValueKind.Array
                ? itemsProperty
                : default;
            if (interactions.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var interaction in interactions.EnumerateArray())
            {
                var type = GetString(interaction, "type");
                if (type is not ("dialogue" or "action" or "thought"))
                {
                    continue;
                }

                var name = CharacterNameGuard.NormalizeOrNull(GetString(interaction, "role"));
                if (name is not null)
                {
                    names.Add(name);
                }
            }
        }

        return names.Count == 0 ? ["主角"] : names.ToArray();
    }

    private static JsonElement GetScenesElement(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("scenes", out var scenesProperty))
        {
            return scenesProperty;
        }

        return root;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var property) &&
               property.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static double? GetDouble(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var property) &&
               property.TryGetDouble(out var value)
            ? value
            : null;
    }

    private static double EstimateDuration(string text)
    {
        return Math.Max(2.5, Math.Min(8, Math.Round(text.Length / 5.5 + 1.0, 1)));
    }
}
