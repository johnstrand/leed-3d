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
        using var jsonDoc = JsonDocument.Parse(stream, CreateDocumentParseOptions(options));
        return DeserializeAndValidate(jsonDoc.RootElement, options);
    }

    /// <summary>
    /// Parses a <see cref="LeedLevelDocument"/> directly from a JSON string.
    /// </summary>
    public static LeedLevelDocument LoadFromJson(string json, JsonSerializerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var jsonDoc = JsonDocument.Parse(json, CreateDocumentParseOptions(options));
        return DeserializeAndValidate(jsonDoc.RootElement, options);
    }

    /// <summary>
    /// Asynchronously loads a <see cref="LeedLevelDocument"/> from a stream.
    /// </summary>
    public static async Task<LeedLevelDocument> LoadAsync(Stream stream, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var jsonDoc = await JsonDocument.ParseAsync(stream, CreateDocumentParseOptions(options), cancellationToken);
        return DeserializeAndValidate(jsonDoc.RootElement, options);
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

    private static JsonDocumentOptions CreateDocumentParseOptions(JsonSerializerOptions? options)
    {
        JsonSerializerOptions effective = options ?? DefaultOptions;
        return new JsonDocumentOptions
        {
            CommentHandling = effective.ReadCommentHandling,
            AllowTrailingCommas = effective.AllowTrailingCommas,
            MaxDepth = effective.MaxDepth
        };
    }

    private static LeedLevelDocument DeserializeAndValidate(JsonElement root, JsonSerializerOptions? options)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"Invalid level file: expected JSON object root, found {root.ValueKind}.");
        }

        bool hasEditor = false;
        bool hasBaked = false;

        foreach (JsonProperty prop in root.EnumerateObject())
        {
            if (prop.Name.Equals("editor", StringComparison.OrdinalIgnoreCase))
            {
                if (prop.Value.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException("Invalid level file: 'editor' section must be a JSON object.");
                }
                hasEditor = true;
            }
            else if (prop.Name.Equals("baked", StringComparison.OrdinalIgnoreCase))
            {
                if (prop.Value.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException("Invalid level file: 'baked' section must be a JSON object.");
                }
                hasBaked = true;
            }
        }

        if (!hasEditor && !hasBaked)
        {
            throw new InvalidDataException("Invalid level file: JSON root must contain an 'editor' or 'baked' section.");
        }

        LeedLevelDocument? doc = root.Deserialize<LeedLevelDocument>(options ?? DefaultOptions);
        if (doc is null)
        {
            throw new InvalidDataException("Failed to deserialize LeedLevelDocument: JSON root was null.");
        }

        if (doc.FormatVersion < 1 || doc.FormatVersion > LeedLevelDocument.CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Unsupported level format version: {doc.FormatVersion}. Expected version {LeedLevelDocument.CurrentFormatVersion}.");
        }

        if (doc.Editor is null)
        {
            throw new InvalidDataException("Invalid level file: 'editor' section cannot be null.");
        }

        if (doc.Baked is null)
        {
            throw new InvalidDataException("Invalid level file: 'baked' section cannot be null.");
        }

        return doc;
    }
}
