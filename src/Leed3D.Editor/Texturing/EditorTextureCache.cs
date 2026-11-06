using System.Collections.Concurrent;
using Leed3D.Kernel.Texturing;
using Raylib_cs;

namespace Leed3D.Editor.Texturing;

/// <summary>
/// GPU texture resource manager that uploads <see cref="RawTextureData"/> and external image files
/// to Raylib <see cref="Texture2D"/> handles with <see cref="TextureWrap.Repeat"/> and trilinear/bilinear filtering.
/// </summary>
public sealed class EditorTextureCache : IDisposable
{
    private readonly MaterialLibrary _materialLibrary;
    private readonly ConcurrentDictionary<string, Texture2D> _gpuTextures = new(StringComparer.OrdinalIgnoreCase);
    private bool _isDisposed;

    public MaterialLibrary Library => _materialLibrary;

    public EditorTextureCache(MaterialLibrary materialLibrary)
    {
        _materialLibrary = materialLibrary ?? throw new ArgumentNullException(nameof(materialLibrary));
    }

    /// <summary>
    /// Gets or uploads the GPU <see cref="Texture2D"/> for the given material ID.
    /// Falls back to <c>dev_grid_gray</c> if not found.
    /// </summary>
    public Texture2D GetTexture(string materialId)
    {
        string normId = string.IsNullOrWhiteSpace(materialId) ? "dev_grid_gray" : materialId.Trim();

        if (_gpuTextures.TryGetValue(normId, out Texture2D tex))
        {
            return tex;
        }

        Texture2D loaded = LoadTextureInternal(normId);
        _gpuTextures[normId] = loaded;
        return loaded;
    }

    private Texture2D LoadTextureInternal(string normId)
    {
        MaterialEntry? entry = _materialLibrary.GetMaterial(normId);

        // If file exists and Raylib window is ready, try loading via Raylib
        if (entry != null && entry.Source == MaterialSource.File && !string.IsNullOrEmpty(entry.FilePath) && File.Exists(entry.FilePath) && Raylib.IsWindowReady())
        {
            try
            {
                Image image = Raylib.LoadImage(entry.FilePath);
                if (image.Width > 0 && image.Height > 0)
                {
                    Texture2D fileTex = Raylib.LoadTextureFromImage(image);
                    Raylib.UnloadImage(image);

                    Raylib.SetTextureWrap(fileTex, TextureWrap.Repeat);
                    Raylib.SetTextureFilter(fileTex, TextureFilter.Bilinear);
                    return fileTex;
                }
            }
            catch
            {
                // Fall back to RawTextureData upload
            }
        }

        // Upload from RawTextureData
        RawTextureData raw = _materialLibrary.GetTextureData(normId);
        return UploadRawTexture(raw);
    }

    private static unsafe Texture2D UploadRawTexture(RawTextureData raw)
    {
        if (!Raylib.IsWindowReady())
        {
            return default;
        }

        fixed (byte* ptr = raw.RgbaPixels)
        {
            Image img = new()
            {
                Data = (void*)ptr,
                Width = raw.Width,
                Height = raw.Height,
                Mipmaps = 1,
                Format = PixelFormat.UncompressedR8G8B8A8
            };

            Texture2D tex = Raylib.LoadTextureFromImage(img);
            Raylib.SetTextureWrap(tex, TextureWrap.Repeat);
            Raylib.SetTextureFilter(tex, TextureFilter.Bilinear);
            return tex;
        }
    }

    public void Unload(string materialId)
    {
        if (string.IsNullOrWhiteSpace(materialId)) return;

        if (_gpuTextures.TryRemove(materialId.Trim(), out Texture2D tex) && tex.Id != 0)
        {
            if (Raylib.IsWindowReady())
            {
                Raylib.UnloadTexture(tex);
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (Raylib.IsWindowReady())
        {
            foreach (Texture2D tex in _gpuTextures.Values)
            {
                if (tex.Id != 0)
                {
                    Raylib.UnloadTexture(tex);
                }
            }
        }

        _gpuTextures.Clear();
    }
}
