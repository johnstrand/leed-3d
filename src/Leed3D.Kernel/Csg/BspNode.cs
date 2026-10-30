using System.Numerics;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Texturing;

namespace Leed3D.Kernel.Csg;

/// <summary>
/// Spatial classification of a point or polygon relative to a 3D splitting plane.
/// </summary>
public enum PolygonPlaneRelation
{
    Coplanar = 0,
    Front = 1,
    Back = 2,
    Spanning = Front | Back,
    CoplanarFront = 4,
    CoplanarBack = 5
}

/// <summary>
/// Represents a planar 3D polygon used during BSP-tree CSG boolean evaluation,
/// preserving vertex loop winding, plane equation, and face material assignment.
/// </summary>
public sealed class CsgPolygon
{
    public List<Vector3> Vertices { get; }
    public Plane3D Plane { get; private set; }
    public string MaterialId { get; set; }
    public FaceUvMapping? UvMapping { get; set; }

    public CsgPolygon(IEnumerable<Vector3> vertices, string materialId = PolygonFace.DefaultMaterialId, FaceUvMapping? uvMapping = null)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        Vertices = new List<Vector3>(vertices);
        MaterialId = string.IsNullOrWhiteSpace(materialId) ? PolygonFace.DefaultMaterialId : materialId;
        Plane = ComputePlane(Vertices);
        UvMapping = uvMapping?.Clone();
    }

    public CsgPolygon(IEnumerable<Vector3> vertices, string materialId, Plane3D explicitPlane, FaceUvMapping? uvMapping = null)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        Vertices = new List<Vector3>(vertices);
        MaterialId = string.IsNullOrWhiteSpace(materialId) ? PolygonFace.DefaultMaterialId : materialId;
        Plane = explicitPlane;
        UvMapping = uvMapping?.Clone();
    }

    /// <summary>
    /// Reverses the polygon's vertex winding order and inverts its plane normal.
    /// </summary>
    public void Flip()
    {
        Vertices.Reverse();
        Plane = new Plane3D(-Plane.Normal, -Plane.D);
    }

    /// <summary>
    /// Creates a deep copy of this polygon.
    /// </summary>
    public CsgPolygon Clone()
    {
        return new CsgPolygon(Vertices, MaterialId, Plane, UvMapping);
    }

    /// <summary>
    /// Computes the unnormalized Newell normal vector of a 3D vertex loop.
    /// Its magnitude equals twice the 3D surface area of the polygon.
    /// </summary>
    public static Vector3 ComputeNewellVector(IReadOnlyList<Vector3> vertices)
    {
        int count = vertices.Count;
        if (count < 3)
        {
            return Vector3.Zero;
        }

        Vector3 normal = Vector3.Zero;
        for (int i = 0; i < count; i++)
        {
            Vector3 curr = vertices[i];
            Vector3 next = vertices[(i + 1) % count];

            normal.X += (curr.Y - next.Y) * (curr.Z + next.Z);
            normal.Y += (curr.Z - next.Z) * (curr.X + next.X);
            normal.Z += (curr.X - next.X) * (curr.Y + next.Y);
        }

        return normal;
    }

    /// <summary>
    /// Computes the unit outward normal of a 3D vertex loop using Newell's method.
    /// </summary>
    public static Vector3 ComputeNewellNormal(IReadOnlyList<Vector3> vertices)
    {
        Vector3 normal = ComputeNewellVector(vertices);
        float lenSq = normal.LengthSquared();
        if (lenSq > 1e-12f)
        {
            return normal / MathF.Sqrt(lenSq);
        }

        return Vector3.UnitY;
    }

    private static Plane3D ComputePlane(IReadOnlyList<Vector3> vertices)
    {
        if (vertices.Count == 0)
        {
            return new Plane3D(Vector3.UnitY, 0.0f);
        }

        Vector3 normal = ComputeNewellNormal(vertices);
        Vector3 centroid = Vector3.Zero;
        for (int i = 0; i < vertices.Count; i++)
        {
            centroid += vertices[i];
        }
        centroid /= vertices.Count;

        return Plane3D.CreateFromPointAndNormal(centroid, normal);
    }
}

/// <summary>
/// 3D Binary Space Partitioning (BSP) tree node for polygon clipping and solid boolean operations.
/// </summary>
public sealed class BspNode
{
    public const float Epsilon = 1e-4f;

    public Plane3D? Plane { get; private set; }
    public List<CsgPolygon> Polygons { get; private set; } = [];
    public BspNode? Front { get; private set; }
    public BspNode? Back { get; private set; }

    public BspNode()
    {
    }

    public BspNode(IReadOnlyList<CsgPolygon> polygons)
    {
        ArgumentNullException.ThrowIfNull(polygons);
        if (polygons.Count > 0)
        {
            Build(polygons);
        }
    }

    /// <summary>
    /// Creates a deep copy of this BSP tree.
    /// </summary>
    public BspNode Clone()
    {
        var node = new BspNode
        {
            Plane = Plane,
            Polygons = new List<CsgPolygon>(Polygons.Count),
            Front = Front?.Clone(),
            Back = Back?.Clone()
        };

        for (int i = 0; i < Polygons.Count; i++)
        {
            node.Polygons.Add(Polygons[i].Clone());
        }

        return node;
    }

    /// <summary>
    /// Recursively partitions and inserts the given polygons into this BSP tree.
    /// </summary>
    public void Build(IReadOnlyList<CsgPolygon> polygons)
    {
        ArgumentNullException.ThrowIfNull(polygons);
        if (polygons.Count == 0)
        {
            return;
        }

        Plane ??= polygons[0].Plane;

        var front = new List<CsgPolygon>();
        var back = new List<CsgPolygon>();

        for (int i = 0; i < polygons.Count; i++)
        {
            SplitPolygon(Plane.Value, polygons[i], Polygons, Polygons, front, back);
        }

        if (front.Count > 0)
        {
            Front ??= new BspNode();
            Front.Build(front);
        }

        if (back.Count > 0)
        {
            Back ??= new BspNode();
            Back.Build(back);
        }
    }

    /// <summary>
    /// Appends polygons that have already been clipped to this tree without re-splitting them against internal planes.
    /// </summary>
    public void AddClippedPolygons(IReadOnlyList<CsgPolygon> polygons)
    {
        ArgumentNullException.ThrowIfNull(polygons);
        for (int i = 0; i < polygons.Count; i++)
        {
            Polygons.Add(polygons[i]);
        }
    }

    /// <summary>
    /// Converts solid space to empty space and empty space to solid space by flipping all polygon windings/planes
    /// and swapping <see cref="Front"/> and <see cref="Back"/> subtrees.
    /// </summary>
    public void Invert()
    {
        for (int i = 0; i < Polygons.Count; i++)
        {
            Polygons[i].Flip();
        }

        if (Plane.HasValue)
        {
            Plane = new Plane3D(-Plane.Value.Normal, -Plane.Value.D);
        }

        Front?.Invert();
        Back?.Invert();

        (Front, Back) = (Back, Front);
    }

    /// <summary>
    /// Recursively clips a list of polygons against this BSP tree, discarding fragments that fall inside solid (<see cref="Back"/> leaf) space.
    /// If a polygon spans an internal splitting plane but both resulting halves survive completely intact in <see cref="Front"/> leaves,
    /// the original unsplit polygon is preserved to avoid unnecessary fragmentation.
    /// </summary>
    public List<CsgPolygon> ClipPolygons(IReadOnlyList<CsgPolygon> polygons)
    {
        ArgumentNullException.ThrowIfNull(polygons);
        if (Plane is null)
        {
            return new List<CsgPolygon>(polygons);
        }

        var result = new List<CsgPolygon>(polygons.Count);
        for (int i = 0; i < polygons.Count; i++)
        {
            List<CsgPolygon> clipped = ClipSinglePolygon(polygons[i], out _);
            result.AddRange(clipped);
        }

        return result;
    }

    private List<CsgPolygon> ClipSinglePolygon(CsgPolygon polygon, out bool intact)
    {
        if (Plane is null)
        {
            intact = true;
            return [polygon];
        }

        var front = new List<CsgPolygon>(1);
        var back = new List<CsgPolygon>(1);

        SplitPolygon(Plane.Value, polygon, front, back, front, back);

        // Polygon lies entirely on the front side (or coplanar front)
        if (front.Count == 1 && back.Count == 0)
        {
            if (Front is not null)
            {
                return Front.ClipSinglePolygon(front[0], out intact);
            }

            intact = true;
            return front;
        }

        // Polygon lies entirely on the back side (or coplanar back)
        if (back.Count == 1 && front.Count == 0)
        {
            if (Back is not null)
            {
                return Back.ClipSinglePolygon(back[0], out intact);
            }

            intact = false;
            return [];
        }

        // Polygon spanned the splitting plane
        List<CsgPolygon> clippedFront;
        bool frontIntact;
        if (front.Count > 0)
        {
            if (Front is not null)
            {
                clippedFront = Front.ClipSinglePolygon(front[0], out frontIntact);
            }
            else
            {
                clippedFront = front;
                frontIntact = true;
            }
        }
        else
        {
            clippedFront = [];
            frontIntact = false;
        }

        List<CsgPolygon> clippedBack;
        bool backIntact;
        if (back.Count > 0)
        {
            if (Back is not null)
            {
                clippedBack = Back.ClipSinglePolygon(back[0], out backIntact);
            }
            else
            {
                clippedBack = [];
                backIntact = false;
            }
        }
        else
        {
            clippedBack = [];
            backIntact = false;
        }

        // If both halves survived 100% intact in empty space, keep the original unsplit polygon
        if (frontIntact && backIntact && front.Count == 1 && back.Count == 1)
        {
            intact = true;
            return [polygon];
        }

        intact = false;
        clippedFront.AddRange(clippedBack);
        return clippedFront;
    }

    /// <summary>
    /// Clips all polygons stored in this BSP tree against <paramref name="otherTree"/>.
    /// </summary>
    public void ClipTo(BspNode otherTree)
    {
        ArgumentNullException.ThrowIfNull(otherTree);
        Polygons = otherTree.ClipPolygons(Polygons);
        Front?.ClipTo(otherTree);
        Back?.ClipTo(otherTree);
    }

    /// <summary>
    /// Flattens this BSP tree into a single list of all stored <see cref="CsgPolygon"/>s.
    /// </summary>
    public List<CsgPolygon> AllPolygons()
    {
        var list = new List<CsgPolygon>(Polygons);
        if (Front is not null)
        {
            list.AddRange(Front.AllPolygons());
        }

        if (Back is not null)
        {
            list.AddRange(Back.AllPolygons());
        }

        return list;
    }

    /// <summary>
    /// Classifies a single 3D point against a plane using <paramref name="epsilon"/> thickness.
    /// </summary>
    public static PolygonPlaneRelation ClassifyPoint(Plane3D plane, Vector3 point, float epsilon = Epsilon)
    {
        float dist = plane.SignedDistance(point);
        if (dist > epsilon)
        {
            return PolygonPlaneRelation.Front;
        }

        if (dist < -epsilon)
        {
            return PolygonPlaneRelation.Back;
        }

        return PolygonPlaneRelation.Coplanar;
    }

    /// <summary>
    /// Classifies a <see cref="CsgPolygon"/> relative to a splitting <see cref="Plane3D"/>.
    /// </summary>
    public static PolygonPlaneRelation ClassifyPolygon(Plane3D plane, CsgPolygon polygon, float epsilon = Epsilon)
    {
        ArgumentNullException.ThrowIfNull(polygon);

        int mask = 0;
        for (int i = 0; i < polygon.Vertices.Count; i++)
        {
            PolygonPlaneRelation ptRel = ClassifyPoint(plane, polygon.Vertices[i], epsilon);
            mask |= (int)ptRel;
        }

        if (mask == 0)
        {
            return Vector3.Dot(plane.Normal, polygon.Plane.Normal) > 0.0f
                ? PolygonPlaneRelation.CoplanarFront
                : PolygonPlaneRelation.CoplanarBack;
        }

        return (PolygonPlaneRelation)mask;
    }

    /// <summary>
    /// Splits <paramref name="polygon"/> by <paramref name="plane"/> into the appropriate output lists.
    /// When the polygon spans the plane, interpolates exact edge-plane intersections and produces two child polygons
    /// inheriting the parent's <see cref="CsgPolygon.MaterialId"/> and plane orientation.
    /// </summary>
    public static void SplitPolygon(
        Plane3D plane,
        CsgPolygon polygon,
        List<CsgPolygon> coplanarFront,
        List<CsgPolygon> coplanarBack,
        List<CsgPolygon> front,
        List<CsgPolygon> back,
        float epsilon = Epsilon)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        ArgumentNullException.ThrowIfNull(coplanarFront);
        ArgumentNullException.ThrowIfNull(coplanarBack);
        ArgumentNullException.ThrowIfNull(front);
        ArgumentNullException.ThrowIfNull(back);

        int count = polygon.Vertices.Count;
        if (count < 3)
        {
            return;
        }

        const int Coplanar = 0;
        const int FrontSide = 1;
        const int BackSide = 2;
        const int Spanning = FrontSide | BackSide;

        int polygonType = Coplanar;
        Span<int> types = count <= 64 ? stackalloc int[count] : new int[count];

        for (int i = 0; i < count; i++)
        {
            float dist = plane.SignedDistance(polygon.Vertices[i]);
            int type = dist < -epsilon ? BackSide : (dist > epsilon ? FrontSide : Coplanar);
            polygonType |= type;
            types[i] = type;
        }

        switch (polygonType)
        {
            case Coplanar:
                if (Vector3.Dot(plane.Normal, polygon.Plane.Normal) > 0.0f)
                {
                    coplanarFront.Add(polygon);
                }
                else
                {
                    coplanarBack.Add(polygon);
                }
                break;

            case FrontSide:
                front.Add(polygon);
                break;

            case BackSide:
                back.Add(polygon);
                break;

            case Spanning:
            {
                var f = new List<Vector3>(count + 2);
                var b = new List<Vector3>(count + 2);

                for (int i = 0; i < count; i++)
                {
                    int j = (i + 1) % count;
                    int ti = types[i];
                    int tj = types[j];
                    Vector3 vi = polygon.Vertices[i];
                    Vector3 vj = polygon.Vertices[j];

                    if (ti != BackSide)
                    {
                        f.Add(vi);
                    }

                    if (ti != FrontSide)
                    {
                        b.Add(vi);
                    }

                    if ((ti | tj) == Spanning)
                    {
                        Vector3 edge = vj - vi;
                        float denom = Vector3.Dot(plane.Normal, edge);
                        float t = MathF.Abs(denom) > 1e-12f
                            ? (-plane.D - Vector3.Dot(plane.Normal, vi)) / denom
                            : 0.5f;
                        t = System.Math.Clamp(t, 0.0f, 1.0f);
                        Vector3 intersection = vi + (edge * t);
                        f.Add(intersection);
                        b.Add(intersection);
                    }
                }

                CleanVertexLoop(f, epsilon * 0.1f);
                CleanVertexLoop(b, epsilon * 0.1f);

                if (f.Count >= 3 && CsgPolygon.ComputeNewellVector(f).LengthSquared() > 1e-12f)
                {
                    front.Add(new CsgPolygon(f, polygon.MaterialId, polygon.Plane, polygon.UvMapping));
                }

                if (b.Count >= 3 && CsgPolygon.ComputeNewellVector(b).LengthSquared() > 1e-12f)
                {
                    back.Add(new CsgPolygon(b, polygon.MaterialId, polygon.Plane, polygon.UvMapping));
                }
                break;
            }
        }
    }

    private static void CleanVertexLoop(List<Vector3> vertices, float minDistance)
    {
        float minDistSq = minDistance * minDistance;
        for (int i = vertices.Count - 1; i > 0; i--)
        {
            if (Vector3.DistanceSquared(vertices[i], vertices[i - 1]) <= minDistSq)
            {
                vertices.RemoveAt(i);
            }
        }

        while (vertices.Count > 1 && Vector3.DistanceSquared(vertices[^1], vertices[0]) <= minDistSq)
        {
            vertices.RemoveAt(vertices.Count - 1);
        }
    }
}
