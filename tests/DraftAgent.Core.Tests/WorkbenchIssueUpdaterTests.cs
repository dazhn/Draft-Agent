using DraftAgent.Core;

namespace DraftAgent.Core.Tests;

public sealed class WorkbenchIssueUpdaterTests
{
    private readonly WorkbenchIssueUpdater _updater = new();

    [Fact]
    public void RefreshBindingIssuesKeepsManualIssuesAndReplacesGeneratedBindingIssues()
    {
        var manualIssue = new DraftIssue("issue_manual", IssueSeverity.Error, "script", "seg_001", "人工问题", "人工处理");
        var staleGeneratedIssue = new DraftIssue("issue_old", IssueSeverity.Warning, "character", "char_old", "旧角色 缺少音色绑定", "选择角色默认音色");
        var scene = Scene("scene_001", "雨夜", hasBackground: false);
        var character = Character("char_chen", "Chen", hasPortrait: false, hasVoice: false);

        var issues = _updater.RefreshBindingIssues(
            [manualIssue, staleGeneratedIssue],
            [scene],
            [character]);

        Assert.Contains(issues, issue => issue.Message == manualIssue.Message);
        Assert.DoesNotContain(issues, issue => issue.TargetId == staleGeneratedIssue.TargetId);
        var sceneIssue = Assert.Single(issues, issue => issue.TargetType == "scene" && issue.TargetId == scene.Id);
        Assert.Contains("未绑定场景素材", sceneIssue.Message);
        Assert.Equal("选择现有图片素材", sceneIssue.Suggestion);
        Assert.DoesNotContain("PSD", sceneIssue.Suggestion, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ComfyUI", sceneIssue.Suggestion, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(issues, issue => issue.TargetType == "character" && issue.TargetId == character.Id && issue.Message.Contains("缺少参考形象绑定"));
        Assert.Contains(issues, issue => issue.TargetType == "character" && issue.TargetId == character.Id && issue.Message.Contains("缺少音色绑定"));
        Assert.Equal(Enumerable.Range(1, issues.Count).Select(index => $"issue_{index}"), issues.Select(issue => issue.Id));
    }

    [Fact]
    public void RefreshBindingIssuesSkipsNarrationCharacter()
    {
        var narration = Character("char_narrator", "旁白", hasPortrait: false, hasVoice: false);

        var issues = _updater.RefreshBindingIssues([], [], [narration]);

        Assert.Empty(issues);
    }

    private static InteractionScene Scene(string id, string name, bool hasBackground)
    {
        return new InteractionScene(
            id,
            1,
            name,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            new SceneAssetBinding(hasBackground ? "scene.png" : null),
            []);
    }

    private static Character Character(string id, string name, bool hasPortrait, bool hasVoice)
    {
        return new Character(
            id,
            name,
            "角色",
            "appearance",
            "voice",
            "Calm",
            new AssetBinding(
                hasPortrait ? "portrait.png" : null,
                hasVoice ? "voice.wav" : null));
    }
}

