using System.Numerics;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Selection;

namespace Leed3D.Kernel.Gizmos;

/// <summary>
/// Captures pre-drag transform or vertex position snapshots and applies drift-free
/// translation, rotation, and scaling deltas to whole objects (Object Mode) or
/// selected vertices/edges/faces (Edit Mode) around a world-space pivot.
/// </summary>
public sealed class TransformApplicator
{
    private readonly record struct ObjectSnapshot(
        int ObjectId,
        Vector3 StartPosition,
        Vector3 StartRotationEulerDegrees,
        Quaternion StartRotationQuaternion,
        Vector3 StartScale);

    private readonly record struct VertexSnapshot(
        int VertexId,
        Vector3 StartLocalPosition,
        Vector3 StartWorldPosition);

    private readonly List<ObjectSnapshot> _objectSnapshots = [];
    private readonly List<VertexSnapshot> _vertexSnapshots = [];

    private InteractionMode _mode = InteractionMode.Object;
    private int? _activeObjectId;
    private Matrix4x4 _activeWorldToLocal = Matrix4x4.Identity;

    public bool IsSessionActive { get; private set; }
    public Vector3 PivotWorld { get; private set; }
    public int AffectedElementCount => _mode == InteractionMode.Object ? _objectSnapshots.Count : _vertexSnapshots.Count;

    /// <summary>
    /// Collects the union of all <see cref="MeshVertex.Id"/> values belonging to the currently selected
    /// vertices, edges, or faces in <paramref name="selection"/>.
    /// </summary>
    public static HashSet<int> CollectSelectedVertexIds(EditableMesh mesh, SelectionManager selection)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(selection);

        var vertexIds = new HashSet<int>();

        switch (selection.Mode)
        {
            case InteractionMode.Vertex:
                foreach (int vId in selection.SelectedVertexIds)
                {
                    if (mesh.GetVertex(vId) is not null)
                    {
                        vertexIds.Add(vId);
                    }
                }
                break;

            case InteractionMode.Edge:
                foreach (EdgeKey edge in selection.SelectedEdges)
                {
                    if (mesh.GetVertex(edge.VMin) is not null)
                    {
                        vertexIds.Add(edge.VMin);
                    }
                    if (mesh.GetVertex(edge.VMax) is not null)
                    {
                        vertexIds.Add(edge.VMax);
                    }
                }
                break;

            case InteractionMode.Face:
                foreach (int faceId in selection.SelectedFaceIds)
                {
                    PolygonFace? face = mesh.GetFace(faceId);
                    if (face is not null)
                    {
                        for (int i = 0; i < face.VertexIds.Count; i++)
                        {
                            int vId = face.VertexIds[i];
                            if (mesh.GetVertex(vId) is not null)
                            {
                                vertexIds.Add(vId);
                            }
                        }
                    }
                }
                break;
        }

        return vertexIds;
    }

    /// <summary>
    /// Begins a transform session, recording pre-drag snapshots and opening an undo transaction on <paramref name="undoStack"/>.
    /// </summary>
    public void BeginSession(
        EditorScene scene,
        SelectionManager selection,
        Vector3 gizmoPivotWorld,
        UndoRedoStack? undoStack = null,
        string description = "Transform Selection")
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(selection);

        _objectSnapshots.Clear();
        _vertexSnapshots.Clear();

        _mode = selection.Mode;
        PivotWorld = gizmoPivotWorld;
        _activeObjectId = selection.ActiveObjectId;

        undoStack?.BeginTransaction(description, scene);

        if (_mode == InteractionMode.Object)
        {
            foreach (int objId in selection.SelectedObjectIds)
            {
                SceneObject? obj = scene.GetObject(objId);
                if (obj is not null && !obj.Locked)
                {
                    _objectSnapshots.Add(new ObjectSnapshot(
                        obj.Id,
                        obj.Transform.Position,
                        obj.Transform.RotationEulerDegrees,
                        obj.Transform.GetRotationQuaternion(),
                        obj.Transform.Scale));
                }
            }
        }
        else if (_activeObjectId.HasValue)
        {
            SceneObject? activeObj = scene.GetObject(_activeObjectId.Value);
            if (activeObj?.Mesh is { } mesh && !activeObj.Locked)
            {
                Matrix4x4 localToWorld = activeObj.Transform.GetLocalMatrix();
                _activeWorldToLocal = activeObj.Transform.GetInverseLocalMatrix();

                HashSet<int> affectedVerts = CollectSelectedVertexIds(mesh, selection);
                foreach (int vId in affectedVerts)
                {
                    MeshVertex? vert = mesh.GetVertex(vId);
                    if (vert is not null)
                    {
                        Vector3 worldPos = Vector3.Transform(vert.Position, localToWorld);
                        _vertexSnapshots.Add(new VertexSnapshot(vId, vert.Position, worldPos));
                    }
                }
            }
        }

        IsSessionActive = true;
    }

    /// <summary>
    /// Applies the current delta from <paramref name="gizmo"/> onto the pre-drag snapshot in <paramref name="scene"/>.
    /// </summary>
    public void ApplyGizmo(EditorScene scene, TransformGizmo3D gizmo)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(gizmo);

        if (!IsSessionActive)
        {
            return;
        }

        switch (gizmo.ActiveDragMode)
        {
            case GizmoToolMode.Translate:
                ApplyTranslation(scene, gizmo.CurrentTranslationDelta);
                break;

            case GizmoToolMode.Rotate:
                ApplyRotation(scene, gizmo.CurrentRotationQuaternion, gizmo.CurrentRotationDeltaDegrees);
                break;

            case GizmoToolMode.Scale:
                ApplyScale(scene, gizmo.CurrentScaleMultiplier);
                break;
        }
    }

    /// <summary>
    /// Restores pre-drag positions and applies <paramref name="worldDelta"/> translation.
    /// </summary>
    public void ApplyTranslation(EditorScene scene, Vector3 worldDelta)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!IsSessionActive)
        {
            return;
        }

        if (_mode == InteractionMode.Object)
        {
            for (int i = 0; i < _objectSnapshots.Count; i++)
            {
                ObjectSnapshot snap = _objectSnapshots[i];
                SceneObject? obj = scene.GetObject(snap.ObjectId);
                if (obj is not null)
                {
                    obj.Transform.Position = snap.StartPosition + worldDelta;
                }
            }
        }
        else if (_activeObjectId.HasValue)
        {
            SceneObject? activeObj = scene.GetObject(_activeObjectId.Value);
            if (activeObj?.Mesh is { } mesh)
            {
                for (int i = 0; i < _vertexSnapshots.Count; i++)
                {
                    VertexSnapshot snap = _vertexSnapshots[i];
                    Vector3 newWorld = snap.StartWorldPosition + worldDelta;
                    Vector3 newLocal = Vector3.Transform(newWorld, _activeWorldToLocal);
                    mesh.SetVertexPosition(snap.VertexId, newLocal);
                }
            }
        }
    }

    /// <summary>
    /// Restores pre-drag state and applies rotation <paramref name="worldRotationDelta"/> around <see cref="PivotWorld"/>.
    /// </summary>
    public void ApplyRotation(EditorScene scene, Quaternion worldRotationDelta, Vector3 eulerDeltaDegrees)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!IsSessionActive)
        {
            return;
        }

        if (_mode == InteractionMode.Object)
        {
            for (int i = 0; i < _objectSnapshots.Count; i++)
            {
                ObjectSnapshot snap = _objectSnapshots[i];
                SceneObject? obj = scene.GetObject(snap.ObjectId);
                if (obj is not null)
                {
                    Vector3 offset = snap.StartPosition - PivotWorld;
                    obj.Transform.Position = PivotWorld + Vector3.Transform(offset, worldRotationDelta);
                    obj.Transform.RotationEulerDegrees = snap.StartRotationEulerDegrees + eulerDeltaDegrees;
                }
            }
        }
        else if (_activeObjectId.HasValue)
        {
            SceneObject? activeObj = scene.GetObject(_activeObjectId.Value);
            if (activeObj?.Mesh is { } mesh)
            {
                for (int i = 0; i < _vertexSnapshots.Count; i++)
                {
                    VertexSnapshot snap = _vertexSnapshots[i];
                    Vector3 offset = snap.StartWorldPosition - PivotWorld;
                    Vector3 newWorld = PivotWorld + Vector3.Transform(offset, worldRotationDelta);
                    Vector3 newLocal = Vector3.Transform(newWorld, _activeWorldToLocal);
                    mesh.SetVertexPosition(snap.VertexId, newLocal);
                }
            }
        }
    }

    /// <summary>
    /// Restores pre-drag state and applies scaling <paramref name="scaleMultiplier"/> around <see cref="PivotWorld"/>.
    /// </summary>
    public void ApplyScale(EditorScene scene, Vector3 scaleMultiplier)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!IsSessionActive)
        {
            return;
        }

        if (_mode == InteractionMode.Object)
        {
            for (int i = 0; i < _objectSnapshots.Count; i++)
            {
                ObjectSnapshot snap = _objectSnapshots[i];
                SceneObject? obj = scene.GetObject(snap.ObjectId);
                if (obj is not null)
                {
                    Vector3 offset = snap.StartPosition - PivotWorld;
                    obj.Transform.Position = PivotWorld + (offset * scaleMultiplier);
                    obj.Transform.Scale = new Vector3(
                        System.Math.Max(0.01f, snap.StartScale.X * scaleMultiplier.X),
                        System.Math.Max(0.01f, snap.StartScale.Y * scaleMultiplier.Y),
                        System.Math.Max(0.01f, snap.StartScale.Z * scaleMultiplier.Z));
                }
            }
        }
        else if (_activeObjectId.HasValue)
        {
            SceneObject? activeObj = scene.GetObject(_activeObjectId.Value);
            if (activeObj?.Mesh is { } mesh)
            {
                for (int i = 0; i < _vertexSnapshots.Count; i++)
                {
                    VertexSnapshot snap = _vertexSnapshots[i];
                    Vector3 offset = snap.StartWorldPosition - PivotWorld;
                    Vector3 newWorld = PivotWorld + (offset * scaleMultiplier);
                    Vector3 newLocal = Vector3.Transform(newWorld, _activeWorldToLocal);
                    mesh.SetVertexPosition(snap.VertexId, newLocal);
                }
            }
        }
    }

    /// <summary>
    /// Restores all affected objects or vertices to their exact pre-drag positions.
    /// </summary>
    public void RestoreInitialState(EditorScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!IsSessionActive)
        {
            return;
        }

        if (_mode == InteractionMode.Object)
        {
            for (int i = 0; i < _objectSnapshots.Count; i++)
            {
                ObjectSnapshot snap = _objectSnapshots[i];
                SceneObject? obj = scene.GetObject(snap.ObjectId);
                if (obj is not null)
                {
                    obj.Transform.Position = snap.StartPosition;
                    obj.Transform.RotationEulerDegrees = snap.StartRotationEulerDegrees;
                    obj.Transform.Scale = snap.StartScale;
                }
            }
        }
        else if (_activeObjectId.HasValue)
        {
            SceneObject? activeObj = scene.GetObject(_activeObjectId.Value);
            if (activeObj?.Mesh is { } mesh)
            {
                for (int i = 0; i < _vertexSnapshots.Count; i++)
                {
                    VertexSnapshot snap = _vertexSnapshots[i];
                    mesh.SetVertexPosition(snap.VertexId, snap.StartLocalPosition);
                }
            }
        }
    }

    /// <summary>
    /// Commits the transform session to <paramref name="undoStack"/> and ends the session.
    /// </summary>
    public void CommitSession(EditorScene scene, UndoRedoStack? undoStack = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!IsSessionActive)
        {
            return;
        }

        undoStack?.CommitTransaction(scene);
        EndSessionInternal();
    }

    /// <summary>
    /// Cancels the transform session, restoring pre-drag state and rolling back any in-progress undo transaction.
    /// </summary>
    public void CancelSession(EditorScene scene, UndoRedoStack? undoStack = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!IsSessionActive)
        {
            return;
        }

        RestoreInitialState(scene);
        undoStack?.CancelTransaction(scene);
        EndSessionInternal();
    }

    private void EndSessionInternal()
    {
        IsSessionActive = false;
        _objectSnapshots.Clear();
        _vertexSnapshots.Clear();
    }
}
