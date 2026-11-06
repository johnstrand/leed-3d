using System.Numerics;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Primitives;
using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Selection;
using Leed3D.Kernel.Texturing;

namespace Leed3D.Tests.Texturing;

public sealed class TextureAssignmentAndUvInspectorTests
{
    [Fact]
    public void MaterialAssignment_ToObject_UpdatesAllFacesAndRecordsUndo()
    {
        var scene = new EditorScene("MaterialTest");
        EditableMesh box = PrimitiveGenerator.CreateBox(new Vector3(2f, 2f, 2f));
        SceneObject obj = scene.AddMeshObject("TestBox", box, Vector3.Zero);

        var undo = new UndoRedoStack();
        var selection = new SelectionManager();
        selection.SelectSingleObject(obj.Id);

        // Before assignment, material is dev_grid_gray
        Assert.All(obj.Mesh!.Faces, f => Assert.Equal("dev_grid_gray", f.MaterialId));

        // Assign dev_grid_orange
        EditorScene before = scene.CloneSnapshot();
        foreach (PolygonFace f in obj.Mesh.Faces)
        {
            f.MaterialId = "dev_grid_orange";
        }
        undo.RecordAction("Assign Material dev_grid_orange", before, scene);

        Assert.All(obj.Mesh.Faces, f => Assert.Equal("dev_grid_orange", f.MaterialId));
        Assert.True(undo.CanUndo);

        // Undo restores dev_grid_gray
        undo.Undo(scene);
        SceneObject restored = scene.GetObject(obj.Id)!;
        Assert.All(restored.Mesh!.Faces, f => Assert.Equal("dev_grid_gray", f.MaterialId));

        // Redo restores dev_grid_orange
        undo.Redo(scene);
        SceneObject redone = scene.GetObject(obj.Id)!;
        Assert.All(redone.Mesh!.Faces, f => Assert.Equal("dev_grid_orange", f.MaterialId));
    }

    [Fact]
    public void MaterialAssignment_ToSingleFace_UpdatesOnlyTargetFace()
    {
        var scene = new EditorScene("FaceMatTest");
        EditableMesh box = PrimitiveGenerator.CreateBox(new Vector3(2f, 2f, 2f));
        SceneObject obj = scene.AddMeshObject("TestBox", box, Vector3.Zero);

        int targetFaceId = obj.Mesh!.Faces[0].Id;
        obj.Mesh.Faces[0].MaterialId = "dev_lava";

        Assert.Equal("dev_lava", obj.Mesh.Faces[0].MaterialId);
        // Other faces remain default
        for (int i = 1; i < obj.Mesh.Faces.Count; i++)
        {
            Assert.Equal("dev_grid_gray", obj.Mesh.Faces[i].MaterialId);
        }
    }

    [Fact]
    public void UvInspector_OffsetScaleRotationFit_ModifiesMappingCorrectly()
    {
        var scene = new EditorScene("UvInspectorTest");
        EditableMesh box = PrimitiveGenerator.CreateBox(new Vector3(4f, 2f, 1f));
        SceneObject obj = scene.AddMeshObject("TestBox", box, Vector3.Zero);
        PolygonFace face = obj.Mesh!.Faces[0];

        // 1. Offset
        face.UvMapping.Offset = new Vector2(0.5f, -0.25f);
        Assert.Equal(new Vector2(0.5f, -0.25f), face.UvMapping.Offset);

        // 2. Scale
        face.UvMapping.Scale = new Vector2(2f, 4f);
        Assert.Equal(new Vector2(2f, 4f), face.UvMapping.Scale);

        // 3. Rotation
        face.UvMapping.RotationDegrees = 45f;
        Assert.Equal(45f, face.UvMapping.RotationDegrees);

        // 4. Fit to Face
        UvProjectionEngine.FitToFace(face, obj.Mesh, obj.Transform, 1f, 1f);
        IReadOnlyList<Vector2> uvs = UvProjectionEngine.ComputeFaceUvs(face, obj.Mesh, obj.Transform);

        float minU = uvs.Min(u => u.X);
        float maxU = uvs.Max(u => u.X);
        float minV = uvs.Min(u => u.Y);
        float maxV = uvs.Max(u => u.Y);

        Assert.Equal(0f, minU, 3);
        Assert.Equal(1f, maxU, 3);
        Assert.Equal(0f, minV, 3);
        Assert.Equal(1f, maxV, 3);

        // 5. Texture Lock toggle
        face.UvMapping.TextureLock = true;
        Assert.True(face.UvMapping.TextureLock);
    }
}
