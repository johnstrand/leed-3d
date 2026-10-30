using System.Numerics;
using System.Text;
using System.Text.Json;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Texturing;

namespace Leed3D.Kernel.Export;

/// <summary>
/// Zero-dependency Khronos glTF 2.0 standard mesh exporter.
/// Packs geometry into a single binary buffer (embedded base64 or external .bin file)
/// with PBR metallic-roughness materials and right-handed OpenGL/glTF coordinates.
/// </summary>
public static class GltfExporter
{
    private const int TargetArrayBuffer = 34962;
    private const int TargetElementArrayBuffer = 34963;
    private const int ComponentTypeFloat = 5126;
    private const int ComponentTypeUnsignedInt = 5125;

    /// <summary>
    /// Exports the specified <paramref name="scene"/> to a glTF 2.0 file (<c>.gltf</c>).
    /// </summary>
    /// <param name="scene">The editor scene to export.</param>
    /// <param name="gltfFilePath">The output path for the <c>.gltf</c> JSON file.</param>
    /// <param name="embedBufferBase64">
    /// When <c>true</c>, packs all geometry into an inline base64 URI inside the <c>.gltf</c> file.
    /// When <c>false</c>, writes a companion <c>.bin</c> file alongside the <c>.gltf</c> file.
    /// </param>
    public static void Export(EditorScene scene, string gltfFilePath, bool embedBufferBase64 = true)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentException.ThrowIfNullOrWhiteSpace(gltfFilePath);

        string? dir = Path.GetDirectoryName(gltfFilePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string binFileName = Path.GetFileNameWithoutExtension(gltfFilePath) + ".bin";
        (string gltfJson, byte[] binaryBuffer) = ExportInternal(scene, embedBufferBase64, binFileName);

        File.WriteAllText(gltfFilePath, gltfJson, Encoding.UTF8);

        if (!embedBufferBase64 && binaryBuffer.Length > 0)
        {
            string binFilePath = Path.Combine(dir ?? string.Empty, binFileName);
            File.WriteAllBytes(binFilePath, binaryBuffer);
        }
    }

    /// <summary>
    /// Exports the specified <paramref name="scene"/> to an in-memory glTF 2.0 JSON string.
    /// </summary>
    public static string ExportToString(EditorScene scene, bool embedBufferBase64 = true)
    {
        ArgumentNullException.ThrowIfNull(scene);
        (string gltfJson, _) = ExportInternal(scene, embedBufferBase64, "buffer.bin");
        return gltfJson;
    }

    /// <summary>
    /// Exports the specified <paramref name="scene"/> to glTF 2.0 JSON and raw binary buffer bytes.
    /// </summary>
    public static (string GltfJson, byte[] BinaryBuffer) ExportToMemory(EditorScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return ExportInternal(scene, embedBufferBase64: false, "buffer.bin");
    }

    private static (string GltfJson, byte[] BinaryBuffer) ExportInternal(
        EditorScene scene,
        bool embedBufferBase64,
        string binFileName)
    {
        using var bufferStream = new MemoryStream();
        using var writer = new BinaryWriter(bufferStream);

        var bufferViews = new List<Dictionary<string, object>>();
        var accessors = new List<Dictionary<string, object>>();
        var materials = new List<Dictionary<string, object>>();
        var materialMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var meshes = new List<Dictionary<string, object>>();
        var nodes = new List<Dictionary<string, object>>();
        var sceneNodes = new List<int>();

        for (int objIdx = 0; objIdx < scene.Objects.Count; objIdx++)
        {
            SceneObject obj = scene.Objects[objIdx];
            if (!obj.Visible || obj.Mesh is null || obj.Mesh.FaceCount == 0)
            {
                continue;
            }

            EditableMesh mesh = obj.Mesh;

            var primitives = new List<Dictionary<string, object>>();

            // Group faces by MaterialId
            var facesByMaterial = mesh.Faces
                .Where(f => f.VertexIds.Count >= 3)
                .GroupBy(f => string.IsNullOrWhiteSpace(f.MaterialId) ? PolygonFace.DefaultMaterialId : f.MaterialId);

            foreach (var matGroup in facesByMaterial)
            {
                string matId = matGroup.Key;

                // Ensure material is registered in glTF materials array
                if (!materialMap.TryGetValue(matId, out int materialIndex))
                {
                    materialIndex = materials.Count;
                    materialMap[matId] = materialIndex;

                    (float r, float g, float b) = ExportMaterialUtils.GetMaterialRgb(matId);

                    materials.Add(new Dictionary<string, object>
                    {
                        ["name"] = matId,
                        ["pbrMetallicRoughness"] = new Dictionary<string, object>
                        {
                            ["baseColorFactor"] = new[] { r, g, b, 1.0f },
                            ["metallicFactor"] = 0.0f,
                            ["roughnessFactor"] = 0.8f
                        },
                        ["doubleSided"] = false
                    });
                }

                var positions = new List<Vector3>();
                var normals = new List<Vector3>();
                var uvs = new List<Vector2>();
                var indices = new List<uint>();

                foreach (PolygonFace face in matGroup)
                {
                    IReadOnlyList<(int V0, int V1, int V2)> tris = PolygonTriangulator.TriangulateFace(face, mesh);
                    if (tris.Count == 0)
                    {
                        continue;
                    }

                    Vector3 localNormal = face.ComputeNormal(mesh);
                    Vector3 worldNormal = Vector3.Normalize(obj.Transform.TransformDirectionToWorld(localNormal));
                    IReadOnlyList<Vector2> faceUvs = UvProjectionEngine.ComputeFaceUvs(face, mesh, obj.Transform);

                    for (int tIdx = 0; tIdx < tris.Count; tIdx++)
                    {
                        var (v0Id, v1Id, v2Id) = tris[tIdx];

                        MeshVertex? mv0 = mesh.GetVertex(v0Id);
                        MeshVertex? mv1 = mesh.GetVertex(v1Id);
                        MeshVertex? mv2 = mesh.GetVertex(v2Id);
                        if (mv0 is null || mv1 is null || mv2 is null)
                        {
                            continue;
                        }

                        int idx0 = face.VertexIds.IndexOf(v0Id);
                        int idx1 = face.VertexIds.IndexOf(v1Id);
                        int idx2 = face.VertexIds.IndexOf(v2Id);

                        Vector2 uv0 = idx0 >= 0 && idx0 < faceUvs.Count ? faceUvs[idx0] : Vector2.Zero;
                        Vector2 uv1 = idx1 >= 0 && idx1 < faceUvs.Count ? faceUvs[idx1] : Vector2.Zero;
                        Vector2 uv2 = idx2 >= 0 && idx2 < faceUvs.Count ? faceUvs[idx2] : Vector2.Zero;

                        Vector3 p0 = obj.Transform.TransformPointToWorld(mv0.Position);
                        Vector3 p1 = obj.Transform.TransformPointToWorld(mv1.Position);
                        Vector3 p2 = obj.Transform.TransformPointToWorld(mv2.Position);

                        Vector3 p0Rh = CoordSpace.ToRightHanded(p0);
                        Vector3 p1Rh = CoordSpace.ToRightHanded(p1);
                        Vector3 p2Rh = CoordSpace.ToRightHanded(p2);
                        Vector3 nRh = CoordSpace.ToRightHanded(worldNormal);

                        // Winding flip for Right-Handed glTF: (p0, p2, p1), (uv0, uv2, uv1)
                        uint baseIdx = (uint)positions.Count;

                        positions.Add(p0Rh);
                        positions.Add(p2Rh);
                        positions.Add(p1Rh);

                        normals.Add(nRh);
                        normals.Add(nRh);
                        normals.Add(nRh);

                        uvs.Add(uv0);
                        uvs.Add(uv2);
                        uvs.Add(uv1);

                        indices.Add(baseIdx);
                        indices.Add(baseIdx + 1);
                        indices.Add(baseIdx + 2);
                    }
                }

                if (positions.Count == 0 || indices.Count == 0)
                {
                    continue;
                }

                // 1. Pack POSITION
                AlignBuffer(writer, bufferStream);
                int posByteOffset = (int)bufferStream.Position;
                Vector3 minPos = new(float.MaxValue, float.MaxValue, float.MaxValue);
                Vector3 maxPos = new(float.MinValue, float.MinValue, float.MinValue);

                for (int i = 0; i < positions.Count; i++)
                {
                    Vector3 p = positions[i];
                    writer.Write(p.X);
                    writer.Write(p.Y);
                    writer.Write(p.Z);
                    minPos = Vector3.Min(minPos, p);
                    maxPos = Vector3.Max(maxPos, p);
                }

                int posByteLength = (int)bufferStream.Position - posByteOffset;
                int posViewIdx = bufferViews.Count;
                bufferViews.Add(new Dictionary<string, object>
                {
                    ["buffer"] = 0,
                    ["byteOffset"] = posByteOffset,
                    ["byteLength"] = posByteLength,
                    ["target"] = TargetArrayBuffer
                });

                int posAccessorIdx = accessors.Count;
                accessors.Add(new Dictionary<string, object>
                {
                    ["bufferView"] = posViewIdx,
                    ["byteOffset"] = 0,
                    ["componentType"] = ComponentTypeFloat,
                    ["count"] = positions.Count,
                    ["type"] = "VEC3",
                    ["min"] = new[] { minPos.X, minPos.Y, minPos.Z },
                    ["max"] = new[] { maxPos.X, maxPos.Y, maxPos.Z }
                });

                // 2. Pack NORMAL
                AlignBuffer(writer, bufferStream);
                int normByteOffset = (int)bufferStream.Position;
                Vector3 minNorm = new(float.MaxValue, float.MaxValue, float.MaxValue);
                Vector3 maxNorm = new(float.MinValue, float.MinValue, float.MinValue);

                for (int i = 0; i < normals.Count; i++)
                {
                    Vector3 n = normals[i];
                    writer.Write(n.X);
                    writer.Write(n.Y);
                    writer.Write(n.Z);
                    minNorm = Vector3.Min(minNorm, n);
                    maxNorm = Vector3.Max(maxNorm, n);
                }

                int normByteLength = (int)bufferStream.Position - normByteOffset;
                int normViewIdx = bufferViews.Count;
                bufferViews.Add(new Dictionary<string, object>
                {
                    ["buffer"] = 0,
                    ["byteOffset"] = normByteOffset,
                    ["byteLength"] = normByteLength,
                    ["target"] = TargetArrayBuffer
                });

                int normAccessorIdx = accessors.Count;
                accessors.Add(new Dictionary<string, object>
                {
                    ["bufferView"] = normViewIdx,
                    ["byteOffset"] = 0,
                    ["componentType"] = ComponentTypeFloat,
                    ["count"] = normals.Count,
                    ["type"] = "VEC3",
                    ["min"] = new[] { minNorm.X, minNorm.Y, minNorm.Z },
                    ["max"] = new[] { maxNorm.X, maxNorm.Y, maxNorm.Z }
                });

                // 3. Pack TEXCOORD_0
                AlignBuffer(writer, bufferStream);
                int uvByteOffset = (int)bufferStream.Position;
                Vector2 minUv = new(float.MaxValue, float.MaxValue);
                Vector2 maxUv = new(float.MinValue, float.MinValue);

                for (int i = 0; i < uvs.Count; i++)
                {
                    Vector2 uv = uvs[i];
                    writer.Write(uv.X);
                    writer.Write(uv.Y);
                    minUv = Vector2.Min(minUv, uv);
                    maxUv = Vector2.Max(maxUv, uv);
                }

                int uvByteLength = (int)bufferStream.Position - uvByteOffset;
                int uvViewIdx = bufferViews.Count;
                bufferViews.Add(new Dictionary<string, object>
                {
                    ["buffer"] = 0,
                    ["byteOffset"] = uvByteOffset,
                    ["byteLength"] = uvByteLength,
                    ["target"] = TargetArrayBuffer
                });

                int uvAccessorIdx = accessors.Count;
                accessors.Add(new Dictionary<string, object>
                {
                    ["bufferView"] = uvViewIdx,
                    ["byteOffset"] = 0,
                    ["componentType"] = ComponentTypeFloat,
                    ["count"] = uvs.Count,
                    ["type"] = "VEC2",
                    ["min"] = new[] { minUv.X, minUv.Y },
                    ["max"] = new[] { maxUv.X, maxUv.Y }
                });

                // 4. Pack INDICES
                AlignBuffer(writer, bufferStream);
                int idxByteOffset = (int)bufferStream.Position;
                uint minIdx = uint.MaxValue;
                uint maxIdx = uint.MinValue;

                for (int i = 0; i < indices.Count; i++)
                {
                    uint idx = indices[i];
                    writer.Write(idx);
                    if (idx < minIdx) minIdx = idx;
                    if (idx > maxIdx) maxIdx = idx;
                }

                int idxByteLength = (int)bufferStream.Position - idxByteOffset;
                int idxViewIdx = bufferViews.Count;
                bufferViews.Add(new Dictionary<string, object>
                {
                    ["buffer"] = 0,
                    ["byteOffset"] = idxByteOffset,
                    ["byteLength"] = idxByteLength,
                    ["target"] = TargetElementArrayBuffer
                });

                int idxAccessorIdx = accessors.Count;
                accessors.Add(new Dictionary<string, object>
                {
                    ["bufferView"] = idxViewIdx,
                    ["byteOffset"] = 0,
                    ["componentType"] = ComponentTypeUnsignedInt,
                    ["count"] = indices.Count,
                    ["type"] = "SCALAR",
                    ["min"] = new[] { minIdx },
                    ["max"] = new[] { maxIdx }
                });

                primitives.Add(new Dictionary<string, object>
                {
                    ["attributes"] = new Dictionary<string, int>
                    {
                        ["POSITION"] = posAccessorIdx,
                        ["NORMAL"] = normAccessorIdx,
                        ["TEXCOORD_0"] = uvAccessorIdx
                    },
                    ["indices"] = idxAccessorIdx,
                    ["material"] = materialIndex,
                    ["mode"] = 4
                });
            }

            if (primitives.Count > 0)
            {
                int meshIndex = meshes.Count;
                meshes.Add(new Dictionary<string, object>
                {
                    ["name"] = obj.Name,
                    ["primitives"] = primitives
                });

                int nodeIndex = nodes.Count;
                nodes.Add(new Dictionary<string, object>
                {
                    ["name"] = obj.Name,
                    ["mesh"] = meshIndex
                });

                sceneNodes.Add(nodeIndex);
            }
        }

        byte[] binaryBuffer = bufferStream.ToArray();

        // 5. Construct buffer dictionary
        var bufferDict = new Dictionary<string, object>
        {
            ["byteLength"] = binaryBuffer.Length
        };

        if (embedBufferBase64)
        {
            bufferDict["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String(binaryBuffer);
        }
        else
        {
            bufferDict["uri"] = binFileName;
        }

        // 6. Build root glTF 2.0 object
        var gltfRoot = new Dictionary<string, object>
        {
            ["asset"] = new Dictionary<string, string>
            {
                ["version"] = "2.0",
                ["generator"] = "Leed3D"
            },
            ["scene"] = 0,
            ["scenes"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["name"] = string.IsNullOrWhiteSpace(scene.Name) ? "Scene" : scene.Name,
                    ["nodes"] = sceneNodes
                }
            },
            ["nodes"] = nodes,
            ["meshes"] = meshes,
            ["materials"] = materials,
            ["accessors"] = accessors,
            ["bufferViews"] = bufferViews,
            ["buffers"] = new[] { bufferDict }
        };

        var serializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        string json = JsonSerializer.Serialize(gltfRoot, serializerOptions);
        return (json, binaryBuffer);
    }

    private static void AlignBuffer(BinaryWriter writer, MemoryStream stream)
    {
        long pos = stream.Position;
        int remainder = (int)(pos % 4);
        if (remainder != 0)
        {
            int pad = 4 - remainder;
            for (int i = 0; i < pad; i++)
            {
                writer.Write((byte)0);
            }
        }
    }
}
