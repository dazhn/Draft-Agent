using DraftAgent.App.Psd.ViewModels;

namespace DraftAgent.App.Tests.PsdInternalLoader;

public sealed class PsdLayerLoaderLazyLoadTests
{
    [Fact]
    public async Task LoadPsdDoesNotGenerateEveryLayerThumbnailUpFront()
    {
        var viewModel = new PsdLayerLoaderViewModel();

        await viewModel.LoadPsdAsync(GetSamplePsdPath());

        var thumbnailsWithBitmap = GetAllThumbnails(viewModel)
            .Count(thumbnail => thumbnail.ThumbnailBitmap is not null);

        Assert.True(thumbnailsWithBitmap <= viewModel.LayerThumbnails.Count);
    }

    private static IReadOnlyList<PsdLayerThumbnailViewModel> GetAllThumbnails(PsdLayerLoaderViewModel viewModel)
    {
        return viewModel.AllLayerThumbnails;
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


