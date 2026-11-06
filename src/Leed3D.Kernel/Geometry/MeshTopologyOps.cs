using System.Numerics;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Scene;

namespace Leed3D.Kernel.Geometry;

/// <summary>
/// Headless vertex, edge, and face topology construction and editing operations for <see cref="EditableMesh"/>.
/// </summary>
public static class MeshTopologyOps
{
    /// <summary>
    /// Welds/merges the specified vertices to their average position (centroid), keeping the lowest vertex ID,
    /// remapping all face vertex references, and running <see cref="EditableMesh.ValidateAndRepair"/>.
    /// Returns the surviving vertex ID, or -1 if no valid vertices were provided.
    /// </summary>
    public static int WeldVerticesToCenter(EditableMesh mesh, IReadOnlyCollection<int> vertexIds)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(vertexIds);

        if (vertexIds.Count == 0)
        {
            return -1;
        }

        var validVerts = new List<MeshVertex>(vertexIds.Count);
        var seenIds = new HashSet<int>();

        foreach (int id in vertexIds)
        {
            if (seenIds.Add(id))
            {
                MeshVertex? v = mesh.GetVertex(id);
                if (v is not null)
                {
                    validVerts.Add(v);
                }
            }
        }

        if (validVerts.Count == 0)
        {
            return -1;
        }

        validVerts.Sort((a, b) => a.Id.CompareTo(b.Id));
        int keptVertexId = validVerts[0].Id;

        if (validVerts.Count == 1)
        {
            return keptVertexId;
        }

        Vector3 sum = Vector3.Zero;
        for (int i = 0; i < validVerts.Count; i++)
        {
            sum += validVerts[i].Position;
        }

        Vector3 centroid = sum / validVerts.Count;
        mesh.SetVertexPosition(keptVertexId, centroid);

        var mergedIds = new HashSet<int>(validVerts.Count - 1);
        for (int i = 1; i < validVerts.Count; i++)
        {
            mergedIds.Add(validVerts[i].Id);
        }

        foreach (PolygonFace face in mesh.Faces)
        {
            List<int> ids = face.VertexIds;
            for (int i = 0; i < ids.Count; i++)
            {
                if (mergedIds.Contains(ids[i]))
                {
                    ids[i] = keptVertexId;
                }
            }
        }

        foreach (int removedId in mergedIds)
        {
            mesh.RemoveVertex(removedId);
        }

        mesh.ValidateAndRepair();
        return keptVertexId;
    }

    /// <summary>
    /// Welds any pair/cluster of vertices within <paramref name="threshold"/> distance,
    /// remapping face indices and repairing degenerate faces.
    /// Returns the number of vertices welded/merged away.
    /// </summary>
    public static int WeldVerticesByDistance(
        EditableMesh mesh,
        IEnumerable<int>? targetVertexIds = null,
        float threshold = 0.001f)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        threshold = System.Math.Max(0.0f, threshold);
        float thresholdSq = threshold * threshold;

        var candidates = new List<MeshVertex>();
        if (targetVertexIds is null)
        {
            candidates.AddRange(mesh.Vertices);
        }
        else
        {
            var seen = new HashSet<int>();
            foreach (int id in targetVertexIds)
            {
                if (seen.Add(id))
                {
                    MeshVertex? v = mesh.GetVertex(id);
                    if (v is not null)
                    {
                        candidates.Add(v);
                    }
                }
            }
        }

        if (candidates.Count < 2)
        {
            return 0;
        }

        candidates.Sort((a, b) => a.Id.CompareTo(b.Id));

        var remap = new Dictionary<int, int>();

        for (int i = 0; i < candidates.Count; i++)
        {
            MeshVertex anchor = candidates[i];
            if (remap.ContainsKey(anchor.Id))
            {
                continue;
            }

            Vector3 clusterSum = anchor.Position;
            int clusterCount = 1;

            for (int j = i + 1; j < candidates.Count; j++)
            {
                MeshVertex other = candidates[j];
                if (remap.ContainsKey(other.Id))
                {
                    continue;
                }

                if (Vector3.DistanceSquared(anchor.Position, other.Position) <= thresholdSq)
                {
                    remap[other.Id] = anchor.Id;
                    clusterSum += other.Position;
                    clusterCount++;
                }
            }

            if (clusterCount > 1)
            {
                anchor.Position = clusterSum / clusterCount;
            }
        }

        if (remap.Count == 0)
        {
            return 0;
        }

        foreach (PolygonFace face in mesh.Faces)
        {
            List<int> ids = face.VertexIds;
            for (int i = 0; i < ids.Count; i++)
            {
                if (remap.TryGetValue(ids[i], out int replacementId))
                {
                    ids[i] = replacementId;
                }
            }
        }

        foreach (int removedId in remap.Keys)
        {
            mesh.RemoveVertex(removedId);
        }

        mesh.ValidateAndRepair();
        return remap.Count;
    }

    /// <summary>
    /// Snaps the world-space positions of the specified vertices to the nearest multiple of <paramref name="gridStep"/>
    /// using <see cref="GridSnapper"/>, converting back into local mesh space.
    /// </summary>
    public static void SnapVerticesToGrid(
        EditableMesh mesh,
        IEnumerable<int> vertexIds,
        Transform3D objectTransform,
        float gridStep)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(vertexIds);
        ArgumentNullException.ThrowIfNull(objectTransform);

        if (gridStep <= 0.0f || float.IsNaN(gridStep) || float.IsInfinity(gridStep))
        {
            return;
        }

        var seen = new HashSet<int>();
        foreach (int vId in vertexIds)
        {
            if (!seen.Add(vId))
            {
                continue;
            }

            MeshVertex? vert = mesh.GetVertex(vId);
            if (vert is null)
            {
                continue;
            }

            Vector3 worldPos = objectTransform.TransformPointToWorld(vert.Position);
            Vector3 snappedWorld = GridSnapper.SnapVector(worldPos, gridStep);
            vert.Position = objectTransform.TransformPointToLocal(snappedWorld);
        }

        mesh.InvalidateTopology();
    }

    /// <summary>
    /// Snaps each selected vertex to the closest unselected vertex in the mesh.
    /// </summary>
    public static void SnapVerticesToNearestVertex(
        EditableMesh mesh,
        IReadOnlyCollection<int> selectedVertexIds)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(selectedVertexIds);

        if (selectedVertexIds.Count == 0)
        {
            return;
        }

        var selectedSet = new HashSet<int>(selectedVertexIds);
        var unselectedVertices = new List<MeshVertex>();

        foreach (MeshVertex v in mesh.Vertices)
        {
            if (!selectedSet.Contains(v.Id))
            {
                unselectedVertices.Add(v);
            }
        }

        if (unselectedVertices.Count == 0)
        {
            return;
        }

        foreach (int selId in selectedSet)
        {
            MeshVertex? selVert = mesh.GetVertex(selId);
            if (selVert is null)
            {
                continue;
            }

            MeshVertex? closest = null;
            float bestDistSq = float.PositiveInfinity;

            for (int i = 0; i < unselectedVertices.Count; i++)
            {
                MeshVertex candidate = unselectedVertices[i];
                float distSq = Vector3.DistanceSquared(selVert.Position, candidate.Position);
                if (distSq < bestDistSq || (distSq == bestDistSq && closest is not null && candidate.Id < closest.Id))
                {
                    bestDistSq = distSq;
                    closest = candidate;
                }
            }

            if (closest is not null)
            {
                selVert.Position = closest.Position;
            }
        }

        mesh.InvalidateTopology();
    }

    /// <summary>
    /// Connects two non-adjacent vertices <paramref name="vA"/> and <paramref name="vB"/> across any face containing both,
    /// splitting that face into two child N-gon faces inheriting the original face's material.
    /// </summary>
    public static bool ConnectVertices(EditableMesh mesh, int vA, int vB)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        if (vA == vB || mesh.GetVertex(vA) is null || mesh.GetVertex(vB) is null)
        {
            return false;
        }

        var facesSnapshot = mesh.Faces.ToList();
        bool splitAny = false;

        foreach (PolygonFace face in facesSnapshot)
        {
            List<int> ids = face.VertexIds;
            int n = ids.Count;
            if (n < 4)
            {
                continue;
            }

            int idxA = ids.IndexOf(vA);
            int idxB = ids.IndexOf(vB);
            if (idxA < 0 || idxB < 0)
            {
                continue;
            }

            // Skip if vA and vB are already adjacent along the polygon perimeter
            if ((idxA + 1) % n == idxB || (idxB + 1) % n == idxA)
            {
                continue;
            }

            // Build child loop 1: [vA, ..., vB]
            var loopAB = new List<int>();
            int curr = idxA;
            while (true)
            {
                loopAB.Add(ids[curr]);
                if (curr == idxB)
                {
                    break;
                }
                curr = (curr + 1) % n;
            }

            // Build child loop 2: [vB, ..., vA]
            var loopBA = new List<int>();
            curr = idxB;
            while (true)
            {
                loopBA.Add(ids[curr]);
                if (curr == idxA)
                {
                    break;
                }
                curr = (curr + 1) % n;
            }

            if (loopAB.Count >= 3 && loopBA.Count >= 3)
            {
                string materialId = face.MaterialId;
                var metaCopy = face.Metadata.Clone();
                mesh.RemoveFace(face.Id);
                int f1 = mesh.AddFace(loopAB, materialId);
                int f2 = mesh.AddFace(loopBA, materialId);
                if (metaCopy.Properties.Count > 0 || metaCopy.Tags.Count > 0)
                {
                    mesh.GetFace(f1)?.Metadata.MergeFrom(metaCopy);
                    mesh.GetFace(f2)?.Metadata.MergeFrom(metaCopy);
                }
                splitAny = true;
            }
        }

        if (splitAny)
        {
            mesh.ValidateAndRepair();
        }

        return splitAny;
    }

    /// <summary>
    /// Creates and adds a new <see cref="PolygonFace"/> from 3 or more vertices, ordering them into a clean
    /// non-self-intersecting loop and orienting the face normal to match adjacent face winding or <paramref name="preferredNormalDir"/>.
    /// Returns the new face ID, or null if fewer than 3 valid unique vertices were supplied.
    /// </summary>
    public static int? FillFaceFromVertices(
        EditableMesh mesh,
        IReadOnlyCollection<int> vertexIds,
        Vector3? preferredNormalDir = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(vertexIds);

        var uniqueIds = new List<int>(vertexIds.Count);
        var seen = new HashSet<int>();

        foreach (int id in vertexIds)
        {
            if (seen.Add(id) && mesh.GetVertex(id) is not null)
            {
                uniqueIds.Add(id);
            }
        }

        if (uniqueIds.Count < 3)
        {
            return null;
        }

        TopologyGraph topology = mesh.GetTopology();
        List<int> orderedLoop = OrderVerticesForFaceLoop(mesh, topology, uniqueIds, preferredNormalDir);
        OrientLoopWinding(mesh, topology, orderedLoop, preferredNormalDir);

        string materialId = ResolveInheritedMaterialId(mesh, topology, orderedLoop);
        int newFaceId = mesh.AddFace(orderedLoop, materialId);
        return newFaceId;
    }

    /// <summary>
    /// Creates and adds a new <see cref="PolygonFace"/> from selected edges (filling a boundary loop or bridging two opposite edges into a quad).
    /// Returns the new face ID, or null if the edges do not span at least 3 valid vertices.
    /// </summary>
    public static int? FillFaceFromEdges(
        EditableMesh mesh,
        IReadOnlyCollection<EdgeKey> selectedEdges,
        Vector3? preferredNormalDir = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(selectedEdges);

        if (selectedEdges.Count == 0)
        {
            return null;
        }

        var validEdges = new List<EdgeKey>(selectedEdges.Count);
        var seenEdges = new HashSet<EdgeKey>();
        var uniqueVertices = new List<int>();
        var seenVertices = new HashSet<int>();

        foreach (EdgeKey edge in selectedEdges)
        {
            if (edge.VMin == edge.VMax ||
                mesh.GetVertex(edge.VMin) is null ||
                mesh.GetVertex(edge.VMax) is null)
            {
                continue;
            }

            if (seenEdges.Add(edge))
            {
                validEdges.Add(edge);
            }

            if (seenVertices.Add(edge.VMin))
            {
                uniqueVertices.Add(edge.VMin);
            }

            if (seenVertices.Add(edge.VMax))
            {
                uniqueVertices.Add(edge.VMax);
            }
        }

        if (uniqueVertices.Count < 3)
        {
            return null;
        }

        TopologyGraph topology = mesh.GetTopology();

        // Special case: bridging two disjoint edges (a0, a1) and (b0, b1) into a quad
        if (validEdges.Count == 2 && uniqueVertices.Count == 4)
        {
            EdgeKey eA = validEdges[0];
            EdgeKey eB = validEdges[1];
            int a0 = eA.VMin;
            int a1 = eA.VMax;
            int b0 = eB.VMin;
            int b1 = eB.VMax;

            Vector3 posA0 = mesh.GetVertex(a0)!.Position;
            Vector3 posA1 = mesh.GetVertex(a1)!.Position;
            Vector3 posB0 = mesh.GetVertex(b0)!.Position;
            Vector3 posB1 = mesh.GetVertex(b1)!.Position;

            Vector3 dirA = posA1 - posA0;
            Vector3 dirB = posB1 - posB0;

            List<int> bridgeLoop = Vector3.Dot(dirA, dirB) >= 0.0f
                ? [a0, a1, b1, b0]
                : [a0, a1, b0, b1];

            OrientLoopWinding(mesh, topology, bridgeLoop, preferredNormalDir);
            string materialId = ResolveInheritedMaterialId(mesh, topology, bridgeLoop);
            return mesh.AddFace(bridgeLoop, materialId);
        }

        // Check if the selected edges themselves form a single cycle or simple path spanning all uniqueVertices
        if (TryBuildChainFromEdges(validEdges, uniqueVertices, out List<int>? edgeChainLoop) && edgeChainLoop is not null)
        {
            OrientLoopWinding(mesh, topology, edgeChainLoop, preferredNormalDir);
            string materialId = ResolveInheritedMaterialId(mesh, topology, edgeChainLoop);
            return mesh.AddFace(edgeChainLoop, materialId);
        }

        return FillFaceFromVertices(mesh, uniqueVertices, preferredNormalDir);
    }

    /// <summary>
    /// Subdivides each specified edge into <paramref name="cuts"/> + 1 segments by creating <paramref name="cuts"/>
    /// evenly spaced intermediate vertices and inserting them in-order into every incident <see cref="PolygonFace"/>.
    /// Returns the list of newly created vertex IDs.
    /// </summary>
    public static IReadOnlyList<int> SubdivideEdges(
        EditableMesh mesh,
        IReadOnlyCollection<EdgeKey> edges,
        int cuts = 1)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(edges);

        if (cuts < 1 || edges.Count == 0)
        {
            return [];
        }

        TopologyGraph topology = mesh.GetTopology();
        var createdVertexIds = new List<int>();
        var edgeSubdivisions = new Dictionary<EdgeKey, int[]>();

        foreach (EdgeKey edge in edges)
        {
            if (edge.VMin == edge.VMax || edgeSubdivisions.ContainsKey(edge))
            {
                continue;
            }

            if (topology.GetEdge(edge) is null)
            {
                continue;
            }

            MeshVertex? vMin = mesh.GetVertex(edge.VMin);
            MeshVertex? vMax = mesh.GetVertex(edge.VMax);
            if (vMin is null || vMax is null)
            {
                continue;
            }

            var midIds = new int[cuts];
            for (int step = 1; step <= cuts; step++)
            {
                float t = (float)step / (cuts + 1);
                Vector3 pos = Vector3.Lerp(vMin.Position, vMax.Position, t);
                int newVertId = mesh.AddVertex(pos);
                midIds[step - 1] = newVertId;
                createdVertexIds.Add(newVertId);
            }

            edgeSubdivisions[edge] = midIds;
        }

        if (edgeSubdivisions.Count == 0)
        {
            return [];
        }

        foreach (PolygonFace face in mesh.Faces)
        {
            List<int> ids = face.VertexIds;
            int n = ids.Count;
            if (n < 3)
            {
                continue;
            }

            bool modified = false;
            var newLoop = new List<int>(n + cuts * 2);

            for (int i = 0; i < n; i++)
            {
                int curr = ids[i];
                int next = ids[(i + 1) % n];
                newLoop.Add(curr);

                var key = new EdgeKey(curr, next);
                if (edgeSubdivisions.TryGetValue(key, out int[]? midIds))
                {
                    modified = true;
                    if (curr == key.VMin)
                    {
                        for (int k = 0; k < midIds.Length; k++)
                        {
                            newLoop.Add(midIds[k]);
                        }
                    }
                    else
                    {
                        for (int k = midIds.Length - 1; k >= 0; k--)
                        {
                            newLoop.Add(midIds[k]);
                        }
                    }
                }
            }

            if (modified)
            {
                ids.Clear();
                ids.AddRange(newLoop);
            }
        }

        foreach (var (edge, midIds) in edgeSubdivisions)
        {
            var meta = mesh.GetEdgeMetadata(edge);
            if (meta is not null && (meta.Properties.Count > 0 || meta.Tags.Count > 0))
            {
                int prev = edge.VMin;
                for (int k = 0; k < midIds.Length; k++)
                {
                    int next = midIds[k];
                    mesh.GetOrCreateEdgeMetadata(new EdgeKey(prev, next)).MergeFrom(meta);
                    prev = next;
                }
                mesh.GetOrCreateEdgeMetadata(new EdgeKey(prev, edge.VMax)).MergeFrom(meta);
            }
        }

        mesh.InvalidateTopology();
        return createdVertexIds;
    }

    /// <summary>
    /// Extrudes the specified edges by duplicating their vertices, offsetting the duplicates by <paramref name="offset"/>,
    /// and creating new quad <see cref="PolygonFace"/>s bridging each original edge to its extruded copy with consistent winding.
    /// </summary>
    public static IReadOnlyList<(EdgeKey NewEdge, int NewFaceId)> ExtrudeEdges(
        EditableMesh mesh,
        IReadOnlyCollection<EdgeKey> edges,
        Vector3 offset)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(edges);

        if (edges.Count == 0)
        {
            return [];
        }

        TopologyGraph topology = mesh.GetTopology();
        var validEdges = new List<EdgeKey>(edges.Count);
        var seenEdges = new HashSet<EdgeKey>();

        foreach (EdgeKey edge in edges)
        {
            if (edge.VMin == edge.VMax || !seenEdges.Add(edge))
            {
                continue;
            }

            if (mesh.GetVertex(edge.VMin) is not null && mesh.GetVertex(edge.VMax) is not null)
            {
                validEdges.Add(edge);
            }
        }

        if (validEdges.Count == 0)
        {
            return [];
        }

        var duplicatedVertices = new Dictionary<int, int>();

        int GetOrCreateExtrudedVertex(int originalVertexId)
        {
            if (duplicatedVertices.TryGetValue(originalVertexId, out int dupId))
            {
                return dupId;
            }

            MeshVertex? origVert = mesh.GetVertex(originalVertexId);
            dupId = mesh.AddVertex(origVert!.Position + offset);
            duplicatedVertices[originalVertexId] = dupId;
            if (mesh.GetVertex(dupId) is { } newV && origVert is not null && (origVert.Metadata.Properties.Count > 0 || origVert.Metadata.Tags.Count > 0))
            {
                newV.Metadata = origVert.Metadata.Clone();
            }
            return dupId;
        }

        var results = new List<(EdgeKey NewEdge, int NewFaceId)>(validEdges.Count);

        foreach (EdgeKey edge in validEdges)
        {
            // Determine consistent winding from any existing incident half-edge
            int u = edge.VMin;
            int v = edge.VMax;
            string materialId = PolygonFace.DefaultMaterialId;

            DirectedHalfEdge? refHalfEdge = FindReferenceHalfEdge(topology, edge);
            if (refHalfEdge.HasValue)
            {
                // The existing face traverses (Origin -> Target).
                // For the new quad wall to share this edge with opposite half-edge direction (manifold consistency),
                // the new quad wall must traverse (Target -> Origin) -> Origin_new -> Target_new.
                u = refHalfEdge.Value.TargetVertexId;
                v = refHalfEdge.Value.OriginVertexId;

                PolygonFace? adjFace = mesh.GetFace(refHalfEdge.Value.FaceId);
                if (adjFace is not null)
                {
                    materialId = adjFace.MaterialId;
                }
            }

            int uNew = GetOrCreateExtrudedVertex(u);
            int vNew = GetOrCreateExtrudedVertex(v);

            int newFaceId = mesh.AddFace([u, v, vNew, uNew], materialId);
            var newEdgeKey = new EdgeKey(uNew, vNew);
            var edgeMeta = mesh.GetEdgeMetadata(edge);
            if (edgeMeta is not null && (edgeMeta.Properties.Count > 0 || edgeMeta.Tags.Count > 0))
            {
                mesh.GetOrCreateEdgeMetadata(newEdgeKey).MergeFrom(edgeMeta);
            }
            results.Add((newEdgeKey, newFaceId));
        }

        mesh.InvalidateTopology();
        return results;
    }

    private static DirectedHalfEdge? FindReferenceHalfEdge(TopologyGraph topology, EdgeKey edge)
    {
        for (int i = 0; i < topology.HalfEdges.Count; i++)
        {
            DirectedHalfEdge he = topology.HalfEdges[i];
            if (he.EdgeKey == edge)
            {
                return he;
            }
        }

        return null;
    }

    private static List<int> OrderVerticesForFaceLoop(
        EditableMesh mesh,
        TopologyGraph topology,
        List<int> uniqueIds,
        Vector3? preferredNormalDir)
    {
        if (uniqueIds.Count == 3)
        {
            return new List<int>(uniqueIds);
        }

        var vertexSet = new HashSet<int>(uniqueIds);

        // 1. Prefer existing boundary edges connecting the selected vertices
        var boundaryEdges = new List<EdgeKey>();
        var allSubgraphEdges = new List<EdgeKey>();

        foreach (TopologyEdge edge in topology.Edges)
        {
            if (vertexSet.Contains(edge.Key.VMin) && vertexSet.Contains(edge.Key.VMax))
            {
                allSubgraphEdges.Add(edge.Key);
                if (edge.IsBoundary)
                {
                    boundaryEdges.Add(edge.Key);
                }
            }
        }

        if (TryBuildChainFromEdges(boundaryEdges, uniqueIds, out List<int>? boundaryChain) && boundaryChain is not null)
        {
            return boundaryChain;
        }

        if (TryBuildChainFromEdges(allSubgraphEdges, uniqueIds, out List<int>? subgraphChain) && subgraphChain is not null)
        {
            return subgraphChain;
        }

        // 2. Otherwise sort vertices by angle around their centroid in their best-fit plane
        int n = uniqueIds.Count;
        var positions = new Vector3[n];
        Vector3 centroid = Vector3.Zero;

        for (int i = 0; i < n; i++)
        {
            positions[i] = mesh.GetVertex(uniqueIds[i])!.Position;
            centroid += positions[i];
        }
        centroid /= n;

        Vector3 planeNormal = EstimateBestFitPlaneNormal(mesh, uniqueIds, positions, centroid, preferredNormalDir);
        BuildPlaneBasis(positions, centroid, planeNormal, out Vector3 axisU, out Vector3 axisV);

        var indexedAngles = new (int VertexId, float Angle)[n];
        for (int i = 0; i < n; i++)
        {
            Vector3 rel = positions[i] - centroid;
            float u = Vector3.Dot(rel, axisU);
            float v = Vector3.Dot(rel, axisV);
            float angle = MathF.Atan2(v, u);
            indexedAngles[i] = (uniqueIds[i], angle);
        }

        Array.Sort(indexedAngles, (a, b) => a.Angle.CompareTo(b.Angle));

        var sorted = new List<int>(n);
        for (int i = 0; i < n; i++)
        {
            sorted.Add(indexedAngles[i].VertexId);
        }

        return sorted;
    }

    private static bool TryBuildChainFromEdges(
        List<EdgeKey> edges,
        List<int> uniqueIds,
        out List<int>? orderedLoop)
    {
        orderedLoop = null;
        int n = uniqueIds.Count;
        if (edges.Count < n - 1)
        {
            return false;
        }

        var adj = new Dictionary<int, List<int>>(n);
        foreach (int id in uniqueIds)
        {
            adj[id] = [];
        }

        foreach (EdgeKey edge in edges)
        {
            if (adj.TryGetValue(edge.VMin, out List<int>? listMin) &&
                adj.TryGetValue(edge.VMax, out List<int>? listMax))
            {
                if (!listMin.Contains(edge.VMax))
                {
                    listMin.Add(edge.VMax);
                }
                if (!listMax.Contains(edge.VMin))
                {
                    listMax.Add(edge.VMin);
                }
            }
        }

        // Every vertex must have degree 1 or 2, with either all degree 2 (single cycle) or two degree 1 and rest degree 2 (single path)
        int deg1Count = 0;
        int startVertex = uniqueIds[0];

        foreach (int id in uniqueIds)
        {
            int deg = adj[id].Count;
            if (deg == 1)
            {
                deg1Count++;
                startVertex = id;
            }
            else if (deg != 2)
            {
                return false;
            }
        }

        if (deg1Count != 0 && deg1Count != 2)
        {
            return false;
        }

        var visited = new HashSet<int>(n);
        var chain = new List<int>(n);
        int current = startVertex;

        while (true)
        {
            visited.Add(current);
            chain.Add(current);

            int next = -1;
            foreach (int neighbor in adj[current])
            {
                if (!visited.Contains(neighbor))
                {
                    next = neighbor;
                    break;
                }
            }

            if (next < 0)
            {
                break;
            }

            current = next;
        }

        if (chain.Count != n)
        {
            return false;
        }

        orderedLoop = chain;
        return true;
    }

    private static Vector3 EstimateBestFitPlaneNormal(
        EditableMesh mesh,
        List<int> uniqueIds,
        Vector3[] positions,
        Vector3 centroid,
        Vector3? preferredNormalDir)
    {
        if (preferredNormalDir.HasValue && preferredNormalDir.Value.LengthSquared() > 1e-8f)
        {
            return Vector3.Normalize(preferredNormalDir.Value);
        }

        // Find farthest vertex from centroid and second vertex maximizing cross product area
        int bestA = 0;
        float maxDistSq = -1.0f;
        for (int i = 0; i < positions.Length; i++)
        {
            float dSq = Vector3.DistanceSquared(positions[i], centroid);
            if (dSq > maxDistSq)
            {
                maxDistSq = dSq;
                bestA = i;
            }
        }

        Vector3 vecA = positions[bestA] - centroid;
        Vector3 bestCross = Vector3.Zero;
        float maxCrossSq = -1.0f;

        for (int i = 0; i < positions.Length; i++)
        {
            if (i == bestA)
            {
                continue;
            }

            Vector3 cross = Vector3.Cross(vecA, positions[i] - centroid);
            float crossSq = cross.LengthSquared();
            if (crossSq > maxCrossSq)
            {
                maxCrossSq = crossSq;
                bestCross = cross;
            }
        }

        if (maxCrossSq <= 1e-12f)
        {
            return Vector3.UnitY;
        }

        Vector3 planeNormal = Vector3.Normalize(bestCross);

        // Align with input Newell normal if non-degenerate
        var tempFace = new PolygonFace(0, uniqueIds);
        Vector3 inputNewell = tempFace.ComputeNormal(mesh);
        if (Vector3.Dot(planeNormal, inputNewell) < 0.0f)
        {
            planeNormal = -planeNormal;
        }

        return planeNormal;
    }

    private static void BuildPlaneBasis(
        Vector3[] positions,
        Vector3 centroid,
        Vector3 planeNormal,
        out Vector3 axisU,
        out Vector3 axisV)
    {
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 rel = positions[i] - centroid;
            Vector3 projected = rel - (planeNormal * Vector3.Dot(rel, planeNormal));
            float lenSq = projected.LengthSquared();
            if (lenSq > 1e-8f)
            {
                axisU = projected / MathF.Sqrt(lenSq);
                axisV = Vector3.Normalize(Vector3.Cross(planeNormal, axisU));
                return;
            }
        }

        Vector3 helper = MathF.Abs(planeNormal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        axisU = Vector3.Normalize(Vector3.Cross(helper, planeNormal));
        axisV = Vector3.Normalize(Vector3.Cross(planeNormal, axisU));
    }

    private static void OrientLoopWinding(
        EditableMesh mesh,
        TopologyGraph topology,
        List<int> orderedLoop,
        Vector3? preferredNormalDir)
    {
        var tempFace = new PolygonFace(0, orderedLoop);
        Vector3 currentNormal = tempFace.ComputeNormal(mesh);

        if (preferredNormalDir.HasValue && preferredNormalDir.Value.LengthSquared() > 1e-8f)
        {
            if (Vector3.Dot(currentNormal, preferredNormalDir.Value) < 0.0f)
            {
                orderedLoop.Reverse();
            }
            return;
        }

        // Vote using existing half-edges along shared edges (opposite direction = consistent manifold twin)
        int consistentVotes = 0;
        int conflictVotes = 0;
        int n = orderedLoop.Count;

        for (int i = 0; i < n; i++)
        {
            int u = orderedLoop[i];
            int v = orderedLoop[(i + 1) % n];
            var key = new EdgeKey(u, v);

            for (int h = 0; h < topology.HalfEdges.Count; h++)
            {
                DirectedHalfEdge he = topology.HalfEdges[h];
                if (he.EdgeKey != key)
                {
                    continue;
                }

                if (he.OriginVertexId == v && he.TargetVertexId == u)
                {
                    consistentVotes++;
                }
                else if (he.OriginVertexId == u && he.TargetVertexId == v)
                {
                    conflictVotes++;
                }
            }
        }

        if (conflictVotes > consistentVotes)
        {
            orderedLoop.Reverse();
            return;
        }

        if (consistentVotes > 0)
        {
            return;
        }

        // Fallback: check average normal of any faces incident to these vertices
        Vector3 incidentNormalSum = Vector3.Zero;
        var seenFaces = new HashSet<int>();

        for (int i = 0; i < n; i++)
        {
            IReadOnlyList<int> incidentFaceIds = topology.GetVertexIncidentFaces(orderedLoop[i]);
            for (int f = 0; f < incidentFaceIds.Count; f++)
            {
                int fId = incidentFaceIds[f];
                if (seenFaces.Add(fId))
                {
                    PolygonFace? face = mesh.GetFace(fId);
                    if (face is not null)
                    {
                        incidentNormalSum += face.ComputeNormal(mesh);
                    }
                }
            }
        }

        if (incidentNormalSum.LengthSquared() > 1e-6f && Vector3.Dot(currentNormal, incidentNormalSum) < 0.0f)
        {
            orderedLoop.Reverse();
        }
    }

    private static string ResolveInheritedMaterialId(
        EditableMesh mesh,
        TopologyGraph topology,
        List<int> orderedLoop)
    {
        int n = orderedLoop.Count;
        for (int i = 0; i < n; i++)
        {
            int u = orderedLoop[i];
            int v = orderedLoop[(i + 1) % n];
            TopologyEdge? edge = topology.GetEdge(u, v);
            if (edge is not null && edge.IncidentFaceIds.Count > 0)
            {
                PolygonFace? adjFace = mesh.GetFace(edge.IncidentFaceIds[0]);
                if (adjFace is not null)
                {
                    return adjFace.MaterialId;
                }
            }
        }

        for (int i = 0; i < n; i++)
        {
            IReadOnlyList<int> incidentFaces = topology.GetVertexIncidentFaces(orderedLoop[i]);
            if (incidentFaces.Count > 0)
            {
                PolygonFace? adjFace = mesh.GetFace(incidentFaces[0]);
                if (adjFace is not null)
                {
                    return adjFace.MaterialId;
                }
            }
        }

        return PolygonFace.DefaultMaterialId;
    }

    /// <summary>
    /// Extrudes (push-pulls) a single N-gon face or a contiguous/multi-face selection.
    /// Duplicates referenced vertices offset by <paramref name="customOffset"/> or <c>avgNormal * distance</c>,
    /// rewires the selected <see cref="PolygonFace"/>s to the new vertex IDs so they keep the same FaceIds,
    /// and creates quad wall faces along all boundary half-edges of the selection.
    /// Returns the extruded cap face IDs.
    /// </summary>
    public static IReadOnlyList<int> ExtrudeFaces(
        EditableMesh mesh,
        IReadOnlyCollection<int> faceIds,
        float distance,
        Vector3? customOffset = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(faceIds);

        if (faceIds.Count == 0)
        {
            return [];
        }

        var selectedFaces = new List<PolygonFace>(faceIds.Count);
        var selectedFaceSet = new HashSet<int>();

        foreach (int fId in faceIds)
        {
            if (selectedFaceSet.Add(fId))
            {
                PolygonFace? face = mesh.GetFace(fId);
                if (face is not null && face.VertexIds.Count >= 3)
                {
                    selectedFaces.Add(face);
                }
                else
                {
                    selectedFaceSet.Remove(fId);
                }
            }
        }

        if (selectedFaces.Count == 0)
        {
            return [];
        }

        // 1. Compute average normal of selected faces (or use customOffset if provided)
        Vector3 offset;
        if (customOffset.HasValue)
        {
            offset = customOffset.Value;
        }
        else
        {
            Vector3 normalSum = Vector3.Zero;
            for (int i = 0; i < selectedFaces.Count; i++)
            {
                normalSum += selectedFaces[i].ComputeNormal(mesh);
            }

            Vector3 avgNormal = normalSum.LengthSquared() > 1e-12f
                ? Vector3.Normalize(normalSum)
                : selectedFaces[0].ComputeNormal(mesh);

            offset = avgNormal * distance;
        }

        // 2. Identify all boundary half-edges of the selected faces via TopologyGraph
        TopologyGraph topology = mesh.GetTopology();
        var boundaryHalfEdges = CollectBoundaryHalfEdges(topology, selectedFaces, selectedFaceSet);

        // 3. Duplicate all vertices referenced by selectedFaces and offset by offset
        var duplicatedVertices = new Dictionary<int, int>();
        var extrudedCapFaceIds = new List<int>(selectedFaces.Count);

        for (int i = 0; i < selectedFaces.Count; i++)
        {
            PolygonFace face = selectedFaces[i];
            extrudedCapFaceIds.Add(face.Id);
            List<int> vIds = face.VertexIds;

            for (int j = 0; j < vIds.Count; j++)
            {
                int oldVId = vIds[j];
                if (!duplicatedVertices.TryGetValue(oldVId, out int newVId))
                {
                    MeshVertex? oldVert = mesh.GetVertex(oldVId);
                    Vector3 oldPos = oldVert!.Position;
                    newVId = mesh.AddVertex(oldPos + offset);
                    duplicatedVertices[oldVId] = newVId;
                    if (mesh.GetVertex(newVId) is { } newVert && oldVert is not null && (oldVert.Metadata.Properties.Count > 0 || oldVert.Metadata.Tags.Count > 0))
                    {
                        newVert.Metadata = oldVert.Metadata.Clone();
                    }
                }

                // 4. Rewire the selected PolygonFace to use the new duplicated vertex IDs
                vIds[j] = newVId;
            }
        }

        // 5. For each boundary half-edge (u -> v), create a new quad wall [u, v, v_new, u_new]
        for (int i = 0; i < boundaryHalfEdges.Count; i++)
        {
            (int u, int v, string materialId) = boundaryHalfEdges[i];
            int uNew = duplicatedVertices[u];
            int vNew = duplicatedVertices[v];
            mesh.AddFace([u, v, vNew, uNew], materialId);

            var oldEdgeMeta = mesh.GetEdgeMetadata(new EdgeKey(u, v));
            if (oldEdgeMeta is not null && (oldEdgeMeta.Properties.Count > 0 || oldEdgeMeta.Tags.Count > 0))
            {
                mesh.GetOrCreateEdgeMetadata(new EdgeKey(uNew, vNew)).MergeFrom(oldEdgeMeta);
            }
        }

        mesh.ValidateAndRepair();
        return extrudedCapFaceIds;
    }

    /// <summary>
    /// Insets each target face (or connected region of faces when <paramref name="individualFaces"/> is false)
    /// by computing inward miter vectors in the face plane (<c>bisector / sin(halfAngle)</c>, clamped),
    /// creating quad border faces around the perimeter, and rewiring the central <see cref="PolygonFace"/>(s)
    /// to the inner vertices so they stay selected.
    /// Returns the inset inner face IDs.
    /// </summary>
    public static IReadOnlyList<int> InsetFaces(
        EditableMesh mesh,
        IReadOnlyCollection<int> faceIds,
        float thickness,
        float depth = 0f,
        bool individualFaces = false)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(faceIds);

        if (faceIds.Count == 0)
        {
            return [];
        }

        var selectedFaces = new List<PolygonFace>(faceIds.Count);
        var selectedFaceSet = new HashSet<int>();

        foreach (int fId in faceIds)
        {
            if (selectedFaceSet.Add(fId))
            {
                PolygonFace? face = mesh.GetFace(fId);
                if (face is not null && face.VertexIds.Count >= 3)
                {
                    selectedFaces.Add(face);
                }
                else
                {
                    selectedFaceSet.Remove(fId);
                }
            }
        }

        if (selectedFaces.Count == 0)
        {
            return [];
        }

        var resultFaceIds = new List<int>(selectedFaces.Count);
        for (int i = 0; i < selectedFaces.Count; i++)
        {
            resultFaceIds.Add(selectedFaces[i].Id);
        }

        if (individualFaces || selectedFaces.Count == 1)
        {
            for (int i = 0; i < selectedFaces.Count; i++)
            {
                InsetSingleFace(mesh, selectedFaces[i], thickness, depth);
            }

            mesh.ValidateAndRepair();
            return resultFaceIds;
        }

        // Group selected faces into connected components across shared edges
        TopologyGraph topology = mesh.GetTopology();
        var visited = new HashSet<int>();

        for (int i = 0; i < selectedFaces.Count; i++)
        {
            PolygonFace startFace = selectedFaces[i];
            if (!visited.Add(startFace.Id))
            {
                continue;
            }

            var component = new List<PolygonFace> { startFace };
            var componentSet = new HashSet<int> { startFace.Id };
            var queue = new Queue<int>();
            queue.Enqueue(startFace.Id);

            while (queue.Count > 0)
            {
                int currId = queue.Dequeue();
                IReadOnlyList<int> adjIds = topology.GetFaceAdjacentFaces(currId);
                for (int a = 0; a < adjIds.Count; a++)
                {
                    int neighborId = adjIds[a];
                    if (selectedFaceSet.Contains(neighborId) && visited.Add(neighborId))
                    {
                        PolygonFace? neighborFace = mesh.GetFace(neighborId);
                        if (neighborFace is not null)
                        {
                            component.Add(neighborFace);
                            componentSet.Add(neighborId);
                            queue.Enqueue(neighborId);
                        }
                    }
                }
            }

            if (component.Count == 1)
            {
                InsetSingleFace(mesh, component[0], thickness, depth);
            }
            else
            {
                InsetConnectedRegion(mesh, topology, component, componentSet, thickness, depth);
            }
        }

        mesh.ValidateAndRepair();
        return resultFaceIds;
    }

    /// <summary>
    /// Removes the specified faces from <paramref name="mesh"/> and runs <see cref="EditableMesh.ValidateAndRepair"/>
    /// to clean up any vertices that become unreferenced.
    /// </summary>
    public static void DeleteFaces(EditableMesh mesh, IReadOnlyCollection<int> faceIds)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(faceIds);

        if (faceIds.Count == 0)
        {
            return;
        }

        bool removedAny = false;
        foreach (int faceId in faceIds)
        {
            if (mesh.RemoveFace(faceId))
            {
                removedAny = true;
            }
        }

        if (removedAny)
        {
            mesh.ValidateAndRepair();
        }
    }

    /// <summary>
    /// Merges coplanar/contiguous selected faces into a single outer boundary N-gon face by walking
    /// the boundary half-edge loop. Returns the new merged face ID, or null if the selection does not
    /// form a valid single boundary loop.
    /// </summary>
    public static int? DissolveFaces(EditableMesh mesh, IReadOnlyCollection<int> faceIds)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(faceIds);

        if (faceIds.Count == 0)
        {
            return null;
        }

        var selectedFaces = new List<PolygonFace>(faceIds.Count);
        var selectedFaceSet = new HashSet<int>();

        foreach (int fId in faceIds)
        {
            if (selectedFaceSet.Add(fId))
            {
                PolygonFace? face = mesh.GetFace(fId);
                if (face is not null && face.VertexIds.Count >= 3)
                {
                    selectedFaces.Add(face);
                }
                else
                {
                    selectedFaceSet.Remove(fId);
                }
            }
        }

        if (selectedFaces.Count == 0)
        {
            return null;
        }

        if (selectedFaces.Count == 1)
        {
            return selectedFaces[0].Id;
        }

        TopologyGraph topology = mesh.GetTopology();
        var boundaryHalfEdges = CollectBoundaryHalfEdges(topology, selectedFaces, selectedFaceSet);
        if (boundaryHalfEdges.Count < 3)
        {
            return null;
        }

        // Walk the boundary half-edge loop
        var outgoingByOrigin = new Dictionary<int, List<int>>();
        for (int i = 0; i < boundaryHalfEdges.Count; i++)
        {
            (int u, _, _) = boundaryHalfEdges[i];
            if (!outgoingByOrigin.TryGetValue(u, out List<int>? list))
            {
                list = [];
                outgoingByOrigin[u] = list;
            }
            list.Add(i);
        }

        var usedHalfEdge = new bool[boundaryHalfEdges.Count];
        var mergedLoop = new List<int>(boundaryHalfEdges.Count);

        int startEdgeIdx = 0;
        int startVertex = boundaryHalfEdges[startEdgeIdx].U;
        int currentEdgeIdx = startEdgeIdx;

        while (true)
        {
            usedHalfEdge[currentEdgeIdx] = true;
            (int u, int v, _) = boundaryHalfEdges[currentEdgeIdx];
            mergedLoop.Add(u);

            if (v == startVertex)
            {
                break;
            }

            if (!outgoingByOrigin.TryGetValue(v, out List<int>? candidates))
            {
                return null;
            }

            int nextEdgeIdx = -1;
            for (int c = 0; c < candidates.Count; c++)
            {
                int candIdx = candidates[c];
                if (!usedHalfEdge[candIdx])
                {
                    nextEdgeIdx = candIdx;
                    break;
                }
            }

            if (nextEdgeIdx < 0)
            {
                return null;
            }

            currentEdgeIdx = nextEdgeIdx;
        }

        // Ensure all boundary half-edges belong to this single outer loop
        if (mergedLoop.Count != boundaryHalfEdges.Count || mergedLoop.Count < 3)
        {
            return null;
        }

        string materialId = selectedFaces[0].MaterialId;

        for (int i = 0; i < selectedFaces.Count; i++)
        {
            mesh.RemoveFace(selectedFaces[i].Id);
        }

        int newFaceId = mesh.AddFace(mergedLoop, materialId);
        mesh.ValidateAndRepair();
        return newFaceId;
    }

    /// <summary>
    /// Removes the specified vertices and all faces referencing them from <paramref name="mesh"/>,
    /// and runs <see cref="EditableMesh.ValidateAndRepair"/> to prune any resulting orphaned vertices.
    /// </summary>
    public static bool DeleteVertices(EditableMesh mesh, IReadOnlyCollection<int> vertexIds)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(vertexIds);

        if (vertexIds.Count == 0)
        {
            return false;
        }

        var targetSet = new HashSet<int>(vertexIds);
        var facesToRemove = new List<int>();

        foreach (PolygonFace face in mesh.Faces)
        {
            for (int i = 0; i < face.VertexIds.Count; i++)
            {
                if (targetSet.Contains(face.VertexIds[i]))
                {
                    facesToRemove.Add(face.Id);
                    break;
                }
            }
        }

        bool changed = false;
        for (int i = 0; i < facesToRemove.Count; i++)
        {
            if (mesh.RemoveFace(facesToRemove[i]))
            {
                changed = true;
            }
        }

        foreach (int vId in targetSet)
        {
            if (mesh.RemoveVertex(vId))
            {
                changed = true;
            }
        }

        if (changed)
        {
            mesh.ValidateAndRepair();
        }

        return changed;
    }

    /// <summary>
    /// Dissolves the specified vertices without creating holes where possible:
    /// - For valence-2 or boundary vertices on faces with 4+ vertices, removes the vertex from each incident face loop.
    /// - For interior vertices shared by 3+ faces, merges the incident faces into a single N-gon face along their outer boundary.
    /// </summary>
    public static bool DissolveVertices(EditableMesh mesh, IReadOnlyCollection<int> vertexIds)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(vertexIds);

        if (vertexIds.Count == 0)
        {
            return false;
        }

        bool dissolvedAny = false;
        var sortedIds = vertexIds.Distinct().OrderBy(id => id).ToList();

        foreach (int vId in sortedIds)
        {
            if (mesh.GetVertex(vId) is null)
            {
                continue;
            }

            TopologyGraph topology = mesh.GetTopology();
            IReadOnlyList<int> incidentFaceIds = topology.GetVertexIncidentFaces(vId);

            if (incidentFaceIds.Count == 0)
            {
                if (mesh.RemoveVertex(vId))
                {
                    dissolvedAny = true;
                }
                continue;
            }

            if (incidentFaceIds.Count <= 2)
            {
                bool allCanShrink = true;
                for (int i = 0; i < incidentFaceIds.Count; i++)
                {
                    PolygonFace? f = mesh.GetFace(incidentFaceIds[i]);
                    if (f is null || f.VertexIds.Count < 4)
                    {
                        allCanShrink = false;
                        break;
                    }
                }

                if (allCanShrink)
                {
                    for (int i = 0; i < incidentFaceIds.Count; i++)
                    {
                        PolygonFace? f = mesh.GetFace(incidentFaceIds[i]);
                        f?.VertexIds.Remove(vId);
                    }
                    mesh.RemoveVertex(vId);
                    mesh.ValidateAndRepair();
                    dissolvedAny = true;
                }
                else if (incidentFaceIds.Count == 2)
                {
                    int? mergedId = DissolveFaces(mesh, incidentFaceIds);
                    if (mergedId.HasValue)
                    {
                        PolygonFace? mergedFace = mesh.GetFace(mergedId.Value);
                        if (mergedFace is not null && mergedFace.VertexIds.Count >= 4 && mergedFace.VertexIds.Remove(vId))
                        {
                            mesh.ValidateAndRepair();
                        }
                        dissolvedAny = true;
                    }
                }
            }
            else
            {
                int? mergedId = DissolveFaces(mesh, incidentFaceIds);
                if (mergedId.HasValue)
                {
                    PolygonFace? mergedFace = mesh.GetFace(mergedId.Value);
                    if (mergedFace is not null && mergedFace.VertexIds.Count >= 4 && mergedFace.VertexIds.Remove(vId))
                    {
                        mesh.ValidateAndRepair();
                    }
                    dissolvedAny = true;
                }
            }
        }

        return dissolvedAny;
    }

    /// <summary>
    /// Removes all faces incident to the specified edges from <paramref name="mesh"/>
    /// and runs <see cref="EditableMesh.ValidateAndRepair"/> to prune any resulting orphaned vertices.
    /// </summary>
    public static bool DeleteEdges(EditableMesh mesh, IReadOnlyCollection<EdgeKey> edges)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(edges);

        if (edges.Count == 0)
        {
            return false;
        }

        TopologyGraph topology = mesh.GetTopology();
        var facesToRemove = new HashSet<int>();

        foreach (EdgeKey edgeKey in edges)
        {
            mesh.EdgeMetadata.Remove(edgeKey);
            TopologyEdge? topoEdge = topology.GetEdge(edgeKey);
            if (topoEdge is not null)
            {
                for (int i = 0; i < topoEdge.IncidentFaceIds.Count; i++)
                {
                    facesToRemove.Add(topoEdge.IncidentFaceIds[i]);
                }
            }
        }

        if (facesToRemove.Count == 0)
        {
            return false;
        }

        DeleteFaces(mesh, facesToRemove);
        return true;
    }

    /// <summary>
    /// Dissolves the specified interior edges by merging the faces on either side of each edge into
    /// a single N-gon face along their outer boundary, and simplifies any remaining collinear valence-2 endpoint vertices.
    /// Returns the IDs of the newly merged faces.
    /// </summary>
    public static IReadOnlyList<int> DissolveEdges(
        EditableMesh mesh,
        IReadOnlyCollection<EdgeKey> edges,
        bool dissolveCollinearEndpoints = true)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(edges);

        if (edges.Count == 0)
        {
            return [];
        }

        TopologyGraph topology = mesh.GetTopology();
        var endpointVertexIds = new HashSet<int>();
        var faceAdjAcrossSelectedEdges = new Dictionary<int, HashSet<int>>();

        foreach (EdgeKey edgeKey in edges)
        {
            TopologyEdge? topoEdge = topology.GetEdge(edgeKey);
            if (topoEdge is null || topoEdge.IncidentFaceIds.Count != 2)
            {
                continue;
            }

            int fA = topoEdge.IncidentFaceIds[0];
            int fB = topoEdge.IncidentFaceIds[1];
            endpointVertexIds.Add(edgeKey.VMin);
            endpointVertexIds.Add(edgeKey.VMax);

            if (!faceAdjAcrossSelectedEdges.TryGetValue(fA, out HashSet<int>? setA))
            {
                setA = [];
                faceAdjAcrossSelectedEdges[fA] = setA;
            }
            setA.Add(fB);

            if (!faceAdjAcrossSelectedEdges.TryGetValue(fB, out HashSet<int>? setB))
            {
                setB = [];
                faceAdjAcrossSelectedEdges[fB] = setB;
            }
            setB.Add(fA);
        }

        if (faceAdjAcrossSelectedEdges.Count == 0)
        {
            return [];
        }

        // Group faces connected across the selected edges into connected components
        var visitedFaces = new HashSet<int>();
        var mergedFaceIds = new List<int>();

        foreach (int startFaceId in faceAdjAcrossSelectedEdges.Keys)
        {
            if (!visitedFaces.Add(startFaceId))
            {
                continue;
            }

            var component = new List<int> { startFaceId };
            var queue = new Queue<int>();
            queue.Enqueue(startFaceId);

            while (queue.Count > 0)
            {
                int curr = queue.Dequeue();
                if (faceAdjAcrossSelectedEdges.TryGetValue(curr, out HashSet<int>? neighbors))
                {
                    foreach (int next in neighbors)
                    {
                        if (visitedFaces.Add(next))
                        {
                            component.Add(next);
                            queue.Enqueue(next);
                        }
                    }
                }
            }

            if (component.Count >= 2)
            {
                int? newFaceId = DissolveFaces(mesh, component);
                if (newFaceId.HasValue)
                {
                    mergedFaceIds.Add(newFaceId.Value);
                }
            }
        }

        foreach (EdgeKey edgeKey in edges)
        {
            mesh.EdgeMetadata.Remove(edgeKey);
        }

        // Clean up any endpoint vertices that now have valence 2 and lie collinearly along straight edges
        if (dissolveCollinearEndpoints && mergedFaceIds.Count > 0 && endpointVertexIds.Count > 0)
        {
            TopologyGraph updatedTopology = mesh.GetTopology();
            bool removedEndpoint = false;

            foreach (int vId in endpointVertexIds)
            {
                MeshVertex? v = mesh.GetVertex(vId);
                if (v is null)
                {
                    continue;
                }

                IReadOnlyList<int> connectedVerts = updatedTopology.GetVertexConnectedVertices(vId);
                if (connectedVerts.Count != 2)
                {
                    continue;
                }

                MeshVertex? vPrev = mesh.GetVertex(connectedVerts[0]);
                MeshVertex? vNext = mesh.GetVertex(connectedVerts[1]);
                if (vPrev is null || vNext is null)
                {
                    continue;
                }

                Vector3 e1 = v.Position - vPrev.Position;
                Vector3 e2 = vNext.Position - v.Position;
                float len1 = e1.Length();
                float len2 = e2.Length();
                if (len1 <= 1e-5f || len2 <= 1e-5f)
                {
                    continue;
                }

                if (Vector3.Dot(e1 / len1, e2 / len2) < 0.9995f)
                {
                    continue;
                }

                IReadOnlyList<int> incFaces = updatedTopology.GetVertexIncidentFaces(vId);
                bool canRemoveFromAll = incFaces.Count > 0;
                for (int i = 0; i < incFaces.Count; i++)
                {
                    PolygonFace? f = mesh.GetFace(incFaces[i]);
                    if (f is null || f.VertexIds.Count < 4)
                    {
                        canRemoveFromAll = false;
                        break;
                    }
                }

                if (canRemoveFromAll)
                {
                    for (int i = 0; i < incFaces.Count; i++)
                    {
                        mesh.GetFace(incFaces[i])?.VertexIds.Remove(vId);
                    }
                    mesh.RemoveVertex(vId);
                    removedEndpoint = true;
                }
            }

            if (removedEndpoint)
            {
                mesh.ValidateAndRepair();
            }
        }

        return mergedFaceIds;
    }

    /// <summary>
    /// Reverses the vertex winding order on the specified faces.
    /// </summary>
    public static void FlipSelectedFaces(EditableMesh mesh, IReadOnlyCollection<int> faceIds)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(faceIds);

        if (faceIds.Count == 0)
        {
            return;
        }

        mesh.FlipNormals(faceIds);
    }

    /// <summary>
    /// Recalculates consistent face normals across each connected component of faces in <paramref name="mesh"/>:
    /// 1. Runs BFS across shared edges ensuring adjacent faces traverse every shared edge in opposite directions.
    /// 2. Computes the signed volume / outward divergence of the component.
    /// 3. Flips the component if needed so normals point outward (<paramref name="insideOut"/> = false)
    ///    or inward (<paramref name="insideOut"/> = true).
    /// </summary>
    public static void RecalculateNormals(EditableMesh mesh, bool insideOut = false)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        if (mesh.FaceCount == 0)
        {
            return;
        }

        TopologyGraph topology = mesh.GetTopology();
        var visited = new HashSet<int>();

        foreach (PolygonFace seedFace in mesh.Faces)
        {
            if (!visited.Add(seedFace.Id))
            {
                continue;
            }

            var componentFaces = new List<PolygonFace> { seedFace };
            var queue = new Queue<PolygonFace>();
            queue.Enqueue(seedFace);

            // 1. BFS across shared edges to enforce opposite half-edge traversal between adjacent faces
            while (queue.Count > 0)
            {
                PolygonFace current = queue.Dequeue();
                IReadOnlyList<int> neighborIds = topology.GetFaceAdjacentFaces(current.Id);

                for (int i = 0; i < neighborIds.Count; i++)
                {
                    int neighborId = neighborIds[i];
                    if (!visited.Add(neighborId))
                    {
                        continue;
                    }

                    PolygonFace? neighbor = mesh.GetFace(neighborId);
                    if (neighbor is null)
                    {
                        continue;
                    }

                    if (SharesSameEdgeDirection(current, neighbor))
                    {
                        neighbor.VertexIds.Reverse();
                    }

                    componentFaces.Add(neighbor);
                    queue.Enqueue(neighbor);
                }
            }

            // 2. Compute component centroid and signed volume / outward divergence:
            //    Sum(Dot(faceCentroid - componentCenter, faceNormal) * faceArea)
            Vector3 componentCenter = ComputeComponentVertexCentroid(mesh, componentFaces);
            float signedVolume = 0.0f;

            for (int i = 0; i < componentFaces.Count; i++)
            {
                PolygonFace face = componentFaces[i];
                Vector3 faceCentroid = face.ComputeCentroid(mesh);
                Vector3 faceNormal = face.ComputeNormal(mesh);
                float faceArea = ComputeFaceArea(mesh, face);

                signedVolume += Vector3.Dot(faceCentroid - componentCenter, faceNormal) * faceArea;
            }

            // Fallback for single flat face where (faceCentroid - componentCenter) is near zero
            if (MathF.Abs(signedVolume) <= 1e-7f)
            {
                for (int i = 0; i < componentFaces.Count; i++)
                {
                    PolygonFace face = componentFaces[i];
                    Vector3 faceCentroid = face.ComputeCentroid(mesh);
                    Vector3 faceNormal = face.ComputeNormal(mesh);
                    float faceArea = ComputeFaceArea(mesh, face);

                    signedVolume += Vector3.Dot(faceCentroid, faceNormal) * faceArea;
                }
            }

            // 3. Flip component if signedVolume sign disagrees with insideOut target
            bool shouldFlip = (!insideOut && signedVolume < -1e-7f) ||
                              (insideOut && signedVolume > 1e-7f);

            if (shouldFlip)
            {
                for (int i = 0; i < componentFaces.Count; i++)
                {
                    componentFaces[i].VertexIds.Reverse();
                }
            }
        }

        mesh.InvalidateTopology();
    }

    /// <summary>
    /// Inserts a continuous edge loop across the ring of quad faces passing through <paramref name="startEdge"/>,
    /// positioning the cut vertices at <paramref name="slideFactor"/> in <c>[0.01, 0.99]</c> along each crossed edge.
    /// Returns the newly created loop edges.
    /// </summary>
    public static IReadOnlyList<EdgeKey> InsertEdgeLoop(
        EditableMesh mesh,
        EdgeKey startEdge,
        float slideFactor = 0.5f)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        float t = System.Math.Clamp(slideFactor, 0.01f, 0.99f);
        TopologyGraph topology = mesh.GetTopology();
        TopologyEdge? startTopo = topology.GetEdge(startEdge);
        if (startTopo is null)
        {
            return [];
        }

        // Collect quad faces incident to startEdge
        var quadFaceSteps = new List<(int FaceId, EdgeKey EdgeA, EdgeKey EdgeB)>();
        var orientedEdgeMap = new Dictionary<EdgeKey, (int U, int V)>();
        var visitedFaces = new HashSet<int>();

        orientedEdgeMap[startEdge] = (startEdge.VMin, startEdge.VMax);

        void WalkQuadRing(EdgeKey currentEdge, int startFaceId)
        {
            EdgeKey edge = currentEdge;
            int faceId = startFaceId;

            while (faceId >= 0)
            {
                PolygonFace? face = mesh.GetFace(faceId);
                if (face is null || face.VertexIds.Count != 4 || !visitedFaces.Add(faceId))
                {
                    break;
                }

                (int u, int v) = orientedEdgeMap[edge];
                List<int> vIds = face.VertexIds;

                // Find indices of u and v in the quad
                int idxU = vIds.IndexOf(u);
                int idxV = vIds.IndexOf(v);
                if (idxU < 0 || idxV < 0)
                {
                    break;
                }

                int oppU;
                int oppV;
                if ((idxU + 1) % 4 == idxV)
                {
                    oppU = vIds[(idxU + 3) % 4];
                    oppV = vIds[(idxU + 2) % 4];
                }
                else if ((idxV + 1) % 4 == idxU)
                {
                    oppV = vIds[(idxV + 3) % 4];
                    oppU = vIds[(idxV + 2) % 4];
                }
                else
                {
                    break;
                }

                var oppEdge = new EdgeKey(oppU, oppV);
                if (!orientedEdgeMap.ContainsKey(oppEdge))
                {
                    orientedEdgeMap[oppEdge] = (oppU, oppV);
                }

                quadFaceSteps.Add((faceId, edge, oppEdge));

                TopologyEdge? oppTopo = topology.GetEdge(oppEdge);
                if (oppTopo is null)
                {
                    break;
                }

                int nextFaceId = -1;
                for (int i = 0; i < oppTopo.IncidentFaceIds.Count; i++)
                {
                    int candFaceId = oppTopo.IncidentFaceIds[i];
                    if (candFaceId != faceId && !visitedFaces.Contains(candFaceId))
                    {
                        nextFaceId = candFaceId;
                        break;
                    }
                }

                edge = oppEdge;
                faceId = nextFaceId;
            }
        }

        if (startTopo.IncidentFaceIds.Count >= 1)
        {
            WalkQuadRing(startEdge, startTopo.IncidentFaceIds[0]);
        }

        if (startTopo.IncidentFaceIds.Count >= 2 && !visitedFaces.Contains(startTopo.IncidentFaceIds[1]))
        {
            WalkQuadRing(startEdge, startTopo.IncidentFaceIds[1]);
        }

        if (quadFaceSteps.Count == 0)
        {
            return [];
        }

        // Create split vertex on each crossed edge
        var edgeSplitVertex = new Dictionary<EdgeKey, int>(orientedEdgeMap.Count);
        foreach (var (edgeKey, (u, v)) in orientedEdgeMap)
        {
            MeshVertex? vertU = mesh.GetVertex(u);
            MeshVertex? vertV = mesh.GetVertex(v);
            if (vertU is null || vertV is null)
            {
                continue;
            }

            Vector3 splitPos = Vector3.Lerp(vertU.Position, vertV.Position, t);
            int splitVertId = mesh.AddVertex(splitPos);
            edgeSplitVertex[edgeKey] = splitVertId;
        }

        // Insert split vertices into any non-ring faces that share a crossed edge so no T-junctions are left
        foreach (var (edgeKey, splitVertId) in edgeSplitVertex)
        {
            TopologyEdge? topoEdge = topology.GetEdge(edgeKey);
            if (topoEdge is null)
            {
                continue;
            }

            for (int i = 0; i < topoEdge.IncidentFaceIds.Count; i++)
            {
                int incFaceId = topoEdge.IncidentFaceIds[i];
                if (visitedFaces.Contains(incFaceId))
                {
                    continue;
                }

                PolygonFace? nonRingFace = mesh.GetFace(incFaceId);
                if (nonRingFace is null)
                {
                    continue;
                }

                List<int> loop = nonRingFace.VertexIds;
                int count = loop.Count;
                for (int j = 0; j < count; j++)
                {
                    int a = loop[j];
                    int b = loop[(j + 1) % count];
                    if (new EdgeKey(a, b) == edgeKey)
                    {
                        loop.Insert(j + 1, splitVertId);
                        break;
                    }
                }
            }
        }

        // Split each quad face in the ring into 2 quads and collect the new loop edges
        var createdLoopEdges = new List<EdgeKey>(quadFaceSteps.Count);

        for (int i = 0; i < quadFaceSteps.Count; i++)
        {
            (int faceId, EdgeKey edgeA, EdgeKey edgeB) = quadFaceSteps[i];
            PolygonFace? face = mesh.GetFace(faceId);
            if (face is null || face.VertexIds.Count != 4)
            {
                continue;
            }

            if (!edgeSplitVertex.TryGetValue(edgeA, out int mA) ||
                !edgeSplitVertex.TryGetValue(edgeB, out int mB))
            {
                continue;
            }

            List<int> vIds = face.VertexIds;
            // Rotate quad loop so edgeA is (vIds[0], vIds[1]) and edgeB is (vIds[2], vIds[3])
            int startIdx = -1;
            for (int j = 0; j < 4; j++)
            {
                if (new EdgeKey(vIds[j], vIds[(j + 1) % 4]) == edgeA)
                {
                    startIdx = j;
                    break;
                }
            }

            if (startIdx < 0)
            {
                continue;
            }

            int v0 = vIds[startIdx];
            int v1 = vIds[(startIdx + 1) % 4];
            int v2 = vIds[(startIdx + 2) % 4];
            int v3 = vIds[(startIdx + 3) % 4];

            string matId = face.MaterialId;
            var uvClone = face.UvMapping.Clone();
            var metaClone = face.Metadata.Clone();

            // Replace original quad in place with first half
            vIds.Clear();
            vIds.Add(v0);
            vIds.Add(mA);
            vIds.Add(mB);
            vIds.Add(v3);

            // Add second half quad preserving material, UV, and metadata
            mesh.AddFace([mA, v1, v2, mB], matId, uvClone, metaClone);
            createdLoopEdges.Add(new EdgeKey(mA, mB));
        }

        mesh.ValidateAndRepair();
        return createdLoopEdges;
    }

    /// <summary>
    /// Bevels / chamfers the specified edges on <paramref name="mesh"/> by <paramref name="width"/>
    /// with <paramref name="segments"/> subdivision strips (<c>1</c> = flat chamfer, <c>&gt;1</c> = rounded bevel).
    /// Returns the IDs of all newly created bevel and corner faces.
    /// </summary>
    public static IReadOnlyList<int> BevelEdges(
        EditableMesh mesh,
        IReadOnlyCollection<EdgeKey> edges,
        float width,
        int segments = 1)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(edges);

        if (edges.Count == 0 || width <= 1e-5f)
        {
            return [];
        }

        int segs = System.Math.Clamp(segments, 1, 16);
        TopologyGraph topology = mesh.GetTopology();

        var selectedEdges = new HashSet<EdgeKey>();
        foreach (EdgeKey key in edges)
        {
            TopologyEdge? te = topology.GetEdge(key);
            if (te is not null && te.IncidentFaceIds.Count == 2)
            {
                selectedEdges.Add(key);
            }
        }

        if (selectedEdges.Count == 0)
        {
            return [];
        }

        // Cache original vertex positions and face normals before any topological mutations
        var origPositions = new Dictionary<int, Vector3>(mesh.VertexCount);
        foreach (MeshVertex v in mesh.Vertices)
        {
            origPositions[v.Id] = v.Position;
        }

        var origFaceNormals = new Dictionary<int, Vector3>(mesh.FaceCount);
        var origFaceCentroids = new Dictionary<int, Vector3>(mesh.FaceCount);
        foreach (PolygonFace f in mesh.Faces)
        {
            origFaceNormals[f.Id] = f.ComputeNormal(mesh);
            origFaceCentroids[f.Id] = f.ComputeCentroid(mesh);
        }

        // Collect affected vertices and clamp bevel width per vertex to 45% of its shortest incident edge
        var affectedVertices = new HashSet<int>();
        foreach (EdgeKey e in selectedEdges)
        {
            affectedVertices.Add(e.VMin);
            affectedVertices.Add(e.VMax);
        }

        // For each (EdgeKey, endpointVertexId), we determine the bevel chain of (segs + 1) vertices
        // ordered from IncidentFaceIds[0] to IncidentFaceIds[1].
        var edgeEndpointChains = new Dictionary<(EdgeKey Edge, int VertexId), int[]>();
        // For each (FaceId, VertexId), the replacement vertex IDs to substitute in FaceId's VertexIds loop
        var faceVertexReplacements = new Dictionary<(int FaceId, int VertexId), int[]>();
        var createdFaceIds = new List<int>();
        var pendingCornerFaces = new List<(List<int> Loop, Vector3 ExpectedNormal, string MaterialId)>();

        foreach (int vId in affectedVertices)
        {
            Vector3 pV = origPositions[vId];
            IReadOnlyList<int> connectedVerts = topology.GetVertexConnectedVertices(vId);
            float minEdgeLen = float.PositiveInfinity;
            var incidentSelectedEdges = new List<EdgeKey>();

            for (int i = 0; i < connectedVerts.Count; i++)
            {
                int nId = connectedVerts[i];
                float len = Vector3.Distance(pV, origPositions[nId]);
                if (len < minEdgeLen)
                {
                    minEdgeLen = len;
                }

                var ek = new EdgeKey(vId, nId);
                if (selectedEdges.Contains(ek))
                {
                    incidentSelectedEdges.Add(ek);
                }
            }

            float localWidth = System.Math.Min(width, minEdgeLen * 0.45f);
            if (localWidth <= 1e-5f)
            {
                localWidth = width;
            }

            int m = incidentSelectedEdges.Count;
            if (m == 1)
            {
                EdgeKey bevEdge = incidentSelectedEdges[0];
                TopologyEdge topoEdge = topology.GetEdge(bevEdge)!;
                int f0Id = topoEdge.IncidentFaceIds[0];
                int f1Id = topoEdge.IncidentFaceIds[1];

                Vector3 pos0 = ComputeVertexOffsetInFaceAwayFromEdge(mesh, f0Id, vId, bevEdge, origPositions, origFaceNormals[f0Id], localWidth);
                Vector3 pos1 = ComputeVertexOffsetInFaceAwayFromEdge(mesh, f1Id, vId, bevEdge, origPositions, origFaceNormals[f1Id], localWidth);

                int[] chain = CreateBevelArcVertices(mesh, vId, pV, pos0, pos1, segs);
                edgeEndpointChains[(bevEdge, vId)] = chain;

                faceVertexReplacements[(f0Id, vId)] = [chain[0]];
                faceVertexReplacements[(f1Id, vId)] = [chain[segs]];

                // Any other face incident to vId (the end face(s) at the termination of bevEdge) gets the chain inserted
                IReadOnlyList<int> incFaces = topology.GetVertexIncidentFaces(vId);
                for (int i = 0; i < incFaces.Count; i++)
                {
                    int fId = incFaces[i];
                    if (fId == f0Id || fId == f1Id)
                    {
                        continue;
                    }

                    PolygonFace? endFace = mesh.GetFace(fId);
                    if (endFace is null)
                    {
                        continue;
                    }

                    // Check whether in endFace, the edge shared with f0 comes before or after vId
                    bool f0BeforeF1 = IsFaceSharedWithPreviousEdgeAtVertex(endFace, vId, mesh.GetFace(f0Id)!);
                    if (f0BeforeF1)
                    {
                        faceVertexReplacements[(fId, vId)] = chain;
                    }
                    else
                    {
                        int[] reversed = new int[chain.Length];
                        for (int k = 0; k < chain.Length; k++)
                        {
                            reversed[k] = chain[chain.Length - 1 - k];
                        }
                        faceVertexReplacements[(fId, vId)] = reversed;
                    }
                }
            }
            else if (m == 2)
            {
                // Partition incident faces around vId into two sectors separated by the 2 beveled edges
                EdgeKey eA = incidentSelectedEdges[0];
                EdgeKey eB = incidentSelectedEdges[1];
                (List<int> sector0, List<int> sector1) = PartitionVertexFacesIntoTwoSectors(topology, vId, eA, eB);

                Vector3 pos0 = ComputeSectorVertexOffset(mesh, sector0, vId, eA, eB, origPositions, origFaceNormals, localWidth);
                Vector3 pos1 = ComputeSectorVertexOffset(mesh, sector1, vId, eA, eB, origPositions, origFaceNormals, localWidth);

                int[] sharedChain = CreateBevelArcVertices(mesh, vId, pV, pos0, pos1, segs);

                for (int i = 0; i < sector0.Count; i++)
                {
                    faceVertexReplacements[(sector0[i], vId)] = [sharedChain[0]];
                }
                for (int i = 0; i < sector1.Count; i++)
                {
                    faceVertexReplacements[(sector1[i], vId)] = [sharedChain[segs]];
                }

                foreach (EdgeKey bevEdge in incidentSelectedEdges)
                {
                    TopologyEdge te = topology.GetEdge(bevEdge)!;
                    int f0Id = te.IncidentFaceIds[0];
                    bool f0InSector0 = sector0.Contains(f0Id);
                    if (f0InSector0)
                    {
                        edgeEndpointChains[(bevEdge, vId)] = sharedChain;
                    }
                    else
                    {
                        int[] rev = new int[sharedChain.Length];
                        for (int k = 0; k < sharedChain.Length; k++)
                        {
                            rev[k] = sharedChain[sharedChain.Length - 1 - k];
                        }
                        edgeEndpointChains[(bevEdge, vId)] = rev;
                    }
                }
            }
            else
            {
                // m >= 3 beveled edges meeting at vId:
                // Order the incident faces around vId cyclically and group into m sectors separated by the m selected edges
                List<(List<int> SectorFaces, EdgeKey PrevEdge, EdgeKey NextEdge)> sectors =
                    OrderVertexSectorsAroundCorner(mesh, topology, vId, selectedEdges, origFaceNormals);

                int sectorCount = sectors.Count;
                var sectorApexVerts = new int[sectorCount];
                var sectorPositions = new Vector3[sectorCount];

                for (int s = 0; s < sectorCount; s++)
                {
                    var (sectorFaces, prevEdge, nextEdge) = sectors[s];
                    Vector3 offsetPos = ComputeSectorVertexOffset(mesh, sectorFaces, vId, prevEdge, nextEdge, origPositions, origFaceNormals, localWidth);
                    sectorPositions[s] = offsetPos;
                    int newV = mesh.AddVertex(offsetPos, mesh.GetVertex(vId)?.Metadata.Clone());
                    sectorApexVerts[s] = newV;

                    for (int f = 0; f < sectorFaces.Count; f++)
                    {
                        faceVertexReplacements[(sectorFaces[f], vId)] = [newV];
                    }
                }

                var cornerLoop = new List<int>();
                for (int s = 0; s < sectorCount; s++)
                {
                    int nextS = (s + 1) % sectorCount;
                    EdgeKey sharedBevEdge = sectors[s].NextEdge;

                    int vStart = sectorApexVerts[s];
                    int vEnd = sectorApexVerts[nextS];
                    Vector3 pStart = sectorPositions[s];
                    Vector3 pEnd = sectorPositions[nextS];

                    var chainFromStoNext = new int[segs + 1];
                    chainFromStoNext[0] = vStart;
                    chainFromStoNext[segs] = vEnd;
                    cornerLoop.Add(vStart);

                    for (int k = 1; k < segs; k++)
                    {
                        float frac = (float)k / segs;
                        Vector3 midPos = Vector3.Lerp(pStart, pEnd, frac);
                        int midV = mesh.AddVertex(midPos, mesh.GetVertex(vId)?.Metadata.Clone());
                        chainFromStoNext[k] = midV;
                        cornerLoop.Add(midV);
                    }

                    TopologyEdge te = topology.GetEdge(sharedBevEdge)!;
                    int f0Id = te.IncidentFaceIds[0];
                    bool f0InSectorS = sectors[s].SectorFaces.Contains(f0Id);
                    if (f0InSectorS)
                    {
                        edgeEndpointChains[(sharedBevEdge, vId)] = chainFromStoNext;
                    }
                    else
                    {
                        int[] rev = new int[chainFromStoNext.Length];
                        for (int k = 0; k < chainFromStoNext.Length; k++)
                        {
                            rev[k] = chainFromStoNext[chainFromStoNext.Length - 1 - k];
                        }
                        edgeEndpointChains[(sharedBevEdge, vId)] = rev;
                    }
                }

                if (cornerLoop.Count >= 3)
                {
                    Vector3 avgNormal = Vector3.Zero;
                    IReadOnlyList<int> incFaces = topology.GetVertexIncidentFaces(vId);
                    string matId = PolygonFace.DefaultMaterialId;
                    for (int i = 0; i < incFaces.Count; i++)
                    {
                        avgNormal += origFaceNormals[incFaces[i]];
                        if (i == 0 && mesh.GetFace(incFaces[i]) is { } firstFace)
                        {
                            matId = firstFace.MaterialId;
                        }
                    }

                    pendingCornerFaces.Add((cornerLoop, avgNormal, matId));
                }
            }
        }

        // 1. Update all existing faces with their replacement vertices
        foreach (PolygonFace face in mesh.Faces)
        {
            List<int> vIds = face.VertexIds;
            var updatedLoop = new List<int>(vIds.Count + 4);
            for (int i = 0; i < vIds.Count; i++)
            {
                int origV = vIds[i];
                if (faceVertexReplacements.TryGetValue((face.Id, origV), out int[]? replacements))
                {
                    for (int r = 0; r < replacements.Length; r++)
                    {
                        updatedLoop.Add(replacements[r]);
                    }
                }
                else
                {
                    updatedLoop.Add(origV);
                }
            }

            vIds.Clear();
            vIds.AddRange(updatedLoop);
        }

        // 2. Create `segs` bevel quad faces along each beveled edge
        foreach (EdgeKey bevEdge in selectedEdges)
        {
            if (!edgeEndpointChains.TryGetValue((bevEdge, bevEdge.VMin), out int[]? chainMin) ||
                !edgeEndpointChains.TryGetValue((bevEdge, bevEdge.VMax), out int[]? chainMax))
            {
                continue;
            }

            TopologyEdge te = topology.GetEdge(bevEdge)!;
            int f0Id = te.IncidentFaceIds[0];
            int f1Id = te.IncidentFaceIds[1];
            PolygonFace? f0 = mesh.GetFace(f0Id);
            string matId = f0?.MaterialId ?? PolygonFace.DefaultMaterialId;
            Vector3 expectedNormal = origFaceNormals[f0Id] + origFaceNormals[f1Id];
            if (expectedNormal.LengthSquared() <= 1e-8f)
            {
                expectedNormal = origFaceNormals[f0Id];
            }

            for (int k = 0; k < segs; k++)
            {
                int a0 = chainMin[k];
                int b0 = chainMax[k];
                int b1 = chainMax[k + 1];
                int a1 = chainMin[k + 1];

                var quadLoop = new List<int> { a0, b0, b1, a1 };
                EnsureLoopNormalMatches(mesh, quadLoop, expectedNormal);
                int newFaceId = mesh.AddFace(quadLoop, matId);
                createdFaceIds.Add(newFaceId);
            }
        }

        // 3. Create corner faces at valence-3+ beveled vertices
        for (int i = 0; i < pendingCornerFaces.Count; i++)
        {
            var (cornerLoop, expectedNormal, matId) = pendingCornerFaces[i];
            EnsureLoopNormalMatches(mesh, cornerLoop, expectedNormal);
            int cornerFaceId = mesh.AddFace(cornerLoop, matId);
            createdFaceIds.Add(cornerFaceId);
        }

        mesh.ValidateAndRepair();
        return createdFaceIds;
    }

    private static int[] CreateBevelArcVertices(
        EditableMesh mesh,
        int origVertexId,
        Vector3 apexPos,
        Vector3 pos0,
        Vector3 pos1,
        int segments)
    {
        var metaTemplate = mesh.GetVertex(origVertexId)?.Metadata;
        var chain = new int[segments + 1];
        chain[0] = mesh.AddVertex(pos0, metaTemplate?.Clone());
        chain[segments] = mesh.AddVertex(pos1, metaTemplate?.Clone());

        if (segments > 1)
        {
            Vector3 mid = (pos0 + pos1) * 0.5f;
            Vector3 control = Vector3.Lerp(mid, apexPos, 0.5f);
            for (int k = 1; k < segments; k++)
            {
                float t = (float)k / segments;
                float u = 1.0f - t;
                Vector3 arcPos = (u * u * pos0) + (2.0f * u * t * control) + (t * t * pos1);
                chain[k] = mesh.AddVertex(arcPos, metaTemplate?.Clone());
            }
        }

        return chain;
    }

    private static Vector3 ComputeVertexOffsetInFaceAwayFromEdge(
        EditableMesh mesh,
        int faceId,
        int vertexId,
        EdgeKey beveledEdge,
        Dictionary<int, Vector3> origPositions,
        Vector3 faceNormal,
        float width)
    {
        PolygonFace? face = mesh.GetFace(faceId);
        Vector3 pCurr = origPositions[vertexId];
        if (face is null)
        {
            return pCurr;
        }

        List<int> vIds = face.VertexIds;
        int n = vIds.Count;
        int idx = vIds.IndexOf(vertexId);
        if (idx < 0)
        {
            return pCurr;
        }

        int prevId = vIds[(idx - 1 + n) % n];
        int nextId = vIds[(idx + 1) % n];

        int otherVertId = new EdgeKey(vertexId, prevId) == beveledEdge ? nextId : prevId;
        Vector3 dir = origPositions[otherVertId] - pCurr;
        if (dir.LengthSquared() > 1e-10f)
        {
            return pCurr + (Vector3.Normalize(dir) * width);
        }

        int bevOtherId = beveledEdge.GetOtherVertex(vertexId);
        Vector3 edgeDir = Vector3.Normalize(origPositions[bevOtherId] - pCurr);
        Vector3 inPlanePerp = Vector3.Cross(faceNormal, edgeDir);
        if (inPlanePerp.LengthSquared() > 1e-10f)
        {
            inPlanePerp = Vector3.Normalize(inPlanePerp);
        }
        return pCurr + (inPlanePerp * width);
    }

    private static bool IsFaceSharedWithPreviousEdgeAtVertex(PolygonFace endFace, int vertexId, PolygonFace targetNeighborFace)
    {
        List<int> vIds = endFace.VertexIds;
        int n = vIds.Count;
        int idx = vIds.IndexOf(vertexId);
        if (idx < 0)
        {
            return true;
        }

        int prevVert = vIds[(idx - 1 + n) % n];
        var prevEdge = new EdgeKey(prevVert, vertexId);

        List<int> targetIds = targetNeighborFace.VertexIds;
        int tCount = targetIds.Count;
        for (int i = 0; i < tCount; i++)
        {
            if (new EdgeKey(targetIds[i], targetIds[(i + 1) % tCount]) == prevEdge)
            {
                return true;
            }
        }

        return false;
    }

    private static (List<int> Sector0, List<int> Sector1) PartitionVertexFacesIntoTwoSectors(
        TopologyGraph topology,
        int vertexId,
        EdgeKey edgeA,
        EdgeKey edgeB)
    {
        IReadOnlyList<int> allFaces = topology.GetVertexIncidentFaces(vertexId);
        var sector0 = new List<int>();
        var sector1 = new List<int>();
        if (allFaces.Count == 0)
        {
            return (sector0, sector1);
        }

        var visited = new HashSet<int>();
        var queue = new Queue<int>();
        int seed = allFaces[0];
        visited.Add(seed);
        sector0.Add(seed);
        queue.Enqueue(seed);

        while (queue.Count > 0)
        {
            int currFace = queue.Dequeue();
            for (int i = 0; i < allFaces.Count; i++)
            {
                int otherFace = allFaces[i];
                if (visited.Contains(otherFace))
                {
                    continue;
                }

                EdgeKey? sharedAtV = FindSharedEdgeAtVertex(topology, vertexId, currFace, otherFace);
                if (sharedAtV.HasValue && sharedAtV.Value != edgeA && sharedAtV.Value != edgeB)
                {
                    visited.Add(otherFace);
                    sector0.Add(otherFace);
                    queue.Enqueue(otherFace);
                }
            }
        }

        for (int i = 0; i < allFaces.Count; i++)
        {
            if (!visited.Contains(allFaces[i]))
            {
                sector1.Add(allFaces[i]);
            }
        }

        return (sector0, sector1);
    }

    private static EdgeKey? FindSharedEdgeAtVertex(TopologyGraph topology, int vertexId, int faceA, int faceB)
    {
        IReadOnlyList<int> heA = topology.GetFaceHalfEdgeIds(faceA);
        IReadOnlyList<int> heB = topology.GetFaceHalfEdgeIds(faceB);
        for (int i = 0; i < heA.Count; i++)
        {
            EdgeKey ekA = topology.HalfEdges[heA[i]].EdgeKey;
            if (!ekA.ContainsVertex(vertexId))
            {
                continue;
            }

            for (int j = 0; j < heB.Count; j++)
            {
                if (topology.HalfEdges[heB[j]].EdgeKey == ekA)
                {
                    return ekA;
                }
            }
        }

        return null;
    }

    private static List<(List<int> SectorFaces, EdgeKey PrevEdge, EdgeKey NextEdge)> OrderVertexSectorsAroundCorner(
        EditableMesh mesh,
        TopologyGraph topology,
        int vertexId,
        HashSet<EdgeKey> selectedEdges,
        Dictionary<int, Vector3> origFaceNormals)
    {
        IReadOnlyList<int> incFaces = topology.GetVertexIncidentFaces(vertexId);
        var remaining = new HashSet<int>(incFaces);
        var rawSectors = new List<(List<int> Faces, EdgeKey EdgeA, EdgeKey EdgeB)>();

        while (remaining.Count > 0)
        {
            int seed = remaining.First();
            remaining.Remove(seed);
            var comp = new List<int> { seed };
            var queue = new Queue<int>();
            queue.Enqueue(seed);

            while (queue.Count > 0)
            {
                int curr = queue.Dequeue();
                foreach (int cand in remaining.ToList())
                {
                    EdgeKey? shared = FindSharedEdgeAtVertex(topology, vertexId, curr, cand);
                    if (shared.HasValue && !selectedEdges.Contains(shared.Value))
                    {
                        remaining.Remove(cand);
                        comp.Add(cand);
                        queue.Enqueue(cand);
                    }
                }
            }

            var boundarySelectedEdges = new List<EdgeKey>();
            for (int i = 0; i < comp.Count; i++)
            {
                PolygonFace f = mesh.GetFace(comp[i])!;
                int n = f.VertexIds.Count;
                int idx = f.VertexIds.IndexOf(vertexId);
                if (idx < 0) continue;

                var ePrev = new EdgeKey(f.VertexIds[(idx - 1 + n) % n], vertexId);
                var eNext = new EdgeKey(vertexId, f.VertexIds[(idx + 1) % n]);
                if (selectedEdges.Contains(ePrev) && !boundarySelectedEdges.Contains(ePrev))
                {
                    boundarySelectedEdges.Add(ePrev);
                }
                if (selectedEdges.Contains(eNext) && !boundarySelectedEdges.Contains(eNext))
                {
                    boundarySelectedEdges.Add(eNext);
                }
            }

            if (boundarySelectedEdges.Count >= 2)
            {
                rawSectors.Add((comp, boundarySelectedEdges[0], boundarySelectedEdges[1]));
            }
        }

        if (rawSectors.Count == 0)
        {
            return [];
        }

        var ordered = new List<(List<int> SectorFaces, EdgeKey PrevEdge, EdgeKey NextEdge)>(rawSectors.Count);
        var used = new bool[rawSectors.Count];

        var first = rawSectors[0];
        used[0] = true;
        PolygonFace firstFace = mesh.GetFace(first.Faces[0])!;
        int firstIdx = firstFace.VertexIds.IndexOf(vertexId);
        int firstCount = firstFace.VertexIds.Count;
        var incomingEdge = new EdgeKey(firstFace.VertexIds[(firstIdx - 1 + firstCount) % firstCount], vertexId);
        EdgeKey currPrev = first.EdgeA == incomingEdge ? first.EdgeA : first.EdgeB;
        EdgeKey currNext = currPrev == first.EdgeA ? first.EdgeB : first.EdgeA;
        ordered.Add((first.Faces, currPrev, currNext));

        for (int step = 1; step < rawSectors.Count; step++)
        {
            EdgeKey targetPrev = ordered[^1].NextEdge;
            int foundIdx = -1;
            for (int i = 0; i < rawSectors.Count; i++)
            {
                if (!used[i] && (rawSectors[i].EdgeA == targetPrev || rawSectors[i].EdgeB == targetPrev))
                {
                    foundIdx = i;
                    break;
                }
            }

            if (foundIdx < 0)
            {
                break;
            }

            used[foundIdx] = true;
            var sec = rawSectors[foundIdx];
            EdgeKey nextEdge = sec.EdgeA == targetPrev ? sec.EdgeB : sec.EdgeA;
            ordered.Add((sec.Faces, targetPrev, nextEdge));
        }

        return ordered;
    }

    private static Vector3 ComputeSectorVertexOffset(
        EditableMesh mesh,
        List<int> sectorFaces,
        int vertexId,
        EdgeKey bevEdgeA,
        EdgeKey bevEdgeB,
        Dictionary<int, Vector3> origPositions,
        Dictionary<int, Vector3> origFaceNormals,
        float width)
    {
        Vector3 pCurr = origPositions[vertexId];
        if (sectorFaces.Count == 0)
        {
            return pCurr;
        }

        for (int i = 0; i < sectorFaces.Count; i++)
        {
            PolygonFace? f = mesh.GetFace(sectorFaces[i]);
            if (f is null) continue;
            int n = f.VertexIds.Count;
            int idx = f.VertexIds.IndexOf(vertexId);
            if (idx < 0) continue;

            int prevV = f.VertexIds[(idx - 1 + n) % n];
            int nextV = f.VertexIds[(idx + 1) % n];

            var ePrev = new EdgeKey(prevV, vertexId);
            if (ePrev != bevEdgeA && ePrev != bevEdgeB)
            {
                Vector3 dir = origPositions[prevV] - pCurr;
                if (dir.LengthSquared() > 1e-10f)
                {
                    return pCurr + (Vector3.Normalize(dir) * width);
                }
            }

            var eNext = new EdgeKey(vertexId, nextV);
            if (eNext != bevEdgeA && eNext != bevEdgeB)
            {
                Vector3 dir = origPositions[nextV] - pCurr;
                if (dir.LengthSquared() > 1e-10f)
                {
                    return pCurr + (Vector3.Normalize(dir) * width);
                }
            }
        }

        int faceId = sectorFaces[0];
        PolygonFace? singleFace = mesh.GetFace(faceId);
        if (singleFace is null)
        {
            return pCurr;
        }

        int count = singleFace.VertexIds.Count;
        int vIdx = singleFace.VertexIds.IndexOf(vertexId);
        if (vIdx < 0)
        {
            return pCurr;
        }

        Vector3 pPrev = origPositions[singleFace.VertexIds[(vIdx - 1 + count) % count]];
        Vector3 pNext = origPositions[singleFace.VertexIds[(vIdx + 1) % count]];
        Vector3 normal = origFaceNormals[faceId];
        Vector3 miter = ComputeInwardMiter(pPrev, pCurr, pNext, normal);
        return pCurr + (miter * width);
    }

    private static void EnsureLoopNormalMatches(EditableMesh mesh, List<int> loop, Vector3 expectedNormal)
    {
        if (loop.Count < 3 || expectedNormal.LengthSquared() <= 1e-10f)
        {
            return;
        }

        Vector3 newell = Vector3.Zero;
        for (int i = 0; i < loop.Count; i++)
        {
            Vector3 curr = mesh.GetVertex(loop[i])!.Position;
            Vector3 next = mesh.GetVertex(loop[(i + 1) % loop.Count])!.Position;
            newell.X += (curr.Y - next.Y) * (curr.Z + next.Z);
            newell.Y += (curr.Z - next.Z) * (curr.X + next.X);
            newell.Z += (curr.X - next.X) * (curr.Y + next.Y);
        }

        if (Vector3.Dot(newell, expectedNormal) < 0.0f)
        {
            loop.Reverse();
        }
    }

    private static List<(int U, int V, string MaterialId)> CollectBoundaryHalfEdges(
        TopologyGraph topology,
        List<PolygonFace> selectedFaces,
        HashSet<int> selectedFaceSet)
    {
        var boundaryHalfEdges = new List<(int U, int V, string MaterialId)>();

        for (int i = 0; i < selectedFaces.Count; i++)
        {
            PolygonFace face = selectedFaces[i];
            IReadOnlyList<int> heIds = topology.GetFaceHalfEdgeIds(face.Id);

            for (int h = 0; h < heIds.Count; h++)
            {
                DirectedHalfEdge he = topology.HalfEdges[heIds[h]];
                bool isSharedInterior = false;

                if (he.TwinHalfEdgeId.HasValue)
                {
                    DirectedHalfEdge twin = topology.HalfEdges[he.TwinHalfEdgeId.Value];
                    if (selectedFaceSet.Contains(twin.FaceId))
                    {
                        isSharedInterior = true;
                    }
                }

                if (!isSharedInterior)
                {
                    TopologyEdge? topoEdge = topology.GetEdge(he.EdgeKey);
                    if (topoEdge is not null)
                    {
                        for (int f = 0; f < topoEdge.IncidentFaceIds.Count; f++)
                        {
                            int incFaceId = topoEdge.IncidentFaceIds[f];
                            if (incFaceId != face.Id && selectedFaceSet.Contains(incFaceId))
                            {
                                isSharedInterior = true;
                                break;
                            }
                        }
                    }
                }

                if (!isSharedInterior)
                {
                    boundaryHalfEdges.Add((he.OriginVertexId, he.TargetVertexId, face.MaterialId));
                }
            }
        }

        return boundaryHalfEdges;
    }

    private static void InsetSingleFace(
        EditableMesh mesh,
        PolygonFace face,
        float thickness,
        float depth)
    {
        List<int> vIds = face.VertexIds;
        int n = vIds.Count;
        if (n < 3)
        {
            return;
        }

        Vector3 faceNormal = face.ComputeNormal(mesh);
        var positions = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            positions[i] = mesh.GetVertex(vIds[i])!.Position;
        }

        var innerIds = new int[n];
        for (int i = 0; i < n; i++)
        {
            Vector3 pPrev = positions[(i - 1 + n) % n];
            Vector3 pCurr = positions[i];
            Vector3 pNext = positions[(i + 1) % n];

            Vector3 miter = ComputeInwardMiter(pPrev, pCurr, pNext, faceNormal);
            Vector3 innerPos = pCurr + (miter * thickness) + (faceNormal * depth);
            innerIds[i] = mesh.AddVertex(innerPos);
            MeshVertex? origVert = mesh.GetVertex(vIds[i]);
            if (mesh.GetVertex(innerIds[i]) is { } inVert && origVert is not null && (origVert.Metadata.Properties.Count > 0 || origVert.Metadata.Tags.Count > 0))
            {
                inVert.Metadata = origVert.Metadata.Clone();
            }
        }

        string materialId = face.MaterialId;
        var originalIds = new int[n];
        vIds.CopyTo(originalIds);

        // Rewire central face to [v_0_inner, v_1_inner, ...]
        for (int i = 0; i < n; i++)
        {
            vIds[i] = innerIds[i];
        }

        // Create quad border faces [v_i, v_{next}, v_{next}_inner, v_i_inner] around perimeter
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            mesh.AddFace([originalIds[i], originalIds[next], innerIds[next], innerIds[i]], materialId);

            var oldEdgeMeta = mesh.GetEdgeMetadata(new EdgeKey(originalIds[i], originalIds[next]));
            if (oldEdgeMeta is not null && (oldEdgeMeta.Properties.Count > 0 || oldEdgeMeta.Tags.Count > 0))
            {
                mesh.GetOrCreateEdgeMetadata(new EdgeKey(innerIds[i], innerIds[next])).MergeFrom(oldEdgeMeta);
            }
        }
    }

    private static void InsetConnectedRegion(
        EditableMesh mesh,
        TopologyGraph topology,
        List<PolygonFace> component,
        HashSet<int> componentSet,
        float thickness,
        float depth)
    {
        Vector3 normalSum = Vector3.Zero;
        for (int i = 0; i < component.Count; i++)
        {
            normalSum += component[i].ComputeNormal(mesh);
        }

        Vector3 regionNormal = normalSum.LengthSquared() > 1e-12f
            ? Vector3.Normalize(normalSum)
            : component[0].ComputeNormal(mesh);

        var boundaryHalfEdges = CollectBoundaryHalfEdges(topology, component, componentSet);

        // Map boundary vertex -> (prevBoundaryVertex, nextBoundaryVertex)
        var prevBoundaryMap = new Dictionary<int, int>();
        var nextBoundaryMap = new Dictionary<int, int>();
        for (int i = 0; i < boundaryHalfEdges.Count; i++)
        {
            (int u, int v, _) = boundaryHalfEdges[i];
            nextBoundaryMap[u] = v;
            prevBoundaryMap[v] = u;
        }

        var innerVertexMap = new Dictionary<int, int>();

        for (int i = 0; i < component.Count; i++)
        {
            List<int> vIds = component[i].VertexIds;
            for (int j = 0; j < vIds.Count; j++)
            {
                int vId = vIds[j];
                if (innerVertexMap.ContainsKey(vId))
                {
                    continue;
                }

                Vector3 pCurr = mesh.GetVertex(vId)!.Position;
                Vector3 innerPos;

                if (prevBoundaryMap.TryGetValue(vId, out int prevId) &&
                    nextBoundaryMap.TryGetValue(vId, out int nextId))
                {
                    Vector3 pPrev = mesh.GetVertex(prevId)!.Position;
                    Vector3 pNext = mesh.GetVertex(nextId)!.Position;
                    Vector3 miter = ComputeInwardMiter(pPrev, pCurr, pNext, regionNormal);
                    innerPos = pCurr + (miter * thickness) + (regionNormal * depth);
                }
                else
                {
                    innerPos = pCurr + (regionNormal * depth);
                }

                MeshVertex? origVert = mesh.GetVertex(vId);
                int newVertId = mesh.AddVertex(innerPos);
                innerVertexMap[vId] = newVertId;
                if (mesh.GetVertex(newVertId) is { } newVert && origVert is not null && (origVert.Metadata.Properties.Count > 0 || origVert.Metadata.Tags.Count > 0))
                {
                    newVert.Metadata = origVert.Metadata.Clone();
                }
            }
        }

        // Rewire region faces to inner vertices
        for (int i = 0; i < component.Count; i++)
        {
            List<int> vIds = component[i].VertexIds;
            for (int j = 0; j < vIds.Count; j++)
            {
                vIds[j] = innerVertexMap[vIds[j]];
            }
        }

        // Create quad border faces along perimeter
        for (int i = 0; i < boundaryHalfEdges.Count; i++)
        {
            (int u, int v, string materialId) = boundaryHalfEdges[i];
            mesh.AddFace([u, v, innerVertexMap[v], innerVertexMap[u]], materialId);

            var oldEdgeMeta = mesh.GetEdgeMetadata(new EdgeKey(u, v));
            if (oldEdgeMeta is not null && (oldEdgeMeta.Properties.Count > 0 || oldEdgeMeta.Tags.Count > 0))
            {
                mesh.GetOrCreateEdgeMetadata(new EdgeKey(innerVertexMap[u], innerVertexMap[v])).MergeFrom(oldEdgeMeta);
            }
        }
    }

    private static Vector3 ComputeInwardMiter(
        Vector3 pPrev,
        Vector3 pCurr,
        Vector3 pNext,
        Vector3 faceNormal)
    {
        Vector3 edgeIn = pCurr - pPrev;
        Vector3 edgeOut = pNext - pCurr;

        Vector3 dirIn = edgeIn.LengthSquared() > 1e-12f ? Vector3.Normalize(edgeIn) : Vector3.UnitX;
        Vector3 dirOut = edgeOut.LengthSquared() > 1e-12f ? Vector3.Normalize(edgeOut) : Vector3.UnitX;

        Vector3 nIn = Vector3.Cross(faceNormal, dirIn);
        if (nIn.LengthSquared() > 1e-12f)
        {
            nIn = Vector3.Normalize(nIn);
        }

        Vector3 nOut = Vector3.Cross(faceNormal, dirOut);
        if (nOut.LengthSquared() > 1e-12f)
        {
            nOut = Vector3.Normalize(nOut);
        }

        Vector3 sumN = nIn + nOut;
        if (sumN.LengthSquared() <= 1e-8f)
        {
            return nOut;
        }

        Vector3 bisector = Vector3.Normalize(sumN);
        float sinHalfAngle = Vector3.Dot(bisector, nIn);
        float clampedSin = System.Math.Max(MathF.Abs(sinHalfAngle), 0.2f);
        return bisector / clampedSin;
    }

    private static bool SharesSameEdgeDirection(PolygonFace faceA, PolygonFace faceB)
    {
        List<int> idsA = faceA.VertexIds;
        List<int> idsB = faceB.VertexIds;
        int countA = idsA.Count;
        int countB = idsB.Count;

        for (int i = 0; i < countA; i++)
        {
            int uA = idsA[i];
            int vA = idsA[(i + 1) % countA];
            var edgeA = new EdgeKey(uA, vA);

            for (int j = 0; j < countB; j++)
            {
                int uB = idsB[j];
                int vB = idsB[(j + 1) % countB];
                if (new EdgeKey(uB, vB) == edgeA)
                {
                    return uA == uB && vA == vB;
                }
            }
        }

        return false;
    }

    private static Vector3 ComputeComponentVertexCentroid(EditableMesh mesh, List<PolygonFace> componentFaces)
    {
        var seenVertices = new HashSet<int>();
        Vector3 sum = Vector3.Zero;

        for (int i = 0; i < componentFaces.Count; i++)
        {
            List<int> vIds = componentFaces[i].VertexIds;
            for (int j = 0; j < vIds.Count; j++)
            {
                int vId = vIds[j];
                if (seenVertices.Add(vId))
                {
                    MeshVertex? v = mesh.GetVertex(vId);
                    if (v is not null)
                    {
                        sum += v.Position;
                    }
                }
            }
        }

        return seenVertices.Count > 0 ? sum / seenVertices.Count : Vector3.Zero;
    }

    private static float ComputeFaceArea(EditableMesh mesh, PolygonFace face)
    {
        List<int> vIds = face.VertexIds;
        int count = vIds.Count;
        if (count < 3)
        {
            return 0.0f;
        }

        Vector3 newell = Vector3.Zero;
        for (int i = 0; i < count; i++)
        {
            MeshVertex? currVert = mesh.GetVertex(vIds[i]);
            MeshVertex? nextVert = mesh.GetVertex(vIds[(i + 1) % count]);
            if (currVert is null || nextVert is null)
            {
                continue;
            }

            Vector3 curr = currVert.Position;
            Vector3 next = nextVert.Position;

            newell.X += (curr.Y - next.Y) * (curr.Z + next.Z);
            newell.Y += (curr.Z - next.Z) * (curr.X + next.X);
            newell.Z += (curr.X - next.X) * (curr.Y + next.Y);
        }

        return 0.5f * newell.Length();
    }
}
