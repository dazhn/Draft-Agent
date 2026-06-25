using DraftAgent.App.ViewModels;
using DraftAgent.Core;

namespace DraftAgent.App.Tests;

public sealed class TtsWorkbenchPolicyTests
{
    [Fact]
    public void MainWindowVoiceReferenceOptionsEndWithCustomAudioAction()
    {
        var viewModel = new MainWindowViewModel(null, loadLastProject: false);

        Assert.Equal(MainWindowViewModel.CustomVoiceReferenceOption, viewModel.VoiceReferenceOptions.Last());
        Assert.Contains(IndexTts2Defaults.DefaultVoiceReferenceId, viewModel.VoiceReferenceOptions);
    }

    [Fact]
    public void BuildVoiceCatalogOptionsPrependsDefaultVoiceAndRemovesDuplicates()
    {
        var voices = TtsWorkbenchPolicy.BuildVoiceCatalogOptions([
            "custom.wav",
            IndexTts2Defaults.DefaultVoiceReferenceId,
            "",
            "custom.wav"
        ]);

        Assert.Equal([
            IndexTts2Defaults.DefaultVoiceReferenceId,
            "custom.wav"
        ], voices);
    }

    [Fact]
    public void BuildVoiceCatalogOptionsKeepsComfyVoiceKeysAndFiltersGeneratedCopies()
    {
        var voices = TtsWorkbenchPolicy.BuildVoiceCatalogOptions(
            [
                "TTS/voices/female_01.wav",
                "local_only.wav",
                "int_001_ComfyUI_00038_103.flac",
                "tts_002_generated.wav"
            ],
            [
                "female_01.wav",
                "fallback_only.wav",
                "row_003_ComfyUI_00038_.flac"
            ]);

        Assert.Equal([
            IndexTts2Defaults.DefaultVoiceReferenceId,
            "local_only.wav",
            "TTS/voices/female_01.wav",
            "fallback_only.wav"
        ], voices);
        Assert.DoesNotContain(voices, option => option.StartsWith("int_001_", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(voices, option => option.StartsWith("tts_002_", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(voices, option => option.StartsWith("row_003_", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildVoiceCatalogOptionsFiltersPluginExampleVoices()
    {
        var voices = TtsWorkbenchPolicy.BuildVoiceCatalogOptions([
            "female_01.wav",
            "linan.flac",
            "voices_examples/female/female_02.wav",
            "custom_nodes/tts_audio_suite/voices_examples/sample.wav"
        ]);

        Assert.Equal([
            IndexTts2Defaults.DefaultVoiceReferenceId,
            "female_01.wav",
            "linan.flac"
        ], voices);
    }

    [Fact]
    public void VisibleVoiceReferenceFilterKeepsUserNamedVoicesButHidesGeneratedCopies()
    {
        Assert.True(TtsWorkbenchPolicy.IsVisibleVoiceReferenceOption("hero.wav"));
        Assert.True(TtsWorkbenchPolicy.IsVisibleVoiceReferenceOption("charismatic.wav"));
        Assert.False(TtsWorkbenchPolicy.IsVisibleVoiceReferenceOption("int_hero.wav"));
        Assert.False(TtsWorkbenchPolicy.IsVisibleVoiceReferenceOption("scene_voice.wav"));
        Assert.False(TtsWorkbenchPolicy.IsVisibleVoiceReferenceOption("int_001_ComfyUI_00038_103.flac"));
        Assert.False(TtsWorkbenchPolicy.IsVisibleVoiceReferenceOption("int_scene_001_001_ComfyUI_00038_.flac"));
        Assert.False(TtsWorkbenchPolicy.IsVisibleVoiceReferenceOption("char_chen_ComfyUI_00038_.flac"));
        Assert.False(TtsWorkbenchPolicy.IsVisibleVoiceReferenceOption("TTS/voices/row_003_output.wav"));
    }

    [Theory]
    [InlineData("output||audio.wav", "127.0.0.1:8000", "http://127.0.0.1:8000/view?filename=audio.wav&type=output")]
    [InlineData("temp|tts queue|line 01.wav", "http://127.0.0.1:8000/", "http://127.0.0.1:8000/view?filename=line%2001.wav&type=temp&subfolder=tts%20queue")]
    public void ResolvePlayableAudioTargetBuildsComfyViewUrl(string asset, string endpoint, string expected)
    {
        var target = TtsWorkbenchPolicy.ResolvePlayableAudioTarget(asset, endpoint);

        Assert.Equal(expected, target);
    }

    [Fact]
    public void ResolvePlayableAudioTargetRejectsNonAudioComfyOutput()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            TtsWorkbenchPolicy.ResolvePlayableAudioTarget("output||ComfyUI_00001_.png", "127.0.0.1:8000"));

        Assert.Contains("audio output", exception.Message);
    }

    [Fact]
    public void ResolvePlayableAudioTargetKeepsAbsoluteOrRemoteAssets()
    {
        var absolutePath = Path.Combine(Path.GetTempPath(), "voice.wav");
        var remoteUrl = "https://example.com/voice.wav";

        Assert.Equal(absolutePath, TtsWorkbenchPolicy.ResolvePlayableAudioTarget(absolutePath, "127.0.0.1:8000"));
        Assert.Equal(remoteUrl, TtsWorkbenchPolicy.ResolvePlayableAudioTarget(remoteUrl, "127.0.0.1:8000"));
    }

    [Fact]
    public async Task ProjectAssetResolverResolvesImportedTtsAssetIdBeforeComfyPreviewFallback()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-audio-preview-{Guid.NewGuid():N}");
        var manifest = await new WorkspaceProjectManager().CreateOrOpenAsync(root, "Audio Preview");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var sourcePath = Path.Combine(Path.GetTempPath(), $"draft-agent-audio-{Guid.NewGuid():N}.flac");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3]);
        var import = await new ProjectMediaAssetService().ImportAsync(
            workspace,
            new MediaImportRequest(sourcePath, MediaAssetKind.Audio, MediaAssetSource.TtsGenerated, "int_001"));

        var resolved = new ProjectAssetResolver().ResolvePath(import.Asset.Id, workspace, import.Manifest)
            ?? TtsWorkbenchPolicy.ResolvePlayableAudioTarget(import.Asset.Id, "127.0.0.1:8000");

        Assert.Equal(import.Asset.AbsolutePath, resolved);
        Assert.DoesNotContain("/view?", resolved);
    }

    [Fact]
    public void BuildRegenerateRowOverridesUsesEffectiveEmotionVectorInsteadOfStaleTextEmotion()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        row.Emotion = "Calm 0.1";
        row.EmotionVectors.Single(vector => vector.Key == "Calm").Value = 0;
        row.EmotionVectors.Single(vector => vector.Key == "Afraid").Value = 0.9;
        row.EmotionVectors.Single(vector => vector.Key == "Sad").Value = 0.4;

        var overrides = TtsWorkbenchPolicy.BuildRegenerateRowOverrides(row);

        Assert.Equal(row.LineText, overrides["text"]);
        Assert.Equal("Afraid", overrides["emotion"]);
        Assert.Equal("0.9", overrides["emotionStrength"]);
        Assert.Equal($"害怕 0.9{Environment.NewLine}悲伤 0.4", row.EffectiveEmotionDisplayText);
    }

    [Fact]
    public void EffectiveEmotionDisplayTextUsesChineseNameWhileInternalEmotionStaysEnglish()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Lin");
        row.EmotionVectors.Single(vector => vector.Key == "Calm").Value = 0;
        row.EmotionVectors.Single(vector => vector.Key == "Sad").Value = 1.1;

        Assert.Equal("悲伤 1.1", row.EffectiveEmotionDisplayText);
        Assert.Equal("Sad 1.1", row.EffectiveEmotionText);
    }

    [Fact]
    public void BuildRegenerateRowOverridesUsesNarrationEmotion()
    {
        var row = AnalysisResultRowViewModel.CreateManualNarration(1);
        row.EmotionVectors.Single(vector => vector.Key == "Sad").Value = 1;

        var overrides = TtsWorkbenchPolicy.BuildRegenerateRowOverrides(row);

        Assert.Equal("Sad", overrides["emotion"]);
        Assert.Equal("1", overrides["emotionStrength"]);
    }
}

