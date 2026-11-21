namespace DraftAgent.Core;

public sealed record ComfyOutputReference(string Type, string Subfolder, string Filename)
{
    public static ComfyOutputReference Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("ComfyUI output reference is empty.");
        }

        var trimmed = value.Trim();
        var parts = trimmed.Split('|', 3);
        if (parts.Length == 3 && !string.IsNullOrWhiteSpace(parts[2]))
        {
            var type = string.IsNullOrWhiteSpace(parts[0]) ? "output" : parts[0].Trim();
            var subfolder = parts[1].Trim();
            var filename = parts[2].Trim();
            Validate(type, subfolder, filename);
            return new ComfyOutputReference(type, subfolder, filename);
        }

        if (parts.Length != 1)
        {
            throw new InvalidOperationException("ComfyUI output reference must use type|subfolder|filename.");
        }

        Validate("output", string.Empty, trimmed);
        return new ComfyOutputReference("output", string.Empty, trimmed);
    }

    public string ToViewQuery()
    {
        var query = $"filename={Uri.EscapeDataString(Filename)}&type={Uri.EscapeDataString(Type)}";
        if (!string.IsNullOrWhiteSpace(Subfolder))
        {
            query += $"&subfolder={Uri.EscapeDataString(Subfolder)}";
        }

        return query;
    }

    private static void Validate(string type, string subfolder, string filename)
    {
        if (!string.Equals(type, "output", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(type, "temp", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("ComfyUI output reference type must be output or temp.");
        }

        if (string.IsNullOrWhiteSpace(filename) ||
            filename is "." or ".." ||
            filename.Contains('/') ||
            filename.Contains('\\') ||
            Path.IsPathRooted(filename) ||
            filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException("ComfyUI output reference filename must be a file name, not a path.");
        }

        if (string.IsNullOrWhiteSpace(subfolder))
        {
            return;
        }

        if (subfolder.Contains('\\') ||
            Path.IsPathRooted(subfolder) ||
            subfolder.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            throw new InvalidOperationException("ComfyUI output reference subfolder must be a relative ComfyUI output folder.");
        }

        var segments = subfolder.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 ||
            segments.Any(segment =>
                segment is "." or ".." ||
                segment.Contains(':') ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new InvalidOperationException("ComfyUI output reference subfolder must not escape the ComfyUI output folder.");
        }
    }
}
