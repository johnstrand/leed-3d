using System.Numerics;
using Leed3D.Kernel.Csg;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Gizmos;
using Leed3D.Kernel.Primitives;
using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Selection;
using Leed3D.Kernel.Texturing;

namespace Leed3D.Tests.Texturing;

public sealed class UvProjectionEngineTests
{
    [Fact]
    public void GetDefaultAxesForNormal_ReturnsExpectedCardinalProjectionAxes()
    {
        // Dominant Y (floor / ceiling)
        var (uYPos, vYPos) = UvProjectionEngine.GetDefaultAxesForNormal(Vector3.UnitY);
        Assert.Equal(new Vector3(1f, 0f, 0f), uYPos);
        Assert.Equal(new Vector3(0f, 0f, 1f), vYPos);

        var (uYNeg, vYNeg) = UvProjectionEngine.GetDefaultAxesForNormal(-Vector3.UnitY);
        Assert.Equal(new Vector3(1f, 0f, 0f), uYNeg);
        Assert.Equal(new Vector3(0f, 0f, 1f), vYNeg);

        // Dominant X (left / right wall)
        var (uXPos, vXPos) = UvProjectionEngine.GetDefaultAxesForNormal(Vector3.UnitX);
        Assert.Equal(new Vector3(0f, 0f, 1f), uXPos);
        Assert.Equal(new Vector3(0f, -1f, 0f), vXPos);

        // Dominant Z (front / back wall)
        var (uZPos, vZPos) = UvProjectionEngine.GetDefaultAxesForNormal(Vector3.UnitZ);
        Assert.Equal(new Vector3(1f, 0f, 0f), uZPos);
        Assert.Equal(new Vector3(0f, -1f, 0f), vZPos);
    }

    [Fact]
    public void WorldSpaceTiling_MovingVertexWithTextureLockFalse_ChangesUvProportionallyToWorldPosition()
    {
        // Create a 4x2x1 wall box
        EditableMesh mesh = PrimitiveGenerator.CreateBox(new Vector3(4.0f, 2.0f, 1.0f));
        PolygonFace frontFace = mesh.Faces.First(f => f.ComputeNormal(mesh).Z > 0.9f);

        Assert.False(frontFace.UvMapping.TextureLock);
        frontFace.UvMapping.Scale = new Vector2(2.0f, 2.0f); // 2 meters per tile

        int targetVertId = frontFace.VertexIds[0];
        Vector3 oldPos = mesh.GetVertex(targetVertId)!.Position;
        Vector2 uvBefore = UvProjectionEngine.ComputeUv(oldPos, frontFace.UvMapping);

        // Stretch vertex horizontally by +6.0m along X and vertically by -4.0m along Y
        Vector3 delta = new(6.0f, -4.0f, 0.0f);
        Vector3 newPos = oldPos + delta;
        mesh.SetVertexPosition(targetVertId, newPos);

        Vector2 uvAfter = UvProjectionEngine.ComputeUv(newPos, frontFace.UvMapping);

        // For front wall (dominant Z): UAxis = (1,0,0), VAxis = (0,-1,0), Scale = (2,2)
        // Expected deltaU = +6.0 / 2.0 = +3.0; Expected deltaV = (-4.0 * -1.0) / 2.0 = +2.0
        Assert.Equal(uvBefore.X + 3.0f, uvAfter.X, 4);
        Assert.Equal(uvBefore.Y + 2.0f, uvAfter.Y, 4);
    }

    [Fact]
    public void TextureLock_DirectApplyTransformToLockedUv_PreservesExactUvsUnderTranslationAndRotation()
    {
        EditableMesh mesh = PrimitiveGenerator.CreateBox(new Vector3(4.0f, 3.0f, 2.0f));
        PolygonFace face = mesh.Faces.First(f => f.ComputeNormal(mesh).Z > 0.9f);

        face.UvMapping.TextureLock = true;
        face.UvMapping.Scale = new Vector2(1.5f, 0.75f);
        face.UvMapping.Offset = new Vector2(0.35f, -0.8f);
        face.UvMapping.RotationDegrees = 25.0f;

        Vector3[] originalPositions = face.VertexIds.Select(id => mesh.GetVertex(id)!.Position).ToArray();
        Vector2[] uvsBefore = originalPositions.Select(p => UvProjectionEngine.ComputeUv(p, face.UvMapping)).ToArray();

        // Apply both a 3D rotation around an arbitrary pivot AND a translation delta
        Vector3 pivot = new(2.0f, -1.0f, 3.5f);
        Quaternion rotation = Quaternion.CreateFromYawPitchRoll(0.65f, -0.35f, 0.42f);
        Vector3 translation = new(-4.25f, 7.1f, 2.8f);

        UvProjectionEngine.ApplyTransformToLockedUv(face.UvMapping, translation, rotation, pivot);

        for (int i = 0; i < originalPositions.Length; i++)
        {
            Vector3 transformedPos = pivot + Vector3.Transform(originalPositions[i] - pivot, rotation) + translation;
            Vector2 uvAfter = UvProjectionEngine.ComputeUv(transformedPos, face.UvMapping);

            Assert.True(
                Vector2.Distance(uvsBefore[i], uvAfter) < 1e-4f,
                $"Expected UV {uvsBefore[i]}, got {uvAfter} (diff={Vector2.Distance(uvsBefore[i], uvAfter)})");
        }
    }

    [Fact]
    public void TextureLock_ViaTransformApplicator_ObjectAndFaceMode_PreservesExactVertexUvs()
    {
        var scene = new EditorScene("TextureLockScene");
        EditableMesh mesh = PrimitiveGenerator.CreateBox(new Vector3(4.0f, 2.0f, 2.0f));
        SceneObject obj = scene.AddMeshObject("LockedWall", mesh, new Vector3(1.0f, 2.0f, -1.0f));

        // Enable TextureLock on all faces with non-trivial UV mapping settings
        foreach (PolygonFace f in obj.Mesh!.Faces)
        {
            f.UvMapping.TextureLock = true;
            f.UvMapping.Scale = new Vector2(2.0f, 1.25f);
            f.UvMapping.Offset = new Vector2(0.2f, -0.4f);
            f.UvMapping.RotationDegrees = 15.0f;
        }

        PolygonFace testFace = obj.Mesh.Faces[0];
        IReadOnlyList<Vector2> uvsInitial = UvProjectionEngine.ComputeFaceUvs(testFace, obj.Mesh, obj.Transform);

        var selection = new SelectionManager();
        selection.SelectSingleObject(obj.Id);
        var undo = new UndoRedoStack();
        var applicator = new TransformApplicator();

        // 1. Object Mode Translation + Rotation via TransformApplicator
        Vector3 pivot = selection.GetSelectionPivotWorld(scene)!.Value;
        applicator.BeginSession(scene, selection, pivot, undo, "Move & Rotate Locked Object");
        applicator.ApplyTranslation(scene, new Vector3(5.5f, -2.25f, 8.0f));

        IReadOnlyList<Vector2> uvsAfterMove = UvProjectionEngine.ComputeFaceUvs(testFace, obj.Mesh, obj.Transform);
        for (int i = 0; i < uvsInitial.Count; i++)
        {
            Assert.True(Vector2.Distance(uvsInitial[i], uvsAfterMove[i]) < 1e-4f);
        }

        Vector3 eulerDelta = new(30.0f, 45.0f, -20.0f);
        Quaternion rotDelta = new Transform3D(Vector3.Zero, eulerDelta).GetRotationQuaternion();
        applicator.ApplyRotation(scene, rotDelta, eulerDelta);

        IReadOnlyList<Vector2> uvsAfterRot = UvProjectionEngine.ComputeFaceUvs(testFace, obj.Mesh, obj.Transform);
        for (int i = 0; i < uvsInitial.Count; i++)
        {
            Assert.True(Vector2.Distance(uvsInitial[i], uvsAfterRot[i]) < 1e-4f);
        }

        applicator.CommitSession(scene, undo);

        // 2. Edit Mode Face Translation & Rotation via TransformApplicator
        selection.SetMode(InteractionMode.Face, scene);
        selection.SelectSingleFace(testFace.Id);
        Vector3 facePivot = selection.GetSelectionPivotWorld(scene)!.Value;

        applicator.BeginSession(scene, selection, facePivot, undo, "Transform Locked Face");
        applicator.ApplyTranslation(scene, new Vector3(-3.0f, 4.0f, 1.5f));

        IReadOnlyList<Vector2> uvsAfterFaceMove = UvProjectionEngine.ComputeFaceUvs(testFace, obj.Mesh, obj.Transform);
        for (int i = 0; i < uvsInitial.Count; i++)
        {
            Assert.True(Vector2.Distance(uvsInitial[i], uvsAfterFaceMove[i]) < 1e-4f);
        }

        Quaternion faceRot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 3.0f);
        applicator.ApplyRotation(scene, faceRot, new Vector3(0f, 60f, 0f));

        IReadOnlyList<Vector2> uvsAfterFaceRot = UvProjectionEngine.ComputeFaceUvs(testFace, obj.Mesh, obj.Transform);
        for (int i = 0; i < uvsInitial.Count; i++)
        {
            Assert.True(Vector2.Distance(uvsInitial[i], uvsAfterFaceRot[i]) < 1e-4f);
        }

        applicator.CommitSession(scene, undo);
    }

    [Fact]
    public void FitToFace_OnArbitraryRectangularFaces_ProducesExactZeroToOneBounds()
    {
        EditableMesh mesh = PrimitiveGenerator.CreateBox(new Vector3(6.0f, 3.5f, 2.0f));
        var transform = new Transform3D(new Vector3(4.0f, -2.0f, 5.0f));

        foreach (PolygonFace face in mesh.Faces)
        {
            UvProjectionEngine.FitToFace(face, mesh, transform, repeatsU: 1.0f, repeatsV: 1.0f);
            IReadOnlyList<Vector2> uvs = UvProjectionEngine.ComputeFaceUvs(face, mesh, transform);

            float minU = uvs.Min(uv => uv.X);
            float maxU = uvs.Max(uv => uv.X);
            float minV = uvs.Min(uv => uv.Y);
            float maxV = uvs.Max(uv => uv.Y);

            Assert.Equal(0.0f, minU, 4);
            Assert.Equal(1.0f, maxU, 4);
            Assert.Equal(0.0f, minV, 4);
            Assert.Equal(1.0f, maxV, 4);
        }
    }

    [Fact]
    public void AlignToFacePlane_And_CsgAndExtrude_ProduceValidWorldAlignedUvs()
    {
        // 1. Extrude a wall face and verify new side walls have valid non-degenerate UVs
        EditableMesh box = PrimitiveGenerator.CreateBox(new Vector3(4.0f, 2.0f, 4.0f));
        int topFaceId = box.Faces.First(f => f.ComputeNormal(box).Y > 0.9f).Id;
        MeshTopologyOps.ExtrudeFaces(box, [topFaceId], 3.0f);

        foreach (PolygonFace face in box.Faces)
        {
            IReadOnlyList<Vector2> uvs = UvProjectionEngine.ComputeFaceUvs(face, box);
            Assert.True(uvs.Count >= 3);
            float area2D = 0.0f;
            for (int i = 0; i < uvs.Count; i++)
            {
                Vector2 a = uvs[i];
                Vector2 b = uvs[(i + 1) % uvs.Count];
                area2D += (a.X * b.Y) - (b.X * a.Y);
            }
            Assert.True(MathF.Abs(area2D) > 1e-4f);
        }

        // 2. CSG doorway subtraction preserves and generates valid world-aligned UVs on all faces
        EditableMesh wall = PrimitiveGenerator.CreateBox(new Vector3(10.0f, 4.0f, 0.5f));
        EditableMesh cutter = PrimitiveGenerator.CreateBox(new Vector3(2.0f, 2.5f, 2.0f));
        EditableMesh carved = CsgBooleanEngine.Evaluate(wall, cutter, CsgOperation.Subtract);

        foreach (PolygonFace face in carved.Faces)
        {
            UvProjectionEngine.AlignToFacePlane(face, carved);
            Assert.Equal(1.0f, face.UvMapping.UAxis.Length(), 3);
            Assert.Equal(1.0f, face.UvMapping.VAxis.Length(), 3);
            Assert.Equal(0.0f, Vector3.Dot(face.UvMapping.UAxis, face.UvMapping.VAxis), 3);
        }
    }
}
