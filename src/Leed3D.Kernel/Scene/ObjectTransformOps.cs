using System.Numerics;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Texturing;

namespace Leed3D.Kernel.Scene;

/// <summary>
/// Pivot, origin, and transform bake utilities for <see cref="SceneObject"/>s.
/// </summary>
public static class ObjectTransformOps
{
    /// <summary>
    /// Moves the object origin/pivot to its geometry centroid without moving the geometry in world space.
    /// </summary>
    public static bool CenterOriginToCentroid(SceneObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        if (obj.Mesh is null || obj.Mesh.VertexCount == 0)
        {
            return false;
        }

        Vector3 localCentroid = Vector3.Zero;
        for (int i = 0; i < obj.Mesh.Vertices.Count; i++)
        {
            localCentroid += obj.Mesh.Vertices[i].Position;
        }
        localCentroid /= obj.Mesh.Vertices.Count;

        return ShiftLocalOrigin(obj, localCentroid);
    }

    /// <summary>
    /// Moves the object origin/pivot to the center of its local axis-aligned bounding box
    /// without moving the geometry or altering UVs in world space.
    /// </summary>
    public static bool SetOriginToBoundsCenter(SceneObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        if (obj.Mesh is null || obj.Mesh.VertexCount == 0)
        {
            return false;
        }

        BoundingBox3D bounds = obj.Mesh.ComputeBoundingBox();
        return ShiftLocalOrigin(obj, bounds.Center);
    }

    /// <summary>
    /// Moves the object origin/pivot to the bottom center of its bounding box (<c>Min.Y</c>)
    /// without moving the geometry or altering UVs in world space.
    /// </summary>
    public static bool SetOriginToBottomCenter(SceneObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        if (obj.Mesh is null || obj.Mesh.VertexCount == 0)
        {
            return false;
        }

        BoundingBox3D bounds = obj.Mesh.ComputeBoundingBox();
        Vector3 localBottom = new(bounds.Center.X, bounds.Min.Y, bounds.Center.Z);
        return ShiftLocalOrigin(obj, localBottom);
    }

    /// <summary>
    /// Moves the object origin/pivot to the bottom center of its bounding box (<c>Min.Y</c>)
    /// without moving the geometry in world space.
    /// </summary>
    public static bool CenterOriginToBottomBounds(SceneObject obj) => SetOriginToBottomCenter(obj);

    /// <summary>
    /// Bakes selected components of the object's transform into its mesh vertex positions and resets
    /// the baked transform components (<see cref="Transform3D.Position"/> to zero,
    /// <see cref="Transform3D.RotationEulerDegrees"/> to zero, and/or <see cref="Transform3D.Scale"/> to one)
    /// while preserving world-space vertex positions, outward normals, and TextureLock UVs.
    /// </summary>
    public static bool ApplyTransform(
        SceneObject obj,
        bool applyPosition = false,
        bool applyRotation = true,
        bool applyScale = true)
    {
        ArgumentNullException.ThrowIfNull(obj);
        if (obj.Mesh is null || obj.Mesh.VertexCount == 0)
        {
            return false;
        }

        bool hasPosition = applyPosition && obj.Transform.Position.LengthSquared() > 1e-6f;
        bool hasRotation = applyRotation && obj.Transform.RotationEulerDegrees.LengthSquared() > 1e-6f;
        bool hasScale = applyScale && (obj.Transform.Scale - Vector3.One).LengthSquared() > 1e-6f;

        if (!hasPosition && !hasRotation && !hasScale)
        {
            return false;
        }

        Matrix4x4 bakeMat = Matrix4x4.Identity;
        if (applyScale)
        {
            bakeMat *= Matrix4x4.CreateScale(obj.Transform.Scale);
        }
        if (applyRotation)
        {
            bakeMat *= Matrix4x4.CreateFromQuaternion(obj.Transform.GetRotationQuaternion());
        }
        if (applyPosition)
        {
            bakeMat *= Matrix4x4.CreateTranslation(obj.Transform.Position);
        }

        // If only Position is applied while Rotation or Scale remain un-applied, transform each vertex into the new local space
        if (applyPosition && (!applyRotation || !applyScale))
        {
            Vector3 oldPos = obj.Transform.Position;
            var newTransform = obj.Transform.Clone();
            newTransform.Position = Vector3.Zero;
            if (applyRotation) newTransform.RotationEulerDegrees = Vector3.Zero;
            if (applyScale) newTransform.Scale = Vector3.One;

            for (int i = 0; i < obj.Mesh.Vertices.Count; i++)
            {
                Vector3 worldPt = obj.Transform.TransformPointToWorld(obj.Mesh.Vertices[i].Position);
                obj.Mesh.Vertices[i].Position = newTransform.TransformPointToLocal(worldPt);
            }

            obj.Transform.Position = newTransform.Position;
            obj.Transform.RotationEulerDegrees = newTransform.RotationEulerDegrees;
            obj.Transform.Scale = newTransform.Scale;
        }
        else
        {
            for (int i = 0; i < obj.Mesh.Vertices.Count; i++)
            {
                obj.Mesh.Vertices[i].Position = Vector3.Transform(obj.Mesh.Vertices[i].Position, bakeMat);
            }

            if (applyPosition) obj.Transform.Position = Vector3.Zero;
            if (applyRotation) obj.Transform.RotationEulerDegrees = Vector3.Zero;
            if (applyScale) obj.Transform.Scale = Vector3.One;
        }

        if (bakeMat.GetDeterminant() < 0.0f)
        {
            for (int f = 0; f < obj.Mesh.Faces.Count; f++)
            {
                obj.Mesh.Faces[f].VertexIds.Reverse();
            }
        }

        obj.Mesh.InvalidateTopology();

        // FaceUvMapping is defined in world space; since world vertex positions are invariant under ApplyTransform,
        // TextureLock UVs already match exact world coordinates, and unlocked faces refresh against the world normal.
        UvProjectionEngine.RefreshUnlockedMeshAxes(obj.Mesh, obj.Transform);
        return true;
    }

    /// <summary>
    /// Bakes the object's current rotation and scale into its mesh vertex positions and resets
    /// <see cref="Transform3D.RotationEulerDegrees"/> to zero and <see cref="Transform3D.Scale"/> to one.
    /// </summary>
    public static bool FreezeScaleAndRotation(SceneObject obj)
        => ApplyTransform(obj, applyPosition: false, applyRotation: true, applyScale: true);

    private static bool ShiftLocalOrigin(SceneObject obj, Vector3 newLocalOrigin)
    {
        if (newLocalOrigin.LengthSquared() < 1e-10f)
        {
            return false;
        }

        Vector3 newWorldPosition = obj.Transform.TransformPointToWorld(newLocalOrigin);

        for (int i = 0; i < obj.Mesh!.Vertices.Count; i++)
        {
            obj.Mesh.Vertices[i].Position -= newLocalOrigin;
        }

        obj.Transform.Position = newWorldPosition;
        obj.Mesh.InvalidateTopology();
        return true;
    }
}
