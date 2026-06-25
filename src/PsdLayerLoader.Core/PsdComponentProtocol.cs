using System.Text.Json;
using System.Text.Json.Serialization;

namespace PsdLayerLoader.Core;

public static class PsdComponentProtocol
{
    public const string RequestSchemaVersion = "psd_layer_request_v1";
    public const string ResponseSchemaVersion = "psd_layer_response_v1";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string SerializeRequest(PsdLayerRequest request)
    {
        return JsonSerializer.Serialize(request, JsonOptions);
    }

    public static PsdLayerRequest DeserializeRequest(string json)
    {
        var request = JsonSerializer.Deserialize<PsdLayerRequest>(json, JsonOptions)
            ?? throw new InvalidOperationException("PSD request JSON is empty.");
        request.Validate();
        return request;
    }

    public static string SerializeResponse(PsdLayerResponse response)
    {
        return JsonSerializer.Serialize(response, JsonOptions);
    }

    public static PsdLayerResponse DeserializeResponse(string json)
    {
        var response = JsonSerializer.Deserialize<PsdLayerResponse>(json, JsonOptions)
            ?? throw new InvalidOperationException("PSD response JSON is empty.");
        response.Validate();
        return response;
    }
}

public enum PsdComponentMode
{
    Interactive,
    Headless
}

public enum PsdExportBounds
{
    LayerBounds,
    CanvasBounds,
    PreviewBounds
}

public enum PsdLayerResponseStatus
{
    Completed,
    Cancelled,
    Failed
}

public sealed record PsdLayerRequest(
    string SchemaVersion,
    string RequestId,
    PsdComponentMode Mode,
    string ProjectRoot,
    PsdCharacterContext Character,
    PsdAssetReference Psd,
    PsdLayerIntent Intent,
    PsdOutputOptions Output,
    PsdLayerSelectionHint? SelectionHint = null)
{
    public static PsdLayerRequest Create(
        string requestId,
        PsdComponentMode mode,
        string projectRoot,
        PsdCharacterContext character,
        PsdAssetReference psd,
        PsdLayerIntent intent,
        PsdOutputOptions output,
        PsdLayerSelectionHint? selectionHint = null)
    {
        return new PsdLayerRequest(
            PsdComponentProtocol.RequestSchemaVersion,
            requestId,
            mode,
            projectRoot,
            character,
            psd,
            intent,
            output,
            selectionHint);
    }

    public void Validate()
    {
        RequireSchema(SchemaVersion, PsdComponentProtocol.RequestSchemaVersion, nameof(SchemaVersion));
        Require(RequestId, nameof(RequestId));
        Require(ProjectRoot, nameof(ProjectRoot));
        Character.Validate();
        Psd.Validate();
        Intent.Validate();
        Output.Validate();
        SelectionHint?.Validate();
    }

    private static void RequireSchema(string actual, string expected, string name)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{name} must be {expected}.");
        }
    }

    internal static void Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{name} is required.");
        }
    }
}

public sealed record PsdCharacterContext(string Id, string Name, string? Role)
{
    public void Validate()
    {
        PsdLayerRequest.Require(Id, nameof(Id));
        PsdLayerRequest.Require(Name, nameof(Name));
    }
}

public sealed record PsdAssetReference(string AssetId, string Path)
{
    public void Validate()
    {
        PsdLayerRequest.Require(AssetId, nameof(AssetId));
        PsdLayerRequest.Require(Path, nameof(Path));
    }
}

public sealed record PsdLayerIntent(
    string Emotion,
    string Action,
    string? Text,
    string SceneId,
    string InteractionId)
{
    public void Validate()
    {
        PsdLayerRequest.Require(Emotion, nameof(Emotion));
        PsdLayerRequest.Require(Action, nameof(Action));
        PsdLayerRequest.Require(SceneId, nameof(SceneId));
        PsdLayerRequest.Require(InteractionId, nameof(InteractionId));
    }
}

public sealed record PsdOutputOptions(
    string Directory,
    string FileNameHint,
    PsdExportBounds Bounds)
{
    public void Validate()
    {
        PsdLayerRequest.Require(Directory, nameof(Directory));
        PsdLayerRequest.Require(FileNameHint, nameof(FileNameHint));
    }
}

public sealed record PsdLayerSelectionHint(
    IReadOnlyList<int> LayerIds,
    IReadOnlyList<string> LayerNames,
    string? PresetId,
    string? SourceFingerprint,
    string? RenderCacheKey,
    IReadOnlyList<string>? ControlledLayerNames = null,
    IReadOnlyList<int>? ControlledLayerIds = null)
{
    public void Validate()
    {
    }
}

public sealed record PsdLayerResponse(
    string SchemaVersion,
    string RequestId,
    PsdLayerResponseStatus Status,
    PsdPngResult? Png,
    PsdLayerMatchResult? Match,
    PsdResponseMetadata? Metadata,
    PsdResponseError? Error)
{
    public static PsdLayerResponse Completed(
        string requestId,
        PsdPngResult png,
        PsdLayerMatchResult match,
        PsdResponseMetadata metadata)
    {
        return new PsdLayerResponse(
            PsdComponentProtocol.ResponseSchemaVersion,
            requestId,
            PsdLayerResponseStatus.Completed,
            png,
            match,
            metadata,
            null);
    }

    public static PsdLayerResponse Cancelled(string requestId)
    {
        return new PsdLayerResponse(
            PsdComponentProtocol.ResponseSchemaVersion,
            requestId,
            PsdLayerResponseStatus.Cancelled,
            null,
            null,
            null,
            null);
    }

    public static PsdLayerResponse Failed(string requestId, string code, string message)
    {
        return new PsdLayerResponse(
            PsdComponentProtocol.ResponseSchemaVersion,
            requestId,
            PsdLayerResponseStatus.Failed,
            null,
            null,
            null,
            new PsdResponseError(code, message));
    }

    public void Validate()
    {
        if (!string.Equals(SchemaVersion, PsdComponentProtocol.ResponseSchemaVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{nameof(SchemaVersion)} must be {PsdComponentProtocol.ResponseSchemaVersion}.");
        }

        PsdLayerRequest.Require(RequestId, nameof(RequestId));
        if (Status == PsdLayerResponseStatus.Completed && (Png is null || Match is null || Metadata is null))
        {
            throw new InvalidOperationException("Completed PSD responses require png, match, and metadata.");
        }

        if (Status == PsdLayerResponseStatus.Failed && Error is null)
        {
            throw new InvalidOperationException("Failed PSD responses require error.");
        }
    }
}

public sealed record PsdPngResult(string Path, int Width, int Height, PsdExportBounds Bounds);

public sealed record PsdLayerMatchResult(
    double Confidence,
    string Emotion,
    string Action,
    string? MatchedPresetId,
    IReadOnlyList<int> LayerIds,
    IReadOnlyList<string> LayerNames);

public sealed record PsdResponseMetadata(
    string SourcePsdAssetId,
    string SourcePsdPath,
    string? SourceFingerprint = null,
    string? RenderCacheKey = null);

public sealed record PsdResponseError(string Code, string Message);
