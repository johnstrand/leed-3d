namespace Leed3D.Kernel.Scene;

/// <summary>
/// A snapshot record representing a state change in the editor.
/// </summary>
public sealed record UndoRecord(string Description, EditorScene BeforeState, EditorScene AfterState);

/// <summary>
/// Bounded snapshot-based Undo/Redo stack for interactive operations, gizmo drags, and topology edits.
/// </summary>
public sealed class UndoRedoStack
{
    private readonly LinkedList<UndoRecord> _undoList = new();
    private readonly LinkedList<UndoRecord> _redoList = new();
    private (string Description, EditorScene Snapshot)? _inProgressTransaction;

    public int MaxHistory { get; set; }

    public UndoRedoStack(int maxHistory = 64)
    {
        MaxHistory = System.Math.Max(1, maxHistory);
    }

    public bool CanUndo => _undoList.Count > 0;
    public bool CanRedo => _redoList.Count > 0;

    public string? NextUndoDescription => _undoList.Last?.Value.Description;
    public string? NextRedoDescription => _redoList.Last?.Value.Description;

    public int UndoCount => _undoList.Count;
    public int RedoCount => _redoList.Count;

    /// <summary>
    /// Records a completed discrete action. Clears any redo history.
    /// </summary>
    public void RecordAction(string description, EditorScene sceneBefore, EditorScene sceneAfter)
    {
        ArgumentNullException.ThrowIfNull(sceneBefore);
        ArgumentNullException.ThrowIfNull(sceneAfter);

        _inProgressTransaction = null;
        _redoList.Clear();

        var record = new UndoRecord(description, sceneBefore.CloneSnapshot(), sceneAfter.CloneSnapshot());
        _undoList.AddLast(record);

        while (_undoList.Count > MaxHistory)
        {
            _undoList.RemoveFirst();
        }
    }

    /// <summary>
    /// Begins an interactive transaction (e.g., when the user starts dragging a gizmo or starts a modal transform).
    /// </summary>
    public void BeginTransaction(string description, EditorScene currentScene)
    {
        ArgumentNullException.ThrowIfNull(currentScene);
        _inProgressTransaction = (description, currentScene.CloneSnapshot());
    }

    /// <summary>
    /// Commits an interactive transaction, recording an undo entry with the snapshot taken at BeginTransaction.
    /// </summary>
    public void CommitTransaction(EditorScene currentScene)
    {
        ArgumentNullException.ThrowIfNull(currentScene);

        if (_inProgressTransaction is null)
        {
            return;
        }

        (string desc, EditorScene before) = _inProgressTransaction.Value;
        _inProgressTransaction = null;

        RecordAction(desc, before, currentScene);
    }

    /// <summary>
    /// Cancels an in-progress interactive transaction and rolls back targetSceneToRestore
    /// to the initial state captured at BeginTransaction. Does not pollute the undo stack.
    /// </summary>
    public void CancelTransaction(EditorScene targetSceneToRestore)
    {
        ArgumentNullException.ThrowIfNull(targetSceneToRestore);

        if (_inProgressTransaction is null)
        {
            return;
        }

        (_, EditorScene before) = _inProgressTransaction.Value;
        _inProgressTransaction = null;

        targetSceneToRestore.RestoreFromSnapshot(before);
    }

    /// <summary>
    /// Reverts the most recent action, updating targetScene to the before state.
    /// </summary>
    public bool Undo(EditorScene targetScene)
    {
        ArgumentNullException.ThrowIfNull(targetScene);

        if (_undoList.Count == 0)
        {
            return false;
        }

        UndoRecord record = _undoList.Last!.Value;
        _undoList.RemoveLast();

        targetScene.RestoreFromSnapshot(record.BeforeState);
        _redoList.AddLast(record);
        return true;
    }

    /// <summary>
    /// Re-applies the most recently undone action, updating targetScene to the after state.
    /// </summary>
    public bool Redo(EditorScene targetScene)
    {
        ArgumentNullException.ThrowIfNull(targetScene);

        if (_redoList.Count == 0)
        {
            return false;
        }

        UndoRecord record = _redoList.Last!.Value;
        _redoList.RemoveLast();

        targetScene.RestoreFromSnapshot(record.AfterState);
        _undoList.AddLast(record);
        return true;
    }

    /// <summary>
    /// Clears both undo and redo histories.
    /// </summary>
    public void Clear()
    {
        _undoList.Clear();
        _redoList.Clear();
        _inProgressTransaction = null;
    }
}
