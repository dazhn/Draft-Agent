using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DraftAgent.App.ViewModels;

public sealed partial class EmotionEditorSessionViewModel : ObservableObject
{
    private static readonly IReadOnlyList<string> DisplayOrder =
    [
        "Happy",
        "Sad",
        "Angry",
        "Calm",
        "Surprised",
        "Melancholic",
        "Afraid",
        "Disgusted"
    ];

    private readonly AnalysisResultRowViewModel _targetRow;

    public EmotionEditorSessionViewModel(AnalysisResultRowViewModel targetRow)
    {
        _targetRow = targetRow ?? throw new ArgumentNullException(nameof(targetRow));
        SubjectName = targetRow.SubjectName;
        DisplayText = targetRow.EffectiveEmotionDisplayText;
        var sourceVectors = targetRow.EmotionVectors.ToDictionary(vector => vector.Key, StringComparer.Ordinal);
        EmotionVectors = new ObservableCollection<EmotionVectorViewModel>(DisplayOrder
            .Select(key => sourceVectors.TryGetValue(key, out var vector)
                ? new EmotionVectorViewModel(vector.Key, vector.Value)
                : new EmotionVectorViewModel(key, 0)));
    }

    public AnalysisResultRowViewModel TargetRow => _targetRow;

    public string? SubjectName { get; }

    public string DisplayText { get; }

    public ObservableCollection<EmotionVectorViewModel> EmotionVectors { get; }

    public void Save()
    {
        var valuesByKey = EmotionVectors.ToDictionary(vector => vector.Key, vector => vector.Value, StringComparer.Ordinal);
        foreach (var targetVector in _targetRow.EmotionVectors)
        {
            if (valuesByKey.TryGetValue(targetVector.Key, out var value))
            {
                targetVector.Value = value;
            }
        }
    }
}
