using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using Raylib_cs;

namespace Leed3D.Editor.UI;

/// <summary>
/// Self-contained renderer and input bridge between Raylib-cs (8.x) and ImGui.NET.
/// Manages context creation, DockingEnable configuration, font texture atlas upload,
/// keyboard/mouse/character input forwarding, and Rlgl draw-list rendering with scissoring.
/// </summary>
public static unsafe class RlImGui
{
    private static IntPtr _imGuiContext;
    private static Texture2D _fontTexture;
    private static readonly Dictionary<uint, Texture2D> _boundTextures = new();

    private static readonly (KeyboardKey RaylibKey, ImGuiKey ImGuiKey)[] KeyMappings =
    [
        (KeyboardKey.Tab, ImGuiKey.Tab),
        (KeyboardKey.Left, ImGuiKey.LeftArrow),
        (KeyboardKey.Right, ImGuiKey.RightArrow),
        (KeyboardKey.Up, ImGuiKey.UpArrow),
        (KeyboardKey.Down, ImGuiKey.DownArrow),
        (KeyboardKey.PageUp, ImGuiKey.PageUp),
        (KeyboardKey.PageDown, ImGuiKey.PageDown),
        (KeyboardKey.Home, ImGuiKey.Home),
        (KeyboardKey.End, ImGuiKey.End),
        (KeyboardKey.Insert, ImGuiKey.Insert),
        (KeyboardKey.Delete, ImGuiKey.Delete),
        (KeyboardKey.Backspace, ImGuiKey.Backspace),
        (KeyboardKey.Space, ImGuiKey.Space),
        (KeyboardKey.Enter, ImGuiKey.Enter),
        (KeyboardKey.Escape, ImGuiKey.Escape),
        (KeyboardKey.Apostrophe, ImGuiKey.Apostrophe),
        (KeyboardKey.Comma, ImGuiKey.Comma),
        (KeyboardKey.Minus, ImGuiKey.Minus),
        (KeyboardKey.Period, ImGuiKey.Period),
        (KeyboardKey.Slash, ImGuiKey.Slash),
        (KeyboardKey.Semicolon, ImGuiKey.Semicolon),
        (KeyboardKey.Equal, ImGuiKey.Equal),
        (KeyboardKey.LeftBracket, ImGuiKey.LeftBracket),
        (KeyboardKey.Backslash, ImGuiKey.Backslash),
        (KeyboardKey.RightBracket, ImGuiKey.RightBracket),
        (KeyboardKey.Grave, ImGuiKey.GraveAccent),
        (KeyboardKey.CapsLock, ImGuiKey.CapsLock),
        (KeyboardKey.ScrollLock, ImGuiKey.ScrollLock),
        (KeyboardKey.NumLock, ImGuiKey.NumLock),
        (KeyboardKey.PrintScreen, ImGuiKey.PrintScreen),
        (KeyboardKey.Pause, ImGuiKey.Pause),
        (KeyboardKey.Kp0, ImGuiKey.Keypad0),
        (KeyboardKey.Kp1, ImGuiKey.Keypad1),
        (KeyboardKey.Kp2, ImGuiKey.Keypad2),
        (KeyboardKey.Kp3, ImGuiKey.Keypad3),
        (KeyboardKey.Kp4, ImGuiKey.Keypad4),
        (KeyboardKey.Kp5, ImGuiKey.Keypad5),
        (KeyboardKey.Kp6, ImGuiKey.Keypad6),
        (KeyboardKey.Kp7, ImGuiKey.Keypad7),
        (KeyboardKey.Kp8, ImGuiKey.Keypad8),
        (KeyboardKey.Kp9, ImGuiKey.Keypad9),
        (KeyboardKey.KpDecimal, ImGuiKey.KeypadDecimal),
        (KeyboardKey.KpDivide, ImGuiKey.KeypadDivide),
        (KeyboardKey.KpMultiply, ImGuiKey.KeypadMultiply),
        (KeyboardKey.KpSubtract, ImGuiKey.KeypadSubtract),
        (KeyboardKey.KpAdd, ImGuiKey.KeypadAdd),
        (KeyboardKey.KpEnter, ImGuiKey.KeypadEnter),
        (KeyboardKey.KpEqual, ImGuiKey.KeypadEqual),
        (KeyboardKey.LeftShift, ImGuiKey.LeftShift),
        (KeyboardKey.LeftControl, ImGuiKey.LeftCtrl),
        (KeyboardKey.LeftAlt, ImGuiKey.LeftAlt),
        (KeyboardKey.LeftSuper, ImGuiKey.LeftSuper),
        (KeyboardKey.RightShift, ImGuiKey.RightShift),
        (KeyboardKey.RightControl, ImGuiKey.RightCtrl),
        (KeyboardKey.RightAlt, ImGuiKey.RightAlt),
        (KeyboardKey.RightSuper, ImGuiKey.RightSuper),
        (KeyboardKey.Zero, ImGuiKey._0),
        (KeyboardKey.One, ImGuiKey._1),
        (KeyboardKey.Two, ImGuiKey._2),
        (KeyboardKey.Three, ImGuiKey._3),
        (KeyboardKey.Four, ImGuiKey._4),
        (KeyboardKey.Five, ImGuiKey._5),
        (KeyboardKey.Six, ImGuiKey._6),
        (KeyboardKey.Seven, ImGuiKey._7),
        (KeyboardKey.Eight, ImGuiKey._8),
        (KeyboardKey.Nine, ImGuiKey._9),
        (KeyboardKey.A, ImGuiKey.A),
        (KeyboardKey.B, ImGuiKey.B),
        (KeyboardKey.C, ImGuiKey.C),
        (KeyboardKey.D, ImGuiKey.D),
        (KeyboardKey.E, ImGuiKey.E),
        (KeyboardKey.F, ImGuiKey.F),
        (KeyboardKey.G, ImGuiKey.G),
        (KeyboardKey.H, ImGuiKey.H),
        (KeyboardKey.I, ImGuiKey.I),
        (KeyboardKey.J, ImGuiKey.J),
        (KeyboardKey.K, ImGuiKey.K),
        (KeyboardKey.L, ImGuiKey.L),
        (KeyboardKey.M, ImGuiKey.M),
        (KeyboardKey.N, ImGuiKey.N),
        (KeyboardKey.O, ImGuiKey.O),
        (KeyboardKey.P, ImGuiKey.P),
        (KeyboardKey.Q, ImGuiKey.Q),
        (KeyboardKey.R, ImGuiKey.R),
        (KeyboardKey.S, ImGuiKey.S),
        (KeyboardKey.T, ImGuiKey.T),
        (KeyboardKey.U, ImGuiKey.U),
        (KeyboardKey.V, ImGuiKey.V),
        (KeyboardKey.W, ImGuiKey.W),
        (KeyboardKey.X, ImGuiKey.X),
        (KeyboardKey.Y, ImGuiKey.Y),
        (KeyboardKey.Z, ImGuiKey.Z),
        (KeyboardKey.F1, ImGuiKey.F1),
        (KeyboardKey.F2, ImGuiKey.F2),
        (KeyboardKey.F3, ImGuiKey.F3),
        (KeyboardKey.F4, ImGuiKey.F4),
        (KeyboardKey.F5, ImGuiKey.F5),
        (KeyboardKey.F6, ImGuiKey.F6),
        (KeyboardKey.F7, ImGuiKey.F7),
        (KeyboardKey.F8, ImGuiKey.F8),
        (KeyboardKey.F9, ImGuiKey.F9),
        (KeyboardKey.F10, ImGuiKey.F10),
        (KeyboardKey.F11, ImGuiKey.F11),
        (KeyboardKey.F12, ImGuiKey.F12)
    ];

    public static void Setup(bool darkTheme = true, bool enableDocking = true)
    {
        _imGuiContext = ImGui.CreateContext();
        ImGui.SetCurrentContext(_imGuiContext);

        ImGuiIOPtr io = ImGui.GetIO();
        if (enableDocking)
        {
            io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        }
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors;

        if (darkTheme)
        {
            ImGui.StyleColorsDark();
            ApplyEditorThemeTweaks();
        }
        else
        {
            ImGui.StyleColorsClassic();
        }

        ReloadFonts();
    }

    private static void ApplyEditorThemeTweaks()
    {
        ImGuiStylePtr style = ImGui.GetStyle();
        style.WindowRounding = 3.0f;
        style.FrameRounding = 2.0f;
        style.GrabRounding = 2.0f;
        style.TabRounding = 2.0f;
        style.WindowBorderSize = 1.0f;
        style.FrameBorderSize = 0.0f;
    }

    public static void ReloadFonts()
    {
        ImGuiIOPtr io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out byte* pixels, out int width, out int height, out _);

        Image image = new()
        {
            Data = pixels,
            Width = width,
            Height = height,
            Mipmaps = 1,
            Format = PixelFormat.UncompressedR8G8B8A8
        };

        if (_fontTexture.Id != 0)
        {
            _boundTextures.Remove(_fontTexture.Id);
            Raylib.UnloadTexture(_fontTexture);
        }

        _fontTexture = Raylib.LoadTextureFromImage(image);
        _boundTextures[_fontTexture.Id] = _fontTexture;
        io.Fonts.SetTexID(new IntPtr(_fontTexture.Id));
    }

    public static IntPtr RegisterTexture(Texture2D texture)
    {
        if (texture.Id != 0)
        {
            _boundTextures[texture.Id] = texture;
        }
        return new IntPtr(texture.Id);
    }

    public static void Begin(float? deltaTime = null)
    {
        ImGui.SetCurrentContext(_imGuiContext);
        ImGuiIOPtr io = ImGui.GetIO();

        if (Raylib.IsWindowFullscreen())
        {
            int monitor = Raylib.GetCurrentMonitor();
            io.DisplaySize = new Vector2(Raylib.GetMonitorWidth(monitor), Raylib.GetMonitorHeight(monitor));
        }
        else
        {
            io.DisplaySize = new Vector2(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
        }

        Vector2 scale = Raylib.GetWindowScaleDPI();
        io.DisplayFramebufferScale = new Vector2(
            scale.X > 0.0f ? scale.X : 1.0f,
            scale.Y > 0.0f ? scale.Y : 1.0f);

        float dt = deltaTime ?? Raylib.GetFrameTime();
        io.DeltaTime = dt > 0.0f ? dt : (1.0f / 60.0f);

        UpdateMouse(io);
        UpdateKeyboard(io);

        ImGui.NewFrame();
    }

    private static void UpdateMouse(ImGuiIOPtr io)
    {
        Vector2 mousePos = Raylib.GetMousePosition();
        io.AddMousePosEvent(mousePos.X, mousePos.Y);

        io.AddMouseButtonEvent(0, Raylib.IsMouseButtonDown(MouseButton.Left));
        io.AddMouseButtonEvent(1, Raylib.IsMouseButtonDown(MouseButton.Right));
        io.AddMouseButtonEvent(2, Raylib.IsMouseButtonDown(MouseButton.Middle));
        io.AddMouseButtonEvent(3, Raylib.IsMouseButtonDown(MouseButton.Side));
        io.AddMouseButtonEvent(4, Raylib.IsMouseButtonDown(MouseButton.Extra));

        Vector2 wheel = Raylib.GetMouseWheelMoveV();
        if (wheel.X != 0.0f || wheel.Y != 0.0f)
        {
            io.AddMouseWheelEvent(wheel.X, wheel.Y);
        }
    }

    private static void UpdateKeyboard(ImGuiIOPtr io)
    {
        bool ctrl = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
        bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
        bool alt = Raylib.IsKeyDown(KeyboardKey.LeftAlt) || Raylib.IsKeyDown(KeyboardKey.RightAlt);
        bool super = Raylib.IsKeyDown(KeyboardKey.LeftSuper) || Raylib.IsKeyDown(KeyboardKey.RightSuper);

        io.AddKeyEvent(ImGuiKey.ModCtrl, ctrl);
        io.AddKeyEvent(ImGuiKey.ModShift, shift);
        io.AddKeyEvent(ImGuiKey.ModAlt, alt);
        io.AddKeyEvent(ImGuiKey.ModSuper, super);

        for (int i = 0; i < KeyMappings.Length; i++)
        {
            var (rlKey, imKey) = KeyMappings[i];
            if (Raylib.IsKeyPressed(rlKey))
            {
                io.AddKeyEvent(imKey, true);
            }
            else if (Raylib.IsKeyReleased(rlKey))
            {
                io.AddKeyEvent(imKey, false);
            }
        }

        int charPressed = Raylib.GetCharPressed();
        while (charPressed != 0)
        {
            io.AddInputCharacter((uint)charPressed);
            charPressed = Raylib.GetCharPressed();
        }
    }

    public static void End()
    {
        ImGui.SetCurrentContext(_imGuiContext);
        ImGui.Render();
        RenderDrawData(ImGui.GetDrawData());
    }

    private static void RenderDrawData(ImDrawDataPtr drawData)
    {
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableBackfaceCulling();

        Vector2 fbScale = drawData.FramebufferScale;
        if (fbScale.X <= 0.0f) fbScale.X = 1.0f;
        if (fbScale.Y <= 0.0f) fbScale.Y = 1.0f;

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            ImDrawListPtr cmdList = drawData.CmdLists[n];
            ImDrawVert* vtxBuffer = (ImDrawVert*)cmdList.VtxBuffer.Data;
            ushort* idxBuffer = (ushort*)cmdList.IdxBuffer.Data;

            for (int cmdi = 0; cmdi < cmdList.CmdBuffer.Size; cmdi++)
            {
                ImDrawCmdPtr pcmd = cmdList.CmdBuffer[cmdi];
                if (pcmd.UserCallback != IntPtr.Zero)
                {
                    continue;
                }

                Vector2 pos = drawData.DisplayPos;
                int rectX = (int)((pcmd.ClipRect.X - pos.X) * fbScale.X);
                int rectY = (int)((pcmd.ClipRect.Y - pos.Y) * fbScale.Y);
                int rectW = (int)((pcmd.ClipRect.Z - pcmd.ClipRect.X) * fbScale.X);
                int rectH = (int)((pcmd.ClipRect.W - pcmd.ClipRect.Y) * fbScale.Y);

                if (rectW <= 0 || rectH <= 0)
                {
                    continue;
                }

                Raylib.BeginScissorMode(rectX, rectY, rectW, rectH);
                RenderTriangles(pcmd.ElemCount, pcmd.IdxOffset, pcmd.VtxOffset, vtxBuffer, idxBuffer, pcmd.TextureId);
            }
        }

        Rlgl.SetTexture(0);
        Raylib.EndScissorMode();
        Rlgl.EnableBackfaceCulling();
    }

    private static void RenderTriangles(
        uint count,
        uint idxOffset,
        uint vtxOffset,
        ImDrawVert* vtxBuffer,
        ushort* idxBuffer,
        IntPtr texturePtr)
    {
        if (count < 3)
        {
            return;
        }

        uint textureId = (uint)texturePtr.ToInt64();
        Rlgl.Begin(DrawMode.Triangles);
        Rlgl.SetTexture(textureId);

        for (uint i = 0; i <= count - 3; i += 3)
        {
            if (Rlgl.CheckRenderBatchLimit(3))
            {
                Rlgl.Begin(DrawMode.Triangles);
                Rlgl.SetTexture(textureId);
            }

            ushort indexA = idxBuffer[idxOffset + i];
            ushort indexB = idxBuffer[idxOffset + i + 1];
            ushort indexC = idxBuffer[idxOffset + i + 2];

            ImDrawVert vertexA = vtxBuffer[vtxOffset + indexA];
            ImDrawVert vertexB = vtxBuffer[vtxOffset + indexB];
            ImDrawVert vertexC = vtxBuffer[vtxOffset + indexC];

            DrawTriangleVertex(vertexA);
            DrawTriangleVertex(vertexB);
            DrawTriangleVertex(vertexC);
        }

        Rlgl.End();
    }

    private static void DrawTriangleVertex(ImDrawVert vertex)
    {
        byte r = (byte)(vertex.col & 0xFF);
        byte g = (byte)((vertex.col >> 8) & 0xFF);
        byte b = (byte)((vertex.col >> 16) & 0xFF);
        byte a = (byte)((vertex.col >> 24) & 0xFF);

        Rlgl.Color4ub(r, g, b, a);
        Rlgl.TexCoord2f(vertex.uv.X, vertex.uv.Y);
        Rlgl.Vertex2f(vertex.pos.X, vertex.pos.Y);
    }

    /// <summary>
    /// Renders a Raylib RenderTexture2D inside an ImGui window (handling OpenGL vertical flip).
    /// </summary>
    public static void ImageRenderTexture(RenderTexture2D renderTexture, Vector2 size)
    {
        IntPtr texId = RegisterTexture(renderTexture.Texture);
        // OpenGL render textures have (0, 1) at top-left and (1, 0) at bottom-right
        ImGui.Image(texId, size, new Vector2(0.0f, 1.0f), new Vector2(1.0f, 0.0f));
    }

    public static void Shutdown()
    {
        if (_fontTexture.Id != 0)
        {
            Raylib.UnloadTexture(_fontTexture);
            _fontTexture = default;
        }
        _boundTextures.Clear();

        if (_imGuiContext != IntPtr.Zero)
        {
            ImGui.DestroyContext(_imGuiContext);
            _imGuiContext = IntPtr.Zero;
        }
    }
}
