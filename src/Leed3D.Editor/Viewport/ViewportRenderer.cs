using System.Numerics;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Gizmos;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Selection;
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
        float gridStep,
        SelectionManager? selection = null,
        TransformGizmo3D? gizmo = null,
        ModalTransformController? modalController = null)
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

            DrawMeshObject(obj, shadingMode, selection);
        }

        // 1. Render active modal operator infinite guide lines
        if (modalController is not null && modalController.IsActive)
        {
            const float guideExtent = 512.0f;
            Vector3 guideOrigin = modalController.PivotWorld;

            Rlgl.DrawRenderBatchActive();
            Rlgl.DisableDepthTest();

            Rlgl.Begin(DrawMode.Lines);
            if (modalController.Constraint is GizmoAxisConstraint.X or GizmoAxisConstraint.XY or GizmoAxisConstraint.XZ)
            {
                EmitLineEditorSpace(guideOrigin - (Vector3.UnitX * guideExtent), guideOrigin + (Vector3.UnitX * guideExtent), AxisXColor);
            }
            if (modalController.Constraint is GizmoAxisConstraint.Y or GizmoAxisConstraint.XY or GizmoAxisConstraint.YZ)
            {
                EmitLineEditorSpace(guideOrigin - (Vector3.UnitY * guideExtent), guideOrigin + (Vector3.UnitY * guideExtent), AxisYColor);
            }
            if (modalController.Constraint is GizmoAxisConstraint.Z or GizmoAxisConstraint.XZ or GizmoAxisConstraint.YZ)
            {
                EmitLineEditorSpace(guideOrigin - (Vector3.UnitZ * guideExtent), guideOrigin + (Vector3.UnitZ * guideExtent), AxisZColor);
            }
            Rlgl.End();

            Rlgl.DrawRenderBatchActive();
            Rlgl.EnableDepthTest();
        }
        // 2. Render 3D Transform Gizmo on top of scene geometry when selection has a valid pivot
        else if (selection is not null && gizmo is not null)
        {
            Vector3? pivotOpt = gizmo.IsDragging
                ? (gizmo.ActiveDragMode == GizmoToolMode.Translate ? gizmo.DragPivotWorld + gizmo.CurrentTranslationDelta : gizmo.DragPivotWorld)
                : selection.GetSelectionPivotWorld(scene);

            if (pivotOpt.HasValue)
            {
                Vector3 pivot = pivotOpt.Value;
                float gizmoScale = TransformGizmo3D.ComputeGizmoScale(camera, pivot);

                Rlgl.DrawRenderBatchActive();
                Rlgl.DisableDepthTest();
                Rlgl.DisableBackfaceCulling();

                DrawTransformGizmo(gizmo, pivot, gizmoScale);

                Rlgl.DrawRenderBatchActive();
                Rlgl.EnableDepthTest();
            }
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

    private static void DrawMeshObject(SceneObject obj, ViewportShadingMode shadingMode, SelectionManager? selection)
    {
        EditableMesh mesh = obj.Mesh!;
        Matrix4x4 worldMatrix = obj.Transform.GetLocalMatrix();

        bool isObjectSelected = selection is not null &&
            selection.Mode == InteractionMode.Object &&
            selection.SelectedObjectIds.Contains(obj.Id);

        bool isEditModeActive = selection is not null &&
            selection.Mode != InteractionMode.Object &&
            selection.ActiveObjectId == obj.Id;

        bool drawSolid = shadingMode != ViewportShadingMode.Wireframe;
        // In Edit Mode, the active object always draws its wireframe topology
        bool drawWireframe = isEditModeActive ||
            shadingMode is ViewportShadingMode.SolidWithWireframe or ViewportShadingMode.Wireframe;

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

        // Selected faces overlay in Face Mode (semi-transparent orange fill)
        HashSet<EdgeKey>? selectedFaceBoundaryEdges = null;
        if (isEditModeActive && selection!.Mode == InteractionMode.Face && selection.SelectedFaceIds.Count > 0)
        {
            selectedFaceBoundaryEdges = [];
            Color selectedFaceFill = new(255, 175, 45, 120);

            Rlgl.Begin(DrawMode.Triangles);
            foreach (int fId in selection.SelectedFaceIds)
            {
                PolygonFace? face = mesh.GetFace(fId);
                if (face is null)
                {
                    continue;
                }

                int count = face.VertexIds.Count;
                for (int i = 0; i < count; i++)
                {
                    selectedFaceBoundaryEdges.Add(new EdgeKey(face.VertexIds[i], face.VertexIds[(i + 1) % count]));
                }

                IReadOnlyList<(int V0, int V1, int V2)> tris = PolygonTriangulator.TriangulateFace(face, mesh);
                for (int tIdx = 0; tIdx < tris.Count; tIdx++)
                {
                    if (Rlgl.CheckRenderBatchLimit(3))
                    {
                        Rlgl.Begin(DrawMode.Triangles);
                    }

                    var (v0Id, v1Id, v2Id) = tris[tIdx];
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

                    Rlgl.Color4ub(selectedFaceFill.R, selectedFaceFill.G, selectedFaceFill.B, selectedFaceFill.A);
                    Rlgl.Vertex3f(p0Rh.X, p0Rh.Y, p0Rh.Z);
                    Rlgl.Vertex3f(p1Rh.X, p1Rh.Y, p1Rh.Z);
                    Rlgl.Vertex3f(p2Rh.X, p2Rh.Y, p2Rh.Z);
                }
            }
            Rlgl.End();
        }

        // Draw true N-gon topological edges (not internal triangulation diagonals!)
        if (drawWireframe || isObjectSelected)
        {
            Color defaultEdgeColor = shadingMode == ViewportShadingMode.Wireframe ? PureWireframeColor : WireframeEdgeColor;
            Color objectHighlightColor = new(255, 140, 30, 255);
            Color selectedSubEdgeColor = new(255, 150, 30, 255);

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

                Color edgeColor = defaultEdgeColor;

                if (isObjectSelected)
                {
                    edgeColor = objectHighlightColor;
                }
                else if (isEditModeActive)
                {
                    if (selection!.Mode == InteractionMode.Edge && selection.SelectedEdges.Contains(edgeKey))
                    {
                        edgeColor = selectedSubEdgeColor;
                    }
                    else if (selection!.Mode == InteractionMode.Face && selectedFaceBoundaryEdges is not null && selectedFaceBoundaryEdges.Contains(edgeKey))
                    {
                        edgeColor = selectedSubEdgeColor;
                    }
                }

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

    private static void DrawTransformGizmo(TransformGizmo3D gizmo, Vector3 pivot, float scale)
    {
        Color hoverYellow = new(255, 230, 50, 255);
        GizmoAxisConstraint highlighted = gizmo.IsDragging ? gizmo.ActiveConstraint : gizmo.HoveredConstraint;

        // 1. Active infinite axis guide lines during drag
        if (gizmo.IsDragging)
        {
            const float guideExtent = 512.0f;
            Vector3 guideOrigin = gizmo.DragPivotWorld;

            Rlgl.Begin(DrawMode.Lines);
            if (gizmo.ActiveConstraint is GizmoAxisConstraint.X or GizmoAxisConstraint.XY or GizmoAxisConstraint.XZ)
            {
                EmitLineEditorSpace(guideOrigin - (Vector3.UnitX * guideExtent), guideOrigin + (Vector3.UnitX * guideExtent), AxisXColor);
            }
            if (gizmo.ActiveConstraint is GizmoAxisConstraint.Y or GizmoAxisConstraint.XY or GizmoAxisConstraint.YZ)
            {
                EmitLineEditorSpace(guideOrigin - (Vector3.UnitY * guideExtent), guideOrigin + (Vector3.UnitY * guideExtent), AxisYColor);
            }
            if (gizmo.ActiveConstraint is GizmoAxisConstraint.Z or GizmoAxisConstraint.XZ or GizmoAxisConstraint.YZ)
            {
                EmitLineEditorSpace(guideOrigin - (Vector3.UnitZ * guideExtent), guideOrigin + (Vector3.UnitZ * guideExtent), AxisZColor);
            }
            Rlgl.End();
        }

        Color colX = highlighted is GizmoAxisConstraint.X or GizmoAxisConstraint.XY or GizmoAxisConstraint.XZ ? hoverYellow : AxisXColor;
        Color colY = highlighted is GizmoAxisConstraint.Y or GizmoAxisConstraint.XY or GizmoAxisConstraint.YZ ? hoverYellow : AxisYColor;
        Color colZ = highlighted is GizmoAxisConstraint.Z or GizmoAxisConstraint.XZ or GizmoAxisConstraint.YZ ? hoverYellow : AxisZColor;
        Color colUniform = highlighted == GizmoAxisConstraint.Uniform ? hoverYellow : new Color(235, 235, 240, 255);

        GizmoToolMode mode = gizmo.ToolMode;

        // 2. Dual-Axis Plane Quads (XY, XZ, YZ)
        if (mode is GizmoToolMode.Translate or GizmoToolMode.Scale or GizmoToolMode.Universal)
        {
            float qMin = scale * TransformGizmo3D.PlaneQuadOffsetFactor;
            float qMax = scale * (TransformGizmo3D.PlaneQuadOffsetFactor + TransformGizmo3D.PlaneQuadSizeFactor);

            // XY quad (locks Z -> Blue)
            Color xyCol = highlighted == GizmoAxisConstraint.XY ? hoverYellow : AxisZColor;
            DrawPlaneQuadHandle(
                pivot + new Vector3(qMin, qMin, 0.0f),
                pivot + new Vector3(qMax, qMin, 0.0f),
                pivot + new Vector3(qMax, qMax, 0.0f),
                pivot + new Vector3(qMin, qMax, 0.0f),
                xyCol);

            // XZ quad (locks Y -> Green)
            Color xzCol = highlighted == GizmoAxisConstraint.XZ ? hoverYellow : AxisYColor;
            DrawPlaneQuadHandle(
                pivot + new Vector3(qMin, 0.0f, qMin),
                pivot + new Vector3(qMax, 0.0f, qMin),
                pivot + new Vector3(qMax, 0.0f, qMax),
                pivot + new Vector3(qMin, 0.0f, qMax),
                xzCol);

            // YZ quad (locks X -> Red)
            Color yzCol = highlighted == GizmoAxisConstraint.YZ ? hoverYellow : AxisXColor;
            DrawPlaneQuadHandle(
                pivot + new Vector3(0.0f, qMin, qMin),
                pivot + new Vector3(0.0f, qMax, qMin),
                pivot + new Vector3(0.0f, qMax, qMax),
                pivot + new Vector3(0.0f, qMin, qMax),
                yzCol);
        }

        // 3. Translate Arrows & Shafts
        if (mode is GizmoToolMode.Translate or GizmoToolMode.Universal)
        {
            float shaftStart = scale * 0.08f;
            float shaftEnd = scale * 0.82f;
            float tipEnd = scale * TransformGizmo3D.AxisEndFactor;
            float tipRadius = scale * 0.055f;

            Rlgl.Begin(DrawMode.Lines);
            EmitLineEditorSpace(pivot + (Vector3.UnitX * shaftStart), pivot + (Vector3.UnitX * shaftEnd), colX);
            EmitLineEditorSpace(pivot + (Vector3.UnitY * shaftStart), pivot + (Vector3.UnitY * shaftEnd), colY);
            EmitLineEditorSpace(pivot + (Vector3.UnitZ * shaftStart), pivot + (Vector3.UnitZ * shaftEnd), colZ);
            Rlgl.End();

            DrawArrowTip3D(pivot + (Vector3.UnitX * shaftEnd), pivot + (Vector3.UnitX * tipEnd), Vector3.UnitY, Vector3.UnitZ, tipRadius, colX);
            DrawArrowTip3D(pivot + (Vector3.UnitY * shaftEnd), pivot + (Vector3.UnitY * tipEnd), Vector3.UnitZ, Vector3.UnitX, tipRadius, colY);
            DrawArrowTip3D(pivot + (Vector3.UnitZ * shaftEnd), pivot + (Vector3.UnitZ * tipEnd), Vector3.UnitX, Vector3.UnitY, tipRadius, colZ);
        }

        // 4. Scale Stems & Cubes
        if (mode == GizmoToolMode.Scale)
        {
            float shaftStart = scale * 0.08f;
            float shaftEnd = scale * 0.90f;
            float cubeHalf = scale * 0.055f;

            Rlgl.Begin(DrawMode.Lines);
            EmitLineEditorSpace(pivot + (Vector3.UnitX * shaftStart), pivot + (Vector3.UnitX * shaftEnd), colX);
            EmitLineEditorSpace(pivot + (Vector3.UnitY * shaftStart), pivot + (Vector3.UnitY * shaftEnd), colY);
            EmitLineEditorSpace(pivot + (Vector3.UnitZ * shaftStart), pivot + (Vector3.UnitZ * shaftEnd), colZ);
            Rlgl.End();

            DrawCubeHandle3D(pivot + (Vector3.UnitX * (shaftEnd + cubeHalf)), cubeHalf, colX);
            DrawCubeHandle3D(pivot + (Vector3.UnitY * (shaftEnd + cubeHalf)), cubeHalf, colY);
            DrawCubeHandle3D(pivot + (Vector3.UnitZ * (shaftEnd + cubeHalf)), cubeHalf, colZ);
        }

        // 5. Rotation Rings
        if (mode is GizmoToolMode.Rotate or GizmoToolMode.Universal)
        {
            float ringRadius = scale * TransformGizmo3D.RotateRingRadiusFactor;
            Color ringX = highlighted == GizmoAxisConstraint.X ? hoverYellow : AxisXColor;
            Color ringY = highlighted == GizmoAxisConstraint.Y ? hoverYellow : AxisYColor;
            Color ringZ = highlighted == GizmoAxisConstraint.Z ? hoverYellow : AxisZColor;

            DrawRotationRing3D(pivot, Vector3.UnitY, Vector3.UnitZ, ringRadius, ringX);
            DrawRotationRing3D(pivot, Vector3.UnitZ, Vector3.UnitX, ringRadius, ringY);
            DrawRotationRing3D(pivot, Vector3.UnitX, Vector3.UnitY, ringRadius, ringZ);
        }

        // 6. Center Uniform Box
        DrawCubeHandle3D(pivot, scale * 0.06f, colUniform);
    }

    private static void DrawPlaneQuadHandle(Vector3 c0, Vector3 c1, Vector3 c2, Vector3 c3, Color borderCol)
    {
        Color fillCol = new(borderCol.R, borderCol.G, borderCol.B, (byte)95);

        Vector3 r0 = CoordSpace.ToRightHanded(c0);
        Vector3 r1 = CoordSpace.ToRightHanded(c1);
        Vector3 r2 = CoordSpace.ToRightHanded(c2);
        Vector3 r3 = CoordSpace.ToRightHanded(c3);

        Rlgl.Begin(DrawMode.Triangles);
        Rlgl.Color4ub(fillCol.R, fillCol.G, fillCol.B, fillCol.A);
        Rlgl.Vertex3f(r0.X, r0.Y, r0.Z);
        Rlgl.Vertex3f(r1.X, r1.Y, r1.Z);
        Rlgl.Vertex3f(r2.X, r2.Y, r2.Z);

        Rlgl.Vertex3f(r0.X, r0.Y, r0.Z);
        Rlgl.Vertex3f(r2.X, r2.Y, r2.Z);
        Rlgl.Vertex3f(r3.X, r3.Y, r3.Z);
        Rlgl.End();

        Rlgl.Begin(DrawMode.Lines);
        EmitLineEditorSpace(c0, c1, borderCol);
        EmitLineEditorSpace(c1, c2, borderCol);
        EmitLineEditorSpace(c2, c3, borderCol);
        EmitLineEditorSpace(c3, c0, borderCol);
        Rlgl.End();
    }

    private static void DrawArrowTip3D(
        Vector3 baseCenter,
        Vector3 tipPoint,
        Vector3 tangentA,
        Vector3 tangentB,
        float radius,
        Color color)
    {
        const int segments = 8;
        Vector3 tipRh = CoordSpace.ToRightHanded(tipPoint);
        Vector3 baseRh = CoordSpace.ToRightHanded(baseCenter);

        Rlgl.Begin(DrawMode.Triangles);
        Rlgl.Color4ub(color.R, color.G, color.B, color.A);

        for (int i = 0; i < segments; i++)
        {
            float a0 = (i / (float)segments) * MathF.Tau;
            float a1 = ((i + 1) / (float)segments) * MathF.Tau;

            Vector3 p0 = baseCenter + ((tangentA * MathF.Cos(a0)) + (tangentB * MathF.Sin(a0))) * radius;
            Vector3 p1 = baseCenter + ((tangentA * MathF.Cos(a1)) + (tangentB * MathF.Sin(a1))) * radius;

            Vector3 p0Rh = CoordSpace.ToRightHanded(p0);
            Vector3 p1Rh = CoordSpace.ToRightHanded(p1);

            // Side cone triangle
            Rlgl.Vertex3f(tipRh.X, tipRh.Y, tipRh.Z);
            Rlgl.Vertex3f(p0Rh.X, p0Rh.Y, p0Rh.Z);
            Rlgl.Vertex3f(p1Rh.X, p1Rh.Y, p1Rh.Z);

            // Base cap triangle
            Rlgl.Vertex3f(baseRh.X, baseRh.Y, baseRh.Z);
            Rlgl.Vertex3f(p1Rh.X, p1Rh.Y, p1Rh.Z);
            Rlgl.Vertex3f(p0Rh.X, p0Rh.Y, p0Rh.Z);
        }

        Rlgl.End();
    }

    private static void DrawCubeHandle3D(Vector3 center, float halfExtent, Color color)
    {
        Vector3 min = center - new Vector3(halfExtent);
        Vector3 max = center + new Vector3(halfExtent);

        Span<Vector3> v =
        [
            CoordSpace.ToRightHanded(new Vector3(min.X, min.Y, min.Z)),
            CoordSpace.ToRightHanded(new Vector3(max.X, min.Y, min.Z)),
            CoordSpace.ToRightHanded(new Vector3(max.X, max.Y, min.Z)),
            CoordSpace.ToRightHanded(new Vector3(min.X, max.Y, min.Z)),
            CoordSpace.ToRightHanded(new Vector3(min.X, min.Y, max.Z)),
            CoordSpace.ToRightHanded(new Vector3(max.X, min.Y, max.Z)),
            CoordSpace.ToRightHanded(new Vector3(max.X, max.Y, max.Z)),
            CoordSpace.ToRightHanded(new Vector3(min.X, max.Y, max.Z))
        ];

        ReadOnlySpan<int> indices =
        [
            0, 1, 2, 0, 2, 3,
            4, 6, 5, 4, 7, 6,
            0, 4, 5, 0, 5, 1,
            3, 2, 6, 3, 6, 7,
            1, 5, 6, 1, 6, 2,
            0, 3, 7, 0, 7, 4
        ];

        Rlgl.Begin(DrawMode.Triangles);
        Rlgl.Color4ub(color.R, color.G, color.B, color.A);

        for (int i = 0; i < indices.Length; i++)
        {
            Vector3 pt = v[indices[i]];
            Rlgl.Vertex3f(pt.X, pt.Y, pt.Z);
        }

        Rlgl.End();
    }

    private static void DrawRotationRing3D(
        Vector3 center,
        Vector3 tangentA,
        Vector3 tangentB,
        float radius,
        Color color)
    {
        const int segments = 64;
        Rlgl.Begin(DrawMode.Lines);

        for (int i = 0; i < segments; i++)
        {
            float a0 = (i / (float)segments) * MathF.Tau;
            float a1 = ((i + 1) / (float)segments) * MathF.Tau;

            Vector3 p0 = center + ((tangentA * MathF.Cos(a0)) + (tangentB * MathF.Sin(a0))) * radius;
            Vector3 p1 = center + ((tangentA * MathF.Cos(a1)) + (tangentB * MathF.Sin(a1))) * radius;

            EmitLineEditorSpace(p0, p1, color);
        }

        Rlgl.End();
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
