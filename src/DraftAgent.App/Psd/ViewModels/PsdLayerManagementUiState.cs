using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using DraftAgent.App.Psd.Services;
using DraftAgent.Core;
using PsdLayerLoader.Core;

namespace DraftAgent.App.Psd.ViewModels;

internal sealed class PsdLayerManagementUiState
{
    private const int RootGroupLayerId = -1;

    private readonly HashSet<int> _collapsedGroupIds = [];
    private readonly List<int?> _groupPath = [null];
    private PsdLayerMatchConfiguration? _layerMatchConfiguration;

    public List<PsdLayerThumbnailViewModel> AllThumbnails { get; } = [];

    public ObservableCollection<PsdDocumentTabViewModel> DocumentTabs { get; } = [];

    public ObservableCollection<PsdBrowserTabViewModel> GroupTabs { get; } = [];

    public ObservableCollection<PsdLayerThumbnailViewModel> LayerThumbnails { get; } = [];

    public ObservableCollection<PsdLayerRowViewModel> Layers { get; } = [];

    public PsdSelectionState SelectionState { get; private set; } = PsdLayerSelectionService.Empty("empty");

    public string BreadcrumbText { get; private set; } = "ROOT";

    public string LayerMatchTemplateSummary { get; private set; } = "未绑定 PSD 图层匹配模板";

    public int LayerCount => Layers.Count;

    public int VisibleLayerCount => AllThumbnails.Count(item => !item.IsGroup && item.IsVisible);

    public int SelectedLayerCount => SelectionState.SelectedLayerIds.Count;

    public int CurrentGroupItemCount => LayerThumbnails.Count(item => !item.IsHideOption);

    public int? CurrentGroupLayerId => _groupPath.Count == 0 ? null : _groupPath[^1];

    public void Reset(string documentId)
    {
        SelectionState = PsdLayerSelectionService.Empty(documentId);
        BreadcrumbText = "ROOT";
        _groupPath.Clear();
        _groupPath.Add(null);
        _collapsedGroupIds.Clear();
        AllThumbnails.Clear();
        DocumentTabs.Clear();
        GroupTabs.Clear();
        LayerThumbnails.Clear();
        Layers.Clear();
    }

    public void LoadDocument(PsdLayerCoreSnapshot state, string displayName, string documentSubtitle)
    {
        ArgumentNullException.ThrowIfNull(state);

        Reset(state.PsdPath);
        SelectionState = BuildSelectionState(state.PsdPath, state.DisplayLayers, state.VisibleLayerIds, new HashSet<int>());
        DocumentTabs.Add(new PsdDocumentTabViewModel(displayName, documentSubtitle) { IsActive = true });

        foreach (var layer in PsdLayerStackOrder.ForPanel(state.DisplayLayers, layer => layer.LayerId))
        {
            var layerName = string.IsNullOrWhiteSpace(layer.Name) ? $"Layer {layer.LayerId}" : layer.Name;
            var kind = layer.IsGroup ? "Group" : "Layer";
            var pixelBounds = $"{layer.Width}x{layer.Height} @ {layer.Left},{layer.Top}";

            Layers.Add(new PsdLayerRowViewModel(
                layer.LayerId,
                layerName,
                kind,
                layer.IsVisible,
                pixelBounds));

            AllThumbnails.Add(new PsdLayerThumbnailViewModel(
                layer.LayerId,
                layer.ParentLayerId,
                layerName,
                kind,
                pixelBounds,
                layer.Left,
                layer.Top,
                layer.Right,
                layer.Bottom,
                layer.IsGroup,
                layer.IsVisible));
        }

        ApplySelectionStateToThumbnails();
    }

    public void ApplyLayerMatchTemplate(PsdLayerMatchConfiguration? configuration)
    {
        _layerMatchConfiguration = configuration;
        LayerMatchTemplateSummary = BuildLayerMatchTemplateSummary(configuration);
        RebuildBrowserTabs();
    }

    public void RebuildBrowserTabs()
    {
        var currentGroupId = CurrentGroupLayerId;

        GroupTabs.Clear();
        AddGroupTab(null, "ROOT", CountChildren(null).ToString(), currentGroupId, 0);
        AddGroupChildren(null, currentGroupId, 1);
        UpdateBreadcrumb();
    }

    public IReadOnlyList<PsdLayerThumbnailViewModel> RefreshLayerThumbnailGrid()
    {
        var currentGroupId = CurrentGroupLayerId;
        var query = PsdLayerStackOrder.ForPanel(
            AllThumbnails.Where(item => item.ParentLayerId == currentGroupId && !item.IsGroup),
            item => item.LayerId);

        LayerThumbnails.Clear();
        var hideOption = PsdLayerThumbnailViewModel.CreateHideOption(currentGroupId);
        hideOption.IsSelected = IsCurrentGroupHidden();
        LayerThumbnails.Add(hideOption);

        var displayedItems = new List<PsdLayerThumbnailViewModel>();
        var supportsExclusiveSwitch = IsExclusiveSwitchGroup(currentGroupId ?? RootGroupLayerId);
        foreach (var item in query)
        {
            item.SupportsExclusiveSwitch = supportsExclusiveSwitch;
            LayerThumbnails.Add(item);
            displayedItems.Add(item);
        }

        return displayedItems;
    }

    public void SelectGroup(int? groupLayerId)
    {
        if (groupLayerId is null)
        {
            _groupPath.Clear();
            _groupPath.Add(null);
        }
        else
        {
            var existingIndex = _groupPath.IndexOf(groupLayerId);
            if (existingIndex >= 0)
            {
                _groupPath.RemoveRange(existingIndex + 1, _groupPath.Count - existingIndex - 1);
            }
            else
            {
                _groupPath.Add(groupLayerId);
            }
        }

        RebuildBrowserTabs();
    }

    public void NavigateUp()
    {
        if (_groupPath.Count <= 1)
        {
            return;
        }

        _groupPath.RemoveAt(_groupPath.Count - 1);
        RebuildBrowserTabs();
    }

    public void ToggleGroupExpanded(PsdBrowserTabViewModel tab)
    {
        if (!tab.HasChildren || tab.GroupLayerId is not { } groupLayerId)
        {
            return;
        }

        if (!_collapsedGroupIds.Add(groupLayerId))
        {
            _collapsedGroupIds.Remove(groupLayerId);
        }

        RebuildBrowserTabs();
    }

    public bool TryToggleLayer(
        PsdLayerThumbnailViewModel thumbnail,
        bool isMultiSelect,
        out string statusText)
    {
        statusText = thumbnail.Name;
        if (thumbnail.IsHideOption)
        {
            HideCurrentGroup();
            statusText = $"Hide {BreadcrumbText}";
            return true;
        }

        if (thumbnail.IsGroup)
        {
            SelectGroup(thumbnail.LayerId);
            statusText = BreadcrumbText;
            return true;
        }

        var currentGroupLayerId = CurrentGroupLayerId ?? RootGroupLayerId;
        if (!IsExclusiveSwitchGroup(currentGroupLayerId))
        {
            statusText = $"{thumbnail.Name}: use the eye button to toggle visibility.";
            return false;
        }

        SelectionState = PsdLayerSelectionService.ToggleLayer(
            SelectionState,
            currentGroupLayerId,
            thumbnail.LayerId,
            isMultiSelect);
        ApplySelectionStateToThumbnails();
        return true;
    }

    public void ToggleLayerVisibility(PsdLayerThumbnailViewModel thumbnail)
    {
        if (thumbnail.IsGroup || thumbnail.IsHideOption || thumbnail.SupportsExclusiveSwitch)
        {
            return;
        }

        thumbnail.IsVisible = !thumbnail.IsVisible;
        thumbnail.IsSelected = thumbnail.IsVisible;
        SetSelectionStateFromVisibleLayers(thumbnail.IsVisible ? [thumbnail.LayerId] : Array.Empty<int>());
    }

    public void SelectSingleMode()
    {
        NormalizeVisibleLayersForSwitchableGroups(SelectionState.SelectedLayerIds);
    }

    public void SelectMultiMode()
    {
        SetSelectionStateFromVisibleLayers(SelectionState.SelectedLayerIds);
    }

    public void ApplyExternalSelection(PsdSelectionState selectionState)
    {
        SelectionState = selectionState;
        ApplySelectionStateToThumbnails();
    }

    public void ApplyVisibleLayers(IEnumerable<int> visibleLayerIds, IEnumerable<int>? selectedLayerIds = null)
    {
        var visibleSet = visibleLayerIds.ToHashSet();
        var selectedSet = selectedLayerIds?.ToHashSet() ?? SelectionState.SelectedLayerIds.ToHashSet();
        SelectionState = BuildSelectionState(SelectionState.DocumentId, AllThumbnails, visibleSet, selectedSet);
        ApplySelectionStateToThumbnails();
    }

    public void CommitThumbnailVisibility(IEnumerable<int> selectedLayerIds)
    {
        SetSelectionStateFromVisibleLayers(selectedLayerIds);
    }

    private void AddGroupChildren(int? parentLayerId, int? selectedGroupLayerId, int depth)
    {
        var childGroups = AllThumbnails
            .Where(item => item.ParentLayerId == parentLayerId && item.IsGroup)
            .OrderByDescending(item => item.LayerId)
            .ToArray();

        foreach (var group in childGroups)
        {
            var hasChildren = HasChildGroups(group.LayerId);
            var isExpanded = !_collapsedGroupIds.Contains(group.LayerId);
            AddGroupTab(
                group.LayerId,
                group.Name,
                CountChildren(group.LayerId).ToString(),
                selectedGroupLayerId,
                depth,
                hasChildren,
                isExpanded);

            if (isExpanded)
            {
                AddGroupChildren(group.LayerId, selectedGroupLayerId, depth + 1);
            }
        }
    }

    private void AddGroupTab(
        int? groupLayerId,
        string displayName,
        string countSummary,
        int? selectedGroupLayerId,
        int depth,
        bool hasChildren = false,
        bool isExpanded = true)
    {
        var tab = new PsdBrowserTabViewModel(
            groupLayerId,
            displayName,
            countSummary,
            groupLayerId is null ? string.Empty : ResolveLayerMatchRole(displayName),
            depth,
            hasChildren,
            isExpanded)
        {
            IsSelected = groupLayerId == selectedGroupLayerId
        };
        GroupTabs.Add(tab);
    }

    private string ResolveLayerMatchRole(string groupName)
    {
        if (_layerMatchConfiguration is null || string.IsNullOrWhiteSpace(groupName))
        {
            return string.Empty;
        }

        var roles = new List<string>();
        AddRoleIfMatches(roles, _layerMatchConfiguration.ExpressionGroup, groupName, "整体表情");
        AddRoleIfMatches(roles, _layerMatchConfiguration.BrowGroup, groupName, "眉");
        AddRoleIfMatches(roles, _layerMatchConfiguration.EyeGroup, groupName, "眼");
        AddRoleIfMatches(roles, _layerMatchConfiguration.MouthGroup, groupName, "嘴");
        AddRoleIfMatches(roles, _layerMatchConfiguration.ActionGroup, groupName, "动作");
        AddRoleIfMatches(roles, _layerMatchConfiguration.LeftHandGroup, groupName, "左手");
        AddRoleIfMatches(roles, _layerMatchConfiguration.RightHandGroup, groupName, "右手");
        return string.Join(" / ", roles);
    }

    private static void AddRoleIfMatches(
        ICollection<string> roles,
        PsdLayerGroupCatalog? catalog,
        string groupName,
        string roleText)
    {
        if (catalog is not null &&
            string.Equals(catalog.GroupName, groupName, StringComparison.Ordinal))
        {
            roles.Add(roleText);
        }
    }

    private static string BuildLayerMatchTemplateSummary(PsdLayerMatchConfiguration? configuration)
    {
        if (configuration is not { HasAnyGroup: true })
        {
            return "未绑定 PSD 图层匹配模板";
        }

        var faceMode = configuration.ExpressionGroup is not null
            ? $"整体表情：{configuration.ExpressionGroup.GroupName}"
            : string.Join(" / ", new[]
                {
                    FormatGroup("眉", configuration.BrowGroup),
                    FormatGroup("眼", configuration.EyeGroup),
                    FormatGroup("嘴", configuration.MouthGroup)
                }
                .Where(text => !string.IsNullOrWhiteSpace(text)));
        if (string.IsNullOrWhiteSpace(faceMode))
        {
            faceMode = "面部未设置";
        }

        var action = string.Join(" / ", new[]
            {
                FormatGroup("动作", configuration.ActionGroup),
                FormatGroup("左手", configuration.LeftHandGroup),
                FormatGroup("右手", configuration.RightHandGroup)
            }
            .Where(text => !string.IsNullOrWhiteSpace(text)));
        return string.IsNullOrWhiteSpace(action)
            ? faceMode
            : $"{faceMode}；{action}";
    }

    private static string FormatGroup(string label, PsdLayerGroupCatalog? group)
    {
        return group is null || string.IsNullOrWhiteSpace(group.GroupName)
            ? string.Empty
            : $"{label}：{group.GroupName}";
    }

    private bool HasChildGroups(int? groupLayerId)
    {
        return AllThumbnails.Any(item => item.ParentLayerId == groupLayerId && item.IsGroup);
    }

    private int CountChildren(int? groupLayerId)
    {
        return AllThumbnails.Count(item => item.ParentLayerId == groupLayerId);
    }

    private void UpdateBreadcrumb()
    {
        var names = _groupPath
            .Skip(1)
            .Select(id => AllThumbnails.FirstOrDefault(item => item.LayerId == id)?.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name));

        BreadcrumbText = "ROOT" + string.Concat(names.Select(name => $" / {name}"));
    }

    private void ApplySelectionStateToThumbnails()
    {
        foreach (var thumbnail in AllThumbnails)
        {
            thumbnail.IsSelected = SelectionState.SelectedLayerIds.Contains(thumbnail.LayerId);
            if (thumbnail.IsGroup)
            {
                continue;
            }

            var groupLayerId = thumbnail.ParentLayerId ?? RootGroupLayerId;
            thumbnail.IsVisible = SelectionState.GroupSelections.TryGetValue(groupLayerId, out var groupSelection)
                ? groupSelection.VisibleChildLayerIds.Contains(thumbnail.LayerId)
                : thumbnail.IsDefaultVisible;
        }

        foreach (var thumbnail in LayerThumbnails.Where(item => item.IsHideOption))
        {
            thumbnail.IsSelected = IsGroupHidden(thumbnail.ParentLayerId);
            thumbnail.IsVisible = false;
        }
    }

    private void HideCurrentGroup()
    {
        var currentGroupLayerId = CurrentGroupLayerId ?? RootGroupLayerId;
        SelectionState = PsdLayerSelectionService.HideGroup(SelectionState, currentGroupLayerId);

        foreach (var layer in AllThumbnails.Where(item => !item.IsGroup && (item.ParentLayerId ?? RootGroupLayerId) == currentGroupLayerId))
        {
            layer.IsVisible = false;
            layer.IsSelected = false;
        }
    }

    private bool IsCurrentGroupHidden()
    {
        return IsGroupHidden(CurrentGroupLayerId);
    }

    private bool IsGroupHidden(int? groupLayerId)
    {
        var normalizedGroupLayerId = groupLayerId ?? RootGroupLayerId;
        var directLayerIds = AllThumbnails
            .Where(item => !item.IsGroup && (item.ParentLayerId ?? RootGroupLayerId) == normalizedGroupLayerId)
            .Select(item => item.LayerId)
            .ToHashSet();

        return directLayerIds.Count > 0 &&
            !SelectionState.VisibleLayerIds.Any(directLayerIds.Contains);
    }

    private void NormalizeVisibleLayersForSwitchableGroups(IEnumerable<int> selectedLayerIds)
    {
        var selectedSet = selectedLayerIds.ToHashSet();
        foreach (var group in AllThumbnails.Where(item => !item.IsGroup).GroupBy(item => item.ParentLayerId))
        {
            var groupLayerId = group.Key ?? RootGroupLayerId;
            if (!IsExclusiveSwitchGroup(groupLayerId))
            {
                continue;
            }

            var selectedVisibleLayer = group.FirstOrDefault(item => selectedSet.Contains(item.LayerId) && item.IsVisible);
            var firstVisibleLayer = group.FirstOrDefault(item => item.IsVisible);
            var layerToKeep = selectedVisibleLayer ?? firstVisibleLayer;

            foreach (var layer in group)
            {
                layer.IsVisible = layerToKeep is not null && layer.LayerId == layerToKeep.LayerId;
            }
        }

        SetSelectionStateFromVisibleLayers(selectedSet);
    }

    private void SetSelectionStateFromVisibleLayers(IEnumerable<int> selectedLayerIds)
    {
        var selectedSet = selectedLayerIds.ToHashSet();
        var visibleLayers = AllThumbnails
            .Where(item => !item.IsGroup && item.IsVisible)
            .ToArray();
        var visibleLayerIds = visibleLayers
            .Select(item => item.LayerId)
            .ToHashSet();
        var nextSelectedLayerIds = selectedSet
            .Where(visibleLayerIds.Contains)
            .ToHashSet();
        SelectionState = BuildSelectionState(SelectionState.DocumentId, AllThumbnails, visibleLayerIds, nextSelectedLayerIds);
    }

    private bool IsExclusiveSwitchGroup(int? groupLayerId)
    {
        var normalizedGroupLayerId = groupLayerId ?? RootGroupLayerId;
        var directLayers = AllThumbnails
            .Where(item => !item.IsGroup && !item.IsHideOption && (item.ParentLayerId ?? RootGroupLayerId) == normalizedGroupLayerId)
            .ToArray();
        return directLayers.Length > 0 &&
            directLayers.Count(item => item.IsDefaultVisible) <= 1;
    }

    private static PsdSelectionState BuildSelectionState(
        string documentId,
        IReadOnlyList<PsdNativeLayerInfo> layers,
        IReadOnlySet<int> visibleLayerIds,
        IReadOnlySet<int> selectedLayerIds)
    {
        var groupSelections = PsdLayerOperationMapper.BuildGroupSelections(layers, visibleLayerIds);
        return new PsdSelectionState(
            documentId,
            groupSelections,
            visibleLayerIds.ToHashSet(),
            selectedLayerIds.Where(visibleLayerIds.Contains).ToHashSet(),
            null);
    }

    private static PsdSelectionState BuildSelectionState(
        string documentId,
        IReadOnlyList<PsdLayerThumbnailViewModel> thumbnails,
        IReadOnlySet<int> visibleLayerIds,
        IReadOnlySet<int> selectedLayerIds)
    {
        var groupSelections = thumbnails
            .Where(item => !item.IsGroup && visibleLayerIds.Contains(item.LayerId))
            .GroupBy(item => item.ParentLayerId ?? RootGroupLayerId)
            .ToDictionary(
                group => group.Key,
                group => new PsdLayerGroupSelection(
                    group.Key,
                    true,
                    group.Select(item => item.LayerId).ToHashSet()));
        return new PsdSelectionState(
            documentId,
            groupSelections,
            visibleLayerIds.ToHashSet(),
            selectedLayerIds.Where(visibleLayerIds.Contains).ToHashSet(),
            null);
    }
}
