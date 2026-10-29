using System.Numerics;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Gizmos;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Primitives;
using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Selection;
using Leed3D.Kernel.Viewport;
using Xunit;

namespace Leed3D.Tests.Gizmos;

public sealed class TransformGizmoTests
{
    [Fact]
    public void ComputeGizmoScale_ScalesProportionallyWithCameraDistanceAndOrthoSize()
    {
        var camera = new EditorCamera
        {
            Position = new Vector3(0f, 0f, -10f),
            Target = Vector3.Zero
        };

        float scaleAt10 = TransformGizmo3D.ComputeGizmoScale(camera, Vector3.Zero);
        camera.Position = new Vector3(0f, 0f, -20f);
        float scaleAt20 = TransformGizmo3D.ComputeGizmoScale(camera, Vector3.Zero);

        Assert.Equal(scaleAt10 * 2.0f, scaleAt20, 3);

        camera.SetProjectionMode(ViewportProjectionMode.OrthoY);
        camera.OrthoSize = 10.0f;
        float ortho10 = TransformGizmo3D.ComputeGizmoScale(camera, Vector3.Zero);
        camera.OrthoSize = 20.0f;
        float ortho20 = TransformGizmo3D.ComputeGizmoScale(camera, Vector3.Zero);

        Assert.Equal(ortho10 * 2.0f, ortho20, 3);
    }

    [Fact]
    public void HitTest_DetectsSingleAxisPlaneQuadCenterAndRotationRings()
    {
        var gizmo = new TransformGizmo3D();
        Vector3 pivot = Vector3.Zero;
        float scale = 2.0f;

        // 1. Center Uniform handle
        var centerRay = new Ray3D(new Vector3(0f, 0f, -5f), Vector3.UnitZ);
        Assert.Equal(GizmoAxisConstraint.Uniform, gizmo.HitTest(centerRay, pivot, scale, GizmoToolMode.Translate));

        // 2. Single-axis X arrow (at x = 0.6 * scale = 1.2, y = 0)
        var xRay = new Ray3D(new Vector3(1.2f, 0f, -5f), Vector3.UnitZ);
        Assert.Equal(GizmoAxisConstraint.X, gizmo.HitTest(xRay, pivot, scale, GizmoToolMode.Translate));

        // 3. Single-axis Y arrow (at x = 0, y = 0.6 * scale = 1.2)
        var yRay = new Ray3D(new Vector3(0f, 1.2f, -5f), Vector3.UnitZ);
        Assert.Equal(GizmoAxisConstraint.Y, gizmo.HitTest(yRay, pivot, scale, GizmoToolMode.Translate));

        // 4. Single-axis Z arrow (ray from side +X looking -X towards z = 1.2)
        var zRay = new Ray3D(new Vector3(5f, 0f, 1.2f), -Vector3.UnitX);
        Assert.Equal(GizmoAxisConstraint.Z, gizmo.HitTest(zRay, pivot, scale, GizmoToolMode.Translate));

        // 5. Dual-axis XY plane quad (offset [0.25..0.47] * scale -> 0.35 * 2 = 0.7)
        var xyQuadRay = new Ray3D(new Vector3(0.7f, 0.7f, -5f), Vector3.UnitZ);
        Assert.Equal(GizmoAxisConstraint.XY, gizmo.HitTest(xyQuadRay, pivot, scale, GizmoToolMode.Translate));

        // 6. Dual-axis XZ plane quad (ray from +Y looking down -Y at x=0.7, z=0.7)
        var xzQuadRay = new Ray3D(new Vector3(0.7f, 5f, 0.7f), -Vector3.UnitY);
        Assert.Equal(GizmoAxisConstraint.XZ, gizmo.HitTest(xzQuadRay, pivot, scale, GizmoToolMode.Translate));

        // 7. Dual-axis YZ plane quad (ray from +X looking -X at y=0.7, z=0.7)
        var yzQuadRay = new Ray3D(new Vector3(5f, 0.7f, 0.7f), -Vector3.UnitX);
        Assert.Equal(GizmoAxisConstraint.YZ, gizmo.HitTest(yzQuadRay, pivot, scale, GizmoToolMode.Translate));

        // 8. Rotation ring Z (radius = 0.90 * scale = 1.80 in XY plane)
        var zRingRay = new Ray3D(new Vector3(1.80f, 0f, -5f), Vector3.UnitZ);
        Assert.Equal(GizmoAxisConstraint.Z, gizmo.HitTest(zRingRay, pivot, scale, GizmoToolMode.Rotate));
    }

    [Fact]
    public void TranslateDrag_SingleAxisAndDualAxisPlane_WithSnappingAndKeyboardOverride()
    {
        var gizmo = new TransformGizmo3D();
        Vector3 pivot = Vector3.Zero;

        // Start ray at (0.5, 0.5, -10) looking +Z
        var startRay = new Ray3D(new Vector3(0.5f, 0.5f, -10f), Vector3.UnitZ);
        // Dragged ray moved by (+3.2, +4.8, 0)
        var currentRay = new Ray3D(new Vector3(3.7f, 5.3f, -10f), Vector3.UnitZ);

        gizmo.BeginDrag(GizmoToolMode.Translate, GizmoAxisConstraint.X, pivot, startRay, Vector3.UnitZ);

        // Unsapped X translation: should move +3.2 on X and 0 on Y, Z
        gizmo.UpdateDrag(currentRay, snapGrid: false);
        Assert.Equal(3.2f, gizmo.CurrentTranslationDelta.X, 3);
        Assert.Equal(0.0f, gizmo.CurrentTranslationDelta.Y, 3);
        Assert.Equal(0.0f, gizmo.CurrentTranslationDelta.Z, 3);

        // Snapped X translation with step 1.0 -> 3.0
        gizmo.UpdateDrag(currentRay, snapGrid: true, gridStep: 1.0f);
        Assert.Equal(3.0f, gizmo.CurrentTranslationDelta.X, 3);

        // Keyboard override mid-drag to Y axis!
        gizmo.SetConstraintOverride(GizmoAxisConstraint.Y);
        gizmo.UpdateDrag(currentRay, snapGrid: false);
        Assert.Equal(0.0f, gizmo.CurrentTranslationDelta.X, 3);
        Assert.Equal(4.8f, gizmo.CurrentTranslationDelta.Y, 3);
        Assert.Equal(0.0f, gizmo.CurrentTranslationDelta.Z, 3);

        // Keyboard override mid-drag to XY dual-axis plane with 0.5 grid snap!
        gizmo.SetConstraintOverride(GizmoAxisConstraint.XY);
        gizmo.UpdateDrag(currentRay, snapGrid: true, gridStep: 0.5f);
        Assert.Equal(3.0f, gizmo.CurrentTranslationDelta.X, 3);
        Assert.Equal(5.0f, gizmo.CurrentTranslationDelta.Y, 3);
        Assert.Equal(0.0f, gizmo.CurrentTranslationDelta.Z, 3);

        gizmo.EndDrag();
        Assert.False(gizmo.IsDragging);
    }

    [Fact]
    public void RotateDrag_ComputesSignedAngleAndAppliesAngleSnapping()
    {
        var gizmo = new TransformGizmo3D();
        Vector3 pivot = Vector3.Zero;

        // Start ray hitting (1, 0, 0) on XY plane (0 degrees)
        var startRay = new Ray3D(new Vector3(1.0f, 0.0f, -10f), Vector3.UnitZ);
        // Current ray hitting (0, 1, 0) on XY plane (+90 degrees around Z)
        var ray90 = new Ray3D(new Vector3(0.0f, 1.0f, -10f), Vector3.UnitZ);

        gizmo.BeginDrag(GizmoToolMode.Rotate, GizmoAxisConstraint.Z, pivot, startRay, Vector3.UnitZ);
        gizmo.UpdateDrag(ray90, snapAngle: false);

        Assert.Equal(90.0f, gizmo.CurrentAngleDegrees, 2);
        Assert.Equal(new Vector3(0f, 0f, 90f), gizmo.CurrentRotationDeltaDegrees);

        // Ray at ~47 degrees -> with 15 degree angle snap should snap to 45 degrees
        float angle47Rad = 47.0f * (MathF.PI / 180.0f);
        var ray47 = new Ray3D(new Vector3(MathF.Cos(angle47Rad), MathF.Sin(angle47Rad), -10f), Vector3.UnitZ);
        gizmo.UpdateDrag(ray47, snapAngle: true, angleStepDegrees: 15.0f);

        Assert.Equal(45.0f, gizmo.CurrentAngleDegrees, 2);
    }

    [Fact]
    public void ScaleDrag_ComputesSingleAndDualAxisMultipliers()
    {
        var gizmo = new TransformGizmo3D();
        Vector3 pivot = Vector3.Zero;

        var startRay = new Ray3D(new Vector3(1.0f, 1.0f, -10f), Vector3.UnitZ);
        var currentRay = new Ray3D(new Vector3(2.5f, 3.0f, -10f), Vector3.UnitZ);

        gizmo.BeginDrag(GizmoToolMode.Scale, GizmoAxisConstraint.X, pivot, startRay, Vector3.UnitZ);
        gizmo.UpdateDrag(currentRay, snapGrid: false);

        // Delta X = +1.5 -> scale X = 1.0 + 1.5 = 2.5
        Assert.Equal(2.5f, gizmo.CurrentScaleMultiplier.X, 3);
        Assert.Equal(1.0f, gizmo.CurrentScaleMultiplier.Y, 3);
        Assert.Equal(1.0f, gizmo.CurrentScaleMultiplier.Z, 3);

        // Override to XY plane
        gizmo.SetConstraintOverride(GizmoAxisConstraint.XY);
        gizmo.UpdateDrag(currentRay, snapGrid: false);
        Assert.Equal(2.5f, gizmo.CurrentScaleMultiplier.X, 3);
        Assert.Equal(3.0f, gizmo.CurrentScaleMultiplier.Y, 3);
        Assert.Equal(1.0f, gizmo.CurrentScaleMultiplier.Z, 3);
    }

    [Fact]
    public void TransformApplicator_ObjectMode_UpdatesTransformAndSupportsUndoAndCancel()
    {
        var scene = new EditorScene();
        var undoStack = new UndoRedoStack();
        var selection = new SelectionManager();
        var gizmo = new TransformGizmo3D();
        var applicator = new TransformApplicator();

        SceneObject cube = scene.AddMeshObject("Cube", PrimitiveGenerator.CreateBox(Vector3.One), new Vector3(1f, 2f, 3f));
        selection.SelectSingleObject(cube.Id);

        Vector3 pivot = selection.GetSelectionPivotWorld(scene)!.Value;
        var startRay = new Ray3D(new Vector3(1f, 2f, -5f), Vector3.UnitZ);
        var dragRay = new Ray3D(new Vector3(5f, 2f, -5f), Vector3.UnitZ);

        gizmo.BeginDrag(GizmoToolMode.Translate, GizmoAxisConstraint.X, pivot, startRay, Vector3.UnitZ);
        applicator.BeginSession(scene, selection, pivot, undoStack, "Move Cube");

        gizmo.UpdateDrag(dragRay, snapGrid: false);
        applicator.ApplyGizmo(scene, gizmo);

        Assert.Equal(new Vector3(5f, 2f, 3f), cube.Transform.Position);

        // Commit and verify Undo/Redo
        gizmo.EndDrag();
        applicator.CommitSession(scene, undoStack);

        Assert.True(undoStack.CanUndo);
        undoStack.Undo(scene);
        Assert.Equal(new Vector3(1f, 2f, 3f), scene.GetObject(cube.Id)!.Transform.Position);

        undoStack.Redo(scene);
        Assert.Equal(new Vector3(5f, 2f, 3f), scene.GetObject(cube.Id)!.Transform.Position);

        // Start a second drag and cancel it -> should restore to (5, 2, 3) without adding an undo record
        int undoCountBeforeCancel = undoStack.UndoCount;
        applicator.BeginSession(scene, selection, new Vector3(5f, 2f, 3f), undoStack, "Canceled Move");
        applicator.ApplyTranslation(scene, new Vector3(10f, 10f, 10f));
        Assert.Equal(new Vector3(15f, 12f, 13f), scene.GetObject(cube.Id)!.Transform.Position);

        applicator.CancelSession(scene, undoStack);
        Assert.Equal(new Vector3(5f, 2f, 3f), scene.GetObject(cube.Id)!.Transform.Position);
        Assert.Equal(undoCountBeforeCancel, undoStack.UndoCount);
    }

    [Fact]
    public void TransformApplicator_EditModeFaceSelection_TransformsFaceVerticesWithoutDrift()
    {
        var scene = new EditorScene();
        var undoStack = new UndoRedoStack();
        var selection = new SelectionManager();
        var applicator = new TransformApplicator();

        EditableMesh boxMesh = PrimitiveGenerator.CreateBox(new Vector3(2f, 2f, 2f));
        SceneObject cube = scene.AddMeshObject("Cube", boxMesh, Vector3.Zero);

        selection.SelectSingleObject(cube.Id);
        selection.SetMode(InteractionMode.Face, scene);

        // Find the top (+Y) face whose normal is (0, 1, 0)
        PolygonFace topFace = boxMesh.Faces.First(f => f.ComputeNormal(boxMesh).Y > 0.9f);
        selection.SelectSingleFace(topFace.Id);

        Vector3 pivot = selection.GetSelectionPivotWorld(scene)!.Value;
        Assert.Equal(1.0f, pivot.Y, 3);

        // Begin session and translate top face up by +2.0 units along Y
        applicator.BeginSession(scene, selection, pivot, undoStack, "Extrude/Move Top Face");

        // Apply multiple intermediate updates to verify zero cumulative drift
        applicator.ApplyTranslation(scene, new Vector3(0f, 0.5f, 0f));
        applicator.ApplyTranslation(scene, new Vector3(0f, 1.25f, 0f));
        applicator.ApplyTranslation(scene, new Vector3(0f, 2.0f, 0f));

        applicator.CommitSession(scene, undoStack);

        foreach (int vId in topFace.VertexIds)
        {
            MeshVertex v = cube.Mesh!.GetVertex(vId)!;
            Assert.Equal(3.0f, v.Position.Y, 3);
        }

        // Undo should restore top face vertices back to Y = 1.0
        undoStack.Undo(scene);
        foreach (int vId in topFace.VertexIds)
        {
            MeshVertex v = scene.GetObject(cube.Id)!.Mesh!.GetVertex(vId)!;
            Assert.Equal(1.0f, v.Position.Y, 3);
        }
    }
}
