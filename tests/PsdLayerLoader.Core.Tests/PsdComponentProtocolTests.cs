using PsdLayerLoader.Core;

namespace PsdLayerLoader.Core.Tests;

public sealed class PsdComponentProtocolTests
{
    [Fact]
    public void RequestRoundTripsWithStableCamelCaseProtocol()
    {
        var request = BuildRequest();

        var json = PsdComponentProtocol.SerializeRequest(request);
        var parsed = PsdComponentProtocol.DeserializeRequest(json);

        Assert.Contains("\"schemaVersion\": \"psd_layer_request_v1\"", json);
        Assert.Contains("\"mode\": \"interactive\"", json);
        Assert.Contains("\"bounds\": \"canvasBounds\"", json);
        Assert.Equal("req_001", parsed.RequestId);
        Assert.Equal(PsdComponentMode.Interactive, parsed.Mode);
        Assert.Equal("陈默", parsed.Character.Name);
        Assert.Equal("asset_psd_chen", parsed.Psd.AssetId);
        Assert.Equal("Calm", parsed.Intent.Emotion);
        Assert.Equal("turn_left", parsed.Intent.Action);
        Assert.Equal(PsdExportBounds.CanvasBounds, parsed.Output.Bounds);
    }

    [Fact]
    public void RequestSupportsPreviewBounds()
    {
        var request = BuildRequest() with
        {
            Output = BuildRequest().Output with { Bounds = PsdExportBounds.PreviewBounds }
        };

        var parsed = PsdComponentProtocol.DeserializeRequest(PsdComponentProtocol.SerializeRequest(request));

        Assert.Equal(PsdExportBounds.PreviewBounds, parsed.Output.Bounds);
    }

    [Fact]
    public void RequestRoundTripsOptionalSelectionHint()
    {
        var request = BuildRequest() with
        {
            SelectionHint = new PsdLayerSelectionHint(
                [12, 18],
                ["Base", "Smile"],
                "preset_smile",
                "source-fp",
                "cache-key",
                ["Brow_Default", "Brow_Raised"])
        };

        var json = PsdComponentProtocol.SerializeRequest(request);
        var parsed = PsdComponentProtocol.DeserializeRequest(json);

        Assert.Contains("\"selectionHint\"", json);
        Assert.Equal([12, 18], parsed.SelectionHint?.LayerIds);
        Assert.Equal(["Base", "Smile"], parsed.SelectionHint?.LayerNames);
        Assert.Equal("preset_smile", parsed.SelectionHint?.PresetId);
        Assert.Equal("source-fp", parsed.SelectionHint?.SourceFingerprint);
        Assert.Equal("cache-key", parsed.SelectionHint?.RenderCacheKey);
        Assert.Equal(["Brow_Default", "Brow_Raised"], parsed.SelectionHint?.ControlledLayerNames);
    }

    [Fact]
    public void RequestValidationAllowsEmptySelectionHintForCoreFiltering()
    {
        var request = BuildRequest() with
        {
            SelectionHint = new PsdLayerSelectionHint([], [], null, null, null)
        };

        var parsed = PsdComponentProtocol.DeserializeRequest(PsdComponentProtocol.SerializeRequest(request));

        Assert.NotNull(parsed.SelectionHint);
        Assert.Empty(parsed.SelectionHint.LayerIds);
        Assert.Empty(parsed.SelectionHint.LayerNames);
    }

    [Fact]
    public void RequestValidationRejectsWrongSchema()
    {
        var request = BuildRequest() with { SchemaVersion = "old" };
        var json = PsdComponentProtocol.SerializeRequest(request);

        var ex = Assert.Throws<InvalidOperationException>(() => PsdComponentProtocol.DeserializeRequest(json));

        Assert.Contains("psd_layer_request_v1", ex.Message);
    }

    [Fact]
    public void RequestValidationRejectsMissingPsdPath()
    {
        var request = BuildRequest() with
        {
            Psd = new PsdAssetReference("asset_psd_chen", "")
        };
        var json = PsdComponentProtocol.SerializeRequest(request);

        var ex = Assert.Throws<InvalidOperationException>(() => PsdComponentProtocol.DeserializeRequest(json));

        Assert.Contains("Path", ex.Message);
    }

    [Fact]
    public void CompletedResponseRoundTripsWithPngMatchAndMetadata()
    {
        var response = PsdLayerResponse.Completed(
            "req_001",
            new PsdPngResult("D:/Demo/assets/images/imported/chen.png", 2048, 2048, PsdExportBounds.CanvasBounds),
            new PsdLayerMatchResult(
                0.82,
                "Calm",
                "turn_left",
                "preset_calm_turn_left",
                [12, 18, 33, 41],
                ["Base", "Eyes_Calm", "Mouth_Neutral", "Body_TurnLeft"]),
            new PsdResponseMetadata(
                "asset_psd_chen",
                "D:/Demo/assets/images/psd/chen.psd",
                "source-fp",
                "render-cache-key"));

        var json = PsdComponentProtocol.SerializeResponse(response);
        var parsed = PsdComponentProtocol.DeserializeResponse(json);

        Assert.Contains("\"status\": \"completed\"", json);
        Assert.Equal(PsdLayerResponseStatus.Completed, parsed.Status);
        Assert.Equal("D:/Demo/assets/images/imported/chen.png", parsed.Png?.Path);
        Assert.Equal(2048, parsed.Png?.Width);
        Assert.Equal(0.82, parsed.Match?.Confidence);
        Assert.Equal([12, 18, 33, 41], parsed.Match?.LayerIds);
        Assert.Equal("asset_psd_chen", parsed.Metadata?.SourcePsdAssetId);
        Assert.Equal("source-fp", parsed.Metadata?.SourceFingerprint);
        Assert.Equal("render-cache-key", parsed.Metadata?.RenderCacheKey);
    }

    [Fact]
    public void PsdVisualSourceMetadataBuildsStableFingerprintAndRenderCacheKey()
    {
        var psdPath = Path.Combine(Path.GetTempPath(), $"draft-agent-psd-protocol-{Guid.NewGuid():N}.psd");
        File.WriteAllBytes(psdPath, [1, 2, 3]);

        var fingerprint = PsdVisualSourceMetadata.BuildSourceFingerprint(" asset_psd_chen ", psdPath);
        var cacheKey = PsdVisualSourceMetadata.BuildRenderCacheKey(
            " asset_psd_chen ",
            fingerprint,
            [12, 18],
            PsdExportBounds.CanvasBounds);

        Assert.Contains("psd-fp-v2", fingerprint, StringComparison.Ordinal);
        Assert.Contains(PsdComponentProtocol.RequestSchemaVersion, fingerprint, StringComparison.Ordinal);
        Assert.Contains("asset_psd_chen", fingerprint, StringComparison.Ordinal);
        Assert.Contains(Path.GetFullPath(psdPath), fingerprint, StringComparison.Ordinal);
        Assert.Contains("psd-render-v2", cacheKey, StringComparison.Ordinal);
        Assert.Contains(fingerprint, cacheKey, StringComparison.Ordinal);
        Assert.Contains("12,18", cacheKey, StringComparison.Ordinal);
    }

    [Fact]
    public void FailedResponseAllowsEmptyPngAndMatch()
    {
        var response = PsdLayerResponse.Failed("req_001", "no_matching_layers", "没有找到匹配图层。");

        var parsed = PsdComponentProtocol.DeserializeResponse(PsdComponentProtocol.SerializeResponse(response));

        Assert.Equal(PsdLayerResponseStatus.Failed, parsed.Status);
        Assert.Null(parsed.Png);
        Assert.Null(parsed.Match);
        Assert.Equal("no_matching_layers", parsed.Error?.Code);
        Assert.Equal("没有找到匹配图层。", parsed.Error?.Message);
    }

    [Fact]
    public void CancelledResponseIsValidWithoutError()
    {
        var response = PsdLayerResponse.Cancelled("req_001");

        var parsed = PsdComponentProtocol.DeserializeResponse(PsdComponentProtocol.SerializeResponse(response));

        Assert.Equal(PsdLayerResponseStatus.Cancelled, parsed.Status);
        Assert.Null(parsed.Error);
    }

    [Fact]
    public void CompletedResponseRequiresPngMatchAndMetadata()
    {
        var response = new PsdLayerResponse(
            PsdComponentProtocol.ResponseSchemaVersion,
            "req_001",
            PsdLayerResponseStatus.Completed,
            null,
            null,
            null,
            null);

        var json = PsdComponentProtocol.SerializeResponse(response);

        var ex = Assert.Throws<InvalidOperationException>(() => PsdComponentProtocol.DeserializeResponse(json));

        Assert.Contains("png, match, and metadata", ex.Message);
    }

    private static PsdLayerRequest BuildRequest()
    {
        return PsdLayerRequest.Create(
            "req_001",
            PsdComponentMode.Interactive,
            "D:/DraftProjects/Demo",
            new PsdCharacterContext("char_chen", "陈默", "主角"),
            new PsdAssetReference("asset_psd_chen", "D:/DraftProjects/Demo/assets/images/psd/chen.psd"),
            new PsdLayerIntent("Calm", "turn_left", "他停在门口，压低声音说话。", "scene_001", "interaction_004"),
            new PsdOutputOptions(
                "D:/DraftProjects/Demo/assets/images/imported",
                "chen_scene_001_interaction_004.png",
                PsdExportBounds.CanvasBounds));
    }
}

