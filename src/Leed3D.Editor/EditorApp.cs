using System.Numerics;
using ImGuiNET;
using Leed3D.Editor.UI;
using Leed3D.Editor.Viewport;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Primitives;
using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Viewport;
using Raylib_cs;

namespace Leed3D.Editor;

/// <summary>
/// Main desktop application shell managing the Raylib window, RlImGui docking workspace,
/// top menu bar, viewport header bar (Projection, Shading, Grid & Angle Snap controls),
/// 3D viewport camera navigation, primitive creation toolbar, Shift+A quick-add menu,
/// interactive adjust-primitive popup, and status bar.
/// </summary>
public sealed class EditorApp : IDisposable
{
    private readonly ViewportRenderer _viewportRenderer = new();

    public EditorScene Scene { get; } = new("Untitled Level");
    public UndoRedoStack UndoStack { get; } = new();
    public EditorCamera Camera { get; } = new();
    public PrimitiveCreationController PrimitiveController { get; }

    public ViewportShadingMode ShadingMode { get; set; } = ViewportShadingMode.SolidWithWireframe;
    public bool SnapToGridEnabled { get; set; } = true;
    public float CurrentGridStep { get; set; } = 1.0f;
    public bool SnapAngleEnabled { get; set; } = true;
    public float CurrentAngleStepDegrees { get; set; } = 15.0f;

    private bool _isViewportHovered;
    private bool _isViewportFocused;
    private Vector2 _viewportScreenMin;
    private Vector2 _viewportLocalMousePos;
    private string _statusMessage = "Ready — Click primitive buttons to add, Shift+A for quick-add menu, RMB+WASD to fly.";

    public EditorApp()
    {
        PrimitiveController = new PrimitiveCreationController(Scene, UndoStack);
        InitializeDefaultStarterScene();
    }

    private void InitializeDefaultStarterScene()
    {
        var floorMesh = PrimitiveGenerator.CreateBox(new Vector3(8.0f, 0.5f, 8.0f), materialId: "dev_grid_gray");
        Scene.AddMeshObject("Floor Block", floorMesh, new Vector3(0.0f, -0.25f, 0.0f));

        var boxMesh = PrimitiveGenerator.CreateBox(new Vector3(2.0f, 2.0f, 2.0f), materialId: "dev_grid_orange");
        Scene.AddMeshObject("Starter Cube", boxMesh, new Vector3(0.0f, 1.0f, 0.0f));
    }

    public void Run()
    {
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint | ConfigFlags.VSyncHint);
        Raylib.InitWindow(1600, 920, "Leed3D — 3D Level & Blockout Editor");
        Raylib.SetTargetFPS(60);
        Raylib.SetExitKey(KeyboardKey.Null); // Reserve Esc for canceling modal/gizmo operations

        RlImGui.Setup(darkTheme: true, enableDocking: true);
        _viewportRenderer.EnsureSize(1280, 720);

        while (!Raylib.WindowShouldClose())
        {
            float dt = Raylib.GetFrameTime();
            HandleGlobalShortcuts();

            // Render 3D scene into offscreen RenderTexture2D before drawing ImGui
            _viewportRenderer.RenderScene(Scene, Camera, ShadingMode, CurrentGridStep);

            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(20, 22, 25, 255));

            RlImGui.Begin(dt);
            DrawDockspaceAndBars(dt);
            RlImGui.End();

            Raylib.EndDrawing();
        }
    }

    private void HandleGlobalShortcuts()
    {
        ImGuiIOPtr io = ImGui.GetIO();
        if (io.WantTextInput)
        {
            return;
        }

        bool ctrl = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
        bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);

        // Undo (Ctrl+Z) & Redo (Ctrl+Y or Ctrl+Shift+Z)
        if (ctrl && !shift && Raylib.IsKeyPressed(KeyboardKey.Z))
        {
            if (UndoStack.Undo(Scene))
            {
                PrimitiveController.Dismiss();
                _statusMessage = "Undid last action.";
            }
        }
        else if ((ctrl && Raylib.IsKeyPressed(KeyboardKey.Y)) ||
                 (ctrl && shift && Raylib.IsKeyPressed(KeyboardKey.Z)))
        {
            if (UndoStack.Redo(Scene))
            {
                PrimitiveController.Dismiss();
                _statusMessage = "Redid action.";
            }
        }
    }

    private void DrawDockspaceAndBars(float dt)
    {
        ImGuiViewportPtr mainViewport = ImGui.GetMainViewport();

        DrawTopMenuBar();

        const float statusBarHeight = 26.0f;
        Vector2 workPos = mainViewport.WorkPos;
        Vector2 workSize = new(mainViewport.WorkSize.X, System.Math.Max(64.0f, mainViewport.WorkSize.Y - statusBarHeight));

        ImGui.SetNextWindowPos(workPos);
        ImGui.SetNextWindowSize(workSize);
        ImGui.SetNextWindowViewport(mainViewport.ID);

        ImGuiWindowFlags hostFlags =
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoBringToFrontOnFocus |
            ImGuiWindowFlags.NoNavFocus |
            ImGuiWindowFlags.NoBackground;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        ImGui.Begin("Leed3DMainDockHost", hostFlags);
        ImGui.PopStyleVar(3);

        uint dockspaceId = ImGui.GetID("Leed3DDockSpace");
        ImGui.DockSpace(dockspaceId, Vector2.Zero, ImGuiDockNodeFlags.PassthruCentralNode);
        ImGui.End();

        DrawViewportWindow(dt);
        DrawSceneSummaryPanel();
        DrawStatusBar(mainViewport, statusBarHeight);
    }

    private void DrawTopMenuBar()
    {
        if (!ImGui.BeginMainMenuBar())
        {
            return;
        }

        if (ImGui.BeginMenu("File"))
        {
            if (ImGui.MenuItem("New Level", "Ctrl+N"))
            {
                EditorScene before = Scene.CloneSnapshot();
                Scene.RestoreFromSnapshot(new EditorScene("Untitled Level"));
                UndoStack.RecordAction("New Level", before, Scene);
                PrimitiveController.Dismiss();
                _statusMessage = "Created new empty level.";
            }

            if (ImGui.MenuItem("Reset Starter Scene"))
            {
                EditorScene before = Scene.CloneSnapshot();
                Scene.RestoreFromSnapshot(new EditorScene("Untitled Level"));
                InitializeDefaultStarterScene();
                UndoStack.RecordAction("Reset Starter Scene", before, Scene);
                PrimitiveController.Dismiss();
                _statusMessage = "Reset starter blockout scene.";
            }

            ImGui.Separator();
            if (ImGui.MenuItem("Exit"))
            {
                Raylib.CloseWindow();
            }
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Edit"))
        {
            string undoLabel = UndoStack.CanUndo ? $"Undo {UndoStack.NextUndoDescription}" : "Undo";
            if (ImGui.MenuItem(undoLabel, "Ctrl+Z", false, UndoStack.CanUndo))
            {
                UndoStack.Undo(Scene);
                PrimitiveController.Dismiss();
            }

            string redoLabel = UndoStack.CanRedo ? $"Redo {UndoStack.NextRedoDescription}" : "Redo";
            if (ImGui.MenuItem(redoLabel, "Ctrl+Y", false, UndoStack.CanRedo))
            {
                UndoStack.Redo(Scene);
                PrimitiveController.Dismiss();
            }
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Add"))
        {
            DrawPrimitiveMenuItems();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("View"))
        {
            if (ImGui.MenuItem("Perspective", "Numpad 5 / V", Camera.ProjectionMode == ViewportProjectionMode.Perspective))
            {
                Camera.SetProjectionMode(ViewportProjectionMode.Perspective);
            }
            if (ImGui.MenuItem("Top (Ortho Y - XZ Plane)", "Numpad 7 / Alt+Y", Camera.ProjectionMode == ViewportProjectionMode.OrthoY))
            {
                Camera.SetProjectionMode(ViewportProjectionMode.OrthoY);
            }
            if (ImGui.MenuItem("Front (Ortho Z - XY Plane)", "Numpad 1 / Alt+Z", Camera.ProjectionMode == ViewportProjectionMode.OrthoZ))
            {
                Camera.SetProjectionMode(ViewportProjectionMode.OrthoZ);
            }
            if (ImGui.MenuItem("Right (Ortho X - YZ Plane)", "Numpad 3 / Alt+X", Camera.ProjectionMode == ViewportProjectionMode.OrthoX))
            {
                Camera.SetProjectionMode(ViewportProjectionMode.OrthoX);
            }

            ImGui.Separator();
            bool showGrid = _viewportRenderer.ShowGrid;
            if (ImGui.MenuItem("Show Grid", null, ref showGrid))
            {
                _viewportRenderer.ShowGrid = showGrid;
            }

            bool showAxes = _viewportRenderer.ShowOriginAxes;
            if (ImGui.MenuItem("Show Origin Axes", null, ref showAxes))
            {
                _viewportRenderer.ShowOriginAxes = showAxes;
            }

            ImGui.Separator();
            if (ImGui.MenuItem("Frame Origin", "Home / F"))
            {
                Camera.FrameTarget(Vector3.Zero, 5.0f);
            }

            ImGui.EndMenu();
        }

        ImGui.EndMainMenuBar();
    }

    private void DrawPrimitiveMenuItems()
    {
        if (ImGui.MenuItem("Plane"))
        {
            SpawnPrimitive(PrimitiveType.Plane);
        }
        if (ImGui.MenuItem("Box"))
        {
            SpawnPrimitive(PrimitiveType.Box);
        }
        if (ImGui.MenuItem("Cylinder"))
        {
            SpawnPrimitive(PrimitiveType.Cylinder);
        }
        if (ImGui.MenuItem("Cone"))
        {
            SpawnPrimitive(PrimitiveType.Cone);
        }
        if (ImGui.MenuItem("Geosphere"))
        {
            SpawnPrimitive(PrimitiveType.Geosphere);
        }
        if (ImGui.MenuItem("Icosahedron"))
        {
            SpawnPrimitive(PrimitiveType.Icosahedron);
        }
    }

    public void SpawnPrimitive(PrimitiveType type)
    {
        Vector3 spawnPos = Vector3.Zero;
        if (SnapToGridEnabled)
        {
            spawnPos = GridSnapper.SnapVector(spawnPos, CurrentGridStep);
        }

        SceneObject obj = PrimitiveController.SpawnPrimitive(type, spawnPos);
        _statusMessage = $"Spawned {obj.Name} at ({spawnPos.X:0.#}, {spawnPos.Y:0.#}, {spawnPos.Z:0.#}). Adjust parameters in the overlay panel.";
    }

    private void DrawViewportWindow(float dt)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4.0f, 4.0f));
        ImGui.Begin("3D Viewport", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleVar();

        DrawViewportHeaderToolbar();
        DrawPrimitiveToolbar();

        Vector2 avail = ImGui.GetContentRegionAvail();
        int targetW = System.Math.Max(64, (int)avail.X);
        int targetH = System.Math.Max(64, (int)avail.Y);
        _viewportRenderer.EnsureSize(targetW, targetH);

        _viewportScreenMin = ImGui.GetCursorScreenPos();
        RlImGui.ImageRenderTexture(_viewportRenderer.RenderTarget, new Vector2(targetW, targetH));

        _isViewportHovered = ImGui.IsItemHovered();
        _isViewportFocused = ImGui.IsWindowFocused();

        Vector2 mouseScreen = ImGui.GetMousePos();
        _viewportLocalMousePos = mouseScreen - _viewportScreenMin;

        // Shift+A Quick Add Menu in Viewport
        bool shiftDown = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
        if (_isViewportHovered && shiftDown && Raylib.IsKeyPressed(KeyboardKey.A))
        {
            ImGui.OpenPopup("ShiftAQuickAddPopup");
        }

        if (ImGui.BeginPopup("ShiftAQuickAddPopup"))
        {
            ImGui.TextDisabled("Add Mesh Primitive");
            ImGui.Separator();
            DrawPrimitiveMenuItems();
            ImGui.EndPopup();
        }

        DrawViewportAxisCompassOverlay(_viewportScreenMin, new Vector2(targetW, targetH));

        // Interactive "Adjust Last Operation" overlay popup in bottom-left corner of viewport
        if (PrimitiveController.IsActive)
        {
            DrawAdjustPrimitiveOverlay(_viewportScreenMin, new Vector2(targetW, targetH));
        }

        if (_isViewportHovered || Raylib.IsMouseButtonDown(MouseButton.Right) || Raylib.IsMouseButtonDown(MouseButton.Middle))
        {
            UpdateViewportCameraInput(dt, new Vector2(targetW, targetH));
        }

        ImGui.End();
    }

    private void DrawViewportHeaderToolbar()
    {
        // 1. Projection Mode Selector
        ImGui.TextDisabled("View:");
        ImGui.SameLine();
        if (ImGui.RadioButton("Persp", Camera.ProjectionMode == ViewportProjectionMode.Perspective))
        {
            Camera.SetProjectionMode(ViewportProjectionMode.Perspective);
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("Ortho X", Camera.ProjectionMode == ViewportProjectionMode.OrthoX))
        {
            Camera.SetProjectionMode(ViewportProjectionMode.OrthoX);
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("Ortho Y", Camera.ProjectionMode == ViewportProjectionMode.OrthoY))
        {
            Camera.SetProjectionMode(ViewportProjectionMode.OrthoY);
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("Ortho Z", Camera.ProjectionMode == ViewportProjectionMode.OrthoZ))
        {
            Camera.SetProjectionMode(ViewportProjectionMode.OrthoZ);
        }

        ImGui.SameLine();
        ImGui.TextDisabled("| Shading:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(145.0f);
        string currentShadingLabel = ShadingMode switch
        {
            ViewportShadingMode.SolidWithWireframe => "Solid + Wire",
            ViewportShadingMode.Solid => "Solid",
            ViewportShadingMode.Wireframe => "Wireframe",
            ViewportShadingMode.LitPreview => "Lit Preview",
            _ => "Solid + Wire"
        };

        if (ImGui.BeginCombo("##ShadingCombo", currentShadingLabel))
        {
            if (ImGui.Selectable("Solid + Wire", ShadingMode == ViewportShadingMode.SolidWithWireframe))
                ShadingMode = ViewportShadingMode.SolidWithWireframe;
            if (ImGui.Selectable("Solid", ShadingMode == ViewportShadingMode.Solid))
                ShadingMode = ViewportShadingMode.Solid;
            if (ImGui.Selectable("Wireframe", ShadingMode == ViewportShadingMode.Wireframe))
                ShadingMode = ViewportShadingMode.Wireframe;
            ImGui.EndCombo();
        }

        // 2. Grid Snap Selector
        ImGui.SameLine();
        ImGui.TextDisabled("|");
        ImGui.SameLine();
        bool snapGrid = SnapToGridEnabled;
        if (ImGui.Checkbox("Grid Snap", ref snapGrid))
        {
            SnapToGridEnabled = snapGrid;
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(75.0f);
        if (ImGui.BeginCombo("##GridStepCombo", $"{CurrentGridStep:0.###}"))
        {
            foreach (float step in GridSnapper.DefaultGridSteps)
            {
                bool selected = MathF.Abs(CurrentGridStep - step) < 1e-4f;
                if (ImGui.Selectable($"{step:0.###}", selected))
                {
                    CurrentGridStep = step;
                }
            }
            ImGui.EndCombo();
        }

        // 3. Angle Snap Selector
        ImGui.SameLine();
        bool snapAngle = SnapAngleEnabled;
        if (ImGui.Checkbox("Angle Snap", ref snapAngle))
        {
            SnapAngleEnabled = snapAngle;
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(65.0f);
        if (ImGui.BeginCombo("##AngleStepCombo", $"{CurrentAngleStepDegrees:0}°"))
        {
            foreach (float angleStep in GridSnapper.DefaultAngleSnapPresets)
            {
                bool selected = MathF.Abs(CurrentAngleStepDegrees - angleStep) < 1e-4f;
                if (ImGui.Selectable($"{angleStep:0}°", selected))
                {
                    CurrentAngleStepDegrees = angleStep;
                }
            }
            ImGui.EndCombo();
        }

        ImGui.Separator();
    }

    private void DrawPrimitiveToolbar()
    {
        ImGui.TextDisabled("Add Primitive:");
        ImGui.SameLine();

        if (ImGui.Button("Plane")) SpawnPrimitive(PrimitiveType.Plane);
        ImGui.SameLine();
        if (ImGui.Button("Box")) SpawnPrimitive(PrimitiveType.Box);
        ImGui.SameLine();
        if (ImGui.Button("Cylinder")) SpawnPrimitive(PrimitiveType.Cylinder);
        ImGui.SameLine();
        if (ImGui.Button("Cone")) SpawnPrimitive(PrimitiveType.Cone);
        ImGui.SameLine();
        if (ImGui.Button("Geosphere")) SpawnPrimitive(PrimitiveType.Geosphere);
        ImGui.SameLine();
        if (ImGui.Button("Icosahedron")) SpawnPrimitive(PrimitiveType.Icosahedron);

        ImGui.Separator();
    }

    private void DrawAdjustPrimitiveOverlay(Vector2 viewportMin, Vector2 viewportSize)
    {
        const float overlayWidth = 280.0f;
        const float margin = 12.0f;

        Vector2 overlayPos = new(viewportMin.X + margin, viewportMin.Y + viewportSize.Y - 240.0f);
        ImGui.SetNextWindowPos(overlayPos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(overlayWidth, 0.0f));

        ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoSavedSettings;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.12f, 0.14f, 0.17f, 0.95f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1.0f);

        string title = $"Adjust {PrimitiveController.ActivePrimitiveType}###AdjustLastPrimitivePopup";
        bool open = true;

        if (ImGui.Begin(title, ref open, flags))
        {
            bool changed = false;

            switch (PrimitiveController.ActivePrimitiveType)
            {
                case PrimitiveType.Plane:
                {
                    PlaneParams p = PrimitiveController.PlaneParams;
                    float w = p.Width;
                    float d = p.Depth;
                    int segW = p.WidthSegments;
                    int segD = p.DepthSegments;

                    if (ImGui.DragFloat("Width", ref w, 0.1f, 0.1f, 100.0f)) { p = p with { Width = w }; changed = true; }
                    if (ImGui.DragFloat("Depth", ref d, 0.1f, 0.1f, 100.0f)) { p = p with { Depth = d }; changed = true; }
                    if (ImGui.SliderInt("Width Segs", ref segW, 1, 32)) { p = p with { WidthSegments = segW }; changed = true; }
                    if (ImGui.SliderInt("Depth Segs", ref segD, 1, 32)) { p = p with { DepthSegments = segD }; changed = true; }

                    if (changed) PrimitiveController.PlaneParams = p;
                    break;
                }

                case PrimitiveType.Box:
                {
                    BoxParams p = PrimitiveController.BoxParams;
                    Vector3 size = p.Size;
                    int segX = p.SegmentsX;
                    int segY = p.SegmentsY;
                    int segZ = p.SegmentsZ;
                    bool inv = p.InvertNormals;

                    if (ImGui.DragFloat3("Size", ref size, 0.1f, 0.1f, 100.0f)) { p = p with { Size = size }; changed = true; }
                    if (ImGui.SliderInt("Segments X", ref segX, 1, 32)) { p = p with { SegmentsX = segX }; changed = true; }
                    if (ImGui.SliderInt("Segments Y", ref segY, 1, 32)) { p = p with { SegmentsY = segY }; changed = true; }
                    if (ImGui.SliderInt("Segments Z", ref segZ, 1, 32)) { p = p with { SegmentsZ = segZ }; changed = true; }
                    if (ImGui.Checkbox("Invert Normals (Room)", ref inv)) { p = p with { InvertNormals = inv }; changed = true; }

                    if (changed) PrimitiveController.BoxParams = p;
                    break;
                }

                case PrimitiveType.Cylinder:
                {
                    CylinderParams p = PrimitiveController.CylinderParams;
                    float radius = p.Radius;
                    float height = p.Height;
                    int radSeg = p.RadialSegments;
                    int hSeg = p.HeightSegments;
                    bool capTop = p.CapTop;
                    bool capBottom = p.CapBottom;

                    if (ImGui.DragFloat("Radius", ref radius, 0.05f, 0.1f, 50.0f)) { p = p with { Radius = radius }; changed = true; }
                    if (ImGui.DragFloat("Height", ref height, 0.1f, 0.1f, 100.0f)) { p = p with { Height = height }; changed = true; }
                    if (ImGui.SliderInt("Radial Segs", ref radSeg, 3, 64)) { p = p with { RadialSegments = radSeg }; changed = true; }
                    if (ImGui.SliderInt("Height Segs", ref hSeg, 1, 16)) { p = p with { HeightSegments = hSeg }; changed = true; }
                    if (ImGui.Checkbox("Cap Top", ref capTop)) { p = p with { CapTop = capTop }; changed = true; }
                    ImGui.SameLine();
                    if (ImGui.Checkbox("Cap Bottom", ref capBottom)) { p = p with { CapBottom = capBottom }; changed = true; }

                    if (changed) PrimitiveController.CylinderParams = p;
                    break;
                }

                case PrimitiveType.Cone:
                {
                    ConeParams p = PrimitiveController.ConeParams;
                    float radius = p.Radius;
                    float height = p.Height;
                    int radSeg = p.RadialSegments;
                    bool capBottom = p.CapBottom;

                    if (ImGui.DragFloat("Radius", ref radius, 0.05f, 0.1f, 50.0f)) { p = p with { Radius = radius }; changed = true; }
                    if (ImGui.DragFloat("Height", ref height, 0.1f, 0.1f, 100.0f)) { p = p with { Height = height }; changed = true; }
                    if (ImGui.SliderInt("Radial Segs", ref radSeg, 3, 64)) { p = p with { RadialSegments = radSeg }; changed = true; }
                    if (ImGui.Checkbox("Cap Bottom", ref capBottom)) { p = p with { CapBottom = capBottom }; changed = true; }

                    if (changed) PrimitiveController.ConeParams = p;
                    break;
                }

                case PrimitiveType.Icosahedron:
                {
                    IcosahedronParams p = PrimitiveController.IcosahedronParams;
                    float radius = p.Radius;

                    if (ImGui.DragFloat("Radius", ref radius, 0.05f, 0.1f, 50.0f)) { p = p with { Radius = radius }; changed = true; }

                    if (changed) PrimitiveController.IcosahedronParams = p;
                    break;
                }

                case PrimitiveType.Geosphere:
                {
                    GeosphereParams p = PrimitiveController.GeosphereParams;
                    float radius = p.Radius;
                    int subs = p.Subdivisions;

                    if (ImGui.DragFloat("Radius", ref radius, 0.05f, 0.1f, 50.0f)) { p = p with { Radius = radius }; changed = true; }
                    if (ImGui.SliderInt("Subdivisions", ref subs, 0, 4)) { p = p with { Subdivisions = subs }; changed = true; }

                    if (changed) PrimitiveController.GeosphereParams = p;
                    break;
                }
            }

            if (changed)
            {
                PrimitiveController.RegenerateActiveMesh();
            }

            ImGui.Separator();
            if (ImGui.Button("Done", new Vector2(-1.0f, 0.0f)))
            {
                open = false;
            }

            ImGui.End();
        }

        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor();

        if (!open)
        {
            PrimitiveController.Dismiss();
        }
    }

    private void UpdateViewportCameraInput(float dt, Vector2 viewportSize)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        Vector2 mouseDelta = Raylib.GetMouseDelta();

        bool rmbDown = Raylib.IsMouseButtonDown(MouseButton.Right);
        bool mmbDown = Raylib.IsMouseButtonDown(MouseButton.Middle);
        bool lmbDown = Raylib.IsMouseButtonDown(MouseButton.Left);
        bool altDown = Raylib.IsKeyDown(KeyboardKey.LeftAlt) || Raylib.IsKeyDown(KeyboardKey.RightAlt);
        bool shiftDown = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
        bool ctrlDown = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);

        // 1. Free-Fly Mode (Right Mouse Held in Viewport)
        if (rmbDown)
        {
            Vector3 moveInput = Vector3.Zero;
            if (Raylib.IsKeyDown(KeyboardKey.D)) moveInput.X += 1.0f;
            if (Raylib.IsKeyDown(KeyboardKey.A)) moveInput.X -= 1.0f;
            if (Raylib.IsKeyDown(KeyboardKey.E) || Raylib.IsKeyDown(KeyboardKey.Space)) moveInput.Y += 1.0f;
            if (Raylib.IsKeyDown(KeyboardKey.Q) || Raylib.IsKeyDown(KeyboardKey.LeftControl)) moveInput.Y -= 1.0f;
            if (Raylib.IsKeyDown(KeyboardKey.W)) moveInput.Z += 1.0f;
            if (Raylib.IsKeyDown(KeyboardKey.S)) moveInput.Z -= 1.0f;

            if (moveInput.LengthSquared() > 1.0f)
            {
                moveInput = Vector3.Normalize(moveInput);
            }

            Camera.UpdateFreeFly(moveInput, mouseDelta, dt, shiftDown);
            return;
        }

        // 2. Orbit (Alt + Left Mouse Drag) or Middle Mouse Drag in Perspective without Shift
        if (altDown && lmbDown && mouseDelta.LengthSquared() > 0.0f)
        {
            Camera.Orbit(mouseDelta);
            return;
        }

        // 3. Pan (Middle Mouse Drag in Ortho, or Shift + Middle Mouse Drag, or Middle Mouse Drag)
        if (mmbDown && mouseDelta.LengthSquared() > 0.0f)
        {
            if (Camera.ProjectionMode == ViewportProjectionMode.Perspective && !shiftDown && ctrlDown)
            {
                Camera.Orbit(mouseDelta);
            }
            else
            {
                Camera.Pan(mouseDelta, viewportSize);
            }
            return;
        }

        // 4. Scroll Wheel Zoom (when hovering viewport)
        if (_isViewportHovered)
        {
            float wheel = Raylib.GetMouseWheelMove();
            if (MathF.Abs(wheel) > 1e-4f)
            {
                Camera.Zoom(wheel);
            }
        }

        // 5. Camera View Hotkeys when not typing text
        if (!io.WantTextInput && (_isViewportHovered || _isViewportFocused))
        {
            if (Raylib.IsKeyPressed(KeyboardKey.Kp5) || (!ctrlDown && !altDown && Raylib.IsKeyPressed(KeyboardKey.V)))
            {
                Camera.TogglePerspectiveOrtho();
            }
            else if (Raylib.IsKeyPressed(KeyboardKey.Kp1) || (altDown && Raylib.IsKeyPressed(KeyboardKey.Z)))
            {
                Camera.SetProjectionMode(ViewportProjectionMode.OrthoZ, invertAxis: ctrlDown);
            }
            else if (Raylib.IsKeyPressed(KeyboardKey.Kp3) || (altDown && Raylib.IsKeyPressed(KeyboardKey.X)))
            {
                Camera.SetProjectionMode(ViewportProjectionMode.OrthoX, invertAxis: ctrlDown);
            }
            else if (Raylib.IsKeyPressed(KeyboardKey.Kp7) || (altDown && Raylib.IsKeyPressed(KeyboardKey.Y)))
            {
                Camera.SetProjectionMode(ViewportProjectionMode.OrthoY, invertAxis: ctrlDown);
            }
            else if (Raylib.IsKeyPressed(KeyboardKey.Home) || (!ctrlDown && !altDown && Raylib.IsKeyPressed(KeyboardKey.F)))
            {
                Camera.FrameTarget(Vector3.Zero, 5.0f);
            }
        }
    }

    /// <summary>
    /// Draws a 3D orientation axis compass in the top-right corner of the viewport showing +X (Right), +Y (Up), +Z (Away).
    /// </summary>
    private void DrawViewportAxisCompassOverlay(Vector2 viewportMin, Vector2 viewportSize)
    {
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        Vector2 center = new(viewportMin.X + viewportSize.X - 52.0f, viewportMin.Y + 52.0f);
        const float axisLen = 32.0f;

        Vector3 right = Camera.GetRight();
        Vector3 up = Camera.GetUp();

        void DrawAxisIndicator(Vector3 worldAxis, uint color, string label)
        {
            float sx = Vector3.Dot(worldAxis, right) * axisLen;
            float sy = -Vector3.Dot(worldAxis, up) * axisLen;
            Vector2 endPos = center + new Vector2(sx, sy);

            drawList.AddLine(center, endPos, color, 2.2f);
            drawList.AddCircleFilled(endPos, 4.5f, color);
            drawList.AddText(endPos + new Vector2(5.0f, -6.0f), color, label);
        }

        DrawAxisIndicator(Vector3.UnitX, ImGui.GetColorU32(new Vector4(0.95f, 0.30f, 0.30f, 1.0f)), "+X");
        DrawAxisIndicator(Vector3.UnitY, ImGui.GetColorU32(new Vector4(0.35f, 0.88f, 0.35f, 1.0f)), "+Y");
        DrawAxisIndicator(Vector3.UnitZ, ImGui.GetColorU32(new Vector4(0.32f, 0.58f, 0.98f, 1.0f)), "+Z");
    }

    private void DrawSceneSummaryPanel()
    {
        ImGui.Begin("Scene Outliner");
        ImGui.Text($"Scene: {Scene.Name}");
        ImGui.TextDisabled($"Objects: {Scene.ObjectCount}");
        ImGui.Separator();

        for (int i = 0; i < Scene.Objects.Count; i++)
        {
            SceneObject obj = Scene.Objects[i];
            int vertCount = obj.Mesh?.VertexCount ?? 0;
            int faceCount = obj.Mesh?.FaceCount ?? 0;
            bool isActiveCreation = PrimitiveController.ActiveObject?.Id == obj.Id;
            if (isActiveCreation)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.85f, 0.3f, 1.0f));
            }
            ImGui.BulletText($"{obj.Name} (V:{vertCount}, F:{faceCount}){(isActiveCreation ? " [Active]" : "")}");
            if (isActiveCreation)
            {
                ImGui.PopStyleColor();
            }
        }

        ImGui.End();
    }

    private void DrawStatusBar(ImGuiViewportPtr mainViewport, float height)
    {
        Vector2 pos = new(mainViewport.WorkPos.X, mainViewport.WorkPos.Y + mainViewport.WorkSize.Y - height);
        Vector2 size = new(mainViewport.WorkSize.X, height);

        ImGui.SetNextWindowPos(pos);
        ImGui.SetNextWindowSize(size);
        ImGui.SetNextWindowViewport(mainViewport.ID);

        ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoDocking;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8.0f, 4.0f));
        ImGui.Begin("##StatusBar", flags);
        ImGui.PopStyleVar(2);

        Vector3 camPos = Camera.Position;
        ImGui.Text($"{_statusMessage}  |  Cam: ({camPos.X:0.0}, {camPos.Y:0.0}, {camPos.Z:0.0})  |  View: {Camera.ProjectionMode}  |  Objects: {Scene.ObjectCount}  |  FPS: {Raylib.GetFPS()}");

        ImGui.End();
    }

    public void Dispose()
    {
        _viewportRenderer.Dispose();
        RlImGui.Shutdown();
        if (Raylib.IsWindowReady())
        {
            Raylib.CloseWindow();
        }
    }
}
