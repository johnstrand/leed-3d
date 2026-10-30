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
