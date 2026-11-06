using System.Numerics;
using Leed3D.Runtime.Metadata;

namespace Leed3D.Kernel.Viewport;

public enum LightType
{
    Point = 0,
    Spot = 1
}

/// <summary>
/// Extracted runtime lighting parameters for a scene light entity,
/// prepared for uniform upload to the GLSL multi-light shader.
/// </summary>
public sealed class SceneLightData
{
    public string EntityId { get; set; } = string.Empty;
    public LightType Type { get; set; } = LightType.Point;

    /// <summary>Position in Left-Handed Editor space (+X Right, +Y Up, +Z Away).</summary>
    public Vector3 PositionEditor { get; set; }

    /// <summary>Normalized forward direction in Left-Handed Editor space.</summary>
    public Vector3 DirectionEditor { get; set; } = Vector3.UnitZ;

    /// <summary>Position in Right-Handed Raylib/OpenGL space (+X Right, +Y Up, -Z Forward).</summary>
    public Vector3 PositionRightHanded { get; set; }

    /// <summary>Normalized forward direction in Right-Handed Raylib/OpenGL space.</summary>
    public Vector3 DirectionRightHanded { get; set; } = -Vector3.UnitZ;

    public ColorRgba Color { get; set; } = new(255, 240, 210, 255);
    public Vector4 ColorNormalized => new(Color.R / 255.0f, Color.G / 255.0f, Color.B / 255.0f, Color.A / 255.0f);

    public float Intensity { get; set; } = 1.5f;
    public float Radius { get; set; } = 12.0f;
    public float SpotAngleDegrees { get; set; } = 45.0f;

    /// <summary>Cosine of the spot cone half-angle (e.g. cos(22.5 deg) for 45 deg spot).</summary>
    public float SpotCosCutoff { get; set; } = 0.9238795f;
}
