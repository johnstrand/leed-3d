using System.Globalization;
using System.Numerics;
using System.Text;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Texturing;

namespace Leed3D.Kernel.Export;

/// <summary>
/// Zero-dependency Wavefront <c>.obj</c> and companion <c>.mtl</c> mesh exporter.
/// Automatically triangulates N-gon faces and converts left-handed editor coordinates
/// to right-handed OpenGL/glTF space.
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
    public static void Export(
        EditorScene scene,
        string objFilePath,
        bool convertToRightHanded = true,
        IReadOnlyCollection<int>? onlyObjectIds = null)
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
            onlyObjectIds);

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
        IReadOnlyCollection<int>? onlyObjectIds = null)
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
                        Vector3 n = worldNormal;

                        if (convertToRightHanded)
                        {
                            p0 = CoordSpace.ToRightHanded(p0);
                            p1 = CoordSpace.ToRightHanded(p1);
                            p2 = CoordSpace.ToRightHanded(p2);
                            n = CoordSpace.ToRightHanded(n);

                            // Triangle winding flip for right-handed space: (p0, p2, p1), (uv0, uv2, uv1)
                            WriteVertex(sbObj, p0);
                            WriteVertex(sbObj, p2);
                            WriteVertex(sbObj, p1);

                            WriteTexCoord(sbObj, uv0);
                            WriteTexCoord(sbObj, uv2);
                            WriteTexCoord(sbObj, uv1);

                            WriteNormal(sbObj, n);

                            sbObj.AppendLine(CultureInfo.InvariantCulture, $"f {vIndex}/{vtIndex}/{vnIndex} {vIndex + 1}/{vtIndex + 1}/{vnIndex} {vIndex + 2}/{vtIndex + 2}/{vnIndex}");

                            vIndex += 3;
                            vtIndex += 3;
                            vnIndex += 1;
                        }
                        else
                        {
                            WriteVertex(sbObj, p0);
                            WriteVertex(sbObj, p1);
                            WriteVertex(sbObj, p2);

                            WriteTexCoord(sbObj, uv0);
                            WriteTexCoord(sbObj, uv1);
                            WriteTexCoord(sbObj, uv2);

                            WriteNormal(sbObj, n);

                            sbObj.AppendLine(CultureInfo.InvariantCulture, $"f {vIndex}/{vtIndex}/{vnIndex} {vIndex + 1}/{vtIndex + 1}/{vnIndex} {vIndex + 2}/{vtIndex + 2}/{vnIndex}");

                            vIndex += 3;
                            vtIndex += 3;
                            vnIndex += 1;
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
            sbMtl.AppendLine();
        }

        return (sbObj.ToString(), sbMtl.ToString());
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
