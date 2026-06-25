using System;
using System.Collections.Generic;
using System.Linq;
using DraftAgent.Core;

namespace DraftAgent.App.ViewModels;

internal readonly record struct AddSceneCommand(
    int TargetIndex,
    SceneTableViewModel Scene)
{
    public static AddSceneCommand FromSceneCollection(IReadOnlyCollection<SceneTableViewModel> scenes)
    {
        return new AddSceneCommand(
            scenes.Count,
            SceneTableViewModel.CreateManual(scenes.Count + 1));
    }
}

internal readonly record struct AddRowCommand(
    string SceneId,
    int SceneIndex,
    int TargetIndex,
    AnalysisResultRowViewModel Row)
{
    public static AddRowCommand ForDialogue(
        SceneTableViewModel scene,
        int sceneIndex,
        AnalysisResultRowViewModel? selectedRow,
        string characterName)
    {
        return new AddRowCommand(
            scene.SceneId,
            sceneIndex,
            InsertIndex(scene, selectedRow) ?? scene.Rows.Count,
            AnalysisResultRowViewModel.CreateManualDialogue(scene.NextRowOrder(), characterName));
    }

    public static AddRowCommand ForNarration(
        SceneTableViewModel scene,
        int sceneIndex,
        AnalysisResultRowViewModel? selectedRow)
    {
        return new AddRowCommand(
            scene.SceneId,
            sceneIndex,
            InsertIndex(scene, selectedRow) ?? scene.Rows.Count,
            AnalysisResultRowViewModel.CreateManualNarration(scene.NextRowOrder()));
    }

    private static int? InsertIndex(SceneTableViewModel scene, AnalysisResultRowViewModel? selectedRow)
    {
        if (selectedRow is null)
        {
            return null;
        }

        for (var index = 0; index < scene.Rows.Count; index++)
        {
            if (ReferenceEquals(scene.Rows[index], selectedRow))
            {
                return index;
            }
        }

        return null;
    }
}

internal readonly record struct RemoveSceneCommand(
    string SceneId,
    int SourceIndex)
{
    public static RemoveSceneCommand FromResolvedLocation(SceneTableViewModel scene, int sourceIndex)
    {
        return new RemoveSceneCommand(scene.SceneId, sourceIndex);
    }
}

internal readonly record struct RemoveRowCommand(
    string InteractionId,
    string SourceSceneId,
    int SourceSceneIndex,
    int SourceRowIndex)
{
    public static RemoveRowCommand FromResolvedLocation(
        AnalysisResultRowViewModel row,
        SceneTableViewModel sourceScene,
        int sourceSceneIndex,
        int sourceRowIndex)
    {
        return new RemoveRowCommand(
            row.InteractionId,
            sourceScene.SceneId,
            sourceSceneIndex,
            sourceRowIndex);
    }
}

internal readonly record struct UpdateSceneTextCommand(
    string SceneId,
    string Name,
    string Effect)
{
    public static UpdateSceneTextCommand FromScene(SceneTableViewModel scene)
    {
        return new UpdateSceneTextCommand(scene.SceneId, scene.SceneName, scene.SceneEffect);
    }
}

internal readonly record struct UpdateSceneBindingCommand(
    string SceneId,
    string? SceneImageAssetId)
{
    public static UpdateSceneBindingCommand FromScene(SceneTableViewModel scene)
    {
        return new UpdateSceneBindingCommand(
            scene.SceneId,
            WorkbenchBindingValue.Asset(scene.SelectedSceneAsset));
    }
}

internal readonly record struct UpdateRowTextCommand(
    string InteractionId,
    string Text)
{
    public static UpdateRowTextCommand FromRow(AnalysisResultRowViewModel row)
    {
        return new UpdateRowTextCommand(row.InteractionId, row.LineText);
    }
}

internal readonly record struct UpdateRowSubjectCommand(
    string InteractionId,
    string SubjectName,
    string? SubjectCharacterId)
{
    public static UpdateRowSubjectCommand FromRow(AnalysisResultRowViewModel row)
    {
        return FromRow(row, []);
    }

    public static UpdateRowSubjectCommand FromRow(
        AnalysisResultRowViewModel row,
        IReadOnlyList<Character> characters)
    {
        return new UpdateRowSubjectCommand(
            row.InteractionId,
            row.SubjectName ?? string.Empty,
            ResolveSubjectCharacterId(row.SubjectName, row.SubjectCharacterId, characters));
    }

    private static string? ResolveSubjectCharacterId(
        string? subjectName,
        string? currentSubjectCharacterId,
        IReadOnlyList<Character> characters)
    {
        if (string.Equals(subjectName?.Trim(), "旁白", StringComparison.Ordinal) ||
            string.Equals(subjectName?.Trim(), AnalysisResultRowViewModel.EnvironmentEffectsSubjectName, StringComparison.Ordinal) ||
            string.Equals(subjectName?.Trim(), AnalysisResultRowViewModel.NarrationStorySubjectName, StringComparison.Ordinal))
        {
            return null;
        }

        for (var index = 0; index < characters.Count; index++)
        {
            var character = characters[index];
            if (character.Name == "旁白")
            {
                continue;
            }

            if (string.Equals(character.Name, subjectName, StringComparison.Ordinal))
            {
                return character.Id;
            }
        }

        return null;
    }
}

internal readonly record struct UpdateRowSourceTypeCommand(
    string InteractionId,
    string SourceItemType,
    string SubjectName,
    string? SubjectCharacterId)
{
    public static UpdateRowSourceTypeCommand FromRow(
        AnalysisResultRowViewModel row,
        string sourceItemType,
        IReadOnlyList<Character> characters)
    {
        var subjectName = ResolveSubjectName(row, sourceItemType, characters);
        return new UpdateRowSourceTypeCommand(
            row.InteractionId,
            sourceItemType,
            subjectName,
            ResolveSubjectCharacterId(subjectName, row.SubjectCharacterId, characters));
    }

    private static string ResolveSubjectName(
        AnalysisResultRowViewModel row,
        string sourceItemType,
        IReadOnlyList<Character> characters)
    {
        return sourceItemType switch
        {
            "effects" => AnalysisResultRowViewModel.EnvironmentEffectsSubjectName,
            "narration" => AnalysisResultRowViewModel.NarrationStorySubjectName,
            _ => ResolveCharacterSubjectName(row, characters)
        };
    }

    private static string ResolveCharacterSubjectName(
        AnalysisResultRowViewModel row,
        IReadOnlyList<Character> characters)
    {
        var current = row.SubjectName?.Trim();
        if (!string.IsNullOrWhiteSpace(current) &&
            !string.Equals(current, "旁白", StringComparison.Ordinal) &&
            !string.Equals(current, AnalysisResultRowViewModel.EnvironmentEffectsSubjectName, StringComparison.Ordinal) &&
            !string.Equals(current, AnalysisResultRowViewModel.NarrationStorySubjectName, StringComparison.Ordinal))
        {
            return current;
        }

        return characters.FirstOrDefault(character => character.Name != "旁白")?.Name ?? "角色";
    }

    private static string? ResolveSubjectCharacterId(
        string? subjectName,
        string? currentSubjectCharacterId,
        IReadOnlyList<Character> characters)
    {
        if (string.Equals(subjectName?.Trim(), AnalysisResultRowViewModel.EnvironmentEffectsSubjectName, StringComparison.Ordinal) ||
            string.Equals(subjectName?.Trim(), AnalysisResultRowViewModel.NarrationStorySubjectName, StringComparison.Ordinal))
        {
            return null;
        }

        return characters.FirstOrDefault(character =>
            character.Name != "旁白" &&
            string.Equals(character.Name, subjectName, StringComparison.Ordinal))?.Id ?? currentSubjectCharacterId;
    }
}

internal readonly record struct UpdateRowEmotionCommand(
    string InteractionId,
    string Emotion,
    IReadOnlyDictionary<string, double>? EmotionVectors,
    double? EmotionAlpha)
{
    public static UpdateRowEmotionCommand FromRow(AnalysisResultRowViewModel row)
    {
        return new UpdateRowEmotionCommand(
            row.InteractionId,
            row.EffectiveEmotionText,
            row.EmotionVectorMap,
            row.EmotionAlpha);
    }
}

internal readonly record struct UpdateRowBindingCommand(
    string InteractionId,
    string? CharacterImageAssetId,
    string? VoiceReferenceId,
    string? VoiceReferenceText)
{
    public static UpdateRowBindingCommand FromRow(AnalysisResultRowViewModel row)
    {
        return new UpdateRowBindingCommand(
            row.InteractionId,
            WorkbenchBindingValue.Asset(row.SelectedCharacterAsset),
            WorkbenchBindingValue.Asset(row.SelectedVoiceReference),
            WorkbenchBindingValue.Text(row.SelectedVoiceReferenceText));
    }
}

internal readonly record struct MoveRowCommand(
    string InteractionId,
    string SourceSceneId,
    int SourceSceneIndex,
    int SourceRowIndex,
    string TargetSceneId,
    int TargetSceneIndex,
    int TargetIndex)
{
    public static MoveRowCommand FromResolvedLocation(
        AnalysisResultRowViewModel row,
        SceneTableViewModel sourceScene,
        int sourceSceneIndex,
        int sourceRowIndex,
        SceneTableViewModel targetScene,
        int targetSceneIndex,
        int targetIndex)
    {
        return new MoveRowCommand(
            row.InteractionId,
            sourceScene.SceneId,
            sourceSceneIndex,
            sourceRowIndex,
            targetScene.SceneId,
            targetSceneIndex,
            targetIndex);
    }
}

internal readonly record struct MoveSceneCommand(
    string SceneId,
    int SourceIndex,
    int TargetIndex)
{
    public static MoveSceneCommand FromResolvedLocation(
        SceneTableViewModel scene,
        int sourceIndex,
        int targetIndex)
    {
        return new MoveSceneCommand(scene.SceneId, sourceIndex, targetIndex);
    }
}
