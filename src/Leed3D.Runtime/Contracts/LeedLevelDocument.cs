namespace Leed3D.Runtime;

/// <summary>
/// Container for lossless editor scene data preserving N-gon topology, metadata, and editable entities.
/// </summary>
public sealed class EditorLayerData
{
    public List<EditorObjectDto> Objects { get; set; } = [];
    public List<LevelEntityDto> Entities { get; set; } = [];
}

/// <summary>
/// Container for baked game-ready runtime assets (zero-triangulation render submeshes, collision triangles, world bounds).
/// </summary>
public sealed class BakedRuntimeLayerData
{
    public BoundingBoxDto WorldBounds { get; set; } = new();
    public List<BakedSubmeshDto> RenderSubmeshes { get; set; } = [];
    public List<BakedCollisionTriangleDto> CollisionTriangles { get; set; } = [];
    public List<LevelEntityDto> Entities { get; set; } = [];
}

/// <summary>
/// Root document contract for the human-readable Dual-Layer <c>.level.json</c> file format.
/// Layer 1 (<see cref="Editor"/>) provides lossless N-gon re-editing.
/// Layer 2 (<see cref="Baked"/>) provides zero-triangulation direct game loading.
/// </summary>
public sealed class LeedLevelDocument
{
    public const int CurrentFormatVersion = 1;
    public const string LeftHandedCoordinateSystem = "LeftHanded_YUp_ZAway";
    public const string RightHandedCoordinateSystem = "RightHanded_YUp_ZBack";

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public string Name { get; set; } = "Untitled";
    public string CoordinateSystem { get; set; } = LeftHandedCoordinateSystem;

    public EditorLayerData Editor { get; set; } = new();
    public BakedRuntimeLayerData Baked { get; set; } = new();

    /// <summary>
    /// Searches baked entities (or editor entities if baked has none) for the first entity matching <paramref name="className"/>.
    /// </summary>
    public LevelEntityDto? FindFirstEntity(string className)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            return null;
        }

        LevelEntityDto? baked = Baked.Entities.FirstOrDefault(e => e.ClassName.Equals(className, StringComparison.OrdinalIgnoreCase));
        if (baked is not null)
        {
            return baked;
        }

        return Editor.Entities.FirstOrDefault(e => e.ClassName.Equals(className, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns all entities matching <paramref name="className"/> from the baked runtime layer
    /// (or editor layer if baked has none).
    /// </summary>
    public IEnumerable<LevelEntityDto> GetEntitiesByClass(string className)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            return [];
        }

        if (Baked.Entities.Count > 0)
        {
            return Baked.Entities.Where(e => e.ClassName.Equals(className, StringComparison.OrdinalIgnoreCase));
        }

        return Editor.Entities.Where(e => e.ClassName.Equals(className, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns the pre-triangulated world-space render submeshes grouped by material.
    /// </summary>
    public IReadOnlyList<BakedSubmeshDto> GetRenderSubmeshes() => Baked.RenderSubmeshes;

    /// <summary>
    /// Returns the pre-filtered world-space collision triangles.
    /// </summary>
    public IReadOnlyList<BakedCollisionTriangleDto> GetCollisionTriangles() => Baked.CollisionTriangles;
}
