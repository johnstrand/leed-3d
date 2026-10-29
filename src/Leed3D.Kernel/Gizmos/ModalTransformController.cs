using System.Globalization;
using System.Numerics;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Selection;

namespace Leed3D.Kernel.Gizmos;

/// <summary>
/// Operational mode for modal transform operators (G / R / S).
/// </summary>
public enum ModalTransformMode
{
    None,
    Move,
    Rotate,
    Scale
}

/// <summary>
/// 100% headless controller for Blender-style modal transform operators (G Grab/Move, R Rotate, S Scale).
/// Supports instant single-axis locks (X, Y, Z), dual-axis plane locks (Shift+X for YZ, Shift+Y for XZ, Shift+Z for XY),
/// constraint toggling back to view-plane, typed numeric input buffers (e.g. "-4.5", "90"), and transaction commit/cancel.
/// </summary>
public sealed class ModalTransformController
{
    private const float RadToDeg = 180.0f / MathF.PI;
    private const float DegToRad = MathF.PI / 180.0f;
    private const float Epsilon = 1e-6f;

    public bool IsActive { get; private set; }
    public ModalTransformMode Mode { get; private set; } = ModalTransformMode.None;
    public GizmoAxisConstraint Constraint { get; private set; } = GizmoAxisConstraint.None;

    public Vector3 PivotWorld { get; private set; }
    public Ray3D InitialRay { get; private set; }
    public Ray3D LastRay { get; private set; }
    public Vector3 CameraForward { get; private set; } = Vector3.UnitZ;

    public string NumericInputBuffer { get; private set; } = string.Empty;
    public bool HasNumericInput => !string.IsNullOrEmpty(NumericInputBuffer);

    public float? ParsedNumericValue
    {
        get
        {
            if (string.IsNullOrWhiteSpace(NumericInputBuffer))
            {
                return null;
            }

            if (float.TryParse(NumericInputBuffer, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
            {
                return val;
            }

            return null;
        }
    }

    public Vector3 CurrentTranslationDelta { get; private set; }
    public Vector3 CurrentRotationDeltaDegrees { get; private set; }
    public float CurrentAngleDegrees { get; private set; }
    public Quaternion CurrentRotationQuaternion { get; private set; } = Quaternion.Identity;
    public Vector3 CurrentScaleMultiplier { get; private set; } = Vector3.One;

    public TransformApplicator Applicator { get; } = new();

    /// <summary>
    /// Starts a modal transform operator session.
    /// </summary>
    public bool Start(
        ModalTransformMode mode,
        EditorScene scene,
        SelectionManager selection,
        Ray3D initialRay,
        Vector3 cameraForward,
        UndoRedoStack? undoStack = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(selection);

        if (mode == ModalTransformMode.None || !selection.HasSelection)
        {
            return false;
        }

        Vector3? pivot = selection.GetSelectionPivotWorld(scene);
        if (!pivot.HasValue)
        {
            return false;
        }

        IsActive = true;
        Mode = mode;
        Constraint = GizmoAxisConstraint.None; // Starts in view-plane / uniform mode
        PivotWorld = pivot.Value;
        InitialRay = initialRay;
        LastRay = initialRay;
        CameraForward = cameraForward.LengthSquared() > Epsilon ? Vector3.Normalize(cameraForward) : Vector3.UnitZ;

        ClearNumericInput();
        ResetDeltas();

        Applicator.BeginSession(scene, selection, PivotWorld, undoStack, $"Modal {mode}");
        return true;
    }

    /// <summary>
    /// Toggles or sets an axis or plane constraint (X/Y/Z or Shift+X/Y/Z).
    /// If the requested constraint is already active, toggles back to <see cref="GizmoAxisConstraint.None"/>.
    /// </summary>
    public void ToggleConstraint(GizmoAxisConstraint targetConstraint, EditorScene? scene = null)
    {
        if (!IsActive)
        {
            return;
        }

        if (Constraint == targetConstraint)
        {
            Constraint = GizmoAxisConstraint.None;
        }
        else
        {
            Constraint = targetConstraint;
        }

        if (scene is not null)
        {
            if (HasNumericInput && ParsedNumericValue.HasValue)
            {
                ApplyNumericInput(scene);
            }
            else
            {
                UpdateRay(scene, LastRay);
            }
        }
    }

    /// <summary>
    /// Convenience helper for toggling axis from character key ('X', 'Y', 'Z') and shift flag.
    /// </summary>
    public void HandleAxisKey(char axisChar, bool shift, EditorScene? scene = null)
    {
        GizmoAxisConstraint target = char.ToUpperInvariant(axisChar) switch
        {
            'X' => shift ? GizmoAxisConstraint.YZ : GizmoAxisConstraint.X,
            'Y' => shift ? GizmoAxisConstraint.XZ : GizmoAxisConstraint.Y,
            'Z' => shift ? GizmoAxisConstraint.XY : GizmoAxisConstraint.Z,
            _ => GizmoAxisConstraint.None
        };

        if (target != GizmoAxisConstraint.None)
        {
            ToggleConstraint(target, scene);
        }
    }

    /// <summary>
    /// Appends or processes a character for the numeric entry buffer ('0'..'9', '.', '-').
    /// </summary>
    public bool HandleNumericKey(char key, EditorScene? scene = null)
    {
        if (!IsActive)
        {
            return false;
        }

        if (key == '-')
        {
            if (NumericInputBuffer.StartsWith('-'))
            {
                NumericInputBuffer = NumericInputBuffer[1..];
            }
            else
            {
                NumericInputBuffer = "-" + NumericInputBuffer;
            }

            if (scene is not null) ApplyNumericInput(scene);
            return true;
        }

        if (key == '.')
        {
            if (!NumericInputBuffer.Contains('.'))
            {
                NumericInputBuffer = string.IsNullOrEmpty(NumericInputBuffer) || NumericInputBuffer == "-"
                    ? NumericInputBuffer + "0."
                    : NumericInputBuffer + ".";
                if (scene is not null) ApplyNumericInput(scene);
            }
            return true;
        }

        if (char.IsDigit(key))
        {
            NumericInputBuffer += key;
            if (scene is not null) ApplyNumericInput(scene);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Deletes the last character in the numeric entry buffer.
    /// </summary>
    public bool HandleBackspace(EditorScene? scene = null)
    {
        if (!IsActive || string.IsNullOrEmpty(NumericInputBuffer))
        {
            return false;
        }

        NumericInputBuffer = NumericInputBuffer[..^1];
        if (NumericInputBuffer == "-")
        {
            NumericInputBuffer = string.Empty;
        }

        if (scene is not null)
        {
            if (HasNumericInput)
            {
                ApplyNumericInput(scene);
            }
            else
            {
                UpdateRay(scene, LastRay);
            }
        }

        return true;
    }

    /// <summary>
    /// Clears the numeric input buffer.
    /// </summary>
    public void ClearNumericInput()
    {
        NumericInputBuffer = string.Empty;
    }

    /// <summary>
    /// Updates the transform using the current mouse ray, applying grid or angle snapping if specified.
    /// Ignored if a valid numeric entry buffer is active.
    /// </summary>
    public void UpdateRay(
        EditorScene scene,
        Ray3D currentRay,
        bool snapGrid = false,
        float gridStep = 1.0f,
        bool snapAngle = false,
        float angleStepDegrees = 15.0f)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (!IsActive)
        {
            return;
        }

        LastRay = currentRay;

        // If numeric input is present, it takes precedence over mouse movement
        if (HasNumericInput && ParsedNumericValue.HasValue)
        {
            ApplyNumericInput(scene);
            return;
        }

        ResetDeltas();

        switch (Mode)
        {
            case ModalTransformMode.Move:
                CalculateMoveRayDelta(currentRay, snapGrid, gridStep);
                Applicator.ApplyTranslation(scene, CurrentTranslationDelta);
                break;

            case ModalTransformMode.Rotate:
                CalculateRotateRayDelta(currentRay, snapAngle, angleStepDegrees);
                Applicator.ApplyRotation(scene, CurrentRotationQuaternion, CurrentRotationDeltaDegrees);
                break;

            case ModalTransformMode.Scale:
                CalculateScaleRayDelta(currentRay, snapGrid, gridStep);
                Applicator.ApplyScale(scene, CurrentScaleMultiplier);
                break;
        }
    }

    /// <summary>
    /// Confirms the modal transform, committing the transaction to <paramref name="undoStack"/>.
    /// </summary>
    public void Confirm(EditorScene scene, UndoRedoStack? undoStack = null)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (!IsActive)
        {
            return;
        }

        Applicator.CommitSession(scene, undoStack);
        EndSessionInternal();
    }

    /// <summary>
    /// Cancels the modal transform, rolling back the scene to the pre-modal state.
    /// </summary>
    public void Cancel(EditorScene scene, UndoRedoStack? undoStack = null)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (!IsActive)
        {
            return;
        }

        Applicator.CancelSession(scene, undoStack);
        EndSessionInternal();
    }

    /// <summary>
    /// Returns a human-readable live status string describing the active modal operation.
    /// </summary>
    public string GetStatusReadout()
    {
        if (!IsActive)
        {
            return string.Empty;
        }

        string constraintStr = Constraint switch
        {
            GizmoAxisConstraint.X => " [X Axis]",
            GizmoAxisConstraint.Y => " [Y Axis]",
            GizmoAxisConstraint.Z => " [Z Axis]",
            GizmoAxisConstraint.YZ => " [YZ Plane (Shift+X)]",
            GizmoAxisConstraint.XZ => " [XZ Plane (Shift+Y)]",
            GizmoAxisConstraint.XY => " [XY Plane (Shift+Z)]",
            _ => " [View Plane]"
        };

        string valStr;
        if (HasNumericInput)
        {
            valStr = $"Typed: {NumericInputBuffer}";
        }
        else
        {
            valStr = Mode switch
            {
                ModalTransformMode.Move => $"Delta: ({CurrentTranslationDelta.X:+0.00;-0.00;0.00}, {CurrentTranslationDelta.Y:+0.00;-0.00;0.00}, {CurrentTranslationDelta.Z:+0.00;-0.00;0.00})",
                ModalTransformMode.Rotate => $"Angle: {CurrentAngleDegrees:+0.0;-0.0;0.0}°",
                ModalTransformMode.Scale => $"Factor: ({CurrentScaleMultiplier.X:0.00}, {CurrentScaleMultiplier.Y:0.00}, {CurrentScaleMultiplier.Z:0.00})",
                _ => string.Empty
            };
        }

        return $"{Mode}{constraintStr} | {valStr} | LMB/Enter: Confirm, RMB/Esc: Cancel";
    }

    private void ApplyNumericInput(EditorScene scene)
    {
        if (!ParsedNumericValue.HasValue)
        {
            return;
        }

        float val = ParsedNumericValue.Value;
        ResetDeltas();

        switch (Mode)
        {
            case ModalTransformMode.Move:
            {
                Vector3 delta = Constraint switch
                {
                    GizmoAxisConstraint.X => new Vector3(val, 0.0f, 0.0f),
                    GizmoAxisConstraint.Y => new Vector3(0.0f, val, 0.0f),
                    GizmoAxisConstraint.Z => new Vector3(0.0f, 0.0f, val),
                    GizmoAxisConstraint.YZ => new Vector3(0.0f, val, val),
                    GizmoAxisConstraint.XZ => new Vector3(val, 0.0f, val),
                    GizmoAxisConstraint.XY => new Vector3(val, val, 0.0f),
                    _ => new Vector3(val, 0.0f, 0.0f)
                };

                CurrentTranslationDelta = delta;
                Applicator.ApplyTranslation(scene, delta);
                break;
            }

            case ModalTransformMode.Rotate:
            {
                Vector3 axis = Constraint switch
                {
                    GizmoAxisConstraint.X or GizmoAxisConstraint.YZ => Vector3.UnitX,
                    GizmoAxisConstraint.Y or GizmoAxisConstraint.XZ => Vector3.UnitY,
                    GizmoAxisConstraint.Z or GizmoAxisConstraint.XY => Vector3.UnitZ,
                    _ => CameraForward
                };

                CurrentAngleDegrees = val;
                CurrentRotationDeltaDegrees = axis * val;
                CurrentRotationQuaternion = Quaternion.CreateFromAxisAngle(axis, val * DegToRad);
                Applicator.ApplyRotation(scene, CurrentRotationQuaternion, CurrentRotationDeltaDegrees);
                break;
            }

            case ModalTransformMode.Scale:
            {
                Vector3 s = Constraint switch
                {
                    GizmoAxisConstraint.X => new Vector3(val, 1.0f, 1.0f),
                    GizmoAxisConstraint.Y => new Vector3(1.0f, val, 1.0f),
                    GizmoAxisConstraint.Z => new Vector3(1.0f, 1.0f, val),
                    GizmoAxisConstraint.YZ => new Vector3(1.0f, val, val),
                    GizmoAxisConstraint.XZ => new Vector3(val, 1.0f, val),
                    GizmoAxisConstraint.XY => new Vector3(val, val, 1.0f),
                    _ => new Vector3(val, val, val)
                };

                CurrentScaleMultiplier = s;
                Applicator.ApplyScale(scene, s);
                break;
            }
        }
    }

    private void CalculateMoveRayDelta(Ray3D currentRay, bool snapGrid, float gridStep)
    {
        if (Constraint is GizmoAxisConstraint.X or GizmoAxisConstraint.Y or GizmoAxisConstraint.Z)
        {
            Vector3 axisDir = TransformGizmo3D.GetPrimaryAxisVector(Constraint);
            Plane3D dragPlane = TransformGizmo3D.BuildAxisDragPlane(PivotWorld, axisDir, InitialRay.Direction);

            if (!InitialRay.IntersectPlane(dragPlane, out _, out Vector3 startHit) ||
                !currentRay.IntersectPlane(dragPlane, out _, out Vector3 currHit))
            {
                return;
            }

            float startProj = Vector3.Dot(startHit - PivotWorld, axisDir);
            float currProj = Vector3.Dot(currHit - PivotWorld, axisDir);
            float deltaScalar = currProj - startProj;

            if (snapGrid)
            {
                deltaScalar = GridSnapper.SnapScalar(deltaScalar, gridStep);
            }

            CurrentTranslationDelta = axisDir * deltaScalar;
        }
        else if (Constraint is GizmoAxisConstraint.XY or GizmoAxisConstraint.XZ or GizmoAxisConstraint.YZ)
        {
            Vector3 planeNormal = TransformGizmo3D.GetPlaneNormalVector(Constraint);
            Plane3D dragPlane = Plane3D.CreateFromPointAndNormal(PivotWorld, planeNormal);

            if (!InitialRay.IntersectPlane(dragPlane, out _, out Vector3 startHit) ||
                !currentRay.IntersectPlane(dragPlane, out _, out Vector3 currHit))
            {
                return;
            }

            Vector3 delta = currHit - startHit;

            switch (Constraint)
            {
                case GizmoAxisConstraint.XY:
                    delta.Z = 0.0f;
                    if (snapGrid) delta = GridSnapper.SnapVector(delta, gridStep, snapX: true, snapY: true, snapZ: false);
                    break;
                case GizmoAxisConstraint.XZ:
                    delta.Y = 0.0f;
                    if (snapGrid) delta = GridSnapper.SnapVector(delta, gridStep, snapX: true, snapY: false, snapZ: true);
                    break;
                case GizmoAxisConstraint.YZ:
                    delta.X = 0.0f;
                    if (snapGrid) delta = GridSnapper.SnapVector(delta, gridStep, snapX: false, snapY: true, snapZ: true);
                    break;
            }

            CurrentTranslationDelta = delta;
        }
        else
        {
            // View-plane move
            Plane3D viewPlane = Plane3D.CreateFromPointAndNormal(PivotWorld, CameraForward);
            if (!InitialRay.IntersectPlane(viewPlane, out _, out Vector3 startHit) ||
                !currentRay.IntersectPlane(viewPlane, out _, out Vector3 currHit))
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

    private void CalculateRotateRayDelta(Ray3D currentRay, bool snapAngle, float angleStepDegrees)
    {
        Vector3 rotAxis = Constraint switch
        {
            GizmoAxisConstraint.X or GizmoAxisConstraint.YZ => Vector3.UnitX,
            GizmoAxisConstraint.Y or GizmoAxisConstraint.XZ => Vector3.UnitY,
            GizmoAxisConstraint.Z or GizmoAxisConstraint.XY => Vector3.UnitZ,
            _ => CameraForward
        };

        Plane3D rotPlane = Plane3D.CreateFromPointAndNormal(PivotWorld, rotAxis);

        if (!InitialRay.IntersectPlane(rotPlane, out _, out Vector3 startHit) ||
            !currentRay.IntersectPlane(rotPlane, out _, out Vector3 currHit))
        {
            return;
        }

        Vector3 vStart = startHit - PivotWorld;
        Vector3 vCurr = currHit - PivotWorld;

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

    private void CalculateScaleRayDelta(Ray3D currentRay, bool snapGrid, float gridStep)
    {
        if (Constraint is GizmoAxisConstraint.X or GizmoAxisConstraint.Y or GizmoAxisConstraint.Z)
        {
            Vector3 axisDir = TransformGizmo3D.GetPrimaryAxisVector(Constraint);
            Plane3D dragPlane = TransformGizmo3D.BuildAxisDragPlane(PivotWorld, axisDir, InitialRay.Direction);

            if (!InitialRay.IntersectPlane(dragPlane, out _, out Vector3 startHit) ||
                !currentRay.IntersectPlane(dragPlane, out _, out Vector3 currHit))
            {
                return;
            }

            float startProj = Vector3.Dot(startHit - PivotWorld, axisDir);
            float currProj = Vector3.Dot(currHit - PivotWorld, axisDir);
            float deltaScalar = currProj - startProj;

            if (snapGrid)
            {
                deltaScalar = GridSnapper.SnapScalar(deltaScalar, gridStep);
            }

            float factor = System.Math.Max(0.01f, 1.0f + deltaScalar);
            CurrentScaleMultiplier = Constraint switch
            {
                GizmoAxisConstraint.X => new Vector3(factor, 1.0f, 1.0f),
                GizmoAxisConstraint.Y => new Vector3(1.0f, factor, 1.0f),
                GizmoAxisConstraint.Z => new Vector3(1.0f, 1.0f, factor),
                _ => Vector3.One
            };
        }
        else if (Constraint is GizmoAxisConstraint.XY or GizmoAxisConstraint.XZ or GizmoAxisConstraint.YZ)
        {
            Vector3 planeNormal = TransformGizmo3D.GetPlaneNormalVector(Constraint);
            Plane3D dragPlane = Plane3D.CreateFromPointAndNormal(PivotWorld, planeNormal);

            if (!InitialRay.IntersectPlane(dragPlane, out _, out Vector3 startHit) ||
                !currentRay.IntersectPlane(dragPlane, out _, out Vector3 currHit))
            {
                return;
            }

            Vector3 delta = currHit - startHit;
            if (snapGrid)
            {
                delta = GridSnapper.SnapVector(delta, gridStep);
            }

            CurrentScaleMultiplier = Constraint switch
            {
                GizmoAxisConstraint.XY => new Vector3(System.Math.Max(0.01f, 1.0f + delta.X), System.Math.Max(0.01f, 1.0f + delta.Y), 1.0f),
                GizmoAxisConstraint.XZ => new Vector3(System.Math.Max(0.01f, 1.0f + delta.X), 1.0f, System.Math.Max(0.01f, 1.0f + delta.Z)),
                GizmoAxisConstraint.YZ => new Vector3(1.0f, System.Math.Max(0.01f, 1.0f + delta.Y), System.Math.Max(0.01f, 1.0f + delta.Z)),
                _ => Vector3.One
            };
        }
        else
        {
            // Uniform scale
            Plane3D viewPlane = Plane3D.CreateFromPointAndNormal(PivotWorld, CameraForward);
            if (!InitialRay.IntersectPlane(viewPlane, out _, out Vector3 startHit) ||
                !currentRay.IntersectPlane(viewPlane, out _, out Vector3 currHit))
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

            float factor = System.Math.Max(0.01f, 1.0f + uniformDelta);
            CurrentScaleMultiplier = new Vector3(factor, factor, factor);
        }
    }

    private void ResetDeltas()
    {
        CurrentTranslationDelta = Vector3.Zero;
        CurrentRotationDeltaDegrees = Vector3.Zero;
        CurrentAngleDegrees = 0.0f;
        CurrentRotationQuaternion = Quaternion.Identity;
        CurrentScaleMultiplier = Vector3.One;
    }

    private void EndSessionInternal()
    {
        IsActive = false;
        Mode = ModalTransformMode.None;
        Constraint = GizmoAxisConstraint.None;
        ClearNumericInput();
        ResetDeltas();
    }
}
