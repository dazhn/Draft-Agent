using System.Collections.Generic;
using DraftAgent.Core;

namespace DraftAgent.App.ViewModels;

internal sealed record WorkbenchCommandResult(
    WorkbenchDocument Document,
    bool Applied,
    IReadOnlyList<string> ChangedSceneIds,
    IReadOnlyList<string> ChangedRowIds,
    IReadOnlyList<string> RemovedSceneIds,
    IReadOnlyList<string> RemovedRowIds,
    AnalysisResultRowViewModel? SuggestedSelectedRow = null)
{
    public static WorkbenchCommandResult NoOp(WorkbenchDocument document)
    {
        return new WorkbenchCommandResult(document, false, [], [], [], []);
    }

    public static WorkbenchCommandResult AppliedResult(
        WorkbenchDocument document,
        IReadOnlyList<string>? changedSceneIds = null,
        IReadOnlyList<string>? changedRowIds = null,
        IReadOnlyList<string>? removedSceneIds = null,
        IReadOnlyList<string>? removedRowIds = null,
        AnalysisResultRowViewModel? suggestedSelectedRow = null)
    {
        return new WorkbenchCommandResult(
            document,
            true,
            changedSceneIds ?? [],
            changedRowIds ?? [],
            removedSceneIds ?? [],
            removedRowIds ?? [],
            suggestedSelectedRow);
    }
}

internal sealed record WorkbenchSceneMutationResult
{
    public WorkbenchSceneMutationResult(
        WorkbenchCommandResult commandResult,
        SceneTableViewModel? scene = null,
        IReadOnlyList<WorkbenchRowDraft>? removedRows = null,
        AnalysisResultRowViewModel? suggestedSelectedRow = null,
        WorkbenchBindingTargetRefresh? bindingTargetRefresh = null,
        WorkbenchDerivedStateRefresh? derivedStateRefresh = null)
    {
        CommandResult = commandResult;
        Scene = scene;
        RemovedRows = removedRows ?? [];
        CommandResult = suggestedSelectedRow is null
            ? commandResult
            : commandResult with { SuggestedSelectedRow = suggestedSelectedRow };
        BindingTargetRefresh = bindingTargetRefresh;
        DerivedStateRefresh = derivedStateRefresh ?? WorkbenchDerivedStateRefresh.Counters;
    }

    public WorkbenchCommandResult CommandResult { get; init; }

    public SceneTableViewModel? Scene { get; init; }

    public IReadOnlyList<WorkbenchRowDraft> RemovedRows { get; init; }

    public AnalysisResultRowViewModel? SuggestedSelectedRow
    {
        get => CommandResult.SuggestedSelectedRow;
        init => CommandResult = CommandResult with { SuggestedSelectedRow = value };
    }

    public WorkbenchBindingTargetRefresh? BindingTargetRefresh { get; init; }

    public WorkbenchDerivedStateRefresh DerivedStateRefresh { get; init; }
}

internal sealed record WorkbenchRowMutationResult
{
    public WorkbenchRowMutationResult(
        WorkbenchCommandResult commandResult,
        AnalysisResultRowViewModel? row = null,
        SceneTableViewModel? SourceScene = null,
        SceneTableViewModel? TargetScene = null,
        WorkbenchRowDraft? removedRow = null,
        AnalysisResultRowViewModel? suggestedSelectedRow = null,
        WorkbenchBindingTargetRefresh? bindingTargetRefresh = null,
        WorkbenchDerivedStateRefresh? derivedStateRefresh = null)
    {
        CommandResult = commandResult;
        Row = row;
        this.SourceScene = SourceScene;
        this.TargetScene = TargetScene;
        RemovedRow = removedRow;
        CommandResult = suggestedSelectedRow is null
            ? commandResult
            : commandResult with { SuggestedSelectedRow = suggestedSelectedRow };
        BindingTargetRefresh = bindingTargetRefresh;
        DerivedStateRefresh = derivedStateRefresh ?? WorkbenchDerivedStateRefresh.Counters;
    }

    public WorkbenchCommandResult CommandResult { get; init; }

    public AnalysisResultRowViewModel? Row { get; init; }

    public SceneTableViewModel? SourceScene { get; init; }

    public SceneTableViewModel? TargetScene { get; init; }

    public WorkbenchRowDraft? RemovedRow { get; init; }

    public AnalysisResultRowViewModel? SuggestedSelectedRow
    {
        get => CommandResult.SuggestedSelectedRow;
        init => CommandResult = CommandResult with { SuggestedSelectedRow = value };
    }

    public WorkbenchBindingTargetRefresh? BindingTargetRefresh { get; init; }

    public WorkbenchDerivedStateRefresh DerivedStateRefresh { get; init; }
}

internal sealed record WorkbenchSceneUpdateResult(
    WorkbenchCommandResult CommandResult,
    SceneTableViewModel? Scene = null,
    WorkbenchBindingTargetRefresh? BindingTargetRefresh = null,
    WorkbenchPreviewAssetRefresh? PreviewAssetRefresh = null,
    WorkbenchDerivedStateRefresh DerivedStateRefresh = default);

internal sealed record WorkbenchRowUpdateResult(
    WorkbenchCommandResult CommandResult,
    AnalysisResultRowViewModel? Row = null,
    WorkbenchBindingTargetRefresh? BindingTargetRefresh = null,
    WorkbenchPreviewAssetRefresh? PreviewAssetRefresh = null,
    WorkbenchDerivedStateRefresh DerivedStateRefresh = default);

internal sealed record WorkbenchBindingTargetApplyResult(
    WorkbenchCommandResult CommandResult,
    WorkbenchDerivedStateRefresh DerivedStateRefresh);

internal readonly record struct WorkbenchDerivedStateRefresh(bool RefreshCounters)
{
    public static WorkbenchDerivedStateRefresh Counters => new(true);

    public static WorkbenchDerivedStateRefresh SessionOnly => new(false);
}

internal sealed record WorkbenchBindingTargetRefresh(
    WorkbenchBindingTargetRefreshKind Kind,
    SceneTableViewModel? Scene = null,
    AnalysisResultRowViewModel? Row = null,
    WorkbenchRowDraft? PreviousRowDraft = null,
    IReadOnlyList<WorkbenchRowDraft>? RemovedRows = null,
    IReadOnlyList<SceneTableViewModel>? Scenes = null,
    IReadOnlyList<AnalysisResultRowViewModel>? Rows = null)
{
    public static WorkbenchBindingTargetRefresh InsertScene(SceneTableViewModel scene)
    {
        return new WorkbenchBindingTargetRefresh(WorkbenchBindingTargetRefreshKind.InsertScene, Scene: scene);
    }

    public static WorkbenchBindingTargetRefresh RemoveScene(SceneTableViewModel scene, IReadOnlyList<WorkbenchRowDraft> removedRows)
    {
        return new WorkbenchBindingTargetRefresh(
            WorkbenchBindingTargetRefreshKind.RemoveScene,
            Scene: scene,
            RemovedRows: removedRows);
    }

    public static WorkbenchBindingTargetRefresh SyncSceneOrder()
    {
        return new WorkbenchBindingTargetRefresh(WorkbenchBindingTargetRefreshKind.SyncSceneOrder);
    }

    public static WorkbenchBindingTargetRefresh ForSceneOrderRange(IReadOnlyList<SceneTableViewModel> scenes)
    {
        return new WorkbenchBindingTargetRefresh(WorkbenchBindingTargetRefreshKind.SceneOrderRange, Scenes: scenes);
    }

    public static WorkbenchBindingTargetRefresh ForScene(SceneTableViewModel scene)
    {
        return new WorkbenchBindingTargetRefresh(WorkbenchBindingTargetRefreshKind.Scene, Scene: scene);
    }

    public static WorkbenchBindingTargetRefresh ForRow(AnalysisResultRowViewModel row)
    {
        return new WorkbenchBindingTargetRefresh(WorkbenchBindingTargetRefreshKind.Row, Row: row);
    }

    public static WorkbenchBindingTargetRefresh ForRows(IReadOnlyList<AnalysisResultRowViewModel> rows)
    {
        return new WorkbenchBindingTargetRefresh(WorkbenchBindingTargetRefreshKind.Rows, Rows: rows);
    }

    public static WorkbenchBindingTargetRefresh ForRowSubjectChange(
        AnalysisResultRowViewModel row,
        WorkbenchRowDraft? previousRowDraft)
    {
        return new WorkbenchBindingTargetRefresh(
            WorkbenchBindingTargetRefreshKind.RowSubjectChange,
            Row: row,
            PreviousRowDraft: previousRowDraft);
    }
}

internal enum WorkbenchBindingTargetRefreshKind
{
    Scene,
    InsertScene,
    RemoveScene,
    SyncSceneOrder,
    SceneOrderRange,
    Row,
    Rows,
    RowSubjectChange
}

internal sealed record WorkbenchPreviewAssetRefresh(
    WorkbenchPreviewAssetRefreshKind Kind,
    SceneTableViewModel? Scene = null,
    AnalysisResultRowViewModel? Row = null)
{
    public static WorkbenchPreviewAssetRefresh ForScene(SceneTableViewModel scene)
    {
        return new WorkbenchPreviewAssetRefresh(WorkbenchPreviewAssetRefreshKind.Scene, Scene: scene);
    }

    public static WorkbenchPreviewAssetRefresh ForRow(AnalysisResultRowViewModel row)
    {
        return new WorkbenchPreviewAssetRefresh(WorkbenchPreviewAssetRefreshKind.Row, Row: row);
    }
}

internal enum WorkbenchPreviewAssetRefreshKind
{
    Scene,
    Row
}
