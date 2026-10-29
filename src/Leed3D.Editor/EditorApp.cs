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
/// 3D viewport camera navigation, and status bar.
/// </summary>
public sealed class EditorApp : IDisposable
{
    private readonly ViewportRenderer _viewportRenderer = new();

    public EditorScene Scene { get; } = new("Untitled Level");
    public UndoRedoStack UndoStack { get; } = new();
    public EditorCamera Camera { get; } = new();

    public ViewportShadingMode ShadingMode { get; set; } = ViewportShadingMode.SolidWithWireframe;
    public bool SnapToGridEnabled { get; set; } = true;
    public float CurrentGridStep { get; set; } = 1.0f;
    public bool SnapAngleEnabled { get; set; } = true;
    public float CurrentAngleStepDegrees { get; set; } = 15.0f;

    private bool _isViewportHovered;
    private bool _isViewportFocused;
    private Vector2 _viewportScreenMin;
    private Vector2 _viewportLocalMousePos;
    private string _statusMessage = "Ready — Hold RMB + WASD to fly, MMB to pan, Alt+LMB to orbit, Scroll to zoom.";

    public EditorApp()
    {
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
                _statusMessage = "Undid last action.";
            }
        }
        else if ((ctrl && Raylib.IsKeyPressed(KeyboardKey.Y)) ||
                 (ctrl && shift && Raylib.IsKeyPressed(KeyboardKey.Z)))
        {
            if (UndoStack.Redo(Scene))
            {
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
                _statusMessage = "Created new empty level.";
            }

            if (ImGui.MenuItem("Reset Starter Scene"))
            {
                EditorScene before = Scene.CloneSnapshot();
                Scene.RestoreFromSnapshot(new EditorScene("Untitled Level"));
                InitializeDefaultStarterScene();
                UndoStack.RecordAction("Reset Starter Scene", before, Scene);
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
            }

            string redoLabel = UndoStack.CanRedo ? $"Redo {UndoStack.NextRedoDescription}" : "Redo";
            if (ImGui.MenuItem(redoLabel, "Ctrl+Y", false, UndoStack.CanRedo))
            {
                UndoStack.Redo(Scene);
            }
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

    private void DrawViewportWindow(float dt)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4.0f, 4.0f));
        ImGui.Begin("3D Viewport", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleVar();

        DrawViewportHeaderToolbar();

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

        DrawViewportAxisCompassOverlay(_viewportScreenMin, new Vector2(targetW, targetH));

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
            ImGui.BulletText($"{obj.Name} (V:{vertCount}, F:{faceCount})");
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
        ImGui.Text($"{_statusMessage}  |  Cam (+X Right, +Y Up, +Z Away): ({camPos.X:0.0}, {camPos.Y:0.0}, {camPos.Z:0.0})  |  View: {Camera.ProjectionMode}  |  FPS: {Raylib.GetFPS()}");

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
