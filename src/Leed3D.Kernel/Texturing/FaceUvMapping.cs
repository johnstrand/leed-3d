using System.Numerics;

namespace Leed3D.Kernel.Texturing;

/// <summary>
/// Stores Valve-220 / TrenchBroom-style planar UV projection parameters for a <see cref="Geometry.PolygonFace"/>.
/// Supports world-space box projection, face-plane alignment, per-face offset/scale/rotation, and Texture Lock invariance.
/// </summary>
public sealed class FaceUvMapping
{
    /// <summary>
    /// 3D projection axis for the horizontal texture coordinate (U).
    /// </summary>
    public Vector3 UAxis { get; set; } = Vector3.UnitX;

    /// <summary>
    /// 3D projection axis for the vertical texture coordinate (V).
    /// </summary>
    public Vector3 VAxis { get; set; } = Vector3.UnitZ;

    /// <summary>
    /// UV translation offset in texture repeats.
    /// </summary>
    public Vector2 Offset { get; set; } = Vector2.Zero;

    /// <summary>
    /// World units per texture repeat along U and V (e.g. <c>(1, 1)</c> = 1 tile per world unit/meter).
    /// </summary>
    public Vector2 Scale { get; set; } = Vector2.One;

    /// <summary>
    /// In-plane texture rotation in degrees around <c>Cross(UAxis, VAxis)</c>.
    /// </summary>
    public float RotationDegrees { get; set; } = 0.0f;

    /// <summary>
    /// When <c>true</c>, transforming the face or parent object compensates <see cref="UAxis"/>, <see cref="VAxis"/>,
    /// and <see cref="Offset"/> so the texture stays pinned to the geometry instead of sliding in world space.
    /// </summary>
    public bool TextureLock { get; set; } = false;

    public FaceUvMapping()
    {
    }

    public FaceUvMapping(
        Vector3 uAxis,
        Vector3 vAxis,
        Vector2? offset = null,
        Vector2? scale = null,
        float rotationDegrees = 0.0f,
        bool textureLock = false)
    {
        UAxis = uAxis;
        VAxis = vAxis;
        Offset = offset ?? Vector2.Zero;
        Scale = scale ?? Vector2.One;
        RotationDegrees = rotationDegrees;
        TextureLock = textureLock;
    }

    /// <summary>
    /// Creates a deep copy of this <see cref="FaceUvMapping"/>.
    /// </summary>
    public FaceUvMapping Clone()
    {
        return new FaceUvMapping(UAxis, VAxis, Offset, Scale, RotationDegrees, TextureLock);
    }
}
