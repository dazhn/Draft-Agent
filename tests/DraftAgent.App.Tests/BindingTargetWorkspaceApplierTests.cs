using DraftAgent.App.ViewModels;
using DraftAgent.Core;

namespace DraftAgent.App.Tests;

public sealed class BindingTargetWorkspaceApplierTests
{
    [Fact]
    public void ApplySceneBindingUpdatesSceneAndRows()
    {
        var imagePath = CreateTempPng();
        var scene = SceneTableViewModel.CreateManual(1);
        var otherScene = SceneTableViewModel.CreateManual(2);
        var dialogue = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var narration = AnalysisResultRowViewModel.CreateManualNarration(2);
        var otherDialogue = AnalysisResultRowViewModel.CreateManualDialogue(1, "Lin");
        scene.Rows.Add(dialogue);
        scene.Rows.Add(narration);
        otherScene.Rows.Add(otherDialogue);
        var target = BindingTargetViewModel.ForScene(scene);
        var session = CreateSession([scene, otherScene]);

        target.ImageAsset = imagePath;
        BindingTargetWorkspaceApplier.Apply(target, [scene, otherScene], session);

        Assert.Equal(imagePath, scene.SelectedSceneAsset);
        Assert.Equal(imagePath, scene.SelectedScenePreviewAsset);
        Assert.Equal(imagePath, dialogue.PreviewAssetText);
        Assert.Equal(imagePath, dialogue.PreviewAssetPath);
        Assert.Equal(imagePath, narration.PreviewAssetText);
        Assert.Equal(imagePath, narration.PreviewAssetPath);
        Assert.NotEqual(imagePath, otherDialogue.PreviewAssetText);
    }

    [Fact]
    public void ApplyCharacterBindingUpdatesMatchingCharacterImagesOnlyUntilVoiceRouteIsEnabled()
    {
        var imagePath = CreateTempPng();
        var scene = SceneTableViewModel.CreateManual(1);
        var chenRow = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var linRow = AnalysisResultRowViewModel.CreateManualDialogue(2, "Lin");
        var narration = AnalysisResultRowViewModel.CreateManualNarration(3);
        scene.Rows.Add(chenRow);
        scene.Rows.Add(linRow);
        scene.Rows.Add(narration);
        var target = BindingTargetViewModel.ForCharacter(CreateCharacter("character_chen", "Chen"), [scene]);
        var session = CreateSession([scene]);

        target.ImageAsset = imagePath;
        target.VoiceAsset = "voices/chen.wav";
        target.VoiceReferenceText = "Chen reference text";
        BindingTargetWorkspaceApplier.Apply(target, [scene], session);

        Assert.Equal(imagePath, chenRow.SelectedCharacterAsset);
        Assert.Equal(imagePath, chenRow.SelectedCharacterPreviewAsset);
        Assert.Equal(imagePath, chenRow.PreviewAssetText);
        Assert.Equal(imagePath, chenRow.PreviewAssetPath);
        Assert.NotEqual("voices/chen.wav", chenRow.SelectedVoiceReference);
        Assert.NotEqual("Chen reference text", chenRow.SelectedVoiceReferenceText);
        Assert.NotEqual("voices/chen.wav", chenRow.VoiceAssetText);
        Assert.NotEqual(imagePath, linRow.SelectedCharacterAsset);
        Assert.NotEqual("voices/chen.wav", linRow.SelectedVoiceReference);
        Assert.NotEqual("voices/chen.wav", narration.SelectedVoiceReference);
    }

    [Fact]
    public void ApplyCharacterVoiceBindingUpdatesReferenceTableWithoutMutatingRoutedRows()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var roleVoiceDraft = new WorkbenchRowDraft(
                "interaction_chen_thought",
                scene.SceneId,
                1,
                WorkbenchRowKind.Narration,
                "Chen",
                null,
                "Calm",
                "Inner line",
                null,
                null,
                new WorkbenchTaskLinks(null),
                VisualAssetBinding.None,
                VoiceAssetBinding.FromProjectReference("voices/chen-old.wav", "old", null, null),
                SourceItemType: "thought",
                SourceRole: "Chen",
                SourceRoleCharacterId: "character_chen",
                VoiceRole: "Chen",
                VoiceRoleCharacterId: "character_chen");
        var roleVoiceRow = AnalysisResultRowViewModel.FromDraft(
            roleVoiceDraft,
            "asset_scene");
        scene.Rows.Add(roleVoiceRow);
        var draft = new WorkbenchSceneDraft(
            scene.SceneId,
            scene.Order,
            scene.SceneName,
            scene.SceneEffect,
            [roleVoiceDraft]);
        WorkbenchViewModelAdapter.MarkAppliedDrafts(scene, draft);
        var target = BindingTargetViewModel.ForCharacter(CreateCharacter("character_chen", "Chen"), [scene]);
        var session = CreateSession([scene]);

        target.VoiceAsset = "voices/chen-new.wav";
        target.VoiceReferenceText = "new reference";
        BindingTargetWorkspaceApplier.Apply(target, [scene], session);

        Assert.Equal("voices/chen-old.wav", roleVoiceRow.SelectedVoiceReference);
        Assert.Equal("old", roleVoiceRow.SelectedVoiceReferenceText);
        var characterBinding = Assert.Single(session.Document.CharacterBindings, binding => binding.CharacterId == "character_chen");
        Assert.Equal("voices/chen-new.wav", characterBinding.VoiceBinding?.EffectiveVoiceReferenceId);
        Assert.Equal("new reference", characterBinding.VoiceBinding?.EffectiveReferenceText);
        Assert.Equal("thought", session.Document.RowsById[roleVoiceRow.InteractionId].SourceItemType);
        Assert.Equal("Chen", session.Document.RowsById[roleVoiceRow.InteractionId].SourceRole);
    }

    [Fact]
    public void CharacterVoiceBindingIsDisabledUntilRoleVoiceRouteIsEnabled()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        scene.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen"));
        var characterTarget = BindingTargetViewModel.ForCharacter(CreateCharacter("character_chen", "Chen"), [scene]);
        var narrationTarget = BindingTargetViewModel.ForNarration([scene]);

        characterTarget.ApplyVoiceBindingEditAccess(false);

        Assert.False(characterTarget.CanEditVoiceBinding);
        Assert.True(characterTarget.HasVoiceSummary);
        Assert.True(characterTarget.ShowVoiceBindingControls);
        Assert.Contains("默认配音", characterTarget.VoiceBindingAccessText);
        Assert.True(narrationTarget.CanEditVoiceBinding);
        Assert.True(narrationTarget.HasVoiceSummary);
        Assert.True(narrationTarget.ShowVoiceBindingControls);

        characterTarget.ApplyVoiceBindingEditAccess(true);

        Assert.True(characterTarget.CanEditVoiceBinding);
        Assert.True(characterTarget.HasVoiceSummary);
        Assert.True(characterTarget.ShowVoiceBindingControls);
    }

    [Fact]
    public void CharacterVoiceBindingEditAccessNotifiesVoiceControlVisibility()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        scene.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen"));
        var characterTarget = BindingTargetViewModel.ForCharacter(CreateCharacter("character_chen", "Chen"), [scene]);
        var notifications = new List<string>();
        characterTarget.PropertyChanged += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.PropertyName))
            {
                notifications.Add(args.PropertyName);
            }
        };

        characterTarget.ApplyVoiceBindingEditAccess(false);
        notifications.Clear();
        characterTarget.ApplyVoiceBindingEditAccess(true);

        Assert.Contains(nameof(BindingTargetViewModel.CanEditVoiceBinding), notifications);
        Assert.Contains(nameof(BindingTargetViewModel.VoiceBindingAccessText), notifications);
        Assert.DoesNotContain(nameof(BindingTargetViewModel.ShowVoiceBindingControls), notifications);
        Assert.DoesNotContain(nameof(BindingTargetViewModel.HasVoiceSummary), notifications);
        Assert.True(characterTarget.HasVoiceSummary);
        Assert.True(characterTarget.ShowVoiceBindingControls);
    }

    [Fact]
    public void ApplyNarrationBindingUpdatesDefaultDialogueVoiceRows()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var dialogue = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var narration = AnalysisResultRowViewModel.CreateManualNarration(2);
        scene.Rows.Add(dialogue);
        scene.Rows.Add(narration);
        var target = BindingTargetViewModel.ForNarration([scene]);
        var session = CreateSession([scene]);

        target.VoiceAsset = "voices/narrator.wav";
        target.VoiceReferenceText = "Narrator reference text";
        BindingTargetWorkspaceApplier.Apply(target, [scene], session);

        Assert.Equal("voices/narrator.wav", dialogue.SelectedVoiceReference);
        Assert.Equal("Narrator reference text", dialogue.SelectedVoiceReferenceText);
        Assert.Equal("voices/narrator.wav", narration.SelectedVoiceReference);
        Assert.Equal("Narrator reference text", narration.SelectedVoiceReferenceText);
    }

    [Fact]
    public void ApplyCharacterBindingRefreshesOnlyAffectedScenes()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var otherScene = SceneTableViewModel.CreateManual(2);
        var chenRow = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var linRow = AnalysisResultRowViewModel.CreateManualDialogue(1, "Lin");
        scene.Rows.Add(chenRow);
        otherScene.Rows.Add(linRow);
        var target = BindingTargetViewModel.ForCharacter(CreateCharacter("character_chen", "Chen"), [scene, otherScene]);
        var session = CreateSession([scene, otherScene]);
        var imagePath = CreateTempPng();
        var affectedNotifications = 0;
        var unaffectedNotifications = 0;
        scene.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SceneTableViewModel.FirstCharacterPreviewAsset))
            {
                affectedNotifications++;
            }
        };
        otherScene.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SceneTableViewModel.FirstCharacterPreviewAsset))
            {
                unaffectedNotifications++;
            }
        };

        target.ImageAsset = imagePath;
        BindingTargetWorkspaceApplier.Apply(target, [scene, otherScene], session);

        Assert.Equal(imagePath, chenRow.SelectedCharacterAsset);
        Assert.Equal(imagePath, scene.FirstCharacterPreviewAsset);
        Assert.NotEqual(imagePath, linRow.SelectedCharacterAsset);
        Assert.Equal(0, affectedNotifications);
        Assert.Equal(0, unaffectedNotifications);
    }

    [Fact]
    public void ApplyCharacterBindingThroughDocumentCommandSupportsAssetIds()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var chenRow = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(chenRow);
        var target = BindingTargetViewModel.ForCharacter(CreateCharacter("character_chen", "Chen"), [scene]);
        var session = CreateSession([scene]);

        target.ImageAsset = "asset_character_chen";
        target.VoiceAsset = "voices/chen.wav";
        target.VoiceReferenceText = "Chen reference text";
        BindingTargetWorkspaceApplier.Apply(target, [scene], session);

        var sessionRow = session.Document.RowsById[chenRow.InteractionId];
        Assert.Equal("asset_character_chen", chenRow.SelectedCharacterAsset);
        Assert.NotEqual("voices/chen.wav", chenRow.SelectedVoiceReference);
        Assert.NotEqual("Chen reference text", chenRow.SelectedVoiceReferenceText);
        Assert.Equal("asset_character_chen", RowVisualId(sessionRow));
        Assert.NotEqual("voices/chen.wav", RowVoiceId(sessionRow));
        Assert.NotEqual("Chen reference text", RowVoiceText(sessionRow));
    }

    [Fact]
    public void ApplyingOnlyVoiceTextPreservesPsdRowVisualBinding()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var chenRow = AnalysisResultRowViewModel.FromDraft(new WorkbenchRowDraft(
            InteractionId: "interaction_chen",
            SceneId: scene.SceneId,
            Order: 1,
            Kind: WorkbenchRowKind.Dialogue,
            SubjectName: "Chen",
            SubjectCharacterId: "character_chen",
            Emotion: "Calm",
            Text: "Hello",
            EmotionVectors: null,
            EmotionAlpha: null,
            TaskLinks: new WorkbenchTaskLinks(null),
            CharacterVisualBinding: VisualAssetBinding.FromPsd(
                "asset_psd_chen",
                "asset_rendered_calm",
                new PsdRenderSelection(
                    [1],
                    ["Calm"],
                    null,
                    "Calm",
                    "Idle",
                    VisualExportBounds.CanvasBounds,
                    "fp",
                    "cache")),
            VoiceBinding: VoiceAssetBinding.FromProjectReference(
                "voices/chen.wav",
                "old reference",
                "asset_voice_source",
                @"C:\voices\chen.wav")),
            "asset_scene");
        scene.Rows.Add(chenRow);
        var target = BindingTargetViewModel.ForCharacter(CreateCharacter("character_chen", "Chen"), [scene]);
        var session = CreateSession([scene]);

        target.VoiceReferenceText = "new reference";
        BindingTargetWorkspaceApplier.Apply(target, [scene], session);

        var sessionRow = session.Document.RowsById[chenRow.InteractionId];
        Assert.Equal("asset_rendered_calm", RowVisualId(sessionRow));
        Assert.Equal(VisualSourceKind.Psd, sessionRow.CharacterVisualBinding?.SourceKind);
        Assert.Equal("asset_psd_chen", sessionRow.CharacterVisualBinding?.SourceAssetId);
        Assert.Equal("asset_rendered_calm", sessionRow.CharacterVisualBinding?.RenderedAssetId);
        Assert.Equal("old reference", RowVoiceText(sessionRow));
        Assert.Equal("asset_voice_source", sessionRow.VoiceBinding?.SourceAssetId);
        Assert.False(target.HasVoiceBindingEdit);
        Assert.False(target.HasImageBindingEdit);
    }

    [Fact]
    public void ApplyWithResultReportsNoOpForEquivalentBinding()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        scene.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen"));
        var target = BindingTargetViewModel.ForCharacter(CreateCharacter("character_chen", "Chen"), [scene]);
        var rowIndex = BindingTargetRowIndex.FromScenes([scene]);
        var session = CreateSession([scene]);
        BindingTargetWorkspaceApplier.ApplyWithResult(target, [scene], rowIndex, session);
        session.Reset(session.Document);

        var result = BindingTargetWorkspaceApplier.ApplyWithResult(target, [scene], rowIndex, session);

        Assert.False(result.CommandResult.Applied);
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.False(result.DerivedStateRefresh.RefreshCounters);
    }

    [Fact]
    public void ApplyWithResultReportsAppliedBindingWithoutCounterRefresh()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        scene.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen"));
        var target = BindingTargetViewModel.ForCharacter(CreateCharacter("character_chen", "Chen"), [scene]);
        var rowIndex = BindingTargetRowIndex.FromScenes([scene]);
        var session = CreateSession([scene]);

        target.ImageAsset = "asset_character_chen";
        var result = BindingTargetWorkspaceApplier.ApplyWithResult(target, [scene], rowIndex, session);

        Assert.True(result.CommandResult.Applied);
        Assert.True(session.IsDirty);
        Assert.True(session.CanUndo);
        Assert.False(result.DerivedStateRefresh.RefreshCounters);
    }

    [Fact]
    public void ApplySceneBindingWithResultReportsChangedScene()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var otherScene = SceneTableViewModel.CreateManual(2);
        var target = BindingTargetViewModel.ForScene(scene);
        var rowIndex = BindingTargetRowIndex.FromScenes([scene, otherScene]);
        var session = CreateSession([scene, otherScene]);

        target.ImageAsset = "asset_scene_changed";
        var result = BindingTargetWorkspaceApplier.ApplyWithResult(target, [scene, otherScene], rowIndex, session);

        Assert.True(result.CommandResult.Applied);
        Assert.Equal([scene.SceneId], result.CommandResult.ChangedSceneIds);
        Assert.Empty(result.CommandResult.ChangedRowIds);
    }

    [Fact]
    public void ApplyCharacterBindingWithResultReportsAffectedRowsAndScenes()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var otherScene = SceneTableViewModel.CreateManual(2);
        var firstRow = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var secondRow = AnalysisResultRowViewModel.CreateManualDialogue(2, "Chen");
        var otherRow = AnalysisResultRowViewModel.CreateManualDialogue(1, "Lin");
        scene.Rows.Add(firstRow);
        otherScene.Rows.Add(secondRow);
        otherScene.Rows.Add(otherRow);
        var target = BindingTargetViewModel.ForCharacter(CreateCharacter("character_chen", "Chen"), [scene, otherScene]);
        var rowIndex = BindingTargetRowIndex.FromScenes([scene, otherScene]);
        var session = CreateSession([scene, otherScene]);

        target.ImageAsset = "asset_character_changed";
        var result = BindingTargetWorkspaceApplier.ApplyWithResult(target, [scene, otherScene], rowIndex, session);

        Assert.True(result.CommandResult.Applied);
        Assert.Equal([scene.SceneId, otherScene.SceneId], result.CommandResult.ChangedSceneIds);
        Assert.Equal([firstRow.InteractionId, secondRow.InteractionId], result.CommandResult.ChangedRowIds);
    }

    [Fact]
    public void ApplyNarrationBindingWithResultReportsAffectedNarratorVoiceRows()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var narration = AnalysisResultRowViewModel.CreateManualNarration(1);
        var dialogue = AnalysisResultRowViewModel.CreateManualDialogue(2, "Chen");
        scene.Rows.Add(narration);
        scene.Rows.Add(dialogue);
        var target = BindingTargetViewModel.ForNarration([scene]);
        var rowIndex = BindingTargetRowIndex.FromScenes([scene]);
        var session = CreateSession([scene]);

        target.VoiceAsset = "voices/narration_changed.wav";
        var result = BindingTargetWorkspaceApplier.ApplyWithResult(target, [scene], rowIndex, session);

        Assert.True(result.CommandResult.Applied);
        Assert.Equal([scene.SceneId], result.CommandResult.ChangedSceneIds);
        Assert.Equal([narration.InteractionId, dialogue.InteractionId], result.CommandResult.ChangedRowIds);
    }

    [Fact]
    public void ApplyCharacterBindingAcrossMultipleRowsIsSingleUndoStep()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var firstRow = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var secondRow = AnalysisResultRowViewModel.CreateManualDialogue(2, "Chen");
        var otherRow = AnalysisResultRowViewModel.CreateManualDialogue(3, "Lin");
        scene.Rows.Add(firstRow);
        scene.Rows.Add(secondRow);
        scene.Rows.Add(otherRow);
        var target = BindingTargetViewModel.ForCharacter(CreateCharacter("character_chen", "Chen"), [scene]);
        var session = CreateSession([scene]);

        target.ImageAsset = "asset_character_chen";
        target.VoiceAsset = "voices/chen.wav";
        target.VoiceReferenceText = "Chen reference text";
        BindingTargetWorkspaceApplier.Apply(target, [scene], session);

        Assert.True(session.CanUndo);
        Assert.Equal(WorkbenchSessionOperationNames.ApplyBindingTarget, session.NextUndoOperationName);
        Assert.Equal("asset_character_chen", RowVisualId(session.Document.RowsById[firstRow.InteractionId]));
        Assert.Equal("asset_character_chen", RowVisualId(session.Document.RowsById[secondRow.InteractionId]));
        Assert.Null(RowVisualId(session.Document.RowsById[otherRow.InteractionId]));

        session.Undo();

        Assert.False(session.CanUndo);
        Assert.True(session.CanRedo);
        Assert.Null(RowVisualId(session.Document.RowsById[firstRow.InteractionId]));
        Assert.Null(RowVisualId(session.Document.RowsById[secondRow.InteractionId]));
        Assert.Null(RowVoiceId(session.Document.RowsById[firstRow.InteractionId]));
        Assert.Null(RowVoiceId(session.Document.RowsById[secondRow.InteractionId]));

        session.Redo();

        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.Equal("asset_character_chen", RowVisualId(session.Document.RowsById[firstRow.InteractionId]));
        Assert.Equal("asset_character_chen", RowVisualId(session.Document.RowsById[secondRow.InteractionId]));
        Assert.Null(RowVoiceId(session.Document.RowsById[firstRow.InteractionId]));
        Assert.Null(RowVoiceId(session.Document.RowsById[secondRow.InteractionId]));
    }

    [Fact]
    public void ApplyCharacterBindingUpdatesObjectPreviewWithoutDetachedTooltipNotifications()
    {
        var imagePath = CreateTempPng();
        var scene = SceneTableViewModel.CreateManual(1);
        var chenRow = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(chenRow);
        var target = BindingTargetViewModel.ForCharacter(CreateCharacter("character_chen", "Chen"), [scene]);
        var session = CreateSession([scene]);
        var notifications = new List<string>();
        chenRow.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                notifications.Add(args.PropertyName);
            }
        };

        target.ImageAsset = imagePath;
        target.VoiceAsset = "voices/chen.wav";
        BindingTargetWorkspaceApplier.Apply(target, [scene], session);

        Assert.Equal(imagePath, chenRow.ObjectPreviewAsset);
        Assert.Equal(imagePath, chenRow.ObjectPreviewSource);
        Assert.Equal(Path.GetFileName(imagePath), chenRow.ObjectPreviewLabel);
        Assert.True(chenRow.HasObjectPreviewImage);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.ObjectPreviewAsset), notifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.ObjectPreviewSource), notifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.ObjectPreviewLabel), notifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.HasObjectPreviewImage), notifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.HasNoObjectPreviewImage), notifications);
    }

    [Fact]
    public void CharacterTargetDoesNotTreatLegacyCharacterVoiceAsReferenceBinding()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        scene.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen"));
        var character = new Character(
            "character_chen",
            "Chen",
            "role",
            "appearance",
            "voice",
            "calm",
            new AssetBinding("asset_character_chen", "voices/chen.wav"));

        var target = BindingTargetViewModel.ForCharacter(character, [scene]);

        Assert.Equal("asset_character_chen", target.ImageAsset);
        Assert.Equal("未绑定声音", target.VoiceAsset);
    }

    [Fact]
    public void ApplyNarrationBindingUpdatesNarratorVoiceRows()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var dialogue = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var narration = AnalysisResultRowViewModel.CreateManualNarration(2);
        scene.Rows.Add(dialogue);
        scene.Rows.Add(narration);
        var target = BindingTargetViewModel.ForNarration([scene]);
        var session = CreateSession([scene]);

        target.VoiceAsset = "voices/narrator.wav";
        target.VoiceReferenceText = "Narrator reference text";
        BindingTargetWorkspaceApplier.Apply(target, [scene], session);

        Assert.Equal("voices/narrator.wav", narration.SelectedVoiceReference);
        Assert.Equal("Narrator reference text", narration.SelectedVoiceReferenceText);
        Assert.Equal("voices/narrator.wav", narration.VoiceAssetText);
        Assert.Equal("voices/narrator.wav", dialogue.SelectedVoiceReference);
        Assert.Equal("Narrator reference text", dialogue.SelectedVoiceReferenceText);
    }

    [Fact]
    public void RowsForTargetReturnsRowsAffectedByVoiceRegeneration()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var chenRow = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var narration = AnalysisResultRowViewModel.CreateManualNarration(2);
        scene.Rows.Add(chenRow);
        scene.Rows.Add(narration);
        var characterTarget = BindingTargetViewModel.ForCharacter(CreateCharacter("character_chen", "Chen"), [scene]);
        var narrationTarget = BindingTargetViewModel.ForNarration([scene]);

        Assert.Same(chenRow, BindingTargetWorkspaceApplier.RowsForTarget(characterTarget, [scene]).Single());
        Assert.Equal([chenRow, narration], BindingTargetWorkspaceApplier.RowsForTarget(narrationTarget, [scene]));
    }

    [Fact]
    public void RowsForTargetCountsRowsMatchingCharacterIdAndNameOnce()
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
                new WorkbenchTaskLinks(null)),
            null);
        scene.Rows.Add(row);
        var target = BindingTargetViewModel.ForCharacter(CreateCharacter("character_chen", "Chen"), [scene]);

        var rows = BindingTargetWorkspaceApplier.RowsForTarget(target, [scene]).ToArray();

        Assert.Single(rows);
        Assert.Same(row, rows[0]);
    }

    [Fact]
    public void RowsForTargetIncludesSourceRoleCharacterRowsOnce()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.FromDraft(
            new WorkbenchRowDraft(
                "interaction_chen_thought",
                scene.SceneId,
                1,
                WorkbenchRowKind.Narration,
                "Chen",
                null,
                "Calm",
                "Inner line",
                null,
                null,
                new WorkbenchTaskLinks(null),
                SourceItemType: "thought",
                SourceRole: "Chen",
                SourceRoleCharacterId: "character_chen"),
            null);
        scene.Rows.Add(row);
        var target = BindingTargetViewModel.ForCharacter(CreateCharacter("character_chen", "Chen"), [scene]);

        var rows = BindingTargetWorkspaceApplier.RowsForTarget(target, [scene]).ToArray();

        Assert.Single(rows);
        Assert.Same(row, rows[0]);
    }

    [Fact]
    public void ApplyUsesProvidedRowIndexForCharacterTargets()
    {
        var imagePath = CreateTempPng();
        var scene = SceneTableViewModel.CreateManual(1);
        var chenRow = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var linRow = AnalysisResultRowViewModel.CreateManualDialogue(2, "Lin");
        scene.Rows.Add(chenRow);
        var rowIndex = BindingTargetRowIndex.FromScenes([scene]);
        scene.Rows.Add(linRow);
        var target = BindingTargetViewModel.ForCharacter(CreateCharacter("character_chen", "Chen"), [scene]);
        var session = CreateSession([scene]);

        target.ImageAsset = imagePath;
        BindingTargetWorkspaceApplier.Apply(target, [scene], rowIndex, session);

        Assert.Equal(imagePath, chenRow.SelectedCharacterAsset);
        Assert.NotEqual(imagePath, linRow.SelectedCharacterAsset);
    }

    private static Character CreateCharacter(string id, string name)
    {
        return new Character(
            id,
            name,
            "role",
            "appearance",
            "voice",
            "calm",
            new AssetBinding());
    }

    private static string CreateTempPng()
    {
        var path = Path.Combine(Path.GetTempPath(), $"draft-agent-binding-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, [137, 80, 78, 71]);
        return path;
    }

    private static WorkbenchSession CreateSession(IReadOnlyList<SceneTableViewModel> scenes)
    {
        return new WorkbenchSession(WorkbenchViewModelAdapter.BuildDocument(scenes));
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
}



