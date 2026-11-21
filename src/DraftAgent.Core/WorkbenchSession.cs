namespace DraftAgent.Core;

public sealed class WorkbenchSession
{
    private readonly List<WorkbenchSessionState> _undoHistory = [];
    private readonly List<WorkbenchSessionState> _redoHistory = [];
    private WorkbenchCoalescingScope? _coalescingScope;

    public WorkbenchSession(WorkbenchDocument document)
    {
        Document = document;
    }

    public WorkbenchDocument Document { get; private set; }

    public bool IsDirty { get; private set; }

    public bool CanUndo => _undoHistory.Count > 0;

    public bool CanRedo => _redoHistory.Count > 0;

    public string? NextUndoOperationName => CanUndo ? _undoHistory[^1].OperationName : null;

    public string? NextRedoOperationName => CanRedo ? _redoHistory[^1].OperationName : null;

    public void EndCoalescing()
    {
        ClearCoalescingScope();
    }

    public bool HasSameDocument(WorkbenchDocument document)
    {
        if (ReferenceEquals(document, Document))
        {
            return true;
        }

        return AreDocumentsEqual(document, Document);
    }

    public WorkbenchDocument Execute(
        Func<WorkbenchDocument, WorkbenchDocument> command,
        string operationName = WorkbenchSessionOperationNames.WorkbenchEdit)
    {
        ClearCoalescingScope();
        var next = command(Document);
        if (ReferenceEquals(next, Document))
        {
            return Document;
        }

        _undoHistory.Add(CaptureState(operationName));
        _redoHistory.Clear();
        Document = next;
        IsDirty = true;

        return Document;
    }

    public WorkbenchDocument ExecuteCoalesced(
        Func<WorkbenchDocument, WorkbenchDocument> command,
        string operationName,
        string coalescingKey)
    {
        var normalizedOperationName = NormalizeOperationName(operationName);
        if (string.IsNullOrWhiteSpace(coalescingKey))
        {
            return Execute(command, normalizedOperationName);
        }

        var normalizedCoalescingKey = coalescingKey.Trim();
        var next = command(Document);
        if (ReferenceEquals(next, Document))
        {
            return Document;
        }

        if (_coalescingScope is { } scope &&
            string.Equals(scope.OperationName, normalizedOperationName, StringComparison.Ordinal) &&
            string.Equals(scope.CoalescingKey, normalizedCoalescingKey, StringComparison.Ordinal) &&
            _undoHistory.Count > 0)
        {
            _redoHistory.Clear();
            Document = next;
            IsDirty = true;
            return Document;
        }

        _undoHistory.Add(CaptureState(normalizedOperationName));
        _redoHistory.Clear();
        _coalescingScope = new WorkbenchCoalescingScope(normalizedOperationName, normalizedCoalescingKey);
        Document = next;
        IsDirty = true;

        return Document;
    }

    public WorkbenchDocument Preview(Func<WorkbenchDocument, WorkbenchDocument> command)
    {
        ClearCoalescingScope();
        var next = command(Document);
        if (ReferenceEquals(next, Document))
        {
            return Document;
        }

        Document = next;
        IsDirty = true;

        return Document;
    }

    public bool Undo()
    {
        ClearCoalescingScope();
        if (!CanUndo)
        {
            return false;
        }

        var previous = Pop(_undoHistory);
        _redoHistory.Add(CaptureState(previous.OperationName));
        RestoreState(previous);
        return true;
    }

    public bool Redo()
    {
        ClearCoalescingScope();
        if (!CanRedo)
        {
            return false;
        }

        var next = Pop(_redoHistory);
        _undoHistory.Add(CaptureState(next.OperationName));
        RestoreState(next);
        return true;
    }

    public WorkbenchSessionCheckpoint CreateCheckpoint()
    {
        ClearCoalescingScope();
        return new WorkbenchSessionCheckpoint(
            CaptureState(),
            _undoHistory,
            _undoHistory.Count,
            _redoHistory,
            _redoHistory.Count);
    }

    public void Restore(WorkbenchSessionCheckpoint checkpoint)
    {
        ClearCoalescingScope();
        RestoreState(checkpoint.Current);
        RestoreHistory(_undoHistory, checkpoint.UndoHistory, checkpoint.UndoHistoryCount);
        RestoreHistory(_redoHistory, checkpoint.RedoHistory, checkpoint.RedoHistoryCount);
    }

    public void SquashHistoryToCheckpoint(
        WorkbenchSessionCheckpoint checkpoint,
        string operationName = WorkbenchSessionOperationNames.WorkbenchDrag)
    {
        ClearCoalescingScope();
        var current = CaptureState();
        RestoreHistory(_undoHistory, checkpoint.UndoHistory, checkpoint.UndoHistoryCount);
        ReplaceHistory(_redoHistory, []);

        if (AreDocumentsEqual(current.Document, checkpoint.Current.Document))
        {
            RestoreState(checkpoint.Current);
            return;
        }

        _undoHistory.Add(checkpoint.Current with { OperationName = NormalizeOperationName(operationName) });
        Document = current.Document;
        IsDirty = true;
    }

    public void Reset(WorkbenchDocument document)
    {
        ClearCoalescingScope();
        Document = document;
        IsDirty = false;
        _undoHistory.Clear();
        _redoHistory.Clear();
    }

    public void Restore(WorkbenchDocument document, bool isDirty)
    {
        ClearCoalescingScope();
        Document = document;
        IsDirty = isDirty;
        _undoHistory.Clear();
        _redoHistory.Clear();
    }

    public void MarkClean()
    {
        ClearCoalescingScope();
        IsDirty = false;
        _undoHistory.Clear();
        _redoHistory.Clear();
    }

    private WorkbenchSessionState CaptureState(string operationName = "")
    {
        return new WorkbenchSessionState(Document, IsDirty, NormalizeOperationName(operationName));
    }

    private static string NormalizeOperationName(string? operationName)
    {
        return string.IsNullOrWhiteSpace(operationName)
            ? WorkbenchSessionOperationNames.WorkbenchEdit
            : operationName.Trim();
    }

    private void RestoreState(WorkbenchSessionState state)
    {
        Document = state.Document;
        IsDirty = state.IsDirty;
    }

    private void ClearCoalescingScope()
    {
        _coalescingScope = null;
    }

    private static void ReplaceHistory(
        List<WorkbenchSessionState> target,
        IReadOnlyList<WorkbenchSessionState> source)
    {
        target.Clear();
        target.AddRange(source);
    }

    private static void RestoreHistory(
        List<WorkbenchSessionState> target,
        IReadOnlyList<WorkbenchSessionState> source,
        int count)
    {
        if (ReferenceEquals(target, source))
        {
            if (target.Count > count)
            {
                target.RemoveRange(count, target.Count - count);
            }

            return;
        }

        target.Clear();
        for (var index = 0; index < count; index++)
        {
            target.Add(source[index]);
        }
    }

    private static WorkbenchSessionState Pop(List<WorkbenchSessionState> history)
    {
        var index = history.Count - 1;
        var state = history[index];
        history.RemoveAt(index);
        return state;
    }

    private static bool AreDocumentsEqual(WorkbenchDocument left, WorkbenchDocument right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Scenes.Count != right.Scenes.Count ||
            left.CharacterBindings.Count != right.CharacterBindings.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Scenes.Count; index++)
        {
            if (!AreScenesEqual(left.Scenes[index], right.Scenes[index]))
            {
                return false;
            }
        }

        for (var index = 0; index < left.CharacterBindings.Count; index++)
        {
            if (!AreCharacterBindingsEqual(left.CharacterBindings[index], right.CharacterBindings[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreScenesEqual(WorkbenchSceneDraft left, WorkbenchSceneDraft right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.SceneId != right.SceneId ||
            left.Order != right.Order ||
            left.Name != right.Name ||
            left.Effect != right.Effect ||
            !VisualAssetsEqual(left.VisualBinding, right.VisualBinding) ||
            left.Rows.Count != right.Rows.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Rows.Count; index++)
        {
            if (!AreRowsEqual(left.Rows[index], right.Rows[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreRowsEqual(WorkbenchRowDraft left, WorkbenchRowDraft right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        return left.InteractionId == right.InteractionId &&
            left.SceneId == right.SceneId &&
            left.Order == right.Order &&
            left.Kind == right.Kind &&
            left.SubjectName == right.SubjectName &&
            left.SubjectCharacterId == right.SubjectCharacterId &&
            left.Emotion == right.Emotion &&
            left.Text == right.Text &&
            VisualAssetsEqual(left.CharacterVisualBinding, right.CharacterVisualBinding) &&
            VoiceBindingsEqual(left.VoiceBinding, right.VoiceBinding) &&
            left.EmotionAlpha == right.EmotionAlpha &&
            left.TaskLinks == right.TaskLinks &&
            AreDictionariesEqual(left.EmotionVectors, right.EmotionVectors);
    }

    private static bool AreCharacterBindingsEqual(
        WorkbenchCharacterBindingEdit left,
        WorkbenchCharacterBindingEdit right)
    {
        return left.CharacterId == right.CharacterId &&
            VisualAssetsEqual(left.VisualBinding, right.VisualBinding) &&
            VoiceBindingsEqual(left.VoiceBinding, right.VoiceBinding);
    }

    private static bool VisualAssetsEqual(
        VisualAssetBinding? leftBinding,
        VisualAssetBinding? rightBinding)
    {
        return VisualAssetBinding.SemanticEquals(leftBinding, rightBinding);
    }

    private static bool VoiceBindingsEqual(
        VoiceAssetBinding? leftBinding,
        VoiceAssetBinding? rightBinding)
    {
        return VoiceAssetBinding.SemanticEquals(leftBinding, rightBinding);
    }

    private static bool AreDictionariesEqual(
        IReadOnlyDictionary<string, double>? left,
        IReadOnlyDictionary<string, double>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.Count == right.Count &&
            left.All(pair => right.TryGetValue(pair.Key, out var value) && value.Equals(pair.Value));
    }
}

public sealed record WorkbenchSessionCheckpoint(
    WorkbenchSessionState Current,
    IReadOnlyList<WorkbenchSessionState> UndoHistory,
    int UndoHistoryCount,
    IReadOnlyList<WorkbenchSessionState> RedoHistory,
    int RedoHistoryCount);

public sealed record WorkbenchSessionState(
    WorkbenchDocument Document,
    bool IsDirty,
    string OperationName = "");

internal sealed record WorkbenchCoalescingScope(
    string OperationName,
    string CoalescingKey);

public static class WorkbenchSessionOperationNames
{
    public const string WorkbenchEdit = "workbench.edit";
    public const string WorkbenchDrag = "workbench.drag";
    public const string AddScene = "workbench.add_scene";
    public const string AddRow = "workbench.add_row";
    public const string RemoveScene = "workbench.remove_scene";
    public const string RemoveRow = "workbench.remove_row";
    public const string MoveRow = "workbench.move_row";
    public const string MoveScene = "workbench.move_scene";
    public const string UpdateSceneText = "workbench.update_scene_text";
    public const string UpdateSceneAsset = "workbench.update_scene_asset";
    public const string UpdateRowText = "workbench.update_row_text";
    public const string UpdateRowSubject = "workbench.update_row_subject";
    public const string UpdateRowEmotion = "workbench.update_row_emotion";
    public const string UpdateRowBinding = "workbench.update_row_binding";
    public const string ApplyBindingTarget = "workbench.apply_binding_target";
    public const string SyncViewModelDocument = "workbench.sync_view_model_document";
}

public static class WorkbenchSessionCoalescingKeys
{
    public static string RowText(string interactionId)
    {
        return $"workbench.row_text:{interactionId}";
    }

    public static string RowEmotion(string interactionId)
    {
        return $"workbench.row_emotion:{interactionId}";
    }
}
