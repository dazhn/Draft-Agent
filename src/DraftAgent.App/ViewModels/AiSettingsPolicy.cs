using System;

namespace DraftAgent.App.ViewModels;

public sealed record AiProviderDefaults(string Provider, string BaseUrl, string ModelName);

public static class AiSettingsPolicy
{
    public const string DefaultProvider = "LM Studio";
    public const string DefaultLmStudioBaseUrl = "http://127.0.0.1:7982/v1";
    public const string DefaultLmStudioModel = "qwopus3.5-9b-v3";
    public const int DefaultMaxOutputTokens = 65536;
    public const int MinimumMaxOutputTokens = 32768;
    public const int MaximumMaxOutputTokens = 1_000_000;

    public static AiProviderDefaults DefaultsFor(string? provider)
    {
        return provider?.Trim() switch
        {
            "DeepSeek" => new AiProviderDefaults("DeepSeek", "https://api.deepseek.com/v1", "deepseek-chat"),
            "OpenAI" => new AiProviderDefaults("OpenAI", "https://api.openai.com/v1", "gpt-4.1-mini"),
            "Gemini" => new AiProviderDefaults("Gemini", "https://generativelanguage.googleapis.com/v1beta", "gemini-1.5-pro"),
            _ => new AiProviderDefaults(DefaultProvider, DefaultLmStudioBaseUrl, DefaultLmStudioModel)
        };
    }

    public static bool RequiresApiKey(string? provider)
    {
        return !string.Equals(DefaultsFor(provider).Provider, DefaultProvider, StringComparison.OrdinalIgnoreCase);
    }

    public static int ResolveMaxOutputTokens(string? value, int fallback)
    {
        var tokens = int.TryParse(value?.Trim(), out var parsed)
            ? parsed
            : fallback;

        return Math.Clamp(tokens, MinimumMaxOutputTokens, MaximumMaxOutputTokens);
    }

    public static string NormalizeMaxOutputTokensText(string? value, int fallback)
    {
        return ResolveMaxOutputTokens(value, fallback).ToString();
    }
}
