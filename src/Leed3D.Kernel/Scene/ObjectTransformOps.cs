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

        if (localCentroid.LengthSquared() < 1e-10f)
        {
            return false;
        }

        Vector3 worldShift = obj.Transform.TransformPointToWorld(localCentroid) - obj.Transform.Position;

        for (int i = 0; i < obj.Mesh.Vertices.Count; i++)
        {
            obj.Mesh.Vertices[i].Position -= localCentroid;
        }

        obj.Transform.Position += worldShift;
        obj.Mesh.InvalidateTopology();
        return true;
    }

    /// <summary>
    /// Moves the object origin/pivot to the bottom center of its bounding box (<c>Min.Y</c>)
    /// without moving the geometry in world space.
    /// </summary>
    public static bool CenterOriginToBottomBounds(SceneObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        if (obj.Mesh is null || obj.Mesh.VertexCount == 0)
        {
            return false;
        }

        BoundingBox3D bounds = obj.Mesh.ComputeBoundingBox();
        Vector3 localBottom = new(bounds.Center.X, bounds.Min.Y, bounds.Center.Z);

        if (localBottom.LengthSquared() < 1e-10f)
        {
            return false;
        }

        Vector3 worldShift = obj.Transform.TransformPointToWorld(localBottom) - obj.Transform.Position;

        for (int i = 0; i < obj.Mesh.Vertices.Count; i++)
        {
            obj.Mesh.Vertices[i].Position -= localBottom;
        }

        obj.Transform.Position += worldShift;
        obj.Mesh.InvalidateTopology();
        return true;
    }

    /// <summary>
    /// Bakes the object's current rotation and scale into its mesh vertex positions and resets
    /// <see cref="Transform3D.RotationEulerDegrees"/> to zero and <see cref="Transform3D.Scale"/> to one.
    /// </summary>
    public static bool FreezeScaleAndRotation(SceneObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        if (obj.Mesh is null || obj.Mesh.VertexCount == 0)
        {
            return false;
        }

        bool hasRotation = obj.Transform.RotationEulerDegrees.LengthSquared() > 1e-6f;
        bool hasScale = (obj.Transform.Scale - Vector3.One).LengthSquared() > 1e-6f;
        if (!hasRotation && !hasScale)
        {
            return false;
        }

        Matrix4x4 rsMat = Matrix4x4.CreateScale(obj.Transform.Scale) *
                          Matrix4x4.CreateFromQuaternion(obj.Transform.GetRotationQuaternion());

        for (int i = 0; i < obj.Mesh.Vertices.Count; i++)
        {
            obj.Mesh.Vertices[i].Position = Vector3.Transform(obj.Mesh.Vertices[i].Position, rsMat);
        }

        for (int f = 0; f < obj.Mesh.Faces.Count; f++)
        {
            var face = obj.Mesh.Faces[f];
            if (face.UvMapping.TextureLock)
            {
                face.UvMapping.UAxis = Vector3.Normalize(Vector3.TransformNormal(face.UvMapping.UAxis, rsMat));
                face.UvMapping.VAxis = Vector3.Normalize(Vector3.TransformNormal(face.UvMapping.VAxis, rsMat));
            }
            else
            {
                Vector3 normal = face.ComputeNormal(obj.Mesh);
                (face.UvMapping.UAxis, face.UvMapping.VAxis) = UvProjectionEngine.GetDefaultAxesForNormal(normal);
            }
        }

        obj.Transform.RotationEulerDegrees = Vector3.Zero;
        obj.Transform.Scale = Vector3.One;
        obj.Mesh.InvalidateTopology();
        return true;
    }
}
