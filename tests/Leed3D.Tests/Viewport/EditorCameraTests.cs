using System.Numerics;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Viewport;

namespace Leed3D.Tests.Viewport;

public class EditorCameraTests
{
    private static readonly Vector2 TestViewportSize = new(1000.0f, 800.0f);

    [Fact]
    public void Perspective_CenterScreenPointToRay_MatchesCameraForward()
    {
        var cam = new EditorCamera
        {
            Position = new Vector3(0.0f, 2.0f, -10.0f),
            YawDegrees = 0.0f,
            PitchDegrees = 0.0f,
            FovYDegrees = 60.0f
        };

        // At Yaw=0, Pitch=0 in Left-Handed (+X Right, +Y Up, +Z Away), Forward is (0, 0, +1)
        Assert.True(Vector3.Distance(cam.GetForward(), Vector3.UnitZ) < 1e-5f);
        Assert.True(Vector3.Distance(cam.GetRight(), Vector3.UnitX) < 1e-5f);
        Assert.True(Vector3.Distance(cam.GetUp(), Vector3.UnitY) < 1e-5f);

        Vector2 centerMouse = TestViewportSize * 0.5f;
        Ray3D centerRay = cam.ScreenPointToRay(centerMouse, TestViewportSize);

        Assert.Equal(cam.Position, centerRay.Origin);
        Assert.True(Vector3.Distance(centerRay.Direction, Vector3.UnitZ) < 1e-5f);

        // Moving mouse right and up from center should angle the ray toward +X and +Y
        Ray3D topRightRay = cam.ScreenPointToRay(new Vector2(800.0f, 200.0f), TestViewportSize);
        Assert.True(topRightRay.Direction.X > 0.1f);
        Assert.True(topRightRay.Direction.Y > 0.1f);
        Assert.True(topRightRay.Direction.Z > 0.5f);
    }

    [Fact]
    public void OrthoY_TopView_ScreenPointToRay_ProjectsOntoXZPlane()
    {
        var cam = new EditorCamera
        {
            Target = Vector3.Zero,
            OrthoSize = 16.0f
        };
        cam.SetProjectionMode(ViewportProjectionMode.OrthoY);

        // OrthoY looks down along -Y, with Right = +X and Up = +Z (away)
        Assert.Equal(-Vector3.UnitY, cam.GetForward());
        Assert.Equal(Vector3.UnitX, cam.GetRight());
        Assert.Equal(Vector3.UnitZ, cam.GetUp());

        // Center ray hits (0, 0, 0) on the XZ ground plane
        Plane3D groundPlane = Plane3D.CreateFromPointAndNormal(Vector3.Zero, Vector3.UnitY);
        Ray3D centerRay = cam.ScreenPointToRay(TestViewportSize * 0.5f, TestViewportSize);

        Assert.True(centerRay.IntersectPlane(groundPlane, out _, out Vector3 centerHit));
        Assert.True(Vector3.Distance(centerHit, Vector3.Zero) < 1e-4f);

        // Top-right corner of viewport (X=1000, Y=0) in 1000x800 with OrthoSize=16 (aspect 1.25):
        // HalfHeight = 8, HalfWidth = 10 -> hits (+10, 0, +8) on XZ plane!
        Ray3D topRightRay = cam.ScreenPointToRay(new Vector2(1000.0f, 0.0f), TestViewportSize);
        Assert.True(topRightRay.IntersectPlane(groundPlane, out _, out Vector3 cornerHit));
        Assert.True(Vector3.Distance(cornerHit, new Vector3(10.0f, 0.0f, 8.0f)) < 1e-4f);
    }

    [Fact]
    public void OrthoZ_FrontView_ScreenPointToRay_ProjectsOntoXYPlane()
    {
        var cam = new EditorCamera
        {
            Target = Vector3.Zero,
            OrthoSize = 16.0f
        };
        cam.SetProjectionMode(ViewportProjectionMode.OrthoZ);

        // OrthoZ looks forward along +Z, with Right = +X and Up = +Y
        Assert.Equal(Vector3.UnitZ, cam.GetForward());
        Assert.Equal(Vector3.UnitX, cam.GetRight());
        Assert.Equal(Vector3.UnitY, cam.GetUp());

        Plane3D xyPlane = Plane3D.CreateFromPointAndNormal(Vector3.Zero, Vector3.UnitZ);
        Ray3D topRightRay = cam.ScreenPointToRay(new Vector2(1000.0f, 0.0f), TestViewportSize);

        Assert.True(topRightRay.IntersectPlane(xyPlane, out _, out Vector3 hit));
        Assert.True(Vector3.Distance(hit, new Vector3(10.0f, 8.0f, 0.0f)) < 1e-4f);
    }

    [Fact]
    public void OrthoX_RightView_ScreenPointToRay_ProjectsOntoYZPlane()
    {
        var cam = new EditorCamera
        {
            Target = Vector3.Zero,
            OrthoSize = 16.0f
        };
        cam.SetProjectionMode(ViewportProjectionMode.OrthoX);

        // OrthoX looks along -X, with Right = +Z and Up = +Y
        Assert.Equal(-Vector3.UnitX, cam.GetForward());
        Assert.Equal(Vector3.UnitZ, cam.GetRight());
        Assert.Equal(Vector3.UnitY, cam.GetUp());

        Plane3D yzPlane = Plane3D.CreateFromPointAndNormal(Vector3.Zero, Vector3.UnitX);
        Ray3D topRightRay = cam.ScreenPointToRay(new Vector2(1000.0f, 0.0f), TestViewportSize);

        Assert.True(topRightRay.IntersectPlane(yzPlane, out _, out Vector3 hit));
        Assert.True(Vector3.Distance(hit, new Vector3(0.0f, 8.0f, 10.0f)) < 1e-4f);
    }

    [Fact]
    public void WorldToScreenPoint_RoundTripsWithScreenPointToRay()
    {
        var cam = new EditorCamera
        {
            Position = new Vector3(-4.0f, 3.0f, -8.0f),
            YawDegrees = 25.0f,
            PitchDegrees = -15.0f,
            FovYDegrees = 60.0f
        };

        Vector2 expectedScreen = new(320.0f, 240.0f);
        Ray3D ray = cam.ScreenPointToRay(expectedScreen, TestViewportSize);
        Vector3 pointOnRay = ray.GetPoint(12.5f);

        Vector2? projectedScreen = cam.WorldToScreenPoint(pointOnRay, TestViewportSize);
        Assert.NotNull(projectedScreen);
        Assert.True(Vector2.Distance(expectedScreen, projectedScreen.Value) < 1e-3f);
    }

    [Fact]
    public void FreeFlyPanOrbitAndZoom_UpdateCameraStateAccurately()
    {
        var cam = new EditorCamera
        {
            Position = Vector3.Zero,
            YawDegrees = 0.0f,
            PitchDegrees = 0.0f,
            OrbitDistance = 10.0f,
            FlySpeed = 10.0f
        };

        // Fly forward (+Z) for 1 second
        cam.UpdateFreeFly(new Vector3(0.0f, 0.0f, 1.0f), Vector2.Zero, 1.0f, fastFly: false);
        Assert.True(Vector3.Distance(cam.Position, new Vector3(0.0f, 0.0f, 10.0f)) < 1e-4f);

        // Zoom in (positive scroll delta reduces OrbitDistance in Perspective)
        float distBefore = cam.OrbitDistance;
        cam.Zoom(2.0f);
        Assert.True(cam.OrbitDistance < distBefore);

        // Switch to OrthoY and zoom out
        cam.SetProjectionMode(ViewportProjectionMode.OrthoY);
        float orthoBefore = cam.OrthoSize;
        cam.Zoom(-2.0f);
        Assert.True(cam.OrthoSize > orthoBefore);
    }
}
