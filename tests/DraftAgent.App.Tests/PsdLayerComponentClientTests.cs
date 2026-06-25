using DraftAgent.App;
using DraftAgent.App.Psd.Services;
using DraftAgent.App.ViewModels;
using DraftAgent.Core;
using PsdLayerLoader.Core;
using System.Text.Json;

namespace DraftAgent.App.Tests;

public sealed class PsdLayerComponentClientTests
{
    [Fact]
    public async Task GenerateAndImportAsyncRunsIntegratedComponentAndImportsReturnedPng()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-client-{Guid.NewGuid():N}");
        var manifest = await new WorkspaceProjectManager().CreateOrOpenAsync(root, "PSD Client Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var component = new FakePsdComponentRunner();
        var client = new PsdLayerComponentClient(new ProjectMediaAssetService(), component);
        var psdPath = await CreateFileAsync(Path.Combine(root, "character.psd"), [1, 2, 3]);
        var request = new PsdLayerComponentBindingRequest(
            BindingTargetKind.Character,
            "char_chen",
            "Chen",
            "char_chen",
            "Chen",
            "Lead",
            "asset_psd_chen",
            psdPath,
            "Calm",
            "UpperLeft",
            "line text",
            "scene_001",
            "interaction_001",
            PsdExportBounds.CanvasBounds,
            new PsdRenderSelection(
                [21, 34],
                ["Base", "Smile"],
                "preset_smile",
                "Calm",
                "UpperLeft",
                VisualExportBounds.CanvasBounds,
                "source-fp",
                "cache-key",
                ["Base", "Smile", "Frown"]));

        var result = await client.GenerateAndImportAsync(
            workspace,
            manifest,
            request,
            new PsdLayerComponentOptions("fake-component.exe", TimeSpan.FromSeconds(5)));

        Assert.StartsWith("asset_", result.ImportedAsset.Id);
        Assert.Equal(MediaAssetKind.Image, result.ImportedAsset.Kind);
        Assert.Equal(MediaAssetSource.ImportedPng, result.ImportedAsset.Source);
        Assert.Equal("char_chen", result.ImportedAsset.TargetId);
        Assert.True(File.Exists(result.ImportedAsset.AbsolutePath));
        Assert.Contains("assets/images/imported", result.ImportedAsset.ProjectRelativePath);
        Assert.Contains(result.Manifest.Assets, asset => asset.Id == result.ImportedAsset.Id);

        var componentRequest = component.LastRequest;
        Assert.NotNull(componentRequest);
        Assert.Equal(PsdComponentMode.Headless, componentRequest.Mode);
        Assert.Equal("Calm", componentRequest.Intent.Emotion);
        Assert.Equal("UpperLeft", componentRequest.Intent.Action);
        Assert.Equal([21, 34], componentRequest.SelectionHint?.LayerIds);
        Assert.Equal(["Base", "Smile"], componentRequest.SelectionHint?.LayerNames);
        Assert.Equal("preset_smile", componentRequest.SelectionHint?.PresetId);
        Assert.Equal("source-fp", componentRequest.SelectionHint?.SourceFingerprint);
        Assert.Equal("cache-key", componentRequest.SelectionHint?.RenderCacheKey);
        Assert.Equal(["Base", "Smile", "Frown"], componentRequest.SelectionHint?.ControlledLayerNames);
        Assert.StartsWith(Path.Combine(workspace.Folders.Logs, "psd-requests"), componentRequest.Output.Directory);
        Assert.EndsWith(Path.Combine(componentRequest.RequestId, "output"), componentRequest.Output.Directory);
        Assert.DoesNotContain(workspace.Folders.ImagesImported, componentRequest.Output.Directory);
        Assert.NotNull(result.Response.Png);
        Assert.True(File.Exists(result.Response.Png.Path));
        Assert.NotEqual(result.Response.Png.Path, result.ImportedAsset.AbsolutePath);
        Assert.DoesNotContain("assets/images/imported", Path.GetRelativePath(workspace.RootPath, result.Response.Png.Path).Replace('\\', '/'));
        Assert.False(Directory.GetFiles(workspace.Folders.Logs, "*.json", SearchOption.AllDirectories).Any());

        Assert.Equal("PsdLayerLoader", result.ImportedAsset.Metadata["source"]);
        Assert.Equal("asset_psd_chen", result.ImportedAsset.Metadata["sourcePsdAssetId"]);
        Assert.Equal("Calm", result.ImportedAsset.Metadata["sourceEmotion"]);
        Assert.Equal("UpperLeft", result.ImportedAsset.Metadata["sourceAction"]);
        Assert.Equal("12,18", result.ImportedAsset.Metadata["matchedLayerIds"]);
        Assert.Equal("0.82", result.ImportedAsset.Metadata["matchConfidence"]);
        Assert.Equal(BindingTargetKind.Character.ToString(), result.ImportedAsset.Metadata["bindingKind"]);
    }

    [Fact]
    public async Task GenerateAndImportAsyncWritesInteractiveRequestWhenRequested()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-client-{Guid.NewGuid():N}");
        var manifest = await new WorkspaceProjectManager().CreateOrOpenAsync(root, "PSD Client Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var component = new FakePsdComponentRunner();
        var client = new PsdLayerComponentClient(new ProjectMediaAssetService(), component);
        var request = (await CreateBindingRequestAsync(root)) with { Mode = PsdComponentMode.Interactive };

        await client.GenerateAndImportAsync(
            workspace,
            manifest,
            request,
            new PsdLayerComponentOptions("fake-component.exe", TimeSpan.FromSeconds(5)));

        Assert.Equal(PsdComponentMode.Interactive, component.LastRequest?.Mode);
    }

    [Fact]
    public async Task GenerateAndImportAsyncRejectsResponseForDifferentRequestId()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-client-{Guid.NewGuid():N}");
        var manifest = await new WorkspaceProjectManager().CreateOrOpenAsync(root, "PSD Client Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var component = new FakePsdComponentRunner
        {
            BuildResponseWithCancellationAsync = async (request, cancellationToken) =>
            {
                var pngPath = await WriteOutputPngAsync(request, cancellationToken);
                return PsdLayerResponse.Completed(
                    $"{request.RequestId}_wrong",
                    new PsdPngResult(pngPath, 1024, 1024, request.Output.Bounds),
                    new PsdLayerMatchResult(0.82, request.Intent.Emotion, request.Intent.Action, null, [12], ["UpperLeft"]),
                    new PsdResponseMetadata(request.Psd.AssetId, request.Psd.Path));
            }
        };
        var client = new PsdLayerComponentClient(new ProjectMediaAssetService(), component);
        var request = await CreateBindingRequestAsync(root);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateAndImportAsync(
            workspace,
            manifest,
            request,
            new PsdLayerComponentOptions("fake-component.exe", TimeSpan.FromSeconds(5))));

        Assert.Contains("requestId", ex.Message);
        Assert.Empty(Directory.Exists(workspace.Folders.ImagesImported)
            ? Directory.GetFiles(workspace.Folders.ImagesImported)
            : []);
    }

    [Fact]
    public async Task GenerateAndImportAsyncReportsInteractiveCancellationWithoutImporting()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-client-{Guid.NewGuid():N}");
        var manifest = await new WorkspaceProjectManager().CreateOrOpenAsync(root, "PSD Client Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var component = new FakePsdComponentRunner
        {
            BuildResponseAsync = request => Task.FromResult(PsdLayerResponse.Cancelled(request.RequestId))
        };
        var client = new PsdLayerComponentClient(new ProjectMediaAssetService(), component);
        var request = (await CreateBindingRequestAsync(root)) with { Mode = PsdComponentMode.Interactive };

        var ex = await Assert.ThrowsAsync<PsdLayerSelectionCancelledException>(() => client.GenerateAndImportAsync(
            workspace,
            manifest,
            request,
            new PsdLayerComponentOptions("fake-component.exe", TimeSpan.FromSeconds(5))));

        Assert.StartsWith("psd_char_chen_", ex.RequestId, StringComparison.Ordinal);
        Assert.Empty(Directory.Exists(workspace.Folders.ImagesImported)
            ? Directory.GetFiles(workspace.Folders.ImagesImported)
            : []);
    }

    [Fact]
    public async Task GenerateAndImportAsyncRejectsCompletedResponseForDifferentPsdSource()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-client-{Guid.NewGuid():N}");
        var manifest = await new WorkspaceProjectManager().CreateOrOpenAsync(root, "PSD Client Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var component = new FakePsdComponentRunner
        {
            BuildResponseWithCancellationAsync = async (request, cancellationToken) =>
            {
                var pngPath = await WriteOutputPngAsync(request, cancellationToken);
                return PsdLayerResponse.Completed(
                    request.RequestId,
                    new PsdPngResult(pngPath, 1024, 1024, request.Output.Bounds),
                    new PsdLayerMatchResult(0.82, request.Intent.Emotion, request.Intent.Action, null, [12], ["UpperLeft"]),
                    new PsdResponseMetadata("asset_other_psd", Path.Combine(root, "other.psd")));
            }
        };
        var client = new PsdLayerComponentClient(new ProjectMediaAssetService(), component);
        var request = await CreateBindingRequestAsync(root);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateAndImportAsync(
            workspace,
            manifest,
            request,
            new PsdLayerComponentOptions("fake-component.exe", TimeSpan.FromSeconds(5))));

        Assert.Contains("source PSD", ex.Message);
        Assert.Empty(Directory.Exists(workspace.Folders.ImagesImported)
            ? Directory.GetFiles(workspace.Folders.ImagesImported)
            : []);
    }

    [Fact]
    public async Task GenerateAndImportAsyncRejectsCompletedResponseWithMissingPng()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-client-{Guid.NewGuid():N}");
        var manifest = await new WorkspaceProjectManager().CreateOrOpenAsync(root, "PSD Client Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var component = new FakePsdComponentRunner
        {
            BuildResponseAsync = request =>
            {
                var pngPath = Path.Combine(request.Output.Directory, request.Output.FileNameHint);
                return Task.FromResult(PsdLayerResponse.Completed(
                    request.RequestId,
                    new PsdPngResult(pngPath, 1024, 1024, request.Output.Bounds),
                    new PsdLayerMatchResult(0.82, request.Intent.Emotion, request.Intent.Action, null, [12], ["UpperLeft"]),
                    new PsdResponseMetadata(request.Psd.AssetId, request.Psd.Path)));
            }
        };
        var client = new PsdLayerComponentClient(new ProjectMediaAssetService(), component);
        var request = await CreateBindingRequestAsync(root);

        await Assert.ThrowsAsync<FileNotFoundException>(() => client.GenerateAndImportAsync(
            workspace,
            manifest,
            request,
            new PsdLayerComponentOptions("fake-component.exe", TimeSpan.FromSeconds(5))));
    }

    [Fact]
    public async Task GenerateAndImportAsyncRejectsCompletedResponseWithNonPngPath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-client-{Guid.NewGuid():N}");
        var manifest = await new WorkspaceProjectManager().CreateOrOpenAsync(root, "PSD Client Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var component = new FakePsdComponentRunner
        {
            BuildResponseWithCancellationAsync = async (request, cancellationToken) =>
            {
                Directory.CreateDirectory(request.Output.Directory);
                var pngPath = Path.Combine(request.Output.Directory, "not-a-png.jpg");
                await File.WriteAllBytesAsync(pngPath, [137, 80, 78, 71, 13, 10, 26, 10], cancellationToken);
                return PsdLayerResponse.Completed(
                    request.RequestId,
                    new PsdPngResult(pngPath, 1024, 1024, request.Output.Bounds),
                    new PsdLayerMatchResult(0.82, request.Intent.Emotion, request.Intent.Action, null, [12], ["UpperLeft"]),
                    new PsdResponseMetadata(request.Psd.AssetId, request.Psd.Path));
            }
        };
        var client = new PsdLayerComponentClient(new ProjectMediaAssetService(), component);
        var request = await CreateBindingRequestAsync(root);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateAndImportAsync(
            workspace,
            manifest,
            request,
            new PsdLayerComponentOptions("fake-component.exe", TimeSpan.FromSeconds(5))));

        Assert.Contains(".png", ex.Message);
    }

    [Fact]
    public void BuildImportRequestPreservesPsdResponseMetadata()
    {
        var request = new PsdLayerComponentBindingRequest(
            BindingTargetKind.Character,
            "char_chen",
            "Chen",
            "char_chen",
            "Chen",
            null,
            "asset_psd_chen",
            "character.psd",
            "Calm",
            "turn_left",
            null,
            "scene_001",
            "interaction_001");
        var pngPath = Path.Combine(Path.GetTempPath(), "chen.png");
        var response = PsdLayerResponse.Completed(
            "req_001",
            new PsdPngResult(pngPath, 100, 200, PsdExportBounds.CanvasBounds),
            new PsdLayerMatchResult(0.9, "Calm", "turn_left", "preset_1", [1, 2], ["Base", "Eyes"]),
            new PsdResponseMetadata(
                "asset_psd_chen",
                "character.psd",
                "source-fp",
                "render-cache-key"));

        var import = PsdLayerComponentClient.BuildImportRequest(request, response);

        Assert.Equal(pngPath, import.SourcePath);
        Assert.Equal(MediaAssetKind.Image, import.Kind);
        Assert.Equal(MediaAssetSource.ImportedPng, import.Source);
        Assert.Equal("char_chen", import.TargetId);
        Assert.Equal("Base,Eyes", import.Metadata!["matchedLayerNames"]);
        Assert.Equal("preset_1", import.Metadata["matchedPresetId"]);
        Assert.Equal("source-fp", import.Metadata["sourceFingerprint"]);
        Assert.Equal("render-cache-key", import.Metadata["renderCacheKey"]);
    }

    [Fact]
    public async Task MainWindowViewModelGeneratePsdImageBindingWritesRenderedPngToCurrentRowWithoutReplacingPsdSource()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var fakeClient = new FakePsdLayerComponentClient();
        var viewModel = new MainWindowViewModel(service, loadLastProject: false, psdLayerComponentClient: fakeClient);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-vm-{Guid.NewGuid():N}");
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        var psdPath = await CreateFileAsync(Path.Combine(projectRoot, "character.psd"), [1, 2, 3]);
        var targetRow = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(row => row.SubjectCharacterId == target.TargetId || row.SubjectName == target.DisplayName);
        viewModel.SelectRowCommand.Execute(targetRow);
        await viewModel.GeneratePsdImageBindingAsync(
            target,
            "asset_psd_character",
            psdPath,
            "Calm",
            "Default",
            string.Empty,
            string.Empty,
            null);
        fakeClient.Reset();

        await viewModel.GeneratePsdImageBindingAsync(
            target,
            "asset_psd_character",
            psdPath,
            "Calm",
            "UpperLeft",
            "scene_001",
            "interaction_001",
            "line");

        Assert.NotNull(fakeClient.LastBindingRequest);
        Assert.Equal(target.TargetId, fakeClient.LastBindingRequest.TargetId);
        Assert.Equal("asset_psd_character", fakeClient.LastBindingRequest.PsdAssetId);
        Assert.NotEqual(fakeClient.ImportedAssetPath, target.ImagePreviewAsset);
        Assert.NotEqual(fakeClient.ImportedAssetPath, target.ImagePreviewSource);
        Assert.Equal("asset_psd_generated", targetRow.SelectedCharacterAsset);
        Assert.Equal(fakeClient.ImportedAssetPath, targetRow.SelectedCharacterPreviewAsset);
        Assert.Equal(fakeClient.ImportedAssetPath, targetRow.ObjectPreviewSource);
        Assert.True(targetRow.HasObjectPreviewImage);

        var rowDraft = viewModel.WorkbenchDocument.RowsById[targetRow.InteractionId];
        Assert.Equal("asset_psd_generated", RowVisualId(rowDraft));
        Assert.Equal(VisualSourceKind.Psd, rowDraft.CharacterVisualBinding?.SourceKind);
        Assert.Equal("asset_psd_character", rowDraft.CharacterVisualBinding?.SourceAssetId);
        Assert.Equal("asset_psd_generated", rowDraft.CharacterVisualBinding?.RenderedAssetId);
        Assert.Contains("psd-fp-v2", rowDraft.CharacterVisualBinding?.PsdSelection?.SourceFingerprint, StringComparison.Ordinal);
        Assert.Contains("asset_psd_character", rowDraft.CharacterVisualBinding?.PsdSelection?.SourceFingerprint, StringComparison.Ordinal);
        Assert.Contains(Path.GetFullPath(psdPath), rowDraft.CharacterVisualBinding?.PsdSelection?.SourceFingerprint, StringComparison.Ordinal);
        Assert.Contains(new FileInfo(psdPath).Length.ToString(System.Globalization.CultureInfo.InvariantCulture), rowDraft.CharacterVisualBinding?.PsdSelection?.SourceFingerprint, StringComparison.Ordinal);
        Assert.Contains(PsdComponentProtocol.RequestSchemaVersion, rowDraft.CharacterVisualBinding?.PsdSelection?.SourceFingerprint, StringComparison.Ordinal);
        Assert.Contains("psd-render-v2", rowDraft.CharacterVisualBinding?.PsdSelection?.RenderCacheKey, StringComparison.Ordinal);
        Assert.Contains(rowDraft.CharacterVisualBinding!.PsdSelection!.SourceFingerprint, rowDraft.CharacterVisualBinding.PsdSelection.RenderCacheKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MainWindowViewModelGeneratePsdImageBindingPassesPreviousRowPsdSelection()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var fakeClient = new FakePsdLayerComponentClient();
        var viewModel = new MainWindowViewModel(service, loadLastProject: false, psdLayerComponentClient: fakeClient);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-selection-{Guid.NewGuid():N}");
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        var psdPath = await CreateFileAsync(Path.Combine(projectRoot, "character.psd"), [1, 2, 3]);
        var targetRow = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(row => row.SubjectCharacterId == target.TargetId || row.SubjectName == target.DisplayName);
        viewModel.SelectRowCommand.Execute(targetRow);
        await viewModel.GeneratePsdImageBindingAsync(
            target,
            "asset_psd_character",
            psdPath,
            "Calm",
            "Default",
            string.Empty,
            string.Empty,
            null);
        fakeClient.Reset();

        await viewModel.GeneratePsdImageBindingAsync(
            target,
            "asset_psd_character",
            psdPath,
            "Calm",
            "UpperLeft",
            "scene_001",
            "interaction_001",
            "line");
        var firstSelection = viewModel.WorkbenchDocument.RowsById[targetRow.InteractionId].CharacterVisualBinding?.PsdSelection;
        Assert.NotNull(firstSelection);

        await viewModel.GeneratePsdImageBindingAsync(
            target,
            "asset_psd_character",
            psdPath,
            "Calm",
            "UpperLeft",
            "scene_001",
            "interaction_001",
            "line");

        Assert.NotNull(fakeClient.LastBindingRequest);
        Assert.Equal(firstSelection, fakeClient.LastBindingRequest.PreviousSelection);
    }

    [Fact]
    public async Task MainWindowViewModelRejectsRowPsdSaveWhenCharacterPsdSourceChangesDuringLoader()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var fakeClient = new FakePsdLayerComponentClient();
        var viewModel = new MainWindowViewModel(service, loadLastProject: false, psdLayerComponentClient: fakeClient);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-save-guard-{Guid.NewGuid():N}");
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        var firstPsdPath = await CreateFileAsync(Path.Combine(projectRoot, "character-first.psd"), [1, 2, 3]);
        var secondPsdPath = await CreateFileAsync(Path.Combine(projectRoot, "character-second.psd"), [4, 5, 6]);
        var targetRow = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(row => row.SubjectCharacterId == target.TargetId || row.SubjectName == target.DisplayName);
        await viewModel.GeneratePsdImageBindingAsync(
            target,
            "asset_psd_first",
            firstPsdPath,
            "Calm",
            "Default",
            string.Empty,
            string.Empty,
            null);
        fakeClient.Reset();
        fakeClient.BeforeReturnResultAsync = async () =>
        {
            fakeClient.BeforeReturnResultAsync = null;
            await viewModel.GeneratePsdImageBindingAsync(
                target,
                "asset_psd_second",
                secondPsdPath,
                "Calm",
                "Default",
                string.Empty,
                string.Empty,
                null);
        };

        var applied = await viewModel.GeneratePsdImageBindingAsync(
            target,
            "asset_psd_first",
            firstPsdPath,
            "Calm",
            "UpperLeft",
            targetRow.OwnerScene?.SceneId ?? viewModel.SceneTables[0].SceneId,
            targetRow.InteractionId,
            targetRow.LineText);

        Assert.False(applied);
        Assert.Contains("同一个 PSD", viewModel.StatusText, StringComparison.Ordinal);
        Assert.NotEqual("asset_psd_generated", RowVisualId(viewModel.WorkbenchDocument.RowsById[targetRow.InteractionId]));
    }

    [Fact]
    public async Task MainWindowViewModelGeneratePsdImageBindingIgnoresPreviousSelectionWhenFingerprintChanges()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var fakeClient = new FakePsdLayerComponentClient();
        var viewModel = new MainWindowViewModel(service, loadLastProject: false, psdLayerComponentClient: fakeClient);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-fingerprint-{Guid.NewGuid():N}");
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        var psdPath = await CreateFileAsync(Path.Combine(projectRoot, "character.psd"), [1, 2, 3]);
        var targetRow = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(row => row.SubjectCharacterId == target.TargetId || row.SubjectName == target.DisplayName);
        viewModel.SelectRowCommand.Execute(targetRow);
        await viewModel.GeneratePsdImageBindingAsync(
            target,
            "asset_psd_character",
            psdPath,
            "Calm",
            "Default",
            string.Empty,
            string.Empty,
            null);
        fakeClient.Reset();

        await viewModel.GeneratePsdImageBindingAsync(
            target,
            "asset_psd_character",
            psdPath,
            "Calm",
            "UpperLeft",
            "scene_001",
            "interaction_001",
            "line");
        var firstSelection = viewModel.WorkbenchDocument.RowsById[targetRow.InteractionId].CharacterVisualBinding?.PsdSelection;
        Assert.NotNull(firstSelection);

        await File.AppendAllBytesAsync(psdPath, [4]);
        fakeClient.Reset();
        await viewModel.GeneratePsdImageBindingAsync(
            target,
            "asset_psd_character",
            psdPath,
            "Calm",
            "UpperLeft",
            "scene_001",
            "interaction_001",
            "line");

        Assert.NotNull(fakeClient.LastBindingRequest);
        Assert.Null(fakeClient.LastBindingRequest.PreviousSelection);
        Assert.Contains("previous layer selection ignored", viewModel.StatusText);
        var secondSelection = viewModel.WorkbenchDocument.RowsById[targetRow.InteractionId].CharacterVisualBinding?.PsdSelection;
        Assert.NotNull(secondSelection);
        Assert.NotEqual(firstSelection.SourceFingerprint, secondSelection.SourceFingerprint);
    }

    [Fact]
    public async Task MainWindowViewModelReselectPsdLayersUsesExistingPsdSourceInInteractiveMode()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var fakeClient = new FakePsdLayerComponentClient();
        var viewModel = new MainWindowViewModel(service, loadLastProject: false, psdLayerComponentClient: fakeClient);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-reselect-{Guid.NewGuid():N}");
        var psdPath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"character-{Guid.NewGuid():N}.psd"), [1, 2, 3]);
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        var targetRow = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(row => row.SubjectCharacterId == target.TargetId || row.SubjectName == target.DisplayName);
        viewModel.SelectRowCommand.Execute(targetRow);

        await viewModel.GeneratePsdImageBindingFromFileAsync(target, psdPath);
        target = viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Character && binding.TargetId == target.TargetId);
        var sourceAssetId = target.VisualBinding.SourceAssetId;
        Assert.False(string.IsNullOrWhiteSpace(sourceAssetId));
        fakeClient.Reset();

        await viewModel.GeneratePsdImageBindingFromExistingSourceAsync(target);

        Assert.NotNull(fakeClient.LastBindingRequest);
        Assert.Equal(PsdComponentMode.Interactive, fakeClient.LastBindingRequest.Mode);
        Assert.Equal(sourceAssetId, fakeClient.LastBindingRequest.PsdAssetId);
        Assert.Null(fakeClient.LastBindingRequest.PreviousSelection);
        Assert.Equal(string.Empty, fakeClient.LastBindingRequest.InteractionId);
    }

    [Fact]
    public async Task MainWindowViewModelTreatsInteractivePsdCancellationAsNonFailure()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var fakeClient = new FakePsdLayerComponentClient
        {
            CancelNextGeneration = true
        };
        var viewModel = new MainWindowViewModel(service, loadLastProject: false, psdLayerComponentClient: fakeClient);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-cancel-{Guid.NewGuid():N}");
        var psdPath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"character-{Guid.NewGuid():N}.psd"), [1, 2, 3]);
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        var targetRow = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(row => row.SubjectCharacterId == target.TargetId || row.SubjectName == target.DisplayName);
        viewModel.SelectRowCommand.Execute(targetRow);
        await viewModel.GeneratePsdImageBindingFromFileAsync(target, psdPath);
        target = viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Character && binding.TargetId == target.TargetId);

        var result = await viewModel.GenerateSelectedRowPsdImageFromCharacterSourceAsync(targetRow);

        Assert.False(result);
        Assert.Equal("PSD 图层选择已取消", target.StatusText);
        Assert.Contains("已取消", viewModel.StatusText);
        Assert.DoesNotContain("失败", viewModel.StatusText);
    }

    [Fact]
    public async Task MatchPsdExpressionLayersStopsBeforeRenderingWhenLayerMatchSettingsMissing()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var fakeClient = new FakePsdLayerComponentClient();
        var viewModel = new MainWindowViewModel(
            service,
            loadLastProject: false,
            psdLayerComponentClient: fakeClient,
            aiModelClientFactory: () => new FakePsdLayerAiModelClient());
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-expression-missing-{Guid.NewGuid():N}");
        var psdPath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"character-{Guid.NewGuid():N}.psd"), [1, 2, 3]);
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);

        await viewModel.GeneratePsdImageBindingFromFileAsync(target, psdPath);
        fakeClient.Reset();
        await viewModel.MatchPsdExpressionLayersCommand.ExecuteAsync(null);

        Assert.Equal(0, fakeClient.GenerationCallCount);
        Assert.Contains("尚未设置 PSD 图层模板", viewModel.StatusText);
    }

    [Fact]
    public async Task MatchPsdExpressionLayersRendersRowsHeadlesslyFromCurrentEmotion()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var fakeClient = new FakePsdLayerComponentClient();
        var fakeAi = new FakePsdLayerAiModelClient();
        var viewModel = new MainWindowViewModel(
            service,
            loadLastProject: false,
            psdLayerComponentClient: fakeClient,
            aiModelClientFactory: () => fakeAi);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-expression-match-{Guid.NewGuid():N}");
        var psdPath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"character-{Guid.NewGuid():N}.psd"), [1, 2, 3]);
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        var targetRows = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .Where(row => row.SubjectCharacterId == target.TargetId || row.SubjectName == target.DisplayName)
            .ToArray();
        Assert.NotEmpty(targetRows);
        targetRows[0].EmotionVectors.Single(vector => vector.Key == "Happy").Value = 0.9;

        await viewModel.GeneratePsdImageBindingFromFileAsync(target, psdPath);
        target = viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Character && binding.TargetId == target.TargetId);
        Assert.True(viewModel.ApplyPsdLayerMatchConfiguration(target, TestPsdLayerMatchConfiguration()));
        fakeClient.Reset();
        await viewModel.MatchPsdExpressionLayersCommand.ExecuteAsync(null);

        Assert.True(fakeClient.GenerationCallCount > 0);
        Assert.Equal(1, fakeAi.ConnectionCheckCount);
        Assert.NotEmpty(fakeAi.JsonRequests);
        Assert.All(fakeAi.JsonRequests, request =>
        {
            Assert.Equal("psd_layer_selection_v1", request.SchemaName);
            Assert.Contains("固定词汇分类器", request.SystemPrompt, StringComparison.Ordinal);
            Assert.Contains("只根据输入 text", request.SystemPrompt, StringComparison.Ordinal);
            Assert.Contains("不得新增字段", request.SystemPrompt, StringComparison.Ordinal);
            Assert.Contains("皱眉", request.SystemPrompt, StringComparison.Ordinal);
            Assert.Contains("张嘴", request.SystemPrompt, StringComparison.Ordinal);
            Assert.DoesNotContain("expression", request.SystemPrompt, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("整体表情", request.SystemPrompt, StringComparison.Ordinal);
            Assert.DoesNotContain("动作(动作)", request.SystemPrompt, StringComparison.Ordinal);
            Assert.DoesNotContain("\"action\"", request.SystemPrompt, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("固定词汇表：", request.SystemPrompt, StringComparison.Ordinal);
            Assert.True(
                request.SystemPrompt.IndexOf("返回 JSON 格式：", StringComparison.Ordinal) <
                request.SystemPrompt.IndexOf("固定词汇表：", StringComparison.Ordinal));
            Assert.StartsWith("rows:", request.UserText, StringComparison.Ordinal);
            Assert.DoesNotContain("固定词汇表：", request.UserText, StringComparison.Ordinal);
            Assert.DoesNotContain("皱眉", request.UserText, StringComparison.Ordinal);
            Assert.DoesNotContain("张嘴", request.UserText, StringComparison.Ordinal);
            Assert.Contains("rows:", request.UserText, StringComparison.Ordinal);
            Assert.DoesNotContain("\"scene\"", request.UserText, StringComparison.Ordinal);
            Assert.DoesNotContain("\"sourceItemType\"", request.UserText, StringComparison.Ordinal);
            Assert.DoesNotContain("\"emotion\"", request.UserText, StringComparison.Ordinal);
            Assert.DoesNotContain("\"action\"", request.UserText, StringComparison.Ordinal);
            Assert.DoesNotContain("角色：", request.UserText, StringComparison.Ordinal);
            Assert.DoesNotContain("说明：", request.UserText, StringComparison.Ordinal);
            Assert.InRange(CountRowsInAiRequest(request.UserText), 1, 12);
        });
        Assert.All(fakeClient.BindingRequests, request =>
        {
            Assert.Equal(PsdComponentMode.Headless, request.Mode);
            Assert.Null(request.PreviousSelection);
            Assert.NotNull(request.SelectionOverride);
            Assert.Equal(VisualExportBounds.LayerBounds, request.SelectionOverride.Bounds);
            Assert.Contains("皱眉", request.SelectionOverride.LayerNames);
            Assert.Contains("张嘴", request.SelectionOverride.LayerNames);
            Assert.Contains("左手默认", request.SelectionOverride.LayerNames);
            Assert.Contains("右手默认", request.SelectionOverride.LayerNames);
            Assert.DoesNotContain("抬手", request.SelectionOverride.LayerNames);
            Assert.Contains("平眉", request.SelectionOverride.ControlledLayerNames ?? []);
            Assert.Contains("闭眼", request.SelectionOverride.ControlledLayerNames ?? []);
            Assert.Contains("闭嘴", request.SelectionOverride.ControlledLayerNames ?? []);
            Assert.Contains("伸手", request.SelectionOverride.ControlledLayerNames ?? []);
            Assert.Contains("握拳", request.SelectionOverride.ControlledLayerNames ?? []);
            Assert.DoesNotContain("抬手", request.SelectionOverride.ControlledLayerNames ?? []);
            Assert.DoesNotContain("默认手", request.SelectionOverride.ControlledLayerNames ?? []);
        });
        Assert.Contains(
            viewModel.WorkbenchDocument.RowsById.Values,
            row => string.Equals(RowVisualId(row), "asset_psd_generated", StringComparison.Ordinal));
        Assert.Contains(
            viewModel.SceneTables.SelectMany(scene => scene.Rows),
            row => string.Equals(row.SelectedCharacterAsset, "asset_psd_generated", StringComparison.Ordinal));
        Assert.Contains("已根据 AI 角色状态结果", viewModel.StatusText);
        Assert.All(fakeClient.BindingRequests, request =>
            Assert.Contains("角色状态选择", request.Text, StringComparison.Ordinal));
    }

    [Fact]
    public async Task MatchPsdExpressionLayersIncludesSourceRoleThoughtAndActionRows()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var fakeClient = new FakePsdLayerComponentClient();
        var fakeAi = new FakePsdLayerAiModelClient();
        var viewModel = new MainWindowViewModel(
            service,
            loadLastProject: false,
            psdLayerComponentClient: fakeClient,
            aiModelClientFactory: () => fakeAi);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-source-role-match-{Guid.NewGuid():N}");
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(projectRoot, "PSD Source Role Match");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        await manager.SaveDraftAsync(workspace, CreateSourceRolePsdProject());
        var psdPath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"source-role-character-{Guid.NewGuid():N}.psd"), [1, 2, 3]);

        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var target = viewModel.BindingTargets.Single(binding =>
            binding.Kind == BindingTargetKind.Character &&
            string.Equals(binding.TargetId, "char_chen", StringComparison.Ordinal));
        await viewModel.GeneratePsdImageBindingFromFileAsync(target, psdPath);
        target = viewModel.BindingTargets.Single(binding =>
            binding.Kind == BindingTargetKind.Character &&
            string.Equals(binding.TargetId, "char_chen", StringComparison.Ordinal));
        Assert.True(viewModel.ApplyPsdLayerMatchConfiguration(target, TestPsdLayerMatchConfiguration()));
        fakeClient.Reset();

        await viewModel.MatchPsdExpressionLayersCommand.ExecuteAsync(null);

        Assert.Equal(["int_thought", "int_action"], fakeClient.BindingRequests.Select(request => request.InteractionId).ToArray());
        Assert.All(fakeClient.BindingRequests, request =>
        {
            Assert.Equal(PsdComponentMode.Headless, request.Mode);
            Assert.Equal(target.TargetId, request.TargetId);
            Assert.NotNull(request.SelectionOverride);
        });
        Assert.Equal("asset_psd_generated", RowVisualId(viewModel.WorkbenchDocument.RowsById["int_thought"]));
        Assert.Equal("asset_psd_generated", RowVisualId(viewModel.WorkbenchDocument.RowsById["int_action"]));
    }

    [Fact]
    public async Task MatchPsdExpressionLayersLocksWorkbenchDataEditsUntilCompletion()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var fakeClient = new FakePsdLayerComponentClient();
        var fakeAi = new FakePsdLayerAiModelClient();
        var viewModel = new MainWindowViewModel(
            service,
            loadLastProject: false,
            psdLayerComponentClient: fakeClient,
            aiModelClientFactory: () => fakeAi);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-expression-lock-{Guid.NewGuid():N}");
        var psdPath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"character-{Guid.NewGuid():N}.psd"), [1, 2, 3]);
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        await viewModel.GeneratePsdImageBindingFromFileAsync(target, psdPath);
        target = viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Character && binding.TargetId == target.TargetId);
        Assert.True(viewModel.ApplyPsdLayerMatchConfiguration(target, TestPsdLayerMatchConfiguration()));
        var scene = viewModel.SceneTables[0];
        var row = viewModel.SceneTables
            .SelectMany(candidate => candidate.Rows)
            .First(candidate => candidate.SubjectCharacterId == target.TargetId || candidate.SubjectName == target.DisplayName);
        var originalSceneName = scene.SceneName;
        var originalText = row.LineText;
        fakeClient.Reset();
        fakeClient.HoldNextGeneration();

        var matchTask = viewModel.MatchPsdExpressionLayersCommand.ExecuteAsync(null);
        await fakeClient.WaitForGenerationStartedAsync();

        Assert.True(viewModel.IsPsdLayerAiMatchingRunning);
        Assert.True(viewModel.IsWorkbenchMutationLocked);
        Assert.False(viewModel.CanEditWorkbenchStructure);
        Assert.False(row.CanEditTextAndEmotion);
        scene.SceneName = "AI 分析中不应保留场景名";
        row.LineText = "AI 分析中不应保留台词";

        Assert.Equal(originalSceneName, scene.SceneName);
        Assert.Equal(originalText, row.LineText);
        Assert.Contains("AI 分析 PSD 图层", viewModel.StatusText, StringComparison.Ordinal);

        fakeClient.ReleaseHeldGeneration();
        await matchTask;

        Assert.False(viewModel.IsPsdLayerAiMatchingRunning);
        Assert.False(viewModel.IsWorkbenchMutationLocked);
        Assert.True(viewModel.CanEditWorkbenchStructure);
        Assert.True(row.CanEditTextAndEmotion);
    }

    private static int CountRowsInAiRequest(string userText)
    {
        var marker = "rows:";
        var markerIndex = userText.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return 0;
        }

        var rowsJson = userText[(markerIndex + marker.Length)..].Trim();
        using var document = JsonDocument.Parse(rowsJson);
        return document.RootElement.GetArrayLength();
    }

    [Fact]
    public async Task MatchPsdExpressionLayersDoesNotRenderWhenAiConnectionFails()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var fakeClient = new FakePsdLayerComponentClient();
        var fakeAi = new FakePsdLayerAiModelClient
        {
            ConnectionResult = new AiConnectionCheckResult(
                "Fake",
                "fake-model",
                "mock://ai",
                AiConnectionState.Unreachable,
                null,
                "fake AI unavailable")
        };
        var viewModel = new MainWindowViewModel(
            service,
            loadLastProject: false,
            psdLayerComponentClient: fakeClient,
            aiModelClientFactory: () => fakeAi);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-expression-cancel-{Guid.NewGuid():N}");
        var psdPath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"character-{Guid.NewGuid():N}.psd"), [1, 2, 3]);
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);

        await viewModel.GeneratePsdImageBindingFromFileAsync(target, psdPath);
        target = viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Character && binding.TargetId == target.TargetId);
        Assert.True(viewModel.ApplyPsdLayerMatchConfiguration(target, TestPsdLayerMatchConfiguration()));
        fakeClient.Reset();
        await viewModel.MatchPsdExpressionLayersCommand.ExecuteAsync(null);

        Assert.Equal(0, fakeClient.GenerationCallCount);
        Assert.Empty(fakeAi.JsonRequests);
        Assert.Contains("AI 服务未连接", viewModel.StatusText);
    }

    [Fact]
    public async Task MainWindowViewModelGeneratePsdImageBindingFromFileKeepsPsdSourceAndWritesCurrentRowRenderedPng()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var fakeClient = new FakePsdLayerComponentClient();
        var viewModel = new MainWindowViewModel(service, loadLastProject: false, psdLayerComponentClient: fakeClient);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-flow-{Guid.NewGuid():N}");
        var psdPath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"character-{Guid.NewGuid():N}.psd"), [1, 2, 3]);
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        var targetRows = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .Where(row => row.SubjectCharacterId == target.TargetId || row.SubjectName == target.DisplayName)
            .ToArray();
        Assert.NotEmpty(targetRows);
        var targetRow = targetRows[0];
        viewModel.SelectRowCommand.Execute(targetRow);

        await viewModel.GeneratePsdImageBindingFromFileAsync(target, psdPath);

        Assert.Null(fakeClient.LastBindingRequest);
        target = viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Character && binding.TargetId == target.TargetId);
        Assert.Equal(VisualSourceKind.Psd, target.VisualBinding.SourceKind);
        Assert.StartsWith("asset_", target.VisualBinding.SourceAssetId);
        var defaultBinding = Assert.Single(viewModel.WorkbenchDocument.CharacterBindings, binding => binding.CharacterId == target.TargetId);
        Assert.Equal(VisualSourceKind.Psd, defaultBinding.VisualBinding?.SourceKind);
        Assert.Equal(target.VisualBinding.SourceAssetId, defaultBinding.VisualBinding?.SourceAssetId);
        Assert.NotEqual("asset_psd_generated", RowVisualId(viewModel.WorkbenchDocument.RowsById[targetRow.InteractionId]));

        await viewModel.GenerateSelectedRowPsdImageFromCharacterSourceAsync(targetRow);

        Assert.NotNull(fakeClient.LastBindingRequest);
        Assert.Equal(PsdComponentMode.Interactive, fakeClient.LastBindingRequest.Mode);
        Assert.StartsWith("asset_", fakeClient.LastBindingRequest.PsdAssetId);
        Assert.Contains("assets\\images\\psd", fakeClient.LastBindingRequest.PsdPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(MediaAssetKind.Psd, fakeClient.LastManifestBeforeGeneration?.Assets.Single(asset => asset.Id == fakeClient.LastBindingRequest.PsdAssetId).Kind);
        Assert.Equal(fakeClient.LastBindingRequest.PsdAssetId, target.VisualBinding.SourceAssetId);
        Assert.NotEqual(fakeClient.ImportedAssetPath, target.ImagePreviewAsset);
        Assert.NotEqual(fakeClient.ImportedAssetPath, target.ImagePreviewSource);

        var targetRowDraft = viewModel.WorkbenchDocument.RowsById[targetRow.InteractionId];
        Assert.Equal("asset_psd_generated", RowVisualId(targetRowDraft));
        Assert.Equal(VisualSourceKind.Psd, targetRowDraft.CharacterVisualBinding?.SourceKind);
        Assert.Equal(fakeClient.LastBindingRequest.PsdAssetId, targetRowDraft.CharacterVisualBinding?.SourceAssetId);
        Assert.Equal("asset_psd_generated", targetRowDraft.CharacterVisualBinding?.RenderedAssetId);
        Assert.Equal(fakeClient.ImportedAssetPath, targetRow.SelectedCharacterPreviewAsset);
        Assert.Equal(fakeClient.ImportedAssetPath, targetRow.ObjectPreviewSource);
        Assert.True(targetRow.HasObjectPreviewImage);

        foreach (var otherRow in targetRows.Skip(1))
        {
            Assert.NotEqual("asset_psd_generated", RowVisualId(viewModel.WorkbenchDocument.RowsById[otherRow.InteractionId]));
            Assert.NotEqual(fakeClient.ImportedAssetPath, otherRow.SelectedCharacterPreviewAsset);
            Assert.NotEqual(fakeClient.ImportedAssetPath, otherRow.ObjectPreviewSource);
        }
    }

    [Fact]
    public async Task MainWindowViewModelRowPsdRenderKeepsSameCharacterRowsAndBindingThumbnailIndependent()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var fakeClient = new FakePsdLayerComponentClient();
        var viewModel = new MainWindowViewModel(service, loadLastProject: false, psdLayerComponentClient: fakeClient);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-same-character-{Guid.NewGuid():N}");
        var psdPath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"character-{Guid.NewGuid():N}.psd"), [1, 2, 3]);
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        var firstRow = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(row => row.SubjectCharacterId == target.TargetId || row.SubjectName == target.DisplayName);
        var secondRow = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(row => !row.IsNarration && !ReferenceEquals(row, firstRow));
        secondRow.SubjectName = target.DisplayName;

        await viewModel.GeneratePsdImageBindingFromFileAsync(target, psdPath);
        target = viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Character && binding.TargetId == target.TargetId);
        var bindingPreviewBeforeRender = target.ImagePreviewAsset;
        var secondRowAssetBeforeRender = secondRow.SelectedCharacterAsset;
        var secondRowPreviewBeforeRender = secondRow.SelectedCharacterPreviewAsset;
        viewModel.SelectRowCommand.Execute(firstRow);

        await viewModel.GenerateSelectedRowPsdImageFromCharacterSourceAsync(firstRow);

        var refreshedTarget = viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Character && binding.TargetId == target.TargetId);
        Assert.Equal(bindingPreviewBeforeRender, refreshedTarget.ImagePreviewAsset);
        Assert.NotEqual(fakeClient.ImportedAssetPath, refreshedTarget.ImagePreviewAsset);
        Assert.NotEqual(fakeClient.ImportedAssetPath, refreshedTarget.ImagePreviewSource);
        Assert.Equal("asset_psd_generated", RowVisualId(viewModel.WorkbenchDocument.RowsById[firstRow.InteractionId]));
        Assert.Equal(fakeClient.ImportedAssetPath, firstRow.SelectedCharacterPreviewAsset);
        Assert.Equal(fakeClient.ImportedAssetPath, firstRow.ObjectPreviewSource);
        Assert.Equal(secondRowAssetBeforeRender, secondRow.SelectedCharacterAsset);
        Assert.Equal(secondRowPreviewBeforeRender, secondRow.SelectedCharacterPreviewAsset);
        Assert.NotEqual(fakeClient.ImportedAssetPath, secondRow.ObjectPreviewSource);
        Assert.NotNull(refreshedTarget.RowVisualDetails.SingleOrDefault(detail => detail.InteractionId == firstRow.InteractionId));
        Assert.NotNull(refreshedTarget.RowVisualDetails.SingleOrDefault(detail => detail.InteractionId == secondRow.InteractionId));
    }

    [Fact]
    public async Task MainWindowViewModelPsdIntentUsesNearbyRoleActionForAutomaticLayerSelection()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var fakeClient = new FakePsdLayerComponentClient();
        var viewModel = new MainWindowViewModel(service, loadLastProject: false, psdLayerComponentClient: fakeClient);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-action-intent-{Guid.NewGuid():N}");
        var psdPath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"character-{Guid.NewGuid():N}.psd"), [1, 2, 3]);
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);

        await viewModel.GeneratePsdImageBindingFromFileAsync(target, psdPath);
        target = viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Character && binding.TargetId == target.TargetId);
        var scene = viewModel.SceneTables.First();
        var targetRow = scene.Rows
            .First(row => row.SubjectCharacterId == target.TargetId || row.SubjectName == target.DisplayName);
        var actionRow = AnalysisResultRowViewModel.CreateManualNarration(targetRow.Order + 1);
        actionRow.LineText = $"{target.DisplayName}抬手示意。";
        scene.Rows.Insert(scene.Rows.IndexOf(targetRow), actionRow);
        targetRow.LineText = "我知道了。";
        viewModel.SelectRowCommand.Execute(targetRow);
        fakeClient.Reset();

        await viewModel.GenerateSelectedRowPsdImageFromCharacterSourceAsync(targetRow);

        Assert.NotNull(fakeClient.LastBindingRequest);
        Assert.Contains(target.DisplayName, fakeClient.LastBindingRequest.Action, StringComparison.Ordinal);
        Assert.Contains("抬手", fakeClient.LastBindingRequest.Action, StringComparison.Ordinal);
        Assert.Contains("台词", fakeClient.LastBindingRequest.Action, StringComparison.Ordinal);
        Assert.Equal("我知道了。", fakeClient.LastBindingRequest.Text);
    }

    [Fact]
    public async Task MainWindowViewModelGeneratePsdImageBindingFromFileShowsOriginalPsdFileName()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var fakeClient = new FakePsdLayerComponentClient();
        var viewModel = new MainWindowViewModel(service, loadLastProject: false, psdLayerComponentClient: fakeClient);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-title-{Guid.NewGuid():N}");
        var psdPath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"角色绑定-{Guid.NewGuid():N}.psd"), [1, 2, 3]);
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        var targetId = target.TargetId;

        await viewModel.GeneratePsdImageBindingFromFileAsync(target, psdPath);

        target = viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Character && binding.TargetId == targetId);
        var expectedFileName = Path.GetFileName(psdPath);
        Assert.Equal(expectedFileName, target.BindingPreviewTitle);
        Assert.Equal(expectedFileName, target.VisualSourceText);
        Assert.Equal("未生成预览", target.VisualRenderedText);
        Assert.False(target.BindingPreviewTitle.StartsWith("asset_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MainWindowViewModelSetImageBindingRoutesCharacterPsdToPsdSourceBinding()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var fakeClient = new FakePsdLayerComponentClient();
        var viewModel = new MainWindowViewModel(service, loadLastProject: false, psdLayerComponentClient: fakeClient);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-route-{Guid.NewGuid():N}");
        var psdPath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"character-route-{Guid.NewGuid():N}.psd"), [1, 2, 3]);
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        var targetId = target.TargetId;

        await viewModel.SetImageBindingAsync(target, psdPath);

        target = viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Character && binding.TargetId == targetId);
        Assert.Equal(VisualSourceKind.Psd, target.VisualBinding.SourceKind);
        Assert.StartsWith("asset_", target.VisualBinding.SourceAssetId);
        Assert.Null(fakeClient.LastBindingRequest);
        Assert.Contains("已绑定 PSD", viewModel.StatusText);
    }

    [Fact]
    public async Task MainWindowViewModelBuildsPsdLayerPreviewRequestWithoutInteractiveGeneration()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var fakeClient = new FakePsdLayerComponentClient();
        var viewModel = new MainWindowViewModel(service, loadLastProject: false, psdLayerComponentClient: fakeClient);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-preview-request-{Guid.NewGuid():N}");
        var psdPath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"character-preview-{Guid.NewGuid():N}.psd"), [1, 2, 3]);
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        var targetId = target.TargetId;
        await viewModel.GeneratePsdImageBindingFromFileAsync(target, psdPath);
        target = viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Character && binding.TargetId == targetId);
        fakeClient.Reset();

        var request = viewModel.BuildPsdLayerPreviewRequest(target);

        Assert.NotNull(request);
        Assert.Equal(target.DisplayName + " PSD 图层预览", request.Title);
        Assert.EndsWith(".psd", request.PsdPath, StringComparison.OrdinalIgnoreCase);
        Assert.Null(fakeClient.LastBindingRequest);
    }

    [Fact]
    public async Task PsdLayerTemplateButtonEnablesFromCharacterPsdSourceInsteadOfRenderedPreview()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var fakeClient = new FakePsdLayerComponentClient();
        var viewModel = new MainWindowViewModel(service, loadLastProject: false, psdLayerComponentClient: fakeClient);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-template-button-{Guid.NewGuid():N}");
        var psdPath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"character-template-{Guid.NewGuid():N}.psd"), [1, 2, 3]);
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        var targetId = target.TargetId;

        await viewModel.GeneratePsdImageBindingFromFileAsync(target, psdPath);
        target = viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Character && binding.TargetId == targetId);
        var otherTarget = viewModel.BindingTargets.FirstOrDefault(binding =>
            binding.Kind == BindingTargetKind.Character &&
            !string.Equals(binding.TargetId, targetId, StringComparison.Ordinal));
        viewModel.SelectedCharacterBindingTarget = otherTarget;

        Assert.Same(target, viewModel.CharacterPsdLayerMatchTarget);
        Assert.True(viewModel.CanConfigureSelectedCharacterPsdLayerMatch);
        Assert.NotNull(viewModel.BuildPsdLayerPreviewRequest(target));
    }

    [Fact]
    public async Task SwitchingMultipleCharacterTargetsFromPngToPsdClearsInheritedPngRows()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var fakeClient = new FakePsdLayerComponentClient();
        var viewModel = new MainWindowViewModel(service, loadLastProject: false, psdLayerComponentClient: fakeClient);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-switch-many-{Guid.NewGuid():N}");
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var characterTargets = viewModel.BindingTargets
            .Where(binding => binding.Kind == BindingTargetKind.Character)
            .Take(3)
            .ToArray();
        Assert.True(characterTargets.Length >= 2);

        foreach (var originalTarget in characterTargets)
        {
            var targetId = originalTarget.TargetId;
            var target = viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Character && binding.TargetId == targetId);
            var pngPath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"character-{targetId}-{Guid.NewGuid():N}.png"), [137, 80, 78, 71]);
            target.ImageAsset = pngPath;
            viewModel.ApplyBindingTargetCommand.Execute(target);
            target = viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Character && binding.TargetId == targetId);
            var rows = CharacterRows(viewModel, target).ToArray();
            Assert.NotEmpty(rows);
            Assert.All(rows, row => Assert.Equal(pngPath, row.CharacterVisualBinding?.EffectiveAssetId));

            var psdPath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"character-{targetId}-{Guid.NewGuid():N}.psd"), [1, 2, 3]);
            await viewModel.GeneratePsdImageBindingFromFileAsync(target, psdPath);
            target = viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Character && binding.TargetId == targetId);
            viewModel.SelectedCharacterBindingTarget = target;

            Assert.Equal(VisualSourceKind.Psd, target.VisualBinding.SourceKind);
            Assert.True(viewModel.CanConfigureSelectedCharacterPsdLayerMatch);
            Assert.All(CharacterRows(viewModel, target), row =>
            {
                Assert.NotEqual(pngPath, row.CharacterVisualBinding?.EffectiveAssetId);
                Assert.True(row.CharacterVisualBinding is null ||
                    row.CharacterVisualBinding.SourceKind is VisualSourceKind.None or VisualSourceKind.Psd);
            });
        }
    }

    [Fact]
    public void PsdLayerMatchSettingsMigratesToDifferentPsdWithSameGroupNames()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var viewModel = new MainWindowViewModel(service, loadLastProject: false);
        var previous = new PsdLayerMatchConfiguration(
            ExpressionGroup: new PsdLayerGroupCatalog("表情组", ["旧开心"]),
            ActionGroup: new PsdLayerGroupCatalog("动作组", ["旧抬手"]),
            LeftHandGroup: new PsdLayerGroupCatalog("左手组", ["旧伸手"]),
            RightHandGroup: new PsdLayerGroupCatalog("右手组", ["旧握拳"]));
        var groups = new[]
        {
            new PsdLayerGroupOptionViewModel(1, "表情组", "角色/表情组", ["开心", "难过"]),
            new PsdLayerGroupOptionViewModel(2, "动作组", "角色/动作组", ["抬手", "垂手"]),
            new PsdLayerGroupOptionViewModel(3, "其他组", "角色/其他组", ["默认"]),
            new PsdLayerGroupOptionViewModel(4, "左手组", "角色/动作组/左手组", ["伸手"]),
            new PsdLayerGroupOptionViewModel(5, "右手组", "角色/动作组/右手组", ["握拳"])
        };

        var migrated = viewModel.TestMigratePsdLayerMatchConfiguration(previous, groups);

        Assert.NotNull(migrated);
        Assert.Equal("表情组", migrated.ExpressionGroup?.GroupName);
        Assert.Equal(["开心", "难过"], migrated.ExpressionGroup?.LayerNames);
        Assert.Equal("动作组", migrated.ActionGroup?.GroupName);
        Assert.Equal(["抬手", "垂手"], migrated.ActionGroup?.LayerNames);
        Assert.Equal("左手组", migrated.LeftHandGroup?.GroupName);
        Assert.Equal(["伸手"], migrated.LeftHandGroup?.LayerNames);
        Assert.Equal("右手组", migrated.RightHandGroup?.GroupName);
        Assert.Equal(["握拳"], migrated.RightHandGroup?.LayerNames);
    }

    [Fact]
    public void PsdAiRenderSelectionExpandsGroupedHandPoseLayers()
    {
        var configuration = new PsdLayerMatchConfiguration(
            LeftHandGroup: new PsdLayerGroupCatalog("左手", ["伸手", "默认"]),
            RightHandGroup: new PsdLayerGroupCatalog("右手", ["握拳", "默认"]));
        var layers = new[]
        {
            new PsdNativeLayerInfo(1, null, "左手", 0, true, true, 0, 0, 0, 0, 0, 0),
            new PsdNativeLayerInfo(2, 1, "伸手", 0, true, false, 0, 0, 0, 0, 0, 0),
            new PsdNativeLayerInfo(3, 2, "手掌", 0, false, true, 0, 0, 10, 10, 10, 10),
            new PsdNativeLayerInfo(4, 2, "袖口", 0, false, true, 0, 0, 10, 10, 10, 10),
            new PsdNativeLayerInfo(5, 1, "默认", 0, true, true, 0, 0, 0, 0, 0, 0),
            new PsdNativeLayerInfo(6, 5, "手掌", 0, false, true, 0, 0, 10, 10, 10, 10),
            new PsdNativeLayerInfo(7, 5, "袖口", 0, false, true, 0, 0, 10, 10, 10, 10),
            new PsdNativeLayerInfo(8, null, "右手", 0, true, true, 0, 0, 0, 0, 0, 0),
            new PsdNativeLayerInfo(9, 8, "握拳", 0, true, false, 0, 0, 0, 0, 0, 0),
            new PsdNativeLayerInfo(10, 9, "手掌", 0, false, true, 0, 0, 10, 10, 10, 10),
            new PsdNativeLayerInfo(11, 8, "默认", 0, true, true, 0, 0, 0, 0, 0, 0),
            new PsdNativeLayerInfo(12, 11, "手掌", 0, false, true, 0, 0, 10, 10, 10, 10)
        };
        var selection = new PsdLayerAiSelectionResult([], string.Empty, string.Empty, string.Empty, "伸手", string.Empty);

        var renderSelection = MainWindowViewModel.TestBuildAiPsdRenderSelection(selection, configuration, layers);

        Assert.NotNull(renderSelection);
        Assert.Equal(VisualExportBounds.LayerBounds, renderSelection.Bounds);
        Assert.Contains(3, renderSelection.LayerIds);
        Assert.Contains(4, renderSelection.LayerIds);
        Assert.DoesNotContain(6, renderSelection.LayerIds);
        Assert.DoesNotContain(7, renderSelection.LayerIds);
        Assert.Contains(12, renderSelection.LayerIds);
        Assert.Contains("手掌", renderSelection.LayerNames);
        Assert.Contains("袖口", renderSelection.LayerNames);
        Assert.DoesNotContain("伸手", renderSelection.LayerNames);
        Assert.Contains(6, renderSelection.ControlledLayerIds ?? []);
        Assert.Contains(10, renderSelection.ControlledLayerIds ?? []);
        Assert.Contains("手掌", renderSelection.ControlledLayerNames ?? []);
        Assert.DoesNotContain("默认", renderSelection.ControlledLayerNames ?? []);
    }

    [Fact]
    public void PsdAiRenderSelectionDoesNotControlHandSlotWhenTemplateGroupCannotResolveInCurrentPsd()
    {
        var configuration = new PsdLayerMatchConfiguration(
            BrowGroup: new PsdLayerGroupCatalog("眉毛", ["皱眉", "平眉"]),
            LeftHandGroup: new PsdLayerGroupCatalog("左手", ["伸手", "默认"]));
        var layers = new[]
        {
            new PsdNativeLayerInfo(1, null, "眉毛", 0, true, true, 0, 0, 0, 0, 0, 0),
            new PsdNativeLayerInfo(2, 1, "平眉", 0, false, true, 0, 0, 10, 10, 10, 10),
            new PsdNativeLayerInfo(3, 1, "皱眉", 0, false, false, 0, 0, 10, 10, 10, 10),
            new PsdNativeLayerInfo(10, null, "其它组", 0, true, true, 0, 0, 0, 0, 0, 0),
            new PsdNativeLayerInfo(11, 10, "伸手", 0, false, true, 0, 0, 10, 10, 10, 10)
        };
        var selection = new PsdLayerAiSelectionResult([], "皱眉", string.Empty, string.Empty, string.Empty, string.Empty);

        var renderSelection = MainWindowViewModel.TestBuildAiPsdRenderSelection(selection, configuration, layers);

        Assert.NotNull(renderSelection);
        Assert.Contains(3, renderSelection.LayerIds);
        Assert.Contains("皱眉", renderSelection.LayerNames);
        Assert.DoesNotContain(11, renderSelection.LayerIds);
        Assert.DoesNotContain("伸手", renderSelection.LayerNames);
        Assert.Contains(2, renderSelection.ControlledLayerIds ?? []);
        Assert.Contains(3, renderSelection.ControlledLayerIds ?? []);
        Assert.DoesNotContain(11, renderSelection.ControlledLayerIds ?? []);
        Assert.DoesNotContain("伸手", renderSelection.ControlledLayerNames ?? []);
    }

    [Fact]
    public void PsdAiRenderSelectionKeepsDefaultHandsForBundledRealPsd()
    {
        var psdPath = GetBundledRealPsdPath();
        var layers = new NativePsdDocumentReader().Load(psdPath).Layers;
        var frontBase = layers.Single(layer => layer.Name == "基础" && PathForLayer(layers, layer).Contains("/前手/", StringComparison.Ordinal));
        var backBase = layers.Single(layer => layer.Name == "基础" && PathForLayer(layers, layer).Contains("/后手/", StringComparison.Ordinal));
        var sectionDividerIds = layers
            .Where(layer => layer.Name == "</Layer group>")
            .Select(layer => layer.LayerId)
            .ToArray();
        var configuration = new PsdLayerMatchConfiguration(
            LeftHandGroup: new PsdLayerGroupCatalog("前手", ["基础", "插兜", "掐腰"]),
            RightHandGroup: new PsdLayerGroupCatalog("后手", ["基础", "招呼", "点破"]));
        var selection = new PsdLayerAiSelectionResult([], string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

        var renderSelection = MainWindowViewModel.TestBuildAiPsdRenderSelection(selection, configuration, layers);

        Assert.NotNull(renderSelection);
        Assert.Contains(frontBase.LayerId, renderSelection.LayerIds);
        Assert.Contains(backBase.LayerId, renderSelection.LayerIds);
        Assert.DoesNotContain(sectionDividerIds, id => renderSelection.LayerIds.Contains(id));
        Assert.DoesNotContain(sectionDividerIds, id => (renderSelection.ControlledLayerIds ?? []).Contains(id));
    }

    [Fact]
    public async Task PsdLayerMatchSettingsPropagatesToOtherPsdCharactersWithoutSettings()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var fakeClient = new FakePsdLayerComponentClient();
        var viewModel = new MainWindowViewModel(service, loadLastProject: false, psdLayerComponentClient: fakeClient);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-template-propagate-{Guid.NewGuid():N}");
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var characterTargets = viewModel.BindingTargets
            .Where(binding => binding.Kind == BindingTargetKind.Character)
            .Take(2)
            .ToArray();
        Assert.Equal(2, characterTargets.Length);

        foreach (var originalTarget in characterTargets)
        {
            var target = viewModel.BindingTargets.Single(binding =>
                binding.Kind == BindingTargetKind.Character &&
                string.Equals(binding.TargetId, originalTarget.TargetId, StringComparison.Ordinal));
            var psdPath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"character-{target.TargetId}-{Guid.NewGuid():N}.psd"), [1, 2, 3]);
            await viewModel.GeneratePsdImageBindingFromFileAsync(target, psdPath);
        }

        var sourceTarget = viewModel.BindingTargets.Single(binding =>
            binding.Kind == BindingTargetKind.Character &&
            string.Equals(binding.TargetId, characterTargets[0].TargetId, StringComparison.Ordinal));
        var relatedTargetId = characterTargets[1].TargetId;
        var configuration = new PsdLayerMatchConfiguration(
            LeftHandGroup: new PsdLayerGroupCatalog("左手组", ["旧伸手"]),
            RightHandGroup: new PsdLayerGroupCatalog("右手组", ["旧握拳"]));
        IReadOnlyList<PsdLayerGroupOptionViewModel> GroupOptions(string _) =>
        [
            new PsdLayerGroupOptionViewModel(1, "左手组", "角色/左手组", ["伸手", "垂手"]),
            new PsdLayerGroupOptionViewModel(2, "右手组", "角色/右手组", ["握拳", "摊手"])
        ];

        var propagated = viewModel.TestPropagatePsdLayerMatchConfigurationToRelatedPsdTargets(
            sourceTarget,
            configuration,
            GroupOptions);

        Assert.Equal(1, propagated);
        var relatedTarget = viewModel.BindingTargets.Single(binding =>
            binding.Kind == BindingTargetKind.Character &&
            string.Equals(binding.TargetId, relatedTargetId, StringComparison.Ordinal));
        var relatedConfiguration = viewModel.ResolvePsdLayerMatchConfiguration(relatedTarget);
        Assert.NotNull(relatedConfiguration);
        Assert.Equal("左手组", relatedConfiguration.LeftHandGroup?.GroupName);
        Assert.Equal(["伸手", "垂手"], relatedConfiguration.LeftHandGroup?.LayerNames);
        Assert.Equal("右手组", relatedConfiguration.RightHandGroup?.GroupName);
        Assert.Equal(["握拳", "摊手"], relatedConfiguration.RightHandGroup?.LayerNames);
    }

    [Fact]
    public async Task MainWindowViewModelSetImageBindingRejectsScenePsd()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var viewModel = new MainWindowViewModel(service, loadLastProject: false);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-scene-psd-reject-{Guid.NewGuid():N}");
        var psdPath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"scene-route-{Guid.NewGuid():N}.psd"), [1, 2, 3]);
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Scene);

        await viewModel.SetImageBindingAsync(target, psdPath);

        Assert.NotEqual(VisualSourceKind.Psd, target.VisualBinding.SourceKind);
        Assert.Equal("场景不支持 PSD", target.StatusText);
        Assert.Contains("PSD 仅支持人物绑定", viewModel.StatusText);
    }

    [Fact]
    public async Task MainWindowViewModelCharacterPsdSourceUsesDefaultMergedPreviewForWorkbenchThumbnail()
    {
        var samplePath = GetSamplePsdPath();
        Assert.True(File.Exists(samplePath), samplePath);
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var fakeClient = new FakePsdLayerComponentClient();
        var viewModel = new MainWindowViewModel(service, loadLastProject: false, psdLayerComponentClient: fakeClient);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-default-preview-{Guid.NewGuid():N}");
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        var targetId = target.TargetId;
        var row = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(candidate => candidate.SubjectCharacterId == target.TargetId || candidate.SubjectName == target.DisplayName);

        await viewModel.SetImageBindingAsync(target, samplePath);

        target = viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Character && binding.TargetId == targetId);
        row = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(candidate => candidate.SubjectCharacterId == target.TargetId || candidate.SubjectName == target.DisplayName);
        Assert.Equal(VisualSourceKind.Psd, target.VisualBinding.SourceKind);
        Assert.StartsWith("asset_", target.VisualBinding.SourceAssetId);
        Assert.StartsWith("asset_", target.VisualBinding.RenderedAssetId);
        Assert.True(File.Exists(target.ImagePreviewAsset), target.ImagePreviewAsset);
        Assert.True(File.Exists(row.SelectedCharacterPreviewAsset), row.SelectedCharacterPreviewAsset);
        Assert.Contains("assets\\images\\imported", row.SelectedCharacterPreviewAsset, StringComparison.OrdinalIgnoreCase);
        Assert.True(row.HasObjectPreviewImage);

        var manifest = await new WorkspaceProjectManager().LoadAsync(projectRoot);
        var sourceAsset = Assert.Single(manifest.Assets, asset => asset.Id == target.VisualBinding.SourceAssetId);
        var renderedAsset = Assert.Single(manifest.Assets, asset => asset.Id == target.VisualBinding.RenderedAssetId);
        Assert.Equal(MediaAssetKind.Psd, sourceAsset.Kind);
        Assert.Equal(MediaAssetKind.Image, renderedAsset.Kind);
        Assert.Equal(MediaAssetSource.ImportedPng, renderedAsset.Source);
        Assert.Equal("InternalPsdDefaultPreview", renderedAsset.Metadata["source"]);
        Assert.Equal(sourceAsset.Id, renderedAsset.Metadata["sourcePsdAssetId"]);

        var defaultBinding = Assert.Single(viewModel.WorkbenchDocument.CharacterBindings, binding => binding.CharacterId == target.TargetId);
        Assert.Equal(target.VisualBinding.SourceAssetId, defaultBinding.VisualBinding?.SourceAssetId);
        Assert.Equal(target.VisualBinding.RenderedAssetId, defaultBinding.VisualBinding?.RenderedAssetId);
    }

    [Fact]
    public async Task MainWindowViewModelPsdRenderKeepsOriginalRowWhenSelectionChangesBeforeLoaderCompletes()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var fakeClient = new FakePsdLayerComponentClient();
        var viewModel = new MainWindowViewModel(service, loadLastProject: false, psdLayerComponentClient: fakeClient);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-row-lock-{Guid.NewGuid():N}");
        var psdPath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"character-{Guid.NewGuid():N}.psd"), [1, 2, 3]);
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        var firstRow = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(row => row.SubjectCharacterId == target.TargetId || row.SubjectName == target.DisplayName);
        var secondRow = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(row => !row.IsNarration && !ReferenceEquals(row, firstRow));
        secondRow.SubjectName = target.DisplayName;
        await viewModel.GeneratePsdImageBindingFromFileAsync(target, psdPath);
        target = viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Character && binding.TargetId == target.TargetId);
        viewModel.SelectRowCommand.Execute(firstRow);
        fakeClient.HoldNextGeneration();

        var renderTask = viewModel.GenerateSelectedRowPsdImageFromCharacterSourceAsync(firstRow);
        await fakeClient.WaitForGenerationStartedAsync();
        viewModel.SelectRowCommand.Execute(secondRow);
        fakeClient.ReleaseHeldGeneration();
        await renderTask;

        Assert.NotNull(fakeClient.LastBindingRequest);
        Assert.Equal(firstRow.InteractionId, fakeClient.LastBindingRequest.InteractionId);
        Assert.Equal("asset_psd_generated", RowVisualId(viewModel.WorkbenchDocument.RowsById[firstRow.InteractionId]));
        Assert.NotEqual("asset_psd_generated", RowVisualId(viewModel.WorkbenchDocument.RowsById[secondRow.InteractionId]));
        Assert.Same(secondRow, viewModel.SelectedRow);
    }

    [Fact]
    public async Task MainWindowViewModelPsdRenderDoesNotWriteRowAfterRowCharacterChangesBeforeLoaderCompletes()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var fakeClient = new FakePsdLayerComponentClient();
        var viewModel = new MainWindowViewModel(service, loadLastProject: false, psdLayerComponentClient: fakeClient);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-row-change-{Guid.NewGuid():N}");
        var psdPath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"character-{Guid.NewGuid():N}.psd"), [1, 2, 3]);
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        var otherTarget = viewModel.BindingTargets.First(binding =>
            binding.Kind == BindingTargetKind.Character &&
            !string.Equals(binding.TargetId, target.TargetId, StringComparison.Ordinal));
        var row = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(candidate => candidate.SubjectCharacterId == target.TargetId || candidate.SubjectName == target.DisplayName);
        await viewModel.GeneratePsdImageBindingFromFileAsync(target, psdPath);
        target = viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Character && binding.TargetId == target.TargetId);
        viewModel.SelectRowCommand.Execute(row);
        fakeClient.HoldNextGeneration();

        var renderTask = viewModel.GenerateSelectedRowPsdImageFromCharacterSourceAsync(row);
        await fakeClient.WaitForGenerationStartedAsync();
        row.SubjectName = otherTarget.DisplayName;
        fakeClient.ReleaseHeldGeneration();
        await renderTask;

        Assert.NotNull(fakeClient.LastBindingRequest);
        Assert.Equal(row.InteractionId, fakeClient.LastBindingRequest.InteractionId);
        Assert.NotEqual("asset_psd_generated", RowVisualId(viewModel.WorkbenchDocument.RowsById[row.InteractionId]));
        Assert.Contains("目标行已变化", viewModel.StatusText);
    }

    [Fact]
    public async Task MainWindowViewModelPsdRenderDoesNotFallbackWhenExplicitInteractionIdDoesNotMatchCharacter()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var fakeClient = new FakePsdLayerComponentClient();
        var viewModel = new MainWindowViewModel(service, loadLastProject: false, psdLayerComponentClient: fakeClient);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-explicit-row-{Guid.NewGuid():N}");
        var psdPath = await CreateFileAsync(Path.Combine(projectRoot, "character.psd"), [1, 2, 3]);
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        var unrelatedRow = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(row =>
                !row.IsNarration &&
                !string.Equals(row.SubjectCharacterId, target.TargetId, StringComparison.Ordinal) &&
                !string.Equals(row.SubjectName, target.DisplayName, StringComparison.Ordinal));
        viewModel.SelectRowCommand.Execute(unrelatedRow);

        var applied = await viewModel.GeneratePsdImageBindingAsync(
            target,
            "asset_psd_character",
            psdPath,
            "Calm",
            "Idle",
            unrelatedRow.OwnerScene?.SceneId ?? viewModel.SceneTables[0].SceneId,
            unrelatedRow.InteractionId,
            unrelatedRow.LineText);

        Assert.False(applied);
        Assert.Null(fakeClient.LastBindingRequest);
        Assert.DoesNotContain(viewModel.WorkbenchDocument.RowsById.Values, row => RowVisualId(row) == "asset_psd_generated");
        Assert.Contains("没有找到 PSD 目标行", viewModel.StatusText);
    }

    [Fact]
    public async Task MainWindowViewModelGeneratePsdImageBindingFromFileDoesNotSupportScenePsd()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var fakeClient = new FakePsdLayerComponentClient();
        var viewModel = new MainWindowViewModel(service, loadLastProject: false, psdLayerComponentClient: fakeClient);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-scene-psd-flow-{Guid.NewGuid():N}");
        var psdPath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"scene-{Guid.NewGuid():N}.psd"), [1, 2, 3]);
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Scene);

        Assert.False(target.SupportsPsd);
        await viewModel.GeneratePsdImageBindingFromFileAsync(target, psdPath);

        Assert.Null(fakeClient.LastBindingRequest);

        var sceneDraft = viewModel.WorkbenchDocument.Scenes.Single(scene => scene.SceneId == target.TargetId);
        Assert.NotEqual(VisualSourceKind.Psd, sceneDraft.VisualBinding?.SourceKind);
    }

    [Fact]
    public async Task MainWindowViewModelImageAndCharacterPsdBindingAreLockedWhileTtsRuns()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var fakeClient = new FakePsdLayerComponentClient();
        var viewModel = new MainWindowViewModel(service, loadLastProject: false, psdLayerComponentClient: fakeClient);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-binding-locked-{Guid.NewGuid():N}");
        var imagePath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"scene-{Guid.NewGuid():N}.png"), [137, 80, 78, 71]);
        var psdPath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"character-{Guid.NewGuid():N}.psd"), [1, 2, 3]);
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var sceneTarget = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Scene);
        var characterTarget = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        var originalSceneAsset = sceneTarget.ImageAsset;
        var originalCharacterBinding = characterTarget.VisualBinding;

        viewModel.IsTtsSynthesisRunning = true;
        await viewModel.SetImageBindingAsync(sceneTarget, imagePath);
        await viewModel.GeneratePsdImageBindingFromFileAsync(characterTarget, psdPath);

        Assert.Null(fakeClient.LastBindingRequest);
        Assert.Equal(originalSceneAsset, sceneTarget.ImageAsset);
        Assert.Equal(originalCharacterBinding, characterTarget.VisualBinding);
        Assert.Contains("绑定已锁定", viewModel.StatusText);
    }

    [Fact]
    public async Task MainWindowViewModelSelectedRowPsdReselectionStaysAvailableWhileTtsRuns()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var fakeClient = new FakePsdLayerComponentClient();
        var viewModel = new MainWindowViewModel(service, loadLastProject: false, psdLayerComponentClient: fakeClient);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-row-psd-tts-{Guid.NewGuid():N}");
        var psdPath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"character-{Guid.NewGuid():N}.psd"), [1, 2, 3]);
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        var row = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(candidate => candidate.SubjectCharacterId == target.TargetId || candidate.SubjectName == target.DisplayName);
        await viewModel.GeneratePsdImageBindingFromFileAsync(target, psdPath);
        fakeClient.Reset();

        viewModel.IsTtsSynthesisRunning = true;
        var applied = await viewModel.GenerateSelectedRowPsdImageFromCharacterSourceAsync(row);

        Assert.True(applied);
        Assert.NotNull(fakeClient.LastBindingRequest);
        Assert.Equal(PsdComponentMode.Interactive, fakeClient.LastBindingRequest.Mode);
        Assert.Equal(row.InteractionId, fakeClient.LastBindingRequest.InteractionId);
    }

    [Fact]
    public async Task MainWindowViewModelSelectedRowPsdReselectionIsLockedWhilePsdAiMatchingRuns()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var fakeClient = new FakePsdLayerComponentClient();
        var viewModel = new MainWindowViewModel(service, loadLastProject: false, psdLayerComponentClient: fakeClient);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-row-psd-ai-locked-{Guid.NewGuid():N}");
        var psdPath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"character-{Guid.NewGuid():N}.psd"), [1, 2, 3]);
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        var row = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(candidate => candidate.SubjectCharacterId == target.TargetId || candidate.SubjectName == target.DisplayName);
        await viewModel.GeneratePsdImageBindingFromFileAsync(target, psdPath);
        fakeClient.Reset();

        viewModel.IsPsdLayerAiMatchingRunning = true;
        var applied = await viewModel.GenerateSelectedRowPsdImageFromCharacterSourceAsync(row);

        Assert.False(applied);
        Assert.Null(fakeClient.LastBindingRequest);
        Assert.Contains("锁定行缩略图 PSD 图层重选", viewModel.StatusText);
    }


    [Fact]
    public async Task RepairExportPreflightIssueUsesCharacterManagementPsdSourceForCharacterTarget()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var fakeClient = new FakePsdLayerComponentClient();
        var resolver = new CountingProjectAssetResolver();
        var viewModel = new MainWindowViewModel(
            service,
            loadLastProject: false,
            psdLayerComponentClient: fakeClient,
            projectAssetResolver: resolver);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-character-psd-repair-{Guid.NewGuid():N}");
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        var psdPath = await CreateFileAsync(Path.Combine(Path.GetTempPath(), $"character-source-{Guid.NewGuid():N}.psd"), [1, 2, 3]);
        await viewModel.GeneratePsdImageBindingFromFileAsync(target, psdPath);
        target = viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Character && binding.TargetId == target.TargetId);
        var sourceAssetId = target.VisualBinding.SourceAssetId;
        Assert.False(string.IsNullOrWhiteSpace(sourceAssetId));
        fakeClient.Reset();
        var row = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(candidate => candidate.SubjectCharacterId == target.TargetId || candidate.SubjectName == target.DisplayName);
        var issue = new ExportPreflightIssueViewModel(
            1,
            EditingTimelinePreflightIssueKind.PsdReference,
            "Character PSD source still needs rendered PNG",
            row.OwnerScene?.SceneId ?? viewModel.SceneTables[0].SceneId,
            row.InteractionId,
            sourceAssetId!,
            string.Empty,
            null,
            "Render character PSD");

        await viewModel.RepairExportPreflightIssueCommand.ExecuteAsync(issue);

        Assert.NotNull(fakeClient.LastBindingRequest);
        Assert.Equal(BindingTargetKind.Character, fakeClient.LastBindingRequest.TargetKind);
        Assert.Equal(sourceAssetId, fakeClient.LastBindingRequest.PsdAssetId);
        Assert.True(resolver.CallsByReference.GetValueOrDefault(sourceAssetId!) > 0);
        Assert.Contains("assets\\images\\psd", fakeClient.LastBindingRequest.PsdPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(row.InteractionId, fakeClient.LastBindingRequest.InteractionId);
    }

    [Fact]
    public async Task RepairExportPreflightIssueDoesNotExposeMissingPsdAssetIdInStatus()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var fakeClient = new FakePsdLayerComponentClient();
        var viewModel = new MainWindowViewModel(
            service,
            loadLastProject: false,
            psdLayerComponentClient: fakeClient);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-character-missing-psd-repair-{Guid.NewGuid():N}");
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        var row = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(candidate => candidate.SubjectCharacterId == target.TargetId || candidate.SubjectName == target.DisplayName);
        var issue = new ExportPreflightIssueViewModel(
            1,
            EditingTimelinePreflightIssueKind.PsdReference,
            "Character PSD source still needs rendered PNG",
            row.OwnerScene?.SceneId ?? viewModel.SceneTables[0].SceneId,
            row.InteractionId,
            "asset_missing_secret_psd",
            string.Empty,
            null,
            "Render character PSD");

        await viewModel.RepairExportPreflightIssueCommand.ExecuteAsync(issue);

        Assert.Null(fakeClient.LastBindingRequest);
        Assert.Contains("行级 PSD 源与角色管理 PSD 不一致", viewModel.StatusText);
        Assert.Contains("重新绑定", viewModel.StatusText);
        Assert.DoesNotContain("asset_missing_secret_psd", viewModel.StatusText, StringComparison.Ordinal);
    }

    private static async Task<string> CreateFileAsync(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes);
        return path;
    }

    private static string GetSamplePsdPath()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "PsdLayerLoader.Native",
            "third_party",
            "psd_sdk",
            "bin",
            "Sample.psd"));
    }

    private static async Task<PsdLayerComponentBindingRequest> CreateBindingRequestAsync(string root)
    {
        var psdPath = await CreateFileAsync(Path.Combine(root, "character.psd"), [1, 2, 3]);
        return new PsdLayerComponentBindingRequest(
            BindingTargetKind.Character,
            "char_chen",
            "Chen",
            "char_chen",
            "Chen",
            "Lead",
            "asset_psd_chen",
            psdPath,
            "Calm",
            "UpperLeft",
            "line text",
            "scene_001",
            "interaction_001");
    }

    private static async Task<string> WriteOutputPngAsync(PsdLayerRequest request, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(request.Output.Directory);
        var pngPath = Path.Combine(request.Output.Directory, request.Output.FileNameHint);
        await File.WriteAllBytesAsync(pngPath, [137, 80, 78, 71, 13, 10, 26, 10], cancellationToken);
        return pngPath;
    }

    private static InteractionProject CreateSourceRolePsdProject()
    {
        var character = new Character("char_chen", "陈默", "主角", string.Empty, string.Empty, "Calm", new AssetBinding());
        var thought = SourceRoleInteraction("int_thought", 1, "thought", "陈默心里一沉。", character);
        var action = SourceRoleInteraction("int_action", 2, "action", "陈默抬手示意别出声。", character);
        var scene = new InteractionScene(
            "scene_001",
            1,
            "雨夜街口",
            "街口",
            "夜",
            "雨",
            "压抑",
            "雨声压抑",
            new SceneAssetBinding(),
            [thought, action]);

        return new InteractionProject(
            "project_psd_source_role",
            "PSD Source Role",
            new ScriptDocument(
                "陈默心里一沉。陈默抬手示意别出声。",
                "陈默心里一沉。陈默抬手示意别出声。",
                []),
            [character],
            [scene],
            [],
            []);
    }

    private static CharacterInteraction SourceRoleInteraction(
        string id,
        int order,
        string sourceItemType,
        string text,
        Character character)
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
            SourceRole = character.Name,
            SourceRoleCharacterId = character.Id
        };
    }

    private sealed class CountingProjectAssetResolver : IProjectAssetResolver
    {
        private readonly ProjectAssetResolver _inner = new();

        public Dictionary<string, int> CallsByReference { get; } = new(StringComparer.Ordinal);

        public string? ResolvePath(string? reference, WorkspaceProject workspace, WorkspaceProjectManifest? manifest = null)
        {
            Count(reference);
            return _inner.ResolvePath(reference, workspace, manifest);
        }

        public ProjectAssetResolution? Resolve(string? reference, WorkspaceProject workspace, WorkspaceProjectManifest? manifest = null)
        {
            Count(reference);
            return _inner.Resolve(reference, workspace, manifest);
        }

        private void Count(string? reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                return;
            }

            var key = reference.Trim();
            CallsByReference[key] = CallsByReference.GetValueOrDefault(key) + 1;
        }
    }

    private static PsdLayerMatchConfiguration TestPsdLayerMatchConfiguration()
    {
        return new PsdLayerMatchConfiguration(
            BrowGroup: new PsdLayerGroupCatalog("眉毛", ["皱眉", "平眉"]),
            EyeGroup: new PsdLayerGroupCatalog("眼睛", ["闭眼", "睁眼"]),
            MouthGroup: new PsdLayerGroupCatalog("嘴巴", ["张嘴", "闭嘴"]),
            ActionGroup: new PsdLayerGroupCatalog("动作", ["抬手", "默认手"]),
            LeftHandGroup: new PsdLayerGroupCatalog("左手", ["左手默认", "伸手"]),
            RightHandGroup: new PsdLayerGroupCatalog("右手", ["右手默认", "握拳"]));
    }

    private sealed class FakePsdLayerAiModelClient : IAiModelClient
    {
        public List<AiModelJsonRequest> JsonRequests { get; } = [];

        public int ConnectionCheckCount { get; private set; }

        public AiConnectionCheckResult ConnectionResult { get; init; } = new(
            "Fake",
            "fake-model",
            "mock://ai",
            AiConnectionState.Connected,
            200,
            "fake AI connected");

        public string? JsonResponse { get; init; }

        public Task<AiModelJsonResponse> GenerateJsonAsync(
            AiModelJsonRequest request,
            CancellationToken cancellationToken = default)
        {
            JsonRequests.Add(request);
            var json = JsonResponse ?? BuildRowsJsonResponse(request.UserText);
            return Task.FromResult(new AiModelJsonResponse(
                json,
                json,
                new AiModelReplyMetadata("Fake", "fake-model", "stop", AiReplyState.Completed, "ok")));
        }

        private static string BuildRowsJsonResponse(string userText)
        {
            var marker = "rows:";
            var markerIndex = userText.IndexOf(marker, StringComparison.Ordinal);
            var rowsJson = markerIndex >= 0
                ? userText[(markerIndex + marker.Length)..].Trim()
                : "[]";
            using var document = JsonDocument.Parse(rowsJson);
            var rows = document.RootElement.EnumerateArray()
                .Select(row => new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["interactionId"] = row.GetProperty("interactionId").GetString() ?? string.Empty,
                    ["brow"] = "皱眉",
                    ["eye"] = "闭眼",
                    ["mouth"] = "张嘴",
                    ["leftHand"] = string.Empty,
                    ["rightHand"] = string.Empty
                })
                .ToArray();
            return JsonSerializer.Serialize(new Dictionary<string, object> { ["rows"] = rows });
        }

        public Task<AiModelTextResponse> GenerateTextAsync(
            AiModelTextRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<AiConnectionCheckResult> CheckConnectionAsync(CancellationToken cancellationToken = default)
        {
            ConnectionCheckCount++;
            return Task.FromResult(ConnectionResult);
        }
    }

    private sealed class FakePsdComponentRunner : IPsdLayerComponentRunner
    {
        public PsdLayerRequest? LastRequest { get; private set; }

        public Func<PsdLayerRequest, Task<PsdLayerResponse>>? BuildResponseAsync { get; init; }

        public Func<PsdLayerRequest, CancellationToken, Task<PsdLayerResponse>>? BuildResponseWithCancellationAsync { get; init; }

        public async Task<PsdLayerResponse> RunAsync(
            PsdLayerRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return BuildResponseWithCancellationAsync is not null
                ? await BuildResponseWithCancellationAsync(request, cancellationToken)
                : BuildResponseAsync is not null
                    ? await BuildResponseAsync(request)
                    : await BuildDefaultResponseAsync(request, cancellationToken);
        }

        private static async Task<PsdLayerResponse> BuildDefaultResponseAsync(
            PsdLayerRequest request,
            CancellationToken cancellationToken)
        {
            var pngPath = await WriteOutputPngAsync(request, cancellationToken);
            return PsdLayerResponse.Completed(
                request.RequestId,
                new PsdPngResult(pngPath, 1024, 1024, request.Output.Bounds),
                new PsdLayerMatchResult(0.82, request.Intent.Emotion, request.Intent.Action, null, [12, 18], ["Base", "UpperLeft"]),
                new PsdResponseMetadata(request.Psd.AssetId, request.Psd.Path));
        }
    }

    private sealed class FakePsdLayerComponentClient : IPsdLayerComponentClient
    {
        private TaskCompletionSource? _generationStarted;
        private TaskCompletionSource? _releaseGeneration;
        private TaskCompletionSource? _heldReleaseGeneration;
        private bool _holdOnce;
        private bool _cancelHeldWhenNextStarts;
        private bool _cancelCurrentAfterRelease;

        public PsdLayerComponentBindingRequest? LastBindingRequest { get; private set; }

        public List<PsdLayerComponentBindingRequest> BindingRequests { get; } = [];

        public int GenerationCallCount { get; private set; }

        public WorkspaceProjectManifest? LastManifestBeforeGeneration { get; private set; }

        public string ImportedAssetPath { get; private set; } = string.Empty;

        public bool CancelNextGeneration { get; set; }

        public Func<Task>? BeforeReturnResultAsync { get; set; }

        public void HoldNextGeneration(bool holdOnce = false, bool cancelHeldWhenNextStarts = false)
        {
            _generationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _releaseGeneration = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _holdOnce = holdOnce;
            _cancelHeldWhenNextStarts = cancelHeldWhenNextStarts;
        }

        public Task WaitForGenerationStartedAsync()
        {
            return _generationStarted?.Task ?? Task.CompletedTask;
        }

        public void ReleaseHeldGeneration()
        {
            _releaseGeneration?.TrySetResult();
            _heldReleaseGeneration?.TrySetResult();
        }

        public void Reset()
        {
            LastBindingRequest = null;
            BindingRequests.Clear();
            GenerationCallCount = 0;
            LastManifestBeforeGeneration = null;
            ImportedAssetPath = string.Empty;
            ReleaseHeldGeneration();
            _generationStarted = null;
            _releaseGeneration = null;
            _heldReleaseGeneration = null;
            _holdOnce = false;
            _cancelHeldWhenNextStarts = false;
            _cancelCurrentAfterRelease = false;
            CancelNextGeneration = false;
            BeforeReturnResultAsync = null;
        }

        public void ResetHistoryOnly()
        {
            LastBindingRequest = null;
            BindingRequests.Clear();
            GenerationCallCount = 0;
            LastManifestBeforeGeneration = null;
            ImportedAssetPath = string.Empty;
        }

        public async Task<PsdLayerComponentBindingResult> GenerateAndImportAsync(
            WorkspaceProject workspace,
            WorkspaceProjectManifest manifest,
            PsdLayerComponentBindingRequest bindingRequest,
            PsdLayerComponentOptions options,
            CancellationToken cancellationToken = default)
        {
            GenerationCallCount++;
            if (_heldReleaseGeneration is not null && _cancelHeldWhenNextStarts)
            {
                _cancelCurrentAfterRelease = true;
                _heldReleaseGeneration.TrySetResult();
                _heldReleaseGeneration = null;
            }

            LastBindingRequest = bindingRequest;
            BindingRequests.Add(bindingRequest);
            LastManifestBeforeGeneration = manifest;
            _generationStarted?.TrySetResult();
            if (CancelNextGeneration)
            {
                CancelNextGeneration = false;
                throw new PsdLayerSelectionCancelledException("fake-cancelled");
            }

            if (_releaseGeneration is not null)
            {
                var releaseGeneration = _releaseGeneration;
                if (_holdOnce)
                {
                    _heldReleaseGeneration = releaseGeneration;
                    _generationStarted = null;
                    _releaseGeneration = null;
                    _holdOnce = false;
                }

                await releaseGeneration.Task.WaitAsync(cancellationToken);
                if (_cancelCurrentAfterRelease)
                {
                    _cancelCurrentAfterRelease = false;
                    throw new PsdLayerSelectionCancelledException("fake-held");
                }
            }

            ImportedAssetPath = Path.Combine(workspace.Folders.ImagesImported, $"psd-generated-{Guid.NewGuid():N}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(ImportedAssetPath)!);
            File.WriteAllBytes(ImportedAssetPath, [137, 80, 78, 71, 13, 10, 26, 10]);
            var now = DateTimeOffset.UtcNow;
            var asset = new ProjectMediaAsset(
                "asset_psd_generated",
                MediaAssetKind.Image,
                MediaAssetSource.ImportedPng,
                "psd-generated.png",
                ImportedAssetPath,
                Path.GetRelativePath(workspace.RootPath, ImportedAssetPath).Replace('\\', '/'),
                bindingRequest.TargetId,
                ImportedAssetPath,
                null,
                new Dictionary<string, string> { ["source"] = "PsdLayerLoader" },
                now);
            var updatedManifest = manifest with
            {
                Assets = manifest.Assets
                    .Where(candidate => !string.Equals(candidate.Id, asset.Id, StringComparison.Ordinal))
                    .Append(asset)
                    .ToArray(),
                UpdatedAt = now
            };
            var response = PsdLayerResponse.Completed(
                "req_vm",
                new PsdPngResult(ImportedAssetPath, 1024, 1024, PsdExportBounds.CanvasBounds),
                new PsdLayerMatchResult(0.82, bindingRequest.Emotion, bindingRequest.Action, null, [12], ["UpperLeft"]),
                new PsdResponseMetadata(bindingRequest.PsdAssetId, bindingRequest.PsdPath));
            if (BeforeReturnResultAsync is not null)
            {
                await BeforeReturnResultAsync();
            }

            return new PsdLayerComponentBindingResult(response, asset, updatedManifest);
        }
    }

    private static IEnumerable<AnalysisResultRowViewModel> CharacterRows(
        MainWindowViewModel viewModel,
        BindingTargetViewModel target)
    {
        return viewModel.SceneTables.SelectMany(scene => scene.Rows).Where(row =>
            string.Equals(row.SubjectCharacterId, target.TargetId, StringComparison.Ordinal) ||
            string.Equals(row.SourceRoleCharacterId, target.TargetId, StringComparison.Ordinal) ||
            string.Equals(row.SubjectName, target.DisplayName, StringComparison.Ordinal) ||
            string.Equals(row.SourceRole, target.DisplayName, StringComparison.Ordinal));
    }

    private static string? RowVisualId(WorkbenchRowDraft row)
    {
        return row.CharacterVisualBinding?.EffectiveAssetId;
    }

    private static string GetBundledRealPsdPath()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "白贝利亚.psd"));
    }

    private static string PathForLayer(
        IReadOnlyList<PsdNativeLayerInfo> layers,
        PsdNativeLayerInfo layer)
    {
        var byId = layers
            .GroupBy(candidate => candidate.LayerId)
            .ToDictionary(group => group.Key, group => group.First());
        var names = new List<string>();
        var current = layer;
        while (true)
        {
            names.Add(current.Name);
            if (current.ParentLayerId is not { } parentId ||
                !byId.TryGetValue(parentId, out var parent))
            {
                break;
            }

            current = parent;
        }

        names.Reverse();
        return string.Join("/", names);
    }
}


