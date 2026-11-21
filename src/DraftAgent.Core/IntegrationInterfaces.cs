namespace DraftAgent.Core;

public interface ILlmAnalysisClient
{
    Task<LlmAnalysisResponse> AnalyzeAsync(LlmAnalysisRequest request, CancellationToken cancellationToken = default);
}

public interface IAiConnectionClient
{
    Task<AiConnectionCheckResult> CheckConnectionAsync(CancellationToken cancellationToken = default);
}

public interface IAiTextGenerationClient
{
    Task<AiModelTextResponse> GenerateTextAsync(AiModelTextRequest request, CancellationToken cancellationToken = default);
}

public interface IAiModelClient : IAiConnectionClient, IAiTextGenerationClient
{
    Task<AiModelJsonResponse> GenerateJsonAsync(AiModelJsonRequest request, CancellationToken cancellationToken = default);
}

public interface IComfyUiClient
{
    Task<ComfyTaskResult> SubmitAsync(ComfyTaskRequest request, CancellationToken cancellationToken = default);
}

public interface IComfyConnectionClient
{
    Task<ComfyConnectionCheckResult> CheckComfyConnectionAsync(CancellationToken cancellationToken = default);
}

public interface IComfyPromptStatusClient
{
    Task<ComfyPromptStatusResult> GetPromptStatusAsync(string promptId, CancellationToken cancellationToken = default);
}

public interface IComfyQueueControlClient
{
    Task<ComfyQueueDeleteResult> DeleteQueuedPromptsAsync(IReadOnlyList<string> promptIds, CancellationToken cancellationToken = default);
}

public interface IComfyOutputFileClient
{
    Task<string> DownloadOutputAsync(string outputReference, string destinationDirectory, CancellationToken cancellationToken = default);
}

public interface IComfyVoiceCatalogClient
{
    Task<ComfyVoiceCatalogResult> GetVoiceCatalogAsync(CancellationToken cancellationToken = default);
}

public interface IInteractionProjectStore
{
    Task SaveAsync(InteractionProject project, CancellationToken cancellationToken = default);

    Task<InteractionProject?> LoadLastAsync(CancellationToken cancellationToken = default);
}

public interface IWorkspaceProjectStateStore
{
    Task SaveAsync(WorkspaceProjectState state, CancellationToken cancellationToken = default);

    Task<WorkspaceProjectState?> LoadAsync(CancellationToken cancellationToken = default);
}

public sealed record AiServiceOptions(string Provider, string ApiKey, string BaseUrl, string ModelName, int MaxOutputTokens = 65536);

public sealed record ComfyUiOptions(string Endpoint, string? TtsWorkflowPath = null);

public sealed record ComfyVoiceReference(string VoiceName, string? ReferenceText);

public sealed record ComfyVoiceCatalogResult(IReadOnlyList<ComfyVoiceReference> Voices);

public sealed record WorkspaceProjectState(string RootPath);

public sealed record AiModelJsonRequest(string SchemaName, string SystemPrompt, string UserText);

public sealed record AiModelTextRequest(string SystemPrompt, string UserText);

public sealed record AiModelJsonResponse(
    string Json,
    string RawContent,
    AiModelReplyMetadata ReplyMetadata,
    string? RawResponse = null,
    string? ReasoningContent = null);

public sealed record AiModelTextResponse(
    string Text,
    string RawContent,
    AiModelReplyMetadata ReplyMetadata,
    string? RawResponse = null,
    string? ReasoningContent = null);

public sealed record AiConnectionCheckResult(
    string Provider,
    string ModelName,
    string BaseUrl,
    AiConnectionState State,
    int? HttpStatusCode,
    string Message);

public enum AiConnectionState
{
    Unknown,
    Connected,
    InvalidConfiguration,
    Unauthorized,
    Unreachable,
    Failed
}

public sealed record AiModelReplyMetadata(
    string Provider,
    string ModelName,
    string? FinishReason,
    AiReplyState State,
    string Message,
    int ContentLength = 0,
    int ReasoningLength = 0);

public enum AiReplyState
{
    Unknown,
    Completed,
    LengthLimited,
    ContentFiltered,
    ToolCallsOnly,
    ReasoningOnly,
    ReasoningLengthLimited,
    EmptyContent,
    InvalidJson,
    RecoveredFromReasoning,
    Failed
}

public sealed record ComfyConnectionCheckResult(
    string Endpoint,
    ComfyConnectionState State,
    int? HttpStatusCode,
    string Message);

public enum ComfyConnectionState
{
    Unknown,
    Connected,
    InvalidConfiguration,
    Unreachable,
    Failed
}

public sealed record ComfyPromptMetadata(
    string? PromptId,
    int? QueueNumber,
    ComfySubmitState State,
    string Message);

public enum ComfySubmitState
{
    Unknown,
    Submitted,
    MissingPromptId,
    Failed
}

public sealed record ComfyPromptStatusResult(
    string PromptId,
    ComfyPromptState State,
    GenerationTaskStatus TaskStatus,
    int? QueuePosition,
    double? Progress,
    IReadOnlyList<string> OutputAssetIds,
    string Message,
    string? RawJson = null);

public sealed record ComfyQueueDeleteResult(
    IReadOnlyList<string> PromptIds,
    string Message,
    string? RawResponse = null);

public enum ComfyPromptState
{
    Unknown,
    Queued,
    Running,
    Completed,
    Failed,
    NotFound
}

public sealed class AiReplyException : Exception
{
    public AiReplyException(
        AiModelReplyMetadata metadata,
        string message,
        Exception? innerException = null,
        string? rawResponse = null,
        string? content = null,
        string? reasoningContent = null)
        : base(message, innerException)
    {
        Metadata = metadata;
        RawResponse = rawResponse;
        Content = content;
        ReasoningContent = reasoningContent;
    }

    public AiModelReplyMetadata Metadata { get; }

    public string? RawResponse { get; }

    public string? Content { get; }

    public string? ReasoningContent { get; }
}

public sealed class ComfyPromptException : Exception
{
    public ComfyPromptException(ComfyPromptMetadata metadata, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Metadata = metadata;
    }

    public ComfyPromptMetadata Metadata { get; }
}

public sealed class ExternalIntegrationException : Exception
{
    public ExternalIntegrationException(string integration, string operation, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Integration = integration;
        Operation = operation;
    }

    public string Integration { get; }

    public string Operation { get; }
}
