using System.IO;
using System.Linq;
using System.Numerics;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.IO;
using Leed3D.Kernel.Physics;
using Leed3D.Kernel.Primitives;
using Leed3D.Kernel.Scene;
using Leed3D.Runtime;
using Xunit;

namespace Leed3D.Tests.Scene;

public class SampleLevelTests
{
    [Fact]
    public void CreateTempleBlockout_ContainsExpectedArchitecturalElementsAndEntities()
    {
        EditorScene scene = SampleLevelFactory.CreateTempleBlockout();

        Assert.Equal("Temple Blockout", scene.Name);
        Assert.True(scene.Objects.Count >= 15, $"Expected at least 15 objects, got {scene.Objects.Count}");
        Assert.True(scene.Entities.Count >= 7, $"Expected at least 7 entities, got {scene.Entities.Count}");

        // Verify key architectural elements
        Assert.NotNull(scene.Objects.FirstOrDefault(o => o.Name == "Atrium_Floor"));
        Assert.NotNull(scene.Objects.FirstOrDefault(o => o.Name == "Dividing_Wall_Carved"));
        Assert.NotNull(scene.Objects.FirstOrDefault(o => o.Name == "Sanctum_Floor"));
        Assert.NotNull(scene.Objects.FirstOrDefault(o => o.Name == "Lava_Hazard_Pit"));
        Assert.NotNull(scene.Objects.FirstOrDefault(o => o.Name == "Fake_Illusion_Wall"));
        Assert.NotNull(scene.Objects.FirstOrDefault(o => o.Name == "Pillar_-1_-1"));
        Assert.NotNull(scene.Objects.FirstOrDefault(o => o.Name == "Stair_Step_1"));
        Assert.NotNull(scene.Objects.FirstOrDefault(o => o.Name == "Stair_Step_5"));

        // Verify entities
        Assert.NotNull(scene.Entities.FirstOrDefault(e => e.Id == "player_spawn_1"));
        Assert.NotNull(scene.Entities.FirstOrDefault(e => e.Id == "spawn_secret"));
        Assert.NotNull(scene.Entities.FirstOrDefault(e => e.Id == "light_atrium_warm"));
        Assert.NotNull(scene.Entities.FirstOrDefault(e => e.Id == "light_sanctum_blue"));
        Assert.NotNull(scene.Entities.FirstOrDefault(e => e.Id == "light_pit_red"));
        Assert.NotNull(scene.Entities.FirstOrDefault(e => e.Id == "light_secret_alcove"));
        Assert.NotNull(scene.Entities.FirstOrDefault(e => e.Id == "portal_to_sanctum"));
        Assert.NotNull(scene.Entities.FirstOrDefault(e => e.Id == "balcony_dest"));

        // Verify metadata
        SceneObject lavaPit = scene.Objects.First(o => o.Name == "Lava_Hazard_Pit");
        Assert.Equal("lava", lavaPit.Metadata.GetString("surface"));

        SceneObject fakeWall = scene.Objects.First(o => o.Name == "Fake_Illusion_Wall");
        Assert.Equal("none", fakeWall.Metadata.GetString("collision"));
    }

    [Fact]
    public void TempleBlockout_RoundTripsThroughSerializerAndRuntimeBaker()
    {
        EditorScene scene = SampleLevelFactory.CreateTempleBlockout();

        string json = LevelSerializer.SaveToJson(scene);
        Assert.False(string.IsNullOrWhiteSpace(json));

        // Test LevelSerializer editor scene round-trip
        EditorScene deserializedScene = LevelSerializer.LoadEditorSceneFromJson(json);
        Assert.Equal(scene.Name, deserializedScene.Name);
        Assert.Equal(scene.Objects.Count, deserializedScene.Objects.Count);
        Assert.Equal(scene.Entities.Count, deserializedScene.Entities.Count);

        // Test Leed3D.Runtime loader
        LeedLevelDocument doc = LeedLevelLoader.LoadFromJson(json);
        Assert.Equal("Temple Blockout", doc.Name);
        Assert.NotEmpty(doc.Baked.RenderSubmeshes);
        Assert.NotEmpty(doc.Baked.CollisionTriangles);
        Assert.True(doc.Baked.Entities.Count >= 7);

        // Verify collision mesh excludes Fake_Illusion_Wall (which has collision=none)
        CollisionMeshWorld collisionWorld = CollisionMeshWorld.Build(scene);
        SceneObject fakeWall = scene.Objects.First(o => o.Name == "Fake_Illusion_Wall");
        Assert.DoesNotContain(collisionWorld.Triangles, t => t.ObjectId == fakeWall.Id);

        // Verify Lava_Hazard_Pit has collision and surface=lava
        SceneObject lavaPit = scene.Objects.First(o => o.Name == "Lava_Hazard_Pit");
        Assert.Contains(collisionWorld.Triangles, t => t.ObjectId == lavaPit.Id && t.Metadata.GetSurfaceType() == "lava");
    }

    [Fact]
    public void TempleBlockout_WalkModeSimulation_WalksAcrossFloorClimbsStairsAndDetectsLava()
    {
        EditorScene scene = SampleLevelFactory.CreateTempleBlockout();
        CollisionMeshWorld colWorld = CollisionMeshWorld.Build(scene);

        WalkController controller = new WalkController(colWorld);

        // 1. Spawn player above atrium floor at (0, 2, 0)
        controller.Spawn(new Vector3(0f, 2f, 0f), yawDegrees: 0f);

        // Step physics to fall onto floor (floor top is at y = 0.0)
        for (int i = 0; i < 60; i++)
        {
            controller.Step(1f / 60f, Vector2.Zero, 0f, 0f, jumpPressed: false);
        }

        Assert.True(controller.IsGrounded, "Player should be grounded on the atrium floor.");
        Assert.Equal(0f, controller.Position.Y, 2);

        // 2. Spawn player near base of staircase (stairs start at Z = 16.0) facing +Z
        controller.Spawn(new Vector3(0f, 0.05f, 15.0f), yawDegrees: 0f);

        // Walk forward onto the steps
        for (int i = 0; i < 60; i++)
        {
            controller.Step(1f / 60f, new Vector2(0f, 1f), 0f, 0f, jumpPressed: false);
        }

        // Player should have climbed the staircase onto the altar mezzanine (Y = 1.25)
        Assert.True(controller.Position.Y >= 1.25f, $"Player should have reached altar platform, Y was {controller.Position.Y}");

        // 3. Spawn player above lava hazard pit (pit top is at Y = 0.2f, X = 8, Z = 24)
        controller.Spawn(new Vector3(8f, 1.0f, 24f), yawDegrees: 0f);
        for (int i = 0; i < 60; i++)
        {
            controller.Step(1f / 60f, Vector2.Zero, 0f, 0f, jumpPressed: false);
        }

        Assert.True(controller.IsGrounded, "Player should be grounded on the lava hazard pit.");
        Assert.NotNull(controller.GroundSurfaceMetadata);
        Assert.Equal("lava", controller.GroundSurfaceMetadata!.GetSurfaceType());

        // 4. Walk straight through Fake_Illusion_Wall (at X = -12.5f, Z = 24f)
        // Spawn at X = -10.0f, Z = 24f, facing -X (yaw = -90)
        controller.Spawn(new Vector3(-10f, 0.5f, 24f), yawDegrees: -90f);
        for (int i = 0; i < 60; i++)
        {
            controller.Step(1f / 60f, new Vector2(0f, 1f), -90f, 0f, jumpPressed: false);
        }

        // Player walked unobstructed through fake wall into secret alcove (X < -13.0f)
        Assert.True(controller.Position.X < -13.0f, $"Player should have walked through fake wall, X was {controller.Position.X}");
    }

    [Fact]
    public void GenerateSampleFile_WritesTempleBlockoutToSamplesDirectory()
    {
        string repoRoot = FindRepoRoot();
        string samplesDir = Path.Combine(repoRoot, "samples");
        Directory.CreateDirectory(samplesDir);

        string samplePath = Path.Combine(samplesDir, "temple_blockout.level.json");
        EditorScene scene = SampleLevelFactory.CreateTempleBlockout();
        LevelSerializer.Save(scene, samplePath);

        Assert.True(File.Exists(samplePath));
        Assert.True(new FileInfo(samplePath).Length > 1000);
    }

    private static string FindRepoRoot()
    {
        string? current = Directory.GetCurrentDirectory();
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current, "Leed3D.sln")))
            {
                return current;
            }
            current = Path.GetDirectoryName(current);
        }
        return Directory.GetCurrentDirectory();
    }

    [Fact]
    public void ObjectTransformOps_CenterOriginToCentroid_PreservesWorldPositions()
    {
        EditableMesh mesh = PrimitiveGenerator.CreateBox(new Vector3(2f, 4f, 6f));
        SceneObject obj = new SceneObject(1, "TestBox", mesh, new Transform3D(new Vector3(10f, 20f, 30f), new Vector3(0f, 45f, 0f), new Vector3(1.5f, 1.5f, 1.5f)));

        // Offset mesh vertices locally so origin is not at centroid
        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            mesh.Vertices[i].Position += new Vector3(5f, 0f, 0f);
        }

        Vector3 worldPosBefore = obj.Transform.TransformPointToWorld(mesh.Vertices[0].Position);

        bool changed = ObjectTransformOps.CenterOriginToCentroid(obj);
        Assert.True(changed);

        Vector3 worldPosAfter = obj.Transform.TransformPointToWorld(mesh.Vertices[0].Position);
        Assert.True(Vector3.Distance(worldPosBefore, worldPosAfter) < 0.001f);
    }

    [Fact]
    public void ObjectTransformOps_CenterOriginToBottomBounds_PreservesWorldPositions()
    {
        EditableMesh mesh = PrimitiveGenerator.CreateBox(new Vector3(2f, 4f, 6f));
        SceneObject obj = new SceneObject(1, "TestBox", mesh, new Transform3D(new Vector3(5f, 5f, 5f)));

        Vector3 worldPosBefore = obj.Transform.TransformPointToWorld(mesh.Vertices[0].Position);

        bool changed = ObjectTransformOps.CenterOriginToBottomBounds(obj);
        Assert.True(changed);

        Vector3 worldPosAfter = obj.Transform.TransformPointToWorld(mesh.Vertices[0].Position);
        Assert.True(Vector3.Distance(worldPosBefore, worldPosAfter) < 0.001f);
    }

    [Fact]
    public void ObjectTransformOps_FreezeScaleAndRotation_ResetsTransformAndPreservesWorldGeometry()
    {
        EditableMesh mesh = PrimitiveGenerator.CreateBox(new Vector3(2f, 4f, 6f));
        SceneObject obj = new SceneObject(1, "TestBox", mesh, new Transform3D(new Vector3(1f, 2f, 3f), new Vector3(0f, 90f, 0f), new Vector3(2f, 3f, 4f)));

        Vector3 worldPosBefore = obj.Transform.TransformPointToWorld(mesh.Vertices[0].Position);

        bool changed = ObjectTransformOps.FreezeScaleAndRotation(obj);
        Assert.True(changed);

        Assert.True(obj.Transform.RotationEulerDegrees.LengthSquared() < 0.0001f);
        Assert.Equal(Vector3.One, obj.Transform.Scale);
        Assert.Equal(new Vector3(1f, 2f, 3f), obj.Transform.Position);

        Vector3 worldPosAfter = obj.Transform.TransformPointToWorld(mesh.Vertices[0].Position);
        Assert.True(Vector3.Distance(worldPosBefore, worldPosAfter) < 0.001f);
    }
}
