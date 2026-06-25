using DraftAgent.Core;

var endpoint = args.ElementAtOrDefault(0) ?? "http://127.0.0.1:8000";
var storePath = args.ElementAtOrDefault(1) ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "DraftAgent",
    "last-project.json");

using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
var service = new DraftWorkspaceService(
    new MockLlmAnalysisClient(),
    new ComfyUiHttpClient(httpClient, () => new ComfyUiOptions(endpoint)),
    new FileInteractionProjectStore(storePath));

var project = await service.LoadLastProjectAsync()
    ?? throw new InvalidOperationException($"未找到项目文件：{storePath}");

PrintSummary("before", project);
var updated = await service.ContinueTtsQueueAsync(project);
PrintSummary("after", updated);

foreach (var task in updated.GenerationTasks.Where(task => task.Type == GenerationTaskType.Tts).Take(8))
{
    task.Parameters.TryGetValue(TtsSynthesisQueueService.PromptIdParameter, out var promptId);
    task.Parameters.TryGetValue(TtsSynthesisQueueService.OutputAssetIdParameter, out var outputAssetId);
    Console.WriteLine($"{task.Id} status={task.Status} prompt={promptId} output={outputAssetId} result={task.ResultAssetId}");
}

return updated.GenerationTasks.Any(task => task.Type == GenerationTaskType.Tts && task.Status is GenerationTaskStatus.Pending or GenerationTaskStatus.Running)
    ? 2
    : 0;

static void PrintSummary(string label, InteractionProject project)
{
    var tasks = project.GenerationTasks.Where(task => task.Type == GenerationTaskType.Tts).ToArray();
    Console.WriteLine(
        $"{label} total={tasks.Length} completed={tasks.Count(task => task.Status == GenerationTaskStatus.Completed)} " +
        $"pending={tasks.Count(task => task.Status == GenerationTaskStatus.Pending)} running={tasks.Count(task => task.Status == GenerationTaskStatus.Running)} " +
        $"failed={tasks.Count(task => task.Status == GenerationTaskStatus.Failed)} " +
        $"withOutput={tasks.Count(task => task.Parameters.ContainsKey(TtsSynthesisQueueService.OutputAssetIdParameter))}");
}
