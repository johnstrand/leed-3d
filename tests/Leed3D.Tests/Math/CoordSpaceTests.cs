using System.Numerics;
using Leed3D.Kernel.Math;

namespace Leed3D.Tests.Math;

public class CoordSpaceTests
{
    [Fact]
    public void Vector_RoundTrip_PreservesValues()
    {
        Vector3 original = new Vector3(1.5f, -2.5f, 3.75f);
        Vector3 rightHanded = CoordSpace.ToRightHanded(original);
        Vector3 restored = CoordSpace.ToEditorSpace(rightHanded);

        Assert.Equal(original.X, rightHanded.X);
        Assert.Equal(original.Y, rightHanded.Y);
        Assert.Equal(-original.Z, rightHanded.Z);
        Assert.Equal(original, restored);
    }

    [Fact]
    public void Quaternion_RoundTrip_PreservesValues()
    {
        Quaternion original = Quaternion.CreateFromYawPitchRoll(0.5f, 0.25f, -0.75f);
        Quaternion rightHanded = CoordSpace.ToRightHandedRotation(original);
        Quaternion restored = CoordSpace.ToEditorRotation(rightHanded);

        Assert.Equal(-original.X, rightHanded.X);
        Assert.Equal(-original.Y, rightHanded.Y);
        Assert.Equal(original.Z, rightHanded.Z);
        Assert.Equal(original.W, rightHanded.W);
        Assert.Equal(original, restored);
    }

    [Fact]
    public void Matrix_RoundTrip_PreservesValues()
    {
        Matrix4x4 translation = Matrix4x4.CreateTranslation(1.0f, 2.0f, 3.0f);
        Matrix4x4 rotation = Matrix4x4.CreateFromYawPitchRoll(0.2f, 0.4f, 0.6f);
        Matrix4x4 scale = Matrix4x4.CreateScale(2.0f, 1.5f, 0.5f);
        Matrix4x4 original = scale * rotation * translation;

        Matrix4x4 rh = CoordSpace.ToRightHandedMatrix(original);
        Matrix4x4 restored = CoordSpace.ToEditorMatrix(rh);

        Assert.True(MathF.Abs(original.M11 - restored.M11) < 1e-5f);
        Assert.True(MathF.Abs(original.M22 - restored.M22) < 1e-5f);
        Assert.True(MathF.Abs(original.M33 - restored.M33) < 1e-5f);
        Assert.True(MathF.Abs(original.M41 - restored.M41) < 1e-5f);
        Assert.True(MathF.Abs(original.M42 - restored.M42) < 1e-5f);
        Assert.True(MathF.Abs(original.M43 - restored.M43) < 1e-5f);
    }

    [Fact]
    public void TransformingTriangle_WithWindingFlip_PreservesOutwardNormalDirection()
    {
        // Define a triangle in editor space with vertices in clockwise or CCW order
        // Left-handed space (+X right, +Y up, +Z away)
        // Triangle on XY plane facing towards +Z (away):
        // v0 = (0, 0, 0), v1 = (0, 1, 0), v2 = (1, 0, 0)
        // Normal in left-handed or right-handed cross product:
        // Cross(v1 - v0, v2 - v0) = Cross((0, 1, 0), (1, 0, 0)) = (0, 0, -1)
        Vector3 v0 = new Vector3(0, 0, 1);
        Vector3 v1 = new Vector3(0, 2, 1);
        Vector3 v2 = new Vector3(2, 0, 1);

        Vector3 edge1 = v1 - v0;
        Vector3 edge2 = v2 - v0;
        Vector3 editorNormal = Vector3.Normalize(Vector3.Cross(edge1, edge2));

        // Transform vertices to right-handed space
        Vector3 rhV0 = CoordSpace.ToRightHanded(v0);
        Vector3 rhV1 = CoordSpace.ToRightHanded(v1);
        Vector3 rhV2 = CoordSpace.ToRightHanded(v2);

        // Apply winding flip: (0, 1, 2) -> (0, 2, 1)
        var (i0, i1, i2) = CoordSpace.FlipTriangleWinding(0, 1, 2);
        Vector3[] rhVertices = [rhV0, rhV1, rhV2];

        Vector3 w0 = rhVertices[i0];
        Vector3 w1 = rhVertices[i1];
        Vector3 w2 = rhVertices[i2];

        Vector3 rhEdge1 = w1 - w0;
        Vector3 rhEdge2 = w2 - w0;
        Vector3 computedRhNormal = Vector3.Normalize(Vector3.Cross(rhEdge1, rhEdge2));

        Vector3 expectedRhNormal = CoordSpace.ToRightHanded(editorNormal);

        Assert.True(Vector3.Distance(computedRhNormal, expectedRhNormal) < 1e-5f,
            $"Expected RH normal {expectedRhNormal} but got {computedRhNormal}");
    }
}
