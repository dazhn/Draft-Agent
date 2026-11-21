using DraftAgent.Core;
using System;
using System.Collections.Generic;

namespace DraftAgent.App.ViewModels;

public enum WorkbenchExportFormat
{
    Fcpxml,
    JianyingDraft,
    AnimateTimeline
}

public sealed record ExportMissingVisualConfirmation(
    IReadOnlyList<ExportPreflightIssueViewModel> Issues,
    string Message,
    Action<ExportPreflightIssueViewModel>? LocateIssue = null);

public sealed record ExportDestinationRequest(
    WorkbenchExportFormat Format,
    string SuggestedName,
    string WorkspaceExportDirectory);

public sealed record ExportDestinationSelection(
    WorkbenchExportFormat Format,
    string OutputPath);

public static class WorkbenchExportFormatExtensions
{
    public static EditingProjectFormat ToEditingProjectFormat(this WorkbenchExportFormat format)
    {
        return format switch
        {
            WorkbenchExportFormat.JianyingDraft => EditingProjectFormat.JianyingDraft,
            WorkbenchExportFormat.AnimateTimeline => EditingProjectFormat.AnimateTimeline,
            _ => EditingProjectFormat.Fcpxml
        };
    }
}
