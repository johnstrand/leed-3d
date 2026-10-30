using System.Numerics;
using Leed3D.Kernel.Entities;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Scene;
using Leed3D.Runtime;
using Leed3D.Runtime.Metadata;

namespace Leed3D.Kernel.Physics;

/// <summary>
/// Represents a single world-space collision triangle extracted from a <see cref="SceneObject"/> face.
/// </summary>
public sealed class CollisionTriangle
{
    public Vector3 V0 { get; init; }
    public Vector3 V1 { get; init; }
    public Vector3 V2 { get; init; }
    public Vector3 Normal { get; init; }
    public BoundingBox3D Bounds { get; init; }
    public int ObjectId { get; init; }
    public int FaceId { get; init; }
    public string MaterialId { get; init; } = string.Empty;
    public ElementMetadata Metadata { get; init; } = new();
}

/// <summary>
/// Represents an active trigger or portal volume box in world space.
/// </summary>
public sealed class CollisionTriggerVolume
{
    public string EntityId { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public BoundingBox3D Bounds { get; init; }
    public Vector3 Position { get; init; }
    public float YawDegrees { get; init; }
    public string TargetId { get; init; } = string.Empty;
    public string EventName { get; init; } = string.Empty;
    public bool OneShot { get; init; }
    public ElementMetadata Properties { get; init; } = new();
}

/// <summary>
/// Represents a named destination point for portal teleportation or trigger linking.
/// </summary>
public sealed class TeleportDestination
{
    public string TargetId { get; init; } = string.Empty;
    public Vector3 Position { get; init; }
    public float? YawDegrees { get; init; }
}

/// <summary>
/// Pre-collected world-space collision mesh and trigger volume database built from an <see cref="EditorScene"/>.
/// Automatically filters out objects and faces tagged with <c>collision=none</c>.
/// </summary>
public sealed class CollisionMeshWorld
{
    private readonly List<CollisionTriangle> _triangles = [];
    private readonly List<CollisionTriggerVolume> _triggers = [];
    private readonly Dictionary<string, TeleportDestination> _destinations = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<CollisionTriangle> Triangles => _triangles;
    public IReadOnlyList<CollisionTriggerVolume> Triggers => _triggers;
    public IReadOnlyDictionary<string, TeleportDestination> Destinations => _destinations;

    public void AddTriangle(CollisionTriangle triangle)
    {
        ArgumentNullException.ThrowIfNull(triangle);
        if (!triangle.Metadata.IsCollisionDisabled())
        {
            _triangles.Add(triangle);
        }
    }

    public void AddTrigger(CollisionTriggerVolume trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        _triggers.Add(trigger);
    }

    public void AddDestination(TeleportDestination destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!string.IsNullOrWhiteSpace(destination.TargetId))
        {
            _destinations[destination.TargetId] = destination;
        }
    }

    /// <summary>
    /// Builds a <see cref="CollisionMeshWorld"/> from all visible collidable objects and entities in <paramref name="scene"/>.
    /// </summary>
    public static CollisionMeshWorld Build(EditorScene scene, EntitySchemaRegistry? entityRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var world = new CollisionMeshWorld();

        // 1. Collect world-space triangles from visible SceneObjects
        for (int i = 0; i < scene.Objects.Count; i++)
        {
            SceneObject obj = scene.Objects[i];
            if (!obj.Visible)
            {
                continue;
            }

            // Register object as potential target destination by name and ID
            if (!string.IsNullOrWhiteSpace(obj.Name))
            {
                world.AddDestination(new TeleportDestination
                {
                    TargetId = obj.Name,
                    Position = obj.Transform.Position,
                    YawDegrees = obj.Transform.RotationEulerDegrees.Y
                });
            }

            world.AddDestination(new TeleportDestination
            {
                TargetId = $"obj_{obj.Id}",
                Position = obj.Transform.Position,
                YawDegrees = obj.Transform.RotationEulerDegrees.Y
            });

            if (obj.Mesh is null || obj.Metadata.IsCollisionDisabled())
            {
                continue;
            }

            EditableMesh mesh = obj.Mesh;
            Matrix4x4 worldMatrix = obj.Transform.GetLocalMatrix();

            for (int fIdx = 0; fIdx < mesh.Faces.Count; fIdx++)
            {
                PolygonFace face = mesh.Faces[fIdx];
                if (face.Metadata.IsCollisionDisabled())
                {
                    continue;
                }

                // Merge object metadata with face metadata (face properties take precedence)
                ElementMetadata mergedMeta = obj.Metadata.Clone();
                mergedMeta.MergeFrom(face.Metadata);
                if (mergedMeta.IsCollisionDisabled())
                {
                    continue;
                }

                Vector3 localNormal = face.ComputeNormal(mesh);
                Vector3 faceWorldNormal = obj.Transform.TransformDirectionToWorld(localNormal);
                if (faceWorldNormal.LengthSquared() > 1e-12f)
                {
                    faceWorldNormal = Vector3.Normalize(faceWorldNormal);
                }
                else
                {
                    faceWorldNormal = Vector3.UnitY;
                }

                IReadOnlyList<(int V0, int V1, int V2)> tris = PolygonTriangulator.TriangulateFace(face, mesh);
                for (int tIdx = 0; tIdx < tris.Count; tIdx++)
                {
                    var (id0, id1, id2) = tris[tIdx];
                    MeshVertex? mv0 = mesh.GetVertex(id0);
                    MeshVertex? mv1 = mesh.GetVertex(id1);
                    MeshVertex? mv2 = mesh.GetVertex(id2);
                    if (mv0 is null || mv1 is null || mv2 is null)
                    {
                        continue;
                    }

                    Vector3 p0 = Vector3.Transform(mv0.Position, worldMatrix);
                    Vector3 p1 = Vector3.Transform(mv1.Position, worldMatrix);
                    Vector3 p2 = Vector3.Transform(mv2.Position, worldMatrix);

                    Vector3 triNormal = Vector3.Cross(p1 - p0, p2 - p0);
                    if (triNormal.LengthSquared() > 1e-12f)
                    {
                        triNormal = Vector3.Normalize(triNormal);
                        // Ensure consistency with Newell face normal if non-degenerate
                        if (Vector3.Dot(triNormal, faceWorldNormal) < 0.0f)
                        {
                            triNormal = -triNormal;
                        }
                    }
                    else
                    {
                        triNormal = faceWorldNormal;
                    }

                    BoundingBox3D bounds = BoundingBox3D.FromPoints([p0, p1, p2]);

                    world._triangles.Add(new CollisionTriangle
                    {
                        V0 = p0,
                        V1 = p1,
                        V2 = p2,
                        Normal = triNormal,
                        Bounds = bounds,
                        ObjectId = obj.Id,
                        FaceId = face.Id,
                        MaterialId = face.MaterialId,
                        Metadata = mergedMeta
                    });
                }
            }
        }

        // 2. Collect portal/trigger volumes and destination entities
        for (int i = 0; i < scene.Entities.Count; i++)
        {
            LevelEntity ent = scene.Entities[i];
            if (!ent.Visible)
            {
                continue;
            }

            float entYaw = ent.Properties.GetFloat("yaw") ?? ent.Transform.RotationEulerDegrees.Y;
            world.AddDestination(new TeleportDestination
            {
                TargetId = ent.Id,
                Position = ent.Transform.Position,
                YawDegrees = entYaw
            });

            bool isPortal = ent.ClassName.Equals("portal", StringComparison.OrdinalIgnoreCase);
            bool isTrigger = ent.ClassName.Equals("trigger", StringComparison.OrdinalIgnoreCase);
            bool isBoxVolume = false;

            if (entityRegistry is not null && entityRegistry.TryGetDefinition(ent.ClassName, out EntityDefinition? def) && def is not null)
            {
                isBoxVolume = def.GizmoShape == EntityGizmoShape.BoxVolume;
            }

            if (isPortal || isTrigger || isBoxVolume)
            {
                BoundingBox3D bounds = ent.GetWorldBounds(entityRegistry);
                string targetId = ent.Properties.GetString("target_id") ?? string.Empty;
                string eventName = ent.Properties.GetString("event_name") ?? string.Empty;
                bool oneShot = ent.Properties.GetBool("one_shot", false);

                world._triggers.Add(new CollisionTriggerVolume
                {
                    EntityId = ent.Id,
                    ClassName = ent.ClassName,
                    Bounds = bounds,
                    Position = ent.Transform.Position,
                    YawDegrees = entYaw,
                    TargetId = targetId,
                    EventName = eventName,
                    OneShot = oneShot,
                    Properties = ent.Properties.Clone()
                });
            }
        }

        return world;
    }

    /// <summary>
    /// Builds a <see cref="CollisionMeshWorld"/> directly from pre-baked runtime layer data.
    /// </summary>
    public static CollisionMeshWorld BuildFromBaked(BakedRuntimeLayerData baked)
    {
        ArgumentNullException.ThrowIfNull(baked);

        var world = new CollisionMeshWorld();

        for (int i = 0; i < baked.CollisionTriangles.Count; i++)
        {
            BakedCollisionTriangleDto tri = baked.CollisionTriangles[i];
            Vector3 v0 = tri.V0;
            Vector3 v1 = tri.V1;
            Vector3 v2 = tri.V2;
            Vector3 normal = tri.Normal;
            BoundingBox3D bounds = BoundingBox3D.FromPoints([v0, v1, v2]);

            world._triangles.Add(new CollisionTriangle
            {
                V0 = v0,
                V1 = v1,
                V2 = v2,
                Normal = normal,
                Bounds = bounds,
                ObjectId = 0,
                FaceId = 0,
                MaterialId = tri.MaterialId,
                Metadata = tri.SurfaceMetadata.Clone()
            });
        }

        for (int i = 0; i < baked.Entities.Count; i++)
        {
            LevelEntityDto ent = baked.Entities[i];
            if (!ent.Visible)
            {
                continue;
            }

            float entYaw = ent.Properties.GetFloat("yaw") ?? ent.Transform.RotationEulerDegrees.Y;
            world.AddDestination(new TeleportDestination
            {
                TargetId = ent.Id,
                Position = ent.Transform.Position,
                YawDegrees = entYaw
            });

            bool isPortal = ent.ClassName.Equals("portal", StringComparison.OrdinalIgnoreCase);
            bool isTrigger = ent.ClassName.Equals("trigger", StringComparison.OrdinalIgnoreCase);

            if (isPortal || isTrigger)
            {
                Vector3 size = Vector3.One;
                if (ent.Properties.TryGet("size", out var sizeVal) && sizeVal is not null && sizeVal.Type == MetadataValueType.Vec3)
                {
                    size = sizeVal.Vec3Value;
                }
                else if (ent.Properties.TryGet("box_size", out var bsizeVal) && bsizeVal is not null && bsizeVal.Type == MetadataValueType.Vec3)
                {
                    size = bsizeVal.Vec3Value;
                }

                Vector3 half = size * 0.5f;
                Vector3 pos = ent.Transform.Position;
                BoundingBox3D bounds = new(pos - half, pos + half);

                string targetId = ent.Properties.GetString("target_id") ?? string.Empty;
                string eventName = ent.Properties.GetString("event_name") ?? string.Empty;
                bool oneShot = ent.Properties.GetBool("one_shot", false);

                world.AddTrigger(new CollisionTriggerVolume
                {
                    EntityId = ent.Id,
                    ClassName = ent.ClassName,
                    Bounds = bounds,
                    Position = pos,
                    YawDegrees = entYaw,
                    TargetId = targetId,
                    EventName = eventName,
                    OneShot = oneShot,
                    Properties = ent.Properties.Clone()
                });
            }
        }

        return world;
    }

    /// <summary>
    /// Clips a 3D triangle against the horizontal slab <c>[yMin, yMax]</c> using Sutherland-Hodgman clipping.
    /// Returns the number of vertices in <paramref name="outPoly"/> (0 if outside the slab).
    /// </summary>
    public static int ClipTriangleToYSlab(
        Vector3 v0,
        Vector3 v1,
        Vector3 v2,
        float yMin,
        float yMax,
        Span<Vector3> outPoly)
    {
        Span<Vector3> polyA = stackalloc Vector3[8];
        Span<Vector3> polyB = stackalloc Vector3[8];

        polyA[0] = v0;
        polyA[1] = v1;
        polyA[2] = v2;
        int countA = 3;

        // Clip against Y >= yMin
        int countB = 0;
        for (int i = 0; i < countA; i++)
        {
            Vector3 curr = polyA[i];
            Vector3 next = polyA[(i + 1) % countA];
            bool currIn = curr.Y >= yMin;
            bool nextIn = next.Y >= yMin;

            if (currIn)
            {
                polyB[countB++] = curr;
            }

            if (currIn != nextIn)
            {
                float dy = next.Y - curr.Y;
                float t = System.Math.Abs(dy) > 1e-8f ? System.Math.Clamp((yMin - curr.Y) / dy, 0.0f, 1.0f) : 0.5f;
                Vector3 hit = Vector3.Lerp(curr, next, t);
                hit.Y = yMin;
                polyB[countB++] = hit;
            }
        }

        if (countB == 0)
        {
            return 0;
        }

        // Clip against Y <= yMax
        int outCount = 0;
        for (int i = 0; i < countB; i++)
        {
            Vector3 curr = polyB[i];
            Vector3 next = polyB[(i + 1) % countB];
            bool currIn = curr.Y <= yMax;
            bool nextIn = next.Y <= yMax;

            if (currIn)
            {
                outPoly[outCount++] = curr;
            }

            if (currIn != nextIn)
            {
                float dy = next.Y - curr.Y;
                float t = System.Math.Abs(dy) > 1e-8f ? System.Math.Clamp((yMax - curr.Y) / dy, 0.0f, 1.0f) : 0.5f;
                Vector3 hit = Vector3.Lerp(curr, next, t);
                hit.Y = yMax;
                outPoly[outCount++] = hit;
            }
        }

        return outCount;
    }

    /// <summary>
    /// Computes the closest 2D point on a polygon (or degenerate segment) in XZ space to <paramref name="p"/>.
    /// </summary>
    public static Vector2 ClosestPointOnPolygon2D(Vector2 p, ReadOnlySpan<Vector2> poly)
    {
        int count = poly.Length;
        if (count == 0)
        {
            return p;
        }
        if (count == 1)
        {
            return poly[0];
        }

        // Check if p is strictly inside any fan triangle (poly[0], poly[i], poly[i+1])
        for (int i = 1; i < count - 1; i++)
        {
            if (IsPointInTriangle2D(p, poly[0], poly[i], poly[i + 1]))
            {
                return p;
            }
        }

        // Otherwise, closest point lies on one of the perimeter segments
        Vector2 bestPoint = poly[0];
        float bestDistSq = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            Vector2 a = poly[i];
            Vector2 b = poly[(i + 1) % count];
            Vector2 candidate = ClosestPointOnSegment2D(p, a, b);
            float distSq = Vector2.DistanceSquared(p, candidate);
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestPoint = candidate;
            }
        }

        return bestPoint;
    }

    public static Vector2 ClosestPointOnSegment2D(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lenSq = ab.LengthSquared();
        if (lenSq <= 1e-12f)
        {
            return a;
        }

        float t = System.Math.Clamp(Vector2.Dot(p - a, ab) / lenSq, 0.0f, 1.0f);
        return a + (ab * t);
    }

    public static bool IsPointInTriangle2D(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float area2 = Cross2D(b - a, c - a);
        if (System.Math.Abs(area2) <= 1e-7f)
        {
            return false;
        }

        float d1 = Cross2D(b - a, p - a);
        float d2 = Cross2D(c - b, p - b);
        float d3 = Cross2D(a - c, p - c);

        bool hasNeg = (d1 < -1e-6f) || (d2 < -1e-6f) || (d3 < -1e-6f);
        bool hasPos = (d1 > 1e-6f) || (d2 > 1e-6f) || (d3 > 1e-6f);

        return !(hasNeg && hasPos);
    }

    private static float Cross2D(Vector2 u, Vector2 v) => (u.X * v.Y) - (u.Y * v.X);

    /// <summary>
    /// Computes the closest point on 3D triangle <c>(a, b, c)</c> to point <paramref name="p"/>.
    /// </summary>
    public static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
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
