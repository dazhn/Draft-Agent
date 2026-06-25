namespace DraftAgent.Core;

public sealed class WorkbenchIssueUpdater
{
    private const string NarrationName = "旁白";

    public IReadOnlyList<DraftIssue> RefreshBindingIssues(
        IReadOnlyList<DraftIssue> existingIssues,
        IReadOnlyList<InteractionScene> scenes,
        IReadOnlyList<Character> characters)
    {
        var issues = existingIssues
            .Where(issue => !IsGeneratedBindingIssue(issue))
            .ToList();
        var existingKeys = new HashSet<string>(
            issues.Select(issue => $"{issue.TargetType}:{issue.TargetId}:{issue.Message}"),
            StringComparer.Ordinal);

        foreach (var scene in scenes.Where(scene => scene.AssetBinding.BackgroundImage is null && scene.AssetBinding.VideoClip is null))
        {
            AddIssueIfMissing(issues, existingKeys, IssueSeverity.Info, "scene", scene.Id, $"{scene.Name} 未绑定场景素材", "选择现有图片素材");
        }

        foreach (var character in characters.Where(character => character.Name != NarrationName))
        {
            if (character.AssetBinding.Portrait is null)
            {
                AddIssueIfMissing(issues, existingKeys, IssueSeverity.Warning, "character", character.Id, $"{character.Name} 缺少参考形象绑定", "为角色绑定参考图或演员素材");
            }

            if (character.AssetBinding.Voice is null)
            {
                AddIssueIfMissing(issues, existingKeys, IssueSeverity.Warning, "character", character.Id, $"{character.Name} 缺少音色绑定", "选择角色默认音色");
            }
        }

        return issues
            .Select((issue, index) => issue with { Id = $"issue_{index + 1}" })
            .ToArray();
    }

    private static bool IsGeneratedBindingIssue(DraftIssue issue)
    {
        return (issue.TargetType == "scene" && issue.Message.Contains("未绑定场景素材", StringComparison.Ordinal)) ||
               (issue.TargetType == "character" && issue.Message.Contains("缺少参考形象绑定", StringComparison.Ordinal)) ||
               (issue.TargetType == "character" && issue.Message.Contains("缺少音色绑定", StringComparison.Ordinal));
    }

    private static void AddIssueIfMissing(
        List<DraftIssue> issues,
        HashSet<string> existingKeys,
        IssueSeverity severity,
        string targetType,
        string targetId,
        string message,
        string suggestion)
    {
        var key = $"{targetType}:{targetId}:{message}";
        if (!existingKeys.Add(key))
        {
            return;
        }

        issues.Add(new DraftIssue($"issue_{issues.Count + 1}", severity, targetType, targetId, message, suggestion));
    }
}
