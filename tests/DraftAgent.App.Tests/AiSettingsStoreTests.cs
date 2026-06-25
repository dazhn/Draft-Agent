using DraftAgent.App.ViewModels;

namespace DraftAgent.App.Tests;

public sealed class AiSettingsStoreTests
{
    [Fact]
    public void SaveThenLoadRestoresAiAndComfySettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"draft-agent-ai-settings-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "ai-settings.json");
        var store = new AiSettingsStore(path);
        var saved = new AiSettingsState(
            "OpenAI",
            "sk-test",
            "https://api.example/v1",
            "model-test",
            123456,
            "http://127.0.0.1:9000");

        store.Save(saved);
        var loaded = store.Load();

        Assert.Equal(saved, loaded);
    }
}
