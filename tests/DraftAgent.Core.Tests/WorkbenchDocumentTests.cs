using DraftAgent.Core;

using System.Text.Json;

namespace DraftAgent.Core.Tests;

public sealed class WorkbenchDocumentTests
{
    [Fact]
    public void FromEditsBuildsDocumentRowsByIdAndRoundTripsSceneEdits()
    {
        var sceneEdits = new[]
        {
            new WorkbenchSceneEdit(
                "scene_001",
                1,
                "Scene 1",
                "Rain",
                [
                    new WorkbenchInteractionEdit(
                        "int_001",
                        1,
                        "Chen",
                        "Calm 60%",
                        "Hello",
                        VisualAssetBinding.FromImage("chen.png"),
                        VoiceAssetBinding.FromProjectReference("chen.wav", "Chen sample", null, null),
                        new Dictionary<string, double> { ["Calm"] = 0.6 },
                        0.8)
                ],
                VisualAssetBinding.FromImage("scene.png"))
        };
        var bindings = new[]
        {
            new WorkbenchCharacterBindingEdit(
                "char_chen",
                VisualAssetBinding.FromImage("portrait.png"),
                VoiceAssetBinding.FromProjectReference("voice.wav", null, null, null))
        };

        var document = WorkbenchDraftBuilder.FromEdits(sceneEdits, bindings);
        var roundTrip = document.ToSceneEdits();

        Assert.Equal("int_001", Assert.Single(document.RowsById).Key);
        Assert.Equal("Chen", document.RowsById["int_001"].SubjectName);
        Assert.Equal("char_chen", Assert.Single(document.CharacterBindings).CharacterId);
        var roundTripScene = Assert.Single(roundTrip);
        var roundTripRow = Assert.Single(roundTripScene.Rows);
        Assert.Equal(sceneEdits[0].SceneId, roundTripScene.SceneId);
        Assert.Equal(sceneEdits[0].Name, roundTripScene.Name);
        Assert.Equal(sceneEdits[0].VisualBinding?.EffectiveAssetId, roundTripScene.VisualBinding?.EffectiveAssetId);
        Assert.Equal(sceneEdits[0].Rows[0].InteractionId, roundTripRow.InteractionId);
        Assert.Equal(sceneEdits[0].Rows[0].VoiceBinding?.EffectiveReferenceText, roundTripRow.VoiceBinding?.EffectiveReferenceText);
    }


    [Fact]
    public void FromProjectKeepsNarrationRowsCanonical()
    {
        var project = new SceneInteractionBuilder().Build("旁白：雨声盖住了两个人沉默的呼吸。");

        var document = WorkbenchDraftBuilder.FromProject(project);

        var row = Assert.Single(Assert.Single(document.Scenes).Rows);
        Assert.Equal(WorkbenchRowKind.Narration, row.Kind);
        Assert.Equal("旁白", row.SubjectName);
        Assert.DoesNotContain(
            document.CharacterBindings,
            binding => binding.CharacterId.Contains("narr", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FromSceneFlowProjectDisplaysActionAndThoughtRolesWhileKeepingNarrationRows()
    {
        const string json = """
        {
          "scenes": [
            {
              "description": "街道，寒风凛冽。",
              "items": [
                {
                  "type": "effects",
                  "role": "",
                  "text": "寒风吹过街口。",
                  "emotion": "平静",
                  "intensity": 0.6
                },
                {
                  "type": "action",
                  "role": "旁白",
                  "text": "旁白转身看向远处。",
                  "emotion": "平静",
                  "intensity": 0.6
                },
                {
                  "type": "thought",
                  "role": "旁白",
                  "text": "旁白觉得事情不对。",
                  "emotion": "惊讶",
                  "intensity": 0.6
                },
                {
                  "type": "dialogue",
                  "role": "洛云曦",
                  "text": "你来了。",
                  "emotion": "开心",
                  "intensity": 0.7
                }
              ]
            }
          ]
        }
        """;
        var converted = LlmSceneFlowJsonConverter.ToInteractionProjectJson(json, string.Empty, new SceneInteractionBuilder());
        var project = JsonSerializer.Deserialize<InteractionProject>(converted, DraftJson.Options)!;

        var document = WorkbenchDraftBuilder.FromProject(project);
        var rows = Assert.Single(document.Scenes).Rows;

        Assert.Equal(4, rows.Count);
        Assert.Equal(WorkbenchRowKind.Narration, rows[0].Kind);
        Assert.Equal("环境场效", rows[0].SubjectName);
        Assert.Null(rows[0].SubjectCharacterId);
        Assert.Equal("effects", rows[0].SourceItemType);
        Assert.Equal(WorkbenchRowKind.Narration, rows[1].Kind);
        Assert.Equal("旁白", rows[1].SubjectName);
        Assert.Null(rows[1].SubjectCharacterId);
        Assert.Equal("action", rows[1].SourceItemType);
        Assert.Equal("旁白", rows[1].SourceRole);
        Assert.Equal(WorkbenchRowKind.Narration, rows[2].Kind);
        Assert.Equal("旁白", rows[2].SubjectName);
        Assert.Null(rows[2].SubjectCharacterId);
        Assert.Equal("thought", rows[2].SourceItemType);
        Assert.Equal("旁白", rows[2].SourceRole);
        Assert.Equal(WorkbenchRowKind.Dialogue, rows[3].Kind);
        Assert.Equal("洛云曦", rows[3].SubjectName);
        Assert.NotNull(rows[3].SubjectCharacterId);
        Assert.Equal("dialogue", rows[3].SourceItemType);
    }

    [Fact]
    public void FromProjectBuildsEditableDocumentWithTaskLinksAndCharacterBindings()
    {
        var project = new SceneInteractionBuilder().Build("陈默说：我来晚了。");
        var scene = project.Scenes.Single();
        var interaction = scene.Interactions.Single();
        var character = project.Characters.Single(character => character.Id == interaction.SubjectCharacterId);
        var ttsTask = interaction.Tasks.Single(task => task.Type == GenerationTaskType.Tts);
        var visualBinding = VisualAssetBinding.FromPsd("asset_psd_chen", "asset_png_chen");
        var voiceBinding = VoiceAssetBinding.FromProjectReference(
            "asset_voice_chen",
            "我来晚了。",
            "asset_voice_source",
            @"C:\ComfyUI\models\voices\chen.wav");
        var updatedTask = ttsTask with
        {
            Parameters = new Dictionary<string, string>(ttsTask.Parameters, StringComparer.Ordinal)
            {
                ["voiceReferenceId"] = voiceBinding.VoiceReferenceId!,
                ["referenceText"] = voiceBinding.ReferenceText!,
                ["voiceSourceAssetId"] = voiceBinding.SourceAssetId!,
                ["voiceSyncedPath"] = voiceBinding.SyncedVoicePath!
            }
        };
        project = project with
        {
            Characters =
            [
                character with
                {
                    AssetBinding = character.AssetBinding with
                    {
                        Portrait = visualBinding.EffectiveAssetId,
                        PortraitVisualBinding = visualBinding,
                        Voice = voiceBinding.VoiceReferenceId,
                        VoiceBinding = voiceBinding
                    }
                }
            ],
            Scenes =
            [
                scene with
                {
                    Interactions =
                    [
                        interaction with
                        {
                            Binding = interaction.Binding with
                            {
                                SubjectVoiceReferenceId = voiceBinding.VoiceReferenceId,
                                SubjectCharacterVisualBinding = visualBinding
                            },
                            Tasks = [updatedTask]
                        }
                    ]
                }
            ],
            GenerationTasks = [updatedTask]
        };

        var document = WorkbenchDraftBuilder.FromProject(project);

        var documentScene = Assert.Single(document.Scenes);
        var row = Assert.Single(documentScene.Rows);
        var binding = Assert.Single(document.CharacterBindings);
        Assert.Equal(interaction.Id, row.InteractionId);
        Assert.Equal(updatedTask.Id, row.TaskLinks.TtsTaskId);
        Assert.Equal(visualBinding, row.CharacterVisualBinding);
        Assert.Equal(voiceBinding, row.VoiceBinding);
        Assert.Equal(character.Id, binding.CharacterId);
        Assert.Equal(visualBinding, binding.VisualBinding);
        Assert.Equal(voiceBinding, binding.VoiceBinding);
        Assert.Same(row, document.RowsById[interaction.Id]);
    }





    [Fact]
    public void FromProjectRestoresVoiceProjectSourceAndSyncedPathFromTtsTaskParameters()
    {
        var project = new SceneInteractionBuilder().Build("Chen: Hello");
        var interaction = project.Scenes.Single().Interactions.Single();
        var ttsTask = interaction.Tasks.Single(task => task.Type == GenerationTaskType.Tts);
        var updatedTtsTask = ttsTask with
        {
            Parameters = new Dictionary<string, string>(ttsTask.Parameters, StringComparer.Ordinal)
            {
                ["voiceReferenceId"] = "voices/chen.wav",
                ["referenceText"] = "Chen project reference text",
                ["voiceSourceAssetId"] = "assets/audio/voice-references/chen.wav",
                ["voiceSyncedPath"] = @"C:\ComfyUI\models\voices\chen.wav"
            }
        };
        var updatedScene = project.Scenes.Single() with
        {
            Interactions = [interaction with { Tasks = interaction.Tasks.Select(task => task.Id == updatedTtsTask.Id ? updatedTtsTask : task).ToArray() }]
        };
        project = project with
        {
            Scenes = [updatedScene],
            GenerationTasks = project.GenerationTasks.Select(task => task.Id == updatedTtsTask.Id ? updatedTtsTask : task).ToArray()
        };

        var document = WorkbenchDraftBuilder.FromProject(project);

        var row = Assert.Single(Assert.Single(document.Scenes).Rows);
        Assert.Equal("voices/chen.wav", row.VoiceBinding!.VoiceReferenceId);
        Assert.Equal("Chen project reference text", row.VoiceBinding.ReferenceText);
        Assert.Equal("assets/audio/voice-references/chen.wav", row.VoiceBinding.SourceAssetId);
        Assert.Equal(@"C:\ComfyUI\models\voices\chen.wav", row.VoiceBinding.SyncedVoicePath);
    }

    [Fact]
    public void FromProjectRestoresTtsTaskLinksFromRootGenerationTasks()
    {
        var project = new SceneInteractionBuilder().Build("Chen: Hello");
        var scene = project.Scenes.Single();
        var interaction = scene.Interactions.Single();
        var rootOnlyTtsTask = interaction.Tasks.Single(task => task.Type == GenerationTaskType.Tts) with
        {
            Status = GenerationTaskStatus.Completed,
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["voiceReferenceId"] = "voices/chen.wav",
                ["referenceText"] = "Chen root reference text"
            },
            ResultAssetId = "asset_tts_root_imported"
        };
        project = project with
        {
            Scenes =
            [
                scene with
                {
                    Interactions =
                    [
                        interaction with
                        {
                            Tasks = []
                        }
                    ]
                }
            ],
            GenerationTasks = [rootOnlyTtsTask]
        };

        var document = WorkbenchDraftBuilder.FromProject(project);

        var row = Assert.Single(Assert.Single(document.Scenes).Rows);
        Assert.Equal(rootOnlyTtsTask.Id, row.TaskLinks.TtsTaskId);
        Assert.Equal("voices/chen.wav", row.VoiceBinding!.VoiceReferenceId);
        Assert.Equal("Chen root reference text", row.VoiceBinding.ReferenceText);
    }

    [Fact]
    public void FromProjectDoesNotRestoreOldVoiceSourceWhenInteractionVoiceReferenceChanges()
    {
        var project = new SceneInteractionBuilder().Build("Chen: Hello");
        var interaction = project.Scenes.Single().Interactions.Single();
        var ttsTask = interaction.Tasks.Single(task => task.Type == GenerationTaskType.Tts);
        var oldTtsTask = ttsTask with
        {
            Parameters = new Dictionary<string, string>(ttsTask.Parameters, StringComparer.Ordinal)
            {
                ["voiceReferenceId"] = "voices/old.wav",
                ["referenceText"] = "old reference",
                ["voiceSourceAssetId"] = "assets/audio/voice-references/old.wav",
                ["voiceSyncedPath"] = @"C:\ComfyUI\models\voices\old.wav"
            }
        };
        var changedInteraction = interaction with
        {
            Binding = interaction.Binding with { SubjectVoiceReferenceId = "voices/new.wav" },
            Dialogue = interaction.Dialogue! with
            {
                Voice = interaction.Dialogue.Voice with { ReferenceText = "new reference" }
            },
            Tasks = interaction.Tasks.Select(task => task.Id == oldTtsTask.Id ? oldTtsTask : task).ToArray()
        };
        var changedScene = project.Scenes.Single() with { Interactions = [changedInteraction] };
        project = project with
        {
            Scenes = [changedScene],
            GenerationTasks = project.GenerationTasks.Select(task => task.Id == oldTtsTask.Id ? oldTtsTask : task).ToArray()
        };

        var document = WorkbenchDraftBuilder.FromProject(project);

        var row = Assert.Single(Assert.Single(document.Scenes).Rows);
        Assert.Equal("voices/new.wav", row.VoiceBinding!.VoiceReferenceId);
        Assert.Equal("new reference", row.VoiceBinding.ReferenceText);
        Assert.Null(row.VoiceBinding.SourceAssetId);
        Assert.Null(row.VoiceBinding.SyncedVoicePath);
    }

    [Fact]
    public void FromProjectRestoresCharacterDefaultVoiceBinding()
    {
        var voiceBinding = VoiceAssetBinding.FromProjectReference(
            "linan.wav",
            "Linan reference",
            "assets/audio/voice-references/linan.wav",
            @"C:\ComfyUI\models\voices\linan.wav");
        var project = new InteractionProject(
            "project",
            "project",
            new DraftAnalyzer().Preprocess(string.Empty),
            [
                new Character(
                    "char_linan",
                    "Linan",
                    "角色",
                    "appearance",
                    "voice",
                    "Calm",
                    new AssetBinding(Voice: "fallback.wav", VoiceBinding: voiceBinding))
            ],
            [],
            [],
            []);

        var document = WorkbenchDraftBuilder.FromProject(project);

        var binding = Assert.Single(document.CharacterBindings);
        Assert.Equal("char_linan", binding.CharacterId);
        Assert.Equal("linan.wav", binding.VoiceBinding?.VoiceReferenceId);
        Assert.Equal("Linan reference", binding.VoiceBinding?.ReferenceText);
        Assert.Equal("assets/audio/voice-references/linan.wav", binding.VoiceBinding?.SourceAssetId);
        Assert.Equal(@"C:\ComfyUI\models\voices\linan.wav", binding.VoiceBinding?.SyncedVoicePath);
    }



    [Fact]
    public void FromProjectRestoresStructuredPsdVisualBindingsFromProjectModel()
    {
        var project = new SceneInteractionBuilder().Build("Chen: Hello");
        var scene = project.Scenes.Single();
        var interaction = scene.Interactions.Single();
        var character = project.Characters.Single(character => character.Id == interaction.SubjectCharacterId);
        var sceneBinding = VisualAssetBinding.FromPsd(
            "asset_psd_scene",
            "asset_png_scene",
            new PsdRenderSelection(
                [1],
                ["Scene"],
                null,
                "Calm",
                "Wide",
                VisualExportBounds.CanvasBounds,
                "scene-fp",
                "scene-cache"));
        var rowBinding = VisualAssetBinding.FromPsd(
            "asset_psd_character",
            "asset_png_character",
            new PsdRenderSelection(
                [2],
                ["Smile"],
                null,
                "Happy",
                "Talk",
                VisualExportBounds.CanvasBounds,
                "row-fp",
                "row-cache"));
        var characterBinding = VisualAssetBinding.FromPsd("asset_psd_default_character");
        project = project with
        {
            Characters =
            [
                character with
                {
                    AssetBinding = character.AssetBinding with
                    {
                        Portrait = "asset_psd_default_character",
                        PortraitVisualBinding = characterBinding
                    }
                }
            ],
            Scenes =
            [
                scene with
                {
                    AssetBinding = scene.AssetBinding with
                    {
                        BackgroundImage = "asset_png_scene",
                        BackgroundVisualBinding = sceneBinding
                    },
                    Interactions =
                    [
                        interaction with
                        {
                            Binding = interaction.Binding with
                            {
                                SceneVisualBinding = sceneBinding,
                                SubjectCharacterVisualBinding = rowBinding
                            }
                        }
                    ]
                }
            ]
        };

        var document = WorkbenchDraftBuilder.FromProject(project);

        var sceneDraft = Assert.Single(document.Scenes);
        var row = Assert.Single(sceneDraft.Rows);
        var defaultBinding = Assert.Single(document.CharacterBindings);
        Assert.Equal("asset_png_scene", sceneDraft.VisualBinding?.EffectiveAssetId);
        Assert.Equal(sceneBinding, sceneDraft.VisualBinding);
        Assert.Equal("asset_png_character", row.CharacterVisualBinding?.EffectiveAssetId);
        Assert.Equal(rowBinding, row.CharacterVisualBinding);
        Assert.Equal("asset_psd_default_character", defaultBinding.VisualBinding?.EffectiveAssetId);
        Assert.Equal(characterBinding, defaultBinding.VisualBinding);
    }



    [Fact]
    public void FromEditsKeepsSceneIdAndNarrationKindOnRows()
    {
        var sceneEdits = new[]
        {
            new WorkbenchSceneEdit(
                "scene_001",
                1,
                "Scene 1",
                string.Empty,
                [
                    new WorkbenchInteractionEdit(
                        "int_001",
                        1,
                        "旁白",
                        string.Empty,
                        "Narration line",
                        null,
                        VoiceAssetBinding.FromProjectReference("voices/narrator.wav", null, null, null))
                ])
        };

        var document = WorkbenchDraftBuilder.FromEdits(sceneEdits);
        var row = Assert.Single(Assert.Single(document.Scenes).Rows);

        Assert.Equal("scene_001", row.SceneId);
        Assert.Equal(WorkbenchRowKind.Narration, row.Kind);
    }

    [Fact]
    public void FromEditsSortsScenesAndRowsByOrder()
    {
        var sceneEdits = new[]
        {
            SceneEdit(
                "scene_late",
                2,
                [
                    RowEdit("late_row_second", 2),
                    RowEdit("late_row_first", 1),
                    RowEdit("late_row_same_order", 1)
                ]),
            SceneEdit("scene_early", 1, [RowEdit("early_row", 1)]),
            SceneEdit("scene_same_order", 1, [RowEdit("same_order_row", 1)])
        };

        var document = WorkbenchDraftBuilder.FromEdits(sceneEdits);

        Assert.Equal(["scene_early", "scene_same_order", "scene_late"], document.Scenes.Select(scene => scene.SceneId));
        Assert.Equal(
            ["late_row_first", "late_row_same_order", "late_row_second"],
            document.Scenes[2].Rows.Select(row => row.InteractionId));
    }

    [Fact]
    public void ToSceneEditsSortsScenesAndRowsByOrder()
    {
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_late",
                    2,
                    "Scene late",
                    string.Empty,
                    [
                        Row("late_row_second", "scene_late", 2),
                        Row("late_row_first", "scene_late", 1),
                        Row("late_row_same_order", "scene_late", 1)
                    ]),
                new WorkbenchSceneDraft("scene_early", 1, "Scene early", string.Empty, []),
                new WorkbenchSceneDraft("scene_same_order", 1, "Scene same order", string.Empty, [])
            ],
            []);

        var sceneEdits = document.ToSceneEdits();

        Assert.Equal(["scene_early", "scene_same_order", "scene_late"], sceneEdits.Select(scene => scene.SceneId));
        Assert.Equal(
            ["late_row_first", "late_row_same_order", "late_row_second"],
            sceneEdits[2].Rows.Select(row => row.InteractionId));
    }

    [Fact]
    public void RowLocationsByIdMapsRowsToSceneAndRowIndexes()
    {
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_001",
                    1,
                    "Scene 1",
                    string.Empty,
                    [
                        Row("int_001", "scene_001", 1),
                        Row("int_002", "scene_001", 2)
                    ]),
                new WorkbenchSceneDraft(
                    "scene_002",
                    2,
                    "Scene 2",
                    string.Empty,
                    [Row("int_003", "scene_002", 1)])
            ],
            []);

        var first = document.RowLocationsById;
        var second = document.RowLocationsById;

        Assert.Same(first, second);
        Assert.Equal(new WorkbenchRowLocation(0, 0), first["int_001"]);
        Assert.Equal(new WorkbenchRowLocation(0, 1), first["int_002"]);
        Assert.Equal(new WorkbenchRowLocation(1, 0), first["int_003"]);
    }

    [Fact]
    public void SceneIndexesByIdMapsScenesToCurrentIndexes()
    {
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft("scene_001", 1, "Scene 1", string.Empty, []),
                new WorkbenchSceneDraft("scene_002", 2, "Scene 2", string.Empty, []),
                new WorkbenchSceneDraft("scene_003", 3, "Scene 3", string.Empty, [])
            ],
            []);

        var first = document.SceneIndexesById;
        var second = document.SceneIndexesById;

        Assert.Same(first, second);
        Assert.Equal(0, first["scene_001"]);
        Assert.Equal(1, first["scene_002"]);
        Assert.Equal(2, first["scene_003"]);
    }

    [Fact]
    public void TryGetSceneDraftAndIndexUseSceneIndex()
    {
        var firstScene = new WorkbenchSceneDraft("scene_001", 1, "Scene 1", string.Empty, []);
        var secondScene = new WorkbenchSceneDraft("scene_002", 2, "Scene 2", string.Empty, []);
        var document = new WorkbenchDocument([firstScene, secondScene], []);

        var foundDraft = document.TryGetSceneDraft("scene_002", out var sceneDraft);
        var foundIndex = document.TryGetSceneIndex("scene_002", out var sceneIndex);

        Assert.True(foundDraft);
        Assert.True(foundIndex);
        Assert.Same(secondScene, sceneDraft);
        Assert.Equal(1, sceneIndex);
        Assert.False(document.TryGetSceneDraft("missing", out _));
        Assert.False(document.TryGetSceneIndex("missing", out _));
    }

    [Fact]
    public void BindingRowIndexMapsCharacterAndNarrationRows()
    {
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_001",
                    1,
                    "Scene 1",
                    string.Empty,
                    [
                        Row("int_001", "scene_001", 1),
                        Row("int_002", "scene_001", 2) with
                        {
                            Kind = WorkbenchRowKind.Narration,
                            SubjectName = "旁白",
                            SubjectCharacterId = null
                        }
                    ]),
                new WorkbenchSceneDraft(
                    "scene_002",
                    2,
                    "Scene 2",
                    string.Empty,
                    [Row("int_003", "scene_002", 1) with { SubjectCharacterId = "char_lin", SubjectName = "Lin" }])
            ],
            []);

        var first = document.BindingRowIndex;
        var second = document.BindingRowIndex;

        Assert.Same(first, second);
        Assert.Equal([new WorkbenchRowLocation(0, 0)], first.RowsForCharacter("char_chen", "Chen"));
        Assert.Equal([new WorkbenchRowLocation(1, 0)], first.RowsForCharacter("missing", "Lin"));
        Assert.Equal([new WorkbenchRowLocation(0, 1)], first.NarrationRows);
    }

    [Fact]
    public void BindingRowIndexCountsCharacterRowsMatchingIdAndNameOnce()
    {
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_001",
                    1,
                    "Scene 1",
                    string.Empty,
                    [Row("int_001", "scene_001", 1)])
            ],
            []);

        var rows = document.BindingRowIndex.RowsForCharacter("char_chen", "Chen");

        Assert.Equal([new WorkbenchRowLocation(0, 0)], rows);
    }

    [Fact]
    public void BindingRowIndexIncludesSourceRoleCharacterRowsOnce()
    {
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_001",
                    1,
                    "Scene 1",
                    string.Empty,
                    [
                        Row("int_001", "scene_001", 1) with
                        {
                            Kind = WorkbenchRowKind.Narration,
                            SubjectName = "Chen",
                            SubjectCharacterId = null,
                            SourceItemType = "thought",
                            SourceRole = "Chen",
                            SourceRoleCharacterId = "char_chen"
                        }
                    ])
            ],
            []);

        var rows = document.BindingRowIndex.RowsForCharacter("char_chen", "Chen");

        Assert.Equal([new WorkbenchRowLocation(0, 0)], rows);
        Assert.Empty(document.BindingRowIndex.NarrationRows);
    }

    [Fact]
    public void BindingRowIndexMergesCharacterRowsInDocumentOrder()
    {
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_001",
                    1,
                    "Scene 1",
                    string.Empty,
                    [
                        Row("int_001", "scene_001", 1) with { SubjectCharacterId = "char_chen", SubjectName = "Alias" },
                        Row("int_002", "scene_001", 2) with { SubjectCharacterId = "char_other", SubjectName = "Chen" }
                    ]),
                new WorkbenchSceneDraft(
                    "scene_002",
                    2,
                    "Scene 2",
                    string.Empty,
                    [Row("int_003", "scene_002", 1) with { SubjectCharacterId = "char_chen", SubjectName = "Alias" }])
            ],
            []);

        var rows = document.BindingRowIndex.RowsForCharacter("char_chen", "Chen");

        Assert.Equal(
            [
                new WorkbenchRowLocation(0, 0),
                new WorkbenchRowLocation(0, 1),
                new WorkbenchRowLocation(1, 0)
            ],
            rows);
    }

    private static WorkbenchRowDraft Row(string interactionId, string sceneId, int order)
    {
        return new WorkbenchRowDraft(
            interactionId,
            sceneId,
            order,
            WorkbenchRowKind.Dialogue,
            "Chen",
            "char_chen",
            "Calm 0.6",
            "Hello",
            null,
            null,
            new WorkbenchTaskLinks(null));
    }

    private static WorkbenchSceneEdit SceneEdit(
        string sceneId,
        int order,
        IReadOnlyList<WorkbenchInteractionEdit> rows)
    {
        return new WorkbenchSceneEdit(
            sceneId,
            order,
            $"Scene {sceneId}",
            string.Empty,
            rows);
    }

    private static WorkbenchInteractionEdit RowEdit(string interactionId, int order)
    {
        return new WorkbenchInteractionEdit(
            interactionId,
            order,
            "Chen",
            "Calm 0",
            "Hello",
            null,
            null);
    }
}




