using System.Numerics;
using ImGuiNET;
using Leed3D.Editor.Texturing;
using Leed3D.Editor.UI;
using Leed3D.Editor.UI.Panels;
using Leed3D.Editor.Viewport;
using Leed3D.Kernel.Csg;
using Leed3D.Kernel.Entities;
using Leed3D.Kernel.Export;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Gizmos;
using Leed3D.Kernel.IO;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Modifiers;
using Leed3D.Kernel.Physics;
using Leed3D.Kernel.Primitives;
using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Selection;
using Leed3D.Kernel.Texturing;
using Leed3D.Kernel.Tools;
using Leed3D.Kernel.Viewport;
using Leed3D.Runtime.Metadata;
using Raylib_cs;
using Color = Raylib_cs.Color;
using RectangleF = System.Drawing.RectangleF;

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
    public InteractiveEditController EditController { get; } = new();
    public EditorShortcutHandler ShortcutHandler { get; }
    public SelectionManager Selection { get; } = new();
    public TransformGizmo3D Gizmo { get; } = new();
    public TransformApplicator Applicator { get; } = new();
    public ModalTransformController ModalController { get; } = new();
    public MaterialLibrary MaterialLibrary { get; } = new();
    public EditorTextureCache TextureCache { get; }
    public string ActiveMaterialId { get; set; } = "dev_grid_gray";

    public TextureBrowserPanel TextureBrowser { get; } = new();
    public UvInspectorPanel UvInspector { get; } = new();
    public MetadataInspectorPanel MetadataInspector { get; } = new();
    public SceneOutlinerPanel Outliner { get; } = new();
    public EntitySchemaRegistry EntityRegistry { get; } = new();
    public EnvironmentLightingSettings EnvironmentSettings { get; } = new();
    public WalkPlayerConfig WalkConfig { get; } = new();
    public WalkController? ActiveWalkController { get; private set; }
    public bool IsWalkModeActive => ActiveWalkController is not null;

    public string? CurrentFilePath { get; set; }
    private int _savedUndoCount = 0;
    public bool HasUnsavedChanges => UndoStack.UndoCount != _savedUndoCount;

    private bool _openNewLevelModal;
    private bool _openOpenLevelModal;
    private bool _openSaveLevelAsModal;
    private bool _openExportObjModal;
    private bool _openExportGltfModal;

    private string _openLevelPath = "samples/temple_blockout.level.json";
    private string _saveAsLevelPath = "my_level.level.json";
    private string _exportObjPath = "export/my_level.obj";
    private bool _exportObjRightHanded = true;
    private string _exportGltfPath = "export/my_level.gltf";
    private bool _exportGltfEmbedBase64 = true;

    private Vector3 _savedWalkCamPosition;
    private Vector3 _savedWalkCamTarget;
    private float _savedWalkCamYaw;
    private float _savedWalkCamPitch;
    private float _savedWalkCamOrbitDist;
    private ViewportProjectionMode _savedWalkCamProjection;
    private bool _savedWalkCamInvertOrtho;
    private ViewportShadingMode _savedWalkShadingMode;

    public ViewportShadingMode ShadingMode { get; set; } = ViewportShadingMode.SolidWithWireframe;
    private ViewportShadingMode _previousShadingMode = ViewportShadingMode.SolidWithWireframe;

    public void ToggleLitPreview()
    {
        if (ShadingMode == ViewportShadingMode.LitPreview)
        {
            ShadingMode = _previousShadingMode == ViewportShadingMode.LitPreview
                ? ViewportShadingMode.SolidWithWireframe
                : _previousShadingMode;
            StatusMessage = $"Lit Preview disabled ({ShadingMode}).";
        }
        else
        {
            _previousShadingMode = ShadingMode;
            ShadingMode = ViewportShadingMode.LitPreview;
            StatusMessage = "Lit Preview enabled.";
        }
    }

    public void ToggleWalkMode()
    {
        if (IsWalkModeActive)
        {
            ExitWalkMode();
        }
        else
        {
            EnterWalkMode();
        }
    }

    public void EnterWalkMode()
    {
        if (IsWalkModeActive)
        {
            return;
        }

        if (EditController.IsActive)
        {
            if (EditController.IsExtrudeActive)
            {
                EditController.CancelExtrude(Scene, UndoStack);
            }
            else if (EditController.IsInsetActive)
            {
                EditController.CancelInset(Scene, UndoStack);
            }
        }
        if (ModalController.IsActive)
        {
            ModalController.Cancel(Scene, UndoStack);
        }
        PrimitiveController.Dismiss();

        _savedWalkCamPosition = Camera.Position;
        _savedWalkCamTarget = Camera.Target;
        _savedWalkCamYaw = Camera.YawDegrees;
        _savedWalkCamPitch = Camera.PitchDegrees;
        _savedWalkCamOrbitDist = Camera.OrbitDistance;
        _savedWalkCamProjection = Camera.ProjectionMode;
        _savedWalkCamInvertOrtho = Camera.InvertOrthoAxis;
        _savedWalkShadingMode = ShadingMode;

        LevelEntity? spawnEnt = Scene.Entities.FirstOrDefault(e =>
            e.Visible && e.ClassName.Equals("player_spawn", StringComparison.OrdinalIgnoreCase));

        Vector3 spawnPos;
        float spawnYaw;
        float spawnPitch;

        if (spawnEnt is not null)
        {
            spawnPos = spawnEnt.Transform.Position;
            spawnYaw = spawnEnt.Properties.GetFloat("yaw") ?? spawnEnt.Transform.RotationEulerDegrees.Y;
            spawnPitch = 0.0f;
        }
        else
        {
            spawnPos = Camera.Position - new Vector3(0.0f, WalkConfig.EyeHeight, 0.0f);
            spawnYaw = Camera.YawDegrees;
            spawnPitch = Camera.PitchDegrees;
        }

        CollisionMeshWorld collisionWorld = CollisionMeshWorld.Build(Scene, EntityRegistry);
        ActiveWalkController = new WalkController(collisionWorld, WalkConfig);
        ActiveWalkController.Spawn(spawnPos, spawnYaw, spawnPitch);

        ShadingMode = ViewportShadingMode.LitPreview;
        Camera.SetProjectionMode(ViewportProjectionMode.Perspective);
        SyncCameraToWalkController();

        if (Raylib.IsWindowReady())
        {
            Raylib.DisableCursor();
        }

        StatusMessage = "Walk Playtest Mode active (WASD Move, Shift Sprint, Space Jump, F5/Esc Exit).";
    }

    public void ExitWalkMode()
    {
        if (!IsWalkModeActive)
        {
            return;
        }

        ActiveWalkController = null;

        if (Raylib.IsWindowReady())
        {
            Raylib.EnableCursor();
        }

        ShadingMode = _savedWalkShadingMode;
        Camera.YawDegrees = _savedWalkCamYaw;
        Camera.PitchDegrees = _savedWalkCamPitch;
        Camera.OrbitDistance = _savedWalkCamOrbitDist;
        Camera.Target = _savedWalkCamTarget;
        Camera.SetProjectionMode(_savedWalkCamProjection, _savedWalkCamInvertOrtho);
        Camera.Position = _savedWalkCamPosition;
        Camera.Target = _savedWalkCamTarget;

        StatusMessage = "Exited Walk Playtest Mode — Restored editor camera.";
    }

    private void UpdateWalkMode(float dt)
    {
        if (ActiveWalkController is null)
        {
            return;
        }

        Vector2 mouseDelta = Raylib.GetMouseDelta();
        float newYaw = ActiveWalkController.YawDegrees + (mouseDelta.X * Camera.MouseLookSensitivity);
        float newPitch = System.Math.Clamp(
            ActiveWalkController.PitchDegrees - (mouseDelta.Y * Camera.MouseLookSensitivity),
            -89.0f,
            89.0f);

        float moveX = 0.0f;
        float moveY = 0.0f;
        if (Raylib.IsKeyDown(KeyboardKey.D) || Raylib.IsKeyDown(KeyboardKey.Right)) moveX += 1.0f;
        if (Raylib.IsKeyDown(KeyboardKey.A) || Raylib.IsKeyDown(KeyboardKey.Left)) moveX -= 1.0f;
        if (Raylib.IsKeyDown(KeyboardKey.W) || Raylib.IsKeyDown(KeyboardKey.Up)) moveY += 1.0f;
        if (Raylib.IsKeyDown(KeyboardKey.S) || Raylib.IsKeyDown(KeyboardKey.Down)) moveY -= 1.0f;

        bool sprintHeld = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
        bool jumpPressed = Raylib.IsKeyPressed(KeyboardKey.Space) || Raylib.IsKeyDown(KeyboardKey.Space);

        ActiveWalkController.Step(dt, new Vector2(moveX, moveY), newYaw, newPitch, jumpPressed, sprintHeld);
        SyncCameraToWalkController();
    }

    private void SyncCameraToWalkController()
    {
        if (ActiveWalkController is null)
        {
            return;
        }

        Camera.YawDegrees = ActiveWalkController.YawDegrees;
        Camera.PitchDegrees = ActiveWalkController.PitchDegrees;
        Camera.Position = ActiveWalkController.EyePosition;
        Camera.Target = Camera.Position + Camera.GetForward();
    }

    public bool SnapToGridEnabled { get; set; } = true;
    public float CurrentGridStep { get; set; } = 1.0f;
    public bool SnapAngleEnabled { get; set; } = true;
    public float CurrentAngleStepDegrees { get; set; } = 15.0f;

    public string StatusMessage { get => _statusMessage; set => _statusMessage = value; }

    private bool _isViewportHovered;
    private bool _isViewportFocused;
    private Vector2 _viewportScreenMin;
    private Vector2 _viewportLocalMousePos;

    private bool _isMouseDownInViewport;
    private Vector2 _mouseDownLocalPos;
    private Vector2 _mouseDownScreenPos;
    private bool _isDraggingMarquee;
    private Vector2 _mouseCurrentLocalPos;
    private Vector2 _mouseCurrentScreenPos;

    private string _statusMessage = "Ready — Click primitive buttons to add, Shift+A for quick-add menu, RMB+WASD to fly.";

    // Subdivide, Weld, Mirror & Array parameters
    private int _subdivideCuts = 1;
    private float _weldThreshold = 0.01f;
    private Axis3D _mirrorAxis = Axis3D.X;
    private float _mirrorPlaneCoord;
    private bool _mirrorKeepOriginal = true;
    private float _mirrorWeldThreshold = 0.001f;

    private int _linearArrayCount = 3;
    private Vector3 _linearArrayOffset = new(2.0f, 0.0f, 0.0f);

    private int _radialArrayCount = 6;
    private Axis3D _radialArrayAxis = Axis3D.Y;
    private Vector3 _radialArrayPivot = Vector3.Zero;
    private float _radialArrayAngle = 360.0f;

    // CSG Boolean parameters & preview state
    public bool CsgDeleteCutter { get; set; } = true;
    public int? CsgPreviewTargetId { get; set; }
    public int? CsgPreviewCutterId { get; set; }
    public Color? CsgCutterWireColor { get; set; }

    public EditorApp()
    {
        TextureCache = new EditorTextureCache(MaterialLibrary);
        PrimitiveController = new PrimitiveCreationController(Scene, UndoStack);
        ShortcutHandler = new EditorShortcutHandler(this);
        if (File.Exists("entities.schema.json"))
        {
            try
            {
                EntityRegistry.LoadFromFile("entities.schema.json");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Entities] Failed to load entities.schema.json: {ex.Message}");
            }
        }
        InitializeDefaultStarterScene();
    }

    private void InitializeDefaultStarterScene()
    {
        var floorMesh = PrimitiveGenerator.CreateBox(new Vector3(8.0f, 0.5f, 8.0f), materialId: "dev_grid_gray");
        Scene.AddMeshObject("Floor Block", floorMesh, new Vector3(0.0f, -0.25f, 0.0f));

        var boxMesh = PrimitiveGenerator.CreateBox(new Vector3(2.0f, 2.0f, 2.0f), materialId: "dev_grid_orange");
        SceneObject cubeObj = Scene.AddMeshObject("Starter Cube", boxMesh, new Vector3(0.0f, 1.0f, 0.0f));
        Selection.SelectSingleObject(cubeObj.Id);
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
            if (IsWalkModeActive)
            {
                UpdateWalkMode(dt);
            }

            // Render 3D scene into offscreen RenderTexture2D before drawing ImGui
            _viewportRenderer.RenderScene(
                Scene,
                Camera,
                ShadingMode,
                CurrentGridStep,
                Selection,
                Gizmo,
                ModalController,
                CsgPreviewTargetId,
                CsgPreviewCutterId,
                CsgCutterWireColor,
                TextureCache,
                EntityRegistry,
                EnvironmentSettings);

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
        ShortcutHandler.HandleShortcuts(_isViewportHovered, _isViewportFocused, _viewportLocalMousePos, _viewportRenderer.ViewportSize);
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
        Outliner.Draw(this);
        DrawCsgPanel();
        TextureBrowser.Draw(this);
        UvInspector.Draw(this);
        MetadataInspector.Draw(this);
        DrawFileModals();
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
                PromptNewLevel();
            }

            if (ImGui.MenuItem("Open Level...", "Ctrl+O"))
            {
                PromptOpenLevel();
            }

            if (ImGui.MenuItem("Save Level", "Ctrl+S"))
            {
                SaveLevel();
            }

            if (ImGui.MenuItem("Save Level As...", "Ctrl+Shift+S"))
            {
                PromptSaveLevelAs();
            }

            ImGui.Separator();

            if (ImGui.MenuItem("Export Wavefront .obj..."))
            {
                PromptExportObj();
            }

            if (ImGui.MenuItem("Export glTF 2.0 (.gltf)..."))
            {
                PromptExportGltf();
            }

            ImGui.Separator();

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

            ImGui.Separator();
            if (ImGui.MenuItem("CSG Union", "Ctrl+=", false, Selection.Mode == InteractionMode.Object && Selection.SelectedObjectIds.Count >= 2))
            {
                ExecuteCsgUnion();
            }
            if (ImGui.MenuItem("CSG Subtract / Carve", "Ctrl+-", false, Selection.Mode == InteractionMode.Object && Selection.SelectedObjectIds.Count >= 2))
            {
                ExecuteCsgSubtract();
            }
            if (ImGui.MenuItem("CSG Intersect", "Ctrl+8", false, Selection.Mode == InteractionMode.Object && Selection.SelectedObjectIds.Count >= 2))
            {
                ExecuteCsgIntersect();
            }
            if (ImGui.MenuItem("TrenchBroom Carve Overlapping", "Ctrl+Shift+C", false, Selection.Mode == InteractionMode.Object && Selection.HasSelection))
            {
                ExecuteCsgCarve();
            }

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Add"))
        {
            if (ImGui.BeginMenu("Mesh"))
            {
                DrawPrimitiveMenuItems();
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Entity"))
            {
                DrawEntityMenuItems();
                ImGui.EndMenu();
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

            ImGui.Separator();
            if (ImGui.MenuItem("Lit Preview", "F4", ShadingMode == ViewportShadingMode.LitPreview))
            {
                ToggleLitPreview();
            }
            if (ImGui.MenuItem("Walk Playtest Mode", "F5", IsWalkModeActive))
            {
                ToggleWalkMode();
            }
            if (ImGui.MenuItem("Environment & Fog Settings..."))
            {
                ImGui.OpenPopup("EnvironmentLightingPopup");
            }

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Window"))
        {
            ImGui.MenuItem("Scene Outliner", null, true);
            ImGui.MenuItem("Transform Inspector", null, true);
            ImGui.MenuItem("Texture Browser", null, true);
            ImGui.MenuItem("UV Inspector", null, true);
            ImGui.MenuItem("Metadata Inspector", null, true);
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Help"))
        {
            if (ImGui.MenuItem("Load Sample Temple Blockout Level"))
            {
                LoadSampleTempleBlockout();
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
        Selection.SelectSingleObject(obj.Id);
        _statusMessage = $"Spawned {obj.Name} at ({spawnPos.X:0.#}, {spawnPos.Y:0.#}, {spawnPos.Z:0.#}). Adjust parameters in the overlay panel.";
    }

    private void DrawEntityMenuItems()
    {
        if (ImGui.MenuItem("Player Spawn"))
        {
            SpawnEntity("player_spawn");
        }
        if (ImGui.MenuItem("Light (Point)"))
        {
            SpawnEntity("light", ("light_type", "point"));
        }
        if (ImGui.MenuItem("Light (Spot)"))
        {
            SpawnEntity("light", ("light_type", "spot"));
        }
        if (ImGui.MenuItem("Portal"))
        {
            SpawnEntity("portal");
        }
        if (ImGui.MenuItem("Trigger Volume"))
        {
            SpawnEntity("trigger");
        }

        bool hasCustom = false;
        foreach (var def in EntityRegistry.Definitions)
        {
            if (def.ClassName.Equals("player_spawn", StringComparison.OrdinalIgnoreCase) ||
                def.ClassName.Equals("light", StringComparison.OrdinalIgnoreCase) ||
                def.ClassName.Equals("portal", StringComparison.OrdinalIgnoreCase) ||
                def.ClassName.Equals("trigger", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!hasCustom)
            {
                ImGui.Separator();
                ImGui.TextDisabled("Custom Entities:");
                hasCustom = true;
            }

            if (ImGui.MenuItem(def.DisplayName))
            {
                SpawnEntity(def.ClassName);
            }
        }
    }

    public LevelEntity SpawnEntity(string className, params (string Key, object Value)[]? propertyOverrides)
    {
        Vector3 spawnPos = Camera.Target;
        if (SnapToGridEnabled)
        {
            spawnPos = GridSnapper.SnapVector(spawnPos, CurrentGridStep);
        }

        EditorScene before = Scene.CloneSnapshot();
        LevelEntity entity = Scene.CreateEntity(className, position: spawnPos, registry: EntityRegistry);

        if (propertyOverrides is not null)
        {
            foreach (var (k, v) in propertyOverrides)
            {
                if (v is float f) entity.Properties.SetFloat(k, f);
                else if (v is int i) entity.Properties.SetInt(k, i);
                else if (v is bool b) entity.Properties.SetBool(k, b);
                else if (v is string s) entity.Properties.SetString(k, s);
                else if (v is ColorRgba c) entity.Properties.SetColor(k, c);
                else if (v is Vector3 vec) entity.Properties.SetVec3(k, vec);
            }
        }

        UndoStack.RecordAction($"Spawn Entity '{entity.Id}'", before, Scene);
        Selection.SelectSingleEntity(entity.Id);
        _statusMessage = $"Spawned Entity '{entity.Id}' ({className}) at ({spawnPos.X:0.#}, {spawnPos.Y:0.#}, {spawnPos.Z:0.#}).";
        return entity;
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
            ImGui.Spacing();
            ImGui.TextDisabled("Add Gameplay Entity");
            ImGui.Separator();
            DrawEntityMenuItems();
            ImGui.EndPopup();
        }

        DrawViewportAxisCompassOverlay(_viewportScreenMin, new Vector2(targetW, targetH));
        DrawViewportHudOverlay(_viewportScreenMin, new Vector2(targetW, targetH));

        // Vertex selection overlay (dots / squares) in Vertex mode
        if (Selection.Mode == InteractionMode.Vertex && Selection.ActiveObjectId.HasValue)
        {
            DrawVertexSelectionOverlay(new Vector2(targetW, targetH));
        }

        // Marquee drag selection box overlay
        if (_isDraggingMarquee)
        {
            ImDrawListPtr drawList = ImGui.GetWindowDrawList();
            Vector2 min = Vector2.Min(_mouseDownScreenPos, _mouseCurrentScreenPos);
            Vector2 max = Vector2.Max(_mouseDownScreenPos, _mouseCurrentScreenPos);

            drawList.PushClipRect(_viewportScreenMin, _viewportScreenMin + new Vector2(targetW, targetH), true);
            drawList.AddRectFilled(min, max, 0x33FFA020);
            drawList.AddRect(min, max, 0xEEFFA020, 0.0f, ImDrawFlags.None, 1.5f);
            drawList.PopClipRect();
        }

        // Reset hover preview for next frame if not re-hovered by buttons
        ClearCsgPreview();

        // Interactive "Adjust Last Operation" overlay popup in bottom-left corner of viewport
        if (PrimitiveController.IsActive)
        {
            DrawAdjustPrimitiveOverlay(_viewportScreenMin, new Vector2(targetW, targetH));
        }
        else if (EditController.IsExtrudeActive)
        {
            DrawExtrudeOverlay(_viewportScreenMin, new Vector2(targetW, targetH));
        }
        else if (EditController.IsInsetActive)
        {
            DrawInsetOverlay(_viewportScreenMin, new Vector2(targetW, targetH));
        }

        // Open requested popups
        if (ShortcutHandler.OpenContextMenuRequested)
        {
            ImGui.OpenPopup("ViewportContextMenu");
            ShortcutHandler.OpenContextMenuRequested = false;
        }
        if (ShortcutHandler.OpenDeletePopupRequested)
        {
            ImGui.OpenPopup("DeleteContextMenu");
            ShortcutHandler.OpenDeletePopupRequested = false;
        }
        if (ShortcutHandler.OpenNormalsPopupRequested)
        {
            ImGui.OpenPopup("NormalsContextMenu");
            ShortcutHandler.OpenNormalsPopupRequested = false;
        }
        if (ShortcutHandler.OpenWeldPopupRequested)
        {
            ImGui.OpenPopup("WeldVerticesPopup");
            ShortcutHandler.OpenWeldPopupRequested = false;
        }
        if (ShortcutHandler.OpenSubdividePopupRequested)
        {
            ImGui.OpenPopup("SubdividePopup");
            ShortcutHandler.OpenSubdividePopupRequested = false;
        }
        if (ShortcutHandler.OpenMirrorPopupRequested)
        {
            ImGui.OpenPopup("MirrorPopup");
            ShortcutHandler.OpenMirrorPopupRequested = false;
        }
        if (ShortcutHandler.OpenLinearArrayPopupRequested)
        {
            ImGui.OpenPopup("LinearArrayPopup");
            ShortcutHandler.OpenLinearArrayPopupRequested = false;
        }
        if (ShortcutHandler.OpenRadialArrayPopupRequested)
        {
            ImGui.OpenPopup("RadialArrayPopup");
            ShortcutHandler.OpenRadialArrayPopupRequested = false;
        }

        DrawViewportContextMenu();
        DrawDeleteContextMenu();
        DrawNormalsContextMenu();
        DrawWeldVerticesPopup();
        DrawSubdividePopup();
        DrawMirrorPopup();
        DrawLinearArrayPopup();
        DrawRadialArrayPopup();
        DrawEnvironmentLightingPopup();

        if (IsWalkModeActive)
        {
            DrawWalkModeHudOverlay(new Vector2(targetW, targetH));
        }
        else
        {
            if (_isViewportHovered || Raylib.IsMouseButtonDown(MouseButton.Right) || Raylib.IsMouseButtonDown(MouseButton.Middle))
            {
                UpdateViewportCameraInput(dt, new Vector2(targetW, targetH));
            }

            UpdateViewportSelectionInput(new Vector2(targetW, targetH));
        }

        ImGui.End();
    }

    private void DrawWalkModeHudOverlay(Vector2 viewportSize)
    {
        if (ActiveWalkController is null)
        {
            return;
        }

        Vector3 p = ActiveWalkController.Position;
        string groundedStr = ActiveWalkController.IsGrounded ? "Yes" : "No";
        string surfaceStr = !string.IsNullOrWhiteSpace(ActiveWalkController.GroundSurfaceName)
            ? ActiveWalkController.GroundSurfaceName!
            : (!string.IsNullOrWhiteSpace(ActiveWalkController.GroundMaterialId) ? ActiveWalkController.GroundMaterialId! : "none");
        string triggerStr = ActiveWalkController.ActiveTriggerIds.Count > 0
            ? string.Join(", ", ActiveWalkController.ActiveTriggerIds)
            : "none";

        string hudText = $"WALK MODE (F5 / Esc to Exit) | Pos: ({p.X:0.00}, {p.Y:0.00}, {p.Z:0.00}) | Grounded: {groundedStr} | Surface: {surfaceStr} | Trigger: {triggerStr}";

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        Vector2 textSize = ImGui.CalcTextSize(hudText);
        Vector2 pad = new(12.0f, 6.0f);
        float bannerWidth = textSize.X + (pad.X * 2.0f);
        float bannerHeight = textSize.Y + (pad.Y * 2.0f);

        Vector2 bannerMin = new(
            _viewportScreenMin.X + ((viewportSize.X - bannerWidth) * 0.5f),
            _viewportScreenMin.Y + 12.0f);
        Vector2 bannerMax = bannerMin + new Vector2(bannerWidth, bannerHeight);

        uint bgCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.08f, 0.10f, 0.13f, 0.85f));
        uint borderCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.95f, 0.50f, 0.18f, 0.90f));
        uint textCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.96f, 0.97f, 0.99f, 1.0f));

        drawList.AddRectFilled(bannerMin, bannerMax, bgCol, 5.0f);
        drawList.AddRect(bannerMin, bannerMax, borderCol, 5.0f, ImDrawFlags.None, 1.5f);
        drawList.AddText(bannerMin + pad, textCol, hudText);

        // Subtle center crosshair
        Vector2 center = _viewportScreenMin + (viewportSize * 0.5f);
        uint crossCol = ImGui.ColorConvertFloat4ToU32(new Vector4(1.0f, 1.0f, 1.0f, 0.65f));
        drawList.AddLine(center - new Vector2(6.0f, 0.0f), center + new Vector2(6.0f, 0.0f), crossCol, 1.5f);
        drawList.AddLine(center - new Vector2(0.0f, 6.0f), center + new Vector2(0.0f, 6.0f), crossCol, 1.5f);
    }

    private void DrawViewportHeaderToolbar()
    {
        // 0. Interaction Mode Selector
        ImGui.TextDisabled("Mode:");
        ImGui.SameLine();
        if (ImGui.RadioButton("Object", Selection.Mode == InteractionMode.Object))
        {
            Selection.SetMode(InteractionMode.Object, Scene);
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("Vertex (1)", Selection.Mode == InteractionMode.Vertex))
        {
            Selection.SetMode(InteractionMode.Vertex, Scene);
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("Edge (2)", Selection.Mode == InteractionMode.Edge))
        {
            Selection.SetMode(InteractionMode.Edge, Scene);
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("Face (3)", Selection.Mode == InteractionMode.Face))
        {
            Selection.SetMode(InteractionMode.Face, Scene);
        }

        ImGui.SameLine();
        ImGui.TextDisabled("| View:");
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
            if (ImGui.Selectable("Lit Preview", ShadingMode == ViewportShadingMode.LitPreview))
                ShadingMode = ViewportShadingMode.LitPreview;
            ImGui.EndCombo();
        }

        ImGui.SameLine();
        bool isLit = ShadingMode == ViewportShadingMode.LitPreview;
        if (isLit)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.20f, 0.55f, 0.35f, 1.0f));
        }
        if (ImGui.Button(isLit ? "Lit Preview [F4]##toggle" : "Lit [F4]##toggle"))
        {
            ToggleLitPreview();
        }
        if (isLit)
        {
            ImGui.PopStyleColor();
        }

        ImGui.SameLine();
        if (ImGui.Button("Lighting...##envSettings"))
        {
            ImGui.OpenPopup("EnvironmentLightingPopup");
        }

        ImGui.SameLine();
        bool walkActive = IsWalkModeActive;
        if (walkActive)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.85f, 0.42f, 0.15f, 1.0f));
        }
        if (ImGui.Button(walkActive ? "Exit Walk [F5]##walkBtn" : "Walk Mode [F5]##walkBtn"))
        {
            ToggleWalkMode();
        }
        if (walkActive)
        {
            ImGui.PopStyleColor();
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

        if (ModalController.IsActive)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("|");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1.0f, 0.85f, 0.2f, 1.0f), ModalController.GetStatusReadout());
        }

        ImGui.Separator();
    }

    private void DrawPrimitiveToolbar()
    {
        ImGui.TextDisabled("Gizmo:");
        ImGui.SameLine();
        if (ImGui.RadioButton("Move (W)", Gizmo.ToolMode == GizmoToolMode.Translate))
        {
            Gizmo.ToolMode = GizmoToolMode.Translate;
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("Rotate (E)", Gizmo.ToolMode == GizmoToolMode.Rotate))
        {
            Gizmo.ToolMode = GizmoToolMode.Rotate;
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("Scale (R)", Gizmo.ToolMode == GizmoToolMode.Scale))
        {
            Gizmo.ToolMode = GizmoToolMode.Scale;
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("Universal", Gizmo.ToolMode == GizmoToolMode.Universal))
        {
            Gizmo.ToolMode = GizmoToolMode.Universal;
        }

        ImGui.SameLine();
        ImGui.TextDisabled("| Tools:");
        ImGui.SameLine();

        if (Selection.Mode == InteractionMode.Object)
        {
            if (ImGui.Button("Clone (Shift+D)"))
            {
                if (EditController.ExecuteQuickClone(Scene, Selection, UndoStack))
                {
                    Ray3D ray = Camera.ScreenPointToRay(_viewportLocalMousePos, _viewportRenderer.ViewportSize);
                    ModalController.Start(ModalTransformMode.Move, Scene, Selection, ray, Camera.GetForward(), UndoStack);
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Mirror...")) ShortcutHandler.OpenMirrorPopupRequested = true;
            ImGui.SameLine();
            if (ImGui.Button("Linear Array...")) ShortcutHandler.OpenLinearArrayPopupRequested = true;
            ImGui.SameLine();
            if (ImGui.Button("Radial Array...")) ShortcutHandler.OpenRadialArrayPopupRequested = true;
            ImGui.SameLine();
            if (ImGui.Button("Delete (Del)")) EditController.ExecuteDeleteObjects(Scene, Selection, UndoStack);

            ImGui.SameLine();
            ImGui.TextDisabled("| CSG:");
            ImGui.SameLine();
            bool csgAvailable = Selection.SelectedObjectIds.Count >= 2;
            if (!csgAvailable) ImGui.BeginDisabled();
            if (ImGui.Button("Union (Ctrl+=)"))
            {
                ExecuteCsgUnion();
            }
            if (ImGui.IsItemHovered())
            {
                SetCsgPreview(new Color(60, 220, 80, 255));
            }
            ImGui.SameLine();
            if (ImGui.Button("Subtract (Ctrl+-)"))
            {
                ExecuteCsgSubtract();
            }
            if (ImGui.IsItemHovered())
            {
                SetCsgPreview(new Color(240, 50, 50, 255));
            }
            ImGui.SameLine();
            if (ImGui.Button("Intersect (Ctrl+8)"))
            {
                ExecuteCsgIntersect();
            }
            if (ImGui.IsItemHovered())
            {
                SetCsgPreview(new Color(50, 220, 240, 255));
            }
            if (!csgAvailable) ImGui.EndDisabled();

            ImGui.SameLine();
            if (!Selection.HasSelection) ImGui.BeginDisabled();
            if (ImGui.Button("Carve (Ctrl+Shift+C)"))
            {
                ExecuteCsgCarve();
            }
            if (ImGui.IsItemHovered())
            {
                SetCsgPreview(new Color(240, 50, 50, 255));
            }
            if (!Selection.HasSelection) ImGui.EndDisabled();

            ImGui.SameLine();
            ImGui.TextDisabled("| Add:");
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

            ImGui.SameLine();
            ImGui.TextDisabled("| Entity:");
            ImGui.SameLine();
            if (ImGui.Button("Spawn")) SpawnEntity("player_spawn");
            ImGui.SameLine();
            if (ImGui.Button("Pt Light")) SpawnEntity("light", ("light_type", "point"));
            ImGui.SameLine();
            if (ImGui.Button("Spot Light")) SpawnEntity("light", ("light_type", "spot"));
            ImGui.SameLine();
            if (ImGui.Button("Portal")) SpawnEntity("portal");
            ImGui.SameLine();
            if (ImGui.Button("Trigger")) SpawnEntity("trigger");
        }
        else if (Selection.Mode == InteractionMode.Vertex)
        {
            if (ImGui.Button("Connect (J)")) EditController.ExecuteConnectVertices(Scene, Selection, UndoStack);
            ImGui.SameLine();
            if (ImGui.Button("Fill Face (F)")) EditController.ExecuteFillFace(Scene, Selection, UndoStack);
            ImGui.SameLine();
            if (ImGui.Button("Weld Center (M)")) EditController.ExecuteWeldCenter(Scene, Selection, UndoStack);
            ImGui.SameLine();
            if (ImGui.Button("Weld Distance...")) ShortcutHandler.OpenWeldPopupRequested = true;
            ImGui.SameLine();
            if (ImGui.Button("Delete (X)")) ShortcutHandler.OpenDeletePopupRequested = true;
        }
        else if (Selection.Mode == InteractionMode.Edge)
        {
            if (ImGui.Button("Extrude (E)"))
            {
                Ray3D ray = Camera.ScreenPointToRay(_viewportLocalMousePos, _viewportRenderer.ViewportSize);
                EditController.StartExtrude(Scene, Selection, ray, Camera.GetForward(), UndoStack);
            }
            ImGui.SameLine();
            if (ImGui.Button("Subdivide...")) ShortcutHandler.OpenSubdividePopupRequested = true;
            ImGui.SameLine();
            if (ImGui.Button("Fill Face (F)")) EditController.ExecuteFillFace(Scene, Selection, UndoStack);
            ImGui.SameLine();
            if (ImGui.Button("Delete (X)")) ShortcutHandler.OpenDeletePopupRequested = true;
        }
        else if (Selection.Mode == InteractionMode.Face)
        {
            if (ImGui.Button("Extrude (E)"))
            {
                Ray3D ray = Camera.ScreenPointToRay(_viewportLocalMousePos, _viewportRenderer.ViewportSize);
                EditController.StartExtrude(Scene, Selection, ray, Camera.GetForward(), UndoStack);
            }
            ImGui.SameLine();
            if (ImGui.Button("Inset (I)")) EditController.StartInset(Scene, Selection, ImGui.GetMousePos(), UndoStack);
            ImGui.SameLine();
            if (ImGui.Button("Clone (Shift+D)"))
            {
                if (EditController.ExecuteQuickClone(Scene, Selection, UndoStack))
                {
                    Ray3D ray = Camera.ScreenPointToRay(_viewportLocalMousePos, _viewportRenderer.ViewportSize);
                    ModalController.Start(ModalTransformMode.Move, Scene, Selection, ray, Camera.GetForward(), UndoStack);
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Dissolve (X)")) EditController.ExecuteDissolveFaces(Scene, Selection, UndoStack);
            ImGui.SameLine();
            if (ImGui.Button("Delete (Del)")) EditController.ExecuteDeleteFaces(Scene, Selection, UndoStack);
            ImGui.SameLine();
            if (ImGui.Button("Normals (Alt+N)...")) ShortcutHandler.OpenNormalsPopupRequested = true;
            ImGui.SameLine();
            if (ImGui.Button("Subdivide...")) ShortcutHandler.OpenSubdividePopupRequested = true;
        }

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

    private void DrawVertexSelectionOverlay(Vector2 viewportSize)
    {
        if (!Selection.ActiveObjectId.HasValue)
        {
            return;
        }

        SceneObject? activeObj = Scene.GetObject(Selection.ActiveObjectId.Value);
        if (activeObj?.Mesh is null)
        {
            return;
        }

        Matrix4x4 localToWorld = activeObj.Transform.GetLocalMatrix();
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(_viewportScreenMin, _viewportScreenMin + viewportSize, true);

        const uint unselectedDotColor = 0xFF805520;
        const uint unselectedBorderColor = 0xFFD0A040;
        const uint selectedSquareColor = 0xFF18A0FF;
        const uint selectedBorderColor = 0xFFFFFFFF;

        foreach (MeshVertex vertex in activeObj.Mesh.Vertices)
        {
            Vector3 wPos = Vector3.Transform(vertex.Position, localToWorld);
            Vector2? sPos = Camera.WorldToScreenPoint(wPos, viewportSize);
            if (!sPos.HasValue)
            {
                continue;
            }

            Vector2 pt = _viewportScreenMin + sPos.Value;
            bool isSelected = Selection.SelectedVertexIds.Contains(vertex.Id);

            if (isSelected)
            {
                // 8px bright orange/yellow square with white outline
                drawList.AddRectFilled(pt - new Vector2(4, 4), pt + new Vector2(4, 4), selectedSquareColor);
                drawList.AddRect(pt - new Vector2(4, 4), pt + new Vector2(4, 4), selectedBorderColor, 0.0f, ImDrawFlags.None, 1.0f);
            }
            else
            {
                // 5px cyan/dark dot
                drawList.AddCircleFilled(pt, 2.5f, unselectedDotColor);
                drawList.AddCircle(pt, 2.5f, unselectedBorderColor, 0, 1.0f);
            }
        }

        drawList.PopClipRect();
    }

    private void UpdateViewportSelectionInput(Vector2 viewportSize)
    {
        // Active Interactive Edit Controller Drag Handling (Extrude / Inset)
        if (EditController.IsActive)
        {
            if (Raylib.IsMouseButtonPressed(MouseButton.Right) || Raylib.IsKeyPressed(KeyboardKey.Escape))
            {
                if (EditController.IsExtrudeActive)
                    EditController.CancelExtrude(Scene, UndoStack);
                else if (EditController.IsInsetActive)
                    EditController.CancelInset(Scene, UndoStack);
                _statusMessage = "Cancelled operation.";
                return;
            }

            if (Raylib.IsMouseButtonPressed(MouseButton.Left) || Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter))
            {
                if (EditController.IsExtrudeActive)
                    EditController.ConfirmExtrude(Scene, UndoStack);
                else if (EditController.IsInsetActive)
                    EditController.ConfirmInset(Scene, UndoStack);
                _statusMessage = "Committed operation.";
                return;
            }

            if (EditController.IsExtrudeActive)
            {
                Ray3D currentRay = Camera.ScreenPointToRay(_viewportLocalMousePos, viewportSize);
                bool ctrlHeld = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
                bool snapGrid = ctrlHeld ? !SnapToGridEnabled : SnapToGridEnabled;
                EditController.UpdateExtrudeRay(Scene, currentRay, snapGrid, CurrentGridStep);
                _statusMessage = EditController.GetStatusReadout();
                return;
            }

            if (EditController.IsInsetActive)
            {
                Vector2 mouseScreenPos = ImGui.GetMousePos();
                bool ctrlHeld = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
                bool snapGrid = ctrlHeld ? !SnapToGridEnabled : SnapToGridEnabled;
                EditController.UpdateInsetMouse(Scene, mouseScreenPos, snapGrid, CurrentGridStep);
                _statusMessage = EditController.GetStatusReadout();
                return;
            }
        }

        // Active Modal Transform Operator Drag Handling
        if (ModalController.IsActive)
        {
            if (Raylib.IsMouseButtonPressed(MouseButton.Right) || Raylib.IsKeyPressed(KeyboardKey.Escape))
            {
                ModalController.Cancel(Scene, UndoStack);
                _statusMessage = "Cancelled modal transform.";
                return;
            }

            if (Raylib.IsMouseButtonPressed(MouseButton.Left) || Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter))
            {
                ModalController.Confirm(Scene, UndoStack);
                PrimitiveController.Dismiss();
                _statusMessage = "Committed modal transform.";
                return;
            }

            Ray3D currentRay = Camera.ScreenPointToRay(_viewportLocalMousePos, viewportSize);
            bool ctrlHeld = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
            bool snapGrid = ctrlHeld ? !SnapToGridEnabled : SnapToGridEnabled;
            bool snapAngle = ctrlHeld ? !SnapAngleEnabled : SnapAngleEnabled;

            ModalController.UpdateRay(Scene, currentRay, snapGrid, CurrentGridStep, snapAngle, CurrentAngleStepDegrees);
            _statusMessage = ModalController.GetStatusReadout();
            return;
        }

        // Active Gizmo Drag Handling
        if (Gizmo.IsDragging)
        {
            if (Raylib.IsMouseButtonPressed(MouseButton.Right) || Raylib.IsKeyPressed(KeyboardKey.Escape))
            {
                Gizmo.CancelDrag();
                Applicator.CancelSession(Scene, UndoStack);
                _statusMessage = "Cancelled gizmo transform.";
                return;
            }

            if (Raylib.IsMouseButtonDown(MouseButton.Left))
            {
                Ray3D dragRay = Camera.ScreenPointToRay(_viewportLocalMousePos, viewportSize);
                bool ctrlHeld = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
                bool snapGrid = ctrlHeld ? !SnapToGridEnabled : SnapToGridEnabled;
                bool snapAngle = ctrlHeld ? !SnapAngleEnabled : SnapAngleEnabled;

                Gizmo.UpdateDrag(dragRay, snapGrid, CurrentGridStep, snapAngle, CurrentAngleStepDegrees);
                Applicator.ApplyGizmo(Scene, Gizmo);

                _statusMessage = Gizmo.ActiveDragMode switch
                {
                    GizmoToolMode.Translate => $"Translate ({Gizmo.ActiveConstraint}): ({Gizmo.CurrentTranslationDelta.X:+0.00;-0.00;0.00}, {Gizmo.CurrentTranslationDelta.Y:+0.00;-0.00;0.00}, {Gizmo.CurrentTranslationDelta.Z:+0.00;-0.00;0.00})",
                    GizmoToolMode.Rotate => $"Rotate ({Gizmo.ActiveConstraint}): {Gizmo.CurrentAngleDegrees:+0.0;-0.0;0.0}°",
                    GizmoToolMode.Scale => $"Scale ({Gizmo.ActiveConstraint}): ({Gizmo.CurrentScaleMultiplier.X:0.00}, {Gizmo.CurrentScaleMultiplier.Y:0.00}, {Gizmo.CurrentScaleMultiplier.Z:0.00})",
                    _ => "Transforming..."
                };
                return;
            }

            if (Raylib.IsMouseButtonReleased(MouseButton.Left))
            {
                Gizmo.EndDrag();
                Applicator.CommitSession(Scene, UndoStack);
                PrimitiveController.Dismiss();
                _statusMessage = "Committed gizmo transform.";
                return;
            }
        }

        // Don't interfere if right or middle mouse buttons are used for camera navigation
        if (Raylib.IsMouseButtonDown(MouseButton.Right) || Raylib.IsMouseButtonDown(MouseButton.Middle))
        {
            _isMouseDownInViewport = false;
            _isDraggingMarquee = false;
            Gizmo.HoveredConstraint = GizmoAxisConstraint.None;
            return;
        }

        Vector2 mouseScreen = ImGui.GetMousePos();

        // Check if mouse is over adjust primitive overlay window in bottom-left
        bool overAdjustOverlay = false;
        if (PrimitiveController.IsActive || EditController.IsActive)
        {
            const float overlayWidth = 280.0f;
            const float margin = 12.0f;
            Vector2 overlayPos = new(_viewportScreenMin.X + margin, _viewportScreenMin.Y + viewportSize.Y - 240.0f);
            if (mouseScreen.X >= overlayPos.X && mouseScreen.X <= overlayPos.X + overlayWidth &&
                mouseScreen.Y >= overlayPos.Y && mouseScreen.Y <= _viewportScreenMin.Y + viewportSize.Y)
            {
                overAdjustOverlay = true;
            }
        }

        // Update Gizmo Hover State when not dragging marquee
        Vector3? activePivot = Selection.GetSelectionPivotWorld(Scene);
        if (_isViewportHovered && !overAdjustOverlay && !_isDraggingMarquee && activePivot.HasValue)
        {
            Ray3D hoverRay = Camera.ScreenPointToRay(_viewportLocalMousePos, viewportSize);
            float gizmoScale = TransformGizmo3D.ComputeGizmoScale(Camera, activePivot.Value);
            Gizmo.HoveredConstraint = Gizmo.HitTest(hoverRay, activePivot.Value, gizmoScale, Gizmo.ToolMode);
        }
        else if (!_isMouseDownInViewport)
        {
            Gizmo.HoveredConstraint = GizmoAxisConstraint.None;
        }

        // 1. Mouse Down
        if (_isViewportHovered && !overAdjustOverlay && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            if (!ImGui.IsAnyItemActive())
            {
                // If hovering a 3D Gizmo handle, start gizmo drag instead of selection/marquee
                if (Gizmo.HoveredConstraint != GizmoAxisConstraint.None && activePivot.HasValue)
                {
                    Ray3D startRay = Camera.ScreenPointToRay(_viewportLocalMousePos, viewportSize);
                    Gizmo.BeginDrag(Gizmo.ToolMode, Gizmo.HoveredConstraint, activePivot.Value, startRay, Camera.GetForward());
                    Applicator.BeginSession(
                        Scene,
                        Selection,
                        activePivot.Value,
                        UndoStack,
                        $"{Gizmo.ActiveDragMode} ({Gizmo.ActiveConstraint})");
                    PrimitiveController.Dismiss();
                    return;
                }

                _isMouseDownInViewport = true;
                _mouseDownLocalPos = _viewportLocalMousePos;
                _mouseDownScreenPos = mouseScreen;
                _mouseCurrentLocalPos = _viewportLocalMousePos;
                _mouseCurrentScreenPos = mouseScreen;
                _isDraggingMarquee = false;
            }
        }

        // 2. Mouse Dragging
        if (_isMouseDownInViewport && Raylib.IsMouseButtonDown(MouseButton.Left))
        {
            _mouseCurrentLocalPos = _viewportLocalMousePos;
            _mouseCurrentScreenPos = mouseScreen;

            if (!_isDraggingMarquee && Vector2.Distance(_mouseDownScreenPos, mouseScreen) > 4.0f)
            {
                _isDraggingMarquee = true;
            }
        }

        // 3. Mouse Released
        if (_isMouseDownInViewport && Raylib.IsMouseButtonReleased(MouseButton.Left))
        {
            _isMouseDownInViewport = false;
            bool shiftHeld = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);

            if (_isDraggingMarquee)
            {
                _isDraggingMarquee = false;
                ExecuteMarqueeSelection(viewportSize, shiftHeld);
            }
            else
            {
                ExecuteSingleClickPicking(viewportSize, shiftHeld);
            }
        }
    }

    private void ExecuteMarqueeSelection(Vector2 viewportSize, bool shiftHeld)
    {
        float minX = System.Math.Min(_mouseDownLocalPos.X, _mouseCurrentLocalPos.X);
        float maxX = System.Math.Max(_mouseDownLocalPos.X, _mouseCurrentLocalPos.X);
        float minY = System.Math.Min(_mouseDownLocalPos.Y, _mouseCurrentLocalPos.Y);
        float maxY = System.Math.Max(_mouseDownLocalPos.Y, _mouseCurrentLocalPos.Y);
        var screenRect = new RectangleF(minX, minY, System.Math.Max(1.0f, maxX - minX), System.Math.Max(1.0f, maxY - minY));

        Func<Vector3, Vector2?> worldToScreen = pt => Camera.WorldToScreenPoint(pt, viewportSize);

        if (Selection.Mode == InteractionMode.Object)
        {
            List<int> picked = MeshPicker.PickObjectsBoxMarquee(Scene, screenRect, worldToScreen);
            List<string> pickedEntities = MeshPicker.PickEntitiesBoxMarquee(Scene, screenRect, worldToScreen);
            if (!shiftHeld)
            {
                Selection.ClearSelection();
            }

            foreach (int id in picked)
            {
                Selection.ToggleSelectObject(id);
            }
            foreach (string entId in pickedEntities)
            {
                Selection.ToggleSelectEntity(entId);
            }

            _statusMessage = $"Box selected {Selection.SelectedObjectIds.Count} object(s), {Selection.SelectedEntityIds.Count} entity/entities.";
        }
        else if (Selection.ActiveObjectId.HasValue && Scene.GetObject(Selection.ActiveObjectId.Value) is { } activeObj)
        {
            MarqueeSelectionResult res = MeshPicker.PickBoxMarquee(activeObj, Selection.Mode, screenRect, worldToScreen);

            switch (Selection.Mode)
            {
                case InteractionMode.Vertex:
                    if (!shiftHeld) Selection.ClearSelection();
                    foreach (int vId in res.VertexIds) Selection.ToggleSelectVertex(vId);
                    _statusMessage = $"Box selected {Selection.SelectedVertexIds.Count} vertex/vertices.";
                    break;

                case InteractionMode.Edge:
                    if (!shiftHeld) Selection.ClearSelection();
                    foreach (EdgeKey edge in res.Edges) Selection.ToggleSelectEdge(edge);
                    _statusMessage = $"Box selected {Selection.SelectedEdges.Count} edge(s).";
                    break;

                case InteractionMode.Face:
                    if (!shiftHeld) Selection.ClearSelection();
                    foreach (int fId in res.FaceIds) Selection.ToggleSelectFace(fId);
                    _statusMessage = $"Box selected {Selection.SelectedFaceIds.Count} face(s).";
                    break;
            }
        }
    }

    private void ExecuteSingleClickPicking(Vector2 viewportSize, bool shiftHeld)
    {
        Ray3D ray = Camera.ScreenPointToRay(_viewportLocalMousePos, viewportSize);
        Func<Vector3, Vector2?> worldToScreen = pt => Camera.WorldToScreenPoint(pt, viewportSize);

        if (Selection.Mode == InteractionMode.Object)
        {
            ObjectHit? hit = MeshPicker.PickObject(Scene, ray);
            EntityHit? entHit = MeshPicker.PickEntity(Scene, ray, EntityRegistry);

            bool pickEntity = false;
            if (entHit.HasValue && hit.HasValue)
            {
                pickEntity = entHit.Value.Distance <= hit.Value.Distance;
            }
            else if (entHit.HasValue)
            {
                pickEntity = true;
            }

            if (pickEntity && entHit.HasValue)
            {
                if (shiftHeld)
                {
                    Selection.ToggleSelectEntity(entHit.Value.EntityId);
                }
                else
                {
                    Selection.SelectSingleEntity(entHit.Value.EntityId);
                }

                LevelEntity? ent = Scene.GetEntity(entHit.Value.EntityId);
                _statusMessage = $"Selected Entity '{ent?.Id ?? "Entity"}' ({ent?.ClassName}). Total: {Selection.SelectedEntityIds.Count}.";
            }
            else if (hit.HasValue)
            {
                if (shiftHeld)
                {
                    Selection.ToggleSelectObject(hit.Value.ObjectId);
                }
                else
                {
                    Selection.SelectSingleObject(hit.Value.ObjectId);
                }

                SceneObject? obj = Scene.GetObject(hit.Value.ObjectId);
                _statusMessage = $"Selected '{obj?.Name ?? "Object"}' (Face {hit.Value.FaceId}). Total: {Selection.SelectedObjectIds.Count}.";
            }
            else if (!shiftHeld)
            {
                Selection.ClearSelection();
                _statusMessage = "Cleared selection.";
            }
        }
        else if (Selection.ActiveObjectId.HasValue && Scene.GetObject(Selection.ActiveObjectId.Value) is { } activeObj)
        {
            switch (Selection.Mode)
            {
                case InteractionMode.Vertex:
                {
                    int? pickedVert = MeshPicker.PickVertex(
                        activeObj,
                        worldToScreen,
                        _viewportLocalMousePos,
                        maxPixelDistance: 12.0f,
                        cameraPos: Camera.Position);

                    if (pickedVert.HasValue)
                    {
                        if (shiftHeld)
                        {
                            Selection.ToggleSelectVertex(pickedVert.Value);
                        }
                        else
                        {
                            Selection.SelectSingleVertex(pickedVert.Value);
                        }

                        _statusMessage = $"Selected Vertex {pickedVert.Value}. Total: {Selection.SelectedVertexIds.Count}.";
                    }
                    else if (!shiftHeld)
                    {
                        Selection.ClearSelection();
                    }
                    break;
                }

                case InteractionMode.Edge:
                {
                    EdgeKey? pickedEdge = MeshPicker.PickEdge(
                        activeObj,
                        ray,
                        worldToScreen,
                        _viewportLocalMousePos,
                        maxPixelDistance: 10.0f);

                    if (pickedEdge.HasValue)
                    {
                        if (shiftHeld)
                        {
                            Selection.ToggleSelectEdge(pickedEdge.Value);
                        }
                        else
                        {
                            Selection.SelectSingleEdge(pickedEdge.Value);
                        }

                        _statusMessage = $"Selected Edge ({pickedEdge.Value.VMin}, {pickedEdge.Value.VMax}). Total: {Selection.SelectedEdges.Count}.";
                    }
                    else if (!shiftHeld)
                    {
                        Selection.ClearSelection();
                    }
                    break;
                }

                case InteractionMode.Face:
                {
                    FaceHit? pickedFace = MeshPicker.PickFace(activeObj, ray, ignoreBackfaces: true);
                    if (pickedFace.HasValue)
                    {
                        bool altHeld = Raylib.IsKeyDown(KeyboardKey.LeftAlt) || Raylib.IsKeyDown(KeyboardKey.RightAlt);

                        // TrenchBroom Face Painting: Shift + Alt + Click -> paint ActiveMaterialId
                        if (shiftHeld && altHeld)
                        {
                            if (activeObj.Mesh?.GetFace(pickedFace.Value.FaceId) is { } f)
                            {
                                EditorScene before = Scene.CloneSnapshot();
                                f.MaterialId = ActiveMaterialId;
                                UndoStack.RecordAction($"Paint Face {f.Id} with {ActiveMaterialId}", before, Scene);
                                _statusMessage = $"Painted Face {f.Id} with material '{ActiveMaterialId}'.";
                            }
                            return;
                        }

                        // TrenchBroom Material Eyedropper / Sampling: Alt + Click -> sample material and UVs
                        if (altHeld)
                        {
                            if (activeObj.Mesh?.GetFace(pickedFace.Value.FaceId) is { } f)
                            {
                                ActiveMaterialId = f.MaterialId;
                                Selection.SelectSingleFace(f.Id);
                                _statusMessage = $"Sampled material '{ActiveMaterialId}' and UV mapping from Face {f.Id}.";
                            }
                            return;
                        }

                        if (shiftHeld)
                        {
                            Selection.ToggleSelectFace(pickedFace.Value.FaceId);
                        }
                        else
                        {
                            Selection.SelectSingleFace(pickedFace.Value.FaceId);
                        }

                        _statusMessage = $"Selected Face {pickedFace.Value.FaceId}. Total: {Selection.SelectedFaceIds.Count}.";
                    }
                    else if (!shiftHeld)
                    {
                        Selection.ClearSelection();
                    }
                    break;
                }
            }
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

    public void ApplyMaterialToSelection(string materialId)
    {
        if (Selection.Mode == InteractionMode.Object && Selection.SelectedObjectIds.Count > 0)
        {
            EditorScene before = Scene.CloneSnapshot();
            foreach (int objId in Selection.SelectedObjectIds)
            {
                if (Scene.GetObject(objId) is { Mesh: { } mesh })
                {
                    for (int f = 0; f < mesh.Faces.Count; f++)
                    {
                        mesh.Faces[f].MaterialId = materialId;
                    }
                }
            }
            UndoStack.RecordAction($"Assign Material {materialId}", before, Scene);
            StatusMessage = $"Assigned {materialId} to {Selection.SelectedObjectIds.Count} object(s).";
        }
        else if (Selection.Mode == InteractionMode.Face && Selection.ActiveObjectId.HasValue && Selection.SelectedFaceIds.Count > 0)
        {
            if (Scene.GetObject(Selection.ActiveObjectId.Value) is { Mesh: { } mesh })
            {
                EditorScene before = Scene.CloneSnapshot();
                foreach (int fId in Selection.SelectedFaceIds)
                {
                    if (mesh.GetFace(fId) is { } face)
                    {
                        face.MaterialId = materialId;
                    }
                }
                UndoStack.RecordAction($"Assign Material {materialId} to Faces", before, Scene);
                StatusMessage = $"Assigned {materialId} to {Selection.SelectedFaceIds.Count} face(s).";
            }
        }
    }

    public void PromptNewLevel()
    {
        if (HasUnsavedChanges)
        {
            _openNewLevelModal = true;
        }
        else
        {
            CreateNewLevel();
        }
    }

    public void CreateNewLevel()
    {
        EditorScene before = Scene.CloneSnapshot();
        Scene.RestoreFromSnapshot(new EditorScene("Untitled Level"));
        CurrentFilePath = null;
        _savedUndoCount = 0;
        UndoStack.RecordAction("New Level", before, Scene);
        Selection.ClearSelection();
        PrimitiveController.Dismiss();
        StatusMessage = "Created new empty level.";
    }

    public void PromptOpenLevel()
    {
        _openOpenLevelModal = true;
    }

    public void LoadLevel(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            StatusMessage = $"Level file does not exist: {filePath}";
            return;
        }

        try
        {
            EditorScene loaded = LevelSerializer.LoadEditorScene(filePath);
            EditorScene before = Scene.CloneSnapshot();
            Scene.RestoreFromSnapshot(loaded);
            CurrentFilePath = filePath;
            _savedUndoCount = UndoStack.UndoCount;
            UndoStack.RecordAction($"Open Level {Path.GetFileName(filePath)}", before, Scene);
            Selection.ClearSelection();
            PrimitiveController.Dismiss();
            StatusMessage = $"Loaded level from {filePath}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load level: {ex.Message}";
        }
    }

    public void SaveLevel()
    {
        if (!string.IsNullOrWhiteSpace(CurrentFilePath))
        {
            SaveLevelAs(CurrentFilePath);
        }
        else
        {
            PromptSaveLevelAs();
        }
    }

    public void PromptSaveLevelAs()
    {
        _openSaveLevelAsModal = true;
    }

    public void SaveLevelAs(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        try
        {
            LevelSerializer.Save(Scene, filePath, bakeRightHandedRuntime: false);
            CurrentFilePath = filePath;
            _savedUndoCount = UndoStack.UndoCount;
            StatusMessage = $"Saved level to {filePath}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to save level: {ex.Message}";
        }
    }

    public void PromptExportObj()
    {
        _openExportObjModal = true;
    }

    public void PromptExportGltf()
    {
        _openExportGltfModal = true;
    }

    public void LoadSampleTempleBlockout()
    {
        EditorScene sample = SampleLevelFactory.CreateTempleBlockout();
        EditorScene before = Scene.CloneSnapshot();
        Scene.RestoreFromSnapshot(sample);
        CurrentFilePath = "samples/temple_blockout.level.json";
        _savedUndoCount = UndoStack.UndoCount;
        UndoStack.RecordAction("Load Temple Blockout", before, Scene);
        Selection.ClearSelection();
        PrimitiveController.Dismiss();
        StatusMessage = "Loaded Sample Temple Blockout Level. Press F4 for Lit Preview or F5 for Walk Mode!";
    }

    private void DrawFileModals()
    {
        if (_openNewLevelModal)
        {
            ImGui.OpenPopup("Unsaved Changes##NewLevelModal");
            _openNewLevelModal = false;
        }
        if (_openOpenLevelModal)
        {
            ImGui.OpenPopup("Open Level (.level.json)##OpenLevelModal");
            _openOpenLevelModal = false;
        }
        if (_openSaveLevelAsModal)
        {
            ImGui.OpenPopup("Save Level As (.level.json)##SaveLevelAsModal");
            _openSaveLevelAsModal = false;
        }
        if (_openExportObjModal)
        {
            ImGui.OpenPopup("Export Wavefront .obj##ExportObjModal");
            _openExportObjModal = false;
        }
        if (_openExportGltfModal)
        {
            ImGui.OpenPopup("Export glTF 2.0 (.gltf)##ExportGltfModal");
            _openExportGltfModal = false;
        }

        DrawNewLevelModal();
        DrawOpenLevelModal();
        DrawSaveLevelAsModal();
        DrawExportObjModal();
        DrawExportGltfModal();
    }

    private void DrawNewLevelModal()
    {
        bool isOpen = true;
        if (ImGui.BeginPopupModal("Unsaved Changes##NewLevelModal", ref isOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("You have unsaved changes in the current level.\nDo you want to save before creating a new level?");
            ImGui.Separator();

            if (ImGui.Button("Save##NewLevelSave", new Vector2(100, 0)))
            {
                SaveLevel();
                CreateNewLevel();
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Don't Save##NewLevelDiscard", new Vector2(100, 0)))
            {
                CreateNewLevel();
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel##NewLevelCancel", new Vector2(100, 0)))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void DrawOpenLevelModal()
    {
        bool isOpen = true;
        if (ImGui.BeginPopupModal("Open Level (.level.json)##OpenLevelModal", ref isOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.SetNextItemWidth(450);
            ImGui.InputText("File Path##OpenLevelInput", ref _openLevelPath, 256);

            ImGui.TextDisabled("Available Level Files:");
            if (ImGui.BeginChild("OpenLevelFileList", new Vector2(450, 160)))
            {
                var candidates = new List<string>();
                if (Directory.Exists("samples"))
                {
                    candidates.AddRange(Directory.GetFiles("samples", "*.level.json"));
                }
                if (Directory.Exists("."))
                {
                    candidates.AddRange(Directory.GetFiles(".", "*.level.json"));
                }

                foreach (string candidate in candidates.Distinct())
                {
                    string norm = candidate.Replace('\\', '/');
                    bool isSelected = _openLevelPath.Replace('\\', '/') == norm;
                    if (ImGui.Selectable(norm, isSelected))
                    {
                        _openLevelPath = norm;
                    }
                    if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    {
                        _openLevelPath = norm;
                        LoadLevel(_openLevelPath);
                        ImGui.CloseCurrentPopup();
                    }
                }
                ImGui.EndChild();
            }

            ImGui.Separator();

            if (ImGui.Button("Open##OpenLevelConfirm", new Vector2(120, 0)))
            {
                LoadLevel(_openLevelPath);
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel##OpenLevelCancel", new Vector2(120, 0)))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void DrawSaveLevelAsModal()
    {
        bool isOpen = true;
        if (ImGui.BeginPopupModal("Save Level As (.level.json)##SaveLevelAsModal", ref isOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.SetNextItemWidth(400);
            ImGui.InputText("Destination File##SaveAsInput", ref _saveAsLevelPath, 256);

            ImGui.Separator();

            if (ImGui.Button("Save##SaveAsConfirm", new Vector2(100, 0)))
            {
                SaveLevelAs(_saveAsLevelPath);
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel##SaveAsCancel", new Vector2(100, 0)))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void DrawExportObjModal()
    {
        bool isOpen = true;
        if (ImGui.BeginPopupModal("Export Wavefront .obj##ExportObjModal", ref isOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.SetNextItemWidth(400);
            ImGui.InputText("Output Path##ExportObjInput", ref _exportObjPath, 256);

            ImGui.Checkbox("Convert to Right-Handed (OpenGL / glTF standard)", ref _exportObjRightHanded);

            ImGui.Separator();

            if (ImGui.Button("Export##ExportObjConfirm", new Vector2(100, 0)))
            {
                try
                {
                    ObjExporter.Export(Scene, _exportObjPath, _exportObjRightHanded);
                    StatusMessage = $"Exported Wavefront OBJ to {_exportObjPath}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Export OBJ failed: {ex.Message}";
                }
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel##ExportObjCancel", new Vector2(100, 0)))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void DrawExportGltfModal()
    {
        bool isOpen = true;
        if (ImGui.BeginPopupModal("Export glTF 2.0 (.gltf)##ExportGltfModal", ref isOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.SetNextItemWidth(400);
            ImGui.InputText("Output Path##ExportGltfInput", ref _exportGltfPath, 256);

            ImGui.Checkbox("Embed Binary Buffer as Base64", ref _exportGltfEmbedBase64);

            ImGui.Separator();

            if (ImGui.Button("Export##ExportGltfConfirm", new Vector2(100, 0)))
            {
                try
                {
                    GltfExporter.Export(Scene, _exportGltfPath, _exportGltfEmbedBase64);
                    StatusMessage = $"Exported glTF 2.0 to {_exportGltfPath}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Export glTF failed: {ex.Message}";
                }
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel##ExportGltfCancel", new Vector2(100, 0)))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void DrawCsgPanel()
    {
        ImGui.Begin("CSG Booleans");

        if (Selection.Mode != InteractionMode.Object)
        {
            ImGui.TextDisabled("Switch to Object Mode (Tab) to use CSG Booleans.");
            ImGui.End();
            return;
        }

        int selCount = Selection.SelectedObjectIds.Count;
        int? primaryId = Selection.ActiveObjectId ?? (selCount > 0 ? Selection.SelectedObjectIds.First() : null);
        SceneObject? primaryObj = primaryId.HasValue ? Scene.GetObject(primaryId.Value) : null;
        int? secondaryId = selCount >= 2 ? Selection.SelectedObjectIds.FirstOrDefault(id => id != primaryId) : null;
        SceneObject? secondaryObj = secondaryId.HasValue ? Scene.GetObject(secondaryId.Value) : null;

        string targetName = primaryObj != null ? primaryObj.Name : "(None)";
        string cutterName = secondaryObj != null ? secondaryObj.Name : "(None)";

        ImGui.Text("CSG Pair Assignment:");
        ImGui.BulletText($"Target (A): {targetName}");
        ImGui.BulletText($"Cutter (B): {cutterName}");
        ImGui.Separator();

        bool deleteCutter = CsgDeleteCutter;
        if (ImGui.Checkbox("Delete Cutter Object After Boolean", ref deleteCutter))
        {
            CsgDeleteCutter = deleteCutter;
        }

        ImGui.Separator();
        ImGui.TextDisabled("Operations:");

        bool csgReady = selCount >= 2;
        if (!csgReady) ImGui.BeginDisabled();

        if (ImGui.Button("Union (A ∪ B)##CsgUnion", new Vector2(-1, 0)))
        {
            ExecuteCsgUnion();
        }
        if (ImGui.IsItemHovered())
        {
            SetCsgPreview(new Color(60, 220, 80, 255));
        }

        if (ImGui.Button("Subtract / Carve (A - B)##CsgSubtract", new Vector2(-1, 0)))
        {
            ExecuteCsgSubtract();
        }
        if (ImGui.IsItemHovered())
        {
            SetCsgPreview(new Color(240, 50, 50, 255));
        }

        if (ImGui.Button("Intersect (A ∩ B)##CsgIntersect", new Vector2(-1, 0)))
        {
            ExecuteCsgIntersect();
        }
        if (ImGui.IsItemHovered())
        {
            SetCsgPreview(new Color(50, 220, 240, 255));
        }

        if (!csgReady) ImGui.EndDisabled();

        ImGui.Separator();
        ImGui.TextDisabled("TrenchBroom-Style Carve:");
        if (!Selection.HasSelection) ImGui.BeginDisabled();
        if (ImGui.Button("Carve Overlapping Meshes##CsgCarve", new Vector2(-1, 0)))
        {
            ExecuteCsgCarve();
        }
        if (ImGui.IsItemHovered())
        {
            SetCsgPreview(new Color(240, 50, 50, 255));
        }
        if (!Selection.HasSelection) ImGui.EndDisabled();

        ImGui.End();
    }

    public void ExecuteCsgUnion()
    {
        if (EditController.ExecuteCsgBoolean(Scene, Selection, CsgOperation.Union, CsgDeleteCutter, UndoStack))
        {
            StatusMessage = "CSG Union completed.";
            ClearCsgPreview();
        }
    }

    public void ExecuteCsgSubtract()
    {
        if (EditController.ExecuteCsgBoolean(Scene, Selection, CsgOperation.Subtract, CsgDeleteCutter, UndoStack))
        {
            StatusMessage = "CSG Subtract / Carve completed.";
            ClearCsgPreview();
        }
    }

    public void ExecuteCsgIntersect()
    {
        if (EditController.ExecuteCsgBoolean(Scene, Selection, CsgOperation.Intersect, CsgDeleteCutter, UndoStack))
        {
            StatusMessage = "CSG Intersect completed.";
            ClearCsgPreview();
        }
    }

    public void ExecuteCsgCarve()
    {
        if (EditController.ExecuteCarve(Scene, Selection, CsgDeleteCutter, UndoStack))
        {
            StatusMessage = "TrenchBroom Carve completed.";
            ClearCsgPreview();
        }
    }

    public void SetCsgPreview(Color cutterWireColor)
    {
        if (Selection.Mode != InteractionMode.Object) return;

        int? primaryId = Selection.ActiveObjectId ?? (Selection.SelectedObjectIds.Count > 0 ? Selection.SelectedObjectIds.First() : null);
        int? secondaryId = Selection.SelectedObjectIds.Count >= 2 ? Selection.SelectedObjectIds.FirstOrDefault(id => id != primaryId) : null;

        CsgPreviewTargetId = primaryId;
        CsgPreviewCutterId = secondaryId ?? primaryId;
        CsgCutterWireColor = cutterWireColor;
    }

    public void ClearCsgPreview()
    {
        CsgPreviewTargetId = null;
        CsgPreviewCutterId = null;
        CsgCutterWireColor = null;
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
        string selectionInfo = Selection.Mode switch
        {
            InteractionMode.Object => $"Sel: {Selection.SelectedObjectIds.Count} obj",
            InteractionMode.Vertex => $"Sel: {Selection.SelectedVertexIds.Count} vert",
            InteractionMode.Edge => $"Sel: {Selection.SelectedEdges.Count} edge",
            InteractionMode.Face => $"Sel: {Selection.SelectedFaceIds.Count} face",
            _ => ""
        };

        Vector3? pivot = Selection.GetSelectionPivotWorld(Scene);
        string pivotInfo = pivot.HasValue ? $"Pivot: ({pivot.Value.X:0.#}, {pivot.Value.Y:0.#}, {pivot.Value.Z:0.#})" : "Pivot: none";

        string displayStatus = ModalController.IsActive
            ? ModalController.GetStatusReadout()
            : EditController.IsActive
                ? EditController.GetStatusReadout()
                : _statusMessage;

        ImGui.Text($"{displayStatus}  |  [{Selection.Mode}] {selectionInfo} | {pivotInfo}  |  Cam: ({camPos.X:0.0}, {camPos.Y:0.0}, {camPos.Z:0.0})  |  FPS: {Raylib.GetFPS()}");

        ImGui.End();
    }

    private void DrawViewportHudOverlay(Vector2 viewportMin, Vector2 viewportSize)
    {
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        Vector2 pos = viewportMin + new Vector2(14.0f, 14.0f);

        // 1. Current Mode Label
        string modeLabel = Selection.Mode switch
        {
            InteractionMode.Object => "OBJECT MODE",
            InteractionMode.Vertex => "EDIT MODE: VERTEX",
            InteractionMode.Edge => "EDIT MODE: EDGE",
            InteractionMode.Face => "EDIT MODE: FACE",
            _ => "OBJECT"
        };

        uint modeColor = Selection.Mode == InteractionMode.Object
            ? 0xFFFFA020  // Cyan/Light Blue
            : 0xFF20C8FF; // Bright Gold/Orange

        // 2. Active Object & Selection Count
        string activeName = "None";
        string summary;

        if (Selection.ActiveObjectId.HasValue && Scene.GetObject(Selection.ActiveObjectId.Value) is { } obj && obj.Mesh is not null)
        {
            activeName = obj.Name;
            int totV = obj.Mesh.VertexCount;
            int totE = obj.Mesh.GetTopology().Edges.Count;
            int totF = obj.Mesh.FaceCount;
            int selV = Selection.SelectedVertexIds.Count;
            int selE = Selection.SelectedEdges.Count;
            int selF = Selection.SelectedFaceIds.Count;

            summary = $"Verts: {selV}/{totV} | Edges: {selE}/{totE} | Faces: {selF}/{totF}";
        }
        else
        {
            summary = $"Selected Objects: {Selection.SelectedObjectIds.Count}/{Scene.ObjectCount}";
        }

        // 3. Modal Operator Status (G/R/S/E/I)
        string? modalStatus = null;
        if (ModalController.IsActive)
        {
            modalStatus = $"OPERATOR: {ModalController.Mode.ToString().ToUpper()} ({ModalController.Constraint}) [Enter: Commit | Esc: Cancel]";
        }
        else if (EditController.IsExtrudeActive)
        {
            modalStatus = $"OPERATOR: EXTRUDE ({EditController.ExtrudeTarget}) [Dist: {EditController.ExtrudeDistance:+0.00;-0.00;0.00}m] [Enter: Commit | Esc: Cancel]";
        }
        else if (EditController.IsInsetActive)
        {
            modalStatus = $"OPERATOR: INSET [Thick: {EditController.InsetThickness:0.00}m, Depth: {EditController.InsetDepth:0.00}m] [Enter: Commit | Esc: Cancel]";
        }

        float width = 360.0f;
        float height = modalStatus is not null ? 68.0f : 48.0f;
        Vector2 boxMax = pos + new Vector2(width, height);

        drawList.PushClipRect(viewportMin, viewportMin + viewportSize, true);
        drawList.AddRectFilled(pos, boxMax, 0xDD141619, 5.0f);
        drawList.AddRect(pos, boxMax, 0xFF353A42, 5.0f, ImDrawFlags.None, 1.0f);

        drawList.AddText(pos + new Vector2(10.0f, 6.0f), modeColor, modeLabel);
        drawList.AddText(pos + new Vector2(10.0f, 24.0f), 0xFFC8CDD5, $"{activeName} | {summary}");

        if (modalStatus is not null)
        {
            drawList.AddText(pos + new Vector2(10.0f, 44.0f), 0xFF30F0FF, modalStatus);
        }
        drawList.PopClipRect();
    }

    private void DrawExtrudeOverlay(Vector2 viewportMin, Vector2 viewportSize)
    {
        const float overlayWidth = 280.0f;
        const float margin = 12.0f;

        Vector2 overlayPos = new(viewportMin.X + margin, viewportMin.Y + viewportSize.Y - 140.0f);
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

        bool open = true;
        if (ImGui.Begin("Extrude Region###ExtrudeOverlay", ref open, flags))
        {
            float dist = EditController.ExtrudeDistance;
            if (ImGui.DragFloat("Distance", ref dist, 0.05f))
            {
                EditController.SetExtrudeDistance(Scene, dist);
            }

            ImGui.Separator();
            if (ImGui.Button("Confirm (Enter)", new Vector2(120, 0)))
            {
                EditController.ConfirmExtrude(Scene, UndoStack);
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel (Esc)", new Vector2(120, 0)))
            {
                EditController.CancelExtrude(Scene, UndoStack);
            }

            ImGui.End();
        }

        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor();

        if (!open)
        {
            EditController.CancelExtrude(Scene, UndoStack);
        }
    }

    private void DrawInsetOverlay(Vector2 viewportMin, Vector2 viewportSize)
    {
        const float overlayWidth = 280.0f;
        const float margin = 12.0f;

        Vector2 overlayPos = new(viewportMin.X + margin, viewportMin.Y + viewportSize.Y - 190.0f);
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

        bool open = true;
        if (ImGui.Begin("Inset Faces###InsetOverlay", ref open, flags))
        {
            float thick = EditController.InsetThickness;
            float depth = EditController.InsetDepth;
            bool indiv = EditController.InsetIndividual;
            bool changed = false;

            if (ImGui.DragFloat("Thickness", ref thick, 0.02f, 0.0f, 10.0f)) changed = true;
            if (ImGui.DragFloat("Depth", ref depth, 0.02f, -10.0f, 10.0f)) changed = true;
            if (ImGui.Checkbox("Individual Faces", ref indiv)) changed = true;

            if (changed)
            {
                EditController.SetInsetParameters(Scene, thick, depth, indiv);
            }

            ImGui.Separator();
            if (ImGui.Button("Confirm (Enter)", new Vector2(120, 0)))
            {
                EditController.ConfirmInset(Scene, UndoStack);
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel (Esc)", new Vector2(120, 0)))
            {
                EditController.CancelInset(Scene, UndoStack);
            }

            ImGui.End();
        }

        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor();

        if (!open)
        {
            EditController.CancelInset(Scene, UndoStack);
        }
    }

    private void DrawSubdividePopup()
    {
        if (ImGui.BeginPopup("SubdividePopup"))
        {
            ImGui.TextDisabled("Subdivide Edges");
            ImGui.Separator();
            ImGui.SliderInt("Cuts", ref _subdivideCuts, 1, 16);
            if (ImGui.Button("Apply", new Vector2(100, 0)))
            {
                EditController.ExecuteSubdivideEdges(Scene, Selection, _subdivideCuts, UndoStack);
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(100, 0)))
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    private void DrawWeldVerticesPopup()
    {
        if (ImGui.BeginPopup("WeldVerticesPopup"))
        {
            ImGui.TextDisabled("Weld / Merge Vertices (M)");
            ImGui.Separator();
            if (ImGui.Button("Weld at Center", new Vector2(-1, 0)))
            {
                EditController.ExecuteWeldCenter(Scene, Selection, UndoStack);
                ImGui.CloseCurrentPopup();
            }
            ImGui.Separator();
            ImGui.DragFloat("Threshold", ref _weldThreshold, 0.001f, 0.0001f, 1.0f, "%.4f m");
            if (ImGui.Button("Weld by Distance", new Vector2(-1, 0)))
            {
                EditController.ExecuteWeldDistance(Scene, Selection, _weldThreshold, UndoStack);
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    private void DrawMirrorPopup()
    {
        if (ImGui.BeginPopup("MirrorPopup"))
        {
            ImGui.TextDisabled("Mirror Modifier");
            ImGui.Separator();
            int axisInt = (int)_mirrorAxis;
            if (ImGui.RadioButton("X", axisInt == 0)) _mirrorAxis = Axis3D.X;
            ImGui.SameLine();
            if (ImGui.RadioButton("Y", axisInt == 1)) _mirrorAxis = Axis3D.Y;
            ImGui.SameLine();
            if (ImGui.RadioButton("Z", axisInt == 2)) _mirrorAxis = Axis3D.Z;

            ImGui.DragFloat("Plane Coord", ref _mirrorPlaneCoord, 0.1f);
            ImGui.Checkbox("Keep Original (Symmetric)", ref _mirrorKeepOriginal);
            ImGui.DragFloat("Weld Threshold", ref _mirrorWeldThreshold, 0.001f, 0.0001f, 0.1f, "%.4f");

            if (ImGui.Button("Apply", new Vector2(100, 0)))
            {
                EditController.ExecuteMirror(Scene, Selection, _mirrorAxis, _mirrorPlaneCoord, _mirrorKeepOriginal, _mirrorWeldThreshold, UndoStack);
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(100, 0)))
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    private void DrawLinearArrayPopup()
    {
        if (ImGui.BeginPopup("LinearArrayPopup"))
        {
            ImGui.TextDisabled("Linear Array");
            ImGui.Separator();
            ImGui.SliderInt("Count", ref _linearArrayCount, 2, 64);
            Vector3 offset = _linearArrayOffset;
            if (ImGui.DragFloat3("Step Offset", ref offset, 0.1f))
            {
                _linearArrayOffset = offset;
            }

            if (ImGui.Button("Apply", new Vector2(100, 0)))
            {
                EditController.ExecuteLinearArray(Scene, Selection, _linearArrayCount, _linearArrayOffset, UndoStack);
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(100, 0)))
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    private void DrawRadialArrayPopup()
    {
        if (ImGui.BeginPopup("RadialArrayPopup"))
        {
            ImGui.TextDisabled("Radial Array");
            ImGui.Separator();
            ImGui.SliderInt("Count", ref _radialArrayCount, 3, 64);
            int axisInt = (int)_radialArrayAxis;
            if (ImGui.RadioButton("X", axisInt == 0)) _radialArrayAxis = Axis3D.X;
            ImGui.SameLine();
            if (ImGui.RadioButton("Y", axisInt == 1)) _radialArrayAxis = Axis3D.Y;
            ImGui.SameLine();
            if (ImGui.RadioButton("Z", axisInt == 2)) _radialArrayAxis = Axis3D.Z;

            Vector3 piv = _radialArrayPivot;
            if (ImGui.DragFloat3("Pivot", ref piv, 0.1f))
            {
                _radialArrayPivot = piv;
            }

            ImGui.SliderFloat("Angle", ref _radialArrayAngle, 1.0f, 360.0f, "%.0f°");

            if (ImGui.Button("Apply", new Vector2(100, 0)))
            {
                EditController.ExecuteRadialArray(Scene, Selection, _radialArrayCount, _radialArrayAxis, _radialArrayPivot, _radialArrayAngle, UndoStack);
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(100, 0)))
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    private void DrawEnvironmentLightingPopup()
    {
        if (ImGui.BeginPopup("EnvironmentLightingPopup"))
        {
            ImGui.TextDisabled("Environment & Fog Settings");
            ImGui.Separator();

            // Ambient lighting
            ImGui.Text("Ambient Lighting:");
            Vector3 ambRgb = EnvironmentSettings.AmbientColorNormalized;
            if (ImGui.ColorEdit3("Ambient Color##ambCol", ref ambRgb))
            {
                EnvironmentSettings.AmbientColor = new ColorRgba(
                    (byte)System.Math.Clamp((int)(ambRgb.X * 255.0f), 0, 255),
                    (byte)System.Math.Clamp((int)(ambRgb.Y * 255.0f), 0, 255),
                    (byte)System.Math.Clamp((int)(ambRgb.Z * 255.0f), 0, 255),
                    255);
            }

            float ambIntensity = EnvironmentSettings.AmbientIntensity;
            if (ImGui.SliderFloat("Intensity##ambInt", ref ambIntensity, 0.0f, 2.0f, "%.2f"))
            {
                EnvironmentSettings.AmbientIntensity = ambIntensity;
            }

            ImGui.Separator();

            // Distance fog
            ImGui.Text("Distance Fog:");
            bool fogEnabled = EnvironmentSettings.FogEnabled;
            if (ImGui.Checkbox("Enable Fog##fogEn", ref fogEnabled))
            {
                EnvironmentSettings.FogEnabled = fogEnabled;
            }

            Vector3 fogRgb = EnvironmentSettings.FogColorNormalized;
            if (ImGui.ColorEdit3("Fog Color##fogCol", ref fogRgb))
            {
                EnvironmentSettings.FogColor = new ColorRgba(
                    (byte)System.Math.Clamp((int)(fogRgb.X * 255.0f), 0, 255),
                    (byte)System.Math.Clamp((int)(fogRgb.Y * 255.0f), 0, 255),
                    (byte)System.Math.Clamp((int)(fogRgb.Z * 255.0f), 0, 255),
                    255);
            }

            float fogDensity = EnvironmentSettings.FogDensity;
            if (ImGui.DragFloat("Density##fogDens", ref fogDensity, 0.001f, 0.0001f, 0.2f, "%.4f"))
            {
                EnvironmentSettings.FogDensity = fogDensity;
            }

            ImGui.Separator();

            if (ImGui.Button("Reset Defaults", new Vector2(120, 0)))
            {
                EnvironmentSettings.ResetDefaults();
            }
            ImGui.SameLine();
            if (ImGui.Button("Close", new Vector2(80, 0)))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void DrawNormalsContextMenu()
    {
        if (ImGui.BeginPopup("NormalsContextMenu"))
        {
            ImGui.TextDisabled("Mesh Normals (Alt+N)");
            ImGui.Separator();
            if (ImGui.MenuItem("Flip Selected Normals"))
            {
                EditController.ExecuteFlipNormals(Scene, Selection, UndoStack);
            }
            if (ImGui.MenuItem("Recalculate Outside"))
            {
                EditController.ExecuteRecalculateNormals(Scene, Selection, insideOut: false, UndoStack);
            }
            if (ImGui.MenuItem("Recalculate Inside / Hollow Room"))
            {
                EditController.ExecuteRecalculateNormals(Scene, Selection, insideOut: true, UndoStack);
            }
            ImGui.EndPopup();
        }
    }

    private void DrawDeleteContextMenu()
    {
        if (ImGui.BeginPopup("DeleteContextMenu"))
        {
            ImGui.TextDisabled("Delete / Dissolve (X / Del)");
            ImGui.Separator();
            if (Selection.Mode == InteractionMode.Face)
            {
                if (ImGui.MenuItem("Delete Faces (Delete)"))
                {
                    EditController.ExecuteDeleteFaces(Scene, Selection, UndoStack);
                }
                if (ImGui.MenuItem("Dissolve Faces (X)"))
                {
                    EditController.ExecuteDissolveFaces(Scene, Selection, UndoStack);
                }
            }
            else if (Selection.Mode == InteractionMode.Vertex)
            {
                if (ImGui.MenuItem("Delete / Weld Vertices (Delete)"))
                {
                    EditController.ExecuteWeldCenter(Scene, Selection, UndoStack);
                }
            }
            else if (Selection.Mode == InteractionMode.Edge)
            {
                if (ImGui.MenuItem("Delete Edges (Delete)"))
                {
                    EditController.ExecuteSubdivideEdges(Scene, Selection, 1, UndoStack);
                }
            }
            else
            {
                if (ImGui.MenuItem("Delete Objects (Delete)"))
                {
                    EditController.ExecuteDeleteObjects(Scene, Selection, UndoStack);
                }
            }
            ImGui.EndPopup();
        }
    }

    private void DrawViewportContextMenu()
    {
        if (ImGui.BeginPopup("ViewportContextMenu"))
        {
            ImGui.TextDisabled($"Context Menu ({Selection.Mode})");
            ImGui.Separator();

            if (Selection.Mode == InteractionMode.Object)
            {
                if (ImGui.MenuItem("Quick Clone", "Shift+D"))
                {
                    if (EditController.ExecuteQuickClone(Scene, Selection, UndoStack))
                    {
                        Ray3D ray = Camera.ScreenPointToRay(_viewportLocalMousePos, _viewportRenderer.ViewportSize);
                        ModalController.Start(ModalTransformMode.Move, Scene, Selection, ray, Camera.GetForward(), UndoStack);
                    }
                }
                if (ImGui.MenuItem("Mirror..."))
                {
                    ShortcutHandler.OpenMirrorPopupRequested = true;
                }
                if (ImGui.MenuItem("Linear Array..."))
                {
                    ShortcutHandler.OpenLinearArrayPopupRequested = true;
                }
                if (ImGui.MenuItem("Radial Array..."))
                {
                    ShortcutHandler.OpenRadialArrayPopupRequested = true;
                }
                ImGui.Separator();
                if (ImGui.MenuItem("CSG Union", "Ctrl+=", false, Selection.SelectedObjectIds.Count >= 2))
                {
                    ExecuteCsgUnion();
                }
                if (ImGui.MenuItem("CSG Subtract / Carve", "Ctrl+-", false, Selection.SelectedObjectIds.Count >= 2))
                {
                    ExecuteCsgSubtract();
                }
                if (ImGui.MenuItem("CSG Intersect", "Ctrl+8", false, Selection.SelectedObjectIds.Count >= 2))
                {
                    ExecuteCsgIntersect();
                }
                if (ImGui.MenuItem("TrenchBroom Carve Overlapping", "Ctrl+Shift+C", false, Selection.HasSelection))
                {
                    ExecuteCsgCarve();
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Delete Selected", "Del / X"))
                {
                    EditController.ExecuteDeleteObjects(Scene, Selection, UndoStack);
                }
            }
            else if (Selection.Mode == InteractionMode.Vertex)
            {
                if (ImGui.MenuItem("Connect Vertices", "J"))
                {
                    EditController.ExecuteConnectVertices(Scene, Selection, UndoStack);
                }
                if (ImGui.MenuItem("Fill Face", "F"))
                {
                    EditController.ExecuteFillFace(Scene, Selection, UndoStack);
                }
                if (ImGui.MenuItem("Weld Vertices...", "M"))
                {
                    ShortcutHandler.OpenWeldPopupRequested = true;
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Delete Vertices", "Del / X"))
                {
                    ShortcutHandler.OpenDeletePopupRequested = true;
                }
            }
            else if (Selection.Mode == InteractionMode.Edge)
            {
                if (ImGui.MenuItem("Extrude Edges", "E"))
                {
                    Ray3D ray = Camera.ScreenPointToRay(_viewportLocalMousePos, _viewportRenderer.ViewportSize);
                    EditController.StartExtrude(Scene, Selection, ray, Camera.GetForward(), UndoStack);
                }
                if (ImGui.MenuItem("Subdivide Edges..."))
                {
                    ShortcutHandler.OpenSubdividePopupRequested = true;
                }
                if (ImGui.MenuItem("Fill Face", "F"))
                {
                    EditController.ExecuteFillFace(Scene, Selection, UndoStack);
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Delete Edges", "Del / X"))
                {
                    ShortcutHandler.OpenDeletePopupRequested = true;
                }
            }
            else if (Selection.Mode == InteractionMode.Face)
            {
                if (ImGui.MenuItem("Extrude Faces", "E"))
                {
                    Ray3D ray = Camera.ScreenPointToRay(_viewportLocalMousePos, _viewportRenderer.ViewportSize);
                    EditController.StartExtrude(Scene, Selection, ray, Camera.GetForward(), UndoStack);
                }
                if (ImGui.MenuItem("Inset Faces", "I"))
                {
                    EditController.StartInset(Scene, Selection, ImGui.GetMousePos(), UndoStack);
                }
                if (ImGui.MenuItem("Quick Clone Faces", "Shift+D"))
                {
                    if (EditController.ExecuteQuickClone(Scene, Selection, UndoStack))
                    {
                        Ray3D ray = Camera.ScreenPointToRay(_viewportLocalMousePos, _viewportRenderer.ViewportSize);
                        ModalController.Start(ModalTransformMode.Move, Scene, Selection, ray, Camera.GetForward(), UndoStack);
                    }
                }
                if (ImGui.MenuItem("Dissolve Faces", "X"))
                {
                    EditController.ExecuteDissolveFaces(Scene, Selection, UndoStack);
                }
                if (ImGui.MenuItem("Delete Faces", "Del"))
                {
                    EditController.ExecuteDeleteFaces(Scene, Selection, UndoStack);
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Normals Menu...", "Alt+N"))
                {
                    ShortcutHandler.OpenNormalsPopupRequested = true;
                }
                if (ImGui.MenuItem("Subdivide..."))
                {
                    ShortcutHandler.OpenSubdividePopupRequested = true;
                }
            }

            ImGui.EndPopup();
        }
    }

    public void Dispose()
    {
        TextureCache.Dispose();
        _viewportRenderer.Dispose();
        RlImGui.Shutdown();
        if (Raylib.IsWindowReady())
        {
            Raylib.CloseWindow();
        }
    }
}
