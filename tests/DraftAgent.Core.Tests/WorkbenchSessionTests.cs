using DraftAgent.Core;

namespace DraftAgent.Core.Tests;

public sealed class WorkbenchSessionTests
{
    private readonly WorkbenchCommandService _commands = new();

    [Fact]
    public void ExecuteUpdatesDocumentAndMarksSessionDirty()
    {
        var document = Document();
        var session = new WorkbenchSession(document);

        var updated = session.Execute(current => _commands.UpdateScene(current, "scene_001", "Updated", "Rain"));

        Assert.True(session.IsDirty);
        Assert.Same(updated, session.Document);
        Assert.Equal("Updated", session.Document.Scenes.Single().Name);
        Assert.Equal("Rain", session.Document.Scenes.Single().Effect);
    }

    [Fact]
    public void SessionUsesImmutableDocumentSnapshotsWithoutDefensiveCopy()
    {
        var document = Document();
        var session = new WorkbenchSession(document);

        Assert.Same(document, session.Document);

        var replacement = Document("scene_002");
        session.Reset(replacement);

        Assert.Same(replacement, session.Document);
    }

    [Fact]
    public void ExecuteNoOpKeepsSessionClean()
    {
        var document = Document();
        var session = new WorkbenchSession(document);

        session.Execute(current => current);

        Assert.False(session.IsDirty);
        Assert.Equal(document.Scenes.Single().SceneId, session.Document.Scenes.Single().SceneId);
        Assert.Equal(document.Scenes.Single().Name, session.Document.Scenes.Single().Name);
    }

    [Fact]
    public void ExecuteSkipsHistoryWhenCommandReturnsSameDocumentInstance()
    {
        var document = Document();
        var session = new WorkbenchSession(document);

        var updated = session.Execute(current => _commands.UpdateScene(current, "scene_001", "Scene", string.Empty));

        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.Same(updated, session.Document);
        Assert.Equal("Scene", session.Document.Scenes.Single().Name);
    }

    [Fact]
    public void ExecuteTrustsCommandIdentityAndAddsHistoryForNewEquivalentDocument()
    {
        var document = Document();
        var session = new WorkbenchSession(document);

        var updated = session.Execute(current => current with { Scenes = current.Scenes.ToArray() });

        Assert.True(session.IsDirty);
        Assert.True(session.CanUndo);
        Assert.Same(updated, session.Document);
        Assert.NotSame(document, session.Document);
        Assert.True(session.HasSameDocument(document));
    }

    [Fact]
    public void PreviewSkipsDirtyStateWhenCommandReturnsSameDocumentInstance()
    {
        var document = DocumentWithRows();
        var session = new WorkbenchSession(document);

        session.Preview(current => _commands.UpdateRowText(current, "row_001", "Line"));

        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.Equal("Line", session.Document.RowsById["row_001"].Text);
    }

    [Fact]
    public void PreviewTrustsCommandIdentityAndMarksNewEquivalentDocumentDirty()
    {
        var document = Document();
        var session = new WorkbenchSession(document);

        var updated = session.Preview(current => current with { Scenes = current.Scenes.ToArray() });

        Assert.True(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.Same(updated, session.Document);
        Assert.NotSame(document, session.Document);
        Assert.True(session.HasSameDocument(document));
    }

    [Fact]
    public void ResetReplacesDocumentAndClearsDirtyState()
    {
        var session = new WorkbenchSession(Document());
        session.Execute(current => _commands.UpdateSceneAsset(current, "scene_001", "asset_scene_001"));
        Assert.True(session.CanUndo);

        var replacement = Document("scene_002");
        session.Reset(replacement);

        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.Equal("scene_002", session.Document.Scenes.Single().SceneId);
    }

    [Fact]
    public void RestoreReplacesDocumentAndPreservesProvidedDirtyState()
    {
        var session = new WorkbenchSession(Document());
        var replacement = Document("scene_002");

        session.Restore(replacement, isDirty: true);

        Assert.True(session.IsDirty);
        Assert.Equal("scene_002", session.Document.Scenes.Single().SceneId);

        session.Restore(Document("scene_003"), isDirty: false);

        Assert.False(session.IsDirty);
        Assert.Equal("scene_003", session.Document.Scenes.Single().SceneId);
    }

    [Fact]
    public void MarkCleanKeepsCurrentDocument()
    {
        var session = new WorkbenchSession(Document());
        session.Execute(current => _commands.UpdateSceneAsset(current, "scene_001", "asset_scene_001"));
        var currentDocument = session.Document;

        session.MarkClean();

        Assert.False(session.IsDirty);
        Assert.Equal(currentDocument, session.Document);
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.Equal("asset_scene_001", session.Document.Scenes.Single().VisualBinding?.EffectiveAssetId);
    }

    [Fact]
    public void UndoRestoresPreviousDocumentAndDirtyState()
    {
        var session = new WorkbenchSession(Document());

        session.Execute(current => _commands.UpdateScene(current, "scene_001", "Updated", "Rain"));

        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);

        var undone = session.Undo();

        Assert.True(undone);
        Assert.False(session.IsDirty);
        Assert.True(session.CanRedo);
        Assert.Equal("Scene", session.Document.Scenes.Single().Name);
        Assert.Equal(string.Empty, session.Document.Scenes.Single().Effect);
    }

    [Fact]
    public void RedoRestoresUndoneDocument()
    {
        var session = new WorkbenchSession(Document());
        session.Execute(current => _commands.UpdateScene(current, "scene_001", "Updated", "Rain"));
        session.Undo();

        var redone = session.Redo();

        Assert.True(redone);
        Assert.True(session.IsDirty);
        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.Equal("Updated", session.Document.Scenes.Single().Name);
        Assert.Equal("Rain", session.Document.Scenes.Single().Effect);
    }

    [Fact]
    public void UndoRestoresDocumentBeforeAddedScene()
    {
        var session = new WorkbenchSession(Document());

        session.Execute(current => _commands.AddScene(current, "scene_002", "Second scene"));

        Assert.Equal(2, session.Document.Scenes.Count);

        session.Undo();

        Assert.False(session.IsDirty);
        Assert.Single(session.Document.Scenes);
        Assert.Equal("scene_001", session.Document.Scenes.Single().SceneId);
    }

    [Fact]
    public void ExecuteRecordsNextUndoOperationName()
    {
        var session = new WorkbenchSession(Document());

        session.Execute(
            current => _commands.UpdateScene(current, "scene_001", "Updated", "Rain"),
            WorkbenchSessionOperationNames.UpdateSceneText);

        Assert.Equal(WorkbenchSessionOperationNames.UpdateSceneText, session.NextUndoOperationName);
        Assert.Null(session.NextRedoOperationName);
    }

    [Fact]
    public void ExecuteNoOpDoesNotRecordOperationName()
    {
        var session = new WorkbenchSession(Document());

        session.Execute(
            current => current,
            WorkbenchSessionOperationNames.UpdateSceneText);

        Assert.Null(session.NextUndoOperationName);
        Assert.Null(session.NextRedoOperationName);
    }

    [Fact]
    public void ExecuteCoalescedCombinesConsecutiveSameKeyEditsIntoOneUndoStep()
    {
        var session = new WorkbenchSession(DocumentWithRows());

        session.ExecuteCoalesced(
            current => _commands.UpdateRowText(current, "row_001", "First edit"),
            WorkbenchSessionOperationNames.UpdateRowText,
            WorkbenchSessionCoalescingKeys.RowText("row_001"));
        session.ExecuteCoalesced(
            current => _commands.UpdateRowText(current, "row_001", "Second edit"),
            WorkbenchSessionOperationNames.UpdateRowText,
            WorkbenchSessionCoalescingKeys.RowText("row_001"));

        Assert.True(session.CanUndo);
        Assert.Equal(WorkbenchSessionOperationNames.UpdateRowText, session.NextUndoOperationName);
        Assert.Equal("Second edit", session.Document.RowsById["row_001"].Text);

        session.Undo();

        Assert.False(session.CanUndo);
        Assert.True(session.CanRedo);
        Assert.Equal("Line", session.Document.RowsById["row_001"].Text);
        Assert.Equal(WorkbenchSessionOperationNames.UpdateRowText, session.NextRedoOperationName);
    }

    [Fact]
    public void ExecuteCoalescedUsesSeparateUndoStepsForDifferentKeys()
    {
        var session = new WorkbenchSession(DocumentWithRows());

        session.ExecuteCoalesced(
            current => _commands.UpdateRowText(current, "row_001", "First row edit"),
            WorkbenchSessionOperationNames.UpdateRowText,
            WorkbenchSessionCoalescingKeys.RowText("row_001"));
        session.ExecuteCoalesced(
            current => _commands.UpdateRowText(current, "row_002", "Second row edit"),
            WorkbenchSessionOperationNames.UpdateRowText,
            WorkbenchSessionCoalescingKeys.RowText("row_002"));

        session.Undo();

        Assert.True(session.CanUndo);
        Assert.Equal("First row edit", session.Document.RowsById["row_001"].Text);
        Assert.Equal("Line", session.Document.RowsById["row_002"].Text);

        session.Undo();

        Assert.False(session.CanUndo);
        Assert.Equal("Line", session.Document.RowsById["row_001"].Text);
    }

    [Fact]
    public void ExecuteCoalescedNoOpDoesNotAlterCurrentCoalescingScope()
    {
        var session = new WorkbenchSession(DocumentWithRows());

        session.ExecuteCoalesced(
            current => _commands.UpdateRowText(current, "row_001", "First edit"),
            WorkbenchSessionOperationNames.UpdateRowText,
            WorkbenchSessionCoalescingKeys.RowText("row_001"));
        session.ExecuteCoalesced(
            current => current,
            WorkbenchSessionOperationNames.UpdateRowText,
            WorkbenchSessionCoalescingKeys.RowText("row_001"));
        session.ExecuteCoalesced(
            current => _commands.UpdateRowText(current, "row_001", "Second edit"),
            WorkbenchSessionOperationNames.UpdateRowText,
            WorkbenchSessionCoalescingKeys.RowText("row_001"));

        session.Undo();

        Assert.False(session.CanUndo);
        Assert.Equal("Line", session.Document.RowsById["row_001"].Text);
    }

    [Fact]
    public void ExecuteBreaksCoalescedEditSession()
    {
        var session = new WorkbenchSession(DocumentWithRows());

        session.ExecuteCoalesced(
            current => _commands.UpdateRowText(current, "row_001", "First edit"),
            WorkbenchSessionOperationNames.UpdateRowText,
            WorkbenchSessionCoalescingKeys.RowText("row_001"));
        session.Execute(
            current => _commands.UpdateScene(current, "scene_001", "Updated", "Rain"),
            WorkbenchSessionOperationNames.UpdateSceneText);
        session.ExecuteCoalesced(
            current => _commands.UpdateRowText(current, "row_001", "Second edit"),
            WorkbenchSessionOperationNames.UpdateRowText,
            WorkbenchSessionCoalescingKeys.RowText("row_001"));

        session.Undo();

        Assert.True(session.CanUndo);
        Assert.Equal("First edit", session.Document.RowsById["row_001"].Text);
        Assert.Equal("Updated", session.Document.Scenes[0].Name);
    }

    [Fact]
    public void EndCoalescingBreaksCoalescedEditSession()
    {
        var session = new WorkbenchSession(DocumentWithRows());

        session.ExecuteCoalesced(
            current => _commands.UpdateRowText(current, "row_001", "First edit"),
            WorkbenchSessionOperationNames.UpdateRowText,
            WorkbenchSessionCoalescingKeys.RowText("row_001"));
        session.EndCoalescing();
        session.ExecuteCoalesced(
            current => _commands.UpdateRowText(current, "row_001", "Second edit"),
            WorkbenchSessionOperationNames.UpdateRowText,
            WorkbenchSessionCoalescingKeys.RowText("row_001"));

        session.Undo();

        Assert.True(session.CanUndo);
        Assert.Equal("First edit", session.Document.RowsById["row_001"].Text);

        session.Undo();

        Assert.False(session.CanUndo);
        Assert.Equal("Line", session.Document.RowsById["row_001"].Text);
    }

    [Fact]
    public void UndoRedoPreserveOperationNames()
    {
        var session = new WorkbenchSession(Document());
        session.Execute(
            current => _commands.UpdateScene(current, "scene_001", "Updated", "Rain"),
            WorkbenchSessionOperationNames.UpdateSceneText);

        session.Undo();

        Assert.Null(session.NextUndoOperationName);
        Assert.Equal(WorkbenchSessionOperationNames.UpdateSceneText, session.NextRedoOperationName);

        session.Redo();

        Assert.Equal(WorkbenchSessionOperationNames.UpdateSceneText, session.NextUndoOperationName);
        Assert.Null(session.NextRedoOperationName);
    }

    [Fact]
    public void NewExecuteAfterUndoClearsRedo()
    {
        var session = new WorkbenchSession(Document());
        session.Execute(current => _commands.UpdateScene(current, "scene_001", "Updated", "Rain"));
        session.Undo();

        session.Execute(current => _commands.UpdateSceneAsset(current, "scene_001", "asset_scene_001"));

        Assert.False(session.CanRedo);
        Assert.True(session.CanUndo);
        Assert.Equal("Scene", session.Document.Scenes.Single().Name);
        Assert.Equal("asset_scene_001", session.Document.Scenes.Single().VisualBinding?.EffectiveAssetId);
    }

    [Fact]
    public void PreviewUpdatesDocumentWithoutAddingUndoHistory()
    {
        var session = new WorkbenchSession(Document());

        session.Preview(current => _commands.AddScene(current, "scene_002", "Second scene"));

        Assert.True(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.Equal(2, session.Document.Scenes.Count);
    }

    [Fact]
    public void HasSameDocumentUsesStructuralComparison()
    {
        var document = Document();
        var session = new WorkbenchSession(document);
        var rebuilt = Document();

        Assert.True(session.HasSameDocument(rebuilt));
    }

    [Fact]
    public void HasSameDocumentAcceptsSameDocumentInstance()
    {
        var document = DocumentWithRows();
        var session = new WorkbenchSession(document);

        Assert.True(session.HasSameDocument(document));
    }

    [Fact]
    public void HasSameDocumentDetectsBindingDifferences()
    {
        var document = new WorkbenchDocument(
            Document().Scenes,
            [CharacterBinding("character_chen", "portrait_a.png", "voice_a.wav")]);
        var session = new WorkbenchSession(document);
        var changed = document with
        {
            CharacterBindings =
            [
                CharacterBinding("character_chen", "portrait_b.png", "voice_a.wav")
            ]
        };

        Assert.False(session.HasSameDocument(changed));
    }

    [Fact]
    public void HasSameDocumentDetectsVoiceBindingSourceDifferences()
    {
        var document = new WorkbenchDocument(
            Document().Scenes,
            [
                new WorkbenchCharacterBindingEdit(
                    "character_chen",
                    VisualAssetBinding.FromImage("portrait.png"),
                    VoiceAssetBinding.FromProjectReference(
                        "voice.wav",
                        "reference line",
                        "asset_voice_a",
                        @"C:\voices\a.wav"))
            ]);
        var session = new WorkbenchSession(document);
        var changed = document with
        {
            CharacterBindings =
            [
                new WorkbenchCharacterBindingEdit(
                    "character_chen",
                    VisualAssetBinding.FromImage("portrait.png"),
                    VoiceAssetBinding.FromProjectReference(
                        "voice.wav",
                        "reference line",
                        "asset_voice_b",
                        @"C:\voices\b.wav"))
            ]
        };

        Assert.False(session.HasSameDocument(changed));
    }

    [Fact]
    public void HasSameDocumentDetectsRowFieldDifferences()
    {
        var document = DocumentWithRows();
        var session = new WorkbenchSession(document);
        var changedRows = document.Scenes[0].Rows.ToArray();
        changedRows[0] = changedRows[0] with { Text = "Changed line" };
        var changedScenes = document.Scenes.ToArray();
        changedScenes[0] = changedScenes[0] with { Rows = changedRows };

        Assert.False(session.HasSameDocument(document with { Scenes = changedScenes }));
    }

    [Fact]
    public void RowsByIdReusesComputedIndexForSameDocumentInstance()
    {
        var document = DocumentWithRows();

        var first = document.RowsById;
        var second = document.RowsById;

        Assert.Same(first, second);
        Assert.Equal("scene_001", first["row_001"].SceneId);
    }

    [Fact]
    public void CheckpointRestoreRestoresDocumentDirtyAndHistory()
    {
        var session = new WorkbenchSession(Document());
        session.Execute(current => _commands.UpdateScene(current, "scene_001", "Updated", "Rain"));
        var checkpoint = session.CreateCheckpoint();
        session.Execute(current => _commands.UpdateSceneAsset(current, "scene_001", "asset_scene_001"));

        session.Restore(checkpoint);

        Assert.True(session.IsDirty);
        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.Equal("Updated", session.Document.Scenes.Single().Name);
        Assert.Null(session.Document.Scenes.Single().VisualBinding?.EffectiveAssetId);

        session.Undo();

        Assert.False(session.IsDirty);
        Assert.Equal("Scene", session.Document.Scenes.Single().Name);
    }

    [Fact]
    public void CheckpointRestoreTruncatesHistoryAddedAfterCheckpoint()
    {
        var session = new WorkbenchSession(Document());
        session.Execute(current => _commands.UpdateScene(current, "scene_001", "Updated", "Rain"));
        var checkpoint = session.CreateCheckpoint();
        session.Execute(current => _commands.UpdateSceneAsset(current, "scene_001", "asset_scene_001"));
        session.Execute(current => _commands.UpdateScene(current, "scene_001", "After checkpoint", "Snow"));

        session.Restore(checkpoint);

        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.Equal("Updated", session.Document.Scenes.Single().Name);
        Assert.Null(session.Document.Scenes.Single().VisualBinding?.EffectiveAssetId);

        session.Undo();

        Assert.False(session.CanUndo);
        Assert.True(session.CanRedo);
        Assert.Equal("Scene", session.Document.Scenes.Single().Name);
    }

    [Fact]
    public void SquashHistoryToCheckpointKeepsDragAsSingleUndoStep()
    {
        var session = new WorkbenchSession(DocumentWithRows());
        var checkpoint = session.CreateCheckpoint();

        session.Execute(current => _commands.MoveRow(current, "row_001", "scene_002", 0));
        session.Execute(current => _commands.MoveRow(current, "row_001", "scene_001", 1));
        session.SquashHistoryToCheckpoint(checkpoint, WorkbenchSessionOperationNames.WorkbenchDrag);

        Assert.True(session.IsDirty);
        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.Equal(WorkbenchSessionOperationNames.WorkbenchDrag, session.NextUndoOperationName);
        Assert.Equal(2, session.Document.RowsById["row_001"].Order);

        session.Undo();

        Assert.False(session.IsDirty);
        Assert.Equal("scene_001", session.Document.RowsById["row_001"].SceneId);
        Assert.Equal(1, session.Document.RowsById["row_001"].Order);
        Assert.True(session.CanRedo);
    }

    private static WorkbenchDocument Document(string sceneId = "scene_001")
    {
        return new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    sceneId,
                    1,
                    "Scene",
                    string.Empty,
                    [])
            ],
            []);
    }

    private static WorkbenchDocument DocumentWithRows()
    {
        return new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_001",
                    1,
                    "Scene 1",
                    string.Empty,
                    [
                        Row("row_001", "scene_001", 1),
                        Row("row_002", "scene_001", 2)
                    ]),
                new WorkbenchSceneDraft(
                    "scene_002",
                    2,
                    "Scene 2",
                    string.Empty,
                    [])
            ],
            []);
    }

    private static WorkbenchRowDraft Row(string rowId, string sceneId, int order)
    {
        return new WorkbenchRowDraft(
            rowId,
            sceneId,
            order,
            WorkbenchRowKind.Dialogue,
            "Chen",
            "character_chen",
            "Calm 0.6",
            "Line",
            new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["Calm"] = 0.6
            },
            1,
            new WorkbenchTaskLinks($"tts_{rowId}"));
    }

    private static WorkbenchCharacterBindingEdit CharacterBinding(string characterId, string? visualId, string? voiceId)
    {
        return new WorkbenchCharacterBindingEdit(
            characterId,
            VisualAssetBinding.FromImage(visualId),
            VoiceAssetBinding.FromProjectReference(voiceId, null, null, null));
    }
}




