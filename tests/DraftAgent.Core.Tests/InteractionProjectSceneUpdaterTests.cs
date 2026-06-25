using DraftAgent.Core;

namespace DraftAgent.Core.Tests;

public sealed class InteractionProjectSceneUpdaterTests
{
    private readonly InteractionProjectSceneUpdater _updater = new(new WorkbenchTaskUpdater());


    [Fact]
    public void ApplyScenesReusesSubjectCharacterIdWhenNameMatchesExistingCharacter()
    {
        var character = Character("char_chen", "Chen");
        var project = EmptyProject() with { Characters = [character] };
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_001",
                    1,
                    "Scene 1",
                    string.Empty,
                    [Row("int_001", "scene_001", WorkbenchRowKind.Dialogue, "Chen", character.Id, "Calm 0.6", "Hello", null, null)])
            ],
            []);

        var result = _updater.ApplyScenes(project, document, new InMemoryWorkflowLogger());

        var interaction = Assert.Single(Assert.Single(result.Scenes).Interactions);
        Assert.Equal(character.Id, interaction.SubjectCharacterId);
        Assert.Single(result.Characters);
    }

    [Fact]
    public void ApplyScenesBuildsNarrationWithoutSubjectCharacter()
    {
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_001",
                    1,
                    "Scene 1",
                    string.Empty,
                    [Row("int_001", "scene_001", WorkbenchRowKind.Narration, "旁白", null, string.Empty, "雨停了。", null, "narrator.wav")])
            ],
            []);

        var result = _updater.ApplyScenes(EmptyProject(), document, new InMemoryWorkflowLogger());

        var interaction = Assert.Single(Assert.Single(result.Scenes).Interactions);
        Assert.Equal(InteractionType.Narration, interaction.Type);
        Assert.Null(interaction.SubjectCharacterId);
        Assert.Null(interaction.Dialogue);
        Assert.Equal("雨停了。", interaction.Narration!.Text);
        Assert.Equal("narrator.wav", interaction.Binding.NarratorVoiceReferenceId);
        Assert.Empty(result.Characters);
    }

    [Fact]
    public void ApplyScenesDoesNotCreateCharacterFromActionFragmentSubjectName()
    {
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_001",
                    1,
                    "Scene 1",
                    string.Empty,
                    [Row("int_001", "scene_001", WorkbenchRowKind.Dialogue, "想伸手又", null, "Calm 0.6", "对不起。", null, null)])
            ],
            []);

        var result = _updater.ApplyScenes(EmptyProject(), document, new InMemoryWorkflowLogger());

        var character = Assert.Single(result.Characters);
        var interaction = Assert.Single(Assert.Single(result.Scenes).Interactions);
        Assert.Equal("未命名人物", character.Name);
        Assert.Equal(character.Id, interaction.SubjectCharacterId);
    }

    [Fact]
    public void ApplyScenesUsesRenderedVisualAssetsAndVoiceBindingWhenPresent()
    {
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_001",
                    1,
                    "Scene 1",
                    string.Empty,
                    [
                        Row("int_001", "scene_001", WorkbenchRowKind.Dialogue, "Chen", null, "Calm 0.6", "Hello", "fallback-character.png", "fallback.wav") with
                        {
                            CharacterVisualBinding = VisualAssetBinding.FromPsd("character-source.psd", "character-rendered.png"),
                            VoiceBinding = VoiceAssetBinding.FromProjectReference(
                                "chen.wav",
                                "Chen reference text",
                                "assets/audio/voice-references/chen.wav",
                                @"C:\ComfyUI\models\voices\chen.wav")
                        }
                    ],
                    VisualAssetBinding.FromPsd("scene-source.psd", "scene-rendered.png"))
            ],
            []);

        var result = _updater.ApplyScenes(EmptyProject(), document, new InMemoryWorkflowLogger());

        var scene = Assert.Single(result.Scenes);
        var interaction = Assert.Single(scene.Interactions);
        var tts = interaction.Tasks.Single(task => task.Type == GenerationTaskType.Tts);
        Assert.Equal("scene-rendered.png", scene.AssetBinding.BackgroundImage);
        Assert.Equal(VisualSourceKind.Image, scene.AssetBinding.BackgroundVisualBinding?.SourceKind);
        Assert.Equal("scene-rendered.png", scene.AssetBinding.BackgroundVisualBinding?.SourceAssetId);
        Assert.Equal("scene-rendered.png", scene.AssetBinding.BackgroundVisualBinding?.RenderedAssetId);
        Assert.Equal(VisualSourceKind.Image, interaction.Binding.SceneVisualBinding?.SourceKind);
        Assert.Equal("scene-rendered.png", interaction.Binding.SceneVisualBinding?.SourceAssetId);
        Assert.Equal("scene-rendered.png", interaction.Binding.SceneVisualBinding?.RenderedAssetId);
        Assert.Equal(VisualSourceKind.Psd, interaction.Binding.SubjectCharacterVisualBinding?.SourceKind);
        Assert.Equal("character-source.psd", interaction.Binding.SubjectCharacterVisualBinding?.SourceAssetId);
        Assert.Equal("character-rendered.png", interaction.Binding.SubjectCharacterVisualBinding?.RenderedAssetId);
        Assert.Equal("chen.wav", interaction.Binding.SubjectVoiceReferenceId);
        Assert.Equal("Chen reference text", interaction.Dialogue!.Voice.ReferenceText);
        Assert.Equal("Chen reference text", tts.Parameters["referenceText"]);
        Assert.Equal("assets/audio/voice-references/chen.wav", tts.Parameters["voiceSourceAssetId"]);
        Assert.Equal(@"C:\ComfyUI\models\voices\chen.wav", tts.Parameters["voiceSyncedPath"]);
    }


    [Fact]
    public void ApplyScenesLogsSceneApplyLifecycle()
    {
        var logger = new InMemoryWorkflowLogger();
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_001",
                    1,
                    "Scene 1",
                    string.Empty,
                    [])
            ],
            []);

        _updater.ApplyScenes(EmptyProject(), document, logger);

        Assert.Contains(logger.Entries, entry => entry.Operation == "scene_apply_started");
        Assert.Contains(logger.Entries, entry => entry.Operation == "scene_apply_completed");
    }

    private static WorkbenchRowDraft Row(
        string interactionId,
        string sceneId,
        WorkbenchRowKind kind,
        string subjectName,
        string? subjectCharacterId,
        string emotion,
        string text,
        string? characterAssetId,
        string? voiceReferenceId)
    {
        return new WorkbenchRowDraft(
            interactionId,
            sceneId,
            1,
            kind,
            subjectName,
            subjectCharacterId,
            emotion,
            text,
            null,
            kind == WorkbenchRowKind.Dialogue ? 1 : null,
            new WorkbenchTaskLinks(null),
            VisualAssetBinding.FromImage(characterAssetId),
            VoiceAssetBinding.FromProjectReference(voiceReferenceId, null, null, null));
    }

    private static Character Character(string id, string name)
    {
        return new Character(id, name, "角色", "appearance", "voice", "Calm", new AssetBinding());
    }

    private static InteractionProject EmptyProject()
    {
        return new InteractionProject("empty", "empty", new DraftAnalyzer().Preprocess(string.Empty), [], [], [], []);
    }
}


