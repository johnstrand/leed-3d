using System.Numerics;
using Leed3D.Kernel.Math;

namespace Leed3D.Kernel.Viewport;

public enum ViewportProjectionMode
{
    Perspective,
    OrthoX,
    OrthoY,
    OrthoZ
}

public enum ViewportShadingMode
{
    SolidWithWireframe,
    Solid,
    Wireframe,
    LitPreview
}

/// <summary>
/// 100% headless 3D editor camera operating in Left-Handed Editor Space (+X Right, +Y Up, +Z Away).
/// Supports Perspective free-fly / orbit / pan / zoom and axis-aligned Orthographic X (YZ plane),
/// Y (XZ ground plane), and Z (XY plane) views, plus screen-to-ray and world-to-screen projections.
/// </summary>
public sealed class EditorCamera
{
    private const float DegToRad = MathF.PI / 180.0f;
    private const float MinPitchDegrees = -89.0f;
    private const float MaxPitchDegrees = 89.0f;
    private const float MinOrthoSize = 0.5f;
    private const float MaxOrthoSize = 500.0f;
    private const float MinOrbitDistance = 0.25f;
    private const float MaxOrbitDistance = 1000.0f;

    public ViewportProjectionMode ProjectionMode { get; private set; } = ViewportProjectionMode.Perspective;

    public Vector3 Position { get; set; } = new(-6.0f, 5.0f, -8.0f);
    public Vector3 Target { get; set; } = Vector3.Zero;

    public float YawDegrees { get; set; } = 36.87f;
    public float PitchDegrees { get; set; } = -26.57f;
    public float OrbitDistance { get; set; } = 11.18f;

    /// <summary>
    /// Vertical field-of-view in degrees for Perspective projection.
    /// </summary>
    public float FovYDegrees { get; set; } = 60.0f;

    /// <summary>
    /// Vertical world-space height visible in Orthographic modes (clamped to 0.5..500).
    /// </summary>
    public float OrthoSize { get; set; } = 16.0f;

    public float NearPlane { get; set; } = 0.05f;
    public float FarPlane { get; set; } = 2000.0f;

    public float FlySpeed { get; set; } = 10.0f;
    public float FastFlyMultiplier { get; set; } = 3.0f;
    public float MouseLookSensitivity { get; set; } = 0.2f;

    /// <summary>
    /// When in OrthoX/Y/Z, determines whether looking along the positive (+1) or negative (-1) axis direction.
    /// </summary>
    public bool InvertOrthoAxis { get; set; } = false;

    public EditorCamera()
    {
        SyncTargetFromAngles();
    }

    /// <summary>
    /// Returns the unit forward direction vector in Left-Handed Editor Space (+X Right, +Y Up, +Z Away).
    /// At Yaw = 0, Pitch = 0: Forward = (0, 0, +1), Right = (+1, 0, 0), Up = (0, +1, 0).
    /// </summary>
    public Vector3 GetForward()
    {
        switch (ProjectionMode)
        {
            case ViewportProjectionMode.OrthoX:
                // Looking from +X toward -X (Right view) by default, or from -X toward +X when inverted
                return InvertOrthoAxis ? Vector3.UnitX : -Vector3.UnitX;

            case ViewportProjectionMode.OrthoY:
                // Looking from +Y down toward -Y (Top view) by default, or from -Y up toward +Y when inverted
                return InvertOrthoAxis ? Vector3.UnitY : -Vector3.UnitY;

            case ViewportProjectionMode.OrthoZ:
                // Looking from -Z into +Z (Front view) by default, or from +Z toward -Z when inverted
                return InvertOrthoAxis ? -Vector3.UnitZ : Vector3.UnitZ;

            case ViewportProjectionMode.Perspective:
            default:
            {
                float yaw = YawDegrees * DegToRad;
                float pitch = System.Math.Clamp(PitchDegrees, MinPitchDegrees, MaxPitchDegrees) * DegToRad;

                float cosPitch = MathF.Cos(pitch);
                float x = MathF.Sin(yaw) * cosPitch;
                float y = MathF.Sin(pitch);
                float z = MathF.Cos(yaw) * cosPitch;
                return Vector3.Normalize(new Vector3(x, y, z));
            }
        }
    }

    /// <summary>
    /// Returns the unit right vector in Left-Handed Editor Space (+X Right, +Y Up, +Z Away).
    /// </summary>
    public Vector3 GetRight()
    {
        switch (ProjectionMode)
        {
            case ViewportProjectionMode.OrthoX:
                // Looking along -X: +Z (away) is to the right on screen (YZ plane)
                return InvertOrthoAxis ? -Vector3.UnitZ : Vector3.UnitZ;

            case ViewportProjectionMode.OrthoY:
                // Looking down along -Y: +X is to the right on screen (XZ plane)
                return Vector3.UnitX;

            case ViewportProjectionMode.OrthoZ:
                // Looking forward along +Z: +X is to the right on screen (XY plane)
                return InvertOrthoAxis ? -Vector3.UnitX : Vector3.UnitX;

            case ViewportProjectionMode.Perspective:
            default:
            {
                float yaw = YawDegrees * DegToRad;
                return Vector3.Normalize(new Vector3(MathF.Cos(yaw), 0.0f, -MathF.Sin(yaw)));
            }
        }
    }

    /// <summary>
    /// Returns the unit camera-up vector in Left-Handed Editor Space (+X Right, +Y Up, +Z Away).
    /// </summary>
    public Vector3 GetUp()
    {
        switch (ProjectionMode)
        {
            case ViewportProjectionMode.OrthoX:
                return Vector3.UnitY;

            case ViewportProjectionMode.OrthoY:
                // Looking down along -Y: +Z (away) is up on screen
                return InvertOrthoAxis ? -Vector3.UnitZ : Vector3.UnitZ;

            case ViewportProjectionMode.OrthoZ:
                return Vector3.UnitY;

            case ViewportProjectionMode.Perspective:
            default:
            {
                Vector3 forward = GetForward();
                Vector3 right = GetRight();
                // Left-handed cross product: Forward x Right = Up (since Z x X = Y in standard vector cross product)
                return Vector3.Normalize(Vector3.Cross(forward, right));
            }
        }
    }

    /// <summary>
    /// Switches camera projection mode while preserving the focus target.
    /// </summary>
    public void SetProjectionMode(ViewportProjectionMode mode, bool invertAxis = false)
    {
        ProjectionMode = mode;
        InvertOrthoAxis = invertAxis;

        if (mode == ViewportProjectionMode.Perspective)
        {
            Position = Target - (GetForward() * OrbitDistance);
        }
        else
        {
            // Place ortho camera well back from Target along its view axis so near clipping doesn't cut scene geometry
            float orthoBackoff = System.Math.Max(200.0f, OrthoSize * 2.0f);
            Position = Target - (GetForward() * orthoBackoff);
        }
    }

    /// <summary>
    /// Toggles between Perspective and the last/closest Orthographic mode (defaulting to OrthoY Top view).
    /// </summary>
    public void TogglePerspectiveOrtho()
    {
        if (ProjectionMode == ViewportProjectionMode.Perspective)
        {
            SetProjectionMode(ViewportProjectionMode.OrthoY);
        }
        else
        {
            SetProjectionMode(ViewportProjectionMode.Perspective);
        }
    }

    /// <summary>
    /// Updates camera orientation and position during Perspective free-fly mode (Right Mouse Button held).
    /// </summary>
    /// <param name="moveInput">X = Right/Left (D/A), Y = Up/Down (E/Q), Z = Forward/Backward (W/S)</param>
    /// <param name="mouseDelta">Screen-space mouse delta in pixels</param>
    /// <param name="dt">Frame delta time in seconds</param>
    /// <param name="fastFly">Whether Shift speed multiplier is active</param>
    public void UpdateFreeFly(Vector3 moveInput, Vector2 mouseDelta, float dt, bool fastFly = false)
    {
        if (ProjectionMode != ViewportProjectionMode.Perspective)
        {
            SetProjectionMode(ViewportProjectionMode.Perspective);
        }

        YawDegrees += mouseDelta.X * MouseLookSensitivity;
        PitchDegrees = System.Math.Clamp(PitchDegrees - (mouseDelta.Y * MouseLookSensitivity), MinPitchDegrees, MaxPitchDegrees);

        Vector3 forward = GetForward();
        Vector3 right = GetRight();
        Vector3 worldUp = Vector3.UnitY;

        float speed = FlySpeed * (fastFly ? FastFlyMultiplier : 1.0f);
        Vector3 velocity = ((right * moveInput.X) + (worldUp * moveInput.Y) + (forward * moveInput.Z)) * (speed * dt);

        Position += velocity;
        Target = Position + (forward * OrbitDistance);
    }

    /// <summary>
    /// Orbits the camera around <see cref="Target"/> in Perspective mode.
    /// </summary>
    public void Orbit(Vector2 mouseDelta)
    {
        if (ProjectionMode != ViewportProjectionMode.Perspective)
        {
            SetProjectionMode(ViewportProjectionMode.Perspective);
        }

        YawDegrees += mouseDelta.X * MouseLookSensitivity * 1.5f;
        PitchDegrees = System.Math.Clamp(PitchDegrees - (mouseDelta.Y * MouseLookSensitivity * 1.5f), MinPitchDegrees, MaxPitchDegrees);

        Position = Target - (GetForward() * OrbitDistance);
    }

    /// <summary>
    /// Pans the camera parallel to the current view plane.
    /// </summary>
    public void Pan(Vector2 mouseDelta, Vector2 viewportSize)
    {
        float height = System.Math.Max(1.0f, viewportSize.Y);
        float unitsPerPixel;

        if (ProjectionMode == ViewportProjectionMode.Perspective)
        {
            float halfFovRad = (FovYDegrees * 0.5f) * DegToRad;
            float viewHeightAtTarget = 2.0f * OrbitDistance * MathF.Tan(halfFovRad);
            unitsPerPixel = viewHeightAtTarget / height;
        }
        else
        {
            unitsPerPixel = OrthoSize / height;
        }

        Vector3 right = GetRight();
        Vector3 up = GetUp();

        // Dragging mouse right/down moves the camera left/up so the world tracks the cursor
        Vector3 deltaWorld = (-right * (mouseDelta.X * unitsPerPixel)) + (up * (mouseDelta.Y * unitsPerPixel));
        Position += deltaWorld;
        Target += deltaWorld;
    }

    /// <summary>
    /// Zooms the camera via mouse scroll wheel delta.
    /// Dollies in Perspective mode or scales <see cref="OrthoSize"/> in Orthographic modes.
    /// </summary>
    public void Zoom(float scrollWheelDelta)
    {
        if (MathF.Abs(scrollWheelDelta) < 1e-5f)
        {
            return;
        }

        float zoomFactor = MathF.Pow(0.88f, scrollWheelDelta);

        if (ProjectionMode == ViewportProjectionMode.Perspective)
        {
            OrbitDistance = System.Math.Clamp(OrbitDistance * zoomFactor, MinOrbitDistance, MaxOrbitDistance);
            Position = Target - (GetForward() * OrbitDistance);
        }
        else
        {
            OrthoSize = System.Math.Clamp(OrthoSize * zoomFactor, MinOrthoSize, MaxOrthoSize);
        }
    }

    /// <summary>
    /// Frames the camera onto a target bounding box or world point.
    /// </summary>
    public void FrameTarget(Vector3 targetPoint, float radius = 4.0f)
    {
        Target = targetPoint;
        float clampedRadius = System.Math.Max(1.0f, radius);
        OrbitDistance = System.Math.Clamp(clampedRadius * 2.5f, MinOrbitDistance, MaxOrbitDistance);
        OrthoSize = System.Math.Clamp(clampedRadius * 2.2f, MinOrthoSize, MaxOrthoSize);

        if (ProjectionMode == ViewportProjectionMode.Perspective)
        {
            Position = Target - (GetForward() * OrbitDistance);
        }
        else
        {
            float orthoBackoff = System.Math.Max(200.0f, OrthoSize * 2.0f);
            Position = Target - (GetForward() * orthoBackoff);
        }
    }

    /// <summary>
    /// Converts a 2D viewport-local mouse coordinate (0..width, 0..height, top-left origin)
    /// into a 3D picking ray in Left-Handed Editor Space (+X Right, +Y Up, +Z Away).
    /// </summary>
    public Ray3D ScreenPointToRay(Vector2 viewportLocalMousePos, Vector2 viewportSize)
    {
        float width = System.Math.Max(1.0f, viewportSize.X);
        float height = System.Math.Max(1.0f, viewportSize.Y);
        float aspect = width / height;

        // Normalized Device Coordinates: ndcX in [-1, +1] (left to right), ndcY in [-1, +1] (bottom to top)
        float ndcX = ((2.0f * viewportLocalMousePos.X) / width) - 1.0f;
        float ndcY = 1.0f - ((2.0f * viewportLocalMousePos.Y) / height);

        Vector3 forward = GetForward();
        Vector3 right = GetRight();
        Vector3 up = GetUp();

        if (ProjectionMode == ViewportProjectionMode.Perspective)
        {
            float tanHalfFov = MathF.Tan((FovYDegrees * 0.5f) * DegToRad);
            Vector3 rayDir = forward + (right * (ndcX * aspect * tanHalfFov)) + (up * (ndcY * tanHalfFov));
            return new Ray3D(Position, Vector3.Normalize(rayDir));
        }
        else
        {
            float halfHeight = OrthoSize * 0.5f;
            float halfWidth = halfHeight * aspect;
            Vector3 rayOrigin = Position + (right * (ndcX * halfWidth)) + (up * (ndcY * halfHeight));
            return new Ray3D(rayOrigin, forward);
        }
    }

    /// <summary>
    /// Projects a 3D world-space point (in Left-Handed Editor Space) onto 2D viewport pixel coordinates
    /// (0..width, 0..height, top-left origin). Returns null if the point is behind the camera near plane.
    /// </summary>
    public Vector2? WorldToScreenPoint(Vector3 worldPoint, Vector2 viewportSize)
    {
        float width = System.Math.Max(1.0f, viewportSize.X);
        float height = System.Math.Max(1.0f, viewportSize.Y);
        float aspect = width / height;

        Vector3 forward = GetForward();
        Vector3 right = GetRight();
        Vector3 up = GetUp();

        Vector3 toPoint = worldPoint - Position;
        float depth = Vector3.Dot(toPoint, forward);

        if (ProjectionMode == ViewportProjectionMode.Perspective)
        {
            if (depth <= NearPlane)
            {
                return null;
            }

            float tanHalfFov = MathF.Tan((FovYDegrees * 0.5f) * DegToRad);
            float localX = Vector3.Dot(toPoint, right);
            float localY = Vector3.Dot(toPoint, up);

            float ndcX = localX / (depth * aspect * tanHalfFov);
            float ndcY = localY / (depth * tanHalfFov);

            float screenX = (ndcX + 1.0f) * 0.5f * width;
            float screenY = (1.0f - ndcY) * 0.5f * height;
            return new Vector2(screenX, screenY);
        }
        else
        {
            float halfHeight = System.Math.Max(1e-4f, OrthoSize * 0.5f);
            float halfWidth = halfHeight * aspect;

            float localX = Vector3.Dot(toPoint, right);
            float localY = Vector3.Dot(toPoint, up);

            float ndcX = localX / halfWidth;
            float ndcY = localY / halfHeight;

            float screenX = (ndcX + 1.0f) * 0.5f * width;
            float screenY = (1.0f - ndcY) * 0.5f * height;
            return new Vector2(screenX, screenY);
        }
    }

    private void SyncTargetFromAngles()
    {
        Target = Position + (GetForward() * OrbitDistance);
    }
}
