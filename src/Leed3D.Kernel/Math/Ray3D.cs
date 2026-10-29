using System.Numerics;

namespace Leed3D.Kernel.Math;

/// <summary>
/// A directed 3D ray defined by an origin point and a normalized direction vector.
/// </summary>
public readonly struct Ray3D : IEquatable<Ray3D>
{
    private const float Epsilon = 1e-6f;

    public Vector3 Origin { get; }
    public Vector3 Direction { get; }

    public Ray3D(Vector3 origin, Vector3 direction)
    {
        Origin = origin;
        float len = direction.Length();
        Direction = len > Epsilon ? direction / len : Vector3.UnitZ;
    }

    /// <summary>
    /// Gets the point along the ray at the specified parametric distance.
    /// </summary>
    public Vector3 GetPoint(float distance)
    {
        return Origin + (Direction * distance);
    }

    /// <summary>
    /// Intersects the ray with an infinite plane. Returns true if an intersection occurs in front of the ray (t >= 0).
    /// </summary>
    public bool IntersectPlane(Plane3D plane, out float distance, out Vector3 hitPoint)
    {
        float denom = Vector3.Dot(plane.Normal, Direction);

        // Ray is nearly parallel to the plane
        if (System.Math.Abs(denom) < Epsilon)
        {
            distance = 0.0f;
            hitPoint = Vector3.Zero;
            return false;
        }

        float t = -(Vector3.Dot(plane.Normal, Origin) + plane.D) / denom;
        if (t < 0.0f)
        {
            distance = t;
            hitPoint = Vector3.Zero;
            return false;
        }

        distance = t;
        hitPoint = GetPoint(t);
        return true;
    }

    /// <summary>
    /// Intersects the ray with a 3D triangle using the Möller–Trumbore intersection algorithm.
    /// </summary>
    public bool IntersectTriangle(
        Vector3 v0,
        Vector3 v1,
        Vector3 v2,
        bool cullBackface,
        out float distance,
        out Vector3 hitPoint)
    {
        distance = 0.0f;
        hitPoint = Vector3.Zero;

        Vector3 edge1 = v1 - v0;
        Vector3 edge2 = v2 - v0;
        Vector3 pvec = Vector3.Cross(Direction, edge2);
        float det = Vector3.Dot(edge1, pvec);

        if (cullBackface)
        {
            if (det < Epsilon)
            {
                return false;
            }
        }
        else
        {
            if (System.Math.Abs(det) < Epsilon)
            {
                return false;
            }
        }

        float invDet = 1.0f / det;
        Vector3 tvec = Origin - v0;
        float u = Vector3.Dot(tvec, pvec) * invDet;
        if (u < 0.0f || u > 1.0f)
        {
            return false;
        }

        Vector3 qvec = Vector3.Cross(tvec, edge1);
        float v = Vector3.Dot(Direction, qvec) * invDet;
        if (v < 0.0f || (u + v) > 1.0f)
        {
            return false;
        }

        float t = Vector3.Dot(edge2, qvec) * invDet;
        if (t < 0.0f)
        {
            return false;
        }

        distance = t;
        hitPoint = GetPoint(t);
        return true;
    }

    /// <summary>
    /// Intersects the ray with an Axis-Aligned Bounding Box (AABB) using the slab method.
    /// </summary>
    public bool IntersectBoundingBox(BoundingBox3D box, out float tMin, out float tMax)
    {
        tMin = float.NegativeInfinity;
        tMax = float.PositiveInfinity;

        // X slab
        if (System.Math.Abs(Direction.X) > Epsilon)
        {
            float invDx = 1.0f / Direction.X;
            float t1 = (box.Min.X - Origin.X) * invDx;
            float t2 = (box.Max.X - Origin.X) * invDx;
            if (t1 > t2) (t1, t2) = (t2, t1);

            tMin = System.Math.Max(tMin, t1);
            tMax = System.Math.Min(tMax, t2);

            if (tMin > tMax) return false;
        }
        else if (Origin.X < box.Min.X || Origin.X > box.Max.X)
        {
            return false;
        }

        // Y slab
        if (System.Math.Abs(Direction.Y) > Epsilon)
        {
            float invDy = 1.0f / Direction.Y;
            float t1 = (box.Min.Y - Origin.Y) * invDy;
            float t2 = (box.Max.Y - Origin.Y) * invDy;
            if (t1 > t2) (t1, t2) = (t2, t1);

            tMin = System.Math.Max(tMin, t1);
            tMax = System.Math.Min(tMax, t2);

            if (tMin > tMax) return false;
        }
        else if (Origin.Y < box.Min.Y || Origin.Y > box.Max.Y)
        {
            return false;
        }

        // Z slab
        if (System.Math.Abs(Direction.Z) > Epsilon)
        {
            float invDz = 1.0f / Direction.Z;
            float t1 = (box.Min.Z - Origin.Z) * invDz;
            float t2 = (box.Max.Z - Origin.Z) * invDz;
            if (t1 > t2) (t1, t2) = (t2, t1);

            tMin = System.Math.Max(tMin, t1);
            tMax = System.Math.Min(tMax, t2);

            if (tMin > tMax) return false;
        }
        else if (Origin.Z < box.Min.Z || Origin.Z > box.Max.Z)
        {
            return false;
        }

        // If the box is behind the ray origin, return false
        if (tMax < 0.0f)
        {
            return false;
        }

        return true;
    }

    public bool Equals(Ray3D other)
    {
        return Origin.Equals(other.Origin) && Direction.Equals(other.Direction);
    }

    public override bool Equals(object? obj) => obj is Ray3D other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Origin, Direction);

    public static bool operator ==(Ray3D left, Ray3D right) => left.Equals(right);

    public static bool operator !=(Ray3D left, Ray3D right) => !left.Equals(right);
}
