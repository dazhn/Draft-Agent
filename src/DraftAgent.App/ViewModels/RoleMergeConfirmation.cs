namespace DraftAgent.App.ViewModels;

public sealed record RoleMergeConfirmation(
    string SourceRoleName,
    string TargetRoleName,
    int AffectedDialogueCount);
