using System.Numerics;
using ImGuiNET;
using Leed3D.Kernel.Entities;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Selection;
using Leed3D.Runtime.Metadata;

namespace Leed3D.Editor.UI.Panels;

/// <summary>
/// ImGui panel providing inspection and editing of typed metadata properties and tags
/// across SceneObjects, PolygonFaces, Edges, and Vertices, including one-click presets and undo/redo.
/// </summary>
public sealed class MetadataInspectorPanel
{
    private string _newTagInput = string.Empty;
    private string _newPropKey = string.Empty;
    private MetadataValueType _newPropType = MetadataValueType.String;
    private static readonly MetadataValueType[] AvailableTypes =
    [
        MetadataValueType.String,
        MetadataValueType.Float,
        MetadataValueType.Int,
        MetadataValueType.Bool,
        MetadataValueType.Color,
        MetadataValueType.Vec3
    ];

    public void Draw(EditorApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        ImGui.Begin("Metadata Inspector");

        if (app.Selection.Mode == InteractionMode.Object && app.Selection.SelectedEntityIds.Count > 0)
        {
            DrawEntityInspector(app);
            ImGui.End();
            return;
        }

        List<ElementMetadata> targets = GetTargetMetadatas(app, out string targetDescription, out int targetCount);

        if (targets.Count == 0)
        {
            ImGui.TextDisabled("Select an object, face, edge, vertex, or entity to inspect metadata.");
            ImGui.End();
            return;
        }

        ElementMetadata primary = targets[0];

        // Target Header Info
        ImGui.TextColored(new Vector4(0.3f, 0.9f, 0.8f, 1.0f), targetDescription);
        if (targetCount > 1)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"({targetCount} selected)");
            if (ImGui.Button("Apply Primary to All Selected"))
            {
                RecordAndMutate(app, "Apply Metadata to Selection", meta =>
                {
                    meta.MergeFrom(primary);
                });
            }
        }

        ImGui.Separator();

        // 1. One-Click Quick Presets
        DrawPresetsSection(app, targets);

        ImGui.Separator();

        // 2. Tags Section
        DrawTagsSection(app, primary);

        ImGui.Separator();

        // 3. Properties Section
        DrawPropertiesSection(app, primary);

        ImGui.End();
    }

    private void DrawEntityInspector(EditorApp app)
    {
        string? primaryId = app.Selection.ActiveEntityId ?? app.Selection.SelectedEntityIds.FirstOrDefault();
        if (primaryId is null || app.Scene.GetEntity(primaryId) is not { } entity)
        {
            ImGui.TextDisabled("Selected entity no longer exists.");
            return;
        }

        int selectedCount = app.Selection.SelectedEntityIds.Count;

        // Header
        ImGui.TextColored(new Vector4(0.3f, 0.9f, 0.5f, 1.0f), $"Entity: {entity.ClassName}");
        if (selectedCount > 1)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"({selectedCount} selected)");
        }

        ImGui.Separator();

        // 1. Identity (ID & ClassName)
        ImGui.TextDisabled("Identity:");
        string id = entity.Id;
        ImGui.SetNextItemWidth(200.0f);
        if (ImGui.InputText("ID##EntityId", ref id, 64, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            RenameEntity(app, entity, id);
        }

        ImGui.SetNextItemWidth(200.0f);
        if (ImGui.BeginCombo("Class##EntityClass", entity.ClassName))
        {
            foreach (var def in app.EntityRegistry.Definitions)
            {
                bool isSelected = def.ClassName.Equals(entity.ClassName, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable($"{def.DisplayName} ({def.ClassName})", isSelected))
                {
                    if (!isSelected)
                    {
                        EditorScene before = app.Scene.CloneSnapshot();
                        entity.ClassName = def.ClassName;
                        foreach (var (k, v) in def.DefaultProperties.Properties)
                        {
                            if (!entity.Properties.Properties.ContainsKey(k))
                            {
                                entity.Properties.SetProperty(k, v);
                            }
                        }
                        app.UndoStack.RecordAction($"Change Entity '{entity.Id}' Class to '{def.ClassName}'", before, app.Scene);
                    }
                }
                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }
            ImGui.EndCombo();
        }

        ImGui.Separator();

        // 2. Transform
        ImGui.TextDisabled("Transform:");

        Vector3 pos = entity.Transform.Position;
        ImGui.SetNextItemWidth(200.0f);
        if (ImGui.DragFloat3("Position##EntPos", ref pos, 0.05f))
        {
            RecordEntityMutation(app, $"Move Entity '{entity.Id}'", e => e.Transform.Position = pos);
        }

        Vector3 rot = entity.Transform.RotationEulerDegrees;
        ImGui.SetNextItemWidth(200.0f);
        if (ImGui.DragFloat3("Rotation##EntRot", ref rot, 1.0f))
        {
            RecordEntityMutation(app, $"Rotate Entity '{entity.Id}'", e =>
            {
                e.Transform.RotationEulerDegrees = rot;
                if (e.ClassName.Equals("player_spawn", StringComparison.OrdinalIgnoreCase) && e.Properties.Properties.ContainsKey("yaw"))
                {
                    e.Properties.SetFloat("yaw", rot.Y);
                }
            });
        }

        Vector3 scale = entity.Transform.Scale;
        ImGui.SetNextItemWidth(200.0f);
        if (ImGui.DragFloat3("Scale##EntScale", ref scale, 0.05f))
        {
            RecordEntityMutation(app, $"Scale Entity '{entity.Id}'", e => e.Transform.Scale = scale);
        }

        ImGui.Separator();

        // 3. Class-Specific Fields & Target Linking
        ImGui.TextDisabled("Class-Specific Fields:");

        // Target Link combo (for portal, trigger, or any entity with target_id/target)
        if (entity.ClassName is "portal" or "trigger" || entity.Properties.Properties.ContainsKey("target_id") || entity.Properties.Properties.ContainsKey("target"))
        {
            string currentTarget = string.Empty;
            if (entity.Properties.TryGet("target_id", out var tv) && tv is not null)
            {
                currentTarget = tv.StringValue;
            }
            else if (entity.Properties.TryGet("target", out var tv2) && tv2 is not null)
            {
                currentTarget = tv2.StringValue;
            }

            ImGui.SetNextItemWidth(200.0f);
            if (ImGui.BeginCombo("Target Link##EntTargetCombo", string.IsNullOrEmpty(currentTarget) ? "(None)" : currentTarget))
            {
                if (ImGui.Selectable("(None)", string.IsNullOrEmpty(currentTarget)))
                {
                    RecordEntityMutation(app, "Clear Target", e =>
                    {
                        e.Properties.SetString("target_id", string.Empty);
                        e.Properties.RemoveProperty("target");
                    });
                }

                if (app.Scene.Entities.Count > 1)
                {
                    ImGui.Separator();
                    ImGui.TextDisabled("Entities:");
                    foreach (var otherEnt in app.Scene.Entities)
                    {
                        if (otherEnt == entity) continue;
                        bool isSel = otherEnt.Id == currentTarget;
                        if (ImGui.Selectable($"{otherEnt.Id} ({otherEnt.ClassName})##targetEnt", isSel))
                        {
                            RecordEntityMutation(app, $"Set Target to '{otherEnt.Id}'", e =>
                            {
                                e.Properties.SetString("target_id", otherEnt.Id);
                            });
                        }
                    }
                }

                if (app.Scene.Objects.Count > 0)
                {
                    ImGui.Separator();
                    ImGui.TextDisabled("Scene Objects:");
                    foreach (var obj in app.Scene.Objects)
                    {
                        bool isSel = obj.Name == currentTarget || obj.Id.ToString() == currentTarget;
                        if (ImGui.Selectable($"{obj.Name} [#{obj.Id}]##targetObj", isSel))
                        {
                            RecordEntityMutation(app, $"Set Target to '{obj.Name}'", e =>
                            {
                                e.Properties.SetString("target_id", obj.Name);
                            });
                        }
                    }
                }

                ImGui.EndCombo();
            }

            ImGui.SetNextItemWidth(200.0f);
            string editTarget = currentTarget;
            if (ImGui.InputText("Custom Target ID##EntCustomTarget", ref editTarget, 64, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                RecordEntityMutation(app, $"Set Target to '{editTarget}'", e =>
                {
                    e.Properties.SetString("target_id", editTarget.Trim());
                });
            }
        }

        // player_spawn specific
        if (entity.ClassName.Equals("player_spawn", StringComparison.OrdinalIgnoreCase))
        {
            int team = 0;
            if (entity.Properties.TryGet("team", out var teamVal) && teamVal is not null)
            {
                team = teamVal.IntValue;
            }

            ImGui.SetNextItemWidth(120.0f);
            if (ImGui.DragInt("Team##PlayerTeam", ref team, 1, 0, 16))
            {
                RecordEntityMutation(app, "Set Spawn Team", e => e.Properties.SetInt("team", team));
            }

            float yaw = entity.Transform.RotationEulerDegrees.Y;
            if (entity.Properties.TryGet("yaw", out var yawVal) && yawVal is not null)
            {
                yaw = yawVal.FloatValue;
            }

            ImGui.SetNextItemWidth(120.0f);
            if (ImGui.DragFloat("Yaw Facing##PlayerYaw", ref yaw, 1.0f, -360.0f, 360.0f))
            {
                RecordEntityMutation(app, "Set Spawn Yaw", e =>
                {
                    e.Properties.SetFloat("yaw", yaw);
                    e.Transform.RotationEulerDegrees = new Vector3(e.Transform.RotationEulerDegrees.X, yaw, e.Transform.RotationEulerDegrees.Z);
                });
            }
        }

        // light specific
        if (entity.ClassName.Equals("light", StringComparison.OrdinalIgnoreCase))
        {
            string lightType = "point";
            if (entity.Properties.TryGet("light_type", out var ltVal) && ltVal is not null)
            {
                lightType = ltVal.StringValue;
            }

            ImGui.SetNextItemWidth(120.0f);
            if (ImGui.BeginCombo("Light Type##LightTypeCombo", lightType))
            {
                if (ImGui.Selectable("point", lightType == "point"))
                {
                    RecordEntityMutation(app, "Set Light Type to point", e => e.Properties.SetString("light_type", "point"));
                }
                if (ImGui.Selectable("spot", lightType == "spot"))
                {
                    RecordEntityMutation(app, "Set Light Type to spot", e => e.Properties.SetString("light_type", "spot"));
                }
                ImGui.EndCombo();
            }

            ColorRgba c = new(255, 240, 210, 255);
            if (entity.Properties.TryGet("color", out var cVal) && cVal is not null)
            {
                c = cVal.ColorValue;
            }

            Vector4 colVec = new(c.R / 255.0f, c.G / 255.0f, c.B / 255.0f, c.A / 255.0f);
            ImGui.SetNextItemWidth(180.0f);
            if (ImGui.ColorEdit4("Color##LightColor", ref colVec, ImGuiColorEditFlags.NoInputs))
            {
                ColorRgba updated = new(
                    (byte)System.Math.Clamp((int)(colVec.X * 255f), 0, 255),
                    (byte)System.Math.Clamp((int)(colVec.Y * 255f), 0, 255),
                    (byte)System.Math.Clamp((int)(colVec.Z * 255f), 0, 255),
                    (byte)System.Math.Clamp((int)(colVec.W * 255f), 0, 255));
                RecordEntityMutation(app, "Set Light Color", e => e.Properties.SetColor("color", updated));
            }

            float intensity = 1.5f;
            if (entity.Properties.TryGet("intensity", out var intVal) && intVal is not null)
            {
                intensity = intVal.FloatValue;
            }

            ImGui.SetNextItemWidth(120.0f);
            if (ImGui.DragFloat("Intensity##LightIntensity", ref intensity, 0.05f, 0.0f, 100.0f))
            {
                RecordEntityMutation(app, "Set Light Intensity", e => e.Properties.SetFloat("intensity", intensity));
            }

            float radius = 12.0f;
            if (entity.Properties.TryGet("radius", out var radVal) && radVal is not null)
            {
                radius = radVal.FloatValue;
            }

            ImGui.SetNextItemWidth(120.0f);
            if (ImGui.DragFloat("Radius##LightRadius", ref radius, 0.25f, 0.1f, 512.0f))
            {
                RecordEntityMutation(app, "Set Light Radius", e => e.Properties.SetFloat("radius", radius));
            }

            if (lightType.Equals("spot", StringComparison.OrdinalIgnoreCase))
            {
                float spotAngle = 45.0f;
                if (entity.Properties.TryGet("spot_angle", out var saVal) && saVal is not null)
                {
                    spotAngle = saVal.FloatValue;
                }

                ImGui.SetNextItemWidth(120.0f);
                if (ImGui.DragFloat("Spot Angle##LightSpotAngle", ref spotAngle, 0.5f, 1.0f, 179.0f))
                {
                    RecordEntityMutation(app, "Set Spot Angle", e => e.Properties.SetFloat("spot_angle", spotAngle));
                }
            }
        }

        // portal / trigger specific
        if (entity.ClassName is "portal" or "trigger")
        {
            string eventName = entity.ClassName == "portal" ? "teleport" : "trigger_enter";
            if (entity.Properties.TryGet("event_name", out var evVal) && evVal is not null)
            {
                eventName = evVal.StringValue;
            }

            ImGui.SetNextItemWidth(180.0f);
            if (ImGui.InputText("Event Name##TriggerEvent", ref eventName, 64, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                RecordEntityMutation(app, "Set Event Name", e => e.Properties.SetString("event_name", eventName.Trim()));
            }

            bool oneShot = entity.Properties.TryGet("one_shot", out var osVal) && osVal is not null && osVal.BoolValue;
            if (ImGui.Checkbox("One-Shot##TriggerOneShot", ref oneShot))
            {
                RecordEntityMutation(app, "Set One-Shot", e => e.Properties.SetBool("one_shot", oneShot));
            }
        }

        ImGui.Separator();

        // 4. Tags Section
        DrawTagsSection(app, entity.Properties);

        ImGui.Separator();

        // 5. Properties Section
        DrawPropertiesSection(app, entity.Properties);
    }

    private static void RenameEntity(EditorApp app, LevelEntity entity, string newId)
    {
        if (string.IsNullOrWhiteSpace(newId) || newId == entity.Id) return;
        newId = newId.Trim();
        if (app.Scene.Entities.Any(e => e != entity && e.Id.Equals(newId, StringComparison.OrdinalIgnoreCase))) return;

        EditorScene before = app.Scene.CloneSnapshot();
        string oldId = entity.Id;
        entity.Id = newId;
        if (app.Selection.SelectedEntityIds.Contains(oldId))
        {
            var updated = app.Selection.SelectedEntityIds.Where(x => x != oldId).Append(newId);
            app.Selection.SelectMultipleEntities(updated);
        }
        if (app.Selection.ActiveEntityId == oldId)
        {
            app.Selection.ActiveEntityId = newId;
        }
        app.UndoStack.RecordAction($"Rename Entity '{oldId}' to '{newId}'", before, app.Scene);
        app.StatusMessage = $"Renamed entity to '{newId}'.";
    }

    private static void RecordEntityMutation(EditorApp app, string description, Action<LevelEntity> action)
    {
        EditorScene before = app.Scene.CloneSnapshot();
        foreach (string entId in app.Selection.SelectedEntityIds)
        {
            if (app.Scene.GetEntity(entId) is { } entity)
            {
                action(entity);
            }
        }
        app.UndoStack.RecordAction(description, before, app.Scene);
        app.StatusMessage = $"{description}.";
    }

    private void DrawPresetsSection(EditorApp app, List<ElementMetadata> targets)
    {
        ImGui.TextDisabled("Quick Presets:");

        if (ImGui.Button("collision=none"))
        {
            RecordAndMutate(app, "Preset: collision=none", meta =>
            {
                meta.SetProperty("collision", "none");
                meta.AddTag("no_collision");
            });
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Disables collision for face or object; marks face with hatched visual overlay.");
        }

        ImGui.SameLine();
        if (ImGui.Button("surface=lava"))
        {
            RecordAndMutate(app, "Preset: surface=lava", meta =>
            {
                meta.SetProperty("surface", "lava");
                meta.AddTag("hazard");
            });
        }

        ImGui.SameLine();
        if (ImGui.Button("surface=water"))
        {
            RecordAndMutate(app, "Preset: surface=water", meta =>
            {
                meta.SetProperty("surface", "water");
                meta.AddTag("liquid");
            });
        }

        ImGui.SameLine();
        if (ImGui.Button("ledge_grab=true"))
        {
            RecordAndMutate(app, "Preset: ledge_grab=true", meta =>
            {
                meta.SetProperty("ledge_grab", true);
                meta.AddTag("ledge");
            });
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Marks edge as climbable / ledge grab in runtime games; highlights cyan in viewport.");
        }

        ImGui.SameLine();
        if (ImGui.Button("portal_id"))
        {
            RecordAndMutate(app, "Preset: portal_id", meta =>
            {
                meta.SetProperty("portal_id", "portal_1");
                meta.AddTag("portal");
            });
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear All##ClearMetadata"))
        {
            RecordAndMutate(app, "Clear Metadata", meta =>
            {
                meta.Clear();
            });
        }
    }

    private void DrawTagsSection(EditorApp app, ElementMetadata primary)
    {
        ImGui.Text($"Tags ({primary.Tags.Count}):");

        // Display existing tags
        if (primary.Tags.Count > 0)
        {
            var tagsList = primary.Tags.ToList();
            for (int i = 0; i < tagsList.Count; i++)
            {
                string tag = tagsList[i];
                if (i > 0)
                {
                    ImGui.SameLine();
                }

                ImGui.PushID($"tag_{tag}");
                ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), $"[{tag}]");
                ImGui.SameLine();
                if (ImGui.SmallButton("x"))
                {
                    RecordAndMutate(app, $"Remove tag '{tag}'", meta => meta.RemoveTag(tag));
                    ImGui.PopID();
                    break;
                }
                ImGui.PopID();
            }
        }
        else
        {
            ImGui.TextDisabled("No tags.");
        }

        // Add new tag input
        ImGui.SetNextItemWidth(140.0f);
        bool enterPressed = ImGui.InputTextWithHint("##NewTagInput", "New tag...", ref _newTagInput, 32, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        if ((ImGui.Button("Add Tag") || enterPressed) && !string.IsNullOrWhiteSpace(_newTagInput))
        {
            string tagToAdd = _newTagInput.Trim().ToLowerInvariant();
            RecordAndMutate(app, $"Add tag '{tagToAdd}'", meta => meta.AddTag(tagToAdd));
            _newTagInput = string.Empty;
        }
    }

    private void DrawPropertiesSection(EditorApp app, ElementMetadata primary)
    {
        ImGui.Text($"Properties ({primary.Properties.Count}):");

        if (primary.Properties.Count > 0)
        {
            if (ImGui.BeginTable("MetadataPropsTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Key", ImGuiTableColumnFlags.WidthStretch, 0.3f);
                ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 60.0f);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch, 0.55f);
                ImGui.TableSetupColumn("##Action", ImGuiTableColumnFlags.WidthFixed, 30.0f);
                ImGui.TableHeadersRow();

                var props = primary.Properties.ToList();
                for (int i = 0; i < props.Count; i++)
                {
                    var (key, val) = props[i];
                    ImGui.PushID($"prop_{key}");
                    ImGui.TableNextRow();

                    // 1. Key
                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(key);

                    // 2. Type
                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextDisabled(val.Type.ToString());

                    // 3. Value editor
                    ImGui.TableSetColumnIndex(2);
                    DrawPropertyValueEditor(app, key, val);

                    // 4. Delete button
                    ImGui.TableSetColumnIndex(3);
                    if (ImGui.Button("X##del"))
                    {
                        RecordAndMutate(app, $"Remove property '{key}'", meta => meta.RemoveProperty(key));
                        ImGui.PopID();
                        break;
                    }

                    ImGui.PopID();
                }

                ImGui.EndTable();
            }
        }
        else
        {
            ImGui.TextDisabled("No properties defined.");
        }

        ImGui.Spacing();

        // Add new property row
        ImGui.TextDisabled("Add Property:");
        ImGui.SetNextItemWidth(120.0f);
        ImGui.InputTextWithHint("##NewPropKey", "Key name...", ref _newPropKey, 32);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(90.0f);
        if (ImGui.BeginCombo("##NewPropType", _newPropType.ToString()))
        {
            for (int i = 0; i < AvailableTypes.Length; i++)
            {
                MetadataValueType t = AvailableTypes[i];
                bool isSelected = t == _newPropType;
                if (ImGui.Selectable(t.ToString(), isSelected))
                {
                    _newPropType = t;
                }
                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }
            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGui.Button("Add##AddPropBtn") && !string.IsNullOrWhiteSpace(_newPropKey))
        {
            string keyToAdd = _newPropKey.Trim();
            MetadataValue defaultVal = _newPropType switch
            {
                MetadataValueType.Float => MetadataValue.FromFloat(0.0f),
                MetadataValueType.Int => MetadataValue.FromInt(0),
                MetadataValueType.Bool => MetadataValue.FromBool(false),
                MetadataValueType.Color => MetadataValue.FromColor(new ColorRgba(255, 255, 255, 255)),
                MetadataValueType.Vec3 => MetadataValue.FromVec3(Vector3.Zero),
                _ => MetadataValue.FromString(string.Empty)
            };

            RecordAndMutate(app, $"Add property '{keyToAdd}'", meta => meta.SetProperty(keyToAdd, defaultVal));
            _newPropKey = string.Empty;
        }
    }

    private static void DrawPropertyValueEditor(EditorApp app, string key, MetadataValue val)
    {
        switch (val.Type)
        {
            case MetadataValueType.String:
            {
                string sVal = val.StringValue;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.InputText($"##str_{key}", ref sVal, 128))
                {
                    RecordAndMutate(app, $"Edit property '{key}'", meta => meta.SetProperty(key, sVal));
                }
                break;
            }

            case MetadataValueType.Float:
            {
                float fVal = val.FloatValue;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.DragFloat($"##flt_{key}", ref fVal, 0.05f))
                {
                    RecordAndMutate(app, $"Edit property '{key}'", meta => meta.SetProperty(key, fVal));
                }
                break;
            }

            case MetadataValueType.Int:
            {
                int iVal = val.IntValue;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.DragInt($"##int_{key}", ref iVal, 1))
                {
                    RecordAndMutate(app, $"Edit property '{key}'", meta => meta.SetProperty(key, iVal));
                }
                break;
            }

            case MetadataValueType.Bool:
            {
                bool bVal = val.BoolValue;
                if (ImGui.Checkbox($"##chk_{key}", ref bVal))
                {
                    RecordAndMutate(app, $"Edit property '{key}'", meta => meta.SetProperty(key, bVal));
                }
                break;
            }

            case MetadataValueType.Color:
            {
                ColorRgba c = val.ColorValue;
                Vector4 colVec = new(c.R / 255.0f, c.G / 255.0f, c.B / 255.0f, c.A / 255.0f);
                ImGui.SetNextItemWidth(-1);
                if (ImGui.ColorEdit4($"##col_{key}", ref colVec, ImGuiColorEditFlags.NoInputs))
                {
                    ColorRgba updated = new(
                        (byte)System.Math.Clamp((int)(colVec.X * 255f), 0, 255),
                        (byte)System.Math.Clamp((int)(colVec.Y * 255f), 0, 255),
                        (byte)System.Math.Clamp((int)(colVec.Z * 255f), 0, 255),
                        (byte)System.Math.Clamp((int)(colVec.W * 255f), 0, 255));
                    RecordAndMutate(app, $"Edit property '{key}'", meta => meta.SetProperty(key, updated));
                }
                break;
            }

            case MetadataValueType.Vec3:
            {
                Vector3 vVal = val.Vec3Value;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.DragFloat3($"##vec_{key}", ref vVal, 0.05f))
                {
                    RecordAndMutate(app, $"Edit property '{key}'", meta => meta.SetProperty(key, vVal));
                }
                break;
            }
        }
    }

    private static List<ElementMetadata> GetTargetMetadatas(EditorApp app, out string targetDescription, out int targetCount)
    {
        var targets = new List<ElementMetadata>();
        targetDescription = string.Empty;
        targetCount = 0;

        switch (app.Selection.Mode)
        {
            case InteractionMode.Object:
            {
                if (app.Selection.SelectedEntityIds.Count > 0)
                {
                    var selEntIds = app.Selection.SelectedEntityIds;
                    targetCount = selEntIds.Count;
                    foreach (string entId in selEntIds)
                    {
                        if (app.Scene.GetEntity(entId) is { } ent)
                        {
                            targets.Add(ent.Properties);
                        }
                    }

                    if (targets.Count > 0)
                    {
                        LevelEntity primaryEnt = app.Scene.GetEntity(selEntIds.First())!;
                        targetDescription = targetCount == 1
                            ? $"Entity: '{primaryEnt.Id}' ({primaryEnt.ClassName})"
                            : $"Entities ({targetCount} selected)";
                    }
                    break;
                }

                var selObjIds = app.Selection.SelectedObjectIds;
                targetCount = selObjIds.Count;
                if (targetCount == 0)
                {
                    return targets;
                }

                foreach (int objId in selObjIds)
                {
                    if (app.Scene.GetObject(objId) is { } obj)
                    {
                        targets.Add(obj.Metadata);
                    }
                }

                if (targets.Count > 0)
                {
                    SceneObject primaryObj = app.Scene.GetObject(selObjIds.First())!;
                    targetDescription = targetCount == 1
                        ? $"Object: '{primaryObj.Name}' [ID: {primaryObj.Id}]"
                        : $"Objects ({targetCount} selected)";
                }
                break;
            }

            case InteractionMode.Face:
            {
                SceneObject? activeObj = GetActiveObject(app);
                if (activeObj?.Mesh is null)
                {
                    return targets;
                }

                var faceIds = app.Selection.SelectedFaceIds;
                targetCount = faceIds.Count;
                if (targetCount == 0)
                {
                    return targets;
                }

                foreach (int fId in faceIds)
                {
                    if (activeObj.Mesh.GetFace(fId) is { } face)
                    {
                        targets.Add(face.Metadata);
                    }
                }

                if (targets.Count > 0)
                {
                    targetDescription = targetCount == 1
                        ? $"Face #{faceIds.First()} on '{activeObj.Name}'"
                        : $"Faces ({targetCount} selected) on '{activeObj.Name}'";
                }
                break;
            }

            case InteractionMode.Edge:
            {
                SceneObject? activeObj = GetActiveObject(app);
                if (activeObj?.Mesh is null)
                {
                    return targets;
                }

                var edges = app.Selection.SelectedEdges;
                targetCount = edges.Count;
                if (targetCount == 0)
                {
                    return targets;
                }

                foreach (EdgeKey edge in edges)
                {
                    targets.Add(activeObj.Mesh.GetOrCreateEdgeMetadata(edge));
                }

                if (targets.Count > 0)
                {
                    EdgeKey primaryEdge = edges.First();
                    targetDescription = targetCount == 1
                        ? $"Edge ({primaryEdge.VMin}, {primaryEdge.VMax}) on '{activeObj.Name}'"
                        : $"Edges ({targetCount} selected) on '{activeObj.Name}'";
                }
                break;
            }

            case InteractionMode.Vertex:
            {
                SceneObject? activeObj = GetActiveObject(app);
                if (activeObj?.Mesh is null)
                {
                    return targets;
                }

                var vertIds = app.Selection.SelectedVertexIds;
                targetCount = vertIds.Count;
                if (targetCount == 0)
                {
                    return targets;
                }

                foreach (int vId in vertIds)
                {
                    if (activeObj.Mesh.GetVertex(vId) is { } vert)
                    {
                        targets.Add(vert.Metadata);
                    }
                }

                if (targets.Count > 0)
                {
                    targetDescription = targetCount == 1
                        ? $"Vertex #{vertIds.First()} on '{activeObj.Name}'"
                        : $"Vertices ({targetCount} selected) on '{activeObj.Name}'";
                }
                break;
            }
        }

        return targets;
    }

    private static SceneObject? GetActiveObject(EditorApp app)
    {
        if (app.Selection.ActiveObjectId.HasValue &&
            app.Scene.GetObject(app.Selection.ActiveObjectId.Value) is { } active)
        {
            return active;
        }

        if (app.Selection.SelectedObjectIds.Count > 0 &&
            app.Scene.GetObject(app.Selection.SelectedObjectIds.First()) is { } first)
        {
            return first;
        }

        return null;
    }

    private static void RecordAndMutate(EditorApp app, string description, Action<ElementMetadata> action)
    {
        EditorScene before = app.Scene.CloneSnapshot();
        List<ElementMetadata> targets = GetTargetMetadatas(app, out _, out int count);
        if (targets.Count == 0)
        {
            return;
        }

        for (int i = 0; i < targets.Count; i++)
        {
            action(targets[i]);
        }

        app.UndoStack.RecordAction(description, before, app.Scene);
        app.StatusMessage = $"{description} ({count} element(s)).";
    }
}
