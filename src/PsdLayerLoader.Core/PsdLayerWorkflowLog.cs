namespace PsdLayerLoader.Core;

public static class PsdLayerWorkflowLog
{
    public const string Step = "psd_layer_loader";

    public const string RequestWritten = "psd_request_written";
    public const string RequestLoaded = "psd_request_loaded";
    public const string DocumentOpenStarted = "psd_document_open_started";
    public const string DocumentOpenCompleted = "psd_document_open_completed";
    public const string MatchStarted = "psd_layer_match_started";
    public const string MatchCompleted = "psd_layer_match_completed";
    public const string ExportStarted = "psd_png_export_started";
    public const string ExportCompleted = "psd_png_export_completed";
    public const string ResponseWritten = "psd_response_written";
    public const string RequestCancelled = "psd_request_cancelled";
    public const string RequestFailed = "psd_request_failed";
}
