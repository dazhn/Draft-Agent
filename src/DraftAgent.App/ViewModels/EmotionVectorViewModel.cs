using System;
using CommunityToolkit.Mvvm.ComponentModel;
using DraftAgent.Core;

namespace DraftAgent.App.ViewModels;

public sealed partial class EmotionVectorViewModel : ObservableObject
{
    [ObservableProperty]
    private double _value;

    public EmotionVectorViewModel(string key, double value)
    {
        Key = key;
        Value = Math.Clamp(value, 0, IndexTts2Defaults.MaxEmotionStrength);
    }

    public string Key { get; }

    public string DisplayName => DisplayNameFor(Key);

    public static string DisplayNameFor(string key)
    {
        return key switch
        {
            "Happy" => "开心",
            "Angry" => "愤怒",
            "Sad" => "悲伤",
            "Surprised" => "惊讶",
            "Afraid" => "害怕",
            "Disgusted" => "厌恶",
            "Calm" => "平静",
            "Melancholic" => "忧郁",
            _ => key
        };
    }

    partial void OnValueChanged(double value)
    {
        var clamped = Math.Clamp(value, 0, IndexTts2Defaults.MaxEmotionStrength);
        if (Math.Abs(clamped - value) > 0.0001)
        {
            Value = clamped;
        }
    }
}
