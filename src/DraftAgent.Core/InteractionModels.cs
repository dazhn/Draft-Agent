using System.Text.Json.Serialization;

namespace DraftAgent.Core;

public sealed record InteractionProject(
    string ProjectId,
    string Title,
    ScriptDocument Script,
    IReadOnlyList<Character> Characters,
    IReadOnlyList<InteractionScene> Scenes,
    IReadOnlyList<GenerationTask> GenerationTasks,
    IReadOnlyList<DraftIssue> Issues)
{
    public IReadOnlyList<ProjectScriptDraft> ScriptDrafts { get; init; } = [];

    public int SelectedScriptDraftIndex { get; init; }
}

public sealed record ProjectScriptDraft(string Title, string Text, bool CanClose);

public sealed record InteractionScene(
    string Id,
    int Order,
    string Name,
    string Location,
    string TimeOfDay,
    string Weather,
    string Mood,
    string Description,
    SceneAssetBinding AssetBinding,
    IReadOnlyList<CharacterInteraction> Interactions);

public sealed record CharacterInteraction(
    string Id,
    int Order,
    InteractionType Type,
    string SceneId,
    string? SubjectCharacterId,
    string? TargetCharacterId,
    EmotionSpec Emotion,
    ActionSpec Action,
    DialogueLine? Dialogue,
    InnerMonologue? InnerMonologue,
    NarrationLine? Narration,
    InteractionBinding Binding,
    IReadOnlyList<GenerationTask> Tasks,
    string SourceSegmentId)
{
    public string SourceItemType { get; init; } = string.Empty;

    public string SourceRole { get; init; } = string.Empty;

    public string? SourceRoleCharacterId { get; init; }

    public string? VoiceRole { get; init; }

    public string? VoiceRoleCharacterId { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InteractionType
{
    SceneEstablish,
    SoloAction,
    CharacterEnter,
    Dialogue,
    Reaction,
    InnerMonologue,
    Narration,
    EmotionShift
}

public sealed record DialogueLine(string Id, string SpeakerCharacterId, string? TargetCharacterId, string Text, VoiceRenderSettings Voice);

public sealed record InnerMonologue(string Id, string CharacterId, string Text, VoiceRenderSettings Voice);

public sealed record NarrationLine(string Id, string Text, VoiceRenderSettings Voice);

public sealed record VoiceRenderSettings(
    string? VoiceReferenceId,
    string Emotion,
    double EmotionIntensity,
    double Speed,
    double PauseAfterSeconds,
    IReadOnlyDictionary<string, double>? EmotionVectors = null,
    double EmotionAlpha = 1,
    string? ReferenceText = null);

public sealed record InteractionBinding(
    string? SubjectVoiceReferenceId,
    string? NarratorVoiceReferenceId,
    VisualAssetBinding? SceneVisualBinding = null,
    VisualAssetBinding? SubjectCharacterVisualBinding = null,
    VisualAssetBinding? TargetCharacterVisualBinding = null);

public sealed record GenerationTask(
    string Id,
    GenerationTaskType Type,
    string TargetId,
    string DisplayName,
    string Provider,
    GenerationTaskStatus Status,
    IReadOnlyDictionary<string, string> Parameters,
    string? ResultAssetId = null);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GenerationTaskType
{
    Tts,
    Video,
    AmbientAudio
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GenerationTaskStatus
{
    Draft,
    Pending,
    Running,
    Completed,
    Failed
}

public sealed record LlmAnalysisRequest(string RawText, string JsonSchemaName, string Instructions);

public sealed record LlmAnalysisResponse(string RawJson, InteractionProject Project, string? ModelRawJson = null, AiModelReplyMetadata? ReplyMetadata = null);

public sealed record ComfyTaskRequest(GenerationTask Task);

public sealed record ComfyTaskResult(string TaskId, GenerationTaskStatus Status, string? ResultAssetId, string? Message, ComfyPromptMetadata? PromptMetadata = null);
