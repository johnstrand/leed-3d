using System.Numerics;

namespace Leed3D.Kernel.Geometry;

/// <summary>
/// Deterministic 2D projected Ear-Clipping N-Gon Triangulator.
/// Projects 3D N-gons onto their dominant cardinal normal plane (XY, XZ, or YZ)
/// while preserving winding orientation, and triangulates both convex and concave polygons.
/// </summary>
public static class PolygonTriangulator
{
    private const float ConvexEpsilon = 1e-6f;
    private const float PointInTriangleEpsilon = -1e-6f;

    /// <summary>
    /// Triangulates a single <see cref="PolygonFace"/> into a list of vertex ID triplets (V0, V1, V2)
    /// whose winding matches the face's outward normal.
    /// </summary>
    public static IReadOnlyList<(int V0, int V1, int V2)> TriangulateFace(PolygonFace face, EditableMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(face);
        ArgumentNullException.ThrowIfNull(mesh);

        List<int> vertexIds = face.VertexIds;
        int n = vertexIds.Count;
        if (n < 3)
        {
            return [];
        }

        // Fast path for triangles
        if (n == 3)
        {
            return [(vertexIds[0], vertexIds[1], vertexIds[2])];
        }

        // Collect 3D vertex positions
        var positions3D = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            MeshVertex? vert = mesh.GetVertex(vertexIds[i]);
            positions3D[i] = vert?.Position ?? Vector3.Zero;
        }

        Vector3 normal = face.ComputeNormal(mesh);

        // Project 3D vertices onto the dominant 2D cardinal plane preserving winding sign
        var poly2D = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            poly2D[i] = ProjectToDominant2DPlane(positions3D[i], normal);
        }

        // Fast path for strictly convex quads: split along the shorter 3D diagonal
        if (n == 4 && IsStrictlyConvexQuad(poly2D))
        {
            float diag02Sq = Vector3.DistanceSquared(positions3D[0], positions3D[2]);
            float diag13Sq = Vector3.DistanceSquared(positions3D[1], positions3D[3]);

            if (diag02Sq <= diag13Sq)
            {
                return
                [
                    (vertexIds[0], vertexIds[1], vertexIds[2]),
                    (vertexIds[0], vertexIds[2], vertexIds[3])
                ];
            }
            else
            {
                return
                [
                    (vertexIds[0], vertexIds[1], vertexIds[3]),
                    (vertexIds[1], vertexIds[2], vertexIds[3])
                ];
            }
        }

        // General N-gon ear-clipping (handles concave polygons, collinear vertices, L/U shapes, doorway cutouts)
        var remaining = new List<int>(n);
        for (int i = 0; i < n; i++)
        {
            remaining.Add(i);
        }

        var triangles = new List<(int V0, int V1, int V2)>(n - 2);

        // Ensure signed 2D area is positive; if numerical noise inverted the projection sign, flip 2D X
        float signedArea2 = ComputeSignedDoubleArea(poly2D);
        if (signedArea2 < 0.0f)
        {
            for (int i = 0; i < n; i++)
            {
                poly2D[i] = new Vector2(-poly2D[i].X, poly2D[i].Y);
            }
        }

        int maxIterations = n * n * 2;
        int iterations = 0;

        while (remaining.Count > 3 && iterations++ < maxIterations)
        {
            int count = remaining.Count;
            int bestEarIdx = -1;
            float bestEarQuality = float.NegativeInfinity;

            // Pass 1: Strict ear test ( strictly convex vertex with no other polygon vertex inside the ear triangle )
            for (int i = 0; i < count; i++)
            {
                int prevIdx = remaining[(i - 1 + count) % count];
                int currIdx = remaining[i];
                int nextIdx = remaining[(i + 1) % count];

                Vector2 a = poly2D[prevIdx];
                Vector2 b = poly2D[currIdx];
                Vector2 c = poly2D[nextIdx];

                float cross = Cross2D(a, b, c);
                if (cross <= ConvexEpsilon)
                {
                    continue;
                }

                if (ContainsAnyPointInTriangle(poly2D, remaining, prevIdx, currIdx, nextIdx, a, b, c))
                {
                    continue;
                }

                // Prefer ears with well-proportioned angles (higher minimum edge-ratio / cross product)
                float perimeter = Vector2.Distance(a, b) + Vector2.Distance(b, c) + Vector2.Distance(c, a);
                float quality = perimeter > 1e-6f ? (cross / (perimeter * perimeter)) : cross;
                if (quality > bestEarQuality)
                {
                    bestEarQuality = quality;
                    bestEarIdx = i;
                }
            }

            // Pass 2: Relaxed collinear/near-degenerate ear test if no strict ear was found
            if (bestEarIdx < 0)
            {
                for (int i = 0; i < count; i++)
                {
                    int prevIdx = remaining[(i - 1 + count) % count];
                    int currIdx = remaining[i];
                    int nextIdx = remaining[(i + 1) % count];

                    Vector2 a = poly2D[prevIdx];
                    Vector2 b = poly2D[currIdx];
                    Vector2 c = poly2D[nextIdx];

                    float cross = Cross2D(a, b, c);
                    if (cross >= -ConvexEpsilon && !ContainsAnyPointInTriangle(poly2D, remaining, prevIdx, currIdx, nextIdx, a, b, c))
                    {
                        bestEarIdx = i;
                        break;
                    }
                }
            }

            // Pass 3: Fallback recovery for self-touching or heavily degenerate polygons (pick largest positive cross product)
            if (bestEarIdx < 0)
            {
                float maxCross = float.NegativeInfinity;
                for (int i = 0; i < count; i++)
                {
                    int prevIdx = remaining[(i - 1 + count) % count];
                    int currIdx = remaining[i];
                    int nextIdx = remaining[(i + 1) % count];

                    float cross = Cross2D(poly2D[prevIdx], poly2D[currIdx], poly2D[nextIdx]);
                    if (cross > maxCross)
                    {
                        maxCross = cross;
                        bestEarIdx = i;
                    }
                }

                if (bestEarIdx < 0)
                {
                    bestEarIdx = 0;
                }
            }

            int earPrev = remaining[(bestEarIdx - 1 + count) % count];
            int earCurr = remaining[bestEarIdx];
            int earNext = remaining[(bestEarIdx + 1) % count];

            triangles.Add((vertexIds[earPrev], vertexIds[earCurr], vertexIds[earNext]));
            remaining.RemoveAt(bestEarIdx);
        }

        if (remaining.Count == 3)
        {
            triangles.Add((vertexIds[remaining[0]], vertexIds[remaining[1]], vertexIds[remaining[2]]));
        }

        return triangles;
    }

    /// <summary>
    /// Triangulates all faces in an <see cref="EditableMesh"/>.
    /// </summary>
    public static IReadOnlyList<(int FaceId, int V0, int V1, int V2)> TriangulateMesh(EditableMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        var result = new List<(int FaceId, int V0, int V1, int V2)>();
        foreach (PolygonFace face in mesh.Faces)
        {
            IReadOnlyList<(int V0, int V1, int V2)> faceTris = TriangulateFace(face, mesh);
            for (int i = 0; i < faceTris.Count; i++)
            {
                var (v0, v1, v2) = faceTris[i];
                result.Add((face.Id, v0, v1, v2));
            }
        }

        return result;
    }

    /// <summary>
    /// Projects a 3D point onto the dominant cardinal plane of <paramref name="normal"/>
    /// such that a loop with positive Newell normal in 3D maps to a counter-clockwise (positive area) 2D loop.
    /// </summary>
    internal static Vector2 ProjectToDominant2DPlane(Vector3 point, Vector3 normal)
    {
        float ax = MathF.Abs(normal.X);
        float ay = MathF.Abs(normal.Y);
        float az = MathF.Abs(normal.Z);

        if (ax >= ay && ax >= az)
        {
            // Dominant X axis -> project onto YZ plane
            return normal.X >= 0.0f
                ? new Vector2(point.Y, point.Z)
                : new Vector2(point.Z, point.Y);
        }

        if (ay >= ax && ay >= az)
        {
            // Dominant Y axis -> project onto XZ plane
            return normal.Y >= 0.0f
                ? new Vector2(point.Z, point.X)
                : new Vector2(point.X, point.Z);
        }

        // Dominant Z axis -> project onto XY plane
        return normal.Z >= 0.0f
            ? new Vector2(point.X, point.Y)
            : new Vector2(point.Y, point.X);
    }

    private static bool IsStrictlyConvexQuad(ReadOnlySpan<Vector2> quad)
    {
        for (int i = 0; i < 4; i++)
        {
            Vector2 a = quad[(i + 3) % 4];
            Vector2 b = quad[i];
            Vector2 c = quad[(i + 1) % 4];
            if (Cross2D(a, b, c) <= ConvexEpsilon)
            {
                return false;
            }
        }

        return true;
    }

    private static float ComputeSignedDoubleArea(ReadOnlySpan<Vector2> poly)
    {
        float sum = 0.0f;
        int n = poly.Length;
        for (int i = 0; i < n; i++)
        {
            Vector2 curr = poly[i];
            Vector2 next = poly[(i + 1) % n];
            sum += (curr.X * next.Y) - (next.X * curr.Y);
        }
        return sum;
    }

    /// <summary>
    /// 2D cross product of (b - a) x (c - a). Positive when (a -> b -> c) turns left (CCW).
    /// </summary>
    internal static float Cross2D(Vector2 a, Vector2 b, Vector2 c)
    {
        return ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));
    }

    private static bool ContainsAnyPointInTriangle(
        Vector2[] poly2D,
        List<int> remaining,
        int iPrev,
        int iCurr,
        int iNext,
        Vector2 a,
        Vector2 b,
        Vector2 c)
    {
        int count = remaining.Count;
        for (int j = 0; j < count; j++)
        {
            int testIdx = remaining[j];
            if (testIdx == iPrev || testIdx == iCurr || testIdx == iNext)
            {
                continue;
            }

            Vector2 p = poly2D[testIdx];

            // Skip vertices that share the exact same 2D coordinates as one of the triangle's corners
            if (Vector2.DistanceSquared(p, a) < 1e-10f ||
                Vector2.DistanceSquared(p, b) < 1e-10f ||
                Vector2.DistanceSquared(p, c) < 1e-10f)
            {
                continue;
            }

            if (PointInTriangle2D(p, a, b, c))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool PointInTriangle2D(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float c1 = Cross2D(a, b, p);
        float c2 = Cross2D(b, c, p);
        float c3 = Cross2D(c, a, p);

        return c1 >= PointInTriangleEpsilon &&
               c2 >= PointInTriangleEpsilon &&
               c3 >= PointInTriangleEpsilon;
    }
}
