using System.Numerics;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Modifiers;
using Leed3D.Kernel.Primitives;
using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Selection;

namespace Leed3D.Tests.Modifiers;

public sealed class MeshModifiersTests
{
    [Fact]
    public void CloneObjects_DeepClonesSceneObjects_AssignsCopyName_AndUpdatesSelection()
    {
        var scene = new EditorScene("TestScene");
        var mesh1 = PrimitiveGenerator.CreateBox(new Vector3(1, 2, 3));
        var mesh2 = PrimitiveGenerator.CreateBox(new Vector3(4, 5, 6));

        var obj1 = scene.AddMeshObject("PillarA", mesh1, new Vector3(1, 0, 0));
        var obj2 = scene.AddMeshObject("PillarB", mesh2, new Vector3(5, 0, 0));

        var selection = new SelectionManager();
        selection.SelectMultipleObjects([obj1.Id, obj2.Id]);

        IReadOnlyList<SceneObject> clones = MeshModifiers.CloneObjects(scene, [obj1.Id, obj2.Id], selection);

        Assert.Equal(2, clones.Count);
        Assert.Equal("PillarA_Copy", clones[0].Name);
        Assert.Equal("PillarB_Copy", clones[1].Name);
        Assert.NotEqual(obj1.Id, clones[0].Id);
        Assert.NotEqual(obj2.Id, clones[1].Id);
        Assert.Equal(4, scene.ObjectCount);

        // Selection should now point to the clones
        Assert.Equal(2, selection.SelectedObjectIds.Count);
        Assert.Contains(clones[0].Id, selection.SelectedObjectIds);
        Assert.Contains(clones[1].Id, selection.SelectedObjectIds);

        // Deep clone verification: mutating clone position doesn't mutate original
        clones[0].Transform.Position = new Vector3(100, 0, 0);
        Assert.Equal(new Vector3(1, 0, 0), obj1.Transform.Position);
    }

    [Fact]
    public void CloneFaces_DuplicatesSelectedFacesAndVertices_KeepsThemConnected()
    {
        EditableMesh box = PrimitiveGenerator.CreateBox(new Vector3(2, 2, 2), materialId: "stone");
        Assert.Equal(8, box.VertexCount);
        Assert.Equal(6, box.FaceCount);

        // Select top face (+Y) and front face (+Z), which share 2 vertices along their common edge
        PolygonFace topFace = box.Faces.Single(f => f.ComputeNormal(box).Y > 0.9f);
        PolygonFace frontFace = box.Faces.Single(f => f.ComputeNormal(box).Z > 0.9f);

        var selection = new SelectionManager();
        selection.SetMode(InteractionMode.Face);

        IReadOnlyList<int> newFaceIds = MeshModifiers.CloneFaces(box, [topFace.Id, frontFace.Id], selection);

        Assert.Equal(2, newFaceIds.Count);
        Assert.Equal(8, box.FaceCount); // 6 + 2
        // Two quads sharing an edge have 6 unique vertices; 8 + 6 = 14
        Assert.Equal(14, box.VertexCount);

        // Selection updated to cloned faces
        Assert.Equal(2, selection.SelectedFaceIds.Count);
        Assert.Contains(newFaceIds[0], selection.SelectedFaceIds);
        Assert.Contains(newFaceIds[1], selection.SelectedFaceIds);

        PolygonFace cloneTop = box.GetFace(newFaceIds[0])!;
        PolygonFace cloneFront = box.GetFace(newFaceIds[1])!;

        // The two clones must share the 2 duplicated vertices along their shared edge
        int sharedCount = cloneTop.VertexIds.Intersect(cloneFront.VertexIds).Count();
        Assert.Equal(2, sharedCount);

        // None of the clone vertices should reference original vertices
        var originalVertIds = new HashSet<int>(topFace.VertexIds.Concat(frontFace.VertexIds));
        Assert.Empty(cloneTop.VertexIds.Intersect(originalVertIds));
        Assert.Empty(cloneFront.VertexIds.Intersect(originalVertIds));
    }

    [Theory]
    [InlineData(Axis3D.X)]
    [InlineData(Axis3D.Y)]
    [InlineData(Axis3D.Z)]
    public void MirrorMesh_SingleMesh_ReversesWindingAndPreservesOutwardNormals(Axis3D axis)
    {
        Vector3 size = new(2, 2, 2);
        EditableMesh box = PrimitiveGenerator.CreateBox(size, materialId: "wood");

        // Move box off-center along axis
        Vector3 offset = axis switch
        {
            Axis3D.X => new Vector3(3, 0, 0),
            Axis3D.Y => new Vector3(0, 3, 0),
            Axis3D.Z => new Vector3(0, 0, 3),
            _ => Vector3.Zero
        };

        foreach (MeshVertex v in box.Vertices)
        {
            v.Position += offset;
        }

        Vector3 expectedMirroredCenter = -offset;

        MeshModifiers.MirrorMesh(box, axis, planeCoordinate: 0f, keepOriginal: false);

        Assert.Equal(8, box.VertexCount);
        Assert.Equal(6, box.FaceCount);

        // Center should be reflected across 0
        Vector3 centroidSum = Vector3.Zero;
        foreach (PolygonFace f in box.Faces)
        {
            centroidSum += f.ComputeCentroid(box);
        }
        Vector3 boxCenter = centroidSum / box.FaceCount;
        Assert.True(Vector3.Distance(expectedMirroredCenter, boxCenter) < 1e-4f);

        // Crucial Invariant: All face normals must still point outward away from box center
        foreach (PolygonFace f in box.Faces)
        {
            Vector3 outward = f.ComputeCentroid(box) - boxCenter;
            Assert.True(Vector3.Dot(outward, f.ComputeNormal(box)) > 0.0f);
        }

        // Must still be a closed manifold
        Assert.True(box.GetTopology().IsClosedManifold());
    }

    [Fact]
    public void MirrorMesh_SymmetricModelingWithKeepOriginalAndWeld_CreatesClosedManifold()
    {
        // Build an open half-box on X in [0, 2], Y in [-1, 1], Z in [-1, 1], missing the X=0 face
        var mesh = new EditableMesh();

        // 4 seam vertices at X = 0
        int s0 = mesh.AddVertex(new Vector3(0, -1, -1));
        int s1 = mesh.AddVertex(new Vector3(0, -1,  1));
        int s2 = mesh.AddVertex(new Vector3(0,  1,  1));
        int s3 = mesh.AddVertex(new Vector3(0,  1, -1));

        // 4 outer vertices at X = 2
        int o0 = mesh.AddVertex(new Vector3(2, -1, -1));
        int o1 = mesh.AddVertex(new Vector3(2, -1,  1));
        int o2 = mesh.AddVertex(new Vector3(2,  1,  1));
        int o3 = mesh.AddVertex(new Vector3(2,  1, -1));

        // 5 outer faces (all normals pointing outward)
        mesh.AddFace([o0, o3, o2, o1]); // Right face (+X)
        mesh.AddFace([s3, s2, o2, o3]); // Top face (+Y)
        mesh.AddFace([s0, o0, o1, s1]); // Bottom face (-Y)
        mesh.AddFace([s1, o1, o2, s2]); // Front face (+Z)
        mesh.AddFace([s0, s3, o3, o0]); // Back face (-Z)

        Assert.Equal(8, mesh.VertexCount);
        Assert.Equal(5, mesh.FaceCount);
        Assert.False(mesh.GetTopology().IsClosedManifold()); // Open boundary at X=0

        // Mirror across X=0 with keepOriginal=true and weldThreshold=0.001
        MeshModifiers.MirrorMesh(mesh, Axis3D.X, planeCoordinate: 0f, keepOriginal: true, weldThreshold: 0.001f);

        // 4 seam vertices were welded, 4 outer vertices were duplicated to X=-2:
        // Total vertices = 4 (seam) + 4 (X=2) + 4 (X=-2) = 12
        Assert.Equal(12, mesh.VertexCount);
        // 5 original faces + 5 mirrored faces = 10 faces
        Assert.Equal(10, mesh.FaceCount);

        // The merged mesh forms a complete, closed manifold box!
        TopologyGraph topology = mesh.GetTopology();
        Assert.True(topology.IsClosedManifold());

        // Verify all 10 normals point outward from (0, 0, 0)
        foreach (PolygonFace f in mesh.Faces)
        {
            Vector3 centroid = f.ComputeCentroid(mesh);
            Vector3 normal = f.ComputeNormal(mesh);
            Assert.True(Vector3.Dot(centroid, normal) > 0.0f);
        }
    }

    [Fact]
    public void MirrorObject_MirrorsWorldPositionRotationAndMesh()
    {
        var scene = new EditorScene("TestScene");
        var mesh = PrimitiveGenerator.CreateBox(new Vector3(1, 1, 1));
        var obj = scene.AddMeshObject("Building", mesh, new Vector3(5, 2, -3));
        obj.Transform.RotationEulerDegrees = new Vector3(0, 45, 0);

        SceneObject? mirrored = MeshModifiers.MirrorObject(
            scene,
            obj.Id,
            Axis3D.X,
            worldPlaneCoordinate: 0f,
            keepOriginal: true);

        Assert.NotNull(mirrored);
        Assert.Equal("Building_Mirrored", mirrored.Name);
        Assert.Equal(2, scene.ObjectCount);

        // Position reflected across X=0: (5, 2, -3) -> (-5, 2, -3)
        Assert.Equal(new Vector3(-5, 2, -3), mirrored.Transform.Position);
        // Yaw 45 deg reflected across X plane -> -45 deg
        Assert.Equal(-45f, mirrored.Transform.RotationEulerDegrees.Y, 2);

        // Mirrored mesh remains manifold and points outward
        Assert.True(mirrored.Mesh!.GetTopology().IsClosedManifold());
    }

    [Fact]
    public void LinearArrayMesh_WholeMesh_GeneratesExactStairsOrCompoundMesh()
    {
        // Step box: 1m wide, 0.25m high, 0.5m deep
        EditableMesh stepBox = PrimitiveGenerator.CreateBox(new Vector3(1.0f, 0.25f, 0.5f));
        Assert.Equal(8, stepBox.VertexCount);
        Assert.Equal(6, stepBox.FaceCount);

        Vector3 stepDelta = new(0.0f, 0.25f, 0.5f);
        MeshModifiers.LinearArrayMesh(stepBox, count: 4, stepOffset: stepDelta);

        // 4 total steps: 8 * 4 = 32 vertices, 6 * 4 = 24 faces
        Assert.Equal(32, stepBox.VertexCount);
        Assert.Equal(24, stepBox.FaceCount);

        // Verify highest step (step 3) exists around Y = 3 * 0.25 = 0.75, Z = 3 * 0.5 = 1.5
        float maxY = stepBox.Vertices.Max(v => v.Position.Y);
        float maxZ = stepBox.Vertices.Max(v => v.Position.Z);
        Assert.Equal(0.75f + 0.125f, maxY, 3);
        Assert.Equal(1.5f + 0.25f, maxZ, 3);
    }

    [Fact]
    public void LinearArrayMesh_SubElementFaces_ArraysOnlySelectedFaces()
    {
        EditableMesh box = PrimitiveGenerator.CreateBox(new Vector3(2, 2, 2));
        PolygonFace topFace = box.Faces.Single(f => f.ComputeNormal(box).Y > 0.9f);

        MeshModifiers.LinearArrayMesh(box, count: 3, stepOffset: new Vector3(0, 1, 0), sourceFaceIds: [topFace.Id]);

        // Original 6 faces + 2 cloned top faces = 8 faces
        Assert.Equal(8, box.FaceCount);
        // Original 8 vertices + 4*2 = 16 vertices
        Assert.Equal(16, box.VertexCount);
    }

    [Fact]
    public void LinearArrayObjects_GeneratesArrayOfObjectsWithAccurateTransforms()
    {
        var scene = new EditorScene("TestScene");
        var mesh = PrimitiveGenerator.CreateBox(new Vector3(1, 1, 1));
        var originObj = scene.AddMeshObject("Block", mesh, new Vector3(0, 0, 0));

        IReadOnlyList<SceneObject> created = MeshModifiers.LinearArrayObjects(
            scene,
            [originObj.Id],
            count: 4,
            stepOffset: new Vector3(2, 0, 0));

        Assert.Equal(3, created.Count); // 3 additional objects (total 4)
        Assert.Equal(4, scene.ObjectCount);

        Assert.Equal(new Vector3(2, 0, 0), created[0].Transform.Position);
        Assert.Equal(new Vector3(4, 0, 0), created[1].Transform.Position);
        Assert.Equal(new Vector3(6, 0, 0), created[2].Transform.Position);

        Assert.Equal("Block_Array1", created[0].Name);
        Assert.Equal("Block_Array2", created[1].Name);
        Assert.Equal("Block_Array3", created[2].Name);
    }

    [Fact]
    public void RadialArrayMesh_RepeatsGeometryAroundPivotAndAxis()
    {
        // Single cube of size 0.5 centered at (2, 0, 0)
        EditableMesh mesh = PrimitiveGenerator.CreateBox(new Vector3(0.5f, 0.5f, 0.5f));
        foreach (MeshVertex v in mesh.Vertices)
        {
            v.Position += new Vector3(2, 0, 0);
        }

        // Radial array 4 times around (0, 0, 0) on Y axis (90 deg intervals)
        MeshModifiers.RadialArrayMesh(mesh, count: 4, pivot: Vector3.Zero, axis: Axis3D.Y, totalAngleDegrees: 360f);

        Assert.Equal(32, mesh.VertexCount); // 8 * 4
        Assert.Equal(24, mesh.FaceCount);   // 6 * 4

        // Check centroids of all 4 cubes
        var centroids = new List<Vector3>();
        for (int i = 0; i < 4; i++)
        {
            Vector3 sum = Vector3.Zero;
            for (int f = i * 6; f < (i + 1) * 6; f++)
            {
                sum += mesh.Faces[f].ComputeCentroid(mesh);
            }
            centroids.Add(sum / 6.0f);
        }

        // Expected positions at radius 2: (2, 0, 0), (0, 0, 2), (-2, 0, 0), (0, 0, -2)
        Assert.Contains(centroids, c => Vector3.Distance(c, new Vector3(2, 0, 0)) < 1e-4f);
        Assert.Contains(centroids, c => Vector3.Distance(c, new Vector3(0, 0, 2)) < 1e-4f);
        Assert.Contains(centroids, c => Vector3.Distance(c, new Vector3(-2, 0, 0)) < 1e-4f);
        Assert.Contains(centroids, c => Vector3.Distance(c, new Vector3(0, 0, -2)) < 1e-4f);
    }

    [Fact]
    public void RadialArrayObjects_CircularColonnadePillarsWithRotation()
    {
        var scene = new EditorScene("Colonnade");
        var pillarMesh = PrimitiveGenerator.CreateCylinder(radius: 0.5f, height: 4.0f, radialSegments: 8);
        var pillarObj = scene.AddMeshObject("Pillar", pillarMesh, new Vector3(4, 0, 0));

        IReadOnlyList<SceneObject> created = MeshModifiers.RadialArrayObjects(
            scene,
            [pillarObj.Id],
            count: 4,
            worldPivot: Vector3.Zero,
            axis: Axis3D.Y,
            totalAngleDegrees: 360f);

        Assert.Equal(3, created.Count);
        Assert.Equal(4, scene.ObjectCount);

        // Check positions at radius 4
        Assert.True(Vector3.Distance(new Vector3(0, 0, -4), created[0].Transform.Position) < 1e-4f);
        Assert.True(Vector3.Distance(new Vector3(-4, 0, 0), created[1].Transform.Position) < 1e-4f);
        Assert.True(Vector3.Distance(new Vector3(0, 0, 4), created[2].Transform.Position) < 1e-4f);

        // Check Yaw orientations rotated by 90, 180, 270 degrees
        Assert.Equal(90f, created[0].Transform.RotationEulerDegrees.Y, 1);
        Assert.Equal(180f, MathF.Abs(created[1].Transform.RotationEulerDegrees.Y), 1);
        Assert.Equal(-90f, created[2].Transform.RotationEulerDegrees.Y, 1);
    }
}
