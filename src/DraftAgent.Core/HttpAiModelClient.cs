using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DraftAgent.Core;

public sealed class HttpAiModelClient : IAiModelClient
{
    private readonly HttpClient _httpClient;
    private readonly Func<AiServiceOptions> _optionsFactory;

    public HttpAiModelClient(HttpClient httpClient, Func<AiServiceOptions> optionsFactory)
    {
        _httpClient = httpClient;
        _optionsFactory = optionsFactory;
    }

    public async Task<AiModelJsonResponse> GenerateJsonAsync(AiModelJsonRequest request, CancellationToken cancellationToken = default)
    {
        var options = ValidateOptions();
        var reply = IsGemini(options)
            ? await CallGeminiAsync(options, request.SystemPrompt, request.UserText, requireJson: true, cancellationToken)
            : await CallOpenAiCompatibleAsync(options, request.SystemPrompt, request.UserText, requireJson: true, cancellationToken);

        return AiModelReplyInterpreter.ToJsonResponse(reply, options);
    }

    public async Task<AiModelTextResponse> GenerateTextAsync(AiModelTextRequest request, CancellationToken cancellationToken = default)
    {
        var options = ValidateOptions();
        var reply = IsGemini(options)
            ? await CallGeminiAsync(options, request.SystemPrompt, request.UserText, requireJson: false, cancellationToken)
            : await CallOpenAiCompatibleAsync(options, request.SystemPrompt, request.UserText, requireJson: false, cancellationToken);

        if (string.IsNullOrWhiteSpace(reply.Content))
        {
            throw new AiReplyException(
                reply.Metadata,
                $"{options.Provider} returned empty text content.",
                rawResponse: reply.RawResponse,
                content: reply.Content,
                reasoningContent: reply.ReasoningContent);
        }

        return new AiModelTextResponse(reply.Content.Trim(), reply.Content, reply.Metadata, reply.RawResponse, reply.ReasoningContent);
    }

    public async Task<AiConnectionCheckResult> CheckConnectionAsync(CancellationToken cancellationToken = default)
    {
        var currentOptions = _optionsFactory();
        try
        {
            var options = ValidateOptions(allowMissingModel: true);
            currentOptions = options;
            var endpoint = IsGemini(options)
                ? new Uri(new Uri(EnsureTrailingSlash(options.BaseUrl)), $"models?key={Uri.EscapeDataString(options.ApiKey)}")
                : new Uri(new Uri(EnsureTrailingSlash(options.BaseUrl)), "models");
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, endpoint);
            if (!IsGemini(options) && !string.IsNullOrWhiteSpace(options.ApiKey))
            {
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
            }

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new AiConnectionCheckResult(options.Provider, options.ModelName, options.BaseUrl, AiConnectionState.Connected, (int)response.StatusCode, "AI service connected.");
            }

            var state = response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                ? AiConnectionState.Unauthorized
                : AiConnectionState.Failed;
            return new AiConnectionCheckResult(options.Provider, options.ModelName, options.BaseUrl, state, (int)response.StatusCode, $"AI service check failed: HTTP {(int)response.StatusCode} {responseText}");
        }
        catch (Exception ex) when (ExternalBoundary.ShouldWrap(ex))
        {
            var state = ex is InvalidOperationException or UriFormatException ? AiConnectionState.InvalidConfiguration : AiConnectionState.Unreachable;
            return new AiConnectionCheckResult(currentOptions.Provider, currentOptions.ModelName, currentOptions.BaseUrl, state, null, $"AI service unreachable: {ex.Message}");
        }
    }

    private AiServiceOptions ValidateOptions(bool allowMissingModel = false)
    {
        var options = _optionsFactory();
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            throw new InvalidOperationException("AI Base URL is required.");
        }

        if (!allowMissingModel && string.IsNullOrWhiteSpace(options.ModelName))
        {
            throw new InvalidOperationException("AI model name is required.");
        }

        if (options.MaxOutputTokens <= 0)
        {
            throw new InvalidOperationException("AI max output tokens must be greater than 0.");
        }

        if (!IsLmStudio(options) && string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException($"{options.Provider} API key is required.");
        }

        _ = new Uri(EnsureTrailingSlash(options.BaseUrl));
        return options;
    }

    private async Task<AiProviderReply> CallOpenAiCompatibleAsync(AiServiceOptions options, string systemPrompt, string userText, bool requireJson, CancellationToken cancellationToken)
    {
        var endpoint = new Uri(new Uri(EnsureTrailingSlash(options.BaseUrl)), "chat/completions");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        }

        var body = new JsonObject
        {
            ["model"] = options.ModelName,
            ["temperature"] = 0.2,
            ["stream"] = false,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = IsLmStudio(options) ? $"{systemPrompt}\n\n/no_think" : systemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = userText }
            }
        };
        if (IsLmStudio(options))
        {
            body["max_tokens"] = options.MaxOutputTokens;
            body["reasoning_effort"] = "none";
            body["enable_thinking"] = false;
            body["chat_template_kwargs"] = new JsonObject
            {
                ["enable_thinking"] = false
            };
        }
        else if (IsOpenAi(options))
        {
            body["max_completion_tokens"] = options.MaxOutputTokens;
            if (requireJson)
            {
                body["response_format"] = new JsonObject { ["type"] = "json_object" };
            }
        }
        else
        {
            body["max_tokens"] = options.MaxOutputTokens;
            if (requireJson)
            {
                body["response_format"] = new JsonObject { ["type"] = "json_object" };
            }
        }

        httpRequest.Content = new StringContent(body.ToJsonString(DraftJson.Options), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{options.Provider} call failed: HTTP {(int)response.StatusCode} {responseText}");
        }

        using var document = JsonDocument.Parse(responseText);
        var choice = document.RootElement.GetProperty("choices")[0];
        var message = choice.GetProperty("message");
        var finishReason = choice.TryGetProperty("finish_reason", out var finishProperty) ? finishProperty.GetString() : null;
        var content = message.TryGetProperty("content", out var contentProperty) ? contentProperty.GetString() : null;
        var reasoning = message.TryGetProperty("reasoning_content", out var reasoningProperty) ? reasoningProperty.GetString() : null;
        var hasToolCalls = message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array && toolCalls.GetArrayLength() > 0;
        var metadata = AiModelReplyInterpreter.BuildOpenAiReplyMetadata(options, finishReason, content, reasoning, hasToolCalls);
        return new AiProviderReply(content ?? string.Empty, metadata, responseText, reasoning);
    }

    private async Task<AiProviderReply> CallGeminiAsync(AiServiceOptions options, string systemPrompt, string userText, bool requireJson, CancellationToken cancellationToken)
    {
        var endpoint = new Uri(new Uri(EnsureTrailingSlash(options.BaseUrl)), $"models/{options.ModelName}:generateContent?key={Uri.EscapeDataString(options.ApiKey)}");
        var generationConfig = new JsonObject
        {
            ["temperature"] = 0.2,
            ["maxOutputTokens"] = options.MaxOutputTokens
        };
        if (requireJson)
        {
            generationConfig["responseMimeType"] = "application/json";
        }

        var body = new JsonObject
        {
            ["generationConfig"] = generationConfig,
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = new JsonArray
                    {
                        new JsonObject { ["text"] = $"{systemPrompt}\n\nUser text:\n{userText}" }
                    }
                }
            }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent(body)
        };
        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Gemini call failed: HTTP {(int)response.StatusCode} {responseText}");
        }

        using var document = JsonDocument.Parse(responseText);
        var candidate = document.RootElement.GetProperty("candidates")[0];
        var finishReason = candidate.TryGetProperty("finishReason", out var finishProperty) ? finishProperty.GetString() : null;
        var content = candidate
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();
        var metadata = AiModelReplyInterpreter.BuildGeminiReplyMetadata(options, finishReason, content);
        return new AiProviderReply(content ?? string.Empty, metadata, responseText, null);
    }

    private static StringContent JsonContent<T>(T value)
    {
        return new StringContent(JsonSerializer.Serialize(value, DraftJson.Options), Encoding.UTF8, "application/json");
    }

    private static string EnsureTrailingSlash(string value)
    {
        return value.EndsWith('/') ? value : $"{value}/";
    }

    private static bool IsGemini(AiServiceOptions options)
    {
        return options.Provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLmStudio(AiServiceOptions options)
    {
        return options.Provider.Equals("LM Studio", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOpenAi(AiServiceOptions options)
    {
        return options.Provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase);
    }
}
