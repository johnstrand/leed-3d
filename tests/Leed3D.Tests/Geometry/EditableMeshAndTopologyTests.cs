using System.Numerics;
using Leed3D.Kernel.Geometry;

namespace Leed3D.Tests.Geometry;

public class EditableMeshAndTopologyTests
{
    private static EditableMesh CreateUnitCubeMesh()
    {
        var mesh = new EditableMesh();

        // 8 vertices of a unit cube [-1, 1]^3 in Left-Handed (+X right, +Y up, +Z away)
        int v0 = mesh.AddVertex(new Vector3(-1, -1, -1)); // front-bottom-left
        int v1 = mesh.AddVertex(new Vector3(1, -1, -1));  // front-bottom-right
        int v2 = mesh.AddVertex(new Vector3(1, 1, -1));   // front-top-right
        int v3 = mesh.AddVertex(new Vector3(-1, 1, -1));  // front-top-left
        int v4 = mesh.AddVertex(new Vector3(-1, -1, 1));  // back-bottom-left
        int v5 = mesh.AddVertex(new Vector3(1, -1, 1));   // back-bottom-right
        int v6 = mesh.AddVertex(new Vector3(1, 1, 1));    // back-top-right
        int v7 = mesh.AddVertex(new Vector3(-1, 1, 1));   // back-top-left

        // 6 faces with consistent outward Newell normals:
        // Front (-Z): normal (0, 0, -1)
        mesh.AddFace([v1, v0, v3, v2], "wall_front");
        // Back (+Z): normal (0, 0, +1)
        mesh.AddFace([v4, v5, v6, v7], "wall_back");
        // Left (-X): normal (-1, 0, 0)
        mesh.AddFace([v0, v4, v7, v3], "wall_left");
        // Right (+X): normal (+1, 0, 0)
        mesh.AddFace([v5, v1, v2, v6], "wall_right");
        // Top (+Y): normal (0, +1, 0)
        mesh.AddFace([v3, v7, v6, v2], "ceiling");
        // Bottom (-Y): normal (0, -1, 0)
        mesh.AddFace([v0, v1, v5, v4], "floor");

        return mesh;
    }

    [Fact]
    public void CubeMesh_ComputesOutwardNormalsAndCentroids()
    {
        EditableMesh cube = CreateUnitCubeMesh();

        Assert.Equal(8, cube.VertexCount);
        Assert.Equal(6, cube.FaceCount);

        foreach (PolygonFace face in cube.Faces)
        {
            Vector3 normal = face.ComputeNormal(cube);
            Vector3 centroid = face.ComputeCentroid(cube);

            // For a centered cube with outward windings, Dot(normal, centroid) should be +1
            Assert.True(Vector3.Dot(normal, centroid) > 0.99f,
                $"Face {face.Id} ({face.MaterialId}) normal {normal} does not point outward from centroid {centroid}");
        }
    }

    [Fact]
    public void FlipNormals_ReversesFaceWindingAndNormalDirection()
    {
        EditableMesh cube = CreateUnitCubeMesh();
        PolygonFace firstFace = cube.Faces[0];
        Vector3 originalNormal = firstFace.ComputeNormal(cube);

        // Flip only the first face
        cube.FlipNormals([firstFace.Id]);
        Vector3 flippedFirstNormal = firstFace.ComputeNormal(cube);
        Assert.True(Vector3.Distance(flippedFirstNormal, -originalNormal) < 1e-5f);

        // Flip all faces back and check global flip
        cube.FlipNormals([firstFace.Id]);
        cube.FlipNormals();

        foreach (PolygonFace face in cube.Faces)
        {
            Vector3 normal = face.ComputeNormal(cube);
            Vector3 centroid = face.ComputeCentroid(cube);
            Assert.True(Vector3.Dot(normal, centroid) < -0.99f);
        }
    }

    [Fact]
    public void ValidateAndRepair_RemovesDegenerateFacesDuplicateVerticesAndOrphans()
    {
        var mesh = new EditableMesh();
        int v1 = mesh.AddVertex(new Vector3(0, 0, 0));
        int v2 = mesh.AddVertex(new Vector3(1, 0, 0));
        int v3 = mesh.AddVertex(new Vector3(1, 1, 0));
        int v4 = mesh.AddVertex(new Vector3(0, 1, 0));
        int orphan = mesh.AddVertex(new Vector3(99, 99, 99));

        // Face 1: valid quad with consecutive duplicate vertex IDs and wrap-around duplicate
        int fValid = mesh.AddFace([v1, v1, v2, v3, v3, v4, v1]);

        // Face 2: degenerate face with only 2 unique vertices
        int fDegenerate = mesh.AddFace([v1, v2, v1, v2]);

        Assert.Equal(5, mesh.VertexCount);
        Assert.Equal(2, mesh.FaceCount);

        mesh.ValidateAndRepair();

        // Orphan vertex and degenerate face should be pruned
        Assert.Equal(4, mesh.VertexCount);
        Assert.Null(mesh.GetVertex(orphan));
        Assert.Equal(1, mesh.FaceCount);
        Assert.Null(mesh.GetFace(fDegenerate));

        PolygonFace? repairedFace = mesh.GetFace(fValid);
        Assert.NotNull(repairedFace);
        Assert.Equal([v1, v2, v3, v4], repairedFace.VertexIds);
    }

    [Fact]
    public void Clone_CreatesIndependentDeepCopyPreservingIds()
    {
        EditableMesh original = CreateUnitCubeMesh();
        EditableMesh clone = original.Clone();

        Assert.Equal(original.VertexCount, clone.VertexCount);
        Assert.Equal(original.FaceCount, clone.FaceCount);

        // Mutate clone and verify original is untouched
        clone.SetVertexPosition(1, new Vector3(100, 200, 300));
        clone.RemoveFace(1);

        Assert.Equal(new Vector3(-1, -1, -1), original.GetVertex(1)!.Position);
        Assert.Equal(6, original.FaceCount);
        Assert.Equal(5, clone.FaceCount);
    }

    [Fact]
    public void TopologyGraph_ClosedCube_IsClosedManifoldWithPairedTwins()
    {
        EditableMesh cube = CreateUnitCubeMesh();
        TopologyGraph topology = cube.GetTopology();

        // A cube has 8 vertices, 12 undirected edges, 24 directed half-edges, 6 faces
        Assert.Equal(12, topology.Edges.Count);
        Assert.Equal(24, topology.HalfEdges.Count);
        Assert.True(topology.IsClosedManifold());

        // Every undirected edge on a closed cube has 2 incident faces and IsBoundary == false
        foreach (TopologyEdge edge in topology.Edges)
        {
            Assert.False(edge.IsBoundary);
            Assert.Equal(2, edge.IncidentFaceIds.Count);
        }

        // Every directed half-edge has a valid TwinHalfEdgeId pointing in the opposite direction
        foreach (DirectedHalfEdge he in topology.HalfEdges)
        {
            Assert.NotNull(he.TwinHalfEdgeId);
            DirectedHalfEdge twin = topology.HalfEdges[he.TwinHalfEdgeId.Value];
            Assert.Equal(he.OriginVertexId, twin.TargetVertexId);
            Assert.Equal(he.TargetVertexId, twin.OriginVertexId);
            Assert.Equal(he.Id, twin.TwinHalfEdgeId);
        }

        // Each vertex on a cube is connected to 3 neighbor vertices and 3 incident faces
        foreach (MeshVertex v in cube.Vertices)
        {
            Assert.Equal(3, topology.GetVertexConnectedVertices(v.Id).Count);
            Assert.Equal(3, topology.GetVertexIncidentFaces(v.Id).Count);
        }

        // Each face on a cube is adjacent to 4 side faces
        foreach (PolygonFace f in cube.Faces)
        {
            Assert.Equal(4, topology.GetFaceAdjacentFaces(f.Id).Count);
        }
    }

    [Fact]
    public void TopologyGraph_OpenBox_DetectsBoundaryEdgesAndNotClosedManifold()
    {
        EditableMesh openBox = CreateUnitCubeMesh();
        // Remove the top face (+Y ceiling, face id 5)
        openBox.RemoveFace(5);

        TopologyGraph topology = openBox.GetTopology();

        Assert.False(topology.IsClosedManifold());
        Assert.Equal(12, topology.Edges.Count);

        int boundaryEdgeCount = topology.Edges.Count(e => e.IsBoundary);
        Assert.Equal(4, boundaryEdgeCount);

        // Flipping a single face on a closed cube breaks manifold orientation
        EditableMesh invertedFaceCube = CreateUnitCubeMesh();
        invertedFaceCube.FlipNormals([1]);
        Assert.False(invertedFaceCube.GetTopology().IsClosedManifold());
    }
}
