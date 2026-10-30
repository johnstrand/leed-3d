using System.Numerics;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Scene;
using Leed3D.Runtime.Metadata;

namespace Leed3D.Kernel.Entities;

/// <summary>
/// Preset definition and schema for a gameplay entity class (built-in or loaded from schema JSON).
/// </summary>
public sealed class EntityDefinition
{
    public string ClassName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public EntityGizmoShape GizmoShape { get; set; } = EntityGizmoShape.Point;
    public ColorRgba Color { get; set; } = ColorRgba.White;
    public Vector3 Size { get; set; } = Vector3.One;
    public ElementMetadata DefaultProperties { get; set; } = new();

    public EntityDefinition()
    {
    }

    public EntityDefinition(
        string className,
        string displayName,
        string category,
        EntityGizmoShape gizmoShape,
        ColorRgba color,
        Vector3 size,
        ElementMetadata? defaultProperties = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);

        ClassName = className;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? className : displayName;
        Category = string.IsNullOrWhiteSpace(category) ? "General" : category;
        GizmoShape = gizmoShape;
        Color = color;
        Size = size;
        DefaultProperties = defaultProperties?.Clone() ?? new ElementMetadata();
    }

    public LevelEntity CreateInstance(string id, Vector3? position = null)
    {
        return new LevelEntity(
            id,
            ClassName,
            new Transform3D(position ?? Vector3.Zero),
            DefaultProperties.Clone());
    }
}
