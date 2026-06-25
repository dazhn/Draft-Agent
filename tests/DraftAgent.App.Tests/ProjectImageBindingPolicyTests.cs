using DraftAgent.App.ViewModels;
using DraftAgent.Core;

namespace DraftAgent.App.Tests;

public sealed class ProjectImageBindingPolicyTests
{
    [Theory]
    [InlineData("scene.png", MediaAssetKind.Image, MediaAssetSource.ImportedPng)]
    [InlineData("scene.JPG", MediaAssetKind.Image, MediaAssetSource.ImportedPng)]
    public void BuildImportRequestMapsSceneImageTypeFromExtension(
        string fileName,
        MediaAssetKind expectedKind,
        MediaAssetSource expectedSource)
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var target = BindingTargetViewModel.ForScene(scene);
        var path = Path.Combine(Path.GetTempPath(), fileName);

        var request = ProjectImageBindingPolicy.BuildImportRequest(target, path);

        Assert.Equal(path, request.SourcePath);
        Assert.Equal(expectedKind, request.Kind);
        Assert.Equal(expectedSource, request.Source);
        Assert.Equal(target.TargetId, request.TargetId);
        Assert.Equal(target.DisplayName, request.DisplayName);
    }

    [Theory]
    [InlineData("character.psd")]
    [InlineData("character.PSB")]
    public void BuildImportRequestAllowsPsdForCharacterTarget(string fileName)
    {
        var character = new Character("char_chen", "Chen", "role", string.Empty, string.Empty, string.Empty, new AssetBinding());
        var target = BindingTargetViewModel.ForCharacter(character, []);
        var path = Path.Combine(Path.GetTempPath(), fileName);

        var request = ProjectImageBindingPolicy.BuildImportRequest(target, path);

        Assert.Equal(MediaAssetKind.Psd, request.Kind);
        Assert.Equal(MediaAssetSource.ImportedPsd, request.Source);
        Assert.Equal(target.TargetId, request.TargetId);
    }

    [Theory]
    [InlineData("scene.psd")]
    [InlineData("scene.PSB")]
    public void BuildImportRequestRejectsPsdForSceneTarget(string fileName)
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var target = BindingTargetViewModel.ForScene(scene);
        var path = Path.Combine(Path.GetTempPath(), fileName);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ProjectImageBindingPolicy.BuildImportRequest(target, path));

        Assert.Contains("PSD", exception.Message);
        Assert.Contains("character", exception.Message);
    }

    [Fact]
    public void BuildImportRequestKeepsBindingMetadata()
    {
        var scene = SceneTableViewModel.CreateManual(1);
        var target = BindingTargetViewModel.ForScene(scene);
        var path = Path.Combine(Path.GetTempPath(), "scene-reference.png");

        var request = ProjectImageBindingPolicy.BuildImportRequest(target, path);

        Assert.NotNull(request.Metadata);
        Assert.Equal(BindingTargetKind.Scene.ToString(), request.Metadata!["bindingKind"]);
        Assert.Equal("scene-reference.png", request.Metadata["originalFileName"]);
    }
}

