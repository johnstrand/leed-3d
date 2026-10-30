using System.Numerics;
using System.Text.Json.Serialization;

namespace Leed3D.Runtime;

/// <summary>
/// Serializable 3D vector with X, Y, Z components and implicit conversions to/from <see cref="Vector3"/>.
/// </summary>
[JsonConverter(typeof(Vector3DtoJsonConverter))]
public struct Vector3Dto : IEquatable<Vector3Dto>
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    public static readonly Vector3Dto Zero = new(0f, 0f, 0f);
    public static readonly Vector3Dto One = new(1f, 1f, 1f);
    public static readonly Vector3Dto UnitX = new(1f, 0f, 0f);
    public static readonly Vector3Dto UnitY = new(0f, 1f, 0f);
    public static readonly Vector3Dto UnitZ = new(0f, 0f, 1f);

    public Vector3Dto()
    {
    }

    public Vector3Dto(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public Vector3Dto(Vector3 v) : this(v.X, v.Y, v.Z)
    {
    }

    public static implicit operator Vector3(Vector3Dto dto) => new(dto.X, dto.Y, dto.Z);
    public static implicit operator Vector3Dto(Vector3 v) => new(v.X, v.Y, v.Z);

    public Vector3 ToVector3() => new(X, Y, Z);

    public bool Equals(Vector3Dto other) =>
        MathF.Abs(X - other.X) < 1e-6f &&
        MathF.Abs(Y - other.Y) < 1e-6f &&
        MathF.Abs(Z - other.Z) < 1e-6f;

    public override bool Equals(object? obj) => obj is Vector3Dto other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
}

/// <summary>
/// Serializable 2D vector with X, Y components and implicit conversions to/from <see cref="Vector2"/>.
/// </summary>
[JsonConverter(typeof(Vector2DtoJsonConverter))]
public struct Vector2Dto : IEquatable<Vector2Dto>
{
    public float X { get; set; }
    public float Y { get; set; }

    public static readonly Vector2Dto Zero = new(0f, 0f);
    public static readonly Vector2Dto One = new(1f, 1f);
    public static readonly Vector2Dto UnitX = new(1f, 0f);
    public static readonly Vector2Dto UnitY = new(0f, 1f);

    public Vector2Dto()
    {
    }

    public Vector2Dto(float x, float y)
    {
        X = x;
        Y = y;
    }

    public Vector2Dto(Vector2 v) : this(v.X, v.Y)
    {
    }

    public static implicit operator Vector2(Vector2Dto dto) => new(dto.X, dto.Y);
    public static implicit operator Vector2Dto(Vector2 v) => new(v.X, v.Y);

    public Vector2 ToVector2() => new(X, Y);

    public bool Equals(Vector2Dto other) =>
        MathF.Abs(X - other.X) < 1e-6f &&
        MathF.Abs(Y - other.Y) < 1e-6f;

    public override bool Equals(object? obj) => obj is Vector2Dto other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public override string ToString() => $"({X:0.###}, {Y:0.###})";
}

/// <summary>
/// Serializable 3D axis-aligned bounding box.
/// </summary>
public sealed class BoundingBoxDto
{
    public Vector3Dto Min { get; set; } = Vector3Dto.Zero;
    public Vector3Dto Max { get; set; } = Vector3Dto.Zero;

    public Vector3 Size => (Vector3)Max - (Vector3)Min;
    public Vector3 Center => ((Vector3)Min + (Vector3)Max) * 0.5f;

    public BoundingBoxDto()
    {
    }

    public BoundingBoxDto(Vector3 min, Vector3 max)
    {
        Min = min;
        Max = max;
    }

    public BoundingBoxDto(Vector3Dto min, Vector3Dto max)
    {
        Min = min;
        Max = max;
    }
}

/// <summary>
/// Serializable 3D transform representation with Position, Euler rotation in degrees (Pitch, Yaw, Roll), and Scale.
/// </summary>
public sealed class TransformDto
{
    public Vector3Dto Position { get; set; } = Vector3Dto.Zero;
    public Vector3Dto RotationEulerDegrees { get; set; } = Vector3Dto.Zero;
    public Vector3Dto Scale { get; set; } = Vector3Dto.One;

    public TransformDto()
    {
    }

    public TransformDto(Vector3 position, Vector3 rotationEulerDegrees, Vector3 scale)
    {
        Position = position;
        RotationEulerDegrees = rotationEulerDegrees;
        Scale = scale;
    }

    public TransformDto(Vector3Dto position, Vector3Dto rotationEulerDegrees, Vector3Dto scale)
    {
        Position = position;
        RotationEulerDegrees = rotationEulerDegrees;
        Scale = scale;
    }

    public TransformDto Clone() => new(Position, RotationEulerDegrees, Scale);
}

/// <summary>
/// Valve-220 / TrenchBroom planar UV projection parameters for a polygon face.
/// </summary>
public sealed class FaceUvMappingDto
{
    public Vector3Dto UAxis { get; set; } = Vector3Dto.UnitX;
    public Vector3Dto VAxis { get; set; } = Vector3Dto.UnitZ;
    public Vector2Dto Offset { get; set; } = Vector2Dto.Zero;
    public Vector2Dto Scale { get; set; } = Vector2Dto.One;
    public float RotationDegrees { get; set; } = 0.0f;
    public bool TextureLock { get; set; } = false;

    public FaceUvMappingDto()
    {
    }

    public FaceUvMappingDto(
        Vector3Dto uAxis,
        Vector3Dto vAxis,
        Vector2Dto offset = default,
        Vector2Dto scale = default,
        float rotationDegrees = 0.0f,
        bool textureLock = false)
    {
        UAxis = uAxis;
        VAxis = vAxis;
        Offset = offset;
        Scale = scale.Equals(default) ? Vector2Dto.One : scale;
        RotationDegrees = rotationDegrees;
        TextureLock = textureLock;
    }

    public FaceUvMappingDto Clone() => new()
    {
        UAxis = UAxis,
        VAxis = VAxis,
        Offset = Offset,
        Scale = Scale,
        RotationDegrees = RotationDegrees,
        TextureLock = TextureLock
    };
}
