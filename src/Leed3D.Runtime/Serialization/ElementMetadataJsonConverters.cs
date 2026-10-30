using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Leed3D.Runtime.Metadata;

namespace Leed3D.Runtime;

/// <summary>
/// JSON converter for <see cref="MetadataValue"/> supporting both typed object representation
/// and raw primitive representation for maximum flexibility.
/// </summary>
public sealed class MetadataValueJsonConverter : JsonConverter<MetadataValue>
{
    public override MetadataValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return MetadataValue.FromString(reader.GetString() ?? string.Empty);
        }

        if (reader.TokenType == JsonTokenType.True || reader.TokenType == JsonTokenType.False)
        {
            return MetadataValue.FromBool(reader.GetBoolean());
        }

        if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetInt32(out int intVal))
            {
                return MetadataValue.FromInt(intVal);
            }
            return MetadataValue.FromFloat((float)reader.GetDouble());
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            reader.Read();
            float x = (float)reader.GetDouble();
            reader.Read();
            float y = (float)reader.GetDouble();
            reader.Read();
            float z = (float)reader.GetDouble();
            reader.Read(); // EndArray
            return MetadataValue.FromVec3(new Vector3(x, y, z));
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            string typeStr = "string";
            string stringVal = "";
            float floatVal = 0;
            int intVal = 0;
            bool boolVal = false;
            ColorRgba colorVal = ColorRgba.White;
            Vector3 vec3Val = Vector3.Zero;
            bool hasVecComponents = false;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    string prop = reader.GetString() ?? "";
                    reader.Read();

                    if (string.Equals(prop, "type", StringComparison.OrdinalIgnoreCase))
                    {
                        typeStr = reader.GetString() ?? "string";
                    }
                    else if (string.Equals(prop, "value", StringComparison.OrdinalIgnoreCase))
                    {
                        if (reader.TokenType == JsonTokenType.String)
                        {
                            stringVal = reader.GetString() ?? "";
                        }
                        else if (reader.TokenType == JsonTokenType.Number)
                        {
                            if (reader.TryGetInt32(out int iv))
                            {
                                intVal = iv;
                                floatVal = iv;
                            }
                            else
                            {
                                floatVal = (float)reader.GetDouble();
                                intVal = (int)floatVal;
                            }
                        }
                        else if (reader.TokenType == JsonTokenType.True || reader.TokenType == JsonTokenType.False)
                        {
                            boolVal = reader.GetBoolean();
                        }
                        else if (reader.TokenType == JsonTokenType.StartArray)
                        {
                            reader.Read();
                            float vx = (float)reader.GetDouble();
                            reader.Read();
                            float vy = (float)reader.GetDouble();
                            reader.Read();
                            float vz = (float)reader.GetDouble();
                            reader.Read(); // EndArray
                            vec3Val = new Vector3(vx, vy, vz);
                            hasVecComponents = true;
                        }
                        else if (reader.TokenType == JsonTokenType.StartObject)
                        {
                            float vx = 0, vy = 0, vz = 0;
                            while (reader.Read())
                            {
                                if (reader.TokenType == JsonTokenType.EndObject) break;
                                if (reader.TokenType == JsonTokenType.PropertyName)
                                {
                                    string p = reader.GetString() ?? "";
                                    reader.Read();
                                    if (string.Equals(p, "x", StringComparison.OrdinalIgnoreCase)) vx = (float)reader.GetDouble();
                                    else if (string.Equals(p, "y", StringComparison.OrdinalIgnoreCase)) vy = (float)reader.GetDouble();
                                    else if (string.Equals(p, "z", StringComparison.OrdinalIgnoreCase)) vz = (float)reader.GetDouble();
                                    else reader.Skip();
                                }
                            }
                            vec3Val = new Vector3(vx, vy, vz);
                            hasVecComponents = true;
                        }
                    }
                    else if (string.Equals(prop, "x", StringComparison.OrdinalIgnoreCase))
                    {
                        vec3Val.X = (float)reader.GetDouble();
                        hasVecComponents = true;
                    }
                    else if (string.Equals(prop, "y", StringComparison.OrdinalIgnoreCase))
                    {
                        vec3Val.Y = (float)reader.GetDouble();
                        hasVecComponents = true;
                    }
                    else if (string.Equals(prop, "z", StringComparison.OrdinalIgnoreCase))
                    {
                        vec3Val.Z = (float)reader.GetDouble();
                        hasVecComponents = true;
                    }
                    else
                    {
                        reader.Skip();
                    }
                }
            }

            if (Enum.TryParse<MetadataValueType>(typeStr, true, out var mType))
            {
                return mType switch
                {
                    MetadataValueType.String => MetadataValue.FromString(stringVal),
                    MetadataValueType.Float => MetadataValue.FromFloat(floatVal),
                    MetadataValueType.Int => MetadataValue.FromInt(intVal),
                    MetadataValueType.Bool => MetadataValue.FromBool(boolVal),
                    MetadataValueType.Color => MetadataValue.FromColor(ColorRgba.FromHex(stringVal)),
                    MetadataValueType.Vec3 => MetadataValue.FromVec3(vec3Val),
                    _ => MetadataValue.FromString(stringVal)
                };
            }

            if (hasVecComponents)
            {
                return MetadataValue.FromVec3(vec3Val);
            }

            return MetadataValue.FromString(stringVal);
        }

        throw new JsonException($"Unexpected token {reader.TokenType} for MetadataValue.");
    }

    public override void Write(Utf8JsonWriter writer, MetadataValue value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("type", value.Type.ToString());
        switch (value.Type)
        {
            case MetadataValueType.String:
                writer.WriteString("value", value.StringValue);
                break;
            case MetadataValueType.Float:
                writer.WriteNumber("value", value.FloatValue);
                break;
            case MetadataValueType.Int:
                writer.WriteNumber("value", value.IntValue);
                break;
            case MetadataValueType.Bool:
                writer.WriteBoolean("value", value.BoolValue);
                break;
            case MetadataValueType.Color:
                writer.WriteString("value", value.ColorValue.ToString());
                break;
            case MetadataValueType.Vec3:
                writer.WriteStartArray("value");
                writer.WriteNumberValue(value.Vec3Value.X);
                writer.WriteNumberValue(value.Vec3Value.Y);
                writer.WriteNumberValue(value.Vec3Value.Z);
                writer.WriteEndArray();
                break;
        }
        writer.WriteEndObject();
    }
}

/// <summary>
/// JSON converter for <see cref="ElementMetadata"/>.
/// Serializes properties as key-value pairs and tags as an array of strings.
/// </summary>
public sealed class ElementMetadataJsonConverter : JsonConverter<ElementMetadata>
{
    public override ElementMetadata Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected StartObject for ElementMetadata, got {reader.TokenType}");
        }

        var result = new ElementMetadata();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                string propName = reader.GetString() ?? "";
                reader.Read();

                if (string.Equals(propName, "properties", StringComparison.OrdinalIgnoreCase))
                {
                    if (reader.TokenType == JsonTokenType.StartObject)
                    {
                        var propConverter = (JsonConverter<MetadataValue>)options.GetConverter(typeof(MetadataValue));
                        while (reader.Read())
                        {
                            if (reader.TokenType == JsonTokenType.EndObject) break;
                            if (reader.TokenType == JsonTokenType.PropertyName)
                            {
                                string key = reader.GetString() ?? "";
                                reader.Read();
                                var val = propConverter.Read(ref reader, typeof(MetadataValue), options);
                                if (val != null && !string.IsNullOrWhiteSpace(key))
                                {
                                    result.Set(key, val);
                                }
                            }
                        }
                    }
                    else
                    {
                        reader.Skip();
                    }
                }
                else if (string.Equals(propName, "tags", StringComparison.OrdinalIgnoreCase))
                {
                    if (reader.TokenType == JsonTokenType.StartArray)
                    {
                        while (reader.Read())
                        {
                            if (reader.TokenType == JsonTokenType.EndArray) break;
                            if (reader.TokenType == JsonTokenType.String)
                            {
                                string? tag = reader.GetString();
                                if (!string.IsNullOrWhiteSpace(tag))
                                {
                                    result.AddTag(tag);
                                }
                            }
                        }
                    }
                    else
                    {
                        reader.Skip();
                    }
                }
                else
                {
                    reader.Skip();
                }
            }
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, ElementMetadata value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WriteStartObject("properties");
        var valConverter = (JsonConverter<MetadataValue>)options.GetConverter(typeof(MetadataValue));
        foreach (var kvp in value.Properties)
        {
            writer.WritePropertyName(kvp.Key);
            valConverter.Write(writer, kvp.Value, options);
        }
        writer.WriteEndObject();

        writer.WriteStartArray("tags");
        foreach (string tag in value.Tags)
        {
            writer.WriteStringValue(tag);
        }
        writer.WriteEndArray();

        writer.WriteEndObject();
    }
}
