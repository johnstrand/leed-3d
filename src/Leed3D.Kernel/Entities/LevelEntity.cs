using System.Numerics;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Scene;
using Leed3D.Runtime.Metadata;

namespace Leed3D.Kernel.Entities;

/// <summary>
/// Non-geometry gameplay entity placed in the level (e.g. player_spawn, light, portal, trigger).
/// </summary>
public sealed class LevelEntity
{
    public string Id { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public Transform3D Transform { get; set; } = new();
    public ElementMetadata Properties { get; set; } = new();
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; } = false;

    public LevelEntity()
    {
    }

    public LevelEntity(
        string id,
        string className,
        Transform3D? transform = null,
        ElementMetadata? properties = null)
    {
        Id = id ?? string.Empty;
        ClassName = className ?? string.Empty;
        Transform = transform ?? new Transform3D();
        Properties = properties ?? new ElementMetadata();
    }

    /// <summary>
    /// Deep clones this entity with an optional new unique ID.
    /// </summary>
    public LevelEntity Clone(string? newId = null)
    {
        return new LevelEntity(
            newId ?? Id,
            ClassName,
            Transform.Clone(),
            Properties.Clone())
        {
            Visible = Visible,
            Locked = Locked
        };
    }

    /// <summary>
    /// Computes the world-space axis-aligned bounding box for raycast picking and selection framing.
    /// </summary>
    public BoundingBox3D GetWorldBounds(EntitySchemaRegistry? registry = null)
    {
        Vector3 size = Vector3.One;

        if (Properties.TryGet("size", out var sizeVal) && sizeVal is not null && sizeVal.Type == MetadataValueType.Vec3)
        {
            size = sizeVal.Vec3Value;
        }
        else if (registry != null && registry.TryGetDefinition(ClassName, out EntityDefinition? def) && def != null)
        {
            size = def.Size;
        }
        else if (ClassName.Equals("player_spawn", StringComparison.OrdinalIgnoreCase))
        {
            size = new Vector3(0.8f, 1.8f, 0.8f);
        }
        else if (ClassName.Equals("light", StringComparison.OrdinalIgnoreCase))
        {
            size = new Vector3(0.6f, 0.6f, 0.6f);
        }
        else if (ClassName.Equals("portal", StringComparison.OrdinalIgnoreCase))
        {
            size = new Vector3(2.0f, 3.0f, 0.5f);
        }
        else if (ClassName.Equals("trigger", StringComparison.OrdinalIgnoreCase))
        {
            size = new Vector3(2.0f, 2.0f, 2.0f);
        }

        // Keep dimensions positive
        size = new Vector3(
            MathF.Max(0.1f, MathF.Abs(size.X)),
            MathF.Max(0.1f, MathF.Abs(size.Y)),
            MathF.Max(0.1f, MathF.Abs(size.Z)));

        var localBox = new BoundingBox3D(-size * 0.5f, size * 0.5f);
        return localBox.Transform(Transform.GetLocalMatrix());
    }

    public override string ToString() => $"[Entity '{Id}' ({ClassName}) @ {Transform.Position}]";
}
