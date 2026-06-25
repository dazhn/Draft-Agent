using System.ComponentModel;
using System.Reflection;
using DraftAgent.App.ViewModels;

namespace DraftAgent.App.Tests;

public sealed class SceneTableViewModelTests
{
    [Fact]
    public void ScenePreviewUsesCachedSnapshotUntilPreviewAssetChanges()
    {
        var firstImage = CreateTempPng();
        var secondImage = CreateTempPng();
        var scene = SceneTableViewModel.CreateManual(1);

        scene.SelectedScenePreviewAsset = firstImage;

        Assert.True(scene.HasSceneImagePreview);
        Assert.Equal(firstImage, scene.SelectedScenePreviewAsset);
        Assert.Equal(Path.GetFileName(firstImage), scene.SceneImagePreviewLabel);

        scene.SelectedScenePreviewAsset = secondImage;

        Assert.True(scene.HasSceneImagePreview);
        Assert.Equal(secondImage, scene.SelectedScenePreviewAsset);
        Assert.Equal(Path.GetFileName(secondImage), scene.SceneImagePreviewLabel);
    }

    [Fact]
    public void ScenePreviewInvalidatesWhenFallbackAssetChanges()
    {
        var scene = SceneTableViewModel.CreateManual(1);

        scene.SelectedSceneAsset = "asset_scene_a";
        Assert.False(scene.HasSceneImagePreview);
        Assert.Equal("asset_scene_a", scene.SceneImagePreviewLabel);

        scene.SelectedSceneAsset = "asset_scene_b";

        Assert.False(scene.HasSceneImagePreview);
        Assert.Equal("asset_scene_b", scene.SceneImagePreviewLabel);
    }

    [Fact]
    public void SceneAssetClearsPreviewWhenItBecomesUnbound()
    {
        var image = CreateTempPng();
        var scene = SceneTableViewModel.CreateManual(1);
        scene.SelectedSceneAsset = image;

        Assert.Equal(image, scene.SelectedScenePreviewAsset);
        Assert.True(scene.HasSceneImagePreview);

        scene.SelectedSceneAsset = "未绑定";

        Assert.Equal(string.Empty, scene.SelectedScenePreviewAsset);
        Assert.False(scene.HasSceneImagePreview);
        Assert.Equal("未绑定", scene.SceneImagePreviewLabel);
    }

    [Fact]
    public void RefreshPreviewAssetsSkipsNotificationsWhenPreviewStateIsUnchanged()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        _ = scene.SceneImagePreviewLabel;
        _ = scene.FirstCharacterPreviewAsset;
        var notifications = new List<string>();
        scene.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                notifications.Add(args.PropertyName);
            }
        };

        scene.RefreshPreviewAssets();

        Assert.Empty(notifications);
    }

    [Fact]
    public void FirstCharacterOnlyRefreshKeepsScenePreviewCache()
    {
        var sceneImage = CreateTempPng();
        var characterImage = CreateTempPng();
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        row.SelectedCharacterPreviewAsset = characterImage;
        scene.Rows.Add(row);
        scene.SelectedScenePreviewAsset = sceneImage;
        _ = scene.SceneImagePreviewLabel;
        _ = scene.FirstCharacterPreviewAsset;
        var scenePreviewSnapshot = GetScenePreviewSnapshot(scene);
        var firstCharacterPreviewSnapshot = GetFirstCharacterPreviewSnapshot(scene);
        var notifications = new List<string>();
        scene.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                notifications.Add(args.PropertyName);
            }
        };

        scene.RefreshPreviewAssets(scenePreviewChanged: false, firstCharacterPreviewChanged: true);

        Assert.Same(scenePreviewSnapshot, GetScenePreviewSnapshot(scene));
        Assert.DoesNotContain(nameof(SceneTableViewModel.SceneImagePreviewLabel), notifications);
        Assert.Equal(characterImage, scene.FirstCharacterPreviewAsset);
        Assert.NotSame(firstCharacterPreviewSnapshot, GetFirstCharacterPreviewSnapshot(scene));
        Assert.DoesNotContain(nameof(SceneTableViewModel.FirstCharacterPreviewAsset), notifications);
    }

    [Fact]
    public void SceneAssetPathChangeRefreshesScenePreviewOnlyOnce()
    {
        var image = CreateTempPng();
        var scene = SceneTableViewModel.CreateManual(1);
        var notifications = new List<string>();
        scene.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                notifications.Add(args.PropertyName);
            }
        };

        scene.SelectedSceneAsset = image;

        Assert.Equal(image, scene.SelectedScenePreviewAsset);
        Assert.Single(notifications, nameof(SceneTableViewModel.HasSceneImagePreview));
        Assert.Single(notifications, nameof(SceneTableViewModel.HasNoSceneImagePreview));
        Assert.Single(notifications, nameof(SceneTableViewModel.SceneImagePreviewLabel));
    }

    [Fact]
    public void FirstCharacterPreviewUsesCachedSnapshotUntilPreviewAssetChanges()
    {
        var firstImage = CreateTempPng();
        var secondImage = CreateTempPng();
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        scene.Rows.Add(row);

        row.SelectedCharacterPreviewAsset = firstImage;

        Assert.Equal(firstImage, scene.FirstCharacterPreviewAsset);
        Assert.Equal(Path.GetFileName(firstImage), scene.FirstCharacterPreviewLabel);
        Assert.True(scene.HasFirstCharacterPreview);

        row.SelectedCharacterPreviewAsset = secondImage;

        Assert.Equal(secondImage, scene.FirstCharacterPreviewAsset);
        Assert.Equal(Path.GetFileName(secondImage), scene.FirstCharacterPreviewLabel);
        Assert.True(scene.HasFirstCharacterPreview);
    }

    [Fact]
    public void FirstCharacterPreviewInvalidatesWhenPreviewRowIsRemoved()
    {
        var image = CreateTempPng();
        var scene = SceneTableViewModel.CreateManual(1);
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        row.SelectedCharacterPreviewAsset = image;
        scene.Rows.Add(row);

        Assert.Equal(image, scene.FirstCharacterPreviewAsset);

        scene.Rows.Remove(row);

        Assert.False(scene.HasFirstCharacterPreview);
        Assert.Equal("随行显示", scene.FirstCharacterPreviewAsset);
        Assert.Equal("随行显示", scene.FirstCharacterPreviewLabel);
    }

    [Fact]
    public void MovingRowWithinSceneKeepsOwnerSceneAndInvalidatesAppliedDraft()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var first = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var second = AnalysisResultRowViewModel.CreateManualDialogue(2, "Lin");
        scene.Rows.Add(first);
        scene.Rows.Add(second);
        var document = WorkbenchViewModelAdapter.BuildDocument([scene]);
        WorkbenchViewModelAdapter.MarkAppliedDrafts(scene, document);

        scene.Rows.Move(0, 1);

        Assert.Same(scene, first.OwnerScene);
        Assert.Same(scene, second.OwnerScene);
        Assert.Null(scene.AppliedDraft);
    }

    [Fact]
    public void MovingRowWithinSceneKeepsRowPropertyChangedSubscriptionStable()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var first = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var second = AnalysisResultRowViewModel.CreateManualDialogue(2, "Lin");
        scene.Rows.Add(first);
        scene.Rows.Add(second);
        var beforeMove = GetPropertyChangedHandler(first);

        scene.Rows.Move(0, 1);
        scene.Rows.Move(1, 0);

        Assert.NotNull(beforeMove);
        Assert.Same(beforeMove, GetPropertyChangedHandler(first));
    }

    [Fact]
    public void MovingRowWithinSceneUpdatesFirstCharacterPreviewWithoutNotification()
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
        _ = scene.FirstCharacterPreviewAsset;
        var notifications = new List<string>();
        scene.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                notifications.Add(args.PropertyName);
            }
        };

        scene.Rows.Move(2, 1);

        Assert.Equal(firstImage, scene.FirstCharacterPreviewAsset);
        Assert.DoesNotContain(nameof(SceneTableViewModel.FirstCharacterPreviewAsset), notifications);

        scene.Rows.Move(2, 0);

        Assert.Equal(secondImage, scene.FirstCharacterPreviewAsset);
        Assert.DoesNotContain(nameof(SceneTableViewModel.FirstCharacterPreviewAsset), notifications);
    }

    [Fact]
    public void LaterRowResolvedPreviewChangeDoesNotNotifyFirstCharacterPreviewWhenFirstPreviewIsUnchanged()
    {
        var firstImage = CreateTempPng();
        var secondImage = CreateTempPng();
        var changedSecondImage = CreateTempPng();
        var scene = SceneTableViewModel.CreateManual(1);
        var first = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var second = AnalysisResultRowViewModel.CreateManualDialogue(2, "Lin");
        first.SelectedCharacterPreviewAsset = firstImage;
        second.SelectedCharacterPreviewAsset = secondImage;
        scene.Rows.Add(first);
        scene.Rows.Add(second);
        _ = scene.FirstCharacterPreviewAsset;
        var notifications = new List<string>();
        scene.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                notifications.Add(args.PropertyName);
            }
        };

        var changes = second.ApplyResolvedPreviewAssets(changedSecondImage, second.PreviewAssetPath);
        scene.RefreshFirstCharacterPreviewAfterResolvedAssetChange(second);

        Assert.True(changes.CharacterPreviewChanged);
        Assert.Equal(firstImage, scene.FirstCharacterPreviewAsset);
        Assert.DoesNotContain(nameof(SceneTableViewModel.FirstCharacterPreviewAsset), notifications);
        Assert.DoesNotContain(nameof(SceneTableViewModel.FirstCharacterPreviewLabel), notifications);
    }

    [Fact]
    public void MovingRowsWithoutCharacterPreviewImagesKeepsFirstCharacterPreviewCache()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var first = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var second = AnalysisResultRowViewModel.CreateManualDialogue(2, "Lin");
        var third = AnalysisResultRowViewModel.CreateManualDialogue(3, "Mo");
        scene.Rows.Add(first);
        scene.Rows.Add(second);
        scene.Rows.Add(third);
        _ = scene.FirstCharacterPreviewAsset;
        var snapshot = GetFirstCharacterPreviewSnapshot(scene);
        var notifications = new List<string>();
        scene.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                notifications.Add(args.PropertyName);
            }
        };

        scene.Rows.Move(0, 2);

        Assert.Same(snapshot, GetFirstCharacterPreviewSnapshot(scene));
        Assert.DoesNotContain(nameof(SceneTableViewModel.FirstCharacterPreviewAsset), notifications);
        Assert.Equal("随行显示", scene.FirstCharacterPreviewAsset);
    }

    [Fact]
    public void NextRowOrderUsesCountWhenRowsAreSequential()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        scene.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen"));
        scene.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(2, "Lin"));
        scene.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(3, "Mo"));

        Assert.Equal(4, scene.NextRowOrder());
    }

    [Fact]
    public void NextRowOrderFallsBackToMaxWhenRowOrdersAreStale()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        scene.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(10, "Chen"));
        scene.Rows.Add(AnalysisResultRowViewModel.CreateManualDialogue(5, "Lin"));

        Assert.Equal(11, scene.NextRowOrder());
    }

    private static string CreateTempPng()
    {
        var path = Path.Combine(Path.GetTempPath(), $"draft-agent-scene-preview-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, [137, 80, 78, 71]);
        return path;
    }

    private static PropertyChangedEventHandler? GetPropertyChangedHandler(AnalysisResultRowViewModel row)
    {
        for (var type = row.GetType(); type is not null; type = type.BaseType)
        {
            var field = type.GetField(
                nameof(AnalysisResultRowViewModel.PropertyChanged),
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field?.GetValue(row) is PropertyChangedEventHandler handler)
            {
                return handler;
            }
        }

        return null;
    }

    private static object? GetFirstCharacterPreviewSnapshot(SceneTableViewModel scene)
    {
        var field = typeof(SceneTableViewModel).GetField(
            "_firstCharacterPreview",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(scene);
    }

    private static object? GetScenePreviewSnapshot(SceneTableViewModel scene)
    {
        var field = typeof(SceneTableViewModel).GetField(
            "_scenePreview",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(scene);
    }
}


