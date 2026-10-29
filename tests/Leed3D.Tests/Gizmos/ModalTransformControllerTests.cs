using System.Numerics;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Gizmos;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Primitives;
using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Selection;
using Xunit;

namespace Leed3D.Tests.Gizmos;

public sealed class ModalTransformControllerTests
{
    private static (EditorScene Scene, SceneObject Object, SelectionManager Selection, UndoRedoStack UndoStack) CreateTestSetup()
    {
        var scene = new EditorScene("Test");
        var mesh = PrimitiveGenerator.CreateBox(new Vector3(2f, 2f, 2f));
        var obj = scene.AddMeshObject("TestBox", mesh, Vector3.Zero);
        var selection = new SelectionManager();
        var undoStack = new UndoRedoStack();

        selection.SelectSingleObject(obj.Id);
        return (scene, obj, selection, undoStack);
    }

    [Fact]
    public void Start_ReturnsFalse_WhenSelectionIsEmpty()
    {
        var (scene, _, selection, undoStack) = CreateTestSetup();
        selection.ClearSelection();

        var controller = new ModalTransformController();
        var ray = new Ray3D(new Vector3(0f, 0f, -5f), Vector3.UnitZ);

        bool started = controller.Start(ModalTransformMode.Move, scene, selection, ray, Vector3.UnitZ, undoStack);
        Assert.False(started);
        Assert.False(controller.IsActive);
        Assert.Equal(ModalTransformMode.None, controller.Mode);
    }

    [Theory]
    [InlineData(ModalTransformMode.Move)]
    [InlineData(ModalTransformMode.Rotate)]
    [InlineData(ModalTransformMode.Scale)]
    public void Start_ActivatesModalSession_WithCorrectModeAndPivot(ModalTransformMode mode)
    {
        var (scene, obj, selection, undoStack) = CreateTestSetup();
        obj.Transform.Position = new Vector3(5f, 2f, -3f);

        var controller = new ModalTransformController();
        var ray = new Ray3D(new Vector3(5f, 2f, -10f), Vector3.UnitZ);

        bool started = controller.Start(mode, scene, selection, ray, Vector3.UnitZ, undoStack);
        Assert.True(started);
        Assert.True(controller.IsActive);
        Assert.Equal(mode, controller.Mode);
        Assert.Equal(GizmoAxisConstraint.None, controller.Constraint);
        Assert.Equal(new Vector3(5f, 2f, -3f), controller.PivotWorld);

        controller.Cancel(scene, undoStack);
        Assert.False(controller.IsActive);
        Assert.Equal(ModalTransformMode.None, controller.Mode);
    }

    [Fact]
    public void ToggleConstraint_And_HandleAxisKey_CyclesAxisAndPlaneLocks()
    {
        var (scene, _, selection, undoStack) = CreateTestSetup();
        var controller = new ModalTransformController();
        var ray = new Ray3D(new Vector3(0f, 0f, -5f), Vector3.UnitZ);
        controller.Start(ModalTransformMode.Move, scene, selection, ray, Vector3.UnitZ, undoStack);

        // 1. Single Axis X -> locks X, second X cycles back to None
        controller.HandleAxisKey('X', shift: false);
        Assert.Equal(GizmoAxisConstraint.X, controller.Constraint);
        controller.HandleAxisKey('X', shift: false);
        Assert.Equal(GizmoAxisConstraint.None, controller.Constraint);

        // 2. Single Axis Y and Z
        controller.HandleAxisKey('y', shift: false);
        Assert.Equal(GizmoAxisConstraint.Y, controller.Constraint);
        controller.HandleAxisKey('z', shift: false);
        Assert.Equal(GizmoAxisConstraint.Z, controller.Constraint);
        controller.HandleAxisKey('Z', shift: false);
        Assert.Equal(GizmoAxisConstraint.None, controller.Constraint);

        // 3. Dual-Axis Plane Locks: Shift+X -> YZ, Shift+Y -> XZ, Shift+Z -> XY
        controller.HandleAxisKey('X', shift: true);
        Assert.Equal(GizmoAxisConstraint.YZ, controller.Constraint);
        controller.HandleAxisKey('X', shift: true);
        Assert.Equal(GizmoAxisConstraint.None, controller.Constraint);

        controller.HandleAxisKey('Y', shift: true);
        Assert.Equal(GizmoAxisConstraint.XZ, controller.Constraint);

        controller.HandleAxisKey('Z', shift: true);
        Assert.Equal(GizmoAxisConstraint.XY, controller.Constraint);
        controller.HandleAxisKey('Z', shift: true);
        Assert.Equal(GizmoAxisConstraint.None, controller.Constraint);
    }

    [Fact]
    public void NumericInput_G_X_4_TranslatesObjectAndSupportsUndoRedo()
    {
        var (scene, obj, selection, undoStack) = CreateTestSetup();
        var controller = new ModalTransformController();
        var ray = new Ray3D(new Vector3(0f, 0f, -5f), Vector3.UnitZ);

        controller.Start(ModalTransformMode.Move, scene, selection, ray, Vector3.UnitZ, undoStack);
        controller.HandleAxisKey('X', shift: false, scene);
        controller.HandleNumericKey('4', scene);

        Assert.Equal("4", controller.NumericInputBuffer);
        Assert.Equal(4.0f, controller.ParsedNumericValue);
        Assert.Equal(new Vector3(4f, 0f, 0f), controller.CurrentTranslationDelta);
        Assert.Equal(new Vector3(4f, 0f, 0f), obj.Transform.Position);

        controller.Confirm(scene, undoStack);
        Assert.False(controller.IsActive);
        Assert.Equal(new Vector3(4f, 0f, 0f), obj.Transform.Position);

        // Undo restores pre-transform position
        Assert.True(undoStack.CanUndo);
        undoStack.Undo(scene);
        Assert.Equal(Vector3.Zero, scene.GetObject(obj.Id)!.Transform.Position);

        // Redo restores transformed position
        Assert.True(undoStack.CanRedo);
        undoStack.Redo(scene);
        Assert.Equal(new Vector3(4f, 0f, 0f), scene.GetObject(obj.Id)!.Transform.Position);
    }

    [Fact]
    public void NumericInput_R_Y_90_RotatesObjectAroundYAxis()
    {
        var (scene, obj, selection, undoStack) = CreateTestSetup();
        var controller = new ModalTransformController();
        var ray = new Ray3D(new Vector3(0f, 0f, -5f), Vector3.UnitZ);

        controller.Start(ModalTransformMode.Rotate, scene, selection, ray, Vector3.UnitZ, undoStack);
        controller.HandleAxisKey('Y', shift: false, scene);
        controller.HandleNumericKey('9', scene);
        controller.HandleNumericKey('0', scene);

        Assert.Equal("90", controller.NumericInputBuffer);
        Assert.Equal(90.0f, controller.CurrentAngleDegrees);
        Assert.Equal(90.0f, obj.Transform.RotationEulerDegrees.Y, 2);

        controller.Confirm(scene, undoStack);
        Assert.False(controller.IsActive);
        Assert.Equal(90.0f, obj.Transform.RotationEulerDegrees.Y, 2);

        undoStack.Undo(scene);
        Assert.Equal(0.0f, scene.GetObject(obj.Id)!.Transform.RotationEulerDegrees.Y, 2);
    }

    [Fact]
    public void NumericInput_S_ShiftY_2_ScalesObjectOnXZPlane()
    {
        var (scene, obj, selection, undoStack) = CreateTestSetup();
        var controller = new ModalTransformController();
        var ray = new Ray3D(new Vector3(0f, 0f, -5f), Vector3.UnitZ);

        controller.Start(ModalTransformMode.Scale, scene, selection, ray, Vector3.UnitZ, undoStack);
        controller.HandleAxisKey('Y', shift: true, scene); // Shift+Y -> XZ plane
        Assert.Equal(GizmoAxisConstraint.XZ, controller.Constraint);

        controller.HandleNumericKey('2', scene);

        Assert.Equal(new Vector3(2f, 1f, 2f), controller.CurrentScaleMultiplier);
        Assert.Equal(new Vector3(2f, 1f, 2f), obj.Transform.Scale);

        controller.Confirm(scene, undoStack);
        Assert.False(controller.IsActive);
        Assert.Equal(new Vector3(2f, 1f, 2f), obj.Transform.Scale);

        undoStack.Undo(scene);
        Assert.Equal(Vector3.One, scene.GetObject(obj.Id)!.Transform.Scale);
    }

    [Fact]
    public void NumericInput_Decimal_Negative_And_Backspace_Handling()
    {
        var (scene, obj, selection, undoStack) = CreateTestSetup();
        var controller = new ModalTransformController();
        var ray = new Ray3D(new Vector3(0f, 0f, -5f), Vector3.UnitZ);

        controller.Start(ModalTransformMode.Move, scene, selection, ray, Vector3.UnitZ, undoStack);
        controller.HandleAxisKey('Z', shift: false, scene);

        // Type "-2.5"
        controller.HandleNumericKey('-', scene);
        Assert.Equal("-", controller.NumericInputBuffer);

        controller.HandleNumericKey('2', scene);
        controller.HandleNumericKey('.', scene);
        controller.HandleNumericKey('5', scene);

        Assert.Equal("-2.5", controller.NumericInputBuffer);
        Assert.Equal(-2.5f, controller.ParsedNumericValue);
        Assert.Equal(new Vector3(0f, 0f, -2.5f), obj.Transform.Position);

        // Backspace to "-2." then "-2"
        controller.HandleBackspace(scene);
        Assert.Equal("-2.", controller.NumericInputBuffer);

        controller.HandleBackspace(scene);
        Assert.Equal("-2", controller.NumericInputBuffer);
        Assert.Equal(new Vector3(0f, 0f, -2.0f), obj.Transform.Position);

        // Toggle sign with '-' -> "2"
        controller.HandleNumericKey('-', scene);
        Assert.Equal("2", controller.NumericInputBuffer);
        Assert.Equal(new Vector3(0f, 0f, 2.0f), obj.Transform.Position);

        // Backspace to empty
        controller.HandleBackspace(scene);
        Assert.Equal(string.Empty, controller.NumericInputBuffer);
        Assert.Null(controller.ParsedNumericValue);

        controller.Cancel(scene, undoStack);
    }

    [Fact]
    public void Cancel_RestoresPreTransformState_WithoutLeavingUndoEntry()
    {
        var (scene, obj, selection, undoStack) = CreateTestSetup();
        obj.Transform.Position = new Vector3(10f, 5f, 2f);

        var controller = new ModalTransformController();
        var ray = new Ray3D(new Vector3(10f, 5f, -5f), Vector3.UnitZ);

        controller.Start(ModalTransformMode.Move, scene, selection, ray, Vector3.UnitZ, undoStack);
        controller.HandleAxisKey('X', shift: false, scene);
        controller.HandleNumericKey('5', scene);

        // Object is at (15, 5, 2) during live modal preview
        Assert.Equal(new Vector3(15f, 5f, 2f), obj.Transform.Position);

        // Cancel restores original (10, 5, 2) and removes transaction from stack
        controller.Cancel(scene, undoStack);
        Assert.False(controller.IsActive);
        Assert.Equal(new Vector3(10f, 5f, 2f), obj.Transform.Position);
        Assert.False(undoStack.CanUndo);
    }

    [Fact]
    public void SubElementModalTransform_TranslatesSelectedVertices()
    {
        var (scene, obj, selection, undoStack) = CreateTestSetup();
        selection.SetMode(InteractionMode.Vertex, scene);

        // Select first vertex of cube
        int vId = obj.Mesh!.Vertices[0].Id;
        Vector3 initialVertPos = obj.Mesh.Vertices[0].Position;
        selection.SelectSingleVertex(vId);

        var controller = new ModalTransformController();
        var ray = new Ray3D(initialVertPos - new Vector3(0f, 0f, 5f), Vector3.UnitZ);

        bool started = controller.Start(ModalTransformMode.Move, scene, selection, ray, Vector3.UnitZ, undoStack);
        Assert.True(started);

        controller.HandleAxisKey('Y', shift: false, scene);
        controller.HandleNumericKey('3', scene);

        Vector3 liveVertPos = obj.Mesh.GetVertex(vId)!.Position;
        Assert.Equal(initialVertPos + new Vector3(0f, 3f, 0f), liveVertPos);

        controller.Confirm(scene, undoStack);
        Assert.False(controller.IsActive);
        Assert.Equal(initialVertPos + new Vector3(0f, 3f, 0f), obj.Mesh.GetVertex(vId)!.Position);

        // Undo restores vertex position
        undoStack.Undo(scene);
        Assert.Equal(initialVertPos, scene.GetObject(obj.Id)!.Mesh!.GetVertex(vId)!.Position);
    }

    [Fact]
    public void MouseRayUpdate_CalculatesRayDeltaAndSnapping()
    {
        var (scene, obj, selection, undoStack) = CreateTestSetup();
        var controller = new ModalTransformController();

        var initialRay = new Ray3D(new Vector3(0f, 0f, -5f), Vector3.UnitZ);
        controller.Start(ModalTransformMode.Move, scene, selection, initialRay, Vector3.UnitZ, undoStack);

        // 1. Move on view plane (camera forward +Z, plane z=0)
        var currentRay = new Ray3D(new Vector3(2.3f, 1.7f, -5f), Vector3.UnitZ);
        controller.UpdateRay(scene, currentRay, snapGrid: false);

        Assert.Equal(2.3f, controller.CurrentTranslationDelta.X, 2);
        Assert.Equal(1.7f, controller.CurrentTranslationDelta.Y, 2);
        Assert.Equal(0.0f, controller.CurrentTranslationDelta.Z, 2);

        // 2. Move with grid snapping (step = 1.0)
        controller.UpdateRay(scene, currentRay, snapGrid: true, gridStep: 1.0f);
        Assert.Equal(2.0f, controller.CurrentTranslationDelta.X, 2);
        Assert.Equal(2.0f, controller.CurrentTranslationDelta.Y, 2);

        // 3. Switch to single-axis X constraint
        controller.HandleAxisKey('X', shift: false, scene);
        controller.UpdateRay(scene, currentRay, snapGrid: false);
        Assert.Equal(2.3f, controller.CurrentTranslationDelta.X, 2);
        Assert.Equal(0.0f, controller.CurrentTranslationDelta.Y, 2);
        Assert.Equal(0.0f, controller.CurrentTranslationDelta.Z, 2);

        controller.Cancel(scene, undoStack);
        Assert.Equal(Vector3.Zero, obj.Transform.Position);
    }

    [Fact]
    public void StatusReadout_ProvidesInformativeStrings()
    {
        var (scene, _, selection, undoStack) = CreateTestSetup();
        var controller = new ModalTransformController();
        var ray = new Ray3D(new Vector3(0f, 0f, -5f), Vector3.UnitZ);

        Assert.Equal(string.Empty, controller.GetStatusReadout());

        controller.Start(ModalTransformMode.Move, scene, selection, ray, Vector3.UnitZ, undoStack);
        string initialStatus = controller.GetStatusReadout();
        Assert.Contains("Move", initialStatus);
        Assert.Contains("View Plane", initialStatus);
        Assert.Contains("LMB/Enter: Confirm", initialStatus);

        controller.HandleAxisKey('Y', shift: true, scene);
        string planeStatus = controller.GetStatusReadout();
        Assert.Contains("XZ Plane (Shift+Y)", planeStatus);

        controller.HandleNumericKey('5', scene);
        string typedStatus = controller.GetStatusReadout();
        Assert.Contains("Typed: 5", typedStatus);

        controller.Cancel(scene, undoStack);
    }
}
