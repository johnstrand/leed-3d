# Agent Instructions — Leed3D

## Issue & Repository Conventions
- Interpret bare references like `#2` as GitHub issue numbers for `johnstrand/leed-3d`, unless the conversation clearly establishes a different meaning.
- When implementing an issue, inspect the issue body and acceptance criteria carefully.
- Maintain **0 warnings and 0 errors** on `dotnet build Leed3D.sln` and ensure all xUnit tests pass (`dotnet test Leed3D.sln`) before completing any issue.

---

## High-Concept Overview

**Leed3D** is a fast, lightweight **3D level and blockout editor** for simple 3D games, built with **C# / .NET 10**, **Raylib-cs**, and **ImGui.NET** over a **100% headless, xUnit-tested geometry kernel**.

It bridges the gap between **TrenchBroom** (rapid grid-snapped blockouts, Valve-220 world-space auto-texturing, CSG booleans, and gameplay entity placement) and **Blender** (interactive 3D transform gizmos, modal `G`/`R`/`S` axis/plane-locked transforms, and direct vertex/edge/face N-gon topology editing). Levels save to a human-readable **dual-layer `.level.json` format** containing both lossless editable N-gon topology and pre-baked triangulated render/collision buffers.

---

## Solution & Project Architecture

```text
Leed3D.sln
├── Directory.Build.props          # net10.0, Nullable=enable, ImplicitUsings=enable, AllowUnsafeBlocks=true
├── Directory.Packages.props       # Central Package Management (Raylib-cs, ImGui.NET, xUnit)
├── src/
│   ├── Leed3D.Runtime/            # Zero-dependency runtime level DTOs & .level.json loader for games
│   ├── Leed3D.Kernel/             # 100% headless N-gon mesh kernel, primitives, CSG, UVs, WalkController, exporters
│   └── Leed3D.Editor/             # Raylib-cs + ImGui.NET desktop editor, RlImGui bridge, 3D viewport, gizmos, shaders
└── tests/
    └── Leed3D.Tests/              # Headless xUnit test suite covering Leed3D.Runtime and Leed3D.Kernel
```

### Strict Dependency Boundaries
1. **`Leed3D.Runtime`**:
   - **Zero external NuGet dependencies** (uses only `System.Numerics` and `System.Text.Json`).
   - Can be referenced directly or copied into any C# 3D game to load `.level.json` files.
2. **`Leed3D.Kernel`**:
   - References `Leed3D.Runtime` only.
   - **Must NEVER reference `Raylib-cs` or `ImGui.NET`**. Every geometry operation, primitive generator, topology edit, 3D raycast/gizmo math calculation, BSP boolean, UV projection, `WalkController` physics step, and `.obj`/`.gltf` exporter lives here and is 100% unit-testable headlessly.
3. **`Leed3D.Editor`**:
   - References `Leed3D.Kernel`, `Leed3D.Runtime`, `Raylib-cs`, and `ImGui.NET`.
   - Contains the self-contained `RlImGui` renderer/input bridge, 3D viewport camera & gizmo rendering, GLSL 330 Lit Preview shader, and ImGui docking/panel UI.

---

## Architectural Invariants

### 1. Coordinate System (`+X Right, +Y Up, +Z Away`)
- **Kernel & Editor UI Space** is **Left-Handed**:
  - `+X` = Right
  - `+Y` = Up
  - `+Z` = Away / Forward (into the screen)
- **Raylib / OpenGL / glTF Space** is **Right-Handed** (`+X` Right, `+Y` Up, `-Z` Forward / `+Z` Toward Viewer).
- All conversions between Editor space and Raylib/OpenGL space go through `Leed3D.Kernel.Math.CoordSpace`:
  - Position/Direction flip: `(x, y, z) <-> (x, y, -z)`
  - Triangle index winding flip when negating Z: `(i0, i1, i2) <-> (i0, i2, i1)` so outward face normals and backface culling remain consistent.

### 2. Indexed N-Gon Mesh Kernel (`EditableMesh`)
- Primary storage uses stable ID-indexed vertices (`MeshVertex`) and ordered N-gon faces (`PolygonFace` with `List<int> VertexIds`, `MaterialId`, `FaceUvMapping`, and `ElementMetadata`).
- Adjacency queries (unique edges, half-edge loops, vertex-to-face and face-to-face neighbors) are computed via `TopologyGraph.Build(EditableMesh)`.
- After any topological mutation (`Extrude`, `Inset`, `Weld`, `ConnectVertices`, `FillFace`, or `CsgBooleanEngine`), call `EditableMesh.ValidateAndRepair()` to prune degenerate (<3 vertex) faces, remove orphaned vertices, and invalidate cached topology/triangulation.
- Polygon triangulation uses deterministic **2D Ear-Clipping projected onto the face's dominant normal plane** (`PolygonTriangulator`).

### 3. TrenchBroom / Valve-220 Auto-Texturing (`FaceUvMapping`)
- Every `PolygonFace` carries a `FaceUvMapping` (`UAxis`, `VAxis`, `Offset`, `Scale`, `RotationDegrees`, `TextureLock`).
- When `TextureLock == false`, `UAxis` and `VAxis` are derived automatically from the face normal's closest cardinal world plane (6-axis box projection), so extruding or resizing walls tiles textures seamlessly in world space.
- When `TextureLock == true`, moving, rotating, or scaling geometry transforms `UAxis`, `VAxis`, and compensates `Offset` so the texture stays locked to the face.

### 4. Snapshot-Based Undo/Redo (`UndoRedoStack`)
- Interactive gizmo drags and modal transforms (`G` / `R` / `S`) capture a pre-operation snapshot when the operation starts and commit a single `UndoRecord` on confirm (`LeftClick` / `Enter`), or restore the pre-operation snapshot on cancel (`RightClick` / `Esc`).

---

## Common Commands

```powershell
# Build entire solution
dotnet build Leed3D.sln

# Run all headless unit tests
dotnet test Leed3D.sln

# Run the Leed3D Editor
dotnet run --project src/Leed3D.Editor

# Run the automated issue agent
.\run-agent.ps1
.\run-agent.ps1 -Loop
```
