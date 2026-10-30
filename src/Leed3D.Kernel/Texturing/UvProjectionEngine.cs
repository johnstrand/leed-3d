using System.Numerics;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Scene;

namespace Leed3D.Kernel.Texturing;

/// <summary>
/// Valve-220 / TrenchBroom planar UV projection engine providing 6-axis cardinal world-plane box projection,
/// face-plane edge alignment, per-vertex UV evaluation, Fit-to-Face bounds calculation, and Texture Lock
/// transform invariance compensation.
/// </summary>
public static class UvProjectionEngine
{
    private const float MinScaleMagnitude = 1e-5f;
    private const float DegToRad = MathF.PI / 180.0f;

    /// <summary>
    /// Selects the 6-axis cardinal world-plane projection axes <c>(UAxis, VAxis)</c> for a given surface normal:
    /// <list type="bullet">
    /// <item><description>Dominant Y (floor/ceiling): <c>UAxis = (1, 0, 0)</c>, <c>VAxis = (0, 0, 1)</c></description></item>
    /// <item><description>Dominant X (left/right wall): <c>UAxis = (0, 0, 1)</c>, <c>VAxis = (0, -1, 0)</c></description></item>
    /// <item><description>Dominant Z (front/back wall): <c>UAxis = (1, 0, 0)</c>, <c>VAxis = (0, -1, 0)</c></description></item>
    /// </list>
    /// </summary>
    public static (Vector3 UAxis, Vector3 VAxis) GetDefaultAxesForNormal(Vector3 normal)
    {
        float ax = MathF.Abs(normal.X);
        float ay = MathF.Abs(normal.Y);
        float az = MathF.Abs(normal.Z);

        // Dominant Y (floor/ceiling)
        if (ay >= ax && ay >= az)
        {
            return (new Vector3(1.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.0f, 1.0f));
        }

        // Dominant X (left/right wall)
        if (ax >= ay && ax >= az)
        {
            return (new Vector3(0.0f, 0.0f, 1.0f), new Vector3(0.0f, -1.0f, 0.0f));
        }

        // Dominant Z (front/back wall)
        return (new Vector3(1.0f, 0.0f, 0.0f), new Vector3(0.0f, -1.0f, 0.0f));
    }

    /// <summary>
    /// Refreshes <see cref="FaceUvMapping.UAxis"/> and <see cref="FaceUvMapping.VAxis"/> from the face's current
    /// world normal when <see cref="FaceUvMapping.TextureLock"/> is <c>false</c>.
    /// </summary>
    public static void RefreshUnlockedFaceAxes(PolygonFace face, EditableMesh mesh, Transform3D? objTransform = null)
    {
        ArgumentNullException.ThrowIfNull(face);
        ArgumentNullException.ThrowIfNull(mesh);

        if (face.UvMapping.TextureLock)
        {
            return;
        }

        Vector3 normal = ComputeWorldNormal(face, mesh, objTransform);
        (face.UvMapping.UAxis, face.UvMapping.VAxis) = GetDefaultAxesForNormal(normal);
    }

    /// <summary>
    /// Refreshes cardinal world-plane projection axes for all unlocked faces in <paramref name="mesh"/>.
    /// </summary>
    public static void RefreshUnlockedMeshAxes(EditableMesh mesh, Transform3D? objTransform = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        for (int i = 0; i < mesh.Faces.Count; i++)
        {
            RefreshUnlockedFaceAxes(mesh.Faces[i], mesh, objTransform);
        }
    }

    /// <summary>
    /// Aligns <paramref name="face"/>'s <see cref="FaceUvMapping"/> to the dominant cardinal world-plane box axes.
    /// </summary>
    public static void AlignToWorldBox(PolygonFace face, EditableMesh mesh, Transform3D? objTransform = null)
    {
        ArgumentNullException.ThrowIfNull(face);
        ArgumentNullException.ThrowIfNull(mesh);

        Vector3 normal = ComputeWorldNormal(face, mesh, objTransform);
        (face.UvMapping.UAxis, face.UvMapping.VAxis) = GetDefaultAxesForNormal(normal);
    }

    /// <summary>
    /// Aligns <see cref="FaceUvMapping.UAxis"/> along the face's first valid edge <c>(v1 - v0)</c>
    /// and sets <c>VAxis = Normalize(Cross(faceNormal, UAxis))</c>.
    /// </summary>
    public static void AlignToFacePlane(PolygonFace face, EditableMesh mesh, Transform3D? objTransform = null)
    {
        ArgumentNullException.ThrowIfNull(face);
        ArgumentNullException.ThrowIfNull(mesh);

        Vector3 faceNormal = ComputeWorldNormal(face, mesh, objTransform);
        int count = face.VertexIds.Count;

        for (int i = 0; i < count; i++)
        {
            MeshVertex? v0 = mesh.GetVertex(face.VertexIds[i]);
            MeshVertex? v1 = mesh.GetVertex(face.VertexIds[(i + 1) % count]);
            if (v0 is null || v1 is null)
            {
                continue;
            }

            Vector3 p0 = objTransform is not null ? objTransform.TransformPointToWorld(v0.Position) : v0.Position;
            Vector3 p1 = objTransform is not null ? objTransform.TransformPointToWorld(v1.Position) : v1.Position;
            Vector3 edge = p1 - p0;
            float edgeLenSq = edge.LengthSquared();
            if (edgeLenSq <= 1e-10f)
            {
                continue;
            }

            Vector3 uAxis = edge / MathF.Sqrt(edgeLenSq);
            Vector3 vAxis = Vector3.Cross(faceNormal, uAxis);
            float vLenSq = vAxis.LengthSquared();
            if (vLenSq > 1e-10f)
            {
                face.UvMapping.UAxis = uAxis;
                face.UvMapping.VAxis = vAxis / MathF.Sqrt(vLenSq);
                return;
            }
        }

        (face.UvMapping.UAxis, face.UvMapping.VAxis) = GetDefaultAxesForNormal(faceNormal);
    }

    /// <summary>
    /// Returns <c>(UAxis, VAxis)</c> rotated around <c>Cross(UAxis, VAxis)</c> by <see cref="FaceUvMapping.RotationDegrees"/>.
    /// </summary>
    public static (Vector3 RotatedU, Vector3 RotatedV) GetRotatedAxes(FaceUvMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        Vector3 uAxis = mapping.UAxis;
        Vector3 vAxis = mapping.VAxis;

        if (uAxis.LengthSquared() < 1e-10f || vAxis.LengthSquared() < 1e-10f)
        {
            uAxis = Vector3.UnitX;
            vAxis = Vector3.UnitZ;
        }

        if (MathF.Abs(mapping.RotationDegrees) > 1e-6f)
        {
            Vector3 axisNormal = Vector3.Cross(uAxis, vAxis);
            float lenSq = axisNormal.LengthSquared();
            if (lenSq > 1e-12f)
            {
                axisNormal /= MathF.Sqrt(lenSq);
                Quaternion rot = Quaternion.CreateFromAxisAngle(axisNormal, mapping.RotationDegrees * DegToRad);
                uAxis = Vector3.Transform(uAxis, rot);
                vAxis = Vector3.Transform(vAxis, rot);
            }
        }

        return (uAxis, vAxis);
    }

    /// <summary>
    /// Evaluates the 2D texture coordinate <c>(u, v)</c> for a 3D position using the specified <see cref="FaceUvMapping"/>.
    /// </summary>
    public static Vector2 ComputeUv(Vector3 pos, FaceUvMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        (Vector3 rotatedU, Vector3 rotatedV) = GetRotatedAxes(mapping);
        float scaleX = SafeScale(mapping.Scale.X);
        float scaleY = SafeScale(mapping.Scale.Y);

        float u = (Vector3.Dot(pos, rotatedU) / scaleX) + mapping.Offset.X;
        float v = (Vector3.Dot(pos, rotatedV) / scaleY) + mapping.Offset.Y;
        return new Vector2(u, v);
    }

    /// <summary>
    /// Computes the world-space projected <c>(u, v)</c> coordinates for all vertices of <paramref name="face"/>.
    /// </summary>
    public static IReadOnlyList<Vector2> ComputeFaceUvs(PolygonFace face, EditableMesh mesh, Transform3D? objTransform = null)
    {
        ArgumentNullException.ThrowIfNull(face);
        ArgumentNullException.ThrowIfNull(mesh);

        int count = face.VertexIds.Count;
        var uvs = new Vector2[count];

        for (int i = 0; i < count; i++)
        {
            MeshVertex? vert = mesh.GetVertex(face.VertexIds[i]);
            if (vert is null)
            {
                uvs[i] = Vector2.Zero;
                continue;
            }

            Vector3 worldPos = objTransform is not null
                ? objTransform.TransformPointToWorld(vert.Position)
                : vert.Position;

            uvs[i] = ComputeUv(worldPos, face.UvMapping);
        }

        return uvs;
    }

    /// <summary>
    /// Convenience overload for <see cref="FitToFace(PolygonFace, EditableMesh, Transform3D?, float, float)"/> without an object transform.
    /// </summary>
    public static void FitToFace(PolygonFace face, EditableMesh mesh, float repeatsU, float repeatsV)
    {
        FitToFace(face, mesh, objTransform: null, repeatsU, repeatsV);
    }

    /// <summary>
    /// Projects all vertices of <paramref name="face"/> onto <c>(rotatedU, rotatedV)</c> and adjusts
    /// <see cref="FaceUvMapping.Scale"/> and <see cref="FaceUvMapping.Offset"/> so the face's UVs span
    /// exactly <c>[0..repeatsU, 0..repeatsV]</c>.
    /// </summary>
    public static void FitToFace(
        PolygonFace face,
        EditableMesh mesh,
        Transform3D? objTransform = null,
        float repeatsU = 1.0f,
        float repeatsV = 1.0f)
    {
        ArgumentNullException.ThrowIfNull(face);
        ArgumentNullException.ThrowIfNull(mesh);

        if (face.VertexIds.Count < 3)
        {
            return;
        }

        // Project vertices using current axes; if degenerate (e.g. face normal rotated perpendicular to old axes), align axes first
        if (!TryComputeProjectedExtents(face, mesh, objTransform, out float minU, out float maxU, out float minV, out float maxV) ||
            (maxU - minU) < 1e-5f ||
            (maxV - minV) < 1e-5f)
        {
            AlignToWorldBox(face, mesh, objTransform);
            if (!TryComputeProjectedExtents(face, mesh, objTransform, out minU, out maxU, out minV, out maxV) ||
                (maxU - minU) < 1e-5f ||
                (maxV - minV) < 1e-5f)
            {
                AlignToFacePlane(face, mesh, objTransform);
                TryComputeProjectedExtents(face, mesh, objTransform, out minU, out maxU, out minV, out maxV);
            }
        }

        float spanU = System.Math.Max(1e-5f, maxU - minU);
        float spanV = System.Math.Max(1e-5f, maxV - minV);

        float safeRepeatsU = SafeScale(repeatsU);
        float safeRepeatsV = SafeScale(repeatsV);

        float newScaleX = spanU / safeRepeatsU;
        float newScaleY = spanV / safeRepeatsV;

        float newOffsetX = -minU / newScaleX;
        float newOffsetY = -minV / newScaleY;

        face.UvMapping.Scale = new Vector2(newScaleX, newScaleY);
        face.UvMapping.Offset = new Vector2(newOffsetX, newOffsetY);
    }

    /// <summary>
    /// Compensates a texture-locked <see cref="FaceUvMapping"/> for a 3D translation and/or rotation around <paramref name="pivot"/>
    /// so that every vertex on the face retains its exact <c>(u, v)</c> coordinates before and after the transform.
    /// Does nothing if <see cref="FaceUvMapping.TextureLock"/> is <c>false</c>.
    /// </summary>
    public static void ApplyTransformToLockedUv(
        FaceUvMapping mapping,
        Vector3 translationDelta,
        Quaternion rotationDelta,
        Vector3 pivot = default)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        if (!mapping.TextureLock)
        {
            return;
        }

        Quaternion normalizedRot = Quaternion.Normalize(rotationDelta);

        // Rotate projection axes by the rotation delta
        mapping.UAxis = Vector3.Transform(mapping.UAxis, normalizedRot);
        mapping.VAxis = Vector3.Transform(mapping.VAxis, normalizedRot);

        // A point p_before rotated around pivot and translated by translationDelta moves to:
        // p_after = pivot + R * (p_before - pivot) + translationDelta = R * p_before + ((pivot - R * pivot) + translationDelta)
        Vector3 effectiveDelta = (pivot - Vector3.Transform(pivot, normalizedRot)) + translationDelta;

        (Vector3 rotatedU, Vector3 rotatedV) = GetRotatedAxes(mapping);
        float scaleX = SafeScale(mapping.Scale.X);
        float scaleY = SafeScale(mapping.Scale.Y);

        float offsetDeltaU = -Vector3.Dot(effectiveDelta, rotatedU) / scaleX;
        float offsetDeltaV = -Vector3.Dot(effectiveDelta, rotatedV) / scaleY;

        mapping.Offset = new Vector2(
            mapping.Offset.X + offsetDeltaU,
            mapping.Offset.Y + offsetDeltaV);
    }

    /// <summary>
    /// Compensates a texture-locked <see cref="FaceUvMapping"/> for scaling around <paramref name="pivot"/>.
    /// </summary>
    public static void ApplyScaleToLockedUv(
        FaceUvMapping mapping,
        Vector3 scaleMultiplier,
        Vector3 pivot = default)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        if (!mapping.TextureLock)
        {
            return;
        }

        (Vector3 rotatedU, Vector3 rotatedV) = GetRotatedAxes(mapping);

        float scaleU = (rotatedU * scaleMultiplier).Length();
        float scaleV = (rotatedV * scaleMultiplier).Length();
        if (scaleU < MinScaleMagnitude) scaleU = 1.0f;
        if (scaleV < MinScaleMagnitude) scaleV = 1.0f;

        float oldScaleX = SafeScale(mapping.Scale.X);
        float oldScaleY = SafeScale(mapping.Scale.Y);
        float newScaleX = oldScaleX * scaleU;
        float newScaleY = oldScaleY * scaleV;

        float pivotProjU = Vector3.Dot(pivot, rotatedU);
        float pivotProjV = Vector3.Dot(pivot, rotatedV);

        float newOffsetX = mapping.Offset.X + (pivotProjU * (scaleU - 1.0f) / newScaleX);
        float newOffsetY = mapping.Offset.Y + (pivotProjV * (scaleV - 1.0f) / newScaleY);

        mapping.Scale = new Vector2(newScaleX, newScaleY);
        mapping.Offset = new Vector2(newOffsetX, newOffsetY);
    }

    private static bool TryComputeProjectedExtents(
        PolygonFace face,
        EditableMesh mesh,
        Transform3D? objTransform,
        out float minU,
        out float maxU,
        out float minV,
        out float maxV)
    {
        minU = float.PositiveInfinity;
        maxU = float.NegativeInfinity;
        minV = float.PositiveInfinity;
        maxV = float.NegativeInfinity;

        (Vector3 rotatedU, Vector3 rotatedV) = GetRotatedAxes(face.UvMapping);
        int validCount = 0;

        for (int i = 0; i < face.VertexIds.Count; i++)
        {
            MeshVertex? vert = mesh.GetVertex(face.VertexIds[i]);
            if (vert is null)
            {
                continue;
            }

            Vector3 worldPos = objTransform is not null
                ? objTransform.TransformPointToWorld(vert.Position)
                : vert.Position;

            float pu = Vector3.Dot(worldPos, rotatedU);
            float pv = Vector3.Dot(worldPos, rotatedV);

            if (pu < minU) minU = pu;
            if (pu > maxU) maxU = pu;
            if (pv < minV) minV = pv;
            if (pv > maxV) maxV = pv;
            validCount++;
        }

        return validCount >= 3;
    }

    private static Vector3 ComputeWorldNormal(PolygonFace face, EditableMesh mesh, Transform3D? objTransform)
    {
        Vector3 localNormal = face.ComputeNormal(mesh);
        if (objTransform is null)
        {
            return localNormal;
        }

        Vector3 worldNormal = objTransform.TransformDirectionToWorld(localNormal);
        float lenSq = worldNormal.LengthSquared();
        return lenSq > 1e-12f ? worldNormal / MathF.Sqrt(lenSq) : localNormal;
    }

    private static float SafeScale(float scale)
    {
        if (MathF.Abs(scale) < MinScaleMagnitude)
        {
            return scale < 0.0f ? -MinScaleMagnitude : MinScaleMagnitude;
        }

        return scale;
    }
}
