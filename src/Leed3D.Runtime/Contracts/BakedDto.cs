using System.Numerics;
using Leed3D.Runtime.Metadata;

namespace Leed3D.Runtime;

/// <summary>
/// Pre-triangulated world-space render submesh grouped by material ID, ready for zero-triangulation GPU buffer uploads.
/// </summary>
public sealed class BakedSubmeshDto
{
    public string MaterialId { get; set; } = string.Empty;
    public List<Vector3Dto> Positions { get; set; } = [];
    public List<Vector3Dto> Normals { get; set; } = [];
    public List<Vector2Dto> UVs { get; set; } = [];
    public List<int> Indices { get; set; } = [];
    public List<int> FaceSourceIds { get; set; } = [];

    public int VertexCount => Positions.Count;
    public int TriangleCount => Indices.Count / 3;

    public BakedSubmeshDto()
    {
    }

    public BakedSubmeshDto(string materialId)
    {
        MaterialId = materialId ?? string.Empty;
    }

    /// <summary>
    /// Returns a flat array of vertex positions <c>[x0, y0, z0, x1, y1, z1, ...]</c>
    /// suitable for OpenGL/Vulkan/DirectX vertex buffers or Raylib <c>Mesh</c>.
    /// </summary>
    public float[] GetPositionsFlat()
    {
        int count = Positions.Count;
        var arr = new float[count * 3];
        for (int i = 0; i < count; i++)
        {
            Vector3Dto p = Positions[i];
            int idx = i * 3;
            arr[idx] = p.X;
            arr[idx + 1] = p.Y;
            arr[idx + 2] = p.Z;
        }
        return arr;
    }

    /// <summary>
    /// Returns a flat array of vertex normals <c>[nx0, ny0, nz0, ...]</c>.
    /// </summary>
    public float[] GetNormalsFlat()
    {
        int count = Normals.Count;
        var arr = new float[count * 3];
        for (int i = 0; i < count; i++)
        {
            Vector3Dto n = Normals[i];
            int idx = i * 3;
            arr[idx] = n.X;
            arr[idx + 1] = n.Y;
            arr[idx + 2] = n.Z;
        }
        return arr;
    }

    /// <summary>
    /// Returns a flat array of texture coordinates <c>[u0, v0, u1, v1, ...]</c>.
    /// </summary>
    public float[] GetUVsFlat()
    {
        int count = UVs.Count;
        var arr = new float[count * 2];
        for (int i = 0; i < count; i++)
        {
            Vector2Dto uv = UVs[i];
            int idx = i * 2;
            arr[idx] = uv.X;
            arr[idx + 1] = uv.Y;
        }
        return arr;
    }

    /// <summary>
    /// Returns vertex positions as an array of <see cref="Vector3"/>.
    /// </summary>
    public Vector3[] GetPositions()
    {
        int count = Positions.Count;
        var arr = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            arr[i] = Positions[i];
        }
        return arr;
    }

    /// <summary>
    /// Returns vertex normals as an array of <see cref="Vector3"/>.
    /// </summary>
    public Vector3[] GetNormals()
    {
        int count = Normals.Count;
        var arr = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            arr[i] = Normals[i];
        }
        return arr;
    }

    /// <summary>
    /// Returns texture coordinates as an array of <see cref="Vector2"/>.
    /// </summary>
    public Vector2[] GetUVs()
    {
        int count = UVs.Count;
        var arr = new Vector2[count];
        for (int i = 0; i < count; i++)
        {
            arr[i] = UVs[i];
        }
        return arr;
    }

    /// <summary>
    /// Returns triangle indices as an array of <see cref="int"/>.
    /// </summary>
    public int[] GetIndices() => Indices.ToArray();

    /// <summary>
    /// Gets the original editor face ID that produced the triangle at <paramref name="triangleIndex"/>.
    /// </summary>
    public int GetFaceSourceIdForTriangle(int triangleIndex)
    {
        if (triangleIndex >= 0 && triangleIndex < FaceSourceIds.Count)
        {
            return FaceSourceIds[triangleIndex];
        }
        return 0;
    }
}

/// <summary>
/// Pre-filtered world-space collision triangle (with <c>collision=none</c> geometry omitted).
/// </summary>
public sealed class BakedCollisionTriangleDto
{
    public Vector3Dto V0 { get; set; } = Vector3Dto.Zero;
    public Vector3Dto V1 { get; set; } = Vector3Dto.Zero;
    public Vector3Dto V2 { get; set; } = Vector3Dto.Zero;
    public Vector3Dto Normal { get; set; } = Vector3Dto.UnitY;
    public string MaterialId { get; set; } = string.Empty;
    public ElementMetadata SurfaceMetadata { get; set; } = new();

    public Vector3 Vertex0 => V0;
    public Vector3 Vertex1 => V1;
    public Vector3 Vertex2 => V2;

    public BakedCollisionTriangleDto()
    {
    }

    public BakedCollisionTriangleDto(
        Vector3 v0,
        Vector3 v1,
        Vector3 v2,
        Vector3 normal,
        string materialId,
        ElementMetadata? surfaceMetadata = null)
    {
        V0 = v0;
        V1 = v1;
        V2 = v2;
        Normal = normal;
        MaterialId = materialId ?? string.Empty;
        SurfaceMetadata = surfaceMetadata ?? new ElementMetadata();
    }

    public BakedCollisionTriangleDto(
        Vector3Dto v0,
        Vector3Dto v1,
        Vector3Dto v2,
        Vector3Dto normal,
        string materialId,
        ElementMetadata? surfaceMetadata = null)
    {
        V0 = v0;
        V1 = v1;
        V2 = v2;
        Normal = normal;
        MaterialId = materialId ?? string.Empty;
        SurfaceMetadata = surfaceMetadata ?? new ElementMetadata();
    }
}
