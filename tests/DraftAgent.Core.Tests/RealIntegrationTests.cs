using System.Net;
using System.Text;
using System.Text.Json;
using DraftAgent.Core;

namespace DraftAgent.Core.Tests;

public sealed class RealIntegrationTests
{
    [Fact]
    public async Task HttpLlmAnalysisClient_OpenAiCompatibleConvertsSceneFlowJson()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(new
            {
                choices = new[]
                {
                    new
                    {
                        finish_reason = "stop",
                        message = new
                        {
                            content = """
                            {
                              "scenes": [
                                {
                                  "description": "雨夜街道，雨声和路灯。",
                                  "items": [
                                    { "type": "narration", "role": "", "text": "雨夜的路上，陈默走了过来。", "emotion": "Calm", "intensity": 0.6 },
                                    { "type": "dialogue", "role": "陈默", "emotion": "Afraid", "intensity": 0.7, "text": "我来晚了。" }
                                  ]
                                }
                              ]
                            }
                            """
                        }
                    }
                }
            })
        });
        var client = new HttpLlmAnalysisClient(new HttpClient(handler), () => new AiServiceOptions("OpenAI", "key", "https://api.openai.test/v1", "model"));

        var response = await client.AnalyzeAsync(new LlmAnalysisRequest("陈默说：我来晚了。", DraftWorkspaceService.SceneInteractionSchemaName, "rules"));

        Assert.Contains("/chat/completions", handler.Requests.Single().RequestUri!.ToString());
        Assert.Contains("\"response_format\"", handler.RequestBodies.Single());
        Assert.Contains(response.Project.Characters, character => character.Name == "陈默");
        Assert.Contains(response.Project.Scenes.SelectMany(scene => scene.Interactions), interaction => interaction.Dialogue?.Text == "我来晚了。");
        Assert.Contains(response.Project.GenerationTasks, task => task.Type == GenerationTaskType.Tts);
        Assert.Contains("雨夜街道", response.ModelRawJson);
        Assert.Equal(AiReplyState.Completed, response.ReplyMetadata?.State);
        Assert.Equal("stop", response.ReplyMetadata?.FinishReason);
    }

    [Fact]
    public async Task HttpLlmAnalysisClient_ConvertsNarrationEmotionIntoTtsTask()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(new
            {
                choices = new[]
                {
                    new
                    {
                        finish_reason = "stop",
                        message = new
                        {
                            content = """
                            {
                              "scenes": [
                                {
                                  "description": "雨夜街道，压抑雨声。",
                                  "items": [
                                    {
                                      "type": "narration",
                                      "role": "",
                                      "text": "雨声压住了陈默的呼吸。",
                                      "emotion": "Sad",
                                      "intensity": 0.82
                                    }
                                  ]
                                }
                              ]
                            }
                            """
                        }
                    }
                }
            })
        });
        var client = new HttpLlmAnalysisClient(new HttpClient(handler), () => new AiServiceOptions("OpenAI", "key", "https://api.openai.test/v1", "model"));

        var response = await client.AnalyzeAsync(new LlmAnalysisRequest("雨声压住了陈默的呼吸。", DraftWorkspaceService.SceneInteractionSchemaName, "rules"));

        var narration = response.Project.Scenes.SelectMany(scene => scene.Interactions).Single();
        var ttsTask = response.Project.GenerationTasks.Single(task => task.Type == GenerationTaskType.Tts);
        Assert.NotNull(narration.Narration);
        Assert.Equal("Sad", narration.Narration.Voice.Emotion);
        Assert.Equal("Sad", ttsTask.Parameters["emotion"]);
        Assert.Equal("0.82", ttsTask.Parameters["emotionStrength"]);
    }

    [Fact]
    public async Task HttpLlmAnalysisClient_LmStudioOmitsResponseFormat()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(new
            {
                choices = new[]
                {
                    new
                    {
                        finish_reason = "stop",
                        message = new
                        {
                            content = """
                            {
                              "scenes": [
                                {
                                  "description": "雨夜街道，雨声。",
                                  "items": [
                                    { "type": "dialogue", "role": "陈默", "emotion": "Afraid", "intensity": 0.7, "text": "我来晚了。" }
                                  ]
                                }
                              ]
                            }
                            """
                        }
                    }
                }
            })
        });
        var client = new HttpLlmAnalysisClient(new HttpClient(handler), () => new AiServiceOptions("LM Studio", string.Empty, "http://127.0.0.1:7982/v1", "local"));

        await client.AnalyzeAsync(new LlmAnalysisRequest("陈默说：我来晚了。", DraftWorkspaceService.SceneInteractionSchemaName, "rules"));

        Assert.DoesNotContain("\"response_format\"", handler.RequestBodies.Single());
    }

    [Fact]
    public async Task HttpLlmAnalysisClient_GenerateTextOmitsJsonResponseFormat()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(new
            {
                choices = new[]
                {
                    new
                    {
                        finish_reason = "stop",
                        message = new
                        {
                            content = "优化后的文案"
                        }
                    }
                }
            })
        });
        var client = new HttpLlmAnalysisClient(new HttpClient(handler), () => new AiServiceOptions("OpenAI", "key", "https://api.openai.test/v1", "model"));

        var response = await client.GenerateTextAsync(new AiModelTextRequest("润色规则", "原文"));

        Assert.Equal("优化后的文案", response.Text);
        Assert.DoesNotContain("\"response_format\"", handler.RequestBodies.Single());
        Assert.Contains("润色规则", handler.RequestBodies.Single());
        Assert.Contains("原文", handler.RequestBodies.Single());
    }

    [Fact]
    public async Task HttpLlmAnalysisClient_GeminiConvertsSceneFlowJson()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(new
            {
                candidates = new[]
                {
                    new
                    {
                        finishReason = "STOP",
                        content = new
                        {
                            parts = new[]
                            {
                                new
                                {
                                    text = """
                                    {
                                      "scenes": [
                                        {
                                          "description": "室内房间，暖光和沉默。",
                                          "items": [
                                            { "type": "dialogue", "role": "林安", "emotion": "Sad", "intensity": 0.7, "text": "你从来没有相信我。" }
                                          ]
                                        }
                                      ]
                                    }
                                    """
                                }
                            }
                        }
                    }
                }
            })
        });
        var client = new HttpLlmAnalysisClient(new HttpClient(handler), () => new AiServiceOptions("Gemini", "key", "https://gemini.test/v1beta", "gemini-pro"));

        var response = await client.AnalyzeAsync(new LlmAnalysisRequest("林安：你从来没有相信我。", DraftWorkspaceService.SceneInteractionSchemaName, "rules"));

        Assert.Contains("generateContent", handler.Requests.Single().RequestUri!.ToString());
        Assert.Contains(response.Project.Characters, character => character.Name == "林安");
        Assert.Equal(AiReplyState.Completed, response.ReplyMetadata?.State);
    }

    [Fact]
    public async Task HttpLlmAnalysisClient_CheckConnectionReportsInvalidConfiguration()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(new { data = Array.Empty<object>() })
        });
        var client = new HttpLlmAnalysisClient(new HttpClient(handler), () => new AiServiceOptions("OpenAI", "key", string.Empty, "model"));

        var result = await client.CheckConnectionAsync();

        Assert.Equal(AiConnectionState.InvalidConfiguration, result.State);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task HttpLlmAnalysisClient_CheckConnectionReportsConnected()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(new { data = Array.Empty<object>() })
        });
        var client = new HttpLlmAnalysisClient(new HttpClient(handler), () => new AiServiceOptions("LM Studio", string.Empty, "http://127.0.0.1:7982/v1", "local"));

        var result = await client.CheckConnectionAsync();

        Assert.Equal(AiConnectionState.Connected, result.State);
        Assert.Contains("/models", handler.Requests.Single().RequestUri!.ToString());
    }

    [Fact]
    public async Task HttpLlmAnalysisClient_CheckConnectionReportsUnauthorized()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("unauthorized")
        });
        var client = new HttpLlmAnalysisClient(new HttpClient(handler), () => new AiServiceOptions("OpenAI", "bad-key", "https://api.openai.test/v1", "model"));

        var result = await client.CheckConnectionAsync();

        Assert.Equal(AiConnectionState.Unauthorized, result.State);
        Assert.Equal(401, result.HttpStatusCode);
    }

    [Fact]
    public async Task HttpLlmAnalysisClient_ReportsReasoningOnlyReplyState()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(new
            {
                choices = new[]
                {
                    new
                    {
                        finish_reason = "stop",
                        message = new
                        {
                            content = "",
                            reasoning_content = "Thinking..."
                        }
                    }
                }
            })
        });
        var client = new HttpLlmAnalysisClient(new HttpClient(handler), () => new AiServiceOptions("LM Studio", string.Empty, "http://127.0.0.1:7982/v1", "local"));

        var exception = await Assert.ThrowsAsync<ExternalIntegrationException>(() =>
            client.AnalyzeAsync(new LlmAnalysisRequest("text", DraftWorkspaceService.SceneInteractionSchemaName, "rules")));

        var replyException = Assert.IsType<AiReplyException>(exception.InnerException);
        Assert.Equal(AiReplyState.ReasoningOnly, replyException.Metadata.State);
        Assert.Equal("Thinking...", replyException.ReasoningContent);
        Assert.Contains("reasoning_content", replyException.RawResponse);
    }

    [Fact]
    public async Task HttpLlmAnalysisClient_ReportsLengthLimitedReplyState()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(new
            {
                choices = new[]
                {
                    new
                    {
                        finish_reason = "length",
                        message = new
                        {
                            content = "{"
                        }
                    }
                }
            })
        });
        var client = new HttpLlmAnalysisClient(new HttpClient(handler), () => new AiServiceOptions("OpenAI", "key", "https://api.openai.test/v1", "model"));

        var exception = await Assert.ThrowsAsync<ExternalIntegrationException>(() =>
            client.AnalyzeAsync(new LlmAnalysisRequest("text", DraftWorkspaceService.SceneInteractionSchemaName, "rules")));

        var replyException = Assert.IsType<AiReplyException>(exception.InnerException);
        Assert.Equal(AiReplyState.LengthLimited, replyException.Metadata.State);
    }

    [Fact]
    public async Task HttpLlmAnalysisClient_ReportsReasoningLengthLimitedBeforeLengthLimited()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(new
            {
                choices = new[]
                {
                    new
                    {
                        finish_reason = "length",
                        message = new
                        {
                            content = string.Empty,
                            reasoning_content = "Thinking Process..."
                        }
                    }
                }
            })
        });
        var client = new HttpLlmAnalysisClient(new HttpClient(handler), () => new AiServiceOptions("LM Studio", string.Empty, "http://127.0.0.1:7982/v1", "local"));

        var exception = await Assert.ThrowsAsync<ExternalIntegrationException>(() =>
            client.AnalyzeAsync(new LlmAnalysisRequest("text", DraftWorkspaceService.SceneInteractionSchemaName, "rules")));

        var replyException = Assert.IsType<AiReplyException>(exception.InnerException);
        Assert.Equal(AiReplyState.ReasoningLengthLimited, replyException.Metadata.State);
        Assert.Equal(0, replyException.Metadata.ContentLength);
        Assert.True(replyException.Metadata.ReasoningLength > 0);
        Assert.Equal("Thinking Process...", replyException.ReasoningContent);
        Assert.Contains("Thinking Process", replyException.RawResponse);
    }

    [Fact]
    public async Task HttpLlmAnalysisClient_RecoversJsonFromReasoningContent()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(new
            {
                choices = new[]
                {
                    new
                    {
                        finish_reason = "stop",
                        message = new
                        {
                            content = string.Empty,
                            reasoning_content = """
                            分析过程略。
                            {
                              "scenes": [
                                {
                                  "description": "雨夜街道，雨声。",
                                  "items": [
                                    { "type": "dialogue", "role": "陈默", "emotion": "Afraid", "intensity": 0.7, "text": "我来晚了。" }
                                  ]
                                }
                              ]
                            }
                            """
                        }
                    }
                }
            })
        });
        var client = new HttpLlmAnalysisClient(new HttpClient(handler), () => new AiServiceOptions("LM Studio", string.Empty, "http://127.0.0.1:7982/v1", "local"));

        var response = await client.AnalyzeAsync(new LlmAnalysisRequest("陈默说：我来晚了。", DraftWorkspaceService.SceneInteractionSchemaName, "rules"));

        Assert.Equal(AiReplyState.RecoveredFromReasoning, response.ReplyMetadata?.State);
        Assert.Contains(response.Project.Characters, character => character.Name == "陈默");
        Assert.Contains(response.Project.Scenes.SelectMany(scene => scene.Interactions), interaction => interaction.Dialogue?.Text == "我来晚了。");
    }

    [Fact]
    public async Task HttpLlmAnalysisClient_LmStudioRequestsNoReasoningEffort()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(new
            {
                choices = new[]
                {
                    new
                    {
                        finish_reason = "stop",
                        message = new
                        {
                            content = """
                            {
                              "scenes": [
                                {
                                  "description": "雨夜街道，雨声。",
                                  "items": [
                                    { "type": "dialogue", "role": "陈默", "emotion": "Afraid", "intensity": 0.7, "text": "我来晚了。" }
                                  ]
                                }
                              ]
                            }
                            """
                        }
                    }
                }
            })
        });
        var client = new HttpLlmAnalysisClient(new HttpClient(handler), () => new AiServiceOptions("LM Studio", string.Empty, "http://127.0.0.1:7982/v1", "local"));

        await client.AnalyzeAsync(new LlmAnalysisRequest("陈默说：我来晚了。", DraftWorkspaceService.SceneInteractionSchemaName, "rules"));

        var requestBody = handler.RequestBodies.Single();
        Assert.Contains("\"reasoning_effort\": \"none\"", requestBody);
        Assert.Contains("\"enable_thinking\": false", requestBody);
        Assert.Contains("\"chat_template_kwargs\"", requestBody);
        Assert.Contains("/no_think", requestBody);
        Assert.Contains("\"max_tokens\": 65536", requestBody);
    }

    [Fact]
    public async Task HttpLlmAnalysisClient_UsesConfiguredMaxTokensForLmStudio()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(new
            {
                choices = new[]
                {
                    new
                    {
                        finish_reason = "stop",
                        message = new
                        {
                            content = """
                            {
                              "scenes": [
                                {
                                  "description": "雨夜街道，雨声。",
                                  "items": [
                                    { "type": "dialogue", "role": "陈默", "emotion": "Afraid", "intensity": 0.7, "text": "我来晚了。" }
                                  ]
                                }
                              ]
                            }
                            """
                        }
                    }
                }
            })
        });
        var client = new HttpLlmAnalysisClient(new HttpClient(handler), () => new AiServiceOptions("LM Studio", string.Empty, "http://127.0.0.1:7982/v1", "local", 8192));

        await client.AnalyzeAsync(new LlmAnalysisRequest("陈默说：我来晚了。", DraftWorkspaceService.SceneInteractionSchemaName, "rules"));

        Assert.Contains("\"max_tokens\": 8192", handler.RequestBodies.Single());
    }

    [Fact]
    public async Task HttpLlmAnalysisClient_UsesConfiguredMaxCompletionTokensForOpenAi()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(new
            {
                choices = new[]
                {
                    new
                    {
                        finish_reason = "stop",
                        message = new
                        {
                            content = """
                            {
                              "scenes": [
                                {
                                  "description": "雨夜街道，雨声。",
                                  "items": [
                                    { "type": "dialogue", "role": "陈默", "emotion": "Afraid", "intensity": 0.7, "text": "我来晚了。" }
                                  ]
                                }
                              ]
                            }
                            """
                        }
                    }
                }
            })
        });
        var client = new HttpLlmAnalysisClient(new HttpClient(handler), () => new AiServiceOptions("OpenAI", "key", "https://api.openai.test/v1", "model", 4096));

        await client.AnalyzeAsync(new LlmAnalysisRequest("陈默说：我来晚了。", DraftWorkspaceService.SceneInteractionSchemaName, "rules"));

        var requestBody = handler.RequestBodies.Single();
        Assert.Contains("\"max_completion_tokens\": 4096", requestBody);
        Assert.DoesNotContain("\"max_tokens\"", requestBody);
    }

    [Fact]
    public async Task HttpLlmAnalysisClient_UsesConfiguredMaxOutputTokensForGemini()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(new
            {
                candidates = new[]
                {
                    new
                    {
                        finishReason = "STOP",
                        content = new
                        {
                            parts = new[]
                            {
                                new
                                {
                                    text = """
                                    {
                                      "scenes": [
                                        {
                                          "description": "雨夜街道，雨声。",
                                          "items": [
                                            { "type": "dialogue", "role": "陈默", "emotion": "Afraid", "intensity": 0.7, "text": "我来晚了。" }
                                          ]
                                        }
                                      ]
                                    }
                                    """
                                }
                            }
                        }
                    }
                }
            })
        });
        var client = new HttpLlmAnalysisClient(new HttpClient(handler), () => new AiServiceOptions("Gemini", "key", "https://gemini.test/v1beta", "gemini-pro", 3072));

        await client.AnalyzeAsync(new LlmAnalysisRequest("陈默说：我来晚了。", DraftWorkspaceService.SceneInteractionSchemaName, "rules"));

        Assert.Contains("\"maxOutputTokens\": 3072", handler.RequestBodies.Single());
    }

    [Fact]
    public async Task HttpLlmAnalysisClient_ConvertsArraySceneWrapperWithContractFields()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(new
            {
                choices = new[]
                {
                    new
                    {
                        finish_reason = "stop",
                        message = new
                        {
                            content = """
                            [
                              {
                                "id": "s_01",
                                "description": "雨夜街口，雨声和路灯。",
                                "items": [
                                  {
                                    "type": "narration",
                                    "role": "",
                                    "text": "林安站在雨夜街口，等了陈默三个小时。",
                                    "emotion": "Sad",
                                    "intensity": 0.7
                                  },
                                  {
                                    "type": "dialogue",
                                    "role": "陈默",
                                    "emotion": "Sad",
                                    "intensity": 0.8,
                                    "text": "对不起，我来晚了。"
                                  }
                                ]
                              }
                            ]
                            """
                        }
                    }
                }
            })
        });
        var client = new HttpLlmAnalysisClient(new HttpClient(handler), () => new AiServiceOptions("LM Studio", string.Empty, "http://127.0.0.1:7982/v1", "local"));

        var response = await client.AnalyzeAsync(new LlmAnalysisRequest("陈默说：对不起，我来晚了。", DraftWorkspaceService.SceneInteractionSchemaName, "rules"));

        Assert.Contains(response.Project.Scenes, scene => scene.Name == "雨夜街口，雨声和路灯。");
        Assert.Contains(response.Project.Characters, character => character.Name == "陈默");
        Assert.Contains(response.Project.Scenes.SelectMany(scene => scene.Interactions), interaction => interaction.Dialogue?.Text == "对不起，我来晚了。");
    }

    [Fact]
    public async Task HttpLlmAnalysisClient_ReportsContentFilteredReplyState()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(new
            {
                choices = new[]
                {
                    new
                    {
                        finish_reason = "content_filter",
                        message = new
                        {
                            content = string.Empty
                        }
                    }
                }
            })
        });
        var client = new HttpLlmAnalysisClient(new HttpClient(handler), () => new AiServiceOptions("OpenAI", "key", "https://api.openai.test/v1", "model"));

        var exception = await Assert.ThrowsAsync<ExternalIntegrationException>(() =>
            client.AnalyzeAsync(new LlmAnalysisRequest("text", DraftWorkspaceService.SceneInteractionSchemaName, "rules")));

        var replyException = Assert.IsType<AiReplyException>(exception.InnerException);
        Assert.Equal(AiReplyState.ContentFiltered, replyException.Metadata.State);
    }

    [Fact]
    public async Task HttpLlmAnalysisClient_ReportsToolCallsOnlyReplyState()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(new
            {
                choices = new[]
                {
                    new
                    {
                        finish_reason = "tool_calls",
                        message = new
                        {
                            content = string.Empty,
                            tool_calls = new[]
                            {
                                new { id = "call_1", type = "function" }
                            }
                        }
                    }
                }
            })
        });
        var client = new HttpLlmAnalysisClient(new HttpClient(handler), () => new AiServiceOptions("OpenAI", "key", "https://api.openai.test/v1", "model"));

        var exception = await Assert.ThrowsAsync<ExternalIntegrationException>(() =>
            client.AnalyzeAsync(new LlmAnalysisRequest("text", DraftWorkspaceService.SceneInteractionSchemaName, "rules")));

        var replyException = Assert.IsType<AiReplyException>(exception.InnerException);
        Assert.Equal(AiReplyState.ToolCallsOnly, replyException.Metadata.State);
    }

    [Fact]
    public async Task HttpLlmAnalysisClient_ReportsInvalidJsonReplyState()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(new
            {
                choices = new[]
                {
                    new
                    {
                        finish_reason = "stop",
                        message = new
                        {
                            content = "{bad json}"
                        }
                    }
                }
            })
        });
        var client = new HttpLlmAnalysisClient(new HttpClient(handler), () => new AiServiceOptions("OpenAI", "key", "https://api.openai.test/v1", "model"));

        var exception = await Assert.ThrowsAsync<ExternalIntegrationException>(() =>
            client.AnalyzeAsync(new LlmAnalysisRequest("text", DraftWorkspaceService.SceneInteractionSchemaName, "rules")));

        var replyException = Assert.IsType<AiReplyException>(exception.InnerException);
        Assert.Equal(AiReplyState.InvalidJson, replyException.Metadata.State);
    }

    [Fact]
    public async Task HttpLlmAnalysisClient_ReportsGeminiSafetyReplyState()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(new
            {
                candidates = new[]
                {
                    new
                    {
                        finishReason = "SAFETY",
                        content = new
                        {
                            parts = new[]
                            {
                                new { text = string.Empty }
                            }
                        }
                    }
                }
            })
        });
        var client = new HttpLlmAnalysisClient(new HttpClient(handler), () => new AiServiceOptions("Gemini", "key", "https://gemini.test/v1beta", "gemini-pro"));

        var exception = await Assert.ThrowsAsync<ExternalIntegrationException>(() =>
            client.AnalyzeAsync(new LlmAnalysisRequest("text", DraftWorkspaceService.SceneInteractionSchemaName, "rules")));

        var replyException = Assert.IsType<AiReplyException>(exception.InnerException);
        Assert.Equal(AiReplyState.ContentFiltered, replyException.Metadata.State);
    }

    [Fact]
    public async Task HttpLlmAnalysisClient_WrapsHttpFailures()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("bad request")
        });
        var client = new HttpLlmAnalysisClient(new HttpClient(handler), () => new AiServiceOptions("OpenAI", "key", "https://api.openai.test/v1", "model"));

        var exception = await Assert.ThrowsAsync<ExternalIntegrationException>(() =>
            client.AnalyzeAsync(new LlmAnalysisRequest("text", DraftWorkspaceService.SceneInteractionSchemaName, "rules")));

        Assert.Equal("LLM", exception.Integration);
        Assert.Equal("analyze", exception.Operation);
    }

    [Fact]
    public async Task ComfyUiHttpClient_SubmitsTtsWorkflowToPromptEndpoint()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(new { prompt_id = "prompt-001", number = 1 })
        });
        var workflowPath = Path.Combine(AppContext.BaseDirectory, "test-tts-workflow.json");
        await File.WriteAllTextAsync(workflowPath, """
        {
          "137": { "inputs": { "value": "" }, "class_type": "PrimitiveString" },
          "51": { "inputs": { "voice_name": "none", "reference_text": "" }, "class_type": "CharacterVoicesNode" },
          "134": { "inputs": { "Happy": 0, "Angry": 0, "Sad": 0, "Surprised": 0, "Afraid": 0, "Disgusted": 0, "Calm": 0, "Melancholic": 0 }, "class_type": "IndexTTSEmotionOptionsNode" }
        }
        """);
        var client = new ComfyUiHttpClient(new HttpClient(handler), () => new ComfyUiOptions("http://127.0.0.1:8188", workflowPath));
        var task = new GenerationTask(
            "tts_1",
            GenerationTaskType.Tts,
            "int_1",
            "TTS",
            "comfyui",
            GenerationTaskStatus.Draft,
            new Dictionary<string, string>
            {
                ["text"] = "我来晚了。",
                ["emotion"] = "紧张",
                ["emotionStrength"] = "0.6",
                ["voiceReferenceId"] = "test.wav",
                ["referenceText"] = "测试参考文本"
            });

        var result = await client.SubmitAsync(new ComfyTaskRequest(task));
        var requestBody = handler.RequestBodies.Single();

        Assert.Equal(GenerationTaskStatus.Pending, result.Status);
        Assert.Equal("prompt-001", result.ResultAssetId);
        Assert.Equal(ComfySubmitState.Submitted, result.PromptMetadata?.State);
        Assert.Equal(1, result.PromptMetadata?.QueueNumber);
        Assert.Contains("/prompt", handler.Requests.Single().RequestUri!.ToString());
        Assert.Contains("我来晚了。", requestBody);
        Assert.Contains("test.wav", requestBody);
        Assert.Contains("\"Afraid\": 0.6", requestBody);
    }

    [Fact]
    public async Task ComfyUiHttpClient_ReadsCharacterVoicesNodeCatalogAndFiltersExamples()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
            {
              "CharacterVoicesNode": {
                "input": {
                  "required": {
                    "voice_name": [
                      [
                        "none",
                        "hero.wav",
                        "TTS/voices/legacy.wav",
                        "voices_examples/female/female_02.wav",
                        "custom_nodes/tts_audio_suite/voices_examples/sample.wav",
                        "C:/absolute/bad.wav"
                      ],
                      { "default": "none" }
                    ]
                  }
                }
              }
            }
            """, Encoding.UTF8, "application/json")
        });
        var client = new ComfyUiHttpClient(new HttpClient(handler), () => new ComfyUiOptions("http://127.0.0.1:8188"));

        var catalog = await client.GetVoiceCatalogAsync();

        Assert.Equal(["hero.wav", "TTS/voices/legacy.wav"], catalog.Voices.Select(voice => voice.VoiceName));
        Assert.Contains("/object_info/CharacterVoicesNode", handler.Requests.Single().RequestUri!.ToString());
    }

    [Fact]
    public async Task ComfyUiHttpClient_RejectsNonTtsTasksBeforeWorkflowOrHttp()
    {
        var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("HTTP should not be called."));
        var client = new ComfyUiHttpClient(
            new HttpClient(handler),
            () => new ComfyUiOptions("http://127.0.0.1:8188", "missing-tts-workflow.json"));
        var task = new GenerationTask(
            "video_1",
            GenerationTaskType.Video,
            "int_1",
            "Video",
            "comfyui",
            GenerationTaskStatus.Draft,
            new Dictionary<string, string> { ["prompt"] = "make an image" });

        var exception = await Assert.ThrowsAsync<ExternalIntegrationException>(() => client.SubmitAsync(new ComfyTaskRequest(task)));

        Assert.Equal("ComfyUI", exception.Integration);
        Assert.Equal("submit", exception.Operation);
        var notSupported = Assert.IsType<NotSupportedException>(exception.InnerException);
        Assert.Contains("TTS", notSupported.Message);
        Assert.Contains("removed", notSupported.Message);
        Assert.Empty(handler.Requests);
        Assert.Empty(handler.RequestBodies);
    }

    [Fact]
    public async Task ComfyUiHttpClient_SubmitsIndexTts2WorkflowWithEightEmotionVectors()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(new { prompt_id = "prompt-idx-001", number = 2 })
        });
        var workflowPath = Path.Combine(AppContext.BaseDirectory, $"test-index-tts2-workflow-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(workflowPath, """
        {
          "47": { "inputs": { "text": "", "TTS_engine": ["123", 0], "opt_narrator": ["51", 0] }, "class_type": "UnifiedTTSTextNode" },
          "51": { "inputs": { "voice_name": "none", "reference_text": "" }, "class_type": "CharacterVoicesNode" },
          "123": { "inputs": { "model_path": "IndexTTS-2", "emotion_alpha": 0.2, "emotion_control": ["125", 0] }, "class_type": "IndexTTSEngineNode" },
          "125": { "inputs": { "Happy": 1, "Angry": 1, "Sad": 1, "Surprised": 1, "Afraid": 1, "Disgusted": 1, "Calm": 1, "Melancholic": 1 }, "class_type": "IndexTTSEmotionOptionsNode" }
        }
        """);
        var client = new ComfyUiHttpClient(new HttpClient(handler), () => new ComfyUiOptions("http://127.0.0.1:8188", workflowPath));
        var task = new GenerationTask(
            "tts_1",
            GenerationTaskType.Tts,
            "int_1",
            "TTS",
            "comfyui",
            GenerationTaskStatus.Draft,
            new Dictionary<string, string>
            {
                ["text"] = "你从来没有相信我。",
                ["emotion"] = "厌恶",
                ["emotionStrength"] = "1.1",
                ["voiceReferenceId"] = "female_01.wav",
                ["referenceText"] = "大家好，我是联系时长两年半的练习生，蔡徐坤",
                ["emotionAlpha"] = "1.2"
            });

        await client.SubmitAsync(new ComfyTaskRequest(task));
        using var document = JsonDocument.Parse(handler.RequestBodies.Single());
        var prompt = document.RootElement.GetProperty("prompt");
        var text = prompt.GetProperty("47").GetProperty("inputs").GetProperty("text").GetString();
        var voice = prompt.GetProperty("51").GetProperty("inputs").GetProperty("voice_name").GetString();
        var referenceText = prompt.GetProperty("51").GetProperty("inputs").GetProperty("reference_text").GetString();
        var modelPath = prompt.GetProperty("123").GetProperty("inputs").GetProperty("model_path").GetString();
        var emotionAlpha = prompt.GetProperty("123").GetProperty("inputs").GetProperty("emotion_alpha").GetDouble();
        var emotionControl = prompt.GetProperty("123").GetProperty("inputs").GetProperty("emotion_control");
        var emotions = prompt.GetProperty("125").GetProperty("inputs");

        Assert.Equal("你从来没有相信我。", text);
        Assert.Equal("female_01.wav", voice);
        Assert.Equal("大家好，我是联系时长两年半的练习生，蔡徐坤", referenceText);
        Assert.Equal("local:IndexTTS-2", modelPath);
        Assert.Equal(1.2, emotionAlpha);
        Assert.Equal("125", emotionControl[0].GetString());
        Assert.Equal(0, emotionControl[1].GetInt32());
        Assert.Equal(0, emotions.GetProperty("Happy").GetDouble());
        Assert.Equal(0, emotions.GetProperty("Angry").GetDouble());
        Assert.Equal(0, emotions.GetProperty("Sad").GetDouble());
        Assert.Equal(0, emotions.GetProperty("Surprised").GetDouble());
        Assert.Equal(0, emotions.GetProperty("Afraid").GetDouble());
        Assert.Equal(1.1, emotions.GetProperty("Disgusted").GetDouble());
        Assert.Equal(0, emotions.GetProperty("Calm").GetDouble());
        Assert.Equal(0, emotions.GetProperty("Melancholic").GetDouble());
    }

    [Fact]
    public async Task ComfyUiHttpClient_PatchesCurrentIndexTts2JsonNodesWithoutChangingStructure()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(new { prompt_id = "prompt-current-json", number = 3 })
        });
        var workflowPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "IndexTTS-2.json"));
        var client = new ComfyUiHttpClient(new HttpClient(handler), () => new ComfyUiOptions("http://127.0.0.1:8188", workflowPath));
        var task = new GenerationTask(
            "tts_current",
            GenerationTaskType.Tts,
            "int_current",
            "TTS",
            "comfyui",
            GenerationTaskStatus.Draft,
            new Dictionary<string, string>
            {
                ["text"] = "我来晚了。",
                ["emotion"] = "Afraid",
                ["emotionStrength"] = "1.17",
                ["voiceReferenceId"] = IndexTts2Defaults.DefaultVoiceReferenceId,
                ["referenceText"] = "大家好，我是联系时长两年半的练习生，蔡徐坤"
            });

        await client.SubmitAsync(new ComfyTaskRequest(task));
        using var document = JsonDocument.Parse(handler.RequestBodies.Single());
        var prompt = document.RootElement.GetProperty("prompt");
        var node47 = prompt.GetProperty("47").GetProperty("inputs");
        var node51 = prompt.GetProperty("51").GetProperty("inputs");
        var node123 = prompt.GetProperty("123").GetProperty("inputs");
        var node134 = prompt.GetProperty("134").GetProperty("inputs");

        Assert.Equal("我来晚了。", node47.GetProperty("text").GetString());
        Assert.Equal(IndexTts2Defaults.DefaultVoiceReferenceId, node51.GetProperty("voice_name").GetString());
        Assert.Equal("大家好，我是联系时长两年半的练习生，蔡徐坤", node51.GetProperty("reference_text").GetString());
        Assert.Equal(IndexTts2Defaults.DefaultModelPath, node123.GetProperty("model_path").GetString());
        Assert.Equal("134", node123.GetProperty("emotion_control")[0].GetString());
        Assert.Equal(1.17, node134.GetProperty("Afraid").GetDouble());
        Assert.Equal(0, node134.GetProperty("Happy").GetDouble());
        Assert.Equal(0, node134.GetProperty("Disgusted").GetDouble());
    }

    [Fact]
    public async Task ComfyUiHttpClient_RejectsAbsoluteVoiceReference()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(new { prompt_id = "prompt-absolute-voice", number = 5 })
        });
        var workflowPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "IndexTTS-2.json"));
        var client = new ComfyUiHttpClient(new HttpClient(handler), () => new ComfyUiOptions("http://127.0.0.1:8188", workflowPath));
        var task = new GenerationTask(
            "tts_absolute_voice",
            GenerationTaskType.Tts,
            "int_absolute_voice",
            "TTS",
            "comfyui",
            GenerationTaskStatus.Draft,
            new Dictionary<string, string>
            {
                ["text"] = "我来晚了。",
                ["emotion"] = "紧张",
                ["emotionStrength"] = "0.9",
                ["voiceReferenceId"] = @"C:\Users\Zhong\Pictures\ComfyUI\models\voices\林安.flac",
                ["referenceText"] = "林安参考文本"
            });

        var exception = await Assert.ThrowsAsync<ExternalIntegrationException>(() => client.SubmitAsync(new ComfyTaskRequest(task)));

        Assert.Contains(TtsTaskValidation.InvalidVoiceReferenceMessage, exception.Message);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Empty(handler.RequestBodies);
    }

    [Fact]
    public async Task ComfyUiHttpClient_RejectsUnboundVoiceReference()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(new { prompt_id = "prompt-default-voice", number = 4 })
        });
        var workflowPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "IndexTTS-2.json"));
        var client = new ComfyUiHttpClient(new HttpClient(handler), () => new ComfyUiOptions("http://127.0.0.1:8188", workflowPath));
        var task = new GenerationTask(
            "tts_default_voice",
            GenerationTaskType.Tts,
            "int_default_voice",
            "TTS",
            "comfyui",
            GenerationTaskStatus.Draft,
            new Dictionary<string, string>
            {
                ["text"] = "我来晚了。",
                ["emotion"] = "紧张",
                ["emotionStrength"] = "0.9",
                ["voiceReferenceId"] = string.Empty
            });

        var exception = await Assert.ThrowsAsync<ExternalIntegrationException>(() =>
            client.SubmitAsync(new ComfyTaskRequest(task)));

        Assert.Contains(TtsTaskValidation.MissingVoiceReferenceMessage, exception.Message);
        Assert.Empty(handler.RequestBodies);
    }

    [Fact]
    public async Task ComfyUiHttpClient_CheckConnectionReportsConnected()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(new { system = new { os = "test" } })
        });
        var client = new ComfyUiHttpClient(new HttpClient(handler), () => new ComfyUiOptions("http://127.0.0.1:8188"));

        var result = await client.CheckComfyConnectionAsync();

        Assert.Equal(ComfyConnectionState.Connected, result.State);
        Assert.Contains("/system_stats", handler.Requests.Single().RequestUri!.ToString());
    }

    [Fact]
    public async Task ComfyUiHttpClient_CheckConnectionReportsInvalidConfiguration()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new ComfyUiHttpClient(new HttpClient(handler), () => new ComfyUiOptions(string.Empty));

        var result = await client.CheckComfyConnectionAsync();

        Assert.Equal(ComfyConnectionState.InvalidConfiguration, result.State);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ComfyUiHttpClient_ReportsMissingPromptId()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(new { number = 9 })
        });
        var workflowPath = Path.Combine(AppContext.BaseDirectory, $"test-tts-workflow-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(workflowPath, """
        {
          "137": { "inputs": { "value": "" }, "class_type": "PrimitiveString" },
          "51": { "inputs": { "voice_name": "none" }, "class_type": "CharacterVoicesNode" },
          "134": { "inputs": { "Happy": 0, "Angry": 0, "Sad": 0, "Surprised": 0, "Afraid": 0, "Disgusted": 0, "Calm": 0, "Melancholic": 0 }, "class_type": "IndexTTSEmotionOptionsNode" }
        }
        """);
        var client = new ComfyUiHttpClient(new HttpClient(handler), () => new ComfyUiOptions("http://127.0.0.1:8188", workflowPath));
        var task = new GenerationTask(
            "tts_1",
            GenerationTaskType.Tts,
            "int_1",
            "TTS",
            "comfyui",
            GenerationTaskStatus.Draft,
            new Dictionary<string, string>
            {
                ["voiceReferenceId"] = IndexTts2Defaults.DefaultVoiceReferenceId,
                ["referenceText"] = "测试参考文本"
            });

        var exception = await Assert.ThrowsAsync<ExternalIntegrationException>(() => client.SubmitAsync(new ComfyTaskRequest(task)));

        var promptException = Assert.IsType<ComfyPromptException>(exception.InnerException);
        Assert.Equal(ComfySubmitState.MissingPromptId, promptException.Metadata.State);
        Assert.Equal(9, promptException.Metadata.QueueNumber);
    }

    [Fact]
    public async Task ComfyUiHttpClient_GetPromptStatusIgnoresNonAudioHistoryOutputs()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/history/prompt-001"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent(new
                    {
                        prompt_001 = new { }
                    })
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        handler = new StubHttpMessageHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
            {
              "prompt-001": {
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
            """)
        });
        var client = new ComfyUiHttpClient(new HttpClient(handler), () => new ComfyUiOptions("http://127.0.0.1:8188"));

        var result = await client.GetPromptStatusAsync("prompt-001");

        Assert.Equal(ComfyPromptState.Running, result.State);
        Assert.Equal(GenerationTaskStatus.Running, result.TaskStatus);
        Assert.Empty(result.OutputAssetIds);
        Assert.Contains("no TTS audio output", result.Message);
        Assert.Contains("/history/prompt-001", handler.Requests.Single().RequestUri!.ToString());
    }

    [Fact]
    public async Task ComfyUiHttpClient_GetPromptStatusPreservesTempAudioAssetType()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
            {
              "prompt-audio": {
                "status": { "status_str": "success", "completed": true, "messages": [] },
                "outputs": {
                  "15": {
                    "audio": [
                      { "filename": "ComfyUI_temp_tufgu_00001_.flac", "subfolder": "", "type": "temp" }
                    ]
                  }
                }
              }
            }
            """)
        });
        var client = new ComfyUiHttpClient(new HttpClient(handler), () => new ComfyUiOptions("http://127.0.0.1:8188"));

        var result = await client.GetPromptStatusAsync("prompt-audio");

        Assert.Equal(ComfyPromptState.Completed, result.State);
        Assert.Equal(GenerationTaskStatus.Completed, result.TaskStatus);
        Assert.Contains("temp||ComfyUI_temp_tufgu_00001_.flac", result.OutputAssetIds);
    }

    [Fact]
    public async Task ComfyUiHttpClient_GetPromptStatusReportsFailedHistory()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
            {
              "prompt-err": {
                "status": {
                  "status_str": "error",
                  "completed": false,
                  "messages": [["execution_error", {"exception_message": "node failed"}]]
                },
                "outputs": {}
              }
            }
            """)
        });
        var client = new ComfyUiHttpClient(new HttpClient(handler), () => new ComfyUiOptions("http://127.0.0.1:8188"));

        var result = await client.GetPromptStatusAsync("prompt-err");

        Assert.Equal(ComfyPromptState.Failed, result.State);
        Assert.Equal(GenerationTaskStatus.Failed, result.TaskStatus);
    }

    [Fact]
    public async Task ComfyUiHttpClient_GetPromptStatusReportsQueued()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/history/"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "queue_running": [],
                  "queue_pending": [
                    [7, "prompt-queue", {"client_id": "client"}]
                  ]
                }
                """)
            };
        });
        var client = new ComfyUiHttpClient(new HttpClient(handler), () => new ComfyUiOptions("http://127.0.0.1:8188"));

        var result = await client.GetPromptStatusAsync("prompt-queue");

        Assert.Equal(ComfyPromptState.Queued, result.State);
        Assert.Equal(GenerationTaskStatus.Pending, result.TaskStatus);
        Assert.Equal(1, result.QueuePosition);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ComfyUiHttpClient_GetPromptStatusReportsRunning()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/history/"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "queue_running": [
                    [3, "prompt-run", {"client_id": "client"}]
                  ],
                  "queue_pending": []
                }
                """)
            };
        });
        var client = new ComfyUiHttpClient(new HttpClient(handler), () => new ComfyUiOptions("http://127.0.0.1:8188"));

        var result = await client.GetPromptStatusAsync("prompt-run");

        Assert.Equal(ComfyPromptState.Running, result.State);
        Assert.Equal(GenerationTaskStatus.Running, result.TaskStatus);
        Assert.Equal(1, result.QueuePosition);
    }

    [Fact]
    public async Task ComfyUiHttpClient_WrapsMissingWorkflowFile()
    {
        var client = new ComfyUiHttpClient(
            new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))),
            () => new ComfyUiOptions("http://127.0.0.1:8188", "missing-workflow.json"));
        var task = new GenerationTask("tts_1", GenerationTaskType.Tts, "int_1", "TTS", "comfyui", GenerationTaskStatus.Draft, new Dictionary<string, string>());

        var exception = await Assert.ThrowsAsync<ExternalIntegrationException>(() => client.SubmitAsync(new ComfyTaskRequest(task)));

        Assert.Equal("ComfyUI", exception.Integration);
        Assert.Equal("submit", exception.Operation);
    }

    [Fact]
    public async Task ComfyUiHttpClient_DeletesQueuedPromptsByPromptId()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new ComfyUiHttpClient(new HttpClient(handler), () => new ComfyUiOptions("http://127.0.0.1:8188"));

        var result = await client.DeleteQueuedPromptsAsync(["prompt-a", "prompt-b", "prompt-a", " "]);

        Assert.Equal(["prompt-a", "prompt-b"], result.PromptIds);
        Assert.Contains("/queue", handler.Requests.Single().RequestUri!.ToString());
        Assert.Equal(HttpMethod.Post, handler.Requests.Single().Method);
        using var body = JsonDocument.Parse(handler.RequestBodies.Single());
        Assert.Equal(
            ["prompt-a", "prompt-b"],
            body.RootElement.GetProperty("delete").EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray());
    }

    [Fact]
    public async Task FileInteractionProjectStore_SavesAndLoadsProject()
    {
        var path = Path.Combine(Path.GetTempPath(), $"draft-agent-test-{Guid.NewGuid():N}.json");
        var store = new FileInteractionProjectStore(path);
        var project = new SceneInteractionBuilder().Build("陈默说：我来晚了。");

        await store.SaveAsync(project);
        var loaded = await store.LoadLastAsync();

        Assert.NotNull(loaded);
        Assert.Equal(project.ProjectId, loaded.ProjectId);
    }

    [Fact]
    public async Task FileInteractionProjectStore_WrapsInvalidJsonOnLoad()
    {
        var path = Path.Combine(Path.GetTempPath(), $"draft-agent-bad-json-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, "{bad json");
        var store = new FileInteractionProjectStore(path);

        var exception = await Assert.ThrowsAsync<ExternalIntegrationException>(() => store.LoadLastAsync());

        Assert.Equal("FileStore", exception.Integration);
        Assert.Equal("load", exception.Operation);
    }

    private static StringContent JsonContent<T>(T value)
    {
        return new StringContent(JsonSerializer.Serialize(value, DraftJson.Options), Encoding.UTF8, "application/json");
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return _handler(request);
        }
    }
}

