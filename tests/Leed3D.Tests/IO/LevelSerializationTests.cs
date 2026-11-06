using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Leed3D.Kernel.Csg;
using Leed3D.Kernel.Entities;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.IO;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Physics;
using Leed3D.Kernel.Primitives;
using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Texturing;
using Leed3D.Runtime;
using Leed3D.Runtime.Metadata;
using Xunit;

namespace Leed3D.Tests.IO;

public class LevelSerializationTests
{
    [Fact]
    public void RoundTrip_ComplexScene_PreservesAllElementsWithFullFidelity()
    {
        var originalScene = new EditorScene("TestLevel_FullFidelity");

        // 1. Create a primary object with N-gons, UV mappings, and metadata
        var mesh = new EditableMesh();
        int v0 = mesh.AddVertex(new Vector3(0f, 0f, 0f));
        int v1 = mesh.AddVertex(new Vector3(2f, 0f, 0f));
        int v2 = mesh.AddVertex(new Vector3(2.5f, 1f, 0f));
        int v3 = mesh.AddVertex(new Vector3(1f, 2f, 0f));
        int v4 = mesh.AddVertex(new Vector3(0f, 1.5f, 0f));

        // Vertex metadata on v3
        MeshVertex? vert3 = mesh.GetVertex(v3);
        Assert.NotNull(vert3);
        vert3.Metadata.SetFloat("weight", 0.75f);
        vert3.Metadata.AddTag("pinned");

        // 5-sided pentagon face with locked UVs and face metadata
        var uvMapping = new FaceUvMapping
        {
            UAxis = new Vector3(1f, 0f, 0f),
            VAxis = new Vector3(0f, 1f, 0f),
            Offset = new Vector2(0.25f, 0.5f),
            Scale = new Vector2(2f, 3f),
            RotationDegrees = 45f,
            TextureLock = true
        };
        var faceMeta = new ElementMetadata();
        faceMeta.SetString("surface", "lava");
        faceMeta.SetInt("damage", 25);
        faceMeta.AddTag("hazard");

        int faceId = mesh.AddFace([v0, v1, v2, v3, v4], "custom_lava_mat", uvMapping, faceMeta);

        // Edge metadata on edge (v0, v1)
        var edgeKey = new EdgeKey(v0, v1);
        ElementMetadata edgeMeta = mesh.GetOrCreateEdgeMetadata(edgeKey);
        edgeMeta.SetFloat("crease", 1.0f);
        edgeMeta.AddTag("sharp");

        var obj1 = originalScene.AddMeshObject("PentagonWall", mesh, new Vector3(10f, 2f, -5f));
        obj1.Transform.RotationEulerDegrees = new Vector3(0f, 90f, 0f);
        obj1.Transform.Scale = new Vector3(1.5f, 1.5f, 1.5f);
        obj1.Metadata.SetString("author", "LevelArchitect");
        obj1.Metadata.SetBool("destructible", false);
        obj1.Metadata.AddTag("architecture");

        // 2. Create a second object (cube with CSG carve)
        var cubeMesh = PrimitiveGenerator.CreateBox(new Vector3(4f, 4f, 4f), materialId: "dev_metal");
        var cutterMesh = PrimitiveGenerator.CreateBox(new Vector3(2f, 2f, 6f), materialId: "dev_grid_gray");
        var carvedMesh = CsgBooleanEngine.Evaluate(cubeMesh, cutterMesh, CsgOperation.Subtract);
        var obj2 = originalScene.AddMeshObject("CarvedColumn", carvedMesh, new Vector3(-8f, 0f, 12f));

        // 3. Add entities (player_spawn, light, portal, trigger)
        var spawn = originalScene.CreateEntity("player_spawn", "spawn_player_1", new Vector3(0f, 1f, 0f));
        spawn.Properties.SetFloat("yaw", 180f);
        spawn.Properties.AddTag("start_point");

        var light = originalScene.CreateEntity("light", "ambient_fill", new Vector3(0f, 5f, 0f));
        light.Properties.SetColor("color", ColorRgba.FromHex("#FFAA33FF"));
        light.Properties.SetFloat("intensity", 2.5f);

        var portal = originalScene.CreateEntity("portal", "portal_to_arena", new Vector3(5f, 1f, 10f));
        portal.Properties.SetVec3("size", new Vector3(2f, 3f, 1f));
        portal.Properties.SetString("target_id", "arena_entry");

        // --- Save to JSON ---
        string json = LevelSerializer.SaveToJson(originalScene);
        Assert.False(string.IsNullOrWhiteSpace(json));

        // --- Load back to EditorScene ---
        EditorScene restoredScene = LevelSerializer.LoadEditorSceneFromJson(json);

        // Verify Scene attributes
        Assert.Equal("TestLevel_FullFidelity", restoredScene.Name);
        Assert.Equal(2, restoredScene.ObjectCount);
        Assert.Equal(3, restoredScene.EntityCount);

        // Verify PentagonWall object
        SceneObject? restoredObj1 = restoredScene.Objects.FirstOrDefault(o => o.Name == "PentagonWall");
        Assert.NotNull(restoredObj1);
        Assert.Equal(obj1.Id, restoredObj1.Id);
        Assert.Equal(new Vector3(10f, 2f, -5f), restoredObj1.Transform.Position);
        Assert.Equal(new Vector3(0f, 90f, 0f), restoredObj1.Transform.RotationEulerDegrees);
        Assert.Equal(new Vector3(1.5f, 1.5f, 1.5f), restoredObj1.Transform.Scale);
        Assert.Equal("LevelArchitect", restoredObj1.Metadata.GetString("author"));
        Assert.False(restoredObj1.Metadata.GetBool("destructible", true));
        Assert.True(restoredObj1.Metadata.HasTag("architecture"));

        // Verify PentagonWall Mesh & Vertices
        Assert.NotNull(restoredObj1.Mesh);
        Assert.Equal(5, restoredObj1.Mesh.VertexCount);
        MeshVertex? restoredV3 = restoredObj1.Mesh.GetVertex(v3);
        Assert.NotNull(restoredV3);
        Assert.Equal(new Vector3(1f, 2f, 0f), restoredV3.Position);
        Assert.Equal(0.75f, restoredV3.Metadata.GetFloat("weight") ?? 0f, 3);
        Assert.True(restoredV3.Metadata.HasTag("pinned"));

        // Verify Pentagon Face N-gon & UV mapping
        Assert.Equal(1, restoredObj1.Mesh.FaceCount);
        PolygonFace? restoredFace = restoredObj1.Mesh.GetFace(faceId);
        Assert.NotNull(restoredFace);
        Assert.Equal([v0, v1, v2, v3, v4], restoredFace.VertexIds);
        Assert.Equal("custom_lava_mat", restoredFace.MaterialId);
        Assert.Equal("lava", restoredFace.Metadata.GetString("surface"));
        Assert.Equal(25, restoredFace.Metadata.GetInt("damage"));
        Assert.True(restoredFace.Metadata.HasTag("hazard"));

        // UV mapping checks
        Assert.True(restoredFace.UvMapping.TextureLock);
        Assert.Equal(45f, restoredFace.UvMapping.RotationDegrees, 2);
        Assert.Equal(new Vector2(0.25f, 0.5f), restoredFace.UvMapping.Offset);
        Assert.Equal(new Vector2(2f, 3f), restoredFace.UvMapping.Scale);
        Assert.Equal(new Vector3(1f, 0f, 0f), restoredFace.UvMapping.UAxis);
        Assert.Equal(new Vector3(0f, 1f, 0f), restoredFace.UvMapping.VAxis);

        // Verify Edge Metadata
        ElementMetadata? restoredEdgeMeta = restoredObj1.Mesh.GetEdgeMetadata(edgeKey);
        Assert.NotNull(restoredEdgeMeta);
        Assert.Equal(1.0f, restoredEdgeMeta.GetFloat("crease") ?? 0f, 3);
        Assert.True(restoredEdgeMeta.HasTag("sharp"));

        // Verify Carved Column
        SceneObject? restoredObj2 = restoredScene.Objects.FirstOrDefault(o => o.Name == "CarvedColumn");
        Assert.NotNull(restoredObj2);
        Assert.NotNull(restoredObj2.Mesh);
        Assert.True(restoredObj2.Mesh.FaceCount > 6); // Carved geometry has more faces than a standard cube

        // Verify Entities
        LevelEntity? restoredSpawn = restoredScene.GetEntity("spawn_player_1");
        Assert.NotNull(restoredSpawn);
        Assert.Equal("player_spawn", restoredSpawn.ClassName);
        Assert.Equal(new Vector3(0f, 1f, 0f), restoredSpawn.Transform.Position);
        Assert.Equal(180f, restoredSpawn.Properties.GetFloat("yaw") ?? 0f, 2);
        Assert.True(restoredSpawn.Properties.HasTag("start_point"));

        LevelEntity? restoredLight = restoredScene.GetEntity("ambient_fill");
        Assert.NotNull(restoredLight);
        Assert.Equal(2.5f, restoredLight.Properties.GetFloat("intensity") ?? 0f, 2);
        Assert.Equal(ColorRgba.FromHex("#FFAA33FF"), restoredLight.Properties.GetColor("color"));

        LevelEntity? restoredPortal = restoredScene.GetEntity("portal_to_arena");
        Assert.NotNull(restoredPortal);
        Assert.Equal("arena_entry", restoredPortal.Properties.GetString("target_id"));
        Assert.Equal(new Vector3(2f, 3f, 1f), restoredPortal.Properties.GetVec3("size"));
    }

    [Fact]
    public void BakeRuntimeLayer_OmitsCollisionNone_AndPreservesRenderSubmeshes()
    {
        var scene = new EditorScene("BakeRuntimeTest");

        // 1. Floor (solid dev_grid_gray)
        var floor = scene.AddMeshObject("Floor", PrimitiveGenerator.CreateBox(new Vector3(20f, 1f, 20f), materialId: "dev_grid_gray"), new Vector3(0f, -0.5f, 0f));
        floor.Metadata.SetString("surface", "concrete");

        // 2. Illusionary Wall (visible dev_brick, but collision disabled on face)
        var illusionWallMesh = PrimitiveGenerator.CreateBox(new Vector3(6f, 3f, 1f), materialId: "dev_brick");
        foreach (var face in illusionWallMesh.Faces)
        {
            face.Metadata.SetString("collision", "none");
        }
        scene.AddMeshObject("GhostWall", illusionWallMesh, new Vector3(0f, 1.5f, 5f));

        // 3. Decorative Mesh (collision disabled on object level)
        var decorObj = scene.AddMeshObject("HologramDecor", PrimitiveGenerator.CreateBox(new Vector3(1f, 2f, 1f), materialId: "dev_water"), new Vector3(-3f, 1f, 0f));
        decorObj.Metadata.SetBool("collision", false);

        // 4. Solid barrier wall
        scene.AddMeshObject("SolidBarrier", PrimitiveGenerator.CreateBox(new Vector3(4f, 2f, 1f), materialId: "dev_metal"), new Vector3(3f, 1f, 0f));

        // Bake the level document
        LeedLevelDocument doc = LevelSerializer.CreateDocumentFromEditorScene(scene, bakeRightHandedRuntime: false);

        // Verify Baked Render Submeshes
        IReadOnlyList<BakedSubmeshDto> submeshes = doc.GetRenderSubmeshes();
        Assert.Equal(4, submeshes.Count); // dev_grid_gray, dev_brick, dev_water, dev_metal all rendered!

        BakedSubmeshDto? brickSubmesh = submeshes.FirstOrDefault(s => s.MaterialId == "dev_brick");
        Assert.NotNull(brickSubmesh);
        Assert.True(brickSubmesh.VertexCount > 0);
        Assert.True(brickSubmesh.TriangleCount > 0);
        Assert.True(brickSubmesh.FaceSourceIds.Count == brickSubmesh.TriangleCount);

        // Verify Baked Collision Triangles (GhostWall and HologramDecor must be completely absent!)
        IReadOnlyList<BakedCollisionTriangleDto> collisionTris = doc.GetCollisionTriangles();
        Assert.NotEmpty(collisionTris);

        bool hasBrickCollision = collisionTris.Any(t => t.MaterialId == "dev_brick");
        bool hasWaterCollision = collisionTris.Any(t => t.MaterialId == "dev_water");
        Assert.False(hasBrickCollision, "Illusionary wall with collision=none faces must not generate collision triangles.");
        Assert.False(hasWaterCollision, "Hologram decor with object collision=false must not generate collision triangles.");

        // Floor and solid metal barrier should be present
        Assert.Contains(collisionTris, t => t.MaterialId == "dev_grid_gray");
        Assert.Contains(collisionTris, t => t.MaterialId == "dev_metal");

        // Verify merged metadata on floor collision triangles
        BakedCollisionTriangleDto floorTri = collisionTris.First(t => t.MaterialId == "dev_grid_gray");
        Assert.Equal("concrete", floorTri.SurfaceMetadata.GetString("surface"));
    }

    [Fact]
    public void BakeRightHanded_ConvertsCoordinatesAndTriangleWinding()
    {
        var scene = new EditorScene("RightHandedBakeTest");
        // A single face on XY plane facing +Z (at Z = 5.0)
        var mesh = new EditableMesh();
        int v0 = mesh.AddVertex(new Vector3(0f, 0f, 5f));
        int v1 = mesh.AddVertex(new Vector3(2f, 0f, 5f));
        int v2 = mesh.AddVertex(new Vector3(2f, 2f, 5f));
        int v3 = mesh.AddVertex(new Vector3(0f, 2f, 5f));
        mesh.AddFace([v0, v1, v2, v3], "test_mat");
        scene.AddMeshObject("QuadWall", mesh, Vector3.Zero);

        var spawn = scene.CreateEntity("player_spawn", "spawn_1", new Vector3(1f, 0f, 8f));

        // Bake in Right-Handed coordinates
        LeedLevelDocument doc = LevelSerializer.CreateDocumentFromEditorScene(scene, bakeRightHandedRuntime: true);

        Assert.Equal(LeedLevelDocument.RightHandedCoordinateSystem, doc.CoordinateSystem);

        // Check Render Submesh positions: Z coordinate must be negated (-5.0f)
        BakedSubmeshDto submesh = Assert.Single(doc.GetRenderSubmeshes());
        foreach (Vector3Dto pos in submesh.Positions)
        {
            Assert.Equal(-5.0f, pos.Z, 3);
        }

        // Check Baked Entity position: Z coordinate must be negated (-8.0f)
        LevelEntityDto? bakedSpawn = doc.FindFirstEntity("player_spawn");
        Assert.NotNull(bakedSpawn);
        Assert.Equal(-8.0f, bakedSpawn.Transform.Position.Z, 3);

        // Check World Bounds Z min and max
        Assert.True(doc.Baked.WorldBounds.Min.Z <= -8.0f);
        Assert.True(doc.Baked.WorldBounds.Max.Z >= -5.0f);
    }

    [Fact]
    public void RuntimeLoader_ConvenienceMethods_WorkWithZeroDependencies()
    {
        var scene = new EditorScene("ZeroDepTest");
        scene.AddMeshObject("Box", PrimitiveGenerator.CreateBox(new Vector3(2f, 2f, 2f), materialId: "mat_stone"), new Vector3(0f, 1f, 0f));
        scene.CreateEntity("player_spawn", "spawn_a", new Vector3(0f, 0f, 0f));
        scene.CreateEntity("light", "light_1", new Vector3(0f, 3f, 0f));
        scene.CreateEntity("light", "light_2", new Vector3(4f, 3f, 0f));

        string tempPath = Path.Combine(Path.GetTempPath(), $"leed_test_{Guid.NewGuid():N}.level.json");
        try
        {
            LevelSerializer.Save(scene, tempPath, bakeRightHandedRuntime: false);

            // Load using zero-dependency LeedLevelLoader
            LeedLevelDocument loadedDoc = LeedLevelLoader.Load(tempPath);

            Assert.Equal("ZeroDepTest", loadedDoc.Name);

            // Entity queries
            LevelEntityDto? spawn = loadedDoc.FindFirstEntity("player_spawn");
            Assert.NotNull(spawn);
            Assert.Equal("spawn_a", spawn.Id);

            var lights = loadedDoc.GetEntitiesByClass("light").ToList();
            Assert.Equal(2, lights.Count);

            // Submesh queries & flat array export
            var submeshes = loadedDoc.GetRenderSubmeshes();
            Assert.Single(submeshes);
            BakedSubmeshDto stoneSubmesh = submeshes[0];

            float[] flatPositions = stoneSubmesh.GetPositionsFlat();
            float[] flatNormals = stoneSubmesh.GetNormalsFlat();
            float[] flatUVs = stoneSubmesh.GetUVsFlat();
            int[] indices = stoneSubmesh.GetIndices();

            Assert.Equal(stoneSubmesh.Positions.Count * 3, flatPositions.Length);
            Assert.Equal(stoneSubmesh.Normals.Count * 3, flatNormals.Length);
            Assert.Equal(stoneSubmesh.UVs.Count * 2, flatUVs.Length);
            Assert.Equal(stoneSubmesh.Indices.Count, indices.Length);

            // Collision triangles query
            var colTris = loadedDoc.GetCollisionTriangles();
            Assert.NotEmpty(colTris);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public void WalkController_PlaysSeamlesslyOnBakedCollisionMesh()
    {
        var scene = new EditorScene("BakedPlaytestLevel");
        // Floor at Y = 0
        var floor = scene.AddMeshObject("Floor", PrimitiveGenerator.CreateBox(new Vector3(40f, 1f, 40f)), new Vector3(0f, -0.5f, 0f));
        floor.Metadata.SetString("surface", "ice");

        // Player spawn at (0, 0, 0)
        scene.CreateEntity("player_spawn", "player_start", new Vector3(0f, 0f, 0f));

        // Wall at Z = 4
        scene.AddMeshObject("Wall", PrimitiveGenerator.CreateBox(new Vector3(10f, 4f, 1f)), new Vector3(0f, 2f, 4.5f));

        // Bake
        LeedLevelDocument doc = LevelSerializer.CreateDocumentFromEditorScene(scene, bakeRightHandedRuntime: false);

        // Build CollisionMeshWorld directly from doc.Baked
        CollisionMeshWorld world = CollisionMeshWorld.BuildFromBaked(doc.Baked);
        var controller = new WalkController(world);

        LevelEntityDto? spawn = doc.FindFirstEntity("player_spawn");
        Assert.NotNull(spawn);
        controller.Spawn(spawn.Transform.Position, 0f);

        Assert.True(controller.IsGrounded);
        Assert.Equal("ice", controller.GroundSurfaceMetadata?.GetString("surface"));

        // Walk forward towards wall (+Z)
        const float dt = 1f / 60f;
        for (int i = 0; i < 90; i++)
        {
            controller.Step(dt, new Vector2(0f, 1f), 0f, 0f, jumpPressed: false);
        }

        // Wall front face is at Z = 4.0; capsule radius is 0.4, so position.Z <= 3.61
        Assert.True(controller.Position.Z <= 4.0f - controller.Config.Radius + 0.02f,
            $"Expected player stopped by wall at Z <= {4.0f - controller.Config.Radius}, got {controller.Position.Z}");
        Assert.True(controller.IsGrounded);
    }
}
