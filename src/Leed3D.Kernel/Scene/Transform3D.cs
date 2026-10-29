using System.Numerics;

namespace Leed3D.Kernel.Scene;

/// <summary>
/// Represents a local 3D transform with Position, Euler rotation in degrees (Pitch X, Yaw Y, Roll Z), and Scale.
/// Coordinates are Left-Handed (+X Right, +Y Up, +Z Away).
/// </summary>
public sealed class Transform3D
{
    private const float DegToRad = MathF.PI / 180.0f;
    private const float RadToDeg = 180.0f / MathF.PI;

    public Vector3 Position { get; set; } = Vector3.Zero;
    public Vector3 RotationEulerDegrees { get; set; } = Vector3.Zero;
    public Vector3 Scale { get; set; } = Vector3.One;

    public Transform3D()
    {
    }

    public Transform3D(Vector3 position, Vector3? rotationEulerDegrees = null, Vector3? scale = null)
    {
        Position = position;
        RotationEulerDegrees = rotationEulerDegrees ?? Vector3.Zero;
        Scale = scale ?? Vector3.One;
    }

    /// <summary>
    /// Gets the rotation quaternion corresponding to Euler degrees (Pitch X, Yaw Y, Roll Z).
    /// </summary>
    public Quaternion GetRotationQuaternion()
    {
        float pitchRad = RotationEulerDegrees.X * DegToRad;
        float yawRad = RotationEulerDegrees.Y * DegToRad;
        float rollRad = RotationEulerDegrees.Z * DegToRad;

        return Quaternion.CreateFromYawPitchRoll(yawRad, pitchRad, rollRad);
    }

    /// <summary>
    /// Sets the rotation Euler degrees from a quaternion.
    /// </summary>
    public void SetRotationFromQuaternion(Quaternion q)
    {
        // Extract pitch, yaw, roll from quaternion
        // Normalize first
        Quaternion n = Quaternion.Normalize(q);

        // Yaw (Y)
        float siny_cosp = 2.0f * (n.W * n.Y + n.X * n.Z);
        float cosy_cosp = 1.0f - 2.0f * (n.X * n.X + n.Y * n.Y);
        float yaw = MathF.Atan2(siny_cosp, cosy_cosp);

        // Pitch (X)
        float sinp = 2.0f * (n.W * n.X - n.Y * n.Z);
        float pitch = MathF.Abs(sinp) >= 1.0f ? MathF.CopySign(MathF.PI / 2.0f, sinp) : MathF.Asin(sinp);

        // Roll (Z)
        float sinr_cosp = 2.0f * (n.W * n.Z + n.X * n.Y);
        float cosr_cosp = 1.0f - 2.0f * (n.X * n.X + n.Z * n.Z);
        float roll = MathF.Atan2(sinr_cosp, cosr_cosp);

        RotationEulerDegrees = new Vector3(pitch * RadToDeg, yaw * RadToDeg, roll * RadToDeg);
    }

    /// <summary>
    /// Returns the local 4x4 matrix (Scale * Rotation * Translation).
    /// </summary>
    public Matrix4x4 GetLocalMatrix()
    {
        Matrix4x4 s = Matrix4x4.CreateScale(Scale);
        Matrix4x4 r = Matrix4x4.CreateFromQuaternion(GetRotationQuaternion());
        Matrix4x4 t = Matrix4x4.CreateTranslation(Position);

        return s * r * t;
    }

    /// <summary>
    /// Returns the inverse of the local 4x4 matrix.
    /// </summary>
    public Matrix4x4 GetInverseLocalMatrix()
    {
        Matrix4x4 mat = GetLocalMatrix();
        if (Matrix4x4.Invert(mat, out Matrix4x4 inv))
        {
            return inv;
        }

        return Matrix4x4.Identity;
    }

    /// <summary>
    /// Transforms a point from local object space into world space.
    /// </summary>
    public Vector3 TransformPointToWorld(Vector3 localPoint)
    {
        return Vector3.Transform(localPoint, GetLocalMatrix());
    }

    /// <summary>
    /// Transforms a point from world space into local object space.
    /// </summary>
    public Vector3 TransformPointToLocal(Vector3 worldPoint)
    {
        return Vector3.Transform(worldPoint, GetInverseLocalMatrix());
    }

    /// <summary>
    /// Transforms a direction vector from local space into world space (ignoring translation).
    /// </summary>
    public Vector3 TransformDirectionToWorld(Vector3 localDir)
    {
        return Vector3.TransformNormal(localDir, GetLocalMatrix());
    }

    /// <summary>
    /// Transforms a direction vector from world space into local space (ignoring translation).
    /// </summary>
    public Vector3 TransformDirectionToLocal(Vector3 worldDir)
    {
        return Vector3.TransformNormal(worldDir, GetInverseLocalMatrix());
    }

    public Transform3D Clone()
    {
        return new Transform3D(Position, RotationEulerDegrees, Scale);
    }
}
