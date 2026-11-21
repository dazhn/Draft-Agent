using DraftAgent.App.ViewModels;
using DraftAgent.Core;

namespace DraftAgent.App.Tests;

public sealed class ExportPreflightIssueViewModelTests
{
    [Fact]
    public void LocationTextUsesDisplayLabelsInsteadOfInternalIds()
    {
        var issue = new ExportPreflightIssueViewModel(
            1,
            EditingTimelinePreflightIssueKind.MissingAudio,
            "internal message scene=scene_001; interaction=int_002; task=tts_secret",
            "scene_001",
            "int_002",
            "asset_secret",
            "tts_secret",
            GenerationTaskStatus.Completed,
            "Regenerate audio")
        {
            DisplaySceneLabel = "第 1 场 雨夜街口",
            DisplayRowLabel = "2"
        };

        Assert.Equal("第 1 场 雨夜街口 / 第 2 行", issue.LocationText);
        Assert.Equal("第 1 场 雨夜街口 / 第 2 行", issue.CompactLocationText);
        Assert.Equal("语音文件缺失", issue.SummaryText);
        Assert.Equal("请重新同步或重新生成该行语音后再导出。", issue.DetailText);
        Assert.Equal("重新生成语音", issue.ActionText);
        Assert.DoesNotContain("scene_001", issue.LocationText, StringComparison.Ordinal);
        Assert.DoesNotContain("int_002", issue.LocationText, StringComparison.Ordinal);
        Assert.DoesNotContain("tts_secret", issue.LocationText, StringComparison.Ordinal);
        Assert.DoesNotContain("asset_secret", issue.LocationText, StringComparison.Ordinal);
        AssertUserFacingTextsDoNotContainInternalIds(issue, "scene_001", "int_002", "tts_secret", "asset_secret");
    }

    [Fact]
    public void LocationTextUsesGenericLabelsWhenDisplayLabelsAreMissing()
    {
        var issue = new ExportPreflightIssueViewModel(
            1,
            EditingTimelinePreflightIssueKind.PsdReference,
            "internal message scene=scene_internal; asset=asset_secret",
            "scene_internal",
            "interaction_internal",
            "asset_secret",
            string.Empty,
            null,
            "Render PSD");

        Assert.Equal("场景 / 当前行", issue.LocationText);
        Assert.Equal("场景 / 当前行", issue.CompactLocationText);
        Assert.Equal("请先渲染 PSD 图层为图片后再导出。", issue.DetailText);
        Assert.Equal("渲染 PSD", issue.ActionText);
        Assert.DoesNotContain("scene_internal", issue.LocationText, StringComparison.Ordinal);
        Assert.DoesNotContain("interaction_internal", issue.LocationText, StringComparison.Ordinal);
        Assert.DoesNotContain("asset_secret", issue.LocationText, StringComparison.Ordinal);
        AssertUserFacingTextsDoNotContainInternalIds(issue, "scene_internal", "interaction_internal", "asset_secret");
    }

    [Fact]
    public void SceneLevelPsdIssueCanLocateButCannotAutoRepair()
    {
        var issue = new ExportPreflightIssueViewModel(
            1,
            EditingTimelinePreflightIssueKind.PsdReference,
            "internal message scene=scene_internal; asset=asset_secret",
            "scene_internal",
            string.Empty,
            "asset_secret",
            string.Empty,
            null,
            "Render PSD");

        Assert.True(issue.CanLocate);
        Assert.False(issue.CanRepair);
        Assert.Equal("请定位到对应绑定后改用普通图片。", issue.DetailText);
        Assert.Equal("定位", issue.RepairText);
        Assert.Equal("定位处理", issue.ActionText);
        AssertUserFacingTextsDoNotContainInternalIds(issue, "scene_internal", "asset_secret");
    }

    [Fact]
    public void RowLevelPsdIssueCanAutoRepair()
    {
        var issue = new ExportPreflightIssueViewModel(
            1,
            EditingTimelinePreflightIssueKind.PsdReference,
            "internal message scene=scene_internal; interaction=int_internal; asset=asset_secret",
            "scene_internal",
            "int_internal",
            "asset_secret",
            string.Empty,
            null,
            "Render PSD");

        Assert.True(issue.CanLocate);
        Assert.True(issue.CanRepair);
        Assert.Equal("请先渲染 PSD 图层为图片后再导出。", issue.DetailText);
        Assert.Equal("渲染", issue.RepairText);
        Assert.Equal("渲染 PSD", issue.ActionText);
        AssertUserFacingTextsDoNotContainInternalIds(issue, "scene_internal", "int_internal", "asset_secret");
    }

    [Fact]
    public void MissingVisualUsesUserFacingSummaryWithoutInternalIds()
    {
        var issue = new ExportPreflightIssueViewModel(
            1,
            EditingTimelinePreflightIssueKind.MissingVisual,
            "internal message scene=scene_internal; interaction=int_internal; asset=asset_secret",
            "scene_internal",
            "int_internal",
            "asset_secret",
            string.Empty,
            null,
            "Bind image")
        {
            DisplaySceneLabel = "第 1 场",
            DisplayRowLabel = "3"
        };

        Assert.Equal("图片文件缺失", issue.SummaryText);
        Assert.Equal("定位", issue.RepairText);
        Assert.Equal("重新绑定图片", issue.ActionText);
        Assert.False(issue.CanRepair);
        Assert.Equal("第 1 场 / 第 3 行", issue.LocationText);
        Assert.DoesNotContain("scene_internal", issue.LocationText, StringComparison.Ordinal);
        Assert.DoesNotContain("int_internal", issue.LocationText, StringComparison.Ordinal);
        Assert.DoesNotContain("asset_secret", issue.LocationText, StringComparison.Ordinal);
        AssertUserFacingTextsDoNotContainInternalIds(issue, "scene_internal", "int_internal", "asset_secret");
    }

    [Fact]
    public void InternalMessageKeepsDiagnosticsOutOfUserFacingTexts()
    {
        var issue = new ExportPreflightIssueViewModel(
            7,
            EditingTimelinePreflightIssueKind.MissingAudio,
            "internal message scene=scene_hidden; interaction=int_hidden; task=task_hidden; asset=asset_hidden",
            "scene_hidden",
            "int_hidden",
            "asset_hidden",
            "task_hidden",
            GenerationTaskStatus.Completed,
            "Regenerate audio for task_hidden using asset_hidden");

        Assert.Contains("scene_hidden", issue.InternalMessage, StringComparison.Ordinal);
        Assert.Contains("task_hidden", issue.InternalSuggestedAction, StringComparison.Ordinal);
        AssertUserFacingTextsDoNotContainInternalIds(
            issue,
            "scene_hidden",
            "int_hidden",
            "task_hidden",
            "asset_hidden");
    }

    [Fact]
    public void UnsupportedAudioUsesFormatGuidanceWithoutInternalIds()
    {
        var issue = new ExportPreflightIssueViewModel(
            8,
            EditingTimelinePreflightIssueKind.UnsupportedAudio,
            "internal message scene=scene_hidden; interaction=int_hidden; task=task_hidden; asset=asset_hidden.mp3",
            "scene_hidden",
            "int_hidden",
            "asset_hidden.mp3",
            "task_hidden",
            GenerationTaskStatus.Completed,
            "Convert asset_hidden.mp3 to WAV/FLAC")
        {
            DisplaySceneLabel = "第 2 场",
            DisplayRowLabel = "5"
        };

        Assert.Equal("语音格式不支持", issue.SummaryText);
        Assert.Equal("请转换为 WAV/FLAC，或补齐该格式真实时长读取器后再导出。", issue.DetailText);
        Assert.Equal("转换音频", issue.ActionText);
        Assert.Equal("第 2 场 / 第 5 行", issue.LocationText);
        AssertUserFacingTextsDoNotContainInternalIds(
            issue,
            "scene_hidden",
            "int_hidden",
            "task_hidden",
            "asset_hidden");
    }

    private static void AssertUserFacingTextsDoNotContainInternalIds(
        ExportPreflightIssueViewModel issue,
        params string[] internalFragments)
    {
        var userFacingTexts = new[]
        {
            issue.SummaryText,
            issue.DetailText,
            issue.LocationText,
            issue.CompactLocationText,
            issue.RepairText,
            issue.ActionText
        };

        foreach (var text in userFacingTexts)
        {
            foreach (var fragment in internalFragments)
            {
                Assert.DoesNotContain(fragment, text, StringComparison.Ordinal);
            }
        }
    }
}

