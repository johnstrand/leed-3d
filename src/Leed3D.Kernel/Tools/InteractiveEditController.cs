using System.Globalization;
using System.Numerics;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Gizmos;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Modifiers;
using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Selection;

namespace Leed3D.Kernel.Tools;

/// <summary>
/// Active modal interactive tool mode.
/// </summary>
public enum ActiveModalEditTool
{
    None,
    Extrude,
    Inset
}

/// <summary>
/// Headless controller managing interactive edit mode operators (Extrude, Inset) with live distance/thickness drag,
/// keyboard numeric entry, and clean Undo/Redo transaction snapshots, as well as execution of sub-object operations
/// (Weld, Connect, Fill, Subdivide, Delete, Dissolve, Normals, Clone, Mirror, Linear/Radial Array).
/// </summary>
public sealed class InteractiveEditController
{
    private const float Epsilon = 1e-6f;

    public ActiveModalEditTool ActiveTool { get; private set; } = ActiveModalEditTool.None;
    public bool IsExtrudeActive => ActiveTool == ActiveModalEditTool.Extrude;
    public bool IsInsetActive => ActiveTool == ActiveModalEditTool.Inset;
    public bool IsActive => ActiveTool != ActiveModalEditTool.None;

    // Snapshot before interactive operation begins
    private EditorScene? _beforeSnapshot;
    private int _activeObjectId;

    // Extrude state
    public InteractionMode ExtrudeTarget { get; private set; } = InteractionMode.Face;
    public float ExtrudeDistance { get; private set; }
    public Vector3 ExtrudeNormal { get; private set; } = Vector3.UnitY;
    public Vector3 ExtrudePivot { get; private set; }
    public Ray3D ExtrudeInitialRay { get; private set; }
    private List<int> _extrudeFaceIds = [];
    private List<EdgeKey> _extrudeEdges = [];
    private Vector3 _extrudeLocalOffsetDir = Vector3.UnitY;

    // Inset state
    public float InsetThickness { get; private set; } = 0.1f;
    public float InsetDepth { get; private set; } = 0.0f;
    public bool InsetIndividual { get; private set; }
    private List<int> _insetFaceIds = [];
    private Vector2 _insetStartScreenPos;

    // Modifier live preview state
    public bool IsModifierPreviewActive { get; private set; }
    public string ModifierPreviewName { get; private set; } = string.Empty;
    private EditorScene? _modifierPreviewSnapshot;

    // Numeric input buffer
    public string NumericInputBuffer { get; private set; } = string.Empty;
    public bool HasNumericInput => !string.IsNullOrEmpty(NumericInputBuffer);

    public float? ParsedNumericValue
    {
        get
        {
            if (string.IsNullOrWhiteSpace(NumericInputBuffer))
            {
                return null;
            }

            if (float.TryParse(NumericInputBuffer, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
            {
                return val;
            }

            return null;
        }
    }

    #region Extrude Workflow

    /// <summary>
    /// Starts an interactive Extrude operation for selected faces or edges.
    /// </summary>
    public bool StartExtrude(
        EditorScene scene,
        SelectionManager selection,
        Ray3D initialRay,
        Vector3 cameraForward,
        UndoRedoStack? undoStack = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(selection);

        if (!selection.ActiveObjectId.HasValue)
        {
            return false;
        }

        SceneObject? activeObj = scene.GetObject(selection.ActiveObjectId.Value);
        if (activeObj?.Mesh is null)
        {
            return false;
        }

        if (selection.Mode == InteractionMode.Face)
        {
            if (selection.SelectedFaceIds.Count == 0)
            {
                return false;
            }

            _activeObjectId = activeObj.Id;
            ExtrudeTarget = InteractionMode.Face;
            _extrudeFaceIds = [.. selection.SelectedFaceIds];

            // Compute centroid and average face normal
            Vector3 centroid = Vector3.Zero;
            Vector3 avgNormal = Vector3.Zero;
            int vertCount = 0;

            foreach (int fid in _extrudeFaceIds)
            {
                PolygonFace? face = activeObj.Mesh.GetFace(fid);
                if (face is null) continue;
                avgNormal += face.ComputeNormal(activeObj.Mesh);
                foreach (int vid in face.VertexIds)
                {
                    MeshVertex? v = activeObj.Mesh.GetVertex(vid);
                    if (v is not null)
                    {
                        centroid += v.Position;
                        vertCount++;
                    }
                }
            }

            if (vertCount > 0) centroid /= vertCount;
            if (avgNormal.LengthSquared() > Epsilon) avgNormal = Vector3.Normalize(avgNormal);
            else avgNormal = Vector3.UnitY;

            Matrix4x4 localToWorld = activeObj.Transform.GetLocalMatrix();
            ExtrudePivot = Vector3.Transform(centroid, localToWorld);
            Vector3 worldNormal = Vector3.TransformNormal(avgNormal, localToWorld);
            ExtrudeNormal = worldNormal.LengthSquared() > Epsilon ? Vector3.Normalize(worldNormal) : Vector3.UnitY;
            _extrudeLocalOffsetDir = avgNormal;

            ExtrudeInitialRay = initialRay;
            ExtrudeDistance = 0.0f;
            ClearNumericInput();

            _beforeSnapshot = scene.CloneSnapshot();
            undoStack?.BeginTransaction("Extrude Faces", scene);

            ActiveTool = ActiveModalEditTool.Extrude;
            SetExtrudeDistance(scene, 0.0f);
            return true;
        }
        else if (selection.Mode == InteractionMode.Edge)
        {
            if (selection.SelectedEdges.Count == 0)
            {
                return false;
            }

            _activeObjectId = activeObj.Id;
            ExtrudeTarget = InteractionMode.Edge;
            _extrudeEdges = [.. selection.SelectedEdges];

            // Centroid and direction
            Vector3 centroid = Vector3.Zero;
            int vertCount = 0;
            foreach (EdgeKey edge in _extrudeEdges)
            {
                MeshVertex? v1 = activeObj.Mesh.GetVertex(edge.VMin);
                MeshVertex? v2 = activeObj.Mesh.GetVertex(edge.VMax);
                if (v1 is not null) { centroid += v1.Position; vertCount++; }
                if (v2 is not null) { centroid += v2.Position; vertCount++; }
            }
            if (vertCount > 0) centroid /= vertCount;

            Matrix4x4 localToWorld = activeObj.Transform.GetLocalMatrix();
            ExtrudePivot = Vector3.Transform(centroid, localToWorld);

            // Perpendicular direction to edge in view
            Vector3 worldDir = cameraForward.LengthSquared() > Epsilon ? -Vector3.Normalize(cameraForward) : Vector3.UnitY;
            ExtrudeNormal = worldDir;
            _extrudeLocalOffsetDir = Vector3.UnitY;

            ExtrudeInitialRay = initialRay;
            ExtrudeDistance = 0.0f;
            ClearNumericInput();

            _beforeSnapshot = scene.CloneSnapshot();
            undoStack?.BeginTransaction("Extrude Edges", scene);

            ActiveTool = ActiveModalEditTool.Extrude;
            SetExtrudeDistance(scene, 0.0f);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Updates extrude distance from 3D ray intersection along the extrusion normal.
    /// </summary>
    public void UpdateExtrudeRay(EditorScene scene, Ray3D currentRay, bool snapGrid, float gridStep)
    {
        if (!IsExtrudeActive || HasNumericInput)
        {
            return;
        }

        Plane3D dragPlane = TransformGizmo3D.BuildAxisDragPlane(ExtrudePivot, ExtrudeNormal, ExtrudeInitialRay.Direction);
        if (!ExtrudeInitialRay.IntersectPlane(dragPlane, out _, out Vector3 startHit) ||
            !currentRay.IntersectPlane(dragPlane, out _, out Vector3 currHit))
        {
            return;
        }

        float startProj = Vector3.Dot(startHit - ExtrudePivot, ExtrudeNormal);
        float currProj = Vector3.Dot(currHit - ExtrudePivot, ExtrudeNormal);
        float deltaDist = currProj - startProj;

        if (snapGrid)
        {
            deltaDist = GridSnapper.SnapScalar(deltaDist, gridStep);
        }

        SetExtrudeDistance(scene, deltaDist);
    }

    /// <summary>
    /// Sets the extrude distance, updating the mesh preview from the clean before snapshot.
    /// </summary>
    public void SetExtrudeDistance(EditorScene scene, float distance)
    {
        if (!IsExtrudeActive || _beforeSnapshot is null)
        {
            return;
        }

        ExtrudeDistance = distance;
        scene.RestoreFromSnapshot(_beforeSnapshot);

        SceneObject? obj = scene.GetObject(_activeObjectId);
        if (obj?.Mesh is null)
        {
            return;
        }

        if (ExtrudeTarget == InteractionMode.Face)
        {
            MeshTopologyOps.ExtrudeFaces(obj.Mesh, _extrudeFaceIds, distance);
        }
        else if (ExtrudeTarget == InteractionMode.Edge)
        {
            Vector3 offset = _extrudeLocalOffsetDir * distance;
            MeshTopologyOps.ExtrudeEdges(obj.Mesh, _extrudeEdges, offset);
        }
    }

    /// <summary>
    /// Confirms the active Extrude operation and commits the transaction.
    /// </summary>
    public void ConfirmExtrude(EditorScene scene, UndoRedoStack? undoStack = null)
    {
        if (!IsExtrudeActive)
        {
            return;
        }

        undoStack?.CommitTransaction(scene);
        ResetTool();
    }

    /// <summary>
    /// Cancels the active Extrude operation and restores the before snapshot.
    /// </summary>
    public void CancelExtrude(EditorScene scene, UndoRedoStack? undoStack = null)
    {
        if (!IsExtrudeActive)
        {
            return;
        }

        if (undoStack is not null)
        {
            undoStack.CancelTransaction(scene);
        }
        else if (_beforeSnapshot is not null)
        {
            scene.RestoreFromSnapshot(_beforeSnapshot);
        }

        ResetTool();
    }

    #endregion

    #region Inset Workflow

    /// <summary>
    /// Starts an interactive Inset operation on selected faces.
    /// </summary>
    public bool StartInset(
        EditorScene scene,
        SelectionManager selection,
        Vector2 mouseScreenPos,
        UndoRedoStack? undoStack = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(selection);

        if (!selection.ActiveObjectId.HasValue || selection.Mode != InteractionMode.Face || selection.SelectedFaceIds.Count == 0)
        {
            return false;
        }

        SceneObject? activeObj = scene.GetObject(selection.ActiveObjectId.Value);
        if (activeObj?.Mesh is null)
        {
            return false;
        }

        _activeObjectId = activeObj.Id;
        _insetFaceIds = [.. selection.SelectedFaceIds];
        _insetStartScreenPos = mouseScreenPos;

        InsetThickness = 0.1f;
        InsetDepth = 0.0f;
        InsetIndividual = false;
        ClearNumericInput();

        _beforeSnapshot = scene.CloneSnapshot();
        undoStack?.BeginTransaction("Inset Faces", scene);

        ActiveTool = ActiveModalEditTool.Inset;
        SetInsetParameters(scene, InsetThickness, InsetDepth, InsetIndividual);
        return true;
    }

    /// <summary>
    /// Updates inset thickness from mouse screen drag distance.
    /// </summary>
    public void UpdateInsetMouse(EditorScene scene, Vector2 currentMouseScreenPos, bool snapGrid, float gridStep)
    {
        if (!IsInsetActive || HasNumericInput)
        {
            return;
        }

        float pixelDelta = currentMouseScreenPos.X - _insetStartScreenPos.X;
        float newThickness = MathF.Max(0.0f, 0.1f + pixelDelta * 0.01f);

        if (snapGrid)
        {
            newThickness = GridSnapper.SnapScalar(newThickness, gridStep * 0.1f);
            if (newThickness < 0.0f) newThickness = 0.0f;
        }

        SetInsetParameters(scene, newThickness, InsetDepth, InsetIndividual);
    }

    /// <summary>
    /// Sets inset parameters and regenerates the inset mesh from the clean before snapshot.
    /// </summary>
    public void SetInsetParameters(EditorScene scene, float thickness, float depth, bool individual)
    {
        if (!IsInsetActive || _beforeSnapshot is null)
        {
            return;
        }

        InsetThickness = MathF.Max(0.0f, thickness);
        InsetDepth = depth;
        InsetIndividual = individual;

        scene.RestoreFromSnapshot(_beforeSnapshot);
        SceneObject? obj = scene.GetObject(_activeObjectId);
        if (obj?.Mesh is null)
        {
            return;
        }

        MeshTopologyOps.InsetFaces(obj.Mesh, _insetFaceIds, InsetThickness, InsetDepth, InsetIndividual);
    }

    /// <summary>
    /// Confirms the active Inset operation and commits the transaction.
    /// </summary>
    public void ConfirmInset(EditorScene scene, UndoRedoStack? undoStack = null)
    {
        if (!IsInsetActive)
        {
            return;
        }

        undoStack?.CommitTransaction(scene);
        ResetTool();
    }

    /// <summary>
    /// Cancels the active Inset operation and restores the before snapshot.
    /// </summary>
    public void CancelInset(EditorScene scene, UndoRedoStack? undoStack = null)
    {
        if (!IsInsetActive)
        {
            return;
        }

        if (undoStack is not null)
        {
            undoStack.CancelTransaction(scene);
        }
        else if (_beforeSnapshot is not null)
        {
            scene.RestoreFromSnapshot(_beforeSnapshot);
        }

        ResetTool();
    }

    #endregion

    #region Modifier Live Preview Sessions

    /// <summary>
    /// Begins a live preview session for modifiers (Mirror, Linear Array, Radial Array).
    /// </summary>
    public void BeginModifierPreview(EditorScene scene, string name)
    {
        if (IsModifierPreviewActive)
        {
            CancelModifierPreview(scene);
        }

        _modifierPreviewSnapshot = scene.CloneSnapshot();
        ModifierPreviewName = name;
        IsModifierPreviewActive = true;
    }

    /// <summary>
    /// Confirms the modifier preview, committing it to undo stack.
    /// </summary>
    public void ConfirmModifierPreview(EditorScene scene, UndoRedoStack undoStack)
    {
        if (!IsModifierPreviewActive || _modifierPreviewSnapshot is null)
        {
            return;
        }

        undoStack.RecordAction(ModifierPreviewName, _modifierPreviewSnapshot, scene);
        IsModifierPreviewActive = false;
        _modifierPreviewSnapshot = null;
        ModifierPreviewName = string.Empty;
    }

    /// <summary>
    /// Cancels modifier preview and restores the scene to its before state.
    /// </summary>
    public void CancelModifierPreview(EditorScene scene)
    {
        if (!IsModifierPreviewActive || _modifierPreviewSnapshot is null)
        {
            return;
        }

        scene.RestoreFromSnapshot(_modifierPreviewSnapshot);
        IsModifierPreviewActive = false;
        _modifierPreviewSnapshot = null;
        ModifierPreviewName = string.Empty;
    }

    #endregion

    #region Direct Sub-Object & Modifier Operations

    /// <summary>
    /// Welds selected vertices to their centroid.
    /// </summary>
    public bool ExecuteWeldCenter(EditorScene scene, SelectionManager selection, UndoRedoStack undoStack)
    {
        if (!selection.ActiveObjectId.HasValue || selection.SelectedVertexIds.Count < 2)
        {
            return false;
        }

        SceneObject? obj = scene.GetObject(selection.ActiveObjectId.Value);
        if (obj?.Mesh is null)
        {
            return false;
        }

        EditorScene before = scene.CloneSnapshot();
        int weldedId = MeshTopologyOps.WeldVerticesToCenter(obj.Mesh, selection.SelectedVertexIds);
        selection.ClearSelection();
        selection.SelectSingleVertex(weldedId);

        undoStack.RecordAction("Weld Vertices to Center", before, scene);
        return true;
    }

    /// <summary>
    /// Welds selected vertices within the specified distance threshold.
    /// </summary>
    public bool ExecuteWeldDistance(EditorScene scene, SelectionManager selection, float distanceThreshold, UndoRedoStack undoStack)
    {
        if (!selection.ActiveObjectId.HasValue || selection.SelectedVertexIds.Count < 2)
        {
            return false;
        }

        SceneObject? obj = scene.GetObject(selection.ActiveObjectId.Value);
        if (obj?.Mesh is null)
        {
            return false;
        }

        EditorScene before = scene.CloneSnapshot();
        int mergedCount = MeshTopologyOps.WeldVerticesByDistance(obj.Mesh, selection.SelectedVertexIds, distanceThreshold);
        if (mergedCount > 0)
        {
            undoStack.RecordAction($"Weld Vertices ({mergedCount} merged)", before, scene);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Connects two selected vertices by splitting the shared face.
    /// </summary>
    public bool ExecuteConnectVertices(EditorScene scene, SelectionManager selection, UndoRedoStack undoStack)
    {
        if (!selection.ActiveObjectId.HasValue || selection.SelectedVertexIds.Count != 2)
        {
            return false;
        }

        SceneObject? obj = scene.GetObject(selection.ActiveObjectId.Value);
        if (obj?.Mesh is null)
        {
            return false;
        }

        int[] vList = [.. selection.SelectedVertexIds];
        EditorScene before = scene.CloneSnapshot();
        bool success = MeshTopologyOps.ConnectVertices(obj.Mesh, vList[0], vList[1]);
        if (success)
        {
            undoStack.RecordAction("Connect Vertices", before, scene);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Fills / creates a face from selected vertices or edges.
    /// </summary>
    public bool ExecuteFillFace(EditorScene scene, SelectionManager selection, UndoRedoStack undoStack)
    {
        if (!selection.ActiveObjectId.HasValue)
        {
            return false;
        }

        SceneObject? obj = scene.GetObject(selection.ActiveObjectId.Value);
        if (obj?.Mesh is null)
        {
            return false;
        }

        EditorScene before = scene.CloneSnapshot();
        int? createdFaceId = null;

        if (selection.Mode == InteractionMode.Vertex && selection.SelectedVertexIds.Count >= 3)
        {
            createdFaceId = MeshTopologyOps.FillFaceFromVertices(obj.Mesh, selection.SelectedVertexIds);
        }
        else if (selection.Mode == InteractionMode.Edge && selection.SelectedEdges.Count >= 3)
        {
            createdFaceId = MeshTopologyOps.FillFaceFromEdges(obj.Mesh, selection.SelectedEdges);
        }

        if (createdFaceId.HasValue)
        {
            selection.SetMode(InteractionMode.Face, scene);
            selection.SelectSingleFace(createdFaceId.Value);
            undoStack.RecordAction("Fill Face", before, scene);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Subdivides selected edges with the given cut count.
    /// </summary>
    public bool ExecuteSubdivideEdges(EditorScene scene, SelectionManager selection, int cuts, UndoRedoStack undoStack)
    {
        if (!selection.ActiveObjectId.HasValue || selection.SelectedEdges.Count == 0)
        {
            return false;
        }

        SceneObject? obj = scene.GetObject(selection.ActiveObjectId.Value);
        if (obj?.Mesh is null)
        {
            return false;
        }

        EditorScene before = scene.CloneSnapshot();
        var newVerts = MeshTopologyOps.SubdivideEdges(obj.Mesh, selection.SelectedEdges, cuts);
        if (newVerts.Count > 0)
        {
            undoStack.RecordAction($"Subdivide Edges ({cuts} cuts)", before, scene);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Deletes selected faces.
    /// </summary>
    public bool ExecuteDeleteFaces(EditorScene scene, SelectionManager selection, UndoRedoStack undoStack)
    {
        if (!selection.ActiveObjectId.HasValue || selection.SelectedFaceIds.Count == 0)
        {
            return false;
        }

        SceneObject? obj = scene.GetObject(selection.ActiveObjectId.Value);
        if (obj?.Mesh is null)
        {
            return false;
        }

        EditorScene before = scene.CloneSnapshot();
        MeshTopologyOps.DeleteFaces(obj.Mesh, selection.SelectedFaceIds);
        selection.ClearSelection();
        undoStack.RecordAction("Delete Faces", before, scene);
        return true;
    }

    /// <summary>
    /// Dissolves selected faces into a unified polygon.
    /// </summary>
    public bool ExecuteDissolveFaces(EditorScene scene, SelectionManager selection, UndoRedoStack undoStack)
    {
        if (!selection.ActiveObjectId.HasValue || selection.SelectedFaceIds.Count < 2)
        {
            return false;
        }

        SceneObject? obj = scene.GetObject(selection.ActiveObjectId.Value);
        if (obj?.Mesh is null)
        {
            return false;
        }

        EditorScene before = scene.CloneSnapshot();
        int? newFaceId = MeshTopologyOps.DissolveFaces(obj.Mesh, selection.SelectedFaceIds);
        if (newFaceId.HasValue)
        {
            selection.SelectSingleFace(newFaceId.Value);
            undoStack.RecordAction("Dissolve Faces", before, scene);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Deletes selected objects from the scene.
    /// </summary>
    public bool ExecuteDeleteObjects(EditorScene scene, SelectionManager selection, UndoRedoStack undoStack)
    {
        if (selection.SelectedObjectIds.Count == 0)
        {
            return false;
        }

        EditorScene before = scene.CloneSnapshot();
        var idsToDelete = new List<int>(selection.SelectedObjectIds);
        foreach (int id in idsToDelete)
        {
            scene.RemoveObject(id);
        }

        selection.ClearSelection();
        undoStack.RecordAction($"Delete {idsToDelete.Count} Object(s)", before, scene);
        return true;
    }

    /// <summary>
    /// Flips normals of selected faces.
    /// </summary>
    public bool ExecuteFlipNormals(EditorScene scene, SelectionManager selection, UndoRedoStack undoStack)
    {
        if (!selection.ActiveObjectId.HasValue || selection.SelectedFaceIds.Count == 0)
        {
            return false;
        }

        SceneObject? obj = scene.GetObject(selection.ActiveObjectId.Value);
        if (obj?.Mesh is null)
        {
            return false;
        }

        EditorScene before = scene.CloneSnapshot();
        MeshTopologyOps.FlipSelectedFaces(obj.Mesh, selection.SelectedFaceIds);
        undoStack.RecordAction("Flip Selected Normals", before, scene);
        return true;
    }

    /// <summary>
    /// Recalculates normals of the active mesh to point outward (or inward for hollow room).
    /// </summary>
    public bool ExecuteRecalculateNormals(EditorScene scene, SelectionManager selection, bool insideOut, UndoRedoStack undoStack)
    {
        if (!selection.ActiveObjectId.HasValue)
        {
            return false;
        }

        SceneObject? obj = scene.GetObject(selection.ActiveObjectId.Value);
        if (obj?.Mesh is null)
        {
            return false;
        }

        EditorScene before = scene.CloneSnapshot();
        MeshTopologyOps.RecalculateNormals(obj.Mesh, insideOut);
        undoStack.RecordAction(insideOut ? "Recalculate Normals Inside (Hollow)" : "Recalculate Normals Outside", before, scene);
        return true;
    }

    /// <summary>
    /// Quick Clones selected objects (Object mode) or faces (Face mode).
    /// Returns true if clones were created.
    /// </summary>
    public bool ExecuteQuickClone(EditorScene scene, SelectionManager selection, UndoRedoStack undoStack)
    {
        if (selection.Mode == InteractionMode.Object && selection.SelectedObjectIds.Count > 0)
        {
            EditorScene before = scene.CloneSnapshot();
            var clones = MeshModifiers.CloneObjects(scene, selection.SelectedObjectIds, selection);
            if (clones.Count > 0)
            {
                undoStack.RecordAction($"Clone {clones.Count} Object(s)", before, scene);
                return true;
            }
        }
        else if (selection.Mode == InteractionMode.Face && selection.ActiveObjectId.HasValue && selection.SelectedFaceIds.Count > 0)
        {
            SceneObject? obj = scene.GetObject(selection.ActiveObjectId.Value);
            if (obj?.Mesh is not null)
            {
                EditorScene before = scene.CloneSnapshot();
                var newFaceIds = MeshModifiers.CloneFaces(obj.Mesh, selection.SelectedFaceIds, selection);
                if (newFaceIds.Count > 0)
                {
                    undoStack.RecordAction($"Clone {newFaceIds.Count} Face(s)", before, scene);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Applies Mirror modifier to selected objects or mesh.
    /// </summary>
    public bool ExecuteMirror(
        EditorScene scene,
        SelectionManager selection,
        Axis3D axis,
        float planeCoord,
        bool keepOriginal,
        float weldThreshold,
        UndoRedoStack undoStack)
    {
        EditorScene before = _modifierPreviewSnapshot ?? scene.CloneSnapshot();
        scene.RestoreFromSnapshot(before);

        if (selection.Mode == InteractionMode.Object)
        {
            if (selection.SelectedObjectIds.Count == 0) return false;
            MeshModifiers.MirrorObjects(scene, selection.SelectedObjectIds, axis, planeCoord, keepOriginal, weldThreshold);
        }
        else if (selection.ActiveObjectId.HasValue)
        {
            SceneObject? obj = scene.GetObject(selection.ActiveObjectId.Value);
            if (obj?.Mesh is null) return false;
            MeshModifiers.MirrorMesh(obj.Mesh, axis, planeCoord, keepOriginal, weldThreshold);
        }

        undoStack.RecordAction($"Mirror ({axis})", before, scene);
        IsModifierPreviewActive = false;
        _modifierPreviewSnapshot = null;
        return true;
    }

    /// <summary>
    /// Applies Linear Array modifier to selected objects or mesh.
    /// </summary>
    public bool ExecuteLinearArray(
        EditorScene scene,
        SelectionManager selection,
        int count,
        Vector3 stepOffset,
        UndoRedoStack undoStack)
    {
        EditorScene before = _modifierPreviewSnapshot ?? scene.CloneSnapshot();
        scene.RestoreFromSnapshot(before);

        if (selection.Mode == InteractionMode.Object)
        {
            if (selection.SelectedObjectIds.Count == 0) return false;
            MeshModifiers.LinearArrayObjects(scene, selection.SelectedObjectIds, count, stepOffset);
        }
        else if (selection.ActiveObjectId.HasValue)
        {
            SceneObject? obj = scene.GetObject(selection.ActiveObjectId.Value);
            if (obj?.Mesh is null) return false;
            MeshModifiers.LinearArrayMesh(obj.Mesh, count, stepOffset, selection.SelectedFaceIds.Count > 0 ? selection.SelectedFaceIds : null);
        }

        undoStack.RecordAction($"Linear Array ({count}x)", before, scene);
        IsModifierPreviewActive = false;
        _modifierPreviewSnapshot = null;
        return true;
    }

    /// <summary>
    /// Applies Radial Array modifier to selected objects or mesh.
    /// </summary>
    public bool ExecuteRadialArray(
        EditorScene scene,
        SelectionManager selection,
        int count,
        Axis3D axis,
        Vector3 pivot,
        float totalAngleDegrees,
        UndoRedoStack undoStack)
    {
        EditorScene before = _modifierPreviewSnapshot ?? scene.CloneSnapshot();
        scene.RestoreFromSnapshot(before);

        if (selection.Mode == InteractionMode.Object)
        {
            if (selection.SelectedObjectIds.Count == 0) return false;
            MeshModifiers.RadialArrayObjects(scene, selection.SelectedObjectIds, count, pivot, axis, totalAngleDegrees);
        }
        else if (selection.ActiveObjectId.HasValue)
        {
            SceneObject? obj = scene.GetObject(selection.ActiveObjectId.Value);
            if (obj?.Mesh is null) return false;
            MeshModifiers.RadialArrayMesh(obj.Mesh, count, pivot, axis, totalAngleDegrees, selection.SelectedFaceIds.Count > 0 ? selection.SelectedFaceIds : null);
        }

        undoStack.RecordAction($"Radial Array ({count}x)", before, scene);
        IsModifierPreviewActive = false;
        _modifierPreviewSnapshot = null;
        return true;
    }

    #endregion

    #region Numeric Input Handling

    public bool HandleNumericKey(char key, EditorScene scene)
    {
        if (!IsActive)
        {
            return false;
        }

        if (key == '-')
        {
            if (NumericInputBuffer.StartsWith('-'))
                NumericInputBuffer = NumericInputBuffer[1..];
            else
                NumericInputBuffer = "-" + NumericInputBuffer;
        }
        else if (key == '.')
        {
            if (!NumericInputBuffer.Contains('.'))
            {
                if (string.IsNullOrEmpty(NumericInputBuffer) || NumericInputBuffer == "-")
                    NumericInputBuffer += "0.";
                else
                    NumericInputBuffer += ".";
            }
        }
        else if (char.IsAsciiDigit(key))
        {
            NumericInputBuffer += key;
        }
        else
        {
            return false;
        }

        if (ParsedNumericValue.HasValue)
        {
            if (IsExtrudeActive)
            {
                SetExtrudeDistance(scene, ParsedNumericValue.Value);
            }
            else if (IsInsetActive)
            {
                SetInsetParameters(scene, ParsedNumericValue.Value, InsetDepth, InsetIndividual);
            }
        }

        return true;
    }

    public bool HandleBackspace(EditorScene scene)
    {
        if (!IsActive || string.IsNullOrEmpty(NumericInputBuffer))
        {
            return false;
        }

        NumericInputBuffer = NumericInputBuffer[..^1];
        if (ParsedNumericValue.HasValue)
        {
            if (IsExtrudeActive)
                SetExtrudeDistance(scene, ParsedNumericValue.Value);
            else if (IsInsetActive)
                SetInsetParameters(scene, ParsedNumericValue.Value, InsetDepth, InsetIndividual);
        }
        else
        {
            if (IsExtrudeActive)
                SetExtrudeDistance(scene, 0.0f);
            else if (IsInsetActive)
                SetInsetParameters(scene, 0.1f, InsetDepth, InsetIndividual);
        }

        return true;
    }

    public void ClearNumericInput()
    {
        NumericInputBuffer = string.Empty;
    }

    public string GetStatusReadout()
    {
        if (IsExtrudeActive)
        {
            string distStr = HasNumericInput ? $"Typed: {NumericInputBuffer}" : $"Dist: {ExtrudeDistance:+0.00;-0.00;0.00}m";
            return $"EXTRUDE ({ExtrudeTarget}) | {distStr} | LMB/Enter: Confirm, RMB/Esc: Cancel";
        }

        if (IsInsetActive)
        {
            string thickStr = HasNumericInput ? $"Typed: {NumericInputBuffer}" : $"Thick: {InsetThickness:0.00}m";
            return $"INSET | {thickStr}, Depth: {InsetDepth:0.00}m | LMB/Enter: Confirm, RMB/Esc: Cancel";
        }

        return string.Empty;
    }

    private void ResetTool()
    {
        ActiveTool = ActiveModalEditTool.None;
        _beforeSnapshot = null;
        _extrudeFaceIds.Clear();
        _extrudeEdges.Clear();
        _insetFaceIds.Clear();
        ClearNumericInput();
    }

    #endregion
}
