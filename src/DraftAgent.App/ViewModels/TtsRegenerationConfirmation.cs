namespace DraftAgent.App.ViewModels;

public sealed record TtsRegenerationConfirmation(
    int TotalCount,
    int CompletedCount,
    int UnfinishedCount,
    int FailedCount)
{
    public string Message =>
        $"已有 {CompletedCount}/{TotalCount} 条语音完成，仍有 {UnfinishedCount} 条未完成、{FailedCount} 条失败。是否重新生成已完成的语音？";
}

internal sealed record TtsRegenerationDecision(
    bool RequiresConfirmation,
    bool RegenerateCompleted,
    bool ResubmitFailed,
    bool HasCompleted,
    bool HasUnfinishedOrFailed,
    TtsRegenerationConfirmation Confirmation);
