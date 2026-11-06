using System.Numerics;
using ImGuiNET;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Selection;
using Leed3D.Kernel.Texturing;

namespace Leed3D.Editor.UI.Panels;

/// <summary>
/// ImGui panel providing fine-grained per-face UV inspection and editing:
/// UV Offset, Scale, Rotation, Fit-to-Face, World/Edge alignment, and Texture Lock toggles.
/// </summary>
public sealed class UvInspectorPanel
{
    public void Draw(EditorApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        ImGui.Begin("UV Inspector");

        List<(SceneObject Obj, PolygonFace Face)> targetFaces = GetTargetFaces(app);

        if (targetFaces.Count == 0)
        {
            ImGui.TextDisabled("Select an object or face(s) to inspect UVs.");
            ImGui.End();
            return;
        }

        PolygonFace primaryFace = targetFaces[0].Face;
        FaceUvMapping uv = primaryFace.UvMapping;

        ImGui.Text($"Target: {targetFaces.Count} face(s) selected");
        ImGui.TextDisabled($"Material: {primaryFace.MaterialId}");
        ImGui.Separator();

        // 1. Texture Lock Toggle
        bool lockVal = uv.TextureLock;
        if (ImGui.Checkbox("Texture Lock", ref lockVal))
        {
            RecordAndMutate(app, "Toggle Texture Lock", (f) => f.UvMapping.TextureLock = lockVal);
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("When enabled, moving or rotating the geometry keeps the texture pinned in place.");
        }

        ImGui.Separator();

        // 2. UV Offset
        Vector2 offset = uv.Offset;
        if (ImGui.DragFloat2("Offset (U, V)", ref offset, 0.05f))
        {
            RecordAndMutate(app, "Adjust UV Offset", (f) => f.UvMapping.Offset = offset);
        }
        ImGui.SameLine();
        if (ImGui.Button("Reset##OffsetReset"))
        {
            RecordAndMutate(app, "Reset UV Offset", (f) => f.UvMapping.Offset = Vector2.Zero);
        }

        // 3. UV Scale
        Vector2 scale = uv.Scale;
        if (ImGui.DragFloat2("Scale (U, V)", ref scale, 0.05f, 0.01f, 100.0f))
        {
            RecordAndMutate(app, "Adjust UV Scale", (f) => f.UvMapping.Scale = scale);
        }

        // Quick scale buttons
        if (ImGui.Button("0.25x")) RecordAndMutate(app, "UV Scale 0.25x", (f) => f.UvMapping.Scale *= 0.25f);
        ImGui.SameLine();
        if (ImGui.Button("0.5x")) RecordAndMutate(app, "UV Scale 0.5x", (f) => f.UvMapping.Scale *= 0.5f);
        ImGui.SameLine();
        if (ImGui.Button("1x")) RecordAndMutate(app, "UV Scale 1x", (f) => f.UvMapping.Scale = Vector2.One);
        ImGui.SameLine();
        if (ImGui.Button("2x")) RecordAndMutate(app, "UV Scale 2x", (f) => f.UvMapping.Scale *= 2.0f);
        ImGui.SameLine();
        if (ImGui.Button("4x")) RecordAndMutate(app, "UV Scale 4x", (f) => f.UvMapping.Scale *= 4.0f);

        if (ImGui.Button("Flip U")) RecordAndMutate(app, "Flip UV Horizontal", (f) => f.UvMapping.Scale = new Vector2(-f.UvMapping.Scale.X, f.UvMapping.Scale.Y));
        ImGui.SameLine();
        if (ImGui.Button("Flip V")) RecordAndMutate(app, "Flip UV Vertical", (f) => f.UvMapping.Scale = new Vector2(f.UvMapping.Scale.X, -f.UvMapping.Scale.Y));

        ImGui.Separator();

        // 4. UV Rotation
        float rot = uv.RotationDegrees;
        if (ImGui.SliderFloat("Rotation (°)", ref rot, -180.0f, 180.0f, "%.1f°"))
        {
            RecordAndMutate(app, "Adjust UV Rotation", (f) => f.UvMapping.RotationDegrees = rot);
        }

        if (ImGui.Button("-90°")) RecordAndMutate(app, "UV Rotate -90", (f) => f.UvMapping.RotationDegrees = (f.UvMapping.RotationDegrees - 90.0f) % 360.0f);
        ImGui.SameLine();
        if (ImGui.Button("-45°")) RecordAndMutate(app, "UV Rotate -45", (f) => f.UvMapping.RotationDegrees = (f.UvMapping.RotationDegrees - 45.0f) % 360.0f);
        ImGui.SameLine();
        if (ImGui.Button("0°")) RecordAndMutate(app, "UV Rotate Reset", (f) => f.UvMapping.RotationDegrees = 0.0f);
        ImGui.SameLine();
        if (ImGui.Button("+45°")) RecordAndMutate(app, "UV Rotate +45", (f) => f.UvMapping.RotationDegrees = (f.UvMapping.RotationDegrees + 45.0f) % 360.0f);
        ImGui.SameLine();
        if (ImGui.Button("+90°")) RecordAndMutate(app, "UV Rotate +90", (f) => f.UvMapping.RotationDegrees = (f.UvMapping.RotationDegrees + 90.0f) % 360.0f);

        ImGui.Separator();

        // 5. Alignment & Fit to Face
        ImGui.TextDisabled("Fit & Projection:");

        if (ImGui.Button("Fit to Face (1x1)", new Vector2(-1, 0)))
        {
            RecordAndMutate(app, "Fit to Face", (f, obj) => UvProjectionEngine.FitToFace(f, obj.Mesh!, obj.Transform, 1.0f, 1.0f));
        }

        if (ImGui.Button("Fit Width (U)"))
        {
            RecordAndMutate(app, "Fit Width", (f, obj) => UvProjectionEngine.FitToFace(f, obj.Mesh!, obj.Transform, 1.0f, f.UvMapping.Scale.Y));
        }
        ImGui.SameLine();
        if (ImGui.Button("Fit Height (V)"))
        {
            RecordAndMutate(app, "Fit Height", (f, obj) => UvProjectionEngine.FitToFace(f, obj.Mesh!, obj.Transform, f.UvMapping.Scale.X, 1.0f));
        }

        if (ImGui.Button("Align to World Plane (Box)", new Vector2(-1, 0)))
        {
            RecordAndMutate(app, "Align UV to World Box", (f, obj) => UvProjectionEngine.AlignToWorldBox(f, obj.Mesh!, obj.Transform));
        }

        if (ImGui.Button("Align to Face Edge", new Vector2(-1, 0)))
        {
            RecordAndMutate(app, "Align UV to Face Edge", (f, obj) => UvProjectionEngine.AlignToFacePlane(f, obj.Mesh!, obj.Transform));
        }

        ImGui.End();
    }

    private static List<(SceneObject Obj, PolygonFace Face)> GetTargetFaces(EditorApp app)
    {
        var list = new List<(SceneObject Obj, PolygonFace Face)>();

        if (app.Selection.Mode == InteractionMode.Face && app.Selection.ActiveObjectId.HasValue)
        {
            if (app.Scene.GetObject(app.Selection.ActiveObjectId.Value) is { Mesh: { } mesh } obj)
            {
                foreach (int fId in app.Selection.SelectedFaceIds)
                {
                    if (mesh.GetFace(fId) is { } face)
                    {
                        list.Add((obj, face));
                    }
                }
            }
        }
        else if (app.Selection.Mode == InteractionMode.Object)
        {
            foreach (int objId in app.Selection.SelectedObjectIds)
            {
                if (app.Scene.GetObject(objId) is { Mesh: { } mesh } obj)
                {
                    for (int f = 0; f < mesh.Faces.Count; f++)
                    {
                        list.Add((obj, mesh.Faces[f]));
                    }
                }
            }
        }

        return list;
    }

    private static void RecordAndMutate(EditorApp app, string description, Action<PolygonFace> action)
    {
        RecordAndMutate(app, description, (face, _) => action(face));
    }

    private static void RecordAndMutate(EditorApp app, string description, Action<PolygonFace, SceneObject> action)
    {
        EditorScene before = app.Scene.CloneSnapshot();
        List<(SceneObject Obj, PolygonFace Face)> targets = GetTargetFaces(app);

        foreach (var (obj, face) in targets)
        {
            action(face, obj);
        }

        app.UndoStack.RecordAction(description, before, app.Scene);
        app.StatusMessage = $"{description} applied to {targets.Count} face(s).";
    }
}
