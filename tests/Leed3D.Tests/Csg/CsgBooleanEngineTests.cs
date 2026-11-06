using System.Numerics;
using Leed3D.Kernel.Csg;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Primitives;
using Leed3D.Kernel.Scene;

namespace Leed3D.Tests.Csg;

public sealed class CsgBooleanEngineTests
{
    private static void SetAllFaceMaterials(EditableMesh mesh, string materialId)
    {
        for (int i = 0; i < mesh.Faces.Count; i++)
        {
            mesh.Faces[i].MaterialId = materialId;
        }
    }

    private static float ComputeMeshSurfaceArea(EditableMesh mesh)
    {
        IReadOnlyList<(int FaceId, int V0, int V1, int V2)> tris = PolygonTriangulator.TriangulateMesh(mesh);
        float totalArea = 0.0f;

        for (int i = 0; i < tris.Count; i++)
        {
            (_, int v0, int v1, int v2) = tris[i];
            Vector3 p0 = mesh.GetVertex(v0)!.Position;
            Vector3 p1 = mesh.GetVertex(v1)!.Position;
            Vector3 p2 = mesh.GetVertex(v2)!.Position;
            totalArea += 0.5f * Vector3.Cross(p1 - p0, p2 - p0).Length();
        }

        return totalArea;
    }

    [Fact]
    public void BspNode_ClassifyAndSplitPolygon_SplitsSpanningQuadIntoTwoQuadsPreservingMaterial()
    {
        var verts = new List<Vector3>
        {
            new(-2.0f, -1.0f, 0.0f),
            new( 2.0f, -1.0f, 0.0f),
            new( 2.0f,  1.0f, 0.0f),
            new(-2.0f,  1.0f, 0.0f)
        };

        var poly = new CsgPolygon(verts, "brick_wall");
        var splitPlane = new Plane3D(Vector3.UnitX, 0.0f); // X = 0

        PolygonPlaneRelation relation = BspNode.ClassifyPolygon(splitPlane, poly);
        Assert.Equal(PolygonPlaneRelation.Spanning, relation);

        var coplanarFront = new List<CsgPolygon>();
        var coplanarBack = new List<CsgPolygon>();
        var front = new List<CsgPolygon>();
        var back = new List<CsgPolygon>();

        BspNode.SplitPolygon(splitPlane, poly, coplanarFront, coplanarBack, front, back);

        Assert.Empty(coplanarFront);
        Assert.Empty(coplanarBack);
        Assert.Single(front);
        Assert.Single(back);

        Assert.Equal("brick_wall", front[0].MaterialId);
        Assert.Equal("brick_wall", back[0].MaterialId);
        Assert.Equal(4, front[0].Vertices.Count);
        Assert.Equal(4, back[0].Vertices.Count);

        // Front half should have X >= 0, Back half should have X <= 0
        Assert.All(front[0].Vertices, v => Assert.True(v.X >= -1e-4f));
        Assert.All(back[0].Vertices, v => Assert.True(v.X <= 1e-4f));

        // Combined area of split halves must equal original area (4 * 2 = 8)
        float areaFront = 0.5f * CsgPolygon.ComputeNewellVector(front[0].Vertices).Length();
        float areaBack = 0.5f * CsgPolygon.ComputeNewellVector(back[0].Vertices).Length();
        Assert.Equal(4.0f, areaFront, 3);
        Assert.Equal(4.0f, areaBack, 3);
    }

    [Fact]
    public void BspNode_CoplanarClassification_DistinguishesCoplanarFrontAndBack()
    {
        var verts = new List<Vector3>
        {
            new(-1.0f, 0.0f, -1.0f),
            new(-1.0f, 0.0f,  1.0f),
            new( 1.0f, 0.0f,  1.0f),
            new( 1.0f, 0.0f, -1.0f)
        };

        var poly = new CsgPolygon(verts, "floor");
        Plane3D samePlane = poly.Plane;
        Plane3D oppositePlane = new(-samePlane.Normal, -samePlane.D);

        Assert.Equal(PolygonPlaneRelation.CoplanarFront, BspNode.ClassifyPolygon(samePlane, poly));
        Assert.Equal(PolygonPlaneRelation.CoplanarBack, BspNode.ClassifyPolygon(oppositePlane, poly));
    }

    [Fact]
    public void Subtract_DoorwayBoxFromWallBox_CarvesDoorwayAndPreservesCutterMaterial()
    {
        // Wall: 10m wide (X: -5..5), 4m tall (Y: -2..2 -> shifted to Y: 0..4), 0.5m thick (Z: -0.25..0.25)
        EditableMesh wallMesh = PrimitiveGenerator.CreateBox(new Vector3(10.0f, 4.0f, 0.5f));
        SetAllFaceMaterials(wallMesh, "wall_plaster");
        var wallTransform = new Transform3D { Position = new Vector3(0.0f, 2.0f, 0.0f) };

        // Doorway cutter: 2m wide (X: -1..1), 2.5m tall (Y: 0..2.5), 2m deep (Z: -1..1)
        EditableMesh doorMesh = PrimitiveGenerator.CreateBox(new Vector3(2.0f, 2.5f, 2.0f));
        SetAllFaceMaterials(doorMesh, "door_jamb");
        var doorTransform = new Transform3D { Position = new Vector3(0.0f, 1.25f, 0.0f) };

        EditableMesh carved = CsgBooleanEngine.Evaluate(
            wallMesh, wallTransform,
            doorMesh, doorTransform,
            CsgOperation.Subtract);

        Assert.True(carved.FaceCount > 0);
        Assert.True(carved.VertexCount > 0);

        // Both wall material and cutter material on inner doorway jambs/lintel must be present
        Assert.Contains(carved.Faces, f => f.MaterialId == "wall_plaster");
        Assert.Contains(carved.Faces, f => f.MaterialId == "door_jamb");

        // Inner doorway faces (left jamb X=-1, right jamb X=1, top lintel Y=2.5 in world -> Y=0.5 in wall local)
        var jambFaces = carved.Faces.Where(f => f.MaterialId == "door_jamb").ToList();
        Assert.Equal(3, jambFaces.Count);

        // Front and back wall faces should be merged into clean U-shaped 8-vertex N-gons
        var frontAndBackFaces = carved.Faces
            .Where(f => f.MaterialId == "wall_plaster" && MathF.Abs(f.ComputeNormal(carved).Z) > 0.99f)
            .ToList();
        Assert.Equal(2, frontAndBackFaces.Count);
        Assert.All(frontAndBackFaces, f => Assert.Equal(8, f.VertexIds.Count));

        // Analytical surface area check:
        // Original wall area: 2*(10*4) + 2*(10*0.5) + 2*(4*0.5) = 80 + 10 + 4 = 94
        // Minus front/back doorway opening: -2*(2*2.5) = -10
        // Minus bottom doorway footprint: -(2*0.5) = -1
        // Plus 2 doorway jambs + 1 lintel inside the 0.5m wall: 2*(2.5*0.5) + (2*0.5) = 2.5 + 1.0 = +3.5
        // Expected total area = 94 - 10 - 1 + 3.5 = 86.5
        float actualArea = ComputeMeshSurfaceArea(carved);
        Assert.Equal(86.5f, actualArea, 2);

        // Every face must have a valid unit normal and triangulate cleanly
        foreach (PolygonFace face in carved.Faces)
        {
            Vector3 n = face.ComputeNormal(carved);
            Assert.Equal(1.0f, n.Length(), 3);
            IReadOnlyList<(int V0, int V1, int V2)> tris = PolygonTriangulator.TriangulateFace(face, carved);
            Assert.NotEmpty(tris);
        }
    }

    [Fact]
    public void Subtract_WindowBoxFromWallBox_CarvesCenteredOpeningAndTriangulatesCleanly()
    {
        // Wall: 10 x 4 x 0.5 at origin
        EditableMesh wallMesh = PrimitiveGenerator.CreateBox(new Vector3(10.0f, 4.0f, 0.5f));
        SetAllFaceMaterials(wallMesh, "brick");

        // Window cutter: 2 x 1.5 x 2 centered at origin (pierces strictly through the middle of the wall)
        EditableMesh windowMesh = PrimitiveGenerator.CreateBox(new Vector3(2.0f, 1.5f, 2.0f));
        SetAllFaceMaterials(windowMesh, "window_frame");

        EditableMesh carved = CsgBooleanEngine.Evaluate(wallMesh, windowMesh, CsgOperation.Subtract);

        // 4 inner window reveal faces (left, right, top, bottom) should carry "window_frame"
        var frameFaces = carved.Faces.Where(f => f.MaterialId == "window_frame").ToList();
        Assert.Equal(4, frameFaces.Count);

        // Analytical surface area check:
        // Original wall area: 94.0
        // Minus front and back window opening: -2 * (2.0 * 1.5) = -6.0
        // Plus 4 inner window reveal faces of depth 0.5: +2*(2.0*0.5) + 2*(1.5*0.5) = +2.0 + 1.5 = +3.5
        // Expected area = 94.0 - 6.0 + 3.5 = 91.5
        float actualArea = ComputeMeshSurfaceArea(carved);
        Assert.Equal(91.5f, actualArea, 2);

        IReadOnlyList<(int FaceId, int V0, int V1, int V2)> allTris = PolygonTriangulator.TriangulateMesh(carved);
        Assert.NotEmpty(allTris);
    }

    [Fact]
    public void Subtract_ChainedDoorwayAndWindowOnSameWall_HandlesConcaveFacesCleanly()
    {
        // Wall: 12 x 4 x 0.5 centered at (0, 2, 0)
        EditableMesh wallMesh = PrimitiveGenerator.CreateBox(new Vector3(12.0f, 4.0f, 0.5f));
        SetAllFaceMaterials(wallMesh, "wall");
        var wallTransform = new Transform3D { Position = new Vector3(0.0f, 2.0f, 0.0f) };

        // 1st subtraction: Doorway on the left (X: -4..-2, Y: 0..2.5)
        EditableMesh doorMesh = PrimitiveGenerator.CreateBox(new Vector3(2.0f, 2.5f, 2.0f));
        SetAllFaceMaterials(doorMesh, "door_trim");
        var doorTransform = new Transform3D { Position = new Vector3(-3.0f, 1.25f, 0.0f) };

        EditableMesh afterDoor = CsgBooleanEngine.Evaluate(
            wallMesh, wallTransform,
            doorMesh, doorTransform,
            CsgOperation.Subtract);

        // 2nd subtraction on the already-carved concave mesh: Window on the right (X: 1..4, Y: 1..2.5)
        EditableMesh windowMesh = PrimitiveGenerator.CreateBox(new Vector3(3.0f, 1.5f, 2.0f));
        SetAllFaceMaterials(windowMesh, "window_trim");
        var windowTransform = new Transform3D { Position = new Vector3(2.5f, 1.75f, 0.0f) };

        EditableMesh afterWindow = CsgBooleanEngine.Evaluate(
            afterDoor, wallTransform,
            windowMesh, windowTransform,
            CsgOperation.Subtract);

        Assert.Contains(afterWindow.Faces, f => f.MaterialId == "wall");
        Assert.Contains(afterWindow.Faces, f => f.MaterialId == "door_trim");
        Assert.Contains(afterWindow.Faces, f => f.MaterialId == "window_trim");

        // Analytical area:
        // Initial 12x4x0.5 wall: 2*(48) + 2*(6) + 2*(2) = 96 + 12 + 4 = 112.0
        // Doorway delta: -2*(2*2.5) - (2*0.5) + 2*(2.5*0.5) + (2*0.5) = -10 - 1 + 2.5 + 1 = -7.5
        // Window delta: -2*(3*1.5) + 2*(3*0.5) + 2*(1.5*0.5) = -9 + 3 + 1.5 = -4.5
        // Expected area = 112.0 - 7.5 - 4.5 = 100.0
        float actualArea = ComputeMeshSurfaceArea(afterWindow);
        Assert.Equal(100.0f, actualArea, 2);
    }

    [Fact]
    public void Union_TwoOverlappingBoxes_CreatesLShapedRoomAndMergesCoplanarFloorAndCeiling()
    {
        // Wing A: X in [0, 6], Y in [0, 3], Z in [0, 4] (size 6 x 3 x 4, center (3, 1.5, 2))
        EditableMesh boxA = PrimitiveGenerator.CreateBox(new Vector3(6.0f, 3.0f, 4.0f));
        SetAllFaceMaterials(boxA, "concrete");
        var transformA = new Transform3D { Position = new Vector3(3.0f, 1.5f, 2.0f) };

        // Wing B: X in [0, 4], Y in [0, 3], Z in [0, 6] (size 4 x 3 x 6, center (2, 1.5, 3))
        EditableMesh boxB = PrimitiveGenerator.CreateBox(new Vector3(4.0f, 3.0f, 6.0f));
        SetAllFaceMaterials(boxB, "concrete");
        var transformB = new Transform3D { Position = new Vector3(2.0f, 1.5f, 3.0f) };

        EditableMesh lRoom = CsgBooleanEngine.Evaluate(
            boxA, transformA,
            boxB, transformB,
            CsgOperation.Union);

        // Floor (normal -Y) and ceiling (normal +Y) should each be merged into a single 6-vertex L-shaped N-gon!
        PolygonFace floorFace = Assert.Single(lRoom.Faces, f => f.ComputeNormal(lRoom).Y < -0.99f);
        PolygonFace ceilingFace = Assert.Single(lRoom.Faces, f => f.ComputeNormal(lRoom).Y > 0.99f);
        Assert.Equal(6, floorFace.VertexIds.Count);
        Assert.Equal(6, ceilingFace.VertexIds.Count);

        // Total faces of an L-shaped prism: 1 floor + 1 ceiling + 6 vertical walls = 8 faces, 12 vertices
        Assert.Equal(8, lRoom.FaceCount);
        Assert.Equal(12, lRoom.VertexCount);

        // Analytical surface area of the L-shaped solid:
        // Footprint area = (6 * 4) + (4 * 2) = 24 + 8 = 32 -> floor + ceiling = 64
        // Perimeter = 6 + 4 + 2 + 2 + 4 + 6 = 24 -> wall area = 24 * 3 = 72
        // Total surface area = 64 + 72 = 136
        float actualArea = ComputeMeshSurfaceArea(lRoom);
        Assert.Equal(136.0f, actualArea, 2);
    }

    [Fact]
    public void Intersect_TwoOverlappingBoxes_ReturnsClosedIntersectionBox()
    {
        // Box A: [-2, 2]^3 (size 4x4x4 at origin)
        var objA = new SceneObject(1, "BoxA", PrimitiveGenerator.CreateBox(new Vector3(4.0f, 4.0f, 4.0f)));

        // Box B: [0, 4]^3 (size 4x4x4 centered at (2, 2, 2))
        var objB = new SceneObject(
            2,
            "BoxB",
            PrimitiveGenerator.CreateBox(new Vector3(4.0f, 4.0f, 4.0f)),
            new Transform3D { Position = new Vector3(2.0f, 2.0f, 2.0f) });

        EditableMesh intersected = CsgBooleanEngine.Evaluate(objA, objB, CsgOperation.Intersect);

        // Intersection is [0, 2]^3 -> a 2x2x2 cube (6 faces, 8 vertices, surface area = 6 * 4 = 24)
        Assert.Equal(6, intersected.FaceCount);
        Assert.Equal(8, intersected.VertexCount);

        BoundingBox3D bounds = intersected.ComputeBoundingBox();
        Assert.Equal(new Vector3(0.0f, 0.0f, 0.0f), bounds.Min, new Vector3Comparer(1e-3f));
        Assert.Equal(new Vector3(2.0f, 2.0f, 2.0f), bounds.Max, new Vector3Comparer(1e-3f));

        float area = ComputeMeshSurfaceArea(intersected);
        Assert.Equal(24.0f, area, 2);
    }

    [Fact]
    public void DisjointMeshes_UnionSubtractIntersect_BehaveCorrectly()
    {
        EditableMesh boxA = PrimitiveGenerator.CreateBox(new Vector3(2.0f, 2.0f, 2.0f));
        var transformA = new Transform3D { Position = new Vector3(-5.0f, 0.0f, 0.0f) };

        EditableMesh boxB = PrimitiveGenerator.CreateBox(new Vector3(2.0f, 2.0f, 2.0f));
        var transformB = new Transform3D { Position = new Vector3(5.0f, 0.0f, 0.0f) };

        // 1. Union of disjoint boxes -> both boxes preserved (12 faces, 16 vertices, area 48)
        EditableMesh unionMesh = CsgBooleanEngine.Evaluate(boxA, transformA, boxB, transformB, CsgOperation.Union);
        Assert.Equal(12, unionMesh.FaceCount);
        Assert.Equal(16, unionMesh.VertexCount);
        Assert.Equal(48.0f, ComputeMeshSurfaceArea(unionMesh), 2);

        // 2. Subtract of disjoint boxes -> Box A unchanged (6 faces, 8 vertices, area 24)
        EditableMesh subtractMesh = CsgBooleanEngine.Evaluate(boxA, transformA, boxB, transformB, CsgOperation.Subtract);
        Assert.Equal(6, subtractMesh.FaceCount);
        Assert.Equal(8, subtractMesh.VertexCount);
        Assert.Equal(24.0f, ComputeMeshSurfaceArea(subtractMesh), 2);

        // 3. Intersect of disjoint boxes -> empty mesh (0 faces, 0 vertices)
        EditableMesh intersectMesh = CsgBooleanEngine.Evaluate(boxA, transformA, boxB, transformB, CsgOperation.Intersect);
        Assert.Equal(0, intersectMesh.FaceCount);
        Assert.Equal(0, intersectMesh.VertexCount);
    }

    private sealed class Vector3Comparer(float tolerance) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 x, Vector3 y) => Vector3.Distance(x, y) <= tolerance;
        public int GetHashCode(Vector3 obj) => 0;
    }
}
