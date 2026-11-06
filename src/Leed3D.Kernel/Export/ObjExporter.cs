using System.Globalization;
using System.Numerics;
using System.Text;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.IO;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Texturing;

namespace Leed3D.Kernel.Export;

/// <summary>
/// Zero-dependency Wavefront <c>.obj</c> and companion <c>.mtl</c> mesh exporter.
/// Automatically triangulates N-gon faces, deduplicates shared vertices, and converts
/// left-handed editor coordinates to right-handed OpenGL/glTF space.
/// </summary>
public static class ObjExporter
{
    /// <summary>
    /// Exports the specified <paramref name="scene"/> (or filtered objects) to an <c>.obj</c> file
    /// and a companion <c>.mtl</c> material file.
    /// </summary>
    /// <param name="scene">The editor scene to export.</param>
    /// <param name="objFilePath">The output path for the <c>.obj</c> file.</param>
    /// <param name="convertToRightHanded">Whether to convert coordinates to right-handed OpenGL space (+X Right, +Y Up, -Z Forward).</param>
    /// <param name="onlyObjectIds">Optional collection of scene object IDs to restrict export to.</param>
    /// <param name="customTexturePaths">Optional map of material ID to custom texture file path for <c>map_Kd</c>.</param>
    public static void Export(
        EditorScene scene,
        string objFilePath,
        bool convertToRightHanded = true,
        IReadOnlyCollection<int>? onlyObjectIds = null,
        IReadOnlyDictionary<string, string>? customTexturePaths = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentException.ThrowIfNullOrWhiteSpace(objFilePath);

        string? dir = Path.GetDirectoryName(objFilePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string mtlFilePath = Path.ChangeExtension(objFilePath, ".mtl");
        string mtlFileName = Path.GetFileName(mtlFilePath);

        (string objContent, string mtlContent) = ExportToString(
            scene,
            mtlFileName,
            convertToRightHanded,
            onlyObjectIds,
            customTexturePaths);

        File.WriteAllText(objFilePath, objContent, Encoding.UTF8);
        File.WriteAllText(mtlFilePath, mtlContent, Encoding.UTF8);
    }

    /// <summary>
    /// Exports the specified <paramref name="scene"/> to in-memory <c>.obj</c> and <c>.mtl</c> text content.
    /// </summary>
    public static (string ObjContent, string MtlContent) ExportToString(
        EditorScene scene,
        string mtlFileName = "materials.mtl",
        bool convertToRightHanded = true,
        IReadOnlyCollection<int>? onlyObjectIds = null,
        IReadOnlyDictionary<string, string>? customTexturePaths = null)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var sbObj = new StringBuilder();
        var sbMtl = new StringBuilder();

        sbObj.AppendLine("# Wavefront OBJ exported by Leed3D");
        if (!string.IsNullOrWhiteSpace(mtlFileName))
        {
            sbObj.AppendLine($"mtllib {mtlFileName}");
        }
        sbObj.AppendLine();

        var referencedMaterials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int vIndex = 1;
        int vtIndex = 1;
        int vnIndex = 1;

        for (int objIdx = 0; objIdx < scene.Objects.Count; objIdx++)
        {
            SceneObject obj = scene.Objects[objIdx];

            if (onlyObjectIds is not null && !onlyObjectIds.Contains(obj.Id))
            {
                continue;
            }

            if (onlyObjectIds is null && !obj.Visible)
            {
                continue;
            }

            if (obj.Mesh is null || obj.Mesh.FaceCount == 0)
            {
                continue;
            }

            EditableMesh mesh = obj.Mesh;
            string objName = SanitizeName(obj.Name);
            sbObj.AppendLine($"o {objName}");

            // Group faces by MaterialId
            var facesByMaterial = mesh.Faces
                .Where(f => f.VertexIds.Count >= 3)
                .GroupBy(f => string.IsNullOrWhiteSpace(f.MaterialId) ? PolygonFace.DefaultMaterialId : f.MaterialId);

            foreach (var matGroup in facesByMaterial)
            {
                string matId = matGroup.Key;
                referencedMaterials.Add(matId);
                sbObj.AppendLine($"usemtl {matId}");

                var vertLookup = new Dictionary<VertexDeduplicationKey, (int vIdx, int vtIdx, int vnIdx)>();
                var normLookup = new Dictionary<(int Nx, int Ny, int Nz), int>();

                foreach (PolygonFace face in matGroup)
                {
                    IReadOnlyList<FaceTriangle> tris = face.GetTrianglesWithLocalIndices(mesh);
                    if (tris.Count == 0)
                    {
                        continue;
                    }

                    Vector3 localNormal = face.ComputeNormal(mesh);
                    Vector3 worldNormal = Vector3.Normalize(obj.Transform.TransformDirectionToWorld(localNormal));
                    IReadOnlyList<Vector2> faceUvs = UvProjectionEngine.ComputeFaceUvs(face, mesh, obj.Transform);
                    Vector3 n = convertToRightHanded ? CoordSpace.ToRightHanded(worldNormal) : worldNormal;

                    (int Nx, int Ny, int Nz) normKey = (
                        (int)MathF.Round(n.X * 100000f),
                        (int)MathF.Round(n.Y * 100000f),
                        (int)MathF.Round(n.Z * 100000f));
                    if (!normLookup.TryGetValue(normKey, out int faceVnIdx))
                    {
                        WriteNormal(sbObj, n);
                        faceVnIdx = vnIndex++;
                        normLookup[normKey] = faceVnIdx;
                    }

                    for (int tIdx = 0; tIdx < tris.Count; tIdx++)
                    {
                        FaceTriangle tri = tris[tIdx];
                        MeshVertex? mv0 = mesh.GetVertex(tri.V0);
                        MeshVertex? mv1 = mesh.GetVertex(tri.V1);
                        MeshVertex? mv2 = mesh.GetVertex(tri.V2);
                        if (mv0 is null || mv1 is null || mv2 is null)
                        {
                            continue;
                        }

                        Vector2 uv0 = tri.I0 >= 0 && tri.I0 < faceUvs.Count ? faceUvs[tri.I0] : Vector2.Zero;
                        Vector2 uv1 = tri.I1 >= 0 && tri.I1 < faceUvs.Count ? faceUvs[tri.I1] : Vector2.Zero;
                        Vector2 uv2 = tri.I2 >= 0 && tri.I2 < faceUvs.Count ? faceUvs[tri.I2] : Vector2.Zero;

                        Vector3 p0 = obj.Transform.TransformPointToWorld(mv0.Position);
                        Vector3 p1 = obj.Transform.TransformPointToWorld(mv1.Position);
                        Vector3 p2 = obj.Transform.TransformPointToWorld(mv2.Position);

                        if (convertToRightHanded)
                        {
                            p0 = CoordSpace.ToRightHanded(p0);
                            p1 = CoordSpace.ToRightHanded(p1);
                            p2 = CoordSpace.ToRightHanded(p2);

                            // Winding flip for right-handed space: (p0, p2, p1), (uv0, uv2, uv1)
                            (int v0, int vt0, int vn0) = GetOrWriteObjVertex(sbObj, vertLookup, p0, n, uv0, faceVnIdx, ref vIndex, ref vtIndex);
                            (int v2, int vt2, int vn2) = GetOrWriteObjVertex(sbObj, vertLookup, p2, n, uv2, faceVnIdx, ref vIndex, ref vtIndex);
                            (int v1, int vt1, int vn1) = GetOrWriteObjVertex(sbObj, vertLookup, p1, n, uv1, faceVnIdx, ref vIndex, ref vtIndex);

                            sbObj.AppendLine(CultureInfo.InvariantCulture, $"f {v0}/{vt0}/{vn0} {v2}/{vt2}/{vn2} {v1}/{vt1}/{vn1}");
                        }
                        else
                        {
                            (int v0, int vt0, int vn0) = GetOrWriteObjVertex(sbObj, vertLookup, p0, n, uv0, faceVnIdx, ref vIndex, ref vtIndex);
                            (int v1, int vt1, int vn1) = GetOrWriteObjVertex(sbObj, vertLookup, p1, n, uv1, faceVnIdx, ref vIndex, ref vtIndex);
                            (int v2, int vt2, int vn2) = GetOrWriteObjVertex(sbObj, vertLookup, p2, n, uv2, faceVnIdx, ref vIndex, ref vtIndex);

                            sbObj.AppendLine(CultureInfo.InvariantCulture, $"f {v0}/{vt0}/{vn0} {v1}/{vt1}/{vn1} {v2}/{vt2}/{vn2}");
                        }
                    }
                }
            }

            sbObj.AppendLine();
        }

        // Generate .mtl material definitions
        sbMtl.AppendLine("# Material Library exported by Leed3D");
        sbMtl.AppendLine();

        foreach (string matId in referencedMaterials.OrderBy(m => m, StringComparer.OrdinalIgnoreCase))
        {
            (float r, float g, float b) = ExportMaterialUtils.GetMaterialRgb(matId);

            sbMtl.AppendLine($"newmtl {matId}");
            sbMtl.AppendLine("Ka 1.000000 1.000000 1.000000");
            sbMtl.AppendLine(CultureInfo.InvariantCulture, $"Kd {r:F6} {g:F6} {b:F6}");
            sbMtl.AppendLine("Ks 0.000000 0.000000 0.000000");
            sbMtl.AppendLine("d 1.000000");
            sbMtl.AppendLine("illum 2");
            if (customTexturePaths is not null &&
                customTexturePaths.TryGetValue(matId, out string? texPath) &&
                !string.IsNullOrWhiteSpace(texPath))
            {
                sbMtl.AppendLine($"map_Kd {Path.GetFileName(texPath)}");
            }
            sbMtl.AppendLine();
        }

        return (sbObj.ToString(), sbMtl.ToString());
    }

    private static (int vIdx, int vtIdx, int vnIdx) GetOrWriteObjVertex(
        StringBuilder sb,
        Dictionary<VertexDeduplicationKey, (int vIdx, int vtIdx, int vnIdx)> lookup,
        Vector3 p,
        Vector3 n,
        Vector2 uv,
        int vnIdx,
        ref int vIndex,
        ref int vtIndex)
    {
        var key = VertexDeduplicationKey.Create(p, n, uv);
        if (lookup.TryGetValue(key, out var existing))
        {
            return existing;
        }

        WriteVertex(sb, p);
        WriteTexCoord(sb, uv);
        var created = (vIndex++, vtIndex++, vnIdx);
        lookup[key] = created;
        return created;
    }

    private static void WriteVertex(StringBuilder sb, Vector3 p)
    {
        sb.AppendLine(CultureInfo.InvariantCulture, $"v {p.X:F6} {p.Y:F6} {p.Z:F6}");
    }

    private static void WriteTexCoord(StringBuilder sb, Vector2 uv)
    {
        sb.AppendLine(CultureInfo.InvariantCulture, $"vt {uv.X:F6} {uv.Y:F6}");
    }

    private static void WriteNormal(StringBuilder sb, Vector3 n)
    {
        sb.AppendLine(CultureInfo.InvariantCulture, $"vn {n.X:F6} {n.Y:F6} {n.Z:F6}");
    }

    private static string SanitizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Object";
        }

        return name.Trim().Replace(' ', '_');
    }
}
