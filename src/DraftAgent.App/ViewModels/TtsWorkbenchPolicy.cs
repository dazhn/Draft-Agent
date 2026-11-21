using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DraftAgent.Core;

namespace DraftAgent.App.ViewModels;

public static partial class TtsWorkbenchPolicy
{
    public static IReadOnlyList<string> BuildVoiceCatalogOptions(IEnumerable<string> voiceNames)
    {
        return BuildVoiceCatalogOptions(voiceNames, []);
    }

    public static IReadOnlyList<string> BuildVoiceCatalogOptions(
        IEnumerable<string> primaryVoiceNames,
        IEnumerable<string> fallbackVoiceNames)
    {
        var options = new List<string> { IndexTts2Defaults.DefaultVoiceReferenceId };
        var seenReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            IndexTts2Defaults.DefaultVoiceReferenceId
        };
        var localFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var voice in primaryVoiceNames
            .Select(NormalizeVoiceReferenceOption)
            .Where(voice => !string.IsNullOrWhiteSpace(voice))
            .Where(IsVisibleVoiceReferenceOption)
            .OrderBy(voice => voice, StringComparer.OrdinalIgnoreCase))
        {
            if (!seenReferences.Add(voice))
            {
                continue;
            }

            localFileNames.Add(Path.GetFileName(voice));
            options.Add(voice);
        }

        foreach (var voice in fallbackVoiceNames
            .Select(NormalizeVoiceReferenceOption)
            .Where(voice => !string.IsNullOrWhiteSpace(voice))
            .Where(IsVisibleVoiceReferenceOption)
            .OrderBy(voice => voice, StringComparer.OrdinalIgnoreCase))
        {
            if (localFileNames.Contains(Path.GetFileName(voice)) ||
                !seenReferences.Add(voice))
            {
                continue;
            }

            options.Add(voice);
        }

        return options.ToArray();
    }

    public static bool IsVisibleVoiceReferenceOption(string? value)
    {
        var normalized = NormalizeVoiceReferenceOption(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (string.Equals(normalized, IndexTts2Defaults.DefaultVoiceReferenceId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!TtsTaskValidation.IsSafeComfyVoiceFileName(normalized))
        {
            return false;
        }

        if (normalized.StartsWith("voices_examples/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/voices_examples/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("custom_nodes/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/custom_nodes/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(fileName)))
        {
            return false;
        }

        return !MediaFileNamePolicy.HasInternalIdPrefix(fileName);
    }

    public static IReadOnlyDictionary<string, string> BuildRegenerateRowOverrides(AnalysisResultRowViewModel row)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["text"] = row.LineText,
            ["emotion"] = ResolveRowEmotionParameter(row),
            ["emotionStrength"] = ResolveRowEmotionStrengthParameter(row)
        };

        if (row.EmotionVectorMap is { Count: > 0 } emotionVectors)
        {
            parameters["emotionAlpha"] = row.EmotionAlpha.ToString("0.##", CultureInfo.InvariantCulture);
            foreach (var pair in emotionVectors.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                parameters[$"emotionVector.{pair.Key}"] = Math.Clamp(pair.Value, 0, 1)
                    .ToString("0.##", CultureInfo.InvariantCulture);
            }
        }

        return parameters;
    }

    public static string ResolveRowEmotionParameter(AnalysisResultRowViewModel row)
    {
        var emotion = row.EffectiveEmotionText.Trim();
        var separatorIndex = emotion.IndexOf(' ');
        var primaryEmotion = separatorIndex > 0 ? emotion[..separatorIndex] : emotion;
        return IndexTts2Defaults.NormalizeEmotion(primaryEmotion);
    }

    public static string ResolveRowEmotionStrengthParameter(AnalysisResultRowViewModel row)
    {
        return IndexTts2Defaults
            .ResolveEmotionStrength(row.EffectiveEmotionText, null)
            .ToString("0.##", CultureInfo.InvariantCulture);
    }

    public static string ResolvePlayableAudioTarget(string asset, string comfyEndpoint)
    {
        if (Path.IsPathRooted(asset) ||
            asset.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            asset.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return asset;
        }

        if (!TtsGeneratedAudioReference.IsSupportedAudioReference(asset))
        {
            throw new InvalidOperationException("TTS preview only supports audio output references.");
        }

        var endpoint = NormalizeEndpoint(comfyEndpoint);
        var parsed = ComfyOutputReference.Parse(asset);
        return $"{endpoint}/view?{parsed.ToViewQuery()}";
    }

    private static string NormalizeEndpoint(string value)
    {
        var endpoint = string.IsNullOrWhiteSpace(value) ? "http://127.0.0.1:8000" : value.Trim();
        return endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? endpoint.TrimEnd('/')
            : $"http://{endpoint.TrimEnd('/')}";
    }

    private static string NormalizeVoiceReferenceOption(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace('\\', '/');
    }

}
