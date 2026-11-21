using DraftAgent.App.ViewModels;
using DraftAgent.Core;

namespace DraftAgent.App.Tests;

public sealed class BindingTargetViewModelTests
{
    [Fact]
    public void ImagePreviewSnapshotInvalidatesWhenPreviewAssetChanges()
    {
        var firstImage = CreateTempPng();
        var secondImage = CreateTempPng();
        var target = BindingTargetViewModel.ForScene(SceneTableViewModel.CreateManual(1));

        target.ImagePreviewAsset = firstImage;

        Assert.True(target.HasImagePreview);
        Assert.Equal(firstImage, target.ImagePreviewSource);
        Assert.Equal(Path.GetFileName(firstImage), target.ImagePreviewLabel);

        target.ImagePreviewAsset = secondImage;

        Assert.True(target.HasImagePreview);
        Assert.Equal(secondImage, target.ImagePreviewSource);
        Assert.Equal(Path.GetFileName(secondImage), target.ImagePreviewLabel);
    }

    [Fact]
    public void ImagePreviewSnapshotInvalidatesWhenFallbackImageAssetChanges()
    {
        var target = BindingTargetViewModel.ForScene(SceneTableViewModel.CreateManual(1));

        target.ImageAsset = "asset_scene_a";
        Assert.False(target.HasImagePreview);
        Assert.Equal(string.Empty, target.ImagePreviewSource);
        Assert.Equal("asset_scene_a", target.ImagePreviewLabel);

        target.ImageAsset = "asset_scene_b";

        Assert.False(target.HasImagePreview);
        Assert.Equal(string.Empty, target.ImagePreviewSource);
        Assert.Equal("asset_scene_b", target.ImagePreviewLabel);
    }

    [Fact]
    public void ImageAssetClearsPreviewWhenItBecomesUnbound()
    {
        var image = CreateTempPng();
        var target = BindingTargetViewModel.ForScene(SceneTableViewModel.CreateManual(1));
        target.ImageAsset = image;

        Assert.Equal(image, target.ImagePreviewAsset);
        Assert.True(target.HasImagePreview);

        target.ImageAsset = "未绑定图片";

        Assert.Equal(string.Empty, target.ImagePreviewAsset);
        Assert.False(target.HasImagePreview);
        Assert.Equal("未绑定图片", target.ImagePreviewLabel);
    }

    [Fact]
    public void SceneTargetExposesVisualSourceAndRenderedSummary()
    {
        var scene = SceneTableViewModel.FromDraft(new WorkbenchSceneDraft(
            "scene_psd",
            1,
            "PSD scene",
            "Rain",
            [],
            VisualAssetBinding.FromPsd("asset_source_scene_psd", "asset_rendered_scene")));

        var target = BindingTargetViewModel.ForScene(scene);

        Assert.Equal("PSD", target.VisualSourceKindText);
        Assert.Equal("asset_source_scene_psd", target.VisualSourceText);
        Assert.Equal("PSD 输出", target.VisualRenderedLabel);
        Assert.Equal("asset_rendered_scene", target.VisualRenderedText);
    }

    [Fact]
    public void BindingPreviewShowsPsdRenderedPngWhenPreviewPathIsResolved()
    {
        var renderedPath = CreateTempPng();
        var scene = SceneTableViewModel.FromDraft(new WorkbenchSceneDraft(
            "scene_psd_preview",
            1,
            "PSD preview",
            "Rain",
            [],
            VisualAssetBinding.FromPsd("asset_source_scene_psd", renderedPath)));

        var target = BindingTargetViewModel.ForScene(scene);

        Assert.True(target.BindingPreviewShowsImage);
        Assert.False(target.BindingPreviewShowsText);
        Assert.Equal(renderedPath, target.ImagePreviewSource);
        Assert.Equal(Path.GetFileName(renderedPath), target.BindingPreviewTitle);
        Assert.Equal("PSD", target.BindingPreviewSubtitle);
    }

    [Fact]
    public void BindingPreviewDoesNotUsePsdSourceAsImageWhenRenderedPngIsMissing()
    {
        var scene = SceneTableViewModel.FromDraft(new WorkbenchSceneDraft(
            "scene_psd_unrendered",
            1,
            "PSD unrendered",
            "Rain",
            [],
            VisualAssetBinding.FromPsd("asset_source_scene_psd")));

        var target = BindingTargetViewModel.ForScene(scene);

        Assert.Equal("未绑定图片", target.ImageAsset);
        Assert.Equal(string.Empty, target.ImagePreviewAsset);
        Assert.False(target.BindingPreviewShowsImage);
        Assert.True(target.BindingPreviewShowsText);
        Assert.Equal("asset_source_scene_psd", target.BindingPreviewTitle);
        Assert.Equal("PSD", target.BindingPreviewSubtitle);
    }

    [Fact]
    public void SceneTargetsUseLargeBindingPreviewWhenTheyDoNotNeedVoice()
    {
        var sceneTarget = BindingTargetViewModel.ForScene(SceneTableViewModel.CreateManual(1));
        var character = new Character("char_chen", "Chen", "role", string.Empty, string.Empty, string.Empty, new AssetBinding());
        var characterTarget = BindingTargetViewModel.ForCharacterRows(character, []);

        Assert.True(sceneTarget.UsesLargeBindingPreview);
        Assert.False(characterTarget.UsesLargeBindingPreview);
    }

    [Fact]
    public void CharacterTargetsExposeEditableNameOnlyForCharacters()
    {
        var sceneTarget = BindingTargetViewModel.ForScene(SceneTableViewModel.CreateManual(1));
        var character = new Character("char_chen", "Chen", "role", string.Empty, string.Empty, string.Empty, new AssetBinding());
        var characterTarget = BindingTargetViewModel.ForCharacterRows(character, []);

        Assert.False(sceneTarget.CanEditDisplayName);
        Assert.True(characterTarget.CanEditDisplayName);

        characterTarget.BeginDisplayNameEdit();
        characterTarget.EditableDisplayName = "Lin";

        Assert.Equal("Chen", characterTarget.DisplayName);
        Assert.Equal("Lin", characterTarget.EditableDisplayName);
        Assert.True(characterTarget.IsEditingDisplayName);
        Assert.Equal("保存", characterTarget.RenameButtonText);

        characterTarget.AcceptDisplayNameEdit("Lin");

        Assert.Equal("Lin", characterTarget.DisplayName);
        Assert.False(characterTarget.IsEditingDisplayName);
        Assert.Equal("重命名", characterTarget.RenameButtonText);
    }

    [Fact]
    public void CharacterTargetExposesVisualAndVoiceSourceSummaryFromFirstRow()
    {
        var draft = new WorkbenchRowDraft(
            InteractionId: "int_psd",
            SceneId: "scene_psd",
            Order: 1,
            Kind: WorkbenchRowKind.Dialogue,
            SubjectName: "Chen",
            SubjectCharacterId: "char_chen",
            Text: "Line",
            Emotion: "Calm 1",
            EmotionVectors: null,
            EmotionAlpha: null,
            TaskLinks: new WorkbenchTaskLinks(null),
            CharacterVisualBinding: VisualAssetBinding.FromPsd("asset_character_psd", "asset_rendered_character"),
            VoiceBinding: VoiceAssetBinding.FromProjectReference(
                "chen.wav",
                "voice line",
                "asset_voice_source",
                @"C:\voices\chen.wav"),
            VoiceRole: "Chen",
            VoiceRoleCharacterId: "char_chen");
        var row = AnalysisResultRowViewModel.FromDraft(draft, "asset_scene");
        row.MarkAppliedDraft(draft);
        var scene = SceneTableViewModel.CreateManual(1);
        scene.Rows.Add(row);
        var character = new Character("char_chen", "Chen", "role", string.Empty, string.Empty, string.Empty, new AssetBinding());

        var target = BindingTargetViewModel.ForCharacter(character, [scene]);

        Assert.Equal("PSD", target.VisualSourceKindText);
        Assert.Equal("asset_character_psd", target.VisualSourceText);
        Assert.Equal("asset_rendered_character", target.VisualRenderedText);
        Assert.Equal("未登记项目源", target.VoiceSourceText);
        Assert.Equal("Missing project source", target.VoiceSourceStateText);
        Assert.Equal("未同步 voices", target.VoiceSyncedText);
        Assert.Equal("Missing voice reference", target.VoiceSyncedStateText);
        Assert.Equal("未绑定参考文本", target.VoiceReferenceTextPreview);
        Assert.Equal("Missing reference text", target.VoiceReferenceStateText);
        Assert.Equal("Voice reference is not bound", target.VoiceDiagnosticText);
        Assert.Equal("Saved binding", target.VoiceReferenceTextSourceText);

        target.VoiceReferenceText = "edited line";

        Assert.Equal("Manual edit", target.VoiceReferenceTextSourceText);
        Assert.Equal("Reference text: Manual edit", target.VoiceReferenceStateText);
    }

    [Fact]
    public void CharacterTargetDoesNotUseRowPsdRenderAsAggregatePreview()
    {
        var row = AnalysisResultRowViewModel.FromDraft(new WorkbenchRowDraft(
            InteractionId: "int_psd",
            SceneId: "scene_psd",
            Order: 1,
            Kind: WorkbenchRowKind.Dialogue,
            SubjectName: "Chen",
            SubjectCharacterId: "char_chen",
            Text: "Line",
            Emotion: "Calm 1",
            EmotionVectors: null,
            EmotionAlpha: null,
            TaskLinks: new WorkbenchTaskLinks(null),
            CharacterVisualBinding: VisualAssetBinding.FromPsd("asset_character_psd", "asset_rendered_character"),
            VoiceBinding: null),
            "asset_scene");
        var scene = SceneTableViewModel.CreateManual(1);
        scene.Rows.Add(row);
        var character = new Character("char_chen", "Chen", "role", string.Empty, string.Empty, string.Empty, new AssetBinding());

        var target = BindingTargetViewModel.ForCharacter(character, [scene]);

        Assert.Equal("未绑定图片", target.ImageAsset);
        Assert.Equal("PSD", target.VisualSourceKindText);
        Assert.Equal("asset_character_psd", target.VisualSourceText);
        Assert.Equal("asset_rendered_character", target.VisualRenderedText);
        Assert.Equal("asset_rendered_character", target.RowVisualDetails.Single().RenderedText);
    }

    [Fact]
    public void CharacterTargetUsesDefaultVisualAndVoiceBindingWhenRowsAreMissing()
    {
        var character = new Character(
            "char_linan",
            "Linan",
            "role",
            string.Empty,
                string.Empty,
                string.Empty,
                new AssetBinding(
                PortraitVisualBinding: VisualAssetBinding.FromPsd("asset_psd_linan", "asset_rendered_linan"),
                VoiceBinding: VoiceAssetBinding.FromProjectReference(
                    "linan.wav",
                    "Linan reference",
                    "asset_voice_linan",
                    @"C:\voices\linan.wav")));

        var target = BindingTargetViewModel.ForCharacterRows(character, []);

        Assert.Equal("asset_rendered_linan", target.ImageAsset);
        Assert.Equal("PSD", target.VisualSourceKindText);
        Assert.Equal("asset_psd_linan", target.VisualSourceText);
        Assert.Equal("asset_rendered_linan", target.VisualRenderedText);
        Assert.Equal("linan.wav", target.VoiceAsset);
        Assert.Equal("asset_voice_linan", target.VoiceSourceText);
        Assert.Equal("Project source registered", target.VoiceSourceStateText);
        Assert.Equal("linan.wav", target.VoiceSyncedText);
        Assert.Equal("Legacy voices path tracked", target.VoiceSyncedStateText);
        Assert.Equal("Linan reference", target.VoiceReferenceTextPreview);
        Assert.Equal("Reference text: Saved binding", target.VoiceReferenceStateText);
    }

    [Fact]
    public void CharacterTargetUsesDefaultVisualAndVoiceBindingWhenFirstRowBindingIsEmpty()
    {
        var row = AnalysisResultRowViewModel.FromDraft(new WorkbenchRowDraft(
            InteractionId: "int_linan",
            SceneId: "scene_001",
            Order: 1,
            Kind: WorkbenchRowKind.Dialogue,
            SubjectName: "Linan",
            SubjectCharacterId: "char_linan",
            Emotion: "Calm",
            Text: "Line",
            EmotionVectors: null,
            EmotionAlpha: null,
            TaskLinks: new WorkbenchTaskLinks(null),
            CharacterVisualBinding: VisualAssetBinding.None,
            VoiceBinding: VoiceAssetBinding.None),
            null);
        var character = new Character(
            "char_linan",
            "Linan",
            "role",
            string.Empty,
                string.Empty,
                string.Empty,
                new AssetBinding(
                PortraitVisualBinding: VisualAssetBinding.FromPsd("asset_psd_linan", "asset_rendered_linan"),
                VoiceBinding: VoiceAssetBinding.FromProjectReference(
                    "linan.wav",
                    "Linan reference",
                    "asset_voice_linan",
                    @"C:\voices\linan.wav")));

        var target = BindingTargetViewModel.ForCharacterRows(character, [row]);

        Assert.Equal("asset_rendered_linan", target.ImageAsset);
        Assert.Equal("PSD", target.VisualSourceKindText);
        Assert.Equal("asset_psd_linan", target.VisualSourceText);
        Assert.Equal("linan.wav", target.VoiceAsset);
        Assert.Equal("asset_voice_linan", target.VoiceSourceText);
        Assert.Equal("Linan reference", target.VoiceReferenceTextPreview);
    }

    [Fact]
    public void CharacterTargetDoesNotUseNarratorRoutedRowVoiceAsCharacterVoiceBinding()
    {
        var row = AnalysisResultRowViewModel.FromDraft(new WorkbenchRowDraft(
            InteractionId: "int_linan",
            SceneId: "scene_001",
            Order: 1,
            Kind: WorkbenchRowKind.Dialogue,
            SubjectName: "Linan",
            SubjectCharacterId: "char_linan",
            Emotion: "Calm",
            Text: "Line",
            EmotionVectors: null,
            EmotionAlpha: null,
            TaskLinks: new WorkbenchTaskLinks(null),
            CharacterVisualBinding: VisualAssetBinding.None,
            VoiceBinding: VoiceAssetBinding.FromProjectReference("narrator.wav", "Narrator reference", null, null)),
            null);
        var character = new Character(
            "char_linan",
            "Linan",
            "role",
            string.Empty,
            string.Empty,
            string.Empty,
            new AssetBinding(
                Voice: "linan.wav",
                VoiceBinding: VoiceAssetBinding.FromProjectReference(
                    "linan.wav",
                    "Linan reference",
                    "asset_voice_linan",
                    @"C:\voices\linan.wav")));

        var target = BindingTargetViewModel.ForCharacterRows(character, [row]);

        Assert.Equal("linan.wav", target.VoiceAsset);
        Assert.Equal("Linan reference", target.VoiceReferenceTextPreview);
        Assert.Equal("asset_voice_linan", target.VoiceSourceText);
    }

    [Fact]
    public void CharacterTargetDoesNotUseRoutedRoleVoiceRowAsReferenceTable()
    {
        var draft = new WorkbenchRowDraft(
            InteractionId: "int_linan",
            SceneId: "scene_001",
            Order: 1,
            Kind: WorkbenchRowKind.Dialogue,
            SubjectName: "Linan",
            SubjectCharacterId: "char_linan",
            Emotion: "Calm",
            Text: "Line",
            EmotionVectors: null,
            EmotionAlpha: null,
            TaskLinks: new WorkbenchTaskLinks(null),
            CharacterVisualBinding: VisualAssetBinding.None,
            VoiceBinding: VoiceAssetBinding.FromProjectReference("linan-routed.wav", "Routed reference", null, null),
            VoiceRole: "Linan",
            VoiceRoleCharacterId: "char_linan");
        var row = AnalysisResultRowViewModel.FromDraft(draft, null);
        row.MarkAppliedDraft(draft);
        var character = new Character(
            "char_linan",
            "Linan",
            "role",
            string.Empty,
            string.Empty,
            string.Empty,
            new AssetBinding());

        var target = BindingTargetViewModel.ForCharacterRows(character, [row]);

        Assert.Equal("未绑定声音", target.VoiceAsset);
        Assert.Equal("Missing reference text", target.VoiceReferenceStateText);
    }

    [Fact]
    public void CharacterTargetVoiceDiagnosticsDistinguishMissingProjectSource()
    {
        var target = CreateCharacterTargetWithVoiceBinding(VoiceAssetBinding.FromProjectReference(
            "chen.wav",
            "voice line",
            null,
            @"C:\voices\chen.wav"));

        Assert.Equal("Missing project source", target.VoiceSourceStateText);
        Assert.Equal("Legacy voices path tracked", target.VoiceSyncedStateText);
        Assert.Equal("Reference text: Saved binding", target.VoiceReferenceStateText);
        Assert.Contains("Project source is not required", target.VoiceDiagnosticText, StringComparison.Ordinal);
    }

    [Fact]
    public void CharacterTargetVoiceDiagnosticsDistinguishMissingSyncedVoicesPath()
    {
        var target = CreateCharacterTargetWithVoiceBinding(VoiceAssetBinding.FromProjectReference(
            "chen.wav",
            "voice line",
            "asset_voice_source",
            null));

        Assert.Equal("Project source registered", target.VoiceSourceStateText);
        Assert.Equal("No ComfyUI voices copy", target.VoiceSyncedStateText);
        Assert.Contains("ComfyUI voices copy is managed by the user", target.VoiceDiagnosticText, StringComparison.Ordinal);
    }

    [Fact]
    public void CharacterTargetVoiceDiagnosticsDistinguishMissingReferenceText()
    {
        var target = CreateCharacterTargetWithVoiceBinding(VoiceAssetBinding.FromProjectReference(
            "chen.wav",
            null,
            "asset_voice_source",
            @"C:\voices\chen.wav"));

        Assert.Equal("Project source registered", target.VoiceSourceStateText);
        Assert.Equal("Legacy voices path tracked", target.VoiceSyncedStateText);
        Assert.Equal("Missing reference text", target.VoiceReferenceStateText);
        Assert.Contains("Reference text is missing", target.VoiceDiagnosticText, StringComparison.Ordinal);
    }


    [Fact]
    public void CharacterTargetRowVisualDetailsTrackCurrentRowMarker()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var first = AnalysisResultRowViewModel.FromDraft(new WorkbenchRowDraft(
            InteractionId: "int_first",
            SceneId: scene.SceneId,
            Order: 1,
            Kind: WorkbenchRowKind.Dialogue,
            SubjectName: "Chen",
            SubjectCharacterId: "char_chen",
            Text: "First line",
            Emotion: "Calm",
            EmotionVectors: null,
            EmotionAlpha: null,
            TaskLinks: new WorkbenchTaskLinks(null),
            CharacterVisualBinding: VisualAssetBinding.FromImage("asset_first"),
            VoiceBinding: null),
            "asset_scene");
        var second = AnalysisResultRowViewModel.FromDraft(new WorkbenchRowDraft(
            InteractionId: "int_second",
            SceneId: scene.SceneId,
            Order: 2,
            Kind: WorkbenchRowKind.Dialogue,
            SubjectName: "Chen",
            SubjectCharacterId: "char_chen",
            Text: "Second line",
            Emotion: "Calm",
            EmotionVectors: null,
            EmotionAlpha: null,
            TaskLinks: new WorkbenchTaskLinks(null),
            CharacterVisualBinding: VisualAssetBinding.FromImage("asset_second"),
            VoiceBinding: null),
            "asset_scene");
        scene.Rows.Add(first);
        scene.Rows.Add(second);
        var character = new Character("char_chen", "Chen", "role", string.Empty, string.Empty, string.Empty, new AssetBinding());
        var target = BindingTargetViewModel.ForCharacter(character, [scene]);

        target.ApplyCurrentRowMarker("int_second");

        Assert.False(target.RowVisualDetails[0].IsCurrentRow);
        Assert.True(target.RowVisualDetails[1].IsCurrentRow);

        target.ApplyCurrentRowMarker("int_first");

        Assert.True(target.RowVisualDetails[0].IsCurrentRow);
        Assert.False(target.RowVisualDetails[1].IsCurrentRow);
    }



    [Fact]
    public void CharacterTargetRowVisualDetailsDetectPsdSourceFingerprintMismatch()
    {
        var psdPath = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-source-{Guid.NewGuid():N}.psd");
        File.WriteAllBytes(psdPath, [1, 2, 3]);
        var matchingFingerprint = PsdVisualBindingDiagnostics.BuildSourceFingerprint("asset_character_psd", psdPath);
        File.WriteAllBytes(psdPath, [1, 2, 3, 4, 5]);
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.FromDraft(new WorkbenchRowDraft(
            InteractionId: "int_psd_changed",
            SceneId: scene.SceneId,
            Order: 1,
            Kind: WorkbenchRowKind.Dialogue,
            SubjectName: "Chen",
            SubjectCharacterId: "char_chen",
            Emotion: "Calm",
            Text: "PSD changed",
            EmotionVectors: null,
            EmotionAlpha: null,
            TaskLinks: new WorkbenchTaskLinks(null),
            CharacterVisualBinding: VisualAssetBinding.FromPsd(
                "asset_character_psd",
                "asset_rendered",
                new PsdRenderSelection(
                    [1],
                    ["Base"],
                    null,
                    "Calm",
                    "Idle",
                    VisualExportBounds.CanvasBounds,
                    matchingFingerprint,
                    "cache")),
            VoiceBinding: null),
            "asset_scene");
        scene.Rows.Add(row);
        var character = new Character("char_chen", "Chen", "role", string.Empty, string.Empty, string.Empty, new AssetBinding());

        var target = BindingTargetViewModel.ForCharacterRows(
            character,
            [row],
            reference => reference == "asset_character_psd" ? psdPath : null);

        Assert.Equal("PSD source state: changed, reselect layers", target.RowVisualDetails[0].PsdSourceStateText);
    }

    [Fact]
    public void CharacterTargetRowVisualDetailsReportsCurrentPsdSourceFingerprint()
    {
        var psdPath = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-source-{Guid.NewGuid():N}.psd");
        File.WriteAllBytes(psdPath, [1, 2, 3]);
        var fingerprint = PsdVisualBindingDiagnostics.BuildSourceFingerprint("asset_character_psd", psdPath);
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.FromDraft(new WorkbenchRowDraft(
            InteractionId: "int_psd_current",
            SceneId: scene.SceneId,
            Order: 1,
            Kind: WorkbenchRowKind.Dialogue,
            SubjectName: "Chen",
            SubjectCharacterId: "char_chen",
            Emotion: "Calm",
            Text: "PSD current",
            EmotionVectors: null,
            EmotionAlpha: null,
            TaskLinks: new WorkbenchTaskLinks(null),
            CharacterVisualBinding: VisualAssetBinding.FromPsd(
                "asset_character_psd",
                "asset_rendered",
                new PsdRenderSelection(
                    [1],
                    ["Base"],
                    null,
                    "Calm",
                    "Idle",
                    VisualExportBounds.CanvasBounds,
                    fingerprint,
                    "cache")),
            VoiceBinding: null),
            "asset_scene");
        scene.Rows.Add(row);
        var character = new Character("char_chen", "Chen", "role", string.Empty, string.Empty, string.Empty, new AssetBinding());

        var target = BindingTargetViewModel.ForCharacterRows(
            character,
            [row],
            reference => reference == "asset_character_psd" ? psdPath : null);

        Assert.Equal("PSD source state: current", target.RowVisualDetails[0].PsdSourceStateText);
    }


    private static string CreateTempPng()
    {
        var path = Path.Combine(Path.GetTempPath(), $"draft-agent-binding-target-preview-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, [137, 80, 78, 71]);
        return path;
    }

    private static BindingTargetViewModel CreateCharacterTargetWithVoiceBinding(VoiceAssetBinding voiceBinding)
    {
        var row = AnalysisResultRowViewModel.FromDraft(new WorkbenchRowDraft(
            InteractionId: "int_voice",
            SceneId: "scene_voice",
            Order: 1,
            Kind: WorkbenchRowKind.Dialogue,
            SubjectName: "Chen",
            SubjectCharacterId: "char_chen",
            Text: "Line",
            Emotion: "Calm 1",
            EmotionVectors: null,
            EmotionAlpha: null,
            TaskLinks: new WorkbenchTaskLinks(null),
            CharacterVisualBinding: VisualAssetBinding.FromImage("asset_character"),
            VoiceBinding: null),
            "asset_scene");
        var scene = SceneTableViewModel.CreateManual(1);
        scene.Rows.Add(row);
        var character = new Character(
            "char_chen",
            "Chen",
            "role",
            string.Empty,
            string.Empty,
            string.Empty,
            new AssetBinding(VoiceBinding: voiceBinding));
        return BindingTargetViewModel.ForCharacter(character, [scene]);
    }
}


