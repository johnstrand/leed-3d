using System.Numerics;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Math;
using Leed3D.Runtime.Metadata;

namespace Leed3D.Kernel.Scene;

/// <summary>
/// An entity/object within an <see cref="EditorScene"/> carrying a <see cref="Transform3D"/>,
/// optional geometry <see cref="EditableMesh"/>, and granular <see cref="ElementMetadata"/>.
/// </summary>
public sealed class SceneObject
{
    public int Id { get; init; }
    public string Name { get; set; } = "Object";
    public Transform3D Transform { get; set; } = new();
    public EditableMesh? Mesh { get; set; }
    public ElementMetadata Metadata { get; set; } = new();
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; } = false;

    public SceneObject()
    {
    }

    public SceneObject(int id, string name, EditableMesh? mesh = null, Transform3D? transform = null, ElementMetadata? metadata = null)
    {
        Id = id;
        Name = name;
        Mesh = mesh;
        Transform = transform ?? new Transform3D();
        Metadata = metadata ?? new ElementMetadata();
    }

    /// <summary>
    /// Computes the world-space axis-aligned bounding box of this object's mesh transformed by its local matrix.
    /// </summary>
    public BoundingBox3D GetWorldBounds()
    {
        if (Mesh is null || Mesh.VertexCount == 0)
        {
            return BoundingBox3D.FromPoint(Transform.Position);
        }

        BoundingBox3D localBounds = Mesh.ComputeBoundingBox();
        return localBounds.Transform(Transform.GetLocalMatrix());
    }

    /// <summary>
    /// Creates a deep copy of this SceneObject, cloning its Transform, EditableMesh, and Metadata.
    /// </summary>
    public SceneObject Clone()
    {
        return new SceneObject(Id, Name, Mesh?.Clone(), Transform.Clone(), Metadata.Clone())
        {
            Visible = Visible,
            Locked = Locked
        };
    }

    /// <summary>
    /// Creates a snapshot copy of this SceneObject, structurally sharing its cached snapshot <see cref="EditableMesh"/>
    /// when the mesh has not changed since the last snapshot.
    /// </summary>
    public SceneObject CloneForSnapshot()
    {
        return new SceneObject(Id, Name, Mesh?.GetOrCreateSnapshotClone(), Transform.Clone(), Metadata.Clone())
        {
            Visible = Visible,
            Locked = Locked
        };
    }

    /// <summary>
    /// Restores a live working copy of this snapshot SceneObject, reusing <paramref name="existingLiveObject"/>'s
    /// mesh when it is still structurally identical to this snapshot's mesh.
    /// </summary>
    public SceneObject CloneFromSnapshot(SceneObject? existingLiveObject = null)
    {
        EditableMesh? restoredMesh = null;
        if (Mesh is not null)
        {
            if (existingLiveObject?.Mesh is not null &&
                existingLiveObject.Mesh.CanReuseForSnapshotRestore(Mesh))
            {
                restoredMesh = existingLiveObject.Mesh;
            }
            else
            {
                restoredMesh = Mesh.CloneFromSnapshot();
            }
        }

        return new SceneObject(Id, Name, restoredMesh, Transform.Clone(), Metadata.Clone())
        {
            Visible = Visible,
            Locked = Locked
        };
    }
}
