using System.Globalization;
using System.Text.Json.Nodes;

namespace DraftAgent.Core;

internal static class ComfyWorkflowPatcher
{
    public static void Patch(JsonObject workflow, GenerationTask task)
    {
        if (task.Type == GenerationTaskType.Tts)
        {
            PatchTtsWorkflow(workflow, task);
            return;
        }

        throw new NotSupportedException("ComfyUI workflow patching only supports TTS tasks; AI image/video generation has been removed.");
    }

    private static void PatchTtsWorkflow(JsonObject workflow, GenerationTask task)
    {
        var text = Parameter(task, "text");
        if (HasNode(workflow, "137"))
        {
            SetInput(workflow, "137", "value", text);
        }
        else
        {
            SetInput(workflow, "47", "text", text);
        }

        TtsTaskValidation.RequireVoiceReference(task);
        var voiceReferenceId = NormalizeVoiceReferenceId(Parameter(task, "voiceReferenceId"));
        SetInput(workflow, "51", "voice_name", voiceReferenceId);
        TrySetInput(workflow, "51", "reference_text", Parameter(task, "referenceText", ResolveReferenceText(voiceReferenceId)));
        TrySetInput(workflow, "123", "model_path", Parameter(task, "modelPath", IndexTts2Defaults.DefaultModelPath));
        TrySetNumericInput(workflow, "123", "emotion_alpha", Parameter(task, "emotionAlpha", "1"));
        TrySetNumericInput(workflow, "123", "max_text_tokens_per_segment", Parameter(task, "maxTextTokensPerSegment", IndexTts2Defaults.SafeMaxTextTokensPerSegment.ToString(CultureInfo.InvariantCulture)));
        TrySetNumericInput(workflow, "123", "num_beams", Parameter(task, "numBeams", IndexTts2Defaults.SafeNumBeams.ToString(CultureInfo.InvariantCulture)));
        TrySetNumericInput(workflow, "123", "max_mel_tokens", Parameter(task, "maxMelTokens", IndexTts2Defaults.SafeMaxMelTokens.ToString(CultureInfo.InvariantCulture)));
        TrySetBooleanInput(workflow, "123", "use_torch_compile", Parameter(task, "useTorchCompile", bool.FalseString));
        TrySetBooleanInput(workflow, "123", "low_vram", Parameter(task, "lowVram", bool.FalseString));
        ApplyEmotion(workflow, task);
    }

    private static void ApplyEmotion(JsonObject workflow, GenerationTask task)
    {
        var emotionNodeId = ResolveEmotionNodeId(workflow);
        if (emotionNodeId is null ||
            !workflow.TryGetPropertyValue(emotionNodeId, out var emotionNode) ||
            emotionNode is not JsonObject emotionObject ||
            !emotionObject.TryGetPropertyValue("inputs", out var inputsNode) ||
            inputsNode is not JsonObject inputs)
        {
            return;
        }

        foreach (var key in IndexTts2Defaults.EmotionKeys)
        {
            inputs[key] = 0;
        }

        if (!TryApplyExplicitEmotionVectors(inputs, task.Parameters))
        {
            var emotion = Parameter(task, "emotion");
            var normalized = IndexTts2Defaults.NormalizeEmotion(emotion);
            inputs[normalized] = IndexTts2Defaults.ResolveEmotionStrength(emotion, Parameter(task, "emotionStrength"));
        }

        SetEmotionControl(workflow, emotionNodeId);
    }

    private static bool TryApplyExplicitEmotionVectors(JsonObject inputs, IReadOnlyDictionary<string, string> parameters)
    {
        var hasAny = false;
        foreach (var key in IndexTts2Defaults.EmotionKeys)
        {
            if (!parameters.TryGetValue($"emotionVector.{key}", out var value) ||
                !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                continue;
            }

            inputs[key] = Math.Clamp(parsed, 0, IndexTts2Defaults.MaxEmotionStrength);
            hasAny = true;
        }

        return hasAny;
    }

    private static string? ResolveEmotionNodeId(JsonObject workflow)
    {
        foreach (var nodeId in new[] { "125", "134" })
        {
            if (IsClassType(workflow, nodeId, "IndexTTSEmotionOptionsNode"))
            {
                return nodeId;
            }
        }

        foreach (var node in workflow)
        {
            if (node.Value is JsonObject nodeObject &&
                nodeObject.TryGetPropertyValue("class_type", out var classType) &&
                classType?.GetValue<string>() == "IndexTTSEmotionOptionsNode")
            {
                return node.Key;
            }
        }

        return null;
    }

    private static void SetEmotionControl(JsonObject workflow, string emotionNodeId)
    {
        if (!workflow.TryGetPropertyValue("123", out var engineNode) ||
            engineNode is not JsonObject engineObject ||
            !engineObject.TryGetPropertyValue("inputs", out var inputsNode) ||
            inputsNode is not JsonObject inputs)
        {
            return;
        }

        inputs["emotion_control"] = new JsonArray(emotionNodeId, 0);
    }

    private static bool HasNode(JsonObject workflow, string nodeId)
    {
        return workflow.TryGetPropertyValue(nodeId, out var node) && node is JsonObject;
    }

    private static bool IsClassType(JsonObject workflow, string nodeId, string classType)
    {
        return workflow.TryGetPropertyValue(nodeId, out var node) &&
            node is JsonObject nodeObject &&
            nodeObject.TryGetPropertyValue("class_type", out var value) &&
            value?.GetValue<string>() == classType;
    }

    private static void TrySetInput(JsonObject workflow, string nodeId, string inputName, string value)
    {
        if (!workflow.TryGetPropertyValue(nodeId, out var node) ||
            node is not JsonObject nodeObject ||
            !nodeObject.TryGetPropertyValue("inputs", out var inputsNode) ||
            inputsNode is not JsonObject inputs)
        {
            return;
        }

        inputs[inputName] = value;
    }

    private static void TrySetNumericInput(JsonObject workflow, string nodeId, string inputName, string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
            !workflow.TryGetPropertyValue(nodeId, out var node) ||
            node is not JsonObject nodeObject ||
            !nodeObject.TryGetPropertyValue("inputs", out var inputsNode) ||
            inputsNode is not JsonObject inputs)
        {
            return;
        }

        inputs[inputName] = number;
    }

    private static void TrySetBooleanInput(JsonObject workflow, string nodeId, string inputName, string value)
    {
        if (!bool.TryParse(value, out var boolean) ||
            !workflow.TryGetPropertyValue(nodeId, out var node) ||
            node is not JsonObject nodeObject ||
            !nodeObject.TryGetPropertyValue("inputs", out var inputsNode) ||
            inputsNode is not JsonObject inputs)
        {
            return;
        }

        inputs[inputName] = boolean;
    }

    private static string ResolveReferenceText(string voiceReferenceId)
    {
        return IndexTts2Defaults.IsDefaultVoiceReference(voiceReferenceId)
            ? IndexTts2Defaults.ReadDefaultReferenceText()
            : string.Empty;
    }

    private static string NormalizeVoiceReferenceId(string voiceReferenceId)
    {
        if (string.IsNullOrWhiteSpace(voiceReferenceId) ||
            voiceReferenceId.Trim().StartsWith("未绑定", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(TtsTaskValidation.MissingVoiceReferenceMessage);
        }

        var normalized = voiceReferenceId.Trim();
        if (!TtsTaskValidation.IsSafeComfyVoiceFileName(normalized))
        {
            throw new InvalidOperationException(TtsTaskValidation.InvalidVoiceReferenceMessage);
        }

        return normalized;
    }

    private static void SetInput(JsonObject workflow, string nodeId, string inputName, string value)
    {
        if (!workflow.TryGetPropertyValue(nodeId, out var node) || node is not JsonObject nodeObject)
        {
            throw new InvalidOperationException($"ComfyUI workflow is missing node {nodeId}.");
        }

        if (!nodeObject.TryGetPropertyValue("inputs", out var inputsNode) || inputsNode is not JsonObject inputs)
        {
            throw new InvalidOperationException($"ComfyUI workflow node {nodeId} is missing inputs.");
        }

        inputs[inputName] = value;
    }

    private static string Parameter(GenerationTask task, string key, string fallback = "")
    {
        return task.Parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
    }
}
