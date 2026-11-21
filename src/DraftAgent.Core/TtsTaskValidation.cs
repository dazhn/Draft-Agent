namespace DraftAgent.Core;

public static class TtsTaskValidation
{
    public const string MissingVoiceReferenceMessage = "请先为语音合成行绑定参考音频。";
    public const string InvalidVoiceReferenceMessage = "参考音频必须是 ComfyUI CharacterVoicesNode 下拉中的相对 voice_name，不能使用绝对路径或 URL。";
    public static bool HasVoiceReference(GenerationTask task)
    {
        return task.Parameters.TryGetValue("voiceReferenceId", out var value) &&
            IsBoundVoiceReference(value);
    }

    public static void RequireVoiceReference(GenerationTask task)
    {
        if (!HasVoiceReference(task))
        {
            throw new InvalidOperationException(MissingVoiceReferenceMessage);
        }

        var value = task.Parameters["voiceReferenceId"];
        if (!IsSafeComfyVoiceFileName(value))
        {
            throw new InvalidOperationException(InvalidVoiceReferenceMessage);
        }
    }

    private static bool IsBoundVoiceReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return trimmed != "未绑定" &&
            trimmed != "未绑定声音" &&
            trimmed != "未绑定参考音频" &&
            !trimmed.StartsWith("未绑定", StringComparison.Ordinal);
    }

    public static bool IsSafeComfyVoiceFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        var normalized = trimmed.Replace('\\', '/');
        if (Path.IsPathRooted(trimmed) ||
            trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(normalized));
    }
}
