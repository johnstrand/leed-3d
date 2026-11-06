using System.Numerics;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Gizmos;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Scene;

namespace Leed3D.Kernel.Selection;

/// <summary>
/// Represents a 3D world-space snap target hit on a scene vertex, edge, or face surface.
/// </summary>
public readonly record struct SnapTargetHit(
    SnapTargetMode Mode,
    Vector3 WorldPosition,
    Vector3 WorldNormal,
    int ObjectId,
    bool IsEdgeMidpoint = false);

/// <summary>
/// Headless spatial query service for snapping gizmo and modal (<c>G</c>/<c>R</c>/<c>S</c>) transforms
/// onto scene vertices, edges (including midpoints), and face surfaces.
/// </summary>
public static class SceneSnapQuery
{
    /// <summary>
    /// Finds the closest valid snap target in <paramref name="scene"/> according to <paramref name="mode"/>,
    /// automatically excluding the currently selected/transforming objects or sub-elements in <paramref name="selection"/>.
    /// </summary>
    public static bool TryFindSnapTarget(
        EditorScene scene,
        SelectionManager? selection,
        SnapTargetMode mode,
        Vector3 referenceWorldPoint,
        Ray3D? cursorRay,
        float maxSnapDistanceWorld,
        out SnapTargetHit hit,
        bool snapToEdgeMidpointOnly = false)
    {
        ArgumentNullException.ThrowIfNull(scene);
        hit = default;

        if (mode == SnapTargetMode.Grid || maxSnapDistanceWorld <= 0.0f)
        {
            return false;
        }

        HashSet<int>? excludedVerticesOnActiveObject = BuildExcludedVertexSet(scene, selection, out int? activeEditObjectId);

        return mode switch
        {
            SnapTargetMode.Vertex => TryFindNearestVertex(scene, selection, activeEditObjectId, excludedVerticesOnActiveObject, referenceWorldPoint, cursorRay, maxSnapDistanceWorld, out hit),
            SnapTargetMode.Edge => TryFindNearestEdge(scene, selection, activeEditObjectId, excludedVerticesOnActiveObject, referenceWorldPoint, cursorRay, maxSnapDistanceWorld, snapToEdgeMidpointOnly, out hit),
            SnapTargetMode.Face => TryFindNearestFace(scene, selection, activeEditObjectId, excludedVerticesOnActiveObject, referenceWorldPoint, cursorRay, maxSnapDistanceWorld, out hit),
            _ => false
        };
    }

    /// <summary>
    /// Computes a constrained translation delta from <paramref name="pivotWorld"/> to <paramref name="snapWorldPosition"/>
    /// respecting <paramref name="constraint"/>.
    /// </summary>
    public static Vector3 ComputeConstrainedSnapDelta(
        Vector3 pivotWorld,
        Vector3 snapWorldPosition,
        GizmoAxisConstraint constraint)
    {
        Vector3 delta = snapWorldPosition - pivotWorld;
        return constraint switch
        {
            GizmoAxisConstraint.X => new Vector3(delta.X, 0.0f, 0.0f),
            GizmoAxisConstraint.Y => new Vector3(0.0f, delta.Y, 0.0f),
            GizmoAxisConstraint.Z => new Vector3(0.0f, 0.0f, delta.Z),
            GizmoAxisConstraint.XY => new Vector3(delta.X, delta.Y, 0.0f),
            GizmoAxisConstraint.XZ => new Vector3(delta.X, 0.0f, delta.Z),
            GizmoAxisConstraint.YZ => new Vector3(0.0f, delta.Y, delta.Z),
            _ => delta
        };
    }

    private static HashSet<int>? BuildExcludedVertexSet(
        EditorScene scene,
        SelectionManager? selection,
        out int? activeEditObjectId)
    {
        activeEditObjectId = null;
        if (selection is null || selection.Mode == InteractionMode.Object || !selection.ActiveObjectId.HasValue)
        {
            return null;
        }

        activeEditObjectId = selection.ActiveObjectId.Value;
        SceneObject? activeObj = scene.GetObject(activeEditObjectId.Value);
        if (activeObj?.Mesh is null)
        {
            return null;
        }

        var excluded = new HashSet<int>();
        switch (selection.Mode)
        {
            case InteractionMode.Vertex:
                foreach (int vId in selection.SelectedVertexIds)
                {
                    excluded.Add(vId);
                }
                break;

            case InteractionMode.Edge:
                foreach (EdgeKey e in selection.SelectedEdges)
                {
                    excluded.Add(e.VMin);
                    excluded.Add(e.VMax);
                }
                break;

            case InteractionMode.Face:
                foreach (int fId in selection.SelectedFaceIds)
                {
                    if (activeObj.Mesh.GetFace(fId) is { } face)
                    {
                        for (int i = 0; i < face.VertexIds.Count; i++)
                        {
                            excluded.Add(face.VertexIds[i]);
                        }
                    }
                }
                break;
        }

        return excluded;
    }

    private static bool IsObjectEligible(SceneObject obj, SelectionManager? selection, int? activeEditObjectId)
    {
        if (!obj.Visible || obj.Mesh is null || obj.Mesh.VertexCount == 0)
        {
            return false;
        }

        if (selection is not null && selection.Mode == InteractionMode.Object && selection.SelectedObjectIds.Contains(obj.Id))
        {
            return false;
        }

        return true;
    }

    private static bool TryFindNearestVertex(
        EditorScene scene,
        SelectionManager? selection,
        int? activeEditObjectId,
        HashSet<int>? excludedVerticesOnActiveObject,
        Vector3 referenceWorldPoint,
        Ray3D? cursorRay,
        float maxSnapDistanceWorld,
        out SnapTargetHit hit)
    {
        hit = default;
        float bestMetric = float.PositiveInfinity;
        float maxDistSq = maxSnapDistanceWorld * maxSnapDistanceWorld;
        bool found = false;

        for (int i = 0; i < scene.Objects.Count; i++)
        {
            SceneObject obj = scene.Objects[i];
            if (!IsObjectEligible(obj, selection, activeEditObjectId))
            {
                continue;
            }

            EditableMesh mesh = obj.Mesh!;
            Matrix4x4 localToWorld = obj.Transform.GetLocalMatrix();
            bool isActiveEditObj = activeEditObjectId.HasValue && obj.Id == activeEditObjectId.Value;

            for (int v = 0; v < mesh.Vertices.Count; v++)
            {
                MeshVertex vert = mesh.Vertices[v];
                if (isActiveEditObj && excludedVerticesOnActiveObject is not null && excludedVerticesOnActiveObject.Contains(vert.Id))
                {
                    continue;
                }

                Vector3 worldPos = Vector3.Transform(vert.Position, localToWorld);
                float distToRefSq = Vector3.DistanceSquared(worldPos, referenceWorldPoint);
                float metric = distToRefSq;

                if (cursorRay.HasValue)
                {
                    float rayDistSq = DistancePointToRaySquared(worldPos, cursorRay.Value);
                    metric = System.Math.Min(distToRefSq, rayDistSq);
                }

                if (metric <= maxDistSq && metric < bestMetric)
                {
                    bestMetric = metric;
                    hit = new SnapTargetHit(SnapTargetMode.Vertex, worldPos, Vector3.UnitY, obj.Id);
                    found = true;
                }
            }
        }

        return found;
    }

    private static bool TryFindNearestEdge(
        EditorScene scene,
        SelectionManager? selection,
        int? activeEditObjectId,
        HashSet<int>? excludedVerticesOnActiveObject,
        Vector3 referenceWorldPoint,
        Ray3D? cursorRay,
        float maxSnapDistanceWorld,
        bool snapToEdgeMidpointOnly,
        out SnapTargetHit hit)
    {
        hit = default;
        float bestMetric = float.PositiveInfinity;
        float maxDistSq = maxSnapDistanceWorld * maxSnapDistanceWorld;
        float midpointThresholdSq = (maxSnapDistanceWorld * 0.35f) * (maxSnapDistanceWorld * 0.35f);
        bool found = false;

        for (int i = 0; i < scene.Objects.Count; i++)
        {
            SceneObject obj = scene.Objects[i];
            if (!IsObjectEligible(obj, selection, activeEditObjectId))
            {
                continue;
            }

            EditableMesh mesh = obj.Mesh!;
            TopologyGraph topology = mesh.GetTopology();
            Matrix4x4 localToWorld = obj.Transform.GetLocalMatrix();
            bool isActiveEditObj = activeEditObjectId.HasValue && obj.Id == activeEditObjectId.Value;

            for (int e = 0; e < topology.Edges.Count; e++)
            {
                EdgeKey key = topology.Edges[e].Key;
                if (isActiveEditObj && excludedVerticesOnActiveObject is not null &&
                    (excludedVerticesOnActiveObject.Contains(key.VMin) || excludedVerticesOnActiveObject.Contains(key.VMax)))
                {
                    continue;
                }

                MeshVertex? vA = mesh.GetVertex(key.VMin);
                MeshVertex? vB = mesh.GetVertex(key.VMax);
                if (vA is null || vB is null)
                {
                    continue;
                }

                Vector3 pA = Vector3.Transform(vA.Position, localToWorld);
                Vector3 pB = Vector3.Transform(vB.Position, localToWorld);
                Vector3 mid = (pA + pB) * 0.5f;

                if (snapToEdgeMidpointOnly)
                {
                    float midDistSq = Vector3.DistanceSquared(mid, referenceWorldPoint);
                    if (cursorRay.HasValue)
                    {
                        midDistSq = System.Math.Min(midDistSq, DistancePointToRaySquared(mid, cursorRay.Value));
                    }

                    if (midDistSq <= maxDistSq && midDistSq < bestMetric)
                    {
                        bestMetric = midDistSq;
                        Vector3 edgeDir = Vector3.Normalize(pB - pA);
                        hit = new SnapTargetHit(SnapTargetMode.Edge, mid, edgeDir, obj.Id, IsEdgeMidpoint: true);
                        found = true;
                    }
                    continue;
                }

                Vector3 closestOnSeg = ClosestPointOnSegment(referenceWorldPoint, pA, pB);
                if (cursorRay.HasValue)
                {
                    Vector3 rayClosestOnSeg = ClosestPointOnSegmentToRay(pA, pB, cursorRay.Value);
                    if (DistancePointToRaySquared(rayClosestOnSeg, cursorRay.Value) < Vector3.DistanceSquared(closestOnSeg, referenceWorldPoint))
                    {
                        closestOnSeg = rayClosestOnSeg;
                    }
                }

                bool isMid = false;
                if (Vector3.DistanceSquared(closestOnSeg, mid) <= midpointThresholdSq)
                {
                    closestOnSeg = mid;
                    isMid = true;
                }

                float metric = Vector3.DistanceSquared(closestOnSeg, referenceWorldPoint);
                if (cursorRay.HasValue)
                {
                    metric = System.Math.Min(metric, DistancePointToRaySquared(closestOnSeg, cursorRay.Value));
                }

                if (metric <= maxDistSq && metric < bestMetric)
                {
                    bestMetric = metric;
                    Vector3 edgeDir = (pB - pA).LengthSquared() > 1e-10f ? Vector3.Normalize(pB - pA) : Vector3.UnitY;
                    hit = new SnapTargetHit(SnapTargetMode.Edge, closestOnSeg, edgeDir, obj.Id, IsEdgeMidpoint: isMid);
                    found = true;
                }
            }
        }

        return found;
    }

    private static bool TryFindNearestFace(
        EditorScene scene,
        SelectionManager? selection,
        int? activeEditObjectId,
        HashSet<int>? excludedVerticesOnActiveObject,
        Vector3 referenceWorldPoint,
        Ray3D? cursorRay,
        float maxSnapDistanceWorld,
        out SnapTargetHit hit)
    {
        hit = default;

        // 1. If a cursor ray is provided, raycast against all eligible faces first
        if (cursorRay.HasValue)
        {
            float closestRayDist = float.PositiveInfinity;
            bool rayHitFound = false;

            for (int i = 0; i < scene.Objects.Count; i++)
            {
                SceneObject obj = scene.Objects[i];
                if (!IsObjectEligible(obj, selection, activeEditObjectId))
                {
                    continue;
                }

                bool isActiveEditObj = activeEditObjectId.HasValue && obj.Id == activeEditObjectId.Value;
                if (!isActiveEditObj)
                {
                    FaceHit? faceHit = MeshPicker.PickFace(obj, cursorRay.Value, ignoreBackfaces: true);
                    if (faceHit.HasValue && faceHit.Value.Distance < closestRayDist)
                    {
                        closestRayDist = faceHit.Value.Distance;
                        Vector3 localNormal = obj.Mesh!.GetFace(faceHit.Value.FaceId)?.ComputeNormal(obj.Mesh) ?? Vector3.UnitY;
                        Vector3 worldNormal = Vector3.TransformNormal(localNormal, obj.Transform.GetLocalMatrix());
                        worldNormal = worldNormal.LengthSquared() > 1e-10f ? Vector3.Normalize(worldNormal) : Vector3.UnitY;
                        hit = new SnapTargetHit(SnapTargetMode.Face, faceHit.Value.HitPoint, worldNormal, obj.Id);
                        rayHitFound = true;
                    }
                }
            }

            if (rayHitFound)
            {
                return true;
            }
        }

        // 2. Fallback / proximity query: find closest point on any eligible face triangle within maxSnapDistanceWorld
        float bestDistSq = maxSnapDistanceWorld * maxSnapDistanceWorld;
        bool found = false;

        for (int i = 0; i < scene.Objects.Count; i++)
        {
            SceneObject obj = scene.Objects[i];
            if (!IsObjectEligible(obj, selection, activeEditObjectId))
            {
                continue;
            }

            EditableMesh mesh = obj.Mesh!;
            Matrix4x4 localToWorld = obj.Transform.GetLocalMatrix();
            bool isActiveEditObj = activeEditObjectId.HasValue && obj.Id == activeEditObjectId.Value;

            for (int f = 0; f < mesh.Faces.Count; f++)
            {
                PolygonFace face = mesh.Faces[f];
                if (isActiveEditObj && excludedVerticesOnActiveObject is not null)
                {
                    bool touchesExcluded = false;
                    for (int k = 0; k < face.VertexIds.Count; k++)
                    {
                        if (excludedVerticesOnActiveObject.Contains(face.VertexIds[k]))
                        {
                            touchesExcluded = true;
                            break;
                        }
                    }
                    if (touchesExcluded) continue;
                }

                IReadOnlyList<FaceTriangle> tris = face.GetTrianglesWithLocalIndices(mesh);
                for (int t = 0; t < tris.Count; t++)
                {
                    FaceTriangle tri = tris[t];
                    MeshVertex? v0 = mesh.GetVertex(tri.V0);
                    MeshVertex? v1 = mesh.GetVertex(tri.V1);
                    MeshVertex? v2 = mesh.GetVertex(tri.V2);
                    if (v0 is null || v1 is null || v2 is null) continue;

                    Vector3 a = Vector3.Transform(v0.Position, localToWorld);
                    Vector3 b = Vector3.Transform(v1.Position, localToWorld);
                    Vector3 c = Vector3.Transform(v2.Position, localToWorld);

                    Vector3 closest = ClosestPointOnTriangle(referenceWorldPoint, a, b, c);
                    float dSq = Vector3.DistanceSquared(closest, referenceWorldPoint);
                    if (dSq <= bestDistSq)
                    {
                        bestDistSq = dSq;
                        Vector3 worldNormal = Vector3.Cross(b - a, c - a);
                        worldNormal = worldNormal.LengthSquared() > 1e-10f ? Vector3.Normalize(worldNormal) : Vector3.UnitY;
                        hit = new SnapTargetHit(SnapTargetMode.Face, closest, worldNormal, obj.Id);
                        found = true;
                    }
                }
            }
        }

        return found;
    }

    private static float DistancePointToRaySquared(Vector3 point, Ray3D ray)
    {
        Vector3 toPoint = point - ray.Origin;
        float t = System.Math.Max(0.0f, Vector3.Dot(toPoint, ray.Direction));
        Vector3 proj = ray.Origin + (ray.Direction * t);
        return Vector3.DistanceSquared(point, proj);
    }

    private static Vector3 ClosestPointOnSegment(Vector3 point, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float lenSq = ab.LengthSquared();
        if (lenSq <= 1e-12f)
        {
            return a;
        }

        float t = System.Math.Clamp(Vector3.Dot(point - a, ab) / lenSq, 0.0f, 1.0f);
        return a + (ab * t);
    }

    private static Vector3 ClosestPointOnSegmentToRay(Vector3 a, Vector3 b, Ray3D ray)
    {
        Vector3 u = b - a;
        Vector3 v = ray.Direction;
        Vector3 w = a - ray.Origin;

        float uu = Vector3.Dot(u, u);
        float uv = Vector3.Dot(u, v);
        float vv = Vector3.Dot(v, v);
        float uw = Vector3.Dot(u, w);
        float vw = Vector3.Dot(v, w);

        float denom = (uu * vv) - (uv * uv);
        if (denom <= 1e-8f)
        {
            return a;
        }

        float s = System.Math.Clamp(((uv * vw) - (vv * uw)) / denom, 0.0f, 1.0f);
        return a + (u * s);
    }

    private static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a;
        Vector3 ac = c - a;
        Vector3 ap = p - a;

        float d1 = Vector3.Dot(ab, ap);
        float d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0.0f && d2 <= 0.0f) return a;

        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp);
        float d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0.0f && d4 <= d3) return b;

        float vc = (d1 * d4) - (d3 * d2);
        if (vc <= 0.0f && d1 >= 0.0f && d3 <= 0.0f)
        {
            float v = d1 / (d1 - d3);
            return a + (v * ab);
        }

        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp);
        float d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0.0f && d5 <= d6) return c;

        float vb = (d5 * d2) - (d1 * d6);
        if (vb <= 0.0f && d2 >= 0.0f && d6 <= 0.0f)
        {
            float w = d2 / (d2 - d6);
            return a + (w * ac);
        }

        float va = (d3 * d6) - (d5 * d4);
        if (va <= 0.0f && (d4 - d3) >= 0.0f && (d5 - d6) >= 0.0f)
        {
            float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            return b + (w * (c - b));
        }

        float denom = 1.0f / (va + vb + vc);
        float vCoord = vb * denom;
        float wCoord = vc * denom;
        return a + (ab * vCoord) + (ac * wCoord);
    }
}
