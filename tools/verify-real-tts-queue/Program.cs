using DraftAgent.Core;

var endpoint = args.ElementAtOrDefault(0) ?? "http://127.0.0.1:8000";
var workflowPath = args.ElementAtOrDefault(1) ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "IndexTTS-2.json"));
var text = args.ElementAtOrDefault(2) ?? "我来晚了。";
var voice = args.ElementAtOrDefault(3) ?? IndexTts2Defaults.DefaultVoiceReferenceId;

var task = new GenerationTask(
    "tts_verify_001",
    GenerationTaskType.Tts,
    "int_verify_001",
    "真实队列验证",
    "comfyui",
    GenerationTaskStatus.Draft,
    new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["text"] = text,
        ["emotion"] = "紧张",
        ["voiceReferenceId"] = voice,
        ["modelPath"] = "local:IndexTTS-2"
    });

var project = new InteractionProject(
    "verify_project",
    "queue verification",
    new ScriptDocument(text, text, []),
    [],
    [],
    [task],
    []);

using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
var comfy = new ComfyUiHttpClient(httpClient, () => new ComfyUiOptions(endpoint, workflowPath));
var logger = new InMemoryWorkflowLogger();
var queue = new TtsSynthesisQueueService(comfy, logger);

var updated = await queue.EnqueueAllAsync(project, async (progress, _) =>
{
    var current = progress.Task;
    current.Parameters.TryGetValue(TtsSynthesisQueueService.PromptIdParameter, out var promptId);
    current.Parameters.TryGetValue(TtsSynthesisQueueService.OutputAssetIdParameter, out var outputAssetId);
    Console.WriteLine($"progress status={current.Status} promptId={promptId} result={current.ResultAssetId} output={outputAssetId}");
    await Task.CompletedTask;
});

var deadline = DateTimeOffset.UtcNow.AddMinutes(15);
while (DateTimeOffset.UtcNow < deadline)
{
    var current = updated.GenerationTasks.Single(task => task.Id == "tts_verify_001");
    if (current.Status is GenerationTaskStatus.Completed or GenerationTaskStatus.Failed)
    {
        break;
    }

    await Task.Delay(TimeSpan.FromSeconds(2));
    updated = await queue.ContinueAsync(updated, new TtsQueueOptions(MaxTasksToProcess: 1), async (progress, _) =>
    {
        var task = progress.Task;
        task.Parameters.TryGetValue(TtsSynthesisQueueService.PromptIdParameter, out var promptId);
        task.Parameters.TryGetValue(TtsSynthesisQueueService.OutputAssetIdParameter, out var outputAssetId);
        Console.WriteLine($"progress status={task.Status} promptId={promptId} result={task.ResultAssetId} output={outputAssetId}");
        await Task.CompletedTask;
    });
}

var result = updated.GenerationTasks.Single(task => task.Id == "tts_verify_001");
result.Parameters.TryGetValue(TtsSynthesisQueueService.PromptIdParameter, out var finalPromptId);
result.Parameters.TryGetValue(TtsSynthesisQueueService.OutputAssetIdParameter, out var finalOutputAsset);
result.Parameters.TryGetValue(TtsSynthesisQueueService.FailureMessageParameter, out var failure);

Console.WriteLine($"final status={result.Status}");
Console.WriteLine($"promptId={finalPromptId}");
Console.WriteLine($"resultAssetId={result.ResultAssetId}");
Console.WriteLine($"outputAssetId={finalOutputAsset}");
if (!string.IsNullOrWhiteSpace(failure))
{
    Console.WriteLine($"failure={failure}");
}

foreach (var entry in logger.Entries)
{
    Console.WriteLine($"log level={entry.Level} step={entry.Step} operation={entry.Operation} message={entry.Message}");
}

if (result.Status != GenerationTaskStatus.Completed || string.IsNullOrWhiteSpace(finalOutputAsset))
{
    return 2;
}

return 0;
