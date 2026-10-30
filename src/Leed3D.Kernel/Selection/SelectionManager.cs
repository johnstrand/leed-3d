using System.Numerics;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Scene;

namespace Leed3D.Kernel.Selection;

public enum InteractionMode
{
    Object,
    Vertex,
    Edge,
    Face
}

/// <summary>
/// Manages selection state across Object Mode and Edit Mode (Vertex, Edge, Face),
/// selection toggling (Shift+Click), Select All (A), Clear (Alt+A), Invert (Ctrl+I),
/// selection conversion between modes, and selection centroid pivot calculation.
/// </summary>
public sealed class SelectionManager
{
    private readonly HashSet<int> _selectedObjectIds = [];
    private readonly HashSet<int> _selectedVertexIds = [];
    private readonly HashSet<EdgeKey> _selectedEdges = [];
    private readonly HashSet<int> _selectedFaceIds = [];

    public InteractionMode Mode { get; private set; } = InteractionMode.Object;
    public int? ActiveObjectId { get; set; }

    public IReadOnlySet<int> SelectedObjectIds => _selectedObjectIds;
    public IReadOnlySet<int> SelectedVertexIds => _selectedVertexIds;
    public IReadOnlySet<EdgeKey> SelectedEdges => _selectedEdges;
    public IReadOnlySet<int> SelectedFaceIds => _selectedFaceIds;

    public bool HasSelection => Mode switch
    {
        InteractionMode.Object => _selectedObjectIds.Count > 0,
        InteractionMode.Vertex => _selectedVertexIds.Count > 0,
        InteractionMode.Edge => _selectedEdges.Count > 0,
        InteractionMode.Face => _selectedFaceIds.Count > 0,
        _ => false
    };

    public void SetMode(InteractionMode newMode, EditorScene? scene = null)
    {
        if (Mode == newMode)
        {
            return;
        }

        InteractionMode oldMode = Mode;
        Mode = newMode;

        // If entering an Edit mode without an active object, pick the first selected object if any
        if (newMode != InteractionMode.Object)
        {
            if (ActiveObjectId is null || (scene != null && scene.GetObject(ActiveObjectId.Value) is null))
            {
                ActiveObjectId = _selectedObjectIds.Count > 0 ? _selectedObjectIds.First() : null;
            }

            if (ActiveObjectId.HasValue)
            {
                _selectedObjectIds.Clear();
                _selectedObjectIds.Add(ActiveObjectId.Value);
            }
        }

        // Automatic selection conversion between sub-element modes
        if (scene != null && ActiveObjectId.HasValue && oldMode != InteractionMode.Object && newMode != InteractionMode.Object)
        {
            SceneObject? obj = scene.GetObject(ActiveObjectId.Value);
            if (obj?.Mesh != null)
            {
                ConvertSubElementSelection(obj.Mesh, oldMode, newMode);
            }
        }
    }

    public void ToggleObjectEditMode(EditorScene? scene = null)
    {
        if (Mode == InteractionMode.Object)
        {
            SetMode(InteractionMode.Face, scene);
        }
        else
        {
            SetMode(InteractionMode.Object, scene);
        }
    }

    public void SelectSingleObject(int objectId)
    {
        _selectedObjectIds.Clear();
        _selectedObjectIds.Add(objectId);
        ActiveObjectId = objectId;
        ClearSubElementSelection();
    }

    public void ToggleSelectObject(int objectId)
    {
        if (_selectedObjectIds.Contains(objectId))
        {
            _selectedObjectIds.Remove(objectId);
            if (ActiveObjectId == objectId)
            {
                ActiveObjectId = _selectedObjectIds.Count > 0 ? _selectedObjectIds.First() : null;
            }
        }
        else
        {
            _selectedObjectIds.Add(objectId);
            ActiveObjectId = objectId;
        }
        ClearSubElementSelection();
    }

    public void SelectSingleVertex(int vertexId)
    {
        _selectedVertexIds.Clear();
        _selectedVertexIds.Add(vertexId);
    }

    public void ToggleSelectVertex(int vertexId)
    {
        if (!_selectedVertexIds.Remove(vertexId))
        {
            _selectedVertexIds.Add(vertexId);
        }
    }

    public void SelectSingleEdge(EdgeKey edge)
    {
        _selectedEdges.Clear();
        _selectedEdges.Add(edge);
    }

    public void ToggleSelectEdge(EdgeKey edge)
    {
        if (!_selectedEdges.Remove(edge))
        {
            _selectedEdges.Add(edge);
        }
    }

    public void SelectSingleFace(int faceId)
    {
        _selectedFaceIds.Clear();
        _selectedFaceIds.Add(faceId);
    }

    public void SelectMultipleFaces(IEnumerable<int> faceIds)
    {
        ArgumentNullException.ThrowIfNull(faceIds);
        _selectedFaceIds.Clear();
        foreach (int id in faceIds)
        {
            _selectedFaceIds.Add(id);
        }
    }

    public void ToggleSelectFace(int faceId)
    {
        if (!_selectedFaceIds.Remove(faceId))
        {
            _selectedFaceIds.Add(faceId);
        }
    }

    public void SelectMultipleObjects(IEnumerable<int> objectIds)
    {
        ArgumentNullException.ThrowIfNull(objectIds);
        _selectedObjectIds.Clear();
        foreach (int id in objectIds)
        {
            _selectedObjectIds.Add(id);
        }
        ActiveObjectId = _selectedObjectIds.Count > 0 ? _selectedObjectIds.First() : null;
        ClearSubElementSelection();
    }

    public void ClearSelection()
    {
        switch (Mode)
        {
            case InteractionMode.Object:
                _selectedObjectIds.Clear();
                ActiveObjectId = null;
                ClearSubElementSelection();
                break;
            case InteractionMode.Vertex:
                _selectedVertexIds.Clear();
                break;
            case InteractionMode.Edge:
                _selectedEdges.Clear();
                break;
            case InteractionMode.Face:
                _selectedFaceIds.Clear();
                break;
        }
    }

    public void ClearSubElementSelection()
    {
        _selectedVertexIds.Clear();
        _selectedEdges.Clear();
        _selectedFaceIds.Clear();
    }

    public void SelectAll(EditorScene scene)
    {
        switch (Mode)
        {
            case InteractionMode.Object:
                _selectedObjectIds.Clear();
                foreach (SceneObject obj in scene.Objects)
                {
                    if (obj.Visible && !obj.Locked)
                    {
                        _selectedObjectIds.Add(obj.Id);
                    }
                }
                if (_selectedObjectIds.Count > 0 && (!ActiveObjectId.HasValue || !_selectedObjectIds.Contains(ActiveObjectId.Value)))
                {
                    ActiveObjectId = _selectedObjectIds.First();
                }
                break;

            case InteractionMode.Vertex:
                if (GetActiveMesh(scene) is { } vMesh)
                {
                    _selectedVertexIds.Clear();
                    foreach (MeshVertex v in vMesh.Vertices) _selectedVertexIds.Add(v.Id);
                }
                break;

            case InteractionMode.Edge:
                if (GetActiveMesh(scene) is { } eMesh)
                {
                    _selectedEdges.Clear();
                    foreach (TopologyEdge e in eMesh.GetTopology().Edges) _selectedEdges.Add(e.Key);
                }
                break;

            case InteractionMode.Face:
                if (GetActiveMesh(scene) is { } fMesh)
                {
                    _selectedFaceIds.Clear();
                    foreach (PolygonFace f in fMesh.Faces) _selectedFaceIds.Add(f.Id);
                }
                break;
        }
    }

    public void InvertSelection(EditorScene scene)
    {
        switch (Mode)
        {
            case InteractionMode.Object:
                var newObjSelection = new HashSet<int>();
                foreach (SceneObject obj in scene.Objects)
                {
                    if (obj.Visible && !obj.Locked && !_selectedObjectIds.Contains(obj.Id))
                    {
                        newObjSelection.Add(obj.Id);
                    }
                }
                _selectedObjectIds.Clear();
                foreach (int id in newObjSelection) _selectedObjectIds.Add(id);
                ActiveObjectId = _selectedObjectIds.Count > 0 ? _selectedObjectIds.First() : null;
                break;

            case InteractionMode.Vertex:
                if (GetActiveMesh(scene) is { } vMesh)
                {
                    var newVerts = new HashSet<int>();
                    foreach (MeshVertex v in vMesh.Vertices)
                    {
                        if (!_selectedVertexIds.Contains(v.Id)) newVerts.Add(v.Id);
                    }
                    _selectedVertexIds.Clear();
                    foreach (int id in newVerts) _selectedVertexIds.Add(id);
                }
                break;

            case InteractionMode.Edge:
                if (GetActiveMesh(scene) is { } eMesh)
                {
                    var newEdges = new HashSet<EdgeKey>();
                    foreach (TopologyEdge e in eMesh.GetTopology().Edges)
                    {
                        if (!_selectedEdges.Contains(e.Key)) newEdges.Add(e.Key);
                    }
                    _selectedEdges.Clear();
                    foreach (EdgeKey k in newEdges) _selectedEdges.Add(k);
                }
                break;

            case InteractionMode.Face:
                if (GetActiveMesh(scene) is { } fMesh)
                {
                    var newFaces = new HashSet<int>();
                    foreach (PolygonFace f in fMesh.Faces)
                    {
                        if (!_selectedFaceIds.Contains(f.Id)) newFaces.Add(f.Id);
                    }
                    _selectedFaceIds.Clear();
                    foreach (int id in newFaces) _selectedFaceIds.Add(id);
                }
                break;
        }
    }

    /// <summary>
    /// Computes the world-space centroid of all selected elements.
    /// Returns null if selection is empty.
    /// </summary>
    public Vector3? GetSelectionPivotWorld(EditorScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (Mode == InteractionMode.Object)
        {
            if (_selectedObjectIds.Count == 0) return null;

            Vector3 sum = Vector3.Zero;
            int count = 0;
            foreach (int id in _selectedObjectIds)
            {
                SceneObject? obj = scene.GetObject(id);
                if (obj is not null)
                {
                    sum += obj.Transform.Position;
                    count++;
                }
            }

            return count > 0 ? sum / count : null;
        }

        if (!ActiveObjectId.HasValue) return null;
        SceneObject? activeObj = scene.GetObject(ActiveObjectId.Value);
        if (activeObj?.Mesh is null) return null;

        EditableMesh mesh = activeObj.Mesh;
        Matrix4x4 localToWorld = activeObj.Transform.GetLocalMatrix();

        switch (Mode)
        {
            case InteractionMode.Vertex:
            {
                if (_selectedVertexIds.Count == 0) return null;
                Vector3 sum = Vector3.Zero;
                int count = 0;
                foreach (int vId in _selectedVertexIds)
                {
                    MeshVertex? v = mesh.GetVertex(vId);
                    if (v is not null)
                    {
                        sum += Vector3.Transform(v.Position, localToWorld);
                        count++;
                    }
                }
                return count > 0 ? sum / count : null;
            }

            case InteractionMode.Edge:
            {
                if (_selectedEdges.Count == 0) return null;
                Vector3 sum = Vector3.Zero;
                int count = 0;
                foreach (EdgeKey edge in _selectedEdges)
                {
                    MeshVertex? vA = mesh.GetVertex(edge.VMin);
                    MeshVertex? vB = mesh.GetVertex(edge.VMax);
                    if (vA is not null && vB is not null)
                    {
                        Vector3 mid = (vA.Position + vB.Position) * 0.5f;
                        sum += Vector3.Transform(mid, localToWorld);
                        count++;
                    }
                }
                return count > 0 ? sum / count : null;
            }

            case InteractionMode.Face:
            {
                if (_selectedFaceIds.Count == 0) return null;
                Vector3 sum = Vector3.Zero;
                int count = 0;
                foreach (int fId in _selectedFaceIds)
                {
                    PolygonFace? face = mesh.GetFace(fId);
                    if (face is not null)
                    {
                        Vector3 centroid = face.ComputeCentroid(mesh);
                        sum += Vector3.Transform(centroid, localToWorld);
                        count++;
                    }
                }
                return count > 0 ? sum / count : null;
            }

            default:
                return null;
        }
    }

    private void ConvertSubElementSelection(EditableMesh mesh, InteractionMode oldMode, InteractionMode newMode)
    {
        if (oldMode == InteractionMode.Face && newMode == InteractionMode.Vertex)
        {
            _selectedVertexIds.Clear();
            foreach (int fId in _selectedFaceIds)
            {
                PolygonFace? face = mesh.GetFace(fId);
                if (face != null)
                {
                    foreach (int vId in face.VertexIds) _selectedVertexIds.Add(vId);
                }
            }
        }
        else if (oldMode == InteractionMode.Face && newMode == InteractionMode.Edge)
        {
            _selectedEdges.Clear();
            foreach (int fId in _selectedFaceIds)
            {
                PolygonFace? face = mesh.GetFace(fId);
                if (face != null)
                {
                    int count = face.VertexIds.Count;
                    for (int i = 0; i < count; i++)
                    {
                        _selectedEdges.Add(new EdgeKey(face.VertexIds[i], face.VertexIds[(i + 1) % count]));
                    }
                }
            }
        }
        else if (oldMode == InteractionMode.Edge && newMode == InteractionMode.Vertex)
        {
            _selectedVertexIds.Clear();
            foreach (EdgeKey edge in _selectedEdges)
            {
                _selectedVertexIds.Add(edge.VMin);
                _selectedVertexIds.Add(edge.VMax);
            }
        }
        else if (oldMode == InteractionMode.Edge && newMode == InteractionMode.Face)
        {
            _selectedFaceIds.Clear();
            foreach (PolygonFace face in mesh.Faces)
            {
                int count = face.VertexIds.Count;
                bool allEdges = true;
                for (int i = 0; i < count; i++)
                {
                    var eKey = new EdgeKey(face.VertexIds[i], face.VertexIds[(i + 1) % count]);
                    if (!_selectedEdges.Contains(eKey))
                    {
                        allEdges = false;
                        break;
                    }
                }
                if (allEdges)
                {
                    _selectedFaceIds.Add(face.Id);
                }
            }
        }
        else if (oldMode == InteractionMode.Vertex && newMode == InteractionMode.Face)
        {
            _selectedFaceIds.Clear();
            foreach (PolygonFace face in mesh.Faces)
            {
                if (face.VertexIds.All(vId => _selectedVertexIds.Contains(vId)))
                {
                    _selectedFaceIds.Add(face.Id);
                }
            }
        }
        else if (oldMode == InteractionMode.Vertex && newMode == InteractionMode.Edge)
        {
            _selectedEdges.Clear();
            foreach (TopologyEdge edge in mesh.GetTopology().Edges)
            {
                if (_selectedVertexIds.Contains(edge.Key.VMin) && _selectedVertexIds.Contains(edge.Key.VMax))
                {
                    _selectedEdges.Add(edge.Key);
                }
            }
        }
    }

    private EditableMesh? GetActiveMesh(EditorScene scene)
    {
        if (!ActiveObjectId.HasValue) return null;
        return scene.GetObject(ActiveObjectId.Value)?.Mesh;
    }
}
