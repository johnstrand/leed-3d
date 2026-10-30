using System.Numerics;
using Leed3D.Kernel.Entities;
using Leed3D.Kernel.Geometry;

namespace Leed3D.Kernel.Scene;

/// <summary>
/// Container for all objects and entities in a level being edited, providing creation, removal,
/// querying, and deep snapshot cloning for undo/redo and serialization.
/// </summary>
public sealed class EditorScene
{
    private readonly Dictionary<int, SceneObject> _objectsById = new();
    private readonly List<SceneObject> _objects = new();
    private int _nextObjectId = 1;

    private readonly Dictionary<string, LevelEntity> _entitiesById = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LevelEntity> _entities = new();
    private int _nextEntityId = 1;

    public string Name { get; set; } = "Untitled";

    public IReadOnlyList<SceneObject> Objects => _objects;
    public int ObjectCount => _objects.Count;

    public IReadOnlyList<LevelEntity> Entities => _entities;
    public int EntityCount => _entities.Count;

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

    /// <summary>
    /// Deep-clones an existing SceneObject, allocates a new unique ID, adds it to the scene, and returns the clone.
    /// </summary>
    public SceneObject AddClonedObject(SceneObject original, string? newName = null)
    {
        ArgumentNullException.ThrowIfNull(original);

        int id = _nextObjectId++;
        string name = newName ?? $"{original.Name}_Copy";
        var clone = new SceneObject(id, name, original.Mesh?.Clone(), original.Transform.Clone())
        {
            Visible = original.Visible,
            Locked = original.Locked
        };

        _objectsById[id] = clone;
        _objects.Add(clone);
        return clone;
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
    /// Adds an entity to the scene. Ensures its ID is unique if not already set.
    /// </summary>
    public LevelEntity AddEntity(LevelEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (string.IsNullOrWhiteSpace(entity.Id) || _entitiesById.ContainsKey(entity.Id))
        {
            entity.Id = GenerateUniqueEntityId(string.IsNullOrWhiteSpace(entity.ClassName) ? "entity" : entity.ClassName);
        }

        _entitiesById[entity.Id] = entity;
        _entities.Add(entity);
        return entity;
    }

    /// <summary>
    /// Creates and adds a new entity of the specified class name.
    /// </summary>
    public LevelEntity CreateEntity(string className, string? id = null, Vector3? position = null, EntitySchemaRegistry? registry = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);

        EntitySchemaRegistry reg = registry ?? EntitySchemaRegistry.Default;
        EntityDefinition def = reg.GetDefinition(className) ?? new EntityDefinition
        {
            ClassName = className,
            DisplayName = className,
            GizmoShape = EntityGizmoShape.Point
        };

        string entityId = string.IsNullOrWhiteSpace(id) ? GenerateUniqueEntityId(className) : id;
        if (_entitiesById.ContainsKey(entityId))
        {
            entityId = GenerateUniqueEntityId(className);
        }

        LevelEntity instance = def.CreateInstance(entityId, position);
        _entitiesById[instance.Id] = instance;
        _entities.Add(instance);
        return instance;
    }

    /// <summary>
    /// Deep clones an existing entity, assigns a new unique ID, and adds it to the scene.
    /// </summary>
    public LevelEntity AddClonedEntity(LevelEntity original, string? newId = null)
    {
        ArgumentNullException.ThrowIfNull(original);

        string cloneId = string.IsNullOrWhiteSpace(newId)
            ? GenerateUniqueEntityId(original.ClassName)
            : newId;

        if (_entitiesById.ContainsKey(cloneId))
        {
            cloneId = GenerateUniqueEntityId(original.ClassName);
        }

        LevelEntity clone = original.Clone(cloneId);
        _entitiesById[clone.Id] = clone;
        _entities.Add(clone);
        return clone;
    }

    public bool RemoveEntity(string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId)) return false;

        if (!_entitiesById.Remove(entityId, out LevelEntity? entity))
        {
            return false;
        }

        _entities.Remove(entity);
        return true;
    }

    public LevelEntity? GetEntity(string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId)) return null;
        return _entitiesById.GetValueOrDefault(entityId);
    }

    private string GenerateUniqueEntityId(string baseName)
    {
        string prefix = string.IsNullOrWhiteSpace(baseName) ? "entity" : baseName.Trim().ToLowerInvariant();
        while (true)
        {
            string candidate = $"{prefix}_{_nextEntityId++}";
            if (!_entitiesById.ContainsKey(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// Creates a complete deep snapshot copy of this EditorScene.
    /// </summary>
    public EditorScene CloneSnapshot()
    {
        var snapshot = new EditorScene(Name)
        {
            _nextObjectId = _nextObjectId,
            _nextEntityId = _nextEntityId
        };

        for (int i = 0; i < _objects.Count; i++)
        {
            SceneObject clonedObj = _objects[i].Clone();
            snapshot._objects.Add(clonedObj);
            snapshot._objectsById[clonedObj.Id] = clonedObj;
        }

        for (int i = 0; i < _entities.Count; i++)
        {
            LevelEntity clonedEnt = _entities[i].Clone();
            snapshot._entities.Add(clonedEnt);
            snapshot._entitiesById[clonedEnt.Id] = clonedEnt;
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
        _nextEntityId = snapshot._nextEntityId;

        _objectsById.Clear();
        _objects.Clear();

        for (int i = 0; i < snapshot._objects.Count; i++)
        {
            SceneObject clonedObj = snapshot._objects[i].Clone();
            _objects.Add(clonedObj);
            _objectsById[clonedObj.Id] = clonedObj;
        }

        _entitiesById.Clear();
        _entities.Clear();

        for (int i = 0; i < snapshot._entities.Count; i++)
        {
            LevelEntity clonedEnt = snapshot._entities[i].Clone();
            _entities.Add(clonedEnt);
            _entitiesById[clonedEnt.Id] = clonedEnt;
        }
    }
}
