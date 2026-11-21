using System.IO;

namespace DraftAgent.App.ViewModels;

internal static class WorkbenchBindingValue
{
    public static string? Asset(string? value)
    {
        var trimmed = RealValue(value);
        return trimmed is not null && LooksLikeAssetReference(trimmed) ? trimmed : null;
    }

    public static string? Text(string? value)
    {
        return RealValue(value);
    }

    public static bool IsUnbound(string? value)
    {
        return RealValue(value) is null;
    }

    private static string? RealValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed == "未绑定" ||
               trimmed == "未绑定图片" ||
               trimmed == "未绑定声音" ||
               trimmed == "旁白无需人物" ||
               trimmed == "默认配音" ||
               trimmed == "旁白声音" ||
               trimmed == "角色声音" ||
               trimmed == "场景预览" ||
               trimmed == "行图预览" ||
               trimmed == "人物预览" ||
               trimmed.StartsWith("未绑定", System.StringComparison.Ordinal)
            ? null
            : trimmed;
    }

    private static bool LooksLikeAssetReference(string value)
    {
        return Path.IsPathRooted(value) ||
            value.StartsWith("asset_", System.StringComparison.Ordinal) ||
            value.Contains('/') ||
            value.Contains('\\') ||
            value.Contains('.');
    }
}
