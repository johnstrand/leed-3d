using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Selection;

namespace Leed3D.Kernel.IO;

/// <summary>
/// Headless document session controller coordinating <see cref="EditorScene"/>, <see cref="UndoRedoStack"/>,
/// <see cref="SelectionManager"/>, active document file path (<see cref="CurrentFilePath"/>), dirty state
/// (<see cref="HasUnsavedChanges"/>), and atomic Open / Save / Save As / New Level transitions.
/// </summary>
public sealed class LevelDocumentSession
{
    public EditorScene Scene { get; }
    public UndoRedoStack UndoStack { get; }
    public SelectionManager Selection { get; }

    /// <summary>
    /// Gets or sets the active file path for the currently open <c>.level.json</c> document,
    /// or <c>null</c> for an unsaved new document.
    /// </summary>
    public string? CurrentFilePath { get; set; }

    /// <summary>
    /// Gets the last file I/O or validation error message when an Open or Save operation fails,
    /// or <c>null</c> if the most recent operation succeeded.
    /// </summary>
    public string? LastErrorMessage { get; private set; }

    /// <summary>
    /// Returns <c>true</c> if the document has unsaved edits relative to the last opened, created, or saved state.
    /// </summary>
    public bool HasUnsavedChanges => UndoStack.HasUnsavedChanges;

    public LevelDocumentSession(EditorScene scene, UndoRedoStack undoStack, SelectionManager selection)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(undoStack);
        ArgumentNullException.ThrowIfNull(selection);

        Scene = scene;
        UndoStack = undoStack;
        Selection = selection;
    }

    /// <summary>
    /// Loads and validates a <c>.level.json</c> file from disk into <see cref="Scene"/>.
    /// If loading or validation fails, returns <c>false</c> with a descriptive <paramref name="errorMessage"/>
    /// without modifying <see cref="Scene"/>, <see cref="Selection"/>, <see cref="UndoStack"/>, or <see cref="CurrentFilePath"/>.
    /// </summary>
    public bool TryOpenLevel(string? filePath, out string? errorMessage, Action? beforeSceneReplace = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            errorMessage = "Level file path cannot be empty.";
            LastErrorMessage = errorMessage;
            return false;
        }

        string trimmedPath = filePath.Trim();
        if (!File.Exists(trimmedPath))
        {
            errorMessage = $"Level file does not exist: {trimmedPath}";
            LastErrorMessage = errorMessage;
            return false;
        }

        EditorScene loadedScene;
        try
        {
            loadedScene = LevelSerializer.LoadEditorScene(trimmedPath);
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to open level '{trimmedPath}': {ex.Message}";
            LastErrorMessage = errorMessage;
            return false;
        }

        ReplaceSceneWithDocument(loadedScene, trimmedPath, beforeSceneReplace);
        errorMessage = null;
        return true;
    }

    /// <summary>
    /// Replaces the current scene with an in-memory <paramref name="documentScene"/>, clears <see cref="UndoStack"/>,
    /// clears <see cref="Selection"/>, sets <see cref="CurrentFilePath"/>, and marks the document clean.
    /// </summary>
    public void ReplaceSceneWithDocument(EditorScene documentScene, string? filePath, Action? beforeSceneReplace = null)
    {
        ArgumentNullException.ThrowIfNull(documentScene);

        beforeSceneReplace?.Invoke();
        Scene.RestoreFromSnapshot(documentScene);
        Selection.ClearSelection();
        UndoStack.Clear();
        UndoStack.MarkSaved();
        CurrentFilePath = string.IsNullOrWhiteSpace(filePath) ? null : filePath.Trim();
        LastErrorMessage = null;
    }

    /// <summary>
    /// Resets the workspace to a fresh empty <see cref="EditorScene"/>, clears <see cref="UndoStack"/> and
    /// <see cref="Selection"/>, resets <see cref="CurrentFilePath"/> to <c>null</c>, and marks the document clean.
    /// </summary>
    public void CreateNewLevel(string levelName = "Untitled Level", Action? beforeSceneReplace = null)
    {
        string resolvedName = string.IsNullOrWhiteSpace(levelName) ? "Untitled Level" : levelName;
        ReplaceSceneWithDocument(new EditorScene(resolvedName), filePath: null, beforeSceneReplace);
    }

    /// <summary>
    /// Ensures a destination file path ends with <c>.level.json</c> (or <c>.json</c> if explicitly provided).
    /// </summary>
    public static string NormalizeLevelFilePath(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        string trimmed = filePath.Trim();

        if (trimmed.EndsWith(".level.json", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        if (trimmed.EndsWith(".level", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed + ".json";
        }

        return trimmed + ".level.json";
    }

    /// <summary>
    /// Extracts a clean level name from a <c>.level.json</c> or <c>.json</c> file path.
    /// </summary>
    public static string DeriveLevelNameFromPath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return "Untitled Level";
        }

        string fileName = Path.GetFileName(filePath.Trim());
        if (fileName.EndsWith(".level.json", StringComparison.OrdinalIgnoreCase))
        {
            fileName = fileName[..^".level.json".Length];
        }
        else if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            fileName = fileName[..^".json".Length];
        }

        return string.IsNullOrWhiteSpace(fileName) ? "Untitled Level" : fileName;
    }

    /// <summary>
    /// Saves the active <see cref="Scene"/> to <see cref="CurrentFilePath"/>.
    /// Returns <c>false</c> if <see cref="CurrentFilePath"/> is not set or if saving fails.
    /// </summary>
    public bool TrySaveLevel(out string? errorMessage, bool bakeRightHandedRuntime = false, Action? beforeSave = null)
    {
        if (string.IsNullOrWhiteSpace(CurrentFilePath))
        {
            errorMessage = "No active file path is set for this level.";
            LastErrorMessage = errorMessage;
            return false;
        }

        return TrySaveLevelAs(CurrentFilePath, out errorMessage, bakeRightHandedRuntime, updateSceneName: false, beforeSave);
    }

    /// <summary>
    /// Saves the active <see cref="Scene"/> to <paramref name="filePath"/>, updates <see cref="CurrentFilePath"/>,
    /// optionally updates <see cref="EditorScene.Name"/> to match the new filename, and marks the document as saved
    /// (<see cref="HasUnsavedChanges"/> becomes <c>false</c>).
    /// </summary>
    public bool TrySaveLevelAs(
        string? filePath,
        out string? errorMessage,
        bool bakeRightHandedRuntime = false,
        bool updateSceneName = true,
        Action? beforeSave = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            errorMessage = "Destination file path cannot be empty.";
            LastErrorMessage = errorMessage;
            return false;
        }

        string normalizedPath = NormalizeLevelFilePath(filePath);
        string previousSceneName = Scene.Name;

        try
        {
            beforeSave?.Invoke();
            if (updateSceneName)
            {
                Scene.Name = DeriveLevelNameFromPath(normalizedPath);
            }

            LevelSerializer.Save(Scene, normalizedPath, bakeRightHandedRuntime);
            CurrentFilePath = normalizedPath;
            UndoStack.MarkSaved();
            LastErrorMessage = null;
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            Scene.Name = previousSceneName;
            errorMessage = $"Failed to save level to '{normalizedPath}': {ex.Message}";
            LastErrorMessage = errorMessage;
            return false;
        }
    }
}
