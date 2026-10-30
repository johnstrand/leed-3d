using System.Numerics;
using Leed3D.Kernel.Entities;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Viewport;
using Leed3D.Runtime.Metadata;

namespace Leed3D.Tests.Viewport;

public class SceneLightGathererTests
{
    [Fact]
    public void ExtractLightData_PointLight_Defaults()
    {
        var ent = new LevelEntity("light_1", "light", new Transform3D(new Vector3(10.0f, 20.0f, 30.0f)));

        SceneLightData light = SceneLightGatherer.ExtractLightData(ent);

        Assert.Equal(LightType.Point, light.Type);
        Assert.Equal(new Vector3(10.0f, 20.0f, 30.0f), light.PositionEditor);
        Assert.Equal(new Vector3(10.0f, 20.0f, -30.0f), light.PositionRightHanded);
        Assert.Equal(new ColorRgba(255, 240, 210, 255), light.Color);
        Assert.Equal(1.5f, light.Intensity);
        Assert.Equal(12.0f, light.Radius);
        Assert.True(light.SpotCosCutoff > 0.0f);
    }

    [Fact]
    public void ExtractLightData_PointLight_CustomProperties()
    {
        var ent = new LevelEntity("light_2", "light", new Transform3D(new Vector3(-4.0f, 6.0f, 8.0f)));
        ent.Properties.SetProperty("light_type", MetadataValue.FromString("point"));
        ent.Properties.SetProperty("color", MetadataValue.FromColor(new ColorRgba(100, 150, 220, 255)));
        ent.Properties.SetProperty("intensity", MetadataValue.FromFloat(3.25f));
        ent.Properties.SetProperty("radius", MetadataValue.FromFloat(18.5f));

        SceneLightData light = SceneLightGatherer.ExtractLightData(ent);

        Assert.Equal(LightType.Point, light.Type);
        Assert.Equal(new Vector3(-4.0f, 6.0f, 8.0f), light.PositionEditor);
        Assert.Equal(new Vector3(-4.0f, 6.0f, -8.0f), light.PositionRightHanded);
        Assert.Equal(new ColorRgba(100, 150, 220, 255), light.Color);
        Assert.Equal(3.25f, light.Intensity);
        Assert.Equal(18.5f, light.Radius);
    }

    [Fact]
    public void ExtractLightData_SpotLight_DirectionAndCutoff()
    {
        // Pitch 90 degrees around X to aim down (-Y)
        var ent = new LevelEntity("spot_1", "light", new Transform3D(new Vector3(0.0f, 5.0f, 0.0f), new Vector3(90.0f, 0.0f, 0.0f)));
        ent.Properties.SetProperty("light_type", MetadataValue.FromString("spot"));
        ent.Properties.SetProperty("spot_angle", MetadataValue.FromFloat(60.0f));

        SceneLightData light = SceneLightGatherer.ExtractLightData(ent);

        Assert.Equal(LightType.Spot, light.Type);
        Assert.Equal(60.0f, light.SpotAngleDegrees);

        // Half angle is 30 deg; cos(30) = sqrt(3)/2 ~ 0.866025
        float expectedCos = MathF.Cos(30.0f * (MathF.PI / 180.0f));
        Assert.InRange(light.SpotCosCutoff, expectedCos - 0.001f, expectedCos + 0.001f);

        // In LH editor space, forward (0,0,1) with Pitch +90 aims along -Y
        Assert.InRange(light.DirectionEditor.X, -0.01f, 0.01f);
        Assert.InRange(light.DirectionEditor.Y, -1.01f, -0.99f);
        Assert.InRange(light.DirectionEditor.Z, -0.01f, 0.01f);

        // In RH Raylib space, -Y remains -Y
        Assert.InRange(light.DirectionRightHanded.X, -0.01f, 0.01f);
        Assert.InRange(light.DirectionRightHanded.Y, -1.01f, -0.99f);
        Assert.InRange(light.DirectionRightHanded.Z, -0.01f, 0.01f);
    }

    [Fact]
    public void GatherLights_FiltersNonLightsAndInvisible()
    {
        var scene = new EditorScene();

        var playerSpawn = new LevelEntity("spawn_1", "player_spawn", new Transform3D(new Vector3(0, 0, 0)));
        var lightVisible = new LevelEntity("light_1", "light", new Transform3D(new Vector3(2, 0, 0)));
        var lightInvisible = new LevelEntity("light_2", "light", new Transform3D(new Vector3(4, 0, 0))) { Visible = false };
        var trigger = new LevelEntity("trigger_1", "trigger", new Transform3D(new Vector3(6, 0, 0)));

        scene.AddEntity(playerSpawn);
        scene.AddEntity(lightVisible);
        scene.AddEntity(lightInvisible);
        scene.AddEntity(trigger);

        List<SceneLightData> gathered = SceneLightGatherer.GatherLights(scene);

        Assert.Single(gathered);
        Assert.Equal(lightVisible.Id, gathered[0].EntityId);
    }

    [Fact]
    public void GatherLights_SortsByCameraDistance_WhenExceedingMaxLights()
    {
        var scene = new EditorScene();
        Vector3 cameraPos = Vector3.Zero;

        // Add 25 lights at varying distances from 1.0 to 25.0
        for (int i = 25; i >= 1; i--)
        {
            var light = new LevelEntity($"light_{i}", "light", new Transform3D(new Vector3(0.0f, 0.0f, (float)i)));
            scene.AddEntity(light);
        }

        List<SceneLightData> gathered = SceneLightGatherer.GatherLights(scene, cameraPos, SceneLightGatherer.MaxLights);

        Assert.Equal(SceneLightGatherer.MaxLights, gathered.Count);
        // The closest 16 lights should be gathered, sorted in ascending distance order
        for (int i = 0; i < gathered.Count; i++)
        {
            float expectedZ = (float)(i + 1);
            Assert.Equal(expectedZ, gathered[i].PositionEditor.Z);
        }
    }

    [Fact]
    public void ComputeAttenuation_WindowedQuadraticFalloff()
    {
        float radius = 10.0f;

        // At 0 distance: attenuation is 1.0
        Assert.Equal(1.0f, SceneLightGatherer.ComputeAttenuation(0.0f, radius));

        // At half distance (5.0 / 10.0): window = 1 - 0.25 = 0.75; atten = 0.75^2 = 0.5625
        float halfAtten = SceneLightGatherer.ComputeAttenuation(5.0f, radius);
        Assert.InRange(halfAtten, 0.5624f, 0.5626f);

        // At exact radius: attenuation is 0.0
        Assert.Equal(0.0f, SceneLightGatherer.ComputeAttenuation(radius, radius));

        // Beyond radius: attenuation remains 0.0
        Assert.Equal(0.0f, SceneLightGatherer.ComputeAttenuation(radius + 2.0f, radius));

        // Monotonic decay
        float prev = 1.0f;
        for (float d = 1.0f; d <= radius; d += 1.0f)
        {
            float cur = SceneLightGatherer.ComputeAttenuation(d, radius);
            Assert.True(cur <= prev);
            prev = cur;
        }
    }

    [Fact]
    public void ComputeSpotFactor_ConeAndPenumbra()
    {
        // 60-degree cone -> half angle 30 deg -> cos = 0.866025
        float cutoff = MathF.Cos(30.0f * (MathF.PI / 180.0f));
        Vector3 spotDir = new(0.0f, -1.0f, 0.0f);

        // Perfectly aligned with spotDir -> angle = 0 -> factor = 1.0
        Vector3 fragDirCenter = new(0.0f, -1.0f, 0.0f);
        Assert.Equal(1.0f, SceneLightGatherer.ComputeSpotFactor(fragDirCenter, spotDir, cutoff));

        // Outside cone -> angle 45 deg (cos = ~0.707 < 0.866) -> factor = 0.0
        Vector3 fragDirOutside = Vector3.Normalize(new Vector3(1.0f, -1.0f, 0.0f));
        Assert.Equal(0.0f, SceneLightGatherer.ComputeSpotFactor(fragDirOutside, spotDir, cutoff));

        // Behind light -> factor = 0.0
        Vector3 fragDirBehind = new(0.0f, 1.0f, 0.0f);
        Assert.Equal(0.0f, SceneLightGatherer.ComputeSpotFactor(fragDirBehind, spotDir, cutoff));
    }

    [Fact]
    public void EvaluateLighting_NoLights_ReturnsAmbientOnly()
    {
        var env = new EnvironmentLightingSettings
        {
            AmbientColor = new ColorRgba(100, 100, 100, 255),
            AmbientIntensity = 0.5f,
            FogEnabled = false
        };

        Vector3 albedo = new(1.0f, 1.0f, 1.0f);
        Vector3 result = SceneLightGatherer.EvaluateLighting(
            fragPosRh: Vector3.Zero,
            normalRh: Vector3.UnitY,
            viewPosRh: new Vector3(0, 5, 0),
            albedo: albedo,
            lights: Array.Empty<SceneLightData>(),
            env: env);

        // Expected ambient: (100/255 * 0.5, 100/255 * 0.5, 100/255 * 0.5)
        float expected = (100.0f / 255.0f) * 0.5f;
        Assert.InRange(result.X, expected - 0.001f, expected + 0.001f);
        Assert.InRange(result.Y, expected - 0.001f, expected + 0.001f);
        Assert.InRange(result.Z, expected - 0.001f, expected + 0.001f);
    }

    [Fact]
    public void EvaluateLighting_PointLight_IlluminatesDiffuseAndSpecular()
    {
        var env = new EnvironmentLightingSettings
        {
            AmbientIntensity = 0.0f,
            FogEnabled = false
        };

        var ent = new LevelEntity("light_1", "light", new Transform3D(new Vector3(0.0f, 4.0f, 0.0f)));
        ent.Properties.SetProperty("color", MetadataValue.FromColor(new ColorRgba(255, 255, 255, 255)));
        ent.Properties.SetProperty("intensity", MetadataValue.FromFloat(1.0f));
        ent.Properties.SetProperty("radius", MetadataValue.FromFloat(10.0f));

        SceneLightData light = SceneLightGatherer.ExtractLightData(ent);

        Vector3 fragPosRh = Vector3.Zero;
        Vector3 normalRh = Vector3.UnitY; // Facing straight up at light
        Vector3 viewPosRh = new(0.0f, 2.0f, -2.0f); // Camera viewing from above
        Vector3 albedo = Vector3.One;

        Vector3 result = SceneLightGatherer.EvaluateLighting(
            fragPosRh, normalRh, viewPosRh, albedo, [light], env);

        // Surface directly facing light above: diffuse nDotL = 1.0, plus specular
        Assert.True(result.X > 0.3f);
        Assert.True(result.Y > 0.3f);
        Assert.True(result.Z > 0.3f);
    }

    [Fact]
    public void EvaluateLighting_DistanceFog_BlendsTowardFogColor()
    {
        var env = new EnvironmentLightingSettings
        {
            AmbientIntensity = 1.0f,
            AmbientColor = new ColorRgba(255, 255, 255, 255),
            FogEnabled = true,
            FogColor = new ColorRgba(50, 50, 50, 255),
            FogDensity = 0.1f // dense fog
        };

        Vector3 fragPosRh = new(0.0f, 0.0f, 100.0f);
        Vector3 viewPosRh = Vector3.Zero; // 100 units away -> fogFactor ~ 1.0
        Vector3 albedo = Vector3.One;

        Vector3 result = SceneLightGatherer.EvaluateLighting(
            fragPosRh, Vector3.UnitY, viewPosRh, albedo, Array.Empty<SceneLightData>(), env);

        // At 100m distance with 0.1 density: exp(-10) = 0.000045, so result is essentially FogColorNormalized
        Vector3 fogNorm = env.FogColorNormalized;
        Assert.InRange(result.X, fogNorm.X - 0.01f, fogNorm.X + 0.01f);
        Assert.InRange(result.Y, fogNorm.Y - 0.01f, fogNorm.Y + 0.01f);
        Assert.InRange(result.Z, fogNorm.Z - 0.01f, fogNorm.Z + 0.01f);
    }

    [Fact]
    public void EnvironmentLightingSettings_ResetDefaults_RestoresInitialValues()
    {
        var env = new EnvironmentLightingSettings
        {
            AmbientIntensity = 1.8f,
            FogEnabled = false,
            FogDensity = 0.05f
        };

        env.ResetDefaults();

        Assert.Equal(new ColorRgba(40, 45, 60, 255), env.AmbientColor);
        Assert.Equal(0.25f, env.AmbientIntensity);
        Assert.True(env.FogEnabled);
        Assert.Equal(new ColorRgba(20, 22, 30, 255), env.FogColor);
        Assert.Equal(0.015f, env.FogDensity);
    }
}
