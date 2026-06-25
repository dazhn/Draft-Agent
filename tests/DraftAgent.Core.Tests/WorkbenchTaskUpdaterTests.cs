using DraftAgent.Core;

namespace DraftAgent.Core.Tests;

public sealed class WorkbenchTaskUpdaterTests
{
    private readonly WorkbenchTaskUpdater _updater = new();





    private static CharacterInteraction NarrationInteraction(string sceneId)
    {
        return new CharacterInteraction(
            "int_001",
            1,
            InteractionType.Narration,
            sceneId,
            null,
            null,
            new EmotionSpec("Calm", 0.6, string.Empty, string.Empty, string.Empty, string.Empty),
            new ActionSpec("Hello", [], string.Empty, 2),
            null,
            null,
            new NarrationLine(
                "nar_int_001",
                "Hello",
                new VoiceRenderSettings("narrator.wav", "Calm", 0.6, 1, 0.6)),
            new InteractionBinding(null, "narrator.wav"),
            [],
            string.Empty);
    }

    private static InteractionScene Scene(IReadOnlyList<CharacterInteraction> interactions)
    {
        return new InteractionScene(
            "scene_001",
            1,
            "Scene",
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            new SceneAssetBinding(),
            interactions);
    }

    private static GenerationTask Task(
        string id,
        GenerationTaskType type,
        string targetId,
        IReadOnlyDictionary<string, string> parameters)
    {
        return new GenerationTask(
            id,
            type,
            targetId,
            id,
            "comfyui",
            GenerationTaskStatus.Completed,
            parameters,
            type == GenerationTaskType.Tts ? "audio.wav" : "image.png");
    }
}


