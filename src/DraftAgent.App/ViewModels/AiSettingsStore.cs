using System;
using System.IO;
using System.Text.Json;

namespace DraftAgent.App.ViewModels;

public sealed record AiSettingsState(
    string SelectedAiProvider,
    string AiApiKey,
    string AiBaseUrl,
    string AiModelName,
    int AiMaxOutputTokens,
    string ComfyEndpoint);

public sealed class AiSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _path;

    public AiSettingsStore(string path)
    {
        _path = path;
    }

    public AiSettingsState? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<AiSettingsState>(File.ReadAllText(_path), JsonOptions);
        }
        catch (Exception ex)
        {
            AppRuntimeLog.Warning($"AI settings load failed: {ex.Message}");
            return null;
        }
    }

    public void Save(AiSettingsState state)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_path, JsonSerializer.Serialize(state, JsonOptions));
    }
}
