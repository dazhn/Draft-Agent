namespace DraftAgent.Core;

public sealed record TtsPromptCancellationResult(
    InteractionProject Project,
    int SubmittedPromptCount,
    int DeleteRequestedCount,
    int RunningPromptCount,
    int ResetTaskCount,
    string Message);
