using System.Numerics;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Math;

namespace Leed3D.Kernel.Scene;

/// <summary>
/// An entity/object within an <see cref="EditorScene"/> carrying a <see cref="Transform3D"/>
/// and optional geometry <see cref="EditableMesh"/>.
/// </summary>
public sealed class SceneObject
{
    public int Id { get; init; }
    public string Name { get; set; } = "Object";
    public Transform3D Transform { get; set; } = new();
    public EditableMesh? Mesh { get; set; }
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; } = false;

    public SceneObject()
    {
    }

    public SceneObject(int id, string name, EditableMesh? mesh = null, Transform3D? transform = null)
    {
        Id = id;
        Name = name;
        Mesh = mesh;
        Transform = transform ?? new Transform3D();
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
    /// Creates a deep copy of this SceneObject, cloning its Transform and EditableMesh.
    /// </summary>
    public SceneObject Clone()
    {
        return new SceneObject(Id, Name, Mesh?.Clone(), Transform.Clone())
        {
            Visible = Visible,
            Locked = Locked
        };
    }
}
