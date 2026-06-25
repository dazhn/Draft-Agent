namespace DraftAgent.Core;

public static class TtsGeneratedAudioReference
{
    public static bool IsSupportedAudioReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        var filename = reference.Trim();
        try
        {
            filename = ComfyOutputReference.Parse(filename).Filename;
        }
        catch
        {
            if (Uri.TryCreate(filename, UriKind.Absolute, out var uri))
            {
                filename = uri.LocalPath;
            }
        }

        return IsSupportedAudioExtension(Path.GetExtension(filename));
    }

    public static bool IsSupportedAudioExtension(string? extension)
    {
        return extension?.Equals(".wav", StringComparison.OrdinalIgnoreCase) == true ||
            extension?.Equals(".flac", StringComparison.OrdinalIgnoreCase) == true ||
            extension?.Equals(".mp3", StringComparison.OrdinalIgnoreCase) == true ||
            extension?.Equals(".m4a", StringComparison.OrdinalIgnoreCase) == true ||
            extension?.Equals(".ogg", StringComparison.OrdinalIgnoreCase) == true;
    }

    public static bool IsSupportedGeneratedAudioReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        var filename = reference.Trim();
        try
        {
            filename = ComfyOutputReference.Parse(filename).Filename;
        }
        catch
        {
            if (Uri.TryCreate(filename, UriKind.Absolute, out var uri))
            {
                filename = uri.LocalPath;
            }
        }

        return IsSupportedGeneratedAudioExtension(Path.GetExtension(filename));
    }

    public static bool IsSupportedGeneratedAudioExtension(string? extension)
    {
        return extension?.Equals(".wav", StringComparison.OrdinalIgnoreCase) == true ||
            extension?.Equals(".flac", StringComparison.OrdinalIgnoreCase) == true;
    }

    public static string? Resolve(GenerationTask? task)
    {
        if (task is null || task.Type != GenerationTaskType.Tts)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(task.ResultAssetId) ? null : task.ResultAssetId;
    }

    public static IReadOnlyList<string> ResolveCandidates(GenerationTask task)
    {
        if (task.Type != GenerationTaskType.Tts)
        {
            return [];
        }

        return string.IsNullOrWhiteSpace(task.ResultAssetId) ? [] : [task.ResultAssetId.Trim()];
    }
}
