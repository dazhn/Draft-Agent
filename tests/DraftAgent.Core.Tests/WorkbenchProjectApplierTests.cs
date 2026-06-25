using DraftAgent.Core;

namespace DraftAgent.Core.Tests;

public sealed class WorkbenchProjectApplierTests
{
    [Fact]
    public void ApplyUsesDocumentSubjectCharacterIdWhenNameMatchesExistingCharacter()
    {
        var editingService = new WorkbenchEditingService(new InMemoryWorkflowLogger());
        var character = new Character(
            "char_chen",
            "Chen",
            "主角",
            "appearance",
            "voice",
            "Calm",
            new AssetBinding());
        var project = EmptyProject() with { Characters = [character] };
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_001",
                    1,
                    "Scene 1",
                    string.Empty,
                    [
                        new WorkbenchRowDraft(
                            "int_001",
                            "scene_001",
                            1,
                            WorkbenchRowKind.Dialogue,
                            "Chen",
                            "char_chen",
                            "Calm 0.6",
                            "Hello",
                            null,
                            null,
                            new WorkbenchTaskLinks(null))
                    ])
            ],
            []);

        var updated = new WorkbenchProjectApplier(editingService).Apply(project, document);

        var interaction = updated.Scenes.Single().Interactions.Single();
        Assert.Equal("char_chen", interaction.SubjectCharacterId);
        Assert.Single(updated.Characters);
    }


    private static InteractionProject EmptyProject()
    {
        return new InteractionProject("empty", "empty", new DraftAnalyzer().Preprocess(string.Empty), [], [], [], []);
    }
}


