using System.Drawing;
using System.Numerics;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Primitives;
using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Selection;
using Leed3D.Kernel.Viewport;
using Xunit;

namespace Leed3D.Tests.Selection;

public sealed class SelectionAndPickingTests
{
    [Fact]
    public void SelectionManager_SingleAndToggleObjectSelection_TracksSelectionCorrectly()
    {
        var sm = new SelectionManager();
        Assert.Equal(InteractionMode.Object, sm.Mode);
        Assert.False(sm.HasSelection);

        sm.SelectSingleObject(42);
        Assert.True(sm.HasSelection);
        Assert.Equal(42, sm.ActiveObjectId);
        Assert.Contains(42, sm.SelectedObjectIds);

        // Shift+Click toggle adds another object
        sm.ToggleSelectObject(99);
        Assert.Equal(2, sm.SelectedObjectIds.Count);
        Assert.Contains(42, sm.SelectedObjectIds);
        Assert.Contains(99, sm.SelectedObjectIds);
        Assert.Equal(99, sm.ActiveObjectId);

        // Shift+Click toggle deselects
        sm.ToggleSelectObject(42);
        Assert.Single(sm.SelectedObjectIds);
        Assert.DoesNotContain(42, sm.SelectedObjectIds);
        Assert.Contains(99, sm.SelectedObjectIds);
    }

    [Fact]
    public void SelectionManager_SubElementSelection_TracksVerticesEdgesAndFaces()
    {
        var sm = new SelectionManager();
        sm.SetMode(InteractionMode.Vertex);

        sm.SelectSingleVertex(1);
        sm.ToggleSelectVertex(2);
        Assert.Equal(2, sm.SelectedVertexIds.Count);
        Assert.Contains(1, sm.SelectedVertexIds);
        Assert.Contains(2, sm.SelectedVertexIds);

        sm.ToggleSelectVertex(1);
        Assert.Single(sm.SelectedVertexIds);
        Assert.DoesNotContain(1, sm.SelectedVertexIds);

        // Edge selection
        sm.SetMode(InteractionMode.Edge);
        var edge1 = new EdgeKey(0, 1);
        var edge2 = new EdgeKey(1, 2);
        sm.SelectSingleEdge(edge1);
        sm.ToggleSelectEdge(edge2);
        Assert.Equal(2, sm.SelectedEdges.Count);
        Assert.Contains(edge1, sm.SelectedEdges);
        Assert.Contains(edge2, sm.SelectedEdges);

        // Face selection
        sm.SetMode(InteractionMode.Face);
        sm.SelectSingleFace(10);
        sm.ToggleSelectFace(20);
        Assert.Equal(2, sm.SelectedFaceIds.Count);
        Assert.Contains(10, sm.SelectedFaceIds);
        Assert.Contains(20, sm.SelectedFaceIds);
    }

    [Fact]
    public void SelectionManager_SelectAllClearAndInvert_WorksInAllModes()
    {
        var scene = new EditorScene();
        var cubeObj = new SceneObject(1, "Cube", PrimitiveGenerator.CreateBox(new Vector3(2f, 2f, 2f)));
        var planeObj = new SceneObject(2, "Plane", PrimitiveGenerator.CreatePlane(2f, 2f));
        var lockedObj = new SceneObject(3, "Locked", PrimitiveGenerator.CreateBox(Vector3.One)) { Locked = true };
        scene.AddObject(cubeObj);
        scene.AddObject(planeObj);
        scene.AddObject(lockedObj);

        var sm = new SelectionManager();

        // Object Mode SelectAll
        sm.SelectAll(scene);
        Assert.Equal(2, sm.SelectedObjectIds.Count);
        Assert.Contains(1, sm.SelectedObjectIds);
        Assert.Contains(2, sm.SelectedObjectIds);
        Assert.DoesNotContain(3, sm.SelectedObjectIds); // Locked excluded

        // InvertSelection
        sm.SelectSingleObject(1);
        sm.InvertSelection(scene);
        Assert.Single(sm.SelectedObjectIds);
        Assert.Contains(2, sm.SelectedObjectIds);

        // ClearSelection
        sm.ClearSelection();
        Assert.Empty(sm.SelectedObjectIds);
        Assert.False(sm.HasSelection);

        // Edit Mode (Face Mode)
        sm.SelectSingleObject(1);
        sm.SetMode(InteractionMode.Face, scene);
        sm.SelectAll(scene);
        Assert.Equal(6, sm.SelectedFaceIds.Count);

        int firstFaceId = cubeObj.Mesh!.Faces[0].Id;
        sm.SelectSingleFace(firstFaceId);
        sm.InvertSelection(scene);
        Assert.Equal(5, sm.SelectedFaceIds.Count);
        Assert.DoesNotContain(firstFaceId, sm.SelectedFaceIds);

        sm.ClearSelection();
        Assert.Empty(sm.SelectedFaceIds);
    }

    [Fact]
    public void SelectionManager_ModeConversion_ConvertsCorrectlyBetweenSubElements()
    {
        var scene = new EditorScene();
        EditableMesh boxMesh = PrimitiveGenerator.CreateBox(new Vector3(2f, 2f, 2f));
        var obj = new SceneObject(1, "Cube", boxMesh);
        scene.AddObject(obj);

        var sm = new SelectionManager();
        sm.SelectSingleObject(1);

        // Select Front Face
        sm.SetMode(InteractionMode.Face, scene);
        int faceId = boxMesh.Faces[0].Id;
        sm.SelectSingleFace(faceId);
        PolygonFace face0 = boxMesh.GetFace(faceId)!;

        // Convert Face -> Vertex
        sm.SetMode(InteractionMode.Vertex, scene);
        Assert.Equal(face0.VertexIds.Count, sm.SelectedVertexIds.Count);
        foreach (int vId in face0.VertexIds)
        {
            Assert.Contains(vId, sm.SelectedVertexIds);
        }

        // Convert Vertex -> Face (since all vertices of Face 0 are selected, Face 0 should be selected)
        sm.SetMode(InteractionMode.Face, scene);
        Assert.Contains(faceId, sm.SelectedFaceIds);

        // Convert Face -> Edge
        sm.SetMode(InteractionMode.Edge, scene);
        Assert.Equal(4, sm.SelectedEdges.Count); // A quad has 4 edges

        // Convert Edge -> Face
        sm.SetMode(InteractionMode.Face, scene);
        Assert.Contains(faceId, sm.SelectedFaceIds);
    }

    [Fact]
    public void SelectionManager_GetSelectionPivotWorld_ComputesAccurateCentroids()
    {
        var scene = new EditorScene();
        var objA = new SceneObject(1, "ObjA", PrimitiveGenerator.CreateBox(Vector3.One))
        {
            Transform = new Transform3D { Position = new Vector3(-10f, 0f, 0f) }
        };
        var objB = new SceneObject(2, "ObjB", PrimitiveGenerator.CreateBox(Vector3.One))
        {
            Transform = new Transform3D { Position = new Vector3(10f, 0f, 0f) }
        };
        scene.AddObject(objA);
        scene.AddObject(objB);

        var sm = new SelectionManager();
        Assert.Null(sm.GetSelectionPivotWorld(scene));

        sm.SelectSingleObject(1);
        sm.ToggleSelectObject(2);
        Vector3? objectCentroid = sm.GetSelectionPivotWorld(scene);
        Assert.NotNull(objectCentroid);
        Assert.True(Vector3.Distance(Vector3.Zero, objectCentroid.Value) < 1e-4f);

        // Sub-element pivot: Vertex mode
        sm.SetMode(InteractionMode.Vertex, scene);
        sm.ClearSelection();
        sm.ActiveObjectId = 1;

        // objA vertices centered at -10f on X
        sm.SelectAll(scene);
        Vector3? vertCentroid = sm.GetSelectionPivotWorld(scene);
        Assert.NotNull(vertCentroid);
        Assert.True(Vector3.Distance(new Vector3(-10f, 0f, 0f), vertCentroid.Value) < 1e-4f);
    }

    [Fact]
    public void MeshPicker_PickObject_HitsCubeAndMissesEmptySpace()
    {
        var scene = new EditorScene();
        var cube = new SceneObject(1, "Cube", PrimitiveGenerator.CreateBox(new Vector3(2f, 2f, 2f)))
        {
            Transform = new Transform3D { Position = Vector3.Zero }
        };
        scene.AddObject(cube);

        // Ray shooting from camera at (0, 0, -5) directly toward origin (+Z direction)
        var hitRay = new Ray3D(new Vector3(0f, 0f, -5f), Vector3.UnitZ);
        ObjectHit? hit = MeshPicker.PickObject(scene, hitRay);

        Assert.NotNull(hit);
        Assert.Equal(1, hit.Value.ObjectId);
        // Cube extends from -1 to +1 on Z, camera is at -5, so front face is at -1, dist = 4
        Assert.Equal(4.0f, hit.Value.Distance, 3);
        Assert.True(Vector3.Distance(new Vector3(0f, 0f, -1f), hit.Value.HitPoint) < 1e-3f);

        // Ray missing the cube
        var missRay = new Ray3D(new Vector3(10f, 10f, -5f), Vector3.UnitZ);
        ObjectHit? miss = MeshPicker.PickObject(scene, missRay);
        Assert.Null(miss);
    }

    [Fact]
    public void MeshPicker_PickFace_CullsBackfaceWhenSpecified()
    {
        var cube = new SceneObject(1, "Cube", PrimitiveGenerator.CreateBox(new Vector3(2f, 2f, 2f)));

        // Front-facing ray towards cube front face
        var frontRay = new Ray3D(new Vector3(0f, 0f, -5f), Vector3.UnitZ);
        FaceHit? hitFront = MeshPicker.PickFace(cube, frontRay, ignoreBackfaces: true);
        Assert.NotNull(hitFront);

        // Inside the cube shooting outward (ray starts at (0, 0, 0) towards +Z)
        // With ignoreBackfaces = true, hitting the back of the +Z face (whose outward normal is +Z, same as ray)
        // det in Möller-Trumbore is < 0, so it gets culled
        var insideRay = new Ray3D(Vector3.Zero, Vector3.UnitZ);
        FaceHit? hitCulled = MeshPicker.PickFace(cube, insideRay, ignoreBackfaces: true);
        Assert.Null(hitCulled);

        // With ignoreBackfaces = false, it should hit
        FaceHit? hitInside = MeshPicker.PickFace(cube, insideRay, ignoreBackfaces: false);
        Assert.NotNull(hitInside);
        Assert.Equal(1.0f, hitInside.Value.Distance, 3);
    }

    [Fact]
    public void MeshPicker_PickVertexAndPickEdge_IdentifiesClosestSubElements()
    {
        var cube = new SceneObject(1, "Cube", PrimitiveGenerator.CreateBox(new Vector3(2f, 2f, 2f)));

        // Synthetic world-to-screen projection mapping (X, Y) directly to 2D screen coordinates
        // World (0,0) -> Screen (400, 300) with 50 px/unit
        Vector2? WorldToScreen(Vector3 p)
        {
            if (p.Z > 5f) return null; // Simple clip
            return new Vector2(400f + (p.X * 50f), 300f - (p.Y * 50f));
        }

        // Vertex at (1, 1, -1) maps to screen (400 + 50, 300 - 50) = (450, 250)
        Vector2 mouseNearVertex = new(452f, 248f); // 2.8 pixels away
        int? pickedVert = MeshPicker.PickVertex(cube, WorldToScreen, mouseNearVertex, maxPixelDistance: 10f);
        Assert.NotNull(pickedVert);

        MeshVertex? v = cube.Mesh!.GetVertex(pickedVert.Value);
        Assert.NotNull(v);
        Assert.Equal(1f, v.Position.X, 2);
        Assert.Equal(1f, v.Position.Y, 2);

        // Edge along top-front from (-1, 1, -1) to (1, 1, -1):
        // Screen segment from (350, 250) to (450, 250) at Y=250.
        // Pick mouse at (400, 253) -> 3 pixels away from edge
        Vector2 mouseNearEdge = new(400f, 253f);
        var ray = new Ray3D(new Vector3(0f, 0f, -5f), Vector3.UnitZ);
        EdgeKey? pickedEdge = MeshPicker.PickEdge(cube, ray, WorldToScreen, mouseNearEdge, maxPixelDistance: 10f);
        Assert.NotNull(pickedEdge);

        MeshVertex? vA = cube.Mesh.GetVertex(pickedEdge.Value.VMin);
        MeshVertex? vB = cube.Mesh.GetVertex(pickedEdge.Value.VMax);
        Assert.NotNull(vA);
        Assert.NotNull(vB);
        Assert.Equal(1f, vA.Position.Y, 2);
        Assert.Equal(1f, vB.Position.Y, 2);
    }

    [Fact]
    public void MeshPicker_PickBoxMarquee_SelectsContainedSubElements()
    {
        var cube = new SceneObject(1, "Cube", PrimitiveGenerator.CreateBox(new Vector3(2f, 2f, 2f)));

        Vector2? WorldToScreen(Vector3 p)
        {
            return new Vector2(400f + (p.X * 50f), 300f - (p.Y * 50f));
        }

        // Box around the right half of the screen (X >= 400, i.e., world X >= 0)
        var screenBox = new RectangleF(410f, 200f, 100f, 200f);

        // Vertices
        MarqueeSelectionResult vertResult = MeshPicker.PickBoxMarquee(cube, InteractionMode.Vertex, screenBox, WorldToScreen);
        Assert.NotEmpty(vertResult.VertexIds);
        foreach (int vId in vertResult.VertexIds)
        {
            MeshVertex? v = cube.Mesh!.GetVertex(vId);
            Assert.True(v!.Position.X >= 0f);
        }

        // Marquee for faces: only faces whose projected centroid is inside the box
        MarqueeSelectionResult faceResult = MeshPicker.PickBoxMarquee(cube, InteractionMode.Face, screenBox, WorldToScreen);
        // Right face (+X) centroid is at (1, 0, 0) -> screen (450, 300) which is inside screenBox
        Assert.Contains(faceResult.FaceIds, id =>
        {
            PolygonFace f = cube.Mesh!.GetFace(id)!;
            return f.ComputeCentroid(cube.Mesh).X > 0.5f;
        });
    }

    [Fact]
    public void EditorCamera_WorldToScreenAndRaycast_IntegratesCorrectly()
    {
        var camera = new EditorCamera
        {
            Position = new Vector3(0f, 0f, -5f),
            Target = Vector3.Zero,
            YawDegrees = 0f,
            PitchDegrees = 0f
        };
        camera.SetProjectionMode(ViewportProjectionMode.Perspective);

        Vector2 viewportSize = new(800f, 600f);

        // Screen center (400, 300) should generate ray straight toward target (+Z)
        Ray3D centerRay = camera.ScreenPointToRay(new Vector2(400f, 300f), viewportSize);
        Assert.Equal(0f, centerRay.Direction.X, 3);
        Assert.Equal(0f, centerRay.Direction.Y, 3);
        Assert.True(centerRay.Direction.Z > 0.99f);

        // Origin (0,0,0) in world space projects to screen center
        Vector2? screenPos = camera.WorldToScreenPoint(Vector3.Zero, viewportSize);
        Assert.NotNull(screenPos);
        Assert.Equal(400f, screenPos.Value.X, 1);
        Assert.Equal(300f, screenPos.Value.Y, 1);
    }

    [Fact]
    public void MeshPicker_PickObjectsBoxMarquee_SelectsObjectsInsideScreenRect()
    {
        var scene = new EditorScene();
        var objLeft = new SceneObject(1, "Left", PrimitiveGenerator.CreateBox(Vector3.One))
        {
            Transform = new Transform3D { Position = new Vector3(-5f, 0f, 0f) }
        };
        var objRight = new SceneObject(2, "Right", PrimitiveGenerator.CreateBox(Vector3.One))
        {
            Transform = new Transform3D { Position = new Vector3(5f, 0f, 0f) }
        };
        scene.AddObject(objLeft);
        scene.AddObject(objRight);

        Vector2? WorldToScreen(Vector3 p)
        {
            return new Vector2(400f + (p.X * 50f), 300f - (p.Y * 50f));
        }

        // Marquee encompassing only right object (centered at 400 + 250 = 650)
        var rightRect = new RectangleF(600f, 250f, 100f, 100f);
        List<int> picked = MeshPicker.PickObjectsBoxMarquee(scene, rightRect, WorldToScreen);
        Assert.Single(picked);
        Assert.Contains(2, picked);
    }

    [Fact]
    public void MeshPicker_PickObject_ReturnsClosestWhenObjectsOverlapInRay()
    {
        var scene = new EditorScene();
        var frontCube = new SceneObject(1, "Front", PrimitiveGenerator.CreateBox(new Vector3(2f, 2f, 2f)))
        {
            Transform = new Transform3D { Position = new Vector3(0f, 0f, 0f) }
        };
        var backCube = new SceneObject(2, "Back", PrimitiveGenerator.CreateBox(new Vector3(2f, 2f, 2f)))
        {
            Transform = new Transform3D { Position = new Vector3(0f, 0f, 10f) }
        };
        scene.AddObject(frontCube);
        scene.AddObject(backCube);

        var ray = new Ray3D(new Vector3(0f, 0f, -5f), Vector3.UnitZ);
        ObjectHit? hit = MeshPicker.PickObject(scene, ray);

        Assert.NotNull(hit);
        Assert.Equal(1, hit.Value.ObjectId); // Front object hit first
    }

    [Fact]
    public void SelectionManager_ToggleObjectEditMode_TogglesCorrectly()
    {
        var scene = new EditorScene();
        var obj = new SceneObject(1, "Cube", PrimitiveGenerator.CreateBox(Vector3.One));
        scene.AddObject(obj);

        var sm = new SelectionManager();
        sm.SelectSingleObject(1);
        Assert.Equal(InteractionMode.Object, sm.Mode);

        sm.ToggleObjectEditMode(scene);
        Assert.Equal(InteractionMode.Face, sm.Mode);
        Assert.Equal(1, sm.ActiveObjectId);

        sm.ToggleObjectEditMode(scene);
        Assert.Equal(InteractionMode.Object, sm.Mode);
    }

    [Fact]
    public void SelectionManager_GetSelectionPivotWorld_WorksForEdgeAndFace()
    {
        var scene = new EditorScene();
        EditableMesh boxMesh = PrimitiveGenerator.CreateBox(new Vector3(2f, 2f, 2f));
        var obj = new SceneObject(1, "Cube", boxMesh)
        {
            Transform = new Transform3D { Position = new Vector3(10f, 20f, 30f) }
        };
        scene.AddObject(obj);

        var sm = new SelectionManager();
        sm.SelectSingleObject(1);

        // Face mode
        sm.SetMode(InteractionMode.Face, scene);
        sm.SelectAll(scene); // All 6 faces centered at box origin
        Vector3? facePivot = sm.GetSelectionPivotWorld(scene);
        Assert.NotNull(facePivot);
        Assert.True(Vector3.Distance(new Vector3(10f, 20f, 30f), facePivot.Value) < 1e-4f);

        // Edge mode
        sm.SetMode(InteractionMode.Edge, scene);
        sm.SelectAll(scene);
        Vector3? edgePivot = sm.GetSelectionPivotWorld(scene);
        Assert.NotNull(edgePivot);
        Assert.True(Vector3.Distance(new Vector3(10f, 20f, 30f), edgePivot.Value) < 1e-4f);
    }
}
