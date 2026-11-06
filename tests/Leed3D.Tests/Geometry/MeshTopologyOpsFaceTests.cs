using System.Numerics;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Primitives;

namespace Leed3D.Tests.Geometry;

public sealed class MeshTopologyOpsFaceTests
{
    [Fact]
    public void ExtrudeFaces_SingleQuadOnBox_PreservesClosedManifoldAndOutwardNormals()
    {
        EditableMesh box = PrimitiveGenerator.CreateBox(new Vector3(2.0f, 2.0f, 2.0f), materialId: "brick_wall");
        Assert.Equal(8, box.VertexCount);
        Assert.Equal(6, box.FaceCount);

        PolygonFace topFace = box.Faces.Single(f => f.ComputeNormal(box).Y > 0.9f);
        int topFaceId = topFace.Id;

        IReadOnlyList<int> extrudedIds = MeshTopologyOps.ExtrudeFaces(box, [topFaceId], distance: 2.0f);

        Assert.Single(extrudedIds);
        Assert.Equal(topFaceId, extrudedIds[0]);

        // 8 original + 4 duplicated cap vertices = 12 vertices; 6 original + 4 side walls = 10 faces
        Assert.Equal(12, box.VertexCount);
        Assert.Equal(10, box.FaceCount);

        // Extruded cap keeps the same FaceId and is offset by +2 along +Y (from Y=1 to Y=3)
        PolygonFace? extrudedCap = box.GetFace(topFaceId);
        Assert.NotNull(extrudedCap);
        Assert.True(Vector3.Distance(new Vector3(0.0f, 3.0f, 0.0f), extrudedCap.ComputeCentroid(box)) < 1e-5f);
        Assert.True(Vector3.Distance(Vector3.UnitY, extrudedCap.ComputeNormal(box)) < 1e-5f);

        // Closed manifold must be preserved
        TopologyGraph topology = box.GetTopology();
        Assert.True(topology.IsClosedManifold());

        // All 10 faces must point outward relative to mesh center (0, 1, 0)
        Vector3 meshCenter = new(0.0f, 1.0f, 0.0f);
        foreach (PolygonFace face in box.Faces)
        {
            Vector3 outwardDir = face.ComputeCentroid(box) - meshCenter;
            Assert.True(Vector3.Dot(outwardDir, face.ComputeNormal(box)) > 0.0f);
            Assert.Equal("brick_wall", face.MaterialId);
        }
    }

    [Fact]
    public void ExtrudeFaces_ContiguousMultiFaceSelection_OmitsInternalSharedWalls()
    {
        // Box with 2 segments along X -> top has 2 adjacent quads sharing an interior edge
        EditableMesh box = PrimitiveGenerator.CreateBox(
            new Vector3(4.0f, 2.0f, 2.0f),
            segmentsX: 2,
            segmentsY: 1,
            segmentsZ: 1);

        int initialVertexCount = box.VertexCount;
        int initialFaceCount = box.FaceCount;

        List<int> topFaceIds = box.Faces
            .Where(f => f.ComputeNormal(box).Y > 0.9f)
            .Select(f => f.Id)
            .ToList();

        Assert.Equal(2, topFaceIds.Count);

        IReadOnlyList<int> extrudedIds = MeshTopologyOps.ExtrudeFaces(box, topFaceIds, distance: 1.5f);

        Assert.Equal(2, extrudedIds.Count);
        // 6 vertices duplicated on the 2-quad top cap; 6 outer boundary edges produce 6 side wall quads
        Assert.Equal(initialVertexCount + 6, box.VertexCount);
        Assert.Equal(initialFaceCount + 6, box.FaceCount);

        TopologyGraph topology = box.GetTopology();
        Assert.True(topology.IsClosedManifold());

        foreach (int id in topFaceIds)
        {
            PolygonFace cap = box.GetFace(id)!;
            Assert.Equal(2.5f, cap.ComputeCentroid(box).Y, 4);
            Assert.True(Vector3.Distance(Vector3.UnitY, cap.ComputeNormal(box)) < 1e-5f);
        }
    }

    [Fact]
    public void ExtrudeFaces_WithCustomOffset_UsesExactCustomVector()
    {
        EditableMesh box = PrimitiveGenerator.CreateBox(new Vector3(2.0f, 2.0f, 2.0f));
        PolygonFace topFace = box.Faces.Single(f => f.ComputeNormal(box).Y > 0.9f);
        Vector3 startCentroid = topFace.ComputeCentroid(box);
        Vector3 customOffset = new(0.5f, 1.25f, -0.75f);

        MeshTopologyOps.ExtrudeFaces(box, [topFace.Id], distance: 0.0f, customOffset: customOffset);

        Vector3 endCentroid = topFace.ComputeCentroid(box);
        Assert.True(Vector3.Distance(startCentroid + customOffset, endCentroid) < 1e-5f);
        Assert.True(box.GetTopology().IsClosedManifold());
    }

    [Fact]
    public void InsetFaces_OnQuadFace_CreatesFourBorderQuadsAndInnerQuadWithExactMiter()
    {
        EditableMesh box = PrimitiveGenerator.CreateBox(new Vector3(4.0f, 4.0f, 4.0f), materialId: "tile_floor");
        PolygonFace topFace = box.Faces.Single(f => f.ComputeNormal(box).Y > 0.9f);
        int topFaceId = topFace.Id;

        IReadOnlyList<int> insetIds = MeshTopologyOps.InsetFaces(box, [topFaceId], thickness: 0.5f, depth: 0.0f);

        Assert.Single(insetIds);
        Assert.Equal(topFaceId, insetIds[0]);

        // 5 untouched faces + 4 border quads + 1 inner quad = 10 faces; 8 + 4 = 12 vertices
        Assert.Equal(10, box.FaceCount);
        Assert.Equal(12, box.VertexCount);

        PolygonFace innerQuad = box.GetFace(topFaceId)!;
        Assert.Equal(4, innerQuad.VertexIds.Count);
        Assert.True(Vector3.Distance(Vector3.UnitY, innerQuad.ComputeNormal(box)) < 1e-5f);

        // Original top face spanned X in [-2, 2], Z in [-2, 2] at Y = 2.
        // With thickness = 0.5, inner vertices must lie at |X| = 1.5, Y = 2.0, |Z| = 1.5.
        foreach (int vId in innerQuad.VertexIds)
        {
            Vector3 pos = box.GetVertex(vId)!.Position;
            Assert.Equal(1.5f, MathF.Abs(pos.X), 4);
            Assert.Equal(2.0f, pos.Y, 4);
            Assert.Equal(1.5f, MathF.Abs(pos.Z), 4);
        }

        Assert.True(box.GetTopology().IsClosedManifold());
    }

    [Fact]
    public void InsetFaces_OnEightSidedCylinderCapNGon_ComputesAccurateMiterAndDepth()
    {
        const float radius = 2.0f;
        const float height = 4.0f;
        const float thickness = 0.4f;
        const float depth = 0.25f;

        EditableMesh cylinder = PrimitiveGenerator.CreateCylinder(
            radius: radius,
            height: height,
            radialSegments: 8);

        int initialFaceCount = cylinder.FaceCount;
        PolygonFace topCap = cylinder.Faces.Single(f => f.VertexIds.Count == 8 && f.ComputeNormal(cylinder).Y > 0.9f);
        int topCapId = topCap.Id;

        // Capture outer edge lines before inset to verify perpendicular inset distance = thickness
        Vector3[] outerPositions = topCap.VertexIds
            .Select(id => cylinder.GetVertex(id)!.Position)
            .ToArray();

        IReadOnlyList<int> insetIds = MeshTopologyOps.InsetFaces(cylinder, [topCapId], thickness: thickness, depth: depth);

        Assert.Single(insetIds);
        Assert.Equal(topCapId, insetIds[0]);

        // 8 new quad border faces added around the 8-gon cap
        Assert.Equal(initialFaceCount + 8, cylinder.FaceCount);

        PolygonFace innerCap = cylinder.GetFace(topCapId)!;
        Assert.Equal(8, innerCap.VertexIds.Count);
        Assert.True(Vector3.Distance(Vector3.UnitY, innerCap.ComputeNormal(cylinder)) < 1e-5f);

        Vector3[] innerPositions = innerCap.VertexIds
            .Select(id => cylinder.GetVertex(id)!.Position)
            .ToArray();

        for (int i = 0; i < 8; i++)
        {
            // Verify depth offset along +Y normal (2.0 + 0.25 = 2.25)
            Assert.Equal(2.25f, innerPositions[i].Y, 4);

            // Verify perpendicular distance in XZ plane between outer edge i and inner edge i equals thickness (0.4)
            Vector3 outerA = new(outerPositions[i].X, 0.0f, outerPositions[i].Z);
            Vector3 outerB = new(outerPositions[(i + 1) % 8].X, 0.0f, outerPositions[(i + 1) % 8].Z);
            Vector3 edgeDir = Vector3.Normalize(outerB - outerA);
            Vector3 inwardEdgeNormal = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, edgeDir));

            Vector3 innerA = new(innerPositions[i].X, 0.0f, innerPositions[i].Z);
            float perpDist = Vector3.Dot(innerA - outerA, inwardEdgeNormal);
            Assert.Equal(thickness, perpDist, 3);
        }

        Assert.True(cylinder.GetTopology().IsClosedManifold());

        // Inner 8-gon must triangulate cleanly into 6 triangles
        var triangles = PolygonTriangulator.TriangulateFace(innerCap, cylinder);
        Assert.Equal(6, triangles.Count);
    }

    [Fact]
    public void InsetFaces_MultiFaceRegionVsIndividualFaces_RespectsIndividualFlag()
    {
        EditableMesh regionPlane = PrimitiveGenerator.CreatePlane(width: 4.0f, depth: 2.0f, widthSegments: 2, depthSegments: 1);
        List<int> allFaceIds = regionPlane.Faces.Select(f => f.Id).ToList();
        Assert.Equal(2, allFaceIds.Count);

        // Region inset (individualFaces: false): 2 inner quads + 6 perimeter border quads = 8 faces
        MeshTopologyOps.InsetFaces(regionPlane, allFaceIds, thickness: 0.2f, depth: 0.0f, individualFaces: false);
        Assert.Equal(8, regionPlane.FaceCount);

        // Individual inset (individualFaces: true): 2 inner quads + (4 * 2) border quads = 10 faces
        EditableMesh individualPlane = PrimitiveGenerator.CreatePlane(width: 4.0f, depth: 2.0f, widthSegments: 2, depthSegments: 1);
        List<int> indFaceIds = individualPlane.Faces.Select(f => f.Id).ToList();
        MeshTopologyOps.InsetFaces(individualPlane, indFaceIds, thickness: 0.2f, depth: 0.0f, individualFaces: true);
        Assert.Equal(10, individualPlane.FaceCount);
    }

    [Fact]
    public void DeleteFaces_RemovesFacesAndCleansUpUnreferencedVertices()
    {
        EditableMesh box = PrimitiveGenerator.CreateBox(new Vector3(2.0f, 2.0f, 2.0f));
        PolygonFace topFace = box.Faces.Single(f => f.ComputeNormal(box).Y > 0.9f);

        MeshTopologyOps.DeleteFaces(box, [topFace.Id]);
        Assert.Equal(5, box.FaceCount);
        Assert.Equal(8, box.VertexCount); // Side faces still reference top 4 vertices

        // Delete the 4 side walls so only the bottom face remains
        List<int> sideFaceIds = box.Faces
            .Where(f => MathF.Abs(f.ComputeNormal(box).Y) < 0.5f)
            .Select(f => f.Id)
            .ToList();

        Assert.Equal(4, sideFaceIds.Count);
        MeshTopologyOps.DeleteFaces(box, sideFaceIds);

        Assert.Equal(1, box.FaceCount);
        Assert.Equal(4, box.VertexCount); // Top 4 vertices became unreferenced and were pruned
    }

    [Fact]
    public void DissolveFaces_TwoTrianglesOfQuad_MergesBackIntoFourVertexQuad()
    {
        EditableMesh box = PrimitiveGenerator.CreateBox(new Vector3(2.0f, 2.0f, 2.0f), materialId: "concrete");
        PolygonFace topQuad = box.Faces.Single(f => f.ComputeNormal(box).Y > 0.9f);

        // Split top quad across diagonal (v0, v2) into 2 triangles
        int v0 = topQuad.VertexIds[0];
        int v2 = topQuad.VertexIds[2];
        bool split = MeshTopologyOps.ConnectVertices(box, v0, v2);
        Assert.True(split);
        Assert.Equal(7, box.FaceCount);

        List<int> topTriangleIds = box.Faces
            .Where(f => f.VertexIds.Count == 3)
            .Select(f => f.Id)
            .ToList();

        Assert.Equal(2, topTriangleIds.Count);

        // Dissolve the 2 triangles back into a single 4-vertex quad
        int? dissolvedFaceId = MeshTopologyOps.DissolveFaces(box, topTriangleIds);

        Assert.True(dissolvedFaceId.HasValue);
        Assert.Equal(6, box.FaceCount);
        Assert.Equal(8, box.VertexCount);

        PolygonFace mergedQuad = box.GetFace(dissolvedFaceId.Value)!;
        Assert.Equal(4, mergedQuad.VertexIds.Count);
        Assert.Equal("concrete", mergedQuad.MaterialId);
        Assert.True(Vector3.Distance(Vector3.UnitY, mergedQuad.ComputeNormal(box)) < 1e-5f);
        Assert.True(box.GetTopology().IsClosedManifold());
    }

    [Fact]
    public void FlipSelectedFaces_ReversesWindingOfTargetFacesOnly()
    {
        EditableMesh box = PrimitiveGenerator.CreateBox(new Vector3(2.0f, 2.0f, 2.0f));
        PolygonFace topFace = box.Faces.Single(f => f.ComputeNormal(box).Y > 0.9f);
        PolygonFace bottomFace = box.Faces.Single(f => f.ComputeNormal(box).Y < -0.9f);

        MeshTopologyOps.FlipSelectedFaces(box, [topFace.Id]);

        Assert.True(Vector3.Distance(-Vector3.UnitY, topFace.ComputeNormal(box)) < 1e-5f);
        Assert.True(Vector3.Distance(-Vector3.UnitY, bottomFace.ComputeNormal(box)) < 1e-5f);
    }

    [Fact]
    public void RecalculateNormals_FixesScrambledWindingsOnBoxAndCylinder_OutsideAndInsideOut()
    {
        // 1. Test on Box with scrambled face windings
        EditableMesh box = PrimitiveGenerator.CreateBox(new Vector3(2.0f, 2.0f, 2.0f));
        box.Faces[0].VertexIds.Reverse();
        box.Faces[2].VertexIds.Reverse();
        box.Faces[4].VertexIds.Reverse();
        box.InvalidateTopology();

        Assert.False(box.GetTopology().IsClosedManifold());

        // Recalculate Outside (insideOut: false)
        MeshTopologyOps.RecalculateNormals(box, insideOut: false);
        Assert.True(box.GetTopology().IsClosedManifold());

        foreach (PolygonFace face in box.Faces)
        {
            Assert.True(Vector3.Dot(face.ComputeCentroid(box), face.ComputeNormal(box)) > 0.0f);
        }

        // Recalculate Inside / Hollow Room (insideOut: true)
        MeshTopologyOps.RecalculateNormals(box, insideOut: true);
        Assert.True(box.GetTopology().IsClosedManifold());

        foreach (PolygonFace face in box.Faces)
        {
            Assert.True(Vector3.Dot(face.ComputeCentroid(box), face.ComputeNormal(box)) < 0.0f);
        }

        // 2. Test on Cylinder with scrambled face windings
        EditableMesh cylinder = PrimitiveGenerator.CreateCylinder(radius: 1.5f, height: 3.0f, radialSegments: 8);
        for (int i = 0; i < cylinder.Faces.Count; i += 2)
        {
            cylinder.Faces[i].VertexIds.Reverse();
        }
        cylinder.InvalidateTopology();

        Assert.False(cylinder.GetTopology().IsClosedManifold());

        MeshTopologyOps.RecalculateNormals(cylinder, insideOut: false);
        Assert.True(cylinder.GetTopology().IsClosedManifold());

        foreach (PolygonFace face in cylinder.Faces)
        {
            Assert.True(Vector3.Dot(face.ComputeCentroid(cylinder), face.ComputeNormal(cylinder)) > 0.0f);
        }
    }
}
