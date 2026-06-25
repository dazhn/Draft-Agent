using System.Text;

namespace DraftAgent.Core;

public static class EditingTimelineSubtitleBuilder
{
    public const int DefaultTargetCharactersPerCue = 24;
    public const int DefaultMinPreferredCharactersPerCue = 20;
    public const int DefaultMaxPreferredCharactersPerCue = 30;

    public const int DefaultMaxChineseCharactersPerCue = DefaultTargetCharactersPerCue;

    public static IReadOnlyList<SubtitleCue> BuildCues(
        string text,
        double durationSeconds,
        int maxChineseCharactersPerCue = DefaultMaxChineseCharactersPerCue)
    {
        var chunks = SplitByCharacterBudget(text, Math.Max(1, maxChineseCharactersPerCue));
        if (chunks.Count == 0)
        {
            return [];
        }

        var safeDuration = Math.Max(durationSeconds, chunks.Count);
        var cueDuration = safeDuration / chunks.Count;
        return chunks
            .Select((chunk, index) => new SubtitleCue(
                index + 1,
                Math.Round(index * cueDuration, 3),
                Math.Round(index == chunks.Count - 1 ? safeDuration - index * cueDuration : cueDuration, 3),
                chunk))
            .ToArray();
    }

    private static List<string> SplitByCharacterBudget(string text, int targetCharactersPerCue)
    {
        var normalized = text.Replace("\r", string.Empty).Replace('\n', ' ').Trim();
        var chunks = new List<string>();
        while (!string.IsNullOrWhiteSpace(normalized))
        {
            normalized = normalized.TrimStart();
            var splitIndex = FindPreferredSplitIndex(normalized, targetCharactersPerCue);
            if (splitIndex >= normalized.Length)
            {
                chunks.Add(normalized.Trim());
                break;
            }

            var chunk = normalized[..splitIndex].Trim();
            if (!string.IsNullOrWhiteSpace(chunk))
            {
                chunks.Add(chunk);
            }

            normalized = normalized[splitIndex..].TrimStart();
        }

        return chunks;
    }

    private static int FindPreferredSplitIndex(string text, int targetCharactersPerCue)
    {
        var minPreferred = Math.Min(DefaultMinPreferredCharactersPerCue, targetCharactersPerCue);
        var maxPreferred = Math.Max(DefaultMaxPreferredCharactersPerCue, targetCharactersPerCue);
        var budget = 0;
        int? targetSplitIndex = null;
        int? maxSplitIndex = null;
        var shouldSplit = false;
        var punctuationSplitIndex = 0;
        var punctuationDistance = int.MaxValue;

        for (var index = 0; index < text.Length;)
        {
            var status = Rune.DecodeFromUtf16(text.AsSpan(index), out var rune, out var consumed);
            if (status != System.Buffers.OperationStatus.Done || consumed <= 0)
            {
                consumed = 1;
                rune = new Rune('\uFFFD');
            }

            var nextIndex = index + consumed;
            if (!Rune.IsWhiteSpace(rune))
            {
                budget++;
            }

            if (IsBreakPunctuation(rune) && budget >= minPreferred && budget <= maxPreferred)
            {
                var distance = Math.Abs(budget - targetCharactersPerCue);
                if (distance <= punctuationDistance)
                {
                    punctuationDistance = distance;
                    punctuationSplitIndex = nextIndex;
                }
            }

            targetSplitIndex ??= budget >= targetCharactersPerCue ? nextIndex : null;
            if (budget >= maxPreferred)
            {
                maxSplitIndex = nextIndex;
                shouldSplit = HasNonWhitespaceText(text, nextIndex);
                break;
            }

            index = nextIndex;
        }

        if (!shouldSplit)
        {
            return text.Length;
        }

        if (punctuationSplitIndex > 0)
        {
            return punctuationSplitIndex;
        }

        return targetSplitIndex ?? maxSplitIndex ?? text.Length;
    }

    private static bool HasNonWhitespaceText(string text, int startIndex)
    {
        for (var index = startIndex; index < text.Length;)
        {
            var status = Rune.DecodeFromUtf16(text.AsSpan(index), out var rune, out var consumed);
            if (status != System.Buffers.OperationStatus.Done || consumed <= 0)
            {
                consumed = 1;
                rune = new Rune('\uFFFD');
            }

            if (!Rune.IsWhiteSpace(rune))
            {
                return true;
            }

            index += consumed;
        }

        return false;
    }

    private static bool IsBreakPunctuation(Rune rune)
    {
        return rune.Value is
            '。' or '！' or '？' or '；' or '，' or '、' or '：' or
            '.' or '!' or '?' or ';' or ',' or ':';
    }
}
