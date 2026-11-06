using System.Numerics;
using Leed3D.Kernel.Csg;
using Leed3D.Kernel.Entities;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Primitives;
using Leed3D.Runtime.Metadata;

namespace Leed3D.Kernel.Scene;

/// <summary>
/// Factory generating playable sample levels and blockouts showcasing all Leed3D features.
/// </summary>
public static class SampleLevelFactory
{
    /// <summary>
    /// Creates the playable "Temple Blockout" sample level featuring interconnected rooms,
    /// CSG-carved doorways and windows, extruded stairs, cylinder pillars, multi-colored dev grids,
    /// player spawn, colored lights, linked portal, lava hazard pit, and a secret fake wall alcove.
    /// </summary>
    public static EditorScene CreateTempleBlockout()
    {
        var scene = new EditorScene("Temple Blockout");

        // 1. Main Atrium Floor & Perimeter
        var floorMesh = PrimitiveGenerator.CreateBox(new Vector3(24f, 0.5f, 24f), materialId: "dev_grid_gray");
        var floor = scene.AddMeshObject("Atrium_Floor", floorMesh, new Vector3(0f, -0.25f, 0f));
        floor.Metadata.SetString("surface", "stone");

        // Atrium Outer Walls (North, South, East, West)
        scene.AddMeshObject("Wall_South", PrimitiveGenerator.CreateBox(new Vector3(24f, 6f, 1f), materialId: "dev_grid_orange"), new Vector3(0f, 3f, -12.5f));
        scene.AddMeshObject("Wall_West", PrimitiveGenerator.CreateBox(new Vector3(1f, 6f, 24f), materialId: "dev_grid_orange"), new Vector3(-12.5f, 3f, 0f));
        scene.AddMeshObject("Wall_East", PrimitiveGenerator.CreateBox(new Vector3(1f, 6f, 24f), materialId: "dev_grid_orange"), new Vector3(12.5f, 3f, 0f));

        // 2. CSG-Carved Dividing Wall (Doorway + Window)
        var partitionMesh = PrimitiveGenerator.CreateBox(new Vector3(24f, 6f, 1f), materialId: "dev_grid_orange");
        var partObj = new SceneObject(998, "Partition", partitionMesh, new Transform3D());
        var doorCutter = PrimitiveGenerator.CreateBox(new Vector3(3f, 3.2f, 3f), materialId: "dev_grid_gray");
        var doorObj = new SceneObject(997, "DoorCutter", doorCutter, new Transform3D(new Vector3(0f, -1.5f, 0f)));
        var windowCutter = PrimitiveGenerator.CreateBox(new Vector3(2.5f, 1.8f, 3f), materialId: "dev_grid_gray");
        var windowObj = new SceneObject(999, "WindowCutter", windowCutter, new Transform3D(new Vector3(6f, 0.5f, 0f)));

        var doorwayCarved = CsgBooleanEngine.Evaluate(partObj, doorObj, CsgOperation.Subtract);
        var doorwayObj = new SceneObject(996, "DoorwayCarved", doorwayCarved, new Transform3D());
        var fullyCarvedPartition = CsgBooleanEngine.Evaluate(doorwayObj, windowObj, CsgOperation.Subtract);

        scene.AddMeshObject("Dividing_Wall_Carved", fullyCarvedPartition, new Vector3(0f, 3f, 12.5f));

        // 3. Four Cylinder Pillars in Atrium
        for (int x = -1; x <= 1; x += 2)
        {
            for (int z = -1; z <= 1; z += 2)
            {
                var pillarMesh = PrimitiveGenerator.CreateCylinder(new CylinderParams(Radius: 0.6f, Height: 6.0f, RadialSegments: 16, MaterialId: "dev_grid_blue"));
                scene.AddMeshObject($"Pillar_{x}_{z}", pillarMesh, new Vector3(x * 6.5f, 3.0f, z * 6.5f));
            }
        }

        // 4. Inner Sanctum Floor (beyond dividing wall)
        var sanctumFloor = scene.AddMeshObject("Sanctum_Floor", PrimitiveGenerator.CreateBox(new Vector3(24f, 0.5f, 20f), materialId: "dev_grid_gray"), new Vector3(0f, -0.25f, 22.5f));
        sanctumFloor.Metadata.SetString("surface", "stone");

        scene.AddMeshObject("Sanctum_North_Wall", PrimitiveGenerator.CreateBox(new Vector3(24f, 6f, 1f), materialId: "dev_grid_orange"), new Vector3(0f, 3f, 32.5f));

        // Sanctum West Wall with CSG opening for the secret alcove
        var westWallMesh = PrimitiveGenerator.CreateBox(new Vector3(1f, 6f, 20f), materialId: "dev_grid_orange");
        var alcoveCutter = PrimitiveGenerator.CreateBox(new Vector3(2f, 4f, 4.2f), materialId: "dev_grid_gray");
        var westWallObj = new SceneObject(995, "WestWall", westWallMesh, new Transform3D());
        var alcoveCutterObj = new SceneObject(994, "AlcoveCutter", alcoveCutter, new Transform3D(new Vector3(0f, -1.0f, 1.5f)));
        var carvedWestWall = CsgBooleanEngine.Evaluate(westWallObj, alcoveCutterObj, CsgOperation.Subtract);
        scene.AddMeshObject("Sanctum_West_Wall", carvedWestWall, new Vector3(-12.5f, 3f, 22.5f));

        scene.AddMeshObject("Sanctum_East_Wall", PrimitiveGenerator.CreateBox(new Vector3(1f, 6f, 20f), materialId: "dev_grid_orange"), new Vector3(12.5f, 3f, 22.5f));

        // 5. Extruded Stair Steps to Raised Platform (5 steps of height 0.25m)
        float stepHeight = 0.25f;
        float stepDepth = 0.8f;
        float stepWidth = 4.0f;
        for (int s = 0; s < 5; s++)
        {
            float curH = (s + 1) * stepHeight;
            float curZ = 16.0f + (s * stepDepth);
            var stepMesh = PrimitiveGenerator.CreateBox(new Vector3(stepWidth, curH, stepDepth), materialId: "dev_grid_blue");
            scene.AddMeshObject($"Stair_Step_{s + 1}", stepMesh, new Vector3(0f, curH * 0.5f, curZ));
        }

        // Raised Mezzanine Platform (Y = 1.25)
        var platformMesh = PrimitiveGenerator.CreateBox(new Vector3(10f, 1.25f, 8f), materialId: "dev_grid_green");
        scene.AddMeshObject("Raised_Altar_Platform", platformMesh, new Vector3(0f, 0.625f, 24f));

        // 6. Lava Hazard Pit in East Corner of Sanctum
        var lavaMesh = PrimitiveGenerator.CreateBox(new Vector3(6f, 0.2f, 6f), materialId: "dev_lava");
        var lavaObj = scene.AddMeshObject("Lava_Hazard_Pit", lavaMesh, new Vector3(8f, 0.1f, 24f));
        lavaObj.Metadata.SetString("surface", "lava");
        lavaObj.Metadata.SetInt("damage", 50);
        lavaObj.Metadata.AddTag("hazard");
        foreach (var face in lavaMesh.Faces)
        {
            face.Metadata.SetString("surface", "lava");
            face.Metadata.AddTag("hazard");
        }

        // 7. Secret Alcove behind Fake Wall (collision=none)
        // Recessed alcove in West wall
        scene.AddMeshObject("Secret_Alcove_Room", PrimitiveGenerator.CreateBox(new Vector3(6f, 4f, 6f), invertNormals: true, materialId: "dev_grid_purple"), new Vector3(-16f, 2f, 24f));

        // Fake Wall concealing the alcove
        var fakeWallMesh = PrimitiveGenerator.CreateBox(new Vector3(0.5f, 4f, 4f), materialId: "dev_grid_purple");
        var fakeWallObj = scene.AddMeshObject("Fake_Illusion_Wall", fakeWallMesh, new Vector3(-12.5f, 2f, 24f));
        fakeWallObj.Metadata.SetString("collision", "none");
        fakeWallObj.Metadata.AddTag("secret");
        foreach (var f in fakeWallMesh.Faces)
        {
            f.Metadata.SetString("collision", "none");
        }

        // 8. Gameplay Entities
        // Player Spawn in Atrium
        var playerSpawn = scene.CreateEntity("player_spawn", "player_spawn_1", new Vector3(0f, 0.1f, -8f));
        playerSpawn.Properties.SetFloat("yaw", 0f);
        playerSpawn.Properties.AddTag("start_point");

        // Warm ambient light in Atrium
        var lightAtrium = scene.CreateEntity("light", "light_atrium_warm", new Vector3(0f, 5.0f, 0f));
        lightAtrium.Properties.SetColor("color", ColorRgba.FromHex("#FFDD99FF"));
        lightAtrium.Properties.SetFloat("intensity", 2.2f);
        lightAtrium.Properties.SetFloat("range", 18.0f);

        // Cool mystical light in Sanctum
        var lightSanctum = scene.CreateEntity("light", "light_sanctum_blue", new Vector3(0f, 5.0f, 24f));
        lightSanctum.Properties.SetColor("color", ColorRgba.FromHex("#66AAFFFF"));
        lightSanctum.Properties.SetFloat("intensity", 2.0f);
        lightSanctum.Properties.SetFloat("range", 16.0f);

        // Fiery red light at Lava Pit
        var lightPit = scene.CreateEntity("light", "light_pit_red", new Vector3(8f, 2.5f, 24f));
        lightPit.Properties.SetColor("color", ColorRgba.FromHex("#FF4411FF"));
        lightPit.Properties.SetFloat("intensity", 2.5f);
        lightPit.Properties.SetFloat("range", 10.0f);

        // Eerie purple light inside secret alcove
        var lightSecret = scene.CreateEntity("light", "light_secret_alcove", new Vector3(-16f, 2.5f, 24f));
        lightSecret.Properties.SetColor("color", ColorRgba.FromHex("#CC55FFFF"));
        lightSecret.Properties.SetFloat("intensity", 1.8f);

        // Secret alcove spawn point
        var secretSpawn = scene.CreateEntity("player_spawn", "spawn_secret", new Vector3(-16f, 0.1f, 24f));
        secretSpawn.Properties.SetFloat("yaw", 90f);

        // Portal on Altar Platform targeting Atrium Balcony
        var portal = scene.CreateEntity("portal", "portal_to_sanctum", new Vector3(0f, 2.25f, 26f));
        portal.Properties.SetVec3("size", new Vector3(2f, 2f, 1f));
        portal.Properties.SetString("target_id", "balcony_dest");

        // Teleport destination back in Atrium
        var dest = scene.CreateEntity("player_spawn", "balcony_dest", new Vector3(0f, 0.1f, -2f));
        dest.Properties.SetFloat("yaw", 0f);

        return scene;
    }
}
