using System.Numerics;

namespace Leed3D.Kernel.Modifiers;

/// <summary>
/// 3D coordinate axis for symmetry, mirror reflection, and radial repetition.
/// </summary>
public enum Axis3D
{
    X,
    Y,
    Z
}

/// <summary>
/// Extension methods for <see cref="Axis3D"/>.
/// </summary>
public static class Axis3DExtensions
{
    /// <summary>
    /// Returns the normalized unit vector for this axis.
    /// </summary>
    public static Vector3 ToVector3(this Axis3D axis) => axis switch
    {
        Axis3D.X => Vector3.UnitX,
        Axis3D.Y => Vector3.UnitY,
        Axis3D.Z => Vector3.UnitZ,
        _ => Vector3.UnitY
    };
}
