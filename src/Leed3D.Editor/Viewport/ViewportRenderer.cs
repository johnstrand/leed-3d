using System.Numerics;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Viewport;
using Raylib_cs;

namespace Leed3D.Editor.Viewport;

/// <summary>
/// Renders the 3D scene, power-of-two grid, world origin axes, and N-gon meshes
/// into an offscreen <see cref="RenderTexture2D"/> using <see cref="CoordSpace"/>
/// Left-Handed <-> Right-Handed conversions.
/// </summary>
public sealed class ViewportRenderer : IDisposable
{
    private RenderTexture2D _renderTarget;
    private int _currentWidth;
    private int _currentHeight;

    private static readonly Color BgColor = new(30, 33, 38, 255);
    private static readonly Color MinorGridColor = new(52, 57, 65, 255);
    private static readonly Color MajorGridColor = new(78, 86, 98, 255);
    private static readonly Color AxisXColor = new(225, 70, 70, 255);   // Red (+X Right)
    private static readonly Color AxisYColor = new(85, 210, 85, 255);   // Green (+Y Up)
    private static readonly Color AxisZColor = new(75, 135, 240, 255);  // Blue (+Z Away)
    private static readonly Color WireframeEdgeColor = new(22, 24, 28, 255);
    private static readonly Color PureWireframeColor = new(200, 210, 225, 255);

    private static readonly Vector3 LightDir1 = Vector3.Normalize(new Vector3(-0.55f, 0.75f, -0.35f));
    private static readonly Vector3 LightDir2 = Vector3.Normalize(new Vector3(0.45f, 0.35f, 0.80f));

    public RenderTexture2D RenderTarget => _renderTarget;
    public Vector2 ViewportSize => new(_currentWidth, _currentHeight);

    public bool ShowGrid { get; set; } = true;
    public bool ShowOriginAxes { get; set; } = true;

    public void EnsureSize(int width, int height)
    {
        width = System.Math.Max(64, width);
        height = System.Math.Max(64, height);

        if (_renderTarget.Id != 0 && _currentWidth == width && _currentHeight == height)
        {
            return;
        }

        if (_renderTarget.Id != 0)
        {
            Raylib.UnloadRenderTexture(_renderTarget);
        }

        _renderTarget = Raylib.LoadRenderTexture(width, height);
        _currentWidth = width;
        _currentHeight = height;
    }

    public void RenderScene(
        EditorScene scene,
        EditorCamera camera,
        ViewportShadingMode shadingMode,
        float gridStep)
    {
        if (_renderTarget.Id == 0)
        {
            EnsureSize(1280, 720);
        }

        Raylib.BeginTextureMode(_renderTarget);
        Raylib.ClearBackground(BgColor);

        Camera3D rlCam = ToRaylibCamera(camera);
        Raylib.BeginMode3D(rlCam);

        if (ShowGrid)
        {
            DrawGrid(camera.ProjectionMode, gridStep);
        }

        if (ShowOriginAxes)
        {
            DrawOriginAxes();
        }

        // Render all visible scene objects
        Rlgl.EnableBackfaceCulling();

        for (int i = 0; i < scene.Objects.Count; i++)
        {
            SceneObject obj = scene.Objects[i];
            if (!obj.Visible || obj.Mesh is null)
            {
                continue;
            }

            DrawMeshObject(obj, shadingMode);
        }

        Rlgl.DrawRenderBatchActive();
        Raylib.EndMode3D();
        Raylib.EndTextureMode();
    }

    /// <summary>
    /// Converts the Left-Handed <see cref="EditorCamera"/> to a Right-Handed Raylib <see cref="Camera3D"/>.
    /// </summary>
    public static Camera3D ToRaylibCamera(EditorCamera camera)
    {
        Vector3 posRh = CoordSpace.ToRightHanded(camera.Position);
        Vector3 targetRh = CoordSpace.ToRightHanded(camera.Target);
        Vector3 upRh = CoordSpace.ToRightHanded(camera.GetUp());

        bool isPerspective = camera.ProjectionMode == ViewportProjectionMode.Perspective;
        return new Camera3D
        {
            Position = posRh,
            Target = targetRh,
            Up = upRh,
            FovY = isPerspective ? camera.FovYDegrees : camera.OrthoSize,
            Projection = isPerspective ? CameraProjection.Perspective : CameraProjection.Orthographic
        };
    }

    private static void DrawGrid(ViewportProjectionMode projectionMode, float step)
    {
        float safeStep = System.Math.Clamp(step, 0.125f, 64.0f);
        int halfLines = 64;
        float extent = halfLines * safeStep;

        Rlgl.Begin(DrawMode.Lines);

        for (int i = -halfLines; i <= halfLines; i++)
        {
            if (i == 0)
            {
                continue; // Center lines are drawn by origin axes
            }

            bool isMajor = (i % 8) == 0;
            Color col = isMajor ? MajorGridColor : MinorGridColor;
            float offset = i * safeStep;

            switch (projectionMode)
            {
                case ViewportProjectionMode.OrthoX:
                    // YZ plane (X = 0)
                    EmitLineEditorSpace(new Vector3(0.0f, offset, -extent), new Vector3(0.0f, offset, extent), col);
                    EmitLineEditorSpace(new Vector3(0.0f, -extent, offset), new Vector3(0.0f, extent, offset), col);
                    break;

                case ViewportProjectionMode.OrthoZ:
                    // XY plane (Z = 0)
                    EmitLineEditorSpace(new Vector3(offset, -extent, 0.0f), new Vector3(offset, extent, 0.0f), col);
                    EmitLineEditorSpace(new Vector3(-extent, offset, 0.0f), new Vector3(extent, offset, 0.0f), col);
                    break;

                case ViewportProjectionMode.Perspective:
                case ViewportProjectionMode.OrthoY:
                default:
                    // XZ ground plane (Y = 0)
                    EmitLineEditorSpace(new Vector3(offset, 0.0f, -extent), new Vector3(offset, 0.0f, extent), col);
                    EmitLineEditorSpace(new Vector3(-extent, 0.0f, offset), new Vector3(extent, 0.0f, offset), col);
                    break;
            }
        }

        Rlgl.End();
    }

    private static void DrawOriginAxes()
    {
        const float axisExtent = 512.0f;
        Rlgl.Begin(DrawMode.Lines);

        // +X Right (bright red), -X (dim red)
        EmitLineEditorSpace(Vector3.Zero, new Vector3(axisExtent, 0.0f, 0.0f), AxisXColor);
        EmitLineEditorSpace(Vector3.Zero, new Vector3(-axisExtent, 0.0f, 0.0f), new Color(110, 45, 45, 255));

        // +Y Up (bright green), -Y (dim green)
        EmitLineEditorSpace(Vector3.Zero, new Vector3(0.0f, axisExtent, 0.0f), AxisYColor);
        EmitLineEditorSpace(Vector3.Zero, new Vector3(0.0f, -axisExtent, 0.0f), new Color(45, 105, 45, 255));

        // +Z Away (bright blue), -Z (dim blue)
        EmitLineEditorSpace(Vector3.Zero, new Vector3(0.0f, 0.0f, axisExtent), AxisZColor);
        EmitLineEditorSpace(Vector3.Zero, new Vector3(0.0f, 0.0f, -axisExtent), new Color(45, 75, 120, 255));

        Rlgl.End();
    }

    private static void DrawMeshObject(SceneObject obj, ViewportShadingMode shadingMode)
    {
        EditableMesh mesh = obj.Mesh!;
        Matrix4x4 worldMatrix = obj.Transform.GetLocalMatrix();

        bool drawSolid = shadingMode != ViewportShadingMode.Wireframe;
        bool drawWireframe = shadingMode is ViewportShadingMode.SolidWithWireframe or ViewportShadingMode.Wireframe;

        if (drawSolid)
        {
            Rlgl.Begin(DrawMode.Triangles);

            for (int fIdx = 0; fIdx < mesh.Faces.Count; fIdx++)
            {
                PolygonFace face = mesh.Faces[fIdx];
                Vector3 localNormal = face.ComputeNormal(mesh);
                Vector3 worldNormal = Vector3.Normalize(obj.Transform.TransformDirectionToWorld(localNormal));

                Color shadedColor = ComputeDirectionalFaceShade(worldNormal, face.MaterialId);

                IReadOnlyList<(int V0, int V1, int V2)> tris = PolygonTriangulator.TriangulateFace(face, mesh);
                for (int tIdx = 0; tIdx < tris.Count; tIdx++)
                {
                    if (Rlgl.CheckRenderBatchLimit(3))
                    {
                        Rlgl.Begin(DrawMode.Triangles);
                    }

                    var (v0Id, v1Id, v2Id) = tris[tIdx];
                    // Flip triangle index winding when negating Z via CoordSpace
                    var (w0Id, w1Id, w2Id) = CoordSpace.FlipTriangleWinding(v0Id, v1Id, v2Id);

                    MeshVertex? mv0 = mesh.GetVertex(w0Id);
                    MeshVertex? mv1 = mesh.GetVertex(w1Id);
                    MeshVertex? mv2 = mesh.GetVertex(w2Id);
                    if (mv0 is null || mv1 is null || mv2 is null)
                    {
                        continue;
                    }

                    Vector3 p0Rh = CoordSpace.ToRightHanded(Vector3.Transform(mv0.Position, worldMatrix));
                    Vector3 p1Rh = CoordSpace.ToRightHanded(Vector3.Transform(mv1.Position, worldMatrix));
                    Vector3 p2Rh = CoordSpace.ToRightHanded(Vector3.Transform(mv2.Position, worldMatrix));

                    Rlgl.Color4ub(shadedColor.R, shadedColor.G, shadedColor.B, shadedColor.A);
                    Rlgl.Vertex3f(p0Rh.X, p0Rh.Y, p0Rh.Z);
                    Rlgl.Vertex3f(p1Rh.X, p1Rh.Y, p1Rh.Z);
                    Rlgl.Vertex3f(p2Rh.X, p2Rh.Y, p2Rh.Z);
                }
            }

            Rlgl.End();
        }

        // Draw true N-gon topological edges (not internal triangulation diagonals!)
        if (drawWireframe)
        {
            Color edgeColor = shadingMode == ViewportShadingMode.Wireframe ? PureWireframeColor : WireframeEdgeColor;
            TopologyGraph topology = mesh.GetTopology();

            Rlgl.Begin(DrawMode.Lines);
            for (int eIdx = 0; eIdx < topology.Edges.Count; eIdx++)
            {
                if (Rlgl.CheckRenderBatchLimit(2))
                {
                    Rlgl.Begin(DrawMode.Lines);
                }

                EdgeKey edgeKey = topology.Edges[eIdx].Key;
                MeshVertex? vA = mesh.GetVertex(edgeKey.VMin);
                MeshVertex? vB = mesh.GetVertex(edgeKey.VMax);
                if (vA is null || vB is null)
                {
                    continue;
                }

                Vector3 pA = Vector3.Transform(vA.Position, worldMatrix);
                Vector3 pB = Vector3.Transform(vB.Position, worldMatrix);

                // Slightly bias wireframe edges toward camera or outward normal to avoid Z-fighting
                EmitLineEditorSpace(pA, pB, edgeColor);
            }
            Rlgl.End();
        }
    }

    private static Color ComputeDirectionalFaceShade(Vector3 worldNormal, string materialId)
    {
        // Base color derived from materialId
        Vector3 baseColor = materialId switch
        {
            "dev_grid_orange" => new Vector3(0.90f, 0.52f, 0.22f),
            "dev_grid_blue" => new Vector3(0.32f, 0.58f, 0.88f),
            "dev_grid_green" => new Vector3(0.35f, 0.75f, 0.42f),
            _ => new Vector3(0.68f, 0.71f, 0.75f)
        };

        float ndotl1 = System.Math.Max(0.0f, Vector3.Dot(worldNormal, LightDir1));
        float ndotl2 = System.Math.Max(0.0f, Vector3.Dot(worldNormal, LightDir2));
        float intensity = System.Math.Clamp(0.38f + (ndotl1 * 0.48f) + (ndotl2 * 0.22f), 0.25f, 1.0f);

        Vector3 lit = baseColor * intensity;
        return new Color(
            (byte)System.Math.Clamp((int)(lit.X * 255.0f), 0, 255),
            (byte)System.Math.Clamp((int)(lit.Y * 255.0f), 0, 255),
            (byte)System.Math.Clamp((int)(lit.Z * 255.0f), 0, 255),
            (byte)255);
    }

    private static void EmitLineEditorSpace(Vector3 startEditor, Vector3 endEditor, Color color)
    {
        if (Rlgl.CheckRenderBatchLimit(2))
        {
            Rlgl.Begin(DrawMode.Lines);
        }

        Vector3 startRh = CoordSpace.ToRightHanded(startEditor);
        Vector3 endRh = CoordSpace.ToRightHanded(endEditor);

        Rlgl.Color4ub(color.R, color.G, color.B, color.A);
        Rlgl.Vertex3f(startRh.X, startRh.Y, startRh.Z);
        Rlgl.Vertex3f(endRh.X, endRh.Y, endRh.Z);
    }

    public void Dispose()
    {
        if (_renderTarget.Id != 0)
        {
            Raylib.UnloadRenderTexture(_renderTarget);
            _renderTarget = default;
        }
    }
}
