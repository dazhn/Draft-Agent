using System.Text.Json;
using DraftAgent.Core;

namespace DraftAgent.Core.Tests;

public sealed class WorkflowStepTests
{
    [Fact]
    public void InteractionProject_ScriptDraftsSerializeAndOldJsonDefaultsToEmpty()
    {
        var project = EmptyProject() with
        {
            ScriptDrafts =
            [
                new ProjectScriptDraft("原文", "原始正文", false),
                new ProjectScriptDraft("改编1", "优化后的正文", true)
            ],
            SelectedScriptDraftIndex = 1
        };

        var json = JsonSerializer.Serialize(project, DraftJson.Options);
        var roundTrip = JsonSerializer.Deserialize<InteractionProject>(json, DraftJson.Options)!;
        var oldJson = """
        {
          "projectId": "old_project",
          "title": "Old Project",
          "script": { "rawText": "", "cleanText": "", "segments": [] },
          "characters": [],
          "scenes": [],
          "generationTasks": [],
          "issues": []
        }
        """;
        var oldProject = JsonSerializer.Deserialize<InteractionProject>(oldJson, DraftJson.Options)!;

        Assert.Equal(["原文", "改编1"], roundTrip.ScriptDrafts.Select(draft => draft.Title));
        Assert.Equal("优化后的正文", roundTrip.ScriptDrafts[1].Text);
        Assert.True(roundTrip.ScriptDrafts[1].CanClose);
        Assert.Equal(1, roundTrip.SelectedScriptDraftIndex);
        Assert.Empty(oldProject.ScriptDrafts);
        Assert.Equal(0, oldProject.SelectedScriptDraftIndex);
    }

    [Fact]
    public void ScriptInputService_PreparesScriptAndLogsStep()
    {
        var logger = new InMemoryWorkflowLogger();
        var service = new ScriptInputService(new DraftAnalyzer(), logger);

        var script = service.Prepare("陈默说：我来晚了。");

        Assert.Single(script.Segments);
        Assert.Contains(logger.Entries, entry => entry.Step == WorkflowSteps.ScriptInput && entry.Operation == "prepare_started");
        Assert.Contains(logger.Entries, entry => entry.Step == WorkflowSteps.ScriptInput && entry.Operation == "prepare_completed");
    }

    [Fact]
    public void LlmPromptService_CreatesRequestAndLogsStep()
    {
        var logger = new InMemoryWorkflowLogger();
        var script = new DraftAnalyzer().Preprocess("陈默说：我来晚了。");
        var service = new LlmPromptService(logger);

        var request = service.CreateRequest(script);

        Assert.Equal(DraftWorkspaceService.SceneInteractionSchemaName, request.JsonSchemaName);
        Assert.Contains("文案结构化分析器", request.Instructions);
        Assert.Contains("只输出 JSON", request.Instructions);
        Assert.Contains("type：dialogue、action、thought、effects、narration", request.Instructions);
        Assert.Contains("参考 JSON", request.Instructions);
        Assert.Contains(logger.Entries, entry => entry.Step == WorkflowSteps.PromptBuild && entry.Operation == "request_started");
        Assert.Contains(logger.Entries, entry => entry.Step == WorkflowSteps.PromptBuild && entry.Operation == "request_completed");
    }

    [Fact]
    public async Task LlmAnalysisStep_CallsClientAndLogsStep()
    {
        var logger = new InMemoryWorkflowLogger();
        var client = new CapturingLlmClient(new SceneInteractionBuilder().Build("陈默说：我来晚了。"));
        var service = new LlmAnalysisStep(client, logger);
        var request = new LlmAnalysisRequest("陈默说：我来晚了。", DraftWorkspaceService.SceneInteractionSchemaName, "rules");

        var response = await service.AnalyzeAsync(request);

        Assert.Equal(request, client.LastRequest);
        Assert.NotEmpty(response.RawJson);
        Assert.Contains(logger.Entries, entry => entry.Step == WorkflowSteps.LlmAnalysis && entry.Operation == "call_started");
        Assert.Contains(logger.Entries, entry => entry.Step == WorkflowSteps.LlmAnalysis && entry.Operation == "call_completed");
    }

    [Fact]
    public void InteractionProjectNormalizer_ParsesRawJsonNormalizesNarrationAndLogsStep()
    {
        var logger = new InMemoryWorkflowLogger();
        var normalizer = new InteractionProjectNormalizer(logger);
        var project = new SceneInteractionBuilder().Build("旁白：雨声盖住了两个人沉默的呼吸。");
        var rawJson = JsonSerializer.Serialize(project, DraftJson.Options);

        var response = normalizer.Normalize(new LlmAnalysisResponse(rawJson, EmptyProject()));
        var narration = response.Project.Scenes.SelectMany(scene => scene.Interactions).Single();

        Assert.NotSame(project, response.Project);
        Assert.Null(narration.SubjectCharacterId);
        Assert.Equal("压抑", narration.Emotion.Emotion);
        Assert.Equal("压抑", narration.Narration!.Voice.Emotion);
        Assert.Contains(response.Project.GenerationTasks.Where(task => task.Type == GenerationTaskType.Tts), task => task.Parameters["emotion"] == "Melancholic");
        Assert.Contains(logger.Entries, entry => entry.Step == WorkflowSteps.JsonNormalize && entry.Operation == "normalize_started");
        Assert.Contains(logger.Entries, entry => entry.Step == WorkflowSteps.JsonNormalize && entry.Operation == "normalize_completed");
    }

    [Fact]
    public void InteractionProjectNormalizer_DropsInvalidCharactersAndDowngradesInvalidDialogue()
    {
        var logger = new InMemoryWorkflowLogger();
        var normalizer = new InteractionProjectNormalizer(logger);
        var project = EmptyProject() with
        {
            Characters =
            [
                new Character("char_chen", "陈默", "角色", string.Empty, string.Empty, "Calm", new AssetBinding()),
                new Character("char_bad", "喘着气", "角色", string.Empty, string.Empty, "Calm", new AssetBinding())
            ],
            Scenes =
            [
                new InteractionScene(
                    "scene_001",
                    1,
                    "雨夜街口",
                    "雨夜街口",
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    new SceneAssetBinding(),
                    [
                        new CharacterInteraction(
                            "int_001",
                            1,
                            InteractionType.Dialogue,
                            "scene_001",
                            "char_bad",
                            null,
                            new EmotionSpec("Calm", 0.6, "Calm", "Calm", "Calm", "喘着气说"),
                            new ActionSpec("喘着气说", ["喘着气说"], "稳定中景", 3),
                            new DialogueLine("dia_001", "char_bad", null, "对不起，我来晚了。", new VoiceRenderSettings(null, "Calm", 0.6, 1, 0.6)),
                            null,
                            null,
                            new InteractionBinding(null, null, null, null, null),
                            [],
                            string.Empty)
                    ])
            ]
        };
        var rawJson = JsonSerializer.Serialize(project, DraftJson.Options);

        var response = normalizer.Normalize(new LlmAnalysisResponse(rawJson, project));
        var interaction = response.Project.Scenes.Single().Interactions.Single();

        Assert.Contains(response.Project.Characters, character => character.Name == "陈默");
        Assert.DoesNotContain(response.Project.Characters, character => character.Name == "喘着气");
        Assert.Equal(InteractionType.Narration, interaction.Type);
        Assert.Null(interaction.SubjectCharacterId);
        Assert.Null(interaction.Dialogue);
        Assert.Equal("对不起，我来晚了。", interaction.Narration?.Text);
    }

    [Fact]
    public void LlmSceneFlowJsonConverter_IgnoresModelSuppliedTasksAndBindings()
    {
        var json = """
        {
          "projectId": "model_supplied_project",
          "characters": [
            {
              "id": "char_bad",
              "name": "陈默",
              "assetBinding": { "portrait": "model_image.png" }
            }
          ],
          "generationTasks": [
            {
              "id": "video_1",
              "type": "Video",
              "targetId": "int_001",
              "displayName": "AI image",
              "provider": "comfyui",
              "status": "Completed",
              "parameters": { "prompt": "make an image" },
              "resultAssetId": "model_image.png"
            }
          ],
          "scenes": [
            {
              "id": "scene_001",
              "description": "雨夜街口，雨声压抑。",
              "assetBinding": { "backgroundImage": "model_scene.png" },
              "items": [
                {
                  "id": "int_001",
                  "type": "dialogue",
                  "role": "陈默",
                  "text": "我来晚了。",
                  "binding": { "sceneAssetId": "model_scene.png" },
                  "tasks": [
                    {
                      "id": "video_nested",
                      "type": "Video",
                      "targetId": "int_001"
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

        var converted = LlmSceneFlowJsonConverter.ToInteractionProjectJson(json, "陈默说：我来晚了。", new SceneInteractionBuilder());
        var project = JsonSerializer.Deserialize<InteractionProject>(converted, DraftJson.Options)!;

        var scene = Assert.Single(project.Scenes);
        var interaction = Assert.Single(scene.Interactions);
        var task = Assert.Single(project.GenerationTasks);
        Assert.Null(scene.AssetBinding.BackgroundImage);
        Assert.Equal("雨夜街口，雨声压抑。", scene.Name);
        Assert.Equal(string.Empty, scene.Description);
        Assert.Null(interaction.Binding.SceneVisualBinding);
        Assert.Null(interaction.Binding.SubjectCharacterVisualBinding);
        Assert.Equal(GenerationTaskType.Tts, task.Type);
        Assert.Equal(interaction.Id, task.TargetId);
        Assert.DoesNotContain(project.GenerationTasks, candidate => candidate.Type != GenerationTaskType.Tts);
        Assert.DoesNotContain("model_image.png", converted);
        Assert.DoesNotContain("model_scene.png", converted);
        Assert.DoesNotContain("video_1", converted);
        Assert.DoesNotContain("video_nested", converted);
    }

    [Fact]
    public void LlmSceneFlowJsonConverter_MapsUnifiedItemsAndKeepsNonDialogueAsNarration()
    {
        var json = """
        {
          "scenes": [
            {
              "description": "雨夜街口，雨势加重，气氛压抑。",
              "items": [
                {
                  "type": "effects",
                  "role": "",
                  "text": "雨声突然变大。",
                  "emotion": "Calm",
                  "intensity": 0.6
                },
                {
                  "type": "action",
                  "role": "林安",
                  "text": "林安攥紧手机。",
                  "emotion": "Angry",
                  "intensity": 0.7
                },
                {
                  "type": "thought",
                  "role": "林安",
                  "text": "她终于明白自己被利用了。",
                  "emotion": "Sad",
                  "intensity": 0.75
                },
                {
                  "type": "dialogue",
                  "role": "林安",
                  "text": "你为什么骗我？",
                  "emotion": "Angry",
                  "intensity": 0.8
                },
                {
                  "type": "narration",
                  "role": "",
                  "text": "三年前的秘密被重新揭开。",
                  "emotion": "Melancholic",
                  "intensity": 0.65
                }
              ]
            }
          ]
        }
        """;

        var converted = LlmSceneFlowJsonConverter.ToInteractionProjectJson(json, "林安攥紧手机。", new SceneInteractionBuilder());
        var project = JsonSerializer.Deserialize<InteractionProject>(converted, DraftJson.Options)!;
        var interactions = project.Scenes.Single().Interactions;

        Assert.Equal(5, interactions.Count);
        Assert.Equal("雨夜街口，雨势加重，气氛压抑。", project.Scenes.Single().Name);
        Assert.Equal(string.Empty, project.Scenes.Single().Description);
        Assert.Equal(InteractionType.Narration, interactions[0].Type);
        Assert.Null(interactions[0].SubjectCharacterId);
        Assert.Equal("雨声突然变大。", interactions[0].Narration?.Text);
        Assert.Equal(InteractionType.Narration, interactions[1].Type);
        Assert.Equal("林安攥紧手机。", interactions[1].Narration?.Text);
        Assert.Equal(InteractionType.Narration, interactions[2].Type);
        Assert.Equal("她终于明白自己被利用了。", interactions[2].Narration?.Text);
        Assert.Equal("thought", interactions[2].SourceItemType);
        Assert.Equal("林安", interactions[2].SourceRole);
        Assert.NotNull(interactions[2].SourceRoleCharacterId);
        Assert.Equal(InteractionType.Dialogue, interactions[3].Type);
        Assert.Equal("你为什么骗我？", interactions[3].Dialogue?.Text);
        Assert.Equal(InteractionType.Narration, interactions[4].Type);
        Assert.Equal("三年前的秘密被重新揭开。", interactions[4].Narration?.Text);
        Assert.Contains(project.Characters, character => character.Name == "林安");
        Assert.Contains(project.GenerationTasks, task => task.TargetId == interactions[2].Id && task.Parameters["text"] == "她终于明白自己被利用了。");
        Assert.Contains(project.GenerationTasks, task => task.TargetId == interactions[2].Id && task.DisplayName == "TTS narration");
        Assert.Contains(project.GenerationTasks, task => task.TargetId == interactions[3].Id && task.DisplayName.Contains("dialogue", StringComparison.Ordinal));
    }

    [Fact]
    public void InteractionProjectNormalizer_IgnoresModelSuppliedTasksAndBindings()
    {
        var logger = new InMemoryWorkflowLogger();
        var normalizer = new InteractionProjectNormalizer(logger);
        var project = EmptyProject() with
        {
            Characters =
            [
                new Character(
                    "char_chen",
                    "陈默",
                    "AI detected character",
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    new AssetBinding(Portrait: "model_portrait.png"))
            ],
            GenerationTasks =
            [
                new GenerationTask(
                    "video_model_supplied",
                    GenerationTaskType.Video,
                    "int_001",
                    "AI image",
                    "comfyui",
                    GenerationTaskStatus.Completed,
                    new Dictionary<string, string> { ["prompt"] = "make an image" },
                    "model_image.png"),
                new GenerationTask(
                    "orphan_model_supplied",
                    GenerationTaskType.Tts,
                    "missing_target",
                    "Orphan model supplied TTS",
                    "comfyui",
                    GenerationTaskStatus.Completed,
                    new Dictionary<string, string> { ["text"] = "orphan model text", ["voiceReferenceId"] = "orphan_voice.wav" },
                    "orphan_audio.wav")
            ],
            Scenes =
            [
                new InteractionScene(
                    "scene_001",
                    1,
                    "雨夜街口",
                    "雨夜街口",
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    "雨声",
                    new SceneAssetBinding("model_scene.png"),
                    [
                        new CharacterInteraction(
                            "int_001",
                            1,
                            InteractionType.Dialogue,
                            "scene_001",
                            "char_chen",
                            null,
                            new EmotionSpec("Calm", 0.6, "Calm", "Calm", "Calm", "道歉"),
                            new ActionSpec("道歉", ["道歉"], "camera close-up", 3),
                            new DialogueLine("dia_001", "char_chen", null, "我来晚了。", new VoiceRenderSettings(null, "Calm", 0.6, 1, 0.6)),
                            new InnerMonologue(
                                "inner_model_supplied",
                                "char_chen",
                                "model supplied inner monologue",
                                new VoiceRenderSettings("inner_voice.wav", "Angry", 1, 1, 0.6)),
                            null,
                            new InteractionBinding(
                                SubjectVoiceReferenceId: "model_voice.wav",
                                NarratorVoiceReferenceId: "model_narrator.wav",
                                SceneVisualBinding: VisualAssetBinding.FromPsd("model_scene_source.psd", "model_scene_rendered.png"),
                                SubjectCharacterVisualBinding: VisualAssetBinding.FromPsd("model_source.psd", "model_rendered.png"),
                                TargetCharacterVisualBinding: VisualAssetBinding.FromImage("model_target.png")),
                            [
                                new GenerationTask(
                                    "video_nested",
                                    GenerationTaskType.Video,
                                    "int_001",
                                    "Nested video",
                                    "comfyui",
                                    GenerationTaskStatus.Completed,
                                    new Dictionary<string, string> { ["prompt"] = "nested image" },
                                    "nested_image.png"),
                                new GenerationTask(
                                    "tts_model_supplied",
                                    GenerationTaskType.Tts,
                                    "int_001",
                                    "Model supplied TTS",
                                    "comfyui",
                                    GenerationTaskStatus.Completed,
                                    new Dictionary<string, string> { ["text"] = "model controlled text", ["voiceReferenceId"] = "model_voice.wav" },
                                    "model_audio.wav")
                            ],
                            string.Empty)
                    ])
            ]
        };
        var rawJson = JsonSerializer.Serialize(project, DraftJson.Options);

        var response = normalizer.Normalize(new LlmAnalysisResponse(rawJson, project));
        var scene = Assert.Single(response.Project.Scenes);
        var interaction = Assert.Single(scene.Interactions);
        var task = Assert.Single(response.Project.GenerationTasks);

        Assert.Null(response.Project.Characters.Single().AssetBinding.Portrait);
        Assert.Null(scene.AssetBinding.BackgroundImage);
        Assert.Null(interaction.Binding.SceneVisualBinding);
        Assert.Null(interaction.Binding.SubjectCharacterVisualBinding);
        Assert.Null(interaction.Binding.TargetCharacterVisualBinding);
        Assert.Null(interaction.Binding.SubjectVoiceReferenceId);
        Assert.Null(interaction.Binding.NarratorVoiceReferenceId);
        Assert.Null(interaction.Binding.SceneVisualBinding);
        Assert.Null(interaction.Binding.SubjectCharacterVisualBinding);
        Assert.Null(interaction.Binding.TargetCharacterVisualBinding);
        Assert.Null(interaction.InnerMonologue);
        Assert.Equal("no camera language", interaction.Action.CameraAction);
        Assert.Equal(GenerationTaskType.Tts, task.Type);
        Assert.Equal("tts_int_001", task.Id);
        Assert.Equal("我来晚了。", task.Parameters["text"]);
        Assert.Equal(string.Empty, task.Parameters["voiceReferenceId"]);
        Assert.DoesNotContain(response.Project.GenerationTasks, candidate => candidate.Type != GenerationTaskType.Tts);
        Assert.DoesNotContain("model_scene.png", response.RawJson);
        Assert.DoesNotContain("model_portrait.png", response.RawJson);
        Assert.DoesNotContain("model_voice.wav", response.RawJson);
        Assert.DoesNotContain("model_audio.wav", response.RawJson);
        Assert.DoesNotContain("orphan_audio.wav", response.RawJson);
        Assert.DoesNotContain("orphan_model_supplied", response.RawJson);
        Assert.DoesNotContain("orphan_voice.wav", response.RawJson);
        Assert.DoesNotContain("nested_image.png", response.RawJson);
        Assert.DoesNotContain("inner_model_supplied", response.RawJson);
        Assert.DoesNotContain("model supplied inner monologue", response.RawJson);
        Assert.DoesNotContain("inner_voice.wav", response.RawJson);
        Assert.DoesNotContain("camera close-up", response.RawJson);
        Assert.DoesNotContain("video_model_supplied", response.RawJson);
        Assert.DoesNotContain("video_nested", response.RawJson);
        Assert.DoesNotContain("tts_model_supplied", response.RawJson);
    }

    [Fact]
    public void LlmSceneFlowJsonConverter_ReadsCurrentSchemaAndIgnoresUnsupportedAliasFields()
    {
        var json = """
        {
          "scenes": [
            {
              "sceneName": "旧场景名",
              "description": "雨夜街口，气氛压抑。",
              "sceneEffect": "旧氛围",
              "location": "旧地点",
              "timeOfDay": "旧时间",
              "weather": "旧天气",
              "items": [
                {
                  "type": "action",
                  "speaker": "陈默",
                  "name": "林安",
                  "line": "旧台词",
                  "dialogue": { "text": "嵌套台词" },
                  "action": "旧动作",
                  "intensity": 0.9
                }
              ]
            }
          ]
        }
        """;

        var converted = LlmSceneFlowJsonConverter.ToInteractionProjectJson(json, "陈默说：我来晚了。", new SceneInteractionBuilder());
        var project = JsonSerializer.Deserialize<InteractionProject>(converted, DraftJson.Options)!;
        var scene = Assert.Single(project.Scenes);
        var interaction = Assert.Single(scene.Interactions);

        Assert.Equal("雨夜街口，气氛压抑。", scene.Name);
        Assert.Equal(string.Empty, scene.Description);
        Assert.Equal(scene.Name, scene.Location);
        Assert.Equal(string.Empty, scene.TimeOfDay);
        Assert.Equal(string.Empty, scene.Weather);
        Assert.Equal(InteractionType.Narration, interaction.Type);
        Assert.Null(interaction.SubjectCharacterId);
        Assert.Null(interaction.Dialogue);
        Assert.Equal(string.Empty, interaction.Narration?.Text);
        Assert.Equal(string.Empty, interaction.Action.MainAction);
        Assert.Equal(0.9, interaction.Emotion.Intensity);
        Assert.DoesNotContain(project.Characters, character => character.Name is "陈默" or "林安");
        Assert.DoesNotContain("旧场景名", converted);
        Assert.DoesNotContain("旧氛围", converted);
        Assert.DoesNotContain("旧台词", converted);
        Assert.DoesNotContain("嵌套台词", converted);
        Assert.DoesNotContain("旧动作", converted);
    }

    private sealed class CapturingLlmClient : ILlmAnalysisClient
    {
        private readonly InteractionProject _project;

        public CapturingLlmClient(InteractionProject project)
        {
            _project = project;
        }

        public LlmAnalysisRequest? LastRequest { get; private set; }

        public Task<LlmAnalysisResponse> AnalyzeAsync(LlmAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new LlmAnalysisResponse(JsonSerializer.Serialize(_project, DraftJson.Options), _project));
        }
    }

    private static InteractionProject EmptyProject()
    {
        return new InteractionProject(
            "project_empty",
            "Empty",
            new ScriptDocument(string.Empty, string.Empty, []),
            [],
            [],
            [],
            []);
    }

}


