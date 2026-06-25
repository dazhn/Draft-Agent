namespace DraftAgent.Core;

internal static class CharacterNameGuard
{
    private static readonly string[] ActionWords =
    [
        "站", "坐", "等", "低头", "看", "盯", "攥紧", "跑来", "喘", "抬头", "停", "伸手", "收回",
        "转身", "离开", "沉默", "推门", "进来", "开门", "关门", "拿", "放", "走", "皱眉", "说", "问",
        "喊", "回答", "喃喃", "道", "低声", "轻声", "压低声音", "喘着气"
    ];

    private static readonly HashSet<string> NonCharacterNameTerms =
    [
        "旁白", "旁白叙述", "用户", "镜头", "场景", "场景描述", "画面", "字幕", "主角光环",
        "角色动作", "角色内心", "动作",
        "雨夜", "街口", "街角", "办公室", "房间", "室内", "室外",
        "低头", "抬头", "伸手", "收回", "转身", "离开", "沉默", "皱眉", "跑来", "喘着气", "低声", "轻声",
        "手机", "屏幕", "手指", "雨伞", "眼眶", "肩膀", "嘴角", "眉头", "呼吸", "雨声", "合同", "电脑",
        "两个人", "一个人", "这个人", "那个人",
        "这句", "那句", "这场", "那场", "这次", "那次", "这段", "那段", "这种", "那种", "这个", "那个"
    ];

    private static readonly char[] InvalidNameCharacters =
    [
        '，', '。', '：', ':', '；', ';', '！', '？', '、', '“', '”', '"', '\'', '（', '）', '(', ')',
        ' ', '\t', '\r', '\n'
    ];

    private static readonly char[] FunctionCharacters =
    [
        '的', '地', '得', '了', '着', '过', '从', '在', '把', '被', '将', '让', '向', '往', '给',
        '和', '与', '对', '又', '还', '也', '都', '就', '才', '再', '正', '已'
    ];

    private static readonly string[] InvalidFragments =
    [
        "街角", "街口", "手机", "屏幕", "雨伞", "眼眶", "呼吸", "个人", "两个", "一个",
        "着气", "从街", "伸手", "抬头", "低头", "低声", "轻声",
        "这句", "那句", "这场", "那场", "这次", "那次", "这段", "那段", "这种", "那种"
    ];

    public static string? NormalizeOrNull(string? name)
    {
        var normalized = Normalize(name);
        return IsLikelyName(normalized) ? normalized : null;
    }

    public static string NormalizeField(string? name)
    {
        return Normalize(name);
    }

    public static bool IsLikelyName(string? name)
    {
        var normalized = Normalize(name);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (normalized.Length is < 2 or > 8)
        {
            return false;
        }

        if (normalized.IndexOfAny(InvalidNameCharacters) >= 0)
        {
            return false;
        }

        if (NonCharacterNameTerms.Contains(normalized))
        {
            return false;
        }

        if (normalized.Any(char.IsDigit))
        {
            return false;
        }

        if (normalized is "他" or "她" or "它" or "他们" or "她们" or "它们")
        {
            return false;
        }

        if (normalized.StartsWith("他", StringComparison.Ordinal) ||
            normalized.StartsWith("她", StringComparison.Ordinal) ||
            normalized.StartsWith("它", StringComparison.Ordinal))
        {
            return false;
        }

        if (normalized.Any(ch => FunctionCharacters.Contains(ch)))
        {
            return false;
        }

        if (ActionWords.Any(word => normalized.Contains(word, StringComparison.Ordinal)))
        {
            return false;
        }

        if (InvalidFragments.Any(fragment => normalized.Contains(fragment, StringComparison.Ordinal)))
        {
            return false;
        }

        if (normalized.All(IsChineseCharacter))
        {
            return normalized.Length <= 4;
        }

        return normalized.All(ch => char.IsLetter(ch) || ch == '_' || ch == '-' || ch == '·');
    }

    private static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim().Trim('【', '】', '[', ']', '「', '」', '『', '』');
    }

    private static bool IsChineseCharacter(char ch)
    {
        return ch is >= '\u4e00' and <= '\u9fff';
    }
}
