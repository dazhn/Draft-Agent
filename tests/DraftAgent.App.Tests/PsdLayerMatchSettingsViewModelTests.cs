using DraftAgent.App.ViewModels;
using DraftAgent.Core;

namespace DraftAgent.App.Tests;

public sealed class PsdLayerMatchSettingsViewModelTests
{
    [Fact]
    public void BindingWholeExpressionClearsFacialPartGroups()
    {
        var groups = TestGroups();
        var viewModel = new PsdLayerMatchSettingsViewModel(
            "陈",
            groups,
            new PsdLayerMatchConfiguration(
                BrowGroup: groups[1].ToCatalog(),
                EyeGroup: groups[2].ToCatalog(),
                MouthGroup: groups[3].ToCatalog()));

        viewModel.SelectPsdGroup(FindPsdGroup(viewModel, "表情"));
        viewModel.ApplySelectedGroupToTemplate(FindTemplateNode(viewModel, PsdLayerTemplateRole.Expression));
        var configuration = viewModel.BuildConfiguration();

        Assert.NotNull(configuration.ExpressionGroup);
        Assert.Equal("表情", configuration.ExpressionGroup.GroupName);
        Assert.Null(configuration.BrowGroup);
        Assert.Null(configuration.EyeGroup);
        Assert.Null(configuration.MouthGroup);
        Assert.Contains("整体", viewModel.FaceModeText, StringComparison.Ordinal);
        Assert.False(FindTemplateNode(viewModel, PsdLayerTemplateRole.Brow).CanApply);
        Assert.False(FindTemplateNode(viewModel, PsdLayerTemplateRole.Eye).CanApply);
        Assert.False(FindTemplateNode(viewModel, PsdLayerTemplateRole.Mouth).CanApply);
    }

    [Fact]
    public void BindingFacialPartClearsWholeExpression()
    {
        var groups = TestGroups();
        var viewModel = new PsdLayerMatchSettingsViewModel(
            "陈",
            groups,
            new PsdLayerMatchConfiguration(
                ExpressionGroup: groups[0].ToCatalog()));

        viewModel.SelectPsdGroup(FindPsdGroup(viewModel, "眉毛"));
        viewModel.ApplySelectedGroupToTemplate(FindTemplateNode(viewModel, PsdLayerTemplateRole.Brow));
        var configuration = viewModel.BuildConfiguration();

        Assert.Null(configuration.ExpressionGroup);
        Assert.NotNull(configuration.BrowGroup);
        Assert.Equal("眉毛", configuration.BrowGroup.GroupName);
        Assert.Contains("眉眼嘴", viewModel.FaceModeText, StringComparison.Ordinal);
        Assert.True(FindTemplateNode(viewModel, PsdLayerTemplateRole.Brow).CanApply);
    }

    [Fact]
    public void BindingActionPartsPersistsLeftAndRightHandGroups()
    {
        var groups = TestGroups();
        var viewModel = new PsdLayerMatchSettingsViewModel("陈", groups);

        viewModel.SelectPsdGroup(FindPsdGroup(viewModel, "左手"));
        viewModel.ApplySelectedGroupToTemplate(FindTemplateNode(viewModel, PsdLayerTemplateRole.LeftHand));
        viewModel.SelectPsdGroup(FindPsdGroup(viewModel, "右手"));
        viewModel.ApplySelectedGroupToTemplate(FindTemplateNode(viewModel, PsdLayerTemplateRole.RightHand));

        var configuration = viewModel.BuildConfiguration();

        Assert.Equal("左手", configuration.LeftHandGroup?.GroupName);
        Assert.Equal("右手", configuration.RightHandGroup?.GroupName);
    }

    [Fact]
    public void LeftTreeShowsGroupHierarchyWithoutBindingVirtualParent()
    {
        var viewModel = new PsdLayerMatchSettingsViewModel("陈", TestGroups());
        var root = Assert.Single(viewModel.PsdGroupTree, node => node.DisplayName == "角色");

        Assert.False(root.CanBind);
        viewModel.SelectPsdGroup(root);

        Assert.Null(viewModel.SelectedPsdGroup);
        Assert.Contains("结构节点", viewModel.StatusText, StringComparison.Ordinal);
        Assert.Contains(viewModel.PsdGroupTree, node => node.DisplayName == "角色");
        Assert.DoesNotContain(viewModel.PsdGroupTree, node => node.DisplayName.EndsWith(".psd", StringComparison.OrdinalIgnoreCase));
    }

    private static PsdLayerGroupTreeNodeViewModel FindPsdGroup(
        PsdLayerMatchSettingsViewModel viewModel,
        string displayName)
    {
        return Flatten(viewModel.PsdGroupTree)
            .Single(node => node.DisplayName == displayName);
    }

    private static PsdLayerTemplateTreeNodeViewModel FindTemplateNode(
        PsdLayerMatchSettingsViewModel viewModel,
        PsdLayerTemplateRole role)
    {
        return Flatten(viewModel.TemplateTree)
            .Single(node => node.Role == role);
    }

    private static IEnumerable<PsdLayerGroupTreeNodeViewModel> Flatten(
        IEnumerable<PsdLayerGroupTreeNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }

    private static IEnumerable<PsdLayerTemplateTreeNodeViewModel> Flatten(
        IEnumerable<PsdLayerTemplateTreeNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }

    private static PsdLayerGroupOptionViewModel[] TestGroups()
    {
        return
        [
            new PsdLayerGroupOptionViewModel(1, "表情", "角色/表情", ["开心", "难过"]),
            new PsdLayerGroupOptionViewModel(2, "眉毛", "角色/表情/眉毛", ["皱眉"]),
            new PsdLayerGroupOptionViewModel(3, "眼睛", "角色/表情/眼睛", ["闭眼"]),
            new PsdLayerGroupOptionViewModel(4, "嘴巴", "角色/表情/嘴巴", ["张嘴"]),
            new PsdLayerGroupOptionViewModel(5, "动作", "角色/动作", ["抬手"]),
            new PsdLayerGroupOptionViewModel(6, "左手", "角色/动作/左手", ["伸手"]),
            new PsdLayerGroupOptionViewModel(7, "右手", "角色/动作/右手", ["握拳"])
        ];
    }
}
