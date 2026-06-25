using System.Text.Json;
using DraftAgent.Core;

namespace DraftAgent.Core.Tests;

public sealed class DraftAnalyzerTests
{
    private readonly DraftAnalyzer _analyzer = new();

    [Fact]
    public void Preprocess_CleansAndSegmentsPastedCopy()
    {
        var script = _analyzer.Preprocess("林安站在雨里。  \n\n陈默说：对不起。\n林安抬头看着他：你从来没有相信过我。");

        Assert.Equal("林安站在雨里。\n陈默说：对不起。\n林安抬头看着他：你从来没有相信过我。", script.CleanText);
        Assert.Equal(3, script.Segments.Count);
        Assert.Equal(SegmentType.Dialogue, script.Segments[1].Type);
        Assert.Equal(SegmentType.Dialogue, script.Segments[2].Type);
    }

    [Fact]
    public void Analyze_GeneratesStructuredDraftEntities()
    {
        var draft = _analyzer.Analyze(DraftAnalyzer.SampleScript);

        Assert.Contains(draft.Characters, character => character.Name == "林安");
        Assert.Contains(draft.Characters, character => character.Name == "陈默");
        Assert.DoesNotContain(draft.Characters, character => character.Name is "于从街角" or "喘着气" or "林安抬头" or "林安低声" or "这句" or "想伸手又" or "了两个人");
        Assert.Contains(draft.Scenes, scene => scene.Name == "雨夜街口");
        Assert.Equal(draft.Script.Segments.Count, draft.Shots.Count);
        Assert.Contains("雨夜街口", draft.Shots[0].GenerationPrompt);
        Assert.True(draft.Timeline.Duration > 0);
    }

    [Fact]
    public void Analyze_SampleScriptProvidesNaturalRowsForPsdAndTtsTesting()
    {
        var draft = _analyzer.Analyze(DraftAnalyzer.SampleScript);

        Assert.Contains(draft.Script.Segments, segment =>
            segment.Type == SegmentType.Action &&
            segment.Text.Contains("陈默", StringComparison.Ordinal) &&
            segment.Text.Contains("抬手", StringComparison.Ordinal));
        Assert.Contains(draft.Script.Segments, segment =>
            segment.Type == SegmentType.Narration &&
            segment.Text.Contains("林安心想", StringComparison.Ordinal));
        Assert.Contains(draft.Script.Segments, segment =>
            segment.Text.Contains("雨声盖住", StringComparison.Ordinal));
        Assert.DoesNotContain(draft.Script.Segments, segment =>
            segment.Text.Contains("低声", StringComparison.Ordinal));
        Assert.DoesNotContain(draft.Script.Segments, segment =>
            segment.Text.Contains("角色动作", StringComparison.Ordinal) ||
            segment.Text.Contains("角色内心", StringComparison.Ordinal) ||
            segment.Text.Contains("场景描述", StringComparison.Ordinal) ||
            segment.Text.Contains("旁白叙述", StringComparison.Ordinal));
        Assert.All(
            draft.Script.Segments.Where(segment => segment.Type is SegmentType.Narration or SegmentType.Action),
            segment => Assert.True(segment.Text.Length <= 32, $"示例旁白/动作需要短句：{segment.Text}"));
        Assert.Contains(draft.Shots, shot =>
            shot.Dialogue?.Speaker == "陈默" &&
            shot.Dialogue.Text.Contains("对不起", StringComparison.Ordinal));
        Assert.Contains(draft.Shots, shot =>
            shot.Dialogue?.Speaker == "林安" &&
            shot.Dialogue.Text.Contains("等了多久", StringComparison.Ordinal));
        Assert.DoesNotContain(draft.Characters, character => character.Name is "林安低声" or "这句");
    }

    [Fact]
    public void Analyze_NaturalInnerThoughtDoesNotBecomeDialogue()
    {
        var draft = _analyzer.Analyze("""
陈默心想：这是怎么回事？
陈默抬手就要一掌打出。
陈默摸摸自己的头，略表无奈。
陈默：先别说话。
""");

        Assert.Contains(draft.Characters, character => character.Name == "陈默");
        Assert.Single(draft.Characters);
        Assert.Contains(draft.Script.Segments, segment => segment.Text.StartsWith("陈默心想", StringComparison.Ordinal) && segment.Type == SegmentType.Narration);
        Assert.Contains(draft.Script.Segments, segment => segment.Text.Contains("抬手", StringComparison.Ordinal) && segment.Type == SegmentType.Action);
        Assert.Contains(draft.Script.Segments, segment => segment.Text.Contains("摸摸", StringComparison.Ordinal) && segment.Type == SegmentType.Action);
    }

    [Fact]
    public void Analyze_NaturalSpeechModifierDoesNotBecomeCharacterName()
    {
        var draft = _analyzer.Analyze("林安低声说：雨这么大，你知道我等了多久吗？");

        Assert.Single(draft.Characters);
        Assert.Contains(draft.Characters, character => character.Name == "林安");
        Assert.DoesNotContain(draft.Characters, character => character.Name == "林安低声");
        Assert.Equal("林安", draft.Shots[0].Dialogue?.Speaker);
    }

    [Fact]
    public void Analyze_NaturalThoughtFragmentDoesNotBecomeCharacterName()
    {
        var draft = _analyzer.Analyze("陈默心里一沉，这句道歉怎么听起来这么轻。");

        Assert.Single(draft.Characters);
        Assert.Contains(draft.Characters, character => character.Name == "陈默");
        Assert.DoesNotContain(draft.Characters, character => character.Name == "这句");
        Assert.Null(draft.Shots[0].Dialogue);
    }

    [Fact]
    public void Analyze_StructuredActionAndNarrationLabelsDoNotBecomeCharacters()
    {
        var draft = _analyzer.Analyze("""
场景描述：办公室里只有电脑屏幕还亮着。
角色动作：陈默抬手示意林安别出声。
角色内心：陈默心里一沉。
旁白叙述：空气安静下来。
陈默：先别说话。
""");

        Assert.Contains(draft.Characters, character => character.Name == "陈默");
        Assert.Contains(draft.Characters, character => character.Name == "林安");
        Assert.DoesNotContain(draft.Characters, character => character.Name is "场景描述" or "角色动作" or "角色内心" or "旁白叙述");
        Assert.Contains(draft.Script.Segments, segment => segment.Text.StartsWith("角色动作：", StringComparison.Ordinal) && segment.Type == SegmentType.Action);
        Assert.Contains(draft.Script.Segments, segment => segment.Text.StartsWith("角色内心：", StringComparison.Ordinal) && segment.Type == SegmentType.Narration);
    }

    [Fact]
    public void Analyze_SplitsActionPlusDialogueSentences()
    {
        var draft = _analyzer.Analyze("林安抬头看着他，眼眶发红：你从来没有相信过我。");
        var shot = draft.Shots[0];

        Assert.Equal(SegmentType.Dialogue, draft.Script.Segments[0].Type);
        Assert.Equal("林安", shot.Dialogue?.Speaker);
        Assert.Equal("你从来没有相信过我。", shot.Dialogue?.Text);
    }

    [Fact]
    public void Analyze_DoesNotExtractActionsOrBodyPartsAsCharacters()
    {
        var draft = _analyzer.Analyze("""
她低头看手机，屏幕上还是没有任何回复，手指攥紧了雨伞。
陈默终于从街角跑来，喘着气说：对不起，我来晚了。
""");

        Assert.Contains(draft.Characters, character => character.Name == "陈默");
        Assert.DoesNotContain(draft.Characters, character => character.Name is "低头" or "手指" or "于从街角" or "喘着气");
    }

    [Fact]
    public void Analyze_RecognizesOfficeActionsAndSpeakers()
    {
        var draft = _analyzer.Analyze("李然坐在办公室里，盯着电脑屏幕。\n周晴推门进来，低声说：合同已经被他们撤回了。\n李然沉默几秒：那就重新开始。");

        Assert.Equal(SegmentType.Action, draft.Script.Segments[0].Type);
        Assert.Equal("周晴", draft.Shots[1].Dialogue?.Speaker);
        Assert.Equal("李然", draft.Shots[2].Dialogue?.Speaker);
        Assert.Contains(draft.Characters, character => character.Name == "周晴");
        Assert.Contains(draft.Scenes, scene => scene.Name == "办公室");
    }

    [Fact]
    public void CheckIssues_TracksMissingBindings()
    {
        var draft = _analyzer.Analyze(DraftAnalyzer.SampleScript);

        Assert.Contains(draft.Issues, issue => issue.Message.Contains("缺少参考形象"));
        Assert.Contains(draft.Issues, issue => issue.Message.Contains("未绑定背景素材"));
    }

    [Fact]
    public void ExportJson_ContainsDraftSections()
    {
        var draft = _analyzer.Analyze(DraftAnalyzer.SampleScript);
        using var document = JsonDocument.Parse(_analyzer.ExportJson(draft));

        Assert.True(document.RootElement.TryGetProperty("projectId", out _));
        Assert.True(document.RootElement.TryGetProperty("shots", out var shots));
        Assert.True(shots.GetArrayLength() > 0);
        Assert.True(document.RootElement.TryGetProperty("timeline", out _));
    }

    [Fact]
    public void ExportMarkdown_ContainsShotTable()
    {
        var draft = _analyzer.Analyze(DraftAnalyzer.SampleScript);
        var markdown = _analyzer.ExportMarkdown(draft);

        Assert.Contains("## 镜头表", markdown);
        Assert.Contains("林安", markdown);
    }
}

