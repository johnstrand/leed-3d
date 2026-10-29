# Leed3D Implementation Roadmap

> *"Basically a cross between TrenchBroom and Blender — a rudimentary 3D level editor for simple 3D games."* (Graduated from [`johnstrand/ideas#7`](https://github.com/johnstrand/ideas/issues/7))

---

## 1. Architecture & Design Decisions

| Decision Area | Agreed Design |
| :--- | :--- |
| **Tech Stack** | **C# / .NET 10**, **`Raylib-cs` (8.1.0)**, **`ImGui.NET`** (via self-contained `RlImGui` bridge), **`xUnit`** |
| **Solution Layout** | 4 Projects: `Leed3D.Runtime` (zero-dep game loader), `Leed3D.Kernel` (100% headless geometry/CSG/UV/WalkController/exporters), `Leed3D.Editor` (Raylib + ImGui desktop app), `Leed3D.Tests` (headless xUnit suite) |
| **Coordinate System** | **Left-Handed `+X` Right, `+Y` Up, `+Z` Away** in Kernel, UI, and `.level.json` Editor layer; centralized `CoordSpace` adapter converts `(x, y, z) <-> (x, y, -z)` + triangle winding `(i0, i2, i1)` at the Raylib viewport boundary and for Right-Handed OpenGL/glTF exports |
| **N-Gon Mesh Kernel** | Indexed N-gon Face-Vertex storage (`EditableMesh`, `MeshVertex`, `PolygonFace`) with on-demand `TopologyGraph` adjacency queries + deterministic 2D projected **Ear-Clipping N-Gon Triangulator** |
| **CSG Booleans** | Destructive 3D BSP-tree polygon clipping (`Union`, `Subtract`, `Intersect`) backed by `UndoRedoStack` snapshots so results remain immediately editable N-gons with preserved UVs/materials/metadata |
| **UV & Auto-Texturing** | Valve-220 / TrenchBroom `FaceUvMapping` (6-axis world box-planar projection by default; explicit `UAxis`/`VAxis` transformed on move/rotate when `TextureLock` is enabled) + built-in procedural blockout textures & local `textures/` folder scanner |
| **Preview & Walk Mode** | Real-time GLSL 330 multi-light + fog shader for Lit Preview; headless `WalkController` in `Leed3D.Kernel` (capsule sweep, step-up, jump, respecting `collision=none` face/object metadata) |
| **Level Format & Exports** | Dual-layer `.level.json` (Editor N-gon layer + Baked Runtime triangulated submeshes/collision layer), zero-dependency `Leed3D.Runtime` C# loader, plus zero-dependency `.obj`/`.mtl` and `.gltf` 2.0 exporters |

---

## 2. Token Optimization Strategy (`<model>-<effort>`)

Issues are decomposed into **26 contract-first atomic slices** ordered `#1` through `#26` so `.\run-agent.ps1 -Loop` can execute them sequentially:

- **`gemini-3.8-flash-low` (1 issue — 4%)**: Solution scaffolding and build props (`#1`).
- **`gemini-3.8-flash-medium` (10 issues — 38%)**: Well-defined math helpers, scene/undo containers, ImGui panels/inspectors, procedural texture generation, and standard `.obj`/`.gltf` file writers (`#2`, `#5`, `#7`, `#14`, `#16`, `#18`, `#19`, `#20`, `#25`, `#26`).
- **`gemini-3.8-flash-high` (7 issues — 27%)**: Structured algorithmic tasks with clear specifications: parametric primitive geometry, ray-mesh picking, modal `G`/`R`/`S` state machine, clone/mirror/array modifiers, entity schemas & gizmos, GLSL 330 lighting shader, and dual-layer `.level.json` serialization (`#4`, `#8`, `#10`, `#13`, `#21`, `#22`, `#24`).
- **`gemini-4-argon-medium` (5 issues — 19%)**: Multi-component subsystems requiring careful invariant preservation: `EditableMesh` + `TopologyGraph` + Ear-Clipping triangulator (`#3`), `RlImGui` bridge + 3D viewport camera (`#6`), vertex/edge topology ops (`#11`), Valve-220 UV projection + Texture Lock math (`#17`), and capsule collide-and-slide `WalkController` (`#23`).
- **`gemini-4-argon-high` (3 issues — 12%)**: Reserved strictly for the three hardest 3D spatial/topological problems: interactive 3D transform gizmo ray-axis/plane math (`#9`), N-gon face extrude/inset/normal BFS (`#12`), and 3D BSP-tree CSG boolean clipping (`#15`).

> **Total Token Efficiency:** **69% of issues (18 / 26)** run on **Gemini 3.8 Flash**, **19% (5 / 26)** on **Gemini 4 Argon (Medium)**, and only **12% (3 / 26)** on **Gemini 4 Argon (High)**.

---

## 3. Milestone & Issue Breakdown

### Milestone 1: Foundation, Viewport, Camera & Parametric Primitives
| # | Title | Model-Effort Tag | Area Labels | Depends On |
| :--- | :--- | :--- | :--- | :--- |
| **#1** | Initialize .NET 10 Solution, 4-Project Architecture, and Centralized Build Configuration | `gemini-3.8-flash-low` | `area:runtime`, `area:kernel`, `area:testing` | — |
| **#2** | Implement Coordinate Space Adapter (`+X Right, +Y Up, +Z Away`), Math Primitives & Snapping Helpers | `gemini-3.8-flash-medium` | `area:kernel`, `area:testing` | #1 |
| **#3** | Implement Core `EditableMesh` Indexed N-Gon Data Model, `TopologyGraph` Adjacency & Ear-Clipping Triangulator | `gemini-4-argon-medium` | `area:kernel`, `area:testing` | #2 |
| **#4** | Implement Parametric Primitive Generators (Plane, Box, Cylinder, Cone, Geosphere, Icosahedron) | `gemini-3.8-flash-high` | `area:kernel`, `area:testing` | #3 |
| **#5** | Implement Scene Graph, `SceneObject` Transform Hierarchy, and Snapshot Undo/Redo Command Stack | `gemini-3.8-flash-medium` | `area:kernel`, `area:testing` | #3 |
| **#6** | Implement Self-Contained `RlImGui` Bridge, Docking Editor Shell, and Single 3D Viewport with Perspective & Ortho `X`/`Y`/`Z` Camera | `gemini-4-argon-medium` | `area:viewport`, `area:editor-ui` | #4, #5 |
| **#7** | Implement Parametric Primitive Creation Toolbar & Interactive Resolution/Segment Creation Popup | `gemini-3.8-flash-medium` | `area:editor-ui`, `area:viewport` | #6 |

### Milestone 2: N-Gon Kernel, 3D Gizmos, Sub-Object Editing & Modifiers
| # | Title | Model-Effort Tag | Area Labels | Depends On |
| :--- | :--- | :--- | :--- | :--- |
| **#8** | Implement Object & Sub-Element (`Vertex`, `Edge`, `Face`) Raycasting, Picking & Selection Manager | `gemini-3.8-flash-high` | `area:kernel`, `area:viewport`, `area:testing` | #6 |
| **#9** | Implement Interactive 3D Transform Gizmo (Translate Arrows, Rotate Rings, Scale Cubes, Dual-Axis Plane Quads) | `gemini-4-argon-high` | `area:kernel`, `area:viewport`, `area:testing` | #8 |
| **#10** | Implement Blender-Style Modal Transform Operators (`G` / `R` / `S`) with Keyboard Axis (`X`/`Y`/`Z`) & Plane (`Shift+X`/`Y`/`Z`) Locks | `gemini-3.8-flash-high` | `area:kernel`, `area:viewport`, `area:editor-ui` | #9 |
| **#11** | Implement Vertex & Edge Topology Operations: Snap/Weld (`M`), Connect Vertices (`J`), Fill Face (`F`), Edge Subdivide & Edge Extrude | `gemini-4-argon-medium` | `area:kernel`, `area:testing` | #3, #8 |
| **#12** | Implement Face Topology Operations: Extrude / Push-Pull (`E`), Inset (`I`), Delete, and Normal Flip / Recalculate | `gemini-4-argon-high` | `area:kernel`, `area:testing` | #11 |
| **#13** | Implement Duplication & Repetition Tools: Quick Clone (`Shift+D`), Axis Mirror (`X`/`Y`/`Z`), and Linear/Radial Array | `gemini-3.8-flash-high` | `area:kernel`, `area:testing` | #12 |
| **#14** | Implement Edit Mode UI Toolbar, Context Menus, Selection Info Overlay & Keyboard Shortcut Bindings | `gemini-3.8-flash-medium` | `area:editor-ui`, `area:viewport` | #10, #12, #13 |

### Milestone 3: CSG Mesh Booleans & TrenchBroom-Style Auto-Texturing
| # | Title | Model-Effort Tag | Area Labels | Depends On |
| :--- | :--- | :--- | :--- | :--- |
| **#15** | Implement 3D BSP-Tree Polygon Clipping & Closed-Mesh CSG Booleans (`Union`, `Subtract`, `Intersect`) | `gemini-4-argon-high` | `area:csg`, `area:kernel`, `area:testing` | #12 |
| **#16** | Integrate CSG Boolean Workflow in Editor UI with Cutter Preview & Undo/Redo | `gemini-3.8-flash-medium` | `area:csg`, `area:editor-ui`, `area:viewport` | #15 |
| **#17** | Implement Valve-220 / TrenchBroom Planar UV Projection Engine & Texture Lock Transform Tracking | `gemini-4-argon-medium` | `area:uv-texturing`, `area:kernel`, `area:testing` | #12, #15 |
| **#18** | Implement Built-in Procedural Prototyping Texture Generator & Project Texture Library Manager | `gemini-3.8-flash-medium` | `area:uv-texturing`, `area:kernel`, `area:viewport` | #17 |
| **#19** | Implement Texture Browser Panel, Face Material Assignment & Per-Face UV Inspector | `gemini-3.8-flash-medium` | `area:uv-texturing`, `area:editor-ui`, `area:viewport` | #17, #18 |

### Milestone 4: Entities, Element Metadata, Walk Preview & Level I/O
| # | Title | Model-Effort Tag | Area Labels | Depends On |
| :--- | :--- | :--- | :--- | :--- |
| **#20** | Implement Granular Typed Key-Value Metadata System for Objects, Faces, Edges & Vertices + Metadata Inspector UI | `gemini-3.8-flash-medium` | `area:entities-walk`, `area:kernel`, `area:editor-ui` | #14 |
| **#21** | Implement Non-Geometry Gameplay Entities (`player_spawn`, `light`, `portal`/Trigger), Custom `entities.schema.json` Loader & Viewport Entity Gizmos | `gemini-3.8-flash-high` | `area:entities-walk`, `area:kernel`, `area:viewport` | #20 |
| **#22** | Implement Real-Time Lit & Textured Viewport Preview Mode (GLSL 330 Multi-Light + Fog Shader) | `gemini-3.8-flash-high` | `area:viewport`, `area:entities-walk` | #19, #21 |
| **#23** | Implement Headless First-Person Walk Mode Physics Controller (`WalkController`) & In-Editor Walk Playtest (`Tab` / `F5`) | `gemini-4-argon-medium` | `area:entities-walk`, `area:kernel`, `area:viewport`, `area:testing` | #21, #22 |
| **#24** | Implement Dual-Layer `.level.json` Schema, Runtime Baker, and Zero-Dependency `Leed3D.Runtime` C# Level Loader | `gemini-3.8-flash-high` | `area:runtime`, `area:io-export`, `area:testing` | #17, #20, #21 |
| **#25** | Implement Zero-Dependency `.obj`/`.mtl` and `.gltf` 2.0 Mesh Exporters | `gemini-3.8-flash-medium` | `area:io-export`, `area:kernel`, `area:testing` | #24 |
| **#26** | Implement Scene Outliner, Object Transform Inspector, File I/O Dialogs, and Sample Playable Blockout Level | `gemini-3.8-flash-medium` | `area:editor-ui`, `area:io-export` | #23, #24, #25 |
