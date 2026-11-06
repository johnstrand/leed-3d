using System.Numerics;
using Leed3D.Kernel.Gizmos;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Primitives;
using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Selection;

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

    [Fact]
    public void SceneSnapQuery_SnapsToVerticesEdgesMidpointsAndFaces_ExcludingSelectedElements()
    {
        var scene = new EditorScene("SnapScene");
        var selection = new SelectionManager();

        // Target cube at (0, 0, 0) with half-extents 1 (vertices at +-1, +-1, +-1)
        SceneObject targetObj = scene.AddMeshObject("TargetCube", PrimitiveGenerator.CreateBox(new Vector3(2f, 2f, 2f)));
        // Moving cube at (5, 0, 0) - selected in Object Mode so it must be excluded from self-snapping
        SceneObject movingObj = scene.AddMeshObject("MovingCube", PrimitiveGenerator.CreateBox(new Vector3(2f, 2f, 2f)));
        movingObj.Transform.Position = new Vector3(5f, 0f, 0f);
        selection.SelectSingleObject(movingObj.Id);

        // 1. Vertex snap near (1.05, 0.95, 1.02) -> should snap to (1, 1, 1) on targetObj
        bool foundVert = SceneSnapQuery.TryFindSnapTarget(
            scene,
            selection,
            SnapTargetMode.Vertex,
            new Vector3(1.05f, 0.95f, 1.02f),
            cursorRay: null,
            maxSnapDistanceWorld: 0.5f,
            out SnapTargetHit vertHit);

        Assert.True(foundVert);
        Assert.Equal(targetObj.Id, vertHit.ObjectId);
        Assert.Equal(new Vector3(1f, 1f, 1f), vertHit.WorldPosition);

        // Verify selected movingObj is excluded from snapping even when querying right on its vertex (6, 1, 1)
        bool foundSelf = SceneSnapQuery.TryFindSnapTarget(
            scene,
            selection,
            SnapTargetMode.Vertex,
            new Vector3(6.01f, 1.0f, 1.0f),
            cursorRay: null,
            maxSnapDistanceWorld: 0.5f,
            out _);
        Assert.False(foundSelf);

        // 2. Edge midpoint snap near (0.02, 1.01, 1.0) -> midpoint of top-front edge (-1,1,1)-(1,1,1) is (0, 1, 1)
        bool foundEdgeMid = SceneSnapQuery.TryFindSnapTarget(
            scene,
            selection,
            SnapTargetMode.Edge,
            new Vector3(0.02f, 1.01f, 1.0f),
            cursorRay: null,
            maxSnapDistanceWorld: 0.5f,
            out SnapTargetHit edgeHit);

        Assert.True(foundEdgeMid);
        Assert.True(edgeHit.IsEdgeMidpoint);
        Assert.Equal(new Vector3(0f, 1f, 1f), edgeHit.WorldPosition);

        // 3. Face surface snap via cursor ray aimed at +Y face (Y = 1) at X = 0.25, Z = -0.5
        var ray = new Ray3D(new Vector3(0.25f, 5.0f, -0.5f), new Vector3(0f, -1f, 0f));
        bool foundFace = SceneSnapQuery.TryFindSnapTarget(
            scene,
            selection,
            SnapTargetMode.Face,
            new Vector3(0.25f, 1.2f, -0.5f),
            cursorRay: ray,
            maxSnapDistanceWorld: 1.0f,
            out SnapTargetHit faceHit);

        Assert.True(foundFace);
        Assert.Equal(targetObj.Id, faceHit.ObjectId);
        Assert.True(Vector3.Distance(new Vector3(0.25f, 1.0f, -0.5f), faceHit.WorldPosition) < 1e-4f);
        Assert.True(Vector3.Distance(Vector3.UnitY, faceHit.WorldNormal) < 1e-4f);

        // 4. Constrained snap delta respects single-axis and dual-axis constraints
        Vector3 constrainedX = SceneSnapQuery.ComputeConstrainedSnapDelta(
            new Vector3(5f, 0f, 0f),
            new Vector3(1f, 1f, 1f),
            GizmoAxisConstraint.X);
        Assert.Equal(new Vector3(-4f, 0f, 0f), constrainedX);
    }
}
