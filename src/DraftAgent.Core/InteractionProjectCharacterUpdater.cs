namespace DraftAgent.Core;

public sealed class InteractionProjectCharacterUpdater
{
    public IReadOnlyList<Character> ApplyBindings(
        IReadOnlyList<Character> characters,
        IReadOnlyList<InteractionScene> scenes,
        IReadOnlyList<WorkbenchCharacterBindingEdit>? characterBindingEdits)
    {
        var interactionsByCharacterId = scenes
            .SelectMany(scene => scene.Interactions)
            .Where(interaction => !string.IsNullOrWhiteSpace(interaction.SubjectCharacterId))
            .GroupBy(interaction => interaction.SubjectCharacterId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var defaultBindingsByCharacterId = (characterBindingEdits ?? [])
            .GroupBy(binding => binding.CharacterId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);

        return characters.Select(character =>
        {
            defaultBindingsByCharacterId.TryGetValue(character.Id, out var defaultBinding);
            if (!interactionsByCharacterId.TryGetValue(character.Id, out var interactions))
            {
                return ApplyDefaultBinding(character, defaultBinding);
            }

            var portrait = ResolvePortrait(character, defaultBinding);
            var voice = interactions
                .Select(interaction => interaction.Binding.SubjectVoiceReferenceId)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                ?? WorkbenchProjectBindingValue.Normalize(defaultBinding?.VoiceBinding?.EffectiveVoiceReferenceId);

            return character with
            {
                AssetBinding = character.AssetBinding with
                {
                    Portrait = portrait,
                    Voice = voice,
                    PortraitVisualBinding = ResolvePortraitVisualBinding(interactions, defaultBinding, portrait),
                    VoiceBinding = ResolveVoiceBinding(interactions, defaultBinding, voice)
                }
            };
        }).ToArray();
    }

    private static string? ResolvePortrait(
        Character character,
        WorkbenchCharacterBindingEdit? defaultBinding)
    {
        return WorkbenchProjectBindingValue.Normalize(defaultBinding?.VisualBinding?.EffectiveAssetId)
            ?? WorkbenchProjectBindingValue.Normalize(character.AssetBinding.PortraitVisualBinding?.EffectiveAssetId)
            ?? WorkbenchProjectBindingValue.Normalize(character.AssetBinding.Portrait);
    }

    private static VisualAssetBinding ResolvePortraitVisualBinding(
        IReadOnlyList<CharacterInteraction> interactions,
        WorkbenchCharacterBindingEdit? defaultBinding,
        string? portrait)
    {
        return defaultBinding?.VisualBinding
            ?? VisualAssetBinding.FromImage(portrait);
    }

    private static VoiceAssetBinding ResolveVoiceBinding(
        IReadOnlyList<CharacterInteraction> interactions,
        WorkbenchCharacterBindingEdit? defaultBinding,
        string? voice)
    {
        if (defaultBinding?.VoiceBinding is { } voiceBinding)
        {
            return voiceBinding;
        }

        var rowVoiceBinding = interactions
            .Select(interaction => InteractionVoiceBinding(interaction))
            .FirstOrDefault(binding => binding is { } && binding != VoiceAssetBinding.None);
        return rowVoiceBinding ?? VoiceAssetBinding.FromProjectReference(voice, null, null, null);
    }

    private static VoiceAssetBinding? InteractionVoiceBinding(CharacterInteraction interaction)
    {
        var voice = interaction.Narration?.Voice ?? interaction.Dialogue?.Voice ?? interaction.InnerMonologue?.Voice;
        var voiceReferenceId = interaction.Narration is not null
            ? interaction.Binding.NarratorVoiceReferenceId
            : interaction.Binding.SubjectVoiceReferenceId;
        var ttsTask = interaction.Tasks.FirstOrDefault(task => task.Type == GenerationTaskType.Tts);
        if (ttsTask is not null)
        {
            return VoiceAssetBinding.FromProjectReference(
                ReadParameter(ttsTask, "voiceReferenceId") ?? voiceReferenceId,
                ReadParameter(ttsTask, "referenceText") ?? voice?.ReferenceText,
                ReadParameter(ttsTask, "voiceSourceAssetId"),
                ReadParameter(ttsTask, "voiceSyncedPath"));
        }

        return VoiceAssetBinding.FromProjectReference(voiceReferenceId, voice?.ReferenceText, null, null);
    }

    private static string? ReadParameter(GenerationTask task, string key)
    {
        return task.Parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    private static Character ApplyDefaultBinding(Character character, WorkbenchCharacterBindingEdit? binding)
    {
        if (binding is null)
        {
            return character;
        }

        return character with
        {
            AssetBinding = character.AssetBinding with
            {
                Portrait = WorkbenchProjectBindingValue.Normalize(binding.VisualBinding?.EffectiveAssetId),
                Voice = WorkbenchProjectBindingValue.Normalize(binding.VoiceBinding?.EffectiveVoiceReferenceId),
                PortraitVisualBinding = binding.VisualBinding is { SourceKind: not VisualSourceKind.None }
                    ? binding.VisualBinding
                    : VisualAssetBinding.None,
                VoiceBinding = binding.VoiceBinding ?? VoiceAssetBinding.None
            }
        };
    }
}
