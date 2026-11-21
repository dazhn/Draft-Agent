namespace DraftAgent.Core;

public sealed record WorkbenchDocument(
    IReadOnlyList<WorkbenchSceneDraft> Scenes,
    IReadOnlyList<WorkbenchCharacterBindingEdit> CharacterBindings)
{
    private IReadOnlyList<WorkbenchSceneDraft>? _rowsByIdSource;
    private IReadOnlyDictionary<string, WorkbenchRowDraft>? _rowsById;
    private IReadOnlyList<WorkbenchSceneDraft>? _rowLocationsByIdSource;
    private IReadOnlyDictionary<string, WorkbenchRowLocation>? _rowLocationsById;
    private IReadOnlyList<WorkbenchSceneDraft>? _sceneIndexesByIdSource;
    private IReadOnlyDictionary<string, int>? _sceneIndexesById;
    private IReadOnlyList<WorkbenchSceneDraft>? _bindingRowIndexSource;
    private WorkbenchBindingRowIndex? _bindingRowIndex;

    public IReadOnlyDictionary<string, WorkbenchRowDraft> RowsById
    {
        get
        {
            if (ReferenceEquals(_rowsByIdSource, Scenes) && _rowsById is not null)
            {
                return _rowsById;
            }

            _rowsByIdSource = Scenes;
            var rowsById = new Dictionary<string, WorkbenchRowDraft>(StringComparer.Ordinal);
            foreach (var scene in Scenes)
            {
                foreach (var row in scene.Rows)
                {
                    rowsById[row.InteractionId] = row;
                }
            }

            _rowsById = rowsById;
            return _rowsById;
        }
    }

    internal IReadOnlyDictionary<string, WorkbenchRowLocation> RowLocationsById
    {
        get
        {
            if (ReferenceEquals(_rowLocationsByIdSource, Scenes) && _rowLocationsById is not null)
            {
                return _rowLocationsById;
            }

            _rowLocationsByIdSource = Scenes;
            var locations = new Dictionary<string, WorkbenchRowLocation>(StringComparer.Ordinal);
            for (var sceneIndex = 0; sceneIndex < Scenes.Count; sceneIndex++)
            {
                var rows = Scenes[sceneIndex].Rows;
                for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    locations[rows[rowIndex].InteractionId] = new WorkbenchRowLocation(sceneIndex, rowIndex);
                }
            }

            _rowLocationsById = locations;
            return _rowLocationsById;
        }
    }

    internal IReadOnlyDictionary<string, int> SceneIndexesById
    {
        get
        {
            if (ReferenceEquals(_sceneIndexesByIdSource, Scenes) && _sceneIndexesById is not null)
            {
                return _sceneIndexesById;
            }

            _sceneIndexesByIdSource = Scenes;
            var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var index = 0; index < Scenes.Count; index++)
            {
                indexes[Scenes[index].SceneId] = index;
            }

            _sceneIndexesById = indexes;
            return _sceneIndexesById;
        }
    }

    public bool TryGetSceneDraft(string sceneId, out WorkbenchSceneDraft scene)
    {
        if (SceneIndexesById.TryGetValue(sceneId, out var index))
        {
            scene = Scenes[index];
            return true;
        }

        scene = null!;
        return false;
    }

    public bool TryGetSceneIndex(string sceneId, out int index)
    {
        return SceneIndexesById.TryGetValue(sceneId, out index);
    }

    internal WorkbenchBindingRowIndex BindingRowIndex
    {
        get
        {
            if (ReferenceEquals(_bindingRowIndexSource, Scenes) && _bindingRowIndex is not null)
            {
                return _bindingRowIndex;
            }

            _bindingRowIndexSource = Scenes;
            _bindingRowIndex = WorkbenchBindingRowIndex.FromScenes(Scenes);
            return _bindingRowIndex;
        }
    }

    public static WorkbenchDocument FromEdits(
        IReadOnlyList<WorkbenchSceneEdit> scenes,
        IReadOnlyList<WorkbenchCharacterBindingEdit>? characterBindings = null)
    {
        var orderedScenes = WorkbenchDraftOrdering.SortScenesByOrder(scenes);
        var sceneDrafts = new WorkbenchSceneDraft[orderedScenes.Count];
        for (var index = 0; index < orderedScenes.Count; index++)
        {
            sceneDrafts[index] = WorkbenchSceneDraft.FromEdit(orderedScenes[index]);
        }

        return new WorkbenchDocument(sceneDrafts, CopyCharacterBindings(characterBindings));
    }

    public IReadOnlyList<WorkbenchSceneEdit> ToSceneEdits()
    {
        var orderedScenes = WorkbenchDraftOrdering.SortScenesByOrder(Scenes);
        var sceneEdits = new WorkbenchSceneEdit[orderedScenes.Count];
        for (var sceneIndex = 0; sceneIndex < orderedScenes.Count; sceneIndex++)
        {
            var scene = orderedScenes[sceneIndex];
            var orderedRows = WorkbenchDraftOrdering.SortRowsByOrder(scene.Rows);
            var rowEdits = new WorkbenchInteractionEdit[orderedRows.Count];
            for (var rowIndex = 0; rowIndex < orderedRows.Count; rowIndex++)
            {
                var row = orderedRows[rowIndex];
                rowEdits[rowIndex] = new WorkbenchInteractionEdit(
                    row.InteractionId,
                    row.Order,
                    row.SubjectName,
                    row.Emotion,
                    row.Text,
                    row.CharacterVisualBinding,
                    row.VoiceBinding,
                    row.EmotionVectors,
                    row.EmotionAlpha,
                    row.SourceItemType,
                    row.SourceRole,
                    row.SourceRoleCharacterId,
                    row.VoiceRole,
                    row.VoiceRoleCharacterId);
            }

            sceneEdits[sceneIndex] = new WorkbenchSceneEdit(
                scene.SceneId,
                scene.Order,
                scene.Name,
                scene.Effect,
                rowEdits,
                scene.VisualBinding);
        }

        return sceneEdits;
    }

    public WorkbenchDocument WithScenes(IEnumerable<WorkbenchSceneDraft> scenes)
    {
        return this with { Scenes = WorkbenchDraftOrdering.CopyScenesByOrder(scenes) };
    }

    private static IReadOnlyList<WorkbenchCharacterBindingEdit> CopyCharacterBindings(
        IReadOnlyList<WorkbenchCharacterBindingEdit>? characterBindings)
    {
        if (characterBindings is null || characterBindings.Count == 0)
        {
            return [];
        }

        var bindings = new WorkbenchCharacterBindingEdit[characterBindings.Count];
        for (var index = 0; index < characterBindings.Count; index++)
        {
            bindings[index] = characterBindings[index];
        }

        return bindings;
    }
}

internal readonly record struct WorkbenchRowLocation(int SceneIndex, int RowIndex);

internal sealed class WorkbenchBindingRowIndex
{
    private readonly Dictionary<string, List<WorkbenchRowLocation>> _characterRowsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<WorkbenchRowLocation>> _characterRowsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<WorkbenchRowLocation>> _voiceRoleRowsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<WorkbenchRowLocation>> _voiceRoleRowsByName = new(StringComparer.Ordinal);

    private WorkbenchBindingRowIndex()
    {
    }

    public IReadOnlyList<WorkbenchRowLocation> NarrationRows { get; private set; } = [];

    public IReadOnlyList<WorkbenchRowLocation> NarratorVoiceRows { get; private set; } = [];

    public IReadOnlyList<WorkbenchRowLocation> RowsForCharacter(string characterId, string characterName)
    {
        return RowsForIndexedTarget(_characterRowsById, _characterRowsByName, characterId, characterName);
    }

    public IReadOnlyList<WorkbenchRowLocation> VoiceRowsForCharacter(string characterId, string characterName)
    {
        return RowsForIndexedTarget(_voiceRoleRowsById, _voiceRoleRowsByName, characterId, characterName)
            .OrderBy(location => location.SceneIndex)
            .ThenBy(location => location.RowIndex)
            .ToArray();
    }

    private static IReadOnlyList<WorkbenchRowLocation> RowsForIndexedTarget(
        Dictionary<string, List<WorkbenchRowLocation>> rowsById,
        Dictionary<string, List<WorkbenchRowLocation>> rowsByName,
        string characterId,
        string characterName)
    {
        List<WorkbenchRowLocation>? byId = null;
        List<WorkbenchRowLocation>? byName = null;
        var hasIdRows = !string.IsNullOrWhiteSpace(characterId) &&
            rowsById.TryGetValue(characterId, out byId);
        var hasNameRows = !string.IsNullOrWhiteSpace(characterName) &&
            rowsByName.TryGetValue(characterName, out byName);

        if (!hasIdRows)
        {
            return hasNameRows ? byName! : [];
        }

        if (!hasNameRows)
        {
            return byId!;
        }

        return MergeRowsByDocumentOrder(byId!, byName!);
    }

    private static IReadOnlyList<WorkbenchRowLocation> MergeRowsByDocumentOrder(
        IReadOnlyList<WorkbenchRowLocation> byId,
        IReadOnlyList<WorkbenchRowLocation> byName)
    {
        var rows = new List<WorkbenchRowLocation>(byId.Count + byName.Count);
        var idIndex = 0;
        var nameIndex = 0;
        while (idIndex < byId.Count && nameIndex < byName.Count)
        {
            var comparison = CompareLocations(byId[idIndex], byName[nameIndex]);
            if (comparison == 0)
            {
                rows.Add(byId[idIndex]);
                idIndex++;
                nameIndex++;
            }
            else if (comparison < 0)
            {
                rows.Add(byId[idIndex]);
                idIndex++;
            }
            else
            {
                rows.Add(byName[nameIndex]);
                nameIndex++;
            }
        }

        while (idIndex < byId.Count)
        {
            rows.Add(byId[idIndex]);
            idIndex++;
        }

        while (nameIndex < byName.Count)
        {
            rows.Add(byName[nameIndex]);
            nameIndex++;
        }

        return rows;
    }

    private static int CompareLocations(WorkbenchRowLocation left, WorkbenchRowLocation right)
    {
        var sceneComparison = left.SceneIndex.CompareTo(right.SceneIndex);
        return sceneComparison != 0 ? sceneComparison : left.RowIndex.CompareTo(right.RowIndex);
    }

    public static WorkbenchBindingRowIndex FromScenes(IReadOnlyList<WorkbenchSceneDraft> scenes)
    {
        var index = new WorkbenchBindingRowIndex();
        var narrationRows = new List<WorkbenchRowLocation>();
        var narratorVoiceRows = new List<WorkbenchRowLocation>();
        for (var sceneIndex = 0; sceneIndex < scenes.Count; sceneIndex++)
        {
            var rows = scenes[sceneIndex].Rows;
            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var row = rows[rowIndex];
                var location = new WorkbenchRowLocation(sceneIndex, rowIndex);
                if (string.IsNullOrWhiteSpace(row.VoiceRole) &&
                    string.IsNullOrWhiteSpace(row.VoiceRoleCharacterId))
                {
                    narratorVoiceRows.Add(location);
                }

                if (!IsCharacterVisualRow(row))
                {
                    narrationRows.Add(location);
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(row.SubjectCharacterId))
                    {
                        Add(index._characterRowsById, row.SubjectCharacterId, location);
                    }

                    if (!string.IsNullOrWhiteSpace(row.SubjectName))
                    {
                        Add(index._characterRowsByName, row.SubjectName, location);
                    }

                    if (!string.IsNullOrWhiteSpace(row.SourceRoleCharacterId))
                    {
                        Add(index._characterRowsById, row.SourceRoleCharacterId, location);
                    }

                    if (!string.IsNullOrWhiteSpace(row.SourceRole))
                    {
                        Add(index._characterRowsByName, row.SourceRole, location);
                    }
                }

                if (!string.IsNullOrWhiteSpace(row.VoiceRoleCharacterId))
                {
                    Add(index._voiceRoleRowsById, row.VoiceRoleCharacterId, location);
                }

                if (!string.IsNullOrWhiteSpace(row.VoiceRole))
                {
                    Add(index._voiceRoleRowsByName, row.VoiceRole, location);
                }
            }
        }

        index.NarrationRows = narrationRows;
        index.NarratorVoiceRows = narratorVoiceRows;
        return index;
    }

    private static void Add(
        Dictionary<string, List<WorkbenchRowLocation>> rowsByKey,
        string key,
        WorkbenchRowLocation row)
    {
        if (!rowsByKey.TryGetValue(key, out var rows))
        {
            rows = [];
            rowsByKey[key] = rows;
        }

        if (!rows.Contains(row))
        {
            rows.Add(row);
        }
    }

    private static bool IsCharacterVisualRow(WorkbenchRowDraft row)
    {
        return row.SourceItemType?.Trim().ToLowerInvariant() switch
        {
            "dialogue" or "action" or "thought" => true,
            "effects" or "narration" => false,
            _ => row.Kind != WorkbenchRowKind.Narration ||
                !string.IsNullOrWhiteSpace(row.SourceRoleCharacterId) ||
                !string.IsNullOrWhiteSpace(row.SourceRole) ||
                (!IsNarrationName(row.SubjectName) && !string.IsNullOrWhiteSpace(row.SubjectCharacterId))
        };
    }

    private static bool IsNarrationName(string? subjectName)
    {
        var normalized = subjectName?.Trim();
        return string.Equals(normalized, "旁白", StringComparison.Ordinal) ||
            string.Equals(normalized, "环境场效", StringComparison.Ordinal) ||
            string.Equals(normalized, "旁白叙述", StringComparison.Ordinal);
    }
}

public sealed record WorkbenchSceneDraft(
    string SceneId,
    int Order,
    string Name,
    string Effect,
    IReadOnlyList<WorkbenchRowDraft> Rows,
    VisualAssetBinding? VisualBinding = null)
{
    public static WorkbenchSceneDraft FromEdit(WorkbenchSceneEdit edit)
    {
        var orderedRows = WorkbenchDraftOrdering.SortRowsByOrder(edit.Rows);
        var rows = new WorkbenchRowDraft[orderedRows.Count];
        for (var index = 0; index < orderedRows.Count; index++)
        {
            rows[index] = WorkbenchRowDraft.FromEdit(orderedRows[index]) with { SceneId = edit.SceneId };
        }

        return new WorkbenchSceneDraft(
            edit.SceneId,
            edit.Order,
            edit.Name,
            edit.Effect,
            rows,
            edit.VisualBinding);
    }
}

internal static class WorkbenchDraftOrdering
{
    public static IReadOnlyList<WorkbenchSceneEdit> SortScenesByOrder(IReadOnlyList<WorkbenchSceneEdit> scenes)
    {
        if (IsOrderedByOrder(scenes))
        {
            return scenes;
        }

        var ordered = new WorkbenchSceneEdit[scenes.Count];
        for (var index = 0; index < scenes.Count; index++)
        {
            ordered[index] = scenes[index];
        }

        StableSortScenesByOrder(ordered);
        return ordered;
    }

    public static IReadOnlyList<WorkbenchSceneDraft> SortScenesByOrder(IReadOnlyList<WorkbenchSceneDraft> scenes)
    {
        if (IsOrderedByOrder(scenes))
        {
            return scenes;
        }

        var ordered = new WorkbenchSceneDraft[scenes.Count];
        for (var index = 0; index < scenes.Count; index++)
        {
            ordered[index] = scenes[index];
        }

        StableSortScenesByOrder(ordered);
        return ordered;
    }

    public static WorkbenchSceneDraft[] CopyScenesByOrder(IEnumerable<WorkbenchSceneDraft> scenes)
    {
        if (scenes is IReadOnlyList<WorkbenchSceneDraft> sceneList)
        {
            var ordered = new WorkbenchSceneDraft[sceneList.Count];
            for (var index = 0; index < sceneList.Count; index++)
            {
                ordered[index] = sceneList[index];
            }

            if (!IsOrderedByOrder(ordered))
            {
                StableSortScenesByOrder(ordered);
            }

            return ordered;
        }

        var collected = new List<WorkbenchSceneDraft>();
        foreach (var scene in scenes)
        {
            collected.Add(scene);
        }

        return CopyScenesByOrder(collected);
    }

    public static IReadOnlyList<WorkbenchInteractionEdit> SortRowsByOrder(IReadOnlyList<WorkbenchInteractionEdit> rows)
    {
        if (IsOrderedByOrder(rows))
        {
            return rows;
        }

        var ordered = new WorkbenchInteractionEdit[rows.Count];
        for (var index = 0; index < rows.Count; index++)
        {
            ordered[index] = rows[index];
        }

        StableSortRowsByOrder(ordered);
        return ordered;
    }

    public static IReadOnlyList<WorkbenchRowDraft> SortRowsByOrder(IReadOnlyList<WorkbenchRowDraft> rows)
    {
        if (IsOrderedByOrder(rows))
        {
            return rows;
        }

        var ordered = new WorkbenchRowDraft[rows.Count];
        for (var index = 0; index < rows.Count; index++)
        {
            ordered[index] = rows[index];
        }

        StableSortRowsByOrder(ordered);
        return ordered;
    }

    private static bool IsOrderedByOrder(IReadOnlyList<WorkbenchSceneEdit> scenes)
    {
        for (var index = 1; index < scenes.Count; index++)
        {
            if (scenes[index - 1].Order > scenes[index].Order)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsOrderedByOrder(IReadOnlyList<WorkbenchSceneDraft> scenes)
    {
        for (var index = 1; index < scenes.Count; index++)
        {
            if (scenes[index - 1].Order > scenes[index].Order)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsOrderedByOrder(IReadOnlyList<WorkbenchInteractionEdit> rows)
    {
        for (var index = 1; index < rows.Count; index++)
        {
            if (rows[index - 1].Order > rows[index].Order)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsOrderedByOrder(IReadOnlyList<WorkbenchRowDraft> rows)
    {
        for (var index = 1; index < rows.Count; index++)
        {
            if (rows[index - 1].Order > rows[index].Order)
            {
                return false;
            }
        }

        return true;
    }

    private static void StableSortScenesByOrder(WorkbenchSceneEdit[] scenes)
    {
        var indexed = new IndexedSceneEdit[scenes.Length];
        for (var index = 0; index < scenes.Length; index++)
        {
            indexed[index] = new IndexedSceneEdit(scenes[index], index);
        }

        Array.Sort(indexed, static (left, right) => CompareOrderThenIndex(left.Scene.Order, left.Index, right.Scene.Order, right.Index));
        for (var index = 0; index < indexed.Length; index++)
        {
            scenes[index] = indexed[index].Scene;
        }
    }

    private static void StableSortScenesByOrder(WorkbenchSceneDraft[] scenes)
    {
        var indexed = new IndexedSceneDraft[scenes.Length];
        for (var index = 0; index < scenes.Length; index++)
        {
            indexed[index] = new IndexedSceneDraft(scenes[index], index);
        }

        Array.Sort(indexed, static (left, right) => CompareOrderThenIndex(left.Scene.Order, left.Index, right.Scene.Order, right.Index));
        for (var index = 0; index < indexed.Length; index++)
        {
            scenes[index] = indexed[index].Scene;
        }
    }

    private static void StableSortRowsByOrder(WorkbenchInteractionEdit[] rows)
    {
        var indexed = new IndexedInteractionEdit[rows.Length];
        for (var index = 0; index < rows.Length; index++)
        {
            indexed[index] = new IndexedInteractionEdit(rows[index], index);
        }

        Array.Sort(indexed, static (left, right) => CompareOrderThenIndex(left.Row.Order, left.Index, right.Row.Order, right.Index));
        for (var index = 0; index < indexed.Length; index++)
        {
            rows[index] = indexed[index].Row;
        }
    }

    private static void StableSortRowsByOrder(WorkbenchRowDraft[] rows)
    {
        var indexed = new IndexedRowDraft[rows.Length];
        for (var index = 0; index < rows.Length; index++)
        {
            indexed[index] = new IndexedRowDraft(rows[index], index);
        }

        Array.Sort(indexed, static (left, right) => CompareOrderThenIndex(left.Row.Order, left.Index, right.Row.Order, right.Index));
        for (var index = 0; index < indexed.Length; index++)
        {
            rows[index] = indexed[index].Row;
        }
    }

    private static int CompareOrderThenIndex(int leftOrder, int leftIndex, int rightOrder, int rightIndex)
    {
        var orderComparison = leftOrder.CompareTo(rightOrder);
        return orderComparison != 0 ? orderComparison : leftIndex.CompareTo(rightIndex);
    }

    private readonly record struct IndexedSceneEdit(WorkbenchSceneEdit Scene, int Index);

    private readonly record struct IndexedSceneDraft(WorkbenchSceneDraft Scene, int Index);

    private readonly record struct IndexedInteractionEdit(WorkbenchInteractionEdit Row, int Index);

    private readonly record struct IndexedRowDraft(WorkbenchRowDraft Row, int Index);
}

public sealed record WorkbenchRowDraft(
    string InteractionId,
    string SceneId,
    int Order,
    WorkbenchRowKind Kind,
    string SubjectName,
    string? SubjectCharacterId,
    string Emotion,
    string Text,
    IReadOnlyDictionary<string, double>? EmotionVectors,
    double? EmotionAlpha,
    WorkbenchTaskLinks TaskLinks,
    VisualAssetBinding? CharacterVisualBinding = null,
    VoiceAssetBinding? VoiceBinding = null,
    string SourceItemType = "",
    string SourceRole = "",
    string? SourceRoleCharacterId = null,
    string? VoiceRole = null,
    string? VoiceRoleCharacterId = null)
{
    public static WorkbenchRowDraft FromEdit(WorkbenchInteractionEdit edit)
    {
        return new WorkbenchRowDraft(
            edit.InteractionId,
            string.Empty,
            edit.Order,
            IsNarration(edit.SubjectName) ? WorkbenchRowKind.Narration : WorkbenchRowKind.Dialogue,
            edit.SubjectName,
            null,
            edit.Emotion,
            edit.Text,
            edit.EmotionVectors,
            edit.EmotionAlpha,
            new WorkbenchTaskLinks(null),
            edit.CharacterVisualBinding,
            edit.VoiceBinding,
            edit.SourceItemType ?? string.Empty,
            edit.SourceRole ?? string.Empty,
            edit.SourceRoleCharacterId,
            edit.VoiceRole,
            edit.VoiceRoleCharacterId);
    }

    private static bool IsNarration(string? subjectName)
    {
        return string.Equals(subjectName?.Trim(), "旁白", StringComparison.Ordinal);
    }
}

public enum WorkbenchRowKind
{
    Dialogue,
    Narration
}

public sealed record WorkbenchTaskLinks(string? TtsTaskId);
