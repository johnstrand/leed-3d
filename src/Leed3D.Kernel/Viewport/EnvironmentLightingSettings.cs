using System.Numerics;
using Leed3D.Runtime.Metadata;

namespace Leed3D.Kernel.Viewport;

/// <summary>
/// Scene-wide environment lighting and distance fog settings for the real-time lit preview shader.
/// </summary>
public sealed class EnvironmentLightingSettings
{
    public static readonly ColorRgba DefaultAmbientColor = new(40, 45, 60, 255);
    public const float DefaultAmbientIntensity = 0.25f;

    public const bool DefaultFogEnabled = true;
    public static readonly ColorRgba DefaultFogColor = new(20, 22, 30, 255);
    public const float DefaultFogDensity = 0.015f;

    public ColorRgba AmbientColor { get; set; } = DefaultAmbientColor;
    public float AmbientIntensity { get; set; } = DefaultAmbientIntensity;

    public bool FogEnabled { get; set; } = DefaultFogEnabled;
    public ColorRgba FogColor { get; set; } = DefaultFogColor;
    public float FogDensity { get; set; } = DefaultFogDensity;

    public Vector3 AmbientColorNormalized => new(AmbientColor.R / 255.0f, AmbientColor.G / 255.0f, AmbientColor.B / 255.0f);
    public Vector3 FogColorNormalized => new(FogColor.R / 255.0f, FogColor.G / 255.0f, FogColor.B / 255.0f);

    public void ResetDefaults()
    {
        AmbientColor = DefaultAmbientColor;
        AmbientIntensity = DefaultAmbientIntensity;
        FogEnabled = DefaultFogEnabled;
        FogColor = DefaultFogColor;
        FogDensity = DefaultFogDensity;
    }

    public EnvironmentLightingSettings Clone()
    {
        return new EnvironmentLightingSettings
        {
            AmbientColor = AmbientColor,
            AmbientIntensity = AmbientIntensity,
            FogEnabled = FogEnabled,
            FogColor = FogColor,
            FogDensity = FogDensity
        };
    }
}
