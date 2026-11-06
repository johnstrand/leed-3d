# Leed3D

[![Build & Test](https://img.shields.io/badge/build-passing-brightgreen.svg)]()
[![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-purple.svg)](LICENSE)

**Leed3D** is a modern, lightweight, standalone retro 3D level editor and blockout toolkit engineered in C# for **.NET 10**. Drawing architectural inspiration from legendary tools like TrenchBroom, Hammer, and Blender, Leed3D empowers level designers and indie game developers to rapidly sculpt, texture, tag, and playtest geometric levels with zero external heavy engine dependencies.

---

## Architecture Overview

Leed3D is organized as a modular, cleanly layered multi-project solution adhering to strict architectural separation:

```
┌────────────────────────────────────────────────────────┐
│                     Leed3D.Editor                      │
│   (Raylib-cs 7.0 + ImGui.NET 1.91 Desktop Level IDE)   │
└──────────────────────────┬─────────────────────────────┘
                           │
┌──────────────────────────▼─────────────────────────────┐
│                     Leed3D.Kernel                      │
│   (Pure C# Geometry, CSG, Half-Edge Mesh, Physics,     │
│    Valve-220 UVs, Procedural Textures, Baker, Exporters)│
└──────────────────────────┬─────────────────────────────┘
                           │
┌──────────────────────────▼─────────────────────────────┐
│                    Leed3D.Runtime                      │
│   (Zero-Dependency Lightweight Level Loader & Contracts│
│    Pre-baked Render Submeshes, Collision Triangles)    │
└────────────────────────────────────────────────────────┘
```

1. **`Leed3D.Runtime`** (`netstandard2.1` / `.NET 10.0`):
   - **Zero external dependencies** (no Raylib, no OpenGL, no third-party native libraries).
   - Provides contracts and fast deserialization for Dual-Layer `.level.json` files.
   - Supplies ready-to-render flat vertex/normal/UV arrays and collision triangle buffers for direct drop-in integration into any C# game engine (MonoGame, Raylib-cs, Godot C#, FNA, Silk.NET, Stride, or custom OpenGL/Vulkan/DirectX engines).

2. **`Leed3D.Kernel`** (`.NET 10.0`):
   - Core mathematical and geometric engine: Half-edge mesh data structure (`EditableMesh`) supporting arbitrary N-gons.
   - Interactive CSG Boolean modeling (Union, Difference/Carve, Intersection) using robust BSP trees with coplanar polygon retriangulation.
   - Valve 220 planar UV projection engine with real-time texture lock transform tracking.
   - Built-in procedural prototype texture generator (checkers, grids, dots, lava, hazard stripes).
   - Granular typed metadata system for objects, faces, edges, and vertices (`surface=lava`, `collision=none`, damage, friction, etc.).
   - Headless kinematic First-Person Walk Controller (`WalkController`) with capsule collide-and-slide, stair step-up, slopes, and portals/triggers.
   - Multi-format exporters: Dual-Layer `.level.json` Runtime Baker, Wavefront `.obj`/`.mtl`, and zero-dependency glTF 2.0 (`.gltf`).

3. **`Leed3D.Editor`** (`.NET 10.0`):
   - Desktop IDE powered by **Raylib-cs 7.0** and **ImGui.NET 1.91**.
   - Dockable multi-panel layout: Scene Outliner, Object Transform Inspector, Texture Browser, UV Inspector, Metadata Inspector, Entity Inspector, and Viewport Gizmo.
   - Real-time Lit & Textured preview mode (`F4`) with GLSL 330 forward multi-light + fog shader.
   - In-editor First-Person Walk playtest mode (`F5` / `Tab`).

---

## Key Features

- **Blender-Style 3D Viewport Navigation & Fly Mode:**
  - Orbit, Pan, and Zoom with smooth interpolation.
  - RMB Fly Camera (`W/A/S/D/Q/E` + Mouse Look + Shift sprint).
  - Modal transforms with Blender keybindings: `G` (Grab/Translate), `R` (Rotate), `S` (Scale), with numeric entry and axis locking (`X`, `Y`, `Z`, `Shift+X/Y/Z`).
  - Interactive 3-Axis Translation, Rotation, and Scale Viewport Gizmos.

- **Parametric Geometric Primitives:**
  - Instant generator for Box, Cylinder, Cone, Sphere, Torus, Wedge, and Multi-Step Staircases with configurable subdivisions and texture coordinates.

- **Half-Edge Editable Mesh Modeling:**
  - Unified Vertex (`1`), Edge (`2`), Face (`3`), and Object (`Tab`) selection modes.
  - Interactive Extrude (`E`), Inset (`I`), and Bevel (`B`) operations.
  - Loop Cut (`Ctrl+R`) and knife topology division.
  - Interactive CSG Boolean modeling (`Ctrl+Shift+U` Union, `Ctrl+Shift+D` Difference, `Ctrl+Shift+I` Intersection).

- **Valve 220 Planar UV Projection Engine & Texture Lock:**
  - TrenchBroom / Valve 220 planar texture projection with independent U/V axis directions, scale, offset, and rotation.
  - **Texture Lock:** Moving or rotating objects or faces automatically recalculates planar UV coordinates to keep textures glued to world-space coordinates.
  - Built-in Texture Browser panel and per-face UV Inspector with Fit, Align, and Reset operations.
  - Built-in procedural prototype textures: `dev_grid_gray`, `dev_grid_orange`, `dev_grid_blue`, `dev_grid_green`, `dev_grid_red`, `dev_grid_dark`, `dev_lava`, and `dev_checker`.

- **Granular Typed Metadata System:**
  - Attach string, float, integer, boolean, Color, Vector3, and tag metadata to individual Objects, Faces, Edges, or Vertices.
  - Built-in game metadata: `surface=stone|lava|wood|metal`, `collision=none`, `damage=50`, `friction=0.8`.
  - Visual Metadata Inspector panel with add/remove/edit functionality.

- **Non-Geometry Gameplay Entities:**
  - Extensible schema-driven entities system loaded from `entities.schema.json`.
  - Built-in entities: `player_spawn`, `light` (point lights with color, intensity, range, attenuation), `portal` (teleport triggers linking to destination entities), and `trigger` (hazard, sound, or event zones).
  - Viewport 3D gizmos: directional arrows, light wireframe spheres, trigger/portal bounding boxes with translucent color fill.

- **Real-Time Lit & Textured Viewport Preview Mode (`F4`):**
  - Custom GLSL 330 multi-light forward shader supporting multiple point lights and directional sun.
  - Exponential distance fog, specular highlights, and per-face material color rendering.

- **Headless Kinematic Walk Controller & Playtest (`F5`):**
  - Quake/Half-Life style player physics: capsule collide-and-slide, slopes up to 45°, stair step-up (< 0.35m), sprinting, crouching, jumping.
  - Surface metadata detection (e.g. `surface=lava`), trigger detection, and seamless portal teleportation.
  - Press `F5` in the editor to immediately drop into First-Person Walk Mode and playtest the geometry.

- **Scene Outliner & Transform Inspector:**
  - Hierarchy tree of all meshes and gameplay entities with search filter.
  - Visibility toggles (`[V]`), Lock toggles (`[L]`), inline renaming (`F2`), duplication (`Shift+D`), deletion (`Del`).
  - Numeric Position, Rotation (Euler degrees), and Scale inputs with full Undo/Redo.
  - Pivot utilities: `Origin to Geometry Centroid`, `Origin to Bottom Bounds`, and `Apply/Freeze Scale & Rotation into Mesh`.

- **Dual-Layer `.level.json` & Multi-Format Exporters:**
  - **Layer 1 (Editor):** Lossless topological serialization preserving N-gons, half-edges, UV matrices, and element metadata.
  - **Layer 2 (Baked Runtime):** Pre-baked, pre-triangulated draw batches grouped by material and pre-extracted collision meshes.
  - One-click Wavefront `.obj`/`.mtl` export and glTF 2.0 (`.gltf`) mesh export.

---

## Keyboard Shortcuts Reference

| Shortcut | Description |
| :--- | :--- |
| **Viewport & Navigation** | |
| `RMB + Drag` | Camera Fly Look |
| `W / A / S / D` | Fly Camera move (during RMB look) |
| `Q / E` | Fly Camera Down / Up (during RMB look) |
| `Shift` (hold) | Fly / Walk Sprint speed boost |
| `MMB + Drag` | Orbit camera around pivot |
| `Shift + MMB` | Pan camera horizontally/vertically |
| `Mouse Wheel` | Zoom camera in / out |
| `Home` | Frame / Center camera on selection |
| **Modes & Selections** | |
| `Tab` | Toggle Object Mode $\leftrightarrow$ Edit Mode |
| `1` / `2` / `3` | Edit Mode: Vertex / Edge / Face select mode |
| `LMB` | Select element / Clear selection |
| `Shift + LMB` | Add / Toggle selection |
| `A` / `Alt + A` | Select All / Deselect All |
| `Ctrl + I` | Invert Selection |
| `F4` | Toggle Real-Time Lit & Textured Preview Mode |
| `F5` | Toggle First-Person Walk Playtest Mode |
| **Modeling & Transforms** | |
| `G` / `R` / `S` | Modal Grab (Move) / Rotate / Scale |
| `X` / `Y` / `Z` | Constrain transform to X / Y / Z axis |
| `Shift + X/Y/Z` | Constrain transform to YZ / XZ / XY plane |
| `Enter` / `LMB` | Confirm active transform / extrude / inset |
| `Esc` / `RMB` | Cancel active transform / extrude / inset |
| `E` | Extrude selected faces |
| `I` | Inset selected faces |
| `B` | Bevel selected edges |
| `Ctrl + R` | Loop Cut mesh |
| `Del` | Delete selected objects / faces / vertices |
| `Shift + D` | Duplicate selected object or entity |
| `F2` | Rename selected object in Outliner |
| **File Operations & History** | |
| `Ctrl + N` | New Level |
| `Ctrl + O` | Open Level (`.level.json`) |
| `Ctrl + S` | Save Level |
| `Ctrl + Shift + S` | Save Level As... |
| `Ctrl + Z` | Undo last action |
| `Ctrl + Y` / `Ctrl + Shift + Z` | Redo action |

---

## Integrating `Leed3D.Runtime` in Your Game Engine

The `Leed3D.Runtime` package has **zero dependencies** and can be loaded directly from any .NET game project:

```csharp
using Leed3D.Runtime;

// 1. Load the dual-layer .level.json file
LeedLevelDocument level = LeedLevelLoader.Load("levels/temple_blockout.level.json");

Console.WriteLine($"Loaded level '{level.Name}' with {level.Baked.Entities.Count} entities.");

// 2. Iterate through pre-baked render submeshes (zero runtime triangulation needed!)
foreach (BakedSubmeshDto submesh in level.GetRenderSubmeshes())
{
    string materialId = submesh.MaterialId;
    float[] positions = submesh.GetPositionsFlat(); // [x0, y0, z0, x1, y1, z1, ...]
    float[] normals   = submesh.GetNormalsFlat();   // [nx0, ny0, nz0, ...]
    float[] uvs       = submesh.GetUVsFlat();       // [u0, v0, u1, v1, ...]
    int[]   indices   = submesh.GetIndices();       // Flat index buffer

    // Upload directly into your engine's GPU Vertex/Index Buffer:
    // MyGraphicsDevice.CreateMesh(positions, normals, uvs, indices, materialId);
}

// 3. Extract collision triangles with surface metadata
foreach (BakedCollisionTriangleDto tri in level.Baked.CollisionTriangles)
{
    // tri.V0, tri.V1, tri.V2 (System.Numerics.Vector3)
    // tri.Normal
    // tri.Metadata: e.g. tri.Metadata["surface"] == "lava"
}

// 4. Query gameplay entities
LevelEntityDto? spawn = level.FindFirstEntity("player_spawn");
if (spawn is not null)
{
    Console.WriteLine($"Player spawn at: ({spawn.Transform.Position.X}, {spawn.Transform.Position.Y}, {spawn.Transform.Position.Z})");
}

foreach (LevelEntityDto light in level.GetEntitiesByClass("light"))
{
    string hexColor = light.Properties.GetValueOrDefault("color")?.ToString() ?? "#FFFFFFFF";
    float intensity = float.Parse(light.Properties.GetValueOrDefault("intensity")?.ToString() ?? "1.0");
    // Spawn point light in your game engine
}
```

---

## Sample Level: Ancient Temple Blockout

The repository includes a ready-to-play sample blockout level located at `samples/temple_blockout.level.json`.

Features demonstrated in `temple_blockout.level.json`:
- **Atrium:** 24x24m courtyard surrounded by high walls, 4 classical cylinder pillars (`dev_grid_blue`), and warm torch point lights.
- **CSG Carved Partition:** Dividing wall featuring a carved doorway and a carved viewing window created via CSG boolean subtraction.
- **5-Step Staircase:** Functional stair steps leading up to an elevated Altar platform (`dev_grid_green`) at Y = 1.25m.
- **Lava Hazard Pit:** East corner pit textured with `dev_lava` and tagged with `surface=lava` and `damage=50`.
- **Secret Room behind Fake Wall:** Recessed alcove hidden behind `Fake_Illusion_Wall` tagged with `collision=none` and illuminated with eerie purple light.
- **Gameplay Portals:** Portal volume on the altar platform targeting destination `balcony_dest` back at the entrance.

To load the sample in the editor:
- Click **Help $\rightarrow$ Load Sample Temple Blockout Level**, or
- Click **File $\rightarrow$ Open Level...** and select `samples/temple_blockout.level.json`.
- Press **`F4`** for Lit Preview or **`F5`** to walk through the temple!

---

## Building and Running

### Prerequisites
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) or later.

### Build the Solution
```powershell
dotnet build Leed3D.sln -c Release
```

### Run All Headless Unit & Integration Tests
```powershell
dotnet test Leed3D.sln
```

### Launch the Desktop Editor
```powershell
dotnet run --project src/Leed3D.Editor
```

---

## License

Leed3D is open source software licensed under the [MIT License](LICENSE).
