using System.Numerics;
using Leed3D.Kernel.Math;

namespace Leed3D.Tests.Math;

public class SpatialPrimitivesTests
{
    [Fact]
    public void Plane3D_FromPoints_ComputesCorrectNormalAndDistance()
    {
        Vector3 a = new Vector3(0, 0, 0);
        Vector3 b = new Vector3(1, 0, 0);
        Vector3 c = new Vector3(0, 1, 0);

        Plane3D plane = Plane3D.CreateFromPoints(a, b, c);

        // Points in XY plane; normal should be along Z (+Z or -Z)
        Assert.True(MathF.Abs(plane.Normal.X) < 1e-5f);
        Assert.True(MathF.Abs(plane.Normal.Y) < 1e-5f);
        Assert.True(MathF.Abs(MathF.Abs(plane.Normal.Z) - 1.0f) < 1e-5f);
        Assert.True(MathF.Abs(plane.SignedDistance(a)) < 1e-5f);
    }

    [Fact]
    public void Plane3D_ProjectPoint_ProjectsOntoPlaneSurface()
    {
        Plane3D plane = Plane3D.CreateFromPointAndNormal(new Vector3(0, 5, 0), Vector3.UnitY);
        Vector3 testPoint = new Vector3(2, 10, 4);

        Vector3 projected = plane.ProjectPoint(testPoint);

        Assert.Equal(2.0f, projected.X);
        Assert.Equal(5.0f, projected.Y);
        Assert.Equal(4.0f, projected.Z);
        Assert.True(MathF.Abs(plane.SignedDistance(projected)) < 1e-5f);
    }

    [Fact]
    public void Ray3D_IntersectPlane_FindsHitPoint()
    {
        Plane3D plane = Plane3D.CreateFromPointAndNormal(new Vector3(0, 0, 10), Vector3.UnitZ);
        Ray3D ray = new Ray3D(new Vector3(0, 0, 0), Vector3.UnitZ);

        bool hit = ray.IntersectPlane(plane, out float distance, out Vector3 hitPoint);

        Assert.True(hit);
        Assert.True(MathF.Abs(distance - 10.0f) < 1e-5f);
        Assert.True(Vector3.Distance(hitPoint, new Vector3(0, 0, 10)) < 1e-5f);
    }

    [Fact]
    public void Ray3D_IntersectPlane_BehindRay_ReturnsFalse()
    {
        Plane3D plane = Plane3D.CreateFromPointAndNormal(new Vector3(0, 0, -10), Vector3.UnitZ);
        Ray3D ray = new Ray3D(new Vector3(0, 0, 0), Vector3.UnitZ);

        bool hit = ray.IntersectPlane(plane, out _, out _);

        Assert.False(hit);
    }

    [Fact]
    public void Ray3D_IntersectTriangle_HitsFrontFace()
    {
        Vector3 v0 = new Vector3(-1, -1, 5);
        Vector3 v1 = new Vector3(1, -1, 5);
        Vector3 v2 = new Vector3(0, 1, 5);

        Ray3D ray = new Ray3D(new Vector3(0, 0, 0), Vector3.UnitZ);

        bool hit = ray.IntersectTriangle(v0, v1, v2, cullBackface: false, out float distance, out Vector3 hitPoint);

        Assert.True(hit);
        Assert.True(MathF.Abs(distance - 5.0f) < 1e-5f);
        Assert.True(MathF.Abs(hitPoint.X) < 1e-5f);
        Assert.True(MathF.Abs(hitPoint.Y) < 1e-5f);
        Assert.True(MathF.Abs(hitPoint.Z - 5.0f) < 1e-5f);
    }

    [Fact]
    public void Ray3D_IntersectTriangle_MissesOutside()
    {
        Vector3 v0 = new Vector3(-1, -1, 5);
        Vector3 v1 = new Vector3(1, -1, 5);
        Vector3 v2 = new Vector3(0, 1, 5);

        Ray3D ray = new Ray3D(new Vector3(10, 10, 0), Vector3.UnitZ);

        bool hit = ray.IntersectTriangle(v0, v1, v2, cullBackface: false, out _, out _);

        Assert.False(hit);
    }

    [Fact]
    public void Ray3D_IntersectBoundingBox_HitsCorrectDistance()
    {
        BoundingBox3D box = new BoundingBox3D(new Vector3(-1, -1, 4), new Vector3(1, 1, 6));
        Ray3D ray = new Ray3D(new Vector3(0, 0, 0), Vector3.UnitZ);

        bool hit = ray.IntersectBoundingBox(box, out float tMin, out float tMax);

        Assert.True(hit);
        Assert.True(MathF.Abs(tMin - 4.0f) < 1e-5f);
        Assert.True(MathF.Abs(tMax - 6.0f) < 1e-5f);
    }

    [Fact]
    public void BoundingBox3D_EncapsulateAndTransform_CalculatesBounds()
    {
        BoundingBox3D box = new BoundingBox3D(new Vector3(-1, -1, -1), new Vector3(1, 1, 1));
        Assert.Equal(Vector3.Zero, box.Center);
        Assert.Equal(new Vector3(2, 2, 2), box.Size);
        Assert.Equal(new Vector3(1, 1, 1), box.Extents);

        Matrix4x4 translation = Matrix4x4.CreateTranslation(5, 10, 15);
        BoundingBox3D transformed = box.Transform(translation);

        Assert.Equal(new Vector3(4, 9, 14), transformed.Min);
        Assert.Equal(new Vector3(6, 11, 16), transformed.Max);
    }
}
