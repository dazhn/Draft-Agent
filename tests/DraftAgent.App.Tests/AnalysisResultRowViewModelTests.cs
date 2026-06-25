using System.Reflection;
using DraftAgent.App.ViewModels;
using DraftAgent.Core;
using SkiaSharp;

namespace DraftAgent.App.Tests;

public sealed class AnalysisResultRowViewModelTests
{
    [Theory]
    [InlineData("dialogue", "词", "对白台词")]
    [InlineData("action", "动", "动作反应")]
    [InlineData("thought", "念", "内心独白")]
    [InlineData("effects", "场", "环境场效")]
    [InlineData("narration", "旁", "旁白叙述")]
    public void SourceTypeGlyphReflectsOriginalJsonItemType(string sourceType, string glyph, string text)
    {
        var row = AnalysisResultRowViewModel.FromDraft(
            Row("int_001", WorkbenchRowKind.Narration) with
            {
                SourceItemType = sourceType,
                SourceRole = sourceType is "effects" or "narration" ? string.Empty : "Chen"
            },
            "scene.png");

        Assert.Equal(glyph, row.SourceTypeGlyph);
        Assert.Equal(text, row.SourceTypeText);
    }

    [Fact]
    public void EffectsAndStoryNarrationRowsUseSpecialSubjectNames()
    {
        var effects = AnalysisResultRowViewModel.FromDraft(
            Row("int_001", WorkbenchRowKind.Narration) with { SourceItemType = "effects" },
            "scene.png");
        var narration = AnalysisResultRowViewModel.FromDraft(
            Row("int_002", WorkbenchRowKind.Narration) with { SourceItemType = "narration" },
            "scene.png");

        Assert.Equal("环境场效", effects.SubjectName);
        Assert.Equal("旁白叙述", narration.SubjectName);
    }

    [Theory]
    [InlineData("action", true, "Chen", true, false)]
    [InlineData("thought", true, "Chen", true, false)]
    [InlineData("effects", false, "环境场效", true, false)]
    [InlineData("narration", false, "旁白叙述", true, false)]
    public void SourceItemTypeControlsVisualPreviewSeparatelyFromVoiceNarrationKind(
        string sourceType,
        bool usesCharacterVisualPreview,
        string expectedSubjectName,
        bool expectsCharacterPreview,
        bool expectsScenePreview)
    {
        var characterImage = CreateTempPng();
        var sceneImage = CreateTempPng();
        var row = AnalysisResultRowViewModel.FromDraft(
            Row("int_visual", WorkbenchRowKind.Narration) with
            {
                SourceItemType = sourceType,
                SourceRole = sourceType is "action" or "thought" ? "Chen" : string.Empty,
                SourceRoleCharacterId = sourceType is "action" or "thought" ? "char_chen" : null,
                SubjectName = sourceType is "action" or "thought" ? "Chen" : "旁白",
                CharacterVisualBinding = VisualAssetBinding.FromImage(characterImage)
            },
            sceneImage);

        Assert.True(row.IsNarration);
        Assert.Equal(usesCharacterVisualPreview, row.UsesCharacterVisualPreview);
        Assert.Equal(expectedSubjectName, row.SubjectName);
        Assert.Equal(
            usesCharacterVisualPreview ? characterImage : characterImage,
            row.SelectedCharacterAsset);
        var expectedObjectPreview = expectsCharacterPreview
            ? characterImage
            : expectsScenePreview
                ? sceneImage
                : string.Empty;
        Assert.Equal(expectedObjectPreview, row.ObjectPreviewSource);
        Assert.Equal(
            usesCharacterVisualPreview ? "C" : sourceType == "effects" ? "场" : "旁",
            row.ObjectPreviewInitial);
    }

    [Fact]
    public void ApplyingActionSourceMetadataSwitchesNarrationVoiceRowToCharacterVisualPreview()
    {
        var characterImage = CreateTempPng();
        var sceneImage = CreateTempPng();
        var row = AnalysisResultRowViewModel.FromDraft(
            Row("int_action_visual", WorkbenchRowKind.Narration) with
            {
                SourceItemType = "effects",
                CharacterVisualBinding = VisualAssetBinding.FromImage(characterImage)
            },
            sceneImage);
        row.SubjectName = "Chen";
        row.SelectedCharacterAsset = characterImage;
        var notifications = new List<string?>();
        row.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        row.MarkAppliedDraft(
            Row("int_action_visual", WorkbenchRowKind.Narration) with
            {
                SourceItemType = "action",
                SourceRole = "Chen",
                SourceRoleCharacterId = "char_chen",
                SubjectName = "Chen",
                CharacterVisualBinding = VisualAssetBinding.FromImage(characterImage)
            });

        Assert.True(row.UsesCharacterVisualPreview);
        Assert.Equal(characterImage, row.ObjectPreviewSource);
        Assert.Equal("C", row.ObjectPreviewInitial);
        Assert.Contains(nameof(AnalysisResultRowViewModel.UsesCharacterVisualPreview), notifications);
        Assert.Contains(nameof(AnalysisResultRowViewModel.InlineObjectPreviewSource), notifications);
    }

    [Fact]
    public void CharacterSubjectCanSwitchSpecialNarrationDraftBackToDialogueDisplay()
    {
        var row = AnalysisResultRowViewModel.FromDraft(
            Row("int_001", WorkbenchRowKind.Dialogue) with
            {
                SubjectName = "Chen",
                SubjectCharacterId = "char_chen",
                SourceItemType = "dialogue",
                SourceRole = "Chen",
                SourceRoleCharacterId = "char_chen"
            },
            "scene.png");

        Assert.False(row.IsNarration);
        Assert.Equal("词", row.SourceTypeGlyph);
        Assert.Equal("Chen", row.SubjectName);
    }

    [Fact]
    public void EmotionVectorMapReusesCachedInstanceUntilVectorValueChanges()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");

        var first = row.EmotionVectorMap;
        var second = row.EmotionVectorMap;

        Assert.Same(first, second);

        row.EmotionVectors.Single(vector => vector.Key == "Afraid").Value = 0.9;
        var updated = row.EmotionVectorMap;

        Assert.NotSame(first, updated);
        Assert.Equal(0.9, updated["Afraid"]);
        Assert.Same(updated, row.EmotionVectorMap);
    }

    [Fact]
    public void EmotionVectorMapInvalidatesWhenVectorCollectionChanges()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var first = row.EmotionVectorMap;

        var calm = row.EmotionVectors.Single(vector => vector.Key == "Calm");
        row.EmotionVectors.Remove(calm);
        var removed = row.EmotionVectorMap;

        Assert.NotSame(first, removed);
        Assert.False(removed.ContainsKey("Calm"));

        row.EmotionVectors.Add(new EmotionVectorViewModel("Calm", 0.4));
        var added = row.EmotionVectorMap;

        Assert.NotSame(removed, added);
        Assert.Equal(0.4, added["Calm"]);
    }

    [Fact]
    public void EffectiveEmotionDisplayTextReusesCachedInstanceUntilEmotionChanges()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        row.EmotionVectors.Single(vector => vector.Key == "Calm").Value = 0;
        row.Emotion = "Sad 1.1";

        var first = row.EffectiveEmotionDisplayText;
        var second = row.EffectiveEmotionDisplayText;

        Assert.Same(first, second);
        Assert.Equal("悲伤 1.1", second);

        row.Emotion = "Happy 0.8";
        var updated = row.EffectiveEmotionDisplayText;

        Assert.NotSame(first, updated);
        Assert.Equal("开心 0.8", updated);
        Assert.Same(updated, row.EffectiveEmotionDisplayText);
    }

    [Fact]
    public void EffectiveEmotionTextCachesInvalidateWhenVectorValueChanges()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var firstText = row.EffectiveEmotionText;
        var firstDisplayText = row.EffectiveEmotionDisplayText;

        row.EmotionVectors.Single(vector => vector.Key == "Happy").Value = 0.8;

        var updatedText = row.EffectiveEmotionText;
        var updatedDisplayText = row.EffectiveEmotionDisplayText;

        Assert.NotSame(firstText, updatedText);
        Assert.NotSame(firstDisplayText, updatedDisplayText);
        Assert.Equal("Happy 0.8", updatedText);
        Assert.Equal($"开心 0.8{Environment.NewLine}平静 0.6", updatedDisplayText);
        Assert.Same(updatedText, row.EffectiveEmotionText);
        Assert.Same(updatedDisplayText, row.EffectiveEmotionDisplayText);
    }

    [Fact]
    public void ObjectPreviewInitialReusesCachedInstanceUntilSubjectChanges()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");

        var first = row.ObjectPreviewInitial;
        var second = row.ObjectPreviewInitial;

        Assert.Same(first, second);
        Assert.Equal("C", second);

        row.SubjectName = "Alice";
        var updated = row.ObjectPreviewInitial;

        Assert.NotSame(first, updated);
        Assert.Equal("A", updated);
        Assert.Same(updated, row.ObjectPreviewInitial);
    }

    [Fact]
    public void ObjectPreviewInitialInvalidatesWhenSubjectSwitchesToNarration()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var first = row.ObjectPreviewInitial;

        row.SubjectName = "旁白";
        var narrationInitial = row.ObjectPreviewInitial;

        Assert.NotSame(first, narrationInitial);
        Assert.Equal("旁", narrationInitial);
        Assert.Same(narrationInitial, row.ObjectPreviewInitial);
    }

    [Fact]
    public void CanRegenerateVoiceUpdatesWhenLineTextEmptinessChanges()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var notifications = new List<string>();
        row.PropertyChanged += (_, args) => AddNotification(notifications, args.PropertyName);

        row.LineText = "   ";

        Assert.False(row.CanRegenerateVoice);
        Assert.Contains(nameof(AnalysisResultRowViewModel.CanRegenerateVoice), notifications);

        notifications.Clear();
        row.LineText = "新的台词";

        Assert.True(row.CanRegenerateVoice);
        Assert.Contains(nameof(AnalysisResultRowViewModel.CanRegenerateVoice), notifications);
    }

    [Fact]
    public void CanRegenerateVoiceDoesNotNotifyWhenLineTextRemainsRegeneratable()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var notifications = new List<string>();
        row.PropertyChanged += (_, args) => AddNotification(notifications, args.PropertyName);

        row.LineText = "第一句";
        row.LineText = "第二句";

        Assert.True(row.CanRegenerateVoice);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.CanRegenerateVoice), notifications);
    }

    [Fact]
    public void EmotionVectorChangeDoesNotNotifyInternalEffectiveEmotionText()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var notifications = new List<string>();
        row.PropertyChanged += (_, args) => AddNotification(notifications, args.PropertyName);

        row.EmotionVectors.Single(vector => vector.Key == "Happy").Value = 0.8;

        Assert.Equal("Happy 0.8", row.EffectiveEmotionText);
        Assert.Equal($"开心 0.8{Environment.NewLine}平静 0.6", row.EffectiveEmotionDisplayText);
        Assert.Contains(nameof(AnalysisResultRowViewModel.EmotionVectorMap), notifications);
        Assert.Contains(nameof(AnalysisResultRowViewModel.EffectiveEmotionDisplayText), notifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.EffectiveEmotionText), notifications);
    }

    [Fact]
    public void EffectiveEmotionDisplayTextShowsTwoStrongestEmotionVectors()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        row.EmotionVectors.Single(vector => vector.Key == "Calm").Value = 0;
        row.EmotionVectors.Single(vector => vector.Key == "Happy").Value = 0.8;
        row.EmotionVectors.Single(vector => vector.Key == "Afraid").Value = 0.9;
        row.EmotionVectors.Single(vector => vector.Key == "Sad").Value = 0.2;

        Assert.Equal($"害怕 0.9{Environment.NewLine}开心 0.8", row.EffectiveEmotionDisplayText);
        Assert.Equal("Afraid 0.9", row.EffectiveEmotionText);
        Assert.DoesNotContain("悲伤", row.EffectiveEmotionDisplayText, StringComparison.Ordinal);
    }

    [Fact]
    public void EmotionEditorSessionSavesOnlyWhenConfirmedAndOrdersOppositePairs()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        row.EmotionVectors.Single(vector => vector.Key == "Calm").Value = 0.6;
        row.EmotionVectors.Single(vector => vector.Key == "Happy").Value = 0.2;
        var session = new EmotionEditorSessionViewModel(row);

        Assert.Equal(
            ["Happy", "Sad", "Angry", "Calm", "Surprised", "Melancholic", "Afraid", "Disgusted"],
            session.EmotionVectors.Select(vector => vector.Key).ToArray());

        session.EmotionVectors.Single(vector => vector.Key == "Happy").Value = 1.1;

        Assert.Equal(0.2, row.EmotionVectors.Single(vector => vector.Key == "Happy").Value);

        session.Save();

        Assert.Equal(1.1, row.EmotionVectors.Single(vector => vector.Key == "Happy").Value);
    }

    [Fact]
    public void EmotionTextChangeDoesNotNotifyInternalEffectiveEmotionText()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        row.EmotionVectors.Single(vector => vector.Key == "Calm").Value = 0;
        var notifications = new List<string>();
        row.PropertyChanged += (_, args) => AddNotification(notifications, args.PropertyName);

        row.Emotion = "Sad 1.1";

        Assert.Equal("Sad 1.1", row.EffectiveEmotionText);
        Assert.Equal("悲伤 1.1", row.EffectiveEmotionDisplayText);
        Assert.Contains(nameof(AnalysisResultRowViewModel.EffectiveEmotionDisplayText), notifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.EffectiveEmotionText), notifications);
    }

    [Fact]
    public void CharacterPreviewSnapshotInvalidatesWhenPreviewPathChanges()
    {
        var firstImage = CreateTempPng();
        var secondImage = CreateTempPng();
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");

        row.SelectedCharacterPreviewAsset = firstImage;

        Assert.True(row.HasSelectedCharacterImage);
        Assert.Equal(Path.GetFileName(firstImage), row.SelectedCharacterAssetLabel);
        Assert.Equal(Path.GetFileName(firstImage), row.ObjectPreviewLabel);

        row.SelectedCharacterPreviewAsset = secondImage;

        Assert.True(row.HasSelectedCharacterImage);
        Assert.Equal(Path.GetFileName(secondImage), row.SelectedCharacterAssetLabel);
        Assert.Equal(Path.GetFileName(secondImage), row.ObjectPreviewLabel);
    }

    [Fact]
    public void InlineObjectPreviewUsesCompactSquareSize()
    {
        var image = CreateSizedPng(width: 48, height: 24);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        row.IsSelected = true;

        row.SelectedCharacterPreviewAsset = image;

        Assert.True(row.HasInlineObjectPreviewImage);
        Assert.Equal(24, row.InlineObjectPreviewWidth);
        Assert.Equal(24, row.InlineObjectPreviewHeight);
    }

    [Fact]
    public void CharacterPreviewSnapshotInvalidatesWhenFallbackAssetChanges()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");

        row.SelectedCharacterAsset = "asset_character_a";
        Assert.False(row.HasSelectedCharacterImage);
        Assert.Equal("asset_character_a", row.SelectedCharacterAssetLabel);
        Assert.Equal("asset_character_a", row.ObjectPreviewLabel);

        row.SelectedCharacterAsset = "asset_character_b";

        Assert.False(row.HasSelectedCharacterImage);
        Assert.Equal("asset_character_b", row.SelectedCharacterAssetLabel);
        Assert.Equal("asset_character_b", row.ObjectPreviewLabel);
    }

    [Fact]
    public void ApplyResolvedPreviewAssetsSkipsNotificationsWhenValuesAreUnchanged()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var notifications = new List<string>();
        row.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                notifications.Add(args.PropertyName);
            }
        };

        var changes = row.ApplyResolvedPreviewAssets(row.SelectedCharacterPreviewAsset, row.PreviewAssetPath);

        Assert.False(changes.Any);
        Assert.Empty(notifications);
    }

    [Fact]
    public void ApplyResolvedPreviewAssetsRefreshesObjectPreviewWhenValuesChange()
    {
        var imagePath = CreateTempPng();
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var notifications = new List<string>();
        row.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                notifications.Add(args.PropertyName);
            }
        };

        var changes = row.ApplyResolvedPreviewAssets(imagePath, row.PreviewAssetPath);

        Assert.True(changes.Any);
        Assert.True(changes.CharacterPreviewChanged);
        Assert.False(changes.ScenePreviewChanged);
        Assert.Equal(imagePath, row.SelectedCharacterPreviewAsset);
        Assert.True(row.HasObjectPreviewImage);
        Assert.Contains(nameof(AnalysisResultRowViewModel.SelectedCharacterPreviewAsset), notifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.HasSelectedCharacterImage), notifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.HasNoSelectedCharacterImage), notifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.SelectedCharacterAssetLabel), notifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.ObjectPreviewAsset), notifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.ObjectPreviewLabel), notifications);
    }

    [Fact]
    public void InlineObjectPreviewShowsImageForUnselectedRows()
    {
        var imagePath = CreateTempPng();
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var notifications = new List<string>();
        row.PropertyChanged += (_, args) => AddNotification(notifications, args.PropertyName);

        row.ApplyResolvedPreviewAssets(imagePath, row.PreviewAssetPath);

        Assert.True(row.HasObjectPreviewImage);
        Assert.Equal(imagePath, row.ObjectPreviewSource);
        Assert.True(row.HasInlineObjectPreviewImage);
        Assert.False(row.HasNoInlineObjectPreviewImage);
        Assert.Equal(imagePath, row.InlineObjectPreviewSource);
        Assert.Equal(Path.GetFileName(imagePath), row.InlineObjectPreviewLabel);
        Assert.Same(row, row.InlineObjectPreviewContext);
        Assert.Contains(nameof(AnalysisResultRowViewModel.InlineObjectPreviewSource), notifications);
        Assert.Contains(nameof(AnalysisResultRowViewModel.InlineObjectPreviewLabel), notifications);
        Assert.Contains(nameof(AnalysisResultRowViewModel.HasInlineObjectPreviewImage), notifications);
        Assert.Contains(nameof(AnalysisResultRowViewModel.HasNoInlineObjectPreviewImage), notifications);
        Assert.Contains(nameof(AnalysisResultRowViewModel.InlineObjectPreviewContext), notifications);

        notifications.Clear();
        row.IsSelected = true;

        Assert.True(row.HasInlineObjectPreviewImage);
        Assert.False(row.HasNoInlineObjectPreviewImage);
        Assert.Equal(imagePath, row.InlineObjectPreviewSource);
        Assert.Equal(Path.GetFileName(imagePath), row.InlineObjectPreviewLabel);
        Assert.Same(row, row.InlineObjectPreviewContext);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.InlineObjectPreviewSource), notifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.InlineObjectPreviewLabel), notifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.HasInlineObjectPreviewImage), notifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.HasNoInlineObjectPreviewImage), notifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.InlineObjectPreviewContext), notifications);

        notifications.Clear();
        row.IsSelected = false;

        Assert.True(row.HasInlineObjectPreviewImage);
        Assert.False(row.HasNoInlineObjectPreviewImage);
        Assert.Equal(imagePath, row.InlineObjectPreviewSource);
        Assert.Equal(Path.GetFileName(imagePath), row.InlineObjectPreviewLabel);
        Assert.Same(row, row.InlineObjectPreviewContext);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.InlineObjectPreviewSource), notifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.InlineObjectPreviewLabel), notifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.InlineObjectPreviewContext), notifications);
    }

    [Fact]
    public void SelectingRowWithoutPreviewImageSkipsUnchangedInlinePreviewNotifications()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var notifications = new List<string>();
        row.PropertyChanged += (_, args) => AddNotification(notifications, args.PropertyName);

        row.IsSelected = true;

        Assert.Equal(string.Empty, row.InlineObjectPreviewSource);
        Assert.False(row.HasInlineObjectPreviewImage);
        Assert.True(row.HasNoInlineObjectPreviewImage);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.InlineObjectPreviewSource), notifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.HasInlineObjectPreviewImage), notifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.HasNoInlineObjectPreviewImage), notifications);

        notifications.Clear();
        row.IsSelected = false;

        Assert.Equal(string.Empty, row.InlineObjectPreviewSource);
        Assert.False(row.HasInlineObjectPreviewImage);
        Assert.True(row.HasNoInlineObjectPreviewImage);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.InlineObjectPreviewSource), notifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.HasInlineObjectPreviewImage), notifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.HasNoInlineObjectPreviewImage), notifications);
    }

    [Fact]
    public void UnselectedPreviewAssetRefreshNotifiesInlineObjectPreview()
    {
        var imagePath = CreateTempPng();
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var notifications = new List<string>();
        row.PropertyChanged += (_, args) => AddNotification(notifications, args.PropertyName);

        row.ApplyResolvedPreviewAssets(imagePath, row.PreviewAssetPath);

        Assert.False(row.IsSelected);
        Assert.Equal(imagePath, row.InlineObjectPreviewSource);
        Assert.Equal(Path.GetFileName(imagePath), row.InlineObjectPreviewLabel);
        Assert.True(row.HasInlineObjectPreviewImage);
        Assert.False(row.HasNoInlineObjectPreviewImage);
        Assert.Contains(nameof(AnalysisResultRowViewModel.InlineObjectPreviewSource), notifications);
        Assert.Contains(nameof(AnalysisResultRowViewModel.InlineObjectPreviewLabel), notifications);
        Assert.Contains(nameof(AnalysisResultRowViewModel.HasInlineObjectPreviewImage), notifications);
        Assert.Contains(nameof(AnalysisResultRowViewModel.HasNoInlineObjectPreviewImage), notifications);
    }

    [Fact]
    public void UnselectedPreviewAssetRefreshCapturesInlineObjectPreviewSnapshot()
    {
        var imagePath = CreateTempPng();
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");

        row.ApplyResolvedPreviewAssets(imagePath, row.PreviewAssetPath);

        Assert.False(row.IsSelected);
        Assert.Equal(imagePath, row.InlineObjectPreviewSource);
        Assert.Equal(Path.GetFileName(imagePath), row.InlineObjectPreviewLabel);
        Assert.NotNull(GetSelectedCharacterPreviewSnapshot(row));

        row.IsSelected = true;

        Assert.Equal(imagePath, row.InlineObjectPreviewSource);
        Assert.Equal(Path.GetFileName(imagePath), row.InlineObjectPreviewLabel);
        Assert.NotNull(GetSelectedCharacterPreviewSnapshot(row));
    }

    [Fact]
    public void SelectedPreviewAssetRefreshNotifiesInlineObjectPreview()
    {
        var imagePath = CreateTempPng();
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        row.IsSelected = true;
        var notifications = new List<string>();
        row.PropertyChanged += (_, args) => AddNotification(notifications, args.PropertyName);

        row.ApplyResolvedPreviewAssets(imagePath, row.PreviewAssetPath);

        Assert.Equal(imagePath, row.InlineObjectPreviewSource);
        Assert.Equal(Path.GetFileName(imagePath), row.InlineObjectPreviewLabel);
        Assert.True(row.HasInlineObjectPreviewImage);
        Assert.False(row.HasNoInlineObjectPreviewImage);
        Assert.Same(row, row.InlineObjectPreviewContext);
        Assert.Contains(nameof(AnalysisResultRowViewModel.InlineObjectPreviewSource), notifications);
        Assert.Contains(nameof(AnalysisResultRowViewModel.InlineObjectPreviewLabel), notifications);
        Assert.Contains(nameof(AnalysisResultRowViewModel.HasInlineObjectPreviewImage), notifications);
        Assert.Contains(nameof(AnalysisResultRowViewModel.HasNoInlineObjectPreviewImage), notifications);
        Assert.Contains(nameof(AnalysisResultRowViewModel.InlineObjectPreviewContext), notifications);
    }

    [Fact]
    public void SelectingRowRefreshesPreviewVoiceAvailabilityBinding()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var notifications = new List<string>();
        row.PropertyChanged += (_, args) => AddNotification(notifications, args.PropertyName);

        row.IsSelected = true;

        Assert.Contains(nameof(AnalysisResultRowViewModel.CanPreviewVoice), notifications);
    }

    [Fact]
    public void UnselectingRowDoesNotRefreshUnchangedTtsStatusBinding()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        row.IsSelected = true;
        var notifications = new List<string>();
        row.PropertyChanged += (_, args) => AddNotification(notifications, args.PropertyName);

        row.IsSelected = false;

        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.TtsStatusText), notifications);
        Assert.Contains(nameof(AnalysisResultRowViewModel.CanPreviewVoice), notifications);
    }

    [Fact]
    public void RefreshTtsDetailTextBindingRaisesTooltipTextNotification()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var notifications = new List<string>();
        row.PropertyChanged += (_, args) => AddNotification(notifications, args.PropertyName);

        row.RefreshTtsDetailTextBinding();

        Assert.Contains(nameof(AnalysisResultRowViewModel.TtsDetailText), notifications);
    }

    [Fact]
    public void ReapplyingSameProjectDerivedStateDoesNotNotifyTtsBindings()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var interaction = CreateDialogueInteractionWithTtsTask(
            GenerationTaskStatus.Completed,
            "audio_project_state.wav");
        ApplyProjectDerivedState(row, interaction);
        var notifications = new List<string>();
        row.PropertyChanged += (_, args) => AddNotification(notifications, args.PropertyName);

        ApplyProjectDerivedState(row, interaction);

        Assert.Equal(string.Empty, row.TtsStatusText);
        Assert.Equal("audio_project_state.wav", row.TtsDetailText);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.TtsStatusText), notifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.TtsDetailText), notifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.CanPreviewVoice), notifications);
    }

    [Fact]
    public void ReapplyingProjectDerivedStateWithInvisibleTaskParameterChangeDoesNotNotifyTtsBindings()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        ApplyProjectDerivedState(row, CreateDialogueInteractionWithTtsTask(
            GenerationTaskStatus.Completed,
            "audio_project_state.wav",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["internalTrace"] = "first"
            }));
        var notifications = new List<string>();
        row.PropertyChanged += (_, args) => AddNotification(notifications, args.PropertyName);

        ApplyProjectDerivedState(row, CreateDialogueInteractionWithTtsTask(
            GenerationTaskStatus.Completed,
            "audio_project_state.wav",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["internalTrace"] = "second"
            }));

        Assert.Equal("audio_project_state.wav", row.TtsDetailText);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.TtsStatusText), notifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.TtsDetailText), notifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.CanPreviewVoice), notifications);
    }

    [Fact]
    public void ProjectDerivedStateUsesCompletedResultAssetId()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var interaction = CreateDialogueInteractionWithTtsTask(
            GenerationTaskStatus.Completed,
            "asset_tts_imported");

        ApplyProjectDerivedState(row, interaction);

        Assert.Equal("asset_tts_imported", row.SynthesizedAudioAsset);
        Assert.Equal("asset_tts_imported", row.TtsDetailText);
    }

    [Fact]
    public async Task ProjectDerivedStateShowsTwoDigitVoiceDurationForCompletedWav()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var audioPath = await CreateWavFileAsync(2.4);
        var interaction = CreateDialogueInteractionWithTtsTask(
            GenerationTaskStatus.Completed,
            audioPath);

        ApplyProjectDerivedState(row, interaction);

        Assert.Equal("02s", row.VoiceDurationText);
    }

    [Fact]
    public void ProjectDerivedStateSnapshotStillUpdatesWhenTtsTaskChanges()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        ApplyProjectDerivedState(row, CreateDialogueInteractionWithTtsTask(
            GenerationTaskStatus.Draft,
            resultAssetId: null));
        var notifications = new List<string>();
        row.PropertyChanged += (_, args) => AddNotification(notifications, args.PropertyName);

        ApplyProjectDerivedState(row, CreateDialogueInteractionWithTtsTask(
            GenerationTaskStatus.Completed,
            "audio_project_state_completed.wav"));

        Assert.Equal(GenerationTaskStatus.Completed, row.TtsStatus);
        Assert.Equal(string.Empty, row.TtsStatusText);
        Assert.Equal("audio_project_state_completed.wav", row.TtsDetailText);
        Assert.True(row.HasSynthesizedVoice);
        Assert.Contains(nameof(AnalysisResultRowViewModel.TtsStatusText), notifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.TtsDetailText), notifications);
    }

    [Fact]
    public void ScenePreviewSnapshotDoesNotDriveNarrationObjectPreview()
    {
        var firstImage = CreateTempPng();
        var secondImage = CreateTempPng();
        var row = AnalysisResultRowViewModel.CreateManualNarration(1);

        row.PreviewAssetPath = firstImage;

        Assert.True(row.HasPreviewImage);
        Assert.False(row.HasObjectPreviewImage);
        Assert.Equal(Path.GetFileName(firstImage), row.PreviewAssetLabel);
        Assert.Equal("未绑定行图", row.ObjectPreviewLabel);

        row.PreviewAssetPath = secondImage;

        Assert.True(row.HasPreviewImage);
        Assert.False(row.HasObjectPreviewImage);
        Assert.Equal(Path.GetFileName(secondImage), row.PreviewAssetLabel);
        Assert.Equal("未绑定行图", row.ObjectPreviewLabel);
    }


    [Fact]
    public void InteractionRowBuildContextUsesFirstCharacterNameForDuplicateIds()
    {
        var project = new InteractionProject(
            "project_001",
            "Demo",
            new ScriptDocument("raw", "clean", []),
            [
                new Character("char_chen", "陈默", "主角", "", "", "", new AssetBinding()),
                new Character("char_chen", "后来的名字", "主角", "", "", "", new AssetBinding())
            ],
            [],
            [],
            []);
        var context = AnalysisResultRowViewModel.InteractionRowBuildContext.FromProject(project);

        Assert.Equal("陈默", context.CharacterName("char_chen"));
        Assert.Equal("char_missing", context.CharacterName("char_missing"));
        Assert.Equal("未识别人物", context.CharacterName(null));
    }

    private static string CreateTempPng()
    {
        var path = Path.Combine(Path.GetTempPath(), $"draft-agent-row-preview-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, [137, 80, 78, 71]);
        return path;
    }

    private static string CreateSizedPng(int width, int height)
    {
        var path = Path.Combine(Path.GetTempPath(), $"draft-agent-row-preview-sized-{Guid.NewGuid():N}.png");
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(path);
        data.SaveTo(stream);
        return path;
    }

    private static async Task<string> CreateWavFileAsync(double durationSeconds)
    {
        var path = Path.Combine(Path.GetTempPath(), $"draft-agent-row-voice-{Guid.NewGuid():N}.wav");
        const int sampleRate = 48000;
        const short channels = 1;
        const short bitsPerSample = 16;
        var byteRate = sampleRate * channels * bitsPerSample / 8;
        var blockAlign = (short)(channels * bitsPerSample / 8);
        var dataSize = (uint)Math.Round(durationSeconds * byteRate);
        await using var stream = File.Create(path);
        await using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36u + dataSize);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16u);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]);
        return path;
    }

    private static void ApplyProjectDerivedState(
        AnalysisResultRowViewModel row,
        CharacterInteraction interaction,
        IReadOnlyList<GenerationTask>? rootTasks = null)
    {
        var project = new InteractionProject(
            "project_state",
            "Project State",
            new ScriptDocument("raw", "clean", []),
            [new Character("char_chen", "Chen", "角色", string.Empty, string.Empty, "Calm", new AssetBinding())],
            [],
            rootTasks ?? [],
            []);
        row.ApplyProjectDerivedState(interaction, AnalysisResultRowViewModel.InteractionRowBuildContext.FromProject(project));
    }

    private static CharacterInteraction CreateDialogueInteractionWithTtsTask(
        GenerationTaskStatus status,
        string? resultAssetId,
        IReadOnlyDictionary<string, string>? extraParameters = null)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        if (extraParameters is not null)
        {
            foreach (var parameter in extraParameters)
            {
                parameters[parameter.Key] = parameter.Value;
            }
        }

        var ttsTask = new GenerationTask(
            "tts_project_state",
            GenerationTaskType.Tts,
            "int_project_state",
            "TTS",
            "comfyui",
            status,
            parameters,
            resultAssetId);

        return new CharacterInteraction(
            "int_project_state",
            1,
            InteractionType.Dialogue,
            "scene_project_state",
            "char_chen",
            null,
            new EmotionSpec("Calm", 0.6, "", "", "", ""),
            new ActionSpec("说话", [], "", 2),
            new DialogueLine(
                "dialogue_project_state",
                "char_chen",
                null,
                "测试台词",
                new VoiceRenderSettings("voice_project_state.wav", "Calm", 0.6, 1, 0)),
            null,
            null,
            new InteractionBinding(
                "voice_project_state.wav",
                null,
                SceneVisualBinding: VisualAssetBinding.FromImage("scene_asset_project_state"),
                SubjectCharacterVisualBinding: VisualAssetBinding.FromImage("character_asset_project_state")),
            [ttsTask],
            "segment_project_state");
    }

    private static void AddNotification(List<string> notifications, string? propertyName)
    {
        if (propertyName is not null)
        {
            notifications.Add(propertyName);
        }
    }

    private static WorkbenchRowDraft Row(string interactionId, WorkbenchRowKind kind)
    {
        return new WorkbenchRowDraft(
            interactionId,
            "scene_001",
            1,
            kind,
            kind == WorkbenchRowKind.Narration ? "旁白" : "Chen",
            kind == WorkbenchRowKind.Narration ? null : "char_chen",
            "Calm 0.6",
            "测试文本",
            null,
            null,
            new WorkbenchTaskLinks(null),
            VisualAssetBinding.None,
            VoiceAssetBinding.None);
    }

    private static object? GetSelectedCharacterPreviewSnapshot(AnalysisResultRowViewModel row)
    {
        var field = typeof(AnalysisResultRowViewModel).GetField(
            "_selectedCharacterPreview",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(row);
    }
}

