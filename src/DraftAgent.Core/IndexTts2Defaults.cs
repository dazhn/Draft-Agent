using System.Globalization;
using System.Text.RegularExpressions;

namespace DraftAgent.Core;

public static class IndexTts2Defaults
{
    public const string DefaultVoiceReferenceId = "ComfyUI_00038_.flac";
    public const string DefaultReferenceTextFileName = "ComfyUI_00038_.txt";
    public const string DefaultModelPath = "local:IndexTTS-2";
    public const int SafeMaxTextTokensPerSegment = 80;
    public const int SafeNumBeams = 1;
    public const int SafeMaxMelTokens = 1000;
    public const double DefaultEmotionStrength = 0.8;
    public const double MaxEmotionStrength = 1.2;

    public static readonly IReadOnlyList<string> EmotionKeys =
    [
        "Happy",
        "Angry",
        "Sad",
        "Surprised",
        "Afraid",
        "Disgusted",
        "Calm",
        "Melancholic"
    ];

    private static readonly IReadOnlyDictionary<string, string> EmotionMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Happy"] = "Happy",
        ["Angry"] = "Angry",
        ["Sad"] = "Sad",
        ["Surprised"] = "Surprised",
        ["Afraid"] = "Afraid",
        ["Disgusted"] = "Disgusted",
        ["Calm"] = "Calm",
        ["Melancholic"] = "Melancholic",
        ["开心"] = "Happy",
        ["高兴"] = "Happy",
        ["兴奋"] = "Happy",
        ["轻松"] = "Happy",
        ["喜悦"] = "Happy",
        ["愤怒"] = "Angry",
        ["生气"] = "Angry",
        ["恼怒"] = "Angry",
        ["悲伤"] = "Sad",
        ["难过"] = "Sad",
        ["委屈"] = "Sad",
        ["哭泣"] = "Sad",
        ["伤心"] = "Sad",
        ["惊讶"] = "Surprised",
        ["震惊"] = "Surprised",
        ["害怕"] = "Afraid",
        ["恐惧"] = "Afraid",
        ["紧张"] = "Afraid",
        ["焦虑"] = "Afraid",
        ["慌张"] = "Afraid",
        ["厌恶"] = "Disgusted",
        ["嫌恶"] = "Disgusted",
        ["反感"] = "Disgusted",
        ["平静"] = "Calm",
        ["冷静"] = "Calm",
        ["克制"] = "Calm",
        ["淡定"] = "Calm",
        ["压抑"] = "Melancholic",
        ["忧郁"] = "Melancholic",
        ["惆怅"] = "Melancholic",
        ["低落"] = "Melancholic"
    };

    public static string NormalizeEmotion(string? emotion)
    {
        if (string.IsNullOrWhiteSpace(emotion))
        {
            return "Calm";
        }

        var normalized = emotion.Trim();
        foreach (var pair in EmotionMap)
        {
            if (normalized.Equals(pair.Key, StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return "Calm";
    }

    public static double ResolveEmotionStrength(string? emotion, string? explicitStrength)
    {
        if (string.IsNullOrWhiteSpace(emotion))
        {
            return 0;
        }

        if (TryParseStrength(explicitStrength, out var configured))
        {
            return ClampStrength(configured);
        }

        if (TryParseStrength(emotion, out var inline))
        {
            return ClampStrength(inline);
        }

        return DefaultEmotionStrength;
    }

    public static string ReadDefaultReferenceText(string? startDirectory = null)
    {
        var root = FindWorkspaceRoot(startDirectory ?? AppContext.BaseDirectory);
        var path = Path.Combine(root, DefaultReferenceTextFileName);
        return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
    }

    public static bool IsDefaultVoiceReference(string? voiceReferenceId)
    {
        return string.IsNullOrWhiteSpace(voiceReferenceId) ||
            string.Equals(Path.GetFileName(voiceReferenceId), DefaultVoiceReferenceId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseStrength(string? value, out double strength)
    {
        strength = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        var percentMatch = Regex.Match(text, @"(?<value>\d+(?:\.\d+)?)\s*%");
        if (percentMatch.Success &&
            double.TryParse(percentMatch.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
        {
            strength = percent / 100;
            return true;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var direct))
        {
            strength = direct;
            return true;
        }

        var numberMatch = Regex.Match(text, @"(?:^|\s)(?<value>\d+(?:\.\d+)?)(?:\s|$)");
        if (numberMatch.Success &&
            double.TryParse(numberMatch.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            strength = number;
            return true;
        }

        return false;
    }

    private static double ClampStrength(double value)
    {
        return Math.Clamp(value, 0, MaxEmotionStrength);
    }

    private static string FindWorkspaceRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DraftAgent.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
