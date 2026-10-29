using System.Numerics;

namespace Leed3D.Kernel.Math;

/// <summary>
/// Representation of a 3D infinite plane defined by the equation Dot(Normal, P) + D = 0,
/// where Normal is normalized and D is the plane's signed distance constant.
/// </summary>
public readonly struct Plane3D : IEquatable<Plane3D>
{
    public Vector3 Normal { get; }
    public float D { get; }

    public Plane3D(Vector3 normal, float d)
    {
        float length = normal.Length();
        if (length > 1e-6f)
        {
            Normal = normal / length;
            D = d / length;
        }
        else
        {
            Normal = Vector3.UnitY;
            D = d;
        }
    }

    public static Plane3D CreateFromPointAndNormal(Vector3 point, Vector3 normal)
    {
        Vector3 normalized = Vector3.Normalize(normal);
        float d = -Vector3.Dot(normalized, point);
        return new Plane3D(normalized, d);
    }

    public static Plane3D CreateFromPoints(Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a;
        Vector3 ac = c - a;
        Vector3 normal = Vector3.Normalize(Vector3.Cross(ab, ac));
        float d = -Vector3.Dot(normal, a);
        return new Plane3D(normal, d);
    }

    /// <summary>
    /// Computes the signed distance from a point to this plane.
    /// Positive values lie in the half-space pointed to by Normal, negative behind.
    /// </summary>
    public float SignedDistance(Vector3 point)
    {
        return Vector3.Dot(Normal, point) + D;
    }

    /// <summary>
    /// Projects a 3D point orthogonally onto the plane surface.
    /// </summary>
    public Vector3 ProjectPoint(Vector3 point)
    {
        float distance = SignedDistance(point);
        return point - (Normal * distance);
    }

    public bool Equals(Plane3D other)
    {
        return Normal.Equals(other.Normal) && D.Equals(other.D);
    }

    public override bool Equals(object? obj) => obj is Plane3D other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Normal, D);

    public static bool operator ==(Plane3D left, Plane3D right) => left.Equals(right);

    public static bool operator !=(Plane3D left, Plane3D right) => !left.Equals(right);
}
