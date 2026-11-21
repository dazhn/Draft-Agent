using System.Text.Json;

namespace DraftAgent.Core;

internal static class ComfyPromptResponseParser
{
    public static ComfyPromptMetadata ParseSubmitResponse(string responseText)
    {
        using var document = JsonDocument.Parse(responseText);
        var promptId = document.RootElement.TryGetProperty("prompt_id", out var promptProperty)
            ? promptProperty.GetString()
            : null;
        int? queueNumber = document.RootElement.TryGetProperty("number", out var numberProperty) && numberProperty.TryGetInt32(out var parsedNumber)
            ? parsedNumber
            : null;

        if (string.IsNullOrWhiteSpace(promptId))
        {
            var metadata = new ComfyPromptMetadata(null, queueNumber, ComfySubmitState.MissingPromptId, "ComfyUI responded without prompt_id.");
            throw new ComfyPromptException(metadata, metadata.Message);
        }

        return new ComfyPromptMetadata(promptId, queueNumber, ComfySubmitState.Submitted, "ComfyUI prompt submitted.");
    }

    public static ComfyPromptStatusResult? TryParseHistory(string promptId, string responseText)
    {
        using var document = JsonDocument.Parse(responseText);
        if (!document.RootElement.TryGetProperty(promptId, out var promptHistory) || promptHistory.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var hasErrors = HistoryHasErrors(promptHistory);
        var errorMessage = hasErrors ? ExtractHistoryErrorMessage(promptHistory) : null;
        var outputs = ExtractHistoryOutputs(promptHistory);
        var hasAudioOutputs = outputs.Count > 0;
        return new ComfyPromptStatusResult(
            promptId,
            hasErrors ? ComfyPromptState.Failed : hasAudioOutputs ? ComfyPromptState.Completed : ComfyPromptState.Running,
            hasErrors ? GenerationTaskStatus.Failed : hasAudioOutputs ? GenerationTaskStatus.Completed : GenerationTaskStatus.Running,
            null,
            1,
            outputs,
            hasErrors
                ? string.IsNullOrWhiteSpace(errorMessage)
                    ? "ComfyUI prompt failed; history contains error status."
                    : $"ComfyUI prompt failed: {errorMessage}"
                : hasAudioOutputs
                    ? "ComfyUI prompt completed."
                    : "ComfyUI prompt history is present but contains no TTS audio output.",
            responseText);
    }

    public static ComfyPromptStatusResult? TryParseQueue(string promptId, string responseText)
    {
        using var document = JsonDocument.Parse(responseText);
        if (ContainsPrompt(document.RootElement, "queue_running", promptId, out var runningPosition))
        {
            return new ComfyPromptStatusResult(
                promptId,
                ComfyPromptState.Running,
                GenerationTaskStatus.Running,
                runningPosition,
                null,
                [],
                "ComfyUI prompt is running.",
                responseText);
        }

        if (ContainsPrompt(document.RootElement, "queue_pending", promptId, out var pendingPosition))
        {
            return new ComfyPromptStatusResult(
                promptId,
                ComfyPromptState.Queued,
                GenerationTaskStatus.Pending,
                pendingPosition,
                null,
                [],
                "ComfyUI prompt is queued.",
                responseText);
        }

        return null;
    }

    private static bool HistoryHasErrors(JsonElement promptHistory)
    {
        if (!promptHistory.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (status.TryGetProperty("status_str", out var statusString) &&
            statusString.ValueKind == JsonValueKind.String &&
            statusString.GetString()?.Contains("error", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        if (status.TryGetProperty("completed", out var completed) &&
            completed.ValueKind is JsonValueKind.False)
        {
            return true;
        }

        if (status.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
        {
            return messages.EnumerateArray().Any(MessageContainsError);
        }

        return false;
    }

    private static bool MessageContainsError(JsonElement message)
    {
        if (message.ValueKind == JsonValueKind.String)
        {
            return message.GetString()?.Contains("error", StringComparison.OrdinalIgnoreCase) == true;
        }

        if (message.ValueKind == JsonValueKind.Array || message.ValueKind == JsonValueKind.Object)
        {
            foreach (var child in EnumerateChildren(message))
            {
                if (MessageContainsError(child))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string? ExtractHistoryErrorMessage(JsonElement promptHistory)
    {
        if (promptHistory.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Object)
        {
            if (FindStringProperty(status, "exception_message") is { } exceptionMessage)
            {
                return NormalizeErrorMessage(exceptionMessage);
            }

            if (FindStringProperty(status, "message") is { } message)
            {
                return NormalizeErrorMessage(message);
            }
        }

        if (FindStringProperty(promptHistory, "exception_message") is { } fallbackExceptionMessage)
        {
            return NormalizeErrorMessage(fallbackExceptionMessage);
        }

        return null;
    }

    private static string? FindStringProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals(propertyName) && property.Value.ValueKind == JsonValueKind.String)
                {
                    var value = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }

                if (FindStringProperty(property.Value, propertyName) is { } nested)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                if (FindStringProperty(child, propertyName) is { } nested)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string NormalizeErrorMessage(string message)
    {
        var normalized = string.Join(" ", message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 600 ? normalized : $"{normalized[..600]}...";
    }

    private static IReadOnlyList<string> ExtractHistoryOutputs(JsonElement promptHistory)
    {
        if (!promptHistory.TryGetProperty("outputs", out var outputs) || outputs.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var assetIds = new List<string>();
        foreach (var nodeOutput in outputs.EnumerateObject())
        {
            CollectOutputAssets(nodeOutput.Value, assetIds);
        }

        return assetIds.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static void CollectOutputAssets(JsonElement element, List<string> assetIds)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("filename", out var filename) && filename.ValueKind == JsonValueKind.String)
            {
                var value = filename.GetString();
                if (!string.IsNullOrWhiteSpace(value) && IsAudioOutput(value))
                {
                    var type = element.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
                        ? typeElement.GetString()
                        : null;
                    var subfolder = element.TryGetProperty("subfolder", out var subfolderElement) && subfolderElement.ValueKind == JsonValueKind.String
                        ? subfolderElement.GetString()
                        : null;
                    assetIds.Add(FormatComfyAssetId(value, type, subfolder));
                }
            }

            foreach (var child in element.EnumerateObject())
            {
                CollectOutputAssets(child.Value, assetIds);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                CollectOutputAssets(child, assetIds);
            }
        }
    }

    private static bool IsAudioOutput(string filename)
    {
        return TtsGeneratedAudioReference.IsSupportedAudioReference(filename);
    }

    private static string FormatComfyAssetId(string filename, string? type, string? subfolder)
    {
        if (string.IsNullOrWhiteSpace(type) && string.IsNullOrWhiteSpace(subfolder))
        {
            return filename;
        }

        return $"{type ?? "output"}|{subfolder ?? string.Empty}|{filename}";
    }

    private static bool ContainsPrompt(JsonElement root, string propertyName, string promptId, out int? position)
    {
        position = null;
        if (!root.TryGetProperty(propertyName, out var queue) || queue.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var index = 0;
        foreach (var item in queue.EnumerateArray())
        {
            index++;
            if (ElementContainsString(item, promptId))
            {
                position = index;
                return true;
            }
        }

        return false;
    }

    private static bool ElementContainsString(JsonElement element, string value)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() == value,
            JsonValueKind.Array => element.EnumerateArray().Any(child => ElementContainsString(child, value)),
            JsonValueKind.Object => element.EnumerateObject().Any(child => ElementContainsString(child.Value, value)),
            _ => false
        };
    }

    private static IEnumerable<JsonElement> EnumerateChildren(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Array => element.EnumerateArray(),
            JsonValueKind.Object => element.EnumerateObject().Select(property => property.Value),
            _ => []
        };
    }

    private static void CollectStringLeaves(JsonElement element, List<string> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                values.Add(element.GetString() ?? string.Empty);
                break;
            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                {
                    CollectStringLeaves(child, values);
                }
                break;
        }
    }
}
