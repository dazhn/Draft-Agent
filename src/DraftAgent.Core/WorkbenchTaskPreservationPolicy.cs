namespace DraftAgent.Core;

public interface IWorkbenchTaskPreservationPolicy
{
    GenerationTask? PreserveTtsTask(GenerationTask? existingTask, IReadOnlyDictionary<string, string> requiredParameters);

    GenerationTask? PreserveVisualTask(GenerationTask? existingTask, IReadOnlyDictionary<string, string> requiredParameters);
}

public sealed class WorkbenchTaskPreservationPolicy : IWorkbenchTaskPreservationPolicy
{
    public GenerationTask? PreserveTtsTask(GenerationTask? existingTask, IReadOnlyDictionary<string, string> requiredParameters)
    {
        if (!HasMatchingParameters(
            existingTask,
            requiredParameters.Where(parameter => !IsNonRenderingVoiceSyncParameter(parameter.Key))))
        {
            return null;
        }

        return MergeNonRenderingVoiceSyncParameters(existingTask!, requiredParameters);
    }

    public GenerationTask? PreserveVisualTask(GenerationTask? existingTask, IReadOnlyDictionary<string, string> requiredParameters)
    {
        if (!HasMatchingParameters(existingTask, requiredParameters.Where(parameter => parameter.Key != "sceneId")))
        {
            return null;
        }

        return PreserveVisualSceneState(existingTask!, requiredParameters);
    }

    private static GenerationTask PreserveVisualSceneState(GenerationTask existingTask, IReadOnlyDictionary<string, string> requiredParameters)
    {
        var parameters = existingTask.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var previousSceneId = parameters.TryGetValue("sceneId", out var existingSceneId) ? existingSceneId : string.Empty;
        foreach (var parameter in requiredParameters)
        {
            parameters[parameter.Key] = parameter.Value;
        }

        if (requiredParameters.TryGetValue("sceneId", out var nextSceneId) &&
            !string.Equals(previousSceneId, nextSceneId, StringComparison.Ordinal))
        {
            parameters["sceneChanged"] = "true";
        }
        else
        {
            parameters.Remove("sceneChanged");
        }

        return existingTask with { Parameters = parameters };
    }

    private static bool HasMatchingParameters(GenerationTask? existingTask, IEnumerable<KeyValuePair<string, string>> requiredParameters)
    {
        if (existingTask is null)
        {
            return false;
        }

        foreach (var parameter in requiredParameters)
        {
            if (!existingTask.Parameters.TryGetValue(parameter.Key, out var existingValue))
            {
                if (CanIgnoreMissingOptionalEmotionParameter(parameter.Key, parameter.Value))
                {
                    continue;
                }

                return false;
            }

            if (!ParameterMatches(parameter.Key, existingValue, parameter.Value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CanIgnoreMissingOptionalEmotionParameter(string key, string requiredValue)
    {
        if (key == "emotionAlpha")
        {
            return string.IsNullOrWhiteSpace(requiredValue) || requiredValue == "1";
        }

        if (key.StartsWith("emotionVector.", StringComparison.Ordinal))
        {
            return string.IsNullOrWhiteSpace(requiredValue) || requiredValue == "0";
        }

        return false;
    }

    private static bool IsNonRenderingVoiceSyncParameter(string key)
    {
        return key is "voiceSourceAssetId" or "voiceSyncedPath";
    }

    private static GenerationTask MergeNonRenderingVoiceSyncParameters(
        GenerationTask existingTask,
        IReadOnlyDictionary<string, string> requiredParameters)
    {
        var parameters = existingTask.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var changed = false;
        foreach (var parameter in requiredParameters.Where(parameter => IsNonRenderingVoiceSyncParameter(parameter.Key)))
        {
            if (string.IsNullOrWhiteSpace(parameter.Value))
            {
                changed |= parameters.Remove(parameter.Key);
                continue;
            }

            if (!parameters.TryGetValue(parameter.Key, out var existingValue) ||
                !string.Equals(existingValue, parameter.Value, StringComparison.Ordinal))
            {
                parameters[parameter.Key] = parameter.Value;
                changed = true;
            }
        }

        return changed ? existingTask with { Parameters = parameters } : existingTask;
    }

    private static bool ParameterMatches(string key, string existingValue, string requiredValue)
    {
        if (existingValue == requiredValue)
        {
            return true;
        }

        if (key == "emotion" &&
            string.IsNullOrWhiteSpace(existingValue) &&
            IndexTts2Defaults.NormalizeEmotion(requiredValue) == "Calm")
        {
            return true;
        }

        if (key == "emotionStrength" &&
            IsEmptyStrength(existingValue) &&
            IsCalmDefaultStrength(requiredValue))
        {
            return true;
        }

        return false;
    }

    private static bool IsEmptyStrength(string value)
    {
        return string.IsNullOrWhiteSpace(value) || value == "0";
    }

    private static bool IsCalmDefaultStrength(string value)
    {
        return double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) &&
            parsed > 0 &&
            parsed <= IndexTts2Defaults.DefaultEmotionStrength;
    }
}
