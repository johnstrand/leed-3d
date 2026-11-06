using System.Numerics;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Texturing;
using Leed3D.Runtime.Metadata;

namespace Leed3D.Kernel.Geometry;

/// <summary>
/// Represents a single vertex with a stable unique ID, 3D position, and granular metadata inside an <see cref="EditableMesh"/>.
/// </summary>
public sealed class MeshVertex
{
    public int Id { get; }
    public Vector3 Position { get; set; }
    public ElementMetadata Metadata { get; set; }

    public MeshVertex(int id, Vector3 position, ElementMetadata? metadata = null)
    {
        Id = id;
        Position = position;
        Metadata = metadata ?? new ElementMetadata();
    }

    public MeshVertex Clone()
    {
        return new MeshVertex(Id, Position, Metadata.Clone());
    }
}

/// <summary>
/// Represents an ordered N-gon polygon face referencing vertex IDs in an <see cref="EditableMesh"/>.
/// </summary>
public sealed class PolygonFace
{
    public const string DefaultMaterialId = "dev_grid_gray";

    public int Id { get; }
    public List<int> VertexIds { get; }
    public string MaterialId { get; set; }
    public FaceUvMapping UvMapping { get; set; }
    public ElementMetadata Metadata { get; set; }

    public PolygonFace(int id, IEnumerable<int> vertexIds, string materialId = DefaultMaterialId, FaceUvMapping? uvMapping = null, ElementMetadata? metadata = null)
    {
        Id = id;
        VertexIds = new List<int>(vertexIds);
        MaterialId = string.IsNullOrWhiteSpace(materialId) ? DefaultMaterialId : materialId;
        UvMapping = uvMapping ?? new FaceUvMapping();
        Metadata = metadata ?? new ElementMetadata();
    }

    /// <summary>
    /// Computes the face normal using Newell's method, ensuring accurate outward normals
    /// for both convex and concave planar or near-planar N-gons.
    /// </summary>
    public Vector3 ComputeNormal(EditableMesh mesh)
    {
        int count = VertexIds.Count;
        if (count < 3)
        {
            return Vector3.UnitY;
        }

        Vector3 normal = Vector3.Zero;
        for (int i = 0; i < count; i++)
        {
            MeshVertex? currVert = mesh.GetVertex(VertexIds[i]);
            MeshVertex? nextVert = mesh.GetVertex(VertexIds[(i + 1) % count]);
            if (currVert is null || nextVert is null)
            {
                continue;
            }

            Vector3 curr = currVert.Position;
            Vector3 next = nextVert.Position;

            normal.X += (curr.Y - next.Y) * (curr.Z + next.Z);
            normal.Y += (curr.Z - next.Z) * (curr.X + next.X);
            normal.Z += (curr.X - next.X) * (curr.Y + next.Y);
        }

        float lenSq = normal.LengthSquared();
        if (lenSq > 1e-12f)
        {
            return normal / MathF.Sqrt(lenSq);
        }

        return Vector3.UnitY;
    }

    /// <summary>
    /// Computes the arithmetic centroid (average vertex position) of the N-gon face.
    /// </summary>
    public Vector3 ComputeCentroid(EditableMesh mesh)
    {
        int count = VertexIds.Count;
        if (count == 0)
        {
            return Vector3.Zero;
        }

        Vector3 sum = Vector3.Zero;
        int validCount = 0;

        for (int i = 0; i < count; i++)
        {
            MeshVertex? vert = mesh.GetVertex(VertexIds[i]);
            if (vert is not null)
            {
                sum += vert.Position;
                validCount++;
            }
        }

        return validCount > 0 ? sum / validCount : Vector3.Zero;
    }

    public PolygonFace Clone()
    {
        return new PolygonFace(Id, VertexIds, MaterialId, UvMapping.Clone(), Metadata.Clone());
    }
}

/// <summary>
/// Headless indexed N-gon mesh data model supporting arbitrary polygon topology,
/// on-demand adjacency analysis via <see cref="TopologyGraph"/>, and deterministic triangulation.
/// </summary>
public sealed class EditableMesh
{
    private readonly Dictionary<int, MeshVertex> _verticesById = new();
    private readonly List<MeshVertex> _vertices = new();
    private readonly Dictionary<int, PolygonFace> _facesById = new();
    private readonly List<PolygonFace> _faces = new();
    private readonly Dictionary<EdgeKey, ElementMetadata> _edgeMetadata = new();

    private int _nextVertexId = 1;
    private int _nextFaceId = 1;
    private TopologyGraph? _cachedTopology;

    public IReadOnlyList<MeshVertex> Vertices => _vertices;
    public IReadOnlyList<PolygonFace> Faces => _faces;
    public Dictionary<EdgeKey, ElementMetadata> EdgeMetadata => _edgeMetadata;

    public int VertexCount => _vertices.Count;
    public int FaceCount => _faces.Count;

    /// <summary>
    /// Gets or creates the <see cref="ElementMetadata"/> for the given edge.
    /// </summary>
    public ElementMetadata GetOrCreateEdgeMetadata(EdgeKey edge)
    {
        if (!_edgeMetadata.TryGetValue(edge, out ElementMetadata? meta))
        {
            meta = new ElementMetadata();
            _edgeMetadata[edge] = meta;
        }

        return meta;
    }

    /// <summary>
    /// Gets existing metadata for the edge, or null if none.
    /// </summary>
    public ElementMetadata? GetEdgeMetadata(EdgeKey edge)
    {
        return _edgeMetadata.GetValueOrDefault(edge);
    }

    /// <summary>
    /// Adds a vertex at the specified 3D position and returns its stable unique ID.
    /// </summary>
    public int AddVertex(Vector3 position, ElementMetadata? metadata = null)
    {
        int id = _nextVertexId++;
        AddVertexWithId(id, position, metadata);
        return id;
    }

    internal void AddVertexWithId(int id, Vector3 position, ElementMetadata? metadata = null)
    {
        if (id >= _nextVertexId)
        {
            _nextVertexId = id + 1;
        }

        var vertex = new MeshVertex(id, position, metadata);
        _verticesById[id] = vertex;
        _vertices.Add(vertex);
        InvalidateTopology();
    }

    /// <summary>
    /// Adds an N-gon polygon face with the given ordered vertex ID loop and returns its stable unique ID.
    /// </summary>
    public int AddFace(IEnumerable<int> vertexIds, string materialId = PolygonFace.DefaultMaterialId, FaceUvMapping? uvMapping = null, ElementMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(vertexIds);

        var idList = vertexIds as IList<int> ?? vertexIds.ToList();
        if (idList.Count < 3)
        {
            throw new ArgumentException("A polygon face requires at least 3 vertices.", nameof(vertexIds));
        }

        for (int i = 0; i < idList.Count; i++)
        {
            if (!_verticesById.ContainsKey(idList[i]))
            {
                throw new ArgumentException($"Vertex ID {idList[i]} does not exist in the mesh.", nameof(vertexIds));
            }
        }

        int id = _nextFaceId++;
        AddFaceWithId(id, idList, materialId, uvMapping, metadata);
        return id;
    }

    internal void AddFaceWithId(int id, IEnumerable<int> vertexIds, string materialId = PolygonFace.DefaultMaterialId, FaceUvMapping? uvMapping = null, ElementMetadata? metadata = null)
    {
        if (id >= _nextFaceId)
        {
            _nextFaceId = id + 1;
        }

        var face = new PolygonFace(id, vertexIds, materialId, uvMapping?.Clone(), metadata?.Clone());
        if (uvMapping is null || !uvMapping.TextureLock)
        {
            Vector3 normal = face.ComputeNormal(this);
            (face.UvMapping.UAxis, face.UvMapping.VAxis) = UvProjectionEngine.GetDefaultAxesForNormal(normal);
        }

        _facesById[id] = face;
        _faces.Add(face);
        InvalidateTopology();
    }

    public MeshVertex? GetVertex(int id)
    {
        return _verticesById.GetValueOrDefault(id);
    }

    public PolygonFace? GetFace(int id)
    {
        return _facesById.GetValueOrDefault(id);
    }

    public bool SetVertexPosition(int id, Vector3 newPosition)
    {
        if (!_verticesById.TryGetValue(id, out MeshVertex? vertex))
        {
            return false;
        }

        vertex.Position = newPosition;

        for (int i = 0; i < _faces.Count; i++)
        {
            PolygonFace face = _faces[i];
            if (!face.UvMapping.TextureLock && face.VertexIds.Contains(id))
            {
                Vector3 normal = face.ComputeNormal(this);
                (face.UvMapping.UAxis, face.UvMapping.VAxis) = UvProjectionEngine.GetDefaultAxesForNormal(normal);
            }
        }

        return true;
    }

    public bool RemoveFace(int faceId)
    {
        if (!_facesById.Remove(faceId, out PolygonFace? face))
        {
            return false;
        }

        _faces.Remove(face);
        InvalidateTopology();
        return true;
    }

    public bool RemoveVertex(int vertexId)
    {
        if (!_verticesById.Remove(vertexId, out MeshVertex? vertex))
        {
            return false;
        }

        _vertices.Remove(vertex);
        InvalidateTopology();
        return true;
    }

    /// <summary>
    /// Creates a deep copy of this mesh preserving all vertex and face IDs.
    /// </summary>
    public EditableMesh Clone()
    {
        var copy = new EditableMesh
        {
            _nextVertexId = _nextVertexId,
            _nextFaceId = _nextFaceId
        };

        for (int i = 0; i < _vertices.Count; i++)
        {
            MeshVertex clonedVert = _vertices[i].Clone();
            copy._vertices.Add(clonedVert);
            copy._verticesById[clonedVert.Id] = clonedVert;
        }

        for (int i = 0; i < _faces.Count; i++)
        {
            PolygonFace clonedFace = _faces[i].Clone();
            copy._faces.Add(clonedFace);
            copy._facesById[clonedFace.Id] = clonedFace;
        }

        foreach (var (edge, meta) in _edgeMetadata)
        {
            copy._edgeMetadata[edge] = meta.Clone();
        }

        return copy;
    }

    /// <summary>
    /// Reverses the vertex winding order on the specified faces, or on all faces if <paramref name="faceIds"/> is null.
    /// </summary>
    public void FlipNormals(IEnumerable<int>? faceIds = null)
    {
        if (faceIds is null)
        {
            for (int i = 0; i < _faces.Count; i++)
            {
                _faces[i].VertexIds.Reverse();
            }
        }
        else
        {
            foreach (int faceId in faceIds)
            {
                if (_facesById.TryGetValue(faceId, out PolygonFace? face))
                {
                    face.VertexIds.Reverse();
                }
            }
        }

        InvalidateTopology();
    }

    /// <summary>
    /// Cleans up topological defects:
    /// 1. Removes non-existent vertex references and consecutive duplicate vertex IDs in faces (including wrap-around).
    /// 2. Deletes degenerate faces with fewer than 3 unique vertices.
    /// 3. Removes orphaned vertices not referenced by any remaining face.
    /// 4. Invalidates the cached <see cref="TopologyGraph"/>.
    /// </summary>
    public void ValidateAndRepair()
    {
        var facesToRemove = new List<int>();
        var referencedVertices = new HashSet<int>();

        for (int i = 0; i < _faces.Count; i++)
        {
            PolygonFace face = _faces[i];
            List<int> ids = face.VertexIds;

            // Remove missing vertices
            for (int j = ids.Count - 1; j >= 0; j--)
            {
                if (!_verticesById.ContainsKey(ids[j]))
                {
                    ids.RemoveAt(j);
                }
            }

            // Remove consecutive duplicate vertex IDs
            for (int j = ids.Count - 1; j > 0; j--)
            {
                if (ids[j] == ids[j - 1])
                {
                    ids.RemoveAt(j);
                }
            }

            // Check wrap-around duplicate (last == first)
            while (ids.Count > 1 && ids[^1] == ids[0])
            {
                ids.RemoveAt(ids.Count - 1);
            }

            // Count unique vertices in the loop
            var uniqueInFace = new HashSet<int>(ids);
            if (ids.Count < 3 || uniqueInFace.Count < 3)
            {
                facesToRemove.Add(face.Id);
            }
            else
            {
                foreach (int vId in ids)
                {
                    referencedVertices.Add(vId);
                }
            }
        }

        // Remove degenerate faces
        for (int i = 0; i < facesToRemove.Count; i++)
        {
            int faceId = facesToRemove[i];
            if (_facesById.Remove(faceId, out PolygonFace? removedFace))
            {
                _faces.Remove(removedFace);
            }
        }

        // Remove orphaned vertices not referenced by any remaining valid face
        for (int i = _vertices.Count - 1; i >= 0; i--)
        {
            int vertId = _vertices[i].Id;
            if (!referencedVertices.Contains(vertId))
            {
                _verticesById.Remove(vertId);
                _vertices.RemoveAt(i);
            }
        }

        // Prune EdgeMetadata for edges that no longer exist in any face
        if (_edgeMetadata.Count > 0)
        {
            var validEdges = new HashSet<EdgeKey>();
            for (int i = 0; i < _faces.Count; i++)
            {
                List<int> loop = _faces[i].VertexIds;
                int count = loop.Count;
                for (int j = 0; j < count; j++)
                {
                    validEdges.Add(new EdgeKey(loop[j], loop[(j + 1) % count]));
                }
            }

            var deadEdges = new List<EdgeKey>();
            foreach (EdgeKey ek in _edgeMetadata.Keys)
            {
                if (!validEdges.Contains(ek))
                {
                    deadEdges.Add(ek);
                }
            }

            for (int i = 0; i < deadEdges.Count; i++)
            {
                _edgeMetadata.Remove(deadEdges[i]);
            }
        }

        InvalidateTopology();
    }

    /// <summary>
    /// Computes the local-space axis-aligned bounding box of all vertices in the mesh.
    /// </summary>
    public BoundingBox3D ComputeBoundingBox()
    {
        if (_vertices.Count == 0)
        {
            return new BoundingBox3D(Vector3.Zero, Vector3.Zero);
        }

        Vector3 min = _vertices[0].Position;
        Vector3 max = min;

        for (int i = 1; i < _vertices.Count; i++)
        {
            Vector3 p = _vertices[i].Position;
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        return new BoundingBox3D(min, max);
    }

    /// <summary>
    /// Gets or builds the cached <see cref="TopologyGraph"/> for this mesh.
    /// </summary>
    public TopologyGraph GetTopology()
    {
        return _cachedTopology ??= TopologyGraph.Build(this);
    }

    /// <summary>
    /// Explicitly invalidates the cached <see cref="TopologyGraph"/> after external face mutations.
    /// </summary>
    public void InvalidateTopology()
    {
        _cachedTopology = null;
    }
}
