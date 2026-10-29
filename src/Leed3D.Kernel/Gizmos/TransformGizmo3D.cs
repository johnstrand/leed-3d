using System.Numerics;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Viewport;

namespace Leed3D.Kernel.Gizmos;

/// <summary>
/// Active transformation tool mode for the 3D gizmo.
/// </summary>
public enum GizmoToolMode
{
    Translate,
    Rotate,
    Scale,
    Universal
}

/// <summary>
/// Axis or dual-axis plane constraint for gizmo manipulation.
/// </summary>
public enum GizmoAxisConstraint
{
    None,
    X,
    Y,
    Z,
    XY,
    XZ,
    YZ,
    Uniform
}

/// <summary>
/// 100% headless 3D Transform Gizmo supporting Translate arrows, dual-axis plane quads (XY, XZ, YZ),
/// Rotate rings, Scale cubes, constant screen-space sizing, ray hit-testing, and snapped drag math.
/// </summary>
public sealed class TransformGizmo3D
{
    private const float Epsilon = 1e-6f;
    private const float RadToDeg = 180.0f / MathF.PI;
    private const float DegToRad = MathF.PI / 180.0f;

    // Normalized handle geometry constants (multiplied by gizmoScale)
    public const float AxisStartFactor = 0.12f;
    public const float AxisEndFactor = 1.05f;
    public const float AxisHitRadiusFactor = 0.09f;

    public const float PlaneQuadOffsetFactor = 0.25f;
    public const float PlaneQuadSizeFactor = 0.22f;

    public const float RotateRingRadiusFactor = 0.90f;
    public const float RotateRingHitToleranceFactor = 0.10f;

    public const float CenterBoxHalfExtentFactor = 0.09f;

    private GizmoToolMode _lastUniversalSubMode = GizmoToolMode.Translate;

    public GizmoToolMode ToolMode { get; set; } = GizmoToolMode.Translate;
    public GizmoAxisConstraint HoveredConstraint { get; set; } = GizmoAxisConstraint.None;
    public GizmoAxisConstraint ActiveConstraint { get; private set; } = GizmoAxisConstraint.None;

    public bool IsDragging { get; private set; }
    public GizmoToolMode ActiveDragMode { get; private set; } = GizmoToolMode.Translate;
    public Vector3 DragPivotWorld { get; private set; }
    public Ray3D InitialDragRay { get; private set; }
    public Vector3 CameraForward { get; private set; } = Vector3.UnitZ;

    public Vector3 CurrentTranslationDelta { get; private set; }
    public Vector3 CurrentRotationDeltaDegrees { get; private set; }
    public float CurrentAngleDegrees { get; private set; }
    public Quaternion CurrentRotationQuaternion { get; private set; } = Quaternion.Identity;
    public Vector3 CurrentScaleMultiplier { get; private set; } = Vector3.One;

    /// <summary>
    /// Computes the world-space scale of the gizmo so it maintains a consistent size on screen
    /// regardless of camera distance or orthographic zoom level.
    /// </summary>
    public static float ComputeGizmoScale(EditorCamera camera, Vector3 gizmoPivot)
    {
        ArgumentNullException.ThrowIfNull(camera);

        if (camera.ProjectionMode == ViewportProjectionMode.Perspective)
        {
            float distance = Vector3.Distance(camera.Position, gizmoPivot);
            return System.Math.Max(0.15f, distance * 0.15f);
        }

        return System.Math.Max(0.15f, camera.OrthoSize * 0.14f);
    }

    /// <summary>
    /// Performs 3D ray hit-testing against the gizmo handles for the given <paramref name="mode"/>.
    /// Returns the highest-priority hit <see cref="GizmoAxisConstraint"/>, or <see cref="GizmoAxisConstraint.None"/>.
    /// </summary>
    public GizmoAxisConstraint HitTest(Ray3D worldRay, Vector3 gizmoPivot, float gizmoScale, GizmoToolMode mode)
    {
        float scale = System.Math.Max(0.01f, gizmoScale);

        // 1. Center Uniform Box (highest priority when clicking dead-center)
        float centerHalf = scale * CenterBoxHalfExtentFactor;
        var centerBox = new BoundingBox3D(
            gizmoPivot - new Vector3(centerHalf),
            gizmoPivot + new Vector3(centerHalf));

        if (worldRay.IntersectBoundingBox(centerBox, out _, out _))
        {
            _lastUniversalSubMode = mode == GizmoToolMode.Universal ? GizmoToolMode.Translate : mode;
            return GizmoAxisConstraint.Uniform;
        }

        // 2. Dual-Axis Plane Quads (XY, XZ, YZ) for Translate, Scale, and Universal modes
        if (mode is GizmoToolMode.Translate or GizmoToolMode.Scale or GizmoToolMode.Universal)
        {
            GizmoAxisConstraint planeHit = HitTestPlaneQuads(worldRay, gizmoPivot, scale);
            if (planeHit != GizmoAxisConstraint.None)
            {
                _lastUniversalSubMode = GizmoToolMode.Translate;
                return planeHit;
            }
        }

        // 3. Single-Axis Arrows / Scale stems for Translate, Scale, and Universal modes
        if (mode is GizmoToolMode.Translate or GizmoToolMode.Scale or GizmoToolMode.Universal)
        {
            GizmoAxisConstraint axisHit = HitTestAxisSegments(worldRay, gizmoPivot, scale);
            if (axisHit != GizmoAxisConstraint.None)
            {
                _lastUniversalSubMode = GizmoToolMode.Translate;
                return axisHit;
            }
        }

        // 4. Rotation Rings for Rotate and Universal modes
        if (mode is GizmoToolMode.Rotate or GizmoToolMode.Universal)
        {
            GizmoAxisConstraint ringHit = HitTestRotationRings(worldRay, gizmoPivot, scale);
            if (ringHit != GizmoAxisConstraint.None)
            {
                _lastUniversalSubMode = GizmoToolMode.Rotate;
                return ringHit;
            }
        }

        return GizmoAxisConstraint.None;
    }

    /// <summary>
    /// Starts an interactive gizmo drag operation.
    /// </summary>
    public void BeginDrag(
        GizmoToolMode mode,
        GizmoAxisConstraint constraint,
        Vector3 gizmoPivot,
        Ray3D initialWorldRay,
        Vector3? cameraForward = null)
    {
        if (constraint == GizmoAxisConstraint.None)
        {
            return;
        }

        IsDragging = true;
        ActiveDragMode = mode == GizmoToolMode.Universal ? ResolveUniversalSubMode(constraint) : mode;
        ActiveConstraint = constraint;
        DragPivotWorld = gizmoPivot;
        InitialDragRay = initialWorldRay;

        Vector3 fwd = cameraForward ?? initialWorldRay.Direction;
        CameraForward = fwd.LengthSquared() > Epsilon ? Vector3.Normalize(fwd) : Vector3.UnitZ;

        ResetCurrentDeltas();
    }

    /// <summary>
    /// Dynamically overrides the active axis or plane constraint mid-drag (e.g. pressing X/Y/Z or Shift+X/Y/Z).
    /// Subsequent <see cref="UpdateDrag"/> calls recalculate deltas from the initial drag ray using the new constraint.
    /// </summary>
    public void SetConstraintOverride(GizmoAxisConstraint newConstraint)
    {
        if (!IsDragging)
        {
            return;
        }

        ActiveConstraint = ActiveConstraint == newConstraint ? GizmoAxisConstraint.None : newConstraint;
        ResetCurrentDeltas();
    }

    /// <summary>
    /// Updates the current translation, rotation, or scale delta from <see cref="InitialDragRay"/> to <paramref name="currentWorldRay"/>.
    /// </summary>
    public void UpdateDrag(
        Ray3D currentWorldRay,
        bool snapGrid = false,
        float gridStep = 1.0f,
        bool snapAngle = false,
        float angleStepDegrees = 15.0f)
    {
        if (!IsDragging || ActiveConstraint == GizmoAxisConstraint.None)
        {
            return;
        }

        ResetCurrentDeltas();

        switch (ActiveDragMode)
        {
            case GizmoToolMode.Translate:
                UpdateTranslateDrag(currentWorldRay, snapGrid, gridStep);
                break;

            case GizmoToolMode.Rotate:
                UpdateRotateDrag(currentWorldRay, snapAngle, angleStepDegrees);
                break;

            case GizmoToolMode.Scale:
                UpdateScaleDrag(currentWorldRay, snapGrid, gridStep);
                break;
        }
    }

    /// <summary>
    /// Completes the active drag session.
    /// </summary>
    public void EndDrag()
    {
        IsDragging = false;
        ActiveConstraint = GizmoAxisConstraint.None;
    }

    /// <summary>
    /// Cancels the active drag session and resets deltas to identity.
    /// </summary>
    public void CancelDrag()
    {
        IsDragging = false;
        ActiveConstraint = GizmoAxisConstraint.None;
        ResetCurrentDeltas();
    }

    private void ResetCurrentDeltas()
    {
        CurrentTranslationDelta = Vector3.Zero;
        CurrentRotationDeltaDegrees = Vector3.Zero;
        CurrentAngleDegrees = 0.0f;
        CurrentRotationQuaternion = Quaternion.Identity;
        CurrentScaleMultiplier = Vector3.One;
    }

    private GizmoToolMode ResolveUniversalSubMode(GizmoAxisConstraint constraint)
    {
        return _lastUniversalSubMode;
    }

    private void UpdateTranslateDrag(Ray3D currentWorldRay, bool snapGrid, float gridStep)
    {
        if (ActiveConstraint is GizmoAxisConstraint.X or GizmoAxisConstraint.Y or GizmoAxisConstraint.Z)
        {
            Vector3 axisDir = GetPrimaryAxisVector(ActiveConstraint);
            Plane3D dragPlane = BuildAxisDragPlane(DragPivotWorld, axisDir, InitialDragRay.Direction);

            if (!InitialDragRay.IntersectPlane(dragPlane, out _, out Vector3 startHit) ||
                !currentWorldRay.IntersectPlane(dragPlane, out _, out Vector3 currHit))
            {
                return;
            }

            float startProj = Vector3.Dot(startHit - DragPivotWorld, axisDir);
            float currProj = Vector3.Dot(currHit - DragPivotWorld, axisDir);
            float deltaScalar = currProj - startProj;

            if (snapGrid)
            {
                deltaScalar = GridSnapper.SnapScalar(deltaScalar, gridStep);
            }

            CurrentTranslationDelta = axisDir * deltaScalar;
        }
        else if (ActiveConstraint is GizmoAxisConstraint.XY or GizmoAxisConstraint.XZ or GizmoAxisConstraint.YZ)
        {
            Vector3 planeNormal = GetPlaneNormalVector(ActiveConstraint);
            Plane3D dragPlane = Plane3D.CreateFromPointAndNormal(DragPivotWorld, planeNormal);

            if (!InitialDragRay.IntersectPlane(dragPlane, out _, out Vector3 startHit) ||
                !currentWorldRay.IntersectPlane(dragPlane, out _, out Vector3 currHit))
            {
                return;
            }

            Vector3 delta = currHit - startHit;

            // Lock the normal axis component to exact 0
            switch (ActiveConstraint)
            {
                case GizmoAxisConstraint.XY:
                    delta.Z = 0.0f;
                    if (snapGrid)
                    {
                        delta = GridSnapper.SnapVector(delta, gridStep, snapX: true, snapY: true, snapZ: false);
                    }
                    break;

                case GizmoAxisConstraint.XZ:
                    delta.Y = 0.0f;
                    if (snapGrid)
                    {
                        delta = GridSnapper.SnapVector(delta, gridStep, snapX: true, snapY: false, snapZ: true);
                    }
                    break;

                case GizmoAxisConstraint.YZ:
                    delta.X = 0.0f;
                    if (snapGrid)
                    {
                        delta = GridSnapper.SnapVector(delta, gridStep, snapX: false, snapY: true, snapZ: true);
                    }
                    break;
            }

            CurrentTranslationDelta = delta;
        }
        else if (ActiveConstraint == GizmoAxisConstraint.Uniform)
        {
            Plane3D viewPlane = Plane3D.CreateFromPointAndNormal(DragPivotWorld, CameraForward);
            if (!InitialDragRay.IntersectPlane(viewPlane, out _, out Vector3 startHit) ||
                !currentWorldRay.IntersectPlane(viewPlane, out _, out Vector3 currHit))
            {
                return;
            }

            Vector3 delta = currHit - startHit;
            if (snapGrid)
            {
                delta = GridSnapper.SnapVector(delta, gridStep);
            }

            CurrentTranslationDelta = delta;
        }
    }

    private void UpdateRotateDrag(Ray3D currentWorldRay, bool snapAngle, float angleStepDegrees)
    {
        Vector3 rotAxis = ActiveConstraint switch
        {
            GizmoAxisConstraint.X or GizmoAxisConstraint.YZ => Vector3.UnitX,
            GizmoAxisConstraint.Y or GizmoAxisConstraint.XZ => Vector3.UnitY,
            GizmoAxisConstraint.Z or GizmoAxisConstraint.XY => Vector3.UnitZ,
            _ => CameraForward
        };

        Plane3D rotPlane = Plane3D.CreateFromPointAndNormal(DragPivotWorld, rotAxis);

        if (!InitialDragRay.IntersectPlane(rotPlane, out _, out Vector3 startHit) ||
            !currentWorldRay.IntersectPlane(rotPlane, out _, out Vector3 currHit))
        {
            return;
        }

        Vector3 vStart = startHit - DragPivotWorld;
        Vector3 vCurr = currHit - DragPivotWorld;

        // Project onto rotation plane
        vStart -= rotAxis * Vector3.Dot(vStart, rotAxis);
        vCurr -= rotAxis * Vector3.Dot(vCurr, rotAxis);

        if (vStart.LengthSquared() < 1e-8f || vCurr.LengthSquared() < 1e-8f)
        {
            return;
        }

        vStart = Vector3.Normalize(vStart);
        vCurr = Vector3.Normalize(vCurr);

        float cos = System.Math.Clamp(Vector3.Dot(vStart, vCurr), -1.0f, 1.0f);
        float sin = Vector3.Dot(rotAxis, Vector3.Cross(vStart, vCurr));
        float angleDeg = MathF.Atan2(sin, cos) * RadToDeg;

        if (snapAngle)
        {
            angleDeg = GridSnapper.SnapAngleDegrees(angleDeg, angleStepDegrees);
        }

        CurrentAngleDegrees = angleDeg;
        CurrentRotationDeltaDegrees = rotAxis * angleDeg;
        CurrentRotationQuaternion = Quaternion.CreateFromAxisAngle(rotAxis, angleDeg * DegToRad);
    }

    private void UpdateScaleDrag(Ray3D currentWorldRay, bool snapGrid, float gridStep)
    {
        if (ActiveConstraint is GizmoAxisConstraint.X or GizmoAxisConstraint.Y or GizmoAxisConstraint.Z)
        {
            Vector3 axisDir = GetPrimaryAxisVector(ActiveConstraint);
            Plane3D dragPlane = BuildAxisDragPlane(DragPivotWorld, axisDir, InitialDragRay.Direction);

            if (!InitialDragRay.IntersectPlane(dragPlane, out _, out Vector3 startHit) ||
                !currentWorldRay.IntersectPlane(dragPlane, out _, out Vector3 currHit))
            {
                return;
            }

            float startProj = Vector3.Dot(startHit - DragPivotWorld, axisDir);
            float currProj = Vector3.Dot(currHit - DragPivotWorld, axisDir);
            float deltaScalar = currProj - startProj;

            if (snapGrid)
            {
                deltaScalar = GridSnapper.SnapScalar(deltaScalar, gridStep);
            }

            float factor = ClampScaleFactor(1.0f + deltaScalar);
            CurrentScaleMultiplier = ActiveConstraint switch
            {
                GizmoAxisConstraint.X => new Vector3(factor, 1.0f, 1.0f),
                GizmoAxisConstraint.Y => new Vector3(1.0f, factor, 1.0f),
                GizmoAxisConstraint.Z => new Vector3(1.0f, 1.0f, factor),
                _ => Vector3.One
            };
        }
        else if (ActiveConstraint is GizmoAxisConstraint.XY or GizmoAxisConstraint.XZ or GizmoAxisConstraint.YZ)
        {
            Vector3 planeNormal = GetPlaneNormalVector(ActiveConstraint);
            Plane3D dragPlane = Plane3D.CreateFromPointAndNormal(DragPivotWorld, planeNormal);

            if (!InitialDragRay.IntersectPlane(dragPlane, out _, out Vector3 startHit) ||
                !currentWorldRay.IntersectPlane(dragPlane, out _, out Vector3 currHit))
            {
                return;
            }

            Vector3 delta = currHit - startHit;
            if (snapGrid)
            {
                delta = GridSnapper.SnapVector(delta, gridStep);
            }

            CurrentScaleMultiplier = ActiveConstraint switch
            {
                GizmoAxisConstraint.XY => new Vector3(ClampScaleFactor(1.0f + delta.X), ClampScaleFactor(1.0f + delta.Y), 1.0f),
                GizmoAxisConstraint.XZ => new Vector3(ClampScaleFactor(1.0f + delta.X), 1.0f, ClampScaleFactor(1.0f + delta.Z)),
                GizmoAxisConstraint.YZ => new Vector3(1.0f, ClampScaleFactor(1.0f + delta.Y), ClampScaleFactor(1.0f + delta.Z)),
                _ => Vector3.One
            };
        }
        else if (ActiveConstraint == GizmoAxisConstraint.Uniform)
        {
            Plane3D viewPlane = Plane3D.CreateFromPointAndNormal(DragPivotWorld, CameraForward);
            if (!InitialDragRay.IntersectPlane(viewPlane, out _, out Vector3 startHit) ||
                !currentWorldRay.IntersectPlane(viewPlane, out _, out Vector3 currHit))
            {
                return;
            }

            Vector3 delta = currHit - startHit;
            float uniformDelta = (delta.X + delta.Y + delta.Z) / 3.0f;
            if (MathF.Abs(uniformDelta) < 1e-4f)
            {
                uniformDelta = delta.X + delta.Y;
            }

            if (snapGrid)
            {
                uniformDelta = GridSnapper.SnapScalar(uniformDelta, gridStep);
            }

            float factor = ClampScaleFactor(1.0f + uniformDelta);
            CurrentScaleMultiplier = new Vector3(factor, factor, factor);
        }
    }

    private static float ClampScaleFactor(float factor)
    {
        return System.Math.Max(0.05f, factor);
    }

    public static Vector3 GetPrimaryAxisVector(GizmoAxisConstraint constraint)
    {
        return constraint switch
        {
            GizmoAxisConstraint.X => Vector3.UnitX,
            GizmoAxisConstraint.Y => Vector3.UnitY,
            GizmoAxisConstraint.Z => Vector3.UnitZ,
            _ => Vector3.UnitX
        };
    }

    public static Vector3 GetPlaneNormalVector(GizmoAxisConstraint constraint)
    {
        return constraint switch
        {
            GizmoAxisConstraint.XY => Vector3.UnitZ,
            GizmoAxisConstraint.XZ => Vector3.UnitY,
            GizmoAxisConstraint.YZ => Vector3.UnitX,
            _ => Vector3.UnitY
        };
    }

    /// <summary>
    /// Constructs a plane passing through <paramref name="pivot"/> that contains <paramref name="axisDir"/>
    /// and is as perpendicular as possible to <paramref name="viewDir"/>.
    /// </summary>
    public static Plane3D BuildAxisDragPlane(Vector3 pivot, Vector3 axisDir, Vector3 viewDir)
    {
        Vector3 tangent = Vector3.Cross(viewDir, axisDir);
        if (tangent.LengthSquared() < 1e-6f)
        {
            Vector3 fallback = MathF.Abs(Vector3.Dot(axisDir, Vector3.UnitY)) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
            tangent = Vector3.Cross(fallback, axisDir);
        }

        Vector3 planeNormal = Vector3.Normalize(Vector3.Cross(axisDir, tangent));
        return Plane3D.CreateFromPointAndNormal(pivot, planeNormal);
    }

    private static GizmoAxisConstraint HitTestPlaneQuads(Ray3D worldRay, Vector3 pivot, float scale)
    {
        float minOffset = scale * PlaneQuadOffsetFactor;
        float maxOffset = scale * (PlaneQuadOffsetFactor + PlaneQuadSizeFactor);

        GizmoAxisConstraint bestConstraint = GizmoAxisConstraint.None;
        float bestDist = float.PositiveInfinity;

        // XY plane (Z = pivot.Z)
        Plane3D planeXY = Plane3D.CreateFromPointAndNormal(pivot, Vector3.UnitZ);
        if (worldRay.IntersectPlane(planeXY, out float distXY, out Vector3 hitXY))
        {
            float dx = hitXY.X - pivot.X;
            float dy = hitXY.Y - pivot.Y;
            if (dx >= minOffset && dx <= maxOffset && dy >= minOffset && dy <= maxOffset && distXY < bestDist)
            {
                bestDist = distXY;
                bestConstraint = GizmoAxisConstraint.XY;
            }
        }

        // XZ plane (Y = pivot.Y)
        Plane3D planeXZ = Plane3D.CreateFromPointAndNormal(pivot, Vector3.UnitY);
        if (worldRay.IntersectPlane(planeXZ, out float distXZ, out Vector3 hitXZ))
        {
            float dx = hitXZ.X - pivot.X;
            float dz = hitXZ.Z - pivot.Z;
            if (dx >= minOffset && dx <= maxOffset && dz >= minOffset && dz <= maxOffset && distXZ < bestDist)
            {
                bestDist = distXZ;
                bestConstraint = GizmoAxisConstraint.XZ;
            }
        }

        // YZ plane (X = pivot.X)
        Plane3D planeYZ = Plane3D.CreateFromPointAndNormal(pivot, Vector3.UnitX);
        if (worldRay.IntersectPlane(planeYZ, out float distYZ, out Vector3 hitYZ))
        {
            float dy = hitYZ.Y - pivot.Y;
            float dz = hitYZ.Z - pivot.Z;
            if (dy >= minOffset && dy <= maxOffset && dz >= minOffset && dz <= maxOffset && distYZ < bestDist)
            {
                bestDist = distYZ;
                bestConstraint = GizmoAxisConstraint.YZ;
            }
        }

        return bestConstraint;
    }

    private static GizmoAxisConstraint HitTestAxisSegments(Ray3D worldRay, Vector3 pivot, float scale)
    {
        float startOffset = scale * AxisStartFactor;
        float endOffset = scale * AxisEndFactor;
        float hitRadius = scale * AxisHitRadiusFactor;

        GizmoAxisConstraint bestAxis = GizmoAxisConstraint.None;
        float bestProximity = hitRadius;

        ReadOnlySpan<(GizmoAxisConstraint Axis, Vector3 Dir)> axes =
        [
            (GizmoAxisConstraint.X, Vector3.UnitX),
            (GizmoAxisConstraint.Y, Vector3.UnitY),
            (GizmoAxisConstraint.Z, Vector3.UnitZ)
        ];

        for (int i = 0; i < axes.Length; i++)
        {
            var (axis, dir) = axes[i];
            Vector3 segStart = pivot + (dir * startOffset);
            Vector3 segEnd = pivot + (dir * endOffset);

            float dist = DistanceRayToSegment(worldRay, segStart, segEnd);
            if (dist <= bestProximity)
            {
                bestProximity = dist;
                bestAxis = axis;
            }
        }

        return bestAxis;
    }

    private static GizmoAxisConstraint HitTestRotationRings(Ray3D worldRay, Vector3 pivot, float scale)
    {
        float ringRadius = scale * RotateRingRadiusFactor;
        float tolerance = scale * RotateRingHitToleranceFactor;

        GizmoAxisConstraint bestRing = GizmoAxisConstraint.None;
        float bestRingError = tolerance;

        ReadOnlySpan<(GizmoAxisConstraint Axis, Vector3 Normal)> rings =
        [
            (GizmoAxisConstraint.X, Vector3.UnitX),
            (GizmoAxisConstraint.Y, Vector3.UnitY),
            (GizmoAxisConstraint.Z, Vector3.UnitZ)
        ];

        for (int i = 0; i < rings.Length; i++)
        {
            var (axis, normal) = rings[i];
            Plane3D ringPlane = Plane3D.CreateFromPointAndNormal(pivot, normal);

            if (worldRay.IntersectPlane(ringPlane, out _, out Vector3 hitPoint))
            {
                float radialDist = Vector3.Distance(hitPoint, pivot);
                float error = MathF.Abs(radialDist - ringRadius);
                if (error <= bestRingError)
                {
                    bestRingError = error;
                    bestRing = axis;
                }
            }
        }

        return bestRing;
    }

    /// <summary>
    /// Computes the closest 3D distance between a <see cref="Ray3D"/> and a line segment AB.
    /// </summary>
    public static float DistanceRayToSegment(Ray3D ray, Vector3 a, Vector3 b)
    {
        Vector3 u = ray.Direction;
        Vector3 v = b - a;
        Vector3 w = ray.Origin - a;

        float aDot = Vector3.Dot(u, u); // 1.0f
        float bDot = Vector3.Dot(u, v);
        float cDot = Vector3.Dot(v, v);
        float dDot = Vector3.Dot(u, w);
        float eDot = Vector3.Dot(v, w);

        if (cDot < 1e-10f)
        {
            float tPt = System.Math.Max(0.0f, -dDot / aDot);
            return Vector3.Distance(ray.GetPoint(tPt), a);
        }

        float denom = (aDot * cDot) - (bDot * bDot);
        float sN, sD = denom;
        float tN, tD = denom;

        if (denom < 1e-8f)
        {
            sN = 0.0f;
            sD = 1.0f;
            tN = eDot;
            tD = cDot;
        }
        else
        {
            sN = (bDot * eDot) - (cDot * dDot);
            tN = (aDot * eDot) - (bDot * dDot);
            if (sN < 0.0f)
            {
                sN = 0.0f;
                tN = eDot;
                tD = cDot;
            }
        }

        if (tN < 0.0f)
        {
            tN = 0.0f;
            if (-dDot < 0.0f)
            {
                sN = 0.0f;
            }
            else
            {
                sN = -dDot;
                sD = aDot;
            }
        }
        else if (tN > tD)
        {
            tN = tD;
            if ((-dDot + bDot) < 0.0f)
            {
                sN = 0.0f;
            }
            else
            {
                sN = -dDot + bDot;
                sD = aDot;
            }
        }

        float sc = MathF.Abs(sN) < 1e-8f ? 0.0f : sN / sD;
        float tc = MathF.Abs(tN) < 1e-8f ? 0.0f : tN / tD;

        sc = System.Math.Max(0.0f, sc);
        tc = System.Math.Clamp(tc, 0.0f, 1.0f);

        Vector3 rayPt = ray.Origin + (u * sc);
        Vector3 segPt = a + (v * tc);
        return Vector3.Distance(rayPt, segPt);
    }
}
