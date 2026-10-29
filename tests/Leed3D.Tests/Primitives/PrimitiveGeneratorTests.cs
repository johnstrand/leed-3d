using System.Numerics;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Primitives;

namespace Leed3D.Tests.Primitives;

public class PrimitiveGeneratorTests
{
    [Fact]
    public void CreatePlane_DefaultParameters_CreatesValidPlaneFacingUp()
    {
        EditableMesh plane = PrimitiveGenerator.CreatePlane(width: 4.0f, depth: 4.0f, widthSegments: 2, depthSegments: 3);

        // 3 * 4 = 12 vertices, 2 * 3 = 6 quad faces
        Assert.Equal(12, plane.VertexCount);
        Assert.Equal(6, plane.FaceCount);

        foreach (PolygonFace face in plane.Faces)
        {
            Vector3 normal = face.ComputeNormal(plane);
            Assert.True(Vector3.Distance(normal, Vector3.UnitY) < 1e-4f,
                $"Plane face normal should point +Y, but got {normal}");
        }

        TopologyGraph topology = plane.GetTopology();
        Assert.False(topology.IsClosedManifold()); // Open plane has boundary edges
        Assert.Contains(topology.Edges, e => e.IsBoundary);
    }

    [Fact]
    public void CreateBox_DefaultParameters_CreatesWeldedClosedManifold()
    {
        EditableMesh box = PrimitiveGenerator.CreateBox(new Vector3(2.0f, 2.0f, 2.0f), segmentsX: 1, segmentsY: 1, segmentsZ: 1);

        // Standard cube: 8 vertices, 6 quad faces, 12 edges
        Assert.Equal(8, box.VertexCount);
        Assert.Equal(6, box.FaceCount);

        TopologyGraph topology = box.GetTopology();
        Assert.Equal(12, topology.Edges.Count);
        Assert.True(topology.IsClosedManifold());

        // Outward normals
        foreach (PolygonFace face in box.Faces)
        {
            Vector3 normal = face.ComputeNormal(box);
            Vector3 centroid = face.ComputeCentroid(box);
            Assert.True(Vector3.Dot(normal, centroid) > 0.99f);
        }
    }

    [Fact]
    public void CreateBox_InvertNormals_InvertsNormalDirectionForHollowRooms()
    {
        EditableMesh room = PrimitiveGenerator.CreateBox(new Vector3(4.0f, 3.0f, 6.0f), invertNormals: true);

        Assert.Equal(8, room.VertexCount);
        Assert.Equal(6, room.FaceCount);

        TopologyGraph topology = room.GetTopology();
        Assert.True(topology.IsClosedManifold());

        // Inward normals
        foreach (PolygonFace face in room.Faces)
        {
            Vector3 normal = face.ComputeNormal(room);
            Vector3 centroid = face.ComputeCentroid(room);
            Assert.True(Vector3.Dot(normal, centroid) < -0.99f);
        }
    }

    [Fact]
    public void CreateBox_Subdivided_WeldsSharedEdgesAndVertices()
    {
        EditableMesh subdividedBox = PrimitiveGenerator.CreateBox(new Vector3(2.0f, 2.0f, 2.0f), segmentsX: 2, segmentsY: 2, segmentsZ: 2);

        // 2x2 per face: 6 faces * 4 = 24 quad faces
        Assert.Equal(24, subdividedBox.FaceCount);

        // A 2x2x2 subdivided cube has 3*3*3 - 1 interior = 26 boundary vertices
        Assert.Equal(26, subdividedBox.VertexCount);

        TopologyGraph topology = subdividedBox.GetTopology();
        Assert.True(topology.IsClosedManifold());
        Assert.True(topology.Edges.All(e => !e.IsBoundary));
    }

    [Fact]
    public void CreateCylinder_CapsAreSingleNgonFaces_IsClosedManifold()
    {
        const int radialSegments = 16;
        EditableMesh cylinder = PrimitiveGenerator.CreateCylinder(
            radius: 1.0f,
            height: 2.0f,
            radialSegments: radialSegments,
            heightSegments: 1,
            capTop: true,
            capBottom: true);

        // 2 rings of 16 vertices = 32 vertices
        Assert.Equal(32, cylinder.VertexCount);

        // 16 side quads + 1 top N-gon cap + 1 bottom N-gon cap = 18 faces
        Assert.Equal(18, cylinder.FaceCount);

        PolygonFace topCap = cylinder.Faces[16];
        PolygonFace bottomCap = cylinder.Faces[17];

        Assert.Equal(radialSegments, topCap.VertexIds.Count);
        Assert.Equal(radialSegments, bottomCap.VertexIds.Count);

        Vector3 topNormal = topCap.ComputeNormal(cylinder);
        Vector3 bottomNormal = bottomCap.ComputeNormal(cylinder);

        Assert.True(Vector3.Distance(topNormal, Vector3.UnitY) < 1e-4f, $"Top cap normal was {topNormal}");
        Assert.True(Vector3.Distance(bottomNormal, -Vector3.UnitY) < 1e-4f, $"Bottom cap normal was {bottomNormal}");

        TopologyGraph topology = cylinder.GetTopology();
        Assert.True(topology.IsClosedManifold());
    }

    [Fact]
    public void CreateCone_CapBottomIsSingleNgonFace_IsClosedManifold()
    {
        const int radialSegments = 12;
        EditableMesh cone = PrimitiveGenerator.CreateCone(
            radius: 1.5f,
            height: 3.0f,
            radialSegments: radialSegments,
            capBottom: true);

        // 1 apex + 12 base vertices = 13 vertices
        Assert.Equal(13, cone.VertexCount);

        // 12 side triangles + 1 bottom N-gon cap = 13 faces
        Assert.Equal(13, cone.FaceCount);

        PolygonFace bottomCap = cone.Faces[12];
        Assert.Equal(radialSegments, bottomCap.VertexIds.Count);

        Vector3 bottomNormal = bottomCap.ComputeNormal(cone);
        Assert.True(Vector3.Distance(bottomNormal, -Vector3.UnitY) < 1e-4f);

        TopologyGraph topology = cone.GetTopology();
        Assert.True(topology.IsClosedManifold());
    }

    [Fact]
    public void CreateIcosahedron_HasExpectedCountsAndIsClosedManifold()
    {
        EditableMesh icosahedron = PrimitiveGenerator.CreateIcosahedron(radius: 1.0f);

        // Regular icosahedron: 12 vertices, 20 triangular faces, 30 edges
        Assert.Equal(12, icosahedron.VertexCount);
        Assert.Equal(20, icosahedron.FaceCount);

        TopologyGraph topology = icosahedron.GetTopology();
        Assert.Equal(30, topology.Edges.Count);
        Assert.True(topology.IsClosedManifold());

        // Check radial distance and outward normals
        foreach (MeshVertex v in icosahedron.Vertices)
        {
            Assert.True(MathF.Abs(v.Position.Length() - 1.0f) < 1e-4f);
        }

        foreach (PolygonFace face in icosahedron.Faces)
        {
            Vector3 normal = face.ComputeNormal(icosahedron);
            Vector3 centroid = face.ComputeCentroid(icosahedron);
            Assert.True(Vector3.Dot(normal, centroid) > 0.0f);
        }
    }

    [Theory]
    [InlineData(0, 12, 20)]
    [InlineData(1, 42, 80)]
    [InlineData(2, 162, 320)]
    public void CreateGeosphere_Subdivisions_MatchEulerCharacteristics(int subdivisions, int expectedVerts, int expectedFaces)
    {
        EditableMesh geosphere = PrimitiveGenerator.CreateGeosphere(radius: 2.0f, subdivisions: subdivisions);

        Assert.Equal(expectedVerts, geosphere.VertexCount);
        Assert.Equal(expectedFaces, geosphere.FaceCount);

        TopologyGraph topology = geosphere.GetTopology();
        Assert.True(topology.IsClosedManifold());

        // For closed convex manifold: V - E + F = 2 => E = V + F - 2
        int expectedEdges = expectedVerts + expectedFaces - 2;
        Assert.Equal(expectedEdges, topology.Edges.Count);

        // Every vertex is normalized to radius = 2.0f
        foreach (MeshVertex v in geosphere.Vertices)
        {
            Assert.True(MathF.Abs(v.Position.Length() - 2.0f) < 1e-4f);
        }
    }
}
