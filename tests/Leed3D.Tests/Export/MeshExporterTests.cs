using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Leed3D.Kernel.Export;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.IO;
using Leed3D.Kernel.Primitives;
using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Texturing;
using Leed3D.Runtime;
using Xunit;

namespace Leed3D.Tests.Export;

public class MeshExporterTests
{
    private static EditorScene CreateSampleScene()
    {
        var scene = new EditorScene("ExportSampleScene");

        // Object 1: Cube with dev_grid_orange
        var cube = scene.AddMeshObject("MainCube", PrimitiveGenerator.CreateBox(new Vector3(4f, 4f, 4f), materialId: "dev_grid_orange"), new Vector3(0f, 2f, 5f));

        // Object 2: Custom N-gon polygon wall with dev_grid_blue
        var polyMesh = new EditableMesh();
        int v0 = polyMesh.AddVertex(new Vector3(0f, 0f, 0f));
        int v1 = polyMesh.AddVertex(new Vector3(3f, 0f, 0f));
        int v2 = polyMesh.AddVertex(new Vector3(3.5f, 2f, 0f));
        int v3 = polyMesh.AddVertex(new Vector3(1.5f, 3.5f, 0f));
        int v4 = polyMesh.AddVertex(new Vector3(0f, 2f, 0f));
        polyMesh.AddFace([v0, v1, v2, v3, v4], "dev_grid_blue");

        scene.AddMeshObject("PentagonWall", polyMesh, new Vector3(-6f, 0f, 10f));

        return scene;
    }

    [Fact]
    public void ObjExporter_ExportsValidObjAndMtl_WithRightHandedConversion()
    {
        EditorScene scene = CreateSampleScene();

        (string objContent, string mtlContent) = ObjExporter.ExportToString(
            scene,
            mtlFileName: "level_materials.mtl",
            convertToRightHanded: true);

        // Verify .obj header
        Assert.Contains("mtllib level_materials.mtl", objContent);

        // Verify object markers
        Assert.Contains("o MainCube", objContent);
        Assert.Contains("o PentagonWall", objContent);

        // Verify material assignments
        Assert.Contains("usemtl dev_grid_orange", objContent);
        Assert.Contains("usemtl dev_grid_blue", objContent);

        // Verify vertex and normal elements
        Assert.Contains("v ", objContent);
        Assert.Contains("vt ", objContent);
        Assert.Contains("vn ", objContent);
        Assert.Contains("f ", objContent);

        // Check coordinate handedness: PentagonWall was at Z = 10.0, so in right-handed space Z is negated (-10.0)
        Assert.Contains("-10.000000", objContent);
        // MainCube was centered at Z = 5.0 with half-depth 2.0, so vertices are at Z = -3.0 and -7.0
        Assert.Contains("-3.000000", objContent);
        Assert.Contains("-7.000000", objContent);

        // Verify .mtl material library content
        Assert.Contains("newmtl dev_grid_orange", mtlContent);
        Assert.Contains("newmtl dev_grid_blue", mtlContent);
        Assert.Contains("Kd ", mtlContent);
        Assert.Contains("illum 2", mtlContent);
    }

    [Fact]
    public void ObjExporter_FiltersByOnlyObjectIds()
    {
        EditorScene scene = CreateSampleScene();
        SceneObject cubeObj = scene.Objects.First(o => o.Name == "MainCube");

        (string objContent, string mtlContent) = ObjExporter.ExportToString(
            scene,
            onlyObjectIds: [cubeObj.Id]);

        Assert.Contains("o MainCube", objContent);
        Assert.DoesNotContain("o PentagonWall", objContent);

        // Only orange material should be referenced
        Assert.Contains("newmtl dev_grid_orange", mtlContent);
        Assert.DoesNotContain("newmtl dev_grid_blue", mtlContent);
    }

    [Fact]
    public void ObjExporter_WritesFilesToDisk()
    {
        EditorScene scene = CreateSampleScene();
        string tempDir = Path.Combine(Path.GetTempPath(), $"leed_obj_test_{Guid.NewGuid():N}");
        string objPath = Path.Combine(tempDir, "test_level.obj");
        string mtlPath = Path.Combine(tempDir, "test_level.mtl");

        try
        {
            ObjExporter.Export(scene, objPath);

            Assert.True(File.Exists(objPath));
            Assert.True(File.Exists(mtlPath));

            string objText = File.ReadAllText(objPath);
            string mtlText = File.ReadAllText(mtlPath);

            Assert.Contains("mtllib test_level.mtl", objText);
            Assert.Contains("o MainCube", objText);
            Assert.Contains("newmtl dev_grid_orange", mtlText);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void GltfExporter_ExportsValidGltfJson_WithEmbeddedBase64()
    {
        EditorScene scene = CreateSampleScene();

        string gltfJson = GltfExporter.ExportToString(scene, embedBufferBase64: true);
        Assert.False(string.IsNullOrWhiteSpace(gltfJson));

        using var doc = JsonDocument.Parse(gltfJson);
        JsonElement root = doc.RootElement;

        // Verify glTF 2.0 Asset Header
        Assert.True(root.TryGetProperty("asset", out JsonElement asset));
        Assert.Equal("2.0", asset.GetProperty("version").GetString());
        Assert.Equal("Leed3D", asset.GetProperty("generator").GetString());

        // Verify Scene and Nodes
        Assert.Equal(0, root.GetProperty("scene").GetInt32());
        JsonElement scenes = root.GetProperty("scenes");
        Assert.True(scenes.GetArrayLength() > 0);
        JsonElement sceneNodes = scenes[0].GetProperty("nodes");
        Assert.Equal(2, sceneNodes.GetArrayLength());

        JsonElement nodes = root.GetProperty("nodes");
        Assert.Equal(2, nodes.GetArrayLength());
        Assert.Equal("MainCube", nodes[0].GetProperty("name").GetString());
        Assert.Equal(0, nodes[0].GetProperty("mesh").GetInt32());
        Assert.Equal("PentagonWall", nodes[1].GetProperty("name").GetString());
        Assert.Equal(1, nodes[1].GetProperty("mesh").GetInt32());

        // Verify Meshes and Primitives
        JsonElement meshes = root.GetProperty("meshes");
        Assert.Equal(2, meshes.GetArrayLength());
        for (int m = 0; m < meshes.GetArrayLength(); m++)
        {
            JsonElement meshElem = meshes[m];
            JsonElement prims = meshElem.GetProperty("primitives");
            Assert.True(prims.GetArrayLength() > 0);

            for (int p = 0; p < prims.GetArrayLength(); p++)
            {
                JsonElement prim = prims[p];
                JsonElement attrs = prim.GetProperty("attributes");
                Assert.True(attrs.TryGetProperty("POSITION", out _));
                Assert.True(attrs.TryGetProperty("NORMAL", out _));
                Assert.True(attrs.TryGetProperty("TEXCOORD_0", out _));
                Assert.True(prim.TryGetProperty("indices", out _));
                Assert.True(prim.TryGetProperty("material", out _));
                Assert.Equal(4, prim.GetProperty("mode").GetInt32()); // 4 = TRIANGLES
            }
        }

        // Verify Materials
        JsonElement materials = root.GetProperty("materials");
        Assert.Equal(2, materials.GetArrayLength());
        string[] matNames = materials.EnumerateArray().Select(m => m.GetProperty("name").GetString()!).ToArray();
        Assert.Contains("dev_grid_orange", matNames);
        Assert.Contains("dev_grid_blue", matNames);

        foreach (JsonElement mat in materials.EnumerateArray())
        {
            JsonElement pbr = mat.GetProperty("pbrMetallicRoughness");
            Assert.Equal(4, pbr.GetProperty("baseColorFactor").GetArrayLength());
            Assert.Equal(0.0f, pbr.GetProperty("metallicFactor").GetSingle());
            Assert.Equal(0.8f, pbr.GetProperty("roughnessFactor").GetSingle());
        }

        // Verify Accessors and Bounds
        JsonElement accessors = root.GetProperty("accessors");
        Assert.True(accessors.GetArrayLength() > 0);

        foreach (JsonElement acc in accessors.EnumerateArray())
        {
            int componentType = acc.GetProperty("componentType").GetInt32();
            string type = acc.GetProperty("type").GetString()!;
            int count = acc.GetProperty("count").GetInt32();
            Assert.True(count > 0);

            if (type == "VEC3")
            {
                Assert.Equal(5126, componentType); // FLOAT
                Assert.True(acc.TryGetProperty("min", out JsonElement minElem));
                Assert.True(acc.TryGetProperty("max", out JsonElement maxElem));
                Assert.Equal(3, minElem.GetArrayLength());
                Assert.Equal(3, maxElem.GetArrayLength());

                float minX = minElem[0].GetSingle();
                float maxX = maxElem[0].GetSingle();
                Assert.True(minX <= maxX);
            }
            else if (type == "VEC2")
            {
                Assert.Equal(5126, componentType); // FLOAT
                Assert.True(acc.TryGetProperty("min", out JsonElement minElem));
                Assert.True(acc.TryGetProperty("max", out JsonElement maxElem));
                Assert.Equal(2, minElem.GetArrayLength());
                Assert.Equal(2, maxElem.GetArrayLength());
            }
            else if (type == "SCALAR")
            {
                Assert.Equal(5125, componentType); // UNSIGNED_INT (uint32)
                Assert.True(acc.TryGetProperty("min", out JsonElement minElem));
                Assert.True(acc.TryGetProperty("max", out JsonElement maxElem));
                Assert.Equal(1, minElem.GetArrayLength());
                Assert.Equal(1, maxElem.GetArrayLength());
            }
        }

        // Verify BufferViews alignment
        JsonElement bufferViews = root.GetProperty("bufferViews");
        foreach (JsonElement bv in bufferViews.EnumerateArray())
        {
            int byteOffset = bv.GetProperty("byteOffset").GetInt32();
            int byteLength = bv.GetProperty("byteLength").GetInt32();
            Assert.Equal(0, byteOffset % 4); // Must be 4-byte aligned
            Assert.True(byteLength > 0);
        }

        // Verify Embedded Base64 Buffer
        JsonElement buffers = root.GetProperty("buffers");
        Assert.Equal(1, buffers.GetArrayLength());
        JsonElement buffer = buffers[0];
        int byteLengthReported = buffer.GetProperty("byteLength").GetInt32();
        string uri = buffer.GetProperty("uri").GetString()!;

        Assert.StartsWith("data:application/octet-stream;base64,", uri);
        string base64Payload = uri["data:application/octet-stream;base64,".Length..];
        byte[] decodedBytes = Convert.FromBase64String(base64Payload);
        Assert.Equal(byteLengthReported, decodedBytes.Length);
    }

    [Fact]
    public void GltfExporter_ExportsValidSidecarBinFile()
    {
        EditorScene scene = CreateSampleScene();
        string tempDir = Path.Combine(Path.GetTempPath(), $"leed_gltf_test_{Guid.NewGuid():N}");
        string gltfPath = Path.Combine(tempDir, "my_level.gltf");
        string binPath = Path.Combine(tempDir, "my_level.bin");

        try
        {
            GltfExporter.Export(scene, gltfPath, embedBufferBase64: false);

            Assert.True(File.Exists(gltfPath));
            Assert.True(File.Exists(binPath));

            string gltfJson = File.ReadAllText(gltfPath);
            using var doc = JsonDocument.Parse(gltfJson);
            JsonElement buffers = doc.RootElement.GetProperty("buffers");
            Assert.Equal(1, buffers.GetArrayLength());

            string uri = buffers[0].GetProperty("uri").GetString()!;
            Assert.Equal("my_level.bin", uri);

            int reportedByteLength = buffers[0].GetProperty("byteLength").GetInt32();
            long actualFileSize = new FileInfo(binPath).Length;
            Assert.Equal(reportedByteLength, actualFileSize);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void GltfExporter_RightHandedConversion_FlipsCoordinatesAndWinding()
    {
        var scene = new EditorScene("RightHandedGltfTest");
        var mesh = new EditableMesh();
        int v0 = mesh.AddVertex(new Vector3(0f, 0f, 10f));
        int v1 = mesh.AddVertex(new Vector3(2f, 0f, 10f));
        int v2 = mesh.AddVertex(new Vector3(2f, 2f, 10f));
        int v3 = mesh.AddVertex(new Vector3(0f, 2f, 10f));
        mesh.AddFace([v0, v1, v2, v3], "test_mat");
        scene.AddMeshObject("Wall", mesh, Vector3.Zero);

        string gltfJson = GltfExporter.ExportToString(scene, embedBufferBase64: true);
        using var doc = JsonDocument.Parse(gltfJson);

        // Find position accessor (VEC3)
        JsonElement accessors = doc.RootElement.GetProperty("accessors");
        JsonElement posAccessor = accessors.EnumerateArray().First(a => a.GetProperty("type").GetString() == "VEC3");

        // In editor space, Z was +10.0. In Right-Handed glTF, Z must be negated to -10.0.
        float minZ = posAccessor.GetProperty("min")[2].GetSingle();
        float maxZ = posAccessor.GetProperty("max")[2].GetSingle();

        Assert.Equal(-10.0f, minZ, 3);
        Assert.Equal(-10.0f, maxZ, 3);
    }

    [Fact]
    public void ExportersAndLevelBaker_DeduplicateSharedVerticesWithinMaterialSubmeshes()
    {
        EditorScene scene = CreateSampleScene();

        // 1. LevelBaker deduplication:
        // MainCube (6 quads = 12 tris) has 24 unique (Position, Normal, UV) vertices instead of 36.
        // PentagonWall (5-gon = 3 tris) has 5 unique (Position, Normal, UV) vertices instead of 9.
        BakedRuntimeLayerData baked = LevelBaker.Bake(scene, rightHanded: true);
        BakedSubmeshDto cubeSubmesh = baked.RenderSubmeshes.Single(s => s.MaterialId == "dev_grid_orange");
        BakedSubmeshDto pentSubmesh = baked.RenderSubmeshes.Single(s => s.MaterialId == "dev_grid_blue");

        Assert.Equal(24, cubeSubmesh.Positions.Count);
        Assert.Equal(36, cubeSubmesh.Indices.Count);
        Assert.Equal(5, pentSubmesh.Positions.Count);
        Assert.Equal(9, pentSubmesh.Indices.Count);

        // 2. ObjExporter deduplication:
        // Total 'v ' lines = 24 (MainCube) + 5 (PentagonWall) = 29 (down from 45)
        (string objContent, _) = ObjExporter.ExportToString(scene);
        int objVertexLines = objContent.Split('\n').Count(line => line.StartsWith("v ", StringComparison.Ordinal));
        Assert.Equal(29, objVertexLines);

        // 3. GltfExporter deduplication:
        string gltfJson = GltfExporter.ExportToString(scene);
        using var doc = JsonDocument.Parse(gltfJson);
        JsonElement accessors = doc.RootElement.GetProperty("accessors");
        // Accessor 0 = MainCube POSITION (count 24), Accessor 3 = MainCube INDICES (count 36)
        // Accessor 4 = PentagonWall POSITION (count 5), Accessor 7 = PentagonWall INDICES (count 9)
        Assert.Equal(24, accessors[0].GetProperty("count").GetInt32());
        Assert.Equal(36, accessors[3].GetProperty("count").GetInt32());
        Assert.Equal(5, accessors[4].GetProperty("count").GetInt32());
        Assert.Equal(9, accessors[7].GetProperty("count").GetInt32());
    }

    [Fact]
    public void GltfExporter_SupportsOnlyObjectIds_NodeTrsTransforms_MetadataExtras_AndCustomTextures()
    {
        EditorScene scene = CreateSampleScene();
        SceneObject cube = scene.Objects.First(o => o.Name == "MainCube");
        cube.Transform.Position = new Vector3(3f, 4f, 5f);
        cube.Transform.RotationEulerDegrees = new Vector3(0f, 90f, 0f);
        cube.Transform.Scale = new Vector3(2f, 2f, 2f);
        cube.Metadata.SetString("surface", "metal");
        cube.Metadata.AddTag("climbable");

        var spawn = scene.CreateEntity("player_spawn", "hero_start", new Vector3(1f, 0f, -2f));
        spawn.Properties.SetInt("team", 1);

        var customTextures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["dev_grid_orange"] = "textures/orange_wall.png"
        };

        // 1. Export only MainCube with custom texture map
        string filteredJson = GltfExporter.ExportToString(
            scene,
            embedBufferBase64: true,
            onlyObjectIds: [cube.Id],
            preserveNodeTransforms: true,
            customTexturePaths: customTextures);

        using (var filteredDoc = JsonDocument.Parse(filteredJson))
        {
            JsonElement root = filteredDoc.RootElement;
            JsonElement nodes = root.GetProperty("nodes");
            Assert.Equal(1, nodes.GetArrayLength());

            JsonElement cubeNode = nodes[0];
            Assert.Equal("MainCube", cubeNode.GetProperty("name").GetString());

            // Right-handed translation: (3, 4, -5)
            JsonElement trans = cubeNode.GetProperty("translation");
            Assert.Equal(3f, trans[0].GetSingle(), 3);
            Assert.Equal(4f, trans[1].GetSingle(), 3);
            Assert.Equal(-5f, trans[2].GetSingle(), 3);

            JsonElement scale = cubeNode.GetProperty("scale");
            Assert.Equal(2f, scale[0].GetSingle(), 3);

            // Node extras metadata
            JsonElement extras = cubeNode.GetProperty("extras");
            Assert.Equal("metal", extras.GetProperty("surface").GetString());
            Assert.Equal("climbable", extras.GetProperty("tags")[0].GetString());

            // Custom texture reference
            Assert.Equal("orange_wall.png", root.GetProperty("images")[0].GetProperty("uri").GetString());
            Assert.Equal(0, root.GetProperty("materials")[0].GetProperty("pbrMetallicRoughness").GetProperty("baseColorTexture").GetProperty("index").GetInt32());
        }

        // 2. Also verify ObjExporter emits map_Kd when customTexturePaths is provided
        (_, string mtlContent) = ObjExporter.ExportToString(
            scene,
            onlyObjectIds: [cube.Id],
            customTexturePaths: customTextures);
        Assert.Contains("map_Kd orange_wall.png", mtlContent);

        // 3. Full scene glTF export includes LevelEntity node with className in extras
        string fullJson = GltfExporter.ExportToString(scene);
        using (var fullDoc = JsonDocument.Parse(fullJson))
        {
            JsonElement nodes = fullDoc.RootElement.GetProperty("nodes");
            Assert.Equal(3, nodes.GetArrayLength()); // MainCube, PentagonWall, hero_start
            JsonElement entNode = nodes[2];
            Assert.Equal("hero_start", entNode.GetProperty("name").GetString());
            Assert.Equal("player_spawn", entNode.GetProperty("extras").GetProperty("className").GetString());
            Assert.Equal(1, entNode.GetProperty("extras").GetProperty("team").GetInt32());
        }
    }
}
