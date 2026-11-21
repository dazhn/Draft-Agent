using System.Collections.Specialized;
using DraftAgent.App.Views;
using DraftAgent.App.ViewModels;
using DraftAgent.Core;

namespace DraftAgent.App.Tests;

public sealed class WorkbenchEditingCommandTests
{
    private const string TestDefaultVoiceReferenceText = "Test default reference text.";

    [Fact]
    public void AddDialogueRowInsertsBeforeSelectedRowInSameScene()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var scene = viewModel.SceneTables.Last();
        viewModel.AddDialogueRowCommand.Execute(scene);
        var selected = scene.Rows.Single();
        viewModel.SelectRowCommand.Execute(selected);

        viewModel.AddDialogueRowCommand.Execute(scene);

        Assert.Equal(2, scene.Rows.Count);
        Assert.NotSame(selected, scene.Rows[0]);
        Assert.Same(selected, scene.Rows[1]);
        Assert.Equal(Enumerable.Range(1, scene.Rows.Count), scene.Rows.Select(row => row.Order));
        Assert.Same(scene.Rows[0], viewModel.SelectedRow);
        Assert.False(scene.Rows[0].IsNarration);
    }

    [Fact]
    public void AddDialogueRowCommandDoesNotNotifyUnaffectedSceneOrRows()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        viewModel.AddSceneCommand.Execute(null);
        var targetScene = viewModel.SceneTables[^2];
        var unrelatedScene = viewModel.SceneTables[^1];
        viewModel.AddDialogueRowCommand.Execute(targetScene);
        viewModel.AddDialogueRowCommand.Execute(unrelatedScene);
        var selectedRow = targetScene.Rows[0];
        viewModel.SelectRowCommand.Execute(selectedRow);
        var unrelatedRows = unrelatedScene.Rows.ToArray();
        var sceneNotifications = new List<string>();
        var rowNotifications = new List<string>();
        unrelatedScene.PropertyChanged += (_, args) => AddNotification(sceneNotifications, args.PropertyName);
        foreach (var row in unrelatedRows)
        {
            row.PropertyChanged += (_, args) => AddNotification(rowNotifications, args.PropertyName);
        }

        viewModel.AddDialogueRowCommand.Execute(targetScene);

        Assert.Equal(unrelatedRows, unrelatedScene.Rows);
        Assert.Empty(sceneNotifications);
        Assert.Empty(rowNotifications);
    }

    [Fact]
    public void AddSceneIsProjectedThroughWorkbenchDocument()
    {
        var viewModel = CreateViewModel();
        var initialCount = viewModel.SceneTables.Count;

        viewModel.AddSceneCommand.Execute(null);

        var scene = viewModel.SceneTables.Last();
        var document = viewModel.WorkbenchDocument;
        Assert.Equal(initialCount + 1, viewModel.SceneTables.Count);
        Assert.Equal(scene.SceneId, document.Scenes.Last().SceneId);
        Assert.Equal(viewModel.SceneTables.Count, scene.Order);
    }

    [Fact]
    public void WorkbenchSessionStartsCleanAndTracksEdits()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.IsWorkbenchDirty);
        Assert.False(viewModel.CanUndoWorkbench);
        Assert.False(viewModel.CanRedoWorkbench);
        Assert.Null(viewModel.NextUndoWorkbenchOperationName);
        Assert.Null(viewModel.NextRedoWorkbenchOperationName);

        viewModel.AddSceneCommand.Execute(null);

        Assert.True(viewModel.IsWorkbenchDirty);
        Assert.True(viewModel.CanUndoWorkbench);
        Assert.False(viewModel.CanRedoWorkbench);
        Assert.Equal(WorkbenchSessionOperationNames.AddScene, viewModel.NextUndoWorkbenchOperationName);
        Assert.Null(viewModel.NextRedoWorkbenchOperationName);
    }

    [Fact]
    public void UndoWorkbenchRevertsAddedSceneAndRedoRestoresIt()
    {
        var viewModel = CreateViewModel();
        var initialCount = viewModel.SceneTables.Count;
        var initialMetrics = viewModel.WorkbenchBindingRefreshMetrics;

        viewModel.AddSceneCommand.Execute(null);
        var addedSceneId = viewModel.SceneTables.Last().SceneId;
        Assert.True(viewModel.CanUndoWorkbench);
        Assert.True(viewModel.UndoWorkbenchCommand.CanExecute(null));

        viewModel.UndoWorkbenchCommand.Execute(null);

        Assert.Equal(initialCount, viewModel.WorkbenchDocument.Scenes.Count);
        Assert.Equal(initialCount, viewModel.SceneTables.Count);
        Assert.DoesNotContain(viewModel.SceneTables, scene => scene.SceneId == addedSceneId);
        Assert.False(viewModel.IsWorkbenchDirty);
        Assert.False(viewModel.CanUndoWorkbench);
        Assert.True(viewModel.CanRedoWorkbench);
        Assert.Null(viewModel.NextUndoWorkbenchOperationName);
        Assert.Equal(WorkbenchSessionOperationNames.AddScene, viewModel.NextRedoWorkbenchOperationName);
        Assert.Equal("workbench_undo", viewModel.WorkbenchBindingRefreshMetrics.LastFullRefreshReason);
        Assert.Equal(
            initialMetrics.FullRefreshReasons.CountFor("workbench_undo") + 1,
            viewModel.WorkbenchBindingRefreshMetrics.FullRefreshReasons.CountFor("workbench_undo"));
        Assert.Equal(
            initialMetrics.FullRefreshReasons.CountFor("workspace_derived_state"),
            viewModel.WorkbenchBindingRefreshMetrics.FullRefreshReasons.CountFor("workspace_derived_state"));

        viewModel.RedoWorkbenchCommand.Execute(null);

        Assert.Equal(initialCount + 1, viewModel.SceneTables.Count);
        Assert.Contains(viewModel.SceneTables, scene => scene.SceneId == addedSceneId);
        Assert.True(viewModel.IsWorkbenchDirty);
        Assert.True(viewModel.CanUndoWorkbench);
        Assert.False(viewModel.CanRedoWorkbench);
        Assert.Equal(WorkbenchSessionOperationNames.AddScene, viewModel.NextUndoWorkbenchOperationName);
        Assert.Null(viewModel.NextRedoWorkbenchOperationName);
        Assert.Equal("workbench_redo", viewModel.WorkbenchBindingRefreshMetrics.LastFullRefreshReason);
        Assert.Equal(
            initialMetrics.FullRefreshReasons.CountFor("workbench_redo") + 1,
            viewModel.WorkbenchBindingRefreshMetrics.FullRefreshReasons.CountFor("workbench_redo"));
        Assert.Equal(
            initialMetrics.FullRefreshReasons.CountFor("workspace_derived_state"),
            viewModel.WorkbenchBindingRefreshMetrics.FullRefreshReasons.CountFor("workspace_derived_state"));
    }

    [Fact]
    public void ProjectingCommandResultDoesNotCreateExtraUndoStep()
    {
        var viewModel = CreateViewModel();

        viewModel.AddSceneCommand.Execute(null);
        viewModel.UndoWorkbenchCommand.Execute(null);

        Assert.False(viewModel.CanUndoWorkbench);
        Assert.Equal(viewModel.Project.Scenes.Count, viewModel.WorkbenchDocument.Scenes.Count);
    }

    [Fact]
    public void UndoWorkbenchRestoresEditedRowText()
    {
        var viewModel = CreateViewModel();
        var row = viewModel.SceneTables[0].Rows[0];
        var originalText = row.LineText;

        row.LineText = "Updated line";

        Assert.Equal("Updated line", viewModel.WorkbenchDocument.RowsById[row.InteractionId].Text);

        viewModel.UndoWorkbenchCommand.Execute(null);

        Assert.Equal(originalText, row.LineText);
        Assert.Equal(originalText, viewModel.WorkbenchDocument.RowsById[row.InteractionId].Text);
        Assert.False(viewModel.IsWorkbenchDirty);

        viewModel.RedoWorkbenchCommand.Execute(null);

        Assert.Equal("Updated line", row.LineText);
        Assert.Equal("Updated line", viewModel.WorkbenchDocument.RowsById[row.InteractionId].Text);
        Assert.True(viewModel.IsWorkbenchDirty);
    }

    [Fact]
    public void ConsecutiveRowTextEditsUseSingleUndoStep()
    {
        var viewModel = CreateViewModel();
        var row = viewModel.SceneTables[0].Rows[0];
        var originalText = row.LineText;

        row.LineText = "First typed line";
        row.LineText = "Second typed line";

        Assert.True(viewModel.CanUndoWorkbench);
        Assert.Equal(WorkbenchSessionOperationNames.UpdateRowText, viewModel.NextUndoWorkbenchOperationName);
        Assert.Equal("Second typed line", viewModel.WorkbenchDocument.RowsById[row.InteractionId].Text);

        viewModel.UndoWorkbenchCommand.Execute(null);

        Assert.False(viewModel.CanUndoWorkbench);
        Assert.True(viewModel.CanRedoWorkbench);
        Assert.Equal(originalText, row.LineText);
        Assert.Equal(originalText, viewModel.WorkbenchDocument.RowsById[row.InteractionId].Text);

        viewModel.RedoWorkbenchCommand.Execute(null);

        Assert.True(viewModel.CanUndoWorkbench);
        Assert.False(viewModel.CanRedoWorkbench);
        Assert.Equal("Second typed line", row.LineText);
        Assert.Equal("Second typed line", viewModel.WorkbenchDocument.RowsById[row.InteractionId].Text);
    }

    [Fact]
    public void RowTextEditsOnDifferentRowsUseSeparateUndoSteps()
    {
        var viewModel = CreateViewModel();
        var scene = viewModel.SceneTables[0];
        var first = scene.Rows[0];
        var second = scene.Rows[1];
        var originalFirstText = first.LineText;
        var originalSecondText = second.LineText;

        first.LineText = "First row edit";
        second.LineText = "Second row edit";

        viewModel.UndoWorkbenchCommand.Execute(null);

        Assert.True(viewModel.CanUndoWorkbench);
        Assert.Equal("First row edit", first.LineText);
        Assert.Equal(originalSecondText, second.LineText);

        viewModel.UndoWorkbenchCommand.Execute(null);

        Assert.False(viewModel.CanUndoWorkbench);
        Assert.Equal(originalFirstText, first.LineText);
        Assert.Equal(originalSecondText, second.LineText);
    }

    [Fact]
    public void EditingRowTextDoesNotNotifyUnchangedWorkspaceCounters()
    {
        var viewModel = CreateViewModel();
        var row = viewModel.SceneTables[0].Rows[0];
        var notified = new List<string>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                notified.Add(args.PropertyName);
            }
        };

        row.LineText = "Updated line";

        Assert.Contains(nameof(MainWindowViewModel.IsWorkbenchDirty), notified);
        Assert.Contains(nameof(MainWindowViewModel.CanUndoWorkbench), notified);
        Assert.Contains(nameof(MainWindowViewModel.NextUndoWorkbenchOperationName), notified);
        Assert.Equal(WorkbenchSessionOperationNames.UpdateRowText, viewModel.NextUndoWorkbenchOperationName);
        Assert.DoesNotContain(nameof(MainWindowViewModel.ProjectMeta), notified);
        Assert.DoesNotContain(nameof(MainWindowViewModel.ResultRowCount), notified);
        Assert.DoesNotContain(nameof(MainWindowViewModel.NarrationCount), notified);
        Assert.DoesNotContain(nameof(MainWindowViewModel.TtsQueueMeta), notified);
    }

    [Fact]
    public void MovingRowDoesNotNotifyUnchangedWorkspaceCounters()
    {
        var viewModel = CreateViewModel();
        var scene = viewModel.SceneTables[0];
        viewModel.AddDialogueRowCommand.Execute(scene);
        var first = scene.Rows[0];
        var second = scene.Rows[1];
        var notified = new List<string>();
        viewModel.PropertyChanged += (_, args) => AddNotification(notified, args.PropertyName);
        notified.Clear();

        viewModel.MoveInteractionRow(first, second);

        Assert.True(viewModel.IsWorkbenchDirty);
        Assert.True(viewModel.CanUndoWorkbench);
        Assert.Contains(nameof(MainWindowViewModel.NextUndoWorkbenchOperationName), notified);
        Assert.Equal(WorkbenchSessionOperationNames.MoveRow, viewModel.NextUndoWorkbenchOperationName);
        Assert.DoesNotContain(nameof(MainWindowViewModel.ProjectMeta), notified);
        Assert.DoesNotContain(nameof(MainWindowViewModel.ResultRowCount), notified);
        Assert.DoesNotContain(nameof(MainWindowViewModel.NarrationCount), notified);
        Assert.DoesNotContain(nameof(MainWindowViewModel.TtsQueueMeta), notified);
    }

    [Fact]
    public void EditingRowTextKeepsCachedWorkspaceCountersStable()
    {
        var viewModel = CreateViewModel();
        var row = viewModel.SceneTables[0].Rows[0];
        var initialRows = viewModel.ResultRowCount;
        var initialNarration = viewModel.NarrationCount;
        var initialProjectMeta = viewModel.ProjectMeta;
        var initialTtsQueueMeta = viewModel.TtsQueueMeta;

        row.LineText = "Updated line";

        Assert.Equal(initialRows, viewModel.ResultRowCount);
        Assert.Equal(initialNarration, viewModel.NarrationCount);
        Assert.Equal(initialProjectMeta, viewModel.ProjectMeta);
        Assert.Equal(initialTtsQueueMeta, viewModel.TtsQueueMeta);
    }

    [Fact]
    public void StructuralRowEditsRefreshCachedWorkspaceCounters()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var scene = viewModel.SceneTables.Last();
        var initialRows = viewModel.ResultRowCount;
        var initialNarration = viewModel.NarrationCount;

        viewModel.AddDialogueRowCommand.Execute(scene);
        viewModel.AddNarrationRowCommand.Execute(scene);

        Assert.Equal(initialRows + 2, viewModel.ResultRowCount);
        Assert.Equal(initialNarration + 1, viewModel.NarrationCount);
        Assert.Contains($"{viewModel.ResultRowCount} 行", viewModel.ProjectMeta);
    }

    [Fact]
    public void UndoThenNewWorkbenchEditClearsRedo()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        viewModel.UndoWorkbenchCommand.Execute(null);
        Assert.True(viewModel.CanRedoWorkbench);

        viewModel.SceneTables[0].SceneName = "New branch";

        Assert.False(viewModel.CanRedoWorkbench);
        Assert.True(viewModel.CanUndoWorkbench);
        Assert.Equal("New branch", viewModel.WorkbenchDocument.Scenes[0].Name);
    }

    [Fact]
    public void AddDialogueRowIsProjectedThroughWorkbenchDocument()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var scene = viewModel.SceneTables.Last();

        viewModel.AddDialogueRowCommand.Execute(scene);

        var row = Assert.Single(scene.Rows);
        var document = viewModel.WorkbenchDocument;
        Assert.Same(row, viewModel.SelectedRow);
        Assert.Equal(row.InteractionId, document.RowsById[row.InteractionId].InteractionId);
        Assert.Equal(1, document.RowsById[row.InteractionId].Order);
    }

    [Fact]
    public void EditingLocallyAddedSceneAndRowIsProjectedThroughWorkbenchDocument()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var scene = viewModel.SceneTables.Last();
        viewModel.AddDialogueRowCommand.Execute(scene);
        var row = scene.Rows.Single();

        scene.SceneName = "Locally added scene";
        row.LineText = "Locally added row";

        var sceneDraft = viewModel.WorkbenchDocument.Scenes.Single(draft => draft.SceneId == scene.SceneId);
        Assert.Equal("Locally added scene", sceneDraft.Name);
        Assert.Equal("Locally added row", viewModel.WorkbenchDocument.RowsById[row.InteractionId].Text);
    }

    [Fact]
    public void EditingSceneTextIsProjectedThroughWorkbenchDocument()
    {
        var viewModel = CreateViewModel();
        var scene = viewModel.SceneTables[0];

        scene.SceneName = "Updated scene";
        scene.SceneEffect = "Updated effect";

        var document = viewModel.WorkbenchDocument;
        var sceneDraft = document.Scenes.Single(draft => draft.SceneId == scene.SceneId);
        Assert.Equal("Updated scene", sceneDraft.Name);
        Assert.Equal("Updated effect", sceneDraft.Effect);
    }

    [Fact]
    public void ProjectRefreshResetsWorkbenchDirtyState()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        Assert.True(viewModel.IsWorkbenchDirty);

        viewModel.Project = viewModel.Project with { Title = "Reloaded project" };

        Assert.False(viewModel.IsWorkbenchDirty);
    }


    [Fact]
    public void ProjectRefreshPreservesSceneExpandedStateBySceneId()
    {
        var viewModel = CreateViewModel();
        var scene = viewModel.SceneTables[0];
        scene.ToggleExpandedCommand.Execute(null);
        Assert.False(scene.IsExpanded);

        viewModel.Project = viewModel.Project with { Title = "Reloaded project" };

        var refreshedScene = viewModel.SceneTables.Single(candidate => candidate.SceneId == scene.SceneId);
        Assert.False(refreshedScene.IsExpanded);
    }

    [Fact]
    public void ProjectRefreshReusesSceneAndRowViewModelsWhenIdsMatch()
    {
        var viewModel = CreateViewModel();
        var scene = viewModel.SceneTables[0];
        var row = scene.Rows[0];
        var sceneCollectionChanges = 0;
        var rowCollectionChanges = 0;
        viewModel.SceneTables.CollectionChanged += (_, _) => sceneCollectionChanges++;
        scene.Rows.CollectionChanged += (_, _) => rowCollectionChanges++;

        viewModel.Project = viewModel.Project with { Title = "Refresh without identity changes" };

        var refreshedScene = viewModel.SceneTables.Single(candidate => candidate.SceneId == scene.SceneId);
        var refreshedRow = refreshedScene.Rows.Single(candidate => candidate.InteractionId == row.InteractionId);
        Assert.Same(scene, refreshedScene);
        Assert.Same(row, refreshedRow);
        Assert.Equal(0, sceneCollectionChanges);
        Assert.Equal(0, rowCollectionChanges);
    }

    [Fact]
    public void ProjectRefreshInitializesNewRowsWithProjectDerivedTtsStateBeforeInsertion()
    {
        var viewModel = CreateViewModel();
        var project = CreateWorkbenchProject("project_derived_new_rows", 3, 30);
        viewModel.Project = project with { Scenes = [] };
        var sceneNotifications = new List<string>();
        var rowNotifications = new List<string>();
        viewModel.SceneTables.CollectionChanged += (_, args) =>
        {
            if (args.NewItems is null)
            {
                return;
            }

            foreach (var item in args.NewItems)
            {
                var scene = Assert.IsType<SceneTableViewModel>(item);
                scene.PropertyChanged += (_, propertyArgs) => AddNotification(sceneNotifications, propertyArgs.PropertyName);
                foreach (var row in scene.Rows)
                {
                    row.PropertyChanged += (_, propertyArgs) => AddNotification(rowNotifications, propertyArgs.PropertyName);
                }
            }
        };

        viewModel.Project = project;

        var row = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(candidate => candidate.TtsTaskId is not null);
        Assert.Equal(GenerationTaskStatus.Completed, row.TtsStatus);
        Assert.NotEmpty(row.SynthesizedAudioAsset);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.TtsStatusText), rowNotifications);
        Assert.DoesNotContain(nameof(SceneTableViewModel.HasSceneImagePreview), sceneNotifications);
        Assert.DoesNotContain(nameof(SceneTableViewModel.HasNoSceneImagePreview), sceneNotifications);
        Assert.DoesNotContain(nameof(SceneTableViewModel.SceneImagePreviewLabel), sceneNotifications);
        Assert.False(viewModel.IsWorkbenchDirty);
    }

    [Fact]
    public void WorkbenchDisplayItemsInitializeFromSceneTables()
    {
        var viewModel = CreateViewModel();

        Assert.NotEmpty(viewModel.WorkbenchDisplayItems);
        AssertDisplayProjectionMatchesSceneTables(viewModel);
    }


    [Fact]
    public void WorkbenchDisplayItemsTrackSceneExpansionAndRows()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var scene = viewModel.SceneTables.Last();
        var header = viewModel.WorkbenchDisplayItems
            .Single(item => item.Kind == WorkbenchDisplayItemKind.SceneHeader && ReferenceEquals(item.Scene, scene));
        Assert.Contains(viewModel.WorkbenchDisplayItems, item =>
            item.Kind == WorkbenchDisplayItemKind.EmptyScene &&
            ReferenceEquals(item.Scene, scene));

        viewModel.AddDialogueRowCommand.Execute(scene);
        var row = scene.Rows.Single();

        Assert.Same(header, viewModel.WorkbenchDisplayItems.Single(item =>
            item.Kind == WorkbenchDisplayItemKind.SceneHeader &&
            ReferenceEquals(item.Scene, scene)));
        Assert.Contains(viewModel.WorkbenchDisplayItems, item =>
            item is WorkbenchRowDisplayItem rowItem &&
            ReferenceEquals(rowItem.Scene, scene) &&
            ReferenceEquals(rowItem.Row, row));
        Assert.DoesNotContain(viewModel.WorkbenchDisplayItems, item =>
            item.Kind == WorkbenchDisplayItemKind.EmptyScene &&
            ReferenceEquals(item.Scene, scene));

        scene.IsExpanded = false;

        Assert.Same(header, viewModel.WorkbenchDisplayItems.Single(item =>
            item.Kind == WorkbenchDisplayItemKind.SceneHeader &&
            ReferenceEquals(item.Scene, scene)));
        Assert.DoesNotContain(viewModel.WorkbenchDisplayItems, item =>
            item is WorkbenchRowDisplayItem rowItem &&
            ReferenceEquals(rowItem.Row, row));
    }

    [Fact]
    public void WorkbenchDisplayItemsTrackDragPreviewMoves()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var sourceScene = viewModel.SceneTables[0];
        var targetScene = viewModel.SceneTables.Last();
        var dragged = sourceScene.Rows[0];

        Assert.True(viewModel.PreviewMoveInteractionRowToSceneEnd(dragged, targetScene));

        Assert.DoesNotContain(viewModel.WorkbenchDisplayItems, item =>
            item is WorkbenchRowDisplayItem rowItem &&
            ReferenceEquals(rowItem.Scene, sourceScene) &&
            ReferenceEquals(rowItem.Row, dragged));
        Assert.Contains(viewModel.WorkbenchDisplayItems, item =>
            item is WorkbenchRowDisplayItem rowItem &&
            ReferenceEquals(rowItem.Scene, targetScene) &&
            ReferenceEquals(rowItem.Row, dragged));
        Assert.DoesNotContain(viewModel.WorkbenchDisplayItems, item =>
            item.Kind == WorkbenchDisplayItemKind.EmptyScene &&
            ReferenceEquals(item.Scene, targetScene));
        AssertDisplayProjectionMatchesSceneTables(viewModel);
    }

    [Fact]
    public void WorkbenchDisplayItemsDeferSyncDuringCompleteRowMove()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var sourceScene = viewModel.SceneTables[0];
        var targetScene = viewModel.SceneTables.Last();
        var dragged = sourceScene.Rows[0];
        var actions = new List<NotifyCollectionChangedAction>();
        viewModel.WorkbenchDisplayItems.CollectionChanged += (_, args) => actions.Add(args.Action);

        viewModel.MoveInteractionRowToSceneEnd(dragged, targetScene);

        Assert.Contains(NotifyCollectionChangedAction.Move, actions);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
        Assert.DoesNotContain(viewModel.WorkbenchDisplayItems, item =>
            item is WorkbenchRowDisplayItem rowItem &&
            ReferenceEquals(rowItem.Scene, sourceScene) &&
            ReferenceEquals(rowItem.Row, dragged));
        Assert.Contains(viewModel.WorkbenchDisplayItems, item =>
            item is WorkbenchRowDisplayItem rowItem &&
            ReferenceEquals(rowItem.Scene, targetScene) &&
            ReferenceEquals(rowItem.Row, dragged));
        AssertDisplayProjectionMatchesSceneTables(viewModel);
    }

    [Fact]
    public void ProjectRefreshWithoutPreviewAssetChangesDoesNotNotifyPreviewProperties()
    {
        var viewModel = CreateViewModel();
        var scene = viewModel.SceneTables[0];
        var row = scene.Rows[0];
        var scenePreviewNotifications = new List<string>();
        var rowPreviewNotifications = new List<string>();
        scene.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                scenePreviewNotifications.Add(args.PropertyName);
            }
        };
        row.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                rowPreviewNotifications.Add(args.PropertyName);
            }
        };

        viewModel.Project = viewModel.Project with { Title = "Refresh without preview changes" };

        Assert.DoesNotContain(nameof(SceneTableViewModel.HasSceneImagePreview), scenePreviewNotifications);
        Assert.DoesNotContain(nameof(SceneTableViewModel.SceneImagePreviewLabel), scenePreviewNotifications);
        Assert.DoesNotContain(nameof(SceneTableViewModel.FirstCharacterPreviewAsset), scenePreviewNotifications);
        Assert.DoesNotContain(nameof(SceneTableViewModel.FirstCharacterPreviewLabel), scenePreviewNotifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.SelectedCharacterPreviewAsset), rowPreviewNotifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.PreviewAssetPath), rowPreviewNotifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.ObjectPreviewAsset), rowPreviewNotifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.ObjectPreviewLabel), rowPreviewNotifications);
    }

    [Fact]
    public void ProjectRefreshUpdatesDerivedSceneMetaAndTtsStateOnReusedViewModels()
    {
        var viewModel = CreateViewModel();
        var scene = viewModel.SceneTables[0];
        var row = scene.Rows[0];
        var projectScene = viewModel.Project.Scenes.Single(candidate => candidate.Id == scene.SceneId);
        var interaction = projectScene.Interactions.Single(candidate => candidate.Id == row.InteractionId);
        var ttsTask = new GenerationTask(
            "tts_project_refresh",
            GenerationTaskType.Tts,
            interaction.Id,
            "Project refresh voice",
            "comfyui",
            GenerationTaskStatus.Completed,
            new Dictionary<string, string>(StringComparer.Ordinal),
            "asset_project_refresh_imported");
        var updatedInteraction = interaction with { Tasks = [ttsTask] };
        var updatedScene = projectScene with
        {
            Location = "刷新后的天台",
            TimeOfDay = "夜",
            Weather = "小雨",
            Mood = "紧张",
            Interactions = [updatedInteraction]
        };

        viewModel.Project = viewModel.Project with
        {
            Scenes = [updatedScene],
            GenerationTasks = [ttsTask]
        };

        Assert.Same(scene, viewModel.SceneTables[0]);
        Assert.Same(row, viewModel.SceneTables[0].Rows[0]);
        Assert.Equal("刷新后的天台 / 夜 / 小雨 / 紧张", scene.SceneMeta);
        Assert.Equal("tts_project_refresh", row.TtsTaskId);
        Assert.Equal(GenerationTaskStatus.Completed, row.TtsStatus);
        Assert.Equal(string.Empty, row.TtsStatusText);
        Assert.Equal("asset_project_refresh_imported", row.TtsDetailText);
        Assert.Equal("asset_project_refresh_imported", row.SynthesizedAudioAsset);
        Assert.True(row.HasSynthesizedVoice);
        Assert.True(row.CanPreviewVoice);
        Assert.Equal("tts_project_refresh", viewModel.WorkbenchDocument.RowsById[row.InteractionId].TaskLinks.TtsTaskId);
        Assert.False(viewModel.IsWorkbenchDirty);
    }

    [Fact]
    public async Task PreviewRowVoiceClearsAudioPlayerStateWhenPlaybackFails()
    {
        var audioPreview = new ThrowingAudioPreviewService();
        var viewModel = CreateViewModel(audioPreviewService: audioPreview);
        var scene = viewModel.SceneTables[0];
        var row = scene.Rows[0];
        var projectScene = viewModel.Project.Scenes.Single(candidate => candidate.Id == scene.SceneId);
        var interaction = projectScene.Interactions.Single(candidate => candidate.Id == row.InteractionId);
        var ttsTask = new GenerationTask(
            "tts_preview_failure",
            GenerationTaskType.Tts,
            interaction.Id,
            "Project preview failure voice",
            "comfyui",
            GenerationTaskStatus.Completed,
            new Dictionary<string, string>(StringComparer.Ordinal),
            "preview_failure.wav");
        viewModel.Project = viewModel.Project with
        {
            Scenes =
            [
                projectScene with
                {
                    Interactions = [interaction with { Tasks = [ttsTask] }]
                }
            ],
            GenerationTasks = [ttsTask]
        };

        await viewModel.PreviewRowVoiceCommand.ExecuteAsync(row);

        Assert.False(viewModel.IsAudioPlayerVisible);
        Assert.Equal(string.Empty, viewModel.ActiveAudioTarget);
        Assert.Contains("试听音频失败", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreviewRowVoiceDoesNotFallbackToComfyWhenProjectAssetIdIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-row-audio-missing-asset-{Guid.NewGuid():N}");
        var audioPreview = new CapturingAudioPreviewService();
        var viewModel = CreateViewModel(audioPreviewService: audioPreview);
        await viewModel.OpenWorkspaceProjectAsync(root);
        var scene = viewModel.SceneTables[0];
        var row = scene.Rows[0];
        var projectScene = viewModel.Project.Scenes.Single(candidate => candidate.Id == scene.SceneId);
        var interaction = projectScene.Interactions.Single(candidate => candidate.Id == row.InteractionId);
        var ttsTask = new GenerationTask(
            "tts_missing_project_asset_preview",
            GenerationTaskType.Tts,
            interaction.Id,
            "Project preview missing asset voice",
            "comfyui",
            GenerationTaskStatus.Completed,
            new Dictionary<string, string>(StringComparer.Ordinal),
            "asset_missing_generated_audio");
        viewModel.Project = viewModel.Project with
        {
            Scenes =
            [
                projectScene with
                {
                    Interactions = [interaction with { Tasks = [ttsTask] }]
                }
            ],
            GenerationTasks = [ttsTask]
        };

        await viewModel.PreviewRowVoiceCommand.ExecuteAsync(row);

        Assert.Equal(string.Empty, audioPreview.LastTarget);
        Assert.False(viewModel.IsAudioPlayerVisible);
        Assert.Equal(string.Empty, viewModel.ActiveAudioTarget);
        Assert.Contains("Generated TTS project asset does not exist", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreviewRowVoiceRemainsAvailableWhileTtsSynthesisIsRunning()
    {
        var audioPath = Path.Combine(Path.GetTempPath(), $"draft-agent-row-preview-running-{Guid.NewGuid():N}.wav");
        await CreateWavFileAsync(audioPath, 0.05);
        var audioPreview = new CapturingAudioPreviewService();
        var viewModel = CreateViewModel(audioPreviewService: audioPreview);
        var scene = viewModel.SceneTables[0];
        var row = scene.Rows[0];
        var projectScene = viewModel.Project.Scenes.Single(candidate => candidate.Id == scene.SceneId);
        var interaction = projectScene.Interactions.Single(candidate => candidate.Id == row.InteractionId);
        var ttsTask = WithDefaultVoice(new GenerationTask(
            "tts_preview_while_running",
            GenerationTaskType.Tts,
            interaction.Id,
            "Project preview while running voice",
            "comfyui",
            GenerationTaskStatus.Completed,
            new Dictionary<string, string>(StringComparer.Ordinal),
            audioPath));
        viewModel.Project = viewModel.Project with
        {
            Scenes =
            [
                projectScene with
                {
                    Interactions = [interaction with { Tasks = [ttsTask] }]
                }
            ],
            GenerationTasks = [ttsTask]
        };
        row = viewModel.SceneTables[0].Rows[0];
        viewModel.IsTtsSynthesisRunning = true;

        Assert.True(row.CanPreviewVoice);

        await viewModel.PreviewRowVoiceCommand.ExecuteAsync(row);

        Assert.Equal(audioPath, audioPreview.LastTarget);
        Assert.True(viewModel.IsAudioPlayerVisible);
        Assert.Equal(audioPath, viewModel.ActiveAudioTarget);
        Assert.Contains("正在内置试听", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectRefreshUpdatesSceneMetaWithoutSceneMetaNotification()
    {
        var viewModel = CreateViewModel();
        var scene = viewModel.SceneTables[0];
        var projectScene = viewModel.Project.Scenes.Single(candidate => candidate.Id == scene.SceneId);
        var notifications = new List<string>();
        scene.PropertyChanged += (_, args) => AddNotification(notifications, args.PropertyName);

        viewModel.Project = viewModel.Project with
        {
            Scenes =
            [
                projectScene with
                {
                    Location = "刷新后的机库",
                    TimeOfDay = "清晨",
                    Weather = "薄雾",
                    Mood = "安静"
                }
            ]
        };

        Assert.Equal("刷新后的机库 / 清晨 / 薄雾 / 安静", scene.SceneMeta);
        Assert.DoesNotContain(nameof(SceneTableViewModel.SceneMeta), notifications);
    }

    [Fact]
    public void ProjectRefreshUpdatesTtsTaskFlagAndNotifiesEditState()
    {
        var viewModel = CreateViewModel();
        var row = viewModel.SceneTables[0].Rows[0];
        viewModel.SelectedRow = viewModel.SceneTables.SelectMany(scene => scene.Rows).First(candidate => !ReferenceEquals(candidate, row));
        var projectScene = viewModel.Project.Scenes.Single(candidate => candidate.Id == viewModel.SceneTables[0].SceneId);
        var interaction = projectScene.Interactions.Single(candidate => candidate.Id == row.InteractionId);
        var ttsTask = new GenerationTask(
            "tts_project_refresh_draft",
            GenerationTaskType.Tts,
            interaction.Id,
            "Project refresh draft voice",
            "comfyui",
            GenerationTaskStatus.Draft,
            new Dictionary<string, string>(StringComparer.Ordinal));
        var updatedInteraction = interaction with { Tasks = [ttsTask] };
        var updatedScene = projectScene with
        {
            Interactions = projectScene.Interactions
                .Select(candidate => candidate.Id == interaction.Id ? updatedInteraction : candidate)
                .ToArray()
        };
        var notifications = new List<string>();
        row.PropertyChanged += (_, args) => AddNotification(notifications, args.PropertyName);

        viewModel.Project = viewModel.Project with
        {
            Scenes = [updatedScene],
            GenerationTasks = [ttsTask]
        };

        Assert.Equal("tts_project_refresh_draft", row.TtsTaskId);
        Assert.True(row.HasTtsTask);
        Assert.False(row.HasSynthesizedVoice);
        Assert.False(row.CanPreviewVoice);
        Assert.Contains(nameof(AnalysisResultRowViewModel.HasTtsTask), notifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.HasSynthesizedVoice), notifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.CanPreviewVoice), notifications);
    }

    [Fact]
    public void ProjectRefreshUpdatesSynthesizedAudioAssetAndRefreshesRowActions()
    {
        var viewModel = CreateViewModel();
        var row = viewModel.SceneTables[0].Rows[0];
        viewModel.SelectedRow = viewModel.SceneTables.SelectMany(scene => scene.Rows).First(candidate => !ReferenceEquals(candidate, row));
        var projectScene = viewModel.Project.Scenes.Single(candidate => candidate.Id == viewModel.SceneTables[0].SceneId);
        var interaction = projectScene.Interactions.Single(candidate => candidate.Id == row.InteractionId);
        var ttsTask = new GenerationTask(
            "tts_project_refresh_completed",
            GenerationTaskType.Tts,
            interaction.Id,
            "Project refresh completed voice",
            "comfyui",
            GenerationTaskStatus.Completed,
            new Dictionary<string, string>(StringComparer.Ordinal),
            "asset_project_refresh_completed_imported");
        var updatedInteraction = interaction with { Tasks = [ttsTask] };
        var updatedScene = projectScene with
        {
            Interactions = projectScene.Interactions
                .Select(candidate => candidate.Id == interaction.Id ? updatedInteraction : candidate)
                .ToArray()
        };
        var notifications = new List<string>();
        row.PropertyChanged += (_, args) => AddNotification(notifications, args.PropertyName);

        viewModel.Project = viewModel.Project with
        {
            Scenes = [updatedScene],
            GenerationTasks = [ttsTask]
        };

        Assert.Equal("asset_project_refresh_completed_imported", row.SynthesizedAudioAsset);
        Assert.True(row.HasSynthesizedVoice);
        Assert.True(row.CanPreviewVoice);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.SynthesizedAudioAsset), notifications);
        Assert.Contains(nameof(AnalysisResultRowViewModel.HasSynthesizedVoice), notifications);
        Assert.Contains(nameof(AnalysisResultRowViewModel.CanPreviewVoice), notifications);
    }

    [Fact]
    public void ProjectRefreshRestoresTtsStateFromRootGenerationTasks()
    {
        var viewModel = CreateViewModel();
        var row = viewModel.SceneTables[0].Rows[0];
        var projectScene = viewModel.Project.Scenes.Single(candidate => candidate.Id == viewModel.SceneTables[0].SceneId);
        var interaction = projectScene.Interactions.Single(candidate => candidate.Id == row.InteractionId);
        var ttsTask = new GenerationTask(
            "tts_project_refresh_root",
            GenerationTaskType.Tts,
            interaction.Id,
            "Project refresh root voice",
            "comfyui",
            GenerationTaskStatus.Completed,
            new Dictionary<string, string>(StringComparer.Ordinal),
            "asset_project_refresh_root_imported");
        var updatedScene = projectScene with
        {
            Interactions = projectScene.Interactions
                .Select(candidate => candidate.Id == interaction.Id ? candidate with { Tasks = [] } : candidate)
                .ToArray()
        };

        viewModel.Project = viewModel.Project with
        {
            Scenes = [updatedScene],
            GenerationTasks = [ttsTask]
        };

        Assert.Equal("tts_project_refresh_root", row.TtsTaskId);
        Assert.Equal(GenerationTaskStatus.Completed, row.TtsStatus);
        Assert.Equal("asset_project_refresh_root_imported", row.SynthesizedAudioAsset);
        Assert.Equal("asset_project_refresh_root_imported", row.TtsDetailText);
        Assert.True(row.HasSynthesizedVoice);
        Assert.True(row.CanPreviewVoice);
        Assert.Equal("tts_project_refresh_root", viewModel.WorkbenchDocument.RowsById[row.InteractionId].TaskLinks.TtsTaskId);
    }

    [Fact]
    public void ProjectRefreshUpdatesTtsStatusAndNotifiesEditState()
    {
        var viewModel = CreateViewModel();
        var row = viewModel.SceneTables[0].Rows[0];
        viewModel.SelectedRow = viewModel.SceneTables.SelectMany(scene => scene.Rows).First(candidate => !ReferenceEquals(candidate, row));
        var projectScene = viewModel.Project.Scenes.Single(candidate => candidate.Id == viewModel.SceneTables[0].SceneId);
        var interaction = projectScene.Interactions.Single(candidate => candidate.Id == row.InteractionId);
        var ttsTask = new GenerationTask(
            "tts_project_refresh_status",
            GenerationTaskType.Tts,
            interaction.Id,
            "Project refresh status voice",
            "comfyui",
            GenerationTaskStatus.Completed,
            new Dictionary<string, string>(StringComparer.Ordinal),
            "asset_project_refresh_status_imported");
        var updatedInteraction = interaction with { Tasks = [ttsTask] };
        var updatedScene = projectScene with
        {
            Interactions = projectScene.Interactions
                .Select(candidate => candidate.Id == interaction.Id ? updatedInteraction : candidate)
                .ToArray()
        };
        var notifications = new List<string>();
        row.PropertyChanged += (_, args) => AddNotification(notifications, args.PropertyName);

        viewModel.Project = viewModel.Project with
        {
            Scenes = [updatedScene],
            GenerationTasks = [ttsTask]
        };

        Assert.Equal(GenerationTaskStatus.Completed, row.TtsStatus);
        Assert.Equal(string.Empty, row.TtsStatusText);
        Assert.Equal("asset_project_refresh_status_imported", row.TtsDetailText);
        Assert.True(row.HasSynthesizedVoice);
        Assert.True(row.CanPreviewVoice);
        Assert.Contains(nameof(AnalysisResultRowViewModel.TtsStatus), notifications);
        Assert.Contains(nameof(AnalysisResultRowViewModel.TtsStatusText), notifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.TtsDetailText), notifications);
        Assert.Contains(nameof(AnalysisResultRowViewModel.HasSynthesizedVoice), notifications);
        Assert.Contains(nameof(AnalysisResultRowViewModel.CanPreviewVoice), notifications);
    }

    [Fact]
    public void ProjectRefreshNotifiesCanPreviewVoiceForSelectedRowsOnly()
    {
        var viewModel = CreateViewModel();
        var row = viewModel.SceneTables[0].Rows[0];
        viewModel.SelectedRow = row;
        var projectScene = viewModel.Project.Scenes.Single(candidate => candidate.Id == viewModel.SceneTables[0].SceneId);
        var interaction = projectScene.Interactions.Single(candidate => candidate.Id == row.InteractionId);
        var ttsTask = new GenerationTask(
            "tts_project_refresh_selected_preview",
            GenerationTaskType.Tts,
            interaction.Id,
            "Project refresh selected preview",
            "comfyui",
            GenerationTaskStatus.Completed,
            new Dictionary<string, string>(StringComparer.Ordinal),
            "fallback_project_refresh_selected_preview.wav");
        var updatedInteraction = interaction with { Tasks = [ttsTask] };
        var updatedScene = projectScene with { Interactions = [updatedInteraction] };
        var notifications = new List<string>();
        row.PropertyChanged += (_, args) => AddNotification(notifications, args.PropertyName);

        viewModel.Project = viewModel.Project with
        {
            Scenes = [updatedScene],
            GenerationTasks = [ttsTask]
        };

        Assert.True(row.IsSelected);
        Assert.True(row.CanPreviewVoice);
        Assert.Contains(nameof(AnalysisResultRowViewModel.CanPreviewVoice), notifications);
        Assert.Contains(nameof(AnalysisResultRowViewModel.TtsStatusText), notifications);
    }

    [Fact]
    public void ProjectRefreshDoesNotNotifyTtsTextWhenRowStillHasNoTtsTask()
    {
        var viewModel = CreateViewModel();
        viewModel.Project = CreateWorkbenchProject("tts_no_task_refresh", 3, 30);
        var scene = viewModel.SceneTables.First(candidate => candidate.Rows.Any(row => row.TtsTaskId is null));
        var row = scene.Rows.First(candidate => candidate.TtsTaskId is null);
        var notifications = new List<string>();
        row.PropertyChanged += (_, args) => AddNotification(notifications, args.PropertyName);

        viewModel.Project = viewModel.Project with { Title = "No TTS task refresh" };

        Assert.Null(row.TtsTaskId);
        Assert.Equal("待合成", row.TtsStatusText);
        Assert.Equal("保存后生成语音任务", row.TtsDetailText);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.TtsStatusText), notifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.TtsDetailText), notifications);
    }


    [Fact]
    public void SyncingWorkbenchFromViewModelsKeepsCharacterBindingsOutOfDerivedTargets()
    {
        var viewModel = CreateViewModel();
        var characterTarget = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        characterTarget.ImageAsset = "stale-derived-portrait.png";
        characterTarget.VoiceAsset = "stale-derived-voice.wav";

        viewModel.SceneTables[0].SceneName = "Trigger scene sync";

        Assert.DoesNotContain(
            viewModel.WorkbenchDocument.CharacterBindings,
            binding => binding.VisualBinding?.EffectiveAssetId == "stale-derived-portrait.png" ||
                binding.VoiceBinding?.EffectiveVoiceReferenceId == "stale-derived-voice.wav");
    }

    [Fact]
    public void BindingTargetRefreshReusesTargetsAndPreservesStatus()
    {
        var viewModel = CreateViewModel();
        var scene = viewModel.SceneTables[0];
        var target = viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Scene && binding.TargetId == scene.SceneId);
        target.StatusText = "busy";

        scene.SceneName = "Updated scene target";

        var refreshed = viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Scene && binding.TargetId == scene.SceneId);
        Assert.Same(target, refreshed);
        Assert.Equal("Updated scene target", refreshed.DisplayName);
        Assert.Equal("busy", refreshed.StatusText);
    }

    [Fact]
    public void BindingTargetRefreshUpdatesExistingTargetWithoutCollectionChurn()
    {
        var viewModel = CreateViewModel();
        var scene = viewModel.SceneTables[0];
        var target = viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Scene && binding.TargetId == scene.SceneId);
        var collectionChanges = 0;
        viewModel.BindingTargets.CollectionChanged += (_, _) => collectionChanges++;

        scene.SceneName = "Updated scene target";

        Assert.Equal(0, collectionChanges);
        Assert.Same(target, viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Scene && binding.TargetId == scene.SceneId));
        Assert.Equal("Updated scene target", target.DisplayName);
    }

    [Fact]
    public void BindingTargetFullRefreshMetricsTrackFullRefreshesAndLocalFallbacksSeparately()
    {
        var viewModel = CreateViewModel();
        var initialMetrics = viewModel.WorkbenchBindingRefreshMetrics;

        viewModel.Project = viewModel.Project with { Title = "Refresh binding metrics" };

        Assert.Equal(initialMetrics.FullRefreshCount + 1, viewModel.WorkbenchBindingRefreshMetrics.FullRefreshCount);
        Assert.Equal(initialMetrics.FallbackFullRefreshCount, viewModel.WorkbenchBindingRefreshMetrics.FallbackFullRefreshCount);
        Assert.Equal("project_refresh", viewModel.WorkbenchBindingRefreshMetrics.LastFullRefreshReason);
        Assert.Equal(initialMetrics.FullRefreshReasons.CountFor("project_refresh") + 1, viewModel.WorkbenchBindingRefreshMetrics.FullRefreshReasons.CountFor("project_refresh"));
        Assert.Equal(initialMetrics.LastFallbackFullRefreshReason, viewModel.WorkbenchBindingRefreshMetrics.LastFallbackFullRefreshReason);
    }

    [Fact]
    public void MissingExpectedSceneTargetIsRecreatedWithoutFullRefreshFallback()
    {
        var viewModel = CreateViewModel();
        var scene = viewModel.SceneTables[0];
        var initialMetrics = viewModel.WorkbenchBindingRefreshMetrics;
        var sceneTarget = viewModel.BindingTargets.Single(binding =>
            binding.Kind == BindingTargetKind.Scene &&
            binding.TargetId == scene.SceneId);
        viewModel.BindingTargets.Remove(sceneTarget);
        var collectionChanges = 0;
        viewModel.BindingTargets.CollectionChanged += (_, _) => collectionChanges++;

        scene.SceneName = "Fallback scene target";

        Assert.Equal(initialMetrics.FallbackFullRefreshCount, viewModel.WorkbenchBindingRefreshMetrics.FallbackFullRefreshCount);
        Assert.Equal(initialMetrics.LastFullRefreshReason, viewModel.WorkbenchBindingRefreshMetrics.LastFullRefreshReason);
        Assert.Equal(initialMetrics.LastFallbackFullRefreshReason, viewModel.WorkbenchBindingRefreshMetrics.LastFallbackFullRefreshReason);
        Assert.Equal(initialMetrics.FullRefreshReasons.CountFor("scene_target"), viewModel.WorkbenchBindingRefreshMetrics.FullRefreshReasons.CountFor("scene_target"));
        Assert.Equal(initialMetrics.FallbackFullRefreshReasons.CountFor("scene_target"), viewModel.WorkbenchBindingRefreshMetrics.FallbackFullRefreshReasons.CountFor("scene_target"));
        Assert.Equal(1, collectionChanges);
        var recreatedTarget = viewModel.BindingTargets.Single(binding =>
            binding.Kind == BindingTargetKind.Scene &&
            binding.TargetId == scene.SceneId);
        Assert.Equal("Fallback scene target", recreatedTarget.DisplayName);
    }

    [Fact]
    public async Task OpenWorkspaceProjectRefreshesBindingTargetsOnlyForCommittedAndLoadedProjects()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-open-refresh-{Guid.NewGuid():N}");
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Open Refresh Test");
        var workspaceProject = new SceneInteractionBuilder()
            .Build("林安说：旧项目。") with
            {
                Title = "Existing Workspace"
            };
        await manager.SaveDraftAsync(WorkspaceProjectManager.ToWorkspace(manifest), workspaceProject);
        var viewModel = CreateViewModel();
        var initialMetrics = viewModel.WorkbenchBindingRefreshMetrics;

        await viewModel.OpenWorkspaceProjectAsync(root);

        Assert.Equal(initialMetrics.FullRefreshCount + 2, viewModel.WorkbenchBindingRefreshMetrics.FullRefreshCount);
        Assert.Equal(initialMetrics.FallbackFullRefreshCount, viewModel.WorkbenchBindingRefreshMetrics.FallbackFullRefreshCount);
        Assert.Equal("project_refresh", viewModel.WorkbenchBindingRefreshMetrics.LastFullRefreshReason);
        Assert.Equal(initialMetrics.FullRefreshReasons.CountFor("project_refresh") + 2, viewModel.WorkbenchBindingRefreshMetrics.FullRefreshReasons.CountFor("project_refresh"));
        Assert.Equal(initialMetrics.FullRefreshReasons.CountFor("workspace_manifest"), viewModel.WorkbenchBindingRefreshMetrics.FullRefreshReasons.CountFor("workspace_manifest"));
        Assert.Equal("Existing Workspace", viewModel.Project.Title);
    }

    [Fact]
    public async Task OpeningEmptyWorkspaceProjectRefreshesOnlyBindingPreviewAssetsForManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-open-preview-only-{Guid.NewGuid():N}");
        var viewModel = CreateViewModel();
        var initialMetrics = viewModel.WorkbenchBindingRefreshMetrics;
        var initialTargets = viewModel.BindingTargets.ToArray();
        var collectionChanges = 0;
        viewModel.BindingTargets.CollectionChanged += (_, _) => collectionChanges++;

        await viewModel.OpenWorkspaceProjectAsync(root);

        Assert.Equal(0, collectionChanges);
        Assert.Equal(initialTargets, viewModel.BindingTargets);
        Assert.Equal(
            initialMetrics.FullRefreshReasons.CountFor("workspace_manifest"),
            viewModel.WorkbenchBindingRefreshMetrics.FullRefreshReasons.CountFor("workspace_manifest"));
    }

    [Fact]
    public async Task SetVoiceBindingShowsManualComfyVoicesInstructionWithoutImportingAudio()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-voice-binding-{Guid.NewGuid():N}");
        var voicesDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-comfy-voices-{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-voice-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDirectory);
        var sourceAudioPath = Path.Combine(sourceDirectory, "chen.wav");
        await File.WriteAllBytesAsync(sourceAudioPath, [0x52, 0x49, 0x46, 0x46]);
        await File.WriteAllTextAsync(Path.ChangeExtension(sourceAudioPath, ".txt"), "reference line");
        var viewModel = CreateViewModel(voicesDirectory);
        await viewModel.OpenWorkspaceProjectAsync(root);
        await viewModel.TestComfyConnectionCommand.ExecuteAsync(null);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Narration);
        var originalVoiceAsset = target.VoiceAsset;

        await viewModel.SetVoiceBindingAsync(target, sourceAudioPath);

        var manifest = await new WorkspaceProjectManager().LoadAsync(root);
        Assert.DoesNotContain(manifest.Assets, candidate =>
            candidate.Kind == MediaAssetKind.Audio &&
            candidate.Source == MediaAssetSource.VoiceReference);
        Assert.False(Directory.Exists(voicesDirectory) &&
            Directory.EnumerateFiles(voicesDirectory, "*", SearchOption.AllDirectories).Any());
        Assert.Equal(originalVoiceAsset, target.VoiceAsset);
        Assert.Equal(string.Empty, target.VoiceSourceAsset);
        Assert.Equal(string.Empty, target.VoiceSyncedPath);
        Assert.Equal(string.Empty, target.VoiceReferenceText);
        Assert.Contains("手动放到 ComfyUI\\models\\voices", target.StatusText, StringComparison.Ordinal);
        Assert.Contains("手动放到 ComfyUI\\models\\voices", viewModel.StatusText, StringComparison.Ordinal);

        var row = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(candidate => candidate.IsNarration);
        var draft = viewModel.WorkbenchDocument.RowsById[row.InteractionId];
        Assert.Null(draft.VoiceBinding?.EffectiveVoiceReferenceId);
        Assert.Null(draft.VoiceBinding?.EffectiveReferenceText);
        Assert.Null(draft.VoiceBinding?.SourceAssetId);
        Assert.Null(draft.VoiceBinding?.SyncedVoicePath);
    }

    [Fact]
    public async Task OpeningWorkspaceProjectDoesNotImportPreviouslySelectedExternalVoice()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-voice-open-import-{Guid.NewGuid():N}");
        var voicesDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-comfy-open-import-voices-{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-voice-open-import-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDirectory);
        var sourceAudioPath = Path.Combine(sourceDirectory, "outside.wav");
        await File.WriteAllBytesAsync(sourceAudioPath, [0x52, 0x49, 0x46, 0x46]);
        await File.WriteAllTextAsync(Path.ChangeExtension(sourceAudioPath, ".txt"), "outside reference line");
        var viewModel = CreateViewModel(voicesDirectory);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Narration);
        await viewModel.SetVoiceBindingAsync(target, sourceAudioPath);

        await viewModel.OpenWorkspaceProjectAsync(root);

        target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        var manifest = await new WorkspaceProjectManager().LoadAsync(root);
        Assert.DoesNotContain(manifest.Assets, candidate =>
            candidate.Kind == MediaAssetKind.Audio &&
            candidate.Source == MediaAssetSource.VoiceReference);
        Assert.Equal(string.Empty, target.VoiceSourceAsset);
        Assert.Equal(string.Empty, target.VoiceSyncedPath);
        Assert.Equal(string.Empty, target.VoiceReferenceText);
        var row = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(candidate => candidate.IsNarration);
        var draft = viewModel.WorkbenchDocument.RowsById[row.InteractionId];
        Assert.Null(draft.VoiceBinding?.SourceAssetId);
        Assert.Null(draft.VoiceBinding?.EffectiveReferenceText);
    }

    [Fact]
    public async Task PreviewVoiceUsesComfyVoicePreviewEndpointWithoutCreatingProjectAsset()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-voice-preview-{Guid.NewGuid():N}");
        var voicesDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-comfy-preview-voices-{Guid.NewGuid():N}");
        Directory.CreateDirectory(voicesDirectory);
        var voicePath = Path.Combine(voicesDirectory, "lin.wav");
        await File.WriteAllBytesAsync(voicePath, [0x52, 0x49, 0x46, 0x46]);
        await File.WriteAllTextAsync(Path.ChangeExtension(voicePath, ".txt"), "preview reference line");
        var audioPreview = new CapturingAudioPreviewService();
        var viewModel = CreateViewModel(voicesDirectory, audioPreview);
        await viewModel.OpenWorkspaceProjectAsync(root);
        viewModel.ComfyEndpoint = "http://127.0.0.1:9000";
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        await viewModel.SetVoiceReferenceSelectionAsync(target, "lin.wav");

        await viewModel.PreviewVoiceCommand.ExecuteAsync(target);

        Assert.Equal("http://127.0.0.1:9000/api/tts-audio-suite/voice-preview?voice_name=lin.wav", audioPreview.LastTarget);
        Assert.Equal(audioPreview.LastTarget, viewModel.ActiveAudioTarget);
        Assert.Equal(string.Empty, target.VoiceSourceAsset);
        Assert.Equal(string.Empty, target.VoiceSyncedPath);
        var manifest = await new WorkspaceProjectManager().LoadAsync(root);
        Assert.DoesNotContain(manifest.Assets, asset => asset.Source == MediaAssetSource.VoiceReference);
    }

    [Fact]
    public async Task ProjectVoiceReferenceDropdownDoesNotShowProjectVoiceAssets()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-project-voice-dropdown-{Guid.NewGuid():N}");
        var voicesDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-project-voice-dropdown-voices-{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-project-voice-dropdown-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(voicesDirectory);
        var sourceAudioPath = Path.Combine(sourceDirectory, "ComfyUI_00038_.flac");
        await File.WriteAllBytesAsync(sourceAudioPath, [0x66, 0x4C, 0x61, 0x43]);
        await File.WriteAllTextAsync(Path.ChangeExtension(sourceAudioPath, ".txt"), "dropdown project reference");
        await File.WriteAllBytesAsync(Path.Combine(voicesDirectory, "catalog_voice.flac"), [0x66, 0x4C, 0x61, 0x43]);
        var client = new CapturingComfyUiClient
        {
            VoiceCatalog = new ComfyVoiceCatalogResult([new ComfyVoiceReference("catalog_voice.flac", null)])
        };
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            client,
            new InMemoryInteractionProjectStore());
        var viewModel = ConfigureFastTts(new MainWindowViewModel(
            service,
            comfyVoicesDirectoryResolver: () => voicesDirectory));
        await viewModel.OpenWorkspaceProjectAsync(root);
        await viewModel.TestComfyConnectionCommand.ExecuteAsync(null);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Narration);

        await viewModel.SetVoiceBindingAsync(target, sourceAudioPath);

        Assert.Contains("catalog_voice.flac", viewModel.VoiceReferenceOptions);
        Assert.Contains("ComfyUI_00038_.flac", viewModel.VoiceReferenceOptions);
        Assert.DoesNotContain(viewModel.VoiceReferenceOptions, option =>
            option.StartsWith("asset_", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(viewModel.VoiceReferenceOptions, option =>
            string.Equals(option, "assets/audio/voice-references/ComfyUI_00038_.flac", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task VoiceReferenceDropdownUsesOnlyComfyHttpCatalog()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-project-voice-dropdown-local-{Guid.NewGuid():N}");
        var voicesDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-project-voice-dropdown-local-voices-{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-project-voice-dropdown-local-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(voicesDirectory);
        Directory.CreateDirectory(sourceDirectory);
        var sourceAudioPath = Path.Combine(sourceDirectory, "same-name.wav");
        await File.WriteAllBytesAsync(sourceAudioPath, [0x52, 0x49, 0x46, 0x46]);
        await File.WriteAllTextAsync(Path.ChangeExtension(sourceAudioPath, ".txt"), "project reference");
        var viewModel = CreateViewModel(voicesDirectory);
        await viewModel.OpenWorkspaceProjectAsync(root);
        await viewModel.TestComfyConnectionCommand.ExecuteAsync(null);
        EnableRoleDialogueVoiceRoute(viewModel);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);

        await viewModel.SetVoiceBindingAsync(target, sourceAudioPath);

        Assert.Equal(0, viewModel.VoiceReferenceOptions.Count(option =>
            string.Equals(option, "same-name.wav", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal("未绑定声音", target.VoiceAsset);
        Assert.DoesNotContain(viewModel.VoiceReferenceOptions, option =>
            option.StartsWith("asset_", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(viewModel.VoiceReferenceOptions, option =>
            option.Contains("voice-references", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PreviewVoiceRejectsLegacyAbsoluteComfyVoicePath()
    {
        var audioPreview = new CapturingAudioPreviewService();
        var viewModel = CreateViewModel(audioPreviewService: audioPreview);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        target.VoiceAsset = @"C:\ComfyUI\models\voices\legacy.wav";
        target.VoiceSourceAsset = string.Empty;
        target.VoiceSyncedPath = string.Empty;

        await viewModel.PreviewVoiceCommand.ExecuteAsync(target);

        Assert.Equal(string.Empty, audioPreview.LastTarget);
        Assert.False(viewModel.IsAudioPlayerVisible);
        Assert.Equal("参考音频试听失败", target.StatusText);
        Assert.Contains("ComfyUI 相对路径", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreviewVoiceDoesNotFallbackToComfyVoiceWhenProjectAssetIdIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-voice-missing-asset-preview-{Guid.NewGuid():N}");
        var voicesDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-comfy-missing-asset-voices-{Guid.NewGuid():N}");
        Directory.CreateDirectory(voicesDirectory);
        var fallbackVoicePath = Path.Combine(voicesDirectory, "asset_missing_voice.wav");
        await File.WriteAllBytesAsync(fallbackVoicePath, [0x52, 0x49, 0x46, 0x46]);
        var audioPreview = new CapturingAudioPreviewService();
        var viewModel = CreateViewModel(voicesDirectory, audioPreview);
        await viewModel.OpenWorkspaceProjectAsync(root);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        target.VoiceAsset = "asset_missing_voice.wav";
        target.VoiceSourceAsset = string.Empty;
        target.VoiceSyncedPath = string.Empty;

        await viewModel.PreviewVoiceCommand.ExecuteAsync(target);

        Assert.Equal(string.Empty, audioPreview.LastTarget);
        Assert.False(viewModel.IsAudioPlayerVisible);
        Assert.Equal(string.Empty, viewModel.ActiveAudioTarget);
        Assert.Equal("参考音频试听失败", target.StatusText);
        Assert.Contains("Voice reference project asset does not exist", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreviewVoiceUsesComfyInputViewUrlForPluginVoiceWhenLocalFileIsMissing()
    {
        var voicesDirectory = CreateIsolatedVoicesDirectory("remote-preview");
        var audioPreview = new CapturingAudioPreviewService();
        var viewModel = CreateViewModel(voicesDirectory, audioPreview);
        viewModel.ComfyEndpoint = "http://127.0.0.1:8188";
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        target.VoiceAsset = "female_01.wav";
        target.VoiceSourceAsset = string.Empty;
        target.VoiceSyncedPath = string.Empty;

        await viewModel.PreviewVoiceCommand.ExecuteAsync(target);

        Assert.Equal(
            "http://127.0.0.1:8188/api/tts-audio-suite/voice-preview?voice_name=female_01.wav",
            audioPreview.LastTarget);
        Assert.Equal(audioPreview.LastTarget, viewModel.ActiveAudioTarget);
    }

    [Fact]
    public async Task PreviewVoiceClearsAudioPlayerStateWhenPlaybackFails()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-voice-preview-fail-{Guid.NewGuid():N}");
        var voicesDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-comfy-preview-fail-voices-{Guid.NewGuid():N}");
        Directory.CreateDirectory(voicesDirectory);
        var voicePath = Path.Combine(voicesDirectory, "lin.wav");
        await File.WriteAllBytesAsync(voicePath, [0x52, 0x49, 0x46, 0x46]);
        await File.WriteAllTextAsync(Path.ChangeExtension(voicePath, ".txt"), "preview reference line");
        var audioPreview = new ThrowingAudioPreviewService();
        var viewModel = CreateViewModel(voicesDirectory, audioPreview);
        await viewModel.OpenWorkspaceProjectAsync(root);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        await viewModel.SetVoiceReferenceSelectionAsync(target, "lin.wav");

        await viewModel.PreviewVoiceCommand.ExecuteAsync(target);

        Assert.False(viewModel.IsAudioPlayerVisible);
        Assert.Equal(string.Empty, viewModel.ActiveAudioTarget);
        Assert.Equal("参考音频试听失败", target.StatusText);
        Assert.Contains("参考音频试听失败", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetVoiceBindingWithoutAudioSidecarShowsManualPlacementInstruction()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-voice-missing-text-{Guid.NewGuid():N}");
        var voicesDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-comfy-missing-text-{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-voice-missing-text-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDirectory);
        var sourceAudioPath = Path.Combine(sourceDirectory, "chen.wav");
        await File.WriteAllBytesAsync(sourceAudioPath, [0x52, 0x49, 0x46, 0x46]);
        var viewModel = CreateViewModel(voicesDirectory);
        await viewModel.OpenWorkspaceProjectAsync(root);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);

        await viewModel.SetVoiceBindingAsync(target, sourceAudioPath);

        Assert.Equal(string.Empty, target.VoiceReferenceText);
        Assert.Equal("Missing reference text", target.VoiceReferenceStateText);
        Assert.Contains("手动放到 ComfyUI\\models\\voices", target.StatusText, StringComparison.Ordinal);
        Assert.Contains("手动放到 ComfyUI\\models\\voices", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResyncVoiceBindingDoesNotCreateComfyVoiceFromCustomAudio()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-voice-resync-{Guid.NewGuid():N}");
        var voicesDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-comfy-resync-{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-voice-resync-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDirectory);
        var sourceAudioPath = Path.Combine(sourceDirectory, "chen.wav");
        await File.WriteAllBytesAsync(sourceAudioPath, [0x52, 0x49, 0x46, 0x46]);
        await File.WriteAllTextAsync(Path.ChangeExtension(sourceAudioPath, ".txt"), "resync line");
        var viewModel = CreateViewModel(voicesDirectory);
        await viewModel.OpenWorkspaceProjectAsync(root);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Narration);
        await viewModel.SetVoiceBindingAsync(target, sourceAudioPath);

        await viewModel.ResyncVoiceReferenceCommand.ExecuteAsync(target);

        Assert.Equal("未绑定声音", target.VoiceAsset);
        Assert.Equal(string.Empty, target.VoiceSourceAsset);
        Assert.Equal(string.Empty, target.VoiceSyncedPath);
        Assert.False(Directory.Exists(voicesDirectory) &&
            Directory.EnumerateFiles(voicesDirectory, "*", SearchOption.AllDirectories).Any());
        var row = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(candidate => candidate.IsNarration);
        var draft = viewModel.WorkbenchDocument.RowsById[row.InteractionId];
        Assert.Null(draft.VoiceBinding?.SyncedVoicePath);
        Assert.Contains("不会复制到 ComfyUI voices", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResyncVoiceBindingDoesNotRecreateInternalIdSyncedVoicePath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-voice-resync-clean-{Guid.NewGuid():N}");
        var voicesDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-comfy-resync-clean-{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-voice-resync-clean-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDirectory);
        var sourceAudioPath = Path.Combine(sourceDirectory, "ComfyUI_00038_.flac");
        await File.WriteAllBytesAsync(sourceAudioPath, [1, 2, 3, 4]);
        await File.WriteAllTextAsync(Path.ChangeExtension(sourceAudioPath, ".txt"), "clean resync line");
        var viewModel = CreateViewModel(voicesDirectory);
        await viewModel.OpenWorkspaceProjectAsync(root);
        EnableRoleDialogueVoiceRoute(viewModel);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        await viewModel.SetVoiceBindingAsync(target, sourceAudioPath);
        var oldInternalIdSyncedPath = Path.Combine(voicesDirectory, "int_scene_001_001_ComfyUI_00038_.flac");
        target.VoiceSyncedPath = oldInternalIdSyncedPath;
        viewModel.ApplyBindingTargetCommand.Execute(target);

        await viewModel.ResyncVoiceReferenceCommand.ExecuteAsync(target);

        Assert.False(File.Exists(oldInternalIdSyncedPath));
        Assert.Equal("未绑定声音", target.VoiceAsset);
        Assert.Equal(string.Empty, target.VoiceSyncedPath);
        var row = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(candidate => candidate.IsNarration);
        var draft = viewModel.WorkbenchDocument.RowsById[row.InteractionId];
        Assert.Null(draft.VoiceBinding?.EffectiveVoiceReferenceId);
        Assert.Null(draft.VoiceBinding?.SyncedVoicePath);
    }

    [Fact]
    public async Task SetVoiceBindingDoesNotWriteDuplicateComfyVoicesPerTarget()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-voice-row-dedupe-{Guid.NewGuid():N}");
        var voicesDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-comfy-row-dedupe-{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-voice-row-dedupe-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDirectory);
        var sourceAudioPath = Path.Combine(sourceDirectory, "ComfyUI_00038_.flac");
        await File.WriteAllBytesAsync(sourceAudioPath, [1, 2, 3, 4]);
        await File.WriteAllTextAsync(Path.ChangeExtension(sourceAudioPath, ".txt"), "shared row reference");
        var viewModel = CreateViewModel(voicesDirectory);
        await viewModel.OpenWorkspaceProjectAsync(root);
        EnableRoleDialogueVoiceRoute(viewModel);
        var targets = viewModel.BindingTargets
            .Where(binding => binding.Kind == BindingTargetKind.Character && binding.SupportsVoice)
            .Take(2)
            .ToArray();
        Assert.Equal(2, targets.Length);

        foreach (var target in targets)
        {
            await viewModel.SetVoiceBindingAsync(target, sourceAudioPath);
        }

        var manifest = await new WorkspaceProjectManager().LoadAsync(root);
        var voiceAssets = manifest.Assets
            .Where(asset => asset.Kind == MediaAssetKind.Audio && asset.Source == MediaAssetSource.VoiceReference)
            .ToArray();
        Assert.Empty(voiceAssets);
        Assert.False(Directory.Exists(manifest.Folders.AudioVoiceReferences) &&
            Directory.EnumerateFiles(manifest.Folders.AudioVoiceReferences, "*.flac").Any());
        Assert.False(Directory.Exists(voicesDirectory) &&
            Directory.EnumerateFiles(voicesDirectory, "*.flac").Any());
        Assert.All(targets, target =>
        {
            Assert.Equal(string.Empty, target.VoiceSourceAsset);
            Assert.Equal(string.Empty, target.VoiceSyncedPath);
            Assert.Equal("未绑定声音", target.VoiceAsset);
        });
    }

    [Fact]
    public async Task ReloadVoiceReferenceTextDoesNotReadComfyVoicesSidecarDirectly()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-voice-text-reload-{Guid.NewGuid():N}");
        var voicesDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-comfy-text-reload-{Guid.NewGuid():N}");
        Directory.CreateDirectory(voicesDirectory);
        var voicePath = Path.Combine(voicesDirectory, "chen.wav");
        await File.WriteAllBytesAsync(voicePath, [0x52, 0x49, 0x46, 0x46]);
        await File.WriteAllTextAsync(Path.ChangeExtension(voicePath, ".txt"), "updated voices reference");
        var viewModel = CreateViewModel(voicesDirectory);
        await viewModel.OpenWorkspaceProjectAsync(root);
        EnableRoleDialogueVoiceRoute(viewModel);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        await viewModel.SetVoiceReferenceSelectionAsync(target, "chen.wav");
        target.VoiceReferenceText = "stale manual reference";

        await viewModel.ReloadVoiceReferenceTextCommand.ExecuteAsync(target);

        Assert.Equal("stale manual reference", target.VoiceReferenceText);
        Assert.Equal("stale manual reference", target.VoiceReferenceTextPreview);
        Assert.Equal("No reference text found", target.StatusText);
        var manifest = await new WorkspaceProjectManager().LoadAsync(root);
        Assert.DoesNotContain(manifest.Assets, asset => asset.Source == MediaAssetSource.VoiceReference);
        var row = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(candidate => !candidate.IsNarration && string.Equals(candidate.SubjectName, target.DisplayName, StringComparison.Ordinal));
        var draft = viewModel.WorkbenchDocument.RowsById[row.InteractionId];
        Assert.Null(draft.VoiceBinding?.EffectiveReferenceText);
        Assert.Null(draft.VoiceBinding?.ReferenceText);
        Assert.Null(draft.VoiceBinding?.SourceAssetId);
        Assert.Null(draft.VoiceBinding?.SyncedVoicePath);
    }

    [Fact]
    public async Task SetVoiceReferenceSelectionDoesNotReadLocalReferenceTxtSidecar()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-voice-reference-txt-{Guid.NewGuid():N}");
        var voicesDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-comfy-reference-txt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(voicesDirectory);
        var voicePath = Path.Combine(voicesDirectory, "chen.wav");
        await File.WriteAllBytesAsync(voicePath, [0x52, 0x49, 0x46, 0x46]);
        await File.WriteAllTextAsync(Path.ChangeExtension(voicePath, ".txt"), "regular text");
        await File.WriteAllTextAsync(Path.Combine(voicesDirectory, "chen.reference.txt"), "priority reference text");
        var viewModel = CreateViewModel(voicesDirectory);
        await viewModel.OpenWorkspaceProjectAsync(root);
        EnableRoleDialogueVoiceRoute(viewModel);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);

        await viewModel.SetVoiceReferenceSelectionAsync(target, "chen.wav");

        Assert.True(target.CanEditVoiceReferenceText);
        Assert.Equal(string.Empty, target.VoiceReferenceText);
        var row = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(candidate => !candidate.IsNarration && string.Equals(candidate.SubjectName, target.DisplayName, StringComparison.Ordinal));
        var draft = viewModel.WorkbenchDocument.RowsById[row.InteractionId];
        Assert.Null(draft.VoiceBinding?.EffectiveReferenceText);
    }

    [Fact]
    public async Task ApplyBindingTargetUsesManualReferenceTextWithoutReadingComfySidecar()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-voice-text-edit-{Guid.NewGuid():N}");
        var voicesDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-comfy-text-edit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(voicesDirectory);
        var voicePath = Path.Combine(voicesDirectory, "chen.wav");
        await File.WriteAllBytesAsync(voicePath, [0x52, 0x49, 0x46, 0x46]);
        await File.WriteAllTextAsync(Path.ChangeExtension(voicePath, ".txt"), "original reference");
        var viewModel = CreateViewModel(voicesDirectory);
        await viewModel.OpenWorkspaceProjectAsync(root);
        EnableRoleDialogueVoiceRoute(viewModel);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        await viewModel.SetVoiceReferenceSelectionAsync(target, "chen.wav");
        var sourceAssetId = target.VoiceSourceAsset;
        var syncedPath = target.VoiceSyncedPath;

        Assert.True(target.CanEditVoiceReferenceText);
        target.VoiceReferenceText = "edited reference line";
        viewModel.ApplyBindingTargetCommand.Execute(target);

        Assert.Equal("edited reference line", target.VoiceReferenceTextPreview);
        Assert.Equal("Manual edit", target.VoiceReferenceTextSourceText);
        var row = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(candidate => !candidate.IsNarration && string.Equals(candidate.SubjectName, target.DisplayName, StringComparison.Ordinal));
        var draft = viewModel.WorkbenchDocument.RowsById[row.InteractionId];
        Assert.Equal("edited reference line", draft.VoiceBinding?.EffectiveReferenceText);
        Assert.Equal("edited reference line", draft.VoiceBinding?.ReferenceText);
        Assert.Null(draft.VoiceBinding?.SourceAssetId);
        Assert.Null(draft.VoiceBinding?.SyncedVoicePath);
    }

    [Fact]
    public async Task SaveWorkbenchEditsAppliesPendingBindingTargetTextEdits()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-save-pending-binding-{Guid.NewGuid():N}");
        var voicesDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-comfy-save-pending-{Guid.NewGuid():N}");
        Directory.CreateDirectory(voicesDirectory);
        var voicePath = Path.Combine(voicesDirectory, "chen.wav");
        await File.WriteAllBytesAsync(voicePath, [0x52, 0x49, 0x46, 0x46]);
        await File.WriteAllTextAsync(Path.ChangeExtension(voicePath, ".txt"), "original reference");
        var viewModel = CreateViewModel(voicesDirectory);
        await viewModel.OpenWorkspaceProjectAsync(root);
        EnableRoleDialogueVoiceRoute(viewModel);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        await viewModel.SetVoiceReferenceSelectionAsync(target, "chen.wav");
        var sourceAssetId = target.VoiceSourceAsset;
        var syncedPath = target.VoiceSyncedPath;

        target.VoiceReferenceText = "pending reference saved by global save";
        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);

        var row = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(candidate => !candidate.IsNarration && string.Equals(candidate.SubjectName, target.DisplayName, StringComparison.Ordinal));
        var draft = viewModel.WorkbenchDocument.RowsById[row.InteractionId];
        Assert.Equal("pending reference saved by global save", draft.VoiceBinding?.EffectiveReferenceText);
        Assert.Null(draft.VoiceBinding?.SourceAssetId);
        Assert.Null(draft.VoiceBinding?.SyncedVoicePath);
        Assert.False(target.HasAnyBindingEdit);
        Assert.False(viewModel.IsWorkbenchDirty);
        Assert.Contains("已保存到项目", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveWorkbenchEditsKeepsLocalCatalogVoiceAsComfyReference()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-save-local-catalog-voice-{Guid.NewGuid():N}");
        var voicesDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-comfy-save-local-catalog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(voicesDirectory);
        var localVoicePath = Path.Combine(voicesDirectory, "female_01.wav");
        await File.WriteAllBytesAsync(localVoicePath, [0x52, 0x49, 0x46, 0x46]);
        await File.WriteAllTextAsync(Path.ChangeExtension(localVoicePath, ".txt"), "catalog reference");
        var viewModel = CreateViewModel(voicesDirectory);
        await viewModel.OpenWorkspaceProjectAsync(root);
        EnableRoleDialogueVoiceRoute(viewModel);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        await viewModel.SetVoiceReferenceSelectionAsync(target, "female_01.wav");

        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);

        target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        var manifest = await new WorkspaceProjectManager().LoadAsync(root);
        Assert.DoesNotContain(manifest.Assets, candidate =>
            candidate.Kind == MediaAssetKind.Audio &&
            candidate.Source == MediaAssetSource.VoiceReference);
        Assert.Equal(string.Empty, target.VoiceSourceAsset);
        Assert.Equal(string.Empty, target.VoiceSyncedPath);
        var row = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(candidate => !candidate.IsNarration && string.Equals(candidate.SubjectName, target.DisplayName, StringComparison.Ordinal));
        var draft = viewModel.WorkbenchDocument.RowsById[row.InteractionId];
        Assert.Null(draft.VoiceBinding?.SourceAssetId);
        Assert.Null(draft.VoiceBinding?.SyncedVoicePath);
        Assert.Equal("female_01.wav", draft.VoiceBinding?.EffectiveVoiceReferenceId);
        Assert.Null(draft.VoiceBinding?.EffectiveReferenceText);
    }

    [Fact]
    public async Task SaveVoiceReferenceTextDoesNotWriteComfyVoicesSidecar()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-voice-text-save-{Guid.NewGuid():N}");
        var voicesDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-comfy-text-save-{Guid.NewGuid():N}");
        Directory.CreateDirectory(voicesDirectory);
        var voicePath = Path.Combine(voicesDirectory, "chen.wav");
        await File.WriteAllBytesAsync(voicePath, [0x52, 0x49, 0x46, 0x46]);
        await File.WriteAllTextAsync(Path.ChangeExtension(voicePath, ".txt"), "original reference");
        var viewModel = CreateViewModel(voicesDirectory);
        await viewModel.OpenWorkspaceProjectAsync(root);
        EnableRoleDialogueVoiceRoute(viewModel);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        await viewModel.SetVoiceReferenceSelectionAsync(target, "chen.wav");
        var referenceTextPath = Path.ChangeExtension(voicePath, ".txt");

        target.VoiceReferenceText = "saved edited reference";
        await viewModel.SaveVoiceReferenceTextCommand.ExecuteAsync(target);

        Assert.Equal("original reference", await File.ReadAllTextAsync(referenceTextPath));
        Assert.DoesNotContain("CharacterVoicesNode", target.StatusText, StringComparison.Ordinal);
        Assert.Contains("不写 ComfyUI voices 文本", target.StatusText, StringComparison.Ordinal);
        var manifest = await new WorkspaceProjectManager().LoadAsync(root);
        Assert.DoesNotContain(manifest.Assets, asset => asset.Source == MediaAssetSource.VoiceReference);
        var row = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(candidate => !candidate.IsNarration && string.Equals(candidate.SubjectName, target.DisplayName, StringComparison.Ordinal));
        var draft = viewModel.WorkbenchDocument.RowsById[row.InteractionId];
        Assert.Null(draft.VoiceBinding?.EffectiveReferenceText);
        Assert.Null(draft.VoiceBinding?.SourceAssetId);
        Assert.Null(draft.VoiceBinding?.SyncedVoicePath);
    }

    [Fact]
    public async Task VoiceReferenceWithoutSidecarAllowsTemporaryReferenceTextButDoesNotSaveFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-voice-temp-text-{Guid.NewGuid():N}");
        var voicesDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-comfy-temp-text-{Guid.NewGuid():N}");
        Directory.CreateDirectory(voicesDirectory);
        var voicePath = Path.Combine(voicesDirectory, "chen.wav");
        await File.WriteAllBytesAsync(voicePath, [0x52, 0x49, 0x46, 0x46]);
        var viewModel = CreateViewModel(voicesDirectory);
        await viewModel.OpenWorkspaceProjectAsync(root);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Narration);

        await viewModel.SetVoiceReferenceSelectionAsync(target, "chen.wav");

        Assert.True(target.CanEditVoiceReferenceText);
        target.VoiceReferenceText = "temporary workflow reference";
        viewModel.ApplyBindingTargetCommand.Execute(target);
        await viewModel.SaveVoiceReferenceTextCommand.ExecuteAsync(target);

        Assert.False(File.Exists(Path.ChangeExtension(voicePath, ".txt")));
        Assert.False(File.Exists(Path.Combine(voicesDirectory, "chen.reference.txt")));
        Assert.Contains("不写 ComfyUI voices 文本", target.StatusText, StringComparison.Ordinal);
        var row = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(candidate => candidate.IsNarration);
        var draft = viewModel.WorkbenchDocument.RowsById[row.InteractionId];
        Assert.Equal("temporary workflow reference", draft.VoiceBinding?.EffectiveReferenceText);
    }

    [Fact]
    public async Task SaveVoiceReferenceTextDoesNotCreateCatalogSidecarWhenRemoteVoiceIsNotCached()
    {
        var voicesDirectory = CreateIsolatedVoicesDirectory("catalog-text-save");
        var audioPreview = new CapturingAudioPreviewService();
        var viewModel = CreateViewModel(voicesDirectory, audioPreview);
        viewModel.ComfyEndpoint = "http://127.0.0.1:8188";
        EnableRoleDialogueVoiceRoute(viewModel);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        await viewModel.SetVoiceReferenceSelectionAsync(target, "female_01.wav");

        await viewModel.PreviewVoiceCommand.ExecuteAsync(target);
        target.VoiceReferenceText = "catalog sidecar reference";
        await viewModel.SaveVoiceReferenceTextCommand.ExecuteAsync(target);

        var referenceTextPath = Path.Combine(Path.GetDirectoryName(voicesDirectory)!, "TTS", "voices", "female_01.txt");
        Assert.Equal(
            "http://127.0.0.1:8188/api/tts-audio-suite/voice-preview?voice_name=female_01.wav",
            audioPreview.LastTarget);
        Assert.False(File.Exists(Path.ChangeExtension(referenceTextPath, ".wav")));
        Assert.False(File.Exists(referenceTextPath));
        Assert.DoesNotContain("CharacterVoicesNode", target.StatusText, StringComparison.Ordinal);
        Assert.Contains("不写 ComfyUI voices 文本", target.StatusText, StringComparison.Ordinal);
        var row = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(candidate => !candidate.IsNarration && string.Equals(candidate.SubjectName, target.DisplayName, StringComparison.Ordinal));
        var draft = viewModel.WorkbenchDocument.RowsById[row.InteractionId];
        Assert.Equal("female_01.wav", draft.VoiceBinding?.EffectiveVoiceReferenceId);
        Assert.NotEqual("catalog sidecar reference", draft.VoiceBinding?.EffectiveReferenceText);
        Assert.Null(draft.VoiceBinding?.SourceAssetId);
        Assert.Null(draft.VoiceBinding?.SyncedVoicePath);
    }

    [Fact]
    public async Task SaveVoiceReferenceTextDoesNotWriteLocalCatalogSidecar()
    {
        var voicesDirectory = CreateIsolatedVoicesDirectory("local-catalog-text-save");
        var ttsVoicesDirectory = Path.Combine(Path.GetDirectoryName(voicesDirectory)!, "TTS", "voices");
        Directory.CreateDirectory(ttsVoicesDirectory);
        var localVoicePath = Path.Combine(ttsVoicesDirectory, "female_01.wav");
        await File.WriteAllBytesAsync(localVoicePath, [0x52, 0x49, 0x46, 0x46]);
        var viewModel = CreateViewModel(voicesDirectory);
        EnableRoleDialogueVoiceRoute(viewModel);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        await viewModel.SetVoiceReferenceSelectionAsync(target, "female_01.wav");

        target.VoiceReferenceText = "local catalog reference";
        await viewModel.SaveVoiceReferenceTextCommand.ExecuteAsync(target);

        var referenceTextPath = Path.ChangeExtension(localVoicePath, ".txt");
        Assert.False(File.Exists(referenceTextPath));
        Assert.DoesNotContain("CharacterVoicesNode", target.StatusText, StringComparison.Ordinal);
        Assert.Contains("不写 ComfyUI voices 文本", target.StatusText, StringComparison.Ordinal);
        var row = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(candidate => !candidate.IsNarration && string.Equals(candidate.SubjectName, target.DisplayName, StringComparison.Ordinal));
        var draft = viewModel.WorkbenchDocument.RowsById[row.InteractionId];
        Assert.Equal("female_01.wav", draft.VoiceBinding?.EffectiveVoiceReferenceId);
        Assert.NotEqual("local catalog reference", draft.VoiceBinding?.EffectiveReferenceText);
    }

    [Fact]
    public async Task SaveVoiceReferenceTextWithoutProjectSourceReportsNoSidecarTarget()
    {
        var voicesDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-comfy-text-save-failure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(voicesDirectory);
        var voicePath = Path.Combine(voicesDirectory, "chen.wav");
        await File.WriteAllBytesAsync(voicePath, [0x52, 0x49, 0x46, 0x46]);
        var viewModel = CreateViewModel(voicesDirectory);
        EnableRoleDialogueVoiceRoute(viewModel);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        await viewModel.SetVoiceReferenceSelectionAsync(target, "chen.wav");

        target.VoiceReferenceText = "cannot be saved";
        await viewModel.SaveVoiceReferenceTextCommand.ExecuteAsync(target);

        Assert.DoesNotContain("CharacterVoicesNode", target.StatusText, StringComparison.Ordinal);
        Assert.Contains("不写 ComfyUI voices 文本", target.StatusText, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.ChangeExtension(voicePath, ".txt")));
        var row = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(candidate => !candidate.IsNarration && string.Equals(candidate.SubjectName, target.DisplayName, StringComparison.Ordinal));
        var draft = viewModel.WorkbenchDocument.RowsById[row.InteractionId];
        Assert.NotEqual("cannot be saved", draft.VoiceBinding?.EffectiveReferenceText);
    }


    [Fact]
    public void ReturnToWorkbenchFromExportIssuesClearsPreflightPanel()
    {
        var viewModel = new MainWindowViewModel(null, loadLastProject: false);
        viewModel.ExportPreflightIssues.Add(new ExportPreflightIssueViewModel(
            1,
            EditingTimelinePreflightIssueKind.PsdReference,
            "PSD source still needs rendered PNG",
            "scene_001",
            "interaction_001",
            "asset_psd",
            string.Empty,
            null,
            "Render PSD to PNG"));

        viewModel.ReturnToWorkbenchFromExportIssuesCommand.Execute(null);

        Assert.Empty(viewModel.ExportPreflightIssues);
        Assert.False(viewModel.HasExportPreflightIssues);
        Assert.Contains("已返回工作台", viewModel.StatusText);
    }

    [Fact]
    public async Task ExportProjectPromptsForMissingVisualsThenWritesFcpxmlAndSrt()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-export-project-{Guid.NewGuid():N}");
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Export Project Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var missingImagePath = Path.Combine(workspace.Folders.ImagesImported, "missing_scene_001.png");
        var audioPath = Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav");
        await CreateWavFileAsync(audioPath, 2);
        var project = new SceneInteractionBuilder().Build("陈默说：我来晚了。");
        var scene = project.Scenes.Single();
        var interaction = scene.Interactions.Single(candidate => candidate.Dialogue is not null);
        project = project with
        {
            Scenes =
            [
                scene with
                {
                    AssetBinding = scene.AssetBinding with { BackgroundImage = missingImagePath },
                    Interactions = scene.Interactions
                        .Select(candidate => candidate with
                        {
                            Binding = candidate.Binding with
                            {
                                SceneVisualBinding = VisualAssetBinding.FromImage(missingImagePath),
                                SubjectCharacterVisualBinding = VisualAssetBinding.FromImage(missingImagePath)
                            }
                        })
                        .ToArray()
                }
            ],
            GenerationTasks =
            [
                new GenerationTask(
                    "tts_export_row",
                    GenerationTaskType.Tts,
                    interaction.Id,
                    "TTS",
                    "comfyui",
                    GenerationTaskStatus.Completed,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["text"] = interaction.Dialogue!.Text
                    },
                    audioPath)
            ]
        };
        var outputPath = Path.Combine(workspace.Folders.Exports, "interactive.fcpxml");
        var viewModel = CreateViewModel();
        viewModel.Project = project;
        await viewModel.OpenWorkspaceProjectAsync(root);
        viewModel.Project = project;
        var prompted = false;
        viewModel.ConfirmExportMissingVisualsAsync = _ =>
        {
            prompted = true;
            return Task.FromResult(true);
        };
        viewModel.SelectExportDestinationAsync = request =>
        {
            Assert.Equal(WorkbenchExportFormat.Fcpxml, request.Format);
            Assert.Equal(workspace.Folders.Exports, request.WorkspaceExportDirectory);
            return Task.FromResult<ExportDestinationSelection?>(new ExportDestinationSelection(WorkbenchExportFormat.Fcpxml, outputPath));
        };

        await viewModel.ExportProjectCommand.ExecuteAsync(null);

        Assert.False(prompted);
        Assert.True(File.Exists(outputPath));
        Assert.True(File.Exists(Path.Combine(workspace.Folders.Exports, "srt", $"audio_{interaction.Id}.srt")));
        Assert.Contains("剪辑项目已导出", viewModel.StatusText);
        Assert.False(viewModel.HasExportPreflightIssues);
        Assert.Contains("<gap", await File.ReadAllTextAsync(outputPath));
    }

    [Fact]
    public async Task ExportProjectAllowsPsdPreflightContinueWithoutReferencingPsdSource()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-export-psd-gap-{Guid.NewGuid():N}");
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Export PSD Gap Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var mediaService = new ProjectMediaAssetService();
        var psdSource = Path.Combine(Path.GetTempPath(), $"draft-agent-export-source-{Guid.NewGuid():N}.psd");
        await File.WriteAllBytesAsync(psdSource, [1, 2, 3]);
        var psdImport = await mediaService.ImportAsync(workspace, new MediaImportRequest(psdSource, MediaAssetKind.Psd, MediaAssetSource.ImportedPsd, "char_chen"));
        var audioSource = Path.Combine(Path.GetTempPath(), $"draft-agent-export-audio-{Guid.NewGuid():N}.wav");
        await CreateWavFileAsync(audioSource, 2);
        var project = new SceneInteractionBuilder().Build("陈默说：我来晚了。");
        var scene = project.Scenes.Single();
        var interaction = scene.Interactions.Single(candidate => candidate.Dialogue is not null);
        var audioImport = await mediaService.ImportAsync(
            workspace,
            new MediaImportRequest(audioSource, MediaAssetKind.Audio, MediaAssetSource.TtsGenerated, interaction.Id));
        project = project with
        {
            Scenes =
            [
                scene with
                {
                    AssetBinding = scene.AssetBinding with
                    {
                        BackgroundImage = psdImport.Asset.Id,
                        BackgroundVisualBinding = VisualAssetBinding.FromPsd(psdImport.Asset.Id)
                    },
                    Interactions = scene.Interactions
                        .Select(candidate => candidate with
                        {
                            Binding = candidate.Binding with
                            {
                                SceneVisualBinding = VisualAssetBinding.FromPsd(psdImport.Asset.Id),
                                SubjectCharacterVisualBinding = VisualAssetBinding.FromPsd(psdImport.Asset.Id)
                            }
                        })
                        .ToArray()
                }
            ],
            GenerationTasks =
            [
                new GenerationTask(
                    "tts_export_psd_gap",
                    GenerationTaskType.Tts,
                    interaction.Id,
                    "TTS",
                    "comfyui",
                    GenerationTaskStatus.Completed,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["text"] = interaction.Dialogue!.Text
                    },
                    audioImport.Asset.Id)
            ]
        };
        var outputPath = Path.Combine(workspace.Folders.Exports, "psd-gap.fcpxml");
        var viewModel = CreateViewModel();
        viewModel.Project = project;
        await viewModel.OpenWorkspaceProjectAsync(root);
        viewModel.Project = project;
        var prompted = false;
        viewModel.ConfirmExportMissingVisualsAsync = _ =>
        {
            prompted = true;
            return Task.FromResult(true);
        };
        viewModel.SelectExportDestinationAsync = request =>
        {
            Assert.Equal(WorkbenchExportFormat.Fcpxml, request.Format);
            return Task.FromResult<ExportDestinationSelection?>(new ExportDestinationSelection(WorkbenchExportFormat.Fcpxml, outputPath));
        };

        await viewModel.ExportProjectCommand.ExecuteAsync(null);

        Assert.False(prompted);
        Assert.True(File.Exists(outputPath), viewModel.StatusText);
        var fcpxml = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("<gap", fcpxml);
        Assert.DoesNotContain(psdImport.Asset.AbsolutePath, fcpxml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(psdImport.Asset.Id, fcpxml, StringComparison.Ordinal);
        Assert.DoesNotContain(psdImport.Asset.DisplayName, fcpxml, StringComparison.Ordinal);
        Assert.False(viewModel.HasExportPreflightIssues);
        Assert.Contains("剪辑项目已导出", viewModel.StatusText);
    }

    [Fact]
    public async Task ExportProjectJianyingDraftReportsDraftExport()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-export-jianying-{Guid.NewGuid():N}");
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Export Jianying Boundary Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var imagePath = Path.Combine(workspace.Folders.ImagesImported, "scene_001.png");
        var audioPath = Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(audioPath)!);
        await File.WriteAllBytesAsync(imagePath, [1]);
        await CreateWavFileAsync(audioPath, 2);
        var project = new SceneInteractionBuilder().Build("陈默说：我来晚了。");
        var scene = project.Scenes.Single();
        var interaction = scene.Interactions.Single(candidate => candidate.Dialogue is not null);
        project = project with
        {
            Scenes =
            [
                scene with
                {
                    AssetBinding = scene.AssetBinding with { BackgroundImage = imagePath },
                    Interactions = scene.Interactions
                        .Select(candidate => candidate with
                        {
                            Binding = candidate.Binding with
                            {
                                SceneVisualBinding = VisualAssetBinding.FromImage(imagePath),
                                SubjectCharacterVisualBinding = VisualAssetBinding.FromImage(imagePath)
                            }
                        })
                        .ToArray()
                }
            ],
            GenerationTasks =
            [
                new GenerationTask(
                    "tts_export_jianying_row",
                    GenerationTaskType.Tts,
                    interaction.Id,
                    "TTS",
                    "comfyui",
                    GenerationTaskStatus.Completed,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["text"] = interaction.Dialogue!.Text
                    },
                    audioPath)
            ]
        };
        var outputDirectory = Path.Combine(workspace.Folders.Exports, "jianying-compatible");
        var viewModel = CreateViewModel();
        viewModel.Project = project;
        await viewModel.OpenWorkspaceProjectAsync(root);
        viewModel.Project = project;
        viewModel.SelectExportDestinationAsync = request =>
        {
            Assert.Equal(WorkbenchExportFormat.Fcpxml, request.Format);
            return Task.FromResult<ExportDestinationSelection?>(new ExportDestinationSelection(WorkbenchExportFormat.JianyingDraft, outputDirectory));
        };

        await viewModel.ExportProjectCommand.ExecuteAsync(null);

        Assert.True(File.Exists(Path.Combine(outputDirectory, "draft_content.json")));
        Assert.Contains("剪映草稿已导出", viewModel.StatusText);
        Assert.DoesNotContain("剪辑项目已导出", viewModel.StatusText);
    }

    [Fact]
    public async Task ExportProjectAnimateTimelineReportsAudioSubtitleExport()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-export-animate-{Guid.NewGuid():N}");
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Export Animate Boundary Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var imagePath = Path.Combine(workspace.Folders.ImagesImported, "scene_001.png");
        var audioPath = Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(audioPath)!);
        await File.WriteAllBytesAsync(imagePath, [1]);
        await CreateWavFileAsync(audioPath, 2);
        var project = new SceneInteractionBuilder().Build("陈默说：我来晚了。");
        var scene = project.Scenes.Single();
        var interaction = scene.Interactions.Single(candidate => candidate.Dialogue is not null);
        project = project with
        {
            Scenes =
            [
                scene with
                {
                    AssetBinding = scene.AssetBinding with { BackgroundImage = imagePath },
                    Interactions = scene.Interactions
                        .Select(candidate => candidate with
                        {
                            Binding = candidate.Binding with
                            {
                                SceneVisualBinding = VisualAssetBinding.FromImage(imagePath),
                                SubjectCharacterVisualBinding = VisualAssetBinding.FromImage(imagePath)
                            }
                        })
                        .ToArray()
                }
            ],
            GenerationTasks =
            [
                new GenerationTask(
                    "tts_export_animate_row",
                    GenerationTaskType.Tts,
                    interaction.Id,
                    "TTS",
                    "comfyui",
                    GenerationTaskStatus.Completed,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["text"] = interaction.Dialogue!.Text
                    },
                    audioPath)
            ]
        };
        var outputDirectory = Path.Combine(workspace.Folders.Exports, "animate-audio-subtitle");
        var viewModel = CreateViewModel();
        viewModel.Project = project;
        await viewModel.OpenWorkspaceProjectAsync(root);
        viewModel.Project = project;
        viewModel.SelectExportDestinationAsync = request =>
        {
            Assert.Equal(WorkbenchExportFormat.Fcpxml, request.Format);
            return Task.FromResult<ExportDestinationSelection?>(new ExportDestinationSelection(WorkbenchExportFormat.AnimateTimeline, outputDirectory));
        };

        await viewModel.ExportProjectCommand.ExecuteAsync(null);

        Assert.True(File.Exists(Path.Combine(outputDirectory, "draft-agent-animate-subtitles.json")), viewModel.StatusText);
        Assert.True(File.Exists(Path.Combine(outputDirectory, "import-draft-agent-audio-subtitles.jsfl")));
        Assert.True(Directory.Exists(Path.Combine(outputDirectory, "audio")));
        Assert.Contains("Animate 音频字幕已导出", viewModel.StatusText);
        Assert.DoesNotContain("剪辑项目已导出", viewModel.StatusText);
    }

    [Fact]
    public async Task ExportProjectLocksWorkbenchDataEditsUntilUserCancelsDestination()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-export-lock-{Guid.NewGuid():N}");
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Export Lock Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var imagePath = Path.Combine(workspace.Folders.ImagesImported, "scene_001.png");
        var audioPath = Path.Combine(workspace.Folders.AudioGenerated, "line_001.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(audioPath)!);
        await File.WriteAllBytesAsync(imagePath, [1]);
        await CreateWavFileAsync(audioPath, 2);
        var project = new SceneInteractionBuilder().Build("陈默说：我来晚了。");
        var scene = project.Scenes.Single();
        var interaction = scene.Interactions.Single(candidate => candidate.Dialogue is not null);
        project = project with
        {
            Scenes =
            [
                scene with
                {
                    AssetBinding = scene.AssetBinding with { BackgroundImage = imagePath },
                    Interactions = scene.Interactions
                        .Select(candidate => candidate with
                        {
                            Binding = candidate.Binding with
                            {
                                SceneVisualBinding = VisualAssetBinding.FromImage(imagePath),
                                SubjectCharacterVisualBinding = VisualAssetBinding.FromImage(imagePath)
                            }
                        })
                        .ToArray()
                }
            ],
            GenerationTasks =
            [
                new GenerationTask(
                    "tts_export_lock_row",
                    GenerationTaskType.Tts,
                    interaction.Id,
                    "TTS",
                    "comfyui",
                    GenerationTaskStatus.Completed,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["text"] = interaction.Dialogue!.Text
                    },
                    audioPath)
            ]
        };
        var viewModel = CreateViewModel();
        viewModel.Project = project;
        await viewModel.OpenWorkspaceProjectAsync(root);
        viewModel.Project = project;
        var editableScene = viewModel.SceneTables[0];
        var editableRow = editableScene.Rows.First(candidate => !candidate.IsNarration);
        var originalSceneName = editableScene.SceneName;
        var originalLineText = editableRow.LineText;
        viewModel.SelectExportDestinationAsync = _ =>
        {
            Assert.True(viewModel.IsProjectExportRunning);
            Assert.True(viewModel.IsWorkbenchMutationLocked);
            Assert.False(viewModel.CanEditWorkbenchStructure);
            Assert.False(editableRow.CanEditTextAndEmotion);

            editableScene.SceneName = "导出中不应保留场景名";
            editableRow.LineText = "导出中不应保留台词";

            Assert.Equal(originalSceneName, editableScene.SceneName);
            Assert.Equal(originalLineText, editableRow.LineText);
            Assert.Contains("导出", viewModel.StatusText, StringComparison.Ordinal);
            return Task.FromResult<ExportDestinationSelection?>(null);
        };

        await viewModel.ExportProjectCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsProjectExportRunning);
        Assert.False(viewModel.IsWorkbenchMutationLocked);
        Assert.True(viewModel.CanEditWorkbenchStructure);
        Assert.True(editableRow.CanEditTextAndEmotion);
        Assert.Equal(originalSceneName, editableScene.SceneName);
        Assert.Equal(originalLineText, editableRow.LineText);
        Assert.Contains("导出已取消", viewModel.StatusText);
    }

    [Fact]
    public async Task ExportProjectBlocksCompletedTtsWhenAudioFileIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-export-missing-audio-{Guid.NewGuid():N}");
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "Export Missing Audio Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var imagePath = Path.Combine(workspace.Folders.ImagesImported, "scene_001.png");
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        await File.WriteAllBytesAsync(imagePath, [1]);
        var missingAudioPath = Path.Combine(workspace.Folders.AudioGenerated, "line_missing.wav");
        var project = new SceneInteractionBuilder().Build("陈默说：我来晚了。");
        var scene = project.Scenes.Single();
        var interaction = scene.Interactions.Single(candidate => candidate.Dialogue is not null);
        project = project with
        {
            Scenes =
            [
                scene with
                {
                    AssetBinding = scene.AssetBinding with { BackgroundImage = imagePath },
                    Interactions = scene.Interactions
                        .Select(candidate => candidate with
                        {
                            Binding = candidate.Binding with
                            {
                                SceneVisualBinding = VisualAssetBinding.FromImage(imagePath),
                                SubjectCharacterVisualBinding = VisualAssetBinding.FromImage(imagePath)
                            }
                        })
                        .ToArray()
                }
            ],
            GenerationTasks =
            [
                new GenerationTask(
                    "tts_missing_audio",
                    GenerationTaskType.Tts,
                    interaction.Id,
                    "TTS",
                    "comfyui",
                    GenerationTaskStatus.Completed,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["text"] = interaction.Dialogue!.Text
                    },
                    missingAudioPath)
            ]
        };
        var outputPath = Path.Combine(workspace.Folders.Exports, "blocked.fcpxml");
        var viewModel = CreateViewModel();
        viewModel.Project = project;
        await viewModel.OpenWorkspaceProjectAsync(root);
        viewModel.Project = project;
        var prompted = false;
        var destinationRequested = false;
        viewModel.ConfirmExportMissingVisualsAsync = _ =>
        {
            prompted = true;
            return Task.FromResult(true);
        };
        viewModel.SelectExportDestinationAsync = _ =>
        {
            destinationRequested = true;
            return Task.FromResult<ExportDestinationSelection?>(new ExportDestinationSelection(WorkbenchExportFormat.Fcpxml, outputPath));
        };

        await viewModel.ExportProjectCommand.ExecuteAsync(null);

        Assert.False(prompted);
        Assert.False(destinationRequested);
        Assert.False(File.Exists(outputPath));
        var issue = Assert.Single(viewModel.ExportPreflightIssues);
        Assert.Equal(EditingTimelinePreflightIssueKind.MissingAudio, issue.Kind);
        Assert.Equal("语音文件缺失", issue.SummaryText);
        Assert.Contains("语音问题", viewModel.StatusText);
    }

    [Fact]
    public async Task ExportJsonCommandDoesNotExportOtioWhenWorkspaceIsOpen()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-json-no-otio-{Guid.NewGuid():N}");
        var manager = new WorkspaceProjectManager();
        var manifest = await manager.CreateOrOpenAsync(root, "JSON No OTIO Test");
        var workspace = WorkspaceProjectManager.ToWorkspace(manifest);
        var viewModel = CreateViewModel();
        await viewModel.OpenWorkspaceProjectAsync(root);

        await viewModel.ExportJsonCommand.ExecuteAsync(null);

        Assert.Contains("\"scenes\"", viewModel.LlmJsonPreview);
        Assert.Empty(Directory.EnumerateFiles(workspace.Folders.Otio, "*.otio"));
        Assert.DoesNotContain("剪辑项目文件已导出", viewModel.StatusText);
        Assert.DoesNotContain(".otio", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FCPXML", viewModel.StatusText);
        Assert.Contains("剪映草稿", viewModel.StatusText);
        Assert.DoesNotContain("剪映" + "兼容描述", viewModel.StatusText);
    }

    [Fact]
    public async Task SynthesizeVoicesDoesNotResyncCustomAudioIntoComfyVoices()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-voice-autoresync-{Guid.NewGuid():N}");
        var voicesDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-comfy-autoresync-{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-voice-autoresync-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDirectory);
        var sourceAudioPath = Path.Combine(sourceDirectory, "chen.wav");
        await File.WriteAllBytesAsync(sourceAudioPath, [0x52, 0x49, 0x46, 0x46]);
        await File.WriteAllTextAsync(Path.ChangeExtension(sourceAudioPath, ".txt"), "auto resync line");
        var client = new CapturingComfyUiClient();
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            client,
            new InMemoryInteractionProjectStore());
        var viewModel = new MainWindowViewModel(
            service,
            comfyVoicesDirectoryResolver: () => voicesDirectory);
        await viewModel.OpenWorkspaceProjectAsync(root);
        EnableRoleDialogueVoiceRoute(viewModel);
        BindAllRowsToDefaultVoice(viewModel);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        await viewModel.SetVoiceBindingAsync(target, sourceAudioPath);

        await viewModel.SynthesizeAllVoicesCommand.ExecuteAsync(null);

        target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        Assert.Equal(string.Empty, target.VoiceSyncedPath);
        Assert.False(Directory.Exists(voicesDirectory) &&
            Directory.EnumerateFiles(voicesDirectory, "*", SearchOption.AllDirectories).Any());
        var targetRow = viewModel.SceneTables.SelectMany(scene => scene.Rows).First(row => !row.IsNarration);
        Assert.DoesNotContain(client.Requests, request =>
            request.Task.Type == GenerationTaskType.Tts &&
            request.Task.TargetId == targetRow.InteractionId &&
            request.Task.Parameters.TryGetValue("voiceReferenceId", out var voiceReferenceId) &&
            string.Equals(voiceReferenceId, sourceAudioPath, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(client.Requests, request =>
            request.Task.Type == GenerationTaskType.Tts &&
            request.Task.TargetId == targetRow.InteractionId &&
            request.Task.Parameters.TryGetValue("voiceReferenceId", out var voiceReferenceId) &&
            string.Equals(voiceReferenceId, Path.GetFileName(sourceAudioPath), StringComparison.OrdinalIgnoreCase));
        Assert.Contains("手动放到 ComfyUI\\models\\voices", target.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SynthesizeVoicesRequiresWorkspaceProjectDirectoryBeforeSubmitting()
    {
        var client = new CapturingComfyUiClient();
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            client,
            new InMemoryInteractionProjectStore());
        var viewModel = ConfigureFastTts(new MainWindowViewModel(service, loadLastProject: false));
        BindAllRowsToDefaultVoice(viewModel);

        await viewModel.SynthesizeAllVoicesCommand.ExecuteAsync(null);

        Assert.Empty(client.Requests);
        Assert.False(viewModel.IsTtsSynthesisRunning);
        Assert.Equal("未提交：请先选择项目保存目录。", viewModel.ComfySubmitStatusText);
        Assert.Contains("选择或新建项目文件夹", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SynthesizeVoicesDoesNotResyncNewVoiceIdFromPreviousProjectSource()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-voice-no-cross-resync-{Guid.NewGuid():N}");
        var voicesDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-comfy-no-cross-resync-{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-voice-no-cross-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDirectory);
        var sourceAudioPath = Path.Combine(sourceDirectory, "chen.wav");
        await File.WriteAllBytesAsync(sourceAudioPath, [0x52, 0x49, 0x46, 0x46]);
        await File.WriteAllTextAsync(Path.ChangeExtension(sourceAudioPath, ".txt"), "old source line");
        var client = new CapturingComfyUiClient();
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            client,
            new InMemoryInteractionProjectStore());
        var viewModel = new MainWindowViewModel(
            service,
            comfyVoicesDirectoryResolver: () => voicesDirectory);
        await viewModel.OpenWorkspaceProjectAsync(root);
        EnableRoleDialogueVoiceRoute(viewModel);
        BindAllRowsToDefaultVoice(viewModel);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        await viewModel.SetVoiceBindingAsync(target, sourceAudioPath);
        viewModel.ApplyBindingTargetCommand.Execute(target);
        var previousSourceAsset = target.VoiceSourceAsset;
        var previousSyncedPath = target.VoiceSyncedPath;
        var targetRow = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(candidate => !candidate.IsNarration && string.Equals(candidate.SubjectName, target.DisplayName, StringComparison.Ordinal));

        target.VoiceAsset = "new-voice.wav";
        target.VoiceReferenceText = "new reference line";
        viewModel.ApplyBindingTargetCommand.Execute(target);

        await viewModel.SynthesizeAllVoicesCommand.ExecuteAsync(null);

        target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        Assert.Equal("new-voice.wav", target.VoiceAsset);
        Assert.Equal(string.Empty, target.VoiceSourceAsset);
        Assert.Equal(string.Empty, target.VoiceSyncedPath);
        var submittedTtsTask = Assert.Single(client.Requests, request =>
            request.Task.Type == GenerationTaskType.Tts &&
            request.Task.TargetId == targetRow.InteractionId);
        Assert.Equal("new-voice.wav", submittedTtsTask.Task.Parameters["voiceReferenceId"]);
        Assert.Equal("new reference line", submittedTtsTask.Task.Parameters["referenceText"]);
        Assert.False(submittedTtsTask.Task.Parameters.TryGetValue("voiceSourceAssetId", out var submittedSourceAsset) &&
            string.Equals(submittedSourceAsset, previousSourceAsset, StringComparison.Ordinal));
        Assert.False(submittedTtsTask.Task.Parameters.TryGetValue("voiceSyncedPath", out var submittedSyncedPath) &&
            string.Equals(submittedSyncedPath, previousSyncedPath, StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(voicesDirectory, "new-voice.wav")));
    }

    [Fact]
    public async Task SynthesizeVoicesImportsEachCompletedTtsAndLeavesPreviewButtonReady()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-live-tts-import-{Guid.NewGuid():N}");
        var client = new DownloadingComfyUiClient();
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            client,
            new InMemoryInteractionProjectStore());
        var viewModel = ConfigureFastTts(new MainWindowViewModel(service));
        await viewModel.OpenWorkspaceProjectAsync(root);
        viewModel.AddDialogueRowCommand.Execute(viewModel.SceneTables[0]);
        BindAllRowsToDefaultVoice(viewModel);

        await viewModel.SynthesizeAllVoicesCommand.ExecuteAsync(null);

        var ttsTaskCount = viewModel.Project.GenerationTasks.Count(task => task.Type == GenerationTaskType.Tts);
        Assert.False(viewModel.IsTtsSynthesisRunning);
        Assert.True(viewModel.CanDragWorkbench);
        Assert.False(viewModel.IsAudioPlayerVisible);
        Assert.Equal(string.Empty, viewModel.ActiveAudioTarget);
        Assert.Equal(ttsTaskCount, client.Requests.Count(request => request.Task.Type == GenerationTaskType.Tts));
        Assert.Equal(ttsTaskCount, client.Downloads.Count(reference => reference.EndsWith(".flac", StringComparison.OrdinalIgnoreCase)));
        Assert.All(
            viewModel.Project.GenerationTasks.Where(task => task.Type == GenerationTaskType.Tts),
            task =>
            {
                Assert.Equal(GenerationTaskStatus.Completed, task.Status);
                Assert.StartsWith("asset_", task.ResultAssetId);
            });
        Assert.All(
            viewModel.SceneTables.SelectMany(scene => scene.Rows).Where(row => row.HasTtsTask),
            row =>
            {
                Assert.True(row.HasSynthesizedVoice);
                Assert.False(string.IsNullOrWhiteSpace(row.SynthesizedAudioAsset));
            });
        Assert.Equal(ttsTaskCount, viewModel.TtsCompletedCount);
    }

    [Fact]
    public async Task CompletedTtsCanPreviewThenEditTextEmotionAndRegenerateWithLatestValues()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-tts-preview-regenerate-{Guid.NewGuid():N}");
        var client = new DownloadingComfyUiClient();
        var audioPreview = new CapturingAudioPreviewService();
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            client,
            new InMemoryInteractionProjectStore());
        var viewModel = ConfigureFastTts(new MainWindowViewModel(service, audioPreviewService: audioPreview));
        await viewModel.OpenWorkspaceProjectAsync(root);
        BindAllRowsToDefaultVoice(viewModel);

        await viewModel.SynthesizeAllVoicesCommand.ExecuteAsync(null);

        var row = viewModel.SceneTables.SelectMany(scene => scene.Rows).First(candidate => !candidate.IsNarration);
        Assert.True(row.HasSynthesizedVoice);
        Assert.True(row.CanPreviewVoice);
        await viewModel.PreviewRowVoiceCommand.ExecuteAsync(row);
        Assert.False(string.IsNullOrWhiteSpace(audioPreview.LastTarget));
        Assert.True(viewModel.IsAudioPlayerVisible);

        row.LineText = "完成后修改文本再重生成";
        row.Emotion = "Happy 0.7";
        Assert.True(row.CanEditTextAndEmotion);
        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);
        row = viewModel.SceneTables.SelectMany(scene => scene.Rows).Single(candidate => candidate.InteractionId == row.InteractionId);
        Assert.False(row.HasSynthesizedVoice);
        Assert.False(row.CanPreviewVoice);
        var requestCountBeforeRegenerate = client.Requests.Count;

        await viewModel.RegenerateRowVoiceCommand.ExecuteAsync(row);

        var regenerateRequest = client.Requests.Skip(requestCountBeforeRegenerate).Single(request =>
            request.Task.Type == GenerationTaskType.Tts &&
            request.Task.TargetId == row.InteractionId);
        Assert.Equal("完成后修改文本再重生成", regenerateRequest.Task.Parameters["text"]);
        Assert.Equal("Happy", regenerateRequest.Task.Parameters["emotion"]);
        Assert.True(regenerateRequest.Task.Parameters.ContainsKey("emotionStrength"));
        Assert.True(row.HasSynthesizedVoice);
        Assert.True(row.CanPreviewVoice);
    }

    [Fact]
    public async Task SetVoiceReferenceSelectionAppliesDropdownChoiceToWorkbench()
    {
        var viewModel = CreateViewModel();
        EnableRoleDialogueVoiceRoute(viewModel);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        target.VoiceAsset = "dropdown-voice.flac";
        target.VoiceReferenceText = "dropdown reference";

        await viewModel.SetVoiceReferenceSelectionAsync(target, target.VoiceAsset);

        var row = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(candidate => !candidate.IsNarration && string.Equals(candidate.SubjectName, target.DisplayName, StringComparison.Ordinal));
        var draft = viewModel.WorkbenchDocument.RowsById[row.InteractionId];
        Assert.Equal("dropdown-voice.flac", draft.VoiceBinding?.EffectiveVoiceReferenceId);
        Assert.Equal("dropdown reference", draft.VoiceBinding?.EffectiveReferenceText);
        Assert.Equal("dropdown-voice.flac", draft.VoiceBinding?.VoiceReferenceId);
    }

    [Fact]
    public async Task SetVoiceReferenceSelectionClearsStaleTextAndSyncedPathWithoutReadingLocalSidecar()
    {
        var voicesDirectory = Path.Combine(Path.GetTempPath(), $"draft-agent-dropdown-text-voices-{Guid.NewGuid():N}");
        Directory.CreateDirectory(voicesDirectory);
        await File.WriteAllTextAsync(Path.Combine(voicesDirectory, "new_voice.txt"), "new reference text");
        var viewModel = CreateViewModel(voicesDirectory);
        EnableRoleDialogueVoiceRoute(viewModel);
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        target.VoiceAsset = "old_voice.wav";
        target.VoiceSyncedPath = Path.Combine(voicesDirectory, "old_voice.wav");
        await File.WriteAllTextAsync(Path.ChangeExtension(target.VoiceSyncedPath, ".txt"), "old stale text");
        target.VoiceReferenceText = "manual previous text";

        await viewModel.SetVoiceReferenceSelectionAsync(target, "new_voice.wav");

        Assert.Equal("new_voice.wav", target.VoiceAsset);
        Assert.Equal(string.Empty, target.VoiceSyncedPath);
        Assert.Equal(string.Empty, target.VoiceSourceAsset);
        Assert.Equal(string.Empty, target.VoiceReferenceText);
        var row = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(candidate => !candidate.IsNarration && string.Equals(candidate.SubjectName, target.DisplayName, StringComparison.Ordinal));
        Assert.Null(viewModel.WorkbenchDocument.RowsById[row.InteractionId].VoiceBinding?.EffectiveReferenceText);
    }

    [Fact]
    public async Task RefreshVoiceCatalogUsesComfyCharacterVoicesNodeAndFiltersExamples()
    {
        var voicesDirectory = CreateIsolatedVoicesDirectory("dropdown-local-first");
        Directory.CreateDirectory(voicesDirectory);
        await File.WriteAllBytesAsync(Path.Combine(voicesDirectory, "female_01.wav"), [0x52, 0x49, 0x46, 0x46]);
        var client = new CapturingComfyUiClient
        {
            VoiceCatalog = new ComfyVoiceCatalogResult(
            [
                new ComfyVoiceReference("TTS/voices/remote_only.wav", "remote reference"),
                new ComfyVoiceReference("voices_examples/female/female_02.wav", "example reference")
            ])
        };
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            client,
            new InMemoryInteractionProjectStore());
        var viewModel = new MainWindowViewModel(
            service,
            loadLastProject: false,
            comfyVoicesDirectoryResolver: () => voicesDirectory);

        await viewModel.TestComfyConnectionCommand.ExecuteAsync(null);

        Assert.DoesNotContain("female_01.wav", viewModel.VoiceReferenceOptions);
        Assert.DoesNotContain("voices_examples/female/female_02.wav", viewModel.VoiceReferenceOptions);
        Assert.Contains("TTS/voices/remote_only.wav", viewModel.VoiceReferenceOptions);
        Assert.Equal(MainWindowViewModel.CustomVoiceReferenceOption, viewModel.VoiceReferenceOptions.Last());
    }

    [Fact]
    public void RoleVoiceMasterRouteLocksDialogueAndAllowsThoughtAndActionToggles()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.UseRoleVoiceForAllCharacterLines);
        Assert.False(viewModel.ShowRoleVoiceDetailRouteButtons);
        Assert.False(viewModel.CanEditCharacterVoiceBindings);
        Assert.All(viewModel.CharacterBindingTargets, target =>
        {
            Assert.True(target.HasVoiceSummary);
            Assert.True(target.ShowVoiceBindingControls);
            Assert.False(target.CanEditVoiceBinding);
        });
        Assert.All(viewModel.NarrationBindingTargets, target => Assert.True(target.HasVoiceSummary));
        Assert.Equal("台词使用角色参考音", viewModel.RoleDialogueVoiceRouteText);
        Assert.Equal("场景/旁白使用默认配音", viewModel.RoleSceneNarrationVoiceRouteText);
        Assert.Equal("角色行使用默认配音", viewModel.RoleAllVoiceRouteText);

        viewModel.ToggleRoleAllVoiceRouteCommand.Execute(null);

        Assert.True(viewModel.UseRoleVoiceForAllCharacterLines);
        Assert.True(viewModel.ShowRoleVoiceDetailRouteButtons);
        Assert.True(viewModel.CanEditCharacterVoiceBindings);
        Assert.All(viewModel.CharacterBindingTargets, target => Assert.True(target.HasVoiceSummary));
        Assert.True(viewModel.UseRoleVoiceForDialogue);
        Assert.False(viewModel.UseRoleVoiceForThought);
        Assert.False(viewModel.UseRoleVoiceForAction);
        Assert.Equal("场景/旁白使用默认配音", viewModel.RoleSceneNarrationVoiceRouteText);
        Assert.Equal("角色行使用参考音频配音", viewModel.RoleAllVoiceRouteText);

        viewModel.ToggleRoleDialogueVoiceRouteCommand.Execute(null);

        Assert.True(viewModel.UseRoleVoiceForDialogue);

        viewModel.ToggleRoleThoughtVoiceRouteCommand.Execute(null);
        viewModel.ToggleRoleActionVoiceRouteCommand.Execute(null);

        Assert.True(viewModel.UseRoleVoiceForThought);
        Assert.True(viewModel.UseRoleVoiceForAction);

        viewModel.ToggleRoleAllVoiceRouteCommand.Execute(null);

        Assert.False(viewModel.UseRoleVoiceForAllCharacterLines);
        Assert.False(viewModel.UseRoleVoiceForDialogue);
        Assert.False(viewModel.UseRoleVoiceForThought);
        Assert.False(viewModel.UseRoleVoiceForAction);
        Assert.False(viewModel.ShowRoleVoiceDetailRouteButtons);
        Assert.False(viewModel.CanEditCharacterVoiceBindings);
        Assert.All(viewModel.CharacterBindingTargets, target =>
        {
            Assert.True(target.HasVoiceSummary);
            Assert.True(target.ShowVoiceBindingControls);
            Assert.False(target.CanEditVoiceBinding);
        });
    }

    [Fact]
    public void RoleVoiceRouteKeepsDefaultVoiceWhenCharacterReferenceAudioIsEmpty()
    {
        var viewModel = CreateViewModel();
        var target = viewModel.CharacterBindingTargets.First();
        var row = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(candidate => !candidate.IsNarration && string.Equals(candidate.SubjectName, target.DisplayName, StringComparison.Ordinal));
        row.SelectedVoiceReference = "default.wav";
        row.SelectedVoiceReferenceText = "Default reference";

        viewModel.ToggleRoleAllVoiceRouteCommand.Execute(null);

        var draft = viewModel.WorkbenchDocument.RowsById[row.InteractionId];
        Assert.Equal("default.wav", draft.VoiceBinding?.EffectiveVoiceReferenceId);
        Assert.Equal("Default reference", draft.VoiceBinding?.EffectiveReferenceText);
        Assert.Null(draft.VoiceRole);
        Assert.Null(draft.VoiceRoleCharacterId);
        Assert.Equal("未绑定声音", target.VoiceAsset);
        Assert.Equal("角色行使用参考音频配音", viewModel.RoleAllVoiceRouteText);
    }

    [Fact]
    public void RoleVoiceRouteDoesNotFlipMasterSwitchWhenNoRoutableRoleRowsExist()
    {
        var viewModel = CreateViewModel();
        foreach (var row in viewModel.SceneTables.SelectMany(scene => scene.Rows))
        {
            row.SubjectName = "旁白";
        }

        viewModel.ToggleRoleAllVoiceRouteCommand.Execute(null);

        Assert.False(viewModel.UseRoleVoiceForAllCharacterLines);
        Assert.False(viewModel.ShowRoleVoiceDetailRouteButtons);
        Assert.Equal("角色行使用默认配音", viewModel.RoleAllVoiceRouteText);
        Assert.Contains("没有可使用角色参考音频的角色行", viewModel.StatusText);
    }

    [Fact]
    public void ScenePropertyEditRefreshesOnlyItsBindingTarget()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var editedScene = viewModel.SceneTables[0];
        var untouchedScene = viewModel.SceneTables[1];
        var editedTarget = viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Scene && binding.TargetId == editedScene.SceneId);
        var untouchedTarget = viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Scene && binding.TargetId == untouchedScene.SceneId);
        var collectionChanges = 0;
        var editedNotifications = new List<string>();
        var untouchedNotifications = new List<string>();
        viewModel.BindingTargets.CollectionChanged += (_, _) => collectionChanges++;
        editedTarget.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                editedNotifications.Add(args.PropertyName);
            }
        };
        untouchedTarget.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                untouchedNotifications.Add(args.PropertyName);
            }
        };

        editedScene.SceneName = "局部刷新场景";
        editedScene.SceneEffect = "局部刷新描述";
        editedScene.SelectedSceneAsset = "asset_scene_local_refresh";

        Assert.Equal(0, collectionChanges);
        Assert.Equal(0, viewModel.WorkbenchBindingRefreshMetrics.FallbackFullRefreshCount);
        Assert.Same(editedTarget, viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Scene && binding.TargetId == editedScene.SceneId));
        Assert.Same(untouchedTarget, viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Scene && binding.TargetId == untouchedScene.SceneId));
        Assert.Equal("局部刷新场景", editedTarget.DisplayName);
        Assert.Equal("局部刷新描述", editedTarget.Detail);
        Assert.Equal("asset_scene_local_refresh", editedTarget.ImageAsset);
        Assert.Contains(nameof(BindingTargetViewModel.DisplayName), editedNotifications);
        Assert.Contains(nameof(BindingTargetViewModel.Detail), editedNotifications);
        Assert.Contains(nameof(BindingTargetViewModel.ImageAsset), editedNotifications);
        Assert.Empty(untouchedNotifications);
    }

    [Fact]
    public void BindingTargetRefreshInsertsAndRemovesSceneTargetsWithoutRebuildingRemainingTargets()
    {
        var viewModel = CreateViewModel();
        var originalScene = viewModel.SceneTables[0];
        var originalTarget = viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Scene && binding.TargetId == originalScene.SceneId);
        var characterTarget = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        var initialSceneTargetCount = viewModel.BindingTargets.Count(binding => binding.Kind == BindingTargetKind.Scene);
        var collectionNotifications = new List<NotifyCollectionChangedAction>();
        var characterNotifications = new List<string>();
        var initialMetrics = viewModel.WorkbenchBindingRefreshMetrics;
        viewModel.BindingTargets.CollectionChanged += (_, args) => collectionNotifications.Add(args.Action);
        characterTarget.PropertyChanged += (_, args) => AddNotification(characterNotifications, args.PropertyName);

        viewModel.AddSceneCommand.Execute(null);

        var addedScene = viewModel.SceneTables.Last();
        var addedTarget = viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Scene && binding.TargetId == addedScene.SceneId);
        Assert.Same(originalTarget, viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Scene && binding.TargetId == originalScene.SceneId));
        Assert.Equal(initialSceneTargetCount + 1, viewModel.BindingTargets.Count(binding => binding.Kind == BindingTargetKind.Scene));
        Assert.Contains(NotifyCollectionChangedAction.Add, collectionNotifications);
        Assert.Empty(characterNotifications);
        Assert.Equal(initialMetrics.FallbackFullRefreshCount, viewModel.WorkbenchBindingRefreshMetrics.FallbackFullRefreshCount);
        Assert.Equal(
            initialMetrics.FallbackFullRefreshReasons.CountFor("insert_scene_target"),
            viewModel.WorkbenchBindingRefreshMetrics.FallbackFullRefreshReasons.CountFor("insert_scene_target"));

        collectionNotifications.Clear();
        characterNotifications.Clear();

        viewModel.RemoveSceneCommand.Execute(addedScene);

        Assert.DoesNotContain(addedTarget, viewModel.BindingTargets);
        Assert.Same(originalTarget, viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Scene && binding.TargetId == originalScene.SceneId));
        Assert.Equal(initialSceneTargetCount, viewModel.BindingTargets.Count(binding => binding.Kind == BindingTargetKind.Scene));
        Assert.Contains(NotifyCollectionChangedAction.Remove, collectionNotifications);
        Assert.Empty(characterNotifications);
    }

    [Fact]
    public void MissingRemovedSceneTargetDoesNotFallbackToFullRefresh()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var addedScene = viewModel.SceneTables.Last();
        var addedTarget = viewModel.BindingTargets.Single(binding =>
            binding.Kind == BindingTargetKind.Scene &&
            binding.TargetId == addedScene.SceneId);
        viewModel.BindingTargets.Remove(addedTarget);
        var initialMetrics = viewModel.WorkbenchBindingRefreshMetrics;

        viewModel.RemoveSceneCommand.Execute(addedScene);

        Assert.Equal(initialMetrics.FallbackFullRefreshCount, viewModel.WorkbenchBindingRefreshMetrics.FallbackFullRefreshCount);
        Assert.Equal(
            initialMetrics.FallbackFullRefreshReasons.CountFor("remove_scene_target"),
            viewModel.WorkbenchBindingRefreshMetrics.FallbackFullRefreshReasons.CountFor("remove_scene_target"));
        Assert.DoesNotContain(viewModel.BindingTargets, binding =>
            binding.Kind == BindingTargetKind.Scene &&
            binding.TargetId == addedScene.SceneId);
    }

    [Fact]
    public void BindingTargetRefreshReordersSceneTargetsWithoutRebuildingExistingTargets()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var firstScene = viewModel.SceneTables[0];
        var secondScene = viewModel.SceneTables[1];
        var firstTarget = viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Scene && binding.TargetId == firstScene.SceneId);
        var secondTarget = viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Scene && binding.TargetId == secondScene.SceneId);
        var characterTarget = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        secondTarget.StatusText = "busy";
        var collectionNotifications = new List<NotifyCollectionChangedAction>();
        var characterNotifications = new List<string>();
        viewModel.BindingTargets.CollectionChanged += (_, args) => collectionNotifications.Add(args.Action);
        characterTarget.PropertyChanged += (_, args) => AddNotification(characterNotifications, args.PropertyName);

        viewModel.MoveScene(secondScene, firstScene);

        var sceneTargets = viewModel.BindingTargets
            .Where(binding => binding.Kind == BindingTargetKind.Scene)
            .ToArray();
        Assert.Equal(
            viewModel.SceneTables.Select(scene => scene.SceneId),
            sceneTargets.Select(target => target.TargetId));
        Assert.Same(firstTarget, viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Scene && binding.TargetId == firstScene.SceneId));
        Assert.Same(secondTarget, viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Scene && binding.TargetId == secondScene.SceneId));
        Assert.Same(characterTarget, viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character));
        Assert.Equal("busy", secondTarget.StatusText);
        Assert.All(collectionNotifications, action => Assert.Equal(NotifyCollectionChangedAction.Move, action));
        Assert.Empty(characterNotifications);
    }

    [Fact]
    public void BindingTargetSceneOrderSyncRepairsSceneTargetsWithoutFullRefreshFallback()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var firstScene = viewModel.SceneTables[0];
        var secondScene = viewModel.SceneTables[1];
        var firstTarget = viewModel.BindingTargets.Single(binding =>
            binding.Kind == BindingTargetKind.Scene &&
            binding.TargetId == firstScene.SceneId);
        var characterTarget = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        var staleScene = SceneTableViewModel.CreateManual(999);
        var staleTarget = BindingTargetViewModel.ForScene(staleScene);
        viewModel.BindingTargets.Remove(firstTarget);
        viewModel.BindingTargets.Insert(0, staleTarget);
        var initialMetrics = viewModel.WorkbenchBindingRefreshMetrics;
        var characterNotifications = new List<string>();
        characterTarget.PropertyChanged += (_, args) => AddNotification(characterNotifications, args.PropertyName);

        viewModel.MoveScene(secondScene, firstScene);

        Assert.Equal(initialMetrics.FallbackFullRefreshCount, viewModel.WorkbenchBindingRefreshMetrics.FallbackFullRefreshCount);
        Assert.Equal(
            initialMetrics.FallbackFullRefreshReasons.CountFor("sync_scene_target_order"),
            viewModel.WorkbenchBindingRefreshMetrics.FallbackFullRefreshReasons.CountFor("sync_scene_target_order"));
        Assert.DoesNotContain(staleTarget, viewModel.BindingTargets);
        Assert.Same(characterTarget, viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character));
        var sceneTargets = viewModel.BindingTargets
            .Where(binding => binding.Kind == BindingTargetKind.Scene)
            .ToArray();
        Assert.Equal(
            viewModel.SceneTables.Select(scene => scene.SceneId),
            sceneTargets.Select(target => target.TargetId));
        Assert.Empty(characterNotifications);
    }

    [Fact]
    public void BindingTargetRefreshCountsRowsMatchingCharacterIdAndNameOnce()
    {
        var viewModel = CreateViewModel();
        var scene = viewModel.SceneTables[0];
        var row = scene.Rows.First(row => !row.IsNarration);
        row.SubjectName = viewModel.Project.Characters[0].Name;
        row.SelectedCharacterAsset = "asset_character_chen";

        var target = viewModel.BindingTargets.Single(binding =>
            binding.Kind == BindingTargetKind.Character &&
            binding.TargetId == viewModel.Project.Characters[0].Id);

        var expectedCount = viewModel.SceneTables
            .SelectMany(table => table.Rows)
            .Count(candidate =>
                !candidate.IsNarration &&
                (string.Equals(candidate.SubjectCharacterId, viewModel.Project.Characters[0].Id, StringComparison.Ordinal) ||
                 string.Equals(candidate.SubjectName, viewModel.Project.Characters[0].Name, StringComparison.Ordinal)));
        Assert.Equal($"{expectedCount} 条台词", target.Detail);
        Assert.Equal("asset_character_chen", target.ImageAsset);
    }

    [Fact]
    public void BindingTargetRefreshUpdatesAlignedCharacterTargetWithoutCollectionChurn()
    {
        var viewModel = CreateViewModel();
        var scene = viewModel.SceneTables[0];
        viewModel.AddDialogueRowCommand.Execute(scene);
        var characters = viewModel.Project.Characters
            .Where(character => character.Name != "旁白")
            .Take(2)
            .ToArray();
        Assert.True(characters.Length >= 2);
        var sourceCharacter = characters[0];
        var character = characters[1];
        scene.Rows[^1].SubjectName = sourceCharacter.Name;
        var target = viewModel.BindingTargets.Single(binding =>
            binding.Kind == BindingTargetKind.Character &&
            binding.TargetId == character.Id);
        var previousCount = LeadingNumber(target.Detail);
        var collectionChanges = 0;
        viewModel.BindingTargets.CollectionChanged += (_, _) => collectionChanges++;

        scene.Rows[^1].SubjectName = character.Name;

        Assert.Equal(0, collectionChanges);
        Assert.Same(target, viewModel.BindingTargets.Single(binding =>
            binding.Kind == BindingTargetKind.Character &&
            binding.TargetId == character.Id));
        Assert.True(LeadingNumber(target.Detail) >= previousCount);
    }

    [Fact]
    public void RowBindingEditRefreshesOnlyItsCurrentBindingTarget()
    {
        var viewModel = CreateViewModel();
        var character = viewModel.Project.Characters.First(character => character.Name != "旁白");
        var row = viewModel.SceneTables.SelectMany(scene => scene.Rows)
            .First(row => !row.IsNarration && row.SubjectName == character.Name);
        var target = viewModel.BindingTargets.Single(binding =>
            binding.Kind == BindingTargetKind.Character &&
            binding.TargetId == character.Id);
        var unrelatedTarget = viewModel.BindingTargets.First(binding =>
            binding.Kind == BindingTargetKind.Scene);
        var collectionChanges = 0;
        var targetNotifications = new List<string>();
        var unrelatedNotifications = new List<string>();
        var initialMetrics = viewModel.WorkbenchBindingRefreshMetrics;
        viewModel.BindingTargets.CollectionChanged += (_, _) => collectionChanges++;
        target.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                targetNotifications.Add(args.PropertyName);
            }
        };
        unrelatedTarget.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                unrelatedNotifications.Add(args.PropertyName);
            }
        };

        row.SelectedCharacterAsset = "asset_character_local_refresh";
        row.SelectedVoiceReference = "local-refresh.wav";
        row.SelectedVoiceReferenceText = "local refresh voice";

        Assert.Equal(0, collectionChanges);
        Assert.Same(target, viewModel.BindingTargets.Single(binding =>
            binding.Kind == BindingTargetKind.Character &&
            binding.TargetId == character.Id));
        Assert.Equal("asset_character_local_refresh", target.ImageAsset);
        Assert.Equal("未绑定声音", target.VoiceAsset);
        Assert.Equal(string.Empty, target.VoiceReferenceText);
        Assert.Equal(initialMetrics.FallbackFullRefreshCount, viewModel.WorkbenchBindingRefreshMetrics.FallbackFullRefreshCount);
        Assert.Equal(
            initialMetrics.FallbackFullRefreshReasons.CountFor("single_target"),
            viewModel.WorkbenchBindingRefreshMetrics.FallbackFullRefreshReasons.CountFor("single_target"));
        Assert.Contains(nameof(BindingTargetViewModel.ImageAsset), targetNotifications);
        Assert.DoesNotContain(nameof(BindingTargetViewModel.VoiceAsset), targetNotifications);
        Assert.DoesNotContain(nameof(BindingTargetViewModel.VoiceReferenceText), targetNotifications);
        Assert.Empty(unrelatedNotifications);
    }

    [Fact]
    public async Task RenameBindingTargetUpdatesProjectRowsAndBindingCard()
    {
        var viewModel = CreateViewModel();
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        var characterId = target.TargetId;
        var originalName = target.DisplayName;
        var newName = $"{originalName}改";
        var typedName = $" {originalName} 改 ";
        var affectedRows = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .Where(row => !row.IsNarration &&
                          (string.Equals(row.SubjectCharacterId, characterId, StringComparison.Ordinal) ||
                           string.Equals(row.SubjectName, originalName, StringComparison.Ordinal)))
            .ToArray();
        Assert.NotEmpty(affectedRows);

        target.BeginDisplayNameEdit();
        target.EditableDisplayName = typedName;

        Assert.Equal(originalName, target.DisplayName);

        await viewModel.RenameBindingTargetCommand.ExecuteAsync(target);

        Assert.Contains(viewModel.Project.Characters, character =>
            string.Equals(character.Id, characterId, StringComparison.Ordinal) &&
            string.Equals(character.Name, newName, StringComparison.Ordinal));
        Assert.DoesNotContain(viewModel.Project.Characters, character =>
            string.Equals(character.Id, characterId, StringComparison.Ordinal) &&
            string.Equals(character.Name, originalName, StringComparison.Ordinal));
        Assert.All(affectedRows, row =>
        {
            Assert.Equal(newName, row.SubjectName);
            Assert.Equal(characterId, row.SubjectCharacterId);
        });
        Assert.Contains(newName, viewModel.CharacterNameOptions);
        Assert.DoesNotContain(originalName, viewModel.CharacterNameOptions);
        Assert.Contains(viewModel.BindingTargets, binding =>
            binding.Kind == BindingTargetKind.Character &&
            string.Equals(binding.TargetId, characterId, StringComparison.Ordinal) &&
            string.Equals(binding.DisplayName, newName, StringComparison.Ordinal));
        Assert.False(viewModel.BindingTargets.Single(binding =>
            binding.Kind == BindingTargetKind.Character &&
            string.Equals(binding.TargetId, characterId, StringComparison.Ordinal)).IsEditingDisplayName);
    }

    [Fact]
    public async Task RenameBindingTargetToExistingNamePromptsAndCancelsMerge()
    {
        var viewModel = CreateViewModel();
        var sourceTarget = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        var targetCharacter = viewModel.Project.Characters.First(character =>
            !string.Equals(character.Id, sourceTarget.TargetId, StringComparison.Ordinal));
        var promptCount = 0;
        viewModel.ConfirmRoleMergeAsync = confirmation =>
        {
            promptCount++;
            Assert.Equal(sourceTarget.DisplayName, confirmation.SourceRoleName);
            Assert.Equal(targetCharacter.Name, confirmation.TargetRoleName);
            Assert.True(confirmation.AffectedDialogueCount > 0);
            return Task.FromResult(false);
        };

        sourceTarget.BeginDisplayNameEdit();
        sourceTarget.EditableDisplayName = targetCharacter.Name;

        await viewModel.RenameBindingTargetCommand.ExecuteAsync(sourceTarget);

        Assert.Equal(1, promptCount);
        Assert.Contains(viewModel.Project.Characters, character =>
            string.Equals(character.Id, sourceTarget.TargetId, StringComparison.Ordinal));
        Assert.Contains(viewModel.Project.Characters, character =>
            string.Equals(character.Id, targetCharacter.Id, StringComparison.Ordinal));
        Assert.Contains(viewModel.BindingTargets, binding =>
            binding.Kind == BindingTargetKind.Character &&
            string.Equals(binding.TargetId, sourceTarget.TargetId, StringComparison.Ordinal));
        Assert.True(sourceTarget.IsEditingDisplayName);
        Assert.Contains("已取消合并", viewModel.StatusText);
    }

    [Fact]
    public async Task RenameBindingTargetToExistingNameMergesRowsAndRemovesSourceRole()
    {
        var viewModel = CreateViewModel();
        var sourceTarget = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        var sourceCharacter = viewModel.Project.Characters.Single(character =>
            string.Equals(character.Id, sourceTarget.TargetId, StringComparison.Ordinal));
        var targetCharacter = viewModel.Project.Characters.First(character =>
            !string.Equals(character.Id, sourceTarget.TargetId, StringComparison.Ordinal));
        var sourceRows = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .Where(row => !row.IsNarration &&
                          (string.Equals(row.SubjectCharacterId, sourceCharacter.Id, StringComparison.Ordinal) ||
                           string.Equals(row.SubjectName, sourceCharacter.Name, StringComparison.Ordinal)))
            .ToArray();
        Assert.NotEmpty(sourceRows);
        viewModel.ConfirmRoleMergeAsync = _ => Task.FromResult(true);

        sourceTarget.BeginDisplayNameEdit();
        sourceTarget.EditableDisplayName = $" {targetCharacter.Name} ";

        await viewModel.RenameBindingTargetCommand.ExecuteAsync(sourceTarget);

        Assert.DoesNotContain(viewModel.Project.Characters, character =>
            string.Equals(character.Id, sourceCharacter.Id, StringComparison.Ordinal));
        Assert.Contains(viewModel.Project.Characters, character =>
            string.Equals(character.Id, targetCharacter.Id, StringComparison.Ordinal));
        Assert.DoesNotContain(sourceCharacter.Name, viewModel.CharacterNameOptions);
        Assert.Contains(targetCharacter.Name, viewModel.CharacterNameOptions);
        Assert.DoesNotContain(viewModel.BindingTargets, binding =>
            binding.Kind == BindingTargetKind.Character &&
            string.Equals(binding.TargetId, sourceCharacter.Id, StringComparison.Ordinal));
        var selectedTarget = Assert.Single(viewModel.BindingTargets, binding =>
            binding.Kind == BindingTargetKind.Character &&
            string.Equals(binding.TargetId, targetCharacter.Id, StringComparison.Ordinal));
        Assert.Same(selectedTarget, viewModel.SelectedBindingTarget);
        Assert.All(sourceRows, row =>
        {
            Assert.Equal(targetCharacter.Name, row.SubjectName);
            Assert.Equal(targetCharacter.Id, row.SubjectCharacterId);
        });
        Assert.DoesNotContain(viewModel.WorkbenchDocument.RowsById.Values, row =>
            string.Equals(row.SubjectCharacterId, sourceCharacter.Id, StringComparison.Ordinal) ||
            string.Equals(row.SubjectName, sourceCharacter.Name, StringComparison.Ordinal));
        Assert.DoesNotContain(viewModel.Project.Scenes.SelectMany(scene => scene.Interactions), interaction =>
            string.Equals(interaction.SubjectCharacterId, sourceCharacter.Id, StringComparison.Ordinal) ||
            string.Equals(interaction.Dialogue?.SpeakerCharacterId, sourceCharacter.Id, StringComparison.Ordinal) ||
            string.Equals(interaction.InnerMonologue?.CharacterId, sourceCharacter.Id, StringComparison.Ordinal));
        Assert.Contains("角色已合并", viewModel.StatusText);
    }

    [Fact]
    public void AddManagedCharacterCreatesDefaultNamedRoleAndBindingCard()
    {
        var viewModel = CreateViewModel();
        var existingCharacterIds = viewModel.Project.Characters
            .Select(character => character.Id)
            .ToHashSet(StringComparer.Ordinal);
        var initialCharacterCount = viewModel.Project.Characters.Count;
        var initialCharacterTargetCount = viewModel.BindingTargets.Count(binding => binding.Kind == BindingTargetKind.Character);

        viewModel.AddManagedCharacterCommand.Execute(null);

        var addedCharacter = Assert.Single(
            viewModel.Project.Characters,
            character => !existingCharacterIds.Contains(character.Id));
        Assert.Equal(initialCharacterCount + 1, viewModel.Project.Characters.Count);
        Assert.StartsWith("新角色 ", addedCharacter.Name, StringComparison.Ordinal);
        Assert.Contains(addedCharacter.Name, viewModel.CharacterNameOptions);
        Assert.Equal(
            initialCharacterTargetCount + 1,
            viewModel.BindingTargets.Count(binding => binding.Kind == BindingTargetKind.Character));
        var addedTarget = Assert.Single(viewModel.BindingTargets, binding =>
            binding.Kind == BindingTargetKind.Character &&
            string.Equals(binding.TargetId, addedCharacter.Id, StringComparison.Ordinal) &&
            string.Equals(binding.DisplayName, addedCharacter.Name, StringComparison.Ordinal));
        Assert.Same(addedTarget, viewModel.SelectedBindingTarget);
        Assert.Same(addedTarget, viewModel.SelectedCharacterBindingTarget);
        Assert.True(addedTarget.IsEditingDisplayName);
        Assert.Equal(addedCharacter.Name, addedTarget.EditableDisplayName);
        Assert.Contains($"已添加角色：{addedCharacter.Name}", viewModel.StatusText);
    }

    [Fact]
    public async Task RemovingAllRowsForImportedRoleRemovesRoleCardAndPrunesSavedProjectCharacters()
    {
        var viewModel = CreateViewModel();
        var staleTarget = viewModel.CharacterBindingTargets.First();
        var staleCharacter = viewModel.Project.Characters.Single(character =>
            string.Equals(character.Id, staleTarget.TargetId, StringComparison.Ordinal));

        viewModel.AddManagedCharacterCommand.Execute(null);
        var manualCharacter = viewModel.Project.Characters.Single(character =>
            character.Id.StartsWith("char_manual_", StringComparison.Ordinal));
        var staleRows = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .Where(row =>
                row.UsesCharacterVisualPreview &&
                (string.Equals(row.SubjectCharacterId, staleCharacter.Id, StringComparison.Ordinal) ||
                 string.Equals(row.SourceRoleCharacterId, staleCharacter.Id, StringComparison.Ordinal) ||
                 string.Equals(row.SubjectName, staleCharacter.Name, StringComparison.Ordinal) ||
                 string.Equals(row.SourceRole, staleCharacter.Name, StringComparison.Ordinal)))
            .ToArray();
        Assert.NotEmpty(staleRows);

        foreach (var row in staleRows)
        {
            viewModel.RemoveInteractionRowCommand.Execute(row);
        }

        Assert.DoesNotContain(viewModel.CharacterBindingTargets, target =>
            string.Equals(target.TargetId, staleCharacter.Id, StringComparison.Ordinal));
        Assert.DoesNotContain(staleCharacter.Name, viewModel.CharacterNameOptions);
        Assert.Contains(viewModel.CharacterBindingTargets, target =>
            string.Equals(target.TargetId, manualCharacter.Id, StringComparison.Ordinal));
        Assert.Contains(manualCharacter.Name, viewModel.CharacterNameOptions);

        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);

        Assert.DoesNotContain(viewModel.Project.Characters, character =>
            string.Equals(character.Id, staleCharacter.Id, StringComparison.Ordinal));
        Assert.Contains(viewModel.Project.Characters, character =>
            string.Equals(character.Id, manualCharacter.Id, StringComparison.Ordinal));
    }

    [Fact]
    public void RoleManagementCommandsAreLockedWhileTtsSynthesisRuns()
    {
        var viewModel = CreateViewModel();
        var initialCharacterCount = viewModel.Project.Characters.Count;

        viewModel.IsTtsSynthesisRunning = true;
        viewModel.AddManagedCharacterCommand.Execute(null);
        viewModel.ToggleRoleAllVoiceRouteCommand.Execute(null);
        viewModel.ToggleRoleThoughtVoiceRouteCommand.Execute(null);
        viewModel.ToggleRoleActionVoiceRouteCommand.Execute(null);

        Assert.Equal(initialCharacterCount, viewModel.Project.Characters.Count);
        Assert.False(viewModel.UseRoleVoiceForAllCharacterLines);
        Assert.False(viewModel.UseRoleVoiceForThought);
        Assert.False(viewModel.UseRoleVoiceForAction);
        Assert.Contains("锁定角色管理", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void RoleBindingControlsAreDisabledWhileTtsSynthesisRunsAndRestoreAfterStop()
    {
        var viewModel = CreateViewModel();

        viewModel.ToggleRoleAllVoiceRouteCommand.Execute(null);
        var characterTargets = viewModel.CharacterBindingTargets.ToArray();
        var narrationTargets = viewModel.NarrationBindingTargets.ToArray();
        Assert.NotEmpty(characterTargets);
        Assert.NotEmpty(narrationTargets);
        Assert.All(characterTargets, target => Assert.True(target.CanEditVoiceBinding));
        Assert.All(narrationTargets, target => Assert.True(target.CanEditVoiceBinding));

        viewModel.IsTtsSynthesisRunning = true;

        Assert.False(viewModel.CanEditWorkbenchStructure);
        Assert.All(characterTargets, target => Assert.False(target.CanEditVoiceBinding));
        Assert.All(narrationTargets, target => Assert.False(target.CanEditVoiceBinding));
        Assert.All(characterTargets, target => Assert.True(target.CanPreviewVoiceBinding));
        Assert.All(narrationTargets, target => Assert.True(target.CanPreviewVoiceBinding));
        Assert.False(viewModel.CanConfigureSelectedCharacterPsdLayerMatch);

        viewModel.IsTtsSynthesisRunning = false;

        Assert.True(viewModel.CanEditWorkbenchStructure);
        Assert.All(characterTargets, target => Assert.True(target.CanEditVoiceBinding));
        Assert.All(narrationTargets, target => Assert.True(target.CanEditVoiceBinding));
    }

    [Fact]
    public void EquivalentRowBindingPlaceholderEditDoesNotRefreshBindingTargetsOrUndo()
    {
        var previousMetrics = BindingTargetRowIndex.Metrics;
        var metrics = new BindingTargetRowIndexMetrics();
        BindingTargetRowIndex.Metrics = metrics;
        try
        {
            var viewModel = CreateViewModel();
            var row = viewModel.SceneTables.SelectMany(scene => scene.Rows)
                .First(row => !row.IsNarration && row.SelectedCharacterAsset == "未绑定");
            var initialBuildCount = metrics.BuildCount;
            var initialRefreshMetrics = viewModel.WorkbenchBindingRefreshMetrics;
            var target = viewModel.BindingTargets.Single(binding =>
                binding.Kind == BindingTargetKind.Character &&
                string.Equals(binding.DisplayName, row.SubjectName, StringComparison.Ordinal));
            var targetNotifications = 0;
            target.PropertyChanged += (_, _) => targetNotifications++;

            row.SelectedCharacterAsset = "未绑定图片";

            Assert.False(viewModel.IsWorkbenchDirty);
            Assert.False(viewModel.CanUndoWorkbench);
            Assert.Equal(initialBuildCount, metrics.BuildCount);
            Assert.Equal(initialRefreshMetrics, viewModel.WorkbenchBindingRefreshMetrics);
            Assert.Equal(0, targetNotifications);
            Assert.Equal("未绑定", row.SelectedCharacterAsset);
            Assert.Null(viewModel.WorkbenchDocument.RowsById[row.InteractionId].CharacterVisualBinding?.EffectiveAssetId);
        }
        finally
        {
            BindingTargetRowIndex.Metrics = previousMetrics;
        }
    }

    [Fact]
    public void TemporaryCharacterRowBindingEditDoesNotBuildRowIndexOrFallbackRefresh()
    {
        var previousMetrics = BindingTargetRowIndex.Metrics;
        var metrics = new BindingTargetRowIndexMetrics();
        BindingTargetRowIndex.Metrics = metrics;
        try
        {
            var viewModel = CreateViewModel();
            var scene = viewModel.SceneTables[0];
            viewModel.AddDialogueRowCommand.Execute(scene);
            var row = scene.Rows[^1];
            row.SubjectName = "临时人物";
            var initialBuildCount = metrics.BuildCount;
            var initialRefreshMetrics = viewModel.WorkbenchBindingRefreshMetrics;
            var collectionChanges = 0;
            viewModel.BindingTargets.CollectionChanged += (_, _) => collectionChanges++;

            row.SelectedCharacterAsset = "asset_temporary_character";

            Assert.Equal("asset_temporary_character", viewModel.WorkbenchDocument.RowsById[row.InteractionId].CharacterVisualBinding?.EffectiveAssetId);
            Assert.Equal(initialRefreshMetrics, viewModel.WorkbenchBindingRefreshMetrics);
            Assert.Equal(0, collectionChanges);
        }
        finally
        {
            BindingTargetRowIndex.Metrics = previousMetrics;
        }
    }

    [Fact]
    public void MissingExpectedCharacterTargetIsRecreatedWithoutFullRefreshFallback()
    {
        var previousMetrics = BindingTargetRowIndex.Metrics;
        var rowIndexMetrics = new BindingTargetRowIndexMetrics();
        BindingTargetRowIndex.Metrics = rowIndexMetrics;
        try
        {
            var viewModel = CreateViewModel();
            var character = viewModel.Project.Characters.First(character => character.Name != "旁白");
            var row = viewModel.SceneTables.SelectMany(scene => scene.Rows)
                .First(row => !row.IsNarration && row.SubjectName == character.Name);
            var target = viewModel.BindingTargets.Single(binding =>
                binding.Kind == BindingTargetKind.Character &&
                binding.TargetId == character.Id);
            viewModel.BindingTargets.Remove(target);
            var initialMetrics = viewModel.WorkbenchBindingRefreshMetrics;
            var initialBuildCount = rowIndexMetrics.BuildCount;

            row.SelectedCharacterAsset = "asset_missing_target_fallback";

            Assert.Equal(initialMetrics.LastFullRefreshReason, viewModel.WorkbenchBindingRefreshMetrics.LastFullRefreshReason);
            Assert.Equal(initialMetrics.FallbackFullRefreshCount, viewModel.WorkbenchBindingRefreshMetrics.FallbackFullRefreshCount);
            Assert.Equal(
                initialMetrics.FallbackFullRefreshReasons.CountFor("row_target"),
                viewModel.WorkbenchBindingRefreshMetrics.FallbackFullRefreshReasons.CountFor("row_target"));
            Assert.Equal(initialBuildCount + 1, rowIndexMetrics.BuildCount);
            var recreatedTarget = viewModel.BindingTargets.Single(binding =>
                binding.Kind == BindingTargetKind.Character &&
                binding.TargetId == character.Id);
            Assert.Equal("asset_missing_target_fallback", recreatedTarget.ImageAsset);
        }
        finally
        {
            BindingTargetRowIndex.Metrics = previousMetrics;
        }
    }

    [Fact]
    public void EquivalentSceneBindingPlaceholderEditDoesNotRefreshSceneTargetOrUndo()
    {
        var viewModel = CreateViewModel();
        var scene = viewModel.SceneTables[0];
        var initialRefreshMetrics = viewModel.WorkbenchBindingRefreshMetrics;
        var target = viewModel.BindingTargets.Single(binding =>
            binding.Kind == BindingTargetKind.Scene &&
            binding.TargetId == scene.SceneId);
        var targetNotifications = 0;
        target.PropertyChanged += (_, _) => targetNotifications++;

        scene.SelectedSceneAsset = "未绑定图片";

        Assert.False(viewModel.IsWorkbenchDirty);
        Assert.False(viewModel.CanUndoWorkbench);
        Assert.Equal(initialRefreshMetrics, viewModel.WorkbenchBindingRefreshMetrics);
        Assert.Equal(0, targetNotifications);
        Assert.Equal("未绑定", scene.SelectedSceneAsset);
        Assert.Null(viewModel.WorkbenchDocument.Scenes[0].VisualBinding?.EffectiveAssetId);
    }

    [Fact]
    public void RowSubjectChangeRefreshesPreviousAndCurrentCharacterTargetsOnly()
    {
        var viewModel = CreateViewModel();
        var characters = viewModel.Project.Characters
            .Where(character => character.Name != "旁白")
            .Take(2)
            .ToArray();
        Assert.True(characters.Length >= 2);
        var row = viewModel.SceneTables.SelectMany(scene => scene.Rows)
            .First(row => !row.IsNarration && row.SubjectName == characters[0].Name);
        var previousTarget = viewModel.BindingTargets.Single(binding =>
            binding.Kind == BindingTargetKind.Character &&
            binding.TargetId == characters[0].Id);
        var currentTarget = viewModel.BindingTargets.Single(binding =>
            binding.Kind == BindingTargetKind.Character &&
            binding.TargetId == characters[1].Id);
        var sceneTarget = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Scene);
        var collectionChanges = 0;
        var previousNotifications = new List<string>();
        var currentNotifications = new List<string>();
        var sceneNotifications = new List<string>();
        var previousCount = LeadingNumber(previousTarget.Detail);
        var currentCount = LeadingNumber(currentTarget.Detail);
        viewModel.BindingTargets.CollectionChanged += (_, _) => collectionChanges++;
        previousTarget.PropertyChanged += (_, args) => AddNotification(previousNotifications, args.PropertyName);
        currentTarget.PropertyChanged += (_, args) => AddNotification(currentNotifications, args.PropertyName);
        sceneTarget.PropertyChanged += (_, args) => AddNotification(sceneNotifications, args.PropertyName);

        row.SubjectName = characters[1].Name;

        Assert.Equal(0, collectionChanges);
        Assert.Equal(previousCount - 1, LeadingNumber(previousTarget.Detail));
        Assert.Equal(currentCount + 1, LeadingNumber(currentTarget.Detail));
        Assert.Contains(nameof(BindingTargetViewModel.Detail), previousNotifications);
        Assert.Contains(nameof(BindingTargetViewModel.Detail), currentNotifications);
        Assert.Empty(sceneNotifications);
    }

    [Fact]
    public void RowSubjectChangeResolvesCurrentSubjectCharacterIdFromProjectCharacters()
    {
        var viewModel = CreateViewModel();
        var characters = viewModel.Project.Characters
            .Where(character => character.Name != "旁白")
            .Take(2)
            .ToArray();
        Assert.True(characters.Length >= 2);
        var row = viewModel.SceneTables.SelectMany(scene => scene.Rows)
            .First(row => !row.IsNarration && row.SubjectName == characters[0].Name);

        row.SubjectName = characters[1].Name;

        var draft = viewModel.WorkbenchDocument.RowsById[row.InteractionId];
        Assert.Equal(characters[1].Name, draft.SubjectName);
        Assert.Equal(characters[1].Id, draft.SubjectCharacterId);
        Assert.Equal(characters[1].Id, row.SubjectCharacterId);
    }

    [Fact]
    public void RowSubjectChangeAppliesNewCharacterDefaultVisualPreview()
    {
        var firstImage = CreateTempPng();
        var secondImage = CreateTempPng();
        var viewModel = CreateViewModel();
        var characters = viewModel.Project.Characters
            .Where(character => character.Name != "旁白")
            .Take(2)
            .ToArray();
        Assert.True(characters.Length >= 2);
        viewModel.Project = viewModel.Project with
        {
            Characters =
            [
                characters[0] with
                {
                    AssetBinding = characters[0].AssetBinding with
                    {
                        Portrait = firstImage,
                        PortraitVisualBinding = VisualAssetBinding.FromImage(firstImage)
                    }
                },
                characters[1] with
                {
                    AssetBinding = characters[1].AssetBinding with
                    {
                        Portrait = secondImage,
                        PortraitVisualBinding = VisualAssetBinding.FromImage(secondImage)
                    }
                }
            ]
        };
        var row = viewModel.SceneTables.SelectMany(scene => scene.Rows)
            .First(row => !row.IsNarration && row.SubjectName == characters[0].Name);
        viewModel.SelectRowCommand.Execute(row);

        row.SubjectName = characters[1].Name;

        var draft = viewModel.WorkbenchDocument.RowsById[row.InteractionId];
        Assert.Equal(characters[1].Id, row.SubjectCharacterId);
        Assert.Equal(secondImage, row.SelectedCharacterAsset);
        Assert.Equal(secondImage, row.SelectedCharacterPreviewAsset);
        Assert.Equal(secondImage, row.InlineObjectPreviewSource);
        Assert.True(row.HasInlineObjectPreviewImage);
        Assert.Equal(VisualSourceKind.Image, draft.CharacterVisualBinding?.SourceKind);
        Assert.Equal(secondImage, draft.CharacterVisualBinding?.RenderedAssetId);
    }

    [Fact]
    public void RowSubjectChangeFromCharacterToEffectsClearsPreviousCharacterVisualPreview()
    {
        var image = CreateTempPng();
        var viewModel = CreateViewModel();
        var character = viewModel.Project.Characters.First(character => character.Name != "旁白");
        viewModel.Project = viewModel.Project with
        {
            Characters =
            [
                character with
                {
                    AssetBinding = character.AssetBinding with
                    {
                        Portrait = image,
                        PortraitVisualBinding = VisualAssetBinding.FromImage(image)
                    }
                }
            ]
        };
        var row = viewModel.SceneTables.SelectMany(scene => scene.Rows)
            .First(row => !row.IsNarration && row.SubjectName == character.Name);
        viewModel.SelectRowCommand.Execute(row);
        row.SubjectName = character.Name;
        Assert.Equal(image, row.InlineObjectPreviewSource);

        row.SubjectName = AnalysisResultRowViewModel.EnvironmentEffectsSubjectName;

        var draft = viewModel.WorkbenchDocument.RowsById[row.InteractionId];
        Assert.True(row.IsNarration);
        Assert.Equal("effects", row.SourceItemType);
        Assert.Equal(AnalysisResultRowViewModel.EnvironmentEffectsSubjectName, row.SubjectName);
        Assert.Equal(VisualSourceKind.None, draft.CharacterVisualBinding?.SourceKind);
        Assert.Equal("未绑定行图", row.SelectedCharacterAsset);
        Assert.Equal(string.Empty, row.SelectedCharacterPreviewAsset);
        Assert.Equal(string.Empty, row.InlineObjectPreviewSource);
        Assert.False(row.HasInlineObjectPreviewImage);
        Assert.Equal("场", row.ObjectPreviewInitial);
    }

    [Fact]
    public async Task RowSubjectChangeFromEffectsBackToCharacterDiscardsEffectsRowImage()
    {
        var characterImage = CreateTempPng();
        var effectsImage = CreateTempPng();
        var viewModel = CreateViewModel();
        var character = viewModel.Project.Characters.First(character => character.Name != "旁白");
        viewModel.Project = viewModel.Project with
        {
            Characters =
            [
                character with
                {
                    AssetBinding = character.AssetBinding with
                    {
                        Portrait = characterImage,
                        PortraitVisualBinding = VisualAssetBinding.FromImage(characterImage)
                    }
                }
            ]
        };
        var row = viewModel.SceneTables.SelectMany(scene => scene.Rows)
            .First(row => !row.IsNarration && row.SubjectName == character.Name);
        viewModel.SelectRowCommand.Execute(row);
        row.SubjectName = AnalysisResultRowViewModel.EnvironmentEffectsSubjectName;
        await viewModel.SetWorkbenchRowImageBindingAsync(row, effectsImage);
        Assert.Equal(effectsImage, row.InlineObjectPreviewSource);

        row.SubjectName = character.Name;

        var draft = viewModel.WorkbenchDocument.RowsById[row.InteractionId];
        Assert.False(row.IsNarration);
        Assert.Equal("dialogue", row.SourceItemType);
        Assert.Equal(character.Id, row.SubjectCharacterId);
        Assert.Equal(characterImage, row.SelectedCharacterAsset);
        Assert.Equal(characterImage, row.InlineObjectPreviewSource);
        Assert.Equal(VisualSourceKind.Image, draft.CharacterVisualBinding?.SourceKind);
        Assert.Equal(characterImage, draft.CharacterVisualBinding?.RenderedAssetId);
    }

    [Fact]
    public void AddingDialogueRowAppliesDefaultCharacterVisualPreviewImmediately()
    {
        var image = CreateTempPng();
        var viewModel = CreateViewModel();
        var character = viewModel.Project.Characters.First(character => character.Name != "旁白");
        viewModel.Project = viewModel.Project with
        {
            Characters =
            [
                character with
                {
                    AssetBinding = character.AssetBinding with
                    {
                        Portrait = image,
                        PortraitVisualBinding = VisualAssetBinding.FromImage(image)
                    }
                }
            ]
        };
        var scene = viewModel.SceneTables[0];

        viewModel.AddDialogueRowCommand.Execute(scene);

        var row = Assert.IsType<AnalysisResultRowViewModel>(viewModel.SelectedRow);
        var draft = viewModel.WorkbenchDocument.RowsById[row.InteractionId];
        Assert.Equal(character.Name, row.SubjectName);
        Assert.Equal(character.Id, row.SubjectCharacterId);
        Assert.Equal(image, row.SelectedCharacterAsset);
        Assert.Equal(image, row.SelectedCharacterPreviewAsset);
        Assert.Equal(image, row.InlineObjectPreviewSource);
        Assert.Equal(image, row.ObjectPreviewSource);
        Assert.True(row.HasInlineObjectPreviewImage);
        Assert.True(row.HasObjectPreviewImage);
        Assert.Equal(VisualSourceKind.Image, draft.CharacterVisualBinding?.SourceKind);
        Assert.Equal(image, draft.CharacterVisualBinding?.RenderedAssetId);
    }

    [Fact]
    public void TemporaryRowSubjectChangeDoesNotAssignProjectCharacterId()
    {
        var viewModel = CreateViewModel();
        var character = viewModel.Project.Characters.First(character => character.Name != "旁白");
        var row = viewModel.SceneTables.SelectMany(scene => scene.Rows)
            .First(row => !row.IsNarration && row.SubjectName == character.Name);

        row.SubjectName = "临时人物";

        var draft = viewModel.WorkbenchDocument.RowsById[row.InteractionId];
        Assert.Equal("临时人物", draft.SubjectName);
        Assert.Null(draft.SubjectCharacterId);
        Assert.Null(row.SubjectCharacterId);
    }

    [Fact]
    public void ChangingSpecialNarrationSubjectBackToCharacterMakesRowDialogueAgain()
    {
        var viewModel = CreateViewModel();
        var scene = viewModel.SceneTables[0];
        viewModel.AddNarrationRowCommand.Execute(scene);
        var row = scene.Rows.First(candidate => candidate.IsNarration);
        row.SubjectName = AnalysisResultRowViewModel.NarrationStorySubjectName;
        var character = viewModel.Project.Characters.First(character => character.Name != "旁白");

        row.SubjectName = character.Name;

        var draft = viewModel.WorkbenchDocument.RowsById[row.InteractionId];
        Assert.False(row.IsNarration);
        Assert.Equal(WorkbenchRowKind.Dialogue, draft.Kind);
        Assert.Equal("dialogue", draft.SourceItemType);
        Assert.Equal(character.Name, draft.SourceRole);
        Assert.Equal(character.Id, draft.SourceRoleCharacterId);
        Assert.Equal("词", row.SourceTypeGlyph);
    }

    [Fact]
    public void ToggleSelectedRowSourceTypeCyclesOnlyCharacterLineKinds()
    {
        var viewModel = CreateViewModel();
        var scene = viewModel.SceneTables[0];
        var row = scene.Rows.First(candidate => !candidate.IsNarration);

        viewModel.ToggleSelectedRowSourceTypeCommand.Execute(row);
        var draft = viewModel.WorkbenchDocument.RowsById[row.InteractionId];
        Assert.False(row.IsNarration);
        Assert.Equal("dialogue", row.SourceItemType);
        Assert.Equal("词", row.SourceTypeGlyph);
        Assert.Equal(WorkbenchRowKind.Dialogue, draft.Kind);

        viewModel.ToggleSelectedRowSourceTypeCommand.Execute(row);
        Assert.Equal("thought", row.SourceItemType);
        Assert.Equal("念", row.SourceTypeGlyph);

        viewModel.ToggleSelectedRowSourceTypeCommand.Execute(row);
        Assert.Equal("action", row.SourceItemType);
        Assert.Equal("动", row.SourceTypeGlyph);

        viewModel.ToggleSelectedRowSourceTypeCommand.Execute(row);
        Assert.Equal("dialogue", row.SourceItemType);
        Assert.Equal("词", row.SourceTypeGlyph);
    }

    [Fact]
    public void ToggleSelectedRowSourceTypeIgnoresNarrationAndEffectsRows()
    {
        var viewModel = CreateViewModel();
        var scene = viewModel.SceneTables[0];
        viewModel.AddNarrationRowCommand.Execute(scene);
        var row = scene.Rows.First(candidate => candidate.IsNarration);
        var initialSourceType = row.SourceItemType;
        var initialSubjectName = row.SubjectName;

        viewModel.ToggleSelectedRowSourceTypeCommand.Execute(row);
        var draft = viewModel.WorkbenchDocument.RowsById[row.InteractionId];

        Assert.True(row.IsNarration);
        Assert.Equal(initialSourceType, row.SourceItemType);
        Assert.Equal("旁", row.SourceTypeGlyph);
        Assert.Equal(initialSubjectName, row.SubjectName);
        Assert.Equal(WorkbenchRowKind.Narration, draft.Kind);
        Assert.Equal(initialSourceType, draft.SourceItemType);
    }

    [Fact]
    public void RowSubjectChangeRecreatesMissingPreviousCharacterTargetWithoutFullRefreshFallback()
    {
        var viewModel = CreateViewModel();
        var characters = viewModel.Project.Characters
            .Where(character => character.Name != "旁白")
            .Take(2)
            .ToArray();
        Assert.True(characters.Length >= 2);
        var row = viewModel.SceneTables.SelectMany(scene => scene.Rows)
            .First(row => !row.IsNarration && row.SubjectName == characters[0].Name);
        var previousTarget = viewModel.BindingTargets.Single(binding =>
            binding.Kind == BindingTargetKind.Character &&
            binding.TargetId == characters[0].Id);
        var currentTarget = viewModel.BindingTargets.Single(binding =>
            binding.Kind == BindingTargetKind.Character &&
            binding.TargetId == characters[1].Id);
        var previousCount = LeadingNumber(previousTarget.Detail);
        var currentCount = LeadingNumber(currentTarget.Detail);
        viewModel.BindingTargets.Remove(previousTarget);
        var initialMetrics = viewModel.WorkbenchBindingRefreshMetrics;
        var collectionChanges = 0;
        viewModel.BindingTargets.CollectionChanged += (_, _) => collectionChanges++;

        row.SubjectName = characters[1].Name;

        Assert.Equal(initialMetrics.FallbackFullRefreshCount, viewModel.WorkbenchBindingRefreshMetrics.FallbackFullRefreshCount);
        Assert.Equal(
            initialMetrics.FallbackFullRefreshReasons.CountFor("row_subject_change"),
            viewModel.WorkbenchBindingRefreshMetrics.FallbackFullRefreshReasons.CountFor("row_subject_change"));
        Assert.Equal(1, collectionChanges);
        var recreatedPreviousTarget = viewModel.BindingTargets.Single(binding =>
            binding.Kind == BindingTargetKind.Character &&
            binding.TargetId == characters[0].Id);
        Assert.Equal(previousCount - 1, LeadingNumber(recreatedPreviousTarget.Detail));
        Assert.Equal(currentCount + 1, LeadingNumber(currentTarget.Detail));
    }

    [Fact]
    public void RowSubjectChangeRecreatesMissingCurrentCharacterTargetWithoutFullRefreshFallback()
    {
        var viewModel = CreateViewModel();
        var characters = viewModel.Project.Characters
            .Where(character => character.Name != "旁白")
            .Take(2)
            .ToArray();
        Assert.True(characters.Length >= 2);
        var row = viewModel.SceneTables.SelectMany(scene => scene.Rows)
            .First(row => !row.IsNarration && row.SubjectName == characters[0].Name);
        var previousTarget = viewModel.BindingTargets.Single(binding =>
            binding.Kind == BindingTargetKind.Character &&
            binding.TargetId == characters[0].Id);
        var currentTarget = viewModel.BindingTargets.Single(binding =>
            binding.Kind == BindingTargetKind.Character &&
            binding.TargetId == characters[1].Id);
        var previousCount = LeadingNumber(previousTarget.Detail);
        var currentCount = LeadingNumber(currentTarget.Detail);
        viewModel.BindingTargets.Remove(currentTarget);
        var initialMetrics = viewModel.WorkbenchBindingRefreshMetrics;
        var collectionChanges = 0;
        viewModel.BindingTargets.CollectionChanged += (_, _) => collectionChanges++;

        row.SubjectName = characters[1].Name;

        Assert.Equal(initialMetrics.FallbackFullRefreshCount, viewModel.WorkbenchBindingRefreshMetrics.FallbackFullRefreshCount);
        Assert.Equal(
            initialMetrics.FallbackFullRefreshReasons.CountFor("row_subject_change"),
            viewModel.WorkbenchBindingRefreshMetrics.FallbackFullRefreshReasons.CountFor("row_subject_change"));
        Assert.Equal(1, collectionChanges);
        Assert.Equal(previousCount - 1, LeadingNumber(previousTarget.Detail));
        var recreatedCurrentTarget = viewModel.BindingTargets.Single(binding =>
            binding.Kind == BindingTargetKind.Character &&
            binding.TargetId == characters[1].Id);
        Assert.Equal(currentCount + 1, LeadingNumber(recreatedCurrentTarget.Detail));
    }

    [Fact]
    public void RowSubjectChangeToNarrationRefreshesPreviousCharacterAndNarrationTargetsOnly()
    {
        var viewModel = CreateViewModel();
        var character = viewModel.Project.Characters.First(character => character.Name != "旁白");
        var row = viewModel.SceneTables.SelectMany(scene => scene.Rows)
            .First(row => !row.IsNarration && row.SubjectName == character.Name);
        var characterTarget = viewModel.BindingTargets.Single(binding =>
            binding.Kind == BindingTargetKind.Character &&
            binding.TargetId == character.Id);
        var narrationTarget = viewModel.BindingTargets.Single(binding =>
            binding.Kind == BindingTargetKind.Narration);
        var sceneTarget = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Scene);
        var previousCharacterCount = LeadingNumber(characterTarget.Detail);
        var previousNarrationCount = LeadingNumber(narrationTarget.Detail);
        var collectionChanges = 0;
        var characterNotifications = new List<string>();
        var narrationNotifications = new List<string>();
        var sceneNotifications = new List<string>();
        viewModel.BindingTargets.CollectionChanged += (_, _) => collectionChanges++;
        characterTarget.PropertyChanged += (_, args) => AddNotification(characterNotifications, args.PropertyName);
        narrationTarget.PropertyChanged += (_, args) => AddNotification(narrationNotifications, args.PropertyName);
        sceneTarget.PropertyChanged += (_, args) => AddNotification(sceneNotifications, args.PropertyName);

        row.SubjectName = "旁白";

        Assert.Equal(0, collectionChanges);
        Assert.Equal(previousCharacterCount - 1, LeadingNumber(characterTarget.Detail));
        Assert.Equal(previousNarrationCount, LeadingNumber(narrationTarget.Detail));
        Assert.Contains(nameof(BindingTargetViewModel.Detail), characterNotifications);
        Assert.Empty(narrationNotifications);
        Assert.Empty(sceneNotifications);
    }

    [Fact]
    public void AddingDialogueRowRefreshesOnlyItsCharacterBindingTarget()
    {
        var viewModel = CreateViewModel();
        var scene = viewModel.SceneTables[0];
        var character = viewModel.Project.Characters.First(character => character.Name != "旁白");
        var characterTarget = viewModel.BindingTargets.Single(binding =>
            binding.Kind == BindingTargetKind.Character &&
            binding.TargetId == character.Id);
        var sceneTarget = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Scene);
        var previousCharacterCount = LeadingNumber(characterTarget.Detail);
        var collectionChanges = 0;
        var characterNotifications = new List<string>();
        var sceneNotifications = new List<string>();
        viewModel.BindingTargets.CollectionChanged += (_, _) => collectionChanges++;
        characterTarget.PropertyChanged += (_, args) => AddNotification(characterNotifications, args.PropertyName);
        sceneTarget.PropertyChanged += (_, args) => AddNotification(sceneNotifications, args.PropertyName);

        viewModel.AddDialogueRowCommand.Execute(scene);

        Assert.Equal(0, collectionChanges);
        Assert.Equal(previousCharacterCount + 1, LeadingNumber(characterTarget.Detail));
        Assert.Contains(nameof(BindingTargetViewModel.Detail), characterNotifications);
        Assert.Empty(sceneNotifications);
    }

    [Fact]
    public void AddingNarrationRowRefreshesOnlyNarrationBindingTarget()
    {
        var viewModel = CreateViewModel();
        var scene = viewModel.SceneTables[0];
        var narrationTarget = viewModel.BindingTargets.Single(binding =>
            binding.Kind == BindingTargetKind.Narration);
        var sceneTarget = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Scene);
        var previousNarrationCount = LeadingNumber(narrationTarget.Detail);
        Assert.Equal("默认配音", narrationTarget.Category);
        Assert.Equal("默认", narrationTarget.DisplayName);
        Assert.Contains("默认配音行", narrationTarget.Detail);
        var collectionChanges = 0;
        var narrationNotifications = new List<string>();
        var sceneNotifications = new List<string>();
        viewModel.BindingTargets.CollectionChanged += (_, _) => collectionChanges++;
        narrationTarget.PropertyChanged += (_, args) => AddNotification(narrationNotifications, args.PropertyName);
        sceneTarget.PropertyChanged += (_, args) => AddNotification(sceneNotifications, args.PropertyName);

        viewModel.AddNarrationRowCommand.Execute(scene);

        Assert.Equal(0, collectionChanges);
        Assert.Equal(previousNarrationCount + 1, LeadingNumber(narrationTarget.Detail));
        Assert.Contains(nameof(BindingTargetViewModel.Detail), narrationNotifications);
        Assert.Empty(sceneNotifications);
    }

    [Fact]
    public void RemovingRowRefreshesOnlyItsPreviousBindingTarget()
    {
        var viewModel = CreateViewModel();
        var character = viewModel.Project.Characters.First(character => character.Name != "旁白");
        var row = viewModel.SceneTables.SelectMany(scene => scene.Rows)
            .First(row => !row.IsNarration && row.SubjectName == character.Name);
        var characterTarget = viewModel.BindingTargets.Single(binding =>
            binding.Kind == BindingTargetKind.Character &&
            binding.TargetId == character.Id);
        var sceneTarget = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Scene);
        var previousCharacterCount = LeadingNumber(characterTarget.Detail);
        var collectionChanges = 0;
        var characterNotifications = new List<string>();
        var sceneNotifications = new List<string>();
        viewModel.BindingTargets.CollectionChanged += (_, _) => collectionChanges++;
        characterTarget.PropertyChanged += (_, args) => AddNotification(characterNotifications, args.PropertyName);
        sceneTarget.PropertyChanged += (_, args) => AddNotification(sceneNotifications, args.PropertyName);

        viewModel.RemoveInteractionRowCommand.Execute(row);

        Assert.Equal(0, collectionChanges);
        Assert.Equal(previousCharacterCount - 1, LeadingNumber(characterTarget.Detail));
        Assert.Contains(nameof(BindingTargetViewModel.Detail), characterNotifications);
        Assert.Empty(sceneNotifications);
    }

    [Fact]
    public void RemoveInteractionRowCommandDoesNotNotifyUnaffectedSceneOrRows()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var sourceScene = viewModel.SceneTables[0];
        var unrelatedScene = viewModel.SceneTables[^1];
        viewModel.AddDialogueRowCommand.Execute(unrelatedScene);
        var removedRow = sourceScene.Rows[0];
        var unrelatedRows = unrelatedScene.Rows.ToArray();
        var sceneNotifications = new List<string>();
        var rowNotifications = new List<string>();
        unrelatedScene.PropertyChanged += (_, args) => AddNotification(sceneNotifications, args.PropertyName);
        foreach (var row in unrelatedRows)
        {
            row.PropertyChanged += (_, args) => AddNotification(rowNotifications, args.PropertyName);
        }

        viewModel.RemoveInteractionRowCommand.Execute(removedRow);

        Assert.Equal(unrelatedRows, unrelatedScene.Rows);
        Assert.Empty(sceneNotifications);
        Assert.Empty(rowNotifications);
    }

    [Fact]
    public void RemovingLastNarrationRowKeepsNarratorTargetWhenDialogueStillUsesNarratorVoice()
    {
        var viewModel = CreateViewModel();
        var scene = viewModel.SceneTables[0];
        while (viewModel.SceneTables.SelectMany(table => table.Rows).Any(row => row.IsNarration))
        {
            var narration = viewModel.SceneTables.SelectMany(table => table.Rows).First(row => row.IsNarration);
            viewModel.RemoveInteractionRowCommand.Execute(narration);
        }

        viewModel.AddNarrationRowCommand.Execute(scene);
        var row = scene.Rows.Single(candidate => candidate.IsNarration);
        var narrationTarget = viewModel.BindingTargets.Single(binding => binding.Kind == BindingTargetKind.Narration);
        var previousNarratorVoiceCount = LeadingNumber(narrationTarget.Detail);
        var initialMetrics = viewModel.WorkbenchBindingRefreshMetrics;
        var collectionChanges = 0;
        viewModel.BindingTargets.CollectionChanged += (_, _) => collectionChanges++;

        viewModel.RemoveInteractionRowCommand.Execute(row);

        Assert.Contains(viewModel.BindingTargets, binding => binding.Kind == BindingTargetKind.Narration);
        Assert.Equal(0, collectionChanges);
        Assert.Equal(previousNarratorVoiceCount - 1, LeadingNumber(narrationTarget.Detail));
        Assert.Equal(initialMetrics.FallbackFullRefreshCount, viewModel.WorkbenchBindingRefreshMetrics.FallbackFullRefreshCount);
    }

    [Fact]
    public void TemporaryCharacterRowSubjectChangeDoesNotBuildRowIndexOrFallbackRefresh()
    {
        var previousMetrics = BindingTargetRowIndex.Metrics;
        var metrics = new BindingTargetRowIndexMetrics();
        BindingTargetRowIndex.Metrics = metrics;
        try
        {
            var viewModel = CreateViewModel();
            var scene = viewModel.SceneTables[0];
            viewModel.AddDialogueRowCommand.Execute(scene);
            var row = scene.Rows[^1];
            row.SubjectName = "临时人物";
            var initialBuildCount = metrics.BuildCount;
            var initialRefreshMetrics = viewModel.WorkbenchBindingRefreshMetrics;

            row.SubjectName = "另一个临时人物";

            Assert.Equal("另一个临时人物", viewModel.WorkbenchDocument.RowsById[row.InteractionId].SubjectName);
            Assert.Equal(initialRefreshMetrics, viewModel.WorkbenchBindingRefreshMetrics);
        }
        finally
        {
            BindingTargetRowIndex.Metrics = previousMetrics;
        }
    }

    [Fact]
    public void RemovingSceneRefreshesTargetsForRowsInsideRemovedScene()
    {
        var viewModel = CreateViewModel();
        var scene = viewModel.SceneTables[0];
        var character = viewModel.Project.Characters.First(character => character.Name != "旁白");
        var characterTarget = viewModel.BindingTargets.Single(binding =>
            binding.Kind == BindingTargetKind.Character &&
            binding.TargetId == character.Id);
        var previousCharacterCount = LeadingNumber(characterTarget.Detail);
        var characterNotifications = new List<string>();
        characterTarget.PropertyChanged += (_, args) => AddNotification(characterNotifications, args.PropertyName);

        viewModel.RemoveSceneCommand.Execute(scene);

        Assert.True(LeadingNumber(characterTarget.Detail) < previousCharacterCount);
        Assert.Contains(nameof(BindingTargetViewModel.Detail), characterNotifications);
    }

    [Fact]
    public void RemovingSceneRecreatesMissingAffectedCharacterTargetWithoutFullRefreshFallback()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var sceneToRemove = viewModel.SceneTables[0];
        var remainingScene = viewModel.SceneTables[^1];
        var character = viewModel.Project.Characters.First(character => character.Name != "旁白");
        viewModel.AddDialogueRowCommand.Execute(remainingScene);
        var remainingRow = remainingScene.Rows[^1];
        remainingRow.SubjectName = character.Name;
        var characterTarget = viewModel.BindingTargets.Single(binding =>
            binding.Kind == BindingTargetKind.Character &&
            binding.TargetId == character.Id);
        viewModel.BindingTargets.Remove(characterTarget);
        var initialMetrics = viewModel.WorkbenchBindingRefreshMetrics;
        var collectionChanges = 0;
        viewModel.BindingTargets.CollectionChanged += (_, _) => collectionChanges++;

        viewModel.RemoveSceneCommand.Execute(sceneToRemove);

        Assert.Equal(initialMetrics.FallbackFullRefreshCount, viewModel.WorkbenchBindingRefreshMetrics.FallbackFullRefreshCount);
        Assert.Equal(
            initialMetrics.FallbackFullRefreshReasons.CountFor("removed_scene_rows"),
            viewModel.WorkbenchBindingRefreshMetrics.FallbackFullRefreshReasons.CountFor("removed_scene_rows"));
        Assert.True(collectionChanges >= 1);
        var recreatedTarget = viewModel.BindingTargets.Single(binding =>
            binding.Kind == BindingTargetKind.Character &&
            binding.TargetId == character.Id);
        Assert.True(LeadingNumber(recreatedTarget.Detail) >= 1);
        Assert.Equal(character.Name, recreatedTarget.DisplayName);
    }

    [Fact]
    public void RemovingSceneDeduplicatesMissingAffectedCharacterTargetRepair()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var sceneToRemove = viewModel.SceneTables[0];
        var remainingScene = viewModel.SceneTables[^1];
        var character = viewModel.Project.Characters.First(character => character.Name != "旁白");
        viewModel.AddDialogueRowCommand.Execute(sceneToRemove);
        var firstDuplicate = Assert.IsType<AnalysisResultRowViewModel>(viewModel.SelectedRow);
        Assert.Contains(firstDuplicate, sceneToRemove.Rows);
        firstDuplicate.SubjectName = character.Name;
        viewModel.AddDialogueRowCommand.Execute(sceneToRemove);
        var secondDuplicate = Assert.IsType<AnalysisResultRowViewModel>(viewModel.SelectedRow);
        Assert.Contains(secondDuplicate, sceneToRemove.Rows);
        secondDuplicate.SubjectName = character.Name;
        viewModel.AddDialogueRowCommand.Execute(remainingScene);
        var remainingRow = remainingScene.Rows[^1];
        remainingRow.SubjectName = character.Name;
        var duplicateRemovedRows = sceneToRemove.Rows
            .Where(row => !row.IsNarration && row.SubjectName == character.Name)
            .Take(2)
            .ToArray();
        Assert.True(duplicateRemovedRows.Length >= 2);
        var characterTarget = viewModel.BindingTargets.Single(binding =>
            binding.Kind == BindingTargetKind.Character &&
            binding.TargetId == character.Id);
        viewModel.BindingTargets.Remove(characterTarget);
        var initialMetrics = viewModel.WorkbenchBindingRefreshMetrics;
        var collectionChanges = 0;
        viewModel.BindingTargets.CollectionChanged += (_, _) => collectionChanges++;

        viewModel.RemoveSceneCommand.Execute(sceneToRemove);

        Assert.Equal(initialMetrics.FallbackFullRefreshCount, viewModel.WorkbenchBindingRefreshMetrics.FallbackFullRefreshCount);
        Assert.Equal(
            initialMetrics.FallbackFullRefreshReasons.CountFor("removed_scene_rows"),
            viewModel.WorkbenchBindingRefreshMetrics.FallbackFullRefreshReasons.CountFor("removed_scene_rows"));
        Assert.InRange(collectionChanges, 1, 3);
        var recreatedTargets = viewModel.BindingTargets
            .Where(binding => binding.Kind == BindingTargetKind.Character && binding.TargetId == character.Id)
            .ToArray();
        Assert.Single(recreatedTargets);
        Assert.True(LeadingNumber(recreatedTargets[0].Detail) >= 1);
    }

    [Fact]
    public void ApplyingBindingTargetBatchesWorkspaceSyncNotifications()
    {
        var viewModel = CreateViewModel();
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        var notifications = new List<string>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                notifications.Add(args.PropertyName);
            }
        };

        target.ImageAsset = "asset_character_chen";
        target.VoiceAsset = "chen.wav";
        viewModel.ApplyBindingTargetCommand.Execute(target);

        Assert.True(viewModel.IsWorkbenchDirty);
        Assert.Equal(1, notifications.Count(name => name == nameof(MainWindowViewModel.IsWorkbenchDirty)));
        Assert.Equal(1, notifications.Count(name => name == nameof(MainWindowViewModel.CanUndoWorkbench)));
        Assert.DoesNotContain(nameof(MainWindowViewModel.ProjectMeta), notifications);
        Assert.DoesNotContain(nameof(MainWindowViewModel.ResultRowCount), notifications);
    }

    [Fact]
    public void EquivalentBindingTargetApplyDoesNotNotifyWorkbenchSessionState()
    {
        var viewModel = CreateViewModel();
        var target = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        var notifications = new List<string>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                notifications.Add(args.PropertyName);
            }
        };

        viewModel.ApplyBindingTargetCommand.Execute(target);

        Assert.False(viewModel.IsWorkbenchDirty);
        Assert.False(viewModel.CanUndoWorkbench);
        Assert.DoesNotContain(nameof(MainWindowViewModel.IsWorkbenchDirty), notifications);
        Assert.DoesNotContain(nameof(MainWindowViewModel.CanUndoWorkbench), notifications);
        Assert.DoesNotContain(nameof(MainWindowViewModel.ProjectMeta), notifications);
        Assert.DoesNotContain(nameof(MainWindowViewModel.ResultRowCount), notifications);
    }

    [Fact]
    public async Task SaveWorkbenchEditsResetsWorkbenchDirtyState()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        Assert.True(viewModel.IsWorkbenchDirty);

        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsWorkbenchDirty);
    }

    [Fact]
    public async Task SaveWorkbenchEditsPersistsWorkspaceDraftWhenProjectIsOpen()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-workspace-save-{Guid.NewGuid():N}");
        var viewModel = CreateViewModel();
        await viewModel.OpenWorkspaceProjectAsync(root);
        var row = viewModel.SelectedRow!;

        row.LineText = "Saved into portable workspace draft";
        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);

        var draftPath = Path.Combine(root, WorkspaceProjectManager.DraftFileName);
        var json = await File.ReadAllTextAsync(draftPath);
        Assert.Contains("Saved into portable workspace draft", json, StringComparison.Ordinal);
        Assert.False(viewModel.IsWorkbenchDirty);
    }

    [Fact]
    public async Task WorkspaceSaveFailureKeepsWorkbenchDirty()
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore(),
            dependencies: new DraftWorkspaceServiceDependencies(WorkspaceProjectManager: new FailingSaveWorkspaceProjectManager()));
        var viewModel = ConfigureFastTts(new MainWindowViewModel(service));
        await viewModel.OpenWorkspaceProjectAsync(Path.Combine(Path.GetTempPath(), $"draft-agent-workspace-fail-{Guid.NewGuid():N}"));
        var row = viewModel.SelectedRow!;
        row.LineText = "Dirty edit that failed to persist";

        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsWorkbenchDirty);
        Assert.Contains("保存工作台调整失败", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void EditingWorkbenchFieldsDoesNotUseFullDocumentSyncFallback()
    {
        var viewModel = CreateViewModel();
        var scene = viewModel.SceneTables[0];
        var row = scene.Rows.First(row => !row.IsNarration);

        scene.SceneName = "DTO scene edit";
        scene.SelectedSceneAsset = "asset_scene_001";
        row.LineText = "DTO line edit";
        row.SelectedCharacterAsset = "asset_character_chen";
        row.SelectedVoiceReference = "chen.wav";
        row.SelectedVoiceReferenceText = "Chen sample";

        Assert.Equal(0, viewModel.WorkbenchDocumentSyncMetrics.FullDocumentBuildAttemptCount);
        Assert.Equal(0, viewModel.WorkbenchDocumentSyncMetrics.AppliedDocumentCount);
        Assert.Equal("DTO scene edit", viewModel.WorkbenchDocument.Scenes[0].Name);
        Assert.Equal("asset_scene_001", viewModel.WorkbenchDocument.Scenes[0].VisualBinding?.EffectiveAssetId);
        Assert.Equal("DTO line edit", viewModel.WorkbenchDocument.RowsById[row.InteractionId].Text);
        Assert.Equal("asset_character_chen", viewModel.WorkbenchDocument.RowsById[row.InteractionId].CharacterVisualBinding?.EffectiveAssetId);
    }

    [Fact]
    public async Task SaveWorkbenchEditsSkipsFullDocumentSyncWhenProjectionIsAligned()
    {
        var viewModel = CreateViewModel();
        var row = viewModel.SelectedRow!;
        row.LineText = "Already synced before save";

        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);

        Assert.Equal(0, viewModel.WorkbenchDocumentSyncMetrics.FullDocumentBuildAttemptCount);
        Assert.Equal(1, viewModel.WorkbenchDocumentSyncMetrics.SkippedAlignedProjectionCount);
        Assert.Equal(0, viewModel.WorkbenchDocumentSyncMetrics.SkippedNoChangeCount);
        Assert.Equal(0, viewModel.WorkbenchDocumentSyncMetrics.AppliedDocumentCount);
        Assert.False(viewModel.IsWorkbenchDirty);
    }

    [Fact]
    public async Task SaveWorkbenchEditsDoesNotRefreshDerivedBindingTargets()
    {
        var viewModel = CreateViewModel();
        var row = viewModel.SelectedRow!;
        var bindingTargetNotifications = 0;
        viewModel.BindingTargets.CollectionChanged += (_, _) => bindingTargetNotifications++;

        row.LineText = "Save without derived binding target refresh";

        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);

        Assert.Equal(0, bindingTargetNotifications);
        Assert.False(viewModel.IsWorkbenchDirty);
    }

    [Fact]
    public async Task SaveWorkbenchEditsKeepsFullDocumentSyncFallbackWhenProjectionIsUnaligned()
    {
        var viewModel = CreateViewModel();
        var row = viewModel.SelectedRow!;

        row.Order = row.Order + 10;

        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);

        Assert.Equal(1, viewModel.WorkbenchDocumentSyncMetrics.FullDocumentBuildAttemptCount);
        Assert.Equal(0, viewModel.WorkbenchDocumentSyncMetrics.SkippedNoChangeCount);
        Assert.Equal(0, viewModel.WorkbenchDocumentSyncMetrics.SkippedAlignedProjectionCount);
        Assert.Equal(1, viewModel.WorkbenchDocumentSyncMetrics.AppliedDocumentCount);
        Assert.Equal(
            MainWindowViewModel.WorkbenchFullDocumentSyncReasonKind.SceneDraftMismatch,
            viewModel.WorkbenchDocumentSyncMetrics.LastFullDocumentSyncReason.Kind);
        Assert.Equal(
            1,
            viewModel.WorkbenchDocumentSyncMetrics.FullDocumentSyncReasons.CountFor(
                MainWindowViewModel.WorkbenchFullDocumentSyncReasonKind.SceneDraftMismatch));
        Assert.False(viewModel.IsWorkbenchDirty);
    }

    [Fact]
    public async Task FullDocumentSyncFallbackRecordsSceneCountMismatchReason()
    {
        var viewModel = CreateViewModel();
        viewModel.SceneTables.Add(SceneTableViewModel.CreateManual(viewModel.SceneTables.Count + 1));

        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);

        Assert.Equal(1, viewModel.WorkbenchDocumentSyncMetrics.FullDocumentBuildAttemptCount);
        Assert.Equal(
            MainWindowViewModel.WorkbenchFullDocumentSyncReasonKind.SceneCountMismatch,
            viewModel.WorkbenchDocumentSyncMetrics.LastFullDocumentSyncReason.Kind);
        Assert.Equal(viewModel.SceneTables.Count, viewModel.WorkbenchDocumentSyncMetrics.LastFullDocumentSyncReason.ActualCount);
        Assert.Equal(
            1,
            viewModel.WorkbenchDocumentSyncMetrics.FullDocumentSyncReasons.CountFor(
                MainWindowViewModel.WorkbenchFullDocumentSyncReasonKind.SceneCountMismatch));
    }

    [Fact]
    public async Task FullDocumentSyncFallbackRecordsRowDraftMismatchReason()
    {
        var viewModel = CreateViewModel();
        var scene = viewModel.SceneTables[0];
        var row = scene.Rows[0];
        var currentDraft = viewModel.WorkbenchDocument.RowsById[row.InteractionId];
        row.MarkAppliedDraft(currentDraft with { Text = currentDraft.Text });

        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);

        Assert.Equal(1, viewModel.WorkbenchDocumentSyncMetrics.FullDocumentBuildAttemptCount);
        Assert.Equal(
            MainWindowViewModel.WorkbenchFullDocumentSyncReasonKind.RowDraftMismatch,
            viewModel.WorkbenchDocumentSyncMetrics.LastFullDocumentSyncReason.Kind);
        Assert.Equal(row.InteractionId, viewModel.WorkbenchDocumentSyncMetrics.LastFullDocumentSyncReason.ActualId);
        Assert.Equal(
            1,
            viewModel.WorkbenchDocumentSyncMetrics.FullDocumentSyncReasons.CountFor(
                MainWindowViewModel.WorkbenchFullDocumentSyncReasonKind.RowDraftMismatch));
    }

    [Fact]
    public async Task FullDocumentSyncFallbackAccumulatesReasonCounts()
    {
        var viewModel = CreateViewModel();
        viewModel.SceneTables.Add(SceneTableViewModel.CreateManual(viewModel.SceneTables.Count + 1));

        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);

        viewModel.AddSceneCommand.Execute(null);
        viewModel.SceneTables.Add(SceneTableViewModel.CreateManual(viewModel.SceneTables.Count + 1));
        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.WorkbenchDocumentSyncMetrics.FullDocumentBuildAttemptCount);
        Assert.Equal(
            2,
            viewModel.WorkbenchDocumentSyncMetrics.FullDocumentSyncReasons.CountFor(
                MainWindowViewModel.WorkbenchFullDocumentSyncReasonKind.SceneCountMismatch));
        Assert.Equal(
            0,
            viewModel.WorkbenchDocumentSyncMetrics.FullDocumentSyncReasons.CountFor(
                MainWindowViewModel.WorkbenchFullDocumentSyncReasonKind.RowDraftMismatch));
    }

    [Fact]
    public void ExpandAndCollapseAllWorkbenchScenesOnlyChangeDisplayProjection()
    {
        var viewModel = CreateViewModel();
        viewModel.Project = CreateWorkbenchProject("large", 80, 1200);
        var document = viewModel.WorkbenchDocument;

        Assert.All(viewModel.SceneTables, scene => Assert.True(scene.IsExpanded));
        Assert.Equal(1280, viewModel.WorkbenchDisplayItems.Count);

        viewModel.CollapseAllWorkbenchScenesCommand.Execute(null);

        Assert.All(viewModel.SceneTables, scene => Assert.False(scene.IsExpanded));
        Assert.Equal(80, viewModel.WorkbenchDisplayItems.Count);
        Assert.Same(document, viewModel.WorkbenchDocument);
        Assert.Equal(1200, viewModel.WorkbenchDocument.RowsById.Count);
        Assert.False(viewModel.IsWorkbenchDirty);
        Assert.Contains("已折叠全部工作台场景", viewModel.StatusText);
        AssertDisplayProjectionMatchesSceneTables(viewModel);

        viewModel.ExpandAllWorkbenchScenesCommand.Execute(null);

        Assert.All(viewModel.SceneTables, scene => Assert.True(scene.IsExpanded));
        Assert.Equal(1280, viewModel.WorkbenchDisplayItems.Count);
        Assert.Same(document, viewModel.WorkbenchDocument);
        Assert.False(viewModel.IsWorkbenchDirty);
        Assert.Contains("已展开全部工作台场景", viewModel.StatusText);
        AssertDisplayProjectionMatchesSceneTables(viewModel);
    }

    [Fact]
    public async Task RevertWorkbenchRestoresOriginalAiJsonEvenAfterSavedWorkbenchEdits()
    {
        var viewModel = CreateViewModel();
        viewModel.ConfirmResetWorkbenchAsync = () => Task.FromResult(true);
        var originalSceneCount = viewModel.SceneTables.Count;
        var row = viewModel.SelectedRow!;
        var interactionId = row.InteractionId;
        var originalText = row.LineText;
        var originalProjectId = viewModel.Project.ProjectId;

        row.LineText = "Saved line should not become reset baseline";
        viewModel.AddSceneCommand.Execute(null);
        Assert.True(viewModel.IsWorkbenchDirty);
        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);
        Assert.False(viewModel.IsWorkbenchDirty);
        Assert.Contains(
            viewModel.WorkbenchDocument.RowsById.Values,
            draft => string.Equals(draft.Text, "Saved line should not become reset baseline", StringComparison.Ordinal));

        await viewModel.RevertWorkbenchCommand.ExecuteAsync(null);

        var revertedRow = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .Single(candidate => candidate.InteractionId == interactionId);
        Assert.Equal(originalProjectId, viewModel.Project.ProjectId);
        Assert.Equal(originalSceneCount, viewModel.SceneTables.Count);
        Assert.Equal(originalText, revertedRow.LineText);
        Assert.Same(revertedRow, viewModel.SelectedRow);
        Assert.True(revertedRow.IsSelected);
        Assert.False(viewModel.IsWorkbenchDirty);
        var sessionDocument = viewModel.WorkbenchDocument;
        var sessionRow = sessionDocument.RowsById[interactionId];
        Assert.Equal(viewModel.SceneTables.Count, sessionDocument.Scenes.Count);
        Assert.Equal(revertedRow.LineText, sessionRow.Text);
        Assert.Equal(revertedRow.SubjectName, sessionRow.SubjectName);
        Assert.DoesNotContain(
            sessionDocument.RowsById.Values,
            draft => string.Equals(draft.Text, "Saved line should not become reset baseline", StringComparison.Ordinal));
        Assert.Contains("原始 AI JSON", viewModel.StatusText);
    }

    [Fact]
    public async Task RevertWorkbenchAfterReopeningWorkspaceRestoresPersistedOriginalAiJson()
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-reset-original-ai-{Guid.NewGuid():N}");
        var firstViewModel = CreateViewModel();
        await firstViewModel.OpenWorkspaceProjectAsync(root);
        var interactionId = firstViewModel.SelectedRow!.InteractionId;
        var originalText = firstViewModel.SelectedRow.LineText;
        var originalSceneCount = firstViewModel.SceneTables.Count;

        firstViewModel.SelectedRow.LineText = "Saved workspace draft must not replace original AI JSON";
        firstViewModel.AddSceneCommand.Execute(null);
        await firstViewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);

        var reopenedViewModel = CreateViewModel();
        reopenedViewModel.ConfirmResetWorkbenchAsync = () => Task.FromResult(true);
        await reopenedViewModel.OpenWorkspaceProjectAsync(root);
        Assert.Contains(
            reopenedViewModel.WorkbenchDocument.RowsById.Values,
            draft => string.Equals(draft.Text, "Saved workspace draft must not replace original AI JSON", StringComparison.Ordinal));
        Assert.Equal(originalSceneCount + 1, reopenedViewModel.SceneTables.Count);

        await reopenedViewModel.RevertWorkbenchCommand.ExecuteAsync(null);

        var revertedRow = reopenedViewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .Single(candidate => candidate.InteractionId == interactionId);
        Assert.Equal(originalText, revertedRow.LineText);
        Assert.Equal(originalSceneCount, reopenedViewModel.SceneTables.Count);
        Assert.DoesNotContain(
            reopenedViewModel.WorkbenchDocument.RowsById.Values,
            draft => string.Equals(draft.Text, "Saved workspace draft must not replace original AI JSON", StringComparison.Ordinal));
        Assert.Contains("原始 AI JSON", reopenedViewModel.StatusText);
    }

    [Fact]
    public async Task RevertWorkbenchPreservesSceneExpandedState()
    {
        var viewModel = CreateViewModel();
        viewModel.ConfirmResetWorkbenchAsync = () => Task.FromResult(true);
        var scene = viewModel.SceneTables[0];
        scene.ToggleExpandedCommand.Execute(null);
        scene.SceneName = "Unsaved scene name";
        Assert.False(scene.IsExpanded);
        Assert.True(viewModel.IsWorkbenchDirty);

        await viewModel.RevertWorkbenchCommand.ExecuteAsync(null);

        var revertedScene = viewModel.SceneTables.Single(candidate => candidate.SceneId == scene.SceneId);
        Assert.False(revertedScene.IsExpanded);
        Assert.NotEqual("Unsaved scene name", revertedScene.SceneName);
        Assert.False(viewModel.IsWorkbenchDirty);
    }

    [Fact]
    public async Task RevertWorkbenchCancelledKeepsUnsavedEdits()
    {
        var viewModel = CreateViewModel();
        viewModel.ConfirmResetWorkbenchAsync = () => Task.FromResult(false);
        var row = viewModel.SelectedRow!;
        var interactionId = row.InteractionId;

        row.LineText = "Unsaved line";
        Assert.True(viewModel.IsWorkbenchDirty);

        await viewModel.RevertWorkbenchCommand.ExecuteAsync(null);

        var currentRow = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .Single(candidate => candidate.InteractionId == interactionId);
        Assert.Equal("Unsaved line", currentRow.LineText);
        Assert.True(viewModel.IsWorkbenchDirty);
        Assert.Contains("已取消重置工作台", viewModel.StatusText);
    }

    [Fact]
    public async Task RevertWorkbenchCommandBlocksWhileWorkbenchMutationLocked()
    {
        var viewModel = CreateViewModel();
        var originalProject = viewModel.Project;
        var row = viewModel.SelectedRow!;
        row.LineText = "Unsaved line before locked reset";
        viewModel.ConfirmResetWorkbenchAsync = () => throw new InvalidOperationException("Reset confirmation should not open while locked.");
        viewModel.IsTtsSynthesisRunning = true;

        await viewModel.RevertWorkbenchCommand.ExecuteAsync(null);

        Assert.Same(originalProject, viewModel.Project);
        Assert.Equal("Unsaved line before locked reset", row.LineText);
        Assert.True(viewModel.IsWorkbenchDirty);
        Assert.Contains("锁定重置工作台", viewModel.StatusText);
    }

    [Fact]
    public async Task OpenWorkspaceProjectBlocksWhileWorkbenchMutationLocked()
    {
        var viewModel = CreateViewModel();
        var originalProject = viewModel.Project;
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-open-locked-{Guid.NewGuid():N}");
        viewModel.IsTtsSynthesisRunning = true;

        await viewModel.OpenWorkspaceProjectAsync(root);

        Assert.Same(originalProject, viewModel.Project);
        Assert.False(Directory.Exists(root));
        Assert.Contains("锁定打开工作台项目", viewModel.StatusText);
    }

    [Fact]
    public async Task CommitWorkbenchUsesSessionDocumentAsSaveSource()
    {
        var viewModel = CreateViewModel();
        var scene = viewModel.SceneTables[0];
        var row = scene.Rows.First(row => !row.IsNarration);

        scene.SelectedSceneAsset = "asset_scene_001";
        row.SelectedCharacterAsset = "asset_character_chen";
        row.SelectedVoiceReference = "chen.wav";
        row.SelectedVoiceReferenceText = "Chen reference";
        row.SelectedCharacterPreviewAsset = @"C:\preview-only\character.png";
        row.PreviewAssetPath = @"C:\preview-only\scene.png";
        var sessionDocument = viewModel.WorkbenchDocument;

        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);

        var savedScene = viewModel.Project.Scenes.Single(projectScene => projectScene.Id == scene.SceneId);
        var savedInteraction = savedScene.Interactions.Single(interaction => interaction.Id == row.InteractionId);
        Assert.Equal("asset_scene_001", sessionDocument.Scenes.Single(draft => draft.SceneId == scene.SceneId).VisualBinding?.EffectiveAssetId);
        Assert.Equal("asset_scene_001", savedScene.AssetBinding.BackgroundImage);
        Assert.Equal("asset_character_chen", savedInteraction.Binding.SubjectCharacterVisualBinding?.EffectiveAssetId);
        Assert.Equal("chen.wav", savedInteraction.Binding.SubjectVoiceReferenceId);
        Assert.Equal("Chen reference", savedInteraction.Dialogue?.Voice.ReferenceText);
        Assert.DoesNotContain("preview-only", savedInteraction.Binding.SubjectCharacterVisualBinding?.EffectiveAssetId ?? string.Empty);
        Assert.False(viewModel.IsWorkbenchDirty);
    }

    [Fact]
    public async Task SynthesizeVoicesFailureKeepsCommittedWorkbenchProjectAndCleanSession()
    {
        var store = new InMemoryInteractionProjectStore();
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new ThrowingComfyUiClient(),
            store);
        var viewModel = ConfigureFastTts(new MainWindowViewModel(service));
        await OpenTemporaryWorkspaceProjectAsync(viewModel);
        BindAllRowsToDefaultVoice(viewModel);
        var row = viewModel.SelectedRow!;
        row.LineText = "Committed before external failure";

        await viewModel.SynthesizeAllVoicesCommand.ExecuteAsync(null);

        var saved = await store.LoadLastAsync();
        var committedInteraction = viewModel.Project.Scenes
            .SelectMany(scene => scene.Interactions)
            .Single(interaction => interaction.Id == row.InteractionId);
        Assert.NotNull(saved);
        Assert.False(viewModel.IsWorkbenchDirty);
        Assert.Equal("Committed before external failure", committedInteraction.Dialogue?.Text ?? committedInteraction.Narration?.Text);
        Assert.Equal(viewModel.Project.ProjectId, saved!.ProjectId);
        Assert.Equal(viewModel.TtsTaskCount, viewModel.TtsFailedCount);
        Assert.Contains("失败", viewModel.StatusText);
    }

    [Fact]
    public async Task SynthesizeVoicesUsesCommittedWorkbenchDocumentForSubmittedTasks()
    {
        var client = new CapturingComfyUiClient();
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            client,
            new InMemoryInteractionProjectStore());
        var viewModel = ConfigureFastTts(new MainWindowViewModel(service));
        await OpenTemporaryWorkspaceProjectAsync(viewModel);
        BindAllRowsToDefaultVoice(viewModel);
        var row = viewModel.SceneTables.SelectMany(scene => scene.Rows).First(candidate => !candidate.IsNarration);
        row.LineText = "Committed text for Comfy submit";
        row.SelectedVoiceReference = "committed-comfy.wav";

        await viewModel.SynthesizeAllVoicesCommand.ExecuteAsync(null);

        var submittedTtsTask = Assert.Single(client.Requests, request =>
            request.Task.Type == GenerationTaskType.Tts &&
            request.Task.TargetId == row.InteractionId);
        Assert.Equal("Committed text for Comfy submit", submittedTtsTask.Task.Parameters["text"]);
        Assert.Equal("committed-comfy.wav", submittedTtsTask.Task.Parameters["voiceReferenceId"]);
        Assert.False(viewModel.IsWorkbenchDirty);
    }

    [Fact]
    public async Task SynthesizeVoicesUsesLatestWorkbenchAfterCharacterVoiceSettings()
    {
        var client = new CapturingComfyUiClient();
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            client,
            new InMemoryInteractionProjectStore());
        var viewModel = ConfigureFastTts(new MainWindowViewModel(service));
        await OpenTemporaryWorkspaceProjectAsync(viewModel);
        BindAllRowsToDefaultVoice(viewModel);
        EnableRoleDialogueVoiceRoute(viewModel);
        var target = viewModel.CharacterBindingTargets.First();
        target.VoiceReferenceText = "role reference after setup";
        await viewModel.SetVoiceReferenceSelectionAsync(target, "role-after-setup.wav");
        var row = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(candidate => !candidate.IsNarration && string.Equals(candidate.SubjectName, target.DisplayName, StringComparison.Ordinal));
        row.LineText = "工作台编辑后的最新台词";
        row.SelectedVoiceReferenceText = "workbench latest reference";

        await viewModel.SynthesizeAllVoicesCommand.ExecuteAsync(null);

        var submittedTtsTask = Assert.Single(client.Requests, request =>
            request.Task.Type == GenerationTaskType.Tts &&
            request.Task.TargetId == row.InteractionId);
        Assert.Equal("工作台编辑后的最新台词", submittedTtsTask.Task.Parameters["text"]);
        Assert.Equal("role-after-setup.wav", submittedTtsTask.Task.Parameters["voiceReferenceId"]);
        Assert.Equal("role reference after setup", submittedTtsTask.Task.Parameters["referenceText"]);
        Assert.False(viewModel.IsWorkbenchDirty);
    }

    [Fact]
    public async Task SynthesizeVoicesKeepsCharacterVoiceTableAcrossDefaultRouteToggle()
    {
        var client = new CapturingComfyUiClient();
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            client,
            new InMemoryInteractionProjectStore());
        var viewModel = ConfigureFastTts(new MainWindowViewModel(service));
        await OpenTemporaryWorkspaceProjectAsync(viewModel);
        const string fallbackVoice = "fallback-route.wav";
        BindAllRowsToVoice(viewModel, fallbackVoice, "fallback route reference");
        EnableRoleDialogueVoiceRoute(viewModel);
        var target = viewModel.CharacterBindingTargets.First();
        target.VoiceReferenceText = "role table reference";
        await viewModel.SetVoiceReferenceSelectionAsync(target, "role-table.wav");
        var row = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(candidate => !candidate.IsNarration && string.Equals(candidate.SubjectName, target.DisplayName, StringComparison.Ordinal));

        viewModel.ToggleRoleAllVoiceRouteCommand.Execute(null);
        Assert.False(viewModel.UseRoleVoiceForAllCharacterLines);
        target = viewModel.CharacterBindingTargets.Single(candidate => candidate.TargetId == target.TargetId);
        Assert.Equal("role-table.wav", target.VoiceAsset);
        await viewModel.SynthesizeAllVoicesCommand.ExecuteAsync(null);

        var defaultRouteTask = client.Requests.Last(request =>
            request.Task.Type == GenerationTaskType.Tts &&
            request.Task.TargetId == row.InteractionId).Task;
        Assert.Equal(fallbackVoice, defaultRouteTask.Parameters["voiceReferenceId"]);

        client.Requests.Clear();
        viewModel.ToggleRoleAllVoiceRouteCommand.Execute(null);
        Assert.True(viewModel.UseRoleVoiceForAllCharacterLines);
        target = viewModel.CharacterBindingTargets.Single(candidate => candidate.TargetId == target.TargetId);
        Assert.Equal("role-table.wav", target.VoiceAsset);
        await viewModel.SynthesizeAllVoicesCommand.ExecuteAsync(null);

        var roleRouteTask = client.Requests.Last(request =>
            request.Task.Type == GenerationTaskType.Tts &&
            request.Task.TargetId == row.InteractionId).Task;
        Assert.Equal("role-table.wav", roleRouteTask.Parameters["voiceReferenceId"]);
        Assert.Equal("role table reference", roleRouteTask.Parameters["referenceText"]);
        Assert.Equal("role-table.wav", target.VoiceAsset);
    }










    [Fact]
    public async Task SynthesizeVoiceTaskFailureUpdatesProjectAndKeepsSessionClean()
    {
        var store = new InMemoryInteractionProjectStore();
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new ThrowingComfyUiClient(),
            store);
        var viewModel = ConfigureFastTts(new MainWindowViewModel(service));
        await OpenTemporaryWorkspaceProjectAsync(viewModel);
        BindAllRowsToDefaultVoice(viewModel);
        var row = viewModel.SelectedRow!;
        row.LineText = "Queued before synthesis failure";

        await viewModel.SynthesizeAllVoicesCommand.ExecuteAsync(null);

        var saved = await store.LoadLastAsync();
        Assert.NotNull(saved);
        Assert.False(viewModel.IsWorkbenchDirty);
        Assert.Contains(
            viewModel.Project.GenerationTasks,
            task => task.Type == GenerationTaskType.Tts &&
                task.TargetId == row.InteractionId &&
                task.Status == GenerationTaskStatus.Failed);
        Assert.Contains("失败", viewModel.StatusText);
        Assert.Equal(viewModel.Project.ProjectId, saved!.ProjectId);
    }

    [Fact]
    public async Task SynthesizeVoicesUsesCommittedWorkbenchDocumentForSubmittedTtsTasks()
    {
        var client = new CapturingComfyUiClient();
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            client,
            new InMemoryInteractionProjectStore());
        var viewModel = ConfigureFastTts(new MainWindowViewModel(service));
        await OpenTemporaryWorkspaceProjectAsync(viewModel);
        BindAllRowsToDefaultVoice(viewModel);
        var row = viewModel.SceneTables.SelectMany(scene => scene.Rows).First(candidate => !candidate.IsNarration);
        row.LineText = "Committed text for TTS synthesis";
        row.SelectedVoiceReference = "committed-tts.wav";

        await viewModel.SynthesizeAllVoicesCommand.ExecuteAsync(null);

        var submittedTtsTask = Assert.Single(client.Requests, request =>
            request.Task.Type == GenerationTaskType.Tts &&
            request.Task.TargetId == row.InteractionId);
        Assert.Equal("Committed text for TTS synthesis", submittedTtsTask.Task.Parameters["text"]);
        Assert.Equal("committed-tts.wav", submittedTtsTask.Task.Parameters["voiceReferenceId"]);
        Assert.False(viewModel.IsWorkbenchDirty);
    }

    [Fact]
    public async Task SynthesizeVoicesPersistsImportedAudioToWorkspaceDraft()
    {
        var client = new DownloadingComfyUiClient();
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            client,
            new InMemoryInteractionProjectStore());
        var viewModel = ConfigureFastTts(new MainWindowViewModel(service));
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-tts-sync-{Guid.NewGuid():N}");
        await viewModel.OpenWorkspaceProjectAsync(root);
        BindAllRowsToDefaultVoice(viewModel);

        await viewModel.SynthesizeAllVoicesCommand.ExecuteAsync(null);

        var draftPath = Path.Combine(root, WorkspaceProjectManager.DraftFileName);
        var json = await File.ReadAllTextAsync(draftPath);
        Assert.Contains("\"resultAssetId\": \"asset_", json, StringComparison.Ordinal);
        Assert.Contains("\"status\": \"Completed\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SynthesizeVoicesBlocksRowsWithoutVoiceBinding()
    {
        var client = new CapturingComfyUiClient();
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            client,
            new InMemoryInteractionProjectStore());
        var viewModel = ConfigureFastTts(new MainWindowViewModel(service));
        await OpenTemporaryWorkspaceProjectAsync(viewModel);

        await viewModel.SynthesizeAllVoicesCommand.ExecuteAsync(null);

        Assert.Empty(client.Requests);
        Assert.Contains("未绑定参考音频", viewModel.StatusText);
        Assert.Contains("未提交：缺少参考音频绑定", viewModel.ComfySubmitStatusText);
        Assert.All(
            viewModel.Project.GenerationTasks.Where(task => task.Type == GenerationTaskType.Tts),
            task => Assert.NotEqual(GenerationTaskStatus.Completed, task.Status));
    }

    [Fact]
    public async Task SynthesizeVoicesTreatsUnboundVoicePlaceholderAsMissingBeforeComfyCheck()
    {
        var client = new CapturingComfyUiClient();
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            client,
            new InMemoryInteractionProjectStore());
        var viewModel = ConfigureFastTts(new MainWindowViewModel(service));
        await OpenTemporaryWorkspaceProjectAsync(viewModel);
        foreach (var row in viewModel.SceneTables.SelectMany(scene => scene.Rows))
        {
            row.SelectedVoiceReference = "未绑定声音";
            row.SelectedVoiceReferenceText = "reference text should not matter";
        }

        await viewModel.SynthesizeAllVoicesCommand.ExecuteAsync(null);

        Assert.Empty(client.Requests);
        Assert.Equal(0, client.ConnectionCheckCount);
        Assert.Contains("未绑定参考音频", viewModel.StatusText);
        Assert.Contains("未提交：缺少参考音频绑定", viewModel.ComfySubmitStatusText);
        Assert.Equal(0, viewModel.TtsFailedCount);
        Assert.All(
            viewModel.Project.GenerationTasks.Where(task => task.Type == GenerationTaskType.Tts),
            task => Assert.Equal(GenerationTaskStatus.Draft, task.Status));
        Assert.All(
            viewModel.SceneTables.SelectMany(scene => scene.Rows),
            row => Assert.Equal("待合成", row.TtsStatusText));
    }

    [Fact]
    public async Task SynthesizeVoicesBlocksFailedRowsWithoutVoiceBindingBeforeComfyCheck()
    {
        var client = new CapturingComfyUiClient();
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            client,
            new InMemoryInteractionProjectStore());
        var viewModel = ConfigureFastTts(new MainWindowViewModel(service));
        await OpenTemporaryWorkspaceProjectAsync(viewModel);
        foreach (var row in viewModel.SceneTables.SelectMany(scene => scene.Rows))
        {
            row.SelectedVoiceReference = "未绑定声音";
            row.SelectedVoiceReferenceText = string.Empty;
        }

        var failedTasks = viewModel.Project.GenerationTasks
            .Select(task => task.Type == GenerationTaskType.Tts
                ? task with
                {
                    Status = GenerationTaskStatus.Failed,
                    Parameters = new Dictionary<string, string>(task.Parameters, StringComparer.Ordinal)
                    {
                        ["voiceReferenceId"] = "未绑定声音",
                        ["referenceText"] = string.Empty,
                        [TtsSynthesisQueueService.FailureMessageParameter] = "previous synthesis failure"
                    }
                }
                : task)
            .ToArray();
        viewModel.Project = TtsSynthesisQueueService.ReplaceTasks(viewModel.Project, failedTasks);

        await viewModel.SynthesizeAllVoicesCommand.ExecuteAsync(null);

        Assert.Empty(client.Requests);
        Assert.Equal(0, client.ConnectionCheckCount);
        Assert.Contains("未绑定参考音频", viewModel.StatusText);
        Assert.Contains("未提交：缺少参考音频绑定", viewModel.ComfySubmitStatusText);
    }

    [Fact]
    public async Task SynthesizeVoicesRefreshesSelectedFailedRowStatusWhenResetToDraft()
    {
        var client = new CapturingComfyUiClient();
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            client,
            new InMemoryInteractionProjectStore());
        var viewModel = ConfigureFastTts(new MainWindowViewModel(service));
        await OpenTemporaryWorkspaceProjectAsync(viewModel);
        var selectedRow = viewModel.SelectedRow!;
        foreach (var row in viewModel.SceneTables.SelectMany(scene => scene.Rows))
        {
            row.SelectedVoiceReference = "未绑定声音";
            row.SelectedVoiceReferenceText = string.Empty;
        }

        var failedTasks = viewModel.Project.GenerationTasks
            .Select(task => task.Type == GenerationTaskType.Tts
                ? task with
                {
                    Status = GenerationTaskStatus.Failed,
                    Parameters = new Dictionary<string, string>(task.Parameters, StringComparer.Ordinal)
                    {
                        ["voiceReferenceId"] = "未绑定声音",
                        ["referenceText"] = string.Empty,
                        [TtsSynthesisQueueService.FailureMessageParameter] = "previous synthesis failure"
                    }
                }
                : task)
            .ToArray();
        viewModel.Project = TtsSynthesisQueueService.ReplaceTasks(viewModel.Project, failedTasks);
        Assert.True(selectedRow.IsSelected);
        Assert.Equal("失败", selectedRow.TtsStatusText);
        var notifications = new List<string>();
        selectedRow.PropertyChanged += (_, args) => AddNotification(notifications, args.PropertyName);

        await viewModel.SynthesizeAllVoicesCommand.ExecuteAsync(null);

        Assert.Empty(client.Requests);
        Assert.Equal(0, client.ConnectionCheckCount);
        Assert.Equal("待合成", selectedRow.TtsStatusText);
        Assert.Contains(nameof(AnalysisResultRowViewModel.TtsStatusText), notifications);
    }

    [Fact]
    public async Task RegenerateRowVoiceBlocksRowWithoutVoiceBinding()
    {
        var client = new CapturingComfyUiClient();
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            client,
            new InMemoryInteractionProjectStore());
        var viewModel = ConfigureFastTts(new MainWindowViewModel(service));
        await OpenTemporaryWorkspaceProjectAsync(viewModel);
        var row = viewModel.SceneTables.SelectMany(scene => scene.Rows).First(candidate => !candidate.IsNarration);

        await viewModel.RegenerateRowVoiceCommand.ExecuteAsync(row);

        Assert.Empty(client.Requests);
        Assert.Contains("未绑定参考音频", viewModel.StatusText);
        Assert.Contains("未提交：缺少参考音频绑定", viewModel.ComfySubmitStatusText);
    }

    [Fact]
    public async Task RegenerateRowVoiceUsesLatestWorkbenchAfterCharacterVoiceSettings()
    {
        var client = new CapturingComfyUiClient();
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            client,
            new InMemoryInteractionProjectStore());
        var viewModel = ConfigureFastTts(new MainWindowViewModel(service));
        await OpenTemporaryWorkspaceProjectAsync(viewModel);
        BindAllRowsToDefaultVoice(viewModel);
        EnableRoleDialogueVoiceRoute(viewModel);
        var target = viewModel.CharacterBindingTargets.First();
        target.VoiceReferenceText = "role reference before row edit";
        await viewModel.SetVoiceReferenceSelectionAsync(target, "role-regenerate.wav");
        var row = viewModel.SceneTables
            .SelectMany(scene => scene.Rows)
            .First(candidate => !candidate.IsNarration && string.Equals(candidate.SubjectName, target.DisplayName, StringComparison.Ordinal));
        row.LineText = "单行重生成使用工作台最新文本";
        row.SelectedVoiceReferenceText = "row latest regenerate reference";

        await viewModel.RegenerateRowVoiceCommand.ExecuteAsync(row);

        var submittedTtsTask = Assert.Single(client.Requests, request =>
            request.Task.Type == GenerationTaskType.Tts &&
            request.Task.TargetId == row.InteractionId);
        Assert.Equal("单行重生成使用工作台最新文本", submittedTtsTask.Task.Parameters["text"]);
        Assert.Equal("role-regenerate.wav", submittedTtsTask.Task.Parameters["voiceReferenceId"]);
        Assert.Equal("row latest regenerate reference", submittedTtsTask.Task.Parameters["referenceText"]);
        Assert.False(viewModel.IsWorkbenchDirty);
    }

    [Fact]
    public async Task RegenerateRowVoiceWhileRunningQueuesCompletedRowWithCurrentTextAndEmotion()
    {
        var viewModel = CreateViewModel();
        var rows = viewModel.SceneTables.SelectMany(scene => scene.Rows).Where(row => !row.IsNarration).Take(2).ToArray();
        Assert.True(rows.Length >= 2);
        var completedRow = rows[0];
        var unfinishedRow = rows[1];
        var completedTask = WithDefaultVoice(new GenerationTask(
            "tts_completed_live_resubmit",
            GenerationTaskType.Tts,
            completedRow.InteractionId,
            "Completed live resubmit voice",
            "comfyui",
            GenerationTaskStatus.Completed,
            new Dictionary<string, string>(StringComparer.Ordinal),
            "completed_before_live_resubmit.wav"));
        var pendingTask = WithDefaultVoice(new GenerationTask(
            "tts_pending_live_resubmit",
            GenerationTaskType.Tts,
            unfinishedRow.InteractionId,
            "Pending live resubmit voice",
            "comfyui",
            GenerationTaskStatus.Pending,
            new Dictionary<string, string>(StringComparer.Ordinal),
            null));
        viewModel.Project = viewModel.Project with
        {
            Scenes = viewModel.Project.Scenes
                .Select(scene => scene with
                {
                    Interactions = scene.Interactions
                        .Select(interaction =>
                            interaction.Id == completedRow.InteractionId
                                ? interaction with { Tasks = [completedTask] }
                                : interaction.Id == unfinishedRow.InteractionId
                                    ? interaction with { Tasks = [pendingTask] }
                                    : interaction)
                        .ToArray()
                })
                .ToArray(),
            GenerationTasks = [completedTask, pendingTask]
        };
        completedRow = viewModel.SceneTables.SelectMany(scene => scene.Rows).Single(row => row.InteractionId == completedTask.TargetId);
        unfinishedRow = viewModel.SceneTables.SelectMany(scene => scene.Rows).Single(row => row.InteractionId == pendingTask.TargetId);
        viewModel.IsTtsSynthesisRunning = true;
        completedRow.LineText = "完成行运行中编辑后插队文本";
        completedRow.Emotion = "Happy 0.8";

        await viewModel.RegenerateRowVoiceCommand.ExecuteAsync(unfinishedRow);

        Assert.Contains("仅允许已完成行", viewModel.StatusText, StringComparison.Ordinal);
        await viewModel.RegenerateRowVoiceCommand.ExecuteAsync(completedRow);

        Assert.Contains("加入当前语音队列", viewModel.StatusText, StringComparison.Ordinal);
        var pendingOverrides = ReadPendingLiveTtsResubmissionOverrides(viewModel, completedTask.Id);
        Assert.Equal("完成行运行中编辑后插队文本", pendingOverrides["text"]);
        Assert.Equal("Happy", pendingOverrides["emotion"]);
        Assert.Equal(TtsWorkbenchPolicy.BuildRegenerateRowOverrides(completedRow)["emotionStrength"], pendingOverrides["emotionStrength"]);
        Assert.Equal("完成行运行中编辑后插队文本", viewModel.WorkbenchDocument.RowsById[completedRow.InteractionId].Text);
    }

    [Fact]
    public async Task SynthesizeVoicesAllowsRowsWithoutReferenceTextForCharacterVoicesNodeSidecar()
    {
        var client = new CapturingComfyUiClient();
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            client,
            new InMemoryInteractionProjectStore());
        var viewModel = ConfigureFastTts(new MainWindowViewModel(service));
        await OpenTemporaryWorkspaceProjectAsync(viewModel);
        foreach (var candidate in viewModel.SceneTables.SelectMany(scene => scene.Rows))
        {
            candidate.SelectedVoiceReference = "reference-text-required.wav";
            candidate.SelectedVoiceReferenceText = "reference text";
        }

        var row = viewModel.SceneTables.SelectMany(scene => scene.Rows).First(candidate => !candidate.IsNarration);
        row.SelectedVoiceReference = "missing-reference-text.wav";
        row.SelectedVoiceReferenceText = string.Empty;

        await viewModel.SynthesizeAllVoicesCommand.ExecuteAsync(null);

        Assert.NotEmpty(client.Requests);
        Assert.Equal(1, client.ConnectionCheckCount);
        Assert.DoesNotContain("参考文本为空", viewModel.StatusText);
        Assert.DoesNotContain("缺少声音参考文本", viewModel.ComfySubmitStatusText);
    }

    [Fact]
    public async Task SynthesizeVoicesBlocksWhenComfyConnectionFailsAfterBindingsPass()
    {
        var client = new CapturingComfyUiClient(new ComfyConnectionCheckResult(
            "http://127.0.0.1:8000",
            ComfyConnectionState.Unreachable,
            null,
            "ComfyUI unreachable: timeout."));
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            client,
            new InMemoryInteractionProjectStore());
        var viewModel = ConfigureFastTts(new MainWindowViewModel(service));
        await OpenTemporaryWorkspaceProjectAsync(viewModel);
        BindAllRowsToDefaultVoice(viewModel);

        await viewModel.SynthesizeAllVoicesCommand.ExecuteAsync(null);

        Assert.Empty(client.Requests);
        Assert.Equal(1, client.ConnectionCheckCount);
        Assert.Equal("failed", viewModel.ComfyConnectionState);
        Assert.Contains("ComfyUI 未连接，请先在 AI 服务设置中检测并修复 ComfyUI 地址", viewModel.StatusText);
        Assert.Contains("未提交：ComfyUI 未连接", viewModel.ComfySubmitStatusText);
    }

    [Fact]
    public async Task SynthesizeVoicesContinuesUnfinishedQueueWithoutRegenerateConfirmation()
    {
        var client = new CapturingComfyUiClient();
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            client,
            new InMemoryInteractionProjectStore());
        var viewModel = ConfigureFastTts(new MainWindowViewModel(service));
        await OpenTemporaryWorkspaceProjectAsync(viewModel);
        const string voiceId = "unfinished-queue.wav";
        const string referenceText = "unfinished queue reference";
        BindAllRowsToVoice(viewModel, voiceId, referenceText);

        var ttsTasks = viewModel.Project.GenerationTasks.Where(task => task.Type == GenerationTaskType.Tts).ToArray();
        var unfinishedTask = ttsTasks[1];
        var updatedTasks = ttsTasks
            .Select(task => task.Id == unfinishedTask.Id
                ? WithVoice(task, voiceId, referenceText) with { Status = GenerationTaskStatus.Draft, ResultAssetId = null }
                : WithVoice(task, voiceId, referenceText) with
                {
                    Status = GenerationTaskStatus.Completed,
                    ResultAssetId = $"audio_{task.Id}.wav",
                    Parameters = new Dictionary<string, string>(WithVoice(task, voiceId, referenceText).Parameters, StringComparer.Ordinal)
                })
            .ToArray();
        viewModel.Project = TtsSynthesisQueueService.ReplaceTasks(viewModel.Project, updatedTasks);
        var confirmationCount = 0;
        viewModel.ConfirmRegenerateCompletedTtsAsync = _ =>
        {
            confirmationCount++;
            return Task.FromResult(false);
        };

        await viewModel.SynthesizeAllVoicesCommand.ExecuteAsync(null);

        Assert.Equal(0, confirmationCount);
        var submittedIds = client.Requests
            .Where(request => request.Task.Type == GenerationTaskType.Tts)
            .Select(request => request.Task.Id)
            .ToArray();
        Assert.Contains(unfinishedTask.Id, submittedIds);
        Assert.DoesNotContain(ttsTasks[0].Id, submittedIds);
        Assert.Contains(
            viewModel.Project.GenerationTasks,
            task => task.Id == ttsTasks[0].Id &&
                task.Status == GenerationTaskStatus.Completed &&
                task.ResultAssetId == $"audio_{ttsTasks[0].Id}.wav");
    }

    [Fact]
    public async Task SynthesizeVoicesDoesNotOpenRepeatedRegenerationConfirmationWhilePending()
    {
        var client = new CapturingComfyUiClient();
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            client,
            new InMemoryInteractionProjectStore());
        var viewModel = ConfigureFastTts(new MainWindowViewModel(service));
        await OpenTemporaryWorkspaceProjectAsync(viewModel);
        const string voiceId = "pending-confirmation.wav";
        const string referenceText = "pending confirmation reference";
        BindAllRowsToVoice(viewModel, voiceId, referenceText);
        var ttsTasks = viewModel.Project.GenerationTasks.Where(task => task.Type == GenerationTaskType.Tts).ToArray();
        var updatedTasks = ttsTasks
            .Select(task => WithVoice(task, voiceId, referenceText) with
            {
                Status = GenerationTaskStatus.Completed,
                ResultAssetId = $"audio_{task.Id}.wav",
                Parameters = new Dictionary<string, string>(WithVoice(task, voiceId, referenceText).Parameters, StringComparer.Ordinal)
            })
            .ToArray();
        viewModel.Project = TtsSynthesisQueueService.ReplaceTasks(viewModel.Project, updatedTasks);
        var confirmationCount = 0;
        var releaseConfirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.ConfirmRegenerateCompletedTtsAsync = _ =>
        {
            confirmationCount++;
            return releaseConfirmation.Task;
        };

        var first = viewModel.SynthesizeAllVoicesCommand.ExecuteAsync(null);
        await Task.Delay(50);
        await viewModel.SynthesizeAllVoicesCommand.ExecuteAsync(null);

        Assert.Equal(1, confirmationCount);

        releaseConfirmation.SetResult(false);
        await first;
    }

    [Fact]
    public async Task SynthesizeVoicesRegeneratesCompletedTasksWhenConfirmed()
    {
        var client = new CapturingComfyUiClient();
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            client,
            new InMemoryInteractionProjectStore());
        var viewModel = ConfigureFastTts(new MainWindowViewModel(service));
        await OpenTemporaryWorkspaceProjectAsync(viewModel);
        const string voiceId = "all-completed.wav";
        const string referenceText = "all completed reference";
        BindAllRowsToVoice(viewModel, voiceId, referenceText);
        var ttsTasks = viewModel.Project.GenerationTasks.Where(task => task.Type == GenerationTaskType.Tts).ToArray();
        var updatedTasks = ttsTasks
            .Select(task => WithDefaultVoice(task) with
            {
                Status = GenerationTaskStatus.Completed,
                ResultAssetId = $"audio_{task.Id}.wav",
                Parameters = new Dictionary<string, string>(WithDefaultVoice(task).Parameters, StringComparer.Ordinal)
                {
                    [TtsSynthesisQueueService.PromptIdParameter] = $"old_prompt_{task.Id}"
                }
            })
            .ToArray();
        viewModel.Project = TtsSynthesisQueueService.ReplaceTasks(viewModel.Project, updatedTasks);
        viewModel.ConfirmRegenerateCompletedTtsAsync = _ => Task.FromResult(true);

        await viewModel.SynthesizeAllVoicesCommand.ExecuteAsync(null);

        var submittedIds = client.Requests
            .Where(request => request.Task.Type == GenerationTaskType.Tts)
            .Select(request => request.Task.Id)
            .ToArray();
        Assert.Equal(ttsTasks.Select(task => task.Id).OrderBy(id => id, StringComparer.Ordinal), submittedIds);
        Assert.DoesNotContain(
            client.Requests.Single(request => request.Task.Id == ttsTasks[0].Id).Task.Parameters.Keys,
                key => key == TtsSynthesisQueueService.PromptIdParameter);
    }

    [Fact]
    public async Task SynthesizeVoicesPromptsWhenAllVoicesAreCompletedAndStopsWhenDeclined()
    {
        var client = new CapturingComfyUiClient();
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            client,
            new InMemoryInteractionProjectStore());
        var viewModel = ConfigureFastTts(new MainWindowViewModel(service));
        await OpenTemporaryWorkspaceProjectAsync(viewModel);
        const string voiceId = "all-completed.wav";
        const string referenceText = "all completed reference";
        BindAllRowsToVoice(viewModel, voiceId, referenceText);
        var ttsTasks = viewModel.Project.GenerationTasks.Where(task => task.Type == GenerationTaskType.Tts).ToArray();
        var completedTasks = ttsTasks
            .Select(task => task with
            {
                Status = GenerationTaskStatus.Completed,
                ResultAssetId = $"audio_{task.Id}.wav",
                Parameters = new Dictionary<string, string>(WithVoice(task, voiceId, referenceText).Parameters, StringComparer.Ordinal)
            })
            .ToArray();
        viewModel.Project = TtsSynthesisQueueService.ReplaceTasks(viewModel.Project, completedTasks);
        var confirmationCount = 0;
        viewModel.ConfirmRegenerateCompletedTtsAsync = confirmation =>
        {
            confirmationCount++;
            Assert.Equal(ttsTasks.Length, confirmation.CompletedCount);
            Assert.Equal(0, confirmation.UnfinishedCount);
            Assert.Equal(0, confirmation.FailedCount);
            return Task.FromResult(false);
        };

        await viewModel.SynthesizeAllVoicesCommand.ExecuteAsync(null);

        Assert.Equal(1, confirmationCount);
        Assert.Empty(client.Requests);
        Assert.Equal(0, client.ConnectionCheckCount);
        Assert.Equal(ttsTasks.Length, viewModel.TtsCompletedCount);
        Assert.Contains("未重新生成", viewModel.StatusText);
    }

    [Fact]
    public void EditingRowTextAndSubjectIsProjectedThroughWorkbenchDocument()
    {
        var viewModel = CreateViewModel();
        var row = viewModel.SceneTables[0].Rows[0];

        row.LineText = "Updated line";
        row.SubjectName = "Chen";

        var document = viewModel.WorkbenchDocument;
        var rowDraft = document.RowsById[row.InteractionId];
        Assert.Equal("Updated line", rowDraft.Text);
        Assert.Equal("Chen", rowDraft.SubjectName);
    }

    [Fact]
    public void EditingRemovedRowDoesNotMutateWorkbenchDocument()
    {
        var viewModel = CreateViewModel();
        var row = viewModel.SceneTables[0].Rows[0];
        var interactionId = row.InteractionId;

        viewModel.RemoveInteractionRowCommand.Execute(row);
        row.LineText = "Detached row edit";

        Assert.False(viewModel.WorkbenchDocument.RowsById.ContainsKey(interactionId));
        Assert.DoesNotContain(
            viewModel.WorkbenchDocument.Scenes.SelectMany(scene => scene.Rows),
            draft => draft.Text == "Detached row edit");
    }

    [Fact]
    public void EditingRemovedSceneDoesNotMutateWorkbenchDocument()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var removedScene = viewModel.SceneTables.Last();
        viewModel.AddDialogueRowCommand.Execute(removedScene);
        var removedRow = removedScene.Rows.Single();
        var sceneId = removedScene.SceneId;
        var rowId = removedRow.InteractionId;

        viewModel.RemoveSceneCommand.Execute(removedScene);
        removedScene.SceneName = "Detached scene edit";
        removedRow.LineText = "Detached row edit";

        Assert.DoesNotContain(viewModel.WorkbenchDocument.Scenes, scene => scene.SceneId == sceneId);
        Assert.DoesNotContain(viewModel.WorkbenchDocument.RowsById.Keys, id => id == rowId);
        Assert.DoesNotContain(viewModel.WorkbenchDocument.Scenes, scene => scene.Name == "Detached scene edit");
        Assert.DoesNotContain(
            viewModel.WorkbenchDocument.Scenes.SelectMany(scene => scene.Rows),
            row => row.Text == "Detached row edit");
    }

    [Fact]
    public void EditingRowEmotionIsProjectedThroughWorkbenchDocument()
    {
        var viewModel = CreateViewModel();
        var scene = viewModel.SceneTables[0];
        viewModel.AddDialogueRowCommand.Execute(scene);
        var row = viewModel.SelectedRow!;

        row.EmotionAlpha = 0.75;
        row.EmotionVectors.Single(vector => vector.Key == "Calm").Value = 0;
        row.EmotionVectors.Single(vector => vector.Key == "Afraid").Value = 0.9;

        var document = viewModel.WorkbenchDocument;
        var rowDraft = document.RowsById[row.InteractionId];
        Assert.Equal(0.75, rowDraft.EmotionAlpha);
        Assert.NotNull(rowDraft.EmotionVectors);
        Assert.Equal(0.9, rowDraft.EmotionVectors!["Afraid"]);
        Assert.Contains("Afraid", rowDraft.Emotion);
    }

    [Fact]
    public void ConsecutiveRowEmotionEditsUseSingleUndoStep()
    {
        var viewModel = CreateViewModel();
        var row = viewModel.SceneTables[0].Rows[0];

        row.EmotionVectors.Single(vector => vector.Key == "Calm").Value = 0.2;
        viewModel.EndWorkbenchEditCoalescing();
        var baselineEmotion = row.Emotion;
        var baselineCalm = row.EmotionVectorMap["Calm"];
        var baselineAfraid = row.EmotionVectorMap["Afraid"];

        row.EmotionVectors.Single(vector => vector.Key == "Calm").Value = 0.4;
        row.EmotionVectors.Single(vector => vector.Key == "Afraid").Value = 0.9;

        Assert.True(viewModel.CanUndoWorkbench);
        Assert.Equal(WorkbenchSessionOperationNames.UpdateRowEmotion, viewModel.NextUndoWorkbenchOperationName);
        Assert.Equal(0.9, viewModel.WorkbenchDocument.RowsById[row.InteractionId].EmotionVectors!["Afraid"]);

        viewModel.UndoWorkbenchCommand.Execute(null);

        Assert.True(viewModel.CanUndoWorkbench);
        Assert.True(viewModel.CanRedoWorkbench);
        Assert.Equal(baselineEmotion, row.Emotion);
        Assert.Equal(baselineCalm, row.EmotionVectorMap["Calm"]);
        Assert.Equal(baselineAfraid, row.EmotionVectorMap["Afraid"]);

        viewModel.RedoWorkbenchCommand.Execute(null);

        Assert.True(viewModel.CanUndoWorkbench);
        Assert.False(viewModel.CanRedoWorkbench);
        Assert.Equal(0.4, row.EmotionVectorMap["Calm"]);
        Assert.Equal(0.9, row.EmotionVectorMap["Afraid"]);
    }

    [Fact]
    public void EndingWorkbenchEditCoalescingSplitsRowEmotionUndoSteps()
    {
        var viewModel = CreateViewModel();
        var row = viewModel.SceneTables[0].Rows[0];

        row.EmotionVectors.Single(vector => vector.Key == "Calm").Value = 0.2;
        viewModel.EndWorkbenchEditCoalescing();
        var baselineCalm = row.EmotionVectorMap["Calm"];

        row.EmotionVectors.Single(vector => vector.Key == "Calm").Value = 0.4;
        viewModel.EndWorkbenchEditCoalescing();
        row.EmotionVectors.Single(vector => vector.Key == "Calm").Value = 0.6;

        viewModel.UndoWorkbenchCommand.Execute(null);

        Assert.True(viewModel.CanUndoWorkbench);
        Assert.Equal(0.4, row.EmotionVectorMap["Calm"]);

        viewModel.UndoWorkbenchCommand.Execute(null);

        Assert.True(viewModel.CanUndoWorkbench);
        Assert.Equal(baselineCalm, row.EmotionVectorMap["Calm"]);
    }

    [Fact]
    public void EditingSceneAssetIsProjectedThroughWorkbenchDocument()
    {
        var viewModel = CreateViewModel();
        var scene = viewModel.SceneTables[0];

        scene.SelectedSceneAsset = "asset_scene_001";

        var document = viewModel.WorkbenchDocument;
        var sceneDraft = document.Scenes.Single(draft => draft.SceneId == scene.SceneId);
        Assert.Equal("asset_scene_001", scene.SelectedSceneAsset);
        Assert.Equal("asset_scene_001", sceneDraft.VisualBinding?.EffectiveAssetId);
    }

    [Fact]
    public void EditingRowBindingIsProjectedThroughWorkbenchDocument()
    {
        var viewModel = CreateViewModel();
        var row = viewModel.SceneTables[0].Rows[0];

        row.SelectedCharacterAsset = "asset_character_chen";
        row.SelectedVoiceReference = "chen.wav";
        row.SelectedVoiceReferenceText = "Chen sample";

        var document = viewModel.WorkbenchDocument;
        var rowDraft = document.RowsById[row.InteractionId];
        Assert.Equal("asset_character_chen", row.SelectedCharacterAsset);
        Assert.Equal("chen.wav", row.SelectedVoiceReference);
        Assert.Equal("Chen sample", row.SelectedVoiceReferenceText);
        Assert.Equal("asset_character_chen", rowDraft.CharacterVisualBinding?.EffectiveAssetId);
        Assert.Equal("chen.wav", rowDraft.VoiceBinding?.EffectiveVoiceReferenceId);
        Assert.Equal("Chen sample", rowDraft.VoiceBinding?.EffectiveReferenceText);
    }

    [Fact]
    public void EditingBindingPlaceholdersIsNormalizedBeforeDocumentProjection()
    {
        var viewModel = CreateViewModel();
        var scene = viewModel.SceneTables[0];
        var row = scene.Rows.First(row => !row.IsNarration);

        scene.SelectedSceneAsset = "未绑定图片";
        row.SelectedCharacterAsset = "未绑定图片";
        row.SelectedVoiceReference = "未绑定声音";
        row.SelectedVoiceReferenceText = "未绑定";

        var document = viewModel.WorkbenchDocument;
        var sceneDraft = document.Scenes.Single(draft => draft.SceneId == scene.SceneId);
        var rowDraft = document.RowsById[row.InteractionId];
        Assert.Equal("未绑定", scene.SelectedSceneAsset);
        Assert.Equal("未绑定", row.SelectedCharacterAsset);
        Assert.Equal("未绑定", row.SelectedVoiceReference);
        Assert.Equal(string.Empty, row.SelectedVoiceReferenceText);
        Assert.Null(sceneDraft.VisualBinding?.EffectiveAssetId);
        Assert.Null(rowDraft.CharacterVisualBinding?.EffectiveAssetId);
        Assert.Null(rowDraft.VoiceBinding?.EffectiveVoiceReferenceId);
        Assert.Null(rowDraft.VoiceBinding?.EffectiveReferenceText);
    }

    [Fact]
    public void AddNarrationRowAppendsWhenSelectedRowIsInAnotherScene()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        viewModel.AddSceneCommand.Execute(null);
        var firstScene = viewModel.SceneTables[^2];
        var secondScene = viewModel.SceneTables[^1];
        viewModel.AddDialogueRowCommand.Execute(firstScene);
        var selected = firstScene.Rows[0];
        viewModel.SelectRowCommand.Execute(selected);

        viewModel.AddNarrationRowCommand.Execute(secondScene);

        Assert.Single(secondScene.Rows);
        Assert.True(secondScene.Rows[0].IsNarration);
        Assert.Equal(1, secondScene.Rows[0].Order);
        Assert.Same(secondScene.Rows[0], viewModel.SelectedRow);
        Assert.Same(selected, firstScene.Rows[0]);
    }

    [Fact]
    public void AddNarrationRowInsertsBeforeSelectedRowInSameScene()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var scene = viewModel.SceneTables.Last();
        viewModel.AddDialogueRowCommand.Execute(scene);
        var selected = scene.Rows.Single();
        viewModel.SelectRowCommand.Execute(selected);

        viewModel.AddNarrationRowCommand.Execute(scene);

        Assert.Equal(2, scene.Rows.Count);
        Assert.True(scene.Rows[0].IsNarration);
        Assert.Same(selected, scene.Rows[1]);
        Assert.Equal(Enumerable.Range(1, scene.Rows.Count), scene.Rows.Select(row => row.Order));
        Assert.Same(scene.Rows[0], viewModel.SelectedRow);
    }

    [Fact]
    public void MoveInteractionRowMovesRowBeforeTargetAndRenumbersRows()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var scene = viewModel.SceneTables.Last();
        viewModel.AddDialogueRowCommand.Execute(scene);
        viewModel.SelectRowCommand.Execute(null);
        viewModel.AddDialogueRowCommand.Execute(scene);
        var first = scene.Rows[0];
        var second = scene.Rows[1];

        viewModel.MoveInteractionRow(second, first);

        Assert.Same(second, scene.Rows[0]);
        Assert.Same(first, scene.Rows[1]);
        Assert.Equal(1, second.Order);
        Assert.Equal(2, first.Order);
        Assert.Same(second, viewModel.SelectedRow);
    }

    [Fact]
    public void MoveInteractionRowDownPlacesDraggedRowAfterTarget()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var scene = viewModel.SceneTables.Last();
        viewModel.AddDialogueRowCommand.Execute(scene);
        viewModel.SelectRowCommand.Execute(null);
        viewModel.AddDialogueRowCommand.Execute(scene);
        var first = scene.Rows[0];
        var second = scene.Rows[1];

        viewModel.MoveInteractionRow(first, second);

        Assert.Same(second, scene.Rows[0]);
        Assert.Same(first, scene.Rows[1]);
        Assert.Equal(1, second.Order);
        Assert.Equal(2, first.Order);
        Assert.Same(first, viewModel.SelectedRow);
    }

    [Fact]
    public void MoveInteractionRowCanMoveRowsAcrossScenes()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var sourceScene = viewModel.SceneTables[0];
        var targetScene = viewModel.SceneTables[1];
        viewModel.AddDialogueRowCommand.Execute(targetScene);
        var dragged = sourceScene.Rows[0];
        var target = targetScene.Rows[0];
        var sourceInitialCount = sourceScene.Rows.Count;

        viewModel.MoveInteractionRow(dragged, target);

        Assert.Equal(sourceInitialCount - 1, sourceScene.Rows.Count);
        Assert.Same(dragged, targetScene.Rows[0]);
        Assert.Same(target, targetScene.Rows[1]);
        Assert.Equal(Enumerable.Range(1, sourceScene.Rows.Count), sourceScene.Rows.Select(row => row.Order));
        Assert.Equal(Enumerable.Range(1, targetScene.Rows.Count), targetScene.Rows.Select(row => row.Order));
        Assert.Same(dragged, viewModel.SelectedRow);
    }

    [Fact]
    public void MoveInteractionRowAcrossScenesKeepsRowInstanceAndCharacter()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var sourceScene = viewModel.SceneTables[0];
        var targetScene = viewModel.SceneTables[1];
        viewModel.AddDialogueRowCommand.Execute(targetScene);
        var dragged = sourceScene.Rows[0];
        dragged.SubjectName = "Chen";
        var interactionId = dragged.InteractionId;

        viewModel.MoveInteractionRow(dragged, targetScene.Rows[0]);

        Assert.Same(dragged, targetScene.Rows[0]);
        Assert.Equal(interactionId, targetScene.Rows[0].InteractionId);
        Assert.Equal("Chen", targetScene.Rows[0].SubjectName);
    }

    [Fact]
    public void MoveInteractionRowToEmptySceneEndPreservesRowStateAndDocumentDraft()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var sourceScene = viewModel.SceneTables[0];
        var targetScene = viewModel.SceneTables.Last();
        var dragged = sourceScene.Rows[0];
        dragged.SubjectName = "Chen";
        dragged.LineText = "keep this line";
        dragged.SelectedCharacterAsset = "characters/chen.png";
        dragged.SelectedVoiceReference = "chen.wav";
        dragged.SelectedVoiceReferenceText = "reference text";
        dragged.Emotion = "Calm 0.6";
        dragged.EmotionAlpha = 0.75;
        var calm = dragged.EmotionVectors.Single(vector => vector.Key == "Calm");
        calm.Value = 0.75;
        var expectedEmotion = dragged.Emotion;
        var expectedEmotionAlpha = dragged.EmotionAlpha;
        var expectedCalm = dragged.EmotionVectorMap["Calm"];

        viewModel.MoveInteractionRowToSceneEnd(dragged, targetScene);

        var moved = targetScene.Rows.Single(row => row.InteractionId == dragged.InteractionId);
        var draft = viewModel.WorkbenchDocument.RowsById[dragged.InteractionId];
        Assert.Same(dragged, moved);
        Assert.DoesNotContain(sourceScene.Rows, row => row.InteractionId == dragged.InteractionId);
        Assert.Equal(1, moved.Order);
        Assert.Equal("Chen", moved.SubjectName);
        Assert.Equal("keep this line", moved.LineText);
        Assert.Equal("characters/chen.png", moved.SelectedCharacterAsset);
        Assert.Equal("chen.wav", moved.SelectedVoiceReference);
        Assert.Equal("reference text", moved.SelectedVoiceReferenceText);
        Assert.Equal(expectedEmotion, moved.Emotion);
        Assert.Equal(expectedEmotionAlpha, moved.EmotionAlpha);
        Assert.Equal(expectedCalm, moved.EmotionVectorMap["Calm"]);
        Assert.Equal(targetScene.SceneId, draft.SceneId);
        Assert.Equal("Chen", draft.SubjectName);
        Assert.Equal("keep this line", draft.Text);
        Assert.Equal("characters/chen.png", draft.CharacterVisualBinding?.EffectiveAssetId);
        Assert.Equal("chen.wav", draft.VoiceBinding?.EffectiveVoiceReferenceId);
        Assert.Equal("reference text", draft.VoiceBinding?.EffectiveReferenceText);
        Assert.Equal(expectedEmotion, draft.Emotion);
        Assert.Equal(expectedEmotionAlpha, draft.EmotionAlpha);
        Assert.Equal(expectedCalm, draft.EmotionVectors!["Calm"]);
        Assert.Equal(dragged.InteractionId, viewModel.SelectedRow?.InteractionId);
    }

    [Fact]
    public void MoveInteractionRowCommandDoesNotNotifyUnaffectedSceneOrRows()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        viewModel.AddSceneCommand.Execute(null);
        var sourceScene = viewModel.SceneTables[0];
        var targetScene = viewModel.SceneTables[1];
        var unrelatedScene = viewModel.SceneTables[2];
        viewModel.AddDialogueRowCommand.Execute(targetScene);
        viewModel.AddDialogueRowCommand.Execute(unrelatedScene);
        var dragged = sourceScene.Rows[0];
        var target = targetScene.Rows[0];
        viewModel.SelectRowCommand.Execute(dragged);
        var unrelatedRows = unrelatedScene.Rows.ToArray();
        var sceneNotifications = new List<string>();
        var rowNotifications = new List<string>();
        unrelatedScene.PropertyChanged += (_, args) => AddNotification(sceneNotifications, args.PropertyName);
        foreach (var row in unrelatedRows)
        {
            row.PropertyChanged += (_, args) => AddNotification(rowNotifications, args.PropertyName);
        }

        viewModel.MoveInteractionRow(dragged, target);

        Assert.Equal(unrelatedRows, unrelatedScene.Rows);
        Assert.Empty(sceneNotifications);
        Assert.Empty(rowNotifications);
    }

    [Fact]
    public void MoveInteractionRowAcrossScenesDoesNotRefreshBindingTargets()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var sourceScene = viewModel.SceneTables[0];
        var targetScene = viewModel.SceneTables[1];
        viewModel.AddDialogueRowCommand.Execute(targetScene);
        var dragged = sourceScene.Rows[0];
        var target = targetScene.Rows[0];
        var bindingTarget = viewModel.BindingTargets.First(binding => binding.Kind == BindingTargetKind.Character);
        var collectionChanges = 0;
        var targetNotifications = new List<string>();
        viewModel.BindingTargets.CollectionChanged += (_, _) => collectionChanges++;
        bindingTarget.PropertyChanged += (_, args) => AddNotification(targetNotifications, args.PropertyName);

        viewModel.MoveInteractionRow(dragged, target);

        Assert.Equal(0, collectionChanges);
        Assert.All(targetNotifications, notification => Assert.Contains(
            notification,
            new[] { nameof(BindingTargetViewModel.RowVisualDetails), nameof(BindingTargetViewModel.HasRowVisualDetails) }));
        Assert.Same(dragged, targetScene.Rows[0]);
    }

    [Fact]
    public void SelectingRowUpdatesCurrentCharacterRowVisualDetailMarker()
    {
        var viewModel = CreateViewModel();
        var row = viewModel.SelectedRow!;
        var bindingTarget = viewModel.BindingTargets.First(binding =>
            binding.Kind == BindingTargetKind.Character &&
            binding.RowVisualDetails.Any(detail => detail.InteractionId == row.InteractionId));

        Assert.True(bindingTarget.RowVisualDetails.First(detail => detail.InteractionId == row.InteractionId).IsCurrentRow);

        viewModel.SelectRowCommand.Execute(null);

        Assert.False(bindingTarget.RowVisualDetails.First(detail => detail.InteractionId == row.InteractionId).IsCurrentRow);
    }

    [Fact]
    public void PreviewMoveInteractionRowReordersImmediatelyAndDefersRowRenumbering()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var scene = viewModel.SceneTables.Last();
        viewModel.AddDialogueRowCommand.Execute(scene);
        viewModel.SelectRowCommand.Execute(null);
        viewModel.AddDialogueRowCommand.Execute(scene);
        var first = scene.Rows[0];
        var second = scene.Rows[1];
        var firstNotifications = new List<string>();
        var secondNotifications = new List<string>();
        first.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                firstNotifications.Add(args.PropertyName);
            }
        };
        second.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                secondNotifications.Add(args.PropertyName);
            }
        };

        var moved = viewModel.PreviewMoveInteractionRow(first, second);

        Assert.True(moved);
        Assert.Same(second, scene.Rows[0]);
        Assert.Same(first, scene.Rows[1]);
        Assert.Equal(2, second.Order);
        Assert.Equal(1, first.Order);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.Order), firstNotifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.OrderLabel), firstNotifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.Order), secondNotifications);
        Assert.DoesNotContain(nameof(AnalysisResultRowViewModel.OrderLabel), secondNotifications);
        Assert.Same(first, viewModel.SelectedRow);
    }

    [Fact]
    public void PreviewMoveInteractionRowAfterTargetUsesExplicitDropPosition()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var scene = viewModel.SceneTables.Last();
        viewModel.AddDialogueRowCommand.Execute(scene);
        viewModel.SelectRowCommand.Execute(null);
        viewModel.AddDialogueRowCommand.Execute(scene);
        var first = scene.Rows[0];
        var second = scene.Rows[1];

        var moved = viewModel.PreviewMoveInteractionRow(first, second, WorkbenchDropPosition.After);

        Assert.True(moved);
        Assert.Same(second, scene.Rows[0]);
        Assert.Same(first, scene.Rows[1]);
    }

    [Fact]
    public void MoveInteractionRowAfterTargetCommitsExplicitDropPositionOnce()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var scene = viewModel.SceneTables.Last();
        viewModel.AddDialogueRowCommand.Execute(scene);
        viewModel.SelectRowCommand.Execute(null);
        viewModel.AddDialogueRowCommand.Execute(scene);
        var first = scene.Rows[0];
        var second = scene.Rows[1];

        viewModel.MoveInteractionRow(first, second, WorkbenchDropPosition.After);

        Assert.Equal([second.InteractionId, first.InteractionId], scene.Rows.Select(row => row.InteractionId));
        Assert.Equal([second.InteractionId, first.InteractionId], viewModel.WorkbenchDocument.Scenes.Last().Rows.Select(row => row.InteractionId));
        Assert.True(viewModel.IsWorkbenchDirty);
        Assert.True(viewModel.CanUndoWorkbench);
        Assert.Same(first, viewModel.SelectedRow);
    }

    [Fact]
    public void PreviewMoveInteractionRowCanContinueAcrossTargetsAfterPreview()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var scene = viewModel.SceneTables.Last();
        viewModel.AddDialogueRowCommand.Execute(scene);
        viewModel.SelectRowCommand.Execute(null);
        viewModel.AddDialogueRowCommand.Execute(scene);
        viewModel.SelectRowCommand.Execute(null);
        viewModel.AddDialogueRowCommand.Execute(scene);
        var first = scene.Rows[0];
        var second = scene.Rows[1];
        var third = scene.Rows[2];

        Assert.True(viewModel.PreviewMoveInteractionRow(first, second, WorkbenchDropPosition.After));
        Assert.True(viewModel.PreviewMoveInteractionRow(first, third, WorkbenchDropPosition.After));

        Assert.Equal(
            [second.InteractionId, third.InteractionId, first.InteractionId],
            scene.Rows.Select(row => row.InteractionId));
        Assert.Equal([2, 3, 1], scene.Rows.Select(row => row.Order));
        Assert.Same(first, viewModel.SelectedRow);
    }

    [Fact]
    public void PreviewMoveInteractionRowCanMoveBackUpAfterMovingDown()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var scene = viewModel.SceneTables.Last();
        viewModel.AddDialogueRowCommand.Execute(scene);
        viewModel.SelectRowCommand.Execute(null);
        viewModel.AddDialogueRowCommand.Execute(scene);
        viewModel.SelectRowCommand.Execute(null);
        viewModel.AddDialogueRowCommand.Execute(scene);
        var first = scene.Rows[0];
        var second = scene.Rows[1];
        var third = scene.Rows[2];

        viewModel.BeginWorkbenchDrag();
        Assert.True(viewModel.PreviewMoveInteractionRow(first, third, WorkbenchDropPosition.After));
        Assert.True(viewModel.PreviewMoveInteractionRow(first, second, WorkbenchDropPosition.Before));

        Assert.Equal(
            [first.InteractionId, second.InteractionId, third.InteractionId],
            scene.Rows.Select(row => row.InteractionId));
        Assert.Same(first, viewModel.SelectedRow);
    }

    [Fact]
    public void PreviewMoveInteractionRowSkipsRepeatedTargetIntent()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var scene = viewModel.SceneTables.Last();
        viewModel.AddDialogueRowCommand.Execute(scene);
        viewModel.SelectRowCommand.Execute(null);
        viewModel.AddDialogueRowCommand.Execute(scene);
        viewModel.SelectRowCommand.Execute(null);
        viewModel.AddDialogueRowCommand.Execute(scene);
        var first = scene.Rows[0];
        var second = scene.Rows[1];

        viewModel.BeginWorkbenchDrag();
        Assert.True(viewModel.PreviewMoveInteractionRow(first, second, WorkbenchDropPosition.After));
        var documentAfterFirstPreview = viewModel.WorkbenchDocument;
        var orderAfterFirstPreview = scene.Rows.Select(row => row.InteractionId).ToArray();

        Assert.False(viewModel.PreviewMoveInteractionRow(first, second, WorkbenchDropPosition.After));

        Assert.Same(documentAfterFirstPreview, viewModel.WorkbenchDocument);
        Assert.Equal(orderAfterFirstPreview, scene.Rows.Select(row => row.InteractionId));
        Assert.Same(first, viewModel.SelectedRow);
        Assert.Equal(2, viewModel.WorkbenchDragPreviewMetrics.PreviewAttemptCount);
        Assert.Equal(1, viewModel.WorkbenchDragPreviewMetrics.DistinctPreviewIntentCount);
        Assert.Equal(1, viewModel.WorkbenchDragPreviewMetrics.SkippedRepeatedPreviewCount);
        Assert.Equal(0, viewModel.WorkbenchDragPreviewMetrics.SkippedNoOpPreviewCount);
        Assert.Equal(1, viewModel.WorkbenchDragPreviewMetrics.AppliedPreviewCount);
    }

    [Fact]
    public void PreviewMoveInteractionRowBeforeCurrentNextRowIsNoOp()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var scene = viewModel.SceneTables.Last();
        viewModel.AddDialogueRowCommand.Execute(scene);
        viewModel.SelectRowCommand.Execute(null);
        viewModel.AddDialogueRowCommand.Execute(scene);
        var first = scene.Rows[0];
        var second = scene.Rows[1];

        viewModel.BeginWorkbenchDrag();
        var moved = viewModel.PreviewMoveInteractionRow(first, second, WorkbenchDropPosition.Before);

        Assert.False(moved);
        Assert.Same(first, scene.Rows[0]);
        Assert.Same(second, scene.Rows[1]);
        Assert.Equal(1, viewModel.WorkbenchDragPreviewMetrics.SkippedNoOpPreviewCount);
    }

    [Fact]
    public void PreviewMoveInteractionRowCanMoveUpBeforeTarget()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var scene = viewModel.SceneTables.Last();
        viewModel.AddDialogueRowCommand.Execute(scene);
        viewModel.SelectRowCommand.Execute(null);
        viewModel.AddDialogueRowCommand.Execute(scene);
        viewModel.SelectRowCommand.Execute(null);
        viewModel.AddDialogueRowCommand.Execute(scene);
        var first = scene.Rows[0];
        var second = scene.Rows[1];
        var third = scene.Rows[2];

        Assert.True(viewModel.PreviewMoveInteractionRow(third, first, WorkbenchDropPosition.Before));

        Assert.Equal(
            [third.InteractionId, first.InteractionId, second.InteractionId],
            scene.Rows.Select(row => row.InteractionId));
        Assert.Equal([3, 1, 2], scene.Rows.Select(row => row.Order));
    }

    [Fact]
    public void PreviewMoveInteractionRowAfterCurrentPreviousRowIsNoOp()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var scene = viewModel.SceneTables.Last();
        viewModel.AddDialogueRowCommand.Execute(scene);
        viewModel.SelectRowCommand.Execute(null);
        viewModel.AddDialogueRowCommand.Execute(scene);
        var first = scene.Rows[0];
        var second = scene.Rows[1];

        var moved = viewModel.PreviewMoveInteractionRow(second, first, WorkbenchDropPosition.After);

        Assert.False(moved);
        Assert.Same(first, scene.Rows[0]);
        Assert.Same(second, scene.Rows[1]);
    }

    [Fact]
    public void PreviewMoveInteractionRowToSceneEndSupportsEmptyTargetScene()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var sourceScene = viewModel.SceneTables[0];
        var emptyScene = viewModel.SceneTables.Last();
        var dragged = sourceScene.Rows[0];

        var moved = viewModel.PreviewMoveInteractionRowToSceneEnd(dragged, emptyScene);

        Assert.True(moved);
        Assert.DoesNotContain(dragged, sourceScene.Rows);
        Assert.Same(dragged, emptyScene.Rows.Single());
        Assert.Equal(Enumerable.Range(2, sourceScene.Rows.Count), sourceScene.Rows.Select(row => row.Order));
        Assert.Equal(1, dragged.Order);
        Assert.Same(dragged, viewModel.SelectedRow);
    }

    [Fact]
    public void PreviewMoveInteractionRowToSceneEndSkipsRepeatedTargetIntent()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var sourceScene = viewModel.SceneTables[0];
        var emptyScene = viewModel.SceneTables.Last();
        var dragged = sourceScene.Rows[0];

        viewModel.BeginWorkbenchDrag();
        Assert.True(viewModel.PreviewMoveInteractionRowToSceneEnd(dragged, emptyScene));
        var documentAfterFirstPreview = viewModel.WorkbenchDocument;

        Assert.False(viewModel.PreviewMoveInteractionRowToSceneEnd(dragged, emptyScene));

        Assert.Same(documentAfterFirstPreview, viewModel.WorkbenchDocument);
        Assert.DoesNotContain(dragged, sourceScene.Rows);
        Assert.Same(dragged, emptyScene.Rows.Single());
    }

    [Fact]
    public void MoveInteractionRowToSceneEndCommitsWithoutPreviewReorder()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var sourceScene = viewModel.SceneTables[0];
        var emptyScene = viewModel.SceneTables.Last();
        var dragged = sourceScene.Rows[0];
        var sourceInitialCount = sourceScene.Rows.Count;

        viewModel.MoveInteractionRowToSceneEnd(dragged, emptyScene);

        Assert.Equal(sourceInitialCount - 1, sourceScene.Rows.Count);
        Assert.Same(dragged, emptyScene.Rows.Single());
        Assert.Equal(emptyScene.SceneId, viewModel.WorkbenchDocument.RowsById[dragged.InteractionId].SceneId);
        Assert.True(viewModel.IsWorkbenchDirty);
        Assert.True(viewModel.CanUndoWorkbench);
    }

    [Fact]
    public async Task CancelWorkbenchDragRestoresRowPreviewAndDirtyState()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);
        Assert.False(viewModel.IsWorkbenchDirty);
        var sourceScene = viewModel.SceneTables[0];
        var targetScene = viewModel.SceneTables.Last();
        var dragged = sourceScene.Rows[0];

        viewModel.SelectRowCommand.Execute(dragged);
        viewModel.BeginWorkbenchDrag();
        var moved = viewModel.PreviewMoveInteractionRowToSceneEnd(dragged, targetScene);
        Assert.True(moved);
        Assert.Same(dragged, targetScene.Rows.Single());
        Assert.True(viewModel.IsWorkbenchDirty);

        viewModel.CancelWorkbenchDrag();

        Assert.Same(dragged, sourceScene.Rows[0]);
        Assert.Empty(targetScene.Rows);
        Assert.False(viewModel.IsWorkbenchDirty);
        Assert.Same(dragged, viewModel.SelectedRow);
    }

    [Fact]
    public async Task CancelWorkbenchDragRestoresScenePreviewAndDirtyState()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);
        var first = viewModel.SceneTables[0];
        var second = viewModel.SceneTables[1];
        Assert.False(viewModel.IsWorkbenchDirty);

        viewModel.BeginWorkbenchDrag();
        var moved = viewModel.PreviewMoveScene(first, second);
        Assert.True(moved);
        Assert.Same(second, viewModel.SceneTables[0]);

        viewModel.CancelWorkbenchDrag();

        Assert.Same(first, viewModel.SceneTables[0]);
        Assert.Same(second, viewModel.SceneTables[1]);
        Assert.False(viewModel.IsWorkbenchDirty);
    }

    [Fact]
    public async Task CancelWorkbenchDragDoesNotRefreshWorkspaceDerivedBindingTargets()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);
        var first = viewModel.SceneTables[0];
        var second = viewModel.SceneTables[1];
        var initialMetrics = viewModel.WorkbenchBindingRefreshMetrics;

        viewModel.BeginWorkbenchDrag();
        Assert.True(viewModel.PreviewMoveScene(first, second));
        viewModel.CancelWorkbenchDrag();

        Assert.Equal(initialMetrics.FullRefreshCount, viewModel.WorkbenchBindingRefreshMetrics.FullRefreshCount);
        Assert.Equal(
            initialMetrics.FullRefreshReasons.CountFor("workspace_derived_state"),
            viewModel.WorkbenchBindingRefreshMetrics.FullRefreshReasons.CountFor("workspace_derived_state"));
        Assert.Equal(
            viewModel.SceneTables.Select(scene => scene.SceneId),
            viewModel.BindingTargets.Where(target => target.Kind == BindingTargetKind.Scene).Select(target => target.TargetId));
    }

    [Fact]
    public async Task CompletedWorkbenchRowDragCanBeUndoneAsSingleStep()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);
        var sourceScene = viewModel.SceneTables[0];
        var targetScene = viewModel.SceneTables.Last();
        var dragged = sourceScene.Rows[0];
        Assert.False(viewModel.IsWorkbenchDirty);

        viewModel.SelectRowCommand.Execute(dragged);
        viewModel.BeginWorkbenchDrag();
        Assert.True(viewModel.PreviewMoveInteractionRowToSceneEnd(dragged, targetScene));
        viewModel.CompleteWorkbenchDrag();

        Assert.True(viewModel.IsWorkbenchDirty);
        Assert.True(viewModel.CanUndoWorkbench);
        Assert.Same(dragged, targetScene.Rows.Single());
        Assert.Equal(Enumerable.Range(1, sourceScene.Rows.Count), sourceScene.Rows.Select(row => row.Order));
        Assert.Equal(1, dragged.Order);

        viewModel.UndoWorkbenchCommand.Execute(null);

        Assert.False(viewModel.IsWorkbenchDirty);
        Assert.Same(dragged, sourceScene.Rows[0]);
        Assert.Empty(targetScene.Rows);
        Assert.True(viewModel.CanRedoWorkbench);

        viewModel.RedoWorkbenchCommand.Execute(null);

        Assert.True(viewModel.IsWorkbenchDirty);
        Assert.Same(dragged, targetScene.Rows.Single());
        Assert.Equal(Enumerable.Range(1, sourceScene.Rows.Count), sourceScene.Rows.Select(row => row.Order));
        Assert.Equal(1, dragged.Order);
    }

    [Fact]
    public async Task CompletedWorkbenchDragDoesNotRefreshWorkspaceDerivedBindingTargets()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);
        var first = viewModel.SceneTables[0];
        var second = viewModel.SceneTables[1];
        var initialMetrics = viewModel.WorkbenchBindingRefreshMetrics;

        viewModel.BeginWorkbenchDrag();
        Assert.True(viewModel.PreviewMoveScene(first, second));
        viewModel.CompleteWorkbenchDrag();

        Assert.Equal(initialMetrics.FullRefreshCount, viewModel.WorkbenchBindingRefreshMetrics.FullRefreshCount);
        Assert.Equal(
            initialMetrics.FullRefreshReasons.CountFor("workspace_derived_state"),
            viewModel.WorkbenchBindingRefreshMetrics.FullRefreshReasons.CountFor("workspace_derived_state"));
        Assert.Equal(
            viewModel.SceneTables.Select(scene => scene.SceneId),
            viewModel.BindingTargets.Where(target => target.Kind == BindingTargetKind.Scene).Select(target => target.TargetId));
    }

    [Fact]
    public async Task CompletedWorkbenchRowDragDoesNotMoveSceneBindingTargets()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);
        var sourceScene = viewModel.SceneTables[0];
        var targetScene = viewModel.SceneTables.Last();
        var dragged = sourceScene.Rows[0];
        var initialSceneTargets = viewModel.BindingTargets
            .Where(target => target.Kind == BindingTargetKind.Scene)
            .ToArray();
        var collectionChanges = 0;
        viewModel.BindingTargets.CollectionChanged += (_, _) => collectionChanges++;

        viewModel.BeginWorkbenchDrag();
        Assert.True(viewModel.PreviewMoveInteractionRowToSceneEnd(dragged, targetScene));
        viewModel.CompleteWorkbenchDrag();

        Assert.Equal(0, collectionChanges);
        Assert.Equal(initialSceneTargets, viewModel.BindingTargets.Where(target => target.Kind == BindingTargetKind.Scene));
        Assert.Equal(0, viewModel.WorkbenchBindingRefreshMetrics.FallbackFullRefreshCount);
    }

    [Fact]
    public async Task CompletedWorkbenchSceneDragMovesOnlySceneBindingTargets()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);
        var first = viewModel.SceneTables[0];
        var second = viewModel.SceneTables[1];
        var firstTarget = viewModel.BindingTargets.Single(target =>
            target.Kind == BindingTargetKind.Scene &&
            target.TargetId == first.SceneId);
        var secondTarget = viewModel.BindingTargets.Single(target =>
            target.Kind == BindingTargetKind.Scene &&
            target.TargetId == second.SceneId);
        var collectionActions = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        viewModel.BindingTargets.CollectionChanged += (_, args) => collectionActions.Add(args.Action);

        viewModel.BeginWorkbenchDrag();
        Assert.True(viewModel.PreviewMoveScene(first, second));
        viewModel.CompleteWorkbenchDrag();

        Assert.Contains(System.Collections.Specialized.NotifyCollectionChangedAction.Move, collectionActions);
        Assert.DoesNotContain(System.Collections.Specialized.NotifyCollectionChangedAction.Add, collectionActions);
        Assert.DoesNotContain(System.Collections.Specialized.NotifyCollectionChangedAction.Remove, collectionActions);
        Assert.Same(secondTarget, viewModel.BindingTargets[0]);
        Assert.Same(firstTarget, viewModel.BindingTargets[1]);
        Assert.Equal(
            viewModel.SceneTables.Select(scene => scene.SceneId),
            viewModel.BindingTargets.Where(target => target.Kind == BindingTargetKind.Scene).Select(target => target.TargetId));
        Assert.Equal(0, viewModel.WorkbenchBindingRefreshMetrics.FallbackFullRefreshCount);
    }

    [Fact]
    public async Task CompletedWorkbenchRowDragAfterLivePreviewDoesNotRequireDropCommit()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);
        var sourceScene = viewModel.SceneTables[0];
        var targetScene = viewModel.SceneTables.Last();
        var dragged = sourceScene.Rows[0];
        Assert.False(viewModel.IsWorkbenchDirty);

        viewModel.SelectRowCommand.Execute(dragged);
        viewModel.BeginWorkbenchDrag();
        Assert.True(viewModel.PreviewMoveInteractionRowToSceneEnd(dragged, targetScene));
        viewModel.CompleteWorkbenchDrag();

        Assert.True(viewModel.IsWorkbenchDirty);
        Assert.True(viewModel.CanUndoWorkbench);
        Assert.DoesNotContain(dragged, sourceScene.Rows);
        Assert.Same(dragged, targetScene.Rows.Single());

        viewModel.UndoWorkbenchCommand.Execute(null);

        Assert.False(viewModel.IsWorkbenchDirty);
        Assert.Same(dragged, sourceScene.Rows[0]);
        Assert.Empty(targetScene.Rows);
    }

    [Fact]
    public async Task CompletedWorkbenchDropCommitCanBeUndoneWithoutPreviewCheckpoint()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);
        var sourceScene = viewModel.SceneTables[0];
        var targetScene = viewModel.SceneTables.Last();
        var dragged = sourceScene.Rows[0];
        Assert.False(viewModel.IsWorkbenchDirty);

        viewModel.SelectRowCommand.Execute(dragged);
        viewModel.BeginWorkbenchDrag();
        viewModel.MoveInteractionRowToSceneEnd(dragged, targetScene);
        viewModel.CompleteWorkbenchDrag();

        Assert.True(viewModel.IsWorkbenchDirty);
        Assert.True(viewModel.CanUndoWorkbench);
        Assert.Same(dragged, targetScene.Rows.Single());

        viewModel.UndoWorkbenchCommand.Execute(null);

        Assert.False(viewModel.IsWorkbenchDirty);
        Assert.Same(dragged, sourceScene.Rows[0]);
        Assert.Empty(targetScene.Rows);
    }

    [Fact]
    public async Task CancelWorkbenchDragWithoutPreviewKeepsSessionClean()
    {
        var viewModel = CreateViewModel();
        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);
        var selected = viewModel.SelectedRow;
        Assert.False(viewModel.IsWorkbenchDirty);
        Assert.False(viewModel.CanUndoWorkbench);

        viewModel.BeginWorkbenchDrag();
        viewModel.CancelWorkbenchDrag();

        Assert.False(viewModel.IsWorkbenchDirty);
        Assert.False(viewModel.CanUndoWorkbench);
        Assert.Same(selected, viewModel.SelectedRow);
    }

    [Fact]
    public async Task WorkbenchDragAndDirectMovesAreBlockedWhileTtsSynthesisRuns()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);
        var firstScene = viewModel.SceneTables[0];
        var secondScene = viewModel.SceneTables.Last();
        var dragged = firstScene.Rows[0];
        var originalSceneOrder = viewModel.SceneTables.Select(scene => scene.SceneId).ToArray();
        var originalFirstSceneRows = firstScene.Rows.Select(row => row.InteractionId).ToArray();
        var originalSecondSceneRows = secondScene.Rows.Select(row => row.InteractionId).ToArray();

        viewModel.IsTtsSynthesisRunning = true;

        viewModel.BeginWorkbenchDrag();
        Assert.False(viewModel.PreviewMoveScene(firstScene, secondScene));
        Assert.False(viewModel.PreviewMoveInteractionRowToSceneEnd(dragged, secondScene));
        viewModel.MoveScene(firstScene, secondScene);
        viewModel.MoveInteractionRowToSceneEnd(dragged, secondScene);
        viewModel.CompleteWorkbenchDrag();

        Assert.False(viewModel.CanDragWorkbench);
        Assert.Equal(originalSceneOrder, viewModel.SceneTables.Select(scene => scene.SceneId));
        Assert.Equal(originalFirstSceneRows, firstScene.Rows.Select(row => row.InteractionId));
        Assert.Equal(originalSecondSceneRows, secondScene.Rows.Select(row => row.InteractionId));
        Assert.False(viewModel.IsWorkbenchDirty);
        Assert.False(viewModel.CanUndoWorkbench);
    }

    [Fact]
    public async Task WorkbenchDragCompletionRollsBackWhenTtsSynthesisStartsDuringPreview()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);
        var sourceScene = viewModel.SceneTables[0];
        var targetScene = viewModel.SceneTables.Last();
        var dragged = sourceScene.Rows[0];

        viewModel.BeginWorkbenchDrag();
        Assert.True(viewModel.PreviewMoveInteractionRowToSceneEnd(dragged, targetScene));
        Assert.Same(dragged, targetScene.Rows.Single());

        viewModel.IsTtsSynthesisRunning = true;
        viewModel.CompleteWorkbenchDrag();

        Assert.Same(dragged, sourceScene.Rows[0]);
        Assert.Empty(targetScene.Rows);
        Assert.False(viewModel.IsWorkbenchDirty);
        Assert.False(viewModel.CanUndoWorkbench);
        Assert.False(viewModel.CanDragWorkbench);
    }

    [Fact]
    public async Task SynthesizeVoicesDisablesWorkbenchDragWhileRunning()
    {
        var client = new BlockingComfyUiClient();
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            client,
            new InMemoryInteractionProjectStore());
        var viewModel = ConfigureFastTts(new MainWindowViewModel(service));
        await OpenTemporaryWorkspaceProjectAsync(viewModel);
        viewModel.AddSceneCommand.Execute(null);
        BindAllRowsToDefaultVoice(viewModel);
        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);
        var submitTask = viewModel.SynthesizeAllVoicesCommand.ExecuteAsync(null);
        await client.SubmissionStarted.Task;
        var sourceScene = viewModel.SceneTables.First(scene => scene.Rows.Count > 0);
        var targetScene = viewModel.SceneTables.Last();
        var dragged = sourceScene.Rows[0];
        var sourceRows = sourceScene.Rows.Select(row => row.InteractionId).ToArray();
        var targetRows = targetScene.Rows.Select(row => row.InteractionId).ToArray();

        Assert.True(viewModel.IsTtsSynthesisRunning);
        Assert.False(viewModel.CanDragWorkbench);
        viewModel.MoveInteractionRowToSceneEnd(dragged, targetScene);

        Assert.Equal(sourceRows, sourceScene.Rows.Select(row => row.InteractionId));
        Assert.Equal(targetRows, targetScene.Rows.Select(row => row.InteractionId));

        client.ReleaseSubmissions();
        await submitTask;
        Assert.False(viewModel.IsTtsSynthesisRunning);
        Assert.True(viewModel.CanDragWorkbench);
    }

    [Fact]
    public void SynthesizeVoicesLocksWorkbenchDataEditsWhileRunning()
    {
        var viewModel = CreateViewModel();
        var rows = viewModel.SceneTables.SelectMany(scene => scene.Rows).Where(row => !row.IsNarration).Take(2).ToArray();
        Assert.True(rows.Length >= 2);
        var completedRow = rows[0];
        var pendingRow = rows[1];
        var completedOriginalText = completedRow.LineText;
        var completedOriginalEmotion = completedRow.Emotion;
        var pendingOriginalText = pendingRow.LineText;
        var pendingOriginalEmotion = pendingRow.Emotion;
        var completedTask = WithDefaultVoice(new GenerationTask(
            "tts_completed_running_edit",
            GenerationTaskType.Tts,
            completedRow.InteractionId,
            "Completed running edit voice",
            "comfyui",
            GenerationTaskStatus.Completed,
            new Dictionary<string, string>(StringComparer.Ordinal),
            "completed_running_edit.wav"));
        var pendingTask = WithDefaultVoice(new GenerationTask(
            "tts_pending_running_edit",
            GenerationTaskType.Tts,
            pendingRow.InteractionId,
            "Pending running edit voice",
            "comfyui",
            GenerationTaskStatus.Pending,
            new Dictionary<string, string>(StringComparer.Ordinal),
            null));
        viewModel.Project = viewModel.Project with
        {
            Scenes = viewModel.Project.Scenes
                .Select(scene => scene with
                {
                    Interactions = scene.Interactions
                        .Select(interaction =>
                            interaction.Id == completedRow.InteractionId
                                ? interaction with { Tasks = [completedTask] }
                                : interaction.Id == pendingRow.InteractionId
                                    ? interaction with { Tasks = [pendingTask] }
                                    : interaction)
                        .ToArray()
                })
                .ToArray(),
            GenerationTasks = [completedTask, pendingTask]
        };
        completedRow = viewModel.SceneTables.SelectMany(scene => scene.Rows).Single(row => row.InteractionId == completedTask.TargetId);
        pendingRow = viewModel.SceneTables.SelectMany(scene => scene.Rows).Single(row => row.InteractionId == pendingTask.TargetId);

        viewModel.IsTtsSynthesisRunning = true;

        Assert.True(completedRow.CanEditTextAndEmotion);
        Assert.False(pendingRow.CanEditTextAndEmotion);

        completedRow.LineText = "已完成行运行中允许修改文本";
        completedRow.Emotion = "Sad 1.1";
        pendingRow.LineText = "未完成行运行中不可改文本";
        pendingRow.Emotion = "Angry 0.9";

        Assert.Equal("已完成行运行中允许修改文本", completedRow.LineText);
        Assert.Equal("Sad 1.1", completedRow.Emotion);
        Assert.Equal("已完成行运行中允许修改文本", viewModel.WorkbenchDocument.RowsById[completedRow.InteractionId].Text);
        Assert.Equal("Sad 1.1", viewModel.WorkbenchDocument.RowsById[completedRow.InteractionId].Emotion);
        Assert.Equal(pendingOriginalText, pendingRow.LineText);
        Assert.Equal(pendingOriginalEmotion, pendingRow.Emotion);
        Assert.Contains("锁定", viewModel.StatusText, StringComparison.Ordinal);
        Assert.True(viewModel.IsWorkbenchDirty);
    }

    [Fact]
    public async Task SaveWorkbenchEditsCommandDoesNotPersistDataEditsWhileTtsSynthesisRuns()
    {
        var viewModel = CreateViewModel();
        var scene = viewModel.SceneTables[0];
        var row = scene.Rows.First(candidate => !candidate.IsNarration);
        var originalSceneName = scene.SceneName;
        var originalLineText = row.LineText;
        var originalSubjectName = row.SubjectName;
        var originalCharacterAsset = row.SelectedCharacterAsset;

        viewModel.IsTtsSynthesisRunning = true;
        scene.SceneName = "运行中不应保存的场景名";
        row.LineText = "运行中不应保存的台词";
        row.SubjectName = "运行中不应保存的角色";
        row.SelectedCharacterAsset = "asset_running_edit_should_not_persist";

        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);

        Assert.Equal(originalSceneName, scene.SceneName);
        Assert.Equal(originalLineText, row.LineText);
        Assert.Equal(originalSubjectName, row.SubjectName);
        Assert.Equal(originalCharacterAsset, row.SelectedCharacterAsset);
        Assert.False(viewModel.IsWorkbenchDirty);
        Assert.Contains("语音合成正在运行", viewModel.StatusText, StringComparison.Ordinal);

        var projectInteraction = viewModel.Project.Scenes
            .SelectMany(projectScene => projectScene.Interactions)
            .Single(interaction => interaction.Id == row.InteractionId);
        Assert.Equal(originalLineText, projectInteraction.Dialogue?.Text ?? projectInteraction.InnerMonologue?.Text);
    }

    [Fact]
    public async Task CancelTtsSynthesisCommandStopsRunningSubmissionAndRestoresWorkbench()
    {
        var client = new BlockingComfyUiClient();
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            client,
            new InMemoryInteractionProjectStore());
        var viewModel = ConfigureFastTts(new MainWindowViewModel(service));
        await OpenTemporaryWorkspaceProjectAsync(viewModel);
        BindAllRowsToDefaultVoice(viewModel);

        var synthesizeTask = viewModel.SynthesizeAllVoicesCommand.ExecuteAsync(null);
        await client.SubmissionStarted.Task;

        Assert.True(viewModel.IsTtsSynthesisRunning);
        Assert.True(viewModel.CancelTtsSynthesisCommand.CanExecute(null));

        viewModel.CancelTtsSynthesisCommand.Execute(null);
        await synthesizeTask;

        Assert.False(viewModel.IsTtsSynthesisRunning);
        Assert.True(viewModel.CanDragWorkbench);
        Assert.Contains("已取消", viewModel.StatusText);
    }

    [Fact]
    public async Task CancelTtsSynthesisCommandDeletesSubmittedPendingComfyPrompts()
    {
        var client = new QueuedComfyUiClient();
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            client,
            new InMemoryInteractionProjectStore());
        var viewModel = ConfigureFastTts(new MainWindowViewModel(service));
        viewModel.LiveTtsQueueDelayMilliseconds = 50;
        await OpenTemporaryWorkspaceProjectAsync(viewModel);
        BindAllRowsToDefaultVoice(viewModel);

        var synthesizeTask = viewModel.SynthesizeAllVoicesCommand.ExecuteAsync(null);
        await client.SubmissionStarted.Task;
        await client.FirstStatusCheck.Task;

        await viewModel.CancelTtsSynthesisCommand.ExecuteAsync(null);
        await synthesizeTask;

        Assert.Contains(client.PromptIds, promptId => client.DeletedPromptIds.Contains(promptId));
        Assert.Contains("等待队列", viewModel.ComfySubmitStatusText);
        Assert.Contains("已取消", viewModel.StatusText);
        Assert.Contains(
            viewModel.Project.GenerationTasks,
            task => task.Type == GenerationTaskType.Tts &&
                task.Status == GenerationTaskStatus.Draft &&
                !task.Parameters.ContainsKey(TtsSynthesisQueueService.PromptIdParameter) &&
                task.ResultAssetId is null);
    }

    [Fact]
    public void TtsSynthesisActionTextSwitchesToStopWhileRunning()
    {
        var viewModel = CreateViewModel();

        Assert.Equal("语音合成", viewModel.TtsSynthesisActionText);

        viewModel.IsTtsSynthesisRunning = true;

        Assert.Equal("停止合成", viewModel.TtsSynthesisActionText);

        viewModel.IsTtsSynthesisRunning = false;

        Assert.Equal("语音合成", viewModel.TtsSynthesisActionText);
    }

    [Fact]
    public async Task RegenerateRowVoiceDisablesWorkbenchDragWhileRunning()
    {
        var client = new BlockingComfyUiClient();
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            client,
            new InMemoryInteractionProjectStore());
        var viewModel = ConfigureFastTts(new MainWindowViewModel(service));
        await OpenTemporaryWorkspaceProjectAsync(viewModel);
        viewModel.AddSceneCommand.Execute(null);
        BindAllRowsToDefaultVoice(viewModel);
        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);
        var row = viewModel.SceneTables.SelectMany(scene => scene.Rows).First(candidate => !candidate.IsNarration);
        var regenerateTask = viewModel.RegenerateRowVoiceCommand.ExecuteAsync(row);
        await client.SubmissionStarted.Task;
        var sourceScene = viewModel.SceneTables.First(scene => scene.Rows.Any(candidate => candidate.InteractionId == row.InteractionId));
        var targetScene = viewModel.SceneTables.Last();
        var refreshedRow = sourceScene.Rows.First(candidate => candidate.InteractionId == row.InteractionId);
        var sourceRows = sourceScene.Rows.Select(candidate => candidate.InteractionId).ToArray();
        var targetRows = targetScene.Rows.Select(candidate => candidate.InteractionId).ToArray();

        Assert.True(viewModel.IsTtsSynthesisRunning);
        Assert.False(viewModel.CanDragWorkbench);
        viewModel.MoveInteractionRowToSceneEnd(refreshedRow, targetScene);

        Assert.Equal(sourceRows, sourceScene.Rows.Select(candidate => candidate.InteractionId));
        Assert.Equal(targetRows, targetScene.Rows.Select(candidate => candidate.InteractionId));

        client.ReleaseSubmissions();
        await regenerateTask;
        Assert.False(viewModel.IsTtsSynthesisRunning);
        Assert.True(viewModel.CanDragWorkbench);
    }

    [Fact]
    public async Task CompletedWorkbenchSceneDragCanBeUndoneAsSingleStep()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        await viewModel.SaveWorkbenchEditsCommand.ExecuteAsync(null);
        var first = viewModel.SceneTables[0];
        var second = viewModel.SceneTables[1];

        viewModel.BeginWorkbenchDrag();
        Assert.True(viewModel.PreviewMoveScene(first, second));
        viewModel.CompleteWorkbenchDrag();

        Assert.Same(second, viewModel.SceneTables[0]);
        Assert.Same(first, viewModel.SceneTables[1]);
        Assert.True(viewModel.IsWorkbenchDirty);

        viewModel.UndoWorkbenchCommand.Execute(null);

        Assert.Same(first, viewModel.SceneTables[0]);
        Assert.Same(second, viewModel.SceneTables[1]);
        Assert.False(viewModel.IsWorkbenchDirty);
    }

    [Fact]
    public void BlankSubjectNameUpdateDoesNotClearRowCharacterDuringReorder()
    {
        var row = AnalysisResultRowViewModel.CreateManualDialogue(1, "Chen");
        var subjectNameNotifications = 0;
        row.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AnalysisResultRowViewModel.SubjectName))
            {
                subjectNameNotifications++;
            }
        };

        row.SubjectName = null;

        Assert.Equal("Chen", row.SubjectName);
        Assert.Equal("C", row.ObjectPreviewInitial);
        Assert.False(row.IsNarration);
        Assert.Equal(1, subjectNameNotifications);
    }

    [Fact]
    public void MoveSceneMovesSceneBeforeTargetAndRenumbersScenes()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        viewModel.AddSceneCommand.Execute(null);
        var first = viewModel.SceneTables[0];
        var third = viewModel.SceneTables[2];

        viewModel.MoveScene(third, first);

        Assert.Same(third, viewModel.SceneTables[0]);
        Assert.Equal(1, third.Order);
        Assert.Equal(2, first.Order);
        Assert.Equal("场景 1", third.SceneLabel);
        Assert.Equal("场景 2", first.SceneLabel);
    }

    [Fact]
    public void MoveSceneDownPlacesDraggedSceneAfterTargetAndRenumbersScenes()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        viewModel.AddSceneCommand.Execute(null);
        var first = viewModel.SceneTables[0];
        var second = viewModel.SceneTables[1];

        viewModel.MoveScene(first, second);

        Assert.Same(second, viewModel.SceneTables[0]);
        Assert.Same(first, viewModel.SceneTables[1]);
        Assert.Equal(1, second.Order);
        Assert.Equal(2, first.Order);
    }


    [Fact]
    public void PreviewMoveSceneReordersImmediatelyAndDefersSceneRenumbering()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        viewModel.AddSceneCommand.Execute(null);
        var first = viewModel.SceneTables[0];
        var second = viewModel.SceneTables[1];
        var firstNotifications = new List<string>();
        var secondNotifications = new List<string>();
        first.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                firstNotifications.Add(args.PropertyName);
            }
        };
        second.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                secondNotifications.Add(args.PropertyName);
            }
        };

        var moved = viewModel.PreviewMoveScene(first, second);

        Assert.True(moved);
        Assert.Same(second, viewModel.SceneTables[0]);
        Assert.Same(first, viewModel.SceneTables[1]);
        Assert.Equal(2, second.Order);
        Assert.Equal(1, first.Order);
        Assert.DoesNotContain(nameof(SceneTableViewModel.Order), firstNotifications);
        Assert.DoesNotContain(nameof(SceneTableViewModel.SceneLabel), firstNotifications);
        Assert.DoesNotContain(nameof(SceneTableViewModel.Order), secondNotifications);
        Assert.DoesNotContain(nameof(SceneTableViewModel.SceneLabel), secondNotifications);
    }

    [Fact]
    public void PreviewMoveSceneCanContinueAcrossTargetsAfterPreview()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        viewModel.AddSceneCommand.Execute(null);
        var first = viewModel.SceneTables[0];
        var second = viewModel.SceneTables[1];
        var third = viewModel.SceneTables[2];
        var fourth = viewModel.SceneTables[3];

        Assert.True(viewModel.PreviewMoveScene(first, second, WorkbenchDropPosition.After));
        Assert.True(viewModel.PreviewMoveScene(first, third, WorkbenchDropPosition.After));

        Assert.Equal(
            [second.SceneId, third.SceneId, first.SceneId, fourth.SceneId],
            viewModel.SceneTables.Take(4).Select(scene => scene.SceneId));
        Assert.Equal([2, 3, 1, 4], viewModel.SceneTables.Take(4).Select(scene => scene.Order));
    }

    [Fact]
    public void DropIntentDispatcherMovesSceneBackUsingCurrentIndexesAfterPreview()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        viewModel.AddSceneCommand.Execute(null);
        var first = viewModel.SceneTables[0];
        var second = viewModel.SceneTables[1];
        var third = viewModel.SceneTables[2];
        var firstIntent = new WorkbenchDropIntent(
            WorkbenchDropIntentKind.SceneToScene,
            second,
            WorkbenchDropPosition.After);
        var secondIntent = new WorkbenchDropIntent(
            WorkbenchDropIntentKind.SceneToScene,
            second,
            WorkbenchDropPosition.After);

        viewModel.BeginWorkbenchDrag();
        Assert.True(WorkbenchDropIntentDispatcher.Preview(viewModel, firstIntent, null, first));
        Assert.True(WorkbenchDropIntentDispatcher.Preview(viewModel, secondIntent, null, first));

        Assert.Equal(
            [first.SceneId, second.SceneId, third.SceneId],
            viewModel.SceneTables.Take(3).Select(scene => scene.SceneId));
    }

    [Fact]
    public void PreviewMoveSceneSkipsRepeatedTargetIntent()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        viewModel.AddSceneCommand.Execute(null);
        var first = viewModel.SceneTables[0];
        var second = viewModel.SceneTables[1];

        viewModel.BeginWorkbenchDrag();
        Assert.True(viewModel.PreviewMoveScene(first, second, WorkbenchDropPosition.After));
        var documentAfterFirstPreview = viewModel.WorkbenchDocument;
        var orderAfterFirstPreview = viewModel.SceneTables.Select(scene => scene.SceneId).ToArray();

        Assert.False(viewModel.PreviewMoveScene(first, second, WorkbenchDropPosition.After));

        Assert.Same(documentAfterFirstPreview, viewModel.WorkbenchDocument);
        Assert.Equal(orderAfterFirstPreview, viewModel.SceneTables.Select(scene => scene.SceneId));
    }

    [Fact]
    public void RemoveSceneDeletesSceneAndKeepsSelectionInRemainingRows()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        viewModel.AddSceneCommand.Execute(null);
        var originalSceneCount = viewModel.SceneTables.Count;
        var firstScene = viewModel.SceneTables[^2];
        var secondScene = viewModel.SceneTables[^1];
        viewModel.AddDialogueRowCommand.Execute(firstScene);
        viewModel.AddDialogueRowCommand.Execute(secondScene);
        viewModel.SelectRowCommand.Execute(firstScene.Rows[0]);

        viewModel.RemoveSceneCommand.Execute(firstScene);

        Assert.Equal(originalSceneCount - 1, viewModel.SceneTables.Count);
        Assert.DoesNotContain(firstScene, viewModel.SceneTables);
        Assert.Contains(secondScene, viewModel.SceneTables);
        Assert.Equal(viewModel.SceneTables.IndexOf(secondScene) + 1, secondScene.Order);
        Assert.Same(secondScene.Rows[0], viewModel.SelectedRow);
    }

    [Fact]
    public void RemoveSceneSelectsNearestAvailableRowWhenNextSceneIsEmpty()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        viewModel.AddSceneCommand.Execute(null);
        viewModel.AddSceneCommand.Execute(null);
        var previousScene = viewModel.SceneTables[^3];
        var removedScene = viewModel.SceneTables[^2];
        var emptyNextScene = viewModel.SceneTables[^1];
        viewModel.AddDialogueRowCommand.Execute(previousScene);
        var fallbackRow = previousScene.Rows[0];
        viewModel.AddDialogueRowCommand.Execute(removedScene);
        viewModel.SelectRowCommand.Execute(removedScene.Rows[0]);

        viewModel.RemoveSceneCommand.Execute(removedScene);

        Assert.Contains(emptyNextScene, viewModel.SceneTables);
        Assert.Empty(emptyNextScene.Rows);
        Assert.Same(fallbackRow, viewModel.SelectedRow);
    }

    [Fact]
    public void DropIntentDispatcherPreviewsRowToRowIntent()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var scene = viewModel.SceneTables.Last();
        viewModel.AddDialogueRowCommand.Execute(scene);
        viewModel.SelectRowCommand.Execute(null);
        viewModel.AddDialogueRowCommand.Execute(scene);
        var first = scene.Rows[0];
        var second = scene.Rows[1];
        var intent = new WorkbenchDropIntent(
            WorkbenchDropIntentKind.RowToRow,
            second,
            WorkbenchDropPosition.After);

        var previewed = WorkbenchDropIntentDispatcher.Preview(viewModel, intent, first, null);

        Assert.True(previewed);
        Assert.Equal([second.InteractionId, first.InteractionId], scene.Rows.Select(row => row.InteractionId));
        Assert.Same(first, viewModel.SelectedRow);
    }

    [Fact]
    public void DropIntentDispatcherCommitsRowToSceneEndIntent()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var sourceScene = viewModel.SceneTables[0];
        var targetScene = viewModel.SceneTables.Last();
        var dragged = sourceScene.Rows[0];
        var intent = new WorkbenchDropIntent(
            WorkbenchDropIntentKind.RowToSceneEnd,
            targetScene,
            null);

        WorkbenchDropIntentDispatcher.Commit(viewModel, intent, dragged, null);

        Assert.DoesNotContain(dragged, sourceScene.Rows);
        Assert.Same(dragged, targetScene.Rows.Single());
        Assert.Equal(dragged.InteractionId, viewModel.WorkbenchDocument.Scenes.Last().Rows.Single().InteractionId);
        Assert.True(viewModel.IsWorkbenchDirty);
        Assert.True(viewModel.CanUndoWorkbench);
    }

    [Fact]
    public void DropIntentDispatcherPreviewsSceneToSceneIntent()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSceneCommand.Execute(null);
        var first = viewModel.SceneTables[0];
        var second = viewModel.SceneTables[1];
        var intent = new WorkbenchDropIntent(
            WorkbenchDropIntentKind.SceneToScene,
            second,
            WorkbenchDropPosition.After);

        var previewed = WorkbenchDropIntentDispatcher.Preview(viewModel, intent, null, first);

        Assert.True(previewed);
        Assert.Equal(second.SceneId, viewModel.SceneTables[0].SceneId);
        Assert.Equal(first.SceneId, viewModel.SceneTables[1].SceneId);
        Assert.Equal([second.SceneId, first.SceneId], viewModel.WorkbenchDocument.Scenes.Take(2).Select(scene => scene.SceneId));
    }

    private static MainWindowViewModel CreateViewModel(
        string? comfyVoicesDirectory = null,
        IAudioPreviewService? audioPreviewService = null)
    {
        var service = new DraftWorkspaceService(
            new MockLlmAnalysisClient(),
            new MockComfyUiClient(),
            new InMemoryInteractionProjectStore());
        return ConfigureFastTts(new MainWindowViewModel(
            service,
            audioPreviewService: audioPreviewService,
            comfyVoicesDirectoryResolver: comfyVoicesDirectory is null ? null : () => comfyVoicesDirectory));
    }

    private static MainWindowViewModel ConfigureFastTts(MainWindowViewModel viewModel)
    {
        viewModel.LiveTtsQueueDelayMilliseconds = 0;
        return viewModel;
    }

    private static async Task OpenTemporaryWorkspaceProjectAsync(MainWindowViewModel viewModel)
    {
        var root = Path.Combine(Path.GetTempPath(), $"draft-agent-test-workspace-{Guid.NewGuid():N}");
        await viewModel.OpenWorkspaceProjectAsync(root);
    }

    private static void BindAllRowsToDefaultVoice(MainWindowViewModel viewModel)
    {
        BindAllRowsToVoice(
            viewModel,
            IndexTts2Defaults.DefaultVoiceReferenceId,
            TestDefaultVoiceReferenceText);
    }

    private static async Task<AnalysisResultRowViewModel> WaitForLockedTtsRowAsync(MainWindowViewModel viewModel)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var row = viewModel.SceneTables
                .SelectMany(scene => scene.Rows)
                .FirstOrDefault(candidate => !candidate.CanEditTextAndEmotion);
            if (row is not null)
            {
                return row;
            }

            await Task.Delay(10);
        }

        throw new InvalidOperationException("Expected at least one TTS row to be locked.");
    }

    private static async Task<ComfyTaskRequest> WaitForSubmittedTtsRequestAsync(
        QueuedComfyUiClient client,
        string taskId)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var request = client.Requests.LastOrDefault(candidate => candidate.Task.Id == taskId);
            if (request is not null)
            {
                return request;
            }

            await Task.Delay(25);
        }

        throw new InvalidOperationException($"Expected queued TTS request for {taskId}.");
    }

    private static IReadOnlyDictionary<string, string> ReadPendingLiveTtsResubmissionOverrides(
        MainWindowViewModel viewModel,
        string taskId)
    {
        var field = typeof(MainWindowViewModel).GetField(
            "_pendingLiveTtsResubmissions",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var pending = Assert.IsType<Dictionary<string, IReadOnlyDictionary<string, string>>>(field?.GetValue(viewModel));
        return pending[taskId];
    }

    private static void BindAllRowsToVoice(MainWindowViewModel viewModel, string voiceReferenceId, string referenceText)
    {
        foreach (var row in viewModel.SceneTables.SelectMany(scene => scene.Rows))
        {
            row.SelectedVoiceReference = voiceReferenceId;
            row.SelectedVoiceReferenceText = referenceText;
        }
    }

    private static async Task<string> CreateWavFileAsync(string path, double durationSeconds)
    {
        const ushort channels = 1;
        const uint sampleRate = 16000;
        const ushort bitsPerSample = 16;
        const ushort blockAlign = channels * bitsPerSample / 8;
        const uint byteRate = sampleRate * blockAlign;
        var dataSize = (uint)Math.Round(durationSeconds * byteRate);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36u + dataSize);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16u);
        writer.Write((ushort)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]);
        return path;
    }

    private static string CreateIsolatedVoicesDirectory(string scenario)
    {
        var modelsRoot = Path.Combine(Path.GetTempPath(), $"draft-agent-{scenario}-models-{Guid.NewGuid():N}");
        var voicesDirectory = Path.Combine(modelsRoot, "voices");
        Directory.CreateDirectory(voicesDirectory);
        return voicesDirectory;
    }

    private static void EnableRoleDialogueVoiceRoute(MainWindowViewModel viewModel)
    {
        if (!viewModel.UseRoleVoiceForAllCharacterLines)
        {
            viewModel.ToggleRoleAllVoiceRouteCommand.Execute(null);
        }

        Assert.True(viewModel.UseRoleVoiceForAllCharacterLines);
        Assert.True(viewModel.UseRoleVoiceForDialogue);
        Assert.False(viewModel.UseRoleVoiceForThought);
        Assert.False(viewModel.UseRoleVoiceForAction);
        Assert.True(viewModel.CanEditCharacterVoiceBindings);
    }

    private static GenerationTask WithDefaultVoice(GenerationTask task)
    {
        return WithVoice(
            task,
            IndexTts2Defaults.DefaultVoiceReferenceId,
            TestDefaultVoiceReferenceText);
    }

    private static GenerationTask WithVoice(GenerationTask task, string voiceReferenceId, string referenceText)
    {
        return task with
        {
            Parameters = new Dictionary<string, string>(task.Parameters, StringComparer.Ordinal)
            {
                ["voiceReferenceId"] = voiceReferenceId,
                ["referenceText"] = referenceText
            }
        };
    }

    private sealed class CapturingAudioPreviewService : IAudioPreviewService
    {
        public string LastTarget { get; private set; } = string.Empty;

        public Task PlayAsync(string target, CancellationToken cancellationToken = default)
        {
            LastTarget = target;
            return Task.CompletedTask;
        }

        public void Stop()
        {
            LastTarget = string.Empty;
        }

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingAudioPreviewService : IAudioPreviewService
    {
        public Task PlayAsync(string target, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("synthetic audio preview failure");
        }

        public void Stop()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class FailingSaveWorkspaceProjectManager : IWorkspaceProjectManager
    {
        private readonly WorkspaceProjectManager _inner = new();
        private int _saveDraftCount;

        public Task<WorkspaceProjectManifest> CreateOrOpenAsync(string rootPath, string projectName, CancellationToken cancellationToken = default)
        {
            return _inner.CreateOrOpenAsync(rootPath, projectName, cancellationToken);
        }

        public Task<WorkspaceProjectManifest> LoadAsync(string rootPath, CancellationToken cancellationToken = default)
        {
            return _inner.LoadAsync(rootPath, cancellationToken);
        }

        public Task<InteractionProject?> LoadDraftAsync(WorkspaceProject workspace, CancellationToken cancellationToken = default)
        {
            return _inner.LoadDraftAsync(workspace, cancellationToken);
        }

        public Task<WorkspaceProjectManifest> SaveDraftAsync(WorkspaceProject workspace, InteractionProject project, CancellationToken cancellationToken = default)
        {
            _saveDraftCount++;
            if (_saveDraftCount == 1)
            {
                return _inner.SaveDraftAsync(workspace, project, cancellationToken);
            }

            throw new InvalidOperationException("synthetic workspace save failure");
        }

        public Task<string?> LoadOriginalAiJsonAsync(WorkspaceProject workspace, CancellationToken cancellationToken = default)
        {
            return _inner.LoadOriginalAiJsonAsync(workspace, cancellationToken);
        }

        public Task<WorkspaceProjectManifest> SaveOriginalAiJsonAsync(WorkspaceProject workspace, string rawJson, CancellationToken cancellationToken = default)
        {
            return _inner.SaveOriginalAiJsonAsync(workspace, rawJson, cancellationToken);
        }
    }

    private static void AssertDisplayProjectionMatchesSceneTables(MainWindowViewModel viewModel)
    {
        var expected = WorkbenchDisplayProjection.Build(viewModel.SceneTables);
        Assert.Equal(expected.Count, viewModel.WorkbenchDisplayItems.Count);

        for (var index = 0; index < expected.Count; index++)
        {
            var expectedItem = expected[index];
            var actualItem = viewModel.WorkbenchDisplayItems[index];
            Assert.Equal(expectedItem.Kind, actualItem.Kind);
            Assert.Same(expectedItem.Scene, actualItem.Scene);

            if (expectedItem is WorkbenchRowDisplayItem expectedRow)
            {
                var actualRow = Assert.IsType<WorkbenchRowDisplayItem>(actualItem);
                Assert.Same(expectedRow.Row, actualRow.Row);
            }
        }
    }

    private static int LeadingNumber(string value)
    {
        var index = 0;
        while (index < value.Length && char.IsDigit(value[index]))
        {
            index++;
        }

        return int.Parse(value[..index]);
    }

    private static void AddNotification(List<string> notifications, string? propertyName)
    {
        if (propertyName is not null)
        {
            notifications.Add(propertyName);
        }
    }

    private static string CreateTempPng()
    {
        var path = Path.Combine(Path.GetTempPath(), $"draft-agent-preview-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path,
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
            0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
            0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
            0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
            0x42, 0x60, 0x82
        ]);
        return path;
    }

    private static InteractionProject CreateWorkbenchProject(string name, int sceneCount, int rowCount)
    {
        var characters = Enumerable.Range(1, 8)
            .Select(index => new Character(
                $"char_{index}",
                $"Character {index}",
                "角色",
                $"Appearance {index}",
                $"Voice {index}",
                "Calm",
                new AssetBinding(Portrait: $"image_character_{index}.png", Voice: $"character_{index}.wav")))
            .ToArray();
        var scenes = new List<InteractionScene>(sceneCount);
        var rootTasks = new List<GenerationTask>();
        var remainingRows = rowCount;
        for (var sceneIndex = 0; sceneIndex < sceneCount; sceneIndex++)
        {
            var scenesLeft = sceneCount - sceneIndex;
            var rowsInScene = Math.Max(1, remainingRows / scenesLeft);
            remainingRows -= rowsInScene;
            var interactions = new List<CharacterInteraction>(rowsInScene);
            var sceneId = $"scene_{sceneIndex + 1}";
            for (var rowIndex = 0; rowIndex < rowsInScene; rowIndex++)
            {
                var globalIndex = interactions.Count + scenes.Sum(scene => scene.Interactions.Count) + 1;
                var isNarration = globalIndex % 5 == 0;
                var character = characters[(globalIndex - 1) % characters.Length];
                var interactionId = $"int_{globalIndex}";
                var hasTts = globalIndex % 3 == 0;
                var voice = new VoiceRenderSettings(
                    hasTts ? character.AssetBinding.Voice : null,
                    "Calm",
                    0.5,
                    1,
                    0.2);
                var task = hasTts
                    ? new GenerationTask(
                        $"tts_{interactionId}",
                        GenerationTaskType.Tts,
                        interactionId,
                        $"TTS {globalIndex}",
                        "IndexTTS-2",
                        GenerationTaskStatus.Completed,
                        new Dictionary<string, string> { ["text"] = $"Line {globalIndex}" },
                        $"audio_{interactionId}.flac")
                    : null;
                if (task is not null)
                {
                    rootTasks.Add(task);
                }

                interactions.Add(new CharacterInteraction(
                    interactionId,
                    rowIndex + 1,
                    isNarration ? InteractionType.Narration : InteractionType.Dialogue,
                    sceneId,
                    isNarration ? null : character.Id,
                    null,
                    new EmotionSpec("Calm", 0.5, "steady", "calm", "plain", "still"),
                    new ActionSpec("stand", [], "static", 2.0),
                    isNarration ? null : new DialogueLine($"dialogue_{interactionId}", character.Id, null, $"Benchmark line {globalIndex}", voice),
                    null,
                    isNarration ? new NarrationLine($"narration_{interactionId}", $"Benchmark narration {globalIndex}", voice) : null,
                    new InteractionBinding(
                        hasTts ? character.AssetBinding.Voice : null,
                        isNarration && hasTts ? character.AssetBinding.Voice : null,
                        VisualAssetBinding.FromImage($"scene_asset_{sceneIndex + 1}"),
                        isNarration ? null : character.AssetBinding.PortraitVisualBinding),
                    task is null ? [] : [task],
                    $"segment_{globalIndex}"));
            }

            scenes.Add(new InteractionScene(
                sceneId,
                sceneIndex + 1,
                $"Benchmark Scene {sceneIndex + 1}",
                $"Location {sceneIndex + 1}",
                "day",
                "clear",
                "calm",
                $"Benchmark scene {sceneIndex + 1}",
                new SceneAssetBinding(BackgroundImage: $"scene_{sceneIndex + 1}.png"),
                interactions));
        }

        return new InteractionProject(
            $"project_{name}",
            name,
            new ScriptDocument(name, name, []),
            characters,
            scenes,
            rootTasks,
            []);
    }

    private sealed class ThrowingComfyUiClient : IComfyUiClient, IComfyConnectionClient
    {
        public Task<ComfyConnectionCheckResult> CheckComfyConnectionAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ComfyConnectionCheckResult(
                "mock://comfy",
                ComfyConnectionState.Connected,
                200,
                "Mock ComfyUI connected."));
        }

        public Task<ComfyTaskResult> SubmitAsync(ComfyTaskRequest request, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("synthetic comfy failure");
        }
    }

    private sealed class CapturingComfyUiClient : IComfyUiClient, IComfyConnectionClient, IComfyVoiceCatalogClient
    {
        private readonly ComfyConnectionCheckResult _connectionResult;

        public CapturingComfyUiClient(ComfyConnectionCheckResult? connectionResult = null)
        {
            _connectionResult = connectionResult ?? new ComfyConnectionCheckResult(
                "mock://comfy",
                ComfyConnectionState.Connected,
                200,
                "Mock ComfyUI connected.");
        }

        public List<ComfyTaskRequest> Requests { get; } = [];

        public int ConnectionCheckCount { get; private set; }

        public ComfyVoiceCatalogResult VoiceCatalog { get; init; } = new([]);

        public Task<ComfyTaskResult> SubmitAsync(ComfyTaskRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ComfyTaskResult(
                request.Task.Id,
                GenerationTaskStatus.Completed,
                $"asset_{request.Task.Id}",
                "captured"));
        }

        public Task<ComfyConnectionCheckResult> CheckComfyConnectionAsync(CancellationToken cancellationToken = default)
        {
            ConnectionCheckCount++;
            return Task.FromResult(_connectionResult);
        }

        public Task<ComfyVoiceCatalogResult> GetVoiceCatalogAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(VoiceCatalog);
        }
    }

    private sealed class BlockingComfyUiClient : IComfyUiClient, IComfyConnectionClient
    {
        private readonly TaskCompletionSource _releaseSubmission = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SubmissionStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<ComfyTaskRequest> Requests { get; } = [];

        public Task<ComfyConnectionCheckResult> CheckComfyConnectionAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ComfyConnectionCheckResult(
                "mock://comfy",
                ComfyConnectionState.Connected,
                200,
                "Mock ComfyUI connected."));
        }

        public async Task<ComfyTaskResult> SubmitAsync(ComfyTaskRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            SubmissionStarted.TrySetResult();
            await _releaseSubmission.Task.WaitAsync(cancellationToken);
            return new ComfyTaskResult(
                request.Task.Id,
                GenerationTaskStatus.Completed,
                $"asset_{request.Task.Id}",
                "captured");
        }

        public void ReleaseSubmissions()
        {
            _releaseSubmission.TrySetResult();
        }
    }

    private sealed class LiveRegenerationComfyUiClient : IComfyUiClient, IComfyConnectionClient, IComfyPromptStatusClient
    {
        private readonly Dictionary<string, string> _promptTaskIds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _submitCountsByTaskId = new(StringComparer.Ordinal);
        private readonly HashSet<string> _regenerationPromptIds = new(StringComparer.Ordinal);
        private bool _completeFirstPrompt;
        private bool _completeRegenerationPrompt;

        public TaskCompletionSource FirstSubmission { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource RegenerationSubmitted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<ComfyTaskRequest> Requests { get; } = [];

        public Task<ComfyConnectionCheckResult> CheckComfyConnectionAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ComfyConnectionCheckResult(
                "mock://comfy",
                ComfyConnectionState.Connected,
                200,
                "Mock ComfyUI connected."));
        }

        public Task<ComfyTaskResult> SubmitAsync(ComfyTaskRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var count = _submitCountsByTaskId.TryGetValue(request.Task.Id, out var existing) ? existing + 1 : 1;
            _submitCountsByTaskId[request.Task.Id] = count;
            var promptId = $"prompt_{request.Task.Id}_{count}";
            _promptTaskIds[promptId] = request.Task.Id;
            if (Requests.Count == 1)
            {
                FirstSubmission.TrySetResult();
            }

            if (Requests.Count > 1)
            {
                _regenerationPromptIds.Add(promptId);
                RegenerationSubmitted.TrySetResult();
            }

            return Task.FromResult(new ComfyTaskResult(
                request.Task.Id,
                GenerationTaskStatus.Pending,
                promptId,
                "queued",
                new ComfyPromptMetadata(promptId, 1, ComfySubmitState.Submitted, "queued")));
        }

        public Task<ComfyPromptStatusResult> GetPromptStatusAsync(string promptId, CancellationToken cancellationToken = default)
        {
            var taskId = _promptTaskIds[promptId];
            var isRegenerationPrompt = _regenerationPromptIds.Contains(promptId);
            var shouldComplete = isRegenerationPrompt ? _completeRegenerationPrompt : _completeFirstPrompt;
            if (!shouldComplete)
            {
                return Task.FromResult(new ComfyPromptStatusResult(
                    promptId,
                    ComfyPromptState.Queued,
                    GenerationTaskStatus.Pending,
                    1,
                    null,
                    [],
                    $"queued:{taskId}"));
            }

            return Task.FromResult(new ComfyPromptStatusResult(
                promptId,
                ComfyPromptState.Completed,
                GenerationTaskStatus.Completed,
                null,
                1,
                [$"output||{promptId}.flac"],
                $"done:{taskId}"));
        }

        public void AllowFirstPromptToComplete()
        {
            _completeFirstPrompt = true;
        }

        public void AllowRegenerationPromptToComplete()
        {
            _completeRegenerationPrompt = true;
        }
    }

    private sealed class QueuedComfyUiClient : IComfyUiClient, IComfyConnectionClient, IComfyPromptStatusClient, IComfyQueueControlClient
    {
        public TaskCompletionSource SubmissionStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource FirstStatusCheck { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> PromptIds { get; } = [];

        public List<string> DeletedPromptIds { get; } = [];

        public List<ComfyTaskRequest> Requests { get; } = [];

        public Task<ComfyConnectionCheckResult> CheckComfyConnectionAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ComfyConnectionCheckResult(
                "mock://comfy",
                ComfyConnectionState.Connected,
                200,
                "Mock ComfyUI connected."));
        }

        public Task<ComfyTaskResult> SubmitAsync(ComfyTaskRequest request, CancellationToken cancellationToken = default)
        {
            var promptId = $"prompt_{request.Task.Id}";
            Requests.Add(request);
            PromptIds.Add(promptId);
            SubmissionStarted.TrySetResult();
            return Task.FromResult(new ComfyTaskResult(
                request.Task.Id,
                GenerationTaskStatus.Pending,
                promptId,
                "queued",
                new ComfyPromptMetadata(promptId, 1, ComfySubmitState.Submitted, "queued")));
        }

        public Task<ComfyPromptStatusResult> GetPromptStatusAsync(string promptId, CancellationToken cancellationToken = default)
        {
            FirstStatusCheck.TrySetResult();
            return Task.FromResult(new ComfyPromptStatusResult(
                promptId,
                ComfyPromptState.Queued,
                GenerationTaskStatus.Pending,
                1,
                null,
                [],
                "queued"));
        }

        public Task<ComfyQueueDeleteResult> DeleteQueuedPromptsAsync(IReadOnlyList<string> promptIds, CancellationToken cancellationToken = default)
        {
            DeletedPromptIds.AddRange(promptIds);
            return Task.FromResult(new ComfyQueueDeleteResult(promptIds.ToArray(), "deleted"));
        }
    }

    private sealed class DownloadingComfyUiClient : IComfyUiClient, IComfyConnectionClient, IComfyPromptStatusClient, IComfyOutputFileClient
    {
        public List<ComfyTaskRequest> Requests { get; } = [];

        public List<string> StatusChecks { get; } = [];

        public List<string> Downloads { get; } = [];

        public Task<ComfyConnectionCheckResult> CheckComfyConnectionAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ComfyConnectionCheckResult(
                "mock://comfy",
                ComfyConnectionState.Connected,
                200,
                "Mock ComfyUI connected."));
        }

        public Task<ComfyTaskResult> SubmitAsync(ComfyTaskRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Task.Type != GenerationTaskType.Tts)
            {
                throw new NotSupportedException("Test ComfyUI downloader only supports TTS tasks.");
            }

            Requests.Add(request);
            return Task.FromResult(new ComfyTaskResult(
                request.Task.Id,
                GenerationTaskStatus.Completed,
                $"output||{request.Task.Id}.flac",
                "captured"));
        }

        public Task<ComfyPromptStatusResult> GetPromptStatusAsync(string promptId, CancellationToken cancellationToken = default)
        {
            StatusChecks.Add(promptId);
            return Task.FromResult(new ComfyPromptStatusResult(
                promptId,
                ComfyPromptState.Completed,
                GenerationTaskStatus.Completed,
                null,
                null,
                [$"output||{promptId}.flac"],
                "completed"));
        }

        public async Task<string> DownloadOutputAsync(string outputReference, string destinationDirectory, CancellationToken cancellationToken = default)
        {
            Downloads.Add(outputReference);
            Directory.CreateDirectory(destinationDirectory);
            var filename = ComfyOutputReference.Parse(outputReference).Filename;
            var path = Path.Combine(destinationDirectory, string.IsNullOrWhiteSpace(filename) ? "generated.flac" : filename);
            await File.WriteAllBytesAsync(path, [0x89, 0x50, 0x4E, 0x47], cancellationToken);
            return path;
        }
    }
}




