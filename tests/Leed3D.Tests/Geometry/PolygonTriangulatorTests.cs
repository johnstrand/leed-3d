using System.Numerics;
using Leed3D.Kernel.Geometry;

namespace Leed3D.Tests.Geometry;

public class PolygonTriangulatorTests
{
    private static float ComputeTriangleArea(Vector3 a, Vector3 b, Vector3 c)
    {
        return 0.5f * Vector3.Cross(b - a, c - a).Length();
    }

    private static Vector3 ComputeTriangleNormal(Vector3 a, Vector3 b, Vector3 c)
    {
        return Vector3.Normalize(Vector3.Cross(b - a, c - a));
    }

    [Fact]
    public void TriangulateFace_Triangle_ReturnsSingleTriangle()
    {
        var mesh = new EditableMesh();
        int v0 = mesh.AddVertex(new Vector3(0, 0, 0));
        int v1 = mesh.AddVertex(new Vector3(2, 0, 0));
        int v2 = mesh.AddVertex(new Vector3(0, 2, 0));
        int fId = mesh.AddFace([v0, v1, v2]);

        PolygonFace face = mesh.GetFace(fId)!;
        var tris = PolygonTriangulator.TriangulateFace(face, mesh);

        Assert.Single(tris);
        Assert.Equal((v0, v1, v2), tris[0]);
    }

    [Fact]
    public void TriangulateFace_ConvexQuad_SplitsAlongShorterDiagonal()
    {
        var mesh = new EditableMesh();
        // Skewed quad where diagonal (v0, v2) is shorter than (v1, v3)
        int v0 = mesh.AddVertex(new Vector3(0, 0, 0));
        int v1 = mesh.AddVertex(new Vector3(3, -1, 0));
        int v2 = mesh.AddVertex(new Vector3(2, 2, 0));
        int v3 = mesh.AddVertex(new Vector3(-1, 3, 0));
        int fId = mesh.AddFace([v0, v1, v2, v3]);

        PolygonFace face = mesh.GetFace(fId)!;
        var tris = PolygonTriangulator.TriangulateFace(face, mesh);

        Assert.Equal(2, tris.Count);
        // Shorter diagonal is v0(0,0,0) <-> v2(2,2,0) (distSq = 8) vs v1(3,-1,0) <-> v3(-1,3,0) (distSq = 32)
        Assert.Contains((v0, v1, v2), tris);
        Assert.Contains((v0, v2, v3), tris);
    }

    [Fact]
    public void TriangulateFace_ConvexOctagon_ProducesSixConsistentTriangles()
    {
        var mesh = new EditableMesh();
        var vIds = new List<int>(8);
        const float radius = 2.5f;

        // Octagon on the XZ plane facing +Y
        for (int i = 0; i < 8; i++)
        {
            float angle = i * MathF.Tau / 8.0f;
            // For +Y normal: Cross(b - a, c - a) points +Y when ordered in (Z, X) or counter-clockwise around +Y
            float x = MathF.Sin(angle) * radius;
            float z = MathF.Cos(angle) * radius;
            vIds.Add(mesh.AddVertex(new Vector3(x, 0.0f, z)));
        }

        int fId = mesh.AddFace(vIds);
        PolygonFace face = mesh.GetFace(fId)!;
        Vector3 faceNormal = face.ComputeNormal(mesh);

        Assert.True(Vector3.Distance(faceNormal, Vector3.UnitY) < 1e-5f);

        var tris = PolygonTriangulator.TriangulateFace(face, mesh);
        Assert.Equal(6, tris.Count);

        foreach (var (t0, t1, t2) in tris)
        {
            Vector3 p0 = mesh.GetVertex(t0)!.Position;
            Vector3 p1 = mesh.GetVertex(t1)!.Position;
            Vector3 p2 = mesh.GetVertex(t2)!.Position;

            Vector3 triNormal = ComputeTriangleNormal(p0, p1, p2);
            Assert.True(Vector3.Dot(triNormal, faceNormal) > 0.999f);
        }
    }

    [Fact]
    public void TriangulateFace_ConcaveLShape_ProducesValidNonOverlappingTrianglesMatchingAreaAndNormal()
    {
        var mesh = new EditableMesh();

        // 6-vertex L-shaped concave polygon on XY plane (Z = 0)
        // Outer width = 4, outer height = 4, inner cutout from (2,2) to (4,4)
        // Expected area = (4 * 2) + (2 * 2) = 12
        int v0 = mesh.AddVertex(new Vector3(0, 0, 0));
        int v1 = mesh.AddVertex(new Vector3(4, 0, 0));
        int v2 = mesh.AddVertex(new Vector3(4, 2, 0));
        int v3 = mesh.AddVertex(new Vector3(2, 2, 0)); // Reflex (concave) interior corner!
        int v4 = mesh.AddVertex(new Vector3(2, 4, 0));
        int v5 = mesh.AddVertex(new Vector3(0, 4, 0));

        int fId = mesh.AddFace([v0, v1, v2, v3, v4, v5]);
        PolygonFace face = mesh.GetFace(fId)!;
        Vector3 faceNormal = face.ComputeNormal(mesh);

        // Newell normal for (0,0)->(4,0)->(4,2)->(2,2)->(2,4)->(0,4) in XY plane is (0, 0, +1)
        Assert.True(Vector3.Distance(faceNormal, Vector3.UnitZ) < 1e-5f);

        var tris = PolygonTriangulator.TriangulateFace(face, mesh);
        Assert.Equal(4, tris.Count);

        float totalTriArea = 0.0f;
        foreach (var (t0, t1, t2) in tris)
        {
            Vector3 p0 = mesh.GetVertex(t0)!.Position;
            Vector3 p1 = mesh.GetVertex(t1)!.Position;
            Vector3 p2 = mesh.GetVertex(t2)!.Position;

            float area = ComputeTriangleArea(p0, p1, p2);
            Assert.True(area > 1e-4f, "Triangle should have non-zero area.");
            totalTriArea += area;

            Vector3 triNormal = ComputeTriangleNormal(p0, p1, p2);
            Assert.True(Vector3.Dot(triNormal, faceNormal) > 0.999f,
                $"Triangle ({t0}, {t1}, {t2}) normal {triNormal} does not match face normal {faceNormal}");
        }

        // If any ear clipped across the concave exterior cutout, total area would exceed 12 (e.g., 14 or 16)!
        Assert.True(MathF.Abs(totalTriArea - 12.0f) < 1e-4f,
            $"Expected L-shape area 12.0, but got {totalTriArea}");
    }

    [Fact]
    public void TriangulateFace_ConcaveUShapeDoorwayWall_ProducesExactAreaAndNormalsAcrossAllPlanes()
    {
        // 8-vertex U-shaped doorway wall on YZ plane (X = 5, facing -X)
        // Wall is 6 units wide (Z in 0..6) and 4 units tall (Y in 0..4), with a 2x3 doorway cutout (Z in 2..4, Y in 0..3)
        // Expected area = (6 * 4) - (2 * 3) = 24 - 6 = 18
        var mesh = new EditableMesh();
        int v0 = mesh.AddVertex(new Vector3(5, 0, 0));
        int v1 = mesh.AddVertex(new Vector3(5, 0, 2));
        int v2 = mesh.AddVertex(new Vector3(5, 3, 2)); // Reflex corner 1
        int v3 = mesh.AddVertex(new Vector3(5, 3, 4)); // Reflex corner 2
        int v4 = mesh.AddVertex(new Vector3(5, 0, 4));
        int v5 = mesh.AddVertex(new Vector3(5, 0, 6));
        int v6 = mesh.AddVertex(new Vector3(5, 4, 6));
        int v7 = mesh.AddVertex(new Vector3(5, 4, 0));

        int fId = mesh.AddFace([v0, v1, v2, v3, v4, v5, v6, v7]);
        PolygonFace face = mesh.GetFace(fId)!;
        Vector3 faceNormal = face.ComputeNormal(mesh);

        var tris = PolygonTriangulator.TriangulateFace(face, mesh);
        Assert.Equal(6, tris.Count);

        float totalArea = 0.0f;
        foreach (var (t0, t1, t2) in tris)
        {
            Vector3 p0 = mesh.GetVertex(t0)!.Position;
            Vector3 p1 = mesh.GetVertex(t1)!.Position;
            Vector3 p2 = mesh.GetVertex(t2)!.Position;

            totalArea += ComputeTriangleArea(p0, p1, p2);
            Vector3 triNormal = ComputeTriangleNormal(p0, p1, p2);
            Assert.True(Vector3.Dot(triNormal, faceNormal) > 0.999f);
        }

        Assert.True(MathF.Abs(totalArea - 18.0f) < 1e-4f,
            $"Expected doorway wall area 18.0, but got {totalArea}");
    }
}
