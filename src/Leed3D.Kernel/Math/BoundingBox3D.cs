using System.Numerics;

namespace Leed3D.Kernel.Math;

/// <summary>
/// Axis-Aligned Bounding Box (AABB) in 3D space.
/// </summary>
public readonly struct BoundingBox3D : IEquatable<BoundingBox3D>
{
    public Vector3 Min { get; }
    public Vector3 Max { get; }

    public BoundingBox3D(Vector3 min, Vector3 max)
    {
        Min = new Vector3(
            System.Math.Min(min.X, max.X),
            System.Math.Min(min.Y, max.Y),
            System.Math.Min(min.Z, max.Z));

        Max = new Vector3(
            System.Math.Max(min.X, max.X),
            System.Math.Max(min.Y, max.Y),
            System.Math.Max(min.Z, max.Z));
    }

    public static BoundingBox3D FromPoint(Vector3 point)
    {
        return new BoundingBox3D(point, point);
    }

    public static BoundingBox3D FromPoints(IEnumerable<Vector3> points)
    {
        using var enumerator = points.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return new BoundingBox3D(Vector3.Zero, Vector3.Zero);
        }

        Vector3 min = enumerator.Current;
        Vector3 max = enumerator.Current;

        while (enumerator.MoveNext())
        {
            min = Vector3.Min(min, enumerator.Current);
            max = Vector3.Max(max, enumerator.Current);
        }

        return new BoundingBox3D(min, max);
    }

    public Vector3 Center => (Min + Max) * 0.5f;

    public Vector3 Size => Max - Min;

    public Vector3 Extents => (Max - Min) * 0.5f;

    public BoundingBox3D Encapsulate(Vector3 point)
    {
        return new BoundingBox3D(Vector3.Min(Min, point), Vector3.Max(Max, point));
    }

    public BoundingBox3D Encapsulate(BoundingBox3D other)
    {
        return new BoundingBox3D(Vector3.Min(Min, other.Min), Vector3.Max(Max, other.Max));
    }

    public bool Contains(Vector3 point)
    {
        return point.X >= Min.X && point.X <= Max.X &&
               point.Y >= Min.Y && point.Y <= Max.Y &&
               point.Z >= Min.Z && point.Z <= Max.Z;
    }

    /// <summary>
    /// Transforms the 8 corner points of this bounding box by the specified 4x4 matrix
    /// and returns the newly fitted AABB.
    /// </summary>
    public BoundingBox3D Transform(Matrix4x4 matrix)
    {
        Span<Vector3> corners = stackalloc Vector3[8];
        corners[0] = new Vector3(Min.X, Min.Y, Min.Z);
        corners[1] = new Vector3(Max.X, Min.Y, Min.Z);
        corners[2] = new Vector3(Min.X, Max.Y, Min.Z);
        corners[3] = new Vector3(Max.X, Max.Y, Min.Z);
        corners[4] = new Vector3(Min.X, Min.Y, Max.Z);
        corners[5] = new Vector3(Max.X, Min.Y, Max.Z);
        corners[6] = new Vector3(Min.X, Max.Y, Max.Z);
        corners[7] = new Vector3(Max.X, Max.Y, Max.Z);

        Vector3 newMin = Vector3.Transform(corners[0], matrix);
        Vector3 newMax = newMin;

        for (int i = 1; i < 8; i++)
        {
            Vector3 transformed = Vector3.Transform(corners[i], matrix);
            newMin = Vector3.Min(newMin, transformed);
            newMax = Vector3.Max(newMax, transformed);
        }

        return new BoundingBox3D(newMin, newMax);
    }

    public bool Equals(BoundingBox3D other)
    {
        return Min.Equals(other.Min) && Max.Equals(other.Max);
    }

    public override bool Equals(object? obj) => obj is BoundingBox3D other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Min, Max);

    public static bool operator ==(BoundingBox3D left, BoundingBox3D right) => left.Equals(right);

    public static bool operator !=(BoundingBox3D left, BoundingBox3D right) => !left.Equals(right);
}
