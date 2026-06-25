using System.Text.Json;
using System.Text.RegularExpressions;

namespace DraftAgent.Core;

internal sealed record AiProviderReply(
    string Content,
    AiModelReplyMetadata Metadata,
    string RawResponse,
    string? ReasoningContent);

internal static class AiModelReplyInterpreter
{
    public static AiModelJsonResponse ToJsonResponse(AiProviderReply reply, AiServiceOptions options)
    {
        if (TryExtractValidJson(reply.Content, reply.Metadata, out var contentJson, out var contentError))
        {
            return new AiModelJsonResponse(contentJson, reply.Content, reply.Metadata, reply.RawResponse, reply.ReasoningContent);
        }

        if (TryExtractValidJson(reply.ReasoningContent, reply.Metadata, out var reasoningJson, out var reasoningError))
        {
            var recoveredMetadata = reply.Metadata with
            {
                State = AiReplyState.RecoveredFromReasoning,
                Message = $"{options.Provider} returned JSON in reasoning_content; Draft Agent recovered it, but disabling reasoning output is recommended."
            };
            return new AiModelJsonResponse(reasoningJson, reply.Content, recoveredMetadata, reply.RawResponse, reply.ReasoningContent);
        }

        var failureMessage = BuildReplyFailureMessage(reply.Metadata, contentError, reasoningError);
        if (reply.Metadata.State != AiReplyState.Completed)
        {
            throw new AiReplyException(
                reply.Metadata,
                failureMessage,
                rawResponse: reply.RawResponse,
                content: reply.Content,
                reasoningContent: reply.ReasoningContent);
        }

        throw new AiReplyException(
            reply.Metadata with { State = AiReplyState.InvalidJson, Message = failureMessage },
            failureMessage,
            rawResponse: reply.RawResponse,
            content: reply.Content,
            reasoningContent: reply.ReasoningContent);
    }

    public static AiModelReplyMetadata BuildOpenAiReplyMetadata(
        AiServiceOptions options,
        string? finishReason,
        string? content,
        string? reasoning,
        bool hasToolCalls)
    {
        var contentLength = content?.Length ?? 0;
        var reasoningLength = reasoning?.Length ?? 0;
        if (string.IsNullOrWhiteSpace(content) && !string.IsNullOrWhiteSpace(reasoning))
        {
            return finishReason is "length"
                ? NewReply(options, finishReason, AiReplyState.ReasoningLengthLimited, $"{options.Provider} returned reasoning_content only and it was length-limited.", contentLength, reasoningLength)
                : NewReply(options, finishReason, AiReplyState.ReasoningOnly, $"{options.Provider} returned reasoning_content only and no JSON content.", contentLength, reasoningLength);
        }

        if (finishReason is "length")
        {
            return NewReply(options, finishReason, AiReplyState.LengthLimited, "Model reply was length-limited.", contentLength, reasoningLength);
        }

        if (finishReason is "content_filter")
        {
            return NewReply(options, finishReason, AiReplyState.ContentFiltered, "Model reply was filtered.", contentLength, reasoningLength);
        }

        if (hasToolCalls && string.IsNullOrWhiteSpace(content))
        {
            return NewReply(options, finishReason, AiReplyState.ToolCallsOnly, "Model returned tool calls only and no JSON content.", contentLength, reasoningLength);
        }

        if (!string.IsNullOrWhiteSpace(content))
        {
            return NewReply(options, finishReason, AiReplyState.Completed, "Model reply completed.", contentLength, reasoningLength);
        }

        return NewReply(options, finishReason, AiReplyState.EmptyContent, $"{options.Provider} returned empty content.", contentLength, reasoningLength);
    }

    public static AiModelReplyMetadata BuildGeminiReplyMetadata(AiServiceOptions options, string? finishReason, string? content)
    {
        if (finishReason is "MAX_TOKENS")
        {
            return NewReply(options, finishReason, AiReplyState.LengthLimited, "Gemini reply was length-limited.");
        }

        if (finishReason is "SAFETY" or "RECITATION" or "BLOCKLIST" or "PROHIBITED_CONTENT" or "SPII")
        {
            return NewReply(options, finishReason, AiReplyState.ContentFiltered, $"Gemini reply was blocked: {finishReason}.");
        }

        return string.IsNullOrWhiteSpace(content)
            ? NewReply(options, finishReason, AiReplyState.EmptyContent, "Gemini returned empty content.")
            : NewReply(options, finishReason, AiReplyState.Completed, "Gemini reply completed.");
    }

    private static string ExtractJson(string content, AiModelReplyMetadata metadata)
    {
        var text = content.Trim();
        var fenceMatch = Regex.Match(text, "```(?:json)?\\s*(.*?)\\s*```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (fenceMatch.Success)
        {
            text = fenceMatch.Groups[1].Value.Trim();
        }

        var start = text.IndexOfAny(['{', '[']);
        var endObject = text.LastIndexOf('}');
        var endArray = text.LastIndexOf(']');
        var end = Math.Max(endObject, endArray);
        if (start < 0 || end < start)
        {
            throw new AiReplyException(
                metadata with { State = AiReplyState.InvalidJson, Message = "AI content did not contain JSON." },
                "AI content did not contain JSON.",
                content: content);
        }

        return text[start..(end + 1)];
    }

    private static bool TryExtractValidJson(string? content, AiModelReplyMetadata metadata, out string json, out string? error)
    {
        json = string.Empty;
        error = null;
        if (string.IsNullOrWhiteSpace(content))
        {
            error = "content is empty";
            return false;
        }

        try
        {
            var extracted = ExtractJson(content, metadata);
            EnsureValidJson(extracted, metadata);
            json = extracted;
            return true;
        }
        catch (AiReplyException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string BuildReplyFailureMessage(AiModelReplyMetadata metadata, string? contentError, string? reasoningError)
    {
        return $"{metadata.Message} contentJson={contentError ?? "not attempted"}; reasoningJson={reasoningError ?? "not attempted"}";
    }

    private static void EnsureValidJson(string json, AiModelReplyMetadata metadata)
    {
        try
        {
            using var _ = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new AiReplyException(
                metadata with { State = AiReplyState.InvalidJson, Message = $"AI returned invalid JSON: {ex.Message}" },
                $"AI returned invalid JSON: {ex.Message}",
                ex,
                content: json);
        }
    }

    private static AiModelReplyMetadata NewReply(
        AiServiceOptions options,
        string? finishReason,
        AiReplyState state,
        string message,
        int contentLength = 0,
        int reasoningLength = 0)
    {
        return new AiModelReplyMetadata(options.Provider, options.ModelName, finishReason, state, message, contentLength, reasoningLength);
    }
}
