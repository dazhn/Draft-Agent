using DraftAgent.App.ViewModels;

namespace DraftAgent.App.Tests;

public sealed class AiSettingsPolicyTests
{
    [Theory]
    [InlineData("DeepSeek", "https://api.deepseek.com/v1", "deepseek-chat")]
    [InlineData("OpenAI", "https://api.openai.com/v1", "gpt-4.1-mini")]
    [InlineData("Gemini", "https://generativelanguage.googleapis.com/v1beta", "gemini-1.5-pro")]
    [InlineData("LM Studio", "http://127.0.0.1:7982/v1", "qwopus3.5-9b-v3")]
    [InlineData("Unknown", "http://127.0.0.1:7982/v1", "qwopus3.5-9b-v3")]
    public void DefaultsForReturnsProviderDefaults(string provider, string expectedBaseUrl, string expectedModelName)
    {
        var defaults = AiSettingsPolicy.DefaultsFor(provider);

        Assert.Equal(expectedBaseUrl, defaults.BaseUrl);
        Assert.Equal(expectedModelName, defaults.ModelName);
    }

    [Theory]
    [InlineData("65536", 65536, 65536)]
    [InlineData(" 65536 ", 32768, 65536)]
    [InlineData("8192", 65536, 32768)]
    [InlineData("1000001", 65536, 1000000)]
    [InlineData("not-a-number", 70000, 70000)]
    public void ResolveMaxOutputTokensKeepsUserValueWithinAllowedRange(string text, int fallback, int expected)
    {
        var tokens = AiSettingsPolicy.ResolveMaxOutputTokens(text, fallback);

        Assert.Equal(expected, tokens);
    }

    [Fact]
    public void NormalizeMaxOutputTokensTextDoesNotTruncateLargeValidValue()
    {
        var normalized = AiSettingsPolicy.NormalizeMaxOutputTokensText("65536", 32768);

        Assert.Equal("65536", normalized);
    }
}

