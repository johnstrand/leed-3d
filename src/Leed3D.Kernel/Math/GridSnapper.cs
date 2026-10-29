using System.Numerics;

namespace Leed3D.Kernel.Math;

/// <summary>
/// Utility for power-of-two grid and angle snapping.
/// </summary>
public static class GridSnapper
{
    public static readonly float[] DefaultGridSteps =
    [
        0.125f, 0.25f, 0.5f, 1.0f, 2.0f, 4.0f, 8.0f, 16.0f, 32.0f, 64.0f
    ];

    public static readonly float[] DefaultAngleSnapPresets =
    [
        1.0f, 5.0f, 15.0f, 30.0f, 45.0f, 90.0f
    ];

    /// <summary>
    /// Snaps a scalar value to the nearest multiple of step.
    /// Uses MidpointRounding.AwayFromZero for consistent symmetric snapping.
    /// </summary>
    public static float SnapScalar(float value, float step)
    {
        if (step <= 0.0f || float.IsNaN(step) || float.IsInfinity(step))
        {
            return value;
        }

        double val = value;
        double st = step;
        double snapped = System.Math.Round(val / st, MidpointRounding.AwayFromZero) * st;
        return (float)snapped;
    }

    /// <summary>
    /// Snaps the specified components of a 3D vector to the nearest multiple of step.
    /// </summary>
    public static Vector3 SnapVector(
        Vector3 value,
        float step,
        bool snapX = true,
        bool snapY = true,
        bool snapZ = true)
    {
        return new Vector3(
            snapX ? SnapScalar(value.X, step) : value.X,
            snapY ? SnapScalar(value.Y, step) : value.Y,
            snapZ ? SnapScalar(value.Z, step) : value.Z);
    }

    /// <summary>
    /// Snaps an angle in degrees to the nearest multiple of stepDegrees.
    /// </summary>
    public static float SnapAngleDegrees(float degrees, float stepDegrees)
    {
        return SnapScalar(degrees, stepDegrees);
    }
}
