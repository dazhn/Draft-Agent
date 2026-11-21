using DraftAgent.App.Psd.Services;
using PsdLayerLoader.Core;
using SkiaSharp;

namespace DraftAgent.App.Tests.PsdInternalLoader;

public sealed class PsdComponentRequestRunnerTests
{
    [Fact]
    public async Task RunFileAsyncWritesCompletedResponseAndPng()
    {
        var samplePath = GetSamplePsdPath();
        var outputRoot = Path.Combine(Path.GetTempPath(), "DraftAgent", "PsdLayerLoader.Tests", Guid.NewGuid().ToString("N"));
        var requestPath = Path.Combine(outputRoot, "request.json");
        var responsePath = Path.Combine(outputRoot, "response.json");
        var exportDirectory = Path.Combine(outputRoot, "exports");
        Directory.CreateDirectory(outputRoot);

        var request = PsdLayerRequest.Create(
            "req_sample",
            PsdComponentMode.Headless,
            outputRoot,
            new PsdCharacterContext("char_sample", "Sample", null),
            new PsdAssetReference("asset_sample_psd", samplePath),
            new PsdLayerIntent("Calm", "UpperLeft", null, "scene_001", "interaction_001"),
            new PsdOutputOptions(exportDirectory, "sample-output.png", PsdExportBounds.CanvasBounds));
        await File.WriteAllTextAsync(requestPath, PsdComponentProtocol.SerializeRequest(request));

        var response = await new PsdComponentRequestRunner().RunFileAsync(requestPath, responsePath);

        Assert.Equal(PsdLayerResponseStatus.Completed, response.Status);
        Assert.NotNull(response.Png);
        Assert.True(File.Exists(responsePath), responsePath);
        Assert.True(File.Exists(response.Png.Path), response.Png.Path);
        Assert.Equal(PsdExportBounds.CanvasBounds, response.Png.Bounds);
        Assert.NotEmpty(response.Match!.LayerIds);
        Assert.Contains("UpperLeft", response.Match.LayerNames);
        Assert.NotNull(response.Metadata);
        var metadata = response.Metadata!;
        Assert.False(string.IsNullOrWhiteSpace(metadata.SourceFingerprint));
        Assert.False(string.IsNullOrWhiteSpace(metadata.RenderCacheKey));
        var sourceFingerprint = metadata.SourceFingerprint!;
        var renderCacheKey = metadata.RenderCacheKey!;
        Assert.Contains("psd-fp-v2", sourceFingerprint, StringComparison.Ordinal);
        Assert.Contains("asset_sample_psd", sourceFingerprint, StringComparison.Ordinal);
        Assert.Contains(Path.GetFullPath(samplePath), sourceFingerprint, StringComparison.Ordinal);
        Assert.Contains("psd-render-v2", renderCacheKey, StringComparison.Ordinal);
        Assert.Contains(sourceFingerprint, renderCacheKey, StringComparison.Ordinal);

        var roundTrip = PsdComponentProtocol.DeserializeResponse(await File.ReadAllTextAsync(responsePath));
        Assert.Equal(PsdLayerResponseStatus.Completed, roundTrip.Status);
        Assert.Equal(sourceFingerprint, roundTrip.Metadata?.SourceFingerprint);
        Assert.Equal(renderCacheKey, roundTrip.Metadata?.RenderCacheKey);

        using var stream = File.OpenRead(response.Png.Path);
        using var bitmap = SKBitmap.Decode(stream);
        Assert.NotNull(bitmap);
        Assert.Equal(1024, bitmap.Width);
        Assert.Equal(1024, bitmap.Height);
    }

    [Fact]
    public async Task RunFileAsyncPrefersSelectionHintBeforeIntentMatch()
    {
        var samplePath = GetSamplePsdPath();
        var outputRoot = Path.Combine(Path.GetTempPath(), "DraftAgent", "PsdLayerLoader.Tests", Guid.NewGuid().ToString("N"));
        var requestPath = Path.Combine(outputRoot, "request.json");
        var responsePath = Path.Combine(outputRoot, "response.json");
        var exportDirectory = Path.Combine(outputRoot, "exports");
        Directory.CreateDirectory(outputRoot);

        var upperLeftLayer = new NativePsdDocumentReader()
            .Load(samplePath)
            .Layers
            .First(layer => layer.Name == "UpperLeft");
        var request = PsdLayerRequest.Create(
            "req_hint",
            PsdComponentMode.Headless,
            outputRoot,
            new PsdCharacterContext("char_sample", "Sample", null),
            new PsdAssetReference("asset_sample_psd", samplePath),
            new PsdLayerIntent("NoSuchEmotion", "NoSuchAction", null, "scene_001", "interaction_001"),
            new PsdOutputOptions(exportDirectory, "sample-output.png", PsdExportBounds.CanvasBounds),
            new PsdLayerSelectionHint([upperLeftLayer.LayerId], ["UpperLeft"], "previous_selection", "fp", "cache"));
        await File.WriteAllTextAsync(requestPath, PsdComponentProtocol.SerializeRequest(request));

        var response = await new PsdComponentRequestRunner().RunFileAsync(requestPath, responsePath);

        Assert.Equal(PsdLayerResponseStatus.Completed, response.Status);
        Assert.Equal(1.0, response.Match?.Confidence);
        Assert.Equal("previous_selection", response.Match?.MatchedPresetId);
        Assert.Contains(upperLeftLayer.LayerId, response.Match!.LayerIds);
        Assert.Contains("UpperLeft", response.Match.LayerNames);
    }

    [Fact]
    public async Task RunFileAsyncUsesLayerNamesToFillMissingHintIds()
    {
        var samplePath = GetSamplePsdPath();
        var outputRoot = Path.Combine(Path.GetTempPath(), "DraftAgent", "PsdLayerLoader.Tests", Guid.NewGuid().ToString("N"));
        var requestPath = Path.Combine(outputRoot, "request.json");
        var responsePath = Path.Combine(outputRoot, "response.json");
        var exportDirectory = Path.Combine(outputRoot, "exports");
        Directory.CreateDirectory(outputRoot);

        var nativeLayers = new NativePsdDocumentReader().Load(samplePath).Layers;
        var upperLeftLayer = nativeLayers.First(layer => layer.Name == "UpperLeft");
        var lowerRightLayer = nativeLayers.First(layer => layer.Name == "LowerRight");
        var request = PsdLayerRequest.Create(
            "req_hint_name_fill",
            PsdComponentMode.Headless,
            outputRoot,
            new PsdCharacterContext("char_sample", "Sample", null),
            new PsdAssetReference("asset_sample_psd", samplePath),
            new PsdLayerIntent("NoSuchEmotion", "NoSuchAction", null, "scene_001", "interaction_001"),
            new PsdOutputOptions(exportDirectory, "sample-output.png", PsdExportBounds.CanvasBounds),
            new PsdLayerSelectionHint([upperLeftLayer.LayerId, 999_999], ["UpperLeft", "LowerRight"], "previous_selection", "fp", "cache"));
        await File.WriteAllTextAsync(requestPath, PsdComponentProtocol.SerializeRequest(request));

        var response = await new PsdComponentRequestRunner().RunFileAsync(requestPath, responsePath);

        Assert.Equal(PsdLayerResponseStatus.Completed, response.Status);
        Assert.Contains(upperLeftLayer.LayerId, response.Match!.LayerIds);
        Assert.Contains(lowerRightLayer.LayerId, response.Match.LayerIds);
        Assert.Contains("UpperLeft", response.Match.LayerNames);
        Assert.Contains("LowerRight", response.Match.LayerNames);
    }

    [Fact]
    public async Task RunFileAsyncIgnoresInvalidHintIdsAndFailsWhenNothingRenderableRemains()
    {
        var samplePath = GetSamplePsdPath();
        var outputRoot = Path.Combine(Path.GetTempPath(), "DraftAgent", "PsdLayerLoader.Tests", Guid.NewGuid().ToString("N"));
        var requestPath = Path.Combine(outputRoot, "request.json");
        var responsePath = Path.Combine(outputRoot, "response.json");
        var exportDirectory = Path.Combine(outputRoot, "exports");
        Directory.CreateDirectory(outputRoot);

        var request = PsdLayerRequest.Create(
            "req_invalid_hint_only",
            PsdComponentMode.Headless,
            outputRoot,
            new PsdCharacterContext("char_sample", "Sample", null),
            new PsdAssetReference("asset_sample_psd", samplePath),
            new PsdLayerIntent("NoSuchEmotion", "NoSuchAction", null, "scene_001", "interaction_001"),
            new PsdOutputOptions(exportDirectory, "sample-output.png", PsdExportBounds.CanvasBounds),
            new PsdLayerSelectionHint([999_999], ["MissingLayer"], "invalid_selection", "fp", "cache"));
        await File.WriteAllTextAsync(requestPath, PsdComponentProtocol.SerializeRequest(request));

        var response = await new PsdComponentRequestRunner().RunFileAsync(requestPath, responsePath);

        Assert.Equal(PsdLayerResponseStatus.Failed, response.Status);
        Assert.Equal("no_exportable_layers", response.Error?.Code);
        Assert.True(File.Exists(responsePath), responsePath);
        Assert.False(File.Exists(Path.Combine(exportDirectory, "sample-output.png")));
    }

    [Fact]
    public async Task RunFileAsyncAllowsEmptyHintAndFailsWithoutFallingBackToDefaultLayers()
    {
        var samplePath = GetSamplePsdPath();
        var outputRoot = Path.Combine(Path.GetTempPath(), "DraftAgent", "PsdLayerLoader.Tests", Guid.NewGuid().ToString("N"));
        var requestPath = Path.Combine(outputRoot, "request.json");
        var responsePath = Path.Combine(outputRoot, "response.json");
        var exportDirectory = Path.Combine(outputRoot, "exports");
        Directory.CreateDirectory(outputRoot);

        var request = PsdLayerRequest.Create(
            "req_empty_hint",
            PsdComponentMode.Headless,
            outputRoot,
            new PsdCharacterContext("char_sample", "Sample", null),
            new PsdAssetReference("asset_sample_psd", samplePath),
            new PsdLayerIntent("UpperLeft", "UpperLeft", null, "scene_001", "interaction_001"),
            new PsdOutputOptions(exportDirectory, "sample-output.png", PsdExportBounds.CanvasBounds),
            new PsdLayerSelectionHint([], [], "empty_selection", "fp", "cache"));
        await File.WriteAllTextAsync(requestPath, PsdComponentProtocol.SerializeRequest(request));

        var response = await new PsdComponentRequestRunner().RunFileAsync(requestPath, responsePath);

        Assert.Equal(PsdLayerResponseStatus.Failed, response.Status);
        Assert.Equal("no_exportable_layers", response.Error?.Code);
        Assert.True(File.Exists(responsePath), responsePath);
        Assert.False(File.Exists(Path.Combine(exportDirectory, "sample-output.png")));
    }

    [Fact]
    public async Task RunFileAsyncKeepsDefaultVisibleLayersOutsideControlledHint()
    {
        var samplePath = GetSamplePsdPath();
        var outputRoot = Path.Combine(Path.GetTempPath(), "DraftAgent", "PsdLayerLoader.Tests", Guid.NewGuid().ToString("N"));
        var requestPath = Path.Combine(outputRoot, "request.json");
        var responsePath = Path.Combine(outputRoot, "response.json");
        var exportDirectory = Path.Combine(outputRoot, "exports");
        Directory.CreateDirectory(outputRoot);

        var nativeLayers = new NativePsdDocumentReader().Load(samplePath).Layers;
        var upperLeftLayer = nativeLayers.First(layer => layer.Name == "UpperLeft");
        var lowerRightLayer = nativeLayers.First(layer => layer.Name == "LowerRight");
        var controlledIds = new HashSet<int> { upperLeftLayer.LayerId, lowerRightLayer.LayerId };
        var defaultVisibleUncontrolledIds = nativeLayers
            .Where(layer => !layer.IsGroup && layer.IsVisible && layer.Type != 3 && layer.Width > 0 && layer.Height > 0)
            .Select(layer => layer.LayerId)
            .Where(layerId => !controlledIds.Contains(layerId))
            .ToArray();
        var request = PsdLayerRequest.Create(
            "req_hint_controlled",
            PsdComponentMode.Headless,
            outputRoot,
            new PsdCharacterContext("char_sample", "Sample", null),
            new PsdAssetReference("asset_sample_psd", samplePath),
            new PsdLayerIntent("NoSuchEmotion", "NoSuchAction", null, "scene_001", "interaction_001"),
            new PsdOutputOptions(exportDirectory, "sample-output.png", PsdExportBounds.CanvasBounds),
            new PsdLayerSelectionHint(
                [],
                ["UpperLeft"],
                "ai_structured",
                "fp",
                "cache",
                ["UpperLeft", "LowerRight"]));
        await File.WriteAllTextAsync(requestPath, PsdComponentProtocol.SerializeRequest(request));

        var response = await new PsdComponentRequestRunner().RunFileAsync(requestPath, responsePath);

        Assert.Equal(PsdLayerResponseStatus.Completed, response.Status);
        Assert.Contains(upperLeftLayer.LayerId, response.Match!.LayerIds);
        Assert.DoesNotContain(lowerRightLayer.LayerId, response.Match.LayerIds);
        Assert.All(defaultVisibleUncontrolledIds, layerId => Assert.Contains(layerId, response.Match.LayerIds));
    }

    private static string GetSamplePsdPath()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "PsdLayerLoader.Native",
            "third_party",
            "psd_sdk",
            "bin",
            "Sample.psd"));
    }
}


