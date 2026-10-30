using System.Numerics;
using ImGuiNET;
using Leed3D.Kernel.Gizmos;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Modifiers;
using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Selection;
using Leed3D.Kernel.Tools;
using Raylib_cs;

namespace Leed3D.Editor.UI;

/// <summary>
/// Dispatches global and viewport-specific keyboard shortcuts, modal operator hotkeys,
/// sub-object edit tools (Extrude, Inset, Weld, Connect, Fill, Delete, Normals, Clone),
/// and gizmo / camera navigation bindings.
/// </summary>
public sealed class EditorShortcutHandler
{
    private readonly EditorApp _app;

    // Track right mouse click position to distinguish click vs fly
    private Vector2 _rmbDownPos;
    private bool _rmbClickedInViewport;

    public bool OpenDeletePopupRequested { get; set; }
    public bool OpenNormalsPopupRequested { get; set; }
    public bool OpenWeldPopupRequested { get; set; }
    public bool OpenSubdividePopupRequested { get; set; }
    public bool OpenMirrorPopupRequested { get; set; }
    public bool OpenLinearArrayPopupRequested { get; set; }
    public bool OpenRadialArrayPopupRequested { get; set; }
    public bool OpenContextMenuRequested { get; set; }

    public EditorShortcutHandler(EditorApp app)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
    }

    /// <summary>
    /// Evaluates keyboard and mouse shortcut triggers for the current frame.
    /// </summary>
    public void HandleShortcuts(bool isViewportHovered, bool isViewportFocused, Vector2 viewportLocalMousePos, Vector2 viewportSize)
    {
        if (_app.IsWalkModeActive)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.F5) || Raylib.IsKeyPressed(KeyboardKey.Escape))
            {
                _app.ExitWalkMode();
            }
            return;
        }

        ImGuiIOPtr io = ImGui.GetIO();
        if (io.WantTextInput)
        {
            return;
        }

        bool ctrl = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
        bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
        bool alt = Raylib.IsKeyDown(KeyboardKey.LeftAlt) || Raylib.IsKeyDown(KeyboardKey.RightAlt);
        bool rmbDown = Raylib.IsMouseButtonDown(MouseButton.Right);

        // 1. Right mouse button click detection (context menu vs camera fly)
        if (isViewportHovered && Raylib.IsMouseButtonPressed(MouseButton.Right))
        {
            _rmbDownPos = ImGui.GetMousePos();
            _rmbClickedInViewport = true;
        }

        if (_rmbClickedInViewport && Raylib.IsMouseButtonReleased(MouseButton.Right))
        {
            _rmbClickedInViewport = false;
            float dist = Vector2.Distance(_rmbDownPos, ImGui.GetMousePos());
            if (dist < 4.0f && !_app.ModalController.IsActive && !_app.EditController.IsActive && !_app.Gizmo.IsDragging)
            {
                OpenContextMenuRequested = true;
            }
        }

        // 2. Active Interactive Edit Controller (Extrude / Inset) Shortcuts
        if (_app.EditController.IsActive)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.Escape) || Raylib.IsMouseButtonPressed(MouseButton.Right))
            {
                if (_app.EditController.IsExtrudeActive)
                {
                    _app.EditController.CancelExtrude(_app.Scene, _app.UndoStack);
                    _app.StatusMessage = "Cancelled extrude operation.";
                }
                else if (_app.EditController.IsInsetActive)
                {
                    _app.EditController.CancelInset(_app.Scene, _app.UndoStack);
                    _app.StatusMessage = "Cancelled inset operation.";
                }
                return;
            }

            if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter) || Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                if (_app.EditController.IsExtrudeActive)
                {
                    _app.EditController.ConfirmExtrude(_app.Scene, _app.UndoStack);
                    _app.StatusMessage = "Committed extrude operation.";
                }
                else if (_app.EditController.IsInsetActive)
                {
                    _app.EditController.ConfirmInset(_app.Scene, _app.UndoStack);
                    _app.StatusMessage = "Committed inset operation.";
                }
                return;
            }

            HandleEditNumericInput();
            _app.StatusMessage = _app.EditController.GetStatusReadout();
            return;
        }

        // 3. Active Modal Transform Controller (G / R / S) Shortcuts
        if (_app.ModalController.IsActive)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.Escape) || Raylib.IsMouseButtonPressed(MouseButton.Right))
            {
                _app.ModalController.Cancel(_app.Scene, _app.UndoStack);
                _app.StatusMessage = "Cancelled modal transform.";
                return;
            }

            if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter) || Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                _app.ModalController.Confirm(_app.Scene, _app.UndoStack);
                _app.PrimitiveController.Dismiss();
                _app.StatusMessage = "Committed modal transform.";
                return;
            }

            if (Raylib.IsKeyPressed(KeyboardKey.X))
            {
                _app.ModalController.HandleAxisKey('X', shift, _app.Scene);
            }
            else if (Raylib.IsKeyPressed(KeyboardKey.Y))
            {
                _app.ModalController.HandleAxisKey('Y', shift, _app.Scene);
            }
            else if (Raylib.IsKeyPressed(KeyboardKey.Z))
            {
                _app.ModalController.HandleAxisKey('Z', shift, _app.Scene);
            }

            HandleModalNumericInput();
            _app.StatusMessage = _app.ModalController.GetStatusReadout();
            return;
        }

        // 4. Active Gizmo Drag Shortcuts
        if (_app.Gizmo.IsDragging)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.Escape) || Raylib.IsMouseButtonPressed(MouseButton.Right))
            {
                _app.Gizmo.CancelDrag();
                _app.Applicator.CancelSession(_app.Scene, _app.UndoStack);
                _app.StatusMessage = "Cancelled gizmo transform.";
                return;
            }

            if (Raylib.IsKeyPressed(KeyboardKey.X))
            {
                _app.Gizmo.SetConstraintOverride(shift ? GizmoAxisConstraint.YZ : GizmoAxisConstraint.X);
            }
            else if (Raylib.IsKeyPressed(KeyboardKey.Y))
            {
                _app.Gizmo.SetConstraintOverride(shift ? GizmoAxisConstraint.XZ : GizmoAxisConstraint.Y);
            }
            else if (Raylib.IsKeyPressed(KeyboardKey.Z))
            {
                _app.Gizmo.SetConstraintOverride(shift ? GizmoAxisConstraint.XY : GizmoAxisConstraint.Z);
            }

            return;
        }

        // 5. Undo (Ctrl+Z) & Redo (Ctrl+Y or Ctrl+Shift+Z)
        if (ctrl && !shift && Raylib.IsKeyPressed(KeyboardKey.Z))
        {
            if (_app.UndoStack.Undo(_app.Scene))
            {
                _app.PrimitiveController.Dismiss();
                _app.StatusMessage = "Undid last action.";
            }
            return;
        }
        else if ((ctrl && Raylib.IsKeyPressed(KeyboardKey.Y)) ||
                 (ctrl && shift && Raylib.IsKeyPressed(KeyboardKey.Z)))
        {
            if (_app.UndoStack.Redo(_app.Scene))
            {
                _app.PrimitiveController.Dismiss();
                _app.StatusMessage = "Redid action.";
            }
            return;
        }

        // F4: Toggle Lit Preview mode
        if (Raylib.IsKeyPressed(KeyboardKey.F4))
        {
            _app.ToggleLitPreview();
            return;
        }

        // F5: Enter First-Person Walk Playtest mode
        if (Raylib.IsKeyPressed(KeyboardKey.F5))
        {
            _app.ToggleWalkMode();
            return;
        }

        // 6. Viewport Operations when not camera flying
        if (!rmbDown && (isViewportHovered || isViewportFocused))
        {
            // Tab: Toggle Object Mode <-> Edit Mode
            if (Raylib.IsKeyPressed(KeyboardKey.Tab))
            {
                _app.Selection.ToggleObjectEditMode(_app.Scene);
                _app.StatusMessage = $"Mode: {_app.Selection.Mode}.";
                return;
            }

            // 1, 2, 3: Vertex, Edge, Face select mode
            if (Raylib.IsKeyPressed(KeyboardKey.One) && !ctrl && !alt)
            {
                _app.Selection.SetMode(InteractionMode.Vertex, _app.Scene);
                _app.StatusMessage = "Vertex Select Mode (1).";
                return;
            }
            if (Raylib.IsKeyPressed(KeyboardKey.Two) && !ctrl && !alt)
            {
                _app.Selection.SetMode(InteractionMode.Edge, _app.Scene);
                _app.StatusMessage = "Edge Select Mode (2).";
                return;
            }
            if (Raylib.IsKeyPressed(KeyboardKey.Three) && !ctrl && !alt)
            {
                _app.Selection.SetMode(InteractionMode.Face, _app.Scene);
                _app.StatusMessage = "Face Select Mode (3).";
                return;
            }

            // W: Gizmo Translate Mode
            if (!ctrl && !alt && !shift && Raylib.IsKeyPressed(KeyboardKey.W))
            {
                _app.Gizmo.ToolMode = GizmoToolMode.Translate;
                _app.StatusMessage = "Gizmo Tool: Translate (W).";
                return;
            }

            // Blender-style modal transform operators (G Grab/Move, R Rotate, S Scale)
            if (!ctrl && !alt && !shift && _app.Selection.HasSelection)
            {
                if (Raylib.IsKeyPressed(KeyboardKey.G))
                {
                    Ray3D ray = _app.Camera.ScreenPointToRay(viewportLocalMousePos, viewportSize);
                    if (_app.ModalController.Start(ModalTransformMode.Move, _app.Scene, _app.Selection, ray, _app.Camera.GetForward(), _app.UndoStack))
                    {
                        _app.PrimitiveController.Dismiss();
                        _app.StatusMessage = _app.ModalController.GetStatusReadout();
                        return;
                    }
                }
                else if (Raylib.IsKeyPressed(KeyboardKey.R))
                {
                    Ray3D ray = _app.Camera.ScreenPointToRay(viewportLocalMousePos, viewportSize);
                    if (_app.ModalController.Start(ModalTransformMode.Rotate, _app.Scene, _app.Selection, ray, _app.Camera.GetForward(), _app.UndoStack))
                    {
                        _app.PrimitiveController.Dismiss();
                        _app.StatusMessage = _app.ModalController.GetStatusReadout();
                        return;
                    }
                }
                else if (Raylib.IsKeyPressed(KeyboardKey.S))
                {
                    Ray3D ray = _app.Camera.ScreenPointToRay(viewportLocalMousePos, viewportSize);
                    if (_app.ModalController.Start(ModalTransformMode.Scale, _app.Scene, _app.Selection, ray, _app.Camera.GetForward(), _app.UndoStack))
                    {
                        _app.PrimitiveController.Dismiss();
                        _app.StatusMessage = _app.ModalController.GetStatusReadout();
                        return;
                    }
                }
            }

            // E: Extrude selected faces or edges (in Edit Mode)
            if (!ctrl && !alt && !shift && Raylib.IsKeyPressed(KeyboardKey.E))
            {
                if (_app.Selection.Mode is InteractionMode.Face or InteractionMode.Edge)
                {
                    Ray3D ray = _app.Camera.ScreenPointToRay(viewportLocalMousePos, viewportSize);
                    if (_app.EditController.StartExtrude(_app.Scene, _app.Selection, ray, _app.Camera.GetForward(), _app.UndoStack))
                    {
                        _app.PrimitiveController.Dismiss();
                        _app.StatusMessage = _app.EditController.GetStatusReadout();
                        return;
                    }
                }
                else if (_app.Selection.Mode == InteractionMode.Object)
                {
                    _app.Gizmo.ToolMode = GizmoToolMode.Rotate;
                    _app.StatusMessage = "Gizmo Tool: Rotate (E).";
                    return;
                }
            }

            // I: Inset selected faces
            if (!ctrl && !alt && !shift && Raylib.IsKeyPressed(KeyboardKey.I))
            {
                if (_app.Selection.Mode == InteractionMode.Face)
                {
                    if (_app.EditController.StartInset(_app.Scene, _app.Selection, ImGui.GetMousePos(), _app.UndoStack))
                    {
                        _app.PrimitiveController.Dismiss();
                        _app.StatusMessage = _app.EditController.GetStatusReadout();
                        return;
                    }
                }
            }

            // M: Weld / Merge Vertices dialog
            if (!ctrl && !alt && !shift && Raylib.IsKeyPressed(KeyboardKey.M))
            {
                OpenWeldPopupRequested = true;
                return;
            }

            // J: Connect Vertices
            if (!ctrl && !alt && !shift && Raylib.IsKeyPressed(KeyboardKey.J))
            {
                if (_app.EditController.ExecuteConnectVertices(_app.Scene, _app.Selection, _app.UndoStack))
                {
                    _app.StatusMessage = "Connected vertices across face.";
                }
                else
                {
                    _app.StatusMessage = "Connect Vertices requires exactly 2 vertices sharing a face.";
                }
                return;
            }

            // F: Fill Face
            if (!ctrl && !alt && !shift && Raylib.IsKeyPressed(KeyboardKey.F))
            {
                if (_app.EditController.ExecuteFillFace(_app.Scene, _app.Selection, _app.UndoStack))
                {
                    _app.StatusMessage = "Created new polygon face.";
                }
                else
                {
                    _app.StatusMessage = "Fill Face requires 3+ coplanar vertices or boundary edges.";
                }
                return;
            }

            // Delete or X: Delete or context menu
            if (Raylib.IsKeyPressed(KeyboardKey.Delete) || (!ctrl && !alt && !shift && Raylib.IsKeyPressed(KeyboardKey.X)))
            {
                if (_app.Selection.Mode == InteractionMode.Object)
                {
                    if (_app.EditController.ExecuteDeleteObjects(_app.Scene, _app.Selection, _app.UndoStack))
                    {
                        _app.StatusMessage = "Deleted selected object(s).";
                    }
                }
                else
                {
                    OpenDeletePopupRequested = true;
                }
                return;
            }

            // Alt+N: Normals Menu
            if (alt && !ctrl && Raylib.IsKeyPressed(KeyboardKey.N))
            {
                OpenNormalsPopupRequested = true;
                return;
            }

            // Shift+D: Quick Clone & Modal Move
            if (shift && !ctrl && !alt && Raylib.IsKeyPressed(KeyboardKey.D))
            {
                if (_app.EditController.ExecuteQuickClone(_app.Scene, _app.Selection, _app.UndoStack))
                {
                    Ray3D ray = _app.Camera.ScreenPointToRay(viewportLocalMousePos, viewportSize);
                    _app.ModalController.Start(ModalTransformMode.Move, _app.Scene, _app.Selection, ray, _app.Camera.GetForward(), _app.UndoStack);
                    _app.StatusMessage = "Cloned selection. Moving (G)...";
                }
                return;
            }

            // Ctrl+I: Invert Selection
            if (ctrl && !alt && Raylib.IsKeyPressed(KeyboardKey.I))
            {
                _app.Selection.InvertSelection(_app.Scene);
                _app.StatusMessage = $"Inverted {_app.Selection.Mode} selection.";
                return;
            }

            // Alt+A: Clear Selection
            if (alt && Raylib.IsKeyPressed(KeyboardKey.A))
            {
                _app.Selection.ClearSelection();
                _app.StatusMessage = "Cleared selection (Alt+A).";
                return;
            }

            // A: Select All
            if (!ctrl && !alt && !shift && Raylib.IsKeyPressed(KeyboardKey.A))
            {
                _app.Selection.SelectAll(_app.Scene);
                _app.StatusMessage = $"Selected all ({_app.Selection.Mode} mode).";
                return;
            }

            // CSG Boolean Hotkeys (in Object Mode):
            // Ctrl+= : Union
            // Ctrl+- : Subtract / Carve
            // Ctrl+8 : Intersect
            // Ctrl+Shift+C : TrenchBroom Carve
            if (_app.Selection.Mode == InteractionMode.Object)
            {
                if (ctrl && shift && Raylib.IsKeyPressed(KeyboardKey.C))
                {
                    _app.ExecuteCsgCarve();
                    return;
                }

                if (ctrl && !shift && !alt)
                {
                    if (Raylib.IsKeyPressed(KeyboardKey.Equal) || Raylib.IsKeyPressed(KeyboardKey.KpAdd))
                    {
                        _app.ExecuteCsgUnion();
                        return;
                    }

                    if (Raylib.IsKeyPressed(KeyboardKey.Minus) || Raylib.IsKeyPressed(KeyboardKey.KpSubtract))
                    {
                        _app.ExecuteCsgSubtract();
                        return;
                    }

                    if (Raylib.IsKeyPressed(KeyboardKey.Eight) || Raylib.IsKeyPressed(KeyboardKey.KpMultiply))
                    {
                        _app.ExecuteCsgIntersect();
                        return;
                    }
                }
            }
        }
    }

    private void HandleEditNumericInput()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Backspace))
        {
            _app.EditController.HandleBackspace(_app.Scene);
            return;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Minus) || Raylib.IsKeyPressed(KeyboardKey.KpSubtract))
        {
            _app.EditController.HandleNumericKey('-', _app.Scene);
            return;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Period) || Raylib.IsKeyPressed(KeyboardKey.KpDecimal))
        {
            _app.EditController.HandleNumericKey('.', _app.Scene);
            return;
        }

        for (int i = 0; i <= 9; i++)
        {
            KeyboardKey mainKey = (KeyboardKey)((int)KeyboardKey.Zero + i);
            KeyboardKey kpKey = (KeyboardKey)((int)KeyboardKey.Kp0 + i);
            if (Raylib.IsKeyPressed(mainKey) || Raylib.IsKeyPressed(kpKey))
            {
                _app.EditController.HandleNumericKey((char)('0' + i), _app.Scene);
                return;
            }
        }
    }

    private void HandleModalNumericInput()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Backspace))
        {
            _app.ModalController.HandleBackspace(_app.Scene);
            return;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Minus) || Raylib.IsKeyPressed(KeyboardKey.KpSubtract))
        {
            _app.ModalController.HandleNumericKey('-', _app.Scene);
            return;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Period) || Raylib.IsKeyPressed(KeyboardKey.KpDecimal))
        {
            _app.ModalController.HandleNumericKey('.', _app.Scene);
            return;
        }

        for (int i = 0; i <= 9; i++)
        {
            KeyboardKey mainKey = (KeyboardKey)((int)KeyboardKey.Zero + i);
            KeyboardKey kpKey = (KeyboardKey)((int)KeyboardKey.Kp0 + i);
            if (Raylib.IsKeyPressed(mainKey) || Raylib.IsKeyPressed(kpKey))
            {
                _app.ModalController.HandleNumericKey((char)('0' + i), _app.Scene);
                return;
            }
        }
    }
}
