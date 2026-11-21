using DraftAgent.Core;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: export-animate-timeline <workspace-root> [output-directory]");
    Environment.ExitCode = 2;
    return;
}

var root = Path.GetFullPath(args[0]);
var outputDirectory = args.Length >= 2
    ? Path.GetFullPath(args[1])
    : Path.Combine(root, "exports", "Animate时间线");

var manager = new WorkspaceProjectManager();
var manifest = await manager.LoadAsync(root);
var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
var project = await manager.LoadDraftAsync(workspace)
    ?? throw new InvalidOperationException($"No workspace draft found at {workspace.DraftPath}.");

var service = new EditingProjectExportService();
var request = new EditingProjectExportRequest(
    project,
    workspace,
    EditingProjectFormat.AnimateTimeline,
    new TimelineExportOptions(AllowMissingVisuals: true),
    manifest,
    outputDirectory,
    AllowMissingVisuals: true);

var result = await service.ExportAsync(request);
Console.WriteLine(result.OutputPath);
Console.WriteLine(result.Message);
Console.WriteLine($"Referenced assets: {result.ReferencedAssetPaths.Count}");
