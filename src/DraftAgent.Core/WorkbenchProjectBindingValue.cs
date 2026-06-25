namespace DraftAgent.Core;

internal static class WorkbenchProjectBindingValue
{
    public static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
