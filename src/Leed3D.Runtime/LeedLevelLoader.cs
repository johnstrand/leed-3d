using System.Text.Json;
using System.Text.Json.Serialization;

namespace Leed3D.Runtime;

/// <summary>
/// Zero-dependency drop-in loader and serializer for Dual-Layer <c>.level.json</c> level documents.
/// Can be referenced directly or copied into any C# game engine.
/// </summary>
public static class LeedLevelLoader
{
    private static readonly JsonSerializerOptions DefaultOptions = CreateDefaultOptions();

    /// <summary>
    /// Creates configured <see cref="JsonSerializerOptions"/> for reading/writing <see cref="LeedLevelDocument"/>.
    /// </summary>
    public static JsonSerializerOptions CreateDefaultOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new Vector3DtoJsonConverter());
        options.Converters.Add(new Vector2DtoJsonConverter());
        options.Converters.Add(new ElementMetadataJsonConverter());
        options.Converters.Add(new MetadataValueJsonConverter());

        return options;
    }

    /// <summary>
    /// Loads and parses a <see cref="LeedLevelDocument"/> from a <c>.level.json</c> file path.
    /// </summary>
    public static LeedLevelDocument Load(string filePath, JsonSerializerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Level file not found: {filePath}", filePath);
        }

        using var stream = File.OpenRead(filePath);
        return Load(stream, options);
    }

    /// <summary>
    /// Loads and parses a <see cref="LeedLevelDocument"/> from an open stream.
    /// </summary>
    public static LeedLevelDocument Load(Stream stream, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var doc = JsonSerializer.Deserialize<LeedLevelDocument>(stream, options ?? DefaultOptions);
        return doc ?? throw new InvalidDataException("Failed to deserialize LeedLevelDocument: JSON root was null.");
    }

    /// <summary>
    /// Parses a <see cref="LeedLevelDocument"/> directly from a JSON string.
    /// </summary>
    public static LeedLevelDocument LoadFromJson(string json, JsonSerializerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var doc = JsonSerializer.Deserialize<LeedLevelDocument>(json, options ?? DefaultOptions);
        return doc ?? throw new InvalidDataException("Failed to deserialize LeedLevelDocument: JSON root was null.");
    }

    /// <summary>
    /// Asynchronously loads a <see cref="LeedLevelDocument"/> from a stream.
    /// </summary>
    public static async Task<LeedLevelDocument> LoadAsync(Stream stream, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var doc = await JsonSerializer.DeserializeAsync<LeedLevelDocument>(stream, options ?? DefaultOptions, cancellationToken);
        return doc ?? throw new InvalidDataException("Failed to deserialize LeedLevelDocument: JSON root was null.");
    }

    /// <summary>
    /// Asynchronously loads a <see cref="LeedLevelDocument"/> from a file path.
    /// </summary>
    public static async Task<LeedLevelDocument> LoadAsync(string filePath, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Level file not found: {filePath}", filePath);
        }

        await using var stream = File.OpenRead(filePath);
        return await LoadAsync(stream, options, cancellationToken);
    }

    /// <summary>
    /// Serializes a <see cref="LeedLevelDocument"/> to the specified file path.
    /// </summary>
    public static void Save(LeedLevelDocument document, string filePath, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = SaveToJson(document, options);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Serializes a <see cref="LeedLevelDocument"/> to a formatted JSON string.
    /// </summary>
    public static string SaveToJson(LeedLevelDocument document, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, options ?? DefaultOptions);
    }
}
