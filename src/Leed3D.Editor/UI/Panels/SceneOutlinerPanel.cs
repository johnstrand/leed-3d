using System.Numerics;
using ImGuiNET;
using Leed3D.Kernel.Entities;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Selection;

namespace Leed3D.Editor.UI.Panels;

/// <summary>
/// ImGui hierarchy panel displaying scene objects and gameplay entities with search,
/// visibility/lock toggles, inline renaming, selection, and transform/pivot inspector.
/// </summary>
public sealed class SceneOutlinerPanel
{
    private string _searchFilter = string.Empty;
    private int? _renamingObjectId;
    private string? _renamingEntityId;
    private string _renameBuffer = string.Empty;
    private bool _focusRename;

    private EditorScene? _dragSnapshot;

    public void Draw(EditorApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        DrawOutliner(app);
        DrawTransformInspector(app);
    }

    private void DrawOutliner(EditorApp app)
    {
        ImGui.Begin("Scene Outliner");

        // Search Filter
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##OutlinerSearch", "Search objects & entities...", ref _searchFilter, 128);

        ImGui.TextDisabled($"Scene: {app.Scene.Name} ({app.Scene.ObjectCount} objects, {app.Scene.EntityCount} entities)");
        ImGui.Separator();

        bool hasFilter = !string.IsNullOrWhiteSpace(_searchFilter);
        string filterLower = _searchFilter.Trim().ToLowerInvariant();

        // 1. Scene Objects Section
        if (ImGui.CollapsingHeader($"Objects ({app.Scene.ObjectCount})", ImGuiTreeNodeFlags.DefaultOpen))
        {
            for (int i = 0; i < app.Scene.Objects.Count; i++)
            {
                SceneObject obj = app.Scene.Objects[i];

                if (hasFilter && !obj.Name.ToLowerInvariant().Contains(filterLower) && !$"obj_{obj.Id}".Contains(filterLower))
                {
                    continue;
                }

                DrawObjectRow(app, obj);
            }
        }

        // 2. Gameplay Entities Section
        if (ImGui.CollapsingHeader($"Entities ({app.Scene.EntityCount})", ImGuiTreeNodeFlags.DefaultOpen))
        {
            for (int i = 0; i < app.Scene.Entities.Count; i++)
            {
                LevelEntity ent = app.Scene.Entities[i];

                if (hasFilter && !ent.Id.ToLowerInvariant().Contains(filterLower) && !ent.ClassName.ToLowerInvariant().Contains(filterLower))
                {
                    continue;
                }

                DrawEntityRow(app, ent);
            }
        }

        // Outliner Keyboard shortcuts when window is focused
        if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
        {
            if (ImGui.IsKeyPressed(ImGuiKey.F2))
            {
                if (app.Selection.ActiveObjectId.HasValue)
                {
                    SceneObject? activeObj = app.Scene.GetObject(app.Selection.ActiveObjectId.Value);
                    if (activeObj is not null) StartRenameObject(activeObj);
                }
                else if (!string.IsNullOrEmpty(app.Selection.ActiveEntityId))
                {
                    LevelEntity? activeEnt = app.Scene.GetEntity(app.Selection.ActiveEntityId);
                    if (activeEnt is not null) StartRenameEntity(activeEnt);
                }
            }
        }

        ImGui.End();
    }

    private void DrawObjectRow(EditorApp app, SceneObject obj)
    {
        ImGui.PushID($"ObjRow_{obj.Id}");

        // Visibility Toggle Button
        Vector4 visColor = obj.Visible ? new Vector4(0.8f, 0.8f, 0.8f, 1f) : new Vector4(0.4f, 0.4f, 0.4f, 0.5f);
        ImGui.PushStyleColor(ImGuiCol.Text, visColor);
        if (ImGui.Button(obj.Visible ? "[V]" : "[-]"))
        {
            obj.Visible = !obj.Visible;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(obj.Visible ? "Hide Object" : "Show Object");
        }
        ImGui.PopStyleColor();

        ImGui.SameLine();

        // Lock Toggle Button
        Vector4 lockColor = obj.Locked ? new Vector4(1.0f, 0.55f, 0.2f, 1f) : new Vector4(0.4f, 0.4f, 0.4f, 0.5f);
        ImGui.PushStyleColor(ImGuiCol.Text, lockColor);
        if (ImGui.Button(obj.Locked ? "[L]" : "[-]"))
        {
            obj.Locked = !obj.Locked;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(obj.Locked ? "Unlock Object" : "Lock Object");
        }
        ImGui.PopStyleColor();

        ImGui.SameLine();

        // Inline Rename or Selectable
        if (_renamingObjectId == obj.Id)
        {
            if (_focusRename)
            {
                ImGui.SetKeyboardFocusHere();
                _focusRename = false;
            }

            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##RenameInput", ref _renameBuffer, 64, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                CommitRenameObject(app, obj);
            }

            if (ImGui.IsItemDeactivated() && ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                CancelRename();
            }
        }
        else
        {
            bool isSelected = app.Selection.SelectedObjectIds.Contains(obj.Id);
            string label = $"{obj.Name} (V:{obj.Mesh?.VertexCount ?? 0}, F:{obj.Mesh?.FaceCount ?? 0})";

            if (ImGui.Selectable(label, isSelected))
            {
                if (ImGui.GetIO().KeyCtrl || ImGui.GetIO().KeyShift)
                {
                    app.Selection.ToggleSelectObject(obj.Id);
                }
                else
                {
                    app.Selection.SelectSingleObject(obj.Id);
                }
            }

            if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                StartRenameObject(obj);
            }

            // Context Menu
            if (ImGui.BeginPopupContextItem())
            {
                if (ImGui.MenuItem("Rename", "F2"))
                {
                    StartRenameObject(obj);
                }

                if (ImGui.MenuItem("Duplicate", "Shift+D"))
                {
                    DuplicateObject(app, obj);
                }

                if (ImGui.MenuItem("Delete", "Del"))
                {
                    DeleteObject(app, obj);
                }

                ImGui.Separator();

                if (ImGui.MenuItem(obj.Visible ? "Hide" : "Show"))
                {
                    obj.Visible = !obj.Visible;
                }

                if (ImGui.MenuItem(obj.Locked ? "Unlock" : "Lock"))
                {
                    obj.Locked = !obj.Locked;
                }

                ImGui.EndPopup();
            }
        }

        ImGui.PopID();
    }

    private void DrawEntityRow(EditorApp app, LevelEntity ent)
    {
        ImGui.PushID($"EntRow_{ent.Id}");

        // Visibility Button
        Vector4 visColor = ent.Visible ? new Vector4(0.8f, 0.8f, 0.8f, 1f) : new Vector4(0.4f, 0.4f, 0.4f, 0.5f);
        ImGui.PushStyleColor(ImGuiCol.Text, visColor);
        if (ImGui.Button(ent.Visible ? "[V]" : "[-]"))
        {
            ent.Visible = !ent.Visible;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(ent.Visible ? "Hide Entity" : "Show Entity");
        }
        ImGui.PopStyleColor();

        ImGui.SameLine();

        // Lock Button
        Vector4 lockColor = ent.Locked ? new Vector4(1.0f, 0.55f, 0.2f, 1f) : new Vector4(0.4f, 0.4f, 0.4f, 0.5f);
        ImGui.PushStyleColor(ImGuiCol.Text, lockColor);
        if (ImGui.Button(ent.Locked ? "[L]" : "[-]"))
        {
            ent.Locked = !ent.Locked;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(ent.Locked ? "Unlock Entity" : "Lock Entity");
        }
        ImGui.PopStyleColor();

        ImGui.SameLine();

        // Inline Rename or Selectable
        if (_renamingEntityId == ent.Id)
        {
            if (_focusRename)
            {
                ImGui.SetKeyboardFocusHere();
                _focusRename = false;
            }

            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##RenameEntInput", ref _renameBuffer, 64, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                CommitRenameEntity(app, ent);
            }

            if (ImGui.IsItemDeactivated() && ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                CancelRename();
            }
        }
        else
        {
            bool isSelected = app.Selection.SelectedEntityIds.Contains(ent.Id);
            string label = $"{ent.Id} [{ent.ClassName}]";

            if (ImGui.Selectable(label, isSelected))
            {
                if (ImGui.GetIO().KeyCtrl || ImGui.GetIO().KeyShift)
                {
                    app.Selection.ToggleSelectEntity(ent.Id);
                }
                else
                {
                    app.Selection.SelectSingleEntity(ent.Id);
                }
            }

            if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                StartRenameEntity(ent);
            }

            // Context Menu
            if (ImGui.BeginPopupContextItem())
            {
                if (ImGui.MenuItem("Rename", "F2"))
                {
                    StartRenameEntity(ent);
                }

                if (ImGui.MenuItem("Duplicate", "Shift+D"))
                {
                    DuplicateEntity(app, ent);
                }

                if (ImGui.MenuItem("Delete", "Del"))
                {
                    DeleteEntity(app, ent);
                }

                ImGui.EndPopup();
            }
        }

        ImGui.PopID();
    }

    private void DrawTransformInspector(EditorApp app)
    {
        ImGui.Begin("Transform Inspector");

        int? activeObjId = app.Selection.ActiveObjectId;
        string? activeEntId = app.Selection.ActiveEntityId;

        if (activeObjId.HasValue && app.Selection.SelectedObjectIds.Contains(activeObjId.Value))
        {
            SceneObject? obj = app.Scene.GetObject(activeObjId.Value);
            if (obj is not null)
            {
                DrawObjectTransform(app, obj);
                ImGui.End();
                return;
            }
        }

        if (!string.IsNullOrEmpty(activeEntId) && app.Selection.SelectedEntityIds.Contains(activeEntId))
        {
            LevelEntity? ent = app.Scene.GetEntity(activeEntId);
            if (ent is not null)
            {
                DrawEntityTransform(app, ent);
                ImGui.End();
                return;
            }
        }

        ImGui.TextDisabled("Select an object or entity to inspect its transform and pivot.");
        ImGui.End();
    }

    private void DrawObjectTransform(EditorApp app, SceneObject obj)
    {
        ImGui.TextColored(new Vector4(0.3f, 0.85f, 1.0f, 1.0f), $"Object: {obj.Name} (ID: {obj.Id})");
        ImGui.Separator();

        Transform3D t = obj.Transform;

        Vector3 pos = t.Position;
        Vector3 rot = t.RotationEulerDegrees;
        Vector3 scale = t.Scale;

        // Position
        if (ImGui.DragFloat3("Position (m)", ref pos, 0.05f))
        {
            EnsureDragSnapshot(app);
            t.Position = pos;
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            CommitDragSnapshot(app, "Move Object");
        }

        // Rotation
        if (ImGui.DragFloat3("Rotation (°)", ref rot, 0.5f))
        {
            EnsureDragSnapshot(app);
            t.RotationEulerDegrees = rot;
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            CommitDragSnapshot(app, "Rotate Object");
        }

        // Scale
        if (ImGui.DragFloat3("Scale", ref scale, 0.01f, 0.001f, 1000f))
        {
            EnsureDragSnapshot(app);
            t.Scale = scale;
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            CommitDragSnapshot(app, "Scale Object");
        }

        ImGui.Separator();
        ImGui.TextDisabled("Pivot & Origin Utilities:");

        bool canModifyGeometry = obj.Mesh is not null && obj.Mesh.VertexCount > 0 && !obj.Locked;
        if (!canModifyGeometry)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("Origin to Geometry Centroid", new Vector2(-1, 0)))
        {
            EditorScene before = app.Scene.CloneSnapshot();
            if (ObjectTransformOps.CenterOriginToCentroid(obj))
            {
                app.UndoStack.RecordAction("Origin to Centroid", before, app.Scene);
                app.StatusMessage = $"Moved origin of {obj.Name} to geometry centroid.";
            }
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Centers the pivot to the geometric average of vertices without moving geometry in world space.");
        }

        if (ImGui.Button("Origin to Bottom Bounds", new Vector2(-1, 0)))
        {
            EditorScene before = app.Scene.CloneSnapshot();
            if (ObjectTransformOps.CenterOriginToBottomBounds(obj))
            {
                app.UndoStack.RecordAction("Origin to Bottom Bounds", before, app.Scene);
                app.StatusMessage = $"Moved origin of {obj.Name} to bottom bounds.";
            }
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Moves the pivot to the bottom center (Min.Y) of the bounding box without moving geometry in world space.");
        }

        if (ImGui.Button("Apply / Freeze Scale & Rotation into Mesh", new Vector2(-1, 0)))
        {
            EditorScene before = app.Scene.CloneSnapshot();
            if (ObjectTransformOps.FreezeScaleAndRotation(obj))
            {
                app.UndoStack.RecordAction("Freeze Transform", before, app.Scene);
                app.StatusMessage = $"Baked scale and rotation of {obj.Name} into mesh vertices.";
            }
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Bakes the object's current scale and rotation directly into mesh vertices and resets transform rotation to 0 and scale to 1.");
        }

        if (!canModifyGeometry)
        {
            ImGui.EndDisabled();
        }
    }

    private void DrawEntityTransform(EditorApp app, LevelEntity ent)
    {
        ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.2f, 1.0f), $"Entity: {ent.Id} [{ent.ClassName}]");
        ImGui.Separator();

        Transform3D t = ent.Transform;

        Vector3 pos = t.Position;
        Vector3 rot = t.RotationEulerDegrees;
        Vector3 scale = t.Scale;

        // Position
        if (ImGui.DragFloat3("Position (m)", ref pos, 0.05f))
        {
            EnsureDragSnapshot(app);
            t.Position = pos;
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            CommitDragSnapshot(app, "Move Entity");
        }

        // Rotation
        if (ImGui.DragFloat3("Rotation (°)", ref rot, 0.5f))
        {
            EnsureDragSnapshot(app);
            t.RotationEulerDegrees = rot;
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            CommitDragSnapshot(app, "Rotate Entity");
        }

        // Scale
        if (ImGui.DragFloat3("Scale", ref scale, 0.01f, 0.001f, 1000f))
        {
            EnsureDragSnapshot(app);
            t.Scale = scale;
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            CommitDragSnapshot(app, "Scale Entity");
        }
    }

    private void EnsureDragSnapshot(EditorApp app)
    {
        _dragSnapshot ??= app.Scene.CloneSnapshot();
    }

    private void CommitDragSnapshot(EditorApp app, string description)
    {
        if (_dragSnapshot is not null)
        {
            app.UndoStack.RecordAction(description, _dragSnapshot, app.Scene);
            _dragSnapshot = null;
        }
    }

    private void StartRenameObject(SceneObject obj)
    {
        _renamingObjectId = obj.Id;
        _renamingEntityId = null;
        _renameBuffer = obj.Name;
        _focusRename = true;
    }

    private void CommitRenameObject(EditorApp app, SceneObject obj)
    {
        if (_renamingObjectId == obj.Id && !string.IsNullOrWhiteSpace(_renameBuffer) && _renameBuffer != obj.Name)
        {
            EditorScene before = app.Scene.CloneSnapshot();
            obj.Name = _renameBuffer.Trim();
            app.UndoStack.RecordAction("Rename Object", before, app.Scene);
            app.StatusMessage = $"Renamed object to {obj.Name}.";
        }
        CancelRename();
    }

    private void StartRenameEntity(LevelEntity ent)
    {
        _renamingEntityId = ent.Id;
        _renamingObjectId = null;
        _renameBuffer = ent.Id;
        _focusRename = true;
    }

    private void CommitRenameEntity(EditorApp app, LevelEntity ent)
    {
        if (_renamingEntityId == ent.Id && !string.IsNullOrWhiteSpace(_renameBuffer) && _renameBuffer != ent.Id)
        {
            EditorScene before = app.Scene.CloneSnapshot();
            ent.Id = _renameBuffer.Trim();
            app.UndoStack.RecordAction("Rename Entity", before, app.Scene);
            app.StatusMessage = $"Renamed entity to {ent.Id}.";
        }
        CancelRename();
    }

    private void CancelRename()
    {
        _renamingObjectId = null;
        _renamingEntityId = null;
        _renameBuffer = string.Empty;
        _focusRename = false;
    }

    private void DuplicateObject(EditorApp app, SceneObject obj)
    {
        EditorScene before = app.Scene.CloneSnapshot();
        SceneObject clone = app.Scene.AddClonedObject(obj);
        clone.Transform.Position += new Vector3(1f, 0f, 1f);
        app.Selection.SelectSingleObject(clone.Id);
        app.UndoStack.RecordAction("Duplicate Object", before, app.Scene);
        app.StatusMessage = $"Duplicated {obj.Name} -> {clone.Name}.";
    }

    private void DeleteObject(EditorApp app, SceneObject obj)
    {
        EditorScene before = app.Scene.CloneSnapshot();
        app.Scene.RemoveObject(obj.Id);
        app.Selection.ClearSelection();
        app.UndoStack.RecordAction($"Delete {obj.Name}", before, app.Scene);
        app.StatusMessage = $"Deleted object {obj.Name}.";
    }

    private void DuplicateEntity(EditorApp app, LevelEntity ent)
    {
        EditorScene before = app.Scene.CloneSnapshot();
        LevelEntity clone = app.Scene.AddClonedEntity(ent);
        clone.Transform.Position += new Vector3(1f, 0f, 1f);
        app.Selection.SelectSingleEntity(clone.Id);
        app.UndoStack.RecordAction("Duplicate Entity", before, app.Scene);
        app.StatusMessage = $"Duplicated entity {ent.Id} -> {clone.Id}.";
    }

    private void DeleteEntity(EditorApp app, LevelEntity ent)
    {
        EditorScene before = app.Scene.CloneSnapshot();
        app.Scene.RemoveEntity(ent.Id);
        app.Selection.ClearSelection();
        app.UndoStack.RecordAction($"Delete {ent.Id}", before, app.Scene);
        app.StatusMessage = $"Deleted entity {ent.Id}.";
    }
}
