using System;
using System.Collections.Generic;

namespace DraftAgent.App.ViewModels;

internal sealed class WorkbenchRowProjectionIndex
{
    private readonly IReadOnlyList<IReadOnlyList<AnalysisResultRowViewModel>> _rowSources;
    private readonly Dictionary<string, AnalysisResultRowViewModel> _preloadedRowsById = new(StringComparer.Ordinal);
    private Dictionary<string, AnalysisResultRowViewModel>? _rowsById;

    public WorkbenchRowProjectionIndex(IEnumerable<SceneTableViewModel> scenes)
    {
        if (scenes is IReadOnlyCollection<SceneTableViewModel> sceneCollection)
        {
            var rowSources = new IReadOnlyList<AnalysisResultRowViewModel>[sceneCollection.Count];
            var index = 0;
            foreach (var scene in sceneCollection)
            {
                rowSources[index++] = scene.Rows;
            }

            _rowSources = rowSources;
            return;
        }

        var sources = new List<IReadOnlyList<AnalysisResultRowViewModel>>();
        foreach (var scene in scenes)
        {
            sources.Add(scene.Rows);
        }

        _rowSources = sources.ToArray();
    }

    public bool TryGetValue(string interactionId, out AnalysisResultRowViewModel row)
    {
        if (_preloadedRowsById.TryGetValue(interactionId, out row!))
        {
            return true;
        }

        return RowsById.TryGetValue(interactionId, out row!);
    }

    public void AddOrUpdate(AnalysisResultRowViewModel row)
    {
        _preloadedRowsById[row.InteractionId] = row;
        if (_rowsById is not null)
        {
            _rowsById[row.InteractionId] = row;
        }
    }

    private Dictionary<string, AnalysisResultRowViewModel> RowsById
    {
        get
        {
            if (_rowsById is not null)
            {
                return _rowsById;
            }

            _rowsById = new Dictionary<string, AnalysisResultRowViewModel>(StringComparer.Ordinal);
            foreach (var rows in _rowSources)
            {
                foreach (var row in rows)
                {
                    _rowsById[row.InteractionId] = row;
                }
            }

            foreach (var row in _preloadedRowsById.Values)
            {
                _rowsById[row.InteractionId] = row;
            }

            return _rowsById;
        }
    }
}
