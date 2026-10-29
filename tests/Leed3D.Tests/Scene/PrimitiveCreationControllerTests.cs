using System.Numerics;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Primitives;
using Leed3D.Kernel.Scene;

namespace Leed3D.Tests.Scene;

public class PrimitiveCreationControllerTests
{
    [Fact]
    public void SpawnPrimitive_CreatesObject_RecordsSingleUndoAction()
    {
        var scene = new EditorScene("TestScene");
        var undoStack = new UndoRedoStack();
        var controller = new PrimitiveCreationController(scene, undoStack);

        Assert.Equal(0, scene.ObjectCount);
        Assert.False(controller.IsActive);

        SceneObject obj = controller.SpawnPrimitive(PrimitiveType.Cylinder, new Vector3(2, 0, 4));

        Assert.True(controller.IsActive);
        Assert.Equal(1, scene.ObjectCount);
        Assert.Equal(PrimitiveType.Cylinder, controller.ActivePrimitiveType);
        Assert.Equal(new Vector3(2, 0, 4), obj.Transform.Position);
        Assert.Equal("Cylinder", obj.Name);
        Assert.NotNull(obj.Mesh);

        // Single undo entry recorded
        Assert.True(undoStack.CanUndo);
        Assert.Equal(1, undoStack.UndoCount);
        Assert.Equal("Add Cylinder", undoStack.NextUndoDescription);
    }

    [Fact]
    public void RegenerateActiveMesh_UpdatesGeometryLive_WithoutExtraUndoRecords()
    {
        var scene = new EditorScene("TestScene");
        var undoStack = new UndoRedoStack();
        var controller = new PrimitiveCreationController(scene, undoStack);

        SceneObject cylinderObj = controller.SpawnPrimitive(PrimitiveType.Cylinder);
        // Default cylinder: 16 radial segments -> 16 side quads + 2 caps = 18 faces, 32 vertices
        Assert.Equal(18, cylinderObj.Mesh!.FaceCount);
        Assert.Equal(32, cylinderObj.Mesh.VertexCount);

        // Adjust parameters to 8 radial segments
        controller.CylinderParams = controller.CylinderParams with { RadialSegments = 8 };
        controller.RegenerateActiveMesh();

        // Updated live: 8 side quads + 2 caps = 10 faces, 16 vertices
        Assert.Equal(10, cylinderObj.Mesh.FaceCount);
        Assert.Equal(16, cylinderObj.Mesh.VertexCount);
        Assert.True(cylinderObj.Mesh.GetTopology().IsClosedManifold());

        // Still exactly 1 undo entry
        Assert.Equal(1, undoStack.UndoCount);

        // Adjust parameters to 32 radial segments
        controller.CylinderParams = controller.CylinderParams with { RadialSegments = 32 };
        controller.RegenerateActiveMesh();

        Assert.Equal(34, cylinderObj.Mesh.FaceCount);
        Assert.Equal(64, cylinderObj.Mesh.VertexCount);
        Assert.Equal(1, undoStack.UndoCount);

        // Single Undo step cleanly rolls back the entire primitive creation
        bool undone = undoStack.Undo(scene);
        Assert.True(undone);
        Assert.Equal(0, scene.ObjectCount);
    }

    [Fact]
    public void Box_InvertNormals_TogglesInwardFacingRoomMesh()
    {
        var scene = new EditorScene("TestScene");
        var undoStack = new UndoRedoStack();
        var controller = new PrimitiveCreationController(scene, undoStack);

        SceneObject boxObj = controller.SpawnPrimitive(PrimitiveType.Box);

        // Default box: outward normals
        PolygonFace topFaceBefore = boxObj.Mesh!.Faces.First(f => f.MaterialId == "dev_grid_gray");
        Vector3 normalBefore = topFaceBefore.ComputeNormal(boxObj.Mesh);
        Vector3 centroidBefore = topFaceBefore.ComputeCentroid(boxObj.Mesh);
        Assert.True(Vector3.Dot(normalBefore, centroidBefore) > 0.0f);

        // Toggle InvertNormals (Hollow Room)
        controller.BoxParams = controller.BoxParams with { InvertNormals = true };
        controller.RegenerateActiveMesh();

        PolygonFace topFaceAfter = boxObj.Mesh.Faces.First(f => f.MaterialId == "dev_grid_gray");
        Vector3 normalAfter = topFaceAfter.ComputeNormal(boxObj.Mesh);
        Vector3 centroidAfter = topFaceAfter.ComputeCentroid(boxObj.Mesh);
        Assert.True(Vector3.Dot(normalAfter, centroidAfter) < 0.0f);
    }

    [Fact]
    public void Geosphere_Subdivisions_RegeneratesLive()
    {
        var scene = new EditorScene("TestScene");
        var undoStack = new UndoRedoStack();
        var controller = new PrimitiveCreationController(scene, undoStack);

        SceneObject sphere = controller.SpawnPrimitive(PrimitiveType.Geosphere);
        // Default subdivisions = 2 -> 162 vertices, 320 faces
        Assert.Equal(162, sphere.Mesh!.VertexCount);
        Assert.Equal(320, sphere.Mesh.FaceCount);

        // Change subdivisions to 1 -> 42 vertices, 80 faces
        controller.GeosphereParams = controller.GeosphereParams with { Subdivisions = 1 };
        controller.RegenerateActiveMesh();

        Assert.Equal(42, sphere.Mesh.VertexCount);
        Assert.Equal(80, sphere.Mesh.FaceCount);
    }

    [Fact]
    public void Dismiss_DeactivatesController()
    {
        var scene = new EditorScene("TestScene");
        var undoStack = new UndoRedoStack();
        var controller = new PrimitiveCreationController(scene, undoStack);

        controller.SpawnPrimitive(PrimitiveType.Plane);
        Assert.True(controller.IsActive);

        controller.Dismiss();
        Assert.False(controller.IsActive);
        Assert.Null(controller.ActiveObject);
        Assert.Null(controller.ActivePrimitiveType);
    }
}
