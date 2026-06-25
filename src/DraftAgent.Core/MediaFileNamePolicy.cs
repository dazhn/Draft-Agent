using System.Text.RegularExpressions;

namespace DraftAgent.Core;

public static partial class MediaFileNamePolicy
{
    public static string PublicFileNameFromSource(string sourcePath)
    {
        var fileName = Path.GetFileName(sourcePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return $"asset{Path.GetExtension(sourcePath)}";
        }

        var extension = Path.GetExtension(fileName);
        var name = StripInternalIdPrefix(Path.GetFileNameWithoutExtension(fileName));
        name = SanitizeFileName(name);
        return $"{(string.IsNullOrWhiteSpace(name) ? "asset" : name)}{extension}";
    }

    public static bool HasInternalIdPrefix(string? fileNameOrPath)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrPath))
        {
            return false;
        }

        var name = Path.GetFileNameWithoutExtension(fileNameOrPath.Trim().Replace('\\', '/'));
        return InternalIdPrefixPattern().IsMatch(name);
    }

    public static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "asset" : cleaned;
    }

    private static string StripInternalIdPrefix(string value)
    {
        var current = value.Trim();
        for (var index = 0; index < 8; index++)
        {
            var next = InternalIdPrefixPattern().Replace(current, string.Empty, 1).TrimStart('_', '-', ' ');
            if (string.Equals(next, current, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(next))
            {
                return current;
            }

            current = next;
        }

        return current;
    }

    [GeneratedRegex(@"^(?:int_scene_[0-9]+_[0-9]+|(?:int|scene|row|tts|nar)_[0-9]+|(?:tts|row|nar|scene|int)[_-]+|char_[A-Za-z0-9]+|character_[0-9]+|scene_[0-9]+|asset_[A-Fa-f0-9]{8,})(?:[_-]+)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InternalIdPrefixPattern();
}
