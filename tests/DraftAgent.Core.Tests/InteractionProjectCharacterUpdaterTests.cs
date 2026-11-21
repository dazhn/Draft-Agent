using DraftAgent.Core;

namespace DraftAgent.Core.Tests;

public sealed class InteractionProjectCharacterUpdaterTests
{
    private readonly InteractionProjectCharacterUpdater _updater = new();

    [Fact]
    public void ApplyBindingsUsesDefaultVisualBindingBeforeInteractionBinding()
    {
        var character = Character("char_chen", "Chen");
        var scene = Scene([
            Interaction("int_001", character.Id, "row-portrait.png", "row-voice.wav")
        ]);

        var updated = _updater.ApplyBindings(
            [character],
            [scene],
            [new WorkbenchCharacterBindingEdit(
                character.Id,
                VisualAssetBinding.FromImage("default-portrait.png"),
                VoiceAssetBinding.FromProjectReference("default-voice.wav", null, null, null))]);

        var result = Assert.Single(updated);
        Assert.Equal("default-portrait.png", result.AssetBinding.Portrait);
        Assert.Equal("row-voice.wav", result.AssetBinding.Voice);
    }

    [Fact]
    public void ApplyBindingsUsesDefaultBindingWhenCharacterHasNoRows()
    {
        var character = Character("char_linan", "Linan");

        var updated = _updater.ApplyBindings(
            [character],
            [],
            [new WorkbenchCharacterBindingEdit(
                character.Id,
                VisualAssetBinding.FromImage("linan.png"),
                VoiceAssetBinding.FromProjectReference("linan.wav", null, null, null))]);

        var result = Assert.Single(updated);
        Assert.Equal("linan.png", result.AssetBinding.Portrait);
        Assert.Equal("linan.wav", result.AssetBinding.Voice);
    }

    [Fact]
    public void ApplyBindingsUsesRenderedDefaultVisualAndVoiceBindingWhenCharacterHasNoRows()
    {
        var character = Character("char_linan", "Linan");

        var updated = _updater.ApplyBindings(
            [character],
            [],
            [
                new WorkbenchCharacterBindingEdit(
                    character.Id,
                    VisualAssetBinding.FromPsd("linan.psd", "linan-rendered.png"),
                    new VoiceAssetBinding("linan.wav", "Linan reference"))
            ]);

        var result = Assert.Single(updated);
        Assert.Equal("linan-rendered.png", result.AssetBinding.Portrait);
        Assert.Equal(VisualSourceKind.Psd, result.AssetBinding.PortraitVisualBinding?.SourceKind);
        Assert.Equal("linan.psd", result.AssetBinding.PortraitVisualBinding?.SourceAssetId);
        Assert.Equal("linan-rendered.png", result.AssetBinding.PortraitVisualBinding?.RenderedAssetId);
        Assert.Equal("linan.wav", result.AssetBinding.Voice);
        Assert.Equal("Linan reference", result.AssetBinding.VoiceBinding?.ReferenceText);
    }

    [Fact]
    public void ApplyBindingsStoresStructuredDefaultVoiceBindingWhenCharacterHasNoRows()
    {
        var character = Character("char_linan", "Linan");
        var voiceBinding = VoiceAssetBinding.FromProjectReference(
            "linan.wav",
            "Linan reference",
            "assets/audio/voice-references/linan.wav",
            @"C:\ComfyUI\models\voices\linan.wav");

        var updated = _updater.ApplyBindings(
            [character],
            [],
            [new WorkbenchCharacterBindingEdit(character.Id, null, voiceBinding)]);

        var result = Assert.Single(updated);
        Assert.Equal("linan.wav", result.AssetBinding.Voice);
        Assert.Equal("linan.wav", result.AssetBinding.VoiceBinding?.VoiceReferenceId);
        Assert.Equal("Linan reference", result.AssetBinding.VoiceBinding?.ReferenceText);
        Assert.Equal("assets/audio/voice-references/linan.wav", result.AssetBinding.VoiceBinding?.SourceAssetId);
        Assert.Equal(@"C:\ComfyUI\models\voices\linan.wav", result.AssetBinding.VoiceBinding?.SyncedVoicePath);
    }

    [Fact]
    public void ApplyBindingsKeepsExplicitDefaultVisualBindingBeforeRowVisualBinding()
    {
        var character = Character("char_linan", "Linan");
        var scene = Scene([
            Interaction("int_001", character.Id, "linan-row-rendered.png", "row-voice.wav")
        ]);

        var updated = _updater.ApplyBindings(
            [character],
            [scene],
            [
                new WorkbenchCharacterBindingEdit(
                    character.Id,
                    VisualAssetBinding.FromPsd("linan.psd", "linan-default-rendered.png"),
                    VoiceAssetBinding.FromProjectReference("default-voice.wav", null, null, null))
            ]);

        var result = Assert.Single(updated);
        Assert.Equal("linan-default-rendered.png", result.AssetBinding.Portrait);
        Assert.Equal(VisualSourceKind.Psd, result.AssetBinding.PortraitVisualBinding?.SourceKind);
        Assert.Equal("linan.psd", result.AssetBinding.PortraitVisualBinding?.SourceAssetId);
        Assert.Equal("linan-default-rendered.png", result.AssetBinding.PortraitVisualBinding?.RenderedAssetId);
        Assert.Equal("row-voice.wav", result.AssetBinding.Voice);
    }


    [Fact]
    public void ApplyBindingsKeepsExplicitDefaultPsdSourceBeforeRowVisualBinding()
    {
        var character = Character("char_linan", "Linan");
        var scene = Scene([
            Interaction("int_001", character.Id, "linan-row-rendered.png", "row-voice.wav")
        ]);

        var updated = _updater.ApplyBindings(
            [character],
            [scene],
            [
                new WorkbenchCharacterBindingEdit(
                    character.Id,
                    VisualAssetBinding.FromPsd("linan.psd"),
                    VoiceAssetBinding.FromProjectReference("default-voice.wav", null, null, null))
            ]);

        var result = Assert.Single(updated);
        Assert.Equal("linan.psd", result.AssetBinding.Portrait);
        Assert.Equal(VisualSourceKind.Psd, result.AssetBinding.PortraitVisualBinding?.SourceKind);
        Assert.Equal("linan.psd", result.AssetBinding.PortraitVisualBinding?.SourceAssetId);
        Assert.Null(result.AssetBinding.PortraitVisualBinding?.RenderedAssetId);
        Assert.Equal("row-voice.wav", result.AssetBinding.Voice);
    }

    [Fact]
    public void ApplyBindingsUsesExistingCharacterDefaultBeforeRowPortraitBinding()
    {
        var character = Character("char_linan", "Linan") with
        {
            AssetBinding = new AssetBinding(Portrait: "existing-default.png")
        };
        var scene = Scene([
            Interaction("int_001", character.Id, "linan-row.png", null)
        ]);

        var updated = _updater.ApplyBindings(
            [character],
            [scene],
            null);

        var result = Assert.Single(updated);
        Assert.Equal("existing-default.png", result.AssetBinding.Portrait);
    }

    [Fact]
    public void ApplyBindingsDoesNotDeriveCharacterDefaultFromRowImage()
    {
        var character = Character("char_linan", "Linan");
        var scene = Scene([
            Interaction("int_001", character.Id, VisualAssetBinding.FromImage("linan-row.png"), null)
        ]);

        var updated = _updater.ApplyBindings([character], [scene], null);

        var result = Assert.Single(updated);
        Assert.Null(result.AssetBinding.Portrait);
        Assert.Equal(VisualSourceKind.None, result.AssetBinding.PortraitVisualBinding?.SourceKind);
    }

    [Fact]
    public void ApplyBindingsDoesNotDeriveCharacterDefaultFromRowPsdRender()
    {
        var character = Character("char_linan", "Linan");
        var scene = Scene([
            Interaction("int_001", character.Id, VisualAssetBinding.FromPsd("linan.psd", "linan-row-rendered.png"), null)
        ]);

        var updated = _updater.ApplyBindings([character], [scene], null);

        var result = Assert.Single(updated);
        Assert.Null(result.AssetBinding.Portrait);
        Assert.Equal(VisualSourceKind.None, result.AssetBinding.PortraitVisualBinding?.SourceKind);
    }

    private static Character Character(string id, string name)
    {
        return new Character(id, name, "角色", "appearance", "voice", "Calm", new AssetBinding());
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

    private static CharacterInteraction Interaction(
        string id,
        string characterId,
        string? portrait,
        string? voice)
    {
        return Interaction(id, characterId, VisualAssetBinding.FromImage(portrait), voice);
    }

    private static CharacterInteraction Interaction(
        string id,
        string characterId,
        VisualAssetBinding? visualBinding,
        string? voice)
    {
        return new CharacterInteraction(
            id,
            1,
            InteractionType.Dialogue,
            "scene_001",
            characterId,
            null,
            new EmotionSpec("Calm", 0.6, string.Empty, string.Empty, string.Empty, string.Empty),
            new ActionSpec("台词表演", [], string.Empty, 2),
            new DialogueLine(
                $"dia_{id}",
                characterId,
                null,
                "Hello",
                new VoiceRenderSettings(voice, "Calm", 0.6, 1, 0.6)),
            null,
            null,
            new InteractionBinding(
                voice,
                null,
                null,
                visualBinding),
            [],
            string.Empty);
    }
}

