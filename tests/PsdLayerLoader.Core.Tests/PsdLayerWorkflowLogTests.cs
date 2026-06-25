using PsdLayerLoader.Core;

namespace PsdLayerLoader.Core.Tests;

public sealed class PsdLayerWorkflowLogTests
{
    [Fact]
    public void LogOperationsUseStablePsdLayerLoaderStep()
    {
        Assert.Equal("psd_layer_loader", PsdLayerWorkflowLog.Step);
        Assert.Equal("psd_request_written", PsdLayerWorkflowLog.RequestWritten);
        Assert.Equal("psd_layer_match_completed", PsdLayerWorkflowLog.MatchCompleted);
        Assert.Equal("psd_png_export_completed", PsdLayerWorkflowLog.ExportCompleted);
        Assert.Equal("psd_request_failed", PsdLayerWorkflowLog.RequestFailed);
    }
}

