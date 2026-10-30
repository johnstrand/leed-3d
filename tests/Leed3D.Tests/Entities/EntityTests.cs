using System.Drawing;
using System.Numerics;
using Leed3D.Kernel.Entities;
using Leed3D.Kernel.Gizmos;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Primitives;
using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Selection;
using Leed3D.Kernel.Tools;
using Leed3D.Runtime.Metadata;
using Xunit;

namespace Leed3D.Tests.Entities;

public class EntityTests
{
    [Fact]
    public void EntitySchemaRegistry_DefaultCoreDefinitions_AreRegistered()
    {
        var registry = new EntitySchemaRegistry(registerBuiltIns: true);

        Assert.True(registry.Count >= 4);

        // 1. player_spawn
        Assert.True(registry.TryGetDefinition("player_spawn", out var spawnDef));
        Assert.NotNull(spawnDef);
        Assert.Equal("Player Spawn", spawnDef.DisplayName);
        Assert.Equal(EntityGizmoShape.CapsuleWithArrow, spawnDef.GizmoShape);
        Assert.Equal(0.8f, spawnDef.Size.X);
        Assert.Equal(1.8f, spawnDef.Size.Y);
        Assert.True(spawnDef.DefaultProperties.TryGet("yaw", out var yawVal));
        Assert.Equal(0.0f, yawVal!.FloatValue);
        Assert.True(spawnDef.DefaultProperties.TryGet("team", out var teamVal));
        Assert.Equal(0, teamVal!.IntValue);

        // 2. light
        Assert.True(registry.TryGetDefinition("light", out var lightDef));
        Assert.NotNull(lightDef);
        Assert.Equal(EntityGizmoShape.LightGizmo, lightDef.GizmoShape);
        Assert.Equal("point", lightDef.DefaultProperties.GetString("light_type"));
        Assert.Equal(1.5f, lightDef.DefaultProperties.GetFloat("intensity"));
        Assert.Equal(12.0f, lightDef.DefaultProperties.GetFloat("radius"));
        Assert.Equal(45.0f, lightDef.DefaultProperties.GetFloat("spot_angle"));

        // 3. portal
        Assert.True(registry.TryGetDefinition("portal", out var portalDef));
        Assert.NotNull(portalDef);
        Assert.Equal(EntityGizmoShape.BoxVolume, portalDef.GizmoShape);
        Assert.Equal(new ColorRgba(0, 220, 240, 255), portalDef.Color);
        Assert.Equal("teleport", portalDef.DefaultProperties.GetString("event_name"));
        Assert.False(portalDef.DefaultProperties.GetBool("one_shot"));

        // 4. trigger
        Assert.True(registry.TryGetDefinition("trigger", out var triggerDef));
        Assert.NotNull(triggerDef);
        Assert.Equal(EntityGizmoShape.BoxVolume, triggerDef.GizmoShape);
        Assert.Equal(new ColorRgba(240, 200, 40, 255), triggerDef.Color);
        Assert.Equal("trigger_enter", triggerDef.DefaultProperties.GetString("event_name"));
    }

    [Fact]
    public void EntitySchemaRegistry_LoadFromJson_ArrayAndObjectFormats()
    {
        var registry = new EntitySchemaRegistry(registerBuiltIns: false);

        string json = """
        {
            "entities": [
                {
                    "className": "ammo_pickup",
                    "displayName": "Ammunition Crate",
                    "category": "Pickups",
                    "gizmoShape": "Box",
                    "color": "#FF9900",
                    "size": [0.6, 0.4, 0.4],
                    "properties": {
                        "ammo_count": 30,
                        "respawn_time": 15.5,
                        "auto_pickup": true,
                        "weapon_type": "shotgun"
                    }
                },
                {
                    "className": "jumppad",
                    "displayName": "Jump Pad",
                    "category": "Traps",
                    "gizmoShape": "Arrow",
                    "color": [100, 200, 255, 255],
                    "size": 1.5,
                    "properties": {
                        "launch_velocity": [0.0, 15.0, 5.0]
                    }
                }
            ]
        }
        """;

        int loaded = registry.LoadFromJson(json);
        Assert.Equal(2, loaded);

        // Validate ammo_pickup
        var ammo = registry.GetDefinition("ammo_pickup");
        Assert.NotNull(ammo);
        Assert.Equal("Ammunition Crate", ammo.DisplayName);
        Assert.Equal("Pickups", ammo.Category);
        Assert.Equal(EntityGizmoShape.Box, ammo.GizmoShape);
        Assert.Equal(ColorRgba.FromHex("#FF9900"), ammo.Color);
        Assert.Equal(new Vector3(0.6f, 0.4f, 0.4f), ammo.Size);
        Assert.Equal(30, ammo.DefaultProperties.GetInt("ammo_count"));
        Assert.Equal(15.5f, ammo.DefaultProperties.GetFloat("respawn_time"));
        Assert.True(ammo.DefaultProperties.GetBool("auto_pickup"));
        Assert.Equal("shotgun", ammo.DefaultProperties.GetString("weapon_type"));

        // Validate jumppad
        var jumppad = registry.GetDefinition("jumppad");
        Assert.NotNull(jumppad);
        Assert.Equal("Jump Pad", jumppad.DisplayName);
        Assert.Equal(EntityGizmoShape.Arrow, jumppad.GizmoShape);
        Assert.Equal(new ColorRgba(100, 200, 255, 255), jumppad.Color);
        Assert.Equal(new Vector3(1.5f, 1.5f, 1.5f), jumppad.Size);
        Assert.Equal(new Vector3(0.0f, 15.0f, 5.0f), jumppad.DefaultProperties.GetVec3("launch_velocity"));
    }

    [Fact]
    public void EditorScene_EntityManagement_AddRemoveAndUniqueIds()
    {
        var scene = new EditorScene();

        // Create default player spawns
        LevelEntity p1 = scene.CreateEntity("player_spawn");
        LevelEntity p2 = scene.CreateEntity("player_spawn");

        Assert.Equal("player_spawn_1", p1.Id);
        Assert.Equal("player_spawn_2", p2.Id);
        Assert.Equal(2, scene.Entities.Count);
        Assert.Same(p1, scene.GetEntity("player_spawn_1"));

        // Explicit ID creation with deduplication
        LevelEntity pExplicit = scene.CreateEntity("player_spawn", id: "player_spawn_1");
        Assert.NotEqual("player_spawn_1", pExplicit.Id);
        Assert.StartsWith("player_spawn_", pExplicit.Id);

        // Remove entity
        bool removed = scene.RemoveEntity(p1.Id);
        Assert.True(removed);
        Assert.Null(scene.GetEntity("player_spawn_1"));
        Assert.Equal(2, scene.Entities.Count);
    }

    [Fact]
    public void EditorScene_SnapshotCloning_DeepClonesEntities()
    {
        var scene = new EditorScene();
        LevelEntity ent = scene.CreateEntity("light", position: new Vector3(2.0f, 4.0f, 6.0f));
        ent.Properties.SetFloat("intensity", 3.0f);

        // Clone snapshot
        EditorScene snapshot = scene.CloneSnapshot();

        // Mutate original scene
        ent.Transform.Position = new Vector3(10.0f, 20.0f, 30.0f);
        ent.Properties.SetFloat("intensity", 9.9f);
        scene.CreateEntity("portal");

        Assert.Equal(2, scene.Entities.Count);
        Assert.Equal(new Vector3(10.0f, 20.0f, 30.0f), scene.GetEntity(ent.Id)!.Transform.Position);

        // Verify snapshot remained unchanged
        Assert.Single(snapshot.Entities);
        LevelEntity snapEnt = snapshot.GetEntity(ent.Id)!;
        Assert.NotNull(snapEnt);
        Assert.Equal(new Vector3(2.0f, 4.0f, 6.0f), snapEnt.Transform.Position);
        Assert.Equal(3.0f, snapEnt.Properties.GetFloat("intensity"));

        // Restore snapshot
        scene.RestoreFromSnapshot(snapshot);
        Assert.Single(scene.Entities);
        LevelEntity restoredEnt = scene.GetEntity(ent.Id)!;
        Assert.Equal(new Vector3(2.0f, 4.0f, 6.0f), restoredEnt.Transform.Position);
        Assert.Equal(3.0f, restoredEnt.Properties.GetFloat("intensity"));
    }

    [Fact]
    public void LevelEntity_GetWorldBounds_ComputesCorrectly()
    {
        var entity = new LevelEntity("test_spawn", "player_spawn", new Transform3D(new Vector3(5.0f, 2.0f, 0.0f)));
        BoundingBox3D bounds = entity.GetWorldBounds();

        // Default player_spawn size is (0.8, 1.8, 0.8), centered around (5, 2, 0)
        Assert.Equal(5.0f - 0.4f, bounds.Min.X, 3);
        Assert.Equal(5.0f + 0.4f, bounds.Max.X, 3);
        Assert.Equal(2.0f - 0.9f, bounds.Min.Y, 3);
        Assert.Equal(2.0f + 0.9f, bounds.Max.Y, 3);
        Assert.Equal(0.0f - 0.4f, bounds.Min.Z, 3);
        Assert.Equal(0.0f + 0.4f, bounds.Max.Z, 3);

        // With custom size property
        entity.Properties.SetVec3("size", new Vector3(4.0f, 4.0f, 4.0f));
        BoundingBox3D customBounds = entity.GetWorldBounds();
        Assert.Equal(5.0f - 2.0f, customBounds.Min.X, 3);
        Assert.Equal(5.0f + 2.0f, customBounds.Max.X, 3);
    }

    [Fact]
    public void MeshPicker_PickEntity_HitsCloserEntity()
    {
        var scene = new EditorScene();
        LevelEntity near = scene.CreateEntity("light", position: new Vector3(0.0f, 0.0f, 5.0f));
        LevelEntity far = scene.CreateEntity("light", position: new Vector3(0.0f, 0.0f, 15.0f));

        var ray = new Ray3D(Vector3.Zero, Vector3.UnitZ);
        EntityHit? hit = MeshPicker.PickEntity(scene, ray);

        Assert.NotNull(hit);
        Assert.Equal(near.Id, hit!.Value.EntityId);
        Assert.True(hit.Value.Distance < 6.0f);

        // Ray pointing opposite direction misses
        var rayMiss = new Ray3D(Vector3.Zero, -Vector3.UnitZ);
        Assert.Null(MeshPicker.PickEntity(scene, rayMiss));
    }

    [Fact]
    public void MeshPicker_PickEntitiesBoxMarquee_SelectsEntitiesInScreenRect()
    {
        var scene = new EditorScene();
        LevelEntity e1 = scene.CreateEntity("player_spawn", position: new Vector3(-2.0f, 0.0f, 5.0f));
        LevelEntity e2 = scene.CreateEntity("light", position: new Vector3(2.0f, 0.0f, 5.0f));

        // Screen projection mock
        Vector2? MockWorldToScreen(Vector3 pt)
        {
            if (pt.X < 0) return new Vector2(50.0f, 50.0f);
            return new Vector2(250.0f, 50.0f);
        }

        var rectLeftOnly = new RectangleF(0.0f, 0.0f, 100.0f, 100.0f);
        List<string> pickedLeft = MeshPicker.PickEntitiesBoxMarquee(scene, rectLeftOnly, MockWorldToScreen);
        Assert.Single(pickedLeft);
        Assert.Equal(e1.Id, pickedLeft[0]);

        var rectBoth = new RectangleF(0.0f, 0.0f, 300.0f, 100.0f);
        List<string> pickedBoth = MeshPicker.PickEntitiesBoxMarquee(scene, rectBoth, MockWorldToScreen);
        Assert.Equal(2, pickedBoth.Count);
        Assert.Contains(e1.Id, pickedBoth);
        Assert.Contains(e2.Id, pickedBoth);
    }

    [Fact]
    public void SelectionManager_EntitySelection_HandlesSelectionAndPivots()
    {
        var scene = new EditorScene();
        var mesh = PrimitiveGenerator.CreateBox(Vector3.One);
        SceneObject obj = scene.AddMeshObject("Cube", mesh, new Vector3(0.0f, 0.0f, 0.0f));
        LevelEntity ent = scene.CreateEntity("player_spawn", position: new Vector3(10.0f, 0.0f, 0.0f));

        var selection = new SelectionManager();
        selection.SetMode(InteractionMode.Object, scene);

        // Select entity
        selection.SelectSingleEntity(ent.Id);
        Assert.True(selection.HasSelection);
        Assert.Contains(ent.Id, selection.SelectedEntityIds);
        Assert.Equal(ent.Id, selection.ActiveEntityId);
        Assert.Equal(new Vector3(10.0f, 0.0f, 0.0f), selection.GetSelectionPivotWorld(scene));

        // Multi-select both object and entity
        selection.ToggleSelectObject(obj.Id);
        Assert.Single(selection.SelectedObjectIds);
        Assert.Single(selection.SelectedEntityIds);

        // Average pivot: (0 + 10) / 2 = 5
        Vector3? combinedPivot = selection.GetSelectionPivotWorld(scene);
        Assert.NotNull(combinedPivot);
        Assert.Equal(5.0f, combinedPivot!.Value.X, 3);

        // Clear selection
        selection.ClearSelection();
        Assert.False(selection.HasSelection);
        Assert.Empty(selection.SelectedEntityIds);
    }

    [Fact]
    public void TransformApplicator_AppliesTransformToSelectedEntities_SyncsYaw()
    {
        var scene = new EditorScene();
        LevelEntity spawn = scene.CreateEntity("player_spawn", position: new Vector3(0.0f, 0.0f, 0.0f));
        var selection = new SelectionManager();
        selection.SetMode(InteractionMode.Object, scene);
        selection.SelectSingleEntity(spawn.Id);

        var applicator = new TransformApplicator();
        applicator.BeginSession(scene, selection, Vector3.Zero);

        // 1. Translate
        applicator.ApplyTranslation(scene, new Vector3(3.0f, 0.0f, -4.0f));
        Assert.Equal(new Vector3(3.0f, 0.0f, -4.0f), spawn.Transform.Position);

        // 2. Rotate (Yaw = 90 deg)
        Quaternion rotQ = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 90.0f * MathF.PI / 180.0f);
        applicator.ApplyRotation(scene, rotQ, new Vector3(0.0f, 90.0f, 0.0f));
        Assert.Equal(90.0f, spawn.Transform.RotationEulerDegrees.Y, 2);
        // Verify yaw property in metadata is kept in sync
        Assert.True(spawn.Properties.TryGet("yaw", out var yawVal));
        Assert.Equal(90.0f, yawVal!.FloatValue, 2);

        // 3. Restore initial state
        applicator.RestoreInitialState(scene);
        Assert.Equal(Vector3.Zero, spawn.Transform.Position);
        Assert.Equal(0.0f, spawn.Transform.RotationEulerDegrees.Y, 2);
        Assert.Equal(0.0f, spawn.Properties.GetFloat("yaw"));
    }

    [Fact]
    public void InteractiveEditController_DeletesAndClonesEntities()
    {
        var scene = new EditorScene();
        var undoStack = new UndoRedoStack();
        var selection = new SelectionManager();
        selection.SetMode(InteractionMode.Object, scene);

        LevelEntity ent = scene.CreateEntity("portal", position: new Vector3(1.0f, 2.0f, 3.0f));
        selection.SelectSingleEntity(ent.Id);

        var controller = new InteractiveEditController();

        // 1. Quick Clone (Shift+D)
        controller.ExecuteQuickClone(scene, selection, undoStack);
        Assert.Equal(2, scene.Entities.Count);
        string clonedId = selection.SelectedEntityIds.Single();
        Assert.NotEqual(ent.Id, clonedId);
        LevelEntity cloned = scene.GetEntity(clonedId)!;
        Assert.Equal(new Vector3(1.0f, 2.0f, 3.0f), cloned.Transform.Position);
        Assert.Equal("teleport", cloned.Properties.GetString("event_name"));

        // 2. Delete selected clone
        controller.ExecuteDeleteObjects(scene, selection, undoStack);
        Assert.Single(scene.Entities);
        Assert.Null(scene.GetEntity(clonedId));
        Assert.NotNull(scene.GetEntity(ent.Id));

        // 3. Undo restore clone
        undoStack.Undo(scene);
        Assert.Equal(2, scene.Entities.Count);
        Assert.NotNull(scene.GetEntity(clonedId));
    }
}
