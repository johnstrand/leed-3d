using System.Numerics;
using Leed3D.Kernel.Math;

namespace Leed3D.Tests.Math;

public class GridSnapperTests
{
    [Theory]
    [InlineData(0.125f)]
    [InlineData(0.25f)]
    [InlineData(0.5f)]
    [InlineData(1.0f)]
    [InlineData(2.0f)]
    [InlineData(4.0f)]
    [InlineData(8.0f)]
    [InlineData(16.0f)]
    [InlineData(32.0f)]
    [InlineData(64.0f)]
    public void SnapScalar_SnapsPositiveNegativeAndZeroAcrossAllSteps(float step)
    {
        // Zero snaps to zero
        Assert.Equal(0.0f, GridSnapper.SnapScalar(0.0f, step));

        // Exactly on step snaps to itself
        Assert.Equal(step, GridSnapper.SnapScalar(step, step));
        Assert.Equal(-step, GridSnapper.SnapScalar(-step, step));

        // Multiple steps
        Assert.Equal(2 * step, GridSnapper.SnapScalar(2 * step, step));
        Assert.Equal(-3 * step, GridSnapper.SnapScalar(-3 * step, step));

        // Slightly above and below
        float smallOffset = step * 0.1f;
        Assert.Equal(step, GridSnapper.SnapScalar(step + smallOffset, step));
        Assert.Equal(step, GridSnapper.SnapScalar(step - smallOffset, step));
        Assert.Equal(-step, GridSnapper.SnapScalar(-step + smallOffset, step));
        Assert.Equal(-step, GridSnapper.SnapScalar(-step - smallOffset, step));

        // Midpoints snap away from zero
        Assert.Equal(step, GridSnapper.SnapScalar(step * 0.5f, step));
        Assert.Equal(-step, GridSnapper.SnapScalar(-step * 0.5f, step));
    }

    [Fact]
    public void SnapVector_SnapsMaskedComponents()
    {
        Vector3 input = new Vector3(1.1f, 2.7f, 3.4f);
        Vector3 snappedAll = GridSnapper.SnapVector(input, 1.0f);

        Assert.Equal(new Vector3(1.0f, 3.0f, 3.0f), snappedAll);

        Vector3 snapOnlyY = GridSnapper.SnapVector(input, 1.0f, snapX: false, snapY: true, snapZ: false);
        Assert.Equal(new Vector3(1.1f, 3.0f, 3.4f), snapOnlyY);
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(5.0f)]
    [InlineData(15.0f)]
    [InlineData(30.0f)]
    [InlineData(45.0f)]
    [InlineData(90.0f)]
    public void SnapAngleDegrees_SnapsToPresetSteps(float angleStep)
    {
        Assert.Equal(0.0f, GridSnapper.SnapAngleDegrees(0.0f, angleStep));
        Assert.Equal(angleStep, GridSnapper.SnapAngleDegrees(angleStep, angleStep));
        Assert.Equal(2 * angleStep, GridSnapper.SnapAngleDegrees(2 * angleStep, angleStep));
        Assert.Equal(-angleStep, GridSnapper.SnapAngleDegrees(-angleStep, angleStep));

        float offset = angleStep * 0.2f;
        Assert.Equal(angleStep, GridSnapper.SnapAngleDegrees(angleStep + offset, angleStep));
        Assert.Equal(angleStep, GridSnapper.SnapAngleDegrees(angleStep - offset, angleStep));
    }
}
