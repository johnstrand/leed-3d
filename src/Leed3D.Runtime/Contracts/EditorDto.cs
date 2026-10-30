using System.Numerics;
using Leed3D.Runtime.Metadata;

namespace Leed3D.Runtime;

/// <summary>
/// Serializable DTO for an editable mesh vertex with a stable ID, 3D position, and granular metadata.
/// </summary>
public sealed class EditorVertexDto
{
    public int Id { get; set; }
    public Vector3Dto Position { get; set; } = Vector3Dto.Zero;
    public ElementMetadata Metadata { get; set; } = new();

    public EditorVertexDto()
    {
    }

    public EditorVertexDto(int id, Vector3 position, ElementMetadata? metadata = null)
    {
        Id = id;
        Position = position;
        Metadata = metadata ?? new ElementMetadata();
    }
}

/// <summary>
/// Serializable DTO for an ordered N-gon polygon face with material ID, UV mapping, and metadata.
/// </summary>
public sealed class PolygonFaceDto
{
    public int Id { get; set; }
    public List<int> VertexIds { get; set; } = [];
    public string MaterialId { get; set; } = "dev_grid_gray";
    public FaceUvMappingDto UvMapping { get; set; } = new();
    public ElementMetadata Metadata { get; set; } = new();

    public PolygonFaceDto()
    {
    }

    public PolygonFaceDto(
        int id,
        IEnumerable<int> vertexIds,
        string materialId = "dev_grid_gray",
        FaceUvMappingDto? uvMapping = null,
        ElementMetadata? metadata = null)
    {
        Id = id;
        VertexIds = new List<int>(vertexIds);
        MaterialId = string.IsNullOrWhiteSpace(materialId) ? "dev_grid_gray" : materialId;
        UvMapping = uvMapping ?? new FaceUvMappingDto();
        Metadata = metadata ?? new ElementMetadata();
    }
}

/// <summary>
/// Serializable DTO for granular metadata attached to an undirected edge <c>(V0, V1)</c>.
/// </summary>
public sealed class EdgeMetadataDto
{
    public int V0 { get; set; }
    public int V1 { get; set; }
    public ElementMetadata Metadata { get; set; } = new();

    public EdgeMetadataDto()
    {
    }

    public EdgeMetadataDto(int v0, int v1, ElementMetadata? metadata = null)
    {
        V0 = Math.Min(v0, v1);
        V1 = Math.Max(v0, v1);
        Metadata = metadata ?? new ElementMetadata();
    }
}

/// <summary>
/// Serializable DTO for lossless N-gon geometry with indexed vertices, N-gon faces, and edge metadata.
/// </summary>
public sealed class EditorMeshDto
{
    public List<EditorVertexDto> Vertices { get; set; } = [];
    public List<PolygonFaceDto> Faces { get; set; } = [];
    public List<EdgeMetadataDto> Edges { get; set; } = [];

    public EditorMeshDto()
    {
    }
}

/// <summary>
/// Serializable DTO for a scene object in the lossless editor layer.
/// </summary>
public sealed class EditorObjectDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "Object";
    public TransformDto Transform { get; set; } = new();
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; } = false;
    public ElementMetadata Metadata { get; set; } = new();
    public EditorMeshDto? Mesh { get; set; }

    public EditorObjectDto()
    {
    }

    public EditorObjectDto(
        int id,
        string name,
        TransformDto? transform = null,
        EditorMeshDto? mesh = null,
        ElementMetadata? metadata = null,
        bool visible = true,
        bool locked = false)
    {
        Id = id;
        Name = name ?? "Object";
        Transform = transform ?? new TransformDto();
        Mesh = mesh;
        Metadata = metadata ?? new ElementMetadata();
        Visible = visible;
        Locked = locked;
    }
}
