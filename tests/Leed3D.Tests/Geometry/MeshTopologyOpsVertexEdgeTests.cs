using System.Numerics;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Primitives;
using Leed3D.Kernel.Scene;

namespace Leed3D.Tests.Geometry;

public class MeshTopologyOpsVertexEdgeTests
{
    [Fact]
    public void WeldVerticesToCenter_OnBoxTopFace_CollapsesTopQuadIntoPyramidApex()
    {
        EditableMesh box = PrimitiveGenerator.CreateBox(new Vector3(2, 2, 2), materialId: "brick_wall");
        Assert.Equal(8, box.VertexCount);
        Assert.Equal(6, box.FaceCount);

        // Find the 4 top vertices (Y > 0)
        List<int> topVertexIds = box.Vertices
            .Where(v => v.Position.Y > 0.5f)
            .Select(v => v.Id)
            .OrderByDescending(id => id) // pass in descending order to verify lowest ID is kept
            .ToList();

        Assert.Equal(4, topVertexIds.Count);
        int expectedKeptId = topVertexIds.Min();

        int keptId = MeshTopologyOps.WeldVerticesToCenter(box, topVertexIds);

        Assert.Equal(expectedKeptId, keptId);
        // 4 top vertices merged into 1 apex + 4 bottom vertices = 5 vertices
        Assert.Equal(5, box.VertexCount);
        // Top quad collapsed (< 3 unique vertices), leaving 4 side triangles + 1 bottom quad = 5 faces
        Assert.Equal(5, box.FaceCount);

        MeshVertex? apex = box.GetVertex(keptId);
        Assert.NotNull(apex);
        Assert.True(Vector3.Distance(new Vector3(0, 1, 0), apex.Position) < 1e-5f);

        // Resulting pyramid should still be a closed manifold
        TopologyGraph topology = box.GetTopology();
        Assert.True(topology.IsClosedManifold());
    }

    [Fact]
    public void WeldVerticesToCenter_TwoVerticesOfQuad_CollapsesQuadIntoTriangle()
    {
        EditableMesh plane = PrimitiveGenerator.CreatePlane(2.0f, 2.0f, 1, 1);
        Assert.Equal(4, plane.VertexCount);
        Assert.Equal(1, plane.FaceCount);

        int vA = plane.Faces[0].VertexIds[0];
        int vB = plane.Faces[0].VertexIds[1];
        Vector3 expectedMid = (plane.GetVertex(vA)!.Position + plane.GetVertex(vB)!.Position) * 0.5f;

        int keptId = MeshTopologyOps.WeldVerticesToCenter(plane, [vB, vA]);

        Assert.Equal(System.Math.Min(vA, vB), keptId);
        Assert.Equal(3, plane.VertexCount);
        Assert.Equal(1, plane.FaceCount);
        Assert.Equal(3, plane.Faces[0].VertexIds.Count);
        Assert.True(Vector3.Distance(expectedMid, plane.GetVertex(keptId)!.Position) < 1e-5f);
    }

    [Fact]
    public void WeldVerticesByDistance_MergesCoincidentSeamVerticesBetweenTwoQuads()
    {
        var mesh = new EditableMesh();

        // Quad 1: X in [-1, 0]
        int a0 = mesh.AddVertex(new Vector3(-1, 0, -1));
        int a1 = mesh.AddVertex(new Vector3(-1, 0,  1));
        int a2 = mesh.AddVertex(new Vector3( 0, 0,  1));
        int a3 = mesh.AddVertex(new Vector3( 0, 0, -1));
        mesh.AddFace([a0, a1, a2, a3]);

        // Quad 2: X in [0, 1] with separate duplicate vertices along X = 0 within 0.0005f
        int b0 = mesh.AddVertex(new Vector3(0.0004f, 0, -1));
        int b1 = mesh.AddVertex(new Vector3(0.0004f, 0,  1));
        int b2 = mesh.AddVertex(new Vector3(1, 0,  1));
        int b3 = mesh.AddVertex(new Vector3(1, 0, -1));
        mesh.AddFace([b0, b1, b2, b3]);

        Assert.Equal(8, mesh.VertexCount);
        Assert.Equal(8, mesh.GetTopology().Edges.Count);

        int weldedCount = MeshTopologyOps.WeldVerticesByDistance(mesh, threshold: 0.001f);

        Assert.Equal(2, weldedCount);
        Assert.Equal(6, mesh.VertexCount);
        Assert.Equal(2, mesh.FaceCount);

        // Shared edge (a2, a3) now has 2 incident faces and paired twin half-edges
        TopologyGraph topology = mesh.GetTopology();
        Assert.Equal(7, topology.Edges.Count);
        TopologyEdge? sharedEdge = topology.GetEdge(a2, a3);
        Assert.NotNull(sharedEdge);
        Assert.False(sharedEdge.IsBoundary);
        Assert.Equal(2, sharedEdge.IncidentFaceIds.Count);
    }

    [Fact]
    public void SnapVerticesToGrid_SnapsWorldPositionsAndTransformsBackToLocal()
    {
        EditableMesh mesh = new();
        int v0 = mesh.AddVertex(new Vector3(0.18f, 0.41f, -0.62f));
        int v1 = mesh.AddVertex(new Vector3(1.1f, 0.0f, 0.0f));
        int v2 = mesh.AddVertex(new Vector3(0.0f, 1.0f, 1.0f));
        mesh.AddFace([v0, v1, v2]);

        // Object translated by (1.0, 2.0, -3.0)
        var transform = new Transform3D(new Vector3(1.0f, 2.0f, -3.0f));

        // v0 world pos = (1.18, 2.41, -3.62) -> snapped to 0.5 grid = (1.0, 2.5, -3.5) -> local = (-0.0, 0.5, -0.5)
        MeshTopologyOps.SnapVerticesToGrid(mesh, [v0], transform, 0.5f);

        Vector3 snappedLocal = mesh.GetVertex(v0)!.Position;
        Assert.True(Vector3.Distance(new Vector3(0.0f, 0.5f, -0.5f), snappedLocal) < 1e-5f);
        // Unselected vertex v1 is untouched
        Assert.Equal(new Vector3(1.1f, 0.0f, 0.0f), mesh.GetVertex(v1)!.Position);
    }

    [Fact]
    public void SnapVerticesToNearestVertex_MovesSelectedVerticesToClosestUnselectedVertices()
    {
        EditableMesh mesh = new();
        int vTarget1 = mesh.AddVertex(new Vector3(0, 0, 0));
        int vTarget2 = mesh.AddVertex(new Vector3(10, 0, 0));
        int vSelected = mesh.AddVertex(new Vector3(0.8f, 0.2f, -0.1f));
        mesh.AddFace([vTarget1, vTarget2, vSelected]);

        MeshTopologyOps.SnapVerticesToNearestVertex(mesh, [vSelected]);

        Assert.Equal(mesh.GetVertex(vTarget1)!.Position, mesh.GetVertex(vSelected)!.Position);
    }

    [Fact]
    public void ConnectVertices_OnQuadDiagonal_SplitsIntoTwoTrianglesWithMatchingNormals()
    {
        EditableMesh plane = PrimitiveGenerator.CreatePlane(4.0f, 4.0f, 1, 1, "dev_grid_orange");
        PolygonFace originalQuad = plane.Faces[0];
        Vector3 originalNormal = originalQuad.ComputeNormal(plane);

        int v0 = originalQuad.VertexIds[0];
        int v1 = originalQuad.VertexIds[1];
        int v2 = originalQuad.VertexIds[2];

        // Connecting adjacent vertices (v0, v1) should return false and not split
        Assert.False(MeshTopologyOps.ConnectVertices(plane, v0, v1));
        Assert.Equal(1, plane.FaceCount);

        // Connecting diagonal vertices (v0, v2) splits quad into 2 triangles
        bool connected = MeshTopologyOps.ConnectVertices(plane, v0, v2);
        Assert.True(connected);
        Assert.Equal(4, plane.VertexCount);
        Assert.Equal(2, plane.FaceCount);

        foreach (PolygonFace tri in plane.Faces)
        {
            Assert.Equal(3, tri.VertexIds.Count);
            Assert.Equal("dev_grid_orange", tri.MaterialId);
            Vector3 triNormal = tri.ComputeNormal(plane);
            Assert.True(Vector3.Distance(originalNormal, triNormal) < 1e-5f);
        }

        // New diagonal edge (v0, v2) is shared by both triangles with opposite half-edges
        TopologyGraph topology = plane.GetTopology();
        TopologyEdge? diagEdge = topology.GetEdge(v0, v2);
        Assert.NotNull(diagEdge);
        Assert.False(diagEdge.IsBoundary);
        Assert.Equal(2, diagEdge.IncidentFaceIds.Count);
    }

    [Fact]
    public void FillFaceFromVertices_CapsOpenBoxAndRestoresClosedManifold()
    {
        EditableMesh box = PrimitiveGenerator.CreateBox(new Vector3(2, 2, 2), materialId: "stone_trim");

        // Find and remove the top (+Y) face
        PolygonFace topFace = box.Faces.First(f => f.ComputeNormal(box).Y > 0.9f);
        // Scramble the vertex order into a bow-tie to test automatic boundary/angle ordering
        int[] scrambledTopVerts = [topFace.VertexIds[0], topFace.VertexIds[2], topFace.VertexIds[1], topFace.VertexIds[3]];

        box.RemoveFace(topFace.Id);
        Assert.Equal(5, box.FaceCount);
        Assert.False(box.GetTopology().IsClosedManifold());

        int? newFaceId = MeshTopologyOps.FillFaceFromVertices(box, scrambledTopVerts);
        Assert.NotNull(newFaceId);
        Assert.Equal(6, box.FaceCount);

        PolygonFace? filledFace = box.GetFace(newFaceId.Value);
        Assert.NotNull(filledFace);
        Assert.Equal(4, filledFace.VertexIds.Count);
        Assert.Equal("stone_trim", filledFace.MaterialId);

        // Normal should point +Y outward to match adjacent walls
        Vector3 filledNormal = filledFace.ComputeNormal(box);
        Assert.True(Vector3.Distance(Vector3.UnitY, filledNormal) < 1e-5f);
        Assert.True(box.GetTopology().IsClosedManifold());
    }

    [Fact]
    public void FillFaceFromVertices_StandaloneVertices_RespectsPreferredNormalAndUncrossesBowtie()
    {
        var mesh = new EditableMesh();
        int v0 = mesh.AddVertex(new Vector3(-1, 0, -1));
        int v1 = mesh.AddVertex(new Vector3( 1, 0,  1));
        int v2 = mesh.AddVertex(new Vector3( 1, 0, -1));
        int v3 = mesh.AddVertex(new Vector3(-1, 0,  1));

        // Pass vertices in crossed bow-tie order with preferredNormalDir = -Y
        int? faceId = MeshTopologyOps.FillFaceFromVertices(mesh, [v0, v1, v2, v3], preferredNormalDir: -Vector3.UnitY);
        Assert.NotNull(faceId);

        PolygonFace face = mesh.GetFace(faceId.Value)!;
        Assert.True(Vector3.Distance(-Vector3.UnitY, face.ComputeNormal(mesh)) < 1e-5f);

        mesh.ValidateAndRepair();
        Assert.Equal(4, mesh.VertexCount);
        Assert.Equal(1, mesh.FaceCount);
    }

    [Fact]
    public void FillFaceFromEdges_BridgesTwoOppositeEdgesIntoConsistentQuad()
    {
        var mesh = new EditableMesh();

        // Left quad on XZ plane facing +Y: X in [-3, -1]
        int a0 = mesh.AddVertex(new Vector3(-3, 0, -1));
        int a1 = mesh.AddVertex(new Vector3(-3, 0,  1));
        int a2 = mesh.AddVertex(new Vector3(-1, 0,  1));
        int a3 = mesh.AddVertex(new Vector3(-1, 0, -1));
        mesh.AddFace([a0, a1, a2, a3], "bridge_mat");

        // Right quad on XZ plane facing +Y: X in [1, 3]
        int b0 = mesh.AddVertex(new Vector3(1, 0, -1));
        int b1 = mesh.AddVertex(new Vector3(1, 0,  1));
        int b2 = mesh.AddVertex(new Vector3(3, 0,  1));
        int b3 = mesh.AddVertex(new Vector3(3, 0, -1));
        mesh.AddFace([b0, b1, b2, b3], "bridge_mat");

        // Bridge edge (a2, a3) with opposite edge (b0, b1)
        int? bridgeFaceId = MeshTopologyOps.FillFaceFromEdges(mesh, [new EdgeKey(a2, a3), new EdgeKey(b0, b1)]);
        Assert.NotNull(bridgeFaceId);
        Assert.Equal(3, mesh.FaceCount);

        PolygonFace bridgeFace = mesh.GetFace(bridgeFaceId.Value)!;
        Assert.Equal(4, bridgeFace.VertexIds.Count);
        Assert.Equal("bridge_mat", bridgeFace.MaterialId);
        Assert.True(Vector3.Distance(Vector3.UnitY, bridgeFace.ComputeNormal(mesh)) < 1e-5f);

        // Both bridged edges should now be interior manifold edges with 2 incident faces
        TopologyGraph topology = mesh.GetTopology();
        Assert.False(topology.GetEdge(a2, a3)!.IsBoundary);
        Assert.False(topology.GetEdge(b0, b1)!.IsBoundary);
    }

    [Fact]
    public void SubdivideEdges_TurnsQuadIntoFiveVertexNGonAndSupportsConnectAndExtrude()
    {
        EditableMesh plane = PrimitiveGenerator.CreatePlane(4.0f, 4.0f, 1, 1);
        PolygonFace face = plane.Faces[0];
        int v0 = face.VertexIds[0];
        int v1 = face.VertexIds[1];
        int v2 = face.VertexIds[2];
        int v3 = face.VertexIds[3];

        var edgeToSubdivide = new EdgeKey(v0, v1);
        IReadOnlyList<int> newVerts = MeshTopologyOps.SubdivideEdges(plane, [edgeToSubdivide], cuts: 1);

        Assert.Single(newVerts);
        int midVertId = newVerts[0];

        // Quad is now a 5-vertex N-gon
        Assert.Equal(5, plane.VertexCount);
        Assert.Equal(5, face.VertexIds.Count);

        Vector3 expectedMidPos = (plane.GetVertex(v0)!.Position + plane.GetVertex(v1)!.Position) * 0.5f;
        Assert.True(Vector3.Distance(expectedMidPos, plane.GetVertex(midVertId)!.Position) < 1e-5f);

        // PolygonTriangulator cleanly triangulates the 5-gon into 3 triangles
        IReadOnlyList<(int V0, int V1, int V2)> tris = PolygonTriangulator.TriangulateFace(face, plane);
        Assert.Equal(3, tris.Count);

        // We can extrude one of the subdivided half-edges (v0, midVertId)
        IReadOnlyList<(EdgeKey NewEdge, int NewFaceId)> extruded = MeshTopologyOps.ExtrudeEdges(
            plane,
            [new EdgeKey(v0, midVertId)],
            new Vector3(-1, 0, 0));
        Assert.Single(extruded);
        Assert.Equal(2, plane.FaceCount);

        // And we can connect the new midpoint vertex to an opposite corner vertex (v2) to split the 5-gon
        bool split = MeshTopologyOps.ConnectVertices(plane, midVertId, v2);
        Assert.True(split);
        Assert.Equal(3, plane.FaceCount);

        plane.ValidateAndRepair();
        Assert.Equal(3, plane.FaceCount);
    }

    [Fact]
    public void SubdivideEdges_OnClosedCubeSharedEdge_PreservesClosedManifold()
    {
        EditableMesh cube = PrimitiveGenerator.CreateBox(new Vector3(2, 2, 2));
        EdgeKey firstEdge = cube.GetTopology().Edges[0].Key;

        IReadOnlyList<int> created = MeshTopologyOps.SubdivideEdges(cube, [firstEdge], cuts: 2);
        Assert.Equal(2, created.Count);
        Assert.Equal(10, cube.VertexCount);

        // The two faces sharing firstEdge are now 6-gons and still traverse the 3 sub-edges in opposite directions
        TopologyGraph topology = cube.GetTopology();
        Assert.True(topology.IsClosedManifold());
        Assert.Equal(14, topology.Edges.Count);
    }

    [Fact]
    public void ExtrudeEdges_OnOpenBoxRim_CreatesConnectedCollarWallsWithConsistentWinding()
    {
        EditableMesh openBox = PrimitiveGenerator.CreateBox(new Vector3(2, 2, 2), materialId: "metal_trim");
        PolygonFace topFace = openBox.Faces.First(f => f.ComputeNormal(openBox).Y > 0.9f);
        openBox.RemoveFace(topFace.Id);

        List<EdgeKey> boundaryEdges = openBox.GetTopology().Edges
            .Where(e => e.IsBoundary)
            .Select(e => e.Key)
            .ToList();

        Assert.Equal(4, boundaryEdges.Count);

        // Extrude the 4 top boundary edges upward by +1 Y
        IReadOnlyList<(EdgeKey NewEdge, int NewFaceId)> extruded = MeshTopologyOps.ExtrudeEdges(
            openBox,
            boundaryEdges,
            new Vector3(0, 1, 0));

        Assert.Equal(4, extruded.Count);
        // 8 original vertices + 4 duplicated rim vertices = 12 vertices
        Assert.Equal(12, openBox.VertexCount);
        // 5 original faces + 4 new collar quads = 9 faces
        Assert.Equal(9, openBox.FaceCount);

        // Original 4 boundary edges are now interior manifold edges!
        TopologyGraph topology = openBox.GetTopology();
        foreach (EdgeKey oldRimEdge in boundaryEdges)
        {
            TopologyEdge? edge = topology.GetEdge(oldRimEdge);
            Assert.NotNull(edge);
            Assert.False(edge.IsBoundary);
            Assert.Equal(2, edge.IncidentFaceIds.Count);
        }

        // And the 4 new extruded top edges can be capped with FillFaceFromEdges to close the mesh!
        List<EdgeKey> newRimEdges = extruded.Select(x => x.NewEdge).ToList();
        int? capId = MeshTopologyOps.FillFaceFromEdges(openBox, newRimEdges);
        Assert.NotNull(capId);
        Assert.Equal(10, openBox.FaceCount);
        Assert.True(openBox.GetTopology().IsClosedManifold());
    }
}
