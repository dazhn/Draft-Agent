using System.Collections.ObjectModel;
using DraftAgent.App.ViewModels;
using DraftAgent.Core;

namespace DraftAgent.App.Tests;

public sealed class WorkbenchViewModelAdapterTests
{
    [Fact]
    public void BuildDocumentMapsViewModelsThroughSingleAdapter()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        scene.SceneName = "Rain street";
        scene.SelectedSceneAsset = "asset_scene_001";
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        row.SelectedCharacterAsset = "asset_character_chen";
        row.SelectedVoiceReference = "voices/chen.wav";
        row.SelectedVoiceReferenceText = "Chen reference";
        scene.Rows.Add(row);

        var document = WorkbenchViewModelAdapter.BuildDocument([scene]);

        Assert.Equal("asset_scene_001", SceneVisualId(document.Scenes.Single()));
        Assert.Equal("asset_character_chen", RowVisualId(document.RowsById[row.InteractionId]));
        Assert.Equal("voices/chen.wav", RowVoiceId(document.RowsById[row.InteractionId]));
        Assert.Equal("Chen reference", RowVoiceText(document.RowsById[row.InteractionId]));
    }

    [Fact]
    public void BuildDocumentPreservesAppliedPsdVisualBinding()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(row);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    scene.SceneId,
                    1,
                    scene.SceneName,
                    scene.SceneEffect,
                    [
                        WorkbenchViewModelAdapter.BuildRowDraft(row, scene.SceneId) with
                        {
                            CharacterVisualBinding = VisualAssetBinding.FromPsd(
                                "asset_psd_chen",
                                "asset_png_chen_calm")
                        }
                    ])
            ],
            []);
        WorkbenchViewModelAdapter.ApplyDocument(scenes, document);

        var rebuilt = WorkbenchViewModelAdapter.BuildDocument(scenes);
        var rebuiltRow = rebuilt.RowsById[row.InteractionId];

        Assert.Equal("asset_png_chen_calm", RowVisualId(rebuiltRow));
        Assert.Equal(VisualSourceKind.Psd, rebuiltRow.CharacterVisualBinding?.SourceKind);
        Assert.Equal("asset_psd_chen", rebuiltRow.CharacterVisualBinding?.SourceAssetId);
        Assert.Equal("asset_png_chen_calm", rebuiltRow.CharacterVisualBinding?.RenderedAssetId);
    }

    [Fact]
    public void BuildDocumentDowngradesAppliedScenePsdVisualBindingToRenderedImage()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    scene.SceneId,
                    1,
                    scene.SceneName,
                    scene.SceneEffect,
                    [],
                    VisualAssetBinding.FromPsd("asset_psd_scene", "asset_png_scene_rendered"))
            ],
            []);
        WorkbenchViewModelAdapter.ApplyDocument(scenes, document);

        var rebuilt = WorkbenchViewModelAdapter.BuildDocument(scenes);
        var rebuiltScene = Assert.Single(rebuilt.Scenes);

        Assert.Equal("asset_png_scene_rendered", SceneVisualId(rebuiltScene));
        Assert.Equal(VisualSourceKind.Image, rebuiltScene.VisualBinding?.SourceKind);
        Assert.Equal("asset_png_scene_rendered", rebuiltScene.VisualBinding?.SourceAssetId);
        Assert.Equal("asset_png_scene_rendered", rebuiltScene.VisualBinding?.RenderedAssetId);
    }

    [Fact]
    public void BuildDocumentClearsVoiceSourceWhenRowVoiceReferenceChanges()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.FromDraft(
            new WorkbenchRowDraft(
                "interaction_chen",
                scene.SceneId,
                1,
                WorkbenchRowKind.Dialogue,
                "Chen",
                "character_chen",
                "Calm",
                "Hello",
                null,
                null,
                new WorkbenchTaskLinks(null),
                null,
                VoiceAssetBinding.FromProjectReference(
                    "voices/old.wav",
                    "old reference",
                    "asset_voice_old",
                    @"C:\voices\old.wav")),
            null);
        scene.Rows.Add(row);

        row.SelectedVoiceReference = "voices/new.wav";
        row.SelectedVoiceReferenceText = "new reference";

        var rebuilt = WorkbenchViewModelAdapter.BuildDocument([scene]);
        var rebuiltRow = rebuilt.RowsById[row.InteractionId];

        Assert.Equal("voices/new.wav", RowVoiceId(rebuiltRow));
        Assert.Equal("new reference", RowVoiceText(rebuiltRow));
        Assert.Null(rebuiltRow.VoiceBinding?.SourceAssetId);
        Assert.Null(rebuiltRow.VoiceBinding?.SyncedVoicePath);
    }

    [Fact]
    public void BuildSceneEditsMapsSceneAndRows()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        scene.SceneName = "rain street";
        scene.SceneEffect = "heavy rain";
        scene.SelectedSceneAsset = "asset_scene_001";
        var narration = AnalysisResultRowViewModel.CreateManualNarration(1);
        narration.LineText = "Chen walks through the rain.";
        narration.SelectedVoiceReference = "voices/narrator.wav";
        narration.SelectedVoiceReferenceText = "Narrator sample";
        var dialogue = AnalysisResultRowViewModel.CreateManualDialogue(2, "Chen");
        dialogue.LineText = "I am late.";
        dialogue.SelectedCharacterAsset = "asset_character_chen";
        dialogue.SelectedVoiceReference = "voices/chen.wav";
        dialogue.SelectedVoiceReferenceText = "Chen sample";
        dialogue.EmotionAlpha = 0.8;
        dialogue.EmotionVectors.Single(vector => vector.Key == "Afraid").Value = 0.7;
        scene.Rows.Add(narration);
        scene.Rows.Add(dialogue);

        var sceneEdit = Assert.Single(WorkbenchViewModelAdapter.BuildSceneEdits([scene]));

        Assert.Equal(scene.SceneId, sceneEdit.SceneId);
        Assert.Equal("rain street", sceneEdit.Name);
        Assert.Equal("heavy rain", sceneEdit.Effect);
        Assert.Equal("asset_scene_001", sceneEdit.VisualBinding?.EffectiveAssetId);
        Assert.Equal(2, sceneEdit.Rows.Count);

        Assert.Equal(narration.InteractionId, sceneEdit.Rows[0].InteractionId);
        Assert.Equal("旁白", sceneEdit.Rows[0].SubjectName);
        Assert.Equal("Calm 0.6", sceneEdit.Rows[0].Emotion);
        Assert.Equal("Chen walks through the rain.", sceneEdit.Rows[0].Text);
        Assert.Equal("voices/narrator.wav", sceneEdit.Rows[0].VoiceBinding?.EffectiveVoiceReferenceId);
        Assert.Equal("Narrator sample", sceneEdit.Rows[0].VoiceBinding?.EffectiveReferenceText);

        Assert.Equal(dialogue.InteractionId, sceneEdit.Rows[1].InteractionId);
        Assert.Equal("Chen", sceneEdit.Rows[1].SubjectName);
        Assert.Equal("I am late.", sceneEdit.Rows[1].Text);
        Assert.Equal("asset_character_chen", sceneEdit.Rows[1].CharacterVisualBinding?.EffectiveAssetId);
        Assert.Equal("voices/chen.wav", sceneEdit.Rows[1].VoiceBinding?.EffectiveVoiceReferenceId);
        Assert.Equal("Chen sample", sceneEdit.Rows[1].VoiceBinding?.EffectiveReferenceText);
        Assert.Equal(0.8, sceneEdit.Rows[1].EmotionAlpha);
        Assert.NotNull(sceneEdit.Rows[1].EmotionVectors);
        Assert.Equal(0.7, sceneEdit.Rows[1].EmotionVectors!["Afraid"]);
    }

    [Fact]
    public void BuildCharacterBindingsMapsCharacterTargetsOnly()
    {
        var visualBinding = VisualAssetBinding.FromPsd(
            "asset_psd_chen",
            "asset_png_chen_calm",
            new PsdRenderSelection(
                [12],
                ["calm"],
                "preset_calm",
                "Calm",
                null,
                VisualExportBounds.CanvasBounds,
                "fingerprint_chen_v1",
                "cache_chen_calm"));
        var voiceBinding = VoiceAssetBinding.FromProjectReference(
            "voices/chen.wav",
            "Chen reference line",
            "asset_voice_chen_source",
            "ComfyUI/input/voices/chen.wav");
        var character = new Character(
            "char_chen",
            "Chen",
            "role",
            "appearance",
            "voice",
            "Calm",
            new AssetBinding(
                Portrait: "asset_png_chen_calm",
                Voice: "voices/chen.wav",
                PortraitVisualBinding: visualBinding,
                VoiceBinding: voiceBinding));
        var target = BindingTargetViewModel.ForCharacter(character, []);
        var scene = SceneTableViewModel.CreateManual(1);
        var sceneTarget = BindingTargetViewModel.ForScene(scene);

        var edits = WorkbenchViewModelAdapter.BuildCharacterBindings([target, sceneTarget]);

        var edit = Assert.Single(edits);
        Assert.Equal("char_chen", edit.CharacterId);
        Assert.Equal("asset_png_chen_calm", edit.VisualBinding?.EffectiveAssetId);
        Assert.Equal("voices/chen.wav", edit.VoiceBinding?.EffectiveVoiceReferenceId);
        Assert.Equal(VisualSourceKind.Psd, edit.VisualBinding?.SourceKind);
        Assert.Equal("asset_psd_chen", edit.VisualBinding?.SourceAssetId);
        Assert.Equal("asset_png_chen_calm", edit.VisualBinding?.RenderedAssetId);
        Assert.Equal(["calm"], edit.VisualBinding?.PsdSelection?.LayerNames);
        Assert.Equal("asset_voice_chen_source", edit.VoiceBinding?.SourceAssetId);
        Assert.Equal("ComfyUI/input/voices/chen.wav", edit.VoiceBinding?.SyncedVoicePath);
        Assert.Equal("Chen reference line", edit.VoiceBinding?.ReferenceText);
    }

    [Fact]
    public void BuildDocumentFromScenesDoesNotIncludeDerivedBindingTargets()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var character = new Character(
            "char_chen",
            "Chen",
            "role",
            "appearance",
            "voice",
            "Calm",
            new AssetBinding());
        var characterTarget = BindingTargetViewModel.ForCharacter(character, [scene]);
        characterTarget.ImageAsset = "asset_character_chen";
        characterTarget.VoiceAsset = "voices/chen.wav";
        var sceneTarget = BindingTargetViewModel.ForScene(scene);

        var document = WorkbenchViewModelAdapter.BuildDocument([scene]);

        Assert.Empty(document.CharacterBindings);

        var binding = Assert.Single(WorkbenchViewModelAdapter.BuildCharacterBindings([characterTarget, sceneTarget]));
        Assert.Equal("char_chen", binding.CharacterId);
        Assert.Equal("asset_character_chen", binding.VisualBinding?.EffectiveAssetId);
        Assert.Equal("voices/chen.wav", binding.VoiceBinding?.EffectiveVoiceReferenceId);
    }

    [Fact]
    public void SyncSceneTargetOrderRangeMovesOnlyAffectedSceneTargets()
    {
        var first = SceneTableViewModel.CreateManual(1);
        var second = SceneTableViewModel.CreateManual(2);
        var third = SceneTableViewModel.CreateManual(3);
        var fourth = SceneTableViewModel.CreateManual(4);
        var targets = new ObservableCollection<BindingTargetViewModel>(
            new[] { first, second, third, fourth }.Select(BindingTargetViewModel.ForScene));
        var firstTarget = targets[0];
        var secondTarget = targets[1];
        var thirdTarget = targets[2];
        var fourthTarget = targets[3];
        var actions = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        targets.CollectionChanged += (_, args) => actions.Add(args.Action);

        first.Order = 4;
        second.Order = 1;
        third.Order = 2;
        fourth.Order = 3;
        var synced = WorkbenchBindingProjection.SyncSceneTargetOrderRange(targets, [second, third, fourth, first]);

        Assert.True(synced);
        Assert.Equal([second.SceneId, third.SceneId, fourth.SceneId, first.SceneId], targets.Select(target => target.TargetId));
        Assert.Same(secondTarget, targets[0]);
        Assert.Same(thirdTarget, targets[1]);
        Assert.Same(fourthTarget, targets[2]);
        Assert.Same(firstTarget, targets[3]);
        Assert.Equal([System.Collections.Specialized.NotifyCollectionChangedAction.Move], actions);
    }

    [Fact]
    public void BuildDocumentConsumesSceneEnumerableOnce()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        scene.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen"));

        var document = WorkbenchViewModelAdapter.BuildDocument(new SinglePassEnumerable<SceneTableViewModel>([scene]));

        Assert.Single(document.Scenes);
        Assert.Single(document.Scenes[0].Rows);
        Assert.Empty(document.CharacterBindings);
    }

    [Fact]
    public void BuildFiltersUiBindingPlaceholders()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        scene.SelectedSceneAsset = "未绑定";
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        row.SelectedCharacterAsset = "未绑定图片";
        row.SelectedVoiceReference = "未绑定声音";
        row.SelectedVoiceReferenceText = "未绑定";
        scene.Rows.Add(row);

        var character = new Character(
            "char_chen",
            "Chen",
            "role",
            "appearance",
            "voice",
            "Calm",
            new AssetBinding());
        var target = BindingTargetViewModel.ForCharacter(character, []);
        target.ImageAsset = "未绑定图片";
        target.VoiceAsset = "未绑定声音";

        var document = WorkbenchViewModelAdapter.BuildDocument([scene]);
        var bindingEdit = Assert.Single(WorkbenchViewModelAdapter.BuildCharacterBindings([target]));

        var sceneDraft = Assert.Single(document.Scenes);
        var rowDraft = Assert.Single(sceneDraft.Rows);
        Assert.Null(SceneVisualId(sceneDraft));
        Assert.Null(RowVisualId(rowDraft));
        Assert.Null(RowVoiceId(rowDraft));
        Assert.Null(RowVoiceText(rowDraft));
        Assert.Null(bindingEdit.VisualBinding?.EffectiveAssetId);
        Assert.Null(bindingEdit.VoiceBinding?.EffectiveVoiceReferenceId);
    }


    [Fact]
    public void BuildDocumentPreservesAppliedNullableEmotionStateWhenRowWasNotEdited()
    {
        var rowDraft = new WorkbenchRowDraft(
            "row_without_tts_emotion",
            "scene",
            1,
            WorkbenchRowKind.Dialogue,
            "Chen",
            null,
            "Calm 0",
            "Line",
            null,
            null,
            new WorkbenchTaskLinks(null));
        var sceneDraft = new WorkbenchSceneDraft("scene", 1, "Scene", string.Empty, [rowDraft]);
        var scenes = new ObservableCollection<SceneTableViewModel>();
        WorkbenchViewModelAdapter.ApplyDocument(scenes, new WorkbenchDocument([sceneDraft], []));

        var rebuilt = WorkbenchViewModelAdapter.BuildDocument(scenes);

        var rebuiltRow = rebuilt.RowsById[rowDraft.InteractionId];
        Assert.Null(rebuiltRow.EmotionVectors);
        Assert.Null(rebuiltRow.EmotionAlpha);
    }

    [Fact]
    public void BuildDocumentWritesEmotionStateAfterRowEmotionEditInvalidatesAppliedDraft()
    {
        var rowDraft = new WorkbenchRowDraft(
            "row_edited_emotion",
            "scene",
            1,
            WorkbenchRowKind.Dialogue,
            "Chen",
            null,
            "Calm 0",
            "Line",
            null,
            null,
            new WorkbenchTaskLinks(null));
        var sceneDraft = new WorkbenchSceneDraft("scene", 1, "Scene", string.Empty, [rowDraft]);
        var scenes = new ObservableCollection<SceneTableViewModel>();
        WorkbenchViewModelAdapter.ApplyDocument(scenes, new WorkbenchDocument([sceneDraft], []));
        var row = scenes.Single().Rows.Single();

        row.EmotionAlpha = 0.75;
        row.EmotionVectors.Single(vector => vector.Key == "Afraid").Value = 0.8;
        var rebuilt = WorkbenchViewModelAdapter.BuildDocument(scenes);

        var rebuiltRow = rebuilt.RowsById[rowDraft.InteractionId];
        Assert.Equal(0.75, rebuiltRow.EmotionAlpha);
        Assert.NotNull(rebuiltRow.EmotionVectors);
        Assert.Equal(0.8, rebuiltRow.EmotionVectors!["Afraid"]);
    }


    [Fact]
    public void ApplyDocumentSkipsUnchangedFieldNotifications()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(row);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var document = WorkbenchViewModelAdapter.BuildDocument(scenes);
        WorkbenchViewModelAdapter.ApplyDocument(scenes, document);
        var sceneNotifications = 0;
        var rowNotifications = 0;
        scene.PropertyChanged += (_, _) => sceneNotifications++;
        row.PropertyChanged += (_, _) => rowNotifications++;

        WorkbenchViewModelAdapter.ApplyDocument(scenes, document);

        Assert.Equal(0, sceneNotifications);
        Assert.Equal(0, rowNotifications);
        Assert.Same(scene, scenes.Single());
        Assert.Same(row, scene.Rows.Single());
    }

    [Fact]
    public void ApplyDocumentUpdatesAlignedRowsWithoutCollectionChanges()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var first = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var second = AnalysisResultRowViewModel.CreateManualDialogue(2, "Lin");
        scene.Rows.Add(first);
        scene.Rows.Add(second);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var document = WorkbenchViewModelAdapter.BuildDocument(scenes);
        WorkbenchViewModelAdapter.ApplyDocument(scenes, document);
        var sceneCollectionChanges = 0;
        var rowCollectionChanges = 0;
        var firstNotifications = new List<string>();
        var secondNotifications = new List<string>();
        scenes.CollectionChanged += (_, _) => sceneCollectionChanges++;
        scene.Rows.CollectionChanged += (_, _) => rowCollectionChanges++;
        first.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                firstNotifications.Add(args.PropertyName);
            }
        };
        second.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                secondNotifications.Add(args.PropertyName);
            }
        };

        var updated = document with
        {
            Scenes =
            [
                document.Scenes[0] with
                {
                    Rows =
                    [
                        document.Scenes[0].Rows[0] with { Text = "Updated first line" },
                        document.Scenes[0].Rows[1]
                    ]
                }
            ]
        };

        WorkbenchViewModelAdapter.ApplyDocument(scenes, updated);

        Assert.Equal("Updated first line", first.LineText);
        Assert.Equal(0, sceneCollectionChanges);
        Assert.Equal(0, rowCollectionChanges);
        Assert.Same(first, scene.Rows[0]);
        Assert.Same(second, scene.Rows[1]);
        Assert.Contains(nameof(AnalysisResultRowViewModel.LineText), firstNotifications);
        Assert.Empty(secondNotifications);
        Assert.Same(updated.Scenes[0], scene.AppliedDraft);
        Assert.Same(updated.Scenes[0].Rows[0], first.AppliedDraft);
        Assert.Same(updated.Scenes[0].Rows[1], second.AppliedDraft);
    }

    [Fact]
    public void ApplyDocumentUpdatesAlignedScenesAndRowsWithoutCollectionChanges()
    {
        var firstScene = SceneTableViewModel.CreateManual(1);
        var firstSceneRow = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        firstScene.Rows.Add(firstSceneRow);
        var secondScene = SceneTableViewModel.CreateManual(2);
        var secondSceneFirstRow = AnalysisResultRowViewModel.CreateManualDialogue(1, "Lin");
        var secondSceneSecondRow = AnalysisResultRowViewModel.CreateManualDialogue(2, "Mo");
        secondScene.Rows.Add(secondSceneFirstRow);
        secondScene.Rows.Add(secondSceneSecondRow);
        var scenes = new ObservableCollection<SceneTableViewModel>([firstScene, secondScene]);
        var document = WorkbenchViewModelAdapter.BuildDocument(scenes);
        WorkbenchViewModelAdapter.ApplyDocument(scenes, document);
        var sceneCollectionChanges = 0;
        var firstSceneRowCollectionChanges = 0;
        var secondSceneRowCollectionChanges = 0;
        var firstSceneNotifications = new List<string>();
        var secondSceneNotifications = new List<string>();
        var firstSceneRowNotifications = new List<string>();
        var secondSceneFirstRowNotifications = new List<string>();
        var secondSceneSecondRowNotifications = new List<string>();
        scenes.CollectionChanged += (_, _) => sceneCollectionChanges++;
        firstScene.Rows.CollectionChanged += (_, _) => firstSceneRowCollectionChanges++;
        secondScene.Rows.CollectionChanged += (_, _) => secondSceneRowCollectionChanges++;
        firstScene.PropertyChanged += (_, args) => AddNotification(firstSceneNotifications, args.PropertyName);
        secondScene.PropertyChanged += (_, args) => AddNotification(secondSceneNotifications, args.PropertyName);
        firstSceneRow.PropertyChanged += (_, args) => AddNotification(firstSceneRowNotifications, args.PropertyName);
        secondSceneFirstRow.PropertyChanged += (_, args) => AddNotification(secondSceneFirstRowNotifications, args.PropertyName);
        secondSceneSecondRow.PropertyChanged += (_, args) => AddNotification(secondSceneSecondRowNotifications, args.PropertyName);

        var updated = document with
        {
            Scenes =
            [
                document.Scenes[0] with
                {
                    Name = "Updated first scene"
                },
                document.Scenes[1] with
                {
                    Rows =
                    [
                        document.Scenes[1].Rows[0] with { Text = "Updated second scene row" },
                        document.Scenes[1].Rows[1]
                    ]
                }
            ]
        };

        WorkbenchViewModelAdapter.ApplyDocument(scenes, updated);

        Assert.Equal("Updated first scene", firstScene.SceneName);
        Assert.Equal("Updated second scene row", secondSceneFirstRow.LineText);
        Assert.Equal(0, sceneCollectionChanges);
        Assert.Equal(0, firstSceneRowCollectionChanges);
        Assert.Equal(0, secondSceneRowCollectionChanges);
        Assert.Same(firstScene, scenes[0]);
        Assert.Same(secondScene, scenes[1]);
        Assert.Same(firstSceneRow, firstScene.Rows[0]);
        Assert.Same(secondSceneFirstRow, secondScene.Rows[0]);
        Assert.Same(secondSceneSecondRow, secondScene.Rows[1]);
        Assert.Contains(nameof(SceneTableViewModel.SceneName), firstSceneNotifications);
        Assert.Empty(secondSceneNotifications);
        Assert.Empty(firstSceneRowNotifications);
        Assert.Contains(nameof(AnalysisResultRowViewModel.LineText), secondSceneFirstRowNotifications);
        Assert.Empty(secondSceneSecondRowNotifications);
        Assert.Same(updated.Scenes[0], firstScene.AppliedDraft);
        Assert.Same(updated.Scenes[1], secondScene.AppliedDraft);
        Assert.Same(updated.Scenes[1].Rows[0], secondSceneFirstRow.AppliedDraft);
    }

    [Fact]
    public void ApplyDocumentDoesNotRefreshPreviewNotificationsForSceneTextOnlyChanges()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(row);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var document = WorkbenchViewModelAdapter.BuildDocument(scenes);
        WorkbenchViewModelAdapter.ApplyDocument(scenes, document);
        var notifications = new List<string>();
        scene.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                notifications.Add(args.PropertyName);
            }
        };

        var updated = document with
        {
            Scenes =
            [
                document.Scenes[0] with
                {
                    Name = "Updated scene",
                    Effect = "Updated effect"
                }
            ]
        };

        WorkbenchViewModelAdapter.ApplyDocument(scenes, updated);

        Assert.Contains(nameof(SceneTableViewModel.SceneName), notifications);
        Assert.Contains(nameof(SceneTableViewModel.SceneEffect), notifications);
        Assert.DoesNotContain(nameof(SceneTableViewModel.FirstCharacterPreviewAsset), notifications);
        Assert.DoesNotContain(nameof(SceneTableViewModel.SceneImagePreviewLabel), notifications);
    }

    [Fact]
    public void ApplyDocumentRefreshesPreviewNotificationsWhenCharacterAssetChanges()
    {
        var imagePath = CreateTempPng();
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(row);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var document = WorkbenchViewModelAdapter.BuildDocument(scenes);
        WorkbenchViewModelAdapter.ApplyDocument(scenes, document);
        var notifications = new List<string>();
        scene.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                notifications.Add(args.PropertyName);
            }
        };

        var updated = document with
        {
            Scenes =
            [
                document.Scenes[0] with
                {
                    Rows =
                    [
                        document.Scenes[0].Rows[0] with
                        {
                            CharacterVisualBinding = VisualAssetBinding.FromImage(imagePath)
                        }
                    ]
                }
            ]
        };

        WorkbenchViewModelAdapter.ApplyDocument(scenes, updated);

        Assert.Equal(imagePath, row.SelectedCharacterAsset);
        Assert.Equal(imagePath, row.SelectedCharacterPreviewAsset);
        Assert.Equal(imagePath, scene.FirstCharacterPreviewAsset);
        Assert.DoesNotContain(nameof(SceneTableViewModel.FirstCharacterPreviewAsset), notifications);
    }

    [Fact]
    public void ApplyDocumentRowReorderNotifiesFirstCharacterPreviewOnlyWhenItChanges()
    {
        var firstImage = CreateTempPng();
        var secondImage = CreateTempPng();
        var scene = SceneTableViewModel.CreateManual(1);
        var first = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var second = AnalysisResultRowViewModel.CreateManualDialogue(2, "Lin");
        var third = AnalysisResultRowViewModel.CreateManualDialogue(3, "Mo");
        first.SelectedCharacterPreviewAsset = firstImage;
        second.SelectedCharacterPreviewAsset = secondImage;
        scene.Rows.Add(first);
        scene.Rows.Add(second);
        scene.Rows.Add(third);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var document = WorkbenchViewModelAdapter.BuildDocument(scenes);
        WorkbenchViewModelAdapter.ApplyDocument(scenes, document);
        _ = scene.FirstCharacterPreviewAsset;
        var notifications = new List<string>();
        scene.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                notifications.Add(args.PropertyName);
            }
        };

        var unchangedPreviewReorder = document with
        {
            Scenes =
            [
                document.Scenes[0] with
                {
                    Rows =
                    [
                        document.Scenes[0].Rows[0] with { Order = 1 },
                        document.Scenes[0].Rows[2] with { Order = 2 },
                        document.Scenes[0].Rows[1] with { Order = 3 }
                    ]
                }
            ]
        };

        WorkbenchViewModelAdapter.ApplyDocument(scenes, unchangedPreviewReorder);

        Assert.Equal(firstImage, scene.FirstCharacterPreviewAsset);
        Assert.DoesNotContain(nameof(SceneTableViewModel.FirstCharacterPreviewAsset), notifications);

        notifications.Clear();
        var changedPreviewReorder = unchangedPreviewReorder with
        {
            Scenes =
            [
                unchangedPreviewReorder.Scenes[0] with
                {
                    Rows =
                    [
                        unchangedPreviewReorder.Scenes[0].Rows[2] with { Order = 1 },
                        unchangedPreviewReorder.Scenes[0].Rows[0] with { Order = 2 },
                        unchangedPreviewReorder.Scenes[0].Rows[1] with { Order = 3 }
                    ]
                }
            ]
        };

        WorkbenchViewModelAdapter.ApplyDocument(scenes, changedPreviewReorder);

        Assert.Equal(secondImage, scene.FirstCharacterPreviewAsset);
        Assert.DoesNotContain(nameof(SceneTableViewModel.FirstCharacterPreviewAsset), notifications);
    }

    [Fact]
    public void ApplyDocumentRowReorderReusesRowsAndUpdatesOnlyAffectedOrders()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var rows = Enumerable.Range(1, 4)
            .Select(index => AnalysisResultRowViewModel.CreateManualDialogue(index, $"Character {index}"))
            .ToArray();
        foreach (var row in rows)
        {
            scene.Rows.Add(row);
        }

        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var document = WorkbenchViewModelAdapter.BuildDocument(scenes);
        WorkbenchViewModelAdapter.ApplyDocument(scenes, document);
        var notificationsByRow = rows.ToDictionary(row => row, _ => 0);
        foreach (var row in rows)
        {
            row.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(AnalysisResultRowViewModel.Order))
                {
                    notificationsByRow[row]++;
                }
            };
        }

        var reordered = document with
        {
            Scenes =
            [
                document.Scenes[0] with
                {
                    Rows =
                    [
                        document.Scenes[0].Rows[1] with { Order = 1 },
                        document.Scenes[0].Rows[2] with { Order = 2 },
                        document.Scenes[0].Rows[0] with { Order = 3 },
                        document.Scenes[0].Rows[3] with { Order = 4 }
                    ]
                }
            ]
        };

        WorkbenchViewModelAdapter.ApplyDocument(scenes, reordered);

        Assert.Equal(
            [rows[1].InteractionId, rows[2].InteractionId, rows[0].InteractionId, rows[3].InteractionId],
            scene.Rows.Select(row => row.InteractionId));
        Assert.Same(rows[1], scene.Rows[0]);
        Assert.Same(rows[2], scene.Rows[1]);
        Assert.Same(rows[0], scene.Rows[2]);
        Assert.Same(rows[3], scene.Rows[3]);
        Assert.True(notificationsByRow[rows[0]] > 0);
        Assert.True(notificationsByRow[rows[1]] > 0);
        Assert.True(notificationsByRow[rows[2]] > 0);
        Assert.Equal(0, notificationsByRow[rows[3]]);
    }

    [Fact]
    public void ApplyDocumentMarksAppliedDraftReferencesForFastReapply()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(row);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var document = WorkbenchViewModelAdapter.BuildDocument(scenes);

        WorkbenchViewModelAdapter.ApplyDocument(scenes, document);

        Assert.Same(document.Scenes.Single(), scene.AppliedDraft);
        Assert.Same(document.Scenes.Single().Rows.Single(), row.AppliedDraft);
    }

    [Fact]
    public void MarkAppliedDraftsMarksAlignedRowsWithoutNotifications()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var first = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var second = AnalysisResultRowViewModel.CreateManualDialogue(2, "Lin");
        scene.Rows.Add(first);
        scene.Rows.Add(second);
        var document = WorkbenchViewModelAdapter.BuildDocument([scene]);
        var updated = document with
        {
            Scenes =
            [
                document.Scenes[0] with
                {
                    Rows =
                    [
                        document.Scenes[0].Rows[0] with { Text = "Updated first line" },
                        document.Scenes[0].Rows[1] with { Text = "Updated second line" }
                    ]
                }
            ]
        };
        var sceneNotifications = 0;
        var rowNotifications = 0;
        scene.PropertyChanged += (_, _) => sceneNotifications++;
        first.PropertyChanged += (_, _) => rowNotifications++;
        second.PropertyChanged += (_, _) => rowNotifications++;

        WorkbenchViewModelAdapter.MarkAppliedDrafts(scene, updated);

        Assert.Equal(0, sceneNotifications);
        Assert.Equal(0, rowNotifications);
        Assert.Same(updated.Scenes[0], scene.AppliedDraft);
        Assert.Same(updated.Scenes[0].Rows[0], first.AppliedDraft);
        Assert.Same(updated.Scenes[0].Rows[1], second.AppliedDraft);
    }

    [Fact]
    public void MarkAppliedDraftsFallsBackToRowIdsWhenOrderDiffers()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var first = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var second = AnalysisResultRowViewModel.CreateManualDialogue(2, "Lin");
        scene.Rows.Add(first);
        scene.Rows.Add(second);
        var document = WorkbenchViewModelAdapter.BuildDocument([scene]);
        var reordered = document with
        {
            Scenes =
            [
                document.Scenes[0] with
                {
                    Rows =
                    [
                        document.Scenes[0].Rows[1],
                        document.Scenes[0].Rows[0]
                    ]
                }
            ]
        };

        WorkbenchViewModelAdapter.MarkAppliedDrafts(scene, reordered);

        Assert.Same(reordered.Scenes[0].Rows[1], first.AppliedDraft);
        Assert.Same(reordered.Scenes[0].Rows[0], second.AppliedDraft);
    }

    [Fact]
    public void EditingRowInvalidatesAppliedDraftCache()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(row);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var document = WorkbenchViewModelAdapter.BuildDocument(scenes);
        WorkbenchViewModelAdapter.ApplyDocument(scenes, document);

        row.LineText = "Changed line";

        Assert.Null(row.AppliedDraft);
        Assert.Null(scene.AppliedDraft);
    }

    [Fact]
    public void EditingSceneInvalidatesAppliedDraftCache()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(row);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var document = WorkbenchViewModelAdapter.BuildDocument(scenes);
        WorkbenchViewModelAdapter.ApplyDocument(scenes, document);

        scene.SceneName = "Changed scene";

        Assert.Null(scene.AppliedDraft);
        Assert.Same(document.Scenes.Single().Rows.Single(), row.AppliedDraft);
    }

    [Fact]
    public void SelectingRowDoesNotInvalidateSceneAppliedDraftCache()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(row);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var document = WorkbenchViewModelAdapter.BuildDocument(scenes);
        WorkbenchViewModelAdapter.ApplyDocument(scenes, document);

        row.IsSelected = true;

        Assert.Same(document.Scenes.Single(), scene.AppliedDraft);
        Assert.Same(document.Scenes.Single().Rows.Single(), row.AppliedDraft);
    }

    [Fact]
    public void PreviewOnlyRowFieldsDoNotInvalidateSceneAppliedDraftCache()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(row);
        var scenes = new ObservableCollection<SceneTableViewModel>([scene]);
        var document = WorkbenchViewModelAdapter.BuildDocument(scenes);
        WorkbenchViewModelAdapter.ApplyDocument(scenes, document);

        row.SelectedCharacterPreviewAsset = @"C:\preview-only\character.png";
        row.PreviewAssetText = "asset_scene_preview";
        row.PreviewAssetPath = @"C:\preview-only\scene.png";

        Assert.Same(document.Scenes.Single(), scene.AppliedDraft);
        Assert.Same(document.Scenes.Single().Rows.Single(), row.AppliedDraft);
    }

    [Fact]
    public void ApplyDocumentRemovesScenesMissingFromDocument()
    {
        var first = SceneTableViewModel.CreateManual(1);
        var second = SceneTableViewModel.CreateManual(2);
        var scenes = new ObservableCollection<SceneTableViewModel>([first, second]);
        var document = WorkbenchViewModelAdapter.BuildDocument([first]);

        WorkbenchViewModelAdapter.ApplyDocument(scenes, document);

        Assert.Single(scenes);
        Assert.Same(first, scenes[0]);
    }

    [Fact]
    public void ApplyDocumentReusesRowsFromInitialSceneSnapshotWhenSourceSceneIsRemoved()
    {
        var first = SceneTableViewModel.CreateManual(1);
        var second = SceneTableViewModel.CreateManual(2);
        var movedRow = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        first.Rows.Add(movedRow);
        var scenes = new ObservableCollection<SceneTableViewModel>([first, second]);
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    second.SceneId,
                    1,
                    second.SceneName,
                    second.SceneEffect,
                    [
                        WorkbenchViewModelAdapter.BuildRowDraft(movedRow, second.SceneId) with
                        {
                            Order = 1
                        }
                    ])
            ],
            []);

        WorkbenchViewModelAdapter.ApplyDocument(scenes, document);

        Assert.Single(scenes);
        Assert.Same(second, scenes[0]);
        Assert.Same(movedRow, Assert.Single(second.Rows));
        Assert.Equal(second.SceneId, document.RowsById[movedRow.InteractionId].SceneId);
    }

    [Fact]
    public void ApplyDocumentReusesRowsRemovedFromEarlierSceneDuringCrossSceneMove()
    {
        var first = SceneTableViewModel.CreateManual(1);
        var second = SceneTableViewModel.CreateManual(2);
        var movedRow = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        movedRow.SelectedVoiceReference = "voices/chen.wav";
        var existingTargetRow = AnalysisResultRowViewModel.CreateManualDialogue(1, "Lin");
        first.Rows.Add(movedRow);
        second.Rows.Add(existingTargetRow);
        var scenes = new ObservableCollection<SceneTableViewModel>([first, second]);
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    first.SceneId,
                    1,
                    first.SceneName,
                    first.SceneEffect,
                    []),
                new WorkbenchSceneDraft(
                    second.SceneId,
                    2,
                    second.SceneName,
                    second.SceneEffect,
                    [
                        WorkbenchViewModelAdapter.BuildRowDraft(movedRow, second.SceneId) with
                        {
                            Order = 1
                        },
                        WorkbenchViewModelAdapter.BuildRowDraft(existingTargetRow, second.SceneId) with
                        {
                            Order = 2
                        }
                    ])
            ],
            []);

        WorkbenchViewModelAdapter.ApplyDocument(scenes, document);

        Assert.Empty(first.Rows);
        Assert.Same(movedRow, second.Rows[0]);
        Assert.Same(existingTargetRow, second.Rows[1]);
        Assert.Equal("Chen", second.Rows[0].SubjectName);
        Assert.Equal("voices/chen.wav", second.Rows[0].SelectedVoiceReference);
    }

    private static string CreateTempPng()
    {
        var path = Path.Combine(Path.GetTempPath(), $"draft-agent-adapter-preview-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, [137, 80, 78, 71]);
        return path;
    }

    private static string? SceneVisualId(WorkbenchSceneDraft scene)
    {
        return scene.VisualBinding?.EffectiveAssetId;
    }

    private static string? RowVisualId(WorkbenchRowDraft row)
    {
        return row.CharacterVisualBinding?.EffectiveAssetId;
    }

    private static string? RowVoiceId(WorkbenchRowDraft row)
    {
        return row.VoiceBinding?.EffectiveVoiceReferenceId;
    }

    private static string? RowVoiceText(WorkbenchRowDraft row)
    {
        return row.VoiceBinding?.EffectiveReferenceText;
    }

    private static void AddNotification(ICollection<string> notifications, string? propertyName)
    {
        if (propertyName is not null)
        {
            notifications.Add(propertyName);
        }
    }

    private sealed class SinglePassEnumerable<T> : IEnumerable<T>
    {
        private readonly IReadOnlyList<T> _items;
        private bool _enumerated;

        public SinglePassEnumerable(IReadOnlyList<T> items)
        {
            _items = items;
        }

        public IEnumerator<T> GetEnumerator()
        {
            if (_enumerated)
            {
                throw new InvalidOperationException("Sequence was enumerated more than once.");
            }

            _enumerated = true;
            return _items.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}



