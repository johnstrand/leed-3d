namespace Leed3D.Kernel.Texturing;

/// <summary>
/// Headless raw 8-bit RGBA pixel buffer representation of an image texture.
/// Independent of any graphics backend / GPU context.
/// </summary>
public sealed class RawTextureData
{
    public string Id { get; }
    public int Width { get; }
    public int Height { get; }
    public byte[] RgbaPixels { get; }

    public RawTextureData(string id, int width, int height, byte[] rgbaPixels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");
        ArgumentNullException.ThrowIfNull(rgbaPixels);
        if (rgbaPixels.Length != width * height * 4)
        {
            throw new ArgumentException($"Pixel array size ({rgbaPixels.Length}) does not match {width}x{height}x4.", nameof(rgbaPixels));
        }

        Id = id;
        Width = width;
        Height = height;
        RgbaPixels = rgbaPixels;
    }
}
