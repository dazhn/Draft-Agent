using System.Text.Json;

namespace DraftAgent.Core;

public sealed class HttpLlmAnalysisClient : ILlmAnalysisClient, IAiConnectionClient, IAiTextGenerationClient
{
    private readonly IAiModelClient _modelClient;
    private readonly SceneInteractionBuilder _fallbackBuilder = new();

    public HttpLlmAnalysisClient(HttpClient httpClient, Func<AiServiceOptions> optionsFactory)
        : this(new HttpAiModelClient(httpClient, optionsFactory))
    {
    }

    public HttpLlmAnalysisClient(IAiModelClient modelClient)
    {
        _modelClient = modelClient;
    }

    public async Task<LlmAnalysisResponse> AnalyzeAsync(LlmAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var modelResponse = await _modelClient.GenerateJsonAsync(
                new AiModelJsonRequest(request.JsonSchemaName, request.Instructions, request.RawText),
                cancellationToken);
            var projectJson = LlmSceneFlowJsonConverter.ToInteractionProjectJson(modelResponse.Json, request.RawText, _fallbackBuilder);
            var project = JsonSerializer.Deserialize<InteractionProject>(projectJson, DraftJson.Options)
                ?? throw new InvalidOperationException("AI response could not be parsed as InteractionProject.");

            return new LlmAnalysisResponse(projectJson, project, modelResponse.RawResponse ?? modelResponse.RawContent, modelResponse.ReplyMetadata);
        }
        catch (Exception ex) when (ExternalBoundary.ShouldWrap(ex))
        {
            throw new ExternalIntegrationException("LLM", "analyze", $"AI analysis failed: {ex.Message}", ex);
        }
    }

    public Task<AiConnectionCheckResult> CheckConnectionAsync(CancellationToken cancellationToken = default)
    {
        return _modelClient.CheckConnectionAsync(cancellationToken);
    }

    public Task<AiModelTextResponse> GenerateTextAsync(AiModelTextRequest request, CancellationToken cancellationToken = default)
    {
        return _modelClient.GenerateTextAsync(request, cancellationToken);
    }
}
