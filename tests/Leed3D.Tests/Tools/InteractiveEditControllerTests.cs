using System.Numerics;
using Leed3D.Kernel.Csg;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Modifiers;
using Leed3D.Kernel.Primitives;
using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Selection;
using Leed3D.Kernel.Tools;

namespace Leed3D.Tests.Tools;

public sealed class InteractiveEditControllerTests
{
    private static (EditorScene Scene, SceneObject Object, SelectionManager Selection, UndoRedoStack UndoStack, InteractiveEditController Controller) SetupBoxScene()
    {
        var scene = new EditorScene("Test");
        var mesh = PrimitiveGenerator.CreateBox(new BoxParams(new Vector3(2f, 2f, 2f)));
        SceneObject obj = scene.AddMeshObject("Box", mesh, Vector3.Zero);
        var selection = new SelectionManager();
        selection.SelectSingleObject(obj.Id);
        var undo = new UndoRedoStack();
        var controller = new InteractiveEditController();
        return (scene, obj, selection, undo, controller);
    }

    [Fact]
    public void ExtrudeFaces_StartDragConfirm_CreatesNewFacesAndUndoEntry()
    {
        var (scene, obj, selection, undo, controller) = SetupBoxScene();
        selection.SetMode(InteractionMode.Face, scene);
        int targetFaceId = obj.Mesh!.Faces[0].Id;
        selection.SelectSingleFace(targetFaceId);

        int initialFaceCount = obj.Mesh!.FaceCount;
        var initialRay = new Ray3D(new Vector3(0, 5, 0), -Vector3.UnitY);
        bool started = controller.StartExtrude(scene, selection, initialRay, -Vector3.UnitZ, undo);
        Assert.True(started);
        Assert.True(controller.IsExtrudeActive);
        Assert.Equal(ActiveModalEditTool.Extrude, controller.ActiveTool);

        // Drag along normal
        controller.SetExtrudeDistance(scene, 1.5f);
        Assert.Equal(1.5f, controller.ExtrudeDistance);

        controller.ConfirmExtrude(scene, undo);
        Assert.False(controller.IsExtrudeActive);
        Assert.Equal(ActiveModalEditTool.None, controller.ActiveTool);

        obj = scene.GetObject(obj.Id)!;
        Assert.True(obj.Mesh!.FaceCount > initialFaceCount);
        Assert.True(undo.CanUndo);

        // Undo restores original face count
        undo.Undo(scene);
        obj = scene.GetObject(obj.Id)!;
        Assert.Equal(initialFaceCount, obj.Mesh!.FaceCount);

        // Redo restores extrusion
        undo.Redo(scene);
        obj = scene.GetObject(obj.Id)!;
        Assert.True(obj.Mesh!.FaceCount > initialFaceCount);
    }

    [Fact]
    public void ExtrudeFaces_Cancel_RollsBackWithoutUndoPollution()
    {
        var (scene, obj, selection, undo, controller) = SetupBoxScene();
        selection.SetMode(InteractionMode.Face, scene);
        int targetFaceId = obj.Mesh!.Faces[0].Id;
        selection.SelectSingleFace(targetFaceId);

        int initialFaceCount = obj.Mesh!.FaceCount;
        var initialRay = new Ray3D(new Vector3(0, 5, 0), -Vector3.UnitY);
        controller.StartExtrude(scene, selection, initialRay, -Vector3.UnitZ, undo);
        controller.SetExtrudeDistance(scene, 2.0f);

        controller.CancelExtrude(scene, undo);
        Assert.False(controller.IsExtrudeActive);

        obj = scene.GetObject(obj.Id)!;
        Assert.Equal(initialFaceCount, obj.Mesh!.FaceCount);
        Assert.False(undo.CanUndo);
    }

    [Fact]
    public void ExtrudeFaces_NumericInput_UpdatesDistanceCorrectly()
    {
        var (scene, obj, selection, undo, controller) = SetupBoxScene();
        selection.SetMode(InteractionMode.Face, scene);
        int targetFaceId = obj.Mesh!.Faces[0].Id;
        selection.SelectSingleFace(targetFaceId);

        controller.StartExtrude(scene, selection, new Ray3D(Vector3.Zero, Vector3.UnitY), Vector3.UnitZ, undo);

        controller.HandleNumericKey('2', scene);
        controller.HandleNumericKey('.', scene);
        controller.HandleNumericKey('5', scene);

        Assert.Equal(2.5f, controller.ExtrudeDistance);
        Assert.Equal("2.5", controller.NumericInputBuffer);

        controller.HandleBackspace(scene);
        Assert.Equal("2.", controller.NumericInputBuffer);
        Assert.Equal(2.0f, controller.ExtrudeDistance);

        controller.ConfirmExtrude(scene, undo);
        Assert.True(undo.CanUndo);
    }

    [Fact]
    public void InsetFaces_StartAdjustConfirm_CreatesInnerFacesAndUndoEntry()
    {
        var (scene, obj, selection, undo, controller) = SetupBoxScene();
        selection.SetMode(InteractionMode.Face, scene);
        int targetFaceId = obj.Mesh!.Faces[0].Id;
        selection.SelectSingleFace(targetFaceId);

        int initialFaceCount = obj.Mesh!.FaceCount;
        bool started = controller.StartInset(scene, selection, new Vector2(100, 100), undo);
        Assert.True(started);
        Assert.True(controller.IsInsetActive);

        controller.SetInsetParameters(scene, 0.25f, 0.1f, false);
        Assert.Equal(0.25f, controller.InsetThickness);
        Assert.Equal(0.1f, controller.InsetDepth);

        controller.ConfirmInset(scene, undo);
        Assert.False(controller.IsInsetActive);

        obj = scene.GetObject(obj.Id)!;
        Assert.True(obj.Mesh!.FaceCount > initialFaceCount);
        Assert.True(undo.CanUndo);

        undo.Undo(scene);
        obj = scene.GetObject(obj.Id)!;
        Assert.Equal(initialFaceCount, obj.Mesh!.FaceCount);
    }

    [Fact]
    public void InsetFaces_Cancel_RestoresInitialMeshState()
    {
        var (scene, obj, selection, undo, controller) = SetupBoxScene();
        selection.SetMode(InteractionMode.Face, scene);
        int targetFaceId = obj.Mesh!.Faces[0].Id;
        selection.SelectSingleFace(targetFaceId);

        int initialFaceCount = obj.Mesh!.FaceCount;
        controller.StartInset(scene, selection, Vector2.Zero, undo);
        controller.SetInsetParameters(scene, 0.3f, 0.2f, false);

        controller.CancelInset(scene, undo);
        Assert.False(controller.IsInsetActive);

        obj = scene.GetObject(obj.Id)!;
        Assert.Equal(initialFaceCount, obj.Mesh!.FaceCount);
        Assert.False(undo.CanUndo);
    }

    [Fact]
    public void ExecuteWeldCenter_MergesSelectedVertices()
    {
        var (scene, obj, selection, undo, controller) = SetupBoxScene();
        selection.SetMode(InteractionMode.Vertex, scene);
        int initialVertCount = obj.Mesh!.VertexCount;

        // Select first 2 vertices
        int v0 = obj.Mesh!.Vertices[0].Id;
        int v1 = obj.Mesh!.Vertices[1].Id;
        selection.SelectSingleVertex(v0);
        selection.ToggleSelectVertex(v1);

        bool success = controller.ExecuteWeldCenter(scene, selection, undo);
        Assert.True(success);

        obj = scene.GetObject(obj.Id)!;
        Assert.Equal(initialVertCount - 1, obj.Mesh!.VertexCount);
        Assert.True(undo.CanUndo);

        undo.Undo(scene);
        obj = scene.GetObject(obj.Id)!;
        Assert.Equal(initialVertCount, obj.Mesh!.VertexCount);
    }

    [Fact]
    public void ExecuteConnectVertices_SplitsFaceBetweenTwoVertices()
    {
        var (scene, obj, selection, undo, controller) = SetupBoxScene();
        selection.SetMode(InteractionMode.Vertex, scene);
        int initialFaceCount = obj.Mesh!.FaceCount;

        // On box face 0 (4 vertices: v0, v1, v2, v3), connect opposite vertices v0 and v2
        PolygonFace face0 = obj.Mesh!.Faces[0];
        selection.SelectSingleVertex(face0.VertexIds[0]);
        selection.ToggleSelectVertex(face0.VertexIds[2]);

        bool success = controller.ExecuteConnectVertices(scene, selection, undo);
        Assert.True(success);

        obj = scene.GetObject(obj.Id)!;
        Assert.Equal(initialFaceCount + 1, obj.Mesh!.FaceCount);
        Assert.True(undo.CanUndo);

        undo.Undo(scene);
        obj = scene.GetObject(obj.Id)!;
        Assert.Equal(initialFaceCount, obj.Mesh!.FaceCount);
    }

    [Fact]
    public void ExecuteFillFace_CreatesNewFaceFromVertices()
    {
        var (scene, _, selection, undo, controller) = SetupBoxScene();
        var emptyMesh = new EditableMesh();
        int v0 = emptyMesh.AddVertex(new Vector3(0, 0, 0));
        int v1 = emptyMesh.AddVertex(new Vector3(1, 0, 0));
        int v2 = emptyMesh.AddVertex(new Vector3(1, 0, 1));
        var obj = scene.AddMeshObject("Triangle", emptyMesh, Vector3.Zero);

        selection.SelectSingleObject(obj.Id);
        selection.SetMode(InteractionMode.Vertex, scene);
        selection.SelectSingleVertex(v0);
        selection.ToggleSelectVertex(v1);
        selection.ToggleSelectVertex(v2);

        bool success = controller.ExecuteFillFace(scene, selection, undo);
        Assert.True(success);

        obj = scene.GetObject(obj.Id)!;
        Assert.Equal(1, obj.Mesh!.FaceCount);
        Assert.True(undo.CanUndo);

        undo.Undo(scene);
        obj = scene.GetObject(obj.Id)!;
        Assert.Equal(0, obj.Mesh!.FaceCount);
    }

    [Fact]
    public void ExecuteDeleteFaces_RemovesSelectedFaces()
    {
        var (scene, obj, selection, undo, controller) = SetupBoxScene();
        selection.SetMode(InteractionMode.Face, scene);
        int targetFaceId = obj.Mesh!.Faces[0].Id;
        selection.SelectSingleFace(targetFaceId);

        int initialFaceCount = obj.Mesh!.FaceCount;
        bool success = controller.ExecuteDeleteFaces(scene, selection, undo);
        Assert.True(success);

        obj = scene.GetObject(obj.Id)!;
        Assert.Equal(initialFaceCount - 1, obj.Mesh!.FaceCount);
        Assert.True(undo.CanUndo);

        undo.Undo(scene);
        obj = scene.GetObject(obj.Id)!;
        Assert.Equal(initialFaceCount, obj.Mesh!.FaceCount);
    }

    [Fact]
    public void ExecuteDissolveFaces_MergesAdjacentCoplanarFaces()
    {
        var scene = new EditorScene("PlaneTest");
        var planeMesh = PrimitiveGenerator.CreatePlane(new PlaneParams(4f, 4f, 2, 2));
        SceneObject planeObj = scene.AddMeshObject("Plane", planeMesh, Vector3.Zero);
        var selection = new SelectionManager();
        selection.SelectSingleObject(planeObj.Id);
        selection.SetMode(InteractionMode.Face, scene);

        int initialFaceCount = planeObj.Mesh!.FaceCount;
        Assert.Equal(4, initialFaceCount);

        // Select faces 0 and 1 from planeObj
        int f0 = planeObj.Mesh!.Faces[0].Id;
        int f1 = planeObj.Mesh!.Faces[1].Id;
        selection.SelectSingleFace(f0);
        selection.ToggleSelectFace(f1);

        var undo = new UndoRedoStack();
        var controller = new InteractiveEditController();
        bool success = controller.ExecuteDissolveFaces(scene, selection, undo);
        Assert.True(success);

        planeObj = scene.GetObject(planeObj.Id)!;
        Assert.Equal(3, planeObj.Mesh!.FaceCount);
        Assert.True(undo.CanUndo);

        undo.Undo(scene);
        planeObj = scene.GetObject(planeObj.Id)!;
        Assert.Equal(4, planeObj.Mesh!.FaceCount);
    }

    [Fact]
    public void ExecuteFlipAndRecalculateNormals_ModifiesMeshWinding()
    {
        var (scene, obj, selection, undo, controller) = SetupBoxScene();
        selection.SetMode(InteractionMode.Face, scene);
        int targetFaceId = obj.Mesh!.Faces[0].Id;
        selection.SelectSingleFace(targetFaceId);

        PolygonFace beforeFace = obj.Mesh!.GetFace(targetFaceId)!;
        Vector3 beforeNormal = beforeFace.ComputeNormal(obj.Mesh!);

        bool flipSuccess = controller.ExecuteFlipNormals(scene, selection, undo);
        Assert.True(flipSuccess);

        obj = scene.GetObject(obj.Id)!;
        PolygonFace flippedFace = obj.Mesh!.GetFace(targetFaceId)!;
        Vector3 flippedNormal = flippedFace.ComputeNormal(obj.Mesh!);
        Assert.True(Vector3.Dot(beforeNormal, flippedNormal) < -0.9f);

        bool recalcSuccess = controller.ExecuteRecalculateNormals(scene, selection, insideOut: false, undo);
        Assert.True(recalcSuccess);
        Assert.True(undo.CanUndo);
    }

    [Fact]
    public void ExecuteQuickClone_ClonesObjectsAndFaces()
    {
        var (scene, obj, selection, undo, controller) = SetupBoxScene();

        // Object mode clone
        int initialObjCount = scene.ObjectCount;
        bool objCloneSuccess = controller.ExecuteQuickClone(scene, selection, undo);
        Assert.True(objCloneSuccess);
        Assert.Equal(initialObjCount + 1, scene.ObjectCount);
        Assert.True(undo.CanUndo);

        undo.Undo(scene);
        Assert.Equal(initialObjCount, scene.ObjectCount);

        // Face mode clone
        obj = scene.GetObject(obj.Id)!;
        selection.SelectSingleObject(obj.Id);
        selection.SetMode(InteractionMode.Face, scene);
        int targetFaceId = obj.Mesh!.Faces[0].Id;
        selection.SelectSingleFace(targetFaceId);
        int initialFaceCount = obj.Mesh!.FaceCount;

        bool faceCloneSuccess = controller.ExecuteQuickClone(scene, selection, undo);
        Assert.True(faceCloneSuccess);

        obj = scene.GetObject(obj.Id)!;
        Assert.Equal(initialFaceCount + 1, obj.Mesh!.FaceCount);
    }

    [Fact]
    public void ExecuteMirror_AppliesSymmetry()
    {
        var (scene, obj, selection, undo, controller) = SetupBoxScene();
        selection.SetMode(InteractionMode.Face, scene);

        int initialFaceCount = obj.Mesh!.FaceCount;
        bool success = controller.ExecuteMirror(scene, selection, Axis3D.X, 0f, keepOriginal: true, weldThreshold: 0.001f, undo);
        Assert.True(success);

        obj = scene.GetObject(obj.Id)!;
        Assert.True(obj.Mesh!.FaceCount > initialFaceCount);
        Assert.True(undo.CanUndo);

        undo.Undo(scene);
        obj = scene.GetObject(obj.Id)!;
        Assert.Equal(initialFaceCount, obj.Mesh!.FaceCount);
    }

    [Fact]
    public void ExecuteLinearAndRadialArray_CreatesRepetitions()
    {
        var (scene, obj, selection, undo, controller) = SetupBoxScene();

        // Linear array in object mode
        int initialObjCount = scene.ObjectCount;
        bool linearSuccess = controller.ExecuteLinearArray(scene, selection, 3, new Vector3(3f, 0, 0), undo);
        Assert.True(linearSuccess);
        Assert.Equal(initialObjCount + 2, scene.ObjectCount);

        undo.Undo(scene);
        Assert.Equal(initialObjCount, scene.ObjectCount);

        // Radial array in object mode
        selection.SelectSingleObject(obj.Id);
        bool radialSuccess = controller.ExecuteRadialArray(scene, selection, 4, Axis3D.Y, Vector3.Zero, 360f, undo);
        Assert.True(radialSuccess);
        Assert.Equal(initialObjCount + 3, scene.ObjectCount);

        undo.Undo(scene);
        Assert.Equal(initialObjCount, scene.ObjectCount);
    }

    [Fact]
    public void ExecuteCsgBoolean_SubtractDoorwayBox_CarvesWallAndSupportsUndoRedo()
    {
        var scene = new EditorScene("CSG Test");
        var wallMesh = PrimitiveGenerator.CreateBox(new Vector3(10f, 4f, 0.5f));
        SceneObject wallObj = scene.AddMeshObject("Wall", wallMesh, new Vector3(0f, 2f, 0f));

        var doorMesh = PrimitiveGenerator.CreateBox(new Vector3(2f, 2.5f, 2f));
        SceneObject doorObj = scene.AddMeshObject("DoorCutter", doorMesh, new Vector3(0f, 1.25f, 0f));

        var selection = new SelectionManager();
        selection.SelectSingleObject(wallObj.Id);
        selection.ToggleSelectObject(doorObj.Id);
        // Active object is the cutter (doorObj)
        selection.ActiveObjectId = doorObj.Id;

        var undo = new UndoRedoStack();
        var controller = new InteractiveEditController();

        int initialWallFaceCount = wallObj.Mesh!.FaceCount;
        Assert.Equal(2, scene.ObjectCount);

        // Execute Subtract with deleteCutter = true
        bool success = controller.ExecuteCsgBoolean(scene, selection, CsgOperation.Subtract, deleteCutter: true, undo);
        Assert.True(success);

        // Door cutter should be removed, leaving 1 object in scene
        Assert.Equal(1, scene.ObjectCount);
        SceneObject updatedWall = scene.GetObject(wallObj.Id)!;
        Assert.NotNull(updatedWall);
        // Wall face count should have changed (door cutout has jambs and U-shaped walls)
        Assert.True(updatedWall.Mesh!.FaceCount > initialWallFaceCount);

        // Undo restores door cutter and original uncarved wall
        Assert.True(undo.CanUndo);
        undo.Undo(scene);

        Assert.Equal(2, scene.ObjectCount);
        SceneObject restoredDoor = scene.GetObject(doorObj.Id)!;
        Assert.NotNull(restoredDoor);
        SceneObject restoredWall = scene.GetObject(wallObj.Id)!;
        Assert.Equal(initialWallFaceCount, restoredWall.Mesh!.FaceCount);

        // Redo re-executes subtraction
        Assert.True(undo.CanRedo);
        undo.Redo(scene);

        Assert.Equal(1, scene.ObjectCount);
        SceneObject redoneWall = scene.GetObject(wallObj.Id)!;
        Assert.True(redoneWall.Mesh!.FaceCount > initialWallFaceCount);
    }

    [Fact]
    public void ExecuteCsgBoolean_UnionAndIntersect_ModifyMeshesAndSupportUndo()
    {
        // 1. Union test
        var sceneUnion = new EditorScene("Union Test");
        var boxA = PrimitiveGenerator.CreateBox(new Vector3(4f, 4f, 4f));
        SceneObject objA = sceneUnion.AddMeshObject("BoxA", boxA, Vector3.Zero);
        var boxB = PrimitiveGenerator.CreateBox(new Vector3(4f, 4f, 4f));
        SceneObject objB = sceneUnion.AddMeshObject("BoxB", boxB, new Vector3(2f, 0f, 0f));

        var selUnion = new SelectionManager();
        selUnion.SelectSingleObject(objA.Id);
        selUnion.ToggleSelectObject(objB.Id);
        selUnion.ActiveObjectId = objA.Id;

        var undoUnion = new UndoRedoStack();
        var controller = new InteractiveEditController();

        bool unionSuccess = controller.ExecuteCsgBoolean(sceneUnion, selUnion, CsgOperation.Union, deleteCutter: true, undoUnion);
        Assert.True(unionSuccess);
        Assert.Equal(1, sceneUnion.ObjectCount);

        undoUnion.Undo(sceneUnion);
        Assert.Equal(2, sceneUnion.ObjectCount);

        // 2. Intersect test
        var sceneIntersect = new EditorScene("Intersect Test");
        var cube1 = PrimitiveGenerator.CreateBox(new Vector3(4f, 4f, 4f));
        SceneObject targetObj = sceneIntersect.AddMeshObject("Target", cube1, Vector3.Zero);
        var cube2 = PrimitiveGenerator.CreateBox(new Vector3(4f, 4f, 4f));
        SceneObject cutterObj = sceneIntersect.AddMeshObject("Cutter", cube2, new Vector3(2f, 2f, 2f));

        var selIntersect = new SelectionManager();
        selIntersect.SelectSingleObject(targetObj.Id);
        selIntersect.ToggleSelectObject(cutterObj.Id);
        selIntersect.ActiveObjectId = targetObj.Id;

        var undoIntersect = new UndoRedoStack();
        bool intersectSuccess = controller.ExecuteCsgBoolean(sceneIntersect, selIntersect, CsgOperation.Intersect, deleteCutter: true, undoIntersect);
        Assert.True(intersectSuccess);
        Assert.Equal(1, sceneIntersect.ObjectCount);
        // Intersect of 4x4x4 cube at (0,0,0) and (2,2,2) is a 2x2x2 cube (6 faces, 8 vertices)
        SceneObject intersectedResult = sceneIntersect.GetObject(targetObj.Id)!;
        Assert.Equal(6, intersectedResult.Mesh!.FaceCount);
        Assert.Equal(8, intersectedResult.Mesh!.VertexCount);

        undoIntersect.Undo(sceneIntersect);
        Assert.Equal(2, sceneIntersect.ObjectCount);
    }

    [Fact]
    public void ExecuteCarve_OverlappingMeshes_SubractsFromOverlappingTargets()
    {
        var scene = new EditorScene("Carve Test");
        // Wall 1
        var wall1 = PrimitiveGenerator.CreateBox(new Vector3(10f, 4f, 0.5f));
        SceneObject wallObj1 = scene.AddMeshObject("Wall1", wall1, new Vector3(0f, 2f, 0f));

        // Wall 2 (far away, should not be touched by carve)
        var wall2 = PrimitiveGenerator.CreateBox(new Vector3(10f, 4f, 0.5f));
        SceneObject wallObj2 = scene.AddMeshObject("Wall2", wall2, new Vector3(50f, 2f, 0f));

        // Cutter positioned only intersecting Wall 1
        var cutterMesh = PrimitiveGenerator.CreateBox(new Vector3(2f, 2.5f, 2f));
        SceneObject cutterObj = scene.AddMeshObject("Cutter", cutterMesh, new Vector3(0f, 1.25f, 0f));

        var selection = new SelectionManager();
        // Select ONLY the cutter
        selection.SelectSingleObject(cutterObj.Id);

        var undo = new UndoRedoStack();
        var controller = new InteractiveEditController();

        int initialWall1FaceCount = wallObj1.Mesh!.FaceCount;
        int initialWall2FaceCount = wallObj2.Mesh!.FaceCount;

        bool success = controller.ExecuteCarve(scene, selection, deleteCutter: true, undo);
        Assert.True(success);

        // Cutter was deleted; 2 walls remain
        Assert.Equal(2, scene.ObjectCount);
        SceneObject resWall1 = scene.GetObject(wallObj1.Id)!;
        SceneObject resWall2 = scene.GetObject(wallObj2.Id)!;

        // Wall 1 was carved
        Assert.True(resWall1.Mesh!.FaceCount > initialWall1FaceCount);
        // Wall 2 was untouched
        Assert.Equal(initialWall2FaceCount, resWall2.Mesh!.FaceCount);

        // Undo restores cutter and original Wall 1
        undo.Undo(scene);
        Assert.Equal(3, scene.ObjectCount);
        Assert.Equal(initialWall1FaceCount, scene.GetObject(wallObj1.Id)!.Mesh!.FaceCount);
    }
}
