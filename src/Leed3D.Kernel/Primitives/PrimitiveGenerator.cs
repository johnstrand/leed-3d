using System.Numerics;
using Leed3D.Kernel.Geometry;

namespace Leed3D.Kernel.Primitives;

/// <summary>
/// Generates parametric 3D primitives in Left-Handed space (+X Right, +Y Up, +Z Away).
/// All shapes are centered at (0, 0, 0) with welded vertices and consistent outward normals.
/// </summary>
public static class PrimitiveGenerator
{
    private const string DefaultMaterialId = "dev_grid_gray";

    /// <summary>
    /// Creates a flat grid plane on the XZ ground plane (Y = 0) facing +Y.
    /// </summary>
    public static EditableMesh CreatePlane(
        float width = 4.0f,
        float depth = 4.0f,
        int widthSegments = 1,
        int depthSegments = 1,
        string materialId = DefaultMaterialId)
    {
        widthSegments = System.Math.Max(1, widthSegments);
        depthSegments = System.Math.Max(1, depthSegments);

        var mesh = new EditableMesh();
        float halfW = width * 0.5f;
        float halfD = depth * 0.5f;

        int cols = widthSegments + 1;
        int rows = depthSegments + 1;
        var grid = new int[rows, cols];

        for (int r = 0; r < rows; r++)
        {
            float z = -halfD + (depth * r / depthSegments); // from -Z to +Z
            for (int c = 0; c < cols; c++)
            {
                float x = -halfW + (width * c / widthSegments); // from -X to +X
                grid[r, c] = mesh.AddVertex(new Vector3(x, 0.0f, z));
            }
        }

        // For (c, r) -> (c, r+1) -> (c+1, r+1) -> (c+1, r), normal points +Y
        for (int r = 0; r < depthSegments; r++)
        {
            for (int c = 0; c < widthSegments; c++)
            {
                int v0 = grid[r, c];
                int v1 = grid[r + 1, c];
                int v2 = grid[r + 1, c + 1];
                int v3 = grid[r, c + 1];

                mesh.AddFace([v0, v1, v2, v3], materialId);
            }
        }

        mesh.ValidateAndRepair();
        return mesh;
    }

    public static EditableMesh CreatePlane(PlaneParams parameters)
    {
        return CreatePlane(
            parameters.Width,
            parameters.Depth,
            parameters.WidthSegments,
            parameters.DepthSegments,
            parameters.MaterialId);
    }

    /// <summary>
    /// Creates a Box centered at (0, 0, 0) with welded vertices across corners and edges.
    /// Supports multi-segment subdivisions and invertNormals (instant hollow room).
    /// </summary>
    public static EditableMesh CreateBox(
        Vector3 size,
        int segmentsX = 1,
        int segmentsY = 1,
        int segmentsZ = 1,
        bool invertNormals = false,
        string materialId = DefaultMaterialId)
    {
        segmentsX = System.Math.Max(1, segmentsX);
        segmentsY = System.Math.Max(1, segmentsY);
        segmentsZ = System.Math.Max(1, segmentsZ);

        var mesh = new EditableMesh();
        Vector3 half = size * 0.5f;

        var vertexMap = new Dictionary<(long X, long Y, long Z), int>();

        int GetOrAddVertex(Vector3 pos)
        {
            long qx = (long)MathF.Round(pos.X * 10000.0f);
            long qy = (long)MathF.Round(pos.Y * 10000.0f);
            long qz = (long)MathF.Round(pos.Z * 10000.0f);
            var key = (qx, qy, qz);

            if (!vertexMap.TryGetValue(key, out int id))
            {
                id = mesh.AddVertex(pos);
                vertexMap[key] = id;
            }

            return id;
        }

        void BuildFaceGrid(
            int segU,
            int segV,
            Func<float, float, Vector3> posEvaluator)
        {
            int[,] grid = new int[segV + 1, segU + 1];
            for (int iv = 0; iv <= segV; iv++)
            {
                float vFrac = (float)iv / segV;
                for (int iu = 0; iu <= segU; iu++)
                {
                    float uFrac = (float)iu / segU;
                    grid[iv, iu] = GetOrAddVertex(posEvaluator(uFrac, vFrac));
                }
            }

            for (int iv = 0; iv < segV; iv++)
            {
                for (int iu = 0; iu < segU; iu++)
                {
                    int v0 = grid[iv, iu];
                    int v1 = grid[iv, iu + 1];
                    int v2 = grid[iv + 1, iu + 1];
                    int v3 = grid[iv + 1, iu];

                    mesh.AddFace([v0, v1, v2, v3], materialId);
                }
            }
        }

        // 1. Front (-Z): normal (0, 0, -1)
        BuildFaceGrid(segmentsX, segmentsY, (u, v) =>
        {
            float x = half.X - (size.X * u);
            float y = -half.Y + (size.Y * v);
            return new Vector3(x, y, -half.Z);
        });

        // 2. Back (+Z): normal (0, 0, +1)
        BuildFaceGrid(segmentsX, segmentsY, (u, v) =>
        {
            float x = -half.X + (size.X * u);
            float y = -half.Y + (size.Y * v);
            return new Vector3(x, y, half.Z);
        });

        // 3. Left (-X): normal (-1, 0, 0)
        BuildFaceGrid(segmentsZ, segmentsY, (u, v) =>
        {
            float z = -half.Z + (size.Z * u);
            float y = -half.Y + (size.Y * v);
            return new Vector3(-half.X, y, z);
        });

        // 4. Right (+X): normal (+1, 0, 0)
        BuildFaceGrid(segmentsZ, segmentsY, (u, v) =>
        {
            float z = half.Z - (size.Z * u);
            float y = -half.Y + (size.Y * v);
            return new Vector3(half.X, y, z);
        });

        // 5. Top (+Y): normal (0, +1, 0)
        BuildFaceGrid(segmentsZ, segmentsX, (u, v) =>
        {
            float z = -half.Z + (size.Z * u);
            float x = -half.X + (size.X * v);
            return new Vector3(x, half.Y, z);
        });

        // 6. Bottom (-Y): normal (0, -1, 0)
        BuildFaceGrid(segmentsX, segmentsZ, (u, v) =>
        {
            float x = -half.X + (size.X * u);
            float z = -half.Z + (size.Z * v);
            return new Vector3(x, -half.Y, z);
        });

        if (invertNormals)
        {
            mesh.FlipNormals();
        }

        mesh.ValidateAndRepair();
        return mesh;
    }

    public static EditableMesh CreateBox(BoxParams parameters)
    {
        return CreateBox(
            parameters.Size,
            parameters.SegmentsX,
            parameters.SegmentsY,
            parameters.SegmentsZ,
            parameters.InvertNormals,
            parameters.MaterialId);
    }

    /// <summary>
    /// Creates a Cylinder centered at (0, 0, 0) with radius and height along the Y axis.
    /// Side walls are quad rings; top and bottom caps are single N-gon faces.
    /// </summary>
    public static EditableMesh CreateCylinder(
        float radius = 1.0f,
        float height = 2.0f,
        int radialSegments = 16,
        int heightSegments = 1,
        bool capTop = true,
        bool capBottom = true,
        string materialId = DefaultMaterialId)
    {
        radialSegments = System.Math.Max(3, radialSegments);
        heightSegments = System.Math.Max(1, heightSegments);

        var mesh = new EditableMesh();
        float halfH = height * 0.5f;

        int[][] rings = new int[heightSegments + 1][];

        for (int h = 0; h <= heightSegments; h++)
        {
            float y = -halfH + (height * h / heightSegments);
            rings[h] = new int[radialSegments];

            for (int r = 0; r < radialSegments; r++)
            {
                float angle = r * MathF.Tau / radialSegments;
                float x = radius * MathF.Cos(angle);
                float z = radius * MathF.Sin(angle);
                rings[h][r] = mesh.AddVertex(new Vector3(x, y, z));
            }
        }

        // Side wall quads: lower[nextR] -> lower[r] -> upper[r] -> upper[nextR]
        // This reverses the side wall winding so outward normal still points away from the Y axis!
        for (int h = 0; h < heightSegments; h++)
        {
            int[] lower = rings[h];
            int[] upper = rings[h + 1];

            for (int r = 0; r < radialSegments; r++)
            {
                int nextR = (r + 1) % radialSegments;
                int v0 = lower[nextR];
                int v1 = lower[r];
                int v2 = upper[r];
                int v3 = upper[nextR];

                mesh.AddFace([v0, v1, v2, v3], materialId);
            }
        }

        // Top cap (+Y): single N-gon face
        // Top edge of side quad is (v2 -> v3) = (upper[r] -> upper[nextR]).
        // Top cap must have opposite edge (upper[nextR] -> upper[r]).
        // Reverse loop [radialSegments-1 down to 0] produces (upper[nextR] -> upper[r]) and normal +Y!
        if (capTop)
        {
            int[] topRing = rings[heightSegments];
            var topFaceVerts = new List<int>(radialSegments);
            for (int r = radialSegments - 1; r >= 0; r--)
            {
                topFaceVerts.Add(topRing[r]);
            }
            mesh.AddFace(topFaceVerts, materialId);
        }

        // Bottom cap (-Y): single N-gon face
        // Bottom edge of side quad is (v0 -> v1) = (lower[nextR] -> lower[r]).
        // Bottom cap must have opposite edge (lower[r] -> lower[nextR]).
        // Forward loop [0..radialSegments-1] produces (lower[r] -> lower[nextR]) and normal -Y!
        if (capBottom)
        {
            int[] bottomRing = rings[0];
            var bottomFaceVerts = new List<int>(radialSegments);
            for (int r = 0; r < radialSegments; r++)
            {
                bottomFaceVerts.Add(bottomRing[r]);
            }
            mesh.AddFace(bottomFaceVerts, materialId);
        }

        mesh.ValidateAndRepair();
        return mesh;
    }

    public static EditableMesh CreateCylinder(CylinderParams parameters)
    {
        return CreateCylinder(
            parameters.Radius,
            parameters.Height,
            parameters.RadialSegments,
            parameters.HeightSegments,
            parameters.CapTop,
            parameters.CapBottom,
            parameters.MaterialId);
    }

    /// <summary>
    /// Creates a Cone centered at (0, 0, 0) with apex at (0, +height/2, 0)
    /// and base ring at (0, -height/2, 0). Base cap is a single N-gon face.
    /// </summary>
    public static EditableMesh CreateCone(
        float radius = 1.0f,
        float height = 2.0f,
        int radialSegments = 16,
        bool capBottom = true,
        string materialId = DefaultMaterialId)
    {
        radialSegments = System.Math.Max(3, radialSegments);

        var mesh = new EditableMesh();
        float halfH = height * 0.5f;

        int apexId = mesh.AddVertex(new Vector3(0.0f, halfH, 0.0f));

        int[] baseRing = new int[radialSegments];
        for (int r = 0; r < radialSegments; r++)
        {
            float angle = r * MathF.Tau / radialSegments;
            float x = radius * MathF.Cos(angle);
            float z = radius * MathF.Sin(angle);
            baseRing[r] = mesh.AddVertex(new Vector3(x, -halfH, z));
        }

        // Side triangle faces: [apexId, baseRing[r], baseRing[nextR]]
        // Base edge is (baseRing[r] -> baseRing[nextR]).
        // Normal points outward away from apex/axis.
        for (int r = 0; r < radialSegments; r++)
        {
            int nextR = (r + 1) % radialSegments;
            mesh.AddFace([apexId, baseRing[nextR], baseRing[r]], materialId);
        }

        // Bottom cap (-Y): single N-gon face
        // Side triangle base edge is (baseRing[nextR] -> baseRing[r]).
        // Bottom cap must have opposite edge (baseRing[r] -> baseRing[nextR]).
        // Forward loop [0..radialSegments-1] produces edges (baseRing[r] -> baseRing[nextR]),
        // and its normal points -Y!
        if (capBottom)
        {
            var bottomFaceVerts = new List<int>(radialSegments);
            for (int r = 0; r < radialSegments; r++)
            {
                bottomFaceVerts.Add(baseRing[r]);
            }
            mesh.AddFace(bottomFaceVerts, materialId);
        }

        mesh.ValidateAndRepair();
        return mesh;
    }

    public static EditableMesh CreateCone(ConeParams parameters)
    {
        return CreateCone(
            parameters.Radius,
            parameters.Height,
            parameters.RadialSegments,
            parameters.CapBottom,
            parameters.MaterialId);
    }

    /// <summary>
    /// Creates a regular 20-triangle icosahedron centered at (0, 0, 0) with vertices normalized to radius.
    /// </summary>
    public static EditableMesh CreateIcosahedron(
        float radius = 1.0f,
        string materialId = DefaultMaterialId)
    {
        var mesh = new EditableMesh();

        // Golden ratio phi = (1 + sqrt(5)) / 2
        float phi = (1.0f + MathF.Sqrt(5.0f)) * 0.5f;

        Vector3[] basePositions =
        [
            new(-1.0f,  phi,  0.0f),
            new( 1.0f,  phi,  0.0f),
            new(-1.0f, -phi,  0.0f),
            new( 1.0f, -phi,  0.0f),

            new( 0.0f, -1.0f,  phi),
            new( 0.0f,  1.0f,  phi),
            new( 0.0f, -1.0f, -phi),
            new( 0.0f,  1.0f, -phi),

            new( phi,  0.0f, -1.0f),
            new( phi,  0.0f,  1.0f),
            new(-phi,  0.0f, -1.0f),
            new(-phi,  0.0f,  1.0f)
        ];

        int[] vertexIds = new int[12];
        for (int i = 0; i < 12; i++)
        {
            Vector3 pos = Vector3.Normalize(basePositions[i]) * radius;
            vertexIds[i] = mesh.AddVertex(pos);
        }

        (int, int, int)[] faces =
        [
            (0, 11, 5),
            (0, 5, 1),
            (0, 1, 7),
            (0, 7, 10),
            (0, 10, 11),

            (1, 5, 9),
            (5, 11, 4),
            (11, 10, 2),
            (10, 7, 6),
            (7, 1, 8),

            (3, 9, 4),
            (3, 4, 2),
            (3, 2, 6),
            (3, 6, 8),
            (3, 8, 9),

            (4, 9, 5),
            (2, 4, 11),
            (6, 2, 10),
            (8, 6, 7),
            (9, 8, 1)
        ];

        foreach (var (a, b, c) in faces)
        {
            int va = vertexIds[a];
            int vb = vertexIds[b];
            int vc = vertexIds[c];

            Vector3 pa = mesh.GetVertex(va)!.Position;
            Vector3 pb = mesh.GetVertex(vb)!.Position;
            Vector3 pc = mesh.GetVertex(vc)!.Position;

            Vector3 centroid = (pa + pb + pc) / 3.0f;
            Vector3 normal = Vector3.Cross(pb - pa, pc - pa);

            if (Vector3.Dot(normal, centroid) < 0.0f)
            {
                (vb, vc) = (vc, vb);
            }

            mesh.AddFace([va, vb, vc], materialId);
        }

        mesh.ValidateAndRepair();
        return mesh;
    }

    public static EditableMesh CreateIcosahedron(IcosahedronParams parameters)
    {
        return CreateIcosahedron(parameters.Radius, parameters.MaterialId);
    }

    /// <summary>
    /// Creates a Geosphere by subdividing an icosahedron (0..5 subdivisions),
    /// deduplicating midpoint vertices per undirected edge, and normalizing all vertices to radius.
    /// </summary>
    public static EditableMesh CreateGeosphere(
        float radius = 1.0f,
        int subdivisions = 2,
        string materialId = DefaultMaterialId)
    {
        subdivisions = System.Math.Clamp(subdivisions, 0, 5);

        EditableMesh current = CreateIcosahedron(radius, materialId);
        if (subdivisions == 0)
        {
            return current;
        }

        for (int sub = 0; sub < subdivisions; sub++)
        {
            var next = new EditableMesh();
            var midpointCache = new Dictionary<EdgeKey, int>();

            var oldToNewVertexMap = new Dictionary<int, int>(current.VertexCount);
            foreach (MeshVertex v in current.Vertices)
            {
                Vector3 projected = Vector3.Normalize(v.Position) * radius;
                int newId = next.AddVertex(projected);
                oldToNewVertexMap[v.Id] = newId;
            }

            int GetOrCreateMidpoint(int vA, int vB)
            {
                var key = new EdgeKey(vA, vB);
                if (midpointCache.TryGetValue(key, out int midId))
                {
                    return midId;
                }

                Vector3 posA = current.GetVertex(vA)!.Position;
                Vector3 posB = current.GetVertex(vB)!.Position;
                Vector3 midpoint = Vector3.Normalize((posA + posB) * 0.5f) * radius;

                midId = next.AddVertex(midpoint);
                midpointCache[key] = midId;
                return midId;
            }

            foreach (PolygonFace face in current.Faces)
            {
                int a = face.VertexIds[0];
                int b = face.VertexIds[1];
                int c = face.VertexIds[2];

                int mAB = GetOrCreateMidpoint(a, b);
                int mBC = GetOrCreateMidpoint(b, c);
                int mCA = GetOrCreateMidpoint(c, a);

                int na = oldToNewVertexMap[a];
                int nb = oldToNewVertexMap[b];
                int nc = oldToNewVertexMap[c];

                next.AddFace([na, mAB, mCA], materialId);
                next.AddFace([nb, mBC, mAB], materialId);
                next.AddFace([nc, mCA, mBC], materialId);
                next.AddFace([mAB, mBC, mCA], materialId);
            }

            next.ValidateAndRepair();
            current = next;
        }

        return current;
    }

    public static EditableMesh CreateGeosphere(GeosphereParams parameters)
    {
        return CreateGeosphere(
            parameters.Radius,
            parameters.Subdivisions,
            parameters.MaterialId);
    }
}
