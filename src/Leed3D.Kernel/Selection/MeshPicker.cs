using System.Drawing;
using System.Numerics;
using Leed3D.Kernel.Entities;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Scene;

namespace Leed3D.Kernel.Selection;

/// <summary>
/// Result of an object-level 3D raycast hit.
/// </summary>
public readonly record struct ObjectHit(int ObjectId, int FaceId, float Distance, Vector3 HitPoint);

/// <summary>
/// Result of an entity-level 3D raycast hit.
/// </summary>
public readonly record struct EntityHit(string EntityId, float Distance, Vector3 HitPoint);

/// <summary>
/// Result of a face-level 3D raycast hit on a single object.
/// </summary>
public readonly record struct FaceHit(int FaceId, float Distance, Vector3 HitPoint);

/// <summary>
/// Result of a marquee 2D box selection.
/// </summary>
public sealed class MarqueeSelectionResult
{
    public List<int> ObjectIds { get; } = [];
    public List<string> EntityIds { get; } = [];
    public List<int> VertexIds { get; } = [];
    public List<EdgeKey> Edges { get; } = [];
    public List<int> FaceIds { get; } = [];
}

/// <summary>
/// 100% headless raycasting and screen-space picking engine for objects and sub-elements
/// (vertices, edges, and N-gon faces).
/// </summary>
public static class MeshPicker
{
    private const float Epsilon = 1e-6f;

    /// <summary>
    /// Performs broadphase AABB and narrowphase N-gon raycast against all visible, unlocked objects in <paramref name="scene"/>.
    /// Returns the closest hit if any.
    /// </summary>
    public static ObjectHit? PickObject(EditorScene scene, Ray3D worldRay)
    {
        ArgumentNullException.ThrowIfNull(scene);

        ObjectHit? closestHit = null;
        float minDistance = float.PositiveInfinity;

        foreach (SceneObject obj in scene.Objects)
        {
            if (!obj.Visible || obj.Locked || obj.Mesh is null || obj.Mesh.FaceCount == 0)
            {
                continue;
            }

            // Broadphase: ray vs world-space AABB
            if (!worldRay.IntersectBoundingBox(obj.GetWorldBounds(), out float tMin, out float tMax))
            {
                continue;
            }

            if (tMin >= minDistance)
            {
                continue;
            }

            // Narrowphase: ray vs triangulated faces in local space
            FaceHit? faceHit = PickFace(obj, worldRay, ignoreBackfaces: true);
            if (faceHit.HasValue && faceHit.Value.Distance < minDistance)
            {
                minDistance = faceHit.Value.Distance;
                closestHit = new ObjectHit(obj.Id, faceHit.Value.FaceId, faceHit.Value.Distance, faceHit.Value.HitPoint);
            }
        }

        return closestHit;
    }

    /// <summary>
    /// Performs raycast against all visible, unlocked gameplay entities in <paramref name="scene"/>.
    /// Returns the closest hit if any.
    /// </summary>
    public static EntityHit? PickEntity(EditorScene scene, Ray3D worldRay, EntitySchemaRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(scene);

        EntityHit? closestHit = null;
        float minDistance = float.PositiveInfinity;

        foreach (LevelEntity entity in scene.Entities)
        {
            if (!entity.Visible || entity.Locked)
            {
                continue;
            }

            BoundingBox3D bounds = entity.GetWorldBounds(registry);
            if (worldRay.IntersectBoundingBox(bounds, out float tMin, out float tMax))
            {
                float hitDist = tMin >= 0 ? tMin : tMax;
                if (hitDist >= 0 && hitDist < minDistance)
                {
                    minDistance = hitDist;
                    Vector3 hitPoint = worldRay.Origin + (worldRay.Direction * hitDist);
                    closestHit = new EntityHit(entity.Id, hitDist, hitPoint);
                }
            }
        }

        return closestHit;
    }

    /// <summary>
    /// Transforms <paramref name="worldRay"/> into <paramref name="obj"/> local space and tests all triangulated
    /// N-gon faces, returning the closest hit <see cref="FaceHit"/> (with distance and hit point expressed in world space).
    /// </summary>
    public static FaceHit? PickFace(SceneObject obj, Ray3D worldRay, bool ignoreBackfaces = true)
    {
        ArgumentNullException.ThrowIfNull(obj);

        if (obj.Mesh is null || obj.Mesh.FaceCount == 0)
        {
            return null;
        }

        Matrix4x4 localToWorld = obj.Transform.GetLocalMatrix();
        if (!Matrix4x4.Invert(localToWorld, out Matrix4x4 worldToLocal))
        {
            return null;
        }

        // Transform ray to local space
        Vector3 localOrigin = Vector3.Transform(worldRay.Origin, worldToLocal);
        Vector3 localDir = Vector3.TransformNormal(worldRay.Direction, worldToLocal);
        float localDirLen = localDir.Length();
        if (localDirLen < Epsilon)
        {
            return null;
        }
        localDir /= localDirLen;

        var localRay = new Ray3D(localOrigin, localDir);

        // Local AABB broadphase
        if (!localRay.IntersectBoundingBox(obj.Mesh.ComputeBoundingBox(), out _, out _))
        {
            return null;
        }

        FaceHit? bestHit = null;
        float minWorldDist = float.PositiveInfinity;

        EditableMesh mesh = obj.Mesh;
        for (int i = 0; i < mesh.Faces.Count; i++)
        {
            PolygonFace face = mesh.Faces[i];
            IReadOnlyList<(int V0, int V1, int V2)> triangles = PolygonTriangulator.TriangulateFace(face, mesh);

            for (int t = 0; t < triangles.Count; t++)
            {
                var (v0, v1, v2) = triangles[t];
                MeshVertex? mv0 = mesh.GetVertex(v0);
                MeshVertex? mv1 = mesh.GetVertex(v1);
                MeshVertex? mv2 = mesh.GetVertex(v2);

                if (mv0 is null || mv1 is null || mv2 is null)
                {
                    continue;
                }

                if (localRay.IntersectTriangle(mv0.Position, mv1.Position, mv2.Position, cullBackface: ignoreBackfaces, out _, out Vector3 localHit))
                {
                    Vector3 worldHit = Vector3.Transform(localHit, localToWorld);
                    float worldDist = Vector3.Distance(worldRay.Origin, worldHit);

                    if (worldDist < minWorldDist)
                    {
                        minWorldDist = worldDist;
                        bestHit = new FaceHit(face.Id, worldDist, worldHit);
                    }
                }
            }
        }

        return bestHit;
    }

    /// <summary>
    /// Projects visible mesh edges (<see cref="TopologyGraph.Edges"/>) to screen space using <paramref name="worldToScreen"/>,
    /// finding edges whose 2D segment distance is within <paramref name="maxPixelDistance"/>, breaking ties by closest 3D depth to ray origin.
    /// </summary>
    public static EdgeKey? PickEdge(
        SceneObject obj,
        Ray3D worldRay,
        Func<Vector3, Vector2?> worldToScreen,
        Vector2 mouseScreenPos,
        float maxPixelDistance = 10f)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(worldToScreen);

        if (obj.Mesh is null || obj.Mesh.VertexCount < 2)
        {
            return null;
        }

        Matrix4x4 localToWorld = obj.Transform.GetLocalMatrix();
        TopologyGraph topology = obj.Mesh.GetTopology();

        EdgeKey? bestEdge = null;
        float bestDepthSq = float.PositiveInfinity;

        foreach (TopologyEdge edge in topology.Edges)
        {
            MeshVertex? vA = obj.Mesh.GetVertex(edge.Key.VMin);
            MeshVertex? vB = obj.Mesh.GetVertex(edge.Key.VMax);
            if (vA is null || vB is null)
            {
                continue;
            }

            Vector3 wA = Vector3.Transform(vA.Position, localToWorld);
            Vector3 wB = Vector3.Transform(vB.Position, localToWorld);

            Vector2? sA = worldToScreen(wA);
            Vector2? sB = worldToScreen(wB);

            // If both vertices are behind camera, skip
            if (!sA.HasValue && !sB.HasValue)
            {
                continue;
            }

            // Both endpoints must project cleanly to screen
            if (!sA.HasValue || !sB.HasValue)
            {
                continue;
            }

            float pixelDist = DistancePointToSegment2D(mouseScreenPos, sA.Value, sB.Value, out float t);
            if (pixelDist <= maxPixelDistance)
            {
                Vector3 wPoint = Vector3.Lerp(wA, wB, t);
                float depthSq = Vector3.DistanceSquared(worldRay.Origin, wPoint);

                if (depthSq < bestDepthSq)
                {
                    bestDepthSq = depthSq;
                    bestEdge = edge.Key;
                }
            }
        }

        return bestEdge;
    }

    /// <summary>
    /// Finds the closest vertex in <paramref name="obj"/> within <paramref name="maxPixelDistance"/> screen pixels of <paramref name="mouseScreenPos"/>.
    /// </summary>
    public static int? PickVertex(
        SceneObject obj,
        Func<Vector3, Vector2?> worldToScreen,
        Vector2 mouseScreenPos,
        float maxPixelDistance = 12f,
        Vector3? cameraPos = null)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(worldToScreen);

        if (obj.Mesh is null || obj.Mesh.VertexCount == 0)
        {
            return null;
        }

        Matrix4x4 localToWorld = obj.Transform.GetLocalMatrix();
        int? bestVertexId = null;
        float bestPixelDist = maxPixelDistance;
        float bestDepthSq = float.PositiveInfinity;

        foreach (MeshVertex vertex in obj.Mesh.Vertices)
        {
            Vector3 wPos = Vector3.Transform(vertex.Position, localToWorld);
            Vector2? sPos = worldToScreen(wPos);
            if (!sPos.HasValue)
            {
                continue;
            }

            float pixelDist = Vector2.Distance(mouseScreenPos, sPos.Value);
            if (pixelDist <= maxPixelDistance)
            {
                float depthSq = cameraPos.HasValue ? Vector3.DistanceSquared(cameraPos.Value, wPos) : 0f;

                // Prefer closer screen distance, breaking near-ties with camera depth
                if (pixelDist < bestPixelDist - 0.5f || (System.Math.Abs(pixelDist - bestPixelDist) <= 0.5f && depthSq < bestDepthSq))
                {
                    bestPixelDist = pixelDist;
                    bestDepthSq = depthSq;
                    bestVertexId = vertex.Id;
                }
            }
        }

        return bestVertexId;
    }

    /// <summary>
    /// Selects all vertices, edges (both endpoints inside), or faces (centroid inside) of <paramref name="obj"/>
    /// within the 2D drag rectangle <paramref name="screenRect"/>.
    /// </summary>
    public static MarqueeSelectionResult PickBoxMarquee(
        SceneObject obj,
        InteractionMode mode,
        RectangleF screenRect,
        Func<Vector3, Vector2?> worldToScreen)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(worldToScreen);

        var result = new MarqueeSelectionResult();
        if (obj.Mesh is null)
        {
            return result;
        }

        float minX = System.Math.Min(screenRect.Left, screenRect.Right);
        float maxX = System.Math.Max(screenRect.Left, screenRect.Right);
        float minY = System.Math.Min(screenRect.Top, screenRect.Bottom);
        float maxY = System.Math.Max(screenRect.Top, screenRect.Bottom);

        bool IsInside(Vector2 pt) => pt.X >= minX && pt.X <= maxX && pt.Y >= minY && pt.Y <= maxY;

        Matrix4x4 localToWorld = obj.Transform.GetLocalMatrix();
        EditableMesh mesh = obj.Mesh;

        switch (mode)
        {
            case InteractionMode.Vertex:
            {
                foreach (MeshVertex vertex in mesh.Vertices)
                {
                    Vector3 wPos = Vector3.Transform(vertex.Position, localToWorld);
                    Vector2? sPos = worldToScreen(wPos);
                    if (sPos.HasValue && IsInside(sPos.Value))
                    {
                        result.VertexIds.Add(vertex.Id);
                    }
                }
                break;
            }

            case InteractionMode.Edge:
            {
                TopologyGraph topology = mesh.GetTopology();
                foreach (TopologyEdge edge in topology.Edges)
                {
                    MeshVertex? vA = mesh.GetVertex(edge.Key.VMin);
                    MeshVertex? vB = mesh.GetVertex(edge.Key.VMax);
                    if (vA is null || vB is null)
                    {
                        continue;
                    }

                    Vector3 wA = Vector3.Transform(vA.Position, localToWorld);
                    Vector3 wB = Vector3.Transform(vB.Position, localToWorld);

                    Vector2? sA = worldToScreen(wA);
                    Vector2? sB = worldToScreen(wB);

                    if (sA.HasValue && sB.HasValue && IsInside(sA.Value) && IsInside(sB.Value))
                    {
                        result.Edges.Add(edge.Key);
                    }
                }
                break;
            }

            case InteractionMode.Face:
            {
                foreach (PolygonFace face in mesh.Faces)
                {
                    Vector3 centroid = face.ComputeCentroid(mesh);
                    Vector3 wCentroid = Vector3.Transform(centroid, localToWorld);
                    Vector2? sCentroid = worldToScreen(wCentroid);

                    if (sCentroid.HasValue && IsInside(sCentroid.Value))
                    {
                        result.FaceIds.Add(face.Id);
                    }
                }
                break;
            }
        }

        return result;
    }

    /// <summary>
    /// Selects all visible, unlocked objects in <paramref name="scene"/> whose world position or bounds center projects inside <paramref name="screenRect"/>.
    /// </summary>
    public static List<int> PickObjectsBoxMarquee(
        EditorScene scene,
        RectangleF screenRect,
        Func<Vector3, Vector2?> worldToScreen)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(worldToScreen);

        var result = new List<int>();

        float minX = System.Math.Min(screenRect.Left, screenRect.Right);
        float maxX = System.Math.Max(screenRect.Left, screenRect.Right);
        float minY = System.Math.Min(screenRect.Top, screenRect.Bottom);
        float maxY = System.Math.Max(screenRect.Top, screenRect.Bottom);

        bool IsInside(Vector2 pt) => pt.X >= minX && pt.X <= maxX && pt.Y >= minY && pt.Y <= maxY;

        foreach (SceneObject obj in scene.Objects)
        {
            if (!obj.Visible || obj.Locked)
            {
                continue;
            }

            Vector3 center = obj.Mesh is not null && obj.Mesh.VertexCount > 0
                ? obj.GetWorldBounds().Center
                : obj.Transform.Position;

            Vector2? sPos = worldToScreen(center);
            if (sPos.HasValue && IsInside(sPos.Value))
            {
                result.Add(obj.Id);
            }
        }

        return result;
    }

    /// <summary>
    /// Selects all visible, unlocked entities whose projected screen-space origin/center falls inside <paramref name="screenRect"/>.
    /// </summary>
    public static List<string> PickEntitiesBoxMarquee(
        EditorScene scene,
        RectangleF screenRect,
        Func<Vector3, Vector2?> worldToScreen)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(worldToScreen);

        var result = new List<string>();

        float minX = System.Math.Min(screenRect.Left, screenRect.Right);
        float maxX = System.Math.Max(screenRect.Left, screenRect.Right);
        float minY = System.Math.Min(screenRect.Top, screenRect.Bottom);
        float maxY = System.Math.Max(screenRect.Top, screenRect.Bottom);

        bool IsInside(Vector2 pt) => pt.X >= minX && pt.X <= maxX && pt.Y >= minY && pt.Y <= maxY;

        foreach (LevelEntity entity in scene.Entities)
        {
            if (!entity.Visible || entity.Locked)
            {
                continue;
            }

            Vector2? sPos = worldToScreen(entity.Transform.Position);
            if (sPos.HasValue && IsInside(sPos.Value))
            {
                result.Add(entity.Id);
            }
        }

        return result;
    }

    /// <summary>
    /// Calculates the minimum 2D distance from a point to a line segment AB.
    /// Also returns the normalized parameter t along AB [0..1].
    /// </summary>
    public static float DistancePointToSegment2D(Vector2 p, Vector2 a, Vector2 b, out float t)
    {
        Vector2 ab = b - a;
        float lenSq = ab.LengthSquared();
        if (lenSq < 1e-8f)
        {
            t = 0.0f;
            return Vector2.Distance(p, a);
        }

        t = Vector2.Dot(p - a, ab) / lenSq;
        t = System.Math.Clamp(t, 0.0f, 1.0f);
        Vector2 projection = a + (t * ab);
        return Vector2.Distance(p, projection);
    }
}
