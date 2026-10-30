using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;

namespace Leed3D.Runtime.Metadata;

/// <summary>
/// Supported data types for granular metadata properties.
/// </summary>
public enum MetadataValueType
{
    String,
    Float,
    Int,
    Bool,
    Color,
    Vec3
}

/// <summary>
/// Simple RGBA color representation for metadata values (0-255 channels).
/// </summary>
public readonly record struct ColorRgba(byte R, byte G, byte B, byte A = 255)
{
    public static readonly ColorRgba White = new(255, 255, 255, 255);
    public static readonly ColorRgba Black = new(0, 0, 0, 255);
    public static readonly ColorRgba Red = new(255, 0, 0, 255);
    public static readonly ColorRgba Green = new(0, 255, 0, 255);
    public static readonly ColorRgba Blue = new(0, 0, 255, 255);

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}{A:X2}";

    public static ColorRgba FromHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return White;
        string s = hex.Trim().TrimStart('#');
        if (s.Length == 6 && uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
        {
            return new ColorRgba((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF), 255);
        }
        if (s.Length == 8 && uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgba))
        {
            return new ColorRgba((byte)((rgba >> 24) & 0xFF), (byte)((rgba >> 16) & 0xFF), (byte)((rgba >> 8) & 0xFF), (byte)(rgba & 0xFF));
        }
        return White;
    }
}

/// <summary>
/// Immutable typed metadata property value supporting string, float, int, bool, ColorRgba, and Vector3.
/// </summary>
[JsonConverter(typeof(MetadataValueJsonConverter))]
public sealed class MetadataValue : IEquatable<MetadataValue>
{
    public MetadataValueType Type { get; }
    public string StringValue { get; }
    public float FloatValue { get; }
    public int IntValue { get; }
    public bool BoolValue { get; }
    public ColorRgba ColorValue { get; }
    public Vector3 Vec3Value { get; }

    public static MetadataValue FromString(string value) => new(MetadataValueType.String, stringVal: value ?? string.Empty);
    public static MetadataValue FromFloat(float value) => new(MetadataValueType.Float, floatVal: value, stringVal: value.ToString(CultureInfo.InvariantCulture));
    public static MetadataValue FromInt(int value) => new(MetadataValueType.Int, intVal: value, stringVal: value.ToString(CultureInfo.InvariantCulture));
    public static MetadataValue FromBool(bool value) => new(MetadataValueType.Bool, boolVal: value, stringVal: value ? "true" : "false");
    public static MetadataValue FromColor(ColorRgba value) => new(MetadataValueType.Color, colorVal: value, stringVal: value.ToString());
    public static MetadataValue FromVec3(Vector3 value) => new(MetadataValueType.Vec3, vec3Val: value, stringVal: $"({value.X:0.##}, {value.Y:0.##}, {value.Z:0.##})");

    private MetadataValue(
        MetadataValueType type,
        string stringVal = "",
        float floatVal = 0.0f,
        int intVal = 0,
        bool boolVal = false,
        ColorRgba colorVal = default,
        Vector3 vec3Val = default)
    {
        Type = type;
        StringValue = stringVal;
        FloatValue = floatVal;
        IntValue = intVal;
        BoolValue = boolVal;
        ColorValue = colorVal;
        Vec3Value = vec3Val;
    }

    public object AsObject() => Type switch
    {
        MetadataValueType.String => StringValue,
        MetadataValueType.Float => FloatValue,
        MetadataValueType.Int => IntValue,
        MetadataValueType.Bool => BoolValue,
        MetadataValueType.Color => ColorValue,
        MetadataValueType.Vec3 => Vec3Value,
        _ => StringValue
    };

    public override string ToString() => StringValue;

    public bool Equals(MetadataValue? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Type != other.Type) return false;

        return Type switch
        {
            MetadataValueType.String => StringValue == other.StringValue,
            MetadataValueType.Float => Math.Abs(FloatValue - other.FloatValue) < 1e-6f,
            MetadataValueType.Int => IntValue == other.IntValue,
            MetadataValueType.Bool => BoolValue == other.BoolValue,
            MetadataValueType.Color => ColorValue.Equals(other.ColorValue),
            MetadataValueType.Vec3 => Vec3Value.Equals(other.Vec3Value),
            _ => false
        };
    }

    public override bool Equals(object? obj) => obj is MetadataValue other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Type, StringValue);
}

/// <summary>
/// Granular typed key-value metadata container and tag set attachable to objects, faces, edges, and vertices.
/// </summary>
[JsonConverter(typeof(ElementMetadataJsonConverter))]
public sealed class ElementMetadata
{
    private readonly Dictionary<string, MetadataValue> _properties = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _tags = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, MetadataValue> Properties => _properties;
    public IReadOnlySet<string> Tags => _tags;

    public bool IsEmpty => _properties.Count == 0 && _tags.Count == 0;
    public int Count => _properties.Count + _tags.Count;

    public ElementMetadata()
    {
    }

    public ElementMetadata(
        IEnumerable<KeyValuePair<string, MetadataValue>>? properties = null,
        IEnumerable<string>? tags = null)
    {
        if (properties != null)
        {
            foreach (var kvp in properties)
            {
                _properties[kvp.Key] = kvp.Value;
            }
        }

        if (tags != null)
        {
            foreach (string t in tags)
            {
                if (!string.IsNullOrWhiteSpace(t))
                {
                    _tags.Add(t.Trim());
                }
            }
        }
    }

    public void Set(string key, MetadataValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        _properties[key.Trim()] = value;
    }

    public void SetProperty(string key, MetadataValue value) => Set(key, value);
    public void SetProperty(string key, string value) => SetString(key, value);
    public void SetProperty(string key, float value) => SetFloat(key, value);
    public void SetProperty(string key, int value) => SetInt(key, value);
    public void SetProperty(string key, bool value) => SetBool(key, value);
    public void SetProperty(string key, ColorRgba value) => SetColor(key, value);
    public void SetProperty(string key, Vector3 value) => SetVec3(key, value);
    public bool RemoveProperty(string key) => Remove(key);

    public void SetString(string key, string value) => Set(key, MetadataValue.FromString(value));
    public void SetFloat(string key, float value) => Set(key, MetadataValue.FromFloat(value));
    public void SetInt(string key, int value) => Set(key, MetadataValue.FromInt(value));
    public void SetBool(string key, bool value) => Set(key, MetadataValue.FromBool(value));
    public void SetColor(string key, ColorRgba value) => Set(key, MetadataValue.FromColor(value));
    public void SetVec3(string key, Vector3 value) => Set(key, MetadataValue.FromVec3(value));

    public MetadataValue? Get(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        return _properties.GetValueOrDefault(key.Trim());
    }

    public string? GetString(string key, string? defaultValue = null)
    {
        return TryGet(key, out var v) && v is not null ? v.StringValue : defaultValue;
    }

    public float? GetFloat(string key, float? defaultValue = null)
    {
        return TryGet(key, out var v) && v is not null ? v.FloatValue : defaultValue;
    }

    public int? GetInt(string key, int? defaultValue = null)
    {
        return TryGet(key, out var v) && v is not null ? v.IntValue : defaultValue;
    }

    public bool GetBool(string key, bool defaultValue = false)
    {
        return TryGet(key, out var v) && v is not null ? v.BoolValue : defaultValue;
    }

    public ColorRgba? GetColor(string key, ColorRgba? defaultValue = null)
    {
        return TryGet(key, out var v) && v is not null ? v.ColorValue : defaultValue;
    }

    public Vector3? GetVec3(string key, Vector3? defaultValue = null)
    {
        return TryGet(key, out var v) && v is not null ? v.Vec3Value : defaultValue;
    }

    public bool TryGet(string key, out MetadataValue? value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            value = null;
            return false;
        }

        return _properties.TryGetValue(key.Trim(), out value);
    }

    public bool Remove(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        return _properties.Remove(key.Trim());
    }

    public void ClearProperties() => _properties.Clear();

    public bool AddTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return false;
        return _tags.Add(tag.Trim());
    }

    public bool RemoveTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return false;
        return _tags.Remove(tag.Trim());
    }

    public bool HasTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return false;
        return _tags.Contains(tag.Trim());
    }

    public void ClearTags() => _tags.Clear();

    public void Clear()
    {
        _properties.Clear();
        _tags.Clear();
    }

    public void MergeFrom(ElementMetadata other)
    {
        ArgumentNullException.ThrowIfNull(other);
        foreach (var kvp in other._properties)
        {
            _properties[kvp.Key] = kvp.Value;
        }

        foreach (string t in other._tags)
        {
            _tags.Add(t);
        }
    }

    public ElementMetadata Clone()
    {
        return new ElementMetadata(_properties, _tags);
    }

    /// <summary>
    /// Checks whether collision should be disabled for this element
    /// (e.g. `collision=none`, `collision=false`, or tag `no_collision`).
    /// </summary>
    public bool IsCollisionDisabled()
    {
        if (HasTag("no_collision"))
        {
            return true;
        }

        if (TryGet("collision", out MetadataValue? val) && val is not null)
        {
            if (val.Type == MetadataValueType.String && string.Equals(val.StringValue, "none", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (val.Type == MetadataValueType.Bool && !val.BoolValue)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the surface type metadata (`surface` property), or <paramref name="defaultSurface"/> if not set.
    /// </summary>
    public string GetSurfaceType(string defaultSurface = "default")
    {
        return GetString("surface", defaultSurface) ?? defaultSurface;
    }
}
