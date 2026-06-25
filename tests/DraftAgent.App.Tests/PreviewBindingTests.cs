using DraftAgent.App;
using DraftAgent.App.ViewModels;
using DraftAgent.Core;

using Avalonia.Media;
using System.Text.Json;

namespace DraftAgent.App.Tests;

public sealed class PreviewBindingTests
{
    [Fact]
    public async Task OptimizeScriptNarrationCommandWritesAdaptationTab()
    {
        var client = new CapturingTextLlmClient("优化后的正文");
        var store = new InMemoryInteractionProjectStore();
        var service = new DraftWorkspaceService(client, new MockComfyUiClient(), store);
        var viewModel = new MainWindowViewModel(service, loadLastProject: false)
        {
            ScriptText = "原始正文"
        };

        await viewModel.OptimizeScriptNarrationCommand.ExecuteAsync(null);

        Assert.Equal("原始正文", viewModel.ScriptDrafts[0].Text);
        Assert.Equal("优化后的正文", viewModel.ScriptText);
        Assert.Same(viewModel.ScriptDrafts[1], viewModel.SelectedScriptDraft);
        Assert.True(viewModel.HasMultipleScriptDrafts);
        Assert.Contains("核心剧情", client.LastTextRequest!.SystemPrompt);
        Assert.Contains("第 1 句写最大矛盾、危险、身份落差或反常处境", client.LastTextRequest.SystemPrompt);
        Assert.Contains("每句控制在 8 到 24 个中文字符", client.LastTextRequest.SystemPrompt);
        Assert.Contains("同一人物连续动作或心理时，后续连续句可以省略姓名", client.LastTextRequest.SystemPrompt);
        Assert.Contains("只输出改写后的正文", client.LastTextRequest.SystemPrompt);
        Assert.DoesNotContain("轻微吐槽感", client.LastTextRequest.SystemPrompt);
        Assert.DoesNotContain("爽点或反转", client.LastTextRequest.SystemPrompt);
        Assert.DoesNotContain("不要", client.LastTextRequest.SystemPrompt);
        Assert.Equal("原始正文", client.LastTextRequest.UserText);
        Assert.Contains("解说优化完成", viewModel.StatusText);

        var saved = await store.LoadLastAsync();
        Assert.NotNull(saved);
        Assert.Equal(["原文", "改编1"], saved.ScriptDrafts.Select(draft => draft.Title));
        Assert.Equal("原始正文", saved.ScriptDrafts[0].Text);
        Assert.Equal("优化后的正文", saved.ScriptDrafts[1].Text);
        Assert.Equal(1, saved.SelectedScriptDraftIndex);
    }

    [Fact]
    public async Task OptimizeScriptNarrationDoesNotRefreshDirtyWorkbench()
    {
        var client = new CapturingTextLlmClient("优化后的正文");
        var service = new DraftWorkspaceService(client, new MockComfyUiClient(), new InMemoryInteractionProjectStore());
        var viewModel = new MainWindowViewModel(service, loadLastProject: false)
        {
            ScriptText = "原始正文"
        };
        var row = viewModel.SelectedRow!;
        row.LineText = "未提交的工作台编辑";

        await viewModel.OptimizeScriptNarrationCommand.ExecuteAsync(null);

        Assert.Equal("未提交的工作台编辑", row.LineText);
        Assert.True(viewModel.IsWorkbenchDirty);
        Assert.Equal("优化后的正文", viewModel.ScriptText);
    }

    [Fact]
    public async Task LoadLastProjectRestoresScriptDraftTabsAndSelection()
    {
        var store = new InMemoryInteractionProjectStore();
        var savedProject = new SceneInteractionBuilder().Build("原始正文") with
        {
            ScriptDrafts =
            [
                new ProjectScriptDraft("原文", "原始正文", false),
                new ProjectScriptDraft("改编1", "优化后的正文", true)
            ],
            SelectedScriptDraftIndex = 1
        };
        await store.SaveAsync(savedProject);
        var service = new DraftWorkspaceService(new CapturingTextLlmClient("unused"), new MockComfyUiClient(), store);
        var viewModel = new MainWindowViewModel(service, loadLastProject: false);

        var loadMethod = typeof(MainWindowViewModel)
            .GetMethod("LoadLastProjectAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        await (Task)loadMethod.Invoke(viewModel, null)!;

        Assert.Equal(2, viewModel.ScriptDrafts.Count);
        Assert.Equal("原文", viewModel.ScriptDrafts[0].Title);
        Assert.Equal("原始正文", viewModel.ScriptDrafts[0].Text);
        Assert.Equal("改编1", viewModel.ScriptDrafts[1].Title);
        Assert.Equal("优化后的正文", viewModel.ScriptDrafts[1].Text);
        Assert.False(viewModel.ScriptDrafts[0].CanClose);
        Assert.True(viewModel.ScriptDrafts[1].CanClose);
        Assert.Same(viewModel.ScriptDrafts[1], viewModel.SelectedScriptDraft);
        Assert.Equal("优化后的正文", viewModel.ScriptText);
    }

    [Fact]
    public async Task LoadLastProjectFallsBackToOriginalWhenSelectedScriptDraftIndexIsInvalid()
    {
        var store = new InMemoryInteractionProjectStore();
        var savedProject = new SceneInteractionBuilder().Build("原始正文") with
        {
            ScriptDrafts =
            [
                new ProjectScriptDraft("原文", "原始正文", false),
                new ProjectScriptDraft("改编1", "优化后的正文", true)
            ],
            SelectedScriptDraftIndex = 99
        };
        await store.SaveAsync(savedProject);
        var service = new DraftWorkspaceService(new CapturingTextLlmClient("unused"), new MockComfyUiClient(), store);
        var viewModel = new MainWindowViewModel(service, loadLastProject: false);

        var loadMethod = typeof(MainWindowViewModel)
            .GetMethod("LoadLastProjectAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        await (Task)loadMethod.Invoke(viewModel, null)!;

        Assert.Same(viewModel.ScriptDrafts[0], viewModel.SelectedScriptDraft);
        Assert.Equal("原始正文", viewModel.ScriptText);
    }

    [Fact]
    public void ScriptTextTracksSelectedDraftTab()
    {
        var viewModel = new MainWindowViewModel(null, loadLastProject: false);
        var adaptation = new ScriptDraftTabViewModel("改编1", "改编");
        viewModel.ScriptDrafts.Add(adaptation);
        viewModel.ScriptDrafts[0].Text = "原文";

        viewModel.SelectedScriptDraft = adaptation;

        Assert.Equal("改编", viewModel.ScriptText);

        viewModel.ScriptText = "改编编辑";

        Assert.Equal("改编编辑", viewModel.ScriptDrafts[1].Text);
        Assert.Equal("原文", viewModel.ScriptDrafts[0].Text);
    }

    [Fact]
    public void LoadSampleTargetsOriginalDraftWhenAdaptationIsSelected()
    {
        var viewModel = new MainWindowViewModel(null, loadLastProject: false);
        var adaptation = new ScriptDraftTabViewModel("改编1", "保留改编");
        viewModel.ScriptDrafts[0].Text = "旧原文";
        viewModel.ScriptDrafts.Add(adaptation);
        viewModel.SelectedScriptDraft = adaptation;

        viewModel.LoadSampleCommand.Execute(null);

        Assert.Same(viewModel.ScriptDrafts[0], viewModel.SelectedScriptDraft);
        Assert.Equal(DraftAnalyzer.SampleScript, viewModel.ScriptDrafts[0].Text);
        Assert.Equal("保留改编", viewModel.ScriptDrafts[1].Text);
    }

    [Fact]
    public async Task LoadSampleThenAnalyzeBuildsWorkbenchFixtureForPsdLayerTesting()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        var viewModel = new MainWindowViewModel(service, loadLastProject: false);

        viewModel.LoadSampleCommand.Execute(null);
        await viewModel.AnalyzeScriptCommand.ExecuteAsync(null);

        var rows = viewModel.SceneTables.SelectMany(scene => scene.Rows).ToArray();
        Assert.Contains(viewModel.Project.Characters, character => character.Name == "林安");
        Assert.Contains(viewModel.Project.Characters, character => character.Name == "陈默");
        Assert.Contains(rows, row =>
            row.IsNarration &&
            row.LineText.Contains("陈默", StringComparison.Ordinal) &&
            row.LineText.Contains("抬手", StringComparison.Ordinal));
        Assert.Contains(rows, row =>
            row.IsNarration &&
            row.LineText.Contains("林安心想", StringComparison.Ordinal));
        Assert.Contains(rows, row =>
            !row.IsNarration &&
            string.Equals(row.SubjectName, "陈默", StringComparison.Ordinal) &&
            row.LineText.Contains("对不起", StringComparison.Ordinal));
        Assert.Contains(rows, row =>
            !row.IsNarration &&
            string.Equals(row.SubjectName, "林安", StringComparison.Ordinal) &&
            row.LineText.Contains("等了多久", StringComparison.Ordinal));
    }

    [Fact]
    public void ClearScriptTargetsOriginalDraftWhenAdaptationIsSelected()
    {
        var viewModel = new MainWindowViewModel(null, loadLastProject: false);
        var adaptation = new ScriptDraftTabViewModel("改编1", "保留改编");
        viewModel.ScriptDrafts[0].Text = "待清空原文";
        viewModel.ScriptDrafts.Add(adaptation);
        viewModel.SelectedScriptDraft = adaptation;

        viewModel.ClearScriptCommand.Execute(null);

        Assert.Same(viewModel.ScriptDrafts[0], viewModel.SelectedScriptDraft);
        Assert.Equal(string.Empty, viewModel.ScriptDrafts[0].Text);
        Assert.Equal("保留改编", viewModel.ScriptDrafts[1].Text);
    }

    [Fact]
    public async Task AnalyzeScriptCommandUsesSelectedScriptDraftText()
    {
        var client = new CapturingTextLlmClient("unused");
        var service = new DraftWorkspaceService(client, new MockComfyUiClient(), new InMemoryInteractionProjectStore());
        var viewModel = new MainWindowViewModel(service, loadLastProject: false);
        var adaptation = new ScriptDraftTabViewModel("改编1", "改编稿：陈默说：现在分析我。");
        viewModel.ScriptDrafts[0].Text = "原文：不要分析我。";
        viewModel.ScriptDrafts.Add(adaptation);
        viewModel.SelectedScriptDraft = adaptation;

        await viewModel.AnalyzeScriptCommand.ExecuteAsync(null);

        Assert.NotNull(client.LastAnalysisRequest);
        Assert.Equal("改编稿：陈默说：现在分析我。", client.LastAnalysisRequest.RawText);
        Assert.Equal(["原文", "改编1"], viewModel.Project.ScriptDrafts.Select(draft => draft.Title));
        Assert.Equal("原文：不要分析我。", viewModel.Project.ScriptDrafts[0].Text);
        Assert.Equal("改编稿：陈默说：现在分析我。", viewModel.Project.ScriptDrafts[1].Text);
        Assert.Equal(1, viewModel.Project.SelectedScriptDraftIndex);
    }

    [Fact]
    public async Task AnalyzeScriptCommandDoesNotOverwriteNewerWorkbenchRevision()
    {
        var client = new GatedAnalysisLlmClient();
        var service = new DraftWorkspaceService(client, new MockComfyUiClient(), new InMemoryInteractionProjectStore());
        var viewModel = new MainWindowViewModel(service, loadLastProject: false)
        {
            ScriptText = "陈默说：旧分析请求。"
        };
        var row = viewModel.SelectedRow!;
        var originalProjectId = viewModel.Project.ProjectId;

        var analyzeTask = viewModel.AnalyzeScriptCommand.ExecuteAsync(null);
        await client.WaitForAnalysisStartedAsync();
        row.LineText = "User edit while analysis is running";
        client.ReleaseAnalysis();
        await analyzeTask;

        Assert.Equal(originalProjectId, viewModel.Project.ProjectId);
        Assert.Equal("User edit while analysis is running", row.LineText);
        Assert.True(viewModel.IsWorkbenchDirty);
        Assert.Contains("为避免覆盖新编辑", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzeScriptCommandBlocksEmptyScriptBeforeAiConnectionCheck()
    {
        var client = new CapturingTextLlmClient("unused");
        var service = new DraftWorkspaceService(client, new MockComfyUiClient(), new InMemoryInteractionProjectStore());
        var viewModel = new MainWindowViewModel(service, loadLastProject: false)
        {
            ScriptText = "   "
        };

        await viewModel.AnalyzeScriptCommand.ExecuteAsync(null);

        Assert.Null(client.LastAnalysisRequest);
        Assert.Equal(0, client.ConnectionCheckCount);
        Assert.Contains("文稿为空", viewModel.StatusText);
    }

    [Fact]
    public async Task AnalyzeScriptCommandBlocksWhenAiConnectionFails()
    {
        var client = new CapturingTextLlmClient(
            "unused",
            new AiConnectionCheckResult(
                "OpenAI",
                "gpt-test",
                "https://api.example/v1",
                AiConnectionState.Unreachable,
                null,
                "AI service unreachable: timeout."));
        var service = new DraftWorkspaceService(client, new MockComfyUiClient(), new InMemoryInteractionProjectStore());
        var viewModel = new MainWindowViewModel(service, loadLastProject: false)
        {
            ScriptText = "陈默说：需要先检测服务。"
        };

        await viewModel.AnalyzeScriptCommand.ExecuteAsync(null);

        Assert.Null(client.LastAnalysisRequest);
        Assert.Equal(1, client.ConnectionCheckCount);
        Assert.Equal("failed", viewModel.AiConnectionState);
        Assert.Contains("AI 服务未连接", viewModel.StatusText);
    }

    [Fact]
    public async Task AnalyzeScriptCommandBlocksWhileWorkbenchMutationLocked()
    {
        var client = new CapturingTextLlmClient("unused");
        var service = new DraftWorkspaceService(client, new MockComfyUiClient(), new InMemoryInteractionProjectStore());
        var viewModel = new MainWindowViewModel(service, loadLastProject: false)
        {
            ScriptText = "陈默说：锁定时不要分析我。",
            IsTtsSynthesisRunning = true
        };

        await viewModel.AnalyzeScriptCommand.ExecuteAsync(null);

        Assert.Null(client.LastAnalysisRequest);
        Assert.Equal(0, client.ConnectionCheckCount);
        Assert.Contains("锁定 AI 分析", viewModel.StatusText);
    }

    [Fact]
    public void CloseScriptDraftRemovesAdaptationAndSelectsOriginal()
    {
        var viewModel = new MainWindowViewModel(null, loadLastProject: false);
        var adaptation = new ScriptDraftTabViewModel("改编1", "改编");
        viewModel.ScriptDrafts.Add(adaptation);
        viewModel.SelectedScriptDraft = adaptation;

        viewModel.CloseScriptDraftCommand.Execute(adaptation);

        Assert.Single(viewModel.ScriptDrafts);
        Assert.Same(viewModel.ScriptDrafts[0], viewModel.SelectedScriptDraft);
        Assert.False(viewModel.HasMultipleScriptDrafts);
    }

    [Fact]
    public void BindingTargetReportsImagePreviewOnlyForExistingImagePath()
    {
        var imagePath = Path.Combine(Path.GetTempPath(), $"draft-agent-image-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(imagePath, [137, 80, 78, 71]);
        var scene = SceneTableViewModel.CreateManual(1);
        var target = BindingTargetViewModel.ForScene(scene);

        target.ImageAsset = imagePath;

        Assert.True(target.HasImagePreview);
        Assert.False(target.HasNoImagePreview);
        Assert.Equal(Path.GetFileName(imagePath), target.ImagePreviewLabel);
    }

    [Fact]
    public void BindingTargetKeepsPlaceholderWhenImageIsUnbound()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var target = BindingTargetViewModel.ForScene(scene);

        Assert.False(target.HasImagePreview);
        Assert.True(target.HasNoImagePreview);
        Assert.Equal("未绑定图片", target.ImagePreviewLabel);
    }

    [Fact]
    public void DialogueRowUsesCharacterImageAsObjectPreview()
    {
        var imagePath = Path.Combine(Path.GetTempPath(), $"draft-agent-character-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(imagePath, [137, 80, 78, 71]);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "陈默");

        row.SelectedCharacterAsset = imagePath;

        Assert.Equal(imagePath, row.ObjectPreviewAsset);
        Assert.True(row.HasObjectPreviewImage);
        Assert.Equal(Path.GetFileName(imagePath), row.ObjectPreviewLabel);
        Assert.Equal("陈", row.ObjectPreviewInitial);
    }

    [Fact]
    public void DialogueRowKeepsCharacterWhenSubjectNameIsClearedByBinding()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "陈默");

        row.SubjectName = null;

        Assert.Equal("陈默", row.SubjectName);
        Assert.Equal("陈", row.ObjectPreviewInitial);
        var scene = SceneTableViewModel.CreateManual(1);
        scene.Rows.Add(row);
        var document = WorkbenchViewModelAdapter.BuildDocument([scene]);
        Assert.Equal("陈默", Assert.Single(Assert.Single(document.Scenes).Rows).SubjectName);
    }

    [Fact]
    public void NarrationRowUsesRowImageAsObjectPreview()
    {
        var sceneImagePath = Path.Combine(Path.GetTempPath(), $"draft-agent-scene-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(sceneImagePath, [137, 80, 78, 71]);
        var row = AnalysisResultRowViewModel.CreateManualNarration(1);

        row.SelectedCharacterAsset = sceneImagePath;
        row.SelectedCharacterPreviewAsset = sceneImagePath;

        Assert.Equal(sceneImagePath, row.ObjectPreviewAsset);
        Assert.True(row.HasObjectPreviewImage);
        Assert.Equal(Path.GetFileName(sceneImagePath), row.ObjectPreviewLabel);
        Assert.Equal("旁", row.ObjectPreviewInitial);
    }

    [Fact]
    public async Task ImageBindingStoresProjectAssetIdAndKeepsResolvedPreviewPath()
    {
        var viewModel = CreateViewModel();
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-project-{Guid.NewGuid():N}");
        var imagePath = Path.Combine(Path.GetTempPath(), $"draft-agent-scene-source-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(imagePath, [137, 80, 78, 71]);

        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Scene);

        await viewModel.SetImageBindingAsync(target, imagePath);
        var scene = viewModel.SceneTables.Single(table => table.SceneId == target.TargetId);

        Assert.StartsWith("asset_", target.ImageAsset);
        Assert.Equal(target.ImageAsset, scene.SelectedSceneAsset);
        Assert.Equal(target.ImagePreviewAsset, scene.SelectedScenePreviewAsset);
        Assert.True(File.Exists(target.ImagePreviewAsset));
        Assert.Contains(projectRoot, target.ImagePreviewAsset, StringComparison.OrdinalIgnoreCase);

        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);

        Assert.Contains(viewModel.Project.Scenes, table => table.AssetBinding.BackgroundImage == target.ImageAsset);
    }

    [Fact]
    public async Task BindingTargetRefreshReusesResolvedProjectAssetPreviewPath()
    {
        var resolver = new CountingProjectAssetResolver();
        var viewModel = CreateViewModel(resolver);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-project-cache-{Guid.NewGuid():N}");
        var imagePath = Path.Combine(Path.GetTempPath(), $"draft-agent-scene-cache-source-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(imagePath, [137, 80, 78, 71]);

        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Scene);
        await viewModel.SetImageBindingAsync(target, imagePath);
        var boundAssetId = target.ImageAsset;
        Assert.DoesNotContain(boundAssetId, resolver.CallsByReference.Keys);

        viewModel.SceneTables.Single(scene => scene.SceneId == target.TargetId).SceneName = "缓存刷新验证";
        var callsAfterFirstRefresh = resolver.CallsByReference[boundAssetId];
        viewModel.SceneTables.Single(scene => scene.SceneId == target.TargetId).SceneName = "缓存刷新验证二";

        Assert.StartsWith("asset_", boundAssetId);
        Assert.Equal(1, callsAfterFirstRefresh);
        Assert.Equal(callsAfterFirstRefresh, resolver.CallsByReference[boundAssetId]);
        Assert.True(File.Exists(target.ImagePreviewAsset));
    }

    [Fact]
    public async Task SceneAssetEditResolvesProjectAssetPreviewLocally()
    {
        var resolver = new CountingProjectAssetResolver();
        var viewModel = CreateViewModel(resolver);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-scene-local-preview-{Guid.NewGuid():N}");
        var imagePath = Path.Combine(Path.GetTempPath(), $"draft-agent-scene-local-preview-source-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(imagePath, [137, 80, 78, 71]);
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Scene);
        await viewModel.SetImageBindingAsync(target, imagePath);
        var scene = viewModel.SceneTables.Single(table => table.SceneId == target.TargetId);
        var assetId = target.ImageAsset;
        var previewPath = target.ImagePreviewAsset;
        scene.SelectedSceneAsset = "未绑定";
        resolver.CallsByReference.Clear();
        var previewNotifications = new List<string>();
        scene.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                previewNotifications.Add(args.PropertyName);
            }
        };

        scene.SelectedSceneAsset = assetId;

        Assert.Equal(assetId, scene.SelectedSceneAsset);
        Assert.Equal(previewPath, scene.SelectedScenePreviewAsset);
        Assert.Equal(1, resolver.CallsByReference[assetId]);
        Assert.Contains(nameof(SceneTableViewModel.SelectedScenePreviewAsset), previewNotifications);
        Assert.Contains(nameof(SceneTableViewModel.HasSceneImagePreview), previewNotifications);
    }

    [Fact]
    public async Task RowCharacterAssetEditResolvesProjectAssetPreviewLocally()
    {
        var resolver = new CountingProjectAssetResolver();
        var viewModel = CreateViewModel(resolver);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-row-local-preview-{Guid.NewGuid():N}");
        var imagePath = Path.Combine(Path.GetTempPath(), $"draft-agent-row-local-preview-source-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(imagePath, [137, 80, 78, 71]);
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var scene = viewModel.SceneTables[0];
        var row = scene.Rows.First(candidate => !candidate.IsNarration);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Scene);
        await viewModel.SetImageBindingAsync(target, imagePath);
        var assetId = target.ImageAsset;
        var previewPath = target.ImagePreviewAsset;
        row.SelectedCharacterAsset = "未绑定";
        resolver.CallsByReference.Clear();
        var rowNotifications = new List<string>();
        var sceneNotifications = new List<string>();
        row.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                rowNotifications.Add(args.PropertyName);
            }
        };
        scene.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                sceneNotifications.Add(args.PropertyName);
            }
        };

        row.SelectedCharacterAsset = assetId;

        Assert.Equal(assetId, row.SelectedCharacterAsset);
        Assert.Equal(previewPath, row.SelectedCharacterPreviewAsset);
        Assert.Equal(assetId, row.ObjectPreviewAsset);
        Assert.Equal(previewPath, row.ObjectPreviewSource);
        Assert.True(row.HasObjectPreviewImage);
        Assert.Contains(nameof(AnalysisResultRowViewModel.SelectedCharacterPreviewAsset), rowNotifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.ObjectPreviewAsset), rowNotifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.ObjectPreviewSource), rowNotifications);
        Assert.Equal(previewPath, scene.FirstCharacterPreviewAsset);
        Assert.DoesNotContain(nameof(SceneTableViewModel.FirstCharacterPreviewAsset), sceneNotifications);
    }

    [Fact]
    public async Task PreviewAssetRefreshDoesNotResolveRepeatedReferencesPerRow()
    {
        var resolver = new CountingProjectAssetResolver();
        var viewModel = CreateViewModel(resolver);
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-repeat-preview-project-{Guid.NewGuid():N}");
        await viewModel.OpenWorkspaceProjectAsync(projectRoot);
        var projectScene = viewModel.Project.Scenes[0];
        var imagePath = Path.Combine(Path.GetTempPath(), $"draft-agent-repeat-preview-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(imagePath, [137, 80, 78, 71]);
        var sourceInteractions = projectScene.Interactions.Count >= 2
            ? projectScene.Interactions
            :
            [
                projectScene.Interactions[0],
                projectScene.Interactions[0] with
                {
                    Id = $"{projectScene.Interactions[0].Id}_repeat",
                    Order = projectScene.Interactions[0].Order + 1
                }
            ];
        var repeatedInteractions = sourceInteractions
            .Select((interaction, index) => interaction with
            {
                Id = index == 0 ? interaction.Id : $"{interaction.Id}_{index}",
                Order = index + 1,
                Binding = interaction.Binding with { SubjectCharacterVisualBinding = VisualAssetBinding.FromImage(imagePath) }
            })
            .ToArray();

        viewModel.Project = viewModel.Project with
        {
            Title = "Refresh repeated previews",
            Scenes = [projectScene with { Interactions = repeatedInteractions }]
        };

        Assert.True(repeatedInteractions.Length >= 2);
        Assert.Equal(2, resolver.CallsByReference[imagePath]);
    }

    [Fact]
    public void BindingTargetRefreshDoesNotResolveRepeatedImageReferencesPerTarget()
    {
        var firstScene = SceneTableViewModel.CreateManual(1);
        var secondScene = SceneTableViewModel.CreateManual(2);
        firstScene.SelectedSceneAsset = "asset_shared_scene";
        secondScene.SelectedSceneAsset = "asset_shared_scene";
        var targets = new System.Collections.ObjectModel.ObservableCollection<BindingTargetViewModel>();
        var callsByReference = new Dictionary<string, int>(StringComparer.Ordinal);

        WorkbenchBindingProjection.RefreshTargets(
            targets,
            [firstScene, secondScene],
            [],
            reference =>
            {
                Assert.NotNull(reference);
                var key = reference.Trim();
                callsByReference[key] = callsByReference.GetValueOrDefault(key) + 1;
                return $"{key}.png";
            });

        Assert.Equal(1, callsByReference["asset_shared_scene"]);
        Assert.All(targets, target => Assert.Equal("asset_shared_scene.png", target.ImagePreviewAsset));
    }

    [Fact]
    public void BindingTargetRefreshKeepsRowPsdRenderedAssetOutOfCharacterCardPreview()
    {
        var renderedPath = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-rendered-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(renderedPath, [137, 80, 78, 71]);
        var sourcePsdPath = Path.Combine(Path.GetTempPath(), $"draft-agent-source-{Guid.NewGuid():N}.psd");
        File.WriteAllBytes(sourcePsdPath, [1, 2, 3]);
        var row = AnalysisResultRowViewModel.FromDraft(new WorkbenchRowDraft(
            InteractionId: "int_psd_preview",
            SceneId: "scene_psd_preview",
            Order: 1,
            Kind: WorkbenchRowKind.Dialogue,
            SubjectName: "Chen",
            SubjectCharacterId: "char_chen",
            Text: "Line",
            Emotion: "Calm",
            EmotionVectors: null,
            EmotionAlpha: null,
            TaskLinks: new WorkbenchTaskLinks(null),
            CharacterVisualBinding: VisualAssetBinding.FromPsd("asset_source_psd", "asset_rendered_png"),
            VoiceBinding: null),
            null);
        var scene = SceneTableViewModel.CreateManual(1);
        scene.Rows.Add(row);
        var character = new Character("char_chen", "Chen", "role", string.Empty, string.Empty, string.Empty, new AssetBinding());
        var targets = new System.Collections.ObjectModel.ObservableCollection<BindingTargetViewModel>();
        var callsByReference = new Dictionary<string, int>(StringComparer.Ordinal);

        WorkbenchBindingProjection.RefreshTargets(
            targets,
            [scene],
            [character],
            reference =>
            {
                Assert.NotNull(reference);
                var key = reference.Trim();
                callsByReference[key] = callsByReference.GetValueOrDefault(key) + 1;
                return key switch
                {
                    "asset_source_psd" => sourcePsdPath,
                    "asset_rendered_png" => renderedPath,
                    _ => null
                };
            });

        var target = Assert.Single(targets, target => target.Kind == BindingTargetKind.Character);
        Assert.Equal("未绑定图片", target.ImageAsset);
        Assert.Equal(string.Empty, target.ImagePreviewAsset);
        Assert.False(target.BindingPreviewShowsImage);
        Assert.Equal("asset_rendered_png", target.RowVisualDetails.Single().RenderedText);
        Assert.DoesNotContain("asset_rendered_png", callsByReference.Keys);
        Assert.DoesNotContain("asset_source_psd", callsByReference.Keys);
    }

    [Fact]
    public void BindingTargetRefreshDoesNotPreviewUnrenderedPsdSource()
    {
        var sourcePsdPath = Path.Combine(Path.GetTempPath(), $"draft-agent-source-{Guid.NewGuid():N}.psd");
        File.WriteAllBytes(sourcePsdPath, [1, 2, 3]);
        var row = AnalysisResultRowViewModel.FromDraft(new WorkbenchRowDraft(
            InteractionId: "int_psd_unrendered",
            SceneId: "scene_psd_unrendered",
            Order: 1,
            Kind: WorkbenchRowKind.Dialogue,
            SubjectName: "Chen",
            SubjectCharacterId: "char_chen",
            Text: "Line",
            Emotion: "Calm",
            EmotionVectors: null,
            EmotionAlpha: null,
            TaskLinks: new WorkbenchTaskLinks(null),
            CharacterVisualBinding: VisualAssetBinding.FromPsd("asset_source_psd"),
            VoiceBinding: null),
            null);
        var scene = SceneTableViewModel.CreateManual(1);
        scene.Rows.Add(row);
        var character = new Character("char_chen", "Chen", "role", string.Empty, string.Empty, string.Empty, new AssetBinding());
        var targets = new System.Collections.ObjectModel.ObservableCollection<BindingTargetViewModel>();
        var callsByReference = new Dictionary<string, int>(StringComparer.Ordinal);

        WorkbenchBindingProjection.RefreshTargets(
            targets,
            [scene],
            [character],
            reference =>
            {
                Assert.NotNull(reference);
                var key = reference.Trim();
                callsByReference[key] = callsByReference.GetValueOrDefault(key) + 1;
                return key == "asset_source_psd" ? sourcePsdPath : null;
            });

        var target = Assert.Single(targets, target => target.Kind == BindingTargetKind.Character);
        Assert.Equal("未绑定图片", target.ImageAsset);
        Assert.Equal(string.Empty, target.ImagePreviewAsset);
        Assert.False(target.BindingPreviewShowsImage);
        Assert.True(target.BindingPreviewShowsText);
        Assert.DoesNotContain("asset_source_psd", callsByReference.Keys);
    }

    [Fact]
    public void SceneTableFromDraftDoesNotUseUnrenderedPsdSourceAsPreviewReference()
    {
        var scene = SceneTableViewModel.FromDraft(new WorkbenchSceneDraft(
            "scene_unrendered_psd",
            1,
            "Unrendered PSD",
            "Rain",
            [],
            VisualAssetBinding.FromPsd("asset_scene_psd")));

        Assert.Equal("未绑定", scene.SelectedSceneAsset);
        Assert.Equal(string.Empty, scene.SelectedScenePreviewAsset);
        Assert.False(scene.HasSceneImagePreview);
    }

    [Fact]
    public async Task OpeningExistingWorkspaceProjectRestoresItsSavedDraft()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-existing-project-{Guid.NewGuid():N}");
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(projectRoot, "Existing UI Project");
        var savedProject = new SceneInteractionBuilder().Build("林安说：这是旧项目。") with { Title = "Existing UI Draft" };
        await manager.SaveDraftAsync(WorkspaceProjectManager.ToWorkspace(manifest), savedProject);
        var viewModel = CreateViewModel();
        viewModel.ScriptText = "陈默说：这是当前工作台。";

        await viewModel.OpenWorkspaceProjectAsync(projectRoot);

        Assert.Equal("Existing UI Draft", viewModel.Project.Title);
        Assert.Contains("旧项目", viewModel.ScriptText);
        Assert.DoesNotContain("当前工作台", viewModel.ScriptText);
    }

    [Fact]
    public void AiSettingsCancelRestoresDraftValues()
    {
        var viewModel = CreateViewModel();
        var originalProvider = viewModel.SelectedAiProvider;
        var originalBaseUrl = viewModel.AiBaseUrl;
        var originalModel = viewModel.AiModelName;
        var originalComfyEndpoint = viewModel.ComfyEndpoint;

        viewModel.BeginAiSettingsEdit();
        viewModel.SelectedAiProvider = "OpenAI";
        viewModel.AiApiKey = "changed";
        viewModel.AiBaseUrl = "https://changed.example/v1";
        viewModel.AiModelName = "changed-model";
        viewModel.AiMaxOutputTokensText = "99999";
        viewModel.ComfyEndpoint = "http://127.0.0.1:9000";

        viewModel.CancelAiSettingsEdit();

        Assert.Equal(originalProvider, viewModel.SelectedAiProvider);
        Assert.Equal(originalBaseUrl, viewModel.AiBaseUrl);
        Assert.Equal(originalModel, viewModel.AiModelName);
        Assert.Equal(originalComfyEndpoint, viewModel.ComfyEndpoint);
        Assert.Equal(string.Empty, viewModel.AiApiKey);
    }

    [Fact]
    public void AiSettingsCommitKeepsDraftValues()
    {
        var viewModel = CreateViewModel();

        viewModel.BeginAiSettingsEdit();
        viewModel.SelectedAiProvider = "OpenAI";
        viewModel.AiApiKey = "saved";
        viewModel.AiBaseUrl = "https://saved.example/v1";
        viewModel.AiModelName = "saved-model";
        viewModel.AiMaxOutputTokensText = "99999";
        viewModel.ComfyEndpoint = "http://127.0.0.1:9000";
        viewModel.CommitAiSettingsEdit();
        viewModel.CancelAiSettingsEdit();

        Assert.Equal("OpenAI", viewModel.SelectedAiProvider);
        Assert.Equal("saved", viewModel.AiApiKey);
        Assert.Equal("https://saved.example/v1", viewModel.AiBaseUrl);
        Assert.Equal("saved-model", viewModel.AiModelName);
        Assert.Equal(99999, viewModel.AiMaxOutputTokens);
        Assert.Equal("99999", viewModel.AiMaxOutputTokensText);
        Assert.Equal("http://127.0.0.1:9000", viewModel.ComfyEndpoint);
    }

    [Fact]
    public void AiApiKeyErrorBrushMarksMissingOnlineKeyAfterFailedCheck()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectedAiProvider = "DeepSeek";
        viewModel.AiApiKey = string.Empty;
        viewModel.AiConnectionState = "failed";

        Assert.Equal(Color.Parse("#2A1114"), BrushColor(viewModel.AiApiKeyBackground));
        Assert.Equal(Color.Parse("#FF453A"), BrushColor(viewModel.AiApiKeyBorderBrush));
    }

    [Fact]
    public void AiApiKeyErrorBrushStaysNeutralForLmStudioWithoutKey()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectedAiProvider = "LM Studio";
        viewModel.AiApiKey = string.Empty;
        viewModel.AiConnectionState = "failed";

        Assert.Equal(Color.Parse("#111722"), BrushColor(viewModel.AiApiKeyBackground));
        Assert.Equal(Color.Parse("#2C3546"), BrushColor(viewModel.AiApiKeyBorderBrush));
    }

    [Fact]
    public void AiApiKeyErrorBrushClearsWhenOnlineKeyIsEntered()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectedAiProvider = "OpenAI";
        viewModel.AiConnectionState = "failed";
        Assert.Equal(Color.Parse("#FF453A"), BrushColor(viewModel.AiApiKeyBorderBrush));

        viewModel.AiApiKey = "sk-test";

        Assert.Equal(Color.Parse("#111722"), BrushColor(viewModel.AiApiKeyBackground));
        Assert.Equal(Color.Parse("#2C3546"), BrushColor(viewModel.AiApiKeyBorderBrush));
    }

    [Fact]
    public async Task TestAiConnectionMarksApiKeyErrorWhenOnlineKeyIsUnauthorized()
    {
        var viewModel = CreateViewModel(new ConnectionCheckLlmClient(new AiConnectionCheckResult(
            "OpenAI",
            "gpt-test",
            "https://api.example/v1",
            AiConnectionState.Unauthorized,
            401,
            "AI service check failed: HTTP 401 invalid API key.")));
        viewModel.SelectedAiProvider = "OpenAI";
        viewModel.AiApiKey = "wrong-key";

        await viewModel.TestAiConnectionCommand.ExecuteAsync(null);

        Assert.Equal("failed", viewModel.AiConnectionState);
        Assert.Equal(Color.Parse("#2A1114"), BrushColor(viewModel.AiApiKeyBackground));
        Assert.Equal(Color.Parse("#FF453A"), BrushColor(viewModel.AiApiKeyBorderBrush));
    }

    [Fact]
    public async Task TestAiConnectionKeepsApiKeyNeutralForNonKeyConnectionFailure()
    {
        var viewModel = CreateViewModel(new ConnectionCheckLlmClient(new AiConnectionCheckResult(
            "OpenAI",
            "gpt-test",
            "https://api.example/v1",
            AiConnectionState.Unreachable,
            null,
            "AI service unreachable: connection timed out.")));
        viewModel.SelectedAiProvider = "OpenAI";
        viewModel.AiApiKey = "sk-present";

        await viewModel.TestAiConnectionCommand.ExecuteAsync(null);

        Assert.Equal("failed", viewModel.AiConnectionState);
        Assert.Equal(Color.Parse("#111722"), BrushColor(viewModel.AiApiKeyBackground));
        Assert.Equal(Color.Parse("#2C3546"), BrushColor(viewModel.AiApiKeyBorderBrush));
    }

    [Fact]
    public void ProjectRefreshKeepsSelectionOnCurrentRowInstance()
    {
        var viewModel = CreateViewModel();
        var selected = viewModel.SelectedRow;

        Assert.NotNull(selected);

        viewModel.Project = viewModel.Project with { Title = "refreshed" };

        var currentRows = viewModel.SceneTables.SelectMany(scene => scene.Rows).ToArray();
        Assert.NotNull(viewModel.SelectedRow);
        Assert.Equal(selected.InteractionId, viewModel.SelectedRow.InteractionId);
        Assert.Same(selected, viewModel.SelectedRow);
        Assert.Contains(viewModel.SelectedRow, currentRows);
        Assert.True(viewModel.SelectedRow.IsSelected);
    }

    private static MainWindowViewModel CreateViewModel(IProjectAssetResolver? projectAssetResolver = null)
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        return new MainWindowViewModel(
            service,
            projectAssetResolver: projectAssetResolver,
            aiSettingsStore: CreateIsolatedAiSettingsStore());
    }

    private static MainWindowViewModel CreateViewModel(ILlmAnalysisClient llmClient)
    {
        var service = new DraftWorkspaceService(
            llmClient,
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        return new MainWindowViewModel(
            service,
            loadLastProject: false,
            aiSettingsStore: CreateIsolatedAiSettingsStore());
    }

    private static AiSettingsStore CreateIsolatedAiSettingsStore()
    {
        return new AiSettingsStore(Path.Combine(
            Path.GetTempPath(),
            $"draft-agent-ai-settings-{Guid.NewGuid():N}",
            "ai-settings.json"));
    }

    private static Color BrushColor(IBrush brush)
    {
        return Assert.IsType<SolidColorBrush>(brush).Color;
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
}

file sealed class CapturingTextLlmClient : ILlmAnalysisClient, IAiTextGenerationClient, IAiConnectionClient
{
    private readonly string _optimizedText;
    private readonly AiConnectionCheckResult _connectionResult;

    public CapturingTextLlmClient(string optimizedText, AiConnectionCheckResult? connectionResult = null)
    {
        _optimizedText = optimizedText;
        _connectionResult = connectionResult ?? new AiConnectionCheckResult(
            "Test",
            "test-model",
            "mock://ai",
            AiConnectionState.Connected,
            200,
            "Test AI connected.");
    }

    public AiModelTextRequest? LastTextRequest { get; private set; }

    public LlmAnalysisRequest? LastAnalysisRequest { get; private set; }

    public int ConnectionCheckCount { get; private set; }

    public Task<LlmAnalysisResponse> AnalyzeAsync(LlmAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        LastAnalysisRequest = request;
        var project = new SceneInteractionBuilder().Build(request.RawText);
        var rawJson = JsonSerializer.Serialize(project, DraftJson.Options);
        return Task.FromResult(new LlmAnalysisResponse(rawJson, project, rawJson, new AiModelReplyMetadata("Test", "test-model", "stop", AiReplyState.Completed, "ok")));
    }

    public Task<AiModelTextResponse> GenerateTextAsync(AiModelTextRequest request, CancellationToken cancellationToken = default)
    {
        LastTextRequest = request;
        return Task.FromResult(new AiModelTextResponse(_optimizedText, _optimizedText, new AiModelReplyMetadata("Test", "test-model", "stop", AiReplyState.Completed, "ok")));
    }

    public Task<AiConnectionCheckResult> CheckConnectionAsync(CancellationToken cancellationToken = default)
    {
        ConnectionCheckCount++;
        return Task.FromResult(_connectionResult);
    }
}

file sealed class GatedAnalysisLlmClient : ILlmAnalysisClient, IAiConnectionClient
{
    private readonly TaskCompletionSource _analysisStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _releaseAnalysis = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<LlmAnalysisResponse> AnalyzeAsync(LlmAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        _analysisStarted.SetResult();
        await _releaseAnalysis.Task.WaitAsync(cancellationToken);
        var project = new SceneInteractionBuilder().Build("林安说：这是迟到的分析结果。");
        var rawJson = JsonSerializer.Serialize(project, DraftJson.Options);
        return new LlmAnalysisResponse(rawJson, project, rawJson, new AiModelReplyMetadata("Test", "test-model", "stop", AiReplyState.Completed, "ok"));
    }

    public Task<AiConnectionCheckResult> CheckConnectionAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AiConnectionCheckResult(
            "Test",
            "test-model",
            "mock://ai",
            AiConnectionState.Connected,
            200,
            "Test AI connected."));
    }

    public Task WaitForAnalysisStartedAsync()
    {
        return _analysisStarted.Task;
    }

    public void ReleaseAnalysis()
    {
        _releaseAnalysis.SetResult();
    }
}

file sealed class ConnectionCheckLlmClient : ILlmAnalysisClient, IAiConnectionClient
{
    private readonly AiConnectionCheckResult _result;

    public ConnectionCheckLlmClient(AiConnectionCheckResult result)
    {
        _result = result;
    }

    public Task<LlmAnalysisResponse> AnalyzeAsync(LlmAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        var project = new SceneInteractionBuilder().Build(request.RawText);
        var rawJson = JsonSerializer.Serialize(project, DraftJson.Options);
        return Task.FromResult(new LlmAnalysisResponse(rawJson, project, rawJson, new AiModelReplyMetadata("Test", "test-model", "stop", AiReplyState.Completed, "ok")));
    }

    public Task<AiConnectionCheckResult> CheckConnectionAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_result);
    }
}


