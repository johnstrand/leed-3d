using System.Numerics;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Scene;

namespace Leed3D.Kernel.Geometry;

/// <summary>
/// Headless vertex and edge topology construction and editing operations for <see cref="EditableMesh"/>.
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
                mesh.RemoveFace(face.Id);
                mesh.AddFace(loopAB, materialId);
                mesh.AddFace(loopBA, materialId);
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

            Vector3 originalPos = mesh.GetVertex(originalVertexId)!.Position;
            dupId = mesh.AddVertex(originalPos + offset);
            duplicatedVertices[originalVertexId] = dupId;
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
            results.Add((new EdgeKey(uNew, vNew), newFaceId));
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
}
