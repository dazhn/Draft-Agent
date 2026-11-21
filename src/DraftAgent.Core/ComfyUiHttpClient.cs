using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DraftAgent.Core;

public sealed class ComfyUiHttpClient : IComfyUiClient, IComfyConnectionClient, IComfyPromptStatusClient, IComfyQueueControlClient, IComfyOutputFileClient, IComfyVoiceCatalogClient
{
    private readonly HttpClient _httpClient;
    private readonly Func<ComfyUiOptions> _optionsFactory;

    public ComfyUiHttpClient(HttpClient httpClient, Func<ComfyUiOptions> optionsFactory)
    {
        _httpClient = httpClient;
        _optionsFactory = optionsFactory;
    }

    public async Task<ComfyTaskResult> SubmitAsync(ComfyTaskRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            if (request.Task.Type != GenerationTaskType.Tts)
            {
                throw new NotSupportedException("ComfyUI submission only supports TTS tasks; AI image/video generation has been removed.");
            }

            var options = ValidateOptions();
            var workflowPath = ResolveWorkflowPath(request.Task.Type, options);
            if (!File.Exists(workflowPath))
            {
                throw new FileNotFoundException($"ComfyUI workflow not found: {workflowPath}", workflowPath);
            }

            var workflow = JsonNode.Parse(await File.ReadAllTextAsync(workflowPath, cancellationToken))?.AsObject()
                ?? throw new InvalidOperationException($"ComfyUI workflow is not valid JSON: {workflowPath}");
            ComfyWorkflowPatcher.Patch(workflow, request.Task);

            var body = new JsonObject
            {
                ["client_id"] = $"draft-agent-{Guid.NewGuid():N}",
                ["prompt"] = workflow
            };
            var endpoint = new Uri(new Uri(EnsureTrailingSlash(options.Endpoint)), "prompt");
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(body.ToJsonString(DraftJson.Options), Encoding.UTF8, "application/json")
            };
            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"ComfyUI submit failed: HTTP {(int)response.StatusCode} {responseText}");
            }

            var promptMetadata = ComfyPromptResponseParser.ParseSubmitResponse(responseText);
            return new ComfyTaskResult(request.Task.Id, GenerationTaskStatus.Pending, promptMetadata.PromptId, responseText, promptMetadata);
        }
        catch (Exception ex) when (ExternalBoundary.ShouldWrap(ex))
        {
            throw new ExternalIntegrationException("ComfyUI", "submit", $"ComfyUI task submit failed: {ex.Message}", ex);
        }
    }

    public async Task<ComfyConnectionCheckResult> CheckComfyConnectionAsync(CancellationToken cancellationToken = default)
    {
        var currentOptions = _optionsFactory();
        try
        {
            var options = ValidateOptions();
            currentOptions = options;
            var endpoint = new Uri(new Uri(EnsureTrailingSlash(options.Endpoint)), "system_stats");
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new ComfyConnectionCheckResult(options.Endpoint, ComfyConnectionState.Connected, (int)response.StatusCode, "ComfyUI connected.");
            }

            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            return new ComfyConnectionCheckResult(options.Endpoint, ComfyConnectionState.Failed, (int)response.StatusCode, $"ComfyUI check failed: HTTP {(int)response.StatusCode} {responseText}");
        }
        catch (Exception ex) when (ExternalBoundary.ShouldWrap(ex))
        {
            var state = ex is InvalidOperationException or UriFormatException ? ComfyConnectionState.InvalidConfiguration : ComfyConnectionState.Unreachable;
            return new ComfyConnectionCheckResult(currentOptions.Endpoint, state, null, $"ComfyUI unreachable: {ex.Message}");
        }
    }

    public async Task<ComfyVoiceCatalogResult> GetVoiceCatalogAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var options = ValidateOptions();
            var endpoint = new Uri(new Uri(EnsureTrailingSlash(options.Endpoint)), "object_info/CharacterVoicesNode");
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"ComfyUI voice catalog query failed: HTTP {(int)response.StatusCode} {responseText}");
            }

            return new ComfyVoiceCatalogResult(ParseCharacterVoiceReferences(responseText));
        }
        catch (Exception ex) when (ExternalBoundary.ShouldWrap(ex))
        {
            throw new ExternalIntegrationException("ComfyUI", "voice_catalog", $"ComfyUI voice catalog query failed: {ex.Message}", ex);
        }
    }

    public async Task<ComfyPromptStatusResult> GetPromptStatusAsync(string promptId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(promptId))
            {
                throw new InvalidOperationException("ComfyUI prompt_id is required.");
            }

            var options = ValidateOptions();
            var historyResult = await TryGetHistoryAsync(options, promptId, cancellationToken);
            if (historyResult is not null)
            {
                return historyResult;
            }

            var queueResult = await TryGetQueueStatusAsync(options, promptId, cancellationToken);
            return queueResult ?? new ComfyPromptStatusResult(
                promptId,
                ComfyPromptState.NotFound,
                GenerationTaskStatus.Pending,
                null,
                null,
                [],
                "ComfyUI prompt_id was not found in history or queue.");
        }
        catch (Exception ex) when (ExternalBoundary.ShouldWrap(ex))
        {
            throw new ExternalIntegrationException("ComfyUI", "status", $"ComfyUI status query failed: {ex.Message}", ex);
        }
    }

    public async Task<ComfyQueueDeleteResult> DeleteQueuedPromptsAsync(IReadOnlyList<string> promptIds, CancellationToken cancellationToken = default)
    {
        try
        {
            var uniquePromptIds = promptIds
                .Where(promptId => !string.IsNullOrWhiteSpace(promptId))
                .Select(promptId => promptId.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (uniquePromptIds.Length == 0)
            {
                return new ComfyQueueDeleteResult([], "No ComfyUI queued prompts to delete.");
            }

            var options = ValidateOptions();
            var body = new JsonObject
            {
                ["delete"] = new JsonArray(uniquePromptIds.Select(promptId => JsonValue.Create(promptId)).ToArray<JsonNode?>())
            };
            var endpoint = new Uri(new Uri(EnsureTrailingSlash(options.Endpoint)), "queue");
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(body.ToJsonString(DraftJson.Options), Encoding.UTF8, "application/json")
            };
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"ComfyUI queue delete failed: HTTP {(int)response.StatusCode} {responseText}");
            }

            return new ComfyQueueDeleteResult(uniquePromptIds, $"Requested deletion for {uniquePromptIds.Length} ComfyUI queued prompt(s).", responseText);
        }
        catch (Exception ex) when (ExternalBoundary.ShouldWrap(ex))
        {
            throw new ExternalIntegrationException("ComfyUI", "queue_delete", $"ComfyUI queue delete failed: {ex.Message}", ex);
        }
    }

    public async Task<string> DownloadOutputAsync(string outputReference, string destinationDirectory, CancellationToken cancellationToken = default)
    {
        try
        {
            var options = ValidateOptions();
            var parsed = ComfyOutputReference.Parse(outputReference);
            Directory.CreateDirectory(destinationDirectory);
            var endpoint = new Uri(new Uri(EnsureTrailingSlash(options.Endpoint)), $"view?{parsed.ToViewQuery()}");
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"ComfyUI output download failed: HTTP {(int)response.StatusCode} {responseText}");
            }

            var destinationPath = AllocateOutputPath(destinationDirectory, parsed.Filename);
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(destination, cancellationToken);
            return destinationPath;
        }
        catch (Exception ex) when (ExternalBoundary.ShouldWrap(ex))
        {
            throw new ExternalIntegrationException("ComfyUI", "download_output", $"ComfyUI output download failed: {ex.Message}", ex);
        }
    }

    private async Task<ComfyPromptStatusResult?> TryGetHistoryAsync(ComfyUiOptions options, string promptId, CancellationToken cancellationToken)
    {
        var endpoint = new Uri(new Uri(EnsureTrailingSlash(options.Endpoint)), $"history/{Uri.EscapeDataString(promptId)}");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"ComfyUI history query failed: HTTP {(int)response.StatusCode} {responseText}");
        }

        return ComfyPromptResponseParser.TryParseHistory(promptId, responseText);
    }

    private async Task<ComfyPromptStatusResult?> TryGetQueueStatusAsync(ComfyUiOptions options, string promptId, CancellationToken cancellationToken)
    {
        var endpoint = new Uri(new Uri(EnsureTrailingSlash(options.Endpoint)), "queue");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"ComfyUI queue query failed: HTTP {(int)response.StatusCode} {responseText}");
        }

        return ComfyPromptResponseParser.TryParseQueue(promptId, responseText);
    }

    private static string ResolveWorkflowPath(GenerationTaskType taskType, ComfyUiOptions options)
    {
        if (taskType != GenerationTaskType.Tts)
        {
            throw new NotSupportedException("Only TTS ComfyUI workflows are supported.");
        }

        var relativePath = options.TtsWorkflowPath ?? "IndexTTS-2.json";
        if (Path.IsPathRooted(relativePath))
        {
            return relativePath;
        }

        var outputCandidate = Path.Combine(AppContext.BaseDirectory, relativePath);
        if (File.Exists(outputCandidate))
        {
            return outputCandidate;
        }

        return Path.Combine(FindWorkspaceRoot(AppContext.BaseDirectory), relativePath);
    }

    private static IReadOnlyList<ComfyVoiceReference> ParseCharacterVoiceReferences(string responseText)
    {
        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;
        if (root.TryGetProperty("CharacterVoicesNode", out var node))
        {
            root = node;
        }

        if (!root.TryGetProperty("input", out var input) ||
            !input.TryGetProperty("required", out var required) ||
            !required.TryGetProperty("voice_name", out var voiceNameInput) ||
            voiceNameInput.ValueKind != JsonValueKind.Array ||
            voiceNameInput.GetArrayLength() == 0)
        {
            return [];
        }

        var voiceNames = voiceNameInput[0].ValueKind == JsonValueKind.Array
            ? voiceNameInput[0].EnumerateArray()
            : voiceNameInput.EnumerateArray();
        var results = new List<ComfyVoiceReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var voiceElement in voiceNames)
        {
            if (voiceElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var voiceName = voiceElement.GetString()?.Trim().Replace('\\', '/') ?? string.Empty;
            if (!TtsTaskValidation.IsSafeComfyVoiceFileName(voiceName) ||
                string.Equals(voiceName, "none", StringComparison.OrdinalIgnoreCase) ||
                IsPluginExampleVoiceReference(voiceName) ||
                !seen.Add(voiceName))
            {
                continue;
            }

            results.Add(new ComfyVoiceReference(voiceName, null));
        }

        return results;
    }

    private static bool IsPluginExampleVoiceReference(string value)
    {
        return value.StartsWith("voices_examples/", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("/voices_examples/", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("custom_nodes/", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("/custom_nodes/", StringComparison.OrdinalIgnoreCase);
    }

    private ComfyUiOptions ValidateOptions()
    {
        var options = _optionsFactory();
        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            throw new InvalidOperationException("ComfyUI endpoint is required.");
        }

        _ = new Uri(EnsureTrailingSlash(options.Endpoint));
        return options;
    }

    private static string FindWorkspaceRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DraftAgent.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static string EnsureTrailingSlash(string value)
    {
        return value.EndsWith('/') ? value : $"{value}/";
    }

    private static string AllocateOutputPath(string destinationDirectory, string filename)
    {
        var publicFileName = MediaFileNamePolicy.PublicFileNameFromSource(filename);
        var name = Path.GetFileNameWithoutExtension(publicFileName);
        var extension = Path.GetExtension(publicFileName);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "comfy-output";
        }

        var candidate = Path.Combine(destinationDirectory, $"{name}{extension}");
        var index = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(destinationDirectory, $"{name}_{index}{extension}");
            index++;
        }

        return candidate;
    }
}
