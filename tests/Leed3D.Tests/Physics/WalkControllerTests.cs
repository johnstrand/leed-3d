using System;
using System.Numerics;
using Leed3D.Kernel.Entities;
using Leed3D.Kernel.Physics;
using Leed3D.Kernel.Primitives;
using Leed3D.Kernel.Scene;
using Leed3D.Runtime.Metadata;
using Xunit;

namespace Leed3D.Tests.Physics;

public class WalkControllerTests
{
    private static SceneObject AddBoxObject(EditorScene scene, string name, Vector3 size, Vector3 centerPosition)
    {
        var mesh = PrimitiveGenerator.CreateBox(size);
        return scene.AddMeshObject(name, mesh, centerPosition);
    }

    [Fact]
    public void Gravity_AirbornePlayer_FallsAndLandsOnFloorAtYZero()
    {
        var scene = new EditorScene();
        // Floor top surface at Y = 0 (thickness 1m centered at Y = -0.5)
        AddBoxObject(scene, "Floor", new Vector3(20f, 1f, 20f), new Vector3(0f, -0.5f, 0f));

        var world = CollisionMeshWorld.Build(scene);
        var controller = new WalkController(world);
        controller.Spawn(new Vector3(0f, 5f, 0f), yawDegrees: 0f);

        Assert.False(controller.IsGrounded);

        const float dt = 1f / 60f;
        for (int i = 0; i < 120; i++)
        {
            controller.Step(dt, Vector2.Zero, 0f, 0f, jumpPressed: false);
        }

        Assert.True(controller.IsGrounded);
        Assert.Equal(0f, controller.Position.Y, 3);
        Assert.Equal(controller.Config.EyeHeight, controller.EyePosition.Y, 3);
        Assert.Equal(0f, controller.Velocity.Y, 3);
    }

    [Fact]
    public void WallCollision_BlocksForwardMovement_AndSlidesAlongWall()
    {
        var scene = new EditorScene();
        // Floor top at Y = 0
        AddBoxObject(scene, "Floor", new Vector3(30f, 1f, 30f), new Vector3(0f, -0.5f, 0f));

        // Solid wall blocking +Z at Z = +2.0 (thickness 1m, center Z = +2.5, height 4m)
        AddBoxObject(scene, "SolidWall", new Vector3(10f, 4f, 1f), new Vector3(0f, 2f, 2.5f));

        var world = CollisionMeshWorld.Build(scene);
        var controller = new WalkController(world);
        controller.Spawn(new Vector3(0f, 0f, 0f), yawDegrees: 0f);

        const float dt = 1f / 60f;
        // Walk straight forward (+Z at yaw 0) for 2 seconds
        for (int i = 0; i < 120; i++)
        {
            controller.Step(dt, new Vector2(0f, 1f), yawDegrees: 0f, pitchDegrees: 0f, jumpPressed: false);
        }

        // Wall front face is at Z = 2.0; capsule radius is 0.4, so center Z cannot exceed 1.6
        Assert.True(controller.Position.Z <= 2.0f - controller.Config.Radius + 0.01f,
            $"Expected Z <= {2.0f - controller.Config.Radius}, got {controller.Position.Z}");

        // Now walk diagonally into the wall (forward + right) and verify player slides along +X
        float startX = controller.Position.X;
        for (int i = 0; i < 60; i++)
        {
            controller.Step(dt, new Vector2(1f, 1f), yawDegrees: 0f, pitchDegrees: 0f, jumpPressed: false);
        }

        Assert.True(controller.Position.X > startX + 1.5f,
            $"Expected player to slide along +X wall, got X={controller.Position.X}");
        Assert.True(controller.Position.Z <= 2.0f - controller.Config.Radius + 0.01f);
    }

    [Fact]
    public void CollisionNoneMetadata_OnObjectOrFace_AllowsPlayerToPassThroughCleanly()
    {
        var scene = new EditorScene();
        AddBoxObject(scene, "Floor", new Vector3(30f, 1f, 30f), new Vector3(0f, -0.5f, 0f));

        // Wall tagged with collision=none on its faces at Z = +2.0
        var illusionWall = AddBoxObject(scene, "IllusionWall", new Vector3(10f, 4f, 0.5f), new Vector3(0f, 2f, 2.0f));
        foreach (var face in illusionWall.Mesh!.Faces)
        {
            face.Metadata.SetString("collision", "none");
        }

        var world = CollisionMeshWorld.Build(scene);
        var controller = new WalkController(world);
        controller.Spawn(new Vector3(0f, 0f, 0f), yawDegrees: 0f);

        const float dt = 1f / 60f;
        for (int i = 0; i < 90; i++)
        {
            controller.Step(dt, new Vector2(0f, 1f), yawDegrees: 0f, pitchDegrees: 0f, jumpPressed: false);
        }

        // Player should have walked right through the wall at Z = 2.0 and reached beyond Z = 4.0
        Assert.True(controller.Position.Z > 4.0f,
            $"Expected player to pass through collision=none wall (Z > 4.0), got Z={controller.Position.Z}");
    }

    [Fact]
    public void StairStep_ClimbsLowStepUnderMaxStepHeight_AndBlocksTallStep()
    {
        var scene = new EditorScene();
        AddBoxObject(scene, "Floor", new Vector3(30f, 1f, 30f), new Vector3(0f, -0.5f, 0f));

        // 0.25m high step (<= MaxStepHeight = 0.35m) starting at Z = +1.5 (depth 4m, centered at Z = +3.5, Y = 0.125)
        AddBoxObject(scene, "LowStep", new Vector3(6f, 0.25f, 4f), new Vector3(0f, 0.125f, 3.5f));

        // 1.0m high wall (> MaxStepHeight) starting at Z = +4.5 (depth 2m, centered at Z = +5.5, Y = 0.5)
        AddBoxObject(scene, "TallStep", new Vector3(6f, 1.0f, 2f), new Vector3(0f, 0.5f, 5.5f));

        var world = CollisionMeshWorld.Build(scene);
        var controller = new WalkController(world);
        controller.Spawn(new Vector3(0f, 0f, 0f), yawDegrees: 0f);

        const float dt = 1f / 60f;
        // Walk forward onto the 0.25m step (for 0.6s -> ~3.6m)
        for (int i = 0; i < 36; i++)
        {
            controller.Step(dt, new Vector2(0f, 1f), yawDegrees: 0f, pitchDegrees: 0f, jumpPressed: false);
        }

        // Player should have climbed onto the 0.25m step
        Assert.True(controller.Position.Z > 2.0f, $"Expected player on step (Z > 2.0), got Z={controller.Position.Z}");
        Assert.Equal(0.25f, controller.Position.Y, 2);
        Assert.True(controller.IsGrounded);

        // Continue walking forward into the 1.0m step
        for (int i = 0; i < 90; i++)
        {
            controller.Step(dt, new Vector2(0f, 1f), yawDegrees: 0f, pitchDegrees: 0f, jumpPressed: false);
        }

        // Blocked by the 1.0m step face at Z = 4.5
        Assert.True(controller.Position.Z <= 4.5f - controller.Config.Radius + 0.02f,
            $"Expected player blocked by 1.0m wall at Z=4.1, got Z={controller.Position.Z}");
        Assert.Equal(0.25f, controller.Position.Y, 2);
    }

    [Fact]
    public void JumpAndSurfaceMetadata_ReadsGroundSurfaceTag_AndAppliesJumpVelocity()
    {
        var scene = new EditorScene();
        var lavaFloor = AddBoxObject(scene, "LavaFloor", new Vector3(20f, 1f, 20f), new Vector3(0f, -0.5f, 0f));
        lavaFloor.Metadata.SetString("surface", "lava");

        var world = CollisionMeshWorld.Build(scene);
        var controller = new WalkController(world);
        controller.Spawn(new Vector3(0f, 0f, 0f), yawDegrees: 0f);

        Assert.True(controller.IsGrounded);
        Assert.NotNull(controller.GroundSurfaceMetadata);
        Assert.Equal("lava", controller.GroundSurfaceMetadata!.GetSurfaceType());

        // Jump on next frame
        const float dt = 1f / 60f;
        controller.Step(dt, Vector2.Zero, 0f, 0f, jumpPressed: true);

        Assert.False(controller.IsGrounded);
        Assert.True(controller.Velocity.Y > 5.0f, $"Expected upward velocity > 5.0 after jump, got {controller.Velocity.Y}");
        Assert.True(controller.Position.Y > 0.05f, $"Expected player airborne (Y > 0.05), got {controller.Position.Y}");
    }

    [Fact]
    public void TriggerAndPortalVolumes_DetectsTrigger_AndTeleportsToTargetEntity()
    {
        var scene = new EditorScene();
        AddBoxObject(scene, "Floor", new Vector3(40f, 1f, 40f), new Vector3(0f, -0.5f, 0f));

        // Add non-teleporting trigger entity at (0, 1, 1.5)
        var triggerZone = scene.CreateEntity("trigger", "ZoneRoomA", new Vector3(0f, 1f, 1.5f));
        triggerZone.Properties.SetVec3("size", new Vector3(2f, 2f, 1f));

        // Add portal entity at (0, 1, 4.0) targeting "dest_room_b"
        var portal = scene.CreateEntity("portal", "PortalA", new Vector3(0f, 1f, 4.0f));
        portal.Properties.SetVec3("size", new Vector3(2f, 2f, 2f));
        portal.Properties.SetString("target_id", "dest_room_b");

        // Add destination entity at (10, 0, 10) with id = "dest_room_b" and yaw = 90
        var dest = scene.CreateEntity("player_spawn", "dest_room_b", new Vector3(10f, 0f, 10f));
        dest.Properties.SetFloat("yaw", 90f);

        var world = CollisionMeshWorld.Build(scene, EntitySchemaRegistry.Default);
        var controller = new WalkController(world);
        controller.Spawn(new Vector3(0f, 0f, 0f), yawDegrees: 0f);

        const float dt = 1f / 60f;
        // Walk into triggerZone at Z = 1.5 (~15 frames -> 1.5m)
        for (int i = 0; i < 15; i++)
        {
            controller.Step(dt, new Vector2(0f, 1f), controller.YawDegrees, 0f, jumpPressed: false);
        }

        Assert.Contains("ZoneRoomA", controller.ActiveTriggerIds);

        // Continue walking forward into PortalA at Z = 4.0
        for (int i = 0; i < 35; i++)
        {
            controller.Step(dt, new Vector2(0f, 1f), controller.YawDegrees, 0f, jumpPressed: false);
        }

        // Player should have entered PortalA and teleported near (10, 0, 10) facing yaw 90
        Assert.Equal("PortalA", controller.LastTeleportedPortalId);
        Assert.True(Vector3.Distance(new Vector3(controller.Position.X, 0f, controller.Position.Z), new Vector3(10f, 0f, 10f)) < 3.0f,
            $"Expected player teleported near (10, 0, 10), got {controller.Position}");
        Assert.Equal(90f, controller.YawDegrees, 2);
    }
}
