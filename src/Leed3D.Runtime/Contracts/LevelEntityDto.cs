using Leed3D.Runtime.Metadata;

namespace Leed3D.Runtime;

/// <summary>
/// Serializable DTO for non-geometry gameplay entities (player spawns, lights, portals, triggers, etc.).
/// </summary>
public sealed class LevelEntityDto
{
    public string Id { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public TransformDto Transform { get; set; } = new();
    public ElementMetadata Properties { get; set; } = new();
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; } = false;

    public LevelEntityDto()
    {
    }

    public LevelEntityDto(
        string id,
        string className,
        TransformDto? transform = null,
        ElementMetadata? properties = null,
        bool visible = true,
        bool locked = false)
    {
        Id = id ?? string.Empty;
        ClassName = className ?? string.Empty;
        Transform = transform ?? new TransformDto();
        Properties = properties ?? new ElementMetadata();
        Visible = visible;
        Locked = locked;
    }

    public LevelEntityDto Clone() => new(
        Id,
        ClassName,
        Transform.Clone(),
        Properties.Clone(),
        Visible,
        Locked);
}
