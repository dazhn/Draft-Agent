using System.Text.Json;

namespace DraftAgent.Core;

public sealed class MockLlmAnalysisClient : ILlmAnalysisClient, IAiConnectionClient
{
    private readonly SceneInteractionBuilder _builder = new();

    public Task<LlmAnalysisResponse> AnalyzeAsync(LlmAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        var project = _builder.Build(request.RawText);
        var rawJson = JsonSerializer.Serialize(project, DraftJson.Options);
        return Task.FromResult(new LlmAnalysisResponse(
            rawJson,
            project,
            rawJson,
            new AiModelReplyMetadata("Mock", "mock-model", "stop", AiReplyState.Completed, "Mock AI 返回完成。")));
    }

    public Task<AiConnectionCheckResult> CheckConnectionAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AiConnectionCheckResult(
            "Mock",
            "mock-model",
            "mock://local",
            AiConnectionState.Connected,
            200,
            "Mock AI connected."));
    }
}

public sealed class MockComfyUiClient : IComfyUiClient, IComfyConnectionClient, IComfyVoiceCatalogClient
{
    public Task<ComfyTaskResult> SubmitAsync(ComfyTaskRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Task.Type != GenerationTaskType.Tts)
        {
            throw new NotSupportedException("Mock ComfyUI client only supports TTS tasks.");
        }

        var resultAssetId = $"asset_tts_{request.Task.TargetId}";
        return Task.FromResult(new ComfyTaskResult(request.Task.Id, GenerationTaskStatus.Completed, resultAssetId, "mock completed"));
    }

    public Task<ComfyConnectionCheckResult> CheckComfyConnectionAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ComfyConnectionCheckResult(
            "mock://comfy",
            ComfyConnectionState.Connected,
            200,
            "Mock ComfyUI connected."));
    }

    public Task<ComfyVoiceCatalogResult> GetVoiceCatalogAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ComfyVoiceCatalogResult([]));
    }
}

public sealed class InMemoryInteractionProjectStore : IInteractionProjectStore
{
    private InteractionProject? _lastProject;

    public Task SaveAsync(InteractionProject project, CancellationToken cancellationToken = default)
    {
        _lastProject = project;
        return Task.CompletedTask;
    }

    public Task<InteractionProject?> LoadLastAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_lastProject);
    }
}
