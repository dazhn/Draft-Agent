using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using DraftAgent.App.Psd.Services;
using DraftAgent.App.Psd.ViewModels;
using DraftAgent.App.Psd.Views;
using DraftAgent.App.ViewModels;
using DraftAgent.Core;
using PsdLayerLoader.Core;

namespace DraftAgent.App;

public sealed record PsdLayerComponentOptions(
    string ComponentExecutablePath,
    TimeSpan Timeout);

public sealed record PsdLayerComponentBindingRequest(
    BindingTargetKind TargetKind,
    string TargetId,
    string TargetName,
    string CharacterId,
    string CharacterName,
    string? CharacterRole,
    string PsdAssetId,
    string PsdPath,
    string Emotion,
    string Action,
    string? Text,
    string SceneId,
    string InteractionId,
    PsdExportBounds Bounds = PsdExportBounds.CanvasBounds,
    PsdRenderSelection? PreviousSelection = null,
    PsdRenderSelection? SelectionOverride = null,
    PsdComponentMode Mode = PsdComponentMode.Headless);

public sealed record PsdLayerComponentBindingResult(
    PsdLayerResponse Response,
    ProjectMediaAsset ImportedAsset,
    WorkspaceProjectManifest Manifest);

public interface IPsdLayerComponentRunner
{
    Task<PsdLayerResponse> RunAsync(PsdLayerRequest request, CancellationToken cancellationToken = default);
}

public interface IPsdLayerComponentClient
{
    Task<PsdLayerComponentBindingResult> GenerateAndImportAsync(
        WorkspaceProject workspace,
        WorkspaceProjectManifest manifest,
        PsdLayerComponentBindingRequest bindingRequest,
        PsdLayerComponentOptions options,
        CancellationToken cancellationToken = default);
}

public sealed class PsdLayerSelectionCancelledException : OperationCanceledException
{
    public PsdLayerSelectionCancelledException(string requestId)
        : base($"PSD layer selection was cancelled for request {requestId}.")
    {
        RequestId = requestId;
    }

    public string RequestId { get; }
}

public sealed class PsdLayerComponentClient : IPsdLayerComponentClient
{
    private const string RequestDirectoryName = "psd-requests";

    private readonly IProjectMediaAssetService _assetService;
    private readonly IPsdLayerComponentRunner _runner;

    public PsdLayerComponentClient(
        IProjectMediaAssetService assetService,
        IPsdLayerComponentRunner? runner = null)
    {
        _assetService = assetService;
        _runner = runner ?? new IntegratedPsdLayerComponentRunner();
    }

    public async Task<PsdLayerComponentBindingResult> GenerateAndImportAsync(
        WorkspaceProject workspace,
        WorkspaceProjectManifest manifest,
        PsdLayerComponentBindingRequest bindingRequest,
        PsdLayerComponentOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(bindingRequest);
        ArgumentNullException.ThrowIfNull(options);

        var requestId = $"psd_{SanitizeFileName(bindingRequest.TargetId)}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var requestDirectory = Path.Combine(workspace.Folders.Logs, RequestDirectoryName, requestId);
        var outputDirectory = Path.Combine(requestDirectory, "output");
        Directory.CreateDirectory(outputDirectory);

        var outputFileName = $"{SanitizeFileName(bindingRequest.TargetName)}_{bindingRequest.SceneId}_{bindingRequest.InteractionId}.png";
        var request = PsdLayerRequest.Create(
            requestId,
            bindingRequest.Mode,
            workspace.RootPath,
            new PsdCharacterContext(bindingRequest.CharacterId, bindingRequest.CharacterName, bindingRequest.CharacterRole),
            new PsdAssetReference(bindingRequest.PsdAssetId, bindingRequest.PsdPath),
            new PsdLayerIntent(
                bindingRequest.Emotion,
                bindingRequest.Action,
                bindingRequest.Text,
                bindingRequest.SceneId,
                bindingRequest.InteractionId),
            new PsdOutputOptions(outputDirectory, outputFileName, bindingRequest.Bounds),
            BuildSelectionHint(bindingRequest.SelectionOverride ?? bindingRequest.PreviousSelection));

        var response = await RunWithTimeoutAsync(request, options.Timeout, cancellationToken);
        ValidateResponseRequestId(response, requestId);
        if (response.Status == PsdLayerResponseStatus.Cancelled)
        {
            throw new PsdLayerSelectionCancelledException(requestId);
        }

        if (response.Status != PsdLayerResponseStatus.Completed || response.Png is null)
        {
            var message = response.Error?.Message ?? $"PSD layer selection failed with status {response.Status}.";
            throw new InvalidOperationException(message);
        }

        ValidateCompletedSource(response, bindingRequest);
        ValidateCompletedPng(response.Png);
        var import = await _assetService.ImportAsync(
            workspace,
            BuildImportRequest(bindingRequest, response),
            cancellationToken);

        return new PsdLayerComponentBindingResult(response, import.Asset, import.Manifest);
    }

    public static MediaImportRequest BuildImportRequest(
        PsdLayerComponentBindingRequest bindingRequest,
        PsdLayerResponse response)
    {
        if (response.Png is null)
        {
            throw new InvalidOperationException("Completed PSD layer responses require a PNG result.");
        }

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["source"] = "PsdLayerLoader",
            ["sourceRequestId"] = response.RequestId,
            ["sourcePsdAssetId"] = response.Metadata?.SourcePsdAssetId ?? bindingRequest.PsdAssetId,
            ["sourcePsdPath"] = response.Metadata?.SourcePsdPath ?? bindingRequest.PsdPath,
            ["sourceFingerprint"] = response.Metadata?.SourceFingerprint ?? string.Empty,
            ["renderCacheKey"] = response.Metadata?.RenderCacheKey ?? string.Empty,
            ["sourceEmotion"] = response.Match?.Emotion ?? bindingRequest.Emotion,
            ["sourceAction"] = response.Match?.Action ?? bindingRequest.Action,
            ["matchedPresetId"] = response.Match?.MatchedPresetId ?? string.Empty,
            ["matchedLayerIds"] = string.Join(",", response.Match?.LayerIds ?? Array.Empty<int>()),
            ["matchedLayerNames"] = string.Join(",", response.Match?.LayerNames ?? Array.Empty<string>()),
            ["matchConfidence"] = (response.Match?.Confidence ?? 0).ToString("0.###", CultureInfo.InvariantCulture),
            ["exportBounds"] = response.Png.Bounds.ToString(),
            ["bindingKind"] = bindingRequest.TargetKind.ToString()
        };

        return new MediaImportRequest(
            response.Png.Path,
            MediaAssetKind.Image,
            MediaAssetSource.ImportedPng,
            bindingRequest.TargetId,
            Path.GetFileName(response.Png.Path),
            metadata);
    }

    public static string ResolveDefaultComponentExecutablePath()
    {
        return "DraftAgent.IntegratedPsdLayerLoader";
    }

    private async Task<PsdLayerResponse> RunWithTimeoutAsync(
        PsdLayerRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            return await _runner.RunAsync(request, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"PSD layer selection did not finish within {timeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} seconds.");
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "psd-layer" : cleaned;
    }

    private static void ValidateResponseRequestId(PsdLayerResponse response, string requestId)
    {
        if (!string.Equals(response.RequestId, requestId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("PSD layer response requestId did not match the active request.");
        }
    }

    private static void ValidateCompletedSource(
        PsdLayerResponse response,
        PsdLayerComponentBindingRequest bindingRequest)
    {
        var metadata = response.Metadata;
        if (metadata is null)
        {
            throw new InvalidOperationException("Completed PSD layer responses require source metadata.");
        }

        if (!string.Equals(metadata.SourcePsdAssetId, bindingRequest.PsdAssetId, StringComparison.Ordinal) ||
            !string.Equals(Path.GetFullPath(metadata.SourcePsdPath), Path.GetFullPath(bindingRequest.PsdPath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("PSD layer response source PSD did not match the active request.");
        }
    }

    private static void ValidateCompletedPng(PsdPngResult png)
    {
        if (!string.Equals(Path.GetExtension(png.Path), ".png", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Completed PSD layer responses must reference a .png file.");
        }

        if (!File.Exists(png.Path))
        {
            throw new FileNotFoundException("Completed PSD layer response PNG file was not found.", png.Path);
        }
    }

    private static PsdLayerSelectionHint? BuildSelectionHint(PsdRenderSelection? selection)
    {
        if (selection is null)
        {
            return null;
        }

        return new PsdLayerSelectionHint(
            selection.LayerIds,
            selection.LayerNames,
            selection.PresetId,
            selection.SourceFingerprint,
            selection.RenderCacheKey,
            selection.ControlledLayerNames,
            selection.ControlledLayerIds);
    }
}

public sealed class IntegratedPsdLayerComponentRunner : IPsdLayerComponentRunner
{
    private readonly PsdComponentRequestRunner _headlessRunner;
    private readonly Func<Window?> _ownerResolver;
    private readonly object _interactiveGate = new();
    private PsdLayerLoaderWindow? _interactiveWindow;
    private PsdLayerRequest? _activeInteractiveRequest;
    private TaskCompletionSource<PsdLayerResponse>? _activeInteractiveCompletion;

    public IntegratedPsdLayerComponentRunner()
        : this(new PsdComponentRequestRunner(), ResolveOwnerWindow)
    {
    }

    internal IntegratedPsdLayerComponentRunner(
        PsdComponentRequestRunner headlessRunner,
        Func<Window?> ownerResolver)
    {
        _headlessRunner = headlessRunner;
        _ownerResolver = ownerResolver;
    }

    public Task<PsdLayerResponse> RunAsync(PsdLayerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Mode == PsdComponentMode.Interactive
            ? RunInteractiveAsync(request, cancellationToken)
            : _headlessRunner.RunAsync(request);
    }

    private async Task<PsdLayerResponse> RunInteractiveAsync(
        PsdLayerRequest request,
        CancellationToken cancellationToken)
    {
        if (Application.Current?.ApplicationLifetime is null)
        {
            return await _headlessRunner.RunAsync(request);
        }

        var viewModel = new PsdLayerLoaderViewModel();
        await viewModel.InitializeComponentRequestAsync(request);
        cancellationToken.ThrowIfCancellationRequested();

        var completion = new TaskCompletionSource<PsdLayerResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        PsdLayerLoaderWindow window;
        var shouldShow = false;
        TaskCompletionSource<PsdLayerResponse>? replacedCompletion = null;
        PsdLayerRequest? replacedRequest = null;
        lock (_interactiveGate)
        {
            replacedCompletion = _activeInteractiveCompletion;
            replacedRequest = _activeInteractiveRequest;
            window = GetOrCreateInteractiveWindowLocked();
            _activeInteractiveRequest = request;
            _activeInteractiveCompletion = completion;
            window.Title = "PSD 图层选择";
            window.DataContext = viewModel;
            shouldShow = !window.IsVisible;
        }

        if (replacedCompletion is not null && replacedRequest is not null)
        {
            replacedCompletion.TrySetResult(PsdLayerResponse.Cancelled(replacedRequest.RequestId));
        }

        var owner = _ownerResolver();
        if (shouldShow)
        {
            if (owner is null)
            {
                window.Show();
            }
            else
            {
                window.Show(owner);
            }
        }
        else
        {
            window.Activate();
        }

        try
        {
            return await completion.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            CancelInteractiveRequest(request);
            throw;
        }
    }

    private PsdLayerLoaderWindow GetOrCreateInteractiveWindowLocked()
    {
        if (_interactiveWindow is { IsVisible: true } existing)
        {
            return existing;
        }

        var window = new PsdLayerLoaderWindow();
        _interactiveWindow = window;
        window.ComponentRequestConfirmed += OnInteractiveWindowConfirmed;
        window.ComponentRequestCancelled += OnInteractiveWindowCancelled;
        window.Closed += (_, _) => OnInteractiveWindowClosed(window);
        return window;
    }

    private async void OnInteractiveWindowConfirmed(object? sender, EventArgs e)
    {
        PsdLayerRequest? request;
        TaskCompletionSource<PsdLayerResponse>? completion;
        PsdLayerLoaderViewModel? viewModel;
        lock (_interactiveGate)
        {
            request = _activeInteractiveRequest;
            completion = _activeInteractiveCompletion;
            viewModel = _interactiveWindow?.DataContext as PsdLayerLoaderViewModel;
            _activeInteractiveRequest = null;
            _activeInteractiveCompletion = null;
        }

        if (request is null || completion is null || viewModel is null)
        {
            return;
        }

        try
        {
            completion.TrySetResult(await viewModel.BuildComponentResponseAsync(request));
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    private void OnInteractiveWindowCancelled(object? sender, EventArgs e)
    {
        CompleteActiveInteractiveAsCancelled();
    }

    private void OnInteractiveWindowClosed(PsdLayerLoaderWindow window)
    {
        CompleteActiveInteractiveAsCancelled();
        lock (_interactiveGate)
        {
            if (ReferenceEquals(_interactiveWindow, window))
            {
                _interactiveWindow = null;
            }
        }
    }

    private void CompleteActiveInteractiveAsCancelled()
    {
        PsdLayerRequest? request;
        TaskCompletionSource<PsdLayerResponse>? completion;
        lock (_interactiveGate)
        {
            request = _activeInteractiveRequest;
            completion = _activeInteractiveCompletion;
            _activeInteractiveRequest = null;
            _activeInteractiveCompletion = null;
        }

        if (request is not null)
        {
            completion?.TrySetResult(PsdLayerResponse.Cancelled(request.RequestId));
        }
    }

    private void CancelInteractiveRequest(PsdLayerRequest request)
    {
        TaskCompletionSource<PsdLayerResponse>? completion = null;
        lock (_interactiveGate)
        {
            if (ReferenceEquals(_activeInteractiveRequest, request))
            {
                completion = _activeInteractiveCompletion;
                _activeInteractiveRequest = null;
                _activeInteractiveCompletion = null;
            }
        }

        completion?.TrySetResult(PsdLayerResponse.Cancelled(request.RequestId));
    }

    private static Window? ResolveOwnerWindow()
    {
        return Application.Current?.ApplicationLifetime switch
        {
            IClassicDesktopStyleApplicationLifetime desktop =>
                desktop.Windows.FirstOrDefault(window => window.IsActive) ?? desktop.MainWindow,
            _ => null
        };
    }
}
