using System.Text.Json;
using System.Text;
using System.Xml.Linq;
using DraftAgent.Core;

namespace DraftAgent.Core.Tests;

public sealed class ProjectManagementTests
{
    [Fact]
    public async Task WorkspaceProjectManager_CreatesPortableProjectFolders()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();

        var manifest = await manager.CreateOrOpenAsync(root, "Rain Story");

        Assert.Equal(WorkspaceProjectManager.ManifestSchema, manifest.Schema);
        Assert.True(File.Exists(Path.Combine(root, WorkspaceProjectManager.ManifestFileName)));
        Assert.True(Directory.Exists(manifest.Folders.ImagesImported));
        Assert.True(Directory.Exists(manifest.Folders.ImagesPsd));
        Assert.True(Directory.Exists(manifest.Folders.AudioGenerated));
        Assert.True(Directory.Exists(manifest.Folders.Otio));
    }

    [Fact]
    public async Task WorkspaceProjectManager_SaveDraftUsesAtomicBackupOnOverwrite()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Atomic Draft Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var first = BuildProjectWithAssets("first.png", "first.wav");
        var second = first with { Title = "Second title persisted atomically" };

        await manager.SaveDraftAsync(workspace, first);
        await manager.SaveDraftAsync(workspace, second);

        var draftPath = Path.Combine(root, WorkspaceProjectManager.DraftFileName);
        var currentJson = await File.ReadAllTextAsync(draftPath);
        var backupJson = await File.ReadAllTextAsync($"{draftPath}.bak");
        Assert.Contains("Second title persisted atomically", currentJson, StringComparison.Ordinal);
        Assert.Contains("Rain Story", backupJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProjectMediaAssetService_ImportsPngAndPsdIntoProjectManifest()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Asset Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var service = new ProjectMediaAssetService();
        var png = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-{Guid.NewGuid():N}.png"), [1, 2, 3]);
        var psd = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-{Guid.NewGuid():N}.psd"), [4, 5, 6]);

        var pngImport = await service.ImportAsync(workspace, new MediaImportRequest(png, MediaAssetKind.Image, MediaAssetSource.ImportedPng, "scene_001"));
        var psdImport = await service.ImportAsync(workspace, new MediaImportRequest(psd, MediaAssetKind.Psd, MediaAssetSource.ImportedPsd, "character_001"));

        Assert.True(File.Exists(pngImport.Asset.AbsolutePath));
        Assert.True(File.Exists(psdImport.Asset.AbsolutePath));
        Assert.Contains("assets/images/imported", pngImport.Asset.ProjectRelativePath);
        Assert.Contains("assets/images/psd", psdImport.Asset.ProjectRelativePath);
        Assert.Equal(2, psdImport.Manifest.Assets.Count);
    }

    [Fact]
    public async Task ProjectMediaAssetService_OverwritesStaleVoiceReferenceSidecarOnImport()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Voice Sidecar Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var service = new ProjectMediaAssetService();
        var sourceDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-voice-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDirectory);
        var sourceAudio = await CreateFileAsync(Path.Combine(sourceDirectory, "chen.wav"), [1, 2, 3]);
        await File.WriteAllTextAsync(Path.ChangeExtension(sourceAudio, ".txt"), "fresh reference");
        var staleDestinationText = Path.Combine(workspace.Folders.AudioVoiceReferences, "char_chen_chen.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(staleDestinationText)!);
        await File.WriteAllTextAsync(staleDestinationText, "stale reference");

        var import = await service.ImportAsync(
            workspace,
            new MediaImportRequest(sourceAudio, MediaAssetKind.Audio, MediaAssetSource.VoiceReference, "char_chen"));

        Assert.Equal("fresh reference", await File.ReadAllTextAsync(Path.ChangeExtension(import.Asset.AbsolutePath, ".txt")));
    }

    [Fact]
    public async Task ProjectMediaAssetService_DoesNotPrefixImportedFileNamesWithInternalTargetIds()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Internal Id Filename Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var service = new ProjectMediaAssetService();
        var sourceDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-internal-id-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDirectory);
        var sourceAudio = await CreateFileAsync(Path.Combine(sourceDirectory, "int_scene_001_001_ComfyUI_00038_.flac"), [1, 2, 3, 4]);

        var import = await service.ImportAsync(
            workspace,
            new MediaImportRequest(sourceAudio, MediaAssetKind.Audio, MediaAssetSource.VoiceReference, "int_scene_001_001"));

        Assert.Equal("ComfyUI_00038_.flac", Path.GetFileName(import.Asset.AbsolutePath));
        Assert.Equal("int_scene_001_001", import.Asset.TargetId);
        Assert.DoesNotContain("int_scene_001_001", import.Asset.ProjectRelativePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProjectMediaAssetService_ReusesExistingVoiceReferenceWithSameContent()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Voice Dedupe Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var service = new ProjectMediaAssetService();
        var sourceDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-voice-dedupe-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDirectory);
        var firstSource = await CreateFileAsync(Path.Combine(sourceDirectory, "ComfyUI_00038_.flac"), [1, 2, 3, 4]);
        await File.WriteAllTextAsync(Path.ChangeExtension(firstSource, ".txt"), "first reference");
        var secondSource = await CreateFileAsync(Path.Combine(sourceDirectory, "int_001_ComfyUI_00038_.flac"), [1, 2, 3, 4]);
        await File.WriteAllTextAsync(Path.ChangeExtension(secondSource, ".txt"), "updated reference");

        var firstImport = await service.ImportAsync(
            workspace,
            new MediaImportRequest(firstSource, MediaAssetKind.Audio, MediaAssetSource.VoiceReference, "char_001"));
        var secondImport = await service.ImportAsync(
            WorkspaceProjectManager.ToWorkspace(firstImport.Manifest),
            new MediaImportRequest(secondSource, MediaAssetKind.Audio, MediaAssetSource.VoiceReference, "int_001"));

        Assert.Equal(firstImport.Asset.Id, secondImport.Asset.Id);
        Assert.Single(secondImport.Manifest.Assets);
        Assert.Single(Directory.EnumerateFiles(workspace.Folders.AudioVoiceReferences, "*.flac"));
        Assert.Equal("updated reference", await File.ReadAllTextAsync(Path.ChangeExtension(firstImport.Asset.AbsolutePath, ".txt")));
    }

    [Fact]
    public async Task ProjectMediaAssetService_ReusesExistingVoiceReferenceWhenSourcePathMatches()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Voice Source Path Dedupe Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var service = new ProjectMediaAssetService();
        var sourceDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-voice-source-path-dedupe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDirectory);
        var sourceAudio = await CreateFileAsync(Path.Combine(sourceDirectory, "same.wav"), [1, 2, 3, 4]);

        var firstImport = await service.ImportAsync(
            workspace,
            new MediaImportRequest(sourceAudio, MediaAssetKind.Audio, MediaAssetSource.VoiceReference, "char_001"));
        var secondImport = await service.ImportAsync(
            WorkspaceProjectManager.ToWorkspace(firstImport.Manifest),
            new MediaImportRequest(sourceAudio, MediaAssetKind.Audio, MediaAssetSource.VoiceReference, "int_001"));

        Assert.Equal(firstImport.Asset.Id, secondImport.Asset.Id);
        Assert.Single(secondImport.Manifest.Assets);
        Assert.Single(Directory.EnumerateFiles(workspace.Folders.AudioVoiceReferences, "*.wav"));
    }

    [Fact]
    public async Task ProjectMediaAssetService_RejectsNonAudioVoiceReferenceImport()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Voice Extension Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var service = new ProjectMediaAssetService();
        var source = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-{Guid.NewGuid():N}.txt"), [1, 2, 3]);

        var exception = await Assert.ThrowsAsync<ExternalIntegrationException>(() =>
            service.ImportAsync(
                workspace,
                new MediaImportRequest(source, MediaAssetKind.Audio, MediaAssetSource.VoiceReference, "char_chen")));

        Assert.Contains("参考音频导入只接受", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(workspace.Folders.AudioVoiceReferences, Path.GetFileName(source))));
    }

    [Fact]
    public async Task ProjectMediaAssetService_RejectsUnsupportedGeneratedTtsAudioImport()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Generated Audio Extension Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var service = new ProjectMediaAssetService();
        var source = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-{Guid.NewGuid():N}.mp3"), [1, 2, 3]);

        var exception = await Assert.ThrowsAsync<ExternalIntegrationException>(() =>
            service.ImportAsync(
                workspace,
                new MediaImportRequest(source, MediaAssetKind.Audio, MediaAssetSource.TtsGenerated, "int_001")));

        Assert.Contains("TTS 生成音频导入只接受 WAV/FLAC", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(workspace.Folders.AudioGenerated, Path.GetFileName(source))));
    }

    [Fact]
    public async Task ProjectMediaAssetService_DoesNotPrefixGeneratedTtsAudioWithInteractionId()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Generated Audio Filename Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var service = new ProjectMediaAssetService();
        var sourceDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-generated-audio-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDirectory);
        var sourceAudio = await CreateFileAsync(Path.Combine(sourceDirectory, "tts_int_001_output.flac"), [1, 2, 3]);

        var import = await service.ImportAsync(
            workspace,
            new MediaImportRequest(sourceAudio, MediaAssetKind.Audio, MediaAssetSource.TtsGenerated, "int_001"));

        Assert.Equal("output.flac", Path.GetFileName(import.Asset.AbsolutePath));
        Assert.Equal("int_001", import.Asset.TargetId);
        Assert.DoesNotContain("int_001", import.Asset.ProjectRelativePath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tts_", import.Asset.ProjectRelativePath, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("scene.png", MediaAssetKind.Image, MediaAssetSource.VoiceReference, "assets/audio/voice-references")]
    [InlineData("line.wav", MediaAssetKind.Audio, MediaAssetSource.ImportedPng, "assets/images/imported")]
    public async Task ProjectMediaAssetService_RejectsMismatchedKindAndSource(
        string fileName,
        MediaAssetKind kind,
        MediaAssetSource source,
        string destinationFolder)
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Kind Source Mismatch Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var service = new ProjectMediaAssetService();
        var sourcePath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-{Guid.NewGuid():N}-{fileName}"), [1, 2, 3]);

        var exception = await Assert.ThrowsAsync<ExternalIntegrationException>(() =>
            service.ImportAsync(
                workspace,
                new MediaImportRequest(sourcePath, kind, source, "target_001")));

        Assert.Contains("kind/source 不匹配", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(workspace.RootPath, destinationFolder, Path.GetFileName(sourcePath))));
    }

    [Fact]
    public async Task ProjectAssetResolver_ResolvesAssetIdAndProjectRelativePath()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Resolver Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var service = new ProjectMediaAssetService();
        var png = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-{Guid.NewGuid():N}.png"), [1, 2, 3]);
        var import = await service.ImportAsync(workspace, new MediaImportRequest(png, MediaAssetKind.Image, MediaAssetSource.ImportedPng, "scene_001"));
        var resolver = new ProjectAssetResolver();

        var byId = resolver.ResolvePath(import.Asset.Id, workspace, import.Manifest);
        var byRelativePath = resolver.ResolvePath(import.Asset.ProjectRelativePath, workspace, import.Manifest);

        Assert.Equal(import.Asset.AbsolutePath, byId);
        Assert.Equal(import.Asset.AbsolutePath, byRelativePath);
    }

    [Fact]
    public async Task ProjectAssetResolver_RejectsWorkspaceRelativePathThatEscapesProjectRoot()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Resolver Escape Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var outsidePath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-outside-{Guid.NewGuid():N}.png"), [1, 2, 3]);
        var escapingReference = Path.GetRelativePath(workspace.RootPath, outsidePath);
        Assert.StartsWith("..", escapingReference, StringComparison.Ordinal);
        var resolver = new ProjectAssetResolver();

        var byEscapingRelativePath = resolver.ResolvePath(escapingReference, workspace, manifest);
        var byExplicitAbsolutePath = resolver.ResolvePath(outsidePath, workspace, manifest);

        Assert.Null(byEscapingRelativePath);
        Assert.Equal(Path.GetFullPath(outsidePath), byExplicitAbsolutePath);
    }

    [Fact]
    public async Task ProjectAssetResolver_RejectsManifestRelativePathThatEscapesProjectRoot()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Resolver Manifest Escape Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var outsidePath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-manifest-outside-{Guid.NewGuid():N}.png"), [1, 2, 3]);
        var escapingRelativePath = Path.GetRelativePath(workspace.RootPath, outsidePath).Replace('\\', '/');
        Assert.StartsWith("..", escapingRelativePath, StringComparison.Ordinal);
        var forgedAsset = new ProjectMediaAsset(
            "asset_escape",
            MediaAssetKind.Image,
            MediaAssetSource.ImportedPng,
            "Escaping asset",
            Path.Combine(workspace.RootPath, "missing.png"),
            escapingRelativePath,
            "scene_001",
            outsidePath,
            null,
            new Dictionary<string, string>(),
            DateTimeOffset.UtcNow);
        var forgedManifest = manifest with { Assets = [forgedAsset] };
        var resolver = new ProjectAssetResolver();

        var byAssetId = resolver.ResolvePath(forgedAsset.Id, workspace, forgedManifest);
        var byProjectRelativePath = resolver.ResolvePath(escapingRelativePath, workspace, forgedManifest);
        var rebased = WorkspaceProjectManager.RebaseManifest(forgedManifest, workspace.RootPath, out _);
        var rebasedAsset = Assert.Single(rebased.Assets);

        Assert.Null(byAssetId);
        Assert.Null(byProjectRelativePath);
        Assert.Equal(forgedAsset.AbsolutePath, rebasedAsset.AbsolutePath);
    }

    [Fact]
    public async Task ProjectAssetResolver_RejectsManifestAbsolutePathOutsideProjectRoot()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Resolver Manifest Absolute Escape Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var outsidePath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-manifest-absolute-outside-{Guid.NewGuid():N}.png"), [1, 2, 3]);
        var forgedAsset = new ProjectMediaAsset(
            "asset_outside_absolute",
            MediaAssetKind.Image,
            MediaAssetSource.ImportedPng,
            "Outside asset",
            outsidePath,
            "assets/images/imported/missing.png",
            "scene_001",
            outsidePath,
            null,
            new Dictionary<string, string>(),
            DateTimeOffset.UtcNow);
        var forgedManifest = manifest with { Assets = [forgedAsset] };
        var resolver = new ProjectAssetResolver();

        var byAssetId = resolver.ResolvePath(forgedAsset.Id, workspace, forgedManifest);
        var byExplicitAbsolutePath = resolver.ResolvePath(outsidePath, workspace, forgedManifest);

        Assert.Null(byAssetId);
        Assert.Equal(Path.GetFullPath(outsidePath), byExplicitAbsolutePath);
    }

    [Fact]
    public async Task WorkspaceProjectManager_LoadRebasesMovedProjectManifestAndAssetPaths()
    {
        var root = NewTempProjectRoot();
        var movedRoot = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Moved Project Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var service = new ProjectMediaAssetService();
        var png = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-{Guid.NewGuid():N}.png"), [1, 2, 3]);
        var import = await service.ImportAsync(workspace, new MediaImportRequest(png, MediaAssetKind.Image, MediaAssetSource.ImportedPng, "scene_001"));

        Directory.Move(root, movedRoot);

        var loaded = await manager.LoadAsync(movedRoot);
        var loadedAsset = loaded.Assets.Single(asset => asset.Id == import.Asset.Id);
        var resolvedPath = new ProjectAssetResolver().ResolvePath(loadedAsset.Id, WorkspaceProjectManager.ToWorkspace(loaded), loaded);

        Assert.Equal(Path.GetFullPath(movedRoot), loaded.RootPath);
        Assert.Equal(Path.Combine(Path.GetFullPath(movedRoot), WorkspaceProjectManager.DraftFileName), loaded.DraftPath);
        Assert.StartsWith(Path.GetFullPath(movedRoot), loadedAsset.AbsolutePath, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(import.Asset.AbsolutePath, loadedAsset.AbsolutePath);
        Assert.True(File.Exists(resolvedPath));

        var persisted = await WorkspaceProjectManager.ReadManifestAsync(
            Path.Combine(movedRoot, WorkspaceProjectManager.ManifestFileName),
            CancellationToken.None);
        var persistedAsset = persisted.Assets.Single(asset => asset.Id == import.Asset.Id);
        Assert.Equal(loaded.RootPath, persisted.RootPath);
        Assert.Equal(loaded.DraftPath, persisted.DraftPath);
        Assert.Equal(loadedAsset.AbsolutePath, persistedAsset.AbsolutePath);
        Assert.StartsWith(Path.GetFullPath(movedRoot), persistedAsset.AbsolutePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WorkspaceProjectManager_CreateOrOpenRebasesMovedProjectManifestOnDisk()
    {
        var root = NewTempProjectRoot();
        var movedRoot = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Moved Existing Project Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var service = new ProjectMediaAssetService();
        var png = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-{Guid.NewGuid():N}.png"), [1, 2, 3]);
        var import = await service.ImportAsync(workspace, new MediaImportRequest(png, MediaAssetKind.Image, MediaAssetSource.ImportedPng, "scene_001"));

        Directory.Move(root, movedRoot);

        var opened = await manager.CreateOrOpenAsync(movedRoot, "Ignored Name");
        var openedAsset = opened.Assets.Single(asset => asset.Id == import.Asset.Id);
        var persisted = await WorkspaceProjectManager.ReadManifestAsync(
            Path.Combine(movedRoot, WorkspaceProjectManager.ManifestFileName),
            CancellationToken.None);
        var persistedAsset = persisted.Assets.Single(asset => asset.Id == import.Asset.Id);

        Assert.Equal(manifest.ProjectId, opened.ProjectId);
        Assert.Equal("Moved Existing Project Test", opened.Name);
        Assert.Equal(Path.GetFullPath(movedRoot), opened.RootPath);
        Assert.Equal(Path.Combine(Path.GetFullPath(movedRoot), WorkspaceProjectManager.DraftFileName), opened.DraftPath);
        Assert.Equal(opened.RootPath, persisted.RootPath);
        Assert.Equal(opened.DraftPath, persisted.DraftPath);
        Assert.Equal(openedAsset.AbsolutePath, persistedAsset.AbsolutePath);
        Assert.StartsWith(Path.GetFullPath(movedRoot), persistedAsset.AbsolutePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WorkspaceProjectManager_SaveDraftUsesRebasedManifestDraftPath()
    {
        var root = NewTempProjectRoot();
        var movedRoot = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Moved Draft Save Test");
        var staleWorkspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var project = new SceneInteractionBuilder().Build("林安说：保存到新目录。") with { Title = "Moved Draft" };

        Directory.Move(root, movedRoot);
        var movedManifest = await manager.LoadAsync(movedRoot);
        var mixedWorkspace = staleWorkspace with
        {
            RootPath = movedManifest.RootPath,
            ManifestPath = Path.Combine(movedManifest.RootPath, WorkspaceProjectManager.ManifestFileName),
            Folders = movedManifest.Folders
        };

        var saved = await manager.SaveDraftAsync(mixedWorkspace, project);
        var loaded = await manager.LoadDraftAsync(WorkspaceProjectManager.ToWorkspace(saved));

        Assert.Equal(Path.Combine(Path.GetFullPath(movedRoot), WorkspaceProjectManager.DraftFileName), saved.DraftPath);
        Assert.True(File.Exists(saved.DraftPath));
        Assert.False(File.Exists(staleWorkspace.DraftPath));
        Assert.NotNull(loaded);
        Assert.Equal("Moved Draft", loaded.Title);
    }

    [Fact]
    public async Task WorkspaceProjectManager_SaveDraftDoesNotOverwriteOriginalAiJson()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Original AI Baseline Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var original = new SceneInteractionBuilder().Build("林安说：原始台词。") with { Title = "Original AI" };
        var edited = original with
        {
            Title = "Edited Draft",
            Scenes =
            [
                original.Scenes[0],
                original.Scenes[0] with { Id = "scene_saved_draft_extra", Order = original.Scenes[0].Order + 1, Name = "保存后的草稿场景" }
            ]
        };
        var originalJson = JsonSerializer.Serialize(original, DraftJson.Options);

        var savedBaselineManifest = await manager.SaveOriginalAiJsonAsync(workspace, originalJson);
        await manager.SaveDraftAsync(WorkspaceProjectManager.ToWorkspace(savedBaselineManifest), edited);

        var loadedOriginalJson = await manager.LoadOriginalAiJsonAsync(WorkspaceProjectManager.ToWorkspace(savedBaselineManifest));
        var loadedDraft = await manager.LoadDraftAsync(WorkspaceProjectManager.ToWorkspace(savedBaselineManifest));

        Assert.Equal(originalJson, loadedOriginalJson);
        Assert.NotNull(loadedDraft);
        Assert.Equal("Edited Draft", loadedDraft.Title);
        Assert.Equal(2, loadedDraft.Scenes.Count);
        Assert.DoesNotContain("Edited Draft", loadedOriginalJson, StringComparison.Ordinal);
        Assert.DoesNotContain("保存后的草稿场景", loadedOriginalJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProjectMediaAssetService_ImportsIntoRebasedFoldersAfterProjectMove()
    {
        var root = NewTempProjectRoot();
        var movedRoot = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Moved Asset Import Test");
        var staleWorkspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var service = new ProjectMediaAssetService();
        var source = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-{Guid.NewGuid():N}.png"), [1, 2, 3]);

        Directory.Move(root, movedRoot);
        var movedManifest = await manager.LoadAsync(movedRoot);
        var mixedWorkspace = staleWorkspace with
        {
            RootPath = movedManifest.RootPath,
            ManifestPath = Path.Combine(movedManifest.RootPath, WorkspaceProjectManager.ManifestFileName)
        };

        var import = await service.ImportAsync(
            mixedWorkspace,
            new MediaImportRequest(source, MediaAssetKind.Image, MediaAssetSource.ImportedPng, "scene_001"));
        var persisted = await WorkspaceProjectManager.ReadManifestAsync(
            Path.Combine(movedRoot, WorkspaceProjectManager.ManifestFileName),
            CancellationToken.None);
        var persistedAsset = persisted.Assets.Single(asset => asset.Id == import.Asset.Id);

        Assert.StartsWith(Path.GetFullPath(movedRoot), import.Asset.AbsolutePath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(Path.GetFullPath(movedRoot), persistedAsset.AbsolutePath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("assets/images/imported", import.Asset.ProjectRelativePath, StringComparison.Ordinal);
        Assert.True(File.Exists(import.Asset.AbsolutePath));
        Assert.False(Directory.Exists(Path.Combine(root, "assets")));
    }

    [Fact]
    public async Task DraftWorkspaceService_OpenExistingProjectDoesNotOverwriteDraft()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Existing Draft Test");
        var existingProject = new SceneInteractionBuilder().Build("林安说：旧项目。") with { Title = "Existing Draft" };
        await manager.SaveDraftAsync(WorkspaceProjectManager.ToWorkspace(manifest), existingProject);
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var currentProject = new SceneInteractionBuilder().Build("陈默说：当前项目。") with { Title = "Current Draft" };

        var openedManifest = await service.CreateOrOpenWorkspaceProjectAsync(root, currentProject.Title, currentProject);
        var loadedProject = await service.LoadWorkspaceProjectDraftAsync(WorkspaceProjectManager.ToWorkspace(openedManifest));

        Assert.NotNull(loadedProject);
        Assert.Equal(existingProject.Title, loadedProject.Title);
        Assert.Contains("旧项目", loadedProject.Script.CleanText);
        Assert.DoesNotContain("当前项目", loadedProject.Script.CleanText);
    }

    [Fact]
    public async Task WorkspaceProjectManager_SaveAndLoadDraftPreservesStructuredVisualBindings()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Structured Visual Binding Test");
        var sceneVisualBinding = VisualAssetBinding.FromPsd(
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
        var rowVisualBinding = VisualAssetBinding.FromPsd(
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
        var characterVisualBinding = VisualAssetBinding.FromPsd("asset_psd_character_default");
        var character = new Character(
            "char_chen",
            "陈默",
            "主角",
            string.Empty,
            string.Empty,
            "Calm",
            new AssetBinding("asset_psd_character_default", PortraitVisualBinding: characterVisualBinding));
        var interaction = new CharacterInteraction(
            "int_001",
            1,
            InteractionType.Dialogue,
            "scene_001",
            character.Id,
            null,
            new EmotionSpec("Calm", 0.6, string.Empty, string.Empty, string.Empty, string.Empty),
            new ActionSpec("台词表演", [], string.Empty, 2),
            new DialogueLine("dia_001", character.Id, null, "Hello", new VoiceRenderSettings(null, "Calm", 0.6, 1, 0.6)),
            null,
            null,
            new InteractionBinding(
                null,
                null,
                sceneVisualBinding,
                rowVisualBinding),
            [],
            string.Empty);
        var project = new InteractionProject(
            "project_structured_visual",
            "Structured Visual",
            new ScriptDocument("Hello", "Hello", []),
            [character],
            [
                new InteractionScene(
                    "scene_001",
                    1,
                    "Scene",
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    new SceneAssetBinding("asset_png_scene", BackgroundVisualBinding: sceneVisualBinding),
                    [interaction])
            ],
            [],
            []);

        await manager.SaveDraftAsync(WorkspaceProjectManager.ToWorkspace(manifest), project);
        var json = await File.ReadAllTextAsync(manifest.DraftPath);
        var loaded = await manager.LoadDraftAsync(WorkspaceProjectManager.ToWorkspace(manifest));

        Assert.Contains("backgroundVisualBinding", json);
        Assert.Contains("subjectCharacterVisualBinding", json);
        Assert.Contains("portraitVisualBinding", json);
        Assert.NotNull(loaded);
        var loadedScene = Assert.Single(loaded.Scenes);
        var loadedInteraction = Assert.Single(loadedScene.Interactions);
        var loadedCharacter = Assert.Single(loaded.Characters);
        AssertVisualBinding(sceneVisualBinding, loadedScene.AssetBinding.BackgroundVisualBinding);
        AssertVisualBinding(sceneVisualBinding, loadedInteraction.Binding.SceneVisualBinding);
        AssertVisualBinding(rowVisualBinding, loadedInteraction.Binding.SubjectCharacterVisualBinding);
        AssertVisualBinding(characterVisualBinding, loadedCharacter.AssetBinding.PortraitVisualBinding);
    }

    [Fact]
    public async Task WorkbenchTimelineAssembler_ProjectsWorkbenchRowsIntoEditingTimeline()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Timeline Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var imagePath = await CreateFileAsync(Path.Combine(workspace.Folders.ImagesImported, "scene_001.png"), [1]);
        var audioPath = await CreateFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav"), [2]);
        var project = BuildProjectWithAssets(imagePath, audioPath);
        var assembler = new WorkbenchTimelineAssembler();

        var timeline = assembler.Assemble(project, workspace, new TimelineExportOptions());

        Assert.Equal(project.ProjectId, timeline.Id);
        Assert.Equal(
            ["Subtitles", "Character Visual", "Character Dialogue Voice", "Character Thought Voice", "Character Action Voice", "Narration Scene Effect Visual", "Narration Scene Effect Voice", "Scene Visual"],
            timeline.Tracks.Select(track => track.Name).ToArray());
        var subtitleTrack = Assert.Single(timeline.Tracks, track => track.Kind == EditingTrackKind.Subtitle);
        var characterTrack = Assert.Single(timeline.Tracks, track => track.Name == "Character Visual");
        var characterVoiceTrack = Assert.Single(timeline.Tracks, track => track.Name == "Character Dialogue Voice");
        var characterThoughtVoiceTrack = Assert.Single(timeline.Tracks, track => track.Name == "Character Thought Voice");
        var characterActionVoiceTrack = Assert.Single(timeline.Tracks, track => track.Name == "Character Action Voice");
        var narrationSceneEffectVisualTrack = Assert.Single(timeline.Tracks, track => track.Name == "Narration Scene Effect Visual");
        var narrationSceneEffectVoiceTrack = Assert.Single(timeline.Tracks, track => track.Name == "Narration Scene Effect Voice");
        var sceneTrack = Assert.Single(timeline.Tracks, track => track.Name == "Scene Visual");
        var characterClip = Assert.Single(characterTrack.Clips);
        Assert.Equal("int_002", characterClip.Metadata["interactionId"]);
        Assert.Equal(imagePath, characterClip.MediaPath);
        Assert.Equal(4, characterClip.StartSeconds, 3);
        Assert.Equal(4.3, characterClip.DurationSeconds, 3);
        var sceneClip = Assert.Single(sceneTrack.Clips);
        Assert.Equal("scene_001", sceneClip.Metadata["sceneId"]);
        Assert.Equal(imagePath, sceneClip.MediaPath);
        Assert.Equal(8.3, sceneClip.DurationSeconds, 3);
        var dialogueClip = Assert.Single(characterVoiceTrack.Clips);
        Assert.Empty(characterThoughtVoiceTrack.Clips);
        Assert.Empty(characterActionVoiceTrack.Clips);
        Assert.Empty(narrationSceneEffectVisualTrack.Clips);
        Assert.Empty(narrationSceneEffectVoiceTrack.Clips);
        Assert.Equal("int_002", dialogueClip.Metadata["interactionId"]);
        Assert.Equal("Melancholic", dialogueClip.Metadata["emotion"]);
        Assert.Equal("tts_int_002", dialogueClip.Metadata["ttsTaskId"]);
        Assert.Equal(audioPath, dialogueClip.MediaPath);
        Assert.Equal(4, dialogueClip.StartSeconds, 3);
        var subtitleClip = Assert.Single(subtitleTrack.Clips);
        Assert.Equal("audio_int_002", subtitleClip.Metadata["sourceClipId"]);
        Assert.Equal("我来晚了。", subtitleClip.Metadata["text"]);
        Assert.Equal(4, subtitleClip.StartSeconds, 3);
        Assert.Equal(dialogueClip.DurationSeconds, subtitleClip.DurationSeconds, 3);
        Assert.Single(timeline.Markers);
        Assert.Equal("scene_001", timeline.Markers[0].Metadata["sceneId"]);
        Assert.Equal(8.3, timeline.Markers[0].DurationSeconds, 3);
    }

    [Fact]
    public async Task WorkbenchTimelineAssembler_UsesSynthesizedWavDurationForRowAndSceneSpan()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Timeline Audio Duration Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var imagePath = await CreateFileAsync(Path.Combine(workspace.Folders.ImagesImported, "scene_001.png"), [1]);
        var audioPath = await CreateWavFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav"), 2.5);
        var project = BuildProjectWithAssets(imagePath, audioPath);
        var assembler = new WorkbenchTimelineAssembler();

        var timeline = assembler.Assemble(project, workspace, new TimelineExportOptions());

        var characterClip = Assert.Single(timeline.Tracks.Single(track => track.Name == "Character Visual").Clips);
        var sceneClip = Assert.Single(timeline.Tracks.Single(track => track.Name == "Scene Visual").Clips);
        var dialogueClip = Assert.Single(timeline.Tracks.Single(track => track.Name == "Character Dialogue Voice").Clips);
        Assert.Equal(2.5, dialogueClip.DurationSeconds, 3);
        Assert.Equal(4, dialogueClip.StartSeconds, 3);
        Assert.Equal(2.5, characterClip.DurationSeconds, 3);
        Assert.Equal(6.5, sceneClip.DurationSeconds, 3);
        Assert.Equal("6.5", timeline.Metadata["durationSeconds"]);
    }

    [Fact]
    public async Task WorkbenchTimelineAssembler_UsesSynthesizedFlacDurationForRowAndSceneSpan()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Timeline Flac Duration Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var imagePath = await CreateFileAsync(Path.Combine(workspace.Folders.ImagesImported, "scene_001.png"), [1]);
        var audioPath = await CreateFlacHeaderFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.flac"), 3.25);
        var project = BuildProjectWithAssets(imagePath, audioPath);
        var assembler = new WorkbenchTimelineAssembler();

        var timeline = assembler.Assemble(project, workspace, new TimelineExportOptions());

        var characterClip = Assert.Single(timeline.Tracks.Single(track => track.Name == "Character Visual").Clips);
        var sceneClip = Assert.Single(timeline.Tracks.Single(track => track.Name == "Scene Visual").Clips);
        var dialogueClip = Assert.Single(timeline.Tracks.Single(track => track.Name == "Character Dialogue Voice").Clips);
        Assert.Equal(3.25, dialogueClip.DurationSeconds, 3);
        Assert.Equal(3.25, characterClip.DurationSeconds, 3);
        Assert.Equal(7.25, sceneClip.DurationSeconds, 3);
    }

    [Fact]
    public async Task WorkbenchTimelineAssembler_ProjectsNarrationAndEffectsRowImagesIntoDedicatedTracks()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Timeline Narration Visual Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var sceneImagePath = await CreateFileAsync(Path.Combine(workspace.Folders.ImagesImported, "scene_001.png"), [1]);
        var rowImagePath = await CreateFileAsync(Path.Combine(workspace.Folders.ImagesImported, "narration_row_001.png"), [2]);
        var dialogueAudioPath = await CreateWavFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav"), 2);
        var narrationAudioPath = await CreateWavFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "narration_001.wav"), 3);
        var baseProject = WithCompletedNarrationAudio(BuildProjectWithAssets(sceneImagePath, dialogueAudioPath), narrationAudioPath);
        var scene = baseProject.Scenes[0];
        var narration = scene.Interactions[0] with
        {
            SourceItemType = "narration",
            Binding = scene.Interactions[0].Binding with
            {
                SubjectCharacterVisualBinding = VisualAssetBinding.FromImage(rowImagePath)
            }
        };
        var project = baseProject with
        {
            Scenes =
            [
                scene with { Interactions = [narration, scene.Interactions[1]] }
            ]
        };
        var assembler = new WorkbenchTimelineAssembler();

        var timeline = assembler.Assemble(project, workspace, new TimelineExportOptions());

        var narrationVisualClip = Assert.Single(timeline.Tracks.Single(track => track.Name == "Narration Scene Effect Visual").Clips);
        var narrationVoiceClip = Assert.Single(timeline.Tracks.Single(track => track.Name == "Narration Scene Effect Voice").Clips);
        var sceneClip = Assert.Single(timeline.Tracks.Single(track => track.Name == "Scene Visual").Clips);
        Assert.Equal(rowImagePath, narrationVisualClip.MediaPath);
        Assert.Equal(narrationVoiceClip.StartSeconds, narrationVisualClip.StartSeconds);
        Assert.Equal(narrationVoiceClip.DurationSeconds, narrationVisualClip.DurationSeconds);
        Assert.Equal(sceneImagePath, sceneClip.MediaPath);
        Assert.Equal(5, sceneClip.DurationSeconds, 3);
    }

    [Fact]
    public async Task WorkbenchTimelineAssembler_RoutesCharacterThoughtAndActionRowsBySourceRole()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Timeline Source Role Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var fixture = await BuildSourceRoleTimelineFixtureAsync(workspace);
        var assembler = new WorkbenchTimelineAssembler();

        var timeline = assembler.Assemble(fixture.Project, workspace, new TimelineExportOptions());

        var characterVisualTrack = Assert.Single(timeline.Tracks, track => track.Name == "Character Visual");
        var thoughtVoiceTrack = Assert.Single(timeline.Tracks, track => track.Name == "Character Thought Voice");
        var actionVoiceTrack = Assert.Single(timeline.Tracks, track => track.Name == "Character Action Voice");
        var narrationVoiceTrack = Assert.Single(timeline.Tracks, track => track.Name == "Narration Scene Effect Voice");

        Assert.Equal(["int_thought", "int_action"], characterVisualTrack.Clips.Select(clip => clip.Metadata["interactionId"]).ToArray());
        Assert.All(characterVisualTrack.Clips, clip => Assert.Equal(fixture.CharacterImagePath, clip.MediaPath));
        Assert.Equal("int_thought", Assert.Single(thoughtVoiceTrack.Clips).Metadata["interactionId"]);
        Assert.Equal(fixture.ThoughtAudioPath, thoughtVoiceTrack.Clips[0].MediaPath);
        Assert.Equal("int_action", Assert.Single(actionVoiceTrack.Clips).Metadata["interactionId"]);
        Assert.Equal(fixture.ActionAudioPath, actionVoiceTrack.Clips[0].MediaPath);
        Assert.Equal("int_narration", Assert.Single(narrationVoiceTrack.Clips).Metadata["interactionId"]);
        Assert.Equal(fixture.NarrationAudioPath, narrationVoiceTrack.Clips[0].MediaPath);
    }

    [Fact]
    public async Task WorkbenchTimelineAssembler_UsesRenderedCharacterPsdForSourceRoleRows()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Timeline Source Role PSD Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var fixture = await BuildSourceRoleTimelineFixtureAsync(workspace);
        var mediaService = new ProjectMediaAssetService();
        var psdSource = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-character-{Guid.NewGuid():N}.psd"), [1]);
        var renderedPngSource = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-character-rendered-{Guid.NewGuid():N}.png"), [2]);
        var psdImport = await mediaService.ImportAsync(workspace, new MediaImportRequest(psdSource, MediaAssetKind.Psd, MediaAssetSource.ImportedPsd, "char_chen"));
        var renderedImport = await mediaService.ImportAsync(workspace, new MediaImportRequest(renderedPngSource, MediaAssetKind.Image, MediaAssetSource.ImportedPng, "char_chen"));
        var psdBinding = VisualAssetBinding.FromPsd(psdImport.Asset.Id, renderedImport.Asset.Id);
        var character = fixture.Project.Characters.Single();
        var project = fixture.Project with
        {
            Characters =
            [
                character with
                {
                    AssetBinding = character.AssetBinding with
                    {
                        Portrait = renderedImport.Asset.Id,
                        PortraitVisualBinding = psdBinding
                    }
                }
            ]
        };
        var assembler = new WorkbenchTimelineAssembler();

        var timeline = assembler.Assemble(project, workspace, new TimelineExportOptions(), renderedImport.Manifest);

        var characterVisualTrack = Assert.Single(timeline.Tracks, track => track.Name == "Character Visual");
        Assert.Equal(["int_thought", "int_action"], characterVisualTrack.Clips.Select(clip => clip.Metadata["interactionId"]).ToArray());
        Assert.All(characterVisualTrack.Clips, clip => Assert.Equal(renderedImport.Asset.AbsolutePath, clip.MediaPath));
        Assert.DoesNotContain(characterVisualTrack.Clips, clip => string.Equals(psdImport.Asset.AbsolutePath, clip.MediaPath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task EditingProjectExportService_JianyingDraftKeepsCharacterThoughtActionAndNarrationVoiceTracksSeparate()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Jianying Source Role Track Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var fixture = await BuildSourceRoleTimelineFixtureAsync(workspace);
        var outputDirectory = Path.Combine(workspace.Folders.Exports, "jianying-source-role-tracks");
        var service = new EditingProjectExportService();

        await service.ExportAsync(new EditingProjectExportRequest(
            fixture.Project,
            workspace,
            EditingProjectFormat.JianyingDraft,
            new TimelineExportOptions(),
            manifest,
            outputDirectory));

        using var draftContent = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "draft_content.json"), Encoding.UTF8));
        var tracks = draftContent.RootElement.GetProperty("tracks").EnumerateArray().ToArray();
        Assert.Equal(
            ["字幕", "Character Visual", "Character Dialogue Voice", "Character Thought Voice", "Character Action Voice", "Narration Scene Effect Visual", "Narration Scene Effect Voice", "Scene Visual"],
            tracks.Select(track => track.GetProperty("name").GetString() ?? string.Empty).ToArray());
        Assert.Equal([fixture.ThoughtAudioPath], DraftAudioPathsForTrack(draftContent.RootElement, "Character Thought Voice"));
        Assert.Equal([fixture.ActionAudioPath], DraftAudioPathsForTrack(draftContent.RootElement, "Character Action Voice"));
        Assert.Equal([fixture.NarrationAudioPath], DraftAudioPathsForTrack(draftContent.RootElement, "Narration Scene Effect Voice"));
        Assert.Equal(2, TrackByName(draftContent.RootElement, "Character Visual").GetProperty("segments").GetArrayLength());
    }

    [Fact]
    public async Task EditingProjectExportService_ExportsOtioFromTimelineProjection()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "OTIO Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var imagePath = await CreateFileAsync(Path.Combine(workspace.Folders.ImagesImported, "scene_001.png"), [1]);
        var audioPath = await CreateFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav"), [2]);
        var project = BuildProjectWithAssets(imagePath, audioPath);
        var service = new EditingProjectExportService();

        var result = await service.ExportAsync(new EditingProjectExportRequest(project, workspace, EditingProjectFormat.Otio, new TimelineExportOptions()));

        Assert.True(File.Exists(result.OutputPath));
        Assert.Contains(imagePath, result.ReferencedAssetPaths);
        Assert.Contains(audioPath, result.ReferencedAssetPaths);
        var json = await File.ReadAllTextAsync(result.OutputPath);
        Assert.Contains("\"OTIO_SCHEMA\": \"Timeline.1\"", json);
        Assert.Contains("\"OTIO_SCHEMA\": \"Track.1\"", json);
        Assert.Contains("\"target_url\": \"../assets/images/imported/scene_001.png\"", json);
        Assert.Contains("\"target_url\": \"../assets/audio/generated/line_001.wav\"", json);

        using var document = JsonDocument.Parse(json);
        var otioTracks = document.RootElement
            .GetProperty("tracks")
            .GetProperty("children");
        Assert.Equal(8, otioTracks.GetArrayLength());
        Assert.Equal(
            ["Subtitles", "Character Visual", "Character Dialogue Voice", "Character Thought Voice", "Character Action Voice", "Narration Scene Effect Visual", "Narration Scene Effect Voice", "Scene Visual"],
            otioTracks.EnumerateArray().Select(track => track.GetProperty("name").GetString() ?? string.Empty).ToArray());
        Assert.Equal("Text", otioTracks[0].GetProperty("kind").GetString());
        Assert.Equal("Video", otioTracks[1].GetProperty("kind").GetString());
        Assert.Equal("Audio", otioTracks[2].GetProperty("kind").GetString());
        Assert.Equal("Audio", otioTracks[3].GetProperty("kind").GetString());
        Assert.Equal("Audio", otioTracks[4].GetProperty("kind").GetString());
        Assert.Equal("Video", otioTracks[5].GetProperty("kind").GetString());
        Assert.Equal("Audio", otioTracks[6].GetProperty("kind").GetString());
        Assert.Equal("Video", otioTracks[7].GetProperty("kind").GetString());

        var subtitleChildren = otioTracks[0].GetProperty("children");
        Assert.Equal("Gap.1", subtitleChildren[0].GetProperty("OTIO_SCHEMA").GetString());
        Assert.Equal("Clip.2", subtitleChildren[1].GetProperty("OTIO_SCHEMA").GetString());
        Assert.Contains("我来晚了。", subtitleChildren[1].GetProperty("metadata").GetProperty("draft_agent").GetProperty("text").GetString());

        var characterVideoChildren = document.RootElement
            .GetProperty("tracks")
            .GetProperty("children")[1]
            .GetProperty("children");
        Assert.Equal(2, characterVideoChildren.GetArrayLength());
        Assert.Equal("Gap.1", characterVideoChildren[0].GetProperty("OTIO_SCHEMA").GetString());
        Assert.Contains("002", characterVideoChildren[1].GetProperty("name").GetString());

        var sceneVideoChildren = document.RootElement
            .GetProperty("tracks")
            .GetProperty("children")[7]
            .GetProperty("children");
        Assert.Single(sceneVideoChildren.EnumerateArray());
        Assert.Contains("scene_001", sceneVideoChildren[0].GetProperty("metadata").GetProperty("draft_agent").GetProperty("sceneId").GetString());

        var dialogueChildren = document.RootElement
            .GetProperty("tracks")
            .GetProperty("children")[2]
            .GetProperty("children");
        Assert.True(dialogueChildren.GetArrayLength() >= 2);
        Assert.Equal("Gap.1", dialogueChildren[0].GetProperty("OTIO_SCHEMA").GetString());
        Assert.Equal("Clip.2", dialogueChildren[1].GetProperty("OTIO_SCHEMA").GetString());
    }

    [Fact]
    public async Task EditingProjectExportService_ExportsOtioWhenWorkbenchBindingsUseAssetIds()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "OTIO Asset Id Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var mediaService = new ProjectMediaAssetService();
        var imageSource = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-{Guid.NewGuid():N}.png"), [1]);
        var audioSource = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-{Guid.NewGuid():N}.wav"), [2]);
        var imageImport = await mediaService.ImportAsync(workspace, new MediaImportRequest(imageSource, MediaAssetKind.Image, MediaAssetSource.ImportedPng, "scene_001"));
        var audioImport = await mediaService.ImportAsync(workspace, new MediaImportRequest(audioSource, MediaAssetKind.Audio, MediaAssetSource.TtsGenerated, "int_002"));
        var project = BuildProjectWithAssets(imageImport.Asset.Id, audioImport.Asset.Id);
        var service = new EditingProjectExportService();

        var result = await service.ExportAsync(new EditingProjectExportRequest(project, workspace, EditingProjectFormat.Otio, new TimelineExportOptions(), audioImport.Manifest));

        Assert.Contains(imageImport.Asset.AbsolutePath, result.ReferencedAssetPaths);
        Assert.Contains(audioImport.Asset.AbsolutePath, result.ReferencedAssetPaths);
        var json = await File.ReadAllTextAsync(result.OutputPath);
        Assert.Contains($"\"target_url\": \"../{imageImport.Asset.ProjectRelativePath}\"", json);
        Assert.Contains($"\"target_url\": \"../{audioImport.Asset.ProjectRelativePath}\"", json);
        Assert.DoesNotContain("\"target_url\": \"../assets/images/imported/scene_001_", json);
        Assert.DoesNotContain("\"target_url\": \"../assets/audio/generated/int_002_", json);
    }

    [Fact]
    public async Task EditingProjectExportService_ExportsFcpxmlAndPerAudioSrt()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "FCPXML Export Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var imagePath = await CreateFileAsync(Path.Combine(workspace.Folders.ImagesImported, "scene_001.png"), [1]);
        var audioPath = await CreateWavFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav"), 2);
        var narrationAudioPath = await CreateWavFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "narration_001.wav"), 4);
        var project = WithCompletedNarrationAudio(BuildProjectWithAssets(imagePath, audioPath), narrationAudioPath);
        var outputPath = Path.Combine(workspace.Folders.Exports, "rain.fcpxml");
        var service = new EditingProjectExportService();

        var result = await service.ExportAsync(new EditingProjectExportRequest(
            project,
            workspace,
            EditingProjectFormat.Fcpxml,
            new TimelineExportOptions(),
            manifest,
            outputPath));

        Assert.Equal("fcpxml", result.Format);
        Assert.Equal(outputPath, result.OutputPath);
        Assert.True(File.Exists(outputPath));
        var xml = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("<fcpxml", xml);
        Assert.Contains("<asset-clip", xml);
        Assert.Contains("<caption", xml);
        Assert.Contains("我来晚了。", xml);
        Assert.Contains("scene_001.png", xml);
        Assert.Contains("line_001.wav", xml);
        Assert.Contains("narration_001.wav", xml);
        var document = XDocument.Parse(xml);
        var sequence = Assert.Single(document.Descendants("sequence"));
        Assert.Equal("r_format_timeline", sequence.Attribute("format")?.Value);
        Assert.Equal("144144/24000s", sequence.Attribute("duration")?.Value);
        Assert.Equal("0s", sequence.Attribute("tcStart")?.Value);
        Assert.Equal("NDF", sequence.Attribute("tcFormat")?.Value);
        Assert.Equal("stereo", sequence.Attribute("audioLayout")?.Value);
        Assert.Equal("48k", sequence.Attribute("audioRate")?.Value);
        AssertFcpxmlFrameTime(sequence.Attribute("duration")?.Value);

        var formats = document.Descendants("format").ToArray();
        Assert.Contains(formats, format =>
            format.Attribute("id")?.Value == "r_format_timeline" &&
            format.Attribute("frameDuration")?.Value == "1001/24000s" &&
            format.Attribute("fieldOrder")?.Value == "progressive" &&
            format.Attribute("width")?.Value == "1920" &&
            format.Attribute("height")?.Value == "1080" &&
            format.Attribute("colorSpace")?.Value == "1-1-1 (Rec. 709)");
        Assert.Contains(formats, format =>
            format.Attribute("id")?.Value == "r_format_image" &&
            format.Attribute("frameDuration") is null &&
            format.Attribute("fieldOrder")?.Value == "progressive");

        var imageAsset = document.Descendants("asset").Single(asset => asset.Attribute("name")?.Value == "scene_001.png");
        Assert.Equal("1", imageAsset.Attribute("hasVideo")?.Value);
        Assert.Equal("1", imageAsset.Attribute("videoSources")?.Value);
        Assert.Equal("0s", imageAsset.Attribute("duration")?.Value);
        Assert.Equal("r_format_image", imageAsset.Attribute("format")?.Value);
        Assert.Null(imageAsset.Attribute("hasAudio"));
        Assert.Null(imageAsset.Attribute("src"));
        var imageMediaRep = Assert.Single(imageAsset.Elements("media-rep"));
        Assert.Equal("original-media", imageMediaRep.Attribute("kind")?.Value);
        Assert.StartsWith("file://", imageMediaRep.Attribute("src")?.Value);

        var audioAsset = document.Descendants("asset").Single(asset => asset.Attribute("name")?.Value == "line_001.wav");
        Assert.Equal("1", audioAsset.Attribute("hasAudio")?.Value);
        Assert.Equal("1", audioAsset.Attribute("audioSources")?.Value);
        Assert.Equal("1", audioAsset.Attribute("audioChannels")?.Value);
        Assert.Equal("16000", audioAsset.Attribute("audioRate")?.Value);
        Assert.Null(audioAsset.Attribute("hasVideo"));
        Assert.Null(audioAsset.Attribute("format"));
        Assert.Null(audioAsset.Attribute("src"));
        AssertFcpxmlFrameTime(audioAsset.Attribute("duration")?.Value);
        var audioMediaRep = Assert.Single(audioAsset.Elements("media-rep"));
        Assert.Equal("original-media", audioMediaRep.Attribute("kind")?.Value);
        Assert.StartsWith("file://", audioMediaRep.Attribute("src")?.Value);
        var srtPath = Path.Combine(workspace.Folders.Exports, "srt", "audio_int_002.srt");
        Assert.True(File.Exists(srtPath));
        Assert.Contains("我来晚了。", await File.ReadAllTextAsync(srtPath));
        Assert.True(File.Exists(Path.Combine(workspace.Folders.Exports, "srt", "audio_int_001.srt")));
        Assert.Contains(srtPath, result.ReferencedAssetPaths);
    }

    [Fact]
    public async Task EditingProjectExportService_ExportsParseableFcpxmlWhenTextContainsXmlSpecialCharacters()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "FCPXML XML Escape Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var imagePath = await CreateFileAsync(Path.Combine(workspace.Folders.ImagesImported, "scene_001.png"), [1]);
        var audioPath = await CreateWavFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav"), 2);
        var narrationAudioPath = await CreateWavFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "narration_001.wav"), 4);
        var project = WithCompletedNarrationAudio(BuildProjectWithAssets(imagePath, audioPath), narrationAudioPath);
        var character = project.Characters.Single();
        var scene = project.Scenes.Single();
        var narration = scene.Interactions.Single(interaction => interaction.Narration is not null);
        var dialogue = scene.Interactions.Single(interaction => interaction.Dialogue is not null);
        var specialText = "陈默说：A&B <C> \"D\"";
        project = project with
        {
            Title = "Rain & <Storm> \"Story\"",
            Characters = [character with { Name = "陈&默<主角>" }],
            Scenes =
            [
                scene with
                {
                    Name = "雨夜 & <街口>",
                    Interactions =
                    [
                        narration with { Narration = narration.Narration! with { Text = "旁白 & <开场>" } },
                        dialogue with { Dialogue = dialogue.Dialogue! with { Text = specialText } }
                    ]
                }
            ]
        };
        var outputPath = Path.Combine(workspace.Folders.Exports, "rain-special.fcpxml");
        var service = new EditingProjectExportService();

        await service.ExportAsync(new EditingProjectExportRequest(
            project,
            workspace,
            EditingProjectFormat.Fcpxml,
            new TimelineExportOptions(),
            manifest,
            outputPath));

        var xml = await File.ReadAllTextAsync(outputPath);
        var document = XDocument.Parse(xml);
        var captionTexts = document
            .Descendants("text-style")
            .Select(element => element.Value)
            .ToArray();

        Assert.Equal("fcpxml", document.Root?.Name.LocalName);
        Assert.Contains(specialText, captionTexts);
        Assert.Contains("Rain &amp; &lt;Storm&gt; &quot;Story&quot;", xml);
        Assert.Contains("陈&amp;默&lt;主角&gt; 台词", xml);
        Assert.DoesNotContain("<text-style ref=\"ts_subtitle\">陈默说：A&B", xml, StringComparison.Ordinal);
        var srtPath = Path.Combine(workspace.Folders.Exports, "srt", "audio_int_002.srt");
        Assert.Contains(specialText, await File.ReadAllTextAsync(srtPath));
    }

    [Fact]
    public async Task EditingProjectExportService_FcpxmlUsesSplitSubtitleTrackForLongText()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "FCPXML Split Subtitle Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var imagePath = await CreateFileAsync(Path.Combine(workspace.Folders.ImagesImported, "scene_001.png"), [1]);
        var audioPath = await CreateWavFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav"), 6);
        var project = DialogueOnlyProject(BuildProjectWithAssets(imagePath, audioPath));
        var longText = "一二三四五六七八九十一二三四五六七八九十一二三四五六七八九十一二三四五六";
        var dialogue = project.Scenes.Single().Interactions.Single();
        project = project with
        {
            Scenes =
            [
                project.Scenes.Single() with
                {
                    Interactions =
                    [
                        dialogue with
                        {
                            Dialogue = dialogue.Dialogue! with { Text = longText }
                        }
                    ]
                }
            ],
            GenerationTasks =
            [
                project.GenerationTasks.Single() with
                {
                    Parameters = new Dictionary<string, string> { ["text"] = longText, ["emotion"] = "Calm" }
                }
            ]
        };
        var outputPath = Path.Combine(workspace.Folders.Exports, "rain-split-subtitle.fcpxml");
        var service = new EditingProjectExportService();

        await service.ExportAsync(new EditingProjectExportRequest(
            project,
            workspace,
            EditingProjectFormat.Fcpxml,
            new TimelineExportOptions(),
            manifest,
            outputPath));

        var document = XDocument.Parse(await File.ReadAllTextAsync(outputPath));
        var captions = document.Descendants("caption").ToArray();

        Assert.Equal(2, captions.Length);
        Assert.Contains(captions, caption =>
            caption.Attribute("offset")?.Value == "0s" &&
            caption.Attribute("duration")?.Value == "72072/24000s" &&
            caption.Value.Contains("一二三四五六七八九十一二三四五六七八九十一二三四", StringComparison.Ordinal));
        Assert.Contains(captions, caption =>
            caption.Attribute("offset")?.Value == "72072/24000s" &&
            caption.Attribute("duration")?.Value == "72072/24000s" &&
            caption.Value.Contains("五六七八九十", StringComparison.Ordinal));
        var srtPath = Path.Combine(workspace.Folders.Exports, "srt", "audio_int_002.srt");
        var srt = await File.ReadAllTextAsync(srtPath);
        Assert.Contains("00:00:00,000 --> 00:00:03,000", srt);
        Assert.Contains("00:00:03,000 --> 00:00:06,000", srt);
    }

    [Fact]
    public async Task EditingProjectExportService_ExportsFcpxmlLayeredVideoAndAudioLanes()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "FCPXML Lane Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var imagePath = await CreateFileAsync(Path.Combine(workspace.Folders.ImagesImported, "scene_001.png"), [1]);
        var audioPath = await CreateWavFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav"), 2);
        var narrationAudioPath = await CreateWavFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "narration_001.wav"), 4);
        var project = WithCompletedNarrationAudio(BuildProjectWithAssets(imagePath, audioPath), narrationAudioPath);
        var outputPath = Path.Combine(workspace.Folders.Exports, "rain-lanes.fcpxml");
        var service = new EditingProjectExportService();

        await service.ExportAsync(new EditingProjectExportRequest(
            project,
            workspace,
            EditingProjectFormat.Fcpxml,
            new TimelineExportOptions(),
            manifest,
            outputPath));

        var xml = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("lane=\"3\"", xml);
        Assert.Contains("audioRole=\"dialogue\"", xml);
        Assert.Contains("<caption", xml);
        Assert.Contains("lane=\"1\"", xml);

        var document = XDocument.Parse(xml);
        var spine = Assert.Single(document.Descendants("spine"));
        var primaryGap = Assert.Single(spine.Elements("gap"));
        Assert.Equal("Draft Agent Timeline", primaryGap.Attribute("name")?.Value);
        Assert.Equal("0s", primaryGap.Attribute("offset")?.Value);
        Assert.Equal("144144/24000s", primaryGap.Attribute("duration")?.Value);
        Assert.Empty(spine.Elements("asset-clip"));
        Assert.Empty(spine.Elements("caption"));
        var audioClips = document
            .Descendants("asset-clip")
            .Where(element => element.Attribute("audioRole") is not null)
            .ToArray();
        var sceneClip = document
            .Descendants("asset-clip")
            .Single(element => element.Attribute("lane")?.Value == "8");
        Assert.StartsWith("01 ", sceneClip.Attribute("name")?.Value, StringComparison.Ordinal);
        Assert.Equal("0s", sceneClip.Attribute("offset")?.Value);
        Assert.Equal("144144/24000s", sceneClip.Attribute("duration")?.Value);
        Assert.Equal("0s", sceneClip.Attribute("start")?.Value);
        var characterClip = document
            .Descendants("asset-clip")
            .Single(element => element.Attribute("lane")?.Value == "2");
        Assert.StartsWith("01-002 ", characterClip.Attribute("name")?.Value, StringComparison.Ordinal);
        Assert.Equal("96096/24000s", characterClip.Attribute("offset")?.Value);
        Assert.Equal("48048/24000s", characterClip.Attribute("duration")?.Value);
        Assert.Equal("0s", characterClip.Attribute("start")?.Value);
        Assert.Equal(2, audioClips.Length);
        var dialogueAudioClip = Assert.Single(audioClips, clip => clip.Attribute("lane")?.Value == "3");
        var narrationAudioClip = Assert.Single(audioClips, clip => clip.Attribute("lane")?.Value == "7");
        Assert.Equal("96096/24000s", dialogueAudioClip.Attribute("offset")?.Value);
        Assert.Equal("48048/24000s", dialogueAudioClip.Attribute("duration")?.Value);
        Assert.Equal("0s", narrationAudioClip.Attribute("offset")?.Value);
        Assert.Equal("96096/24000s", narrationAudioClip.Attribute("duration")?.Value);
        Assert.All(audioClips, clip => AssertFcpxmlFrameTime(clip.Attribute("duration")?.Value));

        var captions = document.Descendants("caption").ToArray();
        Assert.Equal(2, captions.Length);
        Assert.Contains(captions, caption =>
            caption.Attribute("offset")?.Value == "0s" &&
            caption.Attribute("duration")?.Value == "96096/24000s" &&
            caption.Value.Contains("陈默走进雨里。", StringComparison.Ordinal));
        Assert.Contains(captions, caption =>
            caption.Attribute("offset")?.Value == "96096/24000s" &&
            caption.Attribute("duration")?.Value == "48048/24000s" &&
            caption.Value.Contains("我来晚了。", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WorkbenchTimelineAssembler_SplitsLongSubtitleTextIntoSubtitleTrack()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Subtitle Track Split Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var imagePath = await CreateFileAsync(Path.Combine(workspace.Folders.ImagesImported, "scene_001.png"), [1]);
        var audioPath = await CreateWavFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav"), 6);
        var project = DialogueOnlyProject(BuildProjectWithAssets(imagePath, audioPath));
        var longText = "一二三四五六七八九十一二三四五六七八九十一二三四五六七八九十一二三四五六";
        var dialogue = project.Scenes.Single().Interactions.Single();
        project = project with
        {
            Scenes =
            [
                project.Scenes.Single() with
                {
                    Interactions =
                    [
                        dialogue with
                        {
                            Dialogue = dialogue.Dialogue! with { Text = longText }
                        }
                    ]
                }
            ],
            GenerationTasks =
            [
                project.GenerationTasks.Single() with
                {
                    Parameters = new Dictionary<string, string> { ["text"] = longText, ["emotion"] = "Calm" }
                }
            ]
        };

        var timeline = new WorkbenchTimelineAssembler().Assemble(project, workspace, new TimelineExportOptions(), manifest);

        var subtitleTrack = Assert.Single(timeline.Tracks, track => track.Kind == EditingTrackKind.Subtitle);
        Assert.Equal("Subtitles", subtitleTrack.Name);
        Assert.Equal(2, subtitleTrack.Clips.Count);
        Assert.Equal("一二三四五六七八九十一二三四五六七八九十一二三四", subtitleTrack.Clips[0].Metadata["text"]);
        Assert.Equal("五六七八九十一二三四五六", subtitleTrack.Clips[1].Metadata["text"]);
        Assert.Equal(0, subtitleTrack.Clips[0].StartSeconds);
        Assert.Equal(3, subtitleTrack.Clips[1].StartSeconds);
        Assert.Equal(3, subtitleTrack.Clips[0].DurationSeconds);
        Assert.Equal(3, subtitleTrack.Clips[1].DurationSeconds);
        Assert.Equal("audio_int_002", subtitleTrack.Clips[0].Metadata["sourceClipId"]);
    }

    [Fact]
    public async Task EditingProjectExportService_FcpxmlUsesPerRowRenderedPsdPngsForCharacterVisualSwitches()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "FCPXML PSD Row Switch Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var mediaService = new ProjectMediaAssetService();
        var sceneImagePath = await CreateFileAsync(Path.Combine(workspace.Folders.ImagesImported, "scene_001.png"), [1]);
        var psdSource = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-character-{Guid.NewGuid():N}.psd"), [2]);
        var sadPngSource = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-sad-{Guid.NewGuid():N}.png"), [3]);
        var reachPngSource = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-reach-{Guid.NewGuid():N}.png"), [4]);
        var psdImport = await mediaService.ImportAsync(workspace, new MediaImportRequest(psdSource, MediaAssetKind.Psd, MediaAssetSource.ImportedPsd, "char_chen"));
        var sadImport = await mediaService.ImportAsync(workspace, new MediaImportRequest(sadPngSource, MediaAssetKind.Image, MediaAssetSource.ImportedPng, "int_001"));
        var reachImport = await mediaService.ImportAsync(workspace, new MediaImportRequest(reachPngSource, MediaAssetKind.Image, MediaAssetSource.ImportedPng, "int_002"));
        var thoughtAudioPath = await CreateWavFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "thought.wav"), 3);
        var dialogueAudioPath = await CreateWavFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "dialogue.wav"), 2);
        var project = BuildProjectWithAssets(sceneImagePath, dialogueAudioPath);
        var character = project.Characters.Single();
        var scene = project.Scenes.Single();
        var narration = scene.Interactions.Single(interaction => interaction.Narration is not null);
        var dialogue = scene.Interactions.Single(interaction => interaction.Dialogue is not null);
        var thought = narration with
        {
            Type = InteractionType.InnerMonologue,
            SubjectCharacterId = character.Id,
            InnerMonologue = new InnerMonologue(
                "inner_001",
                character.Id,
                "我错了。",
                new VoiceRenderSettings(null, "Sad", 0.9, 1, 0)),
            Narration = null,
            Binding = narration.Binding with
            {
                SubjectCharacterVisualBinding = VisualAssetBinding.FromPsd(
                    psdImport.Asset.Id,
                    sadImport.Asset.Id,
                    new PsdRenderSelection([1], ["难过"], null, "Sad", null, VisualExportBounds.CanvasBounds, "psd", "sad"))
            }
        };
        dialogue = dialogue with
        {
            Binding = dialogue.Binding with
            {
                SubjectCharacterVisualBinding = VisualAssetBinding.FromPsd(
                    psdImport.Asset.Id,
                    reachImport.Asset.Id,
                    new PsdRenderSelection([2], ["伸手"], null, "Melancholic", "伸手", VisualExportBounds.CanvasBounds, "psd", "reach"))
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
                        Portrait = null,
                        PortraitVisualBinding = VisualAssetBinding.FromPsd(psdImport.Asset.Id)
                    }
                }
            ],
            Scenes =
            [
                scene with
                {
                    Interactions = [thought, dialogue]
                }
            ],
            GenerationTasks =
            [
                new GenerationTask(
                    "tts_int_001",
                    GenerationTaskType.Tts,
                    thought.Id,
                    "TTS 内心",
                    "comfyui",
                    GenerationTaskStatus.Completed,
                    new Dictionary<string, string> { ["text"] = "我错了。", ["emotion"] = "Sad" },
                    thoughtAudioPath),
                new GenerationTask(
                    "tts_int_002",
                    GenerationTaskType.Tts,
                    dialogue.Id,
                    "TTS 台词",
                    "comfyui",
                    GenerationTaskStatus.Completed,
                    new Dictionary<string, string> { ["text"] = "我来晚了。", ["emotion"] = "Melancholic" },
                    dialogueAudioPath)
            ]
        };
        var outputPath = Path.Combine(workspace.Folders.Exports, "psd-row-switch.fcpxml");
        var service = new EditingProjectExportService();

        var result = await service.ExportAsync(new EditingProjectExportRequest(
            project,
            workspace,
            EditingProjectFormat.Fcpxml,
            new TimelineExportOptions(),
            reachImport.Manifest,
            outputPath));

        var xml = await File.ReadAllTextAsync(result.OutputPath);
        Assert.Contains(sadImport.Asset.AbsolutePath, result.ReferencedAssetPaths);
        Assert.Contains(reachImport.Asset.AbsolutePath, result.ReferencedAssetPaths);
        Assert.DoesNotContain(psdImport.Asset.AbsolutePath, result.ReferencedAssetPaths);
        Assert.Contains(Path.GetFileName(sadImport.Asset.AbsolutePath), xml);
        Assert.Contains(Path.GetFileName(reachImport.Asset.AbsolutePath), xml);
        Assert.DoesNotContain(Path.GetFileName(psdImport.Asset.AbsolutePath), xml);

        var document = XDocument.Parse(xml);
        var characterClips = document
            .Descendants("asset-clip")
            .Where(element => element.Attribute("lane")?.Value == "2")
            .ToArray();
        Assert.Equal(2, characterClips.Length);
        Assert.Equal("0s", characterClips[0].Attribute("offset")?.Value);
        Assert.Equal("72072/24000s", characterClips[0].Attribute("duration")?.Value);
        Assert.Equal("72072/24000s", characterClips[1].Attribute("offset")?.Value);
        Assert.Equal("48048/24000s", characterClips[1].Attribute("duration")?.Value);
        Assert.NotEqual(characterClips[0].Attribute("ref")?.Value, characterClips[1].Attribute("ref")?.Value);
        var audioClips = document.Descendants("asset-clip")
            .Where(element => element.Attribute("audioRole") is not null)
            .ToArray();
        Assert.Contains(audioClips, clip => clip.Attribute("lane")?.Value == "3" && clip.Attribute("name")?.Value.Contains("台词", StringComparison.Ordinal) == true);
        Assert.Contains(audioClips, clip => clip.Attribute("lane")?.Value == "4" && clip.Attribute("name")?.Value.Contains("台词", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task EditingProjectExportService_ExportsFcpxmlDialogueAndNarrationAudioRoles()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "FCPXML Audio Roles Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var imagePath = await CreateFileAsync(Path.Combine(workspace.Folders.ImagesImported, "scene_001.png"), [1]);
        var dialogueAudioPath = await CreateWavFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "dialogue_001.wav"), 2);
        var narrationAudioPath = await CreateWavFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "narration_001.wav"), 4);
        var project = BuildProjectWithAssets(imagePath, dialogueAudioPath);
        project = project with
        {
            GenerationTasks =
            [
                ..project.GenerationTasks,
                new GenerationTask(
                    "tts_int_001",
                    GenerationTaskType.Tts,
                    "int_001",
                    "TTS 旁白",
                    "comfyui",
                    GenerationTaskStatus.Completed,
                    new Dictionary<string, string> { ["text"] = "陈默走进雨里。", ["emotion"] = "Calm" },
                    narrationAudioPath)
            ]
        };
        var outputPath = Path.Combine(workspace.Folders.Exports, "rain-audio-roles.fcpxml");
        var service = new EditingProjectExportService();

        var result = await service.ExportAsync(new EditingProjectExportRequest(
            project,
            workspace,
            EditingProjectFormat.Fcpxml,
            new TimelineExportOptions(),
            manifest,
            outputPath));

        var xml = await File.ReadAllTextAsync(outputPath);
        Assert.Equal("fcpxml", result.Format);
        Assert.Contains(dialogueAudioPath, result.ReferencedAssetPaths);
        Assert.Contains(narrationAudioPath, result.ReferencedAssetPaths);
        Assert.Contains("audioRole=\"dialogue\"", xml);
        Assert.Contains("audioRole=\"voiceover\"", xml);
        Assert.Contains("旁白 陈默走进雨里。", xml);
        Assert.Contains("陈默 台词", xml);
        Assert.Contains("陈默走进雨里。", xml);
        Assert.Contains("我来晚了。", xml);
        Assert.True(File.Exists(Path.Combine(workspace.Folders.Exports, "srt", "audio_int_001.srt")));
        Assert.True(File.Exists(Path.Combine(workspace.Folders.Exports, "srt", "audio_int_002.srt")));
    }

    [Fact]
    public async Task EditingProjectExportService_ExportsJianyingDraftAndPerAudioSrt()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Jianying Export Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var imagePath = await CreateFileAsync(Path.Combine(workspace.Folders.ImagesImported, "scene_001.png"), [1]);
        var audioPath = await CreateFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav"), [2]);
        var project = BuildProjectWithAssets(imagePath, audioPath);
        var outputDirectory = Path.Combine(workspace.Folders.Exports, "jianying-draft");
        var service = new EditingProjectExportService();

        var result = await service.ExportAsync(new EditingProjectExportRequest(
            project,
            workspace,
            EditingProjectFormat.JianyingDraft,
            new TimelineExportOptions(),
            manifest,
            outputDirectory));

        Assert.Equal("jianying_draft", result.Format);
        Assert.Equal(outputDirectory, result.OutputPath);
        Assert.Contains("剪映草稿 JSON", result.Message);
        var draftContentPath = Path.Combine(outputDirectory, "draft_content.json");
        Assert.True(File.Exists(draftContentPath));
        var json = await File.ReadAllTextAsync(draftContentPath);
        using var draftContent = JsonDocument.Parse(json);
        Assert.Equal(1920, draftContent.RootElement.GetProperty("canvas_config").GetProperty("width").GetInt32());
        Assert.Equal(1080, draftContent.RootElement.GetProperty("canvas_config").GetProperty("height").GetInt32());
        Assert.Equal("110.0.0", draftContent.RootElement.GetProperty("new_version").GetString());
        Assert.Equal(360000, draftContent.RootElement.GetProperty("version").GetInt32());
        var materials = draftContent.RootElement.GetProperty("materials");
        Assert.True(materials.TryGetProperty("videos", out var videos));
        Assert.True(materials.TryGetProperty("audios", out var audios));
        Assert.True(materials.TryGetProperty("texts", out var texts));
        Assert.True(materials.TryGetProperty("speeds", out var speeds));
        Assert.True(videos.GetArrayLength() > 0);
        Assert.True(audios.GetArrayLength() > 0);
        Assert.True(texts.GetArrayLength() > 0);
        Assert.True(speeds.GetArrayLength() > 0);
        Assert.Equal(Path.GetFullPath(audioPath), audios[0].GetProperty("path").GetString());
        Assert.Equal("extract_music", audios[0].GetProperty("type").GetString());
        Assert.Equal(Path.GetFullPath(imagePath), videos[0].GetProperty("path").GetString());
        Assert.Equal("photo", videos[0].GetProperty("type").GetString());
        Assert.Equal("subtitle", texts[0].GetProperty("type").GetString());
        Assert.Contains("我来晚了。", texts[0].GetProperty("content").GetString());
        var tracks = draftContent.RootElement.GetProperty("tracks");
        Assert.Contains(tracks.EnumerateArray(), track => track.GetProperty("type").GetString() == "video");
        Assert.Contains(tracks.EnumerateArray(), track => track.GetProperty("type").GetString() == "audio");
        Assert.Contains(tracks.EnumerateArray(), track => track.GetProperty("type").GetString() == "text");
        Assert.DoesNotContain(tracks.EnumerateArray(), track => track.GetProperty("type").GetString() == "subtitle");
        var draftMetaPath = Path.Combine(outputDirectory, "draft_meta_info.json");
        Assert.True(File.Exists(draftMetaPath));
        using var draftMeta = JsonDocument.Parse(await File.ReadAllTextAsync(draftMetaPath));
        Assert.Equal(outputDirectory, draftMeta.RootElement.GetProperty("draft_fold_path").GetString());
        Assert.Equal("Rain Story", draftMeta.RootElement.GetProperty("draft_name").GetString());
        Assert.True(draftMeta.RootElement.GetProperty("tm_duration").GetInt64() > 0);
        Assert.True(File.Exists(Path.Combine(outputDirectory, "srt", "audio_int_002.srt")));
    }

    [Fact]
    public async Task EditingProjectExportService_ExportsAnimateTimelinePackageWithFixedAnimateTracks()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Animate Audio Subtitle Export Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var imagePath = await CreateFileAsync(Path.Combine(workspace.Folders.ImagesImported, "scene_001.png"), [1]);
        var audioPath = await CreateWavFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav"), 2);
        var narrationAudioPath = await CreateWavFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "narration_001.wav"), 4);
        var project = WithCompletedNarrationAudio(BuildProjectWithAssets(imagePath, audioPath), narrationAudioPath);
        var outputDirectory = Path.Combine(workspace.Folders.Exports, "animate-audio-subtitle");
        var service = new EditingProjectExportService();

        var result = await service.ExportAsync(new EditingProjectExportRequest(
            project,
            workspace,
            EditingProjectFormat.AnimateTimeline,
            new TimelineExportOptions(),
            manifest,
            outputDirectory));

        Assert.Equal("animate_timeline", result.Format);
        Assert.Equal(outputDirectory, result.OutputPath);
        Assert.Contains(imagePath, result.ReferencedAssetPaths);
        Assert.Contains(audioPath, result.ReferencedAssetPaths);
        Assert.Contains(narrationAudioPath, result.ReferencedAssetPaths);
        var manifestPath = Path.Combine(outputDirectory, "draft-agent-animate-subtitles.json");
        var jsflPath = Path.Combine(outputDirectory, "import-draft-agent-audio-subtitles.jsfl");
        var powerShellLauncherPath = Path.Combine(outputDirectory, "run-import-in-animate.ps1");
        var commandLauncherPath = Path.Combine(outputDirectory, "run-import-in-animate.cmd");
        Assert.True(File.Exists(manifestPath));
        Assert.True(File.Exists(jsflPath));
        Assert.True(File.Exists(powerShellLauncherPath));
        Assert.True(File.Exists(commandLauncherPath));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "images", "scene_001.png")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "audio", "line_001.wav")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "audio", "narration_001.wav")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "srt", "audio_int_002.srt")));
        Assert.Contains("run-import-in-animate", result.Message);

        using var manifestJson = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        Assert.Equal("animate_timeline", manifestJson.RootElement.GetProperty("format").GetString());
        Assert.Equal("editing_timeline_8_tracks", manifestJson.RootElement.GetProperty("trackOrder").GetString());
        Assert.Equal(30, manifestJson.RootElement.GetProperty("frameRate").GetDouble());
        Assert.Equal(1920, manifestJson.RootElement.GetProperty("width").GetInt32());
        Assert.Equal(1080, manifestJson.RootElement.GetProperty("height").GetInt32());
        Assert.True(manifestJson.RootElement.GetProperty("durationFrames").GetInt32() > 0);
        var tracks = manifestJson.RootElement.GetProperty("tracks");
        Assert.Equal(8, tracks.GetArrayLength());
        Assert.Equal("01 字幕", tracks[0].GetProperty("name").GetString());
        Assert.Equal("text", tracks[0].GetProperty("kind").GetString());
        Assert.Equal("02 角色图", tracks[1].GetProperty("name").GetString());
        Assert.Equal("image", tracks[1].GetProperty("kind").GetString());
        Assert.Equal("03 角色台词配音", tracks[2].GetProperty("name").GetString());
        Assert.Equal("audio", tracks[2].GetProperty("kind").GetString());
        Assert.Equal("04 角色内心配音", tracks[3].GetProperty("name").GetString());
        Assert.Equal("audio", tracks[3].GetProperty("kind").GetString());
        Assert.Equal("05 角色动作配音", tracks[4].GetProperty("name").GetString());
        Assert.Equal("audio", tracks[4].GetProperty("kind").GetString());
        Assert.Equal("06 旁白场效图", tracks[5].GetProperty("name").GetString());
        Assert.Equal("image", tracks[5].GetProperty("kind").GetString());
        Assert.Equal("07 旁白场效配音", tracks[6].GetProperty("name").GetString());
        Assert.Equal("audio", tracks[6].GetProperty("kind").GetString());
        Assert.Equal("08 场景图", tracks[7].GetProperty("name").GetString());
        Assert.Equal("image", tracks[7].GetProperty("kind").GetString());
        Assert.Contains(tracks[7].GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("kind").GetString() == "image" &&
            item.GetProperty("sourcePath").GetString() == Path.GetFullPath(imagePath) &&
            item.GetProperty("startFrame").GetInt32() == 0 &&
            item.GetProperty("endFrame").GetInt32() > item.GetProperty("startFrame").GetInt32() &&
            item.GetProperty("metadata").GetProperty("visualRole").GetString() == "scene" &&
            item.GetProperty("path").GetString()?.Contains($"{Path.DirectorySeparatorChar}images{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(tracks[2].GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("kind").GetString() == "audio" &&
            item.GetProperty("libraryName").GetString() == "line_001.wav" &&
            item.GetProperty("startFrame").GetInt32() == 120 &&
            item.GetProperty("endFrame").GetInt32() == 180 &&
            item.GetProperty("durationFrames").GetInt32() == 60);
        Assert.Contains(tracks[6].GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("kind").GetString() == "audio" &&
            item.GetProperty("libraryName").GetString() == "narration_001.wav");
        Assert.Contains(tracks[0].GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("kind").GetString() == "text" &&
            item.GetProperty("text").GetString()?.Contains("我来晚了。", StringComparison.Ordinal) == true &&
            item.GetProperty("startFrame").GetInt32() == 120 &&
            item.GetProperty("endFrame").GetInt32() == 180 &&
            item.GetProperty("durationFrames").GetInt32() == 60);
        Assert.Contains("我来晚了。", await File.ReadAllTextAsync(manifestPath));
        var jsfl = await File.ReadAllTextAsync(jsflPath);
        Assert.Contains("It does not create or convert a FLA", jsfl);
        Assert.Contains("fl.createDocument();", jsfl);
        Assert.DoesNotContain("var dom = fl.getDocumentDOM();\r\nif (!dom)", jsfl);
        Assert.Contains("creates a fresh Animate document", jsfl);
        Assert.Contains("importImage", jsfl);
        Assert.Contains("importAudio", jsfl);
        Assert.Contains("addSubtitle", jsfl);
        Assert.Contains("prepareContentSpan", jsfl);
        Assert.Contains("setSelectionBounds", jsfl);
        Assert.Contains("fitSelectionToBounds", jsfl);
        Assert.Contains("stageHeight * 0.78", jsfl);
        Assert.Contains("textElement.setTextAttr(\"size\", 48, 0, textValue.length)", jsfl);
        Assert.Contains("textElement.setTextAttr(\"alignment\", \"center\", 0, textValue.length)", jsfl);
        Assert.Contains("frame.soundLibraryItem = libraryItem", jsfl);
        Assert.Contains("frame.soundSync = \"stream\"", jsfl);
        Assert.Contains("dom.importFile(sourceUri, true, false, false)", jsfl);
        Assert.Contains("dom.importFile(fileUri(item.path), false, false, false)", jsfl);
        Assert.Contains("var textValue = item.text || \"\"", jsfl);
        Assert.Contains("dom.addNewText(bounds, textValue)", jsfl);
        Assert.Contains("textElement.textType = \"static\"", jsfl);
        Assert.Contains("timeline.currentFrame = 0", jsfl);
        Assert.DoesNotContain("setSelectedElementProperty(dom, \"width\"", jsfl);
        Assert.DoesNotContain("setSelectedElementProperty(dom, \"height\"", jsfl);
        Assert.DoesNotContain("frame.duration =", jsfl);
        Assert.Contains("requiredFrameCount", jsfl);
        Assert.Contains("ensureCurrentLayerFrameCount", jsfl);
        Assert.Contains("prepareTrackSpans", jsfl);
        Assert.Contains("requireImportLayer", jsfl);
        Assert.Contains("timeline.layers[timeline.currentLayer].name = layerName", jsfl);
        Assert.Contains("addImportLayer", jsfl);
        Assert.Contains("uniqueLayerName", jsfl);
        Assert.Contains("createImportLayers", jsfl);
        Assert.Contains("timeline.convertToBlankKeyframes(frameIndex, frameIndex + 1)", jsfl);
        Assert.Contains("Draft Agent span check", jsfl);
        Assert.Contains("draft-agent-animate-import-report.txt", jsfl);
        Assert.Contains("writeImportReport", jsfl);
        Assert.DoesNotContain("timeline.insertBlankKeyframe(startFrame)", jsfl);
        Assert.DoesNotContain("timeline.insertBlankKeyframe(endFrame)", jsfl);
        Assert.Contains("for (var i = tracks.length - 1; i >= 0; i--)", jsfl);
        Assert.Contains("findLayerIndexByName", jsfl);
        Assert.Contains("intFrame(item.endFrame, startFrame + 1)", jsfl);
        Assert.DoesNotContain("intFrame(item.endFrame, itemStartFrame(item) + itemDurationFrames(item))", jsfl);
        Assert.Contains("fixed order: text, character, scene, audio", jsfl);
        var powerShellLauncher = await File.ReadAllTextAsync(powerShellLauncherPath);
        Assert.Contains("Adobe Animate 2022", powerShellLauncher);
        Assert.Contains("import-draft-agent-audio-subtitles.jsfl", powerShellLauncher);
        Assert.Contains("-AlwaysRunJSFL", powerShellLauncher);
        Assert.Contains("-AnimateExe", powerShellLauncher);
        var commandLauncher = await File.ReadAllTextAsync(commandLauncherPath);
        Assert.Contains("run-import-in-animate.ps1", commandLauncher);
        Assert.Contains("-ExecutionPolicy Bypass", commandLauncher);
    }

    [Fact]
    public async Task EditingProjectExportService_AnimateTimelineUsesSplitSubtitleTrack()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Animate Text Split Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var imagePath = await CreateFileAsync(Path.Combine(workspace.Folders.ImagesImported, "scene_001.png"), [1]);
        var audioPath = await CreateWavFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav"), 6);
        var project = BuildProjectWithAssets(imagePath, audioPath);
        var longText = "一二三四五六七八九十一二三四五六七八九十一二三四五六七八九十一二三四五六";
        var dialogue = project.Scenes.Single().Interactions.Single(interaction => interaction.Dialogue is not null);
        var longDialogue = dialogue with
        {
            Dialogue = dialogue.Dialogue! with { Text = longText }
        };
        project = project with
        {
            Scenes =
            [
                project.Scenes.Single() with
                {
                    Interactions = [longDialogue]
                }
            ],
            GenerationTasks =
            [
                project.GenerationTasks.Single() with
                {
                    Parameters = new Dictionary<string, string> { ["text"] = longText, ["emotion"] = "Calm" }
                }
            ]
        };
        var outputDirectory = Path.Combine(workspace.Folders.Exports, "animate-text-truncate");
        var service = new EditingProjectExportService();

        await service.ExportAsync(new EditingProjectExportRequest(
            project,
            workspace,
            EditingProjectFormat.AnimateTimeline,
            new TimelineExportOptions(),
            manifest,
            outputDirectory));

        using var manifestJson = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "draft-agent-animate-subtitles.json")));
        var textTrack = manifestJson.RootElement.GetProperty("tracks")[0];
        var textItems = textTrack.GetProperty("items").EnumerateArray().ToArray();

        Assert.Equal(2, textItems.Length);
        Assert.Equal("一二三四五六七八九十一二三四五六七八九十一二三四", textItems[0].GetProperty("text").GetString());
        Assert.Equal("五六七八九十一二三四五六", textItems[1].GetProperty("text").GetString());
        Assert.Equal(0, textItems[0].GetProperty("startFrame").GetInt32());
        Assert.Equal(90, textItems[0].GetProperty("endFrame").GetInt32());
        Assert.Equal(90, textItems[0].GetProperty("durationFrames").GetInt32());
        Assert.Equal(90, textItems[1].GetProperty("startFrame").GetInt32());
        Assert.Equal(180, textItems[1].GetProperty("endFrame").GetInt32());
        Assert.Equal(90, textItems[1].GetProperty("durationFrames").GetInt32());
    }

    [Fact]
    public async Task EditingProjectExportService_JianyingDraftKeepsAbsolutePathsWhenRelativeReferencesAreDisabled()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Jianying Absolute Path Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var imagePath = await CreateFileAsync(Path.Combine(workspace.Folders.ImagesImported, "scene_001.png"), [1]);
        var audioPath = await CreateFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav"), [2]);
        var project = BuildProjectWithAssets(imagePath, audioPath);
        var outputDirectory = Path.Combine(workspace.Folders.Exports, "jianying-absolute");
        var service = new EditingProjectExportService();

        await service.ExportAsync(new EditingProjectExportRequest(
            project,
            workspace,
            EditingProjectFormat.JianyingDraft,
            new TimelineExportOptions(UseRelativeMediaReferences: false),
            manifest,
            outputDirectory));

        using var draftContent = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "draft_content.json")));
        var audioPathInDraft = draftContent.RootElement
            .GetProperty("materials")
            .GetProperty("audios")[0]
            .GetProperty("path")
            .GetString();
        var subtitlePathInDraft = draftContent.RootElement
            .GetProperty("materials")
            .GetProperty("texts")[0]
            .GetProperty("draft_agent_srt_path")
            .GetString();
        Assert.Equal(Path.GetFullPath(audioPath), audioPathInDraft);
        Assert.Equal(Path.GetFullPath(Path.Combine(outputDirectory, "srt", "audio_int_002.srt")), subtitlePathInDraft);
    }

    [Fact]
    public async Task EditingProjectExportService_DefaultJianyingDirectoryUsesDraftName()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Jianying Default Directory Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var imagePath = await CreateFileAsync(Path.Combine(workspace.Folders.ImagesImported, "scene_001.png"), [1]);
        var audioPath = await CreateWavFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav"), 2);
        var narrationAudioPath = await CreateWavFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "narration_001.wav"), 4);
        var project = WithCompletedNarrationAudio(BuildProjectWithAssets(imagePath, audioPath), narrationAudioPath);
        var service = new EditingProjectExportService();

        var result = await service.ExportAsync(new EditingProjectExportRequest(
            project,
            workspace,
            EditingProjectFormat.JianyingDraft,
            new TimelineExportOptions(),
            manifest));

        var expectedDirectory = Path.Combine(workspace.Folders.Exports, "Rain Story_剪映草稿");
        Assert.Equal(expectedDirectory, result.OutputPath);
        Assert.True(Directory.Exists(expectedDirectory));
        Assert.False(Directory.Exists(Path.Combine(workspace.Folders.Exports, "Rain Story_剪映" + "兼容描述")));
        Assert.True(File.Exists(Path.Combine(expectedDirectory, "draft_content.json")));
    }

    [Fact]
    public void EditingTimelineSubtitleBuilder_SplitsTextAfterTargetCharactersWhenNoPunctuationFits()
    {
        var text = "一二三四五六七八九十一二三四五六七八九十一二三四五六七八九十一二三四五六";

        var cues = EditingTimelineSubtitleBuilder.BuildCues(text, 6);

        Assert.Equal(2, cues.Count);
        Assert.Equal("一二三四五六七八九十一二三四五六七八九十一二三四", cues[0].Text);
        Assert.Equal("五六七八九十一二三四五六", cues[1].Text);
        Assert.Equal(0, cues[0].StartSeconds);
        Assert.Equal(3, cues[1].StartSeconds);
    }

    [Fact]
    public void EditingTimelineSubtitleBuilder_PrefersPunctuationBetweenTwentyAndThirtyCharacters()
    {
        var text = "一二三四五六七八九十一二三四五六七八九十，后面还有很多很多很多很多很多文字。";

        var cues = EditingTimelineSubtitleBuilder.BuildCues(text, 6);

        Assert.Equal(2, cues.Count);
        Assert.Equal("一二三四五六七八九十一二三四五六七八九十，", cues[0].Text);
        Assert.Equal("后面还有很多很多很多很多很多文字。", cues[1].Text);
    }

    [Fact]
    public async Task EditingProjectExportService_AllowsMissingVisualsWhenUserContinues()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Missing Visual Continue Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var audioPath = await CreateWavFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav"), 2);
        var narrationAudioPath = await CreateWavFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "narration_001.wav"), 4);
        var project = WithCompletedNarrationAudio(BuildProjectWithAssets("missing_bound_visual.png", audioPath), narrationAudioPath);
        var outputPath = Path.Combine(workspace.Folders.Exports, "missing.fcpxml");
        var service = new EditingProjectExportService();

        var result = await service.ExportAsync(new EditingProjectExportRequest(
            project,
            workspace,
            EditingProjectFormat.Fcpxml,
            new TimelineExportOptions(AllowMissingVisuals: true),
            manifest,
            outputPath,
            AllowMissingVisuals: true));

        Assert.True(File.Exists(result.OutputPath));
        var xml = await File.ReadAllTextAsync(result.OutputPath);
        Assert.Contains("line_001.wav", xml);
        Assert.Contains("<gap", xml);
    }

    [Fact]
    public async Task EditingProjectExportService_FcpxmlPreflightAllowsMissingBoundVisual()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Missing Bound Visual Preflight Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var audioPath = await CreateWavFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav"), 2);
        var narrationAudioPath = await CreateWavFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "narration_001.wav"), 4);
        var missingImageReference = "missing_bound_visual.png";
        var project = WithCompletedNarrationAudio(BuildProjectWithAssets(missingImageReference, audioPath), narrationAudioPath);
        var service = new EditingProjectExportService();

        var issues = await service.PreflightAsync(new EditingProjectExportRequest(
            project,
            workspace,
            EditingProjectFormat.Fcpxml,
            new TimelineExportOptions(),
            manifest));

        Assert.Empty(issues);
        Assert.Empty(Directory.EnumerateFiles(workspace.Folders.Exports, "*.fcpxml"));
    }

    [Fact]
    public async Task EditingProjectExportService_FcpxmlExportsGapForManifestImageAssetOutsideProjectRoot()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "External Manifest Visual Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var outsideImagePath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-outside-visual-{Guid.NewGuid():N}.png"), [1, 2, 3]);
        var audioPath = await CreateWavFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav"), 2);
        var narrationAudioPath = await CreateWavFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "narration_001.wav"), 4);
        var forgedAsset = new ProjectMediaAsset(
            "asset_outside_visual",
            MediaAssetKind.Image,
            MediaAssetSource.ImportedPng,
            "Outside visual",
            outsideImagePath,
            "assets/images/imported/missing.png",
            "scene_001",
            outsideImagePath,
            null,
            new Dictionary<string, string>(),
            DateTimeOffset.UtcNow);
        var forgedManifest = manifest with { Assets = [forgedAsset] };
        var project = WithCompletedNarrationAudio(BuildProjectWithAssets(forgedAsset.Id, audioPath), narrationAudioPath);
        var outputPath = Path.Combine(workspace.Folders.Exports, "forged-gap.fcpxml");
        var service = new EditingProjectExportService();

        var issues = await service.PreflightAsync(new EditingProjectExportRequest(
            project,
            workspace,
            EditingProjectFormat.Fcpxml,
            new TimelineExportOptions(),
            forgedManifest));
        var result = await service.ExportAsync(new EditingProjectExportRequest(
                project,
                workspace,
                EditingProjectFormat.Fcpxml,
                new TimelineExportOptions(),
                forgedManifest,
                outputPath));

        Assert.Empty(issues);
        var xml = await File.ReadAllTextAsync(result.OutputPath);
        Assert.Contains("<gap", xml);
        Assert.DoesNotContain(outsideImagePath, xml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EditingProjectExportService_AllowsExplicitAbsoluteVisualOutsideProjectRoot()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "External Absolute Visual Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var outsideImagePath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-external-visual-{Guid.NewGuid():N}.png"), [1, 2, 3]);
        var audioPath = await CreateWavFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav"), 2);
        var narrationAudioPath = await CreateWavFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "narration_001.wav"), 4);
        var project = WithCompletedNarrationAudio(BuildProjectWithAssets(outsideImagePath, audioPath), narrationAudioPath);
        var outputPath = Path.Combine(workspace.Folders.Exports, "external-visual.fcpxml");
        var service = new EditingProjectExportService();

        var issues = await service.PreflightAsync(new EditingProjectExportRequest(
            project,
            workspace,
            EditingProjectFormat.Fcpxml,
            new TimelineExportOptions(),
            manifest));
        var result = await service.ExportAsync(new EditingProjectExportRequest(
            project,
            workspace,
            EditingProjectFormat.Fcpxml,
            new TimelineExportOptions(),
            manifest,
            outputPath));

        Assert.Empty(issues);
        Assert.Contains(outsideImagePath, result.ReferencedAssetPaths);
        Assert.Contains(new Uri(Path.GetFullPath(outsideImagePath)).AbsoluteUri, await File.ReadAllTextAsync(result.OutputPath));
    }

    [Fact]
    public async Task EditingProjectExportService_UsesSceneAndCharacterDefaultVisualsWhenRowImageMissing()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "OTIO Character Default Visual Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var mediaService = new ProjectMediaAssetService();
        var sceneImageSource = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-scene-{Guid.NewGuid():N}.png"), [1]);
        var portraitSource = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-portrait-{Guid.NewGuid():N}.png"), [2]);
        var sceneImport = await mediaService.ImportAsync(workspace, new MediaImportRequest(sceneImageSource, MediaAssetKind.Image, MediaAssetSource.ImportedPng, "scene_001"));
        var portraitImport = await mediaService.ImportAsync(workspace, new MediaImportRequest(portraitSource, MediaAssetKind.Image, MediaAssetSource.ImportedPng, "char_chen"));
        var audioPath = await CreateFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav"), [3]);
        var project = DialogueOnlyProject(BuildProjectWithAssets(sceneImport.Asset.Id, audioPath));
        var character = project.Characters.Single();
        var scene = project.Scenes.Single();
        var interaction = scene.Interactions.Single();
        project = project with
        {
            Characters =
            [
                character with
                {
                    AssetBinding = character.AssetBinding with
                    {
                        Portrait = portraitImport.Asset.Id,
                        PortraitVisualBinding = VisualAssetBinding.FromImage(portraitImport.Asset.Id)
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
                                SubjectCharacterVisualBinding = null
                            }
                        }
                    ]
                }
            ]
        };
        var service = new EditingProjectExportService();

        var result = await service.ExportAsync(new EditingProjectExportRequest(
            project,
            workspace,
            EditingProjectFormat.Otio,
            new TimelineExportOptions(),
            portraitImport.Manifest));

        Assert.Contains(sceneImport.Asset.AbsolutePath, result.ReferencedAssetPaths);
        Assert.Contains(portraitImport.Asset.AbsolutePath, result.ReferencedAssetPaths);
        var json = await File.ReadAllTextAsync(result.OutputPath);
        Assert.Contains($"\"target_url\": \"../{sceneImport.Asset.ProjectRelativePath}\"", json);
        Assert.Contains($"\"target_url\": \"../{portraitImport.Asset.ProjectRelativePath}\"", json);
        Assert.DoesNotContain("\"target_url\": \"../assets/images/imported/scene_001_", json);
        Assert.DoesNotContain("\"target_url\": \"../assets/images/imported/char_chen_", json);
    }

    [Fact]
    public async Task EditingProjectExportService_DoesNotUseCharacterDefaultPsdAsSceneVisualForOtio()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "OTIO Character Default PSD Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var mediaService = new ProjectMediaAssetService();
        var psdSource = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-portrait-{Guid.NewGuid():N}.psd"), [1]);
        var psdImport = await mediaService.ImportAsync(workspace, new MediaImportRequest(psdSource, MediaAssetKind.Psd, MediaAssetSource.ImportedPsd, "char_chen"));
        var audioPath = await CreateFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav"), [2]);
        var project = DialogueOnlyProject(BuildProjectWithAssets(null!, audioPath));
        var character = project.Characters.Single();
        var scene = project.Scenes.Single();
        var interaction = scene.Interactions.Single();
        project = project with
        {
            Characters =
            [
                character with
                {
                    AssetBinding = character.AssetBinding with
                    {
                        Portrait = psdImport.Asset.Id,
                        PortraitVisualBinding = VisualAssetBinding.FromPsd(psdImport.Asset.Id)
                    }
                }
            ],
            Scenes =
            [
                scene with
                {
                    AssetBinding = scene.AssetBinding with { BackgroundImage = null, BackgroundVisualBinding = null },
                    Interactions =
                    [
                        interaction with
                        {
                            Binding = interaction.Binding with
                            {
                                SceneVisualBinding = null,
                                SubjectCharacterVisualBinding = null
                            }
                        }
                    ]
                }
            ]
        };
        var service = new EditingProjectExportService();

        var result = await service.ExportAsync(new EditingProjectExportRequest(
            project,
            workspace,
            EditingProjectFormat.Otio,
            new TimelineExportOptions(AllowMissingVisuals: true),
            psdImport.Manifest,
            AllowMissingVisuals: true));

        Assert.DoesNotContain(psdImport.Asset.AbsolutePath, result.ReferencedAssetPaths);
        var json = await File.ReadAllTextAsync(result.OutputPath);
        Assert.DoesNotContain(psdImport.Asset.DisplayName, json);
    }


    [Fact]
    public async Task EditingProjectExportService_RejectsPsdMediaReferencesForOtio()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "OTIO PSD Preflight Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var mediaService = new ProjectMediaAssetService();
        var psdSource = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-{Guid.NewGuid():N}.psd"), [1]);
        var psdImport = await mediaService.ImportAsync(workspace, new MediaImportRequest(psdSource, MediaAssetKind.Psd, MediaAssetSource.ImportedPsd, "scene_001"));
        var audioPath = await CreateFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav"), [2]);
        var project = BuildProjectWithAssets(psdImport.Asset.Id, audioPath);
        var service = new EditingProjectExportService();

        var exception = await Assert.ThrowsAsync<EditingTimelinePreflightException>(() =>
            service.ExportAsync(new EditingProjectExportRequest(project, workspace, EditingProjectFormat.Otio, new TimelineExportOptions(), psdImport.Manifest)));

        Assert.Contains("PSD", exception.Message);
        Assert.Contains("scene_001", exception.Message);
    }

    [Fact]
    public async Task EditingProjectExportService_UsesRenderedImageForPsdVisualBinding()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "OTIO PSD Rendered Binding Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var mediaService = new ProjectMediaAssetService();
        var psdSource = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-{Guid.NewGuid():N}.psd"), [1]);
        var pngSource = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-{Guid.NewGuid():N}.png"), [2]);
        var psdImport = await mediaService.ImportAsync(workspace, new MediaImportRequest(psdSource, MediaAssetKind.Psd, MediaAssetSource.ImportedPsd, "char_chen"));
        var pngImport = await mediaService.ImportAsync(workspace, new MediaImportRequest(pngSource, MediaAssetKind.Image, MediaAssetSource.ImportedPng, "char_chen"));
        var audioPath = await CreateFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav"), [3]);
        var psdBinding = VisualAssetBinding.FromPsd(psdImport.Asset.Id, pngImport.Asset.Id);
        var project = BuildProjectWithAssets(pngImport.Asset.Id, audioPath);
        project = project with
        {
            Characters =
            [
                project.Characters.Single() with
                {
                    AssetBinding = project.Characters.Single().AssetBinding with
                    {
                        Portrait = pngImport.Asset.Id,
                        PortraitVisualBinding = psdBinding
                    }
                }
            ],
            Scenes =
            [
                project.Scenes.Single() with
                {
                    Interactions = project.Scenes.Single().Interactions.Select(interaction =>
                        interaction.SubjectCharacterId == "char_chen"
                            ? interaction with
                            {
                                Binding = interaction.Binding with
                                {
                                    SubjectCharacterVisualBinding = psdBinding
                                }
                            }
                            : interaction).ToArray()
                }
            ]
        };
        var service = new EditingProjectExportService();

        var issues = await service.PreflightAsync(new EditingProjectExportRequest(
            project,
            workspace,
            EditingProjectFormat.Otio,
            new TimelineExportOptions(),
            pngImport.Manifest));

        Assert.Empty(issues);
        var result = await service.ExportAsync(new EditingProjectExportRequest(
            project,
            workspace,
            EditingProjectFormat.Otio,
            new TimelineExportOptions(),
            pngImport.Manifest));
        Assert.Contains(pngImport.Asset.AbsolutePath, result.ReferencedAssetPaths);
        Assert.DoesNotContain(psdImport.Asset.AbsolutePath, result.ReferencedAssetPaths);
    }

    [Fact]
    public async Task VisualExportAssetResolver_UsesRenderedImageForPsdVisualBinding()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Visual Resolver PSD Rendered Binding Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var mediaService = new ProjectMediaAssetService();
        var psdSource = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-{Guid.NewGuid():N}.psd"), [1]);
        var pngSource = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-{Guid.NewGuid():N}.png"), [2]);
        var psdImport = await mediaService.ImportAsync(workspace, new MediaImportRequest(psdSource, MediaAssetKind.Psd, MediaAssetSource.ImportedPsd, "char_chen"));
        var pngImport = await mediaService.ImportAsync(workspace, new MediaImportRequest(pngSource, MediaAssetKind.Image, MediaAssetSource.ImportedPng, "char_chen"));
        var audioPath = await CreateFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav"), [3]);
        var psdBinding = VisualAssetBinding.FromPsd(psdImport.Asset.Id, pngImport.Asset.Id);
        var project = DialogueOnlyProject(BuildProjectWithAssets(pngImport.Asset.Id, audioPath));
        var scene = project.Scenes.Single();
        var interaction = scene.Interactions.Single();
        project = project with
        {
            Characters =
            [
                project.Characters.Single() with
                {
                    AssetBinding = project.Characters.Single().AssetBinding with
                    {
                        Portrait = pngImport.Asset.Id,
                        PortraitVisualBinding = psdBinding
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
                                SubjectCharacterVisualBinding = psdBinding
                            }
                        }
                    ]
                }
            ]
        };
        scene = project.Scenes.Single();
        interaction = scene.Interactions.Single();
        var resolver = new VisualExportAssetResolver();

        var resolution = resolver.Resolve(project, scene, interaction, workspace, pngImport.Manifest);

        Assert.NotNull(resolution.Asset);
        Assert.Equal(pngImport.Asset.Id, resolution.Asset.Asset?.Id);
        Assert.Equal(pngImport.Asset.AbsolutePath, resolution.Asset.AbsolutePath);
        Assert.Empty(resolution.Issues);
    }

    [Fact]
    public async Task EditingProjectExportService_RejectsPsdBindingWhenRenderedAssetIsMissing()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "OTIO Missing Rendered PSD Binding Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var mediaService = new ProjectMediaAssetService();
        var psdSource = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-{Guid.NewGuid():N}.psd"), [1]);
        var psdImport = await mediaService.ImportAsync(workspace, new MediaImportRequest(psdSource, MediaAssetKind.Psd, MediaAssetSource.ImportedPsd, "char_chen"));
        var audioPath = await CreateFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav"), [2]);
        var missingRenderedAssetId = "asset_missing_rendered_png";
        var psdBinding = VisualAssetBinding.FromPsd(psdImport.Asset.Id, missingRenderedAssetId);
        var project = BuildProjectWithAssets(missingRenderedAssetId, audioPath);
        project = project with
        {
            Characters =
            [
                project.Characters.Single() with
                {
                    AssetBinding = project.Characters.Single().AssetBinding with
                    {
                        Portrait = missingRenderedAssetId,
                        PortraitVisualBinding = psdBinding
                    }
                }
            ],
            Scenes =
            [
                project.Scenes.Single() with
                {
                    Interactions = project.Scenes.Single().Interactions.Select(interaction =>
                        interaction.SubjectCharacterId == "char_chen"
                            ? interaction with
                            {
                                Binding = interaction.Binding with
                                {
                                    SubjectCharacterVisualBinding = psdBinding
                                }
                            }
                            : interaction).ToArray()
                }
            ]
        };
        var service = new EditingProjectExportService();

        var issues = await service.PreflightAsync(new EditingProjectExportRequest(
            project,
            workspace,
            EditingProjectFormat.Otio,
            new TimelineExportOptions(),
            psdImport.Manifest));

        var issue = Assert.Single(issues, issue => issue.Kind == EditingTimelinePreflightIssueKind.PsdReference);
        Assert.Equal(EditingTimelinePreflightIssueKind.PsdReference, issue.Kind);
        Assert.Equal(psdImport.Asset.Id, issue.AssetReference);
        Assert.Equal("int_002", issue.InteractionId);
        Assert.Contains(issues, issue => issue.Kind == EditingTimelinePreflightIssueKind.MissingVisual);
    }

    [Fact]
    public async Task EditingProjectExportService_PreflightReportsUnrenderedRowPsdEvenWhenCharacterDefaultImageExists()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Row PSD Strict Preflight Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var mediaService = new ProjectMediaAssetService();
        var defaultImage = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-{Guid.NewGuid():N}.png"), [1]);
        var psdSource = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-{Guid.NewGuid():N}.psd"), [2]);
        var defaultImport = await mediaService.ImportAsync(workspace, new MediaImportRequest(defaultImage, MediaAssetKind.Image, MediaAssetSource.ImportedPng, "char_chen"));
        var psdImport = await mediaService.ImportAsync(workspace, new MediaImportRequest(psdSource, MediaAssetKind.Psd, MediaAssetSource.ImportedPsd, "char_chen"));
        var audioPath = await CreateFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav"), [3]);
        var project = DialogueOnlyProject(BuildProjectWithAssets(defaultImport.Asset.Id, audioPath));
        var scene = project.Scenes.Single();
        var interaction = scene.Interactions.Single();
        var rowPsdBinding = VisualAssetBinding.FromPsd(psdImport.Asset.Id);
        project = project with
        {
            Characters =
            [
                project.Characters.Single() with
                {
                    AssetBinding = project.Characters.Single().AssetBinding with
                    {
                        Portrait = defaultImport.Asset.Id,
                        PortraitVisualBinding = VisualAssetBinding.FromImage(defaultImport.Asset.Id)
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
                                SubjectCharacterVisualBinding = rowPsdBinding
                            }
                        }
                    ]
                }
            ]
        };
        var service = new EditingProjectExportService();

        var issues = await service.PreflightAsync(new EditingProjectExportRequest(
            project,
            workspace,
            EditingProjectFormat.Otio,
            new TimelineExportOptions(),
            psdImport.Manifest));

        var issue = Assert.Single(issues, issue => issue.InteractionId == interaction.Id);
        Assert.Equal(EditingTimelinePreflightIssueKind.PsdReference, issue.Kind);
        Assert.Equal(psdImport.Asset.Id, issue.AssetReference);
    }

    [Fact]
    public async Task VisualExportAssetResolver_ReportsPsdSourceWhenRenderedAssetIsMissing()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Visual Resolver Missing Rendered PSD Binding Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var mediaService = new ProjectMediaAssetService();
        var psdSource = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-{Guid.NewGuid():N}.psd"), [1]);
        var psdImport = await mediaService.ImportAsync(workspace, new MediaImportRequest(psdSource, MediaAssetKind.Psd, MediaAssetSource.ImportedPsd, "char_chen"));
        var audioPath = await CreateFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav"), [2]);
        var missingRenderedAssetId = "asset_missing_rendered_png";
        var psdBinding = VisualAssetBinding.FromPsd(psdImport.Asset.Id, missingRenderedAssetId);
        var project = DialogueOnlyProject(BuildProjectWithAssets(missingRenderedAssetId, audioPath));
        var scene = project.Scenes.Single();
        var interaction = scene.Interactions.Single();
        project = project with
        {
            Characters =
            [
                project.Characters.Single() with
                {
                    AssetBinding = project.Characters.Single().AssetBinding with
                    {
                        Portrait = missingRenderedAssetId,
                        PortraitVisualBinding = psdBinding
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
                                SubjectCharacterVisualBinding = psdBinding
                            }
                        }
                    ]
                }
            ]
        };
        scene = project.Scenes.Single();
        interaction = scene.Interactions.Single();
        var resolver = new VisualExportAssetResolver();

        var resolution = resolver.Resolve(project, scene, interaction, workspace, psdImport.Manifest);

        Assert.Null(resolution.Asset);
        var issue = Assert.Single(resolution.Issues);
        Assert.Equal(EditingTimelinePreflightIssueKind.PsdReference, issue.Kind);
        Assert.Equal(psdImport.Asset.Id, issue.AssetReference);
        Assert.Equal(scene.Id, issue.SceneId);
        Assert.Equal(interaction.Id, issue.InteractionId);
    }

    [Fact]
    public async Task VisualExportAssetResolver_ReportsUnrenderedRowPsdEvenWhenCharacterDefaultImageExists()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Visual Resolver Row PSD Strict Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var mediaService = new ProjectMediaAssetService();
        var defaultImage = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-{Guid.NewGuid():N}.png"), [1]);
        var psdSource = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-{Guid.NewGuid():N}.psd"), [2]);
        var defaultImport = await mediaService.ImportAsync(workspace, new MediaImportRequest(defaultImage, MediaAssetKind.Image, MediaAssetSource.ImportedPng, "char_chen"));
        var psdImport = await mediaService.ImportAsync(workspace, new MediaImportRequest(psdSource, MediaAssetKind.Psd, MediaAssetSource.ImportedPsd, "char_chen"));
        var audioPath = await CreateFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav"), [3]);
        var project = DialogueOnlyProject(BuildProjectWithAssets(defaultImport.Asset.Id, audioPath));
        var scene = project.Scenes.Single();
        var interaction = scene.Interactions.Single();
        var rowPsdBinding = VisualAssetBinding.FromPsd(psdImport.Asset.Id);
        project = project with
        {
            Characters =
            [
                project.Characters.Single() with
                {
                    AssetBinding = project.Characters.Single().AssetBinding with
                    {
                        Portrait = defaultImport.Asset.Id,
                        PortraitVisualBinding = VisualAssetBinding.FromImage(defaultImport.Asset.Id)
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
                                SubjectCharacterVisualBinding = rowPsdBinding
                            }
                        }
                    ]
                }
            ]
        };
        scene = project.Scenes.Single();
        interaction = scene.Interactions.Single();
        var resolver = new VisualExportAssetResolver();

        var resolution = resolver.Resolve(project, scene, interaction, workspace, psdImport.Manifest);

        Assert.Null(resolution.Asset);
        var issue = Assert.Single(resolution.Issues);
        Assert.Equal(EditingTimelinePreflightIssueKind.PsdReference, issue.Kind);
        Assert.Equal(psdImport.Asset.Id, issue.AssetReference);
        Assert.Equal(interaction.Id, issue.InteractionId);
    }

    [Fact]
    public async Task EditingProjectExportService_PreflightReturnsIssuesWithoutWritingOtio()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "OTIO Preflight Only Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var mediaService = new ProjectMediaAssetService();
        var psdSource = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-{Guid.NewGuid():N}.psd"), [1]);
        var psdImport = await mediaService.ImportAsync(workspace, new MediaImportRequest(psdSource, MediaAssetKind.Psd, MediaAssetSource.ImportedPsd, "scene_001"));
        var audioPath = await CreateFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav"), [2]);
        var project = BuildProjectWithAssets(psdImport.Asset.Id, audioPath);
        var service = new EditingProjectExportService();

        var issues = await service.PreflightAsync(new EditingProjectExportRequest(
            project,
            workspace,
            EditingProjectFormat.Otio,
            new TimelineExportOptions(),
            psdImport.Manifest));

        Assert.NotEmpty(issues);
        Assert.All(issues, issue => Assert.Equal(EditingTimelinePreflightIssueKind.PsdReference, issue.Kind));
        Assert.All(issues, issue => Assert.Equal("scene_001", issue.SceneId));
        Assert.All(issues, issue => Assert.Equal(psdImport.Asset.Id, issue.AssetReference));
        Assert.Empty(Directory.EnumerateFiles(workspace.Folders.Otio, "*.otio"));
    }

    [Fact]
    public async Task EditingProjectExportService_PreflightReportsCompletedTtsWithMissingAudio()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Missing Completed Audio Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var imagePath = await CreateFileAsync(Path.Combine(workspace.Folders.ImagesImported, "scene_001.png"), [1]);
        var missingAudioPath = Path.Combine(workspace.Folders.AudioGenerated, "missing_line.wav");
        var project = DialogueOnlyProject(BuildProjectWithAssets(imagePath, missingAudioPath));
        var service = new EditingProjectExportService();

        var issues = await service.PreflightAsync(new EditingProjectExportRequest(
            project,
            workspace,
            EditingProjectFormat.Fcpxml,
            new TimelineExportOptions(),
            manifest));

        var issue = Assert.Single(issues);
        Assert.Equal(EditingTimelinePreflightIssueKind.MissingAudio, issue.Kind);
        Assert.Equal("scene_001", issue.SceneId);
        Assert.Equal("int_002", issue.InteractionId);
        Assert.Equal("tts_int_002", issue.TaskId);
        Assert.Equal(GenerationTaskStatus.Completed, issue.TaskStatus);
        Assert.Equal(missingAudioPath, issue.AssetReference);
    }

    [Fact]
    public async Task EditingProjectExportService_FcpxmlPreflightReportsTextRowWithoutCompletedAudio()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Missing Required Audio Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var imagePath = await CreateFileAsync(Path.Combine(workspace.Folders.ImagesImported, "scene_001.png"), [1]);
        var audioPath = await CreateWavFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav"), 2);
        var project = DialogueOnlyProject(BuildProjectWithAssets(imagePath, audioPath)) with
        {
            GenerationTasks = []
        };
        var service = new EditingProjectExportService();

        var issues = await service.PreflightAsync(new EditingProjectExportRequest(
            project,
            workspace,
            EditingProjectFormat.Fcpxml,
            new TimelineExportOptions(),
            manifest));
        var exception = await Assert.ThrowsAsync<EditingTimelinePreflightException>(() =>
            service.ExportAsync(new EditingProjectExportRequest(
                project,
                workspace,
                EditingProjectFormat.Fcpxml,
                new TimelineExportOptions(),
                manifest)));

        var issue = Assert.Single(issues);
        Assert.Equal(EditingTimelinePreflightIssueKind.MissingAudio, issue.Kind);
        Assert.Equal("scene_001", issue.SceneId);
        Assert.Equal("int_002", issue.InteractionId);
        Assert.Null(issue.TaskId);
        Assert.Contains("工作台台词行缺少可导出的语音", issue.Message, StringComparison.Ordinal);
        Assert.Contains("我来晚了。", issue.AssetReference, StringComparison.Ordinal);
        Assert.Contains("工作台台词行缺少可导出的语音", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditingProjectExportService_PreflightRejectsCompletedTtsWithUnsupportedAudioDurationFormat()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Unsupported Audio Duration Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var imagePath = await CreateFileAsync(Path.Combine(workspace.Folders.ImagesImported, "scene_001.png"), [1]);
        var audioPath = await CreateFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.mp3"), [2, 3, 4]);
        var project = DialogueOnlyProject(BuildProjectWithAssets(imagePath, audioPath));
        var service = new EditingProjectExportService();

        var issues = await service.PreflightAsync(new EditingProjectExportRequest(
            project,
            workspace,
            EditingProjectFormat.Fcpxml,
            new TimelineExportOptions(),
            manifest));
        var exception = await Assert.ThrowsAsync<EditingTimelinePreflightException>(() =>
            service.ExportAsync(new EditingProjectExportRequest(
                project,
                workspace,
                EditingProjectFormat.Fcpxml,
                new TimelineExportOptions(),
                manifest)));

        var issue = Assert.Single(issues);
        Assert.Equal(EditingTimelinePreflightIssueKind.UnsupportedAudio, issue.Kind);
        Assert.Equal("scene_001", issue.SceneId);
        Assert.Equal("int_002", issue.InteractionId);
        Assert.Equal("tts_int_002", issue.TaskId);
        Assert.Equal(GenerationTaskStatus.Completed, issue.TaskStatus);
        Assert.Equal(audioPath, issue.AssetReference);
        Assert.Contains("无法读取真实时长", issue.Message, StringComparison.Ordinal);
        Assert.Contains("WAV/FLAC", issue.SuggestedAction, StringComparison.Ordinal);
        Assert.Contains("真实时长读取器", issue.SuggestedAction, StringComparison.Ordinal);
        Assert.Contains("无法读取真实时长", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditingProjectExportService_UsesImportedTtsResultAsset()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Imported Tts Fallback Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var mediaService = new ProjectMediaAssetService();
        var imagePath = await CreateFileAsync(Path.Combine(workspace.Folders.ImagesImported, "scene_001.png"), [1]);
        var audioSource = await CreateWavFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-{Guid.NewGuid():N}.wav"), 2);
        var audioImport = await mediaService.ImportAsync(
            workspace,
            new MediaImportRequest(audioSource, MediaAssetKind.Audio, MediaAssetSource.TtsGenerated, "int_002"));
        var project = DialogueOnlyProject(BuildProjectWithAssets(imagePath, audioImport.Asset.Id));
        project = project with
        {
            GenerationTasks = project.GenerationTasks.Select(task => task.Id == "tts_int_002"
                ? task with
                {
                    ResultAssetId = audioImport.Asset.Id
                }
                : task).ToArray()
        };
        var service = new EditingProjectExportService();

        var issues = await service.PreflightAsync(new EditingProjectExportRequest(
            project,
            workspace,
            EditingProjectFormat.Fcpxml,
            new TimelineExportOptions(),
            audioImport.Manifest));
        var result = await service.ExportAsync(new EditingProjectExportRequest(
            project,
            workspace,
            EditingProjectFormat.Fcpxml,
            new TimelineExportOptions(),
            audioImport.Manifest));

        Assert.Empty(issues);
        Assert.Contains(audioImport.Asset.AbsolutePath, result.ReferencedAssetPaths);
    }

    [Fact]
    public async Task EditingProjectExportService_RejectsCompletedTtsThatOnlyPointsAtExternalComfyCache()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "External Comfy Cache Audio Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var imagePath = await CreateFileAsync(Path.Combine(workspace.Folders.ImagesImported, "scene_001.png"), [1]);
        var externalComfyCachePath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"comfy-cache-{Guid.NewGuid():N}.flac"), [2]);
        var project = DialogueOnlyProject(BuildProjectWithAssets(imagePath, externalComfyCachePath));
        var service = new EditingProjectExportService();

        var issues = await service.PreflightAsync(new EditingProjectExportRequest(
            project,
            workspace,
            EditingProjectFormat.Fcpxml,
            new TimelineExportOptions(),
            manifest));

        var issue = Assert.Single(issues);
        Assert.Equal(EditingTimelinePreflightIssueKind.MissingAudio, issue.Kind);
        Assert.Equal("tts_int_002", issue.TaskId);
        Assert.Equal(externalComfyCachePath, issue.AssetReference);
    }

    [Fact]
    public async Task EditingProjectExportService_JianyingPreflightRejectsCompletedTtsThatOnlyPointsAtExternalComfyCache()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Jianying External Comfy Cache Audio Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var imagePath = await CreateFileAsync(Path.Combine(workspace.Folders.ImagesImported, "scene_001.png"), [1]);
        var externalComfyCachePath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"comfy-cache-{Guid.NewGuid():N}.flac"), [2]);
        var project = BuildProjectWithAssets(imagePath, externalComfyCachePath);
        var service = new EditingProjectExportService();

        var issues = await service.PreflightAsync(new EditingProjectExportRequest(
            project,
            workspace,
            EditingProjectFormat.JianyingDraft,
            new TimelineExportOptions(),
            manifest));
        var exception = await Assert.ThrowsAsync<EditingTimelinePreflightException>(() =>
            service.ExportAsync(new EditingProjectExportRequest(
                project,
                workspace,
                EditingProjectFormat.JianyingDraft,
                new TimelineExportOptions(),
                manifest)));

        var issue = Assert.Single(issues);
        Assert.Equal(EditingTimelinePreflightIssueKind.MissingAudio, issue.Kind);
        Assert.Equal("tts_int_002", issue.TaskId);
        Assert.Equal(externalComfyCachePath, issue.AssetReference);
        Assert.Contains("已完成的 TTS 任务找不到可导出的音频", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(workspace.Folders.Exports, "Rain Story_剪映草稿")));
        Assert.False(Directory.Exists(Path.Combine(workspace.Folders.Exports, "Rain Story_剪映" + "兼容描述")));
    }

    [Fact]
    public async Task EditingProjectExportService_RejectsPathOnlyPsdMediaReferencesForOtio()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "OTIO Path PSD Preflight Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var psdPath = await CreateFileAsync(Path.Combine(workspace.Folders.ImagesPsd, "scene_001.psd"), [1]);
        var audioPath = await CreateFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav"), [2]);
        var project = BuildProjectWithAssets(psdPath, audioPath);
        var service = new EditingProjectExportService();

        var exception = await Assert.ThrowsAsync<EditingTimelinePreflightException>(() =>
            service.ExportAsync(new EditingProjectExportRequest(project, workspace, EditingProjectFormat.Otio, new TimelineExportOptions(), null)));

        Assert.Contains("PSD", exception.Message);
        Assert.Contains(psdPath, exception.Message);
    }

    [Fact]
    public async Task EditingProjectExportService_RejectsWorkspaceRelativePsbMediaReferencesForOtioWithoutManifest()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "OTIO Relative PSB Preflight Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var psbPath = await CreateFileAsync(Path.Combine(workspace.Folders.ImagesPsd, "scene_001.psb"), [1]);
        var relativePsbPath = Path.GetRelativePath(workspace.RootPath, psbPath);
        var audioPath = await CreateFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav"), [2]);
        var project = BuildProjectWithAssets(relativePsbPath, audioPath);
        var service = new EditingProjectExportService();

        var exception = await Assert.ThrowsAsync<EditingTimelinePreflightException>(() =>
            service.ExportAsync(new EditingProjectExportRequest(project, workspace, EditingProjectFormat.Otio, new TimelineExportOptions(), null)));

        Assert.Contains("PSD", exception.Message);
        Assert.Contains(relativePsbPath, exception.Message);
    }







    [Fact]
    public async Task ImportCompletedGeneratedAssetsAsync_ImportsEveryCompletedTtsTask()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Generated Multi Tts Test");
        var comfy = new DownloadingComfyClient();
        var service = new DraftWorkspaceService(new MockLlmAnalysisClient(), comfy, new InMemoryInteractionProjectStore());
        var project = new InteractionProject(
            "multi_tts",
            "Multi Tts",
            new DraftAnalyzer().Preprocess(string.Empty),
            [],
            [],
            [
                CompletedTtsTask("tts_scene_a", "int_scene_a", "output|tts|scene_a.flac"),
                CompletedTtsTask("tts_scene_b", "int_scene_b", "output|tts|scene_b.flac")
            ],
            []);

        var import = await service.ImportCompletedGeneratedAssetsAsync(project, manifest);

        var importedTts = import.Project.GenerationTasks
            .Where(task => task.Type == GenerationTaskType.Tts)
            .OrderBy(task => task.Id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(2, import.ImportedCount);
        Assert.All(importedTts, task =>
        {
            Assert.StartsWith("asset_", task.ResultAssetId);
        });
        Assert.Equal(2, importedTts.Select(task => task.ResultAssetId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, import.Manifest.Assets.Count(asset => asset.Source == MediaAssetSource.TtsGenerated && asset.Kind == MediaAssetKind.Audio));
        Assert.Contains(import.Manifest.Assets, asset => asset.TargetId == "int_scene_a" && asset.Metadata["taskId"] == "tts_scene_a");
        Assert.Contains(import.Manifest.Assets, asset => asset.TargetId == "int_scene_b" && asset.Metadata["taskId"] == "tts_scene_b");
    }

    [Fact]
    public async Task ImportCompletedGeneratedAssetsAsync_SkipsWhenResultIsAlreadyProjectAsset()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Generated Already Imported Tts Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var mediaService = new ProjectMediaAssetService();
        var audioSource = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-{Guid.NewGuid():N}.flac"), [1]);
        var audioImport = await mediaService.ImportAsync(
            workspace,
            new MediaImportRequest(audioSource, MediaAssetKind.Audio, MediaAssetSource.TtsGenerated, "int_scene_a"));
        var comfy = new DownloadingComfyClient();
        var service = new DraftWorkspaceService(new MockLlmAnalysisClient(), comfy, new InMemoryInteractionProjectStore());
        var task = CompletedTtsTask("tts_scene_a", "int_scene_a", audioImport.Asset.Id) with
        {
            ResultAssetId = audioImport.Asset.Id
        };
        var project = new InteractionProject(
            "multi_tts",
            "Multi Tts",
            new DraftAnalyzer().Preprocess(string.Empty),
            [],
            [],
            [task],
            []);

        var import = await service.ImportCompletedGeneratedAssetsAsync(project, audioImport.Manifest);

        Assert.Equal(0, import.ImportedCount);
        Assert.Empty(comfy.DownloadedReferences);
        var updatedTask = Assert.Single(import.Project.GenerationTasks);
        Assert.Equal(audioImport.Asset.Id, updatedTask.ResultAssetId);
        Assert.DoesNotContain("comfyOutputReference", updatedTask.Parameters.Keys);
        Assert.Single(import.Manifest.Assets, asset => asset.Id == audioImport.Asset.Id);
    }

    [Fact]
    public async Task ImportCompletedGeneratedAssetsAsync_ImportsCompletedResultAssetId()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Generated Result Priority Test");
        var comfy = new DownloadingComfyClient();
        var service = new DraftWorkspaceService(new MockLlmAnalysisClient(), comfy, new InMemoryInteractionProjectStore());
        var currentOutputReference = "output|tts|current_scene_a.flac";
        var task = CompletedTtsTask("tts_scene_a", "int_scene_a", currentOutputReference);
        var project = new InteractionProject(
            "multi_tts",
            "Multi Tts",
            new DraftAnalyzer().Preprocess(string.Empty),
            [],
            [],
            [task],
            []);

        var import = await service.ImportCompletedGeneratedAssetsAsync(project, manifest);

        Assert.Equal(1, import.ImportedCount);
        Assert.Equal([currentOutputReference], comfy.DownloadedReferences);
        var updatedTask = Assert.Single(import.Project.GenerationTasks);
        Assert.StartsWith("asset_", updatedTask.ResultAssetId);
        Assert.Equal(currentOutputReference, updatedTask.Parameters["comfyOutputReference"]);
        Assert.Single(import.Manifest.Assets, asset =>
            asset.Source == MediaAssetSource.TtsGenerated &&
            asset.Kind == MediaAssetKind.Audio &&
            asset.TargetId == "int_scene_a" &&
            asset.Metadata["comfyOutputReference"] == currentOutputReference);
    }

    [Fact]
    public async Task ImportCompletedGeneratedAssetsAsync_SkipsUnsupportedGeneratedAudioReferenceBeforeDownload()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Generated Unsupported Audio Import Test");
        var comfy = new DownloadingComfyClient();
        var logger = new InMemoryWorkflowLogger();
        var service = new DraftWorkspaceService(new MockLlmAnalysisClient(), comfy, new InMemoryInteractionProjectStore(), logger);
        var outputReference = "output|tts|line_001.mp3";
        var task = CompletedTtsTask("tts_scene_a", "int_scene_a", outputReference);
        var project = new InteractionProject(
            "unsupported_generated_audio",
            "Unsupported Generated Audio",
            new DraftAnalyzer().Preprocess(string.Empty),
            [],
            [],
            [task],
            []);

        var import = await service.ImportCompletedGeneratedAssetsAsync(project, manifest);

        Assert.Equal(0, import.ImportedCount);
        Assert.Empty(comfy.DownloadedReferences);
        Assert.Empty(import.Manifest.Assets);
        var unchangedTask = Assert.Single(import.Project.GenerationTasks);
        Assert.Equal(outputReference, unchangedTask.ResultAssetId);
        Assert.DoesNotContain("comfyOutputReference", unchangedTask.Parameters.Keys);
        Assert.Contains(logger.Entries, entry =>
            entry.Step == WorkflowSteps.ProjectAsset &&
            entry.Operation == "generated_import_skipped_unsupported_audio" &&
            entry.Data["outputReference"] == outputReference);
    }

    [Fact]
    public async Task ImportCompletedGeneratedAssetsAsync_SkipsMissingCompletedAssetIdWithoutOutputFallback()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Generated Missing AssetId Fallback Test");
        var comfy = new DownloadingComfyClient();
        var service = new DraftWorkspaceService(new MockLlmAnalysisClient(), comfy, new InMemoryInteractionProjectStore());
        var outputReference = "output|tts|recover_scene_a.flac";
        var task = CompletedTtsTask("tts_scene_a", "int_scene_a", outputReference) with
        {
            ResultAssetId = "asset_missing_from_manifest"
        };
        var project = new InteractionProject(
            "missing_asset_tts",
            "Missing Asset Tts",
            new DraftAnalyzer().Preprocess(string.Empty),
            [],
            [],
            [task],
            []);

        var import = await service.ImportCompletedGeneratedAssetsAsync(project, manifest);

        Assert.Equal(0, import.ImportedCount);
        Assert.Empty(comfy.DownloadedReferences);
        var updatedTask = Assert.Single(import.Project.GenerationTasks);
        Assert.Equal("asset_missing_from_manifest", updatedTask.ResultAssetId);
        Assert.DoesNotContain("comfyOutputReference", updatedTask.Parameters.Keys);
        Assert.Empty(import.Manifest.Assets);
    }

    [Fact]
    public async Task ImportCompletedGeneratedAssetsAsync_SkipsMissingManifestAssetFileWithoutOutputFallback()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Generated Missing Asset File Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var mediaService = new ProjectMediaAssetService();
        var audioSource = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-{Guid.NewGuid():N}.flac"), [1]);
        var audioImport = await mediaService.ImportAsync(
            workspace,
            new MediaImportRequest(audioSource, MediaAssetKind.Audio, MediaAssetSource.TtsGenerated, "int_scene_a"));
        File.Delete(audioImport.Asset.AbsolutePath);

        var comfy = new DownloadingComfyClient();
        var service = new DraftWorkspaceService(new MockLlmAnalysisClient(), comfy, new InMemoryInteractionProjectStore());
        var outputReference = "output|tts|recover_missing_file.flac";
        var task = CompletedTtsTask("tts_scene_a", "int_scene_a", outputReference) with
        {
            ResultAssetId = audioImport.Asset.Id
        };
        var project = new InteractionProject(
            "missing_asset_file_tts",
            "Missing Asset File Tts",
            new DraftAnalyzer().Preprocess(string.Empty),
            [],
            [],
            [task],
            []);

        var import = await service.ImportCompletedGeneratedAssetsAsync(project, audioImport.Manifest);

        Assert.Equal(0, import.ImportedCount);
        Assert.Empty(comfy.DownloadedReferences);
        var updatedTask = Assert.Single(import.Project.GenerationTasks);
        Assert.Equal(audioImport.Asset.Id, updatedTask.ResultAssetId);
        Assert.Empty(import.ImportedAssets);
        Assert.Contains(import.Manifest.Assets, asset => asset.Id == audioImport.Asset.Id);
    }

    [Fact]
    public async Task ImportCompletedGeneratedAssetsAsync_SkipsCompletedNonTtsTasks()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Generated Non Tts Test");
        var comfy = new DownloadingComfyClient();
        var service = new DraftWorkspaceService(new MockLlmAnalysisClient(), comfy, new InMemoryInteractionProjectStore());
        var visualTask = new GenerationTask(
            "video_scene_a",
            GenerationTaskType.Video,
            "int_scene_a",
            "Completed visual",
            "comfyui",
            GenerationTaskStatus.Completed,
            new Dictionary<string, string>(),
            "output|images|scene_a.png");
        var project = new InteractionProject(
            "completed_non_tts",
            "Completed Non Tts",
            new DraftAnalyzer().Preprocess(string.Empty),
            [],
            [],
            [visualTask],
            []);

        var import = await service.ImportCompletedGeneratedAssetsAsync(project, manifest);

        Assert.Equal(0, import.ImportedCount);
        Assert.Empty(comfy.DownloadedReferences);
        Assert.Empty(import.Manifest.Assets);
        var unchangedTask = Assert.Single(import.Project.GenerationTasks);
        Assert.Equal(GenerationTaskType.Video, unchangedTask.Type);
        Assert.Equal("output|images|scene_a.png", unchangedTask.ResultAssetId);
    }




    [Fact]
    public async Task DraftWorkspaceService_RestoresLastWorkspaceProjectManifest()
    {
        var root = NewTempProjectRoot();
        var statePath = Path.Combine(Path.GetTempPath(), $"draft-agent-state-{Guid.NewGuid():N}.json");
        var stateStore = new FileWorkspaceProjectStateStore(statePath);
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore(),
            workspaceProjectStateStore: stateStore);

        var manifest = await service.CreateOrOpenWorkspaceProjectAsync(root, "Restore Test");
        var restored = await service.LoadLastWorkspaceProjectAsync();

        Assert.NotNull(restored);
        Assert.Equal(manifest.ProjectId, restored.ProjectId);
        Assert.Equal(Path.GetFullPath(root), restored.RootPath);
    }

    [Fact]
    public async Task DraftWorkspaceService_UsesInjectedProjectMediaAssetService()
    {
        var root = NewTempProjectRoot();
        var manifest = await new WorkspaceProjectManager().CreateOrOpenAsync(root, "Injected Service Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var mediaService = new CapturingProjectMediaAssetService();
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore(),
            dependencies: new DraftWorkspaceServiceDependencies(ProjectMediaAssetService: mediaService));
        var request = new MediaImportRequest("source.png", MediaAssetKind.Image, MediaAssetSource.ImportedPng, "scene_001");

        var result = await service.ImportProjectMediaAsync(workspace, request);

        Assert.True(mediaService.WasCalled);
        Assert.Equal(request, mediaService.LastRequest);
        Assert.Equal("asset_injected", result.Asset.Id);
    }

    [Fact]
    public async Task EditingProjectExportService_ReportsPlannedAdapters()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Adapter Test");
        var service = new EditingProjectExportService();
        var project = new SceneInteractionBuilder().Build("陈默说：我来晚了。");

        var exception = await Assert.ThrowsAsync<NotSupportedException>(() =>
            service.ExportAsync(new EditingProjectExportRequest(project, WorkspaceProjectManager.ToWorkspace(manifest), EditingProjectFormat.PremierePro, new TimelineExportOptions())));

        Assert.Contains("FCPXML", exception.Message, StringComparison.Ordinal);
        Assert.Contains("剪映草稿", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Core 内部 OTIO", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("剪映" + "兼容描述", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("稳定导出格式为 OTIO", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OtioTimelineExporter_RejectsWrongFormatWithCurrentUserExportGuidance()
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "OTIO Adapter Guidance Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var timeline = new EditingTimeline(
            "timeline_guidance",
            "Guidance",
            24,
            [],
            [],
            new Dictionary<string, string>());
        var exporter = new OtioTimelineExporter();

        var exception = await Assert.ThrowsAsync<NotSupportedException>(() =>
            exporter.ExportAsync(new TimelineExportContext(
                timeline,
                workspace,
                EditingProjectFormat.PremierePro,
                new TimelineExportOptions())));

        Assert.Contains("FCPXML", exception.Message, StringComparison.Ordinal);
        Assert.Contains("剪映草稿", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Core 内部 OTIO", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("剪映" + "兼容描述", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("稳定导出格式为 OTIO", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(EditingProjectFormat.DaVinciResolve)]
    [InlineData(EditingProjectFormat.PremierePro)]
    [InlineData(EditingProjectFormat.FinalCutPro)]
    public async Task EditingProjectExportService_PreflightRejectsPlannedAdapters(EditingProjectFormat format)
    {
        var root = NewTempProjectRoot();
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Adapter Preflight Test");
        var service = new EditingProjectExportService();
        var project = new SceneInteractionBuilder().Build("陈默说：我来晚了。");

        var exception = await Assert.ThrowsAsync<NotSupportedException>(() =>
            service.PreflightAsync(new EditingProjectExportRequest(
                project,
                WorkspaceProjectManager.ToWorkspace(manifest),
                format,
                new TimelineExportOptions())));

        Assert.Contains("预检", exception.Message, StringComparison.Ordinal);
        Assert.Contains("FCPXML", exception.Message, StringComparison.Ordinal);
        Assert.Contains("剪映", exception.Message, StringComparison.Ordinal);
    }

    private static string NewTempProjectRoot()
    {
        return Path.Combine(Path.GetTempPath(), $"draft-agent-project-{Guid.NewGuid():N}");
    }

    private static void AssertVisualBinding(VisualAssetBinding expected, VisualAssetBinding? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.SourceKind, actual.SourceKind);
        Assert.Equal(expected.SourceAssetId, actual.SourceAssetId);
        Assert.Equal(expected.RenderedAssetId, actual.RenderedAssetId);
        Assert.Equal(expected.PsdSelection?.LayerIds ?? [], actual.PsdSelection?.LayerIds ?? []);
        Assert.Equal(expected.PsdSelection?.LayerNames ?? [], actual.PsdSelection?.LayerNames ?? []);
        Assert.Equal(expected.PsdSelection?.PresetId, actual.PsdSelection?.PresetId);
        Assert.Equal(expected.PsdSelection?.Emotion, actual.PsdSelection?.Emotion);
        Assert.Equal(expected.PsdSelection?.Action, actual.PsdSelection?.Action);
        Assert.Equal(expected.PsdSelection?.Bounds, actual.PsdSelection?.Bounds);
        Assert.Equal(expected.PsdSelection?.SourceFingerprint, actual.PsdSelection?.SourceFingerprint);
        Assert.Equal(expected.PsdSelection?.RenderCacheKey, actual.PsdSelection?.RenderCacheKey);
    }

    private static async Task<string> CreateFileAsync(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes);
        return path;
    }

    private static async Task<string> CreateWavFileAsync(string path, double durationSeconds)
    {
        const ushort channels = 1;
        const uint sampleRate = 16000;
        const ushort bitsPerSample = 16;
        const ushort blockAlign = channels * bitsPerSample / 8;
        const uint byteRate = sampleRate * blockAlign;
        var dataSize = (uint)Math.Round(durationSeconds * byteRate);
        await using var stream = File.Create(path);
        await using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36u + dataSize);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16u);
        writer.Write((ushort)1);
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

    private static void AssertFcpxmlFrameTime(string? value)
    {
        Assert.False(string.IsNullOrWhiteSpace(value));
        if (value == "0s")
        {
            return;
        }

        Assert.EndsWith("s", value);
        var parts = value[..^1].Split('/');
        Assert.Equal(2, parts.Length);
        var numerator = long.Parse(parts[0]);
        var denominator = long.Parse(parts[1]);
        Assert.Equal(24000, denominator);
        Assert.Equal(0, numerator % 1001);
    }

    private static int CountChineseCharacters(string text)
    {
        return text.EnumerateRunes().Count(rune =>
        {
            var value = rune.Value;
            return value is >= 0x3400 and <= 0x4DBF ||
                value is >= 0x4E00 and <= 0x9FFF ||
                value is >= 0xF900 and <= 0xFAFF ||
                value is >= 0x20000 and <= 0x2A6DF ||
                value is >= 0x2A700 and <= 0x2B73F ||
                value is >= 0x2B740 and <= 0x2B81F ||
                value is >= 0x2B820 and <= 0x2CEAF;
        });
    }

    private static async Task<string> CreateFlacHeaderFileAsync(string path, double durationSeconds)
    {
        const int sampleRate = 16000;
        var totalSamples = (long)Math.Round(durationSeconds * sampleRate);
        var streamInfo = new byte[34];
        WriteUInt16BigEndian(streamInfo, 0, 4096);
        WriteUInt16BigEndian(streamInfo, 2, 4096);
        WriteUInt24BigEndian(streamInfo, 4, 0);
        WriteUInt24BigEndian(streamInfo, 7, 0);
        streamInfo[10] = (byte)(sampleRate >> 12);
        streamInfo[11] = (byte)((sampleRate >> 4) & 0xFF);
        streamInfo[12] = (byte)(((sampleRate & 0x0F) << 4) | 0x06);
        streamInfo[13] = (byte)(((totalSamples >> 32) & 0x0F));
        streamInfo[14] = (byte)(totalSamples >> 24);
        streamInfo[15] = (byte)(totalSamples >> 16);
        streamInfo[16] = (byte)(totalSamples >> 8);
        streamInfo[17] = (byte)totalSamples;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await using var writer = new BinaryWriter(stream);
        writer.Write("fLaC"u8.ToArray());
        writer.Write((byte)0x80);
        writer.Write([0x00, 0x00, 0x22]);
        writer.Write(streamInfo);
        return path;
    }

    private static void WriteUInt16BigEndian(byte[] target, int offset, int value)
    {
        target[offset] = (byte)(value >> 8);
        target[offset + 1] = (byte)value;
    }

    private static void WriteUInt24BigEndian(byte[] target, int offset, int value)
    {
        target[offset] = (byte)(value >> 16);
        target[offset + 1] = (byte)(value >> 8);
        target[offset + 2] = (byte)value;
    }

    private static async Task<SourceRoleTimelineFixture> BuildSourceRoleTimelineFixtureAsync(WorkspaceProject workspace)
    {
        var sceneImagePath = await CreateFileAsync(Path.Combine(workspace.Folders.ImagesImported, "scene_001.png"), [1]);
        var characterImagePath = await CreateFileAsync(Path.Combine(workspace.Folders.ImagesImported, "chen.png"), [2]);
        var thoughtAudioPath = await CreateWavFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "thought.wav"), 1.5);
        var actionAudioPath = await CreateWavFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "action.wav"), 2);
        var narrationAudioPath = await CreateWavFileAsync(Path.Combine(workspace.Folders.AudioGenerated, "narration.wav"), 2.5);
        var character = new Character("char_chen", "陈默", "主角", "", "", "Calm", new AssetBinding(Portrait: characterImagePath));
        var thought = SourceRoleNarrationInteraction(
            "int_thought",
            1,
            "thought",
            "陈默心里一沉。",
            character);
        var action = SourceRoleNarrationInteraction(
            "int_action",
            2,
            "action",
            "陈默抬手示意别出声。",
            character);
        var narration = SourceRoleNarrationInteraction(
            "int_narration",
            3,
            "narration",
            "雨声压低了空气。",
            null);
        var scene = new InteractionScene(
            "scene_001",
            1,
            "雨夜街口",
            "街口",
            "夜",
            "雨",
            "压抑",
            "雨声压抑",
            new SceneAssetBinding(sceneImagePath),
            [thought, action, narration]);
        var project = new InteractionProject(
            "project_source_role_tracks",
            "Source Role Tracks",
            new ScriptDocument(
                "陈默心里一沉。陈默抬手示意别出声。雨声压低了空气。",
                "陈默心里一沉。陈默抬手示意别出声。雨声压低了空气。",
                []),
            [character],
            [scene],
            [
                CompletedTtsTask("tts_int_thought", thought.Id, thoughtAudioPath),
                CompletedTtsTask("tts_int_action", action.Id, actionAudioPath),
                CompletedTtsTask("tts_int_narration", narration.Id, narrationAudioPath)
            ],
            []);

        return new SourceRoleTimelineFixture(project, characterImagePath, thoughtAudioPath, actionAudioPath, narrationAudioPath);
    }

    private static CharacterInteraction SourceRoleNarrationInteraction(
        string id,
        int order,
        string sourceItemType,
        string text,
        Character? character)
    {
        return new CharacterInteraction(
            id,
            order,
            InteractionType.Narration,
            "scene_001",
            null,
            null,
            new EmotionSpec("Calm", 0.5, string.Empty, string.Empty, string.Empty, string.Empty),
            new ActionSpec(text, [], string.Empty, 2),
            null,
            null,
            new NarrationLine($"nar_{id}", text, new VoiceRenderSettings(null, "Calm", 0.5, 1, 0)),
            new InteractionBinding(null, null),
            [],
            string.Empty)
        {
            SourceItemType = sourceItemType,
            SourceRole = character?.Name ?? string.Empty,
            SourceRoleCharacterId = character?.Id
        };
    }

    private static string[] DraftAudioPathsForTrack(JsonElement draftContent, string trackName)
    {
        var audioPathsById = draftContent
            .GetProperty("materials")
            .GetProperty("audios")
            .EnumerateArray()
            .ToDictionary(
                audio => audio.GetProperty("id").GetString() ?? string.Empty,
                audio => audio.GetProperty("path").GetString() ?? string.Empty,
                StringComparer.Ordinal);

        return TrackByName(draftContent, trackName)
            .GetProperty("segments")
            .EnumerateArray()
            .Select(segment => audioPathsById[segment.GetProperty("material_id").GetString() ?? string.Empty])
            .ToArray();
    }

    private static JsonElement TrackByName(JsonElement draftContent, string trackName)
    {
        return draftContent
            .GetProperty("tracks")
            .EnumerateArray()
            .Single(track => string.Equals(track.GetProperty("name").GetString(), trackName, StringComparison.Ordinal));
    }

    private static InteractionProject BuildProjectWithAssets(string imagePath, string audioPath)
    {
        var script = new ScriptDocument("陈默走进雨里。陈默低声说：我来晚了。", "陈默走进雨里。陈默低声说：我来晚了。", []);
        var character = new Character("char_chen", "陈默", "主角", "", "", "Calm", new AssetBinding(Portrait: imagePath));
        var narration = new CharacterInteraction(
            "int_001",
            1,
            InteractionType.Narration,
            "scene_001",
            null,
            null,
            new EmotionSpec(string.Empty, 0, string.Empty, string.Empty, string.Empty, string.Empty),
            new ActionSpec("走进雨里", [], "", 2),
            null,
            null,
            new NarrationLine("nar_001", "陈默走进雨里。", new VoiceRenderSettings(null, string.Empty, 0, 1, 0)),
            new InteractionBinding(imagePath, null, null, null, null),
            [],
            string.Empty);
        var dialogueVoice = new VoiceRenderSettings(null, "Melancholic", 0.8, 1, 0.3);
        var dialogue = new CharacterInteraction(
            "int_002",
            2,
            InteractionType.Dialogue,
            "scene_001",
            character.Id,
            null,
            new EmotionSpec("Melancholic", 0.8, "", "", "", ""),
            new ActionSpec("低声说话", [], "", 3),
            new DialogueLine("dia_001", character.Id, null, "我来晚了。", dialogueVoice),
            null,
            null,
            new InteractionBinding(imagePath, imagePath, null, null, null),
            [],
            string.Empty);
        var scene = new InteractionScene(
            "scene_001",
            1,
            "雨夜街口",
            "街口",
            "夜",
            "雨",
            "压抑",
            "雨声压抑",
            new SceneAssetBinding(imagePath),
            [narration, dialogue]);
        var ttsTask = new GenerationTask(
            "tts_int_002",
            GenerationTaskType.Tts,
            "int_002",
            "TTS 陈默 台词",
            "comfyui",
            GenerationTaskStatus.Completed,
            new Dictionary<string, string> { ["text"] = "我来晚了。", ["emotion"] = "Melancholic" },
            audioPath);
        return new InteractionProject(
            "project_test",
            "Rain Story",
            script,
            [character],
            [scene],
            [ttsTask],
            []);
    }

    private static InteractionProject DialogueOnlyProject(InteractionProject project)
    {
        var dialogue = project.Scenes.Single().Interactions.Single(interaction => interaction.Dialogue is not null);
        return project with
        {
            Scenes =
            [
                project.Scenes.Single() with
                {
                    Interactions = [dialogue]
                }
            ]
        };
    }

    private static InteractionProject WithCompletedNarrationAudio(InteractionProject project, string audioPath)
    {
        var narration = project.Scenes.Single().Interactions.Single(interaction => interaction.Narration is not null);
        return project with
        {
            GenerationTasks =
            [
                ..project.GenerationTasks,
                new GenerationTask(
                    $"tts_{narration.Id}",
                    GenerationTaskType.Tts,
                    narration.Id,
                    "TTS 旁白",
                    "comfyui",
                    GenerationTaskStatus.Completed,
                    new Dictionary<string, string> { ["text"] = narration.Narration?.Text ?? string.Empty, ["emotion"] = "Calm" },
                    audioPath)
            ]
        };
    }

    private static GenerationTask CompletedTtsTask(string taskId, string targetId, string outputReference)
    {
        return new GenerationTask(
            taskId,
            GenerationTaskType.Tts,
            targetId,
            "TTS",
            "comfyui",
            GenerationTaskStatus.Completed,
            new Dictionary<string, string>
            {
                ["text"] = targetId,
                ["emotion"] = "Calm",
                ["emotionStrength"] = "0.6",
                ["voiceReferenceId"] = string.Empty
            },
            outputReference);
    }

    private sealed record SourceRoleTimelineFixture(
        InteractionProject Project,
        string CharacterImagePath,
        string ThoughtAudioPath,
        string ActionAudioPath,
        string NarrationAudioPath);

    private sealed class DownloadingComfyClient : IComfyUiClient, IComfyOutputFileClient
    {
        public List<string> DownloadedReferences { get; } = [];

        public Task<ComfyTaskResult> SubmitAsync(ComfyTaskRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public async Task<string> DownloadOutputAsync(string outputReference, string destinationDirectory, CancellationToken cancellationToken = default)
        {
            DownloadedReferences.Add(outputReference);
            var parsed = ComfyOutputReference.Parse(outputReference);
            Directory.CreateDirectory(destinationDirectory);
            var path = Path.Combine(destinationDirectory, parsed.Filename);
            await File.WriteAllBytesAsync(path, [1, 2, 3, 4], cancellationToken);
            return path;
        }
    }

    private sealed class CapturingProjectMediaAssetService : IProjectMediaAssetService
    {
        public bool WasCalled { get; private set; }

        public MediaImportRequest? LastRequest { get; private set; }

        public Task<MediaImportResult> ImportAsync(WorkspaceProject workspace, MediaImportRequest request, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            LastRequest = request;
            var now = DateTimeOffset.UtcNow;
            var asset = new ProjectMediaAsset(
                "asset_injected",
                request.Kind,
                request.Source,
                request.DisplayName ?? "Injected asset",
                request.SourcePath,
                "assets/injected/source.png",
                request.TargetId,
                request.SourcePath,
                null,
                request.Metadata ?? new Dictionary<string, string>(),
                now);
            var manifest = new WorkspaceProjectManifest(
                WorkspaceProjectManager.ManifestSchema,
                workspace.ProjectId,
                workspace.Name,
                workspace.RootPath,
                workspace.DraftPath,
                workspace.OriginalAiJsonPath,
                workspace.Folders,
                [asset],
                workspace.CreatedAt,
                now);
            return Task.FromResult(new MediaImportResult(asset, manifest));
        }

    }
}

