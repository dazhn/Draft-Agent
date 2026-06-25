using DraftAgent.Core;

namespace DraftAgent.Core.Tests;

public sealed class WorkbenchCommandServiceTests
{
    private readonly WorkbenchCommandService _service = new();



    [Fact]
    public void MoveRowAcrossScenesReplacesOnlySourceAndTargetScenes()
    {
        var document = DocumentWithThreeScenes();
        var unchangedScene = document.Scenes[2];

        var updated = _service.MoveRow(document, "int_001", "scene_002", 0);

        Assert.NotSame(document.Scenes[0], updated.Scenes[0]);
        Assert.NotSame(document.Scenes[1], updated.Scenes[1]);
        Assert.Same(unchangedScene, updated.Scenes[2]);
        Assert.Equal("scene_002", updated.RowsById["int_001"].SceneId);
    }

    [Fact]
    public void MoveRowUpdatesRowLocationIndex()
    {
        var document = DocumentWithTwoRowsInFirstScene();

        var updated = _service.MoveRow(document, "int_001", "scene_002", 0);

        Assert.Equal(new WorkbenchRowLocation(1, 0), updated.RowLocationsById["int_001"]);
        Assert.Equal(new WorkbenchRowLocation(0, 0), updated.RowLocationsById["int_002"]);
    }

    [Fact]
    public void MoveRowWithinSceneKeepsOtherScenesAndRenumbersRows()
    {
        var document = DocumentWithTwoRowsInFirstScene();
        var otherScene = document.Scenes[1];

        var updated = _service.MoveRow(document, "int_001", "scene_001", 1);

        Assert.NotSame(document.Scenes[0], updated.Scenes[0]);
        Assert.Same(otherScene, updated.Scenes[1]);
        Assert.Equal(["int_002", "int_001"], updated.Scenes[0].Rows.Select(row => row.InteractionId));
        Assert.Equal([1, 2], updated.Scenes[0].Rows.Select(row => row.Order));
    }

    [Fact]
    public void MoveRowWithinSceneKeepsRowsWhoseOrderDidNotChange()
    {
        var document = DocumentWithThreeRowsInFirstScene();
        var unchanged = document.Scenes[0].Rows[2];

        var updated = _service.MoveRow(document, "int_001", "scene_001", 1);

        Assert.Same(unchanged, updated.Scenes[0].Rows[2]);
    }

    [Fact]
    public void MoveRowWithinSceneReplacesOnlyAffectedRows()
    {
        var document = DocumentWithFourRowsInFirstScene();
        var first = document.Scenes[0].Rows[0];
        var fourth = document.Scenes[0].Rows[3];

        var updated = _service.MoveRow(document, "int_001", "scene_001", 2);

        Assert.Equal(["int_002", "int_003", "int_001", "int_004"], updated.Scenes[0].Rows.Select(row => row.InteractionId));
        Assert.NotSame(first, updated.Scenes[0].Rows[2]);
        Assert.Same(fourth, updated.Scenes[0].Rows[3]);
        Assert.Equal(3, updated.Scenes[0].Rows[2].Order);
    }

    [Fact]
    public void MoveRowAcrossScenesKeepsRowsOutsideRenumberedRanges()
    {
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_001",
                    1,
                    "Scene 1",
                    string.Empty,
                    [
                        Row("int_001", "scene_001", 1),
                        Row("int_002", "scene_001", 2),
                        Row("int_003", "scene_001", 3)
                    ]),
                new WorkbenchSceneDraft(
                    "scene_002",
                    2,
                    "Scene 2",
                    string.Empty,
                    [
                        Row("int_004", "scene_002", 1),
                        Row("int_005", "scene_002", 2)
                    ])
            ],
            []);
        var sourceUnaffected = document.Scenes[0].Rows[0];
        var targetUnaffected = document.Scenes[1].Rows[0];

        var updated = _service.MoveRow(document, "int_002", "scene_002", 1);

        Assert.Equal(["int_001", "int_003"], updated.Scenes[0].Rows.Select(row => row.InteractionId));
        Assert.Equal(["int_004", "int_002", "int_005"], updated.Scenes[1].Rows.Select(row => row.InteractionId));
        Assert.Same(sourceUnaffected, updated.Scenes[0].Rows[0]);
        Assert.Same(targetUnaffected, updated.Scenes[1].Rows[0]);
        Assert.Equal(2, updated.Scenes[0].Rows[1].Order);
        Assert.Equal(2, updated.Scenes[1].Rows[1].Order);
    }

    [Fact]
    public void MoveRowWithinSceneToSameIndexReturnsSameDocument()
    {
        var document = DocumentWithTwoRowsInFirstScene();

        var updated = _service.MoveRow(document, "int_001", "scene_001", 0);

        Assert.Same(document, updated);
    }

    [Fact]
    public void MoveRowMissingTargetReturnsSameDocument()
    {
        var document = Document();

        var updated = _service.MoveRow(document, "missing", "scene_002", 0);

        Assert.Same(document, updated);
    }

    [Fact]
    public void MoveRowFromKnownLocationMovesWithoutChangingBehavior()
    {
        var document = DocumentWithTwoRowsInFirstScene();

        var updated = _service.MoveRowFromKnownLocation(
            document,
            "int_001",
            "scene_001",
            0,
            0,
            "scene_002",
            1,
            0);

        Assert.Equal(["int_002"], updated.Scenes[0].Rows.Select(row => row.InteractionId));
        Assert.Equal(["int_001"], updated.Scenes[1].Rows.Select(row => row.InteractionId));
        Assert.Equal("scene_002", updated.Scenes[1].Rows[0].SceneId);
        Assert.Equal(1, updated.Scenes[1].Rows[0].Order);
    }

    [Fact]
    public void MoveRowFromKnownLocationRejectsStaleIndexes()
    {
        var document = DocumentWithTwoRowsInFirstScene();

        var updated = _service.MoveRowFromKnownLocation(
            document,
            "int_001",
            "scene_001",
            0,
            1,
            "scene_002",
            1,
            0);

        Assert.Same(document, updated);
        Assert.Equal(["int_001", "int_002"], document.Scenes[0].Rows.Select(row => row.InteractionId));
        Assert.Empty(document.Scenes[1].Rows);
    }

    [Fact]
    public void MoveSceneRenumbersScenesAndRows()
    {
        var document = Document();

        var updated = _service.MoveScene(document, "scene_002", 0);

        Assert.Equal("scene_002", updated.Scenes[0].SceneId);
        Assert.Equal("scene_001", updated.Scenes[1].SceneId);
        Assert.Equal(1, updated.Scenes[0].Order);
        Assert.Equal(2, updated.Scenes[1].Order);
        Assert.All(updated.Scenes.SelectMany(scene => scene.Rows), row => Assert.Equal(1, row.Order));
    }

    [Fact]
    public void MoveSceneKeepsExistingRowLists()
    {
        var document = DocumentWithTwoRowsInFirstScene();
        var firstSceneRows = document.Scenes[0].Rows;
        var secondSceneRows = document.Scenes[1].Rows;

        var updated = _service.MoveScene(document, "scene_002", 0);

        Assert.Same(secondSceneRows, updated.Scenes[0].Rows);
        Assert.Same(firstSceneRows, updated.Scenes[1].Rows);
        Assert.Equal(["int_001", "int_002"], updated.Scenes[1].Rows.Select(row => row.InteractionId));
    }


    [Fact]
    public void MoveSceneKeepsScenesOutsideMovedRange()
    {
        var document = DocumentWithThreeScenes();
        var firstScene = document.Scenes[0];

        var updated = _service.MoveScene(document, "scene_003", 1);

        Assert.Same(firstScene, updated.Scenes[0]);
        Assert.Equal(["scene_001", "scene_003", "scene_002"], updated.Scenes.Select(scene => scene.SceneId));
        Assert.Equal([1, 2, 3], updated.Scenes.Select(scene => scene.Order));
    }

    [Fact]
    public void MoveSceneUpdatesSceneIndex()
    {
        var document = DocumentWithThreeScenes();

        var updated = _service.MoveScene(document, "scene_003", 0);

        Assert.Equal(0, updated.SceneIndexesById["scene_003"]);
        Assert.Equal(1, updated.SceneIndexesById["scene_001"]);
        Assert.Equal(2, updated.SceneIndexesById["scene_002"]);
    }

    [Fact]
    public void MoveSceneUsesDocumentOrderWhenSceneOrdersAreStale()
    {
        var document = DocumentWithThreeScenes() with
        {
            Scenes =
            [
                DocumentWithThreeScenes().Scenes[0] with { Order = 3 },
                DocumentWithThreeScenes().Scenes[1] with { Order = 1 },
                DocumentWithThreeScenes().Scenes[2] with { Order = 2 }
            ]
        };

        var updated = _service.MoveScene(document, "scene_001", 2);

        Assert.Equal(["scene_002", "scene_003", "scene_001"], updated.Scenes.Select(scene => scene.SceneId));
        Assert.Equal([1, 2, 3], updated.Scenes.Select(scene => scene.Order));
        Assert.Equal(2, updated.SceneIndexesById["scene_001"]);
    }

    [Fact]
    public void MoveSceneToSameIndexReturnsSameDocument()
    {
        var document = Document();

        var updated = _service.MoveScene(document, "scene_001", 0);

        Assert.Same(document, updated);
    }

    [Fact]
    public void MoveSceneFromKnownLocationMovesWithoutChangingBehavior()
    {
        var document = DocumentWithThreeScenes();

        var updated = _service.MoveSceneFromKnownLocation(document, "scene_003", 2, 0);

        Assert.Equal(["scene_003", "scene_001", "scene_002"], updated.Scenes.Select(scene => scene.SceneId));
        Assert.Equal([1, 2, 3], updated.Scenes.Select(scene => scene.Order));
        Assert.Equal(0, updated.SceneIndexesById["scene_003"]);
    }

    [Fact]
    public void MoveSceneFromKnownLocationRejectsStaleIndex()
    {
        var document = DocumentWithThreeScenes();

        var updated = _service.MoveSceneFromKnownLocation(document, "scene_003", 1, 0);

        Assert.Same(document, updated);
        Assert.Equal(["scene_001", "scene_002", "scene_003"], document.Scenes.Select(scene => scene.SceneId));
    }

    [Fact]
    public void RemoveSceneFromKnownLocationRemovesWithoutSceneIndexLookup()
    {
        var document = DocumentWithThreeScenes();
        var firstScene = document.Scenes[0];

        var updated = _service.RemoveSceneFromKnownLocation(document, "scene_002", 1);

        Assert.Same(firstScene, updated.Scenes[0]);
        Assert.Equal(["scene_001", "scene_003"], updated.Scenes.Select(scene => scene.SceneId));
        Assert.Equal([1, 2], updated.Scenes.Select(scene => scene.Order));
    }

    [Fact]
    public void RemoveSceneFromKnownLocationRejectsStaleIndex()
    {
        var document = DocumentWithThreeScenes();

        var updated = _service.RemoveSceneFromKnownLocation(document, "scene_002", 2);

        Assert.Same(document, updated);
        Assert.Equal(["scene_001", "scene_002", "scene_003"], document.Scenes.Select(scene => scene.SceneId));
    }

    [Fact]
    public void RemoveSceneMissingSceneReturnsSameDocument()
    {
        var document = Document();

        var updated = _service.RemoveScene(document, "missing");

        Assert.Same(document, updated);
    }

    [Fact]
    public void AddSceneWithExistingIdReturnsSameDocument()
    {
        var document = Document();

        var updated = _service.AddScene(document, "scene_001", "Duplicate");

        Assert.Same(document, updated);
    }

    [Fact]
    public void AddSceneKeepsExistingSceneInstances()
    {
        var document = DocumentWithThreeScenes();
        var firstScene = document.Scenes[0];
        var secondScene = document.Scenes[1];
        var thirdScene = document.Scenes[2];

        var updated = _service.AddScene(document, "scene_004", "Scene 4");

        Assert.Same(firstScene, updated.Scenes[0]);
        Assert.Same(secondScene, updated.Scenes[1]);
        Assert.Same(thirdScene, updated.Scenes[2]);
        Assert.Equal("scene_004", updated.Scenes[3].SceneId);
        Assert.Equal(4, updated.Scenes[3].Order);
    }

    [Fact]
    public void AddSceneUncheckedAppendsWithoutSceneIndexLookup()
    {
        var document = DocumentWithThreeScenes();
        var firstScene = document.Scenes[0];
        var secondScene = document.Scenes[1];
        var thirdScene = document.Scenes[2];

        var updated = _service.AddSceneUnchecked(document, "scene_004", "Scene 4", "Effect 4", "scene4.png");

        Assert.Same(firstScene, updated.Scenes[0]);
        Assert.Same(secondScene, updated.Scenes[1]);
        Assert.Same(thirdScene, updated.Scenes[2]);
        Assert.Equal("scene_004", updated.Scenes[3].SceneId);
        Assert.Equal(4, updated.Scenes[3].Order);
        Assert.Equal("scene4.png", SceneVisualId(updated.Scenes[3]));
    }

    [Fact]
    public void RemoveSceneKeepsScenesBeforeRemovedIndex()
    {
        var document = DocumentWithThreeScenes();
        var firstScene = document.Scenes[0];

        var updated = _service.RemoveScene(document, "scene_002");

        Assert.Same(firstScene, updated.Scenes[0]);
        Assert.Equal(["scene_001", "scene_003"], updated.Scenes.Select(scene => scene.SceneId));
        Assert.Equal([1, 2], updated.Scenes.Select(scene => scene.Order));
    }

    [Fact]
    public void AddAndRemoveRowMaintainRowsById()
    {
        var document = Document();
        var row = Row("int_003", "scene_002", 99);

        var added = _service.AddRow(document, "scene_002", row);
        var removed = _service.RemoveRow(added, "int_001");

        Assert.True(added.RowsById.ContainsKey("int_003"));
        Assert.False(removed.RowsById.ContainsKey("int_001"));
        Assert.True(removed.RowsById.ContainsKey("int_003"));
        Assert.Equal(1, removed.Scenes.Single(scene => scene.SceneId == "scene_002").Rows.Single().Order);
    }

    [Fact]
    public void AddRowReplacesOnlyTargetScene()
    {
        var document = DocumentWithThreeScenes();
        var firstScene = document.Scenes[0];
        var thirdScene = document.Scenes[2];

        var updated = _service.AddRow(document, "scene_002", Row("int_004", "scene_002", 99));

        Assert.Same(firstScene, updated.Scenes[0]);
        Assert.NotSame(document.Scenes[1], updated.Scenes[1]);
        Assert.Same(thirdScene, updated.Scenes[2]);
        Assert.Equal(1, updated.Scenes[1].Rows.Single().Order);
    }

    [Fact]
    public void AddRowMissingSceneReturnsSameDocument()
    {
        var document = Document();

        var updated = _service.AddRow(document, "missing", Row("int_004", "missing", 1));

        Assert.Same(document, updated);
    }

    [Fact]
    public void AddRowWithExistingIdReturnsSameDocument()
    {
        var document = Document();

        var updated = _service.AddRow(document, "scene_002", Row("int_001", "scene_002", 99));

        Assert.Same(document, updated);
    }

    [Fact]
    public void AddRowToKnownSceneReplacesOnlyTargetScene()
    {
        var document = DocumentWithThreeScenes();
        var firstScene = document.Scenes[0];
        var thirdScene = document.Scenes[2];

        var updated = _service.AddRowToKnownScene(
            document,
            "scene_002",
            1,
            Row("int_004", "scene_002", 99),
            0);

        Assert.Same(firstScene, updated.Scenes[0]);
        Assert.NotSame(document.Scenes[1], updated.Scenes[1]);
        Assert.Same(thirdScene, updated.Scenes[2]);
        Assert.Equal("int_004", updated.Scenes[1].Rows.Single().InteractionId);
        Assert.Equal(1, updated.Scenes[1].Rows.Single().Order);
    }

    [Fact]
    public void AddRowToKnownSceneRejectsStaleSceneIndex()
    {
        var document = DocumentWithThreeScenes();

        var updated = _service.AddRowToKnownScene(
            document,
            "scene_002",
            2,
            Row("int_004", "scene_002", 99),
            0);

        Assert.Same(document, updated);
        Assert.Empty(document.Scenes[1].Rows);
        Assert.Equal(["int_003"], document.Scenes[2].Rows.Select(row => row.InteractionId));
    }

    [Fact]
    public void AddRowToKnownSceneRejectsDuplicateInTargetScene()
    {
        var document = DocumentWithTwoRowsInFirstScene();

        var updated = _service.AddRowToKnownScene(
            document,
            "scene_001",
            0,
            Row("int_001", "scene_001", 99),
            1);

        Assert.Same(document, updated);
        Assert.Equal(["int_001", "int_002"], document.Scenes[0].Rows.Select(row => row.InteractionId));
    }

    [Fact]
    public void RemoveRowReplacesOnlyContainingScene()
    {
        var document = DocumentWithThreeScenes();
        var secondScene = document.Scenes[1];
        var thirdScene = document.Scenes[2];

        var updated = _service.RemoveRow(document, "int_001");

        Assert.NotSame(document.Scenes[0], updated.Scenes[0]);
        Assert.Same(secondScene, updated.Scenes[1]);
        Assert.Same(thirdScene, updated.Scenes[2]);
        Assert.Empty(updated.Scenes[0].Rows);
    }

    [Fact]
    public void RemoveRowMissingRowReturnsSameDocument()
    {
        var document = Document();

        var updated = _service.RemoveRow(document, "missing");

        Assert.Same(document, updated);
    }

    [Fact]
    public void RemoveRowFromKnownLocationReplacesOnlyContainingScene()
    {
        var document = DocumentWithThreeScenes();
        var secondScene = document.Scenes[1];
        var thirdScene = document.Scenes[2];

        var updated = _service.RemoveRowFromKnownLocation(
            document,
            "int_001",
            "scene_001",
            0,
            0);

        Assert.NotSame(document.Scenes[0], updated.Scenes[0]);
        Assert.Same(secondScene, updated.Scenes[1]);
        Assert.Same(thirdScene, updated.Scenes[2]);
        Assert.Empty(updated.Scenes[0].Rows);
    }

    [Fact]
    public void RemoveRowFromKnownLocationRejectsStaleIndexes()
    {
        var document = DocumentWithTwoRowsInFirstScene();

        var updated = _service.RemoveRowFromKnownLocation(
            document,
            "int_001",
            "scene_001",
            0,
            1);

        Assert.Same(document, updated);
        Assert.Equal(["int_001", "int_002"], document.Scenes[0].Rows.Select(row => row.InteractionId));
    }

    [Fact]
    public void UpdateRowTextChangesOnlyContent()
    {
        var document = Document();

        var updated = _service.UpdateRowText(document, "int_001", "Changed");
        var row = updated.RowsById["int_001"];

        Assert.Equal("Changed", row.Text);
        Assert.Equal("int_001", row.InteractionId);
        Assert.Equal("scene_001", row.SceneId);
        Assert.Equal("tts_int_001", row.TaskLinks.TtsTaskId);
    }

    [Fact]
    public void UpdateRowTextReplacesOnlyContainingScene()
    {
        var document = Document();
        var originalTargetScene = document.Scenes[0];
        var originalOtherScene = document.Scenes[1];

        var updated = _service.UpdateRowText(document, "int_001", "Changed");

        Assert.NotSame(document, updated);
        Assert.NotSame(originalTargetScene, updated.Scenes[0]);
        Assert.Same(originalOtherScene, updated.Scenes[1]);
        Assert.Equal("Changed", updated.Scenes[0].Rows.Single().Text);
    }

    [Fact]
    public void UpdateRowTextKeepsRowLocationIndex()
    {
        var document = DocumentWithTwoRowsInFirstScene();

        var updated = _service.UpdateRowText(document, "int_002", "Changed");

        Assert.Equal(new WorkbenchRowLocation(0, 1), updated.RowLocationsById["int_002"]);
        Assert.Equal("Changed", updated.Scenes[0].Rows[1].Text);
    }

    [Fact]
    public void UpdateRowTextFromKnownLocationReplacesOnlyContainingScene()
    {
        var document = DocumentWithThreeScenes();
        var secondScene = document.Scenes[1];
        var thirdScene = document.Scenes[2];

        var updated = _service.UpdateRowTextFromKnownLocation(
            document,
            "int_001",
            "scene_001",
            0,
            0,
            "Changed from known location");

        Assert.NotSame(document, updated);
        Assert.NotSame(document.Scenes[0], updated.Scenes[0]);
        Assert.Same(secondScene, updated.Scenes[1]);
        Assert.Same(thirdScene, updated.Scenes[2]);
        Assert.Equal("Changed from known location", updated.Scenes[0].Rows[0].Text);
    }

    [Fact]
    public void UpdateRowFromKnownLocationRejectsStaleIndexes()
    {
        var document = DocumentWithTwoRowsInFirstScene();

        var updated = _service.UpdateRowTextFromKnownLocation(
            document,
            "int_001",
            "scene_001",
            0,
            1,
            "Changed");

        Assert.Same(document, updated);
        Assert.Equal("Hello", document.Scenes[0].Rows[0].Text);
        Assert.Equal("Hello", document.Scenes[0].Rows[1].Text);
    }

    [Fact]
    public void UpdateRowEmotionFromKnownLocationPreservesEquivalentVectorNoOp()
    {
        var document = DocumentWithEmotionVectors();

        var updated = _service.UpdateRowEmotionFromKnownLocation(
            document,
            "int_001",
            "scene_001",
            0,
            0,
            "Calm 0.6",
            new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["Calm"] = 0.6
            },
            1);

        Assert.Same(document, updated);
    }

    [Fact]
    public void UpdateRowBindingFromKnownLocationChangesAssetsAndPreservesIdentity()
    {
        var document = Document();

        var updated = _service.UpdateRowBindingFromKnownLocation(
            document,
            "int_001",
            "scene_001",
            0,
            0,
            "asset_character_new",
            "voices/new.wav",
            "new reference text");
        var row = updated.Scenes[0].Rows[0];

        Assert.Equal("asset_character_new", RowVisualId(row));
        Assert.Equal("voices/new.wav", RowVoiceId(row));
        Assert.Equal("new reference text", RowVoiceText(row));
        Assert.Equal("int_001", row.InteractionId);
        Assert.Equal("tts_int_001", row.TaskLinks.TtsTaskId);
    }

    [Fact]
    public void UpdateRowTextNoOpReturnsSameDocument()
    {
        var document = Document();

        var updated = _service.UpdateRowText(document, "int_001", "Hello");

        Assert.Same(document, updated);
    }

    [Fact]
    public void UpdateSceneChangesNameAndEffectOnly()
    {
        var document = Document();

        var updated = _service.UpdateScene(document, "scene_001", "Updated scene", "Updated effect");
        var scene = updated.Scenes.Single(scene => scene.SceneId == "scene_001");

        Assert.Equal("Updated scene", scene.Name);
        Assert.Equal("Updated effect", scene.Effect);
        Assert.Equal("int_001", scene.Rows.Single().InteractionId);
        Assert.Equal("tts_int_001", scene.Rows.Single().TaskLinks.TtsTaskId);
    }

    [Fact]
    public void UpdateSceneReplacesOnlyTargetScene()
    {
        var document = DocumentWithThreeScenes();
        var secondScene = document.Scenes[1];
        var thirdScene = document.Scenes[2];

        var updated = _service.UpdateScene(document, "scene_001", "Updated scene", "Updated effect");

        Assert.NotSame(document.Scenes[0], updated.Scenes[0]);
        Assert.Same(secondScene, updated.Scenes[1]);
        Assert.Same(thirdScene, updated.Scenes[2]);
    }

    [Fact]
    public void UpdateSceneNoOpReturnsSameDocument()
    {
        var document = Document();

        var updated = _service.UpdateScene(document, "scene_001", "Scene 1", string.Empty);

        Assert.Same(document, updated);
    }

    [Fact]
    public void UpdateSceneFromKnownLocationReplacesOnlyTargetScene()
    {
        var document = DocumentWithThreeScenes();
        var secondScene = document.Scenes[1];
        var thirdScene = document.Scenes[2];

        var updated = _service.UpdateSceneFromKnownLocation(
            document,
            "scene_001",
            0,
            "Updated scene",
            "Updated effect");

        Assert.NotSame(document.Scenes[0], updated.Scenes[0]);
        Assert.Same(secondScene, updated.Scenes[1]);
        Assert.Same(thirdScene, updated.Scenes[2]);
        Assert.Equal("Updated scene", updated.Scenes[0].Name);
        Assert.Equal("Updated effect", updated.Scenes[0].Effect);
    }

    [Fact]
    public void UpdateSceneFromKnownLocationRejectsStaleIndex()
    {
        var document = DocumentWithThreeScenes();

        var updated = _service.UpdateSceneFromKnownLocation(
            document,
            "scene_001",
            1,
            "Updated scene",
            "Updated effect");

        Assert.Same(document, updated);
        Assert.Equal("Scene 1", document.Scenes[0].Name);
    }

    [Fact]
    public void UpdateSceneAssetChangesOnlySceneBinding()
    {
        var document = Document();

        var updated = _service.UpdateSceneAsset(document, "scene_001", "asset_scene_001");
        var scene = updated.Scenes.Single(scene => scene.SceneId == "scene_001");
        var otherScene = updated.Scenes.Single(scene => scene.SceneId == "scene_002");

        Assert.Equal("asset_scene_001", SceneVisualId(scene));
        Assert.Null(SceneVisualId(otherScene));
        Assert.Equal("int_001", scene.Rows.Single().InteractionId);
    }

    [Fact]
    public void UpdateSceneAssetReplacesOnlyTargetScene()
    {
        var document = DocumentWithThreeScenes();
        var firstScene = document.Scenes[0];
        var thirdScene = document.Scenes[2];

        var updated = _service.UpdateSceneAsset(document, "scene_002", "asset_scene_002");

        Assert.Same(firstScene, updated.Scenes[0]);
        Assert.NotSame(document.Scenes[1], updated.Scenes[1]);
        Assert.Same(thirdScene, updated.Scenes[2]);
    }

    [Fact]
    public void UpdateSceneAssetMissingSceneReturnsSameDocument()
    {
        var document = Document();

        var updated = _service.UpdateSceneAsset(document, "missing", "asset_scene_missing");

        Assert.Same(document, updated);
    }

    [Fact]
    public void UpdateSceneAssetFromKnownLocationReplacesOnlyTargetScene()
    {
        var document = DocumentWithThreeScenes();
        var firstScene = document.Scenes[0];
        var thirdScene = document.Scenes[2];

        var updated = _service.UpdateSceneAssetFromKnownLocation(
            document,
            "scene_002",
            1,
            "asset_scene_002");

        Assert.Same(firstScene, updated.Scenes[0]);
        Assert.NotSame(document.Scenes[1], updated.Scenes[1]);
        Assert.Same(thirdScene, updated.Scenes[2]);
        Assert.Equal("asset_scene_002", SceneVisualId(updated.Scenes[1]));
    }

    [Fact]
    public void UpdateSceneAssetFromKnownLocationRejectsStaleIndex()
    {
        var document = DocumentWithThreeScenes();

        var updated = _service.UpdateSceneAssetFromKnownLocation(
            document,
            "scene_002",
            2,
            "asset_scene_002");

        Assert.Same(document, updated);
        Assert.Null(SceneVisualId(document.Scenes[1]));
    }

    [Fact]
    public void UpdateSceneVisualBindingRejectsPsdSourceAndKeepsOnlyRenderedImage()
    {
        var document = DocumentWithThreeScenes();

        var updated = _service.UpdateSceneVisualBinding(
            document,
            "scene_001",
            VisualAssetBinding.FromPsd("asset_psd_scene", "asset_png_scene_rendered"));
        var scene = updated.Scenes[0];

        Assert.Equal("asset_png_scene_rendered", SceneVisualId(scene));
        Assert.Equal(VisualSourceKind.Image, scene.VisualBinding?.SourceKind);
        Assert.Equal("asset_png_scene_rendered", scene.VisualBinding?.SourceAssetId);
        Assert.Equal("asset_png_scene_rendered", scene.VisualBinding?.RenderedAssetId);
        Assert.Same(document.Scenes[1], updated.Scenes[1]);
        Assert.Same(document.Scenes[2], updated.Scenes[2]);
    }

    [Fact]
    public void UpdateRowEmotionPreservesIdentityAndTaskLinks()
    {
        var document = Document();

        var updated = _service.UpdateRowEmotion(
            document,
            "int_001",
            "Afraid 0.9",
            new Dictionary<string, double> { ["Afraid"] = 0.9 },
            0.8);
        var row = updated.RowsById["int_001"];

        Assert.Equal("Afraid 0.9", row.Emotion);
        Assert.Equal(0.9, row.EmotionVectors!["Afraid"]);
        Assert.Equal(0.8, row.EmotionAlpha);
        Assert.Equal("int_001", row.InteractionId);
        Assert.Equal("tts_int_001", row.TaskLinks.TtsTaskId);
    }

    [Fact]
    public void UpdateRowEmotionWithEquivalentVectorsReturnsSameDocument()
    {
        var document = DocumentWithEmotionVectors();

        var updated = _service.UpdateRowEmotion(
            document,
            "int_001",
            "Calm 0.6",
            new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["Calm"] = 0.6
            },
            1);

        Assert.Same(document, updated);
    }

    [Fact]
    public void UpdateRowSubjectUpdatesRowKind()
    {
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_001",
                    1,
                    "Scene",
                    string.Empty,
                    [Row("int_001", "scene_001", 1) with { Kind = WorkbenchRowKind.Narration, SubjectName = "旁白" }])
            ],
            []);

        var updated = _service.UpdateRowSubject(document, "int_001", "Chen", "char_chen");
        var row = updated.RowsById["int_001"];

        Assert.Equal(WorkbenchRowKind.Dialogue, row.Kind);
        Assert.Equal("Chen", row.SubjectName);
        Assert.Equal("char_chen", row.SubjectCharacterId);
    }

    [Fact]
    public void ApplyBindingReplacesCharacterBinding()
    {
        var document = Document();

        var updated = _service.ApplyCharacterBinding(document, "char_chen", "Chen", "new.png", "new.wav", "sample");
        var binding = Assert.Single(updated.CharacterBindings);
        var row = updated.RowsById["int_001"];

        Assert.Equal("char_chen", binding.CharacterId);
        Assert.Equal("new.png", CharacterBindingVisualId(binding));
        Assert.Equal("new.wav", CharacterBindingVoiceId(binding));
        Assert.Equal("new.png", RowVisualId(row));
        Assert.Equal("new.wav", RowVoiceId(row));
        Assert.Equal("sample", RowVoiceText(row));
    }

    [Fact]
    public void ApplyCharacterBindingReplacesOnlyScenesWithMatchingRows()
    {
        var document = DocumentWithThreeScenes();
        var secondScene = document.Scenes[1];
        var thirdScene = document.Scenes[2];

        var updated = _service.ApplyCharacterBinding(document, "char_chen", "Chen", "new.png", "new.wav", "sample");

        Assert.NotSame(document.Scenes[0], updated.Scenes[0]);
        Assert.Same(secondScene, updated.Scenes[1]);
        Assert.Same(thirdScene, updated.Scenes[2]);
        Assert.Equal("new.png", RowVisualId(updated.RowsById["int_001"]));
    }

    [Fact]
    public void ApplyCharacterBindingUpdatesMultipleMatchingRowsInSameScene()
    {
        var document = DocumentWithTwoRowsInFirstScene();
        var secondScene = document.Scenes[1];

        var updated = _service.ApplyCharacterBinding(document, "char_chen", "Chen", "new.png", "new.wav", "sample");

        Assert.NotSame(document.Scenes[0], updated.Scenes[0]);
        Assert.Same(secondScene, updated.Scenes[1]);
        Assert.Equal(["new.png", "new.png"], updated.Scenes[0].Rows.Select(RowVisualId));
        Assert.Equal(["new.wav", "new.wav"], updated.Scenes[0].Rows.Select(RowVoiceId));
    }

    [Fact]
    public void ApplyCharacterBindingUpdatesIdAndNameMatchesAcrossScenesInDocumentOrder()
    {
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_001",
                    1,
                    "Scene 1",
                    string.Empty,
                    [
                        Row("int_001", "scene_001", 1) with { SubjectCharacterId = "char_chen", SubjectName = "Alias" },
                        Row("int_002", "scene_001", 2) with { SubjectCharacterId = "char_other", SubjectName = "Chen" }
                    ]),
                new WorkbenchSceneDraft(
                    "scene_002",
                    2,
                    "Scene 2",
                    string.Empty,
                    [Row("int_003", "scene_002", 1) with { SubjectCharacterId = "char_chen", SubjectName = "Alias" }]),
                new WorkbenchSceneDraft(
                    "scene_003",
                    3,
                    "Scene 3",
                    string.Empty,
                    [Row("int_004", "scene_003", 1) with
                    {
                        SubjectCharacterId = "char_lin",
                        SubjectName = "Lin",
                        CharacterVisualBinding = VisualAssetBinding.FromImage("lin.png")
                    }])
            ],
            []);
        var thirdScene = document.Scenes[2];

        var updated = _service.ApplyCharacterBinding(document, "char_chen", "Chen", "new.png", "new.wav", "sample");

        Assert.NotSame(document.Scenes[0], updated.Scenes[0]);
        Assert.NotSame(document.Scenes[1], updated.Scenes[1]);
        Assert.Same(thirdScene, updated.Scenes[2]);
        Assert.Equal(["new.png", "new.png"], updated.Scenes[0].Rows.Select(RowVisualId));
        Assert.Equal("new.png", RowVisualId(updated.Scenes[1].Rows[0]));
        Assert.Equal("lin.png", RowVisualId(updated.Scenes[2].Rows[0]));
    }

    [Fact]
    public void ApplyCharacterBindingUpdatesSourceRoleRows()
    {
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_001",
                    1,
                    "Scene 1",
                    string.Empty,
                    [
                        Row("int_001", "scene_001", 1) with
                        {
                            Kind = WorkbenchRowKind.Narration,
                            SubjectName = "Chen",
                            SubjectCharacterId = null,
                            CharacterVisualBinding = VisualAssetBinding.FromImage("old.png"),
                            SourceItemType = "thought",
                            SourceRole = "Chen",
                            SourceRoleCharacterId = "char_chen"
                        }
                    ])
            ],
            []);

        var updated = _service.ApplyCharacterBinding(document, "char_chen", "Chen", "new.png", "new.wav", "sample");

        var row = Assert.Single(Assert.Single(updated.Scenes).Rows);
        Assert.Equal("new.png", RowVisualId(row));
    }

    [Fact]
    public void ApplyCharacterBindingWithoutMatchingRowsCanUpdateBindingOnly()
    {
        var document = Document();
        var firstScene = document.Scenes[0];
        var secondScene = document.Scenes[1];

        var updated = _service.ApplyCharacterBinding(document, "char_lin", "Lin", "lin.png", "lin.wav", "sample");

        Assert.Same(firstScene, updated.Scenes[0]);
        Assert.Same(secondScene, updated.Scenes[1]);
        Assert.Contains(updated.CharacterBindings, binding => binding.CharacterId == "char_lin");
    }

    [Fact]
    public void ApplyCharacterBindingNoOpReturnsSameDocument()
    {
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_001",
                    1,
                    "Scene",
                    string.Empty,
                    [Row("int_001", "scene_001", 1) with { SubjectCharacterId = "char_lin", SubjectName = "Lin" }])
            ],
            [CharacterBinding("char_chen", "old.png", "old.wav")]);

        var updated = _service.ApplyCharacterBinding(document, "char_chen", "No matching display", "old.png", "old.wav");

        Assert.Same(document, updated);
    }

    [Fact]
    public void ApplyCharacterBindingUpdatesExistingBindingInPlace()
    {
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_001",
                    1,
                    "Scene",
                    string.Empty,
                    []),
                new WorkbenchSceneDraft(
                    "scene_002",
                    2,
                    "Scene 2",
                    string.Empty,
                    [])
            ],
            [
                CharacterBinding("char_chen", "old.png", "old.wav"),
                CharacterBinding("char_lin", "lin.png", "lin.wav")
            ]);

        var updated = _service.ApplyCharacterBinding(document, "char_chen", "Chen", "new.png", "new.wav");

        Assert.Equal(["char_chen", "char_lin"], updated.CharacterBindings.Select(binding => binding.CharacterId));
        Assert.Equal("new.png", CharacterBindingVisualId(updated.CharacterBindings[0]));
        Assert.Same(document.Scenes, updated.Scenes);
    }

    [Fact]
    public void ApplyCharacterVisualSourceBindingStoresPsdSourceWithoutChangingRows()
    {
        var document = DocumentWithTwoRowsInFirstScene();
        var firstScene = document.Scenes[0];
        var secondScene = document.Scenes[1];

        var updated = _service.ApplyCharacterVisualSourceBinding(
            document,
            "char_chen",
            VisualAssetBinding.FromPsd("asset_psd_chen"));

        var binding = Assert.Single(updated.CharacterBindings);
        Assert.Equal("char_chen", binding.CharacterId);
        Assert.Equal("asset_psd_chen", CharacterBindingVisualId(binding));
        Assert.Equal(VisualSourceKind.Psd, binding.VisualBinding?.SourceKind);
        Assert.Equal("asset_psd_chen", binding.VisualBinding?.SourceAssetId);
        Assert.Null(binding.VisualBinding?.RenderedAssetId);
        Assert.Same(firstScene, updated.Scenes[0]);
        Assert.Same(secondScene, updated.Scenes[1]);
        Assert.Equal(["chen.png", "chen.png"], updated.Scenes[0].Rows.Select(RowVisualId));
    }

    [Fact]
    public void UpdateRowCharacterVisualBindingWritesPsdRenderedAssetToOnlyThatRow()
    {
        var document = DocumentWithTwoRowsInFirstScene();
        var secondRow = document.Scenes[0].Rows[1];

        var updated = _service.UpdateRowCharacterVisualBinding(
            document,
            "int_001",
            VisualAssetBinding.FromPsd("asset_psd_chen", "asset_png_chen_calm"));

        var firstRow = updated.RowsById["int_001"];
        Assert.Equal("asset_png_chen_calm", RowVisualId(firstRow));
        Assert.Equal(VisualSourceKind.Psd, firstRow.CharacterVisualBinding?.SourceKind);
        Assert.Equal("asset_psd_chen", firstRow.CharacterVisualBinding?.SourceAssetId);
        Assert.Equal("asset_png_chen_calm", firstRow.CharacterVisualBinding?.RenderedAssetId);
        Assert.Same(secondRow, updated.Scenes[0].Rows[1]);
        Assert.Equal("chen.png", RowVisualId(updated.Scenes[0].Rows[1]));
    }

    [Fact]
    public void ApplyCharacterVoiceBindingDoesNotOverwriteRowVisualBindings()
    {
        var document = _service.UpdateRowCharacterVisualBinding(
            DocumentWithTwoRowsInFirstScene(),
            "int_001",
            VisualAssetBinding.FromPsd("asset_psd_chen", "asset_png_chen_calm"));

        var updated = _service.ApplyCharacterVoiceBinding(
            document,
            "char_chen",
            "Chen",
            VoiceAssetBinding.FromProjectReference(
                "voices/new.wav",
                "new reference",
                "asset_voice_source",
                @"C:\voices\new.wav"));

        var firstRow = updated.RowsById["int_001"];
        Assert.Equal("asset_png_chen_calm", RowVisualId(firstRow));
        Assert.Equal(VisualSourceKind.Psd, firstRow.CharacterVisualBinding?.SourceKind);
        Assert.Equal("asset_psd_chen", firstRow.CharacterVisualBinding?.SourceAssetId);
        Assert.Equal("asset_png_chen_calm", firstRow.CharacterVisualBinding?.RenderedAssetId);
        Assert.Equal("chen.wav", RowVoiceId(firstRow));
        Assert.Null(RowVoiceText(firstRow));
        var binding = Assert.Single(updated.CharacterBindings);
        Assert.Equal("voices/new.wav", CharacterBindingVoiceId(binding));
        Assert.Equal("new reference", CharacterBindingVoiceText(binding));
    }

    [Fact]
    public void ApplyCharacterVisualBindingDoesNotOverwriteRowVoiceBindings()
    {
        var document = _service.UpdateRowVoiceBinding(
            DocumentWithTwoRowsInFirstScene(),
            "int_001",
            VoiceAssetBinding.FromProjectReference(
                "voices/old.wav",
                "old reference",
                "asset_voice_source",
                @"C:\voices\old.wav"));

        var updated = _service.ApplyCharacterVisualBinding(
            document,
            "char_chen",
            "Chen",
            VisualAssetBinding.FromImage("asset_character_new"));

        var firstRow = updated.RowsById["int_001"];
        Assert.Equal("asset_character_new", RowVisualId(firstRow));
        Assert.Equal(VisualSourceKind.Image, firstRow.CharacterVisualBinding?.SourceKind);
        Assert.Equal("voices/old.wav", RowVoiceId(firstRow));
        Assert.Equal("old reference", RowVoiceText(firstRow));
        Assert.Equal("asset_voice_source", firstRow.VoiceBinding?.SourceAssetId);
        Assert.Equal(@"C:\voices\old.wav", firstRow.VoiceBinding?.SyncedVoicePath);
    }



    [Fact]
    public void UpdateRowBindingKeepsVoiceSourceWhenOnlyReferenceTextChanges()
    {
        var document = _service.UpdateRowVoiceBinding(
            Document(),
            "int_001",
            VoiceAssetBinding.FromProjectReference(
                "voices/chen.wav",
                "old reference",
                "asset_voice_chen",
                @"C:\ComfyUI\models\voices\chen.wav"));

        var updated = _service.UpdateRowBinding(
            document,
            "int_001",
            "chen.png",
            "voices/chen.wav",
            "new reference");
        var row = updated.RowsById["int_001"];

        Assert.Equal("voices/chen.wav", RowVoiceId(row));
        Assert.Equal("new reference", RowVoiceText(row));
        Assert.Equal("asset_voice_chen", row.VoiceBinding?.SourceAssetId);
        Assert.Equal(@"C:\ComfyUI\models\voices\chen.wav", row.VoiceBinding?.SyncedVoicePath);
    }

    [Fact]
    public void UpdateRowVoiceReferenceCanClearVoiceReference()
    {
        var document = Document();

        var updated = _service.UpdateRowVoiceReference(document, "int_001", null, null);
        var row = updated.RowsById["int_001"];

        Assert.Null(RowVoiceId(row));
        Assert.Null(RowVoiceText(row));
        Assert.Equal("chen.png", RowVisualId(row));
    }

    [Fact]
    public void ApplySceneBindingUpdatesSceneAsset()
    {
        var document = Document();

        var updated = _service.ApplySceneBinding(document, "scene_001", "scene.png");

        Assert.Equal("scene.png", SceneVisualId(updated.Scenes.Single(scene => scene.SceneId == "scene_001")));
        Assert.Null(SceneVisualId(updated.Scenes.Single(scene => scene.SceneId == "scene_002")));
    }

    [Fact]
    public void ApplySceneBindingNoOpReturnsSameDocument()
    {
        var document = Document();

        var updated = _service.ApplySceneBinding(document, "missing", "scene.png");

        Assert.Same(document, updated);
    }

    [Fact]
    public void ApplyNarrationBindingUpdatesNarrationRowsOnly()
    {
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_001",
                    1,
                    "Scene",
                    string.Empty,
                    [
                        Row("int_001", "scene_001", 1),
                        Row("int_002", "scene_001", 2) with { Kind = WorkbenchRowKind.Narration, SubjectName = "Narration" }
                    ])
            ],
            []);

        var updated = _service.ApplyNarrationBinding(document, "narrator.wav", "narrator sample");

        Assert.Equal("narrator.wav", RowVoiceId(updated.RowsById["int_001"]));
        Assert.Equal("narrator.wav", RowVoiceId(updated.RowsById["int_002"]));
        Assert.Equal("narrator sample", RowVoiceText(updated.RowsById["int_002"]));
    }

    [Fact]
    public void ApplyNarrationBindingUpdatesDialogueAndNarrationRowsUsingNarratorVoice()
    {
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_001",
                    1,
                    "Scene",
                    string.Empty,
                    [
                        Row("int_001", "scene_001", 1) with
                        {
                            SourceItemType = "dialogue",
                            SourceRole = "Chen",
                            SourceRoleCharacterId = "char_chen"
                        },
                        Row("int_002", "scene_001", 2) with
                        {
                            Kind = WorkbenchRowKind.Narration,
                            SubjectName = "Chen",
                            SourceItemType = "thought",
                            SourceRole = "Chen",
                            SourceRoleCharacterId = "char_chen"
                        },
                        Row("int_003", "scene_001", 3) with
                        {
                            Kind = WorkbenchRowKind.Narration,
                            SubjectName = "旁白叙述",
                            SourceItemType = "narration"
                        }
                    ])
            ],
            []);

        var updated = _service.ApplyNarrationBinding(document, "narrator.wav", "narrator sample");

        Assert.Equal("narrator.wav", RowVoiceId(updated.RowsById["int_001"]));
        Assert.Equal("narrator.wav", RowVoiceId(updated.RowsById["int_002"]));
        Assert.Equal("narrator.wav", RowVoiceId(updated.RowsById["int_003"]));
        Assert.Equal("dialogue", updated.RowsById["int_001"].SourceItemType);
        Assert.Equal("thought", updated.RowsById["int_002"].SourceItemType);
        Assert.Equal("Chen", updated.RowsById["int_002"].SourceRole);
    }

    [Fact]
    public void BindThoughtNarrationVoicesToSourceRolesKeepsNarrationKindAndUsesCharacterVoice()
    {
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_001",
                    1,
                    "Scene",
                    string.Empty,
                    [
                        Row("int_001", "scene_001", 1) with
                        {
                            Kind = WorkbenchRowKind.Narration,
                            SubjectName = "旁白",
                            CharacterVisualBinding = VisualAssetBinding.None,
                            VoiceBinding = VoiceAssetBinding.FromProjectReference("narrator.wav", "narrator sample", null, null),
                            SourceItemType = "thought",
                            SourceRole = "Chen",
                            SourceRoleCharacterId = "char_chen"
                        }
                    ])
            ],
            []);
        var character = new Character(
            "char_chen",
            "Chen",
            "role",
            string.Empty,
            string.Empty,
            "Calm",
            new AssetBinding(
                Voice: "chen.wav",
                VoiceBinding: VoiceAssetBinding.FromProjectReference("chen.wav", "chen sample", "asset_voice_chen", @"C:\voices\chen.wav")));

        var updated = _service.BindThoughtNarrationVoicesToSourceRoles(document, [character]);
        var row = updated.RowsById["int_001"];

        Assert.Equal(WorkbenchRowKind.Narration, row.Kind);
        Assert.Equal("thought", row.SourceItemType);
        Assert.Equal("Chen", row.SourceRole);
        Assert.Equal("char_chen", row.SourceRoleCharacterId);
        Assert.Equal("Chen", row.VoiceRole);
        Assert.Equal("char_chen", row.VoiceRoleCharacterId);
        Assert.Equal("chen.wav", RowVoiceId(row));
        Assert.Equal("chen sample", RowVoiceText(row));
        Assert.Null(RowVisualId(row));
    }

    [Fact]
    public void RouteDialogueVoicesToSourceRolesCanToggleBackToNarratorWithoutChangingSourceJsonFields()
    {
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_001",
                    1,
                    "Scene",
                    string.Empty,
                    [
                        Row("int_001", "scene_001", 1) with
                        {
                            VoiceBinding = VoiceAssetBinding.FromProjectReference("narrator.wav", "narrator sample", null, null),
                            SourceItemType = "dialogue",
                            SourceRole = "Chen",
                            SourceRoleCharacterId = "char_chen"
                        }
                    ])
            ],
            []);
        var character = new Character(
            "char_chen",
            "Chen",
            "role",
            string.Empty,
            string.Empty,
            "Calm",
            new AssetBinding(
                Voice: "chen.wav",
                VoiceBinding: VoiceAssetBinding.FromProjectReference("chen.wav", "chen sample", null, null)));

        var routed = _service.RouteDialogueVoicesToSourceRoles(document, [character], routeToSourceRole: true);
        var restored = _service.ApplyNarrationBinding(
            _service.RouteDialogueVoicesToSourceRoles(routed, [character], routeToSourceRole: false),
            "narrator.wav",
            "narrator sample");

        Assert.Equal("chen.wav", RowVoiceId(routed.RowsById["int_001"]));
        Assert.Equal("Chen", routed.RowsById["int_001"].VoiceRole);
        Assert.Equal("char_chen", routed.RowsById["int_001"].VoiceRoleCharacterId);
        Assert.Equal("dialogue", routed.RowsById["int_001"].SourceItemType);
        Assert.Equal("Chen", routed.RowsById["int_001"].SourceRole);
        Assert.Equal("char_chen", routed.RowsById["int_001"].SourceRoleCharacterId);
        Assert.Equal("narrator.wav", RowVoiceId(restored.RowsById["int_001"]));
        Assert.Null(restored.RowsById["int_001"].VoiceRole);
        Assert.Null(restored.RowsById["int_001"].VoiceRoleCharacterId);
        Assert.Equal("dialogue", restored.RowsById["int_001"].SourceItemType);
        Assert.Equal("Chen", restored.RowsById["int_001"].SourceRole);
    }

    [Fact]
    public void RouteDialogueVoicesToSourceRolesFallsBackToDialogueSubjectForLegacyRows()
    {
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_001",
                    1,
                    "Scene",
                    string.Empty,
                    [
                        Row("int_001", "scene_001", 1)
                    ])
            ],
            []);
        var character = new Character(
            "char_chen",
            "Chen",
            "role",
            string.Empty,
            string.Empty,
            "Calm",
            new AssetBinding(
                Voice: "chen.wav",
                VoiceBinding: VoiceAssetBinding.FromProjectReference("chen.wav", "chen sample", null, null)));

        var routed = _service.RouteDialogueVoicesToSourceRoles(document, [character], routeToSourceRole: true);
        var row = routed.RowsById["int_001"];

        Assert.Equal("chen.wav", RowVoiceId(row));
        Assert.Equal("Chen", row.VoiceRole);
        Assert.Equal("char_chen", row.VoiceRoleCharacterId);
        Assert.Equal(string.Empty, row.SourceItemType);
        Assert.Equal(string.Empty, row.SourceRole);
    }

    [Fact]
    public void RouteDialogueVoicesToSourceRolesKeepsDefaultVoiceWhenCharacterVoiceBindingIsEmpty()
    {
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_001",
                    1,
                    "Scene",
                    string.Empty,
                    [
                        Row("int_001", "scene_001", 1) with
                        {
                            VoiceBinding = VoiceAssetBinding.FromProjectReference("default.wav", "default sample", null, null),
                            SourceItemType = "dialogue",
                            SourceRole = "Chen",
                            SourceRoleCharacterId = "char_chen"
                        }
                    ])
            ],
            []);
        var character = new Character(
            "char_chen",
            "Chen",
            "role",
            string.Empty,
            string.Empty,
            "Calm",
            new AssetBinding(Voice: "legacy-chen.wav"));

        var routed = _service.RouteDialogueVoicesToSourceRoles(document, [character], routeToSourceRole: true);
        var row = routed.RowsById["int_001"];

        Assert.Equal("default.wav", RowVoiceId(row));
        Assert.Equal("default sample", RowVoiceText(row));
        Assert.Null(row.VoiceRole);
        Assert.Null(row.VoiceRoleCharacterId);
        Assert.Equal("dialogue", row.SourceItemType);
        Assert.Equal("Chen", row.SourceRole);
        Assert.Equal("char_chen", row.SourceRoleCharacterId);
    }

    [Fact]
    public void ApplyNarrationBindingSkipsThoughtRowsAlreadyRoutedToRoleVoice()
    {
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_001",
                    1,
                    "Scene",
                    string.Empty,
                    [
                        Row("int_001", "scene_001", 1) with
                        {
                            Kind = WorkbenchRowKind.Narration,
                            SubjectName = "旁白",
                            VoiceBinding = VoiceAssetBinding.FromProjectReference("chen.wav", "chen sample", null, null),
                            SourceItemType = "thought",
                            SourceRole = "Chen",
                            SourceRoleCharacterId = "char_chen",
                            VoiceRole = "Chen",
                            VoiceRoleCharacterId = "char_chen"
                        },
                        Row("int_002", "scene_001", 2) with
                        {
                            Kind = WorkbenchRowKind.Narration,
                            SubjectName = "旁白",
                            VoiceBinding = VoiceAssetBinding.FromProjectReference("narrator.wav", "old narrator", null, null),
                            SourceItemType = "narration"
                        }
                    ])
            ],
            []);

        var updated = _service.ApplyNarrationBinding(document, "new-narrator.wav", "new narrator");

        Assert.Equal("chen.wav", RowVoiceId(updated.RowsById["int_001"]));
        Assert.Equal("chen sample", RowVoiceText(updated.RowsById["int_001"]));
        Assert.Equal("new-narrator.wav", RowVoiceId(updated.RowsById["int_002"]));
        Assert.Equal("new narrator", RowVoiceText(updated.RowsById["int_002"]));
    }

    [Fact]
    public void ApplyCharacterVoiceBindingUpdatesReferenceTableWithoutChangingRoutedRows()
    {
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_001",
                    1,
                    "Scene",
                    string.Empty,
                    [
                        Row("int_001", "scene_001", 1),
                        Row("int_002", "scene_001", 2) with
                        {
                            Kind = WorkbenchRowKind.Narration,
                            SubjectName = "旁白",
                            CharacterVisualBinding = VisualAssetBinding.None,
                            VoiceBinding = VoiceAssetBinding.FromProjectReference("chen-old.wav", "old", null, null),
                            SourceItemType = "thought",
                            SourceRole = "Chen",
                            SourceRoleCharacterId = "char_chen",
                            VoiceRole = "Chen",
                            VoiceRoleCharacterId = "char_chen"
                        }
                    ])
            ],
            []);

        var updated = _service.ApplyCharacterVoiceBinding(
            document,
            "char_chen",
            "Chen",
            VoiceAssetBinding.FromProjectReference("chen-new.wav", "new", null, null));

        Assert.Equal("chen.wav", RowVoiceId(updated.RowsById["int_001"]));
        Assert.Equal("chen-old.wav", RowVoiceId(updated.RowsById["int_002"]));
        var characterBinding = Assert.Single(updated.CharacterBindings, binding => binding.CharacterId == "char_chen");
        Assert.Equal("chen-new.wav", characterBinding.VoiceBinding?.EffectiveVoiceReferenceId);
        Assert.Equal("new", characterBinding.VoiceBinding?.EffectiveReferenceText);
        Assert.Null(RowVisualId(updated.RowsById["int_002"]));
    }

    [Fact]
    public void UpdateRowSubjectFromSpecialNarrationOptionToCharacterClearsNarrationSourceType()
    {
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_001",
                    1,
                    "Scene",
                    string.Empty,
                    [
                        Row("int_001", "scene_001", 1) with
                        {
                            Kind = WorkbenchRowKind.Narration,
                            SubjectName = "旁白叙述",
                            SubjectCharacterId = null,
                            SourceItemType = "narration",
                            SourceRole = string.Empty,
                            SourceRoleCharacterId = null
                        }
                    ])
            ],
            []);

        var updated = _service.UpdateRowSubject(document, "int_001", "Chen", "char_chen");
        var row = updated.RowsById["int_001"];

        Assert.Equal(WorkbenchRowKind.Dialogue, row.Kind);
        Assert.Equal("dialogue", row.SourceItemType);
        Assert.Equal("Chen", row.SourceRole);
        Assert.Equal("char_chen", row.SourceRoleCharacterId);
    }

    [Fact]
    public void UpdateRowSubjectForThoughtKeepsThoughtTypeAndChangesSourceRole()
    {
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_001",
                    1,
                    "Scene",
                    string.Empty,
                    [
                        Row("int_001", "scene_001", 1) with
                        {
                            Kind = WorkbenchRowKind.Narration,
                            SubjectName = "Chen",
                            SubjectCharacterId = null,
                            SourceItemType = "thought",
                            SourceRole = "Chen",
                            SourceRoleCharacterId = "char_chen"
                        }
                    ])
            ],
            []);

        var updated = _service.UpdateRowSubject(document, "int_001", "Lin", "char_lin");
        var row = updated.RowsById["int_001"];

        Assert.Equal(WorkbenchRowKind.Narration, row.Kind);
        Assert.Equal("thought", row.SourceItemType);
        Assert.Equal("Lin", row.SourceRole);
        Assert.Equal("char_lin", row.SourceRoleCharacterId);
    }

    [Fact]
    public void ApplyNarrationBindingReplacesOnlyScenesWithNarrationRows()
    {
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_001",
                    1,
                    "Scene",
                    string.Empty,
                    [Row("int_001", "scene_001", 1)]),
                new WorkbenchSceneDraft(
                    "scene_002",
                    2,
                    "Scene 2",
                    string.Empty,
                    [Row("int_002", "scene_002", 1) with { Kind = WorkbenchRowKind.Narration, SubjectName = "Narration" }]),
                new WorkbenchSceneDraft(
                    "scene_003",
                    3,
                    "Scene 3",
                    string.Empty,
                    [])
            ],
            []);
        var firstScene = document.Scenes[0];
        var thirdScene = document.Scenes[2];

        var updated = _service.ApplyNarrationBinding(document, "narrator.wav", "narrator sample");

        Assert.NotSame(firstScene, updated.Scenes[0]);
        Assert.NotSame(document.Scenes[1], updated.Scenes[1]);
        Assert.Same(thirdScene, updated.Scenes[2]);
        Assert.Equal("narrator.wav", RowVoiceId(updated.RowsById["int_001"]));
        Assert.Equal("narrator.wav", RowVoiceId(updated.RowsById["int_002"]));
    }

    [Fact]
    public void ApplyNarrationBindingUpdatesMultipleNarrationRowsInSameScene()
    {
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_001",
                    1,
                    "Scene",
                    string.Empty,
                    [
                        Row("int_001", "scene_001", 1),
                        Row("int_002", "scene_001", 2) with { Kind = WorkbenchRowKind.Narration, SubjectName = "Narration" },
                        Row("int_003", "scene_001", 3) with { Kind = WorkbenchRowKind.Narration, SubjectName = "Narration" }
                    ])
            ],
            []);

        var updated = _service.ApplyNarrationBinding(document, "narrator.wav", "narrator sample");

        Assert.Equal("narrator.wav", RowVoiceId(updated.RowsById["int_001"]));
        Assert.Equal("narrator.wav", RowVoiceId(updated.RowsById["int_002"]));
        Assert.Equal("narrator.wav", RowVoiceId(updated.RowsById["int_003"]));
        Assert.Equal("narrator sample", RowVoiceText(updated.RowsById["int_003"]));
    }

    [Fact]
    public void ApplyNarrationBindingNoOpReturnsSameDocument()
    {
        var document = Document();

        var updated = _service.ApplyNarrationBinding(document, "chen.wav", null);

        Assert.Same(document, updated);
    }

    [Fact]
    public void ApplyCharacterVoiceBindingStoresProjectSourceAndSyncedPath()
    {
        var document = Document();
        var voiceBinding = VoiceAssetBinding.FromProjectReference(
            "chen_project.wav",
            "Chen project reference",
            "asset_voice_chen",
            @"C:\ComfyUI\models\voices\chen_project.wav");

        var updated = _service.ApplyCharacterVoiceBinding(document, "char_chen", "Chen", voiceBinding);

        var row = updated.RowsById["int_001"];
        var binding = Assert.Single(updated.CharacterBindings);
        Assert.Equal("chen.wav", RowVoiceId(row));
        Assert.Null(RowVoiceText(row));
        Assert.Null(row.VoiceBinding!.SourceAssetId);
        Assert.Null(row.VoiceBinding.SyncedVoicePath);
        Assert.Equal("chen_project.wav", CharacterBindingVoiceId(binding));
        Assert.Equal("asset_voice_chen", binding.VoiceBinding!.SourceAssetId);
        Assert.Equal(@"C:\ComfyUI\models\voices\chen_project.wav", binding.VoiceBinding.SyncedVoicePath);
    }

    [Fact]
    public void ApplyNarrationVoiceBindingStoresProjectSourceAndSyncedPath()
    {
        var document = new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_001",
                    1,
                    "Scene",
                    string.Empty,
                    [
                        Row("int_001", "scene_001", 1),
                        Row("int_002", "scene_001", 2) with { Kind = WorkbenchRowKind.Narration, SubjectName = "旁白" }
                    ])
            ],
            []);
        var voiceBinding = VoiceAssetBinding.FromProjectReference(
            "narrator_project.wav",
            "Narrator project reference",
            "asset_voice_narrator",
            @"C:\ComfyUI\models\voices\narrator_project.wav");

        var updated = _service.ApplyNarrationVoiceBinding(document, voiceBinding);

        var dialogue = updated.RowsById["int_001"];
        var narration = updated.RowsById["int_002"];
        Assert.Equal("narrator_project.wav", RowVoiceId(dialogue));
        Assert.Equal("Narrator project reference", RowVoiceText(dialogue));
        Assert.Equal("narrator_project.wav", RowVoiceId(narration));
        Assert.Equal("Narrator project reference", RowVoiceText(narration));
        Assert.Equal("asset_voice_narrator", narration.VoiceBinding!.SourceAssetId);
        Assert.Equal(@"C:\ComfyUI\models\voices\narrator_project.wav", narration.VoiceBinding.SyncedVoicePath);
    }

    private static WorkbenchDocument Document()
    {
        return new WorkbenchDocument(
            [
                new WorkbenchSceneDraft(
                    "scene_001",
                    1,
                    "Scene 1",
                    string.Empty,
                    [Row("int_001", "scene_001", 1)]),
                new WorkbenchSceneDraft(
                    "scene_002",
                    2,
                    "Scene 2",
                    string.Empty,
                    [])
            ],
            [CharacterBinding("char_chen", "old.png", "old.wav")]);
    }

    private static WorkbenchDocument DocumentWithThreeScenes()
    {
        var document = Document();
        return document with
        {
            Scenes =
            [
                document.Scenes[0],
                document.Scenes[1],
                new WorkbenchSceneDraft(
                    "scene_003",
                    3,
                    "Scene 3",
                    string.Empty,
                    [Row("int_003", "scene_003", 1) with { SubjectCharacterId = "char_lin", SubjectName = "Lin" }])
            ]
        };
    }

    private static WorkbenchDocument DocumentWithTwoRowsInFirstScene()
    {
        var document = Document();
        return document with
        {
            Scenes =
            [
                document.Scenes[0] with
                {
                    Rows =
                    [
                        document.Scenes[0].Rows[0],
                        Row("int_002", "scene_001", 2)
                    ]
                },
                document.Scenes[1]
            ]
        };
    }

    private static WorkbenchDocument DocumentWithThreeRowsInFirstScene()
    {
        var document = Document();
        return document with
        {
            Scenes =
            [
                document.Scenes[0] with
                {
                    Rows =
                    [
                        document.Scenes[0].Rows[0],
                        Row("int_002", "scene_001", 2),
                        Row("int_003", "scene_001", 3)
                    ]
                },
                document.Scenes[1]
            ]
        };
    }

    private static WorkbenchDocument DocumentWithFourRowsInFirstScene()
    {
        var document = DocumentWithThreeRowsInFirstScene();
        return document with
        {
            Scenes =
            [
                document.Scenes[0] with
                {
                    Rows =
                    [
                        document.Scenes[0].Rows[0],
                        document.Scenes[0].Rows[1],
                        document.Scenes[0].Rows[2],
                        Row("int_004", "scene_001", 4)
                    ]
                },
                document.Scenes[1]
            ]
        };
    }

    private static WorkbenchDocument DocumentWithEmotionVectors()
    {
        var document = Document();
        return document with
        {
            Scenes =
            [
                document.Scenes[0] with
                {
                    Rows =
                    [
                        document.Scenes[0].Rows[0] with
                        {
                            Emotion = "Calm 0.6",
                            EmotionVectors = new Dictionary<string, double>(StringComparer.Ordinal)
                            {
                                ["Calm"] = 0.6
                            },
                            EmotionAlpha = 1
                        }
                    ]
                },
                document.Scenes[1]
            ]
        };
    }

    private static WorkbenchRowDraft Row(string interactionId, string sceneId, int order)
    {
        return new WorkbenchRowDraft(
            interactionId,
            sceneId,
            order,
            WorkbenchRowKind.Dialogue,
            "Chen",
            "char_chen",
            "Calm 0.6",
            "Hello",
            null,
            null,
            new WorkbenchTaskLinks($"tts_{interactionId}"),
            VisualAssetBinding.FromImage("chen.png"),
            VoiceAssetBinding.FromProjectReference("chen.wav", null, null, null));
    }

    private static WorkbenchCharacterBindingEdit CharacterBinding(string characterId, string? visualId, string? voiceId, string? referenceText = null)
    {
        return new WorkbenchCharacterBindingEdit(
            characterId,
            VisualAssetBinding.FromImage(visualId),
            VoiceAssetBinding.FromProjectReference(voiceId, referenceText, null, null));
    }

    private static string? SceneVisualId(WorkbenchSceneDraft scene)
    {
        return scene.VisualBinding?.EffectiveAssetId;
    }

    private static string? RowVisualId(WorkbenchRowDraft row)
    {
        return row.CharacterVisualBinding?.EffectiveAssetId;
    }

    private static string? RowVoiceId(WorkbenchRowDraft row)
    {
        return row.VoiceBinding?.EffectiveVoiceReferenceId;
    }

    private static string? RowVoiceText(WorkbenchRowDraft row)
    {
        return row.VoiceBinding?.EffectiveReferenceText;
    }

    private static string? CharacterBindingVisualId(WorkbenchCharacterBindingEdit binding)
    {
        return binding.VisualBinding?.EffectiveAssetId;
    }

    private static string? CharacterBindingVoiceId(WorkbenchCharacterBindingEdit binding)
    {
        return binding.VoiceBinding?.EffectiveVoiceReferenceId;
    }

    private static string? CharacterBindingVoiceText(WorkbenchCharacterBindingEdit binding)
    {
        return binding.VoiceBinding?.EffectiveReferenceText;
    }
}



