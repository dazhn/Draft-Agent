using System.Text.Encodings.Web;
using System.Text.Json;

namespace DraftAgent.Core;

public static class DraftJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
