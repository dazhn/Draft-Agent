using System.IO;
using System.Threading.Tasks;
using DraftAgent.App.Psd.ViewModels;
using PsdLayerLoader.Core;

namespace DraftAgent.App.Psd.Services;

public sealed class PsdInteractiveRequestSession
{
    public PsdInteractiveRequestSession(PsdLayerRequest request, string responsePath)
    {
        Request = request;
        ResponsePath = responsePath;
    }

    public PsdLayerRequest Request { get; }

    public string ResponsePath { get; }

    public async Task InitializeAsync(PsdLayerLoaderViewModel viewModel)
    {
        await viewModel.InitializeComponentRequestAsync(Request);
    }

    public async Task<PsdLayerResponse> ConfirmAsync(PsdLayerLoaderViewModel viewModel)
    {
        var response = await viewModel.BuildComponentResponseAsync(Request);
        await WriteResponseAsync(response);
        return response;
    }

    public Task CancelAsync()
    {
        return WriteResponseAsync(PsdLayerResponse.Cancelled(Request.RequestId));
    }

    private async Task WriteResponseAsync(PsdLayerResponse response)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ResponsePath) ?? ".");
        await File.WriteAllTextAsync(ResponsePath, PsdComponentProtocol.SerializeResponse(response));
    }
}

