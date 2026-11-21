using DraftAgent.Core;

namespace DraftAgent.Core.Tests;

public sealed class WorkbenchTaskPreservationPolicyTests
{
    private readonly WorkbenchTaskPreservationPolicy _policy = new();

    [Fact]
    public void PreserveTtsTaskKeepsTaskWhenRequiredParametersMatch()
    {
        var task = Task(
            "tts_int_001",
            GenerationTaskType.Tts,
            new Dictionary<string, string>
            {
                ["text"] = "Hello",
                ["emotion"] = "Calm",
                ["voiceReferenceId"] = "voice.wav"
            });

        var preserved = _policy.PreserveTtsTask(task, new Dictionary<string, string>
        {
            ["text"] = "Hello",
            ["emotion"] = "Calm",
            ["voiceReferenceId"] = "voice.wav"
        });

        Assert.Same(task, preserved);
    }

    [Fact]
    public void PreserveTtsTaskRejectsTaskWhenTextChanges()
    {
        var task = Task(
            "tts_int_001",
            GenerationTaskType.Tts,
            new Dictionary<string, string>
            {
                ["text"] = "Hello",
                ["emotion"] = "Calm",
                ["voiceReferenceId"] = "voice.wav"
            });

        var preserved = _policy.PreserveTtsTask(task, new Dictionary<string, string>
        {
            ["text"] = "Changed",
            ["emotion"] = "Calm",
            ["voiceReferenceId"] = "voice.wav"
        });

        Assert.Null(preserved);
    }

    [Fact]
    public void PreserveTtsTaskKeepsCompletedResultWhenOnlyVoiceSyncMetadataIsAdded()
    {
        var task = Task(
            "tts_int_001",
            GenerationTaskType.Tts,
            new Dictionary<string, string>
            {
                ["text"] = "Hello",
                ["emotion"] = "Calm",
                ["voiceReferenceId"] = "voice.wav",
                ["referenceText"] = "Reference text"
            });

        var preserved = _policy.PreserveTtsTask(task, new Dictionary<string, string>
        {
            ["text"] = "Hello",
            ["emotion"] = "Calm",
            ["voiceReferenceId"] = "voice.wav",
            ["referenceText"] = "Reference text",
            ["voiceSourceAssetId"] = "asset_voice_001",
            ["voiceSyncedPath"] = @"D:\project\assets\audio\voice-references\voice.wav"
        });

        Assert.NotNull(preserved);
        Assert.Equal(GenerationTaskStatus.Completed, preserved.Status);
        Assert.Equal("asset.png", preserved.ResultAssetId);
        Assert.Equal("asset_voice_001", preserved.Parameters["voiceSourceAssetId"]);
        Assert.Equal(@"D:\project\assets\audio\voice-references\voice.wav", preserved.Parameters["voiceSyncedPath"]);
    }



    private static GenerationTask Task(
        string id,
        GenerationTaskType type,
        IReadOnlyDictionary<string, string> parameters)
    {
        return new GenerationTask(
            id,
            type,
            "int_001",
            id,
            "comfyui",
            GenerationTaskStatus.Completed,
            parameters,
            "asset.png");
    }
}

