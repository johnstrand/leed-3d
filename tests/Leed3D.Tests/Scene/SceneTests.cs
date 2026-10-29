using System.Numerics;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Primitives;
using Leed3D.Kernel.Scene;

namespace Leed3D.Tests.Scene;

public class SceneTests
{
    [Fact]
    public void Transform3D_LocalToWorldAndBack_PreservesPointAndDirection()
    {
        var transform = new Transform3D
        {
            Position = new Vector3(10.0f, -5.0f, 20.0f),
            RotationEulerDegrees = new Vector3(0.0f, 90.0f, 0.0f), // 90 deg yaw
            Scale = new Vector3(2.0f, 2.0f, 2.0f)
        };

        Vector3 localPoint = new Vector3(1.0f, 2.0f, 3.0f);
        Vector3 worldPoint = transform.TransformPointToWorld(localPoint);
        Vector3 restoredPoint = transform.TransformPointToLocal(worldPoint);

        Assert.True(Vector3.Distance(localPoint, restoredPoint) < 1e-4f);

        Vector3 localDir = Vector3.Normalize(new Vector3(0.0f, 1.0f, 1.0f));
        Vector3 worldDir = transform.TransformDirectionToWorld(localDir);
        Vector3 restoredDir = transform.TransformDirectionToLocal(worldDir);

        Assert.True(Vector3.Distance(localDir, restoredDir) < 1e-4f);
    }

    [Fact]
    public void SceneObject_GetWorldBounds_AppliesTransform()
    {
        EditableMesh box = PrimitiveGenerator.CreateBox(new Vector3(2.0f, 2.0f, 2.0f));
        var obj = new SceneObject(1, "TestBox", box)
        {
            Transform = new Transform3D(new Vector3(10.0f, 20.0f, 30.0f))
        };

        BoundingBox3D worldBounds = obj.GetWorldBounds();

        Assert.Equal(new Vector3(10.0f, 20.0f, 30.0f), worldBounds.Center);
        Assert.Equal(new Vector3(9.0f, 19.0f, 29.0f), worldBounds.Min);
        Assert.Equal(new Vector3(11.0f, 21.0f, 31.0f), worldBounds.Max);
    }

    [Fact]
    public void EditorScene_AddAndRemoveObjects_UpdatesList()
    {
        var scene = new EditorScene("Level1");
        EditableMesh box = PrimitiveGenerator.CreateBox(new Vector3(1, 1, 1));

        SceneObject obj1 = scene.AddMeshObject("Box1", box, new Vector3(0, 0, 0));
        SceneObject obj2 = scene.AddMeshObject("Box2", box, new Vector3(5, 0, 0));

        Assert.Equal(2, scene.ObjectCount);
        Assert.NotNull(scene.GetObject(obj1.Id));
        Assert.NotNull(scene.GetObject(obj2.Id));

        bool removed = scene.RemoveObject(obj1.Id);
        Assert.True(removed);
        Assert.Equal(1, scene.ObjectCount);
        Assert.Null(scene.GetObject(obj1.Id));
    }

    [Fact]
    public void EditorScene_CloneSnapshot_IsDeepCopy()
    {
        var scene = new EditorScene("Original");
        EditableMesh box = PrimitiveGenerator.CreateBox(new Vector3(2, 2, 2));
        SceneObject obj = scene.AddMeshObject("Box", box, new Vector3(1, 2, 3));

        EditorScene snapshot = scene.CloneSnapshot();

        // Mutate original
        obj.Transform.Position = new Vector3(99, 99, 99);
        obj.Mesh!.SetVertexPosition(1, new Vector3(50, 50, 50));
        scene.AddMeshObject("Box2", box);

        Assert.Equal(2, scene.ObjectCount);
        Assert.Equal(1, snapshot.ObjectCount);

        SceneObject snapshotObj = snapshot.GetObject(obj.Id)!;
        Assert.Equal(new Vector3(1, 2, 3), snapshotObj.Transform.Position);
        Assert.NotEqual(new Vector3(50, 50, 50), snapshotObj.Mesh!.GetVertex(1)!.Position);
    }

    [Fact]
    public void UndoRedoStack_UndoAndRedo_RestoresPriorState()
    {
        var scene = new EditorScene("TestScene");
        var undoStack = new UndoRedoStack(maxHistory: 10);

        EditableMesh box = PrimitiveGenerator.CreateBox(new Vector3(2, 2, 2));
        SceneObject obj = scene.AddMeshObject("Box", box, new Vector3(0, 0, 0));

        // Action 1: Move object to (5, 0, 0)
        EditorScene beforeMove = scene.CloneSnapshot();
        obj.Transform.Position = new Vector3(5, 0, 0);
        undoStack.RecordAction("Move Box", beforeMove, scene);

        Assert.True(undoStack.CanUndo);
        Assert.False(undoStack.CanRedo);
        Assert.Equal("Move Box", undoStack.NextUndoDescription);

        // Action 2: Mutate vertex position
        EditorScene beforeEdit = scene.CloneSnapshot();
        obj.Mesh!.SetVertexPosition(1, new Vector3(10, 10, 10));
        undoStack.RecordAction("Edit Vertex", beforeEdit, scene);

        Assert.Equal(2, undoStack.UndoCount);

        // Undo edit vertex
        bool undone = undoStack.Undo(scene);
        Assert.True(undone);
        SceneObject? currentObj = scene.GetObject(obj.Id);
        Assert.NotNull(currentObj);
        Assert.NotEqual(new Vector3(10, 10, 10), currentObj.Mesh!.GetVertex(1)!.Position);
        Assert.Equal(new Vector3(5, 0, 0), currentObj.Transform.Position);
        Assert.True(undoStack.CanRedo);
        Assert.Equal("Edit Vertex", undoStack.NextRedoDescription);

        // Undo move box
        undone = undoStack.Undo(scene);
        Assert.True(undone);
        currentObj = scene.GetObject(obj.Id);
        Assert.NotNull(currentObj);
        Assert.Equal(Vector3.Zero, currentObj.Transform.Position);

        // Redo move box
        bool redone = undoStack.Redo(scene);
        Assert.True(redone);
        currentObj = scene.GetObject(obj.Id);
        Assert.NotNull(currentObj);
        Assert.Equal(new Vector3(5, 0, 0), currentObj.Transform.Position);

        // Redo edit vertex
        redone = undoStack.Redo(scene);
        Assert.True(redone);
        currentObj = scene.GetObject(obj.Id);
        Assert.NotNull(currentObj);
        Assert.Equal(new Vector3(10, 10, 10), currentObj.Mesh!.GetVertex(1)!.Position);
    }

    [Fact]
    public void UndoRedoStack_NewActionInvalidatesRedo()
    {
        var scene = new EditorScene("TestScene");
        var undoStack = new UndoRedoStack();

        EditorScene s0 = scene.CloneSnapshot();
        scene.AddMeshObject("A", PrimitiveGenerator.CreateBox(new Vector3(1, 1, 1)));
        undoStack.RecordAction("Add A", s0, scene);

        EditorScene s1 = scene.CloneSnapshot();
        scene.AddMeshObject("B", PrimitiveGenerator.CreateBox(new Vector3(1, 1, 1)));
        undoStack.RecordAction("Add B", s1, scene);

        undoStack.Undo(scene);
        Assert.True(undoStack.CanRedo);

        // Record a new action while redo was available
        EditorScene sBeforeNew = scene.CloneSnapshot();
        scene.AddMeshObject("C", PrimitiveGenerator.CreateBox(new Vector3(1, 1, 1)));
        undoStack.RecordAction("Add C", sBeforeNew, scene);

        Assert.False(undoStack.CanRedo);
    }

    [Fact]
    public void UndoRedoStack_Transaction_CommitAndCancel()
    {
        var scene = new EditorScene("TestScene");
        var undoStack = new UndoRedoStack();
        SceneObject obj = scene.AddMeshObject("Box", PrimitiveGenerator.CreateBox(new Vector3(1, 1, 1)), new Vector3(0, 0, 0));

        // 1. Transaction Cancel (e.g. user pressed Esc during gizmo drag)
        undoStack.BeginTransaction("Gizmo Drag", scene);
        obj.Transform.Position = new Vector3(20, 30, 40);

        undoStack.CancelTransaction(scene);

        Assert.Equal(Vector3.Zero, scene.GetObject(obj.Id)!.Transform.Position);
        Assert.False(undoStack.CanUndo); // Did not pollute undo stack!

        // 2. Transaction Commit (e.g. user released left mouse button)
        undoStack.BeginTransaction("Gizmo Drag", scene);
        scene.GetObject(obj.Id)!.Transform.Position = new Vector3(5, 5, 5);
        undoStack.CommitTransaction(scene);

        Assert.True(undoStack.CanUndo);
        Assert.Equal("Gizmo Drag", undoStack.NextUndoDescription);

        undoStack.Undo(scene);
        Assert.Equal(Vector3.Zero, scene.GetObject(obj.Id)!.Transform.Position);
    }
}
