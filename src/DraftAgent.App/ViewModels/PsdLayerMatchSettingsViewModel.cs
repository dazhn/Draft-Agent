using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using DraftAgent.Core;

namespace DraftAgent.App.ViewModels;

public sealed partial class PsdLayerMatchSettingsViewModel : ObservableObject
{
    private readonly Dictionary<PsdLayerTemplateRole, PsdLayerGroupOptionViewModel> _bindingByRole = [];

    [ObservableProperty]
    private PsdLayerGroupTreeNodeViewModel? _selectedPsdGroup;

    [ObservableProperty]
    private string _statusText = "先在左侧选择 PSD 图层组，再点击右侧模板节点完成绑定。";

    public PsdLayerMatchSettingsViewModel(
        string characterName,
        IReadOnlyList<PsdLayerGroupOptionViewModel> groups,
        PsdLayerMatchConfiguration? currentSettings = null)
    {
        CharacterName = characterName;
        foreach (var node in BuildPsdGroupTree(groups))
        {
            PsdGroupTree.Add(node);
        }

        foreach (var node in BuildTemplateTree())
        {
            TemplateTree.Add(node);
        }

        RestoreBinding(PsdLayerTemplateRole.Expression, currentSettings?.ExpressionGroup);
        if (!_bindingByRole.ContainsKey(PsdLayerTemplateRole.Expression))
        {
            RestoreBinding(PsdLayerTemplateRole.Brow, currentSettings?.BrowGroup);
            RestoreBinding(PsdLayerTemplateRole.Eye, currentSettings?.EyeGroup);
            RestoreBinding(PsdLayerTemplateRole.Mouth, currentSettings?.MouthGroup);
        }

        RestoreBinding(PsdLayerTemplateRole.Action, currentSettings?.ActionGroup);
        RestoreBinding(PsdLayerTemplateRole.LeftHand, currentSettings?.LeftHandGroup);
        RestoreBinding(PsdLayerTemplateRole.RightHand, currentSettings?.RightHandGroup);
        RefreshTemplateBindings();
    }

    public string CharacterName { get; }

    public ObservableCollection<PsdLayerGroupTreeNodeViewModel> PsdGroupTree { get; } = [];

    public ObservableCollection<PsdLayerTemplateTreeNodeViewModel> TemplateTree { get; } = [];

    public string SummaryText =>
        "左侧只显示 PSD 图层组。右侧是模板语义树：表情可绑定上层整体表情，或拆分绑定眉/眼/嘴；两者互斥。动作可绑定上层动作，也可补充左手/右手。";

    public string FaceModeText => _bindingByRole.ContainsKey(PsdLayerTemplateRole.Expression)
        ? "当前：整体表情模板，眉/眼/嘴已禁用"
        : HasFacialParts
            ? "当前：眉眼嘴拆分模板"
            : "当前：未设置表情模板";

    private bool HasFacialParts =>
        _bindingByRole.ContainsKey(PsdLayerTemplateRole.Brow) ||
        _bindingByRole.ContainsKey(PsdLayerTemplateRole.Eye) ||
        _bindingByRole.ContainsKey(PsdLayerTemplateRole.Mouth);

    public void SelectPsdGroup(PsdLayerGroupTreeNodeViewModel node)
    {
        if (!node.CanBind)
        {
            StatusText = $"“{node.Path}”只是结构节点，请选择包含图层的 PSD 图层组。";
            return;
        }

        ClearPsdGroupSelection(PsdGroupTree);
        node.IsSelected = true;
        SelectedPsdGroup = node;
        StatusText = $"已选择 PSD 图层组：{node.Path}";
    }

    public void ApplySelectedGroupToTemplate(PsdLayerTemplateTreeNodeViewModel node)
    {
        if (node.Role == PsdLayerTemplateRole.None)
        {
            StatusText = "请选择右侧具体模板节点。";
            return;
        }

        if (SelectedPsdGroup is null)
        {
            StatusText = "请先在左侧选择一个 PSD 图层组。";
            return;
        }

        _bindingByRole[node.Role] = SelectedPsdGroup.Group;
        if (node.Role == PsdLayerTemplateRole.Expression)
        {
            _bindingByRole.Remove(PsdLayerTemplateRole.Brow);
            _bindingByRole.Remove(PsdLayerTemplateRole.Eye);
            _bindingByRole.Remove(PsdLayerTemplateRole.Mouth);
        }
        else if (node.Role is PsdLayerTemplateRole.Brow or PsdLayerTemplateRole.Eye or PsdLayerTemplateRole.Mouth)
        {
            _bindingByRole.Remove(PsdLayerTemplateRole.Expression);
        }

        RefreshTemplateBindings();
        StatusText = $"已将 {SelectedPsdGroup.Path} 绑定到 {node.DisplayName}。";
    }

    public PsdLayerMatchConfiguration BuildConfiguration()
    {
        return new PsdLayerMatchConfiguration(
            ExpressionGroup: ToCatalog(PsdLayerTemplateRole.Expression),
            BrowGroup: _bindingByRole.ContainsKey(PsdLayerTemplateRole.Expression) ? null : ToCatalog(PsdLayerTemplateRole.Brow),
            EyeGroup: _bindingByRole.ContainsKey(PsdLayerTemplateRole.Expression) ? null : ToCatalog(PsdLayerTemplateRole.Eye),
            MouthGroup: _bindingByRole.ContainsKey(PsdLayerTemplateRole.Expression) ? null : ToCatalog(PsdLayerTemplateRole.Mouth),
            ActionGroup: ToCatalog(PsdLayerTemplateRole.Action),
            LeftHandGroup: ToCatalog(PsdLayerTemplateRole.LeftHand),
            RightHandGroup: ToCatalog(PsdLayerTemplateRole.RightHand));
    }

    private PsdLayerGroupCatalog? ToCatalog(PsdLayerTemplateRole role)
    {
        return _bindingByRole.TryGetValue(role, out var group)
            ? group.ToCatalog()
            : null;
    }

    private void RestoreBinding(PsdLayerTemplateRole role, PsdLayerGroupCatalog? catalog)
    {
        if (catalog is null || string.IsNullOrWhiteSpace(catalog.GroupName))
        {
            return;
        }

        var group = FlattenPsdGroups(PsdGroupTree)
            .FirstOrDefault(node => node.Group.CanBind &&
                string.Equals(node.Group.Name, catalog.GroupName, StringComparison.Ordinal))
            ?.Group;
        if (group is not null)
        {
            _bindingByRole[role] = group;
        }
    }

    private void RefreshTemplateBindings()
    {
        var expressionBound = _bindingByRole.ContainsKey(PsdLayerTemplateRole.Expression);
        foreach (var node in FlattenTemplateNodes(TemplateTree))
        {
            node.BoundGroupName = _bindingByRole.TryGetValue(node.Role, out var group) ? group.Name : string.Empty;
            node.IsDisabledByExpression = expressionBound &&
                node.Role is PsdLayerTemplateRole.Brow or PsdLayerTemplateRole.Eye or PsdLayerTemplateRole.Mouth;
            if (node.IsDisabledByExpression)
            {
                node.BoundGroupName = string.Empty;
            }
        }

        OnPropertyChanged(nameof(FaceModeText));
    }

    private static void ClearPsdGroupSelection(IEnumerable<PsdLayerGroupTreeNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            node.IsSelected = false;
            ClearPsdGroupSelection(node.Children);
        }
    }

    private static IReadOnlyList<PsdLayerGroupTreeNodeViewModel> BuildPsdGroupTree(
        IReadOnlyList<PsdLayerGroupOptionViewModel> groups)
    {
        var roots = new List<PsdLayerGroupTreeNodeViewModel>();
        var nodeByPath = new Dictionary<string, PsdLayerGroupTreeNodeViewModel>(StringComparer.Ordinal);
        foreach (var group in groups.OrderBy(group => group.Path, StringComparer.Ordinal))
        {
            var segments = group.Path
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            ICollection<PsdLayerGroupTreeNodeViewModel> parentCollection = roots;
            var currentPath = string.Empty;
            PsdLayerGroupTreeNodeViewModel? node = null;
            for (var index = 0; index < segments.Length; index++)
            {
                currentPath = string.IsNullOrWhiteSpace(currentPath)
                    ? segments[index]
                    : $"{currentPath}/{segments[index]}";
                if (!nodeByPath.TryGetValue(currentPath, out node))
                {
                    var sourceGroup = index == segments.Length - 1
                        ? group
                        : PsdLayerGroupOptionViewModel.CreateVirtualGroup(segments[index], currentPath);
                    node = new PsdLayerGroupTreeNodeViewModel(sourceGroup, index);
                    nodeByPath[currentPath] = node;
                    parentCollection.Add(node);
                }

                parentCollection = node.Children;
            }

            if (segments.Length == 0)
            {
                roots.Add(new PsdLayerGroupTreeNodeViewModel(group, 0));
            }
        }

        return roots;
    }

    private static IReadOnlyList<PsdLayerTemplateTreeNodeViewModel> BuildTemplateTree()
    {
        return
        [
            new PsdLayerTemplateTreeNodeViewModel("表情", PsdLayerTemplateRole.Expression)
            {
                Children =
                {
                    new PsdLayerTemplateTreeNodeViewModel("眉", PsdLayerTemplateRole.Brow, 1),
                    new PsdLayerTemplateTreeNodeViewModel("眼", PsdLayerTemplateRole.Eye, 1),
                    new PsdLayerTemplateTreeNodeViewModel("嘴", PsdLayerTemplateRole.Mouth, 1)
                }
            },
            new PsdLayerTemplateTreeNodeViewModel("动作", PsdLayerTemplateRole.Action)
            {
                Children =
                {
                    new PsdLayerTemplateTreeNodeViewModel("左手", PsdLayerTemplateRole.LeftHand, 1),
                    new PsdLayerTemplateTreeNodeViewModel("右手", PsdLayerTemplateRole.RightHand, 1)
                }
            }
        ];
    }

    private static IEnumerable<PsdLayerGroupTreeNodeViewModel> FlattenPsdGroups(IEnumerable<PsdLayerGroupTreeNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in FlattenPsdGroups(node.Children))
            {
                yield return child;
            }
        }
    }

    private static IEnumerable<PsdLayerTemplateTreeNodeViewModel> FlattenTemplateNodes(IEnumerable<PsdLayerTemplateTreeNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in FlattenTemplateNodes(node.Children))
            {
                yield return child;
            }
        }
    }
}

public sealed partial class PsdLayerGroupTreeNodeViewModel : ObservableObject
{
    public PsdLayerGroupTreeNodeViewModel(PsdLayerGroupOptionViewModel group, int depth)
    {
        Group = group;
        Depth = depth;
    }

    public PsdLayerGroupOptionViewModel Group { get; }

    public string DisplayName => Group.Name;

    public string Path => Group.Path;

    public int Depth { get; }

    public double IndentWidth => Depth * 18.0;

    public ObservableCollection<PsdLayerGroupTreeNodeViewModel> Children { get; } = [];

    public string CountSummary => Group.LayerNames.Count > 0 ? $"{Group.LayerNames.Count} 层" : string.Empty;

    public bool CanBind => Group.CanBind;

    [ObservableProperty]
    private bool _isSelected;
}

public sealed partial class PsdLayerTemplateTreeNodeViewModel : ObservableObject
{
    public PsdLayerTemplateTreeNodeViewModel(
        string displayName,
        PsdLayerTemplateRole role,
        int depth = 0)
    {
        DisplayName = displayName;
        Role = role;
        Depth = depth;
    }

    public string DisplayName { get; }

    public PsdLayerTemplateRole Role { get; }

    public int Depth { get; }

    public double IndentWidth => Depth * 18.0;

    public ObservableCollection<PsdLayerTemplateTreeNodeViewModel> Children { get; init; } = [];

    public bool HasBinding => !string.IsNullOrWhiteSpace(BoundGroupName);

    public bool CanApply => Role != PsdLayerTemplateRole.None && !IsDisabledByExpression;

    public string BindingText => HasBinding ? $"← {BoundGroupName}" : string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBinding))]
    [NotifyPropertyChangedFor(nameof(BindingText))]
    private string _boundGroupName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    private bool _isDisabledByExpression;
}

public enum PsdLayerTemplateRole
{
    None,
    Expression,
    Brow,
    Eye,
    Mouth,
    Action,
    LeftHand,
    RightHand
}

public sealed record PsdLayerGroupOptionViewModel(
    int LayerId,
    string Name,
    string Path,
    IReadOnlyList<string> LayerNames,
    bool IsVirtual = false)
{
    public bool CanBind => !IsVirtual && LayerNames.Count > 0;

    public string DisplayText => string.IsNullOrWhiteSpace(Path)
        ? $"{Name}（{LayerNames.Count} 层）"
        : $"{Path}（{LayerNames.Count} 层）";

    public bool IsExpressionCandidate => IsExpressionName(Name) || IsExpressionName(Path);

    public bool IsBrowCandidate => ContainsAny(Name, ["眉", "眉毛", "brow", "eyebrow"]) ||
        ContainsAny(Path, ["眉", "眉毛", "brow", "eyebrow"]);

    public bool IsEyeCandidate => ContainsAny(Name, ["眼", "眼睛", "eye", "eyes"]) ||
        ContainsAny(Path, ["眼", "眼睛", "eye", "eyes"]);

    public bool IsMouthCandidate => ContainsAny(Name, ["嘴", "嘴巴", "口", "mouth", "lip", "lips"]) ||
        ContainsAny(Path, ["嘴", "嘴巴", "口", "mouth", "lip", "lips"]);

    public bool IsActionCandidate => IsActionName(Name) || IsActionName(Path);

    public PsdLayerGroupCatalog ToCatalog()
    {
        if (!CanBind)
        {
            throw new InvalidOperationException("虚拟 PSD 结构节点不能保存为图层模板。");
        }

        return new PsdLayerGroupCatalog(
            Name,
            LayerNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray());
    }

    public static PsdLayerGroupOptionViewModel CreateVirtualGroup(string name, string path)
    {
        return new PsdLayerGroupOptionViewModel(-1, name, path, [], true);
    }

    private static bool IsExpressionName(string? value)
    {
        return ContainsAny(value, ["表情", "眉", "眉毛", "眼", "眼睛", "嘴", "嘴巴", "expression", "emotion", "face", "eye", "mouth"]);
    }

    private static bool IsActionName(string? value)
    {
        return ContainsAny(value, ["动作", "姿势", "身体", "手", "胳膊", "pose", "action", "body", "hand", "arm"]);
    }

    private static bool ContainsAny(string? value, IReadOnlyList<string> tokens)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = Normalize(value);
        return tokens.Any(token => normalized.Contains(Normalize(token), StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string value)
    {
        return string.Concat(value
            .Where(character => !char.IsWhiteSpace(character) && character is not '/' and not '\\' and not '_' and not '-' and not '：' and not ':'))
            .ToLowerInvariant();
    }
}
