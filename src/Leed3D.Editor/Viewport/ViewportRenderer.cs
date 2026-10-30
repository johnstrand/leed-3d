using System.Numerics;
using Leed3D.Editor.Texturing;
using Leed3D.Kernel.Entities;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Gizmos;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Selection;
using Leed3D.Kernel.Texturing;
using Leed3D.Kernel.Viewport;
using Leed3D.Runtime.Metadata;
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
    private readonly LitPreviewShader _litShader = new();

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
        ModalTransformController? modalController = null,
        int? csgPreviewTargetId = null,
        int? csgPreviewCutterId = null,
        Color? csgCutterWireColor = null,
        EditorTextureCache? textureCache = null,
        EntitySchemaRegistry? entityRegistry = null,
        EnvironmentLightingSettings? environmentSettings = null)
    {
        if (_renderTarget.Id == 0)
        {
            EnsureSize(1280, 720);
        }

        bool isLitPreview = shadingMode == ViewportShadingMode.LitPreview;
        EnvironmentLightingSettings env = environmentSettings ?? new EnvironmentLightingSettings();

        Raylib.BeginTextureMode(_renderTarget);

        Color clearColor = isLitPreview && env.FogEnabled
            ? new Color((int)env.FogColor.R, (int)env.FogColor.G, (int)env.FogColor.B, 255)
            : BgColor;
        Raylib.ClearBackground(clearColor);

        Camera3D rlCam = ToRaylibCamera(camera);
        Raylib.BeginMode3D(rlCam);

        if (!isLitPreview && ShowGrid)
        {
            DrawGrid(camera.ProjectionMode, gridStep);
        }

        if (!isLitPreview && ShowOriginAxes)
        {
            DrawOriginAxes();
        }

        // Render all visible scene objects
        Rlgl.EnableBackfaceCulling();

        if (isLitPreview)
        {
            List<SceneLightData> lights = SceneLightGatherer.GatherLights(scene, camera.Position);
            _litShader.Begin(camera, lights, env);
        }

        for (int i = 0; i < scene.Objects.Count; i++)
        {
            SceneObject obj = scene.Objects[i];
            if (!obj.Visible || obj.Mesh is null)
            {
                continue;
            }

            Color? customEdgeColor = null;
            if (csgCutterWireColor.HasValue && obj.Id == csgPreviewCutterId)
            {
                customEdgeColor = csgCutterWireColor.Value;
            }
            else if (obj.Id == csgPreviewTargetId)
            {
                customEdgeColor = new Color(70, 160, 255, 255); // Highlight Target in blue/cyan
            }

            DrawMeshObject(obj, shadingMode, selection, customEdgeColor, textureCache);
        }

        if (isLitPreview)
        {
            _litShader.End();
        }

        // Render gameplay entities and target-link lines
        if (!isLitPreview)
        {
            DrawEntitiesAndTargetLinks(scene, selection, entityRegistry);
        }

        // 1. Render active modal operator infinite guide lines
        if (!isLitPreview && modalController is not null && modalController.IsActive)
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
        else if (!isLitPreview && selection is not null && gizmo is not null)
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

    private static void DrawMeshObject(
        SceneObject obj,
        ViewportShadingMode shadingMode,
        SelectionManager? selection,
        Color? customEdgeColor = null,
        EditorTextureCache? textureCache = null)
    {
        EditableMesh mesh = obj.Mesh!;
        Matrix4x4 worldMatrix = obj.Transform.GetLocalMatrix();

        bool isLitPreview = shadingMode == ViewportShadingMode.LitPreview;

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
            // Group faces by material to batch textured draw calls
            var facesByMaterial = new Dictionary<string, List<PolygonFace>>(StringComparer.OrdinalIgnoreCase);
            for (int fIdx = 0; fIdx < mesh.Faces.Count; fIdx++)
            {
                PolygonFace face = mesh.Faces[fIdx];
                string matId = string.IsNullOrWhiteSpace(face.MaterialId) ? PolygonFace.DefaultMaterialId : face.MaterialId;
                if (!facesByMaterial.TryGetValue(matId, out List<PolygonFace>? list))
                {
                    list = [];
                    facesByMaterial[matId] = list;
                }
                list.Add(face);
            }

            foreach (var (matId, faces) in facesByMaterial)
            {
                Texture2D texture = default;
                bool hasTexture = textureCache != null && Raylib.IsWindowReady();
                if (hasTexture)
                {
                    texture = textureCache!.GetTexture(matId);
                    if (texture.Id != 0)
                    {
                        Rlgl.SetTexture(texture.Id);
                    }
                    else
                    {
                        hasTexture = false;
                    }
                }

                if (!hasTexture && isLitPreview && Raylib.IsWindowReady())
                {
                    Rlgl.SetTexture(Rlgl.GetTextureIdDefault());
                }

                Rlgl.Begin(DrawMode.Triangles);

                for (int f = 0; f < faces.Count; f++)
                {
                    PolygonFace face = faces[f];
                    Vector3 localNormal = face.ComputeNormal(mesh);
                    Vector3 worldNormal = Vector3.Normalize(obj.Transform.TransformDirectionToWorld(localNormal));
                    Vector3 worldNormalRh = CoordSpace.ToRightHanded(worldNormal);

                    Color shadedColor;
                    if (isLitPreview)
                    {
                        if (hasTexture)
                        {
                            shadedColor = new Color(255, 255, 255, 255);
                        }
                        else
                        {
                            Vector3 baseColor = GetMaterialBaseColor(face.MaterialId);
                            shadedColor = new Color(
                                (int)System.Math.Clamp((int)(baseColor.X * 255.0f), 0, 255),
                                (int)System.Math.Clamp((int)(baseColor.Y * 255.0f), 0, 255),
                                (int)System.Math.Clamp((int)(baseColor.Z * 255.0f), 0, 255),
                                255);
                        }
                    }
                    else
                    {
                        shadedColor = ComputeDirectionalFaceShade(worldNormal, face.MaterialId, hasTexture);
                    }

                    IReadOnlyList<Vector2> faceUvs = UvProjectionEngine.ComputeFaceUvs(face, mesh, obj.Transform);
                    IReadOnlyList<(int V0, int V1, int V2)> tris = PolygonTriangulator.TriangulateFace(face, mesh);

                    for (int tIdx = 0; tIdx < tris.Count; tIdx++)
                    {
                        if (Rlgl.CheckRenderBatchLimit(3))
                        {
                            Rlgl.Begin(DrawMode.Triangles);
                        }

                        var (v0Id, v1Id, v2Id) = tris[tIdx];
                        // Find local vertex loop indices for UVs
                        int idx0 = face.VertexIds.IndexOf(v0Id);
                        int idx1 = face.VertexIds.IndexOf(v1Id);
                        int idx2 = face.VertexIds.IndexOf(v2Id);

                        Vector2 uv0 = idx0 >= 0 && idx0 < faceUvs.Count ? faceUvs[idx0] : Vector2.Zero;
                        Vector2 uv1 = idx1 >= 0 && idx1 < faceUvs.Count ? faceUvs[idx1] : Vector2.Zero;
                        Vector2 uv2 = idx2 >= 0 && idx2 < faceUvs.Count ? faceUvs[idx2] : Vector2.Zero;

                        // Flip triangle index winding when negating Z via CoordSpace
                        var (w0Id, w1Id, w2Id) = CoordSpace.FlipTriangleWinding(v0Id, v1Id, v2Id);
                        Vector2 wUv0 = (w0Id == v0Id) ? uv0 : ((w0Id == v1Id) ? uv1 : uv2);
                        Vector2 wUv1 = (w1Id == v0Id) ? uv0 : ((w1Id == v1Id) ? uv1 : uv2);
                        Vector2 wUv2 = (w2Id == v0Id) ? uv0 : ((w2Id == v1Id) ? uv1 : uv2);

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
                        Rlgl.Normal3f(worldNormalRh.X, worldNormalRh.Y, worldNormalRh.Z);

                        Rlgl.TexCoord2f(wUv0.X, wUv0.Y);
                        Rlgl.Vertex3f(p0Rh.X, p0Rh.Y, p0Rh.Z);

                        Rlgl.TexCoord2f(wUv1.X, wUv1.Y);
                        Rlgl.Vertex3f(p1Rh.X, p1Rh.Y, p1Rh.Z);

                        Rlgl.TexCoord2f(wUv2.X, wUv2.Y);
                        Rlgl.Vertex3f(p2Rh.X, p2Rh.Y, p2Rh.Z);
                    }
                }

                Rlgl.End();

                if (hasTexture || (isLitPreview && Raylib.IsWindowReady()))
                {
                    Rlgl.SetTexture(0);
                }
            }
        }

        // Selected faces overlay in Face Mode (semi-transparent orange fill)
        HashSet<EdgeKey>? selectedFaceBoundaryEdges = null;
        if (!isLitPreview && isEditModeActive && selection!.Mode == InteractionMode.Face && selection.SelectedFaceIds.Count > 0)
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

        // Collision-disabled faces visual indicator (translucent magenta/purple fill with hatched lines to centroid)
        bool hasCollisionDisabledFaces = false;
        if (!isLitPreview)
        {
            for (int i = 0; i < mesh.Faces.Count; i++)
            {
                if (mesh.Faces[i].Metadata.IsCollisionDisabled())
                {
                    hasCollisionDisabledFaces = true;
                    break;
                }
            }
        }

        if (!isLitPreview && hasCollisionDisabledFaces)
        {
            Color collisionDisabledFill = new(180, 50, 220, 75);
            Color collisionDisabledHatch = new(215, 80, 255, 200);

            Rlgl.Begin(DrawMode.Triangles);
            for (int fIdx = 0; fIdx < mesh.Faces.Count; fIdx++)
            {
                PolygonFace face = mesh.Faces[fIdx];
                if (!face.Metadata.IsCollisionDisabled())
                {
                    continue;
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

                    Rlgl.Color4ub(collisionDisabledFill.R, collisionDisabledFill.G, collisionDisabledFill.B, collisionDisabledFill.A);
                    Rlgl.Vertex3f(p0Rh.X, p0Rh.Y, p0Rh.Z);
                    Rlgl.Vertex3f(p1Rh.X, p1Rh.Y, p1Rh.Z);
                    Rlgl.Vertex3f(p2Rh.X, p2Rh.Y, p2Rh.Z);
                }
            }
            Rlgl.End();

            // Diagonal hatch lines connecting centroid to face vertices
            Rlgl.Begin(DrawMode.Lines);
            for (int fIdx = 0; fIdx < mesh.Faces.Count; fIdx++)
            {
                PolygonFace face = mesh.Faces[fIdx];
                if (!face.Metadata.IsCollisionDisabled())
                {
                    continue;
                }

                Vector3 centerWorld = Vector3.Transform(face.ComputeCentroid(mesh), worldMatrix);
                for (int v = 0; v < face.VertexIds.Count; v++)
                {
                    if (mesh.GetVertex(face.VertexIds[v]) is { } vert)
                    {
                        Vector3 vertWorld = Vector3.Transform(vert.Position, worldMatrix);
                        EmitLineEditorSpace(centerWorld, vertWorld, collisionDisabledHatch);
                    }
                }
            }
            Rlgl.End();
        }

        // Draw true N-gon topological edges (not internal triangulation diagonals!)
        if (!isLitPreview && (drawWireframe || isObjectSelected || customEdgeColor.HasValue))
        {
            Color defaultEdgeColor = shadingMode == ViewportShadingMode.Wireframe ? PureWireframeColor : WireframeEdgeColor;
            Color objectHighlightColor = new(255, 140, 30, 255);
            Color selectedSubEdgeColor = new(255, 150, 30, 255);
            Color metadataEdgeColor = new(48, 224, 208, 255);

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

                Color edgeColor = customEdgeColor ?? defaultEdgeColor;

                if (!customEdgeColor.HasValue)
                {
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
                        else if (mesh.EdgeMetadata.TryGetValue(edgeKey, out var edgeMeta) && (edgeMeta.Properties.Count > 0 || edgeMeta.Tags.Count > 0))
                        {
                            edgeColor = metadataEdgeColor;
                        }
                    }
                    else if (mesh.EdgeMetadata.TryGetValue(edgeKey, out var edgeMeta) && (edgeMeta.Properties.Count > 0 || edgeMeta.Tags.Count > 0))
                    {
                        edgeColor = metadataEdgeColor;
                    }
                }

                EmitLineEditorSpace(pA, pB, edgeColor);
            }
            Rlgl.End();

            // In Vertex Mode, draw vertex handles with metadata highlights
            if (isEditModeActive && selection!.Mode == InteractionMode.Vertex)
            {
                Color vertexNormalColor = new(200, 200, 200, 255);
                Color vertexSelectedColor = new(255, 150, 30, 255);
                Color vertexMetaColor = new(48, 224, 208, 255);
                const float vSize = 0.04f;

                for (int i = 0; i < mesh.Vertices.Count; i++)
                {
                    MeshVertex v = mesh.Vertices[i];
                    Vector3 pWorld = Vector3.Transform(v.Position, worldMatrix);
                    bool isSelected = selection.SelectedVertexIds.Contains(v.Id);
                    bool hasMeta = v.Metadata.Properties.Count > 0 || v.Metadata.Tags.Count > 0;
                    Color col = isSelected ? vertexSelectedColor : (hasMeta ? vertexMetaColor : vertexNormalColor);
                    DrawCubeHandle3D(pWorld, isSelected ? vSize * 1.4f : vSize, col);
                }
            }
        }
    }

    private static Color ComputeDirectionalFaceShade(Vector3 worldNormal, string materialId, bool hasTexture = false)
    {
        if (hasTexture)
        {
            // Subtle directional lighting tint when texture is active
            float nDotL1 = System.Math.Max(0.0f, Vector3.Dot(worldNormal, LightDir1));
            float nDotL2 = System.Math.Max(0.0f, Vector3.Dot(worldNormal, LightDir2));
            float light = 0.55f + (nDotL1 * 0.35f) + (nDotL2 * 0.10f);
            byte litVal = (byte)System.Math.Clamp((int)(light * 255.0f), 0, 255);
            return new Color((int)litVal, (int)litVal, (int)litVal, 255);
        }

        // Base color derived from materialId
        Vector3 baseColor = GetMaterialBaseColor(materialId);

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

    public static Vector3 GetMaterialBaseColor(string materialId)
    {
        return materialId switch
        {
            "dev_grid_orange" => new Vector3(0.90f, 0.52f, 0.22f),
            "dev_grid_blue" => new Vector3(0.32f, 0.58f, 0.88f),
            "dev_grid_green" => new Vector3(0.35f, 0.75f, 0.42f),
            _ => new Vector3(0.68f, 0.71f, 0.75f)
        };
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

    private static void EmitTriangleEditorSpace(Vector3 p0, Vector3 p1, Vector3 p2, Color color)
    {
        if (Rlgl.CheckRenderBatchLimit(3))
        {
            Rlgl.Begin(DrawMode.Triangles);
        }
        Vector3 r0 = CoordSpace.ToRightHanded(p0);
        Vector3 r1 = CoordSpace.ToRightHanded(p1);
        Vector3 r2 = CoordSpace.ToRightHanded(p2);
        Rlgl.Color4ub(color.R, color.G, color.B, color.A);
        Rlgl.Vertex3f(r0.X, r0.Y, r0.Z);
        Rlgl.Vertex3f(r1.X, r1.Y, r1.Z);
        Rlgl.Vertex3f(r2.X, r2.Y, r2.Z);
    }

    private static void DrawEntitiesAndTargetLinks(
        EditorScene scene,
        SelectionManager? selection,
        EntitySchemaRegistry? entityRegistry)
    {
        if (scene.Entities.Count == 0)
        {
            return;
        }

        EntitySchemaRegistry registry = entityRegistry ?? EntitySchemaRegistry.Default;

        // 1. Draw Target Link Lines
        for (int i = 0; i < scene.Entities.Count; i++)
        {
            LevelEntity ent = scene.Entities[i];
            if (!ent.Visible) continue;

            string? targetId = null;
            if (ent.Properties.TryGet("target_id", out var tVal) && tVal is not null && !string.IsNullOrWhiteSpace(tVal.StringValue))
            {
                targetId = tVal.StringValue.Trim();
            }
            else if (ent.Properties.TryGet("target", out var tVal2) && tVal2 is not null && !string.IsNullOrWhiteSpace(tVal2.StringValue))
            {
                targetId = tVal2.StringValue.Trim();
            }

            if (!string.IsNullOrEmpty(targetId))
            {
                Vector3? targetPos = null;
                LevelEntity? destEnt = scene.GetEntity(targetId);
                if (destEnt != null)
                {
                    targetPos = destEnt.Transform.Position;
                }
                else
                {
                    SceneObject? destObj = scene.Objects.FirstOrDefault(o =>
                        o.Name.Equals(targetId, StringComparison.OrdinalIgnoreCase) ||
                        o.Id.ToString() == targetId);
                    if (destObj != null)
                    {
                        targetPos = destObj.Transform.Position;
                    }
                }

                if (targetPos.HasValue)
                {
                    bool isSourceSelected = selection is not null &&
                        selection.Mode == InteractionMode.Object &&
                        selection.SelectedEntityIds.Contains(ent.Id);

                    DrawTargetLink(ent.Transform.Position, targetPos.Value, isSourceSelected);
                }
            }
        }

        // 2. Draw Entities
        for (int i = 0; i < scene.Entities.Count; i++)
        {
            LevelEntity ent = scene.Entities[i];
            if (!ent.Visible) continue;

            bool isSelected = selection is not null &&
                selection.Mode == InteractionMode.Object &&
                selection.SelectedEntityIds.Contains(ent.Id);

            registry.TryGetDefinition(ent.ClassName, out EntityDefinition? def);
            DrawEntityGizmo(ent, def, isSelected, registry);
        }
    }

    private static void DrawTargetLink(Vector3 start, Vector3 end, bool isHighlighted)
    {
        Vector3 delta = end - start;
        float dist = delta.Length();
        if (dist < 0.001f)
        {
            return;
        }

        Vector3 dir = delta / dist;
        Color lineColor = isHighlighted
            ? new Color(255, 230, 50, 255)
            : new Color(0, 230, 255, 200);

        Rlgl.Begin(DrawMode.Lines);
        EmitLineEditorSpace(start, end, lineColor);

        Vector3 mid = Vector3.Lerp(start, end, 0.5f);
        float arrowLen = MathF.Min(0.4f, dist * 0.25f);
        float arrowWidth = arrowLen * 0.4f;

        Vector3 up = MathF.Abs(dir.Y) > 0.95f ? Vector3.UnitX : Vector3.UnitY;
        Vector3 tanA = Vector3.Normalize(Vector3.Cross(dir, up));
        Vector3 tanB = Vector3.Cross(dir, tanA);

        Vector3 tip = mid + (dir * (arrowLen * 0.5f));
        Vector3 arrowBase = mid - (dir * (arrowLen * 0.5f));

        EmitLineEditorSpace(tip, arrowBase + (tanA * arrowWidth), lineColor);
        EmitLineEditorSpace(tip, arrowBase - (tanA * arrowWidth), lineColor);
        EmitLineEditorSpace(tip, arrowBase + (tanB * arrowWidth), lineColor);
        EmitLineEditorSpace(tip, arrowBase - (tanB * arrowWidth), lineColor);
        Rlgl.End();
    }

    private static void DrawEntityGizmo(
        LevelEntity entity,
        EntityDefinition? def,
        bool isSelected,
        EntitySchemaRegistry registry)
    {
        EntityGizmoShape shape = def?.GizmoShape ?? InferGizmoShape(entity.ClassName);
        Color baseColor = GetEntityColor(entity, def);

        if (isSelected)
        {
            BoundingBox3D bounds = entity.GetWorldBounds(registry);
            Color selColor = new(255, 140, 30, 255);
            DrawWireBoundingBox(bounds, selColor);
        }

        switch (shape)
        {
            case EntityGizmoShape.CapsuleWithArrow:
            case EntityGizmoShape.Capsule:
                DrawCapsuleGizmo(entity, baseColor, withArrow: shape == EntityGizmoShape.CapsuleWithArrow);
                break;

            case EntityGizmoShape.LightGizmo:
                DrawLightGizmo(entity, baseColor, isSelected);
                break;

            case EntityGizmoShape.BoxVolume:
            case EntityGizmoShape.Box:
                DrawBoxGizmo(entity, def, baseColor, isVolume: shape == EntityGizmoShape.BoxVolume);
                break;

            case EntityGizmoShape.Sphere:
                DrawSphereGizmo(entity, def, baseColor);
                break;

            case EntityGizmoShape.Arrow:
                DrawArrowGizmo(entity, baseColor);
                break;

            case EntityGizmoShape.Point:
            default:
                DrawPointGizmo(entity, baseColor);
                break;
        }
    }

    private static EntityGizmoShape InferGizmoShape(string className)
    {
        if (className.Equals("player_spawn", StringComparison.OrdinalIgnoreCase)) return EntityGizmoShape.CapsuleWithArrow;
        if (className.Equals("light", StringComparison.OrdinalIgnoreCase)) return EntityGizmoShape.LightGizmo;
        if (className.Equals("portal", StringComparison.OrdinalIgnoreCase) || className.Equals("trigger", StringComparison.OrdinalIgnoreCase)) return EntityGizmoShape.BoxVolume;
        return EntityGizmoShape.Box;
    }

    private static Color GetEntityColor(LevelEntity entity, EntityDefinition? def)
    {
        if (entity.Properties.TryGet("color", out var colVal) && colVal is not null && colVal.Type == MetadataValueType.Color)
        {
            ColorRgba c = colVal.ColorValue;
            return new Color(c.R, c.G, c.B, c.A);
        }

        if (def is not null)
        {
            return new Color(def.Color.R, def.Color.G, def.Color.B, def.Color.A);
        }

        if (entity.ClassName.Equals("player_spawn", StringComparison.OrdinalIgnoreCase))
        {
            return new Color(60, 220, 80, 255);
        }
        if (entity.ClassName.Equals("light", StringComparison.OrdinalIgnoreCase))
        {
            return new Color(255, 240, 210, 255);
        }
        if (entity.ClassName.Equals("portal", StringComparison.OrdinalIgnoreCase))
        {
            return new Color(0, 220, 240, 255);
        }
        if (entity.ClassName.Equals("trigger", StringComparison.OrdinalIgnoreCase))
        {
            return new Color(240, 200, 40, 255);
        }

        return new Color(200, 200, 200, 255);
    }

    private static void DrawCapsuleGizmo(LevelEntity entity, Color baseColor, bool withArrow)
    {
        float h = 1.8f;
        float r = 0.4f;
        float halfH = h * 0.5f;
        float capY = halfH - r;

        // Cap rings
        DrawLocalCircle(entity, new Vector3(0.0f, capY, 0.0f), Vector3.UnitY, r, baseColor);
        DrawLocalCircle(entity, new Vector3(0.0f, -capY, 0.0f), Vector3.UnitY, r, baseColor);

        // 4 vertical connecting struts
        Rlgl.Begin(DrawMode.Lines);
        EmitLineEditorSpace(entity.Transform.TransformPointToWorld(new Vector3(r, capY, 0.0f)), entity.Transform.TransformPointToWorld(new Vector3(r, -capY, 0.0f)), baseColor);
        EmitLineEditorSpace(entity.Transform.TransformPointToWorld(new Vector3(-r, capY, 0.0f)), entity.Transform.TransformPointToWorld(new Vector3(-r, -capY, 0.0f)), baseColor);
        EmitLineEditorSpace(entity.Transform.TransformPointToWorld(new Vector3(0.0f, capY, r)), entity.Transform.TransformPointToWorld(new Vector3(0.0f, -capY, r)), baseColor);
        EmitLineEditorSpace(entity.Transform.TransformPointToWorld(new Vector3(0.0f, capY, -r)), entity.Transform.TransformPointToWorld(new Vector3(0.0f, -capY, -r)), baseColor);
        Rlgl.End();

        // Top dome arcs
        DrawLocalArc(entity, new Vector3(0.0f, capY, 0.0f), Vector3.UnitX, Vector3.UnitY, r, 0.0f, MathF.PI, baseColor);
        DrawLocalArc(entity, new Vector3(0.0f, capY, 0.0f), Vector3.UnitZ, Vector3.UnitY, r, 0.0f, MathF.PI, baseColor);

        // Bottom dome arcs
        DrawLocalArc(entity, new Vector3(0.0f, -capY, 0.0f), Vector3.UnitX, Vector3.UnitY, r, MathF.PI, MathF.Tau, baseColor);
        DrawLocalArc(entity, new Vector3(0.0f, -capY, 0.0f), Vector3.UnitZ, Vector3.UnitY, r, MathF.PI, MathF.Tau, baseColor);

        if (withArrow)
        {
            Vector3 chest = new(0.0f, 0.2f, 0.0f);
            Vector3 tip = chest + new Vector3(0.0f, 0.0f, r + 0.35f);
            Vector3 wChest = entity.Transform.TransformPointToWorld(chest);
            Vector3 wTip = entity.Transform.TransformPointToWorld(tip);
            Vector3 wWingL = entity.Transform.TransformPointToWorld(tip + new Vector3(-0.09f, 0.0f, -0.15f));
            Vector3 wWingR = entity.Transform.TransformPointToWorld(tip + new Vector3(0.09f, 0.0f, -0.15f));

            Rlgl.Begin(DrawMode.Lines);
            EmitLineEditorSpace(wChest, wTip, baseColor);
            EmitLineEditorSpace(wTip, wWingL, baseColor);
            EmitLineEditorSpace(wTip, wWingR, baseColor);
            Rlgl.End();
        }
    }

    private static void DrawLightGizmo(LevelEntity entity, Color baseColor, bool isSelected)
    {
        float s = 0.25f;
        Vector3 vTop = entity.Transform.TransformPointToWorld(new Vector3(0.0f, s, 0.0f));
        Vector3 vBot = entity.Transform.TransformPointToWorld(new Vector3(0.0f, -s, 0.0f));
        Vector3 vR = entity.Transform.TransformPointToWorld(new Vector3(s, 0.0f, 0.0f));
        Vector3 vL = entity.Transform.TransformPointToWorld(new Vector3(-s, 0.0f, 0.0f));
        Vector3 vF = entity.Transform.TransformPointToWorld(new Vector3(0.0f, 0.0f, s));
        Vector3 vB = entity.Transform.TransformPointToWorld(new Vector3(0.0f, 0.0f, -s));

        Color fillCol = new(baseColor.R, baseColor.G, baseColor.B, (byte)70);
        EmitTriangleEditorSpace(vTop, vR, vF, fillCol);
        EmitTriangleEditorSpace(vTop, vF, vL, fillCol);
        EmitTriangleEditorSpace(vTop, vL, vB, fillCol);
        EmitTriangleEditorSpace(vTop, vB, vR, fillCol);
        EmitTriangleEditorSpace(vBot, vF, vR, fillCol);
        EmitTriangleEditorSpace(vBot, vL, vF, fillCol);
        EmitTriangleEditorSpace(vBot, vB, vL, fillCol);
        EmitTriangleEditorSpace(vBot, vR, vB, fillCol);

        Rlgl.Begin(DrawMode.Lines);
        EmitLineEditorSpace(vR, vF, baseColor);
        EmitLineEditorSpace(vF, vL, baseColor);
        EmitLineEditorSpace(vL, vB, baseColor);
        EmitLineEditorSpace(vB, vR, baseColor);

        EmitLineEditorSpace(vTop, vR, baseColor);
        EmitLineEditorSpace(vTop, vF, baseColor);
        EmitLineEditorSpace(vTop, vL, baseColor);
        EmitLineEditorSpace(vTop, vB, baseColor);

        EmitLineEditorSpace(vBot, vR, baseColor);
        EmitLineEditorSpace(vBot, vF, baseColor);
        EmitLineEditorSpace(vBot, vL, baseColor);
        EmitLineEditorSpace(vBot, vB, baseColor);
        Rlgl.End();

        if (isSelected)
        {
            string lightType = "point";
            if (entity.Properties.TryGet("light_type", out var lt) && lt is not null && !string.IsNullOrWhiteSpace(lt.StringValue))
            {
                lightType = lt.StringValue;
            }

            float radius = 12.0f;
            if (entity.Properties.TryGet("radius", out var rVal) && rVal is not null && rVal.Type == MetadataValueType.Float)
            {
                radius = rVal.FloatValue;
            }

            Color rangeColor = new(baseColor.R, baseColor.G, baseColor.B, (byte)130);

            if (lightType.Equals("spot", StringComparison.OrdinalIgnoreCase))
            {
                float spotAngle = 45.0f;
                if (entity.Properties.TryGet("spot_angle", out var saVal) && saVal is not null && saVal.Type == MetadataValueType.Float)
                {
                    spotAngle = saVal.FloatValue;
                }

                float halfAngleRad = (spotAngle * 0.5f) * (MathF.PI / 180.0f);
                float coneBaseRadius = radius * MathF.Tan(halfAngleRad);

                DrawLocalCircle(entity, new Vector3(0.0f, 0.0f, radius), Vector3.UnitZ, coneBaseRadius, rangeColor);

                Vector3 wApex = entity.Transform.Position;
                Vector3 wC0 = entity.Transform.TransformPointToWorld(new Vector3(coneBaseRadius, 0.0f, radius));
                Vector3 wC1 = entity.Transform.TransformPointToWorld(new Vector3(-coneBaseRadius, 0.0f, radius));
                Vector3 wC2 = entity.Transform.TransformPointToWorld(new Vector3(0.0f, coneBaseRadius, radius));
                Vector3 wC3 = entity.Transform.TransformPointToWorld(new Vector3(0.0f, -coneBaseRadius, radius));
                Vector3 wCenter = entity.Transform.TransformPointToWorld(new Vector3(0.0f, 0.0f, radius));

                Rlgl.Begin(DrawMode.Lines);
                EmitLineEditorSpace(wApex, wC0, rangeColor);
                EmitLineEditorSpace(wApex, wC1, rangeColor);
                EmitLineEditorSpace(wApex, wC2, rangeColor);
                EmitLineEditorSpace(wApex, wC3, rangeColor);
                EmitLineEditorSpace(wApex, wCenter, rangeColor);
                Rlgl.End();
            }
            else
            {
                DrawWorldCircle(entity.Transform.Position, Vector3.UnitX, Vector3.UnitY, radius, rangeColor);
                DrawWorldCircle(entity.Transform.Position, Vector3.UnitX, Vector3.UnitZ, radius, rangeColor);
                DrawWorldCircle(entity.Transform.Position, Vector3.UnitY, Vector3.UnitZ, radius, rangeColor);
            }
        }
    }

    private static void DrawBoxGizmo(LevelEntity entity, EntityDefinition? def, Color baseColor, bool isVolume)
    {
        Vector3 size = def?.Size ?? Vector3.One;
        if (entity.Properties.TryGet("size", out var sVal) && sVal is not null && sVal.Type == MetadataValueType.Vec3)
        {
            size = sVal.Vec3Value;
        }

        Vector3 half = size * 0.5f;

        Vector3 p0 = entity.Transform.TransformPointToWorld(new Vector3(-half.X, -half.Y, -half.Z));
        Vector3 p1 = entity.Transform.TransformPointToWorld(new Vector3( half.X, -half.Y, -half.Z));
        Vector3 p2 = entity.Transform.TransformPointToWorld(new Vector3( half.X,  half.Y, -half.Z));
        Vector3 p3 = entity.Transform.TransformPointToWorld(new Vector3(-half.X,  half.Y, -half.Z));
        Vector3 p4 = entity.Transform.TransformPointToWorld(new Vector3(-half.X, -half.Y,  half.Z));
        Vector3 p5 = entity.Transform.TransformPointToWorld(new Vector3( half.X, -half.Y,  half.Z));
        Vector3 p6 = entity.Transform.TransformPointToWorld(new Vector3( half.X,  half.Y,  half.Z));
        Vector3 p7 = entity.Transform.TransformPointToWorld(new Vector3(-half.X,  half.Y,  half.Z));

        if (isVolume)
        {
            Color fillCol = new(baseColor.R, baseColor.G, baseColor.B, (byte)50);
            EmitTriangleEditorSpace(p0, p2, p1, fillCol);
            EmitTriangleEditorSpace(p0, p3, p2, fillCol);
            EmitTriangleEditorSpace(p4, p5, p6, fillCol);
            EmitTriangleEditorSpace(p4, p6, p7, fillCol);
            EmitTriangleEditorSpace(p0, p1, p5, fillCol);
            EmitTriangleEditorSpace(p0, p5, p4, fillCol);
            EmitTriangleEditorSpace(p3, p6, p2, fillCol);
            EmitTriangleEditorSpace(p3, p7, p6, fillCol);
            EmitTriangleEditorSpace(p0, p4, p7, fillCol);
            EmitTriangleEditorSpace(p0, p7, p3, fillCol);
            EmitTriangleEditorSpace(p1, p2, p6, fillCol);
            EmitTriangleEditorSpace(p1, p6, p5, fillCol);
        }

        Rlgl.Begin(DrawMode.Lines);
        EmitLineEditorSpace(p0, p1, baseColor);
        EmitLineEditorSpace(p1, p5, baseColor);
        EmitLineEditorSpace(p5, p4, baseColor);
        EmitLineEditorSpace(p4, p0, baseColor);

        EmitLineEditorSpace(p3, p2, baseColor);
        EmitLineEditorSpace(p2, p6, baseColor);
        EmitLineEditorSpace(p6, p7, baseColor);
        EmitLineEditorSpace(p7, p3, baseColor);

        EmitLineEditorSpace(p0, p3, baseColor);
        EmitLineEditorSpace(p1, p2, baseColor);
        EmitLineEditorSpace(p5, p6, baseColor);
        EmitLineEditorSpace(p4, p7, baseColor);
        Rlgl.End();
    }

    private static void DrawSphereGizmo(LevelEntity entity, EntityDefinition? def, Color baseColor)
    {
        Vector3 size = def?.Size ?? Vector3.One;
        float radius = MathF.Max(0.1f, size.X * 0.5f);

        DrawLocalCircle(entity, Vector3.Zero, Vector3.UnitX, radius, baseColor);
        DrawLocalCircle(entity, Vector3.Zero, Vector3.UnitY, radius, baseColor);
        DrawLocalCircle(entity, Vector3.Zero, Vector3.UnitZ, radius, baseColor);
    }

    private static void DrawArrowGizmo(LevelEntity entity, Color baseColor)
    {
        Vector3 start = entity.Transform.TransformPointToWorld(new Vector3(0.0f, 0.0f, -0.5f));
        Vector3 end = entity.Transform.TransformPointToWorld(new Vector3(0.0f, 0.0f, 0.5f));
        Vector3 wingA = entity.Transform.TransformPointToWorld(new Vector3(0.15f, 0.0f, 0.3f));
        Vector3 wingB = entity.Transform.TransformPointToWorld(new Vector3(-0.15f, 0.0f, 0.3f));
        Vector3 wingC = entity.Transform.TransformPointToWorld(new Vector3(0.0f, 0.15f, 0.3f));
        Vector3 wingD = entity.Transform.TransformPointToWorld(new Vector3(0.0f, -0.15f, 0.3f));

        Rlgl.Begin(DrawMode.Lines);
        EmitLineEditorSpace(start, end, baseColor);
        EmitLineEditorSpace(end, wingA, baseColor);
        EmitLineEditorSpace(end, wingB, baseColor);
        EmitLineEditorSpace(end, wingC, baseColor);
        EmitLineEditorSpace(end, wingD, baseColor);
        Rlgl.End();
    }

    private static void DrawPointGizmo(LevelEntity entity, Color baseColor)
    {
        const float len = 0.3f;
        Vector3 x0 = entity.Transform.TransformPointToWorld(new Vector3(-len, 0.0f, 0.0f));
        Vector3 x1 = entity.Transform.TransformPointToWorld(new Vector3(len, 0.0f, 0.0f));
        Vector3 y0 = entity.Transform.TransformPointToWorld(new Vector3(0.0f, -len, 0.0f));
        Vector3 y1 = entity.Transform.TransformPointToWorld(new Vector3(0.0f, len, 0.0f));
        Vector3 z0 = entity.Transform.TransformPointToWorld(new Vector3(0.0f, 0.0f, -len));
        Vector3 z1 = entity.Transform.TransformPointToWorld(new Vector3(0.0f, 0.0f, len));

        Rlgl.Begin(DrawMode.Lines);
        EmitLineEditorSpace(x0, x1, baseColor);
        EmitLineEditorSpace(y0, y1, baseColor);
        EmitLineEditorSpace(z0, z1, baseColor);
        Rlgl.End();
    }

    private static void DrawWireBoundingBox(BoundingBox3D bounds, Color color)
    {
        Vector3 min = bounds.Min;
        Vector3 max = bounds.Max;

        Vector3 p0 = new(min.X, min.Y, min.Z);
        Vector3 p1 = new(max.X, min.Y, min.Z);
        Vector3 p2 = new(max.X, max.Y, min.Z);
        Vector3 p3 = new(min.X, max.Y, min.Z);
        Vector3 p4 = new(min.X, min.Y, max.Z);
        Vector3 p5 = new(max.X, min.Y, max.Z);
        Vector3 p6 = new(max.X, max.Y, max.Z);
        Vector3 p7 = new(min.X, max.Y, max.Z);

        Rlgl.Begin(DrawMode.Lines);
        EmitLineEditorSpace(p0, p1, color);
        EmitLineEditorSpace(p1, p5, color);
        EmitLineEditorSpace(p5, p4, color);
        EmitLineEditorSpace(p4, p0, color);

        EmitLineEditorSpace(p3, p2, color);
        EmitLineEditorSpace(p2, p6, color);
        EmitLineEditorSpace(p6, p7, color);
        EmitLineEditorSpace(p7, p3, color);

        EmitLineEditorSpace(p0, p3, color);
        EmitLineEditorSpace(p1, p2, color);
        EmitLineEditorSpace(p5, p6, color);
        EmitLineEditorSpace(p4, p7, color);
        Rlgl.End();
    }

    private static void DrawLocalCircle(LevelEntity entity, Vector3 centerLocal, Vector3 normalLocal, float radius, Color color, int segments = 24)
    {
        Vector3 up = MathF.Abs(normalLocal.Y) > 0.95f ? Vector3.UnitX : Vector3.UnitY;
        Vector3 tanA = Vector3.Normalize(Vector3.Cross(normalLocal, up));
        Vector3 tanB = Vector3.Cross(normalLocal, tanA);

        Rlgl.Begin(DrawMode.Lines);
        for (int i = 0; i < segments; i++)
        {
            float a0 = (i / (float)segments) * MathF.Tau;
            float a1 = ((i + 1) / (float)segments) * MathF.Tau;

            Vector3 l0 = centerLocal + (((tanA * MathF.Cos(a0)) + (tanB * MathF.Sin(a0))) * radius);
            Vector3 l1 = centerLocal + (((tanA * MathF.Cos(a1)) + (tanB * MathF.Sin(a1))) * radius);

            Vector3 w0 = entity.Transform.TransformPointToWorld(l0);
            Vector3 w1 = entity.Transform.TransformPointToWorld(l1);

            EmitLineEditorSpace(w0, w1, color);
        }
        Rlgl.End();
    }

    private static void DrawLocalArc(LevelEntity entity, Vector3 centerLocal, Vector3 tanA, Vector3 tanB, float radius, float startAngle, float endAngle, Color color, int segments = 12)
    {
        Rlgl.Begin(DrawMode.Lines);
        for (int i = 0; i < segments; i++)
        {
            float t0 = (float)i / segments;
            float t1 = (float)(i + 1) / segments;
            float a0 = startAngle + (t0 * (endAngle - startAngle));
            float a1 = startAngle + (t1 * (endAngle - startAngle));

            Vector3 l0 = centerLocal + (((tanA * MathF.Cos(a0)) + (tanB * MathF.Sin(a0))) * radius);
            Vector3 l1 = centerLocal + (((tanA * MathF.Cos(a1)) + (tanB * MathF.Sin(a1))) * radius);

            Vector3 w0 = entity.Transform.TransformPointToWorld(l0);
            Vector3 w1 = entity.Transform.TransformPointToWorld(l1);

            EmitLineEditorSpace(w0, w1, color);
        }
        Rlgl.End();
    }

    private static void DrawWorldCircle(Vector3 centerWorld, Vector3 tanA, Vector3 tanB, float radius, Color color, int segments = 32)
    {
        Rlgl.Begin(DrawMode.Lines);
        for (int i = 0; i < segments; i++)
        {
            float a0 = (i / (float)segments) * MathF.Tau;
            float a1 = ((i + 1) / (float)segments) * MathF.Tau;

            Vector3 w0 = centerWorld + (((tanA * MathF.Cos(a0)) + (tanB * MathF.Sin(a0))) * radius);
            Vector3 w1 = centerWorld + (((tanA * MathF.Cos(a1)) + (tanB * MathF.Sin(a1))) * radius);

            EmitLineEditorSpace(w0, w1, color);
        }
        Rlgl.End();
    }

    public void Dispose()
    {
        _litShader.Dispose();
        if (_renderTarget.Id != 0)
        {
            Raylib.UnloadRenderTexture(_renderTarget);
            _renderTarget = default;
        }
    }
}
