using System.Numerics;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Texturing;
using Leed3D.Runtime.Metadata;

namespace Leed3D.Kernel.Csg;

/// <summary>
/// Supported 3D Constructive Solid Geometry (CSG) boolean operations.
/// </summary>
public enum CsgOperation
{
    Union,
    Subtract,
    Intersect
}

/// <summary>
/// Evaluates closed-mesh 3D CSG boolean operations (Union, Subtract/Carve, Intersect)
/// using 3D BSP-tree polygon clipping and reconstructs clean N-gon <see cref="EditableMesh"/> topology.
/// </summary>
public static class CsgBooleanEngine
{
    public const float WeldEpsilon = 1e-4f;

    /// <summary>
    /// Evaluates a CSG boolean operation between two <see cref="SceneObject"/> instances and returns
    /// the resulting <see cref="EditableMesh"/> in <paramref name="objectA"/>'s local coordinate space.
    /// </summary>
    public static EditableMesh Evaluate(SceneObject objectA, SceneObject objectB, CsgOperation operation)
    {
        ArgumentNullException.ThrowIfNull(objectA);
        ArgumentNullException.ThrowIfNull(objectB);

        EditableMesh meshA = objectA.Mesh ?? new EditableMesh();
        EditableMesh meshB = objectB.Mesh ?? new EditableMesh();
        return Evaluate(meshA, objectA.Transform, meshB, objectB.Transform, operation);
    }

    /// <summary>
    /// Evaluates a CSG boolean operation between two <see cref="EditableMesh"/> instances in shared local/world space.
    /// </summary>
    public static EditableMesh Evaluate(EditableMesh meshA, EditableMesh meshB, CsgOperation operation)
    {
        return Evaluate(meshA, new Transform3D(), meshB, new Transform3D(), operation);
    }

    /// <summary>
    /// Evaluates a CSG boolean operation between <paramref name="meshA"/> (with <paramref name="transformA"/>)
    /// and <paramref name="meshB"/> (with <paramref name="transformB"/>), returning a new <see cref="EditableMesh"/>
    /// in <paramref name="transformA"/>'s local space.
    /// </summary>
    public static EditableMesh Evaluate(
        EditableMesh meshA,
        Transform3D transformA,
        EditableMesh meshB,
        Transform3D transformB,
        CsgOperation operation)
    {
        ArgumentNullException.ThrowIfNull(meshA);
        ArgumentNullException.ThrowIfNull(transformA);
        ArgumentNullException.ThrowIfNull(meshB);
        ArgumentNullException.ThrowIfNull(transformB);

        Matrix4x4 worldA = transformA.GetLocalMatrix();
        Matrix4x4 worldB = transformB.GetLocalMatrix();
        if (!Matrix4x4.Invert(worldA, out Matrix4x4 worldToLocalA))
        {
            worldToLocalA = Matrix4x4.Identity;
        }

        List<CsgPolygon> polysA = MeshToPolygons(meshA, worldA);
        List<CsgPolygon> polysB = MeshToPolygons(meshB, worldB);

        if (polysA.Count == 0)
        {
            return operation == CsgOperation.Union && polysB.Count > 0
                ? PolygonsToMesh(polysB, worldToLocalA)
                : new EditableMesh();
        }

        if (polysB.Count == 0)
        {
            return operation == CsgOperation.Intersect
                ? new EditableMesh()
                : meshA.Clone();
        }

        var a = new BspNode(polysA);
        var b = new BspNode(polysB);

        switch (operation)
        {
            case CsgOperation.Union:
                a.ClipTo(b);
                b.ClipTo(a);
                b.Invert();
                b.ClipTo(a);
                b.Invert();
                a.AddClippedPolygons(b.AllPolygons());
                break;

            case CsgOperation.Subtract:
                a.Invert();
                a.ClipTo(b);
                b.ClipTo(a);
                b.Invert();
                b.ClipTo(a);
                b.Invert();
                a.AddClippedPolygons(b.AllPolygons());
                a.Invert();
                break;

            case CsgOperation.Intersect:
                a.Invert();
                b.ClipTo(a);
                b.Invert();
                a.ClipTo(b);
                b.ClipTo(a);
                a.AddClippedPolygons(b.AllPolygons());
                a.Invert();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unsupported CSG operation.");
        }

        List<CsgPolygon> resultPolygons = a.AllPolygons();
        return PolygonsToMesh(resultPolygons, worldToLocalA);
    }

    /// <summary>
    /// Converts an <see cref="EditableMesh"/> into convex world-space <see cref="CsgPolygon"/>s.
    /// Concave N-gon faces are decomposed into triangles prior to BSP clipping so plane splitting remains exact;
    /// surviving coplanar fragments are re-merged into N-gons during <see cref="PolygonsToMesh"/>.
    /// </summary>
    public static List<CsgPolygon> MeshToPolygons(EditableMesh mesh, Matrix4x4 worldMatrix)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        bool flipWinding = worldMatrix.GetDeterminant() < 0.0f;
        var polygons = new List<CsgPolygon>(mesh.FaceCount);

        for (int i = 0; i < mesh.Faces.Count; i++)
        {
            PolygonFace face = mesh.Faces[i];
            if (face.VertexIds.Count < 3)
            {
                continue;
            }

            var worldVerts = new List<Vector3>(face.VertexIds.Count);
            bool valid = true;
            for (int j = 0; j < face.VertexIds.Count; j++)
            {
                MeshVertex? v = mesh.GetVertex(face.VertexIds[j]);
                if (v is null)
                {
                    valid = false;
                    break;
                }

                worldVerts.Add(Vector3.Transform(v.Position, worldMatrix));
            }

            if (!valid || worldVerts.Count < 3)
            {
                continue;
            }

            if (flipWinding)
            {
                worldVerts.Reverse();
            }

            Vector3 newell = CsgPolygon.ComputeNewellVector(worldVerts);
            float newellLenSq = newell.LengthSquared();
            if (newellLenSq <= 1e-12f)
            {
                continue;
            }

            Vector3 normal = newell / MathF.Sqrt(newellLenSq);
            if (worldVerts.Count == 3 || IsConvexPolygon(worldVerts, normal))
            {
                polygons.Add(new CsgPolygon(worldVerts, face.MaterialId, face.UvMapping, face.Metadata));
            }
            else
            {
                // Decompose concave face into triangles for exact convex BSP splitting
                IReadOnlyList<(int V0, int V1, int V2)> tris = PolygonTriangulator.TriangulateFace(face, mesh);
                Plane3D parentPlane = new CsgPolygon(worldVerts, face.MaterialId, face.UvMapping, face.Metadata).Plane;

                for (int t = 0; t < tris.Count; t++)
                {
                    (int v0, int v1, int v2) = tris[t];
                    MeshVertex? va = mesh.GetVertex(v0);
                    MeshVertex? vb = mesh.GetVertex(v1);
                    MeshVertex? vc = mesh.GetVertex(v2);
                    if (va is null || vb is null || vc is null)
                    {
                        continue;
                    }

                    Vector3 p0 = Vector3.Transform(va.Position, worldMatrix);
                    Vector3 p1 = Vector3.Transform(vb.Position, worldMatrix);
                    Vector3 p2 = Vector3.Transform(vc.Position, worldMatrix);

                    List<Vector3> triVerts = flipWinding ? [p0, p2, p1] : [p0, p1, p2];
                    if (CsgPolygon.ComputeNewellVector(triVerts).LengthSquared() > 1e-12f)
                    {
                        polygons.Add(new CsgPolygon(triVerts, face.MaterialId, parentPlane, face.UvMapping, face.Metadata));
                    }
                }
            }
        }

        return polygons;
    }

    /// <summary>
    /// Reconstructs a clean <see cref="EditableMesh"/> from a list of world-space <see cref="CsgPolygon"/>s,
    /// welding vertices within <paramref name="weldEpsilon"/>, resolving T-junctions, merging coplanar
    /// same-material faces into N-gons, removing collinear mid-edge vertices, and running <see cref="EditableMesh.ValidateAndRepair"/>.
    /// </summary>
    public static EditableMesh PolygonsToMesh(
        IReadOnlyList<CsgPolygon> worldPolygons,
        Matrix4x4 worldToLocalMatrix,
        float weldEpsilon = WeldEpsilon)
    {
        ArgumentNullException.ThrowIfNull(worldPolygons);

        var mesh = new EditableMesh();
        if (worldPolygons.Count == 0)
        {
            return mesh;
        }

        bool flipWinding = worldToLocalMatrix.GetDeterminant() < 0.0f;
        float cellSize = System.Math.Max(weldEpsilon * 10.0f, 1e-3f);
        float weldEpsSq = weldEpsilon * weldEpsilon;

        var positions = new List<Vector3>();
        var spatialGrid = new Dictionary<(int, int, int), List<int>>();
        var indexedPolygons = new List<IndexedPolygon>(worldPolygons.Count);

        for (int i = 0; i < worldPolygons.Count; i++)
        {
            CsgPolygon poly = worldPolygons[i];
            if (poly.Vertices.Count < 3)
            {
                continue;
            }

            var loop = new List<int>(poly.Vertices.Count);
            for (int j = 0; j < poly.Vertices.Count; j++)
            {
                Vector3 localPos = Vector3.Transform(poly.Vertices[j], worldToLocalMatrix);
                int idx = GetOrAddWeldedVertex(localPos, positions, spatialGrid, cellSize, weldEpsSq);
                loop.Add(idx);
            }

            if (flipWinding)
            {
                loop.Reverse();
            }

            CleanIndexLoop(loop);
            if (loop.Count < 3)
            {
                continue;
            }

            if (TryCreateIndexedPolygon(loop, poly.MaterialId, poly.UvMapping, poly.Metadata, positions, out IndexedPolygon? indexed) && indexed is not null)
            {
                indexedPolygons.Add(indexed);
            }
        }

        if (indexedPolygons.Count == 0)
        {
            return mesh;
        }

        // 1. Subdivide unsplit edges at T-junction vertices so coplanar fragments share matching half-edges
        InsertTJunctionVertices(indexedPolygons, positions, weldEpsilon);

        // 2. Merge adjacent coplanar polygons that share the same normal and MaterialId
        MergeCoplanarFaces(indexedPolygons, positions);

        // 3. Remove redundant collinear vertices along straight N-gon edges
        RemoveCollinearVertices(indexedPolygons, positions);

        // 4. Populate EditableMesh with welded vertices and reconstructed N-gon faces
        var vertexRemap = new Dictionary<int, int>();
        for (int i = 0; i < indexedPolygons.Count; i++)
        {
            IndexedPolygon poly = indexedPolygons[i];
            CleanIndexLoop(poly.Loop);
            if (poly.Loop.Count < 3 || ComputeLoopNewellVector(poly.Loop, positions).LengthSquared() <= 1e-10f)
            {
                continue;
            }

            var meshVertIds = new List<int>(poly.Loop.Count);
            for (int j = 0; j < poly.Loop.Count; j++)
            {
                int posIdx = poly.Loop[j];
                if (!vertexRemap.TryGetValue(posIdx, out int meshVertexId))
                {
                    meshVertexId = mesh.AddVertex(positions[posIdx]);
                    vertexRemap[posIdx] = meshVertexId;
                }

                meshVertIds.Add(meshVertexId);
            }

            mesh.AddFace(meshVertIds, poly.MaterialId, poly.UvMapping, poly.Metadata);
        }

        mesh.ValidateAndRepair();
        return mesh;
    }

    private static bool IsConvexPolygon(IReadOnlyList<Vector3> vertices, Vector3 normal)
    {
        int count = vertices.Count;
        if (count <= 3)
        {
            return true;
        }

        for (int i = 0; i < count; i++)
        {
            Vector3 prev = vertices[(i - 1 + count) % count];
            Vector3 curr = vertices[i];
            Vector3 next = vertices[(i + 1) % count];

            Vector3 e1 = curr - prev;
            Vector3 e2 = next - curr;
            float len1 = e1.Length();
            float len2 = e2.Length();
            if (len1 <= 1e-6f || len2 <= 1e-6f)
            {
                continue;
            }

            Vector3 cross = Vector3.Cross(e1 / len1, e2 / len2);
            if (Vector3.Dot(cross, normal) < -1e-4f)
            {
                return false;
            }
        }

        return true;
    }

    private static int GetOrAddWeldedVertex(
        Vector3 pos,
        List<Vector3> positions,
        Dictionary<(int, int, int), List<int>> grid,
        float cellSize,
        float weldEpsSq)
    {
        int cx = (int)MathF.Floor(pos.X / cellSize);
        int cy = (int)MathF.Floor(pos.Y / cellSize);
        int cz = (int)MathF.Floor(pos.Z / cellSize);

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (grid.TryGetValue((cx + dx, cy + dy, cz + dz), out List<int>? bucket))
                    {
                        for (int i = 0; i < bucket.Count; i++)
                        {
                            int candidateIdx = bucket[i];
                            if (Vector3.DistanceSquared(positions[candidateIdx], pos) <= weldEpsSq)
                            {
                                return candidateIdx;
                            }
                        }
                    }
                }
            }
        }

        int newIdx = positions.Count;
        positions.Add(pos);
        var key = (cx, cy, cz);
        if (!grid.TryGetValue(key, out List<int>? list))
        {
            list = [];
            grid[key] = list;
        }

        list.Add(newIdx);
        return newIdx;
    }

    private static void CleanIndexLoop(List<int> loop)
    {
        bool changed = true;
        while (changed && loop.Count >= 3)
        {
            changed = false;

            for (int i = loop.Count - 1; i > 0; i--)
            {
                if (loop[i] == loop[i - 1])
                {
                    loop.RemoveAt(i);
                    changed = true;
                }
            }

            while (loop.Count > 1 && loop[^1] == loop[0])
            {
                loop.RemoveAt(loop.Count - 1);
                changed = true;
            }

            if (loop.Count >= 3)
            {
                for (int i = 0; i < loop.Count; i++)
                {
                    int prev = loop[(i - 1 + loop.Count) % loop.Count];
                    int next = loop[(i + 1) % loop.Count];
                    if (prev == next)
                    {
                        loop.RemoveAt(i);
                        changed = true;
                        break;
                    }
                }
            }
        }
    }

    private static Vector3 ComputeLoopNewellVector(IReadOnlyList<int> loop, IReadOnlyList<Vector3> positions)
    {
        int count = loop.Count;
        if (count < 3)
        {
            return Vector3.Zero;
        }

        Vector3 normal = Vector3.Zero;
        for (int i = 0; i < count; i++)
        {
            Vector3 curr = positions[loop[i]];
            Vector3 next = positions[loop[(i + 1) % count]];

            normal.X += (curr.Y - next.Y) * (curr.Z + next.Z);
            normal.Y += (curr.Z - next.Z) * (curr.X + next.X);
            normal.Z += (curr.X - next.X) * (curr.Y + next.Y);
        }

        return normal;
    }

    private static bool TryCreateIndexedPolygon(
        List<int> loop,
        string materialId,
        FaceUvMapping? uvMapping,
        ElementMetadata? metadata,
        IReadOnlyList<Vector3> positions,
        out IndexedPolygon? polygon)
    {
        polygon = null;
        if (loop.Count < 3)
        {
            return false;
        }

        Vector3 newell = ComputeLoopNewellVector(loop, positions);
        float lenSq = newell.LengthSquared();
        if (lenSq <= 1e-10f)
        {
            return false;
        }

        float newellLen = MathF.Sqrt(lenSq);
        Vector3 normal = newell / newellLen;

        Vector3 centroid = Vector3.Zero;
        for (int i = 0; i < loop.Count; i++)
        {
            centroid += positions[loop[i]];
        }
        centroid /= loop.Count;

        Plane3D plane = Plane3D.CreateFromPointAndNormal(centroid, normal);
        polygon = new IndexedPolygon(loop, materialId, uvMapping?.Clone(), metadata?.Clone(), plane, newellLen);
        return true;
    }

    private static void InsertTJunctionVertices(
        List<IndexedPolygon> polygons,
        IReadOnlyList<Vector3> positions,
        float epsilon)
    {
        var activeVertices = new HashSet<int>();
        for (int i = 0; i < polygons.Count; i++)
        {
            List<int> loop = polygons[i].Loop;
            for (int j = 0; j < loop.Count; j++)
            {
                activeVertices.Add(loop[j]);
            }
        }

        int[] activeArray = activeVertices.ToArray();
        float epsSq = epsilon * epsilon;
        var insertions = new List<(float T, int VertexIdx)>();

        for (int p = 0; p < polygons.Count; p++)
        {
            List<int> loop = polygons[p].Loop;
            var rebuilt = new List<int>(loop.Count + 4);

            for (int i = 0; i < loop.Count; i++)
            {
                int u = loop[i];
                int w = loop[(i + 1) % loop.Count];
                rebuilt.Add(u);

                Vector3 p0 = positions[u];
                Vector3 p1 = positions[w];
                Vector3 edge = p1 - p0;
                float lenSq = edge.LengthSquared();
                if (lenSq <= epsSq * 4.0f)
                {
                    continue;
                }

                Vector3 min = Vector3.Min(p0, p1) - new Vector3(epsilon);
                Vector3 max = Vector3.Max(p0, p1) + new Vector3(epsilon);
                insertions.Clear();

                for (int k = 0; k < activeArray.Length; k++)
                {
                    int v = activeArray[k];
                    if (v == u || v == w)
                    {
                        continue;
                    }

                    Vector3 pv = positions[v];
                    if (pv.X < min.X || pv.Y < min.Y || pv.Z < min.Z ||
                        pv.X > max.X || pv.Y > max.Y || pv.Z > max.Z)
                    {
                        continue;
                    }

                    float t = Vector3.Dot(pv - p0, edge) / lenSq;
                    if (t <= 1e-4f || t >= 1.0f - 1e-4f)
                    {
                        continue;
                    }

                    Vector3 proj = p0 + (edge * t);
                    if (Vector3.DistanceSquared(pv, proj) <= epsSq && !loop.Contains(v))
                    {
                        insertions.Add((t, v));
                    }
                }

                if (insertions.Count > 0)
                {
                    insertions.Sort((a, b) => a.T.CompareTo(b.T));
                    for (int m = 0; m < insertions.Count; m++)
                    {
                        rebuilt.Add(insertions[m].VertexIdx);
                    }
                }
            }

            if (rebuilt.Count != loop.Count)
            {
                CleanIndexLoop(rebuilt);
                polygons[p].Loop = rebuilt;
            }
        }
    }

    private static void MergeCoplanarFaces(List<IndexedPolygon> polygons, IReadOnlyList<Vector3> positions)
    {
        bool mergedAny = true;
        while (mergedAny)
        {
            mergedAny = false;

            for (int i = 0; i < polygons.Count && !mergedAny; i++)
            {
                IndexedPolygon polyA = polygons[i];

                for (int j = i + 1; j < polygons.Count; j++)
                {
                    IndexedPolygon polyB = polygons[j];

                    if (!string.Equals(polyA.MaterialId, polyB.MaterialId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (Vector3.Dot(polyA.Plane.Normal, polyB.Plane.Normal) < 0.9995f)
                    {
                        continue;
                    }

                    if (MathF.Abs(polyA.Plane.D - polyB.Plane.D) > 1e-3f)
                    {
                        continue;
                    }

                    if (!TryMergePolygonLoops(polyA, polyB, positions, out IndexedPolygon? mergedPoly) || mergedPoly is null)
                    {
                        continue;
                    }

                    polygons[i] = mergedPoly;
                    polygons.RemoveAt(j);
                    mergedAny = true;
                    break;
                }
            }
        }
    }

    private static bool TryMergePolygonLoops(
        IndexedPolygon polyA,
        IndexedPolygon polyB,
        IReadOnlyList<Vector3> positions,
        out IndexedPolygon? merged)
    {
        merged = null;

        int countA = polyA.Loop.Count;
        int countB = polyB.Loop.Count;

        var edgesA = new List<(int From, int To)>(countA);
        for (int i = 0; i < countA; i++)
        {
            edgesA.Add((polyA.Loop[i], polyA.Loop[(i + 1) % countA]));
        }

        var edgesB = new List<(int From, int To)>(countB);
        for (int i = 0; i < countB; i++)
        {
            edgesB.Add((polyB.Loop[i], polyB.Loop[(i + 1) % countB]));
        }

        // Cancel all opposite shared half-edges (u -> v in A, v -> u in B)
        var usedB = new bool[countB];
        var remEdges = new List<(int From, int To)>(countA + countB);
        int sharedCount = 0;

        for (int i = 0; i < countA; i++)
        {
            (int u, int v) = edgesA[i];
            int matchB = -1;
            for (int j = 0; j < countB; j++)
            {
                if (!usedB[j] && edgesB[j].From == v && edgesB[j].To == u)
                {
                    matchB = j;
                    break;
                }
            }

            if (matchB >= 0)
            {
                usedB[matchB] = true;
                sharedCount++;
            }
            else
            {
                remEdges.Add((u, v));
            }
        }

        if (sharedCount == 0)
        {
            return false;
        }

        for (int j = 0; j < countB; j++)
        {
            if (!usedB[j])
            {
                remEdges.Add(edgesB[j]);
            }
        }

        if (remEdges.Count < 3)
        {
            return false;
        }

        // Verify that the remaining boundary half-edges form a single simple directed cycle
        // (every vertex in-degree == 1 and out-degree == 1, preventing pinch vertices and interior holes)
        var nextMap = new Dictionary<int, int>(remEdges.Count);
        var inDegree = new Dictionary<int, int>(remEdges.Count);

        for (int i = 0; i < remEdges.Count; i++)
        {
            (int u, int v) = remEdges[i];
            if (!nextMap.TryAdd(u, v))
            {
                return false;
            }

            inDegree[v] = inDegree.GetValueOrDefault(v) + 1;
        }

        foreach (int deg in inDegree.Values)
        {
            if (deg != 1)
            {
                return false;
            }
        }

        var cycle = new List<int>(remEdges.Count);
        int start = remEdges[0].From;
        int current = start;

        for (int step = 0; step < remEdges.Count; step++)
        {
            cycle.Add(current);
            if (!nextMap.TryGetValue(current, out int next))
            {
                return false;
            }

            current = next;
            if (current == start)
            {
                break;
            }
        }

        if (current != start || cycle.Count != remEdges.Count)
        {
            return false;
        }

        CleanIndexLoop(cycle);
        if (cycle.Count < 3)
        {
            return false;
        }

        Vector3 newell = ComputeLoopNewellVector(cycle, positions);
        float newellLen = newell.Length();
        if (newellLen <= 1e-5f)
        {
            return false;
        }

        Vector3 mergedNormal = newell / newellLen;
        if (Vector3.Dot(mergedNormal, polyA.Plane.Normal) < 0.999f)
        {
            return false;
        }

        float expectedLen = polyA.NewellLength + polyB.NewellLength;
        if (MathF.Abs(newellLen - expectedLen) > System.Math.Max(1e-2f, expectedLen * 1e-2f))
        {
            return false;
        }

        merged = new IndexedPolygon(cycle, polyA.MaterialId, polyA.UvMapping?.Clone(), polyA.Metadata?.Clone(), polyA.Plane, newellLen);
        return true;
    }

    private static void RemoveCollinearVertices(List<IndexedPolygon> polygons, IReadOnlyList<Vector3> positions)
    {
        for (int p = 0; p < polygons.Count; p++)
        {
            List<int> loop = polygons[p].Loop;
            bool changed = true;

            while (changed && loop.Count > 3)
            {
                changed = false;
                for (int i = 0; i < loop.Count; i++)
                {
                    int prevIdx = loop[(i - 1 + loop.Count) % loop.Count];
                    int currIdx = loop[i];
                    int nextIdx = loop[(i + 1) % loop.Count];

                    Vector3 prev = positions[prevIdx];
                    Vector3 curr = positions[currIdx];
                    Vector3 next = positions[nextIdx];

                    Vector3 e1 = curr - prev;
                    Vector3 e2 = next - curr;
                    float len1 = e1.Length();
                    float len2 = e2.Length();

                    if (len1 <= 1e-5f || len2 <= 1e-5f)
                    {
                        loop.RemoveAt(i);
                        changed = true;
                        break;
                    }

                    float dot = Vector3.Dot(e1 / len1, e2 / len2);
                    if (dot >= 0.9999f)
                    {
                        loop.RemoveAt(i);
                        changed = true;
                        break;
                    }
                }
            }
        }
    }

    private sealed class IndexedPolygon
    {
        public List<int> Loop { get; set; }
        public string MaterialId { get; }
        public FaceUvMapping? UvMapping { get; }
        public ElementMetadata? Metadata { get; }
        public Plane3D Plane { get; }
        public float NewellLength { get; }

        public IndexedPolygon(List<int> loop, string materialId, FaceUvMapping? uvMapping, ElementMetadata? metadata, Plane3D plane, float newellLength)
        {
            Loop = loop;
            MaterialId = materialId;
            UvMapping = uvMapping;
            Metadata = metadata;
            Plane = plane;
            NewellLength = newellLength;
        }
    }
}
