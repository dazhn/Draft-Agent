using System.Text.Json;
using System.Text.Json.Nodes;
using DraftAgent.Core;

namespace DraftAgent.Core.Tests;

public sealed class IntegrationBoundaryTests
{
    [Fact]
    public void DraftWorkspaceServiceAnalysisDoesNotUseOldShotListAnalyzer()
    {
        var source = File.ReadAllText(FindSourceFile("src", "DraftAgent.Core", "DraftWorkspaceService.cs"));

        Assert.Contains("AnalyzeWithLlmAsync", source, StringComparison.Ordinal);
        Assert.Contains("SceneInteractionSchemaName", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Analyze(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExportMarkdown", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DraftProject", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AiModelReplyInterpreter_ExtractsJsonFromFencedContent()
    {
        var options = new AiServiceOptions("LM Studio", string.Empty, "http://127.0.0.1:1234/v1", "local");
        var metadata = AiModelReplyInterpreter.BuildOpenAiReplyMetadata(options, "stop", "```json\n{\"scenes\":[]}\n```", null, false);
        var reply = new AiProviderReply("```json\n{\"scenes\":[]}\n```", metadata, "{\"raw\":true}", null);

        var result = AiModelReplyInterpreter.ToJsonResponse(reply, options);

        Assert.Equal("{\"scenes\":[]}", result.Json);
        Assert.Equal(AiReplyState.Completed, result.ReplyMetadata.State);
    }

    [Fact]
    public void AiModelReplyInterpreter_RecoversJsonFromReasoning()
    {
        var options = new AiServiceOptions("LM Studio", string.Empty, "http://127.0.0.1:1234/v1", "local");
        var metadata = AiModelReplyInterpreter.BuildOpenAiReplyMetadata(options, "stop", string.Empty, "thinking {\"scenes\":[]}", false);
        var reply = new AiProviderReply(string.Empty, metadata, "{\"raw\":true}", "thinking {\"scenes\":[]}");

        var result = AiModelReplyInterpreter.ToJsonResponse(reply, options);

        Assert.Equal("{\"scenes\":[]}", result.Json);
        Assert.Equal(AiReplyState.RecoveredFromReasoning, result.ReplyMetadata.State);
        Assert.Equal("thinking {\"scenes\":[]}", result.ReasoningContent);
    }

    [Fact]
    public void ComfyPromptResponseParser_ParsesSubmitResponse()
    {
        var metadata = ComfyPromptResponseParser.ParseSubmitResponse("""{"prompt_id":"prompt-123","number":42}""");

        Assert.Equal("prompt-123", metadata.PromptId);
        Assert.Equal(42, metadata.QueueNumber);
        Assert.Equal(ComfySubmitState.Submitted, metadata.State);
    }

    [Fact]
    public void ComfyPromptResponseParser_ReportsMissingPromptId()
    {
        var exception = Assert.Throws<ComfyPromptException>(() =>
            ComfyPromptResponseParser.ParseSubmitResponse("""{"number":9}"""));

        Assert.Equal(ComfySubmitState.MissingPromptId, exception.Metadata.State);
        Assert.Equal(9, exception.Metadata.QueueNumber);
    }

    [Fact]
    public void ComfyPromptResponseParser_ParsesHistoryOutputsRecursively()
    {
        var result = ComfyPromptResponseParser.TryParseHistory("prompt-audio", """
        {
          "prompt-audio": {
            "status": { "status_str": "success", "completed": true, "messages": [] },
            "outputs": {
              "15": {
                "nested": {
                  "audio": [
                    { "filename": "ComfyUI_00001_.flac", "subfolder": "tts", "type": "output" }
                  ]
                }
              }
            }
          }
        }
        """);

        Assert.NotNull(result);
        Assert.Equal(ComfyPromptState.Completed, result.State);
        Assert.Contains("output|tts|ComfyUI_00001_.flac", result.OutputAssetIds);
    }

    [Fact]
    public void ComfyPromptResponseParser_IgnoresNonAudioHistoryOutputs()
    {
        var result = ComfyPromptResponseParser.TryParseHistory("prompt-image", """
        {
          "prompt-image": {
            "status": { "status_str": "success", "completed": true, "messages": [] },
            "outputs": {
              "12": {
                "images": [
                  { "filename": "ComfyUI_00001_.png", "subfolder": "", "type": "output" }
                ]
              }
            }
          }
        }
        """);

        Assert.NotNull(result);
        Assert.Equal(ComfyPromptState.Running, result.State);
        Assert.Equal(GenerationTaskStatus.Running, result.TaskStatus);
        Assert.Empty(result.OutputAssetIds);
    }

    [Fact]
    public void ComfyPromptResponseParser_ExtractsHistoryExceptionMessage()
    {
        var result = ComfyPromptResponseParser.TryParseHistory("prompt-failed", """
        {
          "prompt-failed": {
            "status": {
              "status_str": "error",
              "completed": false,
              "messages": [
                [
                  "execution_error",
                  {
                    "exception_message": "IndexTTS-2 generation failed: DefaultCPUAllocator: not enough memory"
                  }
                ]
              ]
            },
            "outputs": {}
          }
        }
        """);

        Assert.NotNull(result);
        Assert.Equal(ComfyPromptState.Failed, result.State);
        Assert.Equal(GenerationTaskStatus.Failed, result.TaskStatus);
        Assert.Contains("IndexTTS-2 generation failed", result.Message, StringComparison.Ordinal);
        Assert.Contains("not enough memory", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ComfyPromptResponseParser_ParsesQueuePosition()
    {
        var result = ComfyPromptResponseParser.TryParseQueue("prompt-queue", """
        {
          "queue_running": [],
          "queue_pending": [[7, "prompt-queue", {"client_id": "client"}]]
        }
        """);

        Assert.NotNull(result);
        Assert.Equal(ComfyPromptState.Queued, result.State);
        Assert.Equal(GenerationTaskStatus.Pending, result.TaskStatus);
        Assert.Equal(1, result.QueuePosition);
    }

    [Fact]
    public void ComfyOutputReference_ParseAcceptsOutputAndTempReferences()
    {
        var output = ComfyOutputReference.Parse("output|tts|ComfyUI_00001_.flac");
        var temp = ComfyOutputReference.Parse("temp||ComfyUI_temp_00001_.flac");
        var defaulted = ComfyOutputReference.Parse("ComfyUI_default_00001_.flac");

        Assert.Equal("output", output.Type);
        Assert.Equal("tts", output.Subfolder);
        Assert.Equal("ComfyUI_00001_.flac", output.Filename);
        Assert.Equal("temp", temp.Type);
        Assert.Equal(string.Empty, temp.Subfolder);
        Assert.Equal("ComfyUI_temp_00001_.flac", temp.Filename);
        Assert.Equal("output", defaulted.Type);
        Assert.Equal(string.Empty, defaulted.Subfolder);
        Assert.Equal("ComfyUI_default_00001_.flac", defaulted.Filename);
    }

    [Theory]
    [InlineData("input||voice.wav")]
    [InlineData("output|../secrets|voice.wav")]
    [InlineData("output|tts|../voice.wav")]
    [InlineData("output|tts|")]
    [InlineData("output|tts")]
    public void ComfyOutputReference_ParseRejectsUnsafeReferences(string reference)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ComfyOutputReference.Parse(reference));

        Assert.Contains("ComfyUI output reference", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ComfyWorkflowPatcher_PatchesIndexTts2Workflow()
    {
        var workflow = JsonNode.Parse("""
        {
          "47": { "inputs": { "text": "" }, "class_type": "UnifiedTTSTextNode" },
          "51": { "inputs": { "voice_name": "none", "reference_text": "" }, "class_type": "CharacterVoicesNode" },
          "123": { "inputs": { "model_path": "", "emotion_alpha": 0, "emotion_control": ["125", 0] }, "class_type": "IndexTTSEngineNode" },
          "125": { "inputs": { "Happy": 1, "Angry": 1, "Sad": 1, "Surprised": 1, "Afraid": 1, "Disgusted": 1, "Calm": 1, "Melancholic": 1 }, "class_type": "IndexTTSEmotionOptionsNode" }
        }
        """)!.AsObject();
        var task = new GenerationTask(
            "tts_1",
            GenerationTaskType.Tts,
            "int_1",
            "TTS",
            "comfyui",
            GenerationTaskStatus.Draft,
            new Dictionary<string, string>
            {
                ["text"] = "line text",
                ["emotion"] = "Sad",
                ["emotionStrength"] = "0.75",
                ["voiceReferenceId"] = "linan.wav",
                ["referenceText"] = "reference",
                ["emotionAlpha"] = "1.1"
            });

        ComfyWorkflowPatcher.Patch(workflow, task);

        using var document = JsonDocument.Parse(workflow.ToJsonString(DraftJson.Options));
        var root = document.RootElement;
        Assert.Equal("line text", root.GetProperty("47").GetProperty("inputs").GetProperty("text").GetString());
        Assert.Equal("linan.wav", root.GetProperty("51").GetProperty("inputs").GetProperty("voice_name").GetString());
        Assert.Equal("reference", root.GetProperty("51").GetProperty("inputs").GetProperty("reference_text").GetString());
        Assert.Equal(IndexTts2Defaults.DefaultModelPath, root.GetProperty("123").GetProperty("inputs").GetProperty("model_path").GetString());
        Assert.Equal(1.1, root.GetProperty("123").GetProperty("inputs").GetProperty("emotion_alpha").GetDouble());
        Assert.Equal(IndexTts2Defaults.SafeMaxTextTokensPerSegment, root.GetProperty("123").GetProperty("inputs").GetProperty("max_text_tokens_per_segment").GetInt32());
        Assert.Equal(IndexTts2Defaults.SafeNumBeams, root.GetProperty("123").GetProperty("inputs").GetProperty("num_beams").GetInt32());
        Assert.Equal(IndexTts2Defaults.SafeMaxMelTokens, root.GetProperty("123").GetProperty("inputs").GetProperty("max_mel_tokens").GetInt32());
        Assert.False(root.GetProperty("123").GetProperty("inputs").GetProperty("use_torch_compile").GetBoolean());
        Assert.False(root.GetProperty("123").GetProperty("inputs").GetProperty("low_vram").GetBoolean());
        Assert.Equal("125", root.GetProperty("123").GetProperty("inputs").GetProperty("emotion_control")[0].GetString());
        Assert.Equal(0.75, root.GetProperty("125").GetProperty("inputs").GetProperty("Sad").GetDouble());
        Assert.Equal(0, root.GetProperty("125").GetProperty("inputs").GetProperty("Happy").GetDouble());
    }

    [Fact]
    public void ComfyWorkflowPatcher_RejectsNonTtsTasks()
    {
        var workflow = JsonNode.Parse("""{"11":{"inputs":{"text":""}}}""")!.AsObject();
        var task = new GenerationTask(
            "video_1",
            GenerationTaskType.Video,
            "int_1",
            "Video",
            "comfyui",
            GenerationTaskStatus.Draft,
            new Dictionary<string, string> { ["prompt"] = "make an image" });

        var exception = Assert.Throws<NotSupportedException>(() => ComfyWorkflowPatcher.Patch(workflow, task));

        Assert.Contains("TTS", exception.Message);
        Assert.Contains("removed", exception.Message);
    }

    [Fact]
    public void ComfyWorkflowPatcher_RejectsMissingVoiceReference()
    {
        var workflow = JsonNode.Parse("""
        {
          "47": { "inputs": { "text": "" }, "class_type": "UnifiedTTSTextNode" },
          "51": { "inputs": { "voice_name": "none", "reference_text": "" }, "class_type": "CharacterVoicesNode" },
          "123": { "inputs": { "model_path": "", "emotion_alpha": 0 }, "class_type": "IndexTTSEngineNode" }
        }
        """)!.AsObject();
        var task = new GenerationTask(
            "tts_missing_voice",
            GenerationTaskType.Tts,
            "int_missing_voice",
            "TTS",
            "comfyui",
            GenerationTaskStatus.Draft,
            new Dictionary<string, string>
            {
                ["text"] = "line text",
                ["emotion"] = "Calm",
                ["voiceReferenceId"] = string.Empty
            });

        var exception = Assert.Throws<InvalidOperationException>(() => ComfyWorkflowPatcher.Patch(workflow, task));

        Assert.Contains(TtsTaskValidation.MissingVoiceReferenceMessage, exception.Message);
    }

    [Fact]
    public void ComfyWorkflowPatcher_RejectsUnboundVoicePlaceholder()
    {
        var workflow = JsonNode.Parse("""
        {
          "47": { "inputs": { "text": "" }, "class_type": "UnifiedTTSTextNode" },
          "51": { "inputs": { "voice_name": "none", "reference_text": "" }, "class_type": "CharacterVoicesNode" },
          "123": { "inputs": { "model_path": "", "emotion_alpha": 0 }, "class_type": "IndexTTSEngineNode" }
        }
        """)!.AsObject();
        var task = new GenerationTask(
            "tts_unbound_placeholder",
            GenerationTaskType.Tts,
            "int_unbound_placeholder",
            "TTS",
            "comfyui",
            GenerationTaskStatus.Draft,
            new Dictionary<string, string>
            {
                ["text"] = "line text",
                ["emotion"] = "Calm",
                ["voiceReferenceId"] = "未绑定声音",
                ["referenceText"] = "reference text"
            });

        var exception = Assert.Throws<InvalidOperationException>(() => ComfyWorkflowPatcher.Patch(workflow, task));

        Assert.Contains(TtsTaskValidation.MissingVoiceReferenceMessage, exception.Message);
    }

    [Fact]
    public void ComfyWorkflowPatcher_RejectsPathVoiceReference()
    {
        var workflow = JsonNode.Parse("""
        {
          "47": { "inputs": { "text": "" }, "class_type": "UnifiedTTSTextNode" },
          "51": { "inputs": { "voice_name": "none", "reference_text": "" }, "class_type": "CharacterVoicesNode" },
          "123": { "inputs": { "model_path": "", "emotion_alpha": 0 }, "class_type": "IndexTTSEngineNode" }
        }
        """)!.AsObject();
        var task = new GenerationTask(
            "tts_path_voice",
            GenerationTaskType.Tts,
            "int_path_voice",
            "TTS",
            "comfyui",
            GenerationTaskStatus.Draft,
            new Dictionary<string, string>
            {
                ["text"] = "line text",
                ["emotion"] = "Calm",
                ["voiceReferenceId"] = @"C:\voices\linan.wav",
                ["referenceText"] = "reference text"
            });

        var exception = Assert.Throws<InvalidOperationException>(() => ComfyWorkflowPatcher.Patch(workflow, task));

        Assert.Contains(TtsTaskValidation.InvalidVoiceReferenceMessage, exception.Message);
    }

    [Fact]
    public void ComfyWorkflowPatcher_AllowsComfyRelativeVoiceReferenceKey()
    {
        var workflow = JsonNode.Parse("""
        {
          "47": { "inputs": { "text": "" }, "class_type": "UnifiedTTSTextNode" },
          "51": { "inputs": { "voice_name": "none", "reference_text": "" }, "class_type": "CharacterVoicesNode" },
          "123": { "inputs": { "model_path": "", "emotion_alpha": 0 }, "class_type": "IndexTTSEngineNode" }
        }
        """)!.AsObject();
        var task = new GenerationTask(
            "tts_comfy_relative_voice",
            GenerationTaskType.Tts,
            "int_comfy_relative_voice",
            "TTS",
            "comfyui",
            GenerationTaskStatus.Draft,
            new Dictionary<string, string>
            {
                ["text"] = "line text",
                ["emotion"] = "Calm",
                ["voiceReferenceId"] = "TTS/voices/linan.wav",
                ["referenceText"] = "reference text"
            });

        ComfyWorkflowPatcher.Patch(workflow, task);

        using var document = JsonDocument.Parse(workflow.ToJsonString(DraftJson.Options));
        Assert.Equal(
            "TTS/voices/linan.wav",
            document.RootElement.GetProperty("51").GetProperty("inputs").GetProperty("voice_name").GetString());
    }

    [Fact]
    public void ComfyWorkflowPatcher_AllowsMissingReferenceTextForCharacterVoicesNodeSidecar()
    {
        var workflow = JsonNode.Parse("""
        {
          "47": { "inputs": { "text": "" }, "class_type": "UnifiedTTSTextNode" },
          "51": { "inputs": { "voice_name": "none", "reference_text": "" }, "class_type": "CharacterVoicesNode" },
          "123": { "inputs": { "model_path": "", "emotion_alpha": 0 }, "class_type": "IndexTTSEngineNode" }
        }
        """)!.AsObject();
        var task = new GenerationTask(
            "tts_missing_reference_text",
            GenerationTaskType.Tts,
            "int_missing_reference_text",
            "TTS",
            "comfyui",
            GenerationTaskStatus.Draft,
            new Dictionary<string, string>
            {
                ["text"] = "line text",
                ["emotion"] = "Calm",
                ["voiceReferenceId"] = "custom.wav"
            });

        ComfyWorkflowPatcher.Patch(workflow, task);

        using var document = JsonDocument.Parse(workflow.ToJsonString(DraftJson.Options));
        Assert.Equal("custom.wav", document.RootElement.GetProperty("51").GetProperty("inputs").GetProperty("voice_name").GetString());
        Assert.Equal(string.Empty, document.RootElement.GetProperty("51").GetProperty("inputs").GetProperty("reference_text").GetString());
    }

    private static string FindSourceFile(params string[] segments)
    {
        return Path.Combine(FindWorkspaceRoot(), Path.Combine(segments));
    }

    private static string FindWorkspaceRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "DraftAgent.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException("Could not locate DraftAgent.sln.");
    }
}

