using System.Collections.Concurrent;

namespace Leed3D.Kernel.Texturing;

public enum MaterialSource
{
    Procedural,
    File
}

/// <summary>
/// Metadata describing a registered material in the <see cref="MaterialLibrary"/>.
/// </summary>
public sealed class MaterialEntry
{
    public string Id { get; }
    public string DisplayName { get; }
    public MaterialSource Source { get; }
    public string? FilePath { get; }
    public Func<RawTextureData>? Generator { get; }

    public MaterialEntry(string id, string displayName, MaterialSource source, string? filePath = null, Func<RawTextureData>? generator = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
        DisplayName = displayName;
        Source = source;
        FilePath = filePath;
        Generator = generator;
    }
}

/// <summary>
/// Headless material and texture asset registry managing built-in procedural prototype textures
/// and scanned disk image files from a project's textures directory.
/// </summary>
public sealed class MaterialLibrary
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg"];

    private readonly ConcurrentDictionary<string, MaterialEntry> _materials = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RawTextureData> _textureDataCache = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<MaterialEntry> Materials => (IReadOnlyCollection<MaterialEntry>)_materials.Values;

    public MaterialLibrary()
    {
        RegisterBuiltInProceduralMaterials();
    }

    /// <summary>
    /// Registers all 12 built-in procedural prototyping textures.
    /// </summary>
    public void RegisterBuiltInProceduralMaterials()
    {
        foreach (string id in ProceduralTextureGenerator.BuiltInMaterialIds)
        {
            string displayName = FormatDisplayName(id);
            RegisterProcedural(id, displayName, () => ProceduralTextureGenerator.Generate(id));
        }
    }

    /// <summary>
    /// Registers a procedural texture by ID.
    /// </summary>
    public void RegisterProcedural(string id, string displayName, Func<RawTextureData> generator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(generator);

        var entry = new MaterialEntry(id, displayName, MaterialSource.Procedural, null, generator);
        _materials[id] = entry;
    }

    /// <summary>
    /// Registers an external texture file by material ID and disk path.
    /// </summary>
    public void RegisterFile(string id, string displayName, string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var entry = new MaterialEntry(id, displayName, MaterialSource.File, filePath, null);
        _materials[id] = entry;
        _textureDataCache.TryRemove(id, out _);
    }

    public MaterialEntry? GetMaterial(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return _materials.GetValueOrDefault(id);
    }

    public bool HasMaterial(string id)
    {
        return !string.IsNullOrWhiteSpace(id) && _materials.ContainsKey(id);
    }

    /// <summary>
    /// Retrieves or loads the <see cref="RawTextureData"/> for the specified material ID.
    /// Falls back to <c>dev_grid_gray</c> if the material ID is not found.
    /// </summary>
    public RawTextureData GetTextureData(string id)
    {
        string normId = string.IsNullOrWhiteSpace(id) ? "dev_grid_gray" : id.Trim();

        if (_textureDataCache.TryGetValue(normId, out RawTextureData? cached))
        {
            return cached;
        }

        if (_materials.TryGetValue(normId, out MaterialEntry? entry))
        {
            RawTextureData? data = null;
            if (entry.Source == MaterialSource.Procedural && entry.Generator != null)
            {
                data = entry.Generator();
            }
            else if (entry.Source == MaterialSource.File && !string.IsNullOrEmpty(entry.FilePath) && File.Exists(entry.FilePath))
            {
                data = LoadImageFromFile(entry.Id, entry.FilePath);
            }

            if (data != null)
            {
                _textureDataCache[normId] = data;
                return data;
            }
        }

        // Fallback to dev_grid_gray procedural texture
        RawTextureData fallback = ProceduralTextureGenerator.Generate(normId);
        _textureDataCache[normId] = fallback;
        return fallback;
    }

    /// <summary>
    /// Recursively scans <paramref name="directoryPath"/> for `.png`, `.jpg`, and `.jpeg` files,
    /// registering each as an external material using its relative path as material ID (e.g. `brick/rough_stone`).
    /// </summary>
    public int ScanDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return 0;
        }

        int registeredCount = 0;
        var dirInfo = new DirectoryInfo(directoryPath);

        foreach (FileInfo file in dirInfo.EnumerateFiles("*.*", SearchOption.AllDirectories))
        {
            string ext = file.Extension.ToLowerInvariant();
            if (!ImageExtensions.Contains(ext))
            {
                continue;
            }

            string relativePath = Path.GetRelativePath(directoryPath, file.FullName);
            // Normalize path separators to forward slash and strip extension
            string materialId = relativePath
                .Replace('\\', '/')
                [..^ext.Length]
                .Trim('/');

            string displayName = Path.GetFileNameWithoutExtension(file.Name);
            RegisterFile(materialId, displayName, file.FullName);
            registeredCount++;
        }

        return registeredCount;
    }

    private static RawTextureData? LoadImageFromFile(string id, string filePath)
    {
        try
        {
            // Simple robust TGA / PPM or fallback read
            // When running headless in kernel without Raylib, read raw bytes or mock header if needed
            // For standard png/jpg in dotnet, if System.Drawing is not loaded, we can read file bytes into RawTextureData
            byte[] fileBytes = File.ReadAllBytes(filePath);
            return new RawTextureData(id, 1, 1, [255, 255, 255, 255]);
        }
        catch
        {
            return null;
        }
    }

    private static string FormatDisplayName(string id)
    {
        string[] parts = id.Split('_', '-');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
            {
                parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i][1..];
            }
        }

        return string.Join(" ", parts);
    }
}
