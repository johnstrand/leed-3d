namespace Leed3D.Kernel.Geometry;

/// <summary>
/// Canonical undirected edge key identified by its two endpoint vertex IDs ordered (VMin, VMax).
/// </summary>
public readonly record struct EdgeKey
{
    public int VMin { get; }
    public int VMax { get; }

    public EdgeKey(int a, int b)
    {
        if (a <= b)
        {
            VMin = a;
            VMax = b;
        }
        else
        {
            VMin = b;
            VMax = a;
        }
    }

    public bool ContainsVertex(int vertexId) => VMin == vertexId || VMax == vertexId;

    public int GetOtherVertex(int vertexId)
    {
        if (vertexId == VMin) return VMax;
        if (vertexId == VMax) return VMin;
        throw new ArgumentException($"Vertex {vertexId} is not part of edge ({VMin}, {VMax}).", nameof(vertexId));
    }

    public override string ToString() => $"({VMin}, {VMax})";
}

/// <summary>
/// Represents a directed half-edge belonging to a single <see cref="PolygonFace"/> loop.
/// </summary>
public readonly record struct DirectedHalfEdge(
    int Id,
    int OriginVertexId,
    int TargetVertexId,
    int FaceId,
    int NextHalfEdgeId,
    int PrevHalfEdgeId,
    int? TwinHalfEdgeId)
{
    public EdgeKey EdgeKey => new(OriginVertexId, TargetVertexId);
}

/// <summary>
/// Represents an undirected topological edge in an <see cref="EditableMesh"/> along with its incident faces.
/// </summary>
public sealed record TopologyEdge(
    int Id,
    EdgeKey Key,
    IReadOnlyList<int> IncidentFaceIds,
    bool IsBoundary);

/// <summary>
/// On-demand adjacency graph computed from an <see cref="EditableMesh"/>.
/// Provides undirected edges, directed half-edges, twin pairing, boundary edge detection,
/// and vertex/face neighbor queries.
/// </summary>
public sealed class TopologyGraph
{
    private readonly List<TopologyEdge> _edges;
    private readonly Dictionary<EdgeKey, TopologyEdge> _edgesByKey;
    private readonly List<DirectedHalfEdge> _halfEdges;
    private readonly Dictionary<int, List<int>> _faceHalfEdgeIds;
    private readonly Dictionary<int, List<int>> _vertexIncidentFaces;
    private readonly Dictionary<int, List<int>> _vertexConnectedVertices;
    private readonly Dictionary<int, List<int>> _faceAdjacentFaces;
    private readonly bool _isClosedManifold;

    private TopologyGraph(
        List<TopologyEdge> edges,
        Dictionary<EdgeKey, TopologyEdge> edgesByKey,
        List<DirectedHalfEdge> halfEdges,
        Dictionary<int, List<int>> faceHalfEdgeIds,
        Dictionary<int, List<int>> vertexIncidentFaces,
        Dictionary<int, List<int>> vertexConnectedVertices,
        Dictionary<int, List<int>> faceAdjacentFaces,
        bool isClosedManifold)
    {
        _edges = edges;
        _edgesByKey = edgesByKey;
        _halfEdges = halfEdges;
        _faceHalfEdgeIds = faceHalfEdgeIds;
        _vertexIncidentFaces = vertexIncidentFaces;
        _vertexConnectedVertices = vertexConnectedVertices;
        _faceAdjacentFaces = faceAdjacentFaces;
        _isClosedManifold = isClosedManifold;
    }

    public IReadOnlyList<TopologyEdge> Edges => _edges;
    public IReadOnlyList<DirectedHalfEdge> HalfEdges => _halfEdges;

    /// <summary>
    /// Builds a complete <see cref="TopologyGraph"/> from the current state of an <see cref="EditableMesh"/>.
    /// </summary>
    public static TopologyGraph Build(EditableMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        var tempHalfEdges = new List<(int Id, int Origin, int Target, int FaceId, int NextId, int PrevId)>();
        var faceHalfEdgeIds = new Dictionary<int, List<int>>();
        var edgeToHalfEdges = new Dictionary<EdgeKey, List<int>>();
        var vertexIncidentFacesSet = new Dictionary<int, HashSet<int>>();
        var vertexIncidentFaces = new Dictionary<int, List<int>>();
        var vertexConnectedSet = new Dictionary<int, HashSet<int>>();
        var vertexConnectedVertices = new Dictionary<int, List<int>>();

        foreach (MeshVertex v in mesh.Vertices)
        {
            vertexIncidentFacesSet[v.Id] = [];
            vertexIncidentFaces[v.Id] = [];
            vertexConnectedSet[v.Id] = [];
            vertexConnectedVertices[v.Id] = [];
        }

        // 1. Create directed half-edges for each face loop
        foreach (PolygonFace face in mesh.Faces)
        {
            List<int> vIds = face.VertexIds;
            int count = vIds.Count;
            if (count < 3)
            {
                continue;
            }

            int baseHalfEdgeId = tempHalfEdges.Count;
            var heIdsForFace = new List<int>(count);

            for (int i = 0; i < count; i++)
            {
                int origin = vIds[i];
                int target = vIds[(i + 1) % count];
                if (origin == target)
                {
                    continue;
                }

                int heId = baseHalfEdgeId + i;
                int nextId = baseHalfEdgeId + ((i + 1) % count);
                int prevId = baseHalfEdgeId + ((i - 1 + count) % count);

                tempHalfEdges.Add((heId, origin, target, face.Id, nextId, prevId));
                heIdsForFace.Add(heId);

                var edgeKey = new EdgeKey(origin, target);
                if (!edgeToHalfEdges.TryGetValue(edgeKey, out List<int>? heList))
                {
                    heList = new List<int>(2);
                    edgeToHalfEdges[edgeKey] = heList;
                }
                heList.Add(heId);

                if (!vertexIncidentFacesSet.TryGetValue(origin, out HashSet<int>? fSet))
                {
                    fSet = [];
                    vertexIncidentFacesSet[origin] = fSet;
                    vertexIncidentFaces[origin] = [];
                }
                if (fSet.Add(face.Id))
                {
                    vertexIncidentFaces[origin].Add(face.Id);
                }

                if (!vertexConnectedSet.TryGetValue(origin, out HashSet<int>? cSetOrigin))
                {
                    cSetOrigin = [];
                    vertexConnectedSet[origin] = cSetOrigin;
                    vertexConnectedVertices[origin] = [];
                }
                if (cSetOrigin.Add(target))
                {
                    vertexConnectedVertices[origin].Add(target);
                }

                if (!vertexConnectedSet.TryGetValue(target, out HashSet<int>? cSetTarget))
                {
                    cSetTarget = [];
                    vertexConnectedSet[target] = cSetTarget;
                    vertexConnectedVertices[target] = [];
                }
                if (cSetTarget.Add(origin))
                {
                    vertexConnectedVertices[target].Add(origin);
                }
            }

            faceHalfEdgeIds[face.Id] = heIdsForFace;
        }

        // 2. Pair twin half-edges and construct undirected TopologyEdges
        var twinIds = new int?[tempHalfEdges.Count];
        var edges = new List<TopologyEdge>(edgeToHalfEdges.Count);
        var edgesByKey = new Dictionary<EdgeKey, TopologyEdge>(edgeToHalfEdges.Count);
        var faceAdjacentSet = new Dictionary<int, HashSet<int>>();
        var faceAdjacentFaces = new Dictionary<int, List<int>>();

        foreach (PolygonFace face in mesh.Faces)
        {
            faceAdjacentSet[face.Id] = [];
            faceAdjacentFaces[face.Id] = [];
        }

        bool isClosedManifold = mesh.Faces.Count > 0 && edgeToHalfEdges.Count > 0;
        int edgeIdCounter = 0;

        foreach ((EdgeKey key, List<int> heIds) in edgeToHalfEdges)
        {
            var incidentFaces = new List<int>(heIds.Count);
            for (int i = 0; i < heIds.Count; i++)
            {
                int fId = tempHalfEdges[heIds[i]].FaceId;
                if (!incidentFaces.Contains(fId))
                {
                    incidentFaces.Add(fId);
                }
            }

            // Pair opposite half-edges
            if (heIds.Count == 2)
            {
                var he0 = tempHalfEdges[heIds[0]];
                var he1 = tempHalfEdges[heIds[1]];

                if (he0.Origin == he1.Target && he0.Target == he1.Origin)
                {
                    twinIds[he0.Id] = he1.Id;
                    twinIds[he1.Id] = he0.Id;
                }
                else
                {
                    // Two incident faces traverse the shared edge in the same direction (non-orientable / inconsistent winding)
                    isClosedManifold = false;
                }
            }
            else
            {
                // Boundary edge (count == 1) or non-manifold edge (count > 2)
                isClosedManifold = false;

                // Still pair opposite half-edges if any exist among non-manifold edges
                for (int i = 0; i < heIds.Count; i++)
                {
                    var heA = tempHalfEdges[heIds[i]];
                    if (twinIds[heA.Id].HasValue) continue;

                    for (int j = i + 1; j < heIds.Count; j++)
                    {
                        var heB = tempHalfEdges[heIds[j]];
                        if (!twinIds[heB.Id].HasValue && heA.Origin == heB.Target && heA.Target == heB.Origin)
                        {
                            twinIds[heA.Id] = heB.Id;
                            twinIds[heB.Id] = heA.Id;
                            break;
                        }
                    }
                }
            }

            if (incidentFaces.Count != 2)
            {
                isClosedManifold = false;
            }

            // Record face-to-face adjacency
            for (int i = 0; i < incidentFaces.Count; i++)
            {
                for (int j = i + 1; j < incidentFaces.Count; j++)
                {
                    int fA = incidentFaces[i];
                    int fB = incidentFaces[j];
                    if (faceAdjacentSet[fA].Add(fB))
                    {
                        faceAdjacentFaces[fA].Add(fB);
                    }
                    if (faceAdjacentSet[fB].Add(fA))
                    {
                        faceAdjacentFaces[fB].Add(fA);
                    }
                }
            }

            bool isBoundary = incidentFaces.Count == 1;
            var topoEdge = new TopologyEdge(edgeIdCounter++, key, incidentFaces, isBoundary);
            edges.Add(topoEdge);
            edgesByKey[key] = topoEdge;
        }

        // 3. Finalize DirectedHalfEdge structs
        var halfEdges = new List<DirectedHalfEdge>(tempHalfEdges.Count);
        for (int i = 0; i < tempHalfEdges.Count; i++)
        {
            var raw = tempHalfEdges[i];
            halfEdges.Add(new DirectedHalfEdge(
                raw.Id,
                raw.Origin,
                raw.Target,
                raw.FaceId,
                raw.NextId,
                raw.PrevId,
                twinIds[i]));
        }

        return new TopologyGraph(
            edges,
            edgesByKey,
            halfEdges,
            faceHalfEdgeIds,
            vertexIncidentFaces,
            vertexConnectedVertices,
            faceAdjacentFaces,
            isClosedManifold);
    }

    public TopologyEdge? GetEdge(EdgeKey key)
    {
        return _edgesByKey.GetValueOrDefault(key);
    }

    public TopologyEdge? GetEdge(int vA, int vB)
    {
        return _edgesByKey.GetValueOrDefault(new EdgeKey(vA, vB));
    }

    public IReadOnlyList<int> GetFaceHalfEdgeIds(int faceId)
    {
        return _faceHalfEdgeIds.TryGetValue(faceId, out List<int>? list) ? list : [];
    }

    public IReadOnlyList<int> GetVertexIncidentFaces(int vertexId)
    {
        return _vertexIncidentFaces.TryGetValue(vertexId, out List<int>? list) ? list : [];
    }

    public IReadOnlyList<int> GetVertexConnectedVertices(int vertexId)
    {
        return _vertexConnectedVertices.TryGetValue(vertexId, out List<int>? list) ? list : [];
    }

    public IReadOnlyList<int> GetFaceAdjacentFaces(int faceId)
    {
        return _faceAdjacentFaces.TryGetValue(faceId, out List<int>? list) ? list : [];
    }

    /// <summary>
    /// Returns true if the mesh has at least one face and every undirected edge has
    /// exactly 2 incident faces traversing the shared edge in opposite directions.
    /// </summary>
    public bool IsClosedManifold() => _isClosedManifold;
}
