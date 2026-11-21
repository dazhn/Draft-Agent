using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PsdLayerLoader.Core;

namespace DraftAgent.App.Psd.Services;

public sealed class PsdComponentRequestRunner
{
    private readonly PsdLayerCore _core;
    private readonly PsdLayerSelectionResolver _selectionResolver;
    private readonly PsdLayerMatchService _matcher;

    public PsdComponentRequestRunner()
        : this(new PsdLayerCore(), new PsdLayerMatchService())
    {
    }

    public PsdComponentRequestRunner(NativePsdDocumentReader reader, PsdLayerMatchService matcher)
        : this(new PsdLayerCore(reader), matcher)
    {
    }

    internal PsdComponentRequestRunner(PsdLayerCore core, PsdLayerMatchService matcher)
    {
        _core = core;
        _matcher = matcher;
        _selectionResolver = new PsdLayerSelectionResolver(matcher);
    }

    public async Task<PsdLayerResponse> RunAsync(PsdLayerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return await Task.Run(() => Run(request));
        }
        catch (Exception ex)
        {
            return PsdLayerResponse.Failed(request.RequestId, "psd_component_failed", ex.Message);
        }
    }

    private PsdLayerResponse Run(PsdLayerRequest request)
    {
        var psdPath = ResolvePath(request.ProjectRoot, request.Psd.Path);
        var snapshot = _core.Load(psdPath);
        var selection = _selectionResolver.ResolveForRequest(snapshot, request);
        _core.SetVisibleLayers(selection.VisibleLayerIds);
        var match = selection.Match is not null && selection.Match.LayerIds.Count > 0
            ? selection.Match
            : _core.BuildMatchFromCurrent(
                request,
                selection.Match is null ? 1.0 : selection.Match.Confidence,
                request.SelectionHint?.PresetId ?? selection.Match?.MatchedPresetId);
        var outputDirectory = ResolvePath(request.ProjectRoot, request.Output.Directory);
        var outputPath = BuildOutputPath(outputDirectory, request.Output.FileNameHint);
        var export = _core.ExportVisiblePngAsync(outputPath, request.Output.Bounds).GetAwaiter().GetResult();
        return _core.BuildCompletedResponse(request, export, match);
    }

    public async Task<PsdLayerResponse> RunFileAsync(string requestPath, string responsePath)
    {
        var requestJson = await File.ReadAllTextAsync(requestPath, Encoding.UTF8);
        var request = PsdComponentProtocol.DeserializeRequest(requestJson);
        var response = await RunAsync(request);

        Directory.CreateDirectory(Path.GetDirectoryName(responsePath) ?? ".");
        await File.WriteAllTextAsync(responsePath, PsdComponentProtocol.SerializeResponse(response), Encoding.UTF8);
        return response;
    }

    private static string ResolvePath(string projectRoot, string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(Path.Combine(projectRoot, path));
    }

    private static string BuildOutputPath(string directory, string fileNameHint)
    {
        var fileName = Path.GetFileName(fileNameHint);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "psd-export.png";
        }

        if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".png";
        }

        return Path.Combine(directory, fileName);
    }

}

