using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using DraftAgent.Core;

namespace DraftAgent.App.ViewModels;

internal static class WorkbenchBindingProjection
{
    public static void RefreshTargets(
        ObservableCollection<BindingTargetViewModel> currentTargets,
        IReadOnlyList<SceneTableViewModel> scenes,
        IReadOnlyList<Character> characters,
        Func<string?, string?> resolveProjectAssetPath,
        IReadOnlyList<GenerationTask>? generationTasks = null)
    {
        var desiredTargets = BuildTargets(scenes, characters, resolveProjectAssetPath, generationTasks);
        RefreshPreviewAssets(desiredTargets, resolveProjectAssetPath);
        SyncTargets(currentTargets, desiredTargets);
    }

    public static bool RefreshSceneTarget(
        ObservableCollection<BindingTargetViewModel> currentTargets,
        SceneTableViewModel scene,
        Func<string?, string?> resolveProjectAssetPath)
    {
        var desiredTarget = BindingTargetViewModel.ForScene(scene);
        RefreshPreviewAsset(desiredTarget, new BindingTargetPreviewAssetResolver(resolveProjectAssetPath));

        for (var index = 0; index < currentTargets.Count; index++)
        {
            var currentTarget = currentTargets[index];
            if (currentTarget.Kind == BindingTargetKind.Scene &&
                string.Equals(currentTarget.TargetId, scene.SceneId, StringComparison.Ordinal))
            {
                currentTarget.ApplyRefreshedStateFrom(desiredTarget);
                return true;
            }
        }

        return false;
    }

    public static bool InsertSceneTarget(
        ObservableCollection<BindingTargetViewModel> currentTargets,
        SceneTableViewModel scene,
        int sceneIndex,
        Func<string?, string?> resolveProjectAssetPath)
    {
        if (sceneIndex < 0 || sceneIndex > currentTargets.Count)
        {
            return false;
        }

        var sceneTargetCount = 0;
        foreach (var target in currentTargets)
        {
            if (target.Kind == BindingTargetKind.Scene)
            {
                if (string.Equals(target.TargetId, scene.SceneId, StringComparison.Ordinal))
                {
                    return RefreshSceneTarget(currentTargets, scene, resolveProjectAssetPath);
                }

                sceneTargetCount++;
            }
        }

        if (sceneIndex > sceneTargetCount)
        {
            return false;
        }

        var targetToInsert = BindingTargetViewModel.ForScene(scene);
        RefreshPreviewAsset(targetToInsert, new BindingTargetPreviewAssetResolver(resolveProjectAssetPath));
        currentTargets.Insert(sceneIndex, targetToInsert);
        return true;
    }

    public static bool RemoveSceneTarget(
        ObservableCollection<BindingTargetViewModel> currentTargets,
        string sceneId)
    {
        for (var index = 0; index < currentTargets.Count; index++)
        {
            var target = currentTargets[index];
            if (target.Kind == BindingTargetKind.Scene &&
                string.Equals(target.TargetId, sceneId, StringComparison.Ordinal))
            {
                currentTargets.RemoveAt(index);
                return true;
            }
        }

        return false;
    }

    public static bool RemoveNarrationTarget(
        ObservableCollection<BindingTargetViewModel> currentTargets)
    {
        for (var index = 0; index < currentTargets.Count; index++)
        {
            if (currentTargets[index].Kind == BindingTargetKind.Narration)
            {
                currentTargets.RemoveAt(index);
                return true;
            }
        }

        return true;
    }

    public static bool SyncSceneTargetOrder(
        ObservableCollection<BindingTargetViewModel> currentTargets,
        IReadOnlyList<SceneTableViewModel> scenes)
    {
        if (scenes.Count == 0)
        {
            return true;
        }

        var sceneTargetIndexById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < currentTargets.Count; index++)
        {
            var target = currentTargets[index];
            if (target.Kind == BindingTargetKind.Scene)
            {
                sceneTargetIndexById[target.TargetId] = index;
            }
        }

        if (sceneTargetIndexById.Count != scenes.Count)
        {
            return false;
        }

        for (var sceneIndex = 0; sceneIndex < scenes.Count; sceneIndex++)
        {
            var scene = scenes[sceneIndex];
            if (!sceneTargetIndexById.TryGetValue(scene.SceneId, out var currentIndex))
            {
                return false;
            }

            if (currentIndex == sceneIndex)
            {
                continue;
            }

            currentTargets.Move(currentIndex, sceneIndex);
            RefreshSceneTargetIndexes(currentTargets, sceneTargetIndexById, Math.Min(currentIndex, sceneIndex));
        }

        return true;
    }

    public static bool SyncSceneTargetOrderRange(
        ObservableCollection<BindingTargetViewModel> currentTargets,
        IReadOnlyList<SceneTableViewModel> scenes)
    {
        if (scenes.Count == 0)
        {
            return true;
        }

        var sceneTargetCount = CountTargetsOfKindBefore(currentTargets, BindingTargetKind.Scene, currentTargets.Count);
        var desiredSceneTargetIndexes = new int[scenes.Count];
        for (var sceneIndex = 0; sceneIndex < scenes.Count; sceneIndex++)
        {
            var scene = scenes[sceneIndex];
            var currentIndex = IndexOfSceneTarget(currentTargets, scene.SceneId);
            if (currentIndex < 0 || currentIndex >= sceneTargetCount)
            {
                return false;
            }

            var desiredIndex = scene.Order - 1;
            if (desiredIndex < 0 || desiredIndex >= sceneTargetCount)
            {
                return false;
            }

            desiredSceneTargetIndexes[sceneIndex] = desiredIndex;
        }

        if (TrySyncSceneTargetOrderRangeWithSingleMove(currentTargets, sceneTargetCount, scenes))
        {
            return true;
        }

        for (var sceneIndex = 0; sceneIndex < scenes.Count; sceneIndex++)
        {
            var scene = scenes[sceneIndex];
            var desiredIndex = desiredSceneTargetIndexes[sceneIndex];
            var currentIndex = IndexOfSceneTarget(currentTargets, scene.SceneId);
            if (currentIndex < 0 || currentIndex >= sceneTargetCount)
            {
                return false;
            }

            if (currentIndex != desiredIndex)
            {
                currentTargets.Move(currentIndex, desiredIndex);
            }
        }

        for (var sceneIndex = 0; sceneIndex < scenes.Count; sceneIndex++)
        {
            var scene = scenes[sceneIndex];
            var desiredIndex = desiredSceneTargetIndexes[sceneIndex];
            if (desiredIndex >= currentTargets.Count ||
                currentTargets[desiredIndex].Kind != BindingTargetKind.Scene ||
                !string.Equals(currentTargets[desiredIndex].TargetId, scene.SceneId, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TrySyncSceneTargetOrderRangeWithSingleMove(
        ObservableCollection<BindingTargetViewModel> currentTargets,
        int sceneTargetCount,
        IReadOnlyList<SceneTableViewModel> changedScenes)
    {
        if (changedScenes.Count < 2)
        {
            return true;
        }

        var currentSceneTargetIds = new string[sceneTargetCount];
        var desiredSceneTargetIds = new string[sceneTargetCount];
        for (var index = 0; index < sceneTargetCount; index++)
        {
            var target = currentTargets[index];
            if (target.Kind != BindingTargetKind.Scene)
            {
                return false;
            }

            currentSceneTargetIds[index] = target.TargetId;
            desiredSceneTargetIds[index] = target.TargetId;
        }

        foreach (var scene in changedScenes)
        {
            var desiredIndex = scene.Order - 1;
            if (desiredIndex < 0 || desiredIndex >= sceneTargetCount)
            {
                return false;
            }

            desiredSceneTargetIds[desiredIndex] = scene.SceneId;
        }

        if (SequencesEqual(currentSceneTargetIds, desiredSceneTargetIds))
        {
            return true;
        }

        var firstMismatchIndex = -1;
        var lastMismatchIndex = -1;
        for (var index = 0; index < sceneTargetCount; index++)
        {
            if (string.Equals(currentSceneTargetIds[index], desiredSceneTargetIds[index], StringComparison.Ordinal))
            {
                continue;
            }

            if (firstMismatchIndex < 0)
            {
                firstMismatchIndex = index;
            }

            lastMismatchIndex = index;
        }

        if (firstMismatchIndex < 0)
        {
            return true;
        }

        if (TryApplySceneTargetSingleMove(
                currentTargets,
                currentSceneTargetIds,
                desiredSceneTargetIds,
                firstMismatchIndex,
                lastMismatchIndex))
        {
            return true;
        }

        if (TryApplySceneTargetSingleMove(
                currentTargets,
                currentSceneTargetIds,
                desiredSceneTargetIds,
                lastMismatchIndex,
                firstMismatchIndex))
        {
            return true;
        }

        return false;
    }

    private static bool TryApplySceneTargetSingleMove(
        ObservableCollection<BindingTargetViewModel> currentTargets,
        IReadOnlyList<string> currentIds,
        IReadOnlyList<string> desiredIds,
        int sourceIndex,
        int destinationIndex)
    {
        if (sourceIndex == destinationIndex ||
            !SingleMoveMatches(currentIds, desiredIds, sourceIndex, destinationIndex))
        {
            return false;
        }

        currentTargets.Move(sourceIndex, destinationIndex);
        return true;
    }

    private static bool SingleMoveMatches(
        IReadOnlyList<string> currentIds,
        IReadOnlyList<string> desiredIds,
        int sourceIndex,
        int destinationIndex)
    {
        var movedIds = new string[currentIds.Count];
        for (var index = 0; index < currentIds.Count; index++)
        {
            movedIds[index] = currentIds[index];
        }

        var movedId = movedIds[sourceIndex];
        if (sourceIndex < destinationIndex)
        {
            for (var index = sourceIndex; index < destinationIndex; index++)
            {
                movedIds[index] = movedIds[index + 1];
            }
        }
        else
        {
            for (var index = sourceIndex; index > destinationIndex; index--)
            {
                movedIds[index] = movedIds[index - 1];
            }
        }

        movedIds[destinationIndex] = movedId;
        return SequencesEqual(movedIds, desiredIds);
    }

    private static bool SequencesEqual(IReadOnlyList<string> first, IReadOnlyList<string> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        for (var index = 0; index < first.Count; index++)
        {
            if (!string.Equals(first[index], second[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public static bool SyncSceneTargets(
        ObservableCollection<BindingTargetViewModel> currentTargets,
        IReadOnlyList<SceneTableViewModel> scenes,
        Func<string?, string?> resolveProjectAssetPath)
    {
        var desiredSceneTargets = new BindingTargetViewModel[scenes.Count];
        var resolver = new BindingTargetPreviewAssetResolver(resolveProjectAssetPath);
        for (var index = 0; index < scenes.Count; index++)
        {
            var target = BindingTargetViewModel.ForScene(scenes[index]);
            RefreshPreviewAsset(target, resolver);
            desiredSceneTargets[index] = target;
        }

        for (var index = currentTargets.Count - 1; index >= 0; index--)
        {
            var currentTarget = currentTargets[index];
            if (currentTarget.Kind != BindingTargetKind.Scene)
            {
                continue;
            }

            if (!ContainsSceneTarget(desiredSceneTargets, currentTarget.TargetId))
            {
                currentTargets.RemoveAt(index);
            }
        }

        for (var desiredIndex = 0; desiredIndex < desiredSceneTargets.Length; desiredIndex++)
        {
            var desiredTarget = desiredSceneTargets[desiredIndex];
            var currentIndex = IndexOfSceneTarget(currentTargets, desiredTarget.TargetId);
            if (currentIndex < 0)
            {
                currentTargets.Insert(desiredIndex, desiredTarget);
                continue;
            }

            currentTargets[currentIndex].ApplyRefreshedStateFrom(desiredTarget);
            if (currentIndex != desiredIndex)
            {
                currentTargets.Move(currentIndex, desiredIndex);
            }
        }

        return SceneTargetsMatch(currentTargets, desiredSceneTargets);
    }

    public static bool RefreshTarget(
        ObservableCollection<BindingTargetViewModel> currentTargets,
        BindingTargetViewModel target,
        BindingTargetRowIndex rowIndex,
        IReadOnlyList<Character> characters,
        Func<string?, string?> resolveProjectAssetPath,
        IReadOnlyList<GenerationTask>? generationTasks = null)
    {
        var desiredTarget = BuildTarget(target, rowIndex, characters, generationTasks, resolveProjectAssetPath);
        if (desiredTarget is null)
        {
            return false;
        }

        RefreshPreviewAsset(desiredTarget, new BindingTargetPreviewAssetResolver(resolveProjectAssetPath));
        target.ApplyRefreshedStateFrom(desiredTarget);
        return true;
    }

    public static bool UpsertRowTarget(
        ObservableCollection<BindingTargetViewModel> currentTargets,
        AnalysisResultRowViewModel row,
        BindingTargetRowIndex rowIndex,
        IReadOnlyList<Character> characters,
        Func<string?, string?> resolveProjectAssetPath,
        IReadOnlyList<GenerationTask>? generationTasks = null)
    {
        var desiredTarget = BuildRowTarget(row, rowIndex, characters, generationTasks, resolveProjectAssetPath);
        if (desiredTarget is null)
        {
            return false;
        }

        RefreshPreviewAsset(desiredTarget, new BindingTargetPreviewAssetResolver(resolveProjectAssetPath));
        UpsertTarget(currentTargets, desiredTarget);
        return true;
    }

    public static bool UpsertRowTarget(
        ObservableCollection<BindingTargetViewModel> currentTargets,
        WorkbenchRowDraft draft,
        BindingTargetRowIndex rowIndex,
        IReadOnlyList<Character> characters,
        Func<string?, string?> resolveProjectAssetPath,
        IReadOnlyList<GenerationTask>? generationTasks = null)
    {
        var desiredTarget = BuildRowTarget(draft, rowIndex, characters, generationTasks, resolveProjectAssetPath);
        if (desiredTarget is null)
        {
            return false;
        }

        RefreshPreviewAsset(desiredTarget, new BindingTargetPreviewAssetResolver(resolveProjectAssetPath));
        UpsertTarget(currentTargets, desiredTarget);
        return true;
    }

    public static void RefreshPreviewAssets(
        IEnumerable<BindingTargetViewModel> targets,
        Func<string?, string?> resolveProjectAssetPath)
    {
        var resolver = new BindingTargetPreviewAssetResolver(resolveProjectAssetPath);
        foreach (var target in targets)
        {
            RefreshPreviewAsset(target, resolver);
        }
    }

    public static IReadOnlyList<BindingTargetViewModel> BuildTargets(
        IReadOnlyList<SceneTableViewModel> scenes,
        IReadOnlyList<Character> characters,
        Func<string?, string?>? resolveProjectAssetPath = null,
        IReadOnlyList<GenerationTask>? generationTasks = null)
    {
        var targets = new List<BindingTargetViewModel>(scenes.Count + characters.Count + 1);
        var rowIndex = BindingTargetRowIndex.FromScenes(scenes);
        for (var index = 0; index < scenes.Count; index++)
        {
            targets.Add(BindingTargetViewModel.ForScene(scenes[index]));
        }

        for (var index = 0; index < characters.Count; index++)
        {
            var character = characters[index];
            if (character.Name != "旁白")
            {
                targets.Add(BindingTargetViewModel.ForCharacterRows(
                    character,
                    rowIndex.RowsForCharacter(character),
                    resolveProjectAssetPath,
                    generationTasks));
            }
        }

        if (rowIndex.NarratorVoiceRows.Count > 0)
        {
            targets.Add(BindingTargetViewModel.ForNarrationRows(rowIndex.NarratorVoiceRows));
        }

        return targets.Count == 0 ? [] : targets.ToArray();
    }

    private static BindingTargetViewModel? BuildTarget(
        BindingTargetViewModel target,
        BindingTargetRowIndex rowIndex,
        IReadOnlyList<Character> characters,
        IReadOnlyList<GenerationTask>? generationTasks,
        Func<string?, string?>? resolveProjectAssetPath)
    {
        return target.Kind switch
        {
            BindingTargetKind.Character => BuildCharacterTarget(target, rowIndex, characters, generationTasks, resolveProjectAssetPath),
            BindingTargetKind.Narration => BindingTargetViewModel.ForNarrationRows(rowIndex.NarratorVoiceRows),
            _ => null
        };
    }

    private static BindingTargetViewModel? BuildRowTarget(
        AnalysisResultRowViewModel row,
        BindingTargetRowIndex rowIndex,
        IReadOnlyList<Character> characters,
        IReadOnlyList<GenerationTask>? generationTasks,
        Func<string?, string?>? resolveProjectAssetPath)
    {
        if (!row.UsesCharacterVisualPreview)
        {
            return rowIndex.NarratorVoiceRows.Count > 0
                ? BindingTargetViewModel.ForNarrationRows(rowIndex.NarratorVoiceRows)
                : null;
        }

        for (var index = 0; index < characters.Count; index++)
        {
            var character = characters[index];
            if (character.Name == "旁白")
            {
                continue;
            }

            if (string.Equals(character.Name, row.SubjectName, StringComparison.Ordinal) ||
                string.Equals(character.Name, row.SourceRole, StringComparison.Ordinal) ||
                string.Equals(character.Name, row.AppliedDraft?.VoiceRole, StringComparison.Ordinal))
            {
                return BindingTargetViewModel.ForCharacterRows(
                    character,
                    rowIndex.RowsForCharacter(character),
                    resolveProjectAssetPath,
                    generationTasks);
            }
        }

        for (var index = 0; index < characters.Count; index++)
        {
            var character = characters[index];
            if (character.Name == "旁白")
            {
                continue;
            }

            if (string.Equals(character.Id, row.SubjectCharacterId, StringComparison.Ordinal) ||
                string.Equals(character.Id, row.SourceRoleCharacterId, StringComparison.Ordinal) ||
                string.Equals(character.Id, row.AppliedDraft?.VoiceRoleCharacterId, StringComparison.Ordinal))
            {
                return BindingTargetViewModel.ForCharacterRows(
                    character,
                    rowIndex.RowsForCharacter(character),
                    resolveProjectAssetPath,
                    generationTasks);
            }
        }

        return null;
    }

    private static BindingTargetViewModel? BuildRowTarget(
        WorkbenchRowDraft draft,
        BindingTargetRowIndex rowIndex,
        IReadOnlyList<Character> characters,
        IReadOnlyList<GenerationTask>? generationTasks,
        Func<string?, string?>? resolveProjectAssetPath)
    {
        if (!AnalysisResultRowViewModel.UsesCharacterVisualPreviewForDraft(draft))
        {
            return rowIndex.NarratorVoiceRows.Count > 0
                ? BindingTargetViewModel.ForNarrationRows(rowIndex.NarratorVoiceRows)
                : null;
        }

        for (var index = 0; index < characters.Count; index++)
        {
            var character = characters[index];
            if (character.Name == "旁白")
            {
                continue;
            }

            if (string.Equals(character.Id, draft.SubjectCharacterId, StringComparison.Ordinal) ||
                string.Equals(character.Id, draft.SourceRoleCharacterId, StringComparison.Ordinal) ||
                string.Equals(character.Name, draft.SubjectName, StringComparison.Ordinal) ||
                string.Equals(character.Name, draft.SourceRole, StringComparison.Ordinal))
            {
                return BindingTargetViewModel.ForCharacterRows(
                    character,
                    rowIndex.RowsForCharacter(character),
                    resolveProjectAssetPath,
                    generationTasks);
            }
        }

        return null;
    }

    private static BindingTargetViewModel? BuildCharacterTarget(
        BindingTargetViewModel target,
        BindingTargetRowIndex rowIndex,
        IReadOnlyList<Character> characters,
        IReadOnlyList<GenerationTask>? generationTasks,
        Func<string?, string?>? resolveProjectAssetPath)
    {
        for (var index = 0; index < characters.Count; index++)
        {
            var character = characters[index];
            if (string.Equals(character.Id, target.TargetId, StringComparison.Ordinal))
            {
                return BindingTargetViewModel.ForCharacterRows(
                    character,
                    rowIndex.RowsForCharacter(character),
                    resolveProjectAssetPath,
                    generationTasks);
            }
        }

        return null;
    }

    public static void SyncTargets(
        ObservableCollection<BindingTargetViewModel> currentTargets,
        IReadOnlyList<BindingTargetViewModel> desiredTargets)
    {
        if (TrySyncTargetsByPosition(currentTargets, desiredTargets))
        {
            return;
        }

        var desiredKeys = BuildDesiredBindingTargetKeys(desiredTargets);

        for (var index = currentTargets.Count - 1; index >= 0; index--)
        {
            if (!desiredKeys.Contains(ToBindingTargetKey(currentTargets[index])))
            {
                currentTargets.RemoveAt(index);
            }
        }

        var currentIndexByKey = BuildCurrentBindingTargetIndex(currentTargets);
        for (var desiredIndex = 0; desiredIndex < desiredTargets.Count; desiredIndex++)
        {
            var desired = desiredTargets[desiredIndex];
            var desiredKey = ToBindingTargetKey(desired);
            if (!currentIndexByKey.TryGetValue(desiredKey, out var existingIndex))
            {
                currentTargets.Insert(desiredIndex, desired);
                RefreshCurrentBindingTargetIndexes(currentTargets, currentIndexByKey, desiredIndex);
                continue;
            }

            var existing = currentTargets[existingIndex];
            existing.ApplyRefreshedStateFrom(desired);
            if (existingIndex != desiredIndex)
            {
                currentTargets.Move(existingIndex, desiredIndex);
                RefreshCurrentBindingTargetIndexes(currentTargets, currentIndexByKey, Math.Min(existingIndex, desiredIndex));
            }
        }
    }

    private static void UpsertTarget(
        ObservableCollection<BindingTargetViewModel> currentTargets,
        BindingTargetViewModel desiredTarget)
    {
        var desiredKey = ToBindingTargetKey(desiredTarget);
        for (var index = 0; index < currentTargets.Count; index++)
        {
            var currentTarget = currentTargets[index];
            if (ToBindingTargetKey(currentTarget).Equals(desiredKey))
            {
                currentTarget.ApplyRefreshedStateFrom(desiredTarget);
                return;
            }
        }

        var insertIndex = ResolveTargetInsertIndex(currentTargets, desiredTarget);
        currentTargets.Insert(insertIndex, desiredTarget);
    }

    private static int ResolveTargetInsertIndex(
        IReadOnlyList<BindingTargetViewModel> currentTargets,
        BindingTargetViewModel desiredTarget)
    {
        if (desiredTarget.Kind == BindingTargetKind.Scene)
        {
            return CountTargetsOfKindBefore(currentTargets, BindingTargetKind.Scene, currentTargets.Count);
        }

        var sceneTargetCount = CountTargetsOfKindBefore(currentTargets, BindingTargetKind.Scene, currentTargets.Count);
        if (desiredTarget.Kind == BindingTargetKind.Character)
        {
            var characterInsertIndex = sceneTargetCount;
            while (characterInsertIndex < currentTargets.Count &&
                   currentTargets[characterInsertIndex].Kind == BindingTargetKind.Character &&
                   string.CompareOrdinal(currentTargets[characterInsertIndex].DisplayName, desiredTarget.DisplayName) <= 0)
            {
                characterInsertIndex++;
            }

            return characterInsertIndex;
        }

        return currentTargets.Count;
    }

    private static int CountTargetsOfKindBefore(
        IReadOnlyList<BindingTargetViewModel> currentTargets,
        BindingTargetKind kind,
        int endExclusive)
    {
        var count = 0;
        var end = Math.Clamp(endExclusive, 0, currentTargets.Count);
        for (var index = 0; index < end; index++)
        {
            if (currentTargets[index].Kind == kind)
            {
                count++;
            }
        }

        return count;
    }

    public static bool TrySyncTargetsByPosition(
        ObservableCollection<BindingTargetViewModel> currentTargets,
        IReadOnlyList<BindingTargetViewModel> desiredTargets)
    {
        if (currentTargets.Count != desiredTargets.Count)
        {
            return false;
        }

        for (var index = 0; index < currentTargets.Count; index++)
        {
            var current = currentTargets[index];
            var desired = desiredTargets[index];
            if (current.Kind != desired.Kind ||
                !string.Equals(current.TargetId, desired.TargetId, StringComparison.Ordinal))
            {
                return false;
            }
        }

        for (var index = 0; index < currentTargets.Count; index++)
        {
            currentTargets[index].ApplyRefreshedStateFrom(desiredTargets[index]);
        }

        return true;
    }

    private static void RefreshPreviewAsset(
        BindingTargetViewModel target,
        BindingTargetPreviewAssetResolver resolver)
    {
        if (target.SupportsImage)
        {
            target.ImagePreviewAsset = resolver.Resolve(target.ImageAsset) ?? string.Empty;
        }
    }

    private sealed class BindingTargetPreviewAssetResolver
    {
        private readonly Func<string?, string?> _resolve;
        private readonly Dictionary<string, string?> _resolvedByReference = new(StringComparer.Ordinal);

        public BindingTargetPreviewAssetResolver(Func<string?, string?> resolve)
        {
            _resolve = resolve;
        }

        public string? Resolve(string? reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                return null;
            }

            var key = reference.Trim();
            if (_resolvedByReference.TryGetValue(key, out var resolved))
            {
                return resolved;
            }

            resolved = _resolve(reference);
            _resolvedByReference[key] = resolved;
            return resolved;
        }
    }

    private static HashSet<BindingTargetKey> BuildDesiredBindingTargetKeys(
        IReadOnlyList<BindingTargetViewModel> desiredTargets)
    {
        var desiredKeys = new HashSet<BindingTargetKey>(desiredTargets.Count);
        foreach (var target in desiredTargets)
        {
            desiredKeys.Add(ToBindingTargetKey(target));
        }

        return desiredKeys;
    }

    private static Dictionary<BindingTargetKey, int> BuildCurrentBindingTargetIndex(
        IReadOnlyList<BindingTargetViewModel> currentTargets)
    {
        var currentIndexByKey = new Dictionary<BindingTargetKey, int>(currentTargets.Count);
        for (var index = 0; index < currentTargets.Count; index++)
        {
            currentIndexByKey[ToBindingTargetKey(currentTargets[index])] = index;
        }

        return currentIndexByKey;
    }

    private static void RefreshCurrentBindingTargetIndexes(
        IReadOnlyList<BindingTargetViewModel> currentTargets,
        Dictionary<BindingTargetKey, int> currentIndexByKey,
        int startIndex)
    {
        for (var index = Math.Max(startIndex, 0); index < currentTargets.Count; index++)
        {
            currentIndexByKey[ToBindingTargetKey(currentTargets[index])] = index;
        }
    }

    private static void RefreshSceneTargetIndexes(
        IReadOnlyList<BindingTargetViewModel> currentTargets,
        Dictionary<string, int> sceneTargetIndexById,
        int startIndex)
    {
        for (var index = Math.Max(startIndex, 0); index < currentTargets.Count; index++)
        {
            var target = currentTargets[index];
            if (target.Kind == BindingTargetKind.Scene)
            {
                sceneTargetIndexById[target.TargetId] = index;
            }
        }
    }

    private static bool ContainsSceneTarget(
        IReadOnlyList<BindingTargetViewModel> targets,
        string sceneId)
    {
        return IndexOfSceneTarget(targets, sceneId) >= 0;
    }

    private static int IndexOfSceneTarget(
        IReadOnlyList<BindingTargetViewModel> targets,
        string sceneId)
    {
        for (var index = 0; index < targets.Count; index++)
        {
            var target = targets[index];
            if (target.Kind == BindingTargetKind.Scene &&
                string.Equals(target.TargetId, sceneId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool SceneTargetsMatch(
        IReadOnlyList<BindingTargetViewModel> currentTargets,
        IReadOnlyList<BindingTargetViewModel> desiredSceneTargets)
    {
        if (currentTargets.Count < desiredSceneTargets.Count)
        {
            return false;
        }

        for (var index = 0; index < desiredSceneTargets.Count; index++)
        {
            var current = currentTargets[index];
            var desired = desiredSceneTargets[index];
            if (current.Kind != BindingTargetKind.Scene ||
                desired.Kind != BindingTargetKind.Scene ||
                !string.Equals(current.TargetId, desired.TargetId, StringComparison.Ordinal))
            {
                return false;
            }
        }

        for (var index = desiredSceneTargets.Count; index < currentTargets.Count; index++)
        {
            if (currentTargets[index].Kind == BindingTargetKind.Scene)
            {
                return false;
            }
        }

        return true;
    }

    private static BindingTargetKey ToBindingTargetKey(BindingTargetViewModel target)
    {
        return new BindingTargetKey(target.Kind, target.TargetId);
    }

    private readonly record struct BindingTargetKey(BindingTargetKind Kind, string TargetId);
}
