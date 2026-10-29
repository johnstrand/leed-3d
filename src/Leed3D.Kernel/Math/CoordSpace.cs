using System.Numerics;

namespace Leed3D.Kernel.Math;

/// <summary>
/// Centralized adapter between Leed3D Editor / Kernel Left-Handed space
/// (+X Right, +Y Up, +Z Away / Forward) and Raylib / OpenGL / glTF Right-Handed space
/// (+X Right, +Y Up, -Z Forward / +Z Toward Viewer).
/// </summary>
public static class CoordSpace
{
    private static readonly Matrix4x4 ZFlipMatrix = Matrix4x4.CreateScale(1.0f, 1.0f, -1.0f);

    /// <summary>
    /// Converts a position or direction vector from editor left-handed space to right-handed space.
    /// </summary>
    public static Vector3 ToRightHanded(Vector3 editorVec)
    {
        return new Vector3(editorVec.X, editorVec.Y, -editorVec.Z);
    }

    /// <summary>
    /// Converts a position or direction vector from right-handed space to editor left-handed space.
    /// </summary>
    public static Vector3 ToEditorSpace(Vector3 rightHandedVec)
    {
        return new Vector3(rightHandedVec.X, rightHandedVec.Y, -rightHandedVec.Z);
    }

    /// <summary>
    /// Converts a rotation quaternion from editor left-handed space to right-handed space.
    /// </summary>
    public static Quaternion ToRightHandedRotation(Quaternion editorRot)
    {
        return new Quaternion(-editorRot.X, -editorRot.Y, editorRot.Z, editorRot.W);
    }

    /// <summary>
    /// Converts a rotation quaternion from right-handed space to editor left-handed space.
    /// </summary>
    public static Quaternion ToEditorRotation(Quaternion rightHandedRot)
    {
        return new Quaternion(-rightHandedRot.X, -rightHandedRot.Y, rightHandedRot.Z, rightHandedRot.W);
    }

    /// <summary>
    /// Converts a 4x4 transform matrix from editor space to right-handed space:
    /// S_z * editorMatrix * S_z where S_z = CreateScale(1, 1, -1).
    /// </summary>
    public static Matrix4x4 ToRightHandedMatrix(Matrix4x4 editorMatrix)
    {
        return ZFlipMatrix * editorMatrix * ZFlipMatrix;
    }

    /// <summary>
    /// Converts a 4x4 transform matrix from right-handed space to editor space.
    /// </summary>
    public static Matrix4x4 ToEditorMatrix(Matrix4x4 rightHandedMatrix)
    {
        return ZFlipMatrix * rightHandedMatrix * ZFlipMatrix;
    }

    /// <summary>
    /// Flips triangle index winding order (i0, i1, i2) -> (i0, i2, i1) to preserve
    /// outward surface normal direction when the Z axis is negated.
    /// </summary>
    public static (int I0, int I1, int I2) FlipTriangleWinding(int i0, int i1, int i2)
    {
        return (i0, i2, i1);
    }
}
