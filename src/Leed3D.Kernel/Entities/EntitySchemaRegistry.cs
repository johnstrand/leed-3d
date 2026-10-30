using System.Numerics;
using System.Text.Json;
using Leed3D.Runtime.Metadata;

namespace Leed3D.Kernel.Entities;

/// <summary>
/// Registry of entity schemas and definitions, including built-in core types (player_spawn, light, portal, trigger)
/// and custom game entity definitions loaded from JSON (e.g. entities.schema.json).
/// </summary>
public sealed class EntitySchemaRegistry
{
    private readonly Dictionary<string, EntityDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);

    public static EntitySchemaRegistry Default { get; } = new();

    public IReadOnlyCollection<EntityDefinition> Definitions => _definitions.Values;
    public int Count => _definitions.Count;

    public EntitySchemaRegistry(bool registerBuiltIns = true)
    {
        if (registerBuiltIns)
        {
            RegisterCoreBuiltIns();
        }
    }

    /// <summary>
    /// Registers a new entity definition or replaces an existing definition with the same class name.
    /// </summary>
    public void Register(EntityDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.ClassName);

        _definitions[definition.ClassName.Trim()] = definition;
    }

    public bool Unregister(string className)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            return false;
        }

        return _definitions.Remove(className.Trim());
    }

    public EntityDefinition? GetDefinition(string className)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            return null;
        }

        return _definitions.GetValueOrDefault(className.Trim());
    }

    public bool TryGetDefinition(string className, out EntityDefinition? definition)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            definition = null;
            return false;
        }

        return _definitions.TryGetValue(className.Trim(), out definition);
    }

    /// <summary>
    /// Registers the standard built-in core entity definitions (player_spawn, light, portal, trigger).
    /// </summary>
    public void RegisterCoreBuiltIns()
    {
        // 1. player_spawn
        var spawnProps = new ElementMetadata();
        spawnProps.SetFloat("yaw", 0.0f);
        spawnProps.SetInt("team", 0);
        Register(new EntityDefinition(
            className: "player_spawn",
            displayName: "Player Spawn",
            category: "Gameplay",
            gizmoShape: EntityGizmoShape.CapsuleWithArrow,
            color: new ColorRgba(60, 220, 80, 255), // Green
            size: new Vector3(0.8f, 1.8f, 0.8f),
            defaultProperties: spawnProps));

        // 2. light
        var lightProps = new ElementMetadata();
        lightProps.SetString("light_type", "point");
        lightProps.SetColor("color", new ColorRgba(255, 240, 210, 255));
        lightProps.SetFloat("intensity", 1.5f);
        lightProps.SetFloat("radius", 12.0f);
        lightProps.SetFloat("spot_angle", 45.0f);
        Register(new EntityDefinition(
            className: "light",
            displayName: "Light",
            category: "Lighting",
            gizmoShape: EntityGizmoShape.LightGizmo,
            color: new ColorRgba(255, 240, 210, 255), // Warm yellow/white
            size: new Vector3(0.6f, 0.6f, 0.6f),
            defaultProperties: lightProps));

        // 3. portal
        var portalProps = new ElementMetadata();
        portalProps.SetString("target_id", string.Empty);
        portalProps.SetString("event_name", "teleport");
        portalProps.SetBool("one_shot", false);
        Register(new EntityDefinition(
            className: "portal",
            displayName: "Portal",
            category: "Triggers",
            gizmoShape: EntityGizmoShape.BoxVolume,
            color: new ColorRgba(0, 220, 240, 255), // Cyan
            size: new Vector3(2.0f, 3.0f, 0.5f),
            defaultProperties: portalProps));

        // 4. trigger
        var triggerProps = new ElementMetadata();
        triggerProps.SetString("target_id", string.Empty);
        triggerProps.SetString("event_name", "trigger_enter");
        triggerProps.SetBool("one_shot", false);
        Register(new EntityDefinition(
            className: "trigger",
            displayName: "Trigger Volume",
            category: "Triggers",
            gizmoShape: EntityGizmoShape.BoxVolume,
            color: new ColorRgba(240, 200, 40, 255), // Amber / Yellow
            size: new Vector3(2.0f, 2.0f, 2.0f),
            defaultProperties: triggerProps));
    }

    /// <summary>
    /// Loads custom entity schemas from a JSON string.
    /// Supports array of schemas or object with an "entities" array.
    /// </summary>
    public int LoadFromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        int loadedCount = 0;

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement element in root.EnumerateArray())
            {
                if (ParseDefinition(element) is { } def)
                {
                    Register(def);
                    loadedCount++;
                }
            }
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("entities", out JsonElement entitiesElem) && entitiesElem.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement element in entitiesElem.EnumerateArray())
                {
                    if (ParseDefinition(element) is { } def)
                    {
                        Register(def);
                        loadedCount++;
                    }
                }
            }
            else if (root.TryGetProperty("definitions", out JsonElement defsElem) && defsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement element in defsElem.EnumerateArray())
                {
                    if (ParseDefinition(element) is { } def)
                    {
                        Register(def);
                        loadedCount++;
                    }
                }
            }
            else if (ParseDefinition(root) is { } def)
            {
                Register(def);
                loadedCount++;
            }
        }

        return loadedCount;
    }

    /// <summary>
    /// Loads custom entity schemas from a file path if it exists.
    /// </summary>
    public int LoadFromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return 0;
        }

        string json = File.ReadAllText(filePath);
        return LoadFromJson(json);
    }

    private static EntityDefinition? ParseDefinition(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? className = null;
        if (element.TryGetProperty("className", out JsonElement classElem))
        {
            className = classElem.GetString();
        }
        else if (element.TryGetProperty("class", out JsonElement cElem))
        {
            className = cElem.GetString();
        }
        else if (element.TryGetProperty("name", out JsonElement nElem))
        {
            className = nElem.GetString();
        }

        if (string.IsNullOrWhiteSpace(className))
        {
            return null;
        }

        string displayName = className;
        if (element.TryGetProperty("displayName", out JsonElement dnElem) && dnElem.GetString() is { } dn)
        {
            displayName = dn;
        }

        string category = "Custom";
        if (element.TryGetProperty("category", out JsonElement catElem) && catElem.GetString() is { } cat)
        {
            category = cat;
        }

        EntityGizmoShape shape = EntityGizmoShape.Point;
        if (element.TryGetProperty("gizmoShape", out JsonElement shapeElem) && shapeElem.GetString() is { } shapeStr)
        {
            if (Enum.TryParse<EntityGizmoShape>(shapeStr, ignoreCase: true, out var parsedShape))
            {
                shape = parsedShape;
            }
        }
        else if (element.TryGetProperty("shape", out JsonElement sElem) && sElem.GetString() is { } sStr)
        {
            if (Enum.TryParse<EntityGizmoShape>(sStr, ignoreCase: true, out var parsedShape))
            {
                shape = parsedShape;
            }
        }

        ColorRgba color = ColorRgba.White;
        if (element.TryGetProperty("color", out JsonElement colElem))
        {
            color = ParseColor(colElem);
        }

        Vector3 size = Vector3.One;
        if (element.TryGetProperty("size", out JsonElement sizeElem))
        {
            size = ParseVector3(sizeElem);
        }

        var props = new ElementMetadata();
        if (element.TryGetProperty("properties", out JsonElement propsElem) && propsElem.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty prop in propsElem.EnumerateObject())
            {
                if (ParseMetadataValue(prop.Value) is { } metaVal)
                {
                    props.Set(prop.Name, metaVal);
                }
            }
        }

        return new EntityDefinition(className, displayName, category, shape, color, size, props);
    }

    private static ColorRgba ParseColor(JsonElement elem)
    {
        if (elem.ValueKind == JsonValueKind.String)
        {
            return ColorRgba.FromHex(elem.GetString() ?? "#FFFFFF");
        }

        if (elem.ValueKind == JsonValueKind.Array)
        {
            var list = elem.EnumerateArray().ToList();
            if (list.Count >= 3)
            {
                byte r = (byte)list[0].GetInt32();
                byte g = (byte)list[1].GetInt32();
                byte b = (byte)list[2].GetInt32();
                byte a = list.Count >= 4 ? (byte)list[3].GetInt32() : (byte)255;
                return new ColorRgba(r, g, b, a);
            }
        }
        else if (elem.ValueKind == JsonValueKind.Object)
        {
            byte r = elem.TryGetProperty("r", out var rVal) ? (byte)rVal.GetInt32() : (byte)255;
            byte g = elem.TryGetProperty("g", out var gVal) ? (byte)gVal.GetInt32() : (byte)255;
            byte b = elem.TryGetProperty("b", out var bVal) ? (byte)bVal.GetInt32() : (byte)255;
            byte a = elem.TryGetProperty("a", out var aVal) ? (byte)aVal.GetInt32() : (byte)255;
            return new ColorRgba(r, g, b, a);
        }

        return ColorRgba.White;
    }

    private static Vector3 ParseVector3(JsonElement elem)
    {
        if (elem.ValueKind == JsonValueKind.Array)
        {
            var list = elem.EnumerateArray().ToList();
            if (list.Count >= 3)
            {
                return new Vector3(list[0].GetSingle(), list[1].GetSingle(), list[2].GetSingle());
            }
            if (list.Count == 1)
            {
                return new Vector3(list[0].GetSingle());
            }
        }
        else if (elem.ValueKind == JsonValueKind.Number)
        {
            return new Vector3(elem.GetSingle());
        }
        else if (elem.ValueKind == JsonValueKind.Object)
        {
            float x = elem.TryGetProperty("x", out var xVal) ? xVal.GetSingle() : 1.0f;
            float y = elem.TryGetProperty("y", out var yVal) ? yVal.GetSingle() : 1.0f;
            float z = elem.TryGetProperty("z", out var zVal) ? zVal.GetSingle() : 1.0f;
            return new Vector3(x, y, z);
        }

        return Vector3.One;
    }

    private static MetadataValue? ParseMetadataValue(JsonElement val)
    {
        switch (val.ValueKind)
        {
            case JsonValueKind.String:
            {
                string s = val.GetString() ?? string.Empty;
                if (s.StartsWith('#') && (s.Length == 7 || s.Length == 9))
                {
                    return MetadataValue.FromColor(ColorRgba.FromHex(s));
                }
                return MetadataValue.FromString(s);
            }

            case JsonValueKind.True:
            case JsonValueKind.False:
                return MetadataValue.FromBool(val.GetBoolean());

            case JsonValueKind.Number:
            {
                if (val.TryGetInt32(out int iVal))
                {
                    return MetadataValue.FromInt(iVal);
                }
                return MetadataValue.FromFloat(val.GetSingle());
            }

            case JsonValueKind.Array:
            {
                var list = val.EnumerateArray().ToList();
                if (list.Count == 3 && list[0].ValueKind == JsonValueKind.Number)
                {
                    return MetadataValue.FromVec3(new Vector3(list[0].GetSingle(), list[1].GetSingle(), list[2].GetSingle()));
                }
                break;
            }

            case JsonValueKind.Object:
            {
                if (val.TryGetProperty("type", out var typeElem) && val.TryGetProperty("value", out var vElem))
                {
                    string tStr = typeElem.GetString() ?? "";
                    if (tStr.Equals("color", StringComparison.OrdinalIgnoreCase))
                    {
                        return MetadataValue.FromColor(ParseColor(vElem));
                    }
                    if (tStr.Equals("vec3", StringComparison.OrdinalIgnoreCase))
                    {
                        return MetadataValue.FromVec3(ParseVector3(vElem));
                    }
                    if (tStr.Equals("float", StringComparison.OrdinalIgnoreCase))
                    {
                        return MetadataValue.FromFloat(vElem.GetSingle());
                    }
                    if (tStr.Equals("int", StringComparison.OrdinalIgnoreCase))
                    {
                        return MetadataValue.FromInt(vElem.GetInt32());
                    }
                    if (tStr.Equals("bool", StringComparison.OrdinalIgnoreCase))
                    {
                        return MetadataValue.FromBool(vElem.GetBoolean());
                    }
                    return MetadataValue.FromString(vElem.GetString() ?? "");
                }
                break;
            }
        }

        return null;
    }
}
