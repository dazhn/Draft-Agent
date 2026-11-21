using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using DraftAgent.Core;

namespace DraftAgent.App.ViewModels;

internal static class WorkbenchProjectProjection
{
    public static void ApplyProject(
        ObservableCollection<SceneTableViewModel> scenes,
        InteractionProject project,
        WorkbenchDocument document)
    {
        var candidates = BuildProjectViewModelCandidates(scenes, project, document);
        if (candidates.HasAny)
        {
            WorkbenchViewModelAdapter.ApplyDocument(scenes, document, candidates.NewScenes, candidates.NewRows);
        }
        else
        {
            WorkbenchViewModelAdapter.ApplyDocument(scenes, document);
        }

        ApplyProjectDerivedState(scenes, project);
    }

    private static void ApplyProjectDerivedState(
        IReadOnlyList<SceneTableViewModel> scenes,
        InteractionProject project)
    {
        var projectScenesById = BuildProjectSceneIndex(project.Scenes);
        var context = AnalysisResultRowViewModel.InteractionRowBuildContext.FromProject(project);
        for (var sceneIndex = 0; sceneIndex < scenes.Count; sceneIndex++)
        {
            var scene = scenes[sceneIndex];
            if (!projectScenesById.TryGetValue(scene.SceneId, out var projectScene))
            {
                continue;
            }

            ApplyProjectDerivedState(scene, projectScene, context);
        }
    }

    private static void ApplyProjectDerivedState(
        SceneTableViewModel scene,
        InteractionScene projectScene,
        AnalysisResultRowViewModel.InteractionRowBuildContext context)
    {
        scene.ApplyProjectDerivedState(projectScene);
        var interactionsById = BuildInteractionIndex(projectScene.Interactions);
        for (var rowIndex = 0; rowIndex < scene.Rows.Count; rowIndex++)
        {
            var row = scene.Rows[rowIndex];
            if (interactionsById.TryGetValue(row.InteractionId, out var interaction))
            {
                row.ApplyProjectDerivedState(interaction, context);
            }
        }
    }

    private static Dictionary<string, InteractionScene> BuildProjectSceneIndex(
        IReadOnlyList<InteractionScene> scenes)
    {
        var index = new Dictionary<string, InteractionScene>(scenes.Count, StringComparer.Ordinal);
        for (var sceneIndex = 0; sceneIndex < scenes.Count; sceneIndex++)
        {
            index[scenes[sceneIndex].Id] = scenes[sceneIndex];
        }

        return index;
    }

    private static Dictionary<string, CharacterInteraction> BuildInteractionIndex(
        IReadOnlyList<CharacterInteraction> interactions)
    {
        var index = new Dictionary<string, CharacterInteraction>(interactions.Count, StringComparer.Ordinal);
        for (var rowIndex = 0; rowIndex < interactions.Count; rowIndex++)
        {
            index[interactions[rowIndex].Id] = interactions[rowIndex];
        }

        return index;
    }

    private static ProjectViewModelCandidates BuildProjectViewModelCandidates(
        IReadOnlyList<SceneTableViewModel> scenes,
        InteractionProject project,
        WorkbenchDocument document)
    {
        var existingSceneIds = new HashSet<string>(StringComparer.Ordinal);
        var existingRowIds = new HashSet<string>(StringComparer.Ordinal);
        for (var sceneIndex = 0; sceneIndex < scenes.Count; sceneIndex++)
        {
            var scene = scenes[sceneIndex];
            existingSceneIds.Add(scene.SceneId);
            for (var rowIndex = 0; rowIndex < scene.Rows.Count; rowIndex++)
            {
                existingRowIds.Add(scene.Rows[rowIndex].InteractionId);
            }
        }

        var context = AnalysisResultRowViewModel.InteractionRowBuildContext.FromProject(project);
        var newScenes = new List<SceneTableViewModel>();
        var newRows = new List<AnalysisResultRowViewModel>();
        for (var sceneIndex = 0; sceneIndex < project.Scenes.Count; sceneIndex++)
        {
            var projectScene = project.Scenes[sceneIndex];
            if (!existingSceneIds.Contains(projectScene.Id))
            {
                var sceneViewModel = document.TryGetSceneDraft(projectScene.Id, out var sceneDraft)
                    ? SceneTableViewModel.FromDraft(sceneDraft)
                    : SceneTableViewModel.FromScene(projectScene, context);
                ApplyProjectDerivedState(sceneViewModel, projectScene, context);
                newScenes.Add(sceneViewModel);
                continue;
            }

            for (var rowIndex = 0; rowIndex < projectScene.Interactions.Count; rowIndex++)
            {
                var interaction = projectScene.Interactions[rowIndex];
                if (!existingRowIds.Contains(interaction.Id))
                {
                    var rowViewModel = document.RowsById.TryGetValue(interaction.Id, out var rowDraft)
                        ? AnalysisResultRowViewModel.FromDraft(rowDraft, document.TryGetSceneDraft(projectScene.Id, out var sceneDraft)
                            ? sceneDraft.VisualBinding?.EffectiveAssetId
                            : projectScene.AssetBinding.BackgroundImage)
                        : AnalysisResultRowViewModel.FromInteraction(
                            interaction,
                            context,
                            projectScene.AssetBinding.BackgroundImage);
                    rowViewModel.ApplyProjectDerivedState(interaction, context);
                    newRows.Add(rowViewModel);
                }
            }
        }

        return new ProjectViewModelCandidates(newScenes, newRows);
    }

    private readonly record struct ProjectViewModelCandidates(
        IReadOnlyList<SceneTableViewModel> NewScenes,
        IReadOnlyList<AnalysisResultRowViewModel> NewRows)
    {
        public bool HasAny => NewScenes.Count > 0 || NewRows.Count > 0;
    }
}
