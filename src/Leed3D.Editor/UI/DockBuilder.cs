using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;

namespace Leed3D.Editor.UI;

/// <summary>
/// Minimal bindings for cimgui's internal DockBuilder API, which ImGui.NET exports natively but does not wrap.
/// </summary>
internal static unsafe class DockBuilder
{
    private const string CimGuiLibrary = "cimgui";

    // ImGuiDockNodeFlags_DockSpace from imgui_internal.h.
    private const int DockSpaceNodeFlag = 1 << 10;

    public static bool NodeExists(uint nodeId) => igDockBuilderGetNode(nodeId) != IntPtr.Zero;

    public static void ResetDockSpace(uint dockSpaceId, Vector2 size)
    {
        igDockBuilderRemoveNode(dockSpaceId);
        igDockBuilderAddNode(dockSpaceId, DockSpaceNodeFlag);
        igDockBuilderSetNodeSize(dockSpaceId, size);
    }

    /// <summary>Splits <paramref name="nodeId"/> and returns the node on <paramref name="direction"/>.</summary>
    public static uint SplitNode(uint nodeId, ImGuiDir direction, float ratio, out uint remainingNodeId)
    {
        uint atDirection;
        uint opposite;
        igDockBuilderSplitNode(nodeId, direction, ratio, &atDirection, &opposite);
        remainingNodeId = opposite;
        return atDirection;
    }

    public static void DockWindow(string windowName, uint nodeId) => igDockBuilderDockWindow(windowName, nodeId);

    public static void Finish(uint dockSpaceId) => igDockBuilderFinish(dockSpaceId);

    [DllImport(CimGuiLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr igDockBuilderGetNode(uint nodeId);

    [DllImport(CimGuiLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern uint igDockBuilderAddNode(uint nodeId, int flags);

    [DllImport(CimGuiLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern void igDockBuilderRemoveNode(uint nodeId);

    [DllImport(CimGuiLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern void igDockBuilderSetNodeSize(uint nodeId, Vector2 size);

    [DllImport(CimGuiLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern uint igDockBuilderSplitNode(uint nodeId, ImGuiDir splitDir, float sizeRatioForNodeAtDir, uint* outIdAtDir, uint* outIdAtOppositeDir);

    [DllImport(CimGuiLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern void igDockBuilderDockWindow([MarshalAs(UnmanagedType.LPUTF8Str)] string windowName, uint nodeId);

    [DllImport(CimGuiLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern void igDockBuilderFinish(uint nodeId);
}
