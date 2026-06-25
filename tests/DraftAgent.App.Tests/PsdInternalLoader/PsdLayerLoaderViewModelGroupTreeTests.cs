using DraftAgent.App.Psd.Services;
using DraftAgent.App.Psd.ViewModels;
using DraftAgent.Core;
using PsdLayerLoader.Core;

namespace DraftAgent.App.Tests.PsdInternalLoader;

public sealed class PsdLayerLoaderViewModelGroupTreeTests
{
    [Fact]
    public void GroupTreeDefaultsExpandedAndCanCollapseNestedGroups()
    {
        var uiState = new PsdLayerManagementUiState();
        AddThumbnail(uiState, layerId: 10, parentLayerId: null, name: "Character", isGroup: true);
        AddThumbnail(uiState, layerId: 20, parentLayerId: 10, name: "Expression", isGroup: true);

        uiState.RebuildBrowserTabs();

        var parent = uiState.GroupTabs.FirstOrDefault(tab => tab.HasChildren && tab.GroupLayerId is not null);
        Assert.NotNull(parent);

        var nested = uiState.GroupTabs.FirstOrDefault(tab => tab.Depth > parent.Depth);
        Assert.NotNull(nested);

        uiState.ToggleGroupExpanded(parent);

        Assert.DoesNotContain(uiState.GroupTabs, tab => tab.Depth > parent.Depth);
        var collapsedParent = uiState.GroupTabs.Single(tab => tab.GroupLayerId == parent.GroupLayerId);
        Assert.False(collapsedParent.IsExpanded);

        uiState.ToggleGroupExpanded(collapsedParent);

        Assert.Contains(uiState.GroupTabs, tab => tab.GroupLayerId == nested.GroupLayerId);
        var expandedParent = uiState.GroupTabs.Single(tab => tab.GroupLayerId == parent.GroupLayerId);
        Assert.True(expandedParent.IsExpanded);
    }

    [Fact]
    public void LayerThumbnailGridIncludesHideOptionAndExcludesGroups()
    {
        var uiState = new PsdLayerManagementUiState();
        AddThumbnail(uiState, layerId: 10, parentLayerId: null, name: "Character", isGroup: true);
        AddThumbnail(uiState, layerId: 20, parentLayerId: null, name: "Base", isGroup: false);
        AddThumbnail(uiState, layerId: 30, parentLayerId: 10, name: "Eyes", isGroup: false);

        uiState.RefreshLayerThumbnailGrid();

        Assert.Equal(2, uiState.LayerThumbnails.Count);
        Assert.True(uiState.LayerThumbnails[0].IsHideOption);
        Assert.Equal(20, uiState.LayerThumbnails[1].LayerId);
        Assert.DoesNotContain(uiState.LayerThumbnails, thumbnail => thumbnail.IsGroup);
    }

    [Fact]
    public void HideOptionClearsOnlyTheCurrentGroupDirectLayers()
    {
        var uiState = new PsdLayerManagementUiState();
        AddThumbnail(uiState, layerId: 101, parentLayerId: null, name: "Root A", isGroup: false, isDefaultVisible: false);
        AddThumbnail(uiState, layerId: 102, parentLayerId: null, name: "Root B", isGroup: false, isDefaultVisible: false);
        AddThumbnail(uiState, layerId: 20, parentLayerId: null, name: "Group", isGroup: true, isDefaultVisible: false);
        AddThumbnail(uiState, layerId: 201, parentLayerId: 20, name: "Nested A", isGroup: false, isDefaultVisible: false);
        AddThumbnail(uiState, layerId: 202, parentLayerId: 20, name: "Nested B", isGroup: false, isDefaultVisible: false);

        uiState.RefreshLayerThumbnailGrid();
        uiState.TryToggleLayer(uiState.LayerThumbnails.Single(item => item.LayerId == 101), isMultiSelect: false, out _);

        uiState.SelectGroup(20);
        uiState.RefreshLayerThumbnailGrid();
        uiState.TryToggleLayer(uiState.LayerThumbnails.Single(item => item.LayerId == 201), isMultiSelect: false, out _);
        uiState.TryToggleLayer(uiState.LayerThumbnails.Single(item => item.IsHideOption), isMultiSelect: false, out _);

        uiState.RefreshLayerThumbnailGrid();
        Assert.Contains(uiState.LayerThumbnails, item => item.IsHideOption && item.IsSelected);
        Assert.DoesNotContain(uiState.LayerThumbnails, item => !item.IsHideOption && item.IsVisible);

        uiState.SelectGroup(null);
        uiState.RefreshLayerThumbnailGrid();
        Assert.Contains(uiState.LayerThumbnails, item => item.LayerId == 101 && item.IsVisible);
    }

    [Fact]
    public void MultiVisibleGroupsUseEyeToggleInsteadOfExclusiveLayerSwitch()
    {
        var uiState = new PsdLayerManagementUiState();
        AddThumbnail(uiState, layerId: 101, parentLayerId: null, name: "Root A", isGroup: false, isDefaultVisible: true);
        AddThumbnail(uiState, layerId: 102, parentLayerId: null, name: "Root B", isGroup: false, isDefaultVisible: true);

        uiState.RefreshLayerThumbnailGrid();

        Assert.All(uiState.LayerThumbnails.Where(item => !item.IsHideOption), item =>
        {
            Assert.False(item.SupportsExclusiveSwitch);
            Assert.True(item.CanToggleVisibility);
            Assert.True(item.IsVisible);
        });

        var first = uiState.LayerThumbnails.Single(item => item.LayerId == 101);
        var toggled = uiState.TryToggleLayer(first, isMultiSelect: false, out var statusText);

        Assert.False(toggled);
        Assert.True(first.IsVisible);
        Assert.Contains("eye button", statusText, StringComparison.Ordinal);

        uiState.ToggleLayerVisibility(first);
        uiState.RefreshLayerThumbnailGrid();

        Assert.False(first.IsVisible);
        Assert.True(uiState.LayerThumbnails.Single(item => item.LayerId == 102).IsVisible);
    }

    [Fact]
    public void GroupTreeShowsLayerMatchTemplateRoles()
    {
        var uiState = new PsdLayerManagementUiState();
        AddThumbnail(uiState, layerId: 10, parentLayerId: null, name: "表情", isGroup: true);
        AddThumbnail(uiState, layerId: 11, parentLayerId: 10, name: "开心", isGroup: false);
        AddThumbnail(uiState, layerId: 20, parentLayerId: null, name: "动作", isGroup: true);
        AddThumbnail(uiState, layerId: 21, parentLayerId: 20, name: "抬手", isGroup: false);
        AddThumbnail(uiState, layerId: 30, parentLayerId: null, name: "左手", isGroup: true);
        AddThumbnail(uiState, layerId: 31, parentLayerId: 30, name: "伸手", isGroup: false);

        uiState.ApplyLayerMatchTemplate(new PsdLayerMatchConfiguration(
            ExpressionGroup: new PsdLayerGroupCatalog("表情", ["开心"]),
            ActionGroup: new PsdLayerGroupCatalog("动作", ["抬手"]),
            LeftHandGroup: new PsdLayerGroupCatalog("左手", ["伸手"])));

        Assert.Equal("整体表情", uiState.GroupTabs.Single(tab => tab.DisplayName == "表情").MatchRoleText);
        Assert.Equal("动作", uiState.GroupTabs.Single(tab => tab.DisplayName == "动作").MatchRoleText);
        Assert.Equal("左手", uiState.GroupTabs.Single(tab => tab.DisplayName == "左手").MatchRoleText);
        Assert.Contains("整体表情", uiState.LayerMatchTemplateSummary, StringComparison.Ordinal);
        Assert.Contains("动作", uiState.LayerMatchTemplateSummary, StringComparison.Ordinal);
        Assert.Contains("左手", uiState.LayerMatchTemplateSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectionHintWithControlledLayersKeepsUncontrolledDefaultVisibility()
    {
        var layers = new[]
        {
            Layer(1, null, "身体", isGroup: false, isVisible: true),
            Layer(10, null, "眉毛", isGroup: true, isVisible: false),
            Layer(11, 10, "平眉", isGroup: false, isVisible: true),
            Layer(12, 10, "皱眉", isGroup: false, isVisible: false),
            Layer(20, null, "装饰", isGroup: false, isVisible: true)
        };
        var snapshot = new PsdLayerCoreSnapshot(
            "test.psd",
            "fp",
            new PsdNativeDocumentInfo(1, 1, 8, 4, 3, layers),
            layers,
            new HashSet<int> { 1, 11, 20 },
            null,
            null,
            null);

        var selection = new PsdLayerSelectionResolver().ResolveSelectionHint(snapshot, new PsdLayerSelectionHint(
            [],
            ["皱眉"],
            "ai_structured",
            "fp",
            "cache",
            ["平眉", "皱眉"]));

        Assert.NotNull(selection);
        Assert.Contains(1, selection.VisibleLayerIds);
        Assert.DoesNotContain(11, selection.VisibleLayerIds);
        Assert.Contains(12, selection.VisibleLayerIds);
        Assert.Contains(20, selection.VisibleLayerIds);
        Assert.Contains(12, selection.SelectedLayerIds);
    }

    [Fact]
    public void SelectionHintUsesControlledIdsBeforeDuplicateControlledNames()
    {
        var layers = new[]
        {
            Layer(1, null, "身体", isGroup: false, isVisible: true),
            Layer(10, null, "左手", isGroup: true, isVisible: true),
            Layer(11, 10, "伸手", isGroup: true, isVisible: false),
            Layer(12, 11, "手掌", isGroup: false, isVisible: false),
            Layer(13, 10, "默认", isGroup: true, isVisible: true),
            Layer(14, 13, "手掌", isGroup: false, isVisible: true),
            Layer(20, null, "装饰手掌", isGroup: true, isVisible: true),
            Layer(21, 20, "手掌", isGroup: false, isVisible: true)
        };
        var snapshot = new PsdLayerCoreSnapshot(
            "test.psd",
            "fp",
            new PsdNativeDocumentInfo(1, 1, 8, 4, 3, layers),
            layers,
            new HashSet<int> { 1, 14, 21 },
            null,
            null,
            null);

        var selection = new PsdLayerSelectionResolver().ResolveSelectionHint(snapshot, new PsdLayerSelectionHint(
            [12],
            ["手掌"],
            "ai_structured",
            "fp",
            "cache",
            ["手掌"],
            [12, 14]));

        Assert.Contains(1, selection.VisibleLayerIds);
        Assert.Contains(12, selection.VisibleLayerIds);
        Assert.DoesNotContain(14, selection.VisibleLayerIds);
        Assert.Contains(21, selection.VisibleLayerIds);
        Assert.DoesNotContain(21, selection.ControlledLayerIds);
    }

    private static void AddThumbnail(
        PsdLayerManagementUiState uiState,
        int layerId,
        int? parentLayerId,
        string name,
        bool isGroup,
        bool isDefaultVisible = true)
    {
        uiState.AllThumbnails.Add(new PsdLayerThumbnailViewModel(
            layerId,
            parentLayerId,
            name,
            isGroup ? "Group" : "Layer",
            "0x0 @ 0,0",
            0,
            0,
            0,
            0,
            isGroup,
            isDefaultVisible));
    }

    private static PsdNativeLayerInfo Layer(
        int layerId,
        int? parentLayerId,
        string name,
        bool isGroup,
        bool isVisible)
    {
        return new PsdNativeLayerInfo(
            layerId,
            parentLayerId,
            name,
            0,
            isGroup,
            isVisible,
            0,
            0,
            isGroup ? 0 : 1,
            isGroup ? 0 : 1,
            isGroup ? 0 : 1,
            isGroup ? 0 : 1);
    }
}


