using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Leed3D.Runtime;

/// <summary>
/// JSON converter for <see cref="Vector3Dto"/> supporting both object notation <c>{"x": 1, "y": 2, "z": 3}</c>
/// and array notation <c>[1, 2, 3]</c>.
/// </summary>
public sealed class Vector3DtoJsonConverter : JsonConverter<Vector3Dto>
{
    public override Vector3Dto Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            reader.Read();
            float x = (float)reader.GetDouble();
            reader.Read();
            float y = (float)reader.GetDouble();
            reader.Read();
            float z = (float)reader.GetDouble();
            reader.Read(); // EndArray
            return new Vector3Dto(x, y, z);
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            float x = 0, y = 0, z = 0;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    string? propName = reader.GetString();
                    reader.Read();
                    if (string.Equals(propName, "x", StringComparison.OrdinalIgnoreCase))
                    {
                        x = (float)reader.GetDouble();
                    }
                    else if (string.Equals(propName, "y", StringComparison.OrdinalIgnoreCase))
                    {
                        y = (float)reader.GetDouble();
                    }
                    else if (string.Equals(propName, "z", StringComparison.OrdinalIgnoreCase))
                    {
                        z = (float)reader.GetDouble();
                    }
                    else
                    {
                        reader.Skip();
                    }
                }
            }
            return new Vector3Dto(x, y, z);
        }

        throw new JsonException($"Unexpected token type {reader.TokenType} when parsing Vector3Dto.");
    }

    public override void Write(Utf8JsonWriter writer, Vector3Dto value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", value.X);
        writer.WriteNumber("y", value.Y);
        writer.WriteNumber("z", value.Z);
        writer.WriteEndObject();
    }
}

/// <summary>
/// JSON converter for <see cref="Vector2Dto"/> supporting both object notation <c>{"x": 1, "y": 2}</c>
/// and array notation <c>[1, 2]</c>.
/// </summary>
public sealed class Vector2DtoJsonConverter : JsonConverter<Vector2Dto>
{
    public override Vector2Dto Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            reader.Read();
            float x = (float)reader.GetDouble();
            reader.Read();
            float y = (float)reader.GetDouble();
            reader.Read(); // EndArray
            return new Vector2Dto(x, y);
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            float x = 0, y = 0;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    string? propName = reader.GetString();
                    reader.Read();
                    if (string.Equals(propName, "x", StringComparison.OrdinalIgnoreCase))
                    {
                        x = (float)reader.GetDouble();
                    }
                    else if (string.Equals(propName, "y", StringComparison.OrdinalIgnoreCase))
                    {
                        y = (float)reader.GetDouble();
                    }
                    else
                    {
                        reader.Skip();
                    }
                }
            }
            return new Vector2Dto(x, y);
        }

        throw new JsonException($"Unexpected token type {reader.TokenType} when parsing Vector2Dto.");
    }

    public override void Write(Utf8JsonWriter writer, Vector2Dto value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", value.X);
        writer.WriteNumber("y", value.Y);
        writer.WriteEndObject();
    }
}
