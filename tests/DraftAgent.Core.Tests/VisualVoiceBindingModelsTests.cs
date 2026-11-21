using DraftAgent.Core;

namespace DraftAgent.Core.Tests;

public sealed class VisualVoiceBindingModelsTests
{
    [Fact]
    public void VisualAssetBindingNormalizesImageAndPsdReferences()
    {
        var image = VisualAssetBinding.FromImage(" asset_scene ");
        var psd = VisualAssetBinding.FromPsd(" asset_psd ", " asset_rendered ");
        var empty = VisualAssetBinding.FromImage(" ");

        Assert.True(image.IsImage);
        Assert.Equal("asset_scene", image.SourceAssetId);
        Assert.Equal("asset_scene", image.RenderedAssetId);
        Assert.Equal("asset_scene", image.EffectiveAssetId);
        Assert.True(psd.IsPsd);
        Assert.Equal("asset_psd", psd.SourceAssetId);
        Assert.Equal("asset_rendered", psd.RenderedAssetId);
        Assert.Equal("asset_rendered", psd.EffectiveAssetId);
        Assert.False(empty.HasRenderedAsset);
        Assert.Equal(VisualAssetBinding.None, empty);
    }

    [Fact]
    public void VisualAssetBindingSemanticEqualsComparesStructuredBindings()
    {
        var structured = VisualAssetBinding.FromImage("asset_character");

        Assert.True(VisualAssetBinding.SemanticEquals(structured, VisualAssetBinding.FromImage(" asset_character ")));
        Assert.False(VisualAssetBinding.SemanticEquals(
            VisualAssetBinding.FromPsd("asset_psd", "asset_rendered"),
            VisualAssetBinding.FromImage("asset_rendered")));
    }

    [Fact]
    public void VisualAssetBindingPsdLayerMatchConfigurationIsStructuredState()
    {
        var settings = new PsdLayerMatchConfiguration(
            ExpressionGroup: new PsdLayerGroupCatalog("表情", ["开心", "悲伤"]),
            ActionGroup: new PsdLayerGroupCatalog("动作", ["抬手", "默认手"]));

        var binding = VisualAssetBinding.FromPsd("asset_psd", "asset_rendered", layerMatch: settings);

        Assert.True(binding.PsdLayerMatch?.HasAnyGroup);
        Assert.Equal("表情", binding.PsdLayerMatch?.ExpressionGroup?.GroupName);
        Assert.True(VisualAssetBinding.SemanticEquals(
            binding,
            VisualAssetBinding.FromPsd("asset_psd", "asset_rendered", layerMatch: settings)));
        Assert.False(VisualAssetBinding.SemanticEquals(
            binding,
            VisualAssetBinding.FromPsd("asset_psd", "asset_rendered")));
    }

    [Fact]
    public void TtsGeneratedAudioReferenceResolvesCompletedResultBeforeStaleOutput()
    {
        var task = new GenerationTask(
            "tts_1",
            GenerationTaskType.Tts,
            "int_1",
            "TTS",
            "comfyui",
            GenerationTaskStatus.Completed,
            new Dictionary<string, string>(),
            "asset_generated_current");

        var reference = TtsGeneratedAudioReference.Resolve(task);
        var candidates = TtsGeneratedAudioReference.ResolveCandidates(task);

        Assert.Equal("asset_generated_current", reference);
        Assert.Equal(["asset_generated_current"], candidates);
    }

    [Fact]
    public void TtsGeneratedAudioReferenceSeparatesPlayableAudioFromGeneratedProjectAudio()
    {
        Assert.True(TtsGeneratedAudioReference.IsSupportedAudioReference("output||preview.mp3"));
        Assert.True(TtsGeneratedAudioReference.IsSupportedAudioReference("temp||preview.ogg"));
        Assert.False(TtsGeneratedAudioReference.IsSupportedGeneratedAudioReference("output||preview.mp3"));
        Assert.True(TtsGeneratedAudioReference.IsSupportedGeneratedAudioReference("output||preview.flac"));
    }
}

