using System.Numerics;
using Leed3D.Kernel.Geometry;

namespace Leed3D.Kernel.Scene;

/// <summary>
/// Container for all objects in a level being edited, providing creation, removal,
/// querying, and deep snapshot cloning for undo/redo and serialization.
/// </summary>
public sealed class EditorScene
{
    private readonly Dictionary<int, SceneObject> _objectsById = new();
    private readonly List<SceneObject> _objects = new();
    private int _nextObjectId = 1;

    public string Name { get; set; } = "Untitled";

    public IReadOnlyList<SceneObject> Objects => _objects;
    public int ObjectCount => _objects.Count;

    public EditorScene()
    {
    }

    public EditorScene(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Adds a mesh object to the scene at an optional position.
    /// </summary>
    public SceneObject AddMeshObject(string name, EditableMesh mesh, Vector3? position = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        int id = _nextObjectId++;
        var transform = new Transform3D(position ?? Vector3.Zero);
        var sceneObject = new SceneObject(id, name, mesh, transform);

        _objectsById[id] = sceneObject;
        _objects.Add(sceneObject);
        return sceneObject;
    }

    /// <summary>
    /// Adds an arbitrary SceneObject (e.g. gameplay entity or camera).
    /// </summary>
    public void AddObject(SceneObject sceneObject)
    {
        ArgumentNullException.ThrowIfNull(sceneObject);

        if (sceneObject.Id >= _nextObjectId)
        {
            _nextObjectId = sceneObject.Id + 1;
        }

        _objectsById[sceneObject.Id] = sceneObject;
        _objects.Add(sceneObject);
    }

    public bool RemoveObject(int objectId)
    {
        if (!_objectsById.Remove(objectId, out SceneObject? sceneObject))
        {
            return false;
        }

        _objects.Remove(sceneObject);
        return true;
    }

    public SceneObject? GetObject(int objectId)
    {
        return _objectsById.GetValueOrDefault(objectId);
    }

    /// <summary>
    /// Creates a complete deep snapshot copy of this EditorScene.
    /// </summary>
    public EditorScene CloneSnapshot()
    {
        var snapshot = new EditorScene(Name)
        {
            _nextObjectId = _nextObjectId
        };

        for (int i = 0; i < _objects.Count; i++)
        {
            SceneObject clonedObj = _objects[i].Clone();
            snapshot._objects.Add(clonedObj);
            snapshot._objectsById[clonedObj.Id] = clonedObj;
        }

        return snapshot;
    }

    /// <summary>
    /// Restores this EditorScene to the state of <paramref name="snapshot"/>.
    /// </summary>
    public void RestoreFromSnapshot(EditorScene snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Name = snapshot.Name;
        _nextObjectId = snapshot._nextObjectId;

        _objectsById.Clear();
        _objects.Clear();

        for (int i = 0; i < snapshot._objects.Count; i++)
        {
            SceneObject clonedObj = snapshot._objects[i].Clone();
            _objects.Add(clonedObj);
            _objectsById[clonedObj.Id] = clonedObj;
        }
    }
}
