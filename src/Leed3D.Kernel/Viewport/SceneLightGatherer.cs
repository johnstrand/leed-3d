using System.Numerics;
using Leed3D.Kernel.Entities;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Scene;
using Leed3D.Runtime.Metadata;
using SysMath = System.Math;

namespace Leed3D.Kernel.Viewport;

/// <summary>
/// Headless utility that extracts and converts active scene <see cref="LevelEntity"/> light sources
/// to Right-Handed coordinates via <see cref="CoordSpace"/> for GLSL uniform uploads.
/// </summary>
public static class SceneLightGatherer
{
    public const int MaxLights = 16;

    /// <summary>
    /// Gathers all visible light entities in <paramref name="scene"/>, sorting by distance to <paramref name="cameraPos"/>
    /// if more than <paramref name="maxLights"/> lights exist.
    /// </summary>
    public static List<SceneLightData> GatherLights(EditorScene scene, Vector3? cameraPos = null, int maxLights = MaxLights)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var lights = new List<SceneLightData>();

        for (int i = 0; i < scene.Entities.Count; i++)
        {
            LevelEntity ent = scene.Entities[i];
            if (!ent.Visible || !ent.ClassName.Equals("light", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            lights.Add(ExtractLightData(ent));
        }

        if (lights.Count > maxLights)
        {
            if (cameraPos.HasValue)
            {
                Vector3 cPos = cameraPos.Value;
                lights.Sort((a, b) =>
                {
                    float distA = Vector3.DistanceSquared(a.PositionEditor, cPos);
                    float distB = Vector3.DistanceSquared(b.PositionEditor, cPos);
                    return distA.CompareTo(distB);
                });
            }

            lights = lights.Take(maxLights).ToList();
        }

        return lights;
    }

    /// <summary>
    /// Extracts light parameters from a <see cref="LevelEntity"/> and converts them to right-handed coordinates.
    /// </summary>
    public static SceneLightData ExtractLightData(LevelEntity ent)
    {
        ArgumentNullException.ThrowIfNull(ent);

        string lightTypeStr = "point";
        if (ent.Properties.TryGet("light_type", out var lt) && lt is not null && !string.IsNullOrWhiteSpace(lt.StringValue))
        {
            lightTypeStr = lt.StringValue.Trim();
        }
        LightType type = lightTypeStr.Equals("spot", StringComparison.OrdinalIgnoreCase) ? LightType.Spot : LightType.Point;

        ColorRgba color = new(255, 240, 210, 255);
        if (ent.Properties.TryGet("color", out var cVal) && cVal is not null && cVal.Type == MetadataValueType.Color)
        {
            color = cVal.ColorValue;
        }

        float intensity = 1.5f;
        if (ent.Properties.TryGet("intensity", out var intVal) && intVal is not null && intVal.Type == MetadataValueType.Float)
        {
            intensity = intVal.FloatValue;
        }

        float radius = 12.0f;
        if (ent.Properties.TryGet("radius", out var radVal) && radVal is not null && radVal.Type == MetadataValueType.Float)
        {
            radius = radVal.FloatValue;
        }

        float spotAngle = 45.0f;
        if (ent.Properties.TryGet("spot_angle", out var saVal) && saVal is not null && saVal.Type == MetadataValueType.Float)
        {
            spotAngle = saVal.FloatValue;
        }

        Vector3 posEditor = ent.Transform.Position;
        Vector3 dirEditor = ent.Transform.TransformDirectionToWorld(new Vector3(0.0f, 0.0f, 1.0f));
        if (dirEditor.LengthSquared() > 1e-6f)
        {
            dirEditor = Vector3.Normalize(dirEditor);
        }
        else
        {
            dirEditor = new Vector3(0.0f, 0.0f, 1.0f);
        }

        Vector3 posRh = CoordSpace.ToRightHanded(posEditor);
        Vector3 dirRh = CoordSpace.ToRightHanded(dirEditor);
        if (dirRh.LengthSquared() > 1e-6f)
        {
            dirRh = Vector3.Normalize(dirRh);
        }
        else
        {
            dirRh = -Vector3.UnitZ;
        }

        float halfAngleRad = (spotAngle * 0.5f) * (MathF.PI / 180.0f);
        float spotCosCutoff = MathF.Cos(halfAngleRad);

        return new SceneLightData
        {
            EntityId = ent.Id,
            Type = type,
            PositionEditor = posEditor,
            DirectionEditor = dirEditor,
            PositionRightHanded = posRh,
            DirectionRightHanded = dirRh,
            Color = color,
            Intensity = intensity,
            Radius = MathF.Max(0.01f, radius),
            SpotAngleDegrees = spotAngle,
            SpotCosCutoff = spotCosCutoff
        };
    }

    /// <summary>
    /// Computes the smooth windowed distance attenuation:
    /// atten = clamp(1.0 - pow(dist / radius, 2.0), 0.0, 1.0)^2.
    /// </summary>
    public static float ComputeAttenuation(float distance, float radius)
    {
        if (distance >= radius || radius <= 0.0001f || distance < 0.0f)
        {
            return 0.0f;
        }

        float dOverR = distance / radius;
        float window = SysMath.Clamp(1.0f - (dOverR * dOverR), 0.0f, 1.0f);
        return window * window;
    }

    /// <summary>
    /// Computes the spotlight angular cone factor.
    /// </summary>
    public static float ComputeSpotFactor(Vector3 lightToFragDir, Vector3 spotDir, float spotCosCutoff)
    {
        float cosTheta = Vector3.Dot(lightToFragDir, spotDir);
        if (cosTheta <= spotCosCutoff)
        {
            return 0.0f;
        }

        float innerCos = MathF.FusedMultiplyAdd(1.0f - spotCosCutoff, 0.15f, spotCosCutoff);
        return SysMath.Clamp((cosTheta - spotCosCutoff) / MathF.Max(innerCos - spotCosCutoff, 0.0001f), 0.0f, 1.0f);
    }

    /// <summary>
    /// CPU reference evaluation of the multi-light and fog lighting equation for a fragment point.
    /// </summary>
    public static Vector3 EvaluateLighting(
        Vector3 fragPosRh,
        Vector3 normalRh,
        Vector3 viewPosRh,
        Vector3 albedo,
        IReadOnlyList<SceneLightData> lights,
        EnvironmentLightingSettings env)
    {
        ArgumentNullException.ThrowIfNull(lights);
        ArgumentNullException.ThrowIfNull(env);

        Vector3 N = Vector3.Normalize(normalRh);
        Vector3 V = Vector3.Normalize(viewPosRh - fragPosRh);

        Vector3 ambient = env.AmbientColorNormalized * env.AmbientIntensity;
        Vector3 accumLight = Vector3.Zero;

        int activeCount = SysMath.Min(lights.Count, MaxLights);
        for (int i = 0; i < activeCount; i++)
        {
            SceneLightData light = lights[i];
            Vector3 lightDelta = light.PositionRightHanded - fragPosRh;
            float dist = lightDelta.Length();

            if (dist >= light.Radius || dist < 0.0001f)
            {
                continue;
            }

            Vector3 L = lightDelta / dist;
            float atten = ComputeAttenuation(dist, light.Radius);

            float spotFactor = 1.0f;
            if (light.Type == LightType.Spot)
            {
                spotFactor = ComputeSpotFactor(-L, light.DirectionRightHanded, light.SpotCosCutoff);
            }

            if (spotFactor > 0.0f)
            {
                float nDotL = MathF.Max(0.0f, Vector3.Dot(N, L));
                if (nDotL > 0.0f)
                {
                    Vector3 H = Vector3.Normalize(L + V);
                    float nDotH = MathF.Max(0.0f, Vector3.Dot(N, H));
                    float spec = MathF.Pow(nDotH, 32.0f) * 0.25f;

                    Vector3 lightColor = new(light.Color.R / 255.0f, light.Color.G / 255.0f, light.Color.B / 255.0f);
                    accumLight += lightColor * light.Intensity * atten * spotFactor * (nDotL + spec);
                }
            }
        }

        Vector3 litColor = albedo * (ambient + accumLight);

        if (env.FogEnabled)
        {
            float fragDist = Vector3.Distance(viewPosRh, fragPosRh);
            float fogFactor = SysMath.Clamp(1.0f - MathF.Exp(-fragDist * env.FogDensity), 0.0f, 1.0f);
            litColor = Vector3.Lerp(litColor, env.FogColorNormalized, fogFactor);
        }

        return litColor;
    }
}
