using System.Numerics;
using Leed3D.Kernel.Entities;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Selection;
using Leed3D.Kernel.Texturing;
using Leed3D.Runtime.Metadata;

namespace Leed3D.Kernel.Scene;

/// <summary>
/// Headless clipboard buffer supporting Copy (<c>Ctrl+C</c>), Cut (<c>Ctrl+X</c>), and Paste (<c>Ctrl+V</c>)
/// for <see cref="SceneObject"/>s, <see cref="LevelEntity"/> instances, and face material/UV/metadata attributes.
/// </summary>
public sealed class SceneClipboard
{
    private readonly List<SceneObject> _copiedObjects = [];
    private readonly List<LevelEntity> _copiedEntities = [];

    private string? _copiedFaceMaterialId;
    private FaceUvMapping? _copiedFaceUvMapping;
    private ElementMetadata? _copiedFaceMetadata;

    public IReadOnlyList<SceneObject> CopiedObjects => _copiedObjects;
    public IReadOnlyList<LevelEntity> CopiedEntities => _copiedEntities;

    public bool HasObjectsOrEntities => _copiedObjects.Count > 0 || _copiedEntities.Count > 0;
    public bool HasFaceAttributes => _copiedFaceMaterialId is not null && _copiedFaceUvMapping is not null;
    public bool HasItems => HasObjectsOrEntities || HasFaceAttributes;

    public int CopiedObjectCount => _copiedObjects.Count;
    public int CopiedEntityCount => _copiedEntities.Count;

    /// <summary>
    /// Clears all clipboard contents.
    /// </summary>
    public void Clear()
    {
        _copiedObjects.Clear();
        _copiedEntities.Clear();
        _copiedFaceMaterialId = null;
        _copiedFaceUvMapping = null;
        _copiedFaceMetadata = null;
    }

    /// <summary>
    /// Checks whether the current selection can be copied.
    /// </summary>
    public static bool CanCopy(EditorScene scene, SelectionManager selection)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(selection);

        if (selection.Mode == InteractionMode.Object)
        {
            return selection.SelectedObjectIds.Count > 0 || selection.SelectedEntityIds.Count > 0;
        }

        if (selection.Mode == InteractionMode.Face &&
            selection.ActiveObjectId.HasValue &&
            selection.SelectedFaceIds.Count > 0 &&
            scene.GetObject(selection.ActiveObjectId.Value)?.Mesh is not null)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks whether the current selection can be cut (Object mode selections of objects or entities).
    /// </summary>
    public static bool CanCut(EditorScene scene, SelectionManager selection)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(selection);

        return selection.Mode == InteractionMode.Object &&
               (selection.SelectedObjectIds.Count > 0 || selection.SelectedEntityIds.Count > 0);
    }

    /// <summary>
    /// Checks whether clipboard contents can be pasted in the current interaction mode.
    /// </summary>
    public bool CanPaste(EditorScene scene, SelectionManager selection)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(selection);

        if (selection.Mode == InteractionMode.Object)
        {
            return HasObjectsOrEntities;
        }

        if (selection.Mode == InteractionMode.Face &&
            HasFaceAttributes &&
            selection.ActiveObjectId.HasValue &&
            selection.SelectedFaceIds.Count > 0 &&
            scene.GetObject(selection.ActiveObjectId.Value)?.Mesh is not null)
        {
            return true;
        }

        return HasObjectsOrEntities;
    }

    /// <summary>
    /// Copies the currently selected objects/entities (in Object mode) or face material/UV/metadata (in Face mode).
    /// </summary>
    public bool Copy(EditorScene scene, SelectionManager selection)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(selection);

        if (selection.Mode == InteractionMode.Face &&
            selection.ActiveObjectId.HasValue &&
            selection.SelectedFaceIds.Count > 0 &&
            scene.GetObject(selection.ActiveObjectId.Value)?.Mesh is { } activeMesh)
        {
            foreach (int faceId in selection.SelectedFaceIds)
            {
                if (activeMesh.GetFace(faceId) is { } face)
                {
                    _copiedFaceMaterialId = face.MaterialId;
                    _copiedFaceUvMapping = face.UvMapping.Clone();
                    _copiedFaceMetadata = face.Metadata.Clone();
                    return true;
                }
            }

            return false;
        }

        if (selection.SelectedObjectIds.Count == 0 && selection.SelectedEntityIds.Count == 0)
        {
            return false;
        }

        _copiedObjects.Clear();
        _copiedEntities.Clear();

        IReadOnlyList<int> orderedObjectIds = selection.OrderedSelectedObjectIds.Count > 0
            ? selection.OrderedSelectedObjectIds
            : selection.SelectedObjectIds.ToList();

        for (int i = 0; i < orderedObjectIds.Count; i++)
        {
            if (scene.GetObject(orderedObjectIds[i]) is { } obj)
            {
                _copiedObjects.Add(obj.Clone());
            }
        }

        foreach (string entityId in selection.SelectedEntityIds)
        {
            if (scene.GetEntity(entityId) is { } entity)
            {
                _copiedEntities.Add(entity.Clone());
            }
        }

        return HasObjectsOrEntities;
    }

    /// <summary>
    /// Copies the selected objects and entities to the clipboard and removes them from <paramref name="scene"/>
    /// as a single undoable operation.
    /// </summary>
    public bool Cut(EditorScene scene, SelectionManager selection, UndoRedoStack undoStack)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(undoStack);

        if (selection.Mode != InteractionMode.Object)
        {
            return false;
        }

        if (!Copy(scene, selection))
        {
            return false;
        }

        EditorScene before = scene.CloneSnapshot();

        var objectIdsToRemove = selection.SelectedObjectIds.ToList();
        for (int i = 0; i < objectIdsToRemove.Count; i++)
        {
            scene.RemoveObject(objectIdsToRemove[i]);
        }

        var entityIdsToRemove = selection.SelectedEntityIds.ToList();
        for (int i = 0; i < entityIdsToRemove.Count; i++)
        {
            scene.RemoveEntity(entityIdsToRemove[i]);
        }

        int totalCut = objectIdsToRemove.Count + entityIdsToRemove.Count;
        selection.ClearSelection();
        undoStack.RecordAction($"Cut {totalCut} Item(s)", before, scene);
        return true;
    }

    /// <summary>
    /// Pastes the clipboard contents into <paramref name="scene"/>, updates <paramref name="selection"/>
    /// to select the newly pasted items, and records a single undo action.
    /// </summary>
    public bool Paste(EditorScene scene, SelectionManager selection, UndoRedoStack undoStack, Vector3? positionOffset = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(undoStack);

        if (selection.Mode == InteractionMode.Face &&
            HasFaceAttributes &&
            selection.ActiveObjectId.HasValue &&
            selection.SelectedFaceIds.Count > 0 &&
            scene.GetObject(selection.ActiveObjectId.Value)?.Mesh is { } activeMesh)
        {
            EditorScene beforeFacePaste = scene.CloneSnapshot();
            int updatedFaces = 0;
            foreach (int faceId in selection.SelectedFaceIds)
            {
                if (activeMesh.GetFace(faceId) is { } face)
                {
                    face.MaterialId = _copiedFaceMaterialId!;
                    face.UvMapping = _copiedFaceUvMapping!.Clone();
                    face.Metadata = _copiedFaceMetadata?.Clone() ?? new ElementMetadata();
                    updatedFaces++;
                }
            }

            if (updatedFaces > 0)
            {
                activeMesh.InvalidateTopology();
                undoStack.RecordAction($"Paste Face Attributes ({updatedFaces} face(s))", beforeFacePaste, scene);
                return true;
            }

            return false;
        }

        if (!HasObjectsOrEntities)
        {
            return false;
        }

        EditorScene before = scene.CloneSnapshot();
        Vector3 offset = positionOffset ?? Vector3.Zero;

        if (selection.Mode != InteractionMode.Object)
        {
            selection.SetMode(InteractionMode.Object, scene);
        }

        selection.ClearSelection();

        var pastedObjectIds = new List<int>(_copiedObjects.Count);
        for (int i = 0; i < _copiedObjects.Count; i++)
        {
            SceneObject template = _copiedObjects[i];
            SceneObject pastedObj = scene.AddClonedObject(template, template.Name);
            if (offset != Vector3.Zero)
            {
                pastedObj.Transform.Position += offset;
            }
            pastedObjectIds.Add(pastedObj.Id);
        }

        var pastedEntityIds = new List<string>(_copiedEntities.Count);
        for (int i = 0; i < _copiedEntities.Count; i++)
        {
            LevelEntity template = _copiedEntities[i];
            LevelEntity pastedEntity = scene.AddClonedEntity(template);
            if (offset != Vector3.Zero)
            {
                pastedEntity.Transform.Position += offset;
            }
            pastedEntityIds.Add(pastedEntity.Id);
        }

        for (int i = 0; i < pastedObjectIds.Count; i++)
        {
            selection.ToggleSelectObject(pastedObjectIds[i]);
        }

        for (int i = 0; i < pastedEntityIds.Count; i++)
        {
            selection.ToggleSelectEntity(pastedEntityIds[i]);
        }

        int totalPasted = pastedObjectIds.Count + pastedEntityIds.Count;
        undoStack.RecordAction($"Paste {totalPasted} Item(s)", before, scene);
        return true;
    }
}
