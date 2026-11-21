using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using DraftAgent.Core;

namespace DraftAgent.App.ViewModels;

internal sealed class BindingTargetRowIndex
{
    private static readonly AsyncLocal<BindingTargetRowIndexMetrics?> CurrentMetrics = new();

    public static BindingTargetRowIndexMetrics? Metrics
    {
        get => CurrentMetrics.Value;
        set => CurrentMetrics.Value = value;
    }

    private readonly Dictionary<string, List<AnalysisResultRowViewModel>> _characterRowsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<AnalysisResultRowViewModel>> _characterRowsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<AnalysisResultRowViewModel>> _voiceRoleRowsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<AnalysisResultRowViewModel>> _voiceRoleRowsByName = new(StringComparer.Ordinal);

    private BindingTargetRowIndex()
    {
    }

    public IReadOnlyList<AnalysisResultRowViewModel> NarrationRows { get; private set; } = [];

    public IReadOnlyList<AnalysisResultRowViewModel> NarratorVoiceRows { get; private set; } = [];

    public IReadOnlyList<AnalysisResultRowViewModel> RowsForTarget(BindingTargetViewModel target)
    {
        return target.Kind switch
        {
            BindingTargetKind.Character => RowsForCharacter(target.TargetId, target.DisplayName),
            BindingTargetKind.Narration => NarratorVoiceRows,
            _ => []
        };
    }

    public IReadOnlyList<AnalysisResultRowViewModel> VoiceRowsForTarget(BindingTargetViewModel target)
    {
        return target.Kind == BindingTargetKind.Character
            ? VoiceRowsForCharacter(target.TargetId, target.DisplayName)
            : RowsForTarget(target);
    }

    public IReadOnlyList<AnalysisResultRowViewModel> RowsForCharacter(Character character)
    {
        return RowsForCharacter(character.Id, character.Name);
    }

    public IReadOnlyList<AnalysisResultRowViewModel> RowsForCharacter(string characterId, string characterName)
    {
        return RowsForIndexedTarget(_characterRowsById, _characterRowsByName, characterId, characterName);
    }

    public IReadOnlyList<AnalysisResultRowViewModel> VoiceRowsForCharacter(string characterId, string characterName)
    {
        var rows = new List<AnalysisResultRowViewModel>();
        var seenRows = new HashSet<AnalysisResultRowViewModel>(ReferenceEqualityComparer.Instance);
        foreach (var row in RowsForIndexedTarget(_voiceRoleRowsById, _voiceRoleRowsByName, characterId, characterName))
        {
            if (seenRows.Add(row))
            {
                rows.Add(row);
            }
        }

        return rows
            .OrderBy(row => row.OwnerScene?.Order ?? int.MaxValue)
            .ThenBy(row => row.Order)
            .ToArray();
    }

    private static IReadOnlyList<AnalysisResultRowViewModel> RowsForIndexedTarget(
        Dictionary<string, List<AnalysisResultRowViewModel>> rowsById,
        Dictionary<string, List<AnalysisResultRowViewModel>> rowsByName,
        string characterId,
        string characterName)
    {
        List<AnalysisResultRowViewModel>? byId = null;
        List<AnalysisResultRowViewModel>? byName = null;
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

        var rows = new List<AnalysisResultRowViewModel>(byId!.Count + byName!.Count);
        var seenRows = new HashSet<AnalysisResultRowViewModel>(ReferenceEqualityComparer.Instance);
        foreach (var row in byId)
        {
            if (seenRows.Add(row))
            {
                rows.Add(row);
            }
        }

        foreach (var row in byName)
        {
            if (seenRows.Add(row))
            {
                rows.Add(row);
            }
        }

        return rows;
    }

    public static BindingTargetRowIndex FromScenes(IEnumerable<SceneTableViewModel> scenes)
    {
        var stopwatch = Metrics is null ? null : Stopwatch.StartNew();
        var index = new BindingTargetRowIndex();
        var narrationRows = new List<AnalysisResultRowViewModel>();
        var narratorVoiceRows = new List<AnalysisResultRowViewModel>();
        foreach (var scene in scenes)
        {
            foreach (var row in scene.Rows)
            {
                if (string.IsNullOrWhiteSpace(row.AppliedDraft?.VoiceRole) &&
                    string.IsNullOrWhiteSpace(row.AppliedDraft?.VoiceRoleCharacterId))
                {
                    narratorVoiceRows.Add(row);
                }

                if (!row.UsesCharacterVisualPreview)
                {
                    narrationRows.Add(row);
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(row.SubjectCharacterId))
                    {
                        Add(index._characterRowsById, row.SubjectCharacterId, row);
                    }

                    if (!string.IsNullOrWhiteSpace(row.SubjectName))
                    {
                        Add(index._characterRowsByName, row.SubjectName, row);
                    }

                    if (!string.IsNullOrWhiteSpace(row.SourceRoleCharacterId))
                    {
                        Add(index._characterRowsById, row.SourceRoleCharacterId, row);
                    }

                    if (!string.IsNullOrWhiteSpace(row.SourceRole))
                    {
                        Add(index._characterRowsByName, row.SourceRole, row);
                    }
                }

                if (!string.IsNullOrWhiteSpace(row.AppliedDraft?.VoiceRoleCharacterId))
                {
                    Add(index._voiceRoleRowsById, row.AppliedDraft.VoiceRoleCharacterId, row);
                }

                if (!string.IsNullOrWhiteSpace(row.AppliedDraft?.VoiceRole))
                {
                    Add(index._voiceRoleRowsByName, row.AppliedDraft.VoiceRole, row);
                }
            }
        }

        index.NarrationRows = narrationRows;
        index.NarratorVoiceRows = narratorVoiceRows;
        if (stopwatch is not null)
        {
            stopwatch.Stop();
            Metrics?.RecordBuild(stopwatch.Elapsed);
        }

        return index;
    }

    private static void Add(
        Dictionary<string, List<AnalysisResultRowViewModel>> rowsByKey,
        string key,
        AnalysisResultRowViewModel row)
    {
        if (!rowsByKey.TryGetValue(key, out var rows))
        {
            rows = [];
            rowsByKey[key] = rows;
        }

        if (!rows.Contains(row, ReferenceEqualityComparer.Instance))
        {
            rows.Add(row);
        }
    }
}

internal sealed class BindingTargetRowIndexMetrics
{
    public int BuildCount { get; private set; }

    public TimeSpan TotalBuildElapsed { get; private set; }

    public void RecordBuild(TimeSpan elapsed)
    {
        BuildCount++;
        TotalBuildElapsed += elapsed;
    }
}
