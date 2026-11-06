namespace Leed3D.Kernel.Texturing;

using Math = System.Math;
using MathF = System.MathF;

/// <summary>
/// Deterministic procedural 256x256 RGBA8 texture generator producing prototype blockout grids,
/// UV orientation checkers, and special gameplay surface textures without external asset dependencies.
/// </summary>
public static class ProceduralTextureGenerator
{
    public const int DefaultSize = 256;

    /// <summary>
    /// Supported built-in procedural material IDs.
    /// </summary>
    public static readonly string[] BuiltInMaterialIds =
    [
        "dev_grid_gray",
        "dev_grid_orange",
        "dev_grid_blue",
        "dev_grid_green",
        "dev_grid_purple",
        "dev_grid_red",
        "dev_uv_checker",
        "dev_trigger",
        "dev_portal",
        "dev_clip",
        "dev_lava",
        "dev_water"
    ];

    /// <summary>
    /// Generates a procedural <see cref="RawTextureData"/> for the given material ID.
    /// Falls back to <c>dev_grid_gray</c> if the ID is unrecognized.
    /// </summary>
    public static RawTextureData Generate(string materialId, int width = DefaultSize, int height = DefaultSize)
    {
        string normId = materialId?.Trim().ToLowerInvariant() ?? "dev_grid_gray";

        return normId switch
        {
            "dev_grid_gray" => GenerateGrid(normId, width, height, (60, 64, 70), (75, 80, 88), (140, 148, 160), (220, 225, 230)),
            "dev_grid_orange" => GenerateGrid(normId, width, height, (180, 85, 20), (210, 105, 25), (245, 140, 45), (255, 220, 160)),
            "dev_grid_blue" => GenerateGrid(normId, width, height, (30, 70, 140), (40, 90, 175), (70, 140, 230), (180, 220, 255)),
            "dev_grid_green" => GenerateGrid(normId, width, height, (35, 110, 50), (45, 135, 65), (80, 190, 100), (190, 250, 205)),
            "dev_grid_purple" => GenerateGrid(normId, width, height, (90, 40, 130), (115, 55, 165), (165, 85, 225), (235, 195, 255)),
            "dev_grid_red" => GenerateGrid(normId, width, height, (150, 30, 30), (185, 40, 40), (230, 70, 70), (255, 185, 185)),
            "dev_uv_checker" => GenerateUvChecker(normId, width, height),
            "dev_trigger" => GenerateTrigger(normId, width, height),
            "dev_portal" => GeneratePortal(normId, width, height),
            "dev_clip" => GenerateClip(normId, width, height),
            "dev_lava" => GenerateLava(normId, width, height),
            "dev_water" => GenerateWater(normId, width, height),
            _ => GenerateGrid(normId, width, height, (60, 64, 70), (75, 80, 88), (140, 148, 160), (220, 225, 230))
        };
    }

    /// <summary>
    /// Generates a prototype blockout grid texture with a crisp outer border, 4x4 sub-grid lines,
    /// subtle checkerboard quadrants, and corner crosshairs.
    /// </summary>
    public static RawTextureData GenerateGrid(
        string id,
        int width,
        int height,
        (byte R, byte G, byte B) baseColorA,
        (byte R, byte G, byte B) baseColorB,
        (byte R, byte G, byte B) subGridColor,
        (byte R, byte G, byte B) outerBorderColor)
    {
        byte[] pixels = new byte[width * height * 4];
        int borderThickness = Math.Max(2, width / 64);
        int subGridStep = width / 4;
        int crosshairSize = width / 16;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * width + x) * 4;

                // Outer border (meter boundary)
                if (x < borderThickness || x >= width - borderThickness ||
                    y < borderThickness || y >= height - borderThickness)
                {
                    pixels[index] = outerBorderColor.R;
                    pixels[index + 1] = outerBorderColor.G;
                    pixels[index + 2] = outerBorderColor.B;
                    pixels[index + 3] = 255;
                    continue;
                }

                // Sub-grid lines (0.25m intervals)
                bool isSubGridLine = (x % subGridStep == 0) || (y % subGridStep == 0);
                if (isSubGridLine)
                {
                    pixels[index] = subGridColor.R;
                    pixels[index + 1] = subGridColor.G;
                    pixels[index + 2] = subGridColor.B;
                    pixels[index + 3] = 255;
                    continue;
                }

                // Corner crosshairs at sub-grid intersections
                bool nearSubX = (x % subGridStep) <= 2 || (x % subGridStep) >= subGridStep - 2;
                bool nearSubY = (y % subGridStep) <= 2 || (y % subGridStep) >= subGridStep - 2;
                int distFromCenterX = Math.Abs(x - (width / 2));
                int distFromCenterY = Math.Abs(y - (height / 2));
                bool isCenterCross = (distFromCenterX <= crosshairSize && Math.Abs(y - (height / 2)) <= 1) ||
                                     (distFromCenterY <= crosshairSize && Math.Abs(x - (width / 2)) <= 1);

                if (isCenterCross)
                {
                    pixels[index] = outerBorderColor.R;
                    pixels[index + 1] = outerBorderColor.G;
                    pixels[index + 2] = outerBorderColor.B;
                    pixels[index + 3] = 255;
                    continue;
                }

                // Subtle quadrant checkerboard pattern (0.5m quadrants)
                bool quadX = (x / (width / 2)) % 2 == 0;
                bool quadY = (y / (height / 2)) % 2 == 0;
                bool isQuadA = quadX ^ quadY;

                var c = isQuadA ? baseColorA : baseColorB;
                pixels[index] = c.R;
                pixels[index + 1] = c.G;
                pixels[index + 2] = c.B;
                pixels[index + 3] = 255;
            }
        }

        return new RawTextureData(id, width, height, pixels);
    }

    /// <summary>
    /// Generates an 8x8 multi-colored UV orientation checkerboard with distinct quadrant hues
    /// and top-right orientation indicator stripes so UV mirror / rotation errors are instantly visible.
    /// </summary>
    public static RawTextureData GenerateUvChecker(string id, int width, int height)
    {
        byte[] pixels = new byte[width * height * 4];
        int numTiles = 8;
        int tileSize = width / numTiles;

        for (int y = 0; y < height; y++)
        {
            int tileY = y / tileSize;
            for (int x = 0; x < width; x++)
            {
                int tileX = x / tileSize;
                int index = (y * width + x) * 4;

                bool isLight = ((tileX + tileY) % 2) == 0;

                // Color tint depending on quadrant:
                // Top-Left: Red tint
                // Top-Right: Green tint (with bright orientation marker)
                // Bottom-Left: Blue tint
                // Bottom-Right: Yellow tint
                byte r, g, b;
                if (tileX < 4 && tileY < 4) // Top-Left (or Bottom-Left in UV)
                {
                    r = isLight ? (byte)240 : (byte)150;
                    g = isLight ? (byte)100 : (byte)40;
                    b = isLight ? (byte)100 : (byte)40;
                }
                else if (tileX >= 4 && tileY < 4) // Top-Right (Orientation indicator quadrant)
                {
                    r = isLight ? (byte)100 : (byte)30;
                    g = isLight ? (byte)230 : (byte)150;
                    b = isLight ? (byte)100 : (byte)30;

                    // Distinct diagonal marker stripe in top-right corner tile
                    if (tileX == 7 && tileY == 0 && Math.Abs((x % tileSize) - (y % tileSize)) <= 3)
                    {
                        r = 255; g = 255; b = 255;
                    }
                }
                else if (tileX < 4 && tileY >= 4)
                {
                    r = isLight ? (byte)80 : (byte)30;
                    g = isLight ? (byte)120 : (byte)60;
                    b = isLight ? (byte)240 : (byte)170;
                }
                else
                {
                    r = isLight ? (byte)230 : (byte)160;
                    g = isLight ? (byte)220 : (byte)150;
                    b = isLight ? (byte)80 : (byte)30;
                }

                // Grid border line between tiles
                if ((x % tileSize == 0) || (y % tileSize == 0) ||
                    x == 0 || y == 0 || x == width - 1 || y == height - 1)
                {
                    r = 30; g = 30; b = 30;
                }

                pixels[index] = r;
                pixels[index + 1] = g;
                pixels[index + 2] = b;
                pixels[index + 3] = 255;
            }
        }

        return new RawTextureData(id, width, height, pixels);
    }

    /// <summary>
    /// Generates a semi-translucent trigger volume marker with high-visibility 45-degree warning hazard stripes.
    /// </summary>
    public static RawTextureData GenerateTrigger(string id, int width, int height)
    {
        byte[] pixels = new byte[width * height * 4];
        int stripeWidth = width / 8;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * width + x) * 4;
                bool isStripe = ((x + y) / stripeWidth) % 2 == 0;

                if (isStripe)
                {
                    pixels[index] = 255;     // R
                    pixels[index + 1] = 160; // G
                    pixels[index + 2] = 0;   // B (Bright amber-orange)
                    pixels[index + 3] = 220; // Semi-translucent A
                }
                else
                {
                    pixels[index] = 40;
                    pixels[index + 1] = 30;
                    pixels[index + 2] = 10;
                    pixels[index + 3] = 160;
                }

                // Border
                if (x < 3 || x >= width - 3 || y < 3 || y >= height - 3)
                {
                    pixels[index] = 255;
                    pixels[index + 1] = 200;
                    pixels[index + 2] = 40;
                    pixels[index + 3] = 255;
                }
            }
        }

        return new RawTextureData(id, width, height, pixels);
    }

    /// <summary>
    /// Generates a cyan concentric portal / teleporter frame pattern.
    /// </summary>
    public static RawTextureData GeneratePortal(string id, int width, int height)
    {
        byte[] pixels = new byte[width * height * 4];
        float cx = width * 0.5f;
        float cy = height * 0.5f;
        float maxR = Math.Min(cx, cy);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * width + x) * 4;
                float dx = x - cx;
                float dy = y - cy;
                float dist = MathF.Sqrt(dx * dx + dy * dy);

                // Concentric ripples
                float ring = MathF.Sin(dist * 0.2f);
                byte intensity = (byte)Math.Clamp((ring * 0.5f + 0.5f) * 200 + 40, 0, 255);

                pixels[index] = (byte)(intensity / 4);  // R
                pixels[index + 1] = (byte)(intensity * 0.9f); // G
                pixels[index + 2] = intensity;          // B (Bright cyan/electric blue)
                pixels[index + 3] = 255;

                // Outer border
                if (x < 2 || x >= width - 2 || y < 2 || y >= height - 2)
                {
                    pixels[index] = 180;
                    pixels[index + 1] = 255;
                    pixels[index + 2] = 255;
                }
            }
        }

        return new RawTextureData(id, width, height, pixels);
    }

    /// <summary>
    /// Generates a magenta collision / player clip pattern with crisp diagonal hash marks.
    /// </summary>
    public static RawTextureData GenerateClip(string id, int width, int height)
    {
        byte[] pixels = new byte[width * height * 4];
        int stripeWidth = width / 12;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * width + x) * 4;
                bool isStripe = ((x - y + width * 2) / stripeWidth) % 2 == 0;

                if (isStripe)
                {
                    pixels[index] = 220;     // R
                    pixels[index + 1] = 30;  // G
                    pixels[index + 2] = 200; // B (Bright magenta)
                    pixels[index + 3] = 255;
                }
                else
                {
                    pixels[index] = 70;
                    pixels[index + 1] = 10;
                    pixels[index + 2] = 60;
                    pixels[index + 3] = 255;
                }

                // Border
                if (x < 2 || x >= width - 2 || y < 2 || y >= height - 2)
                {
                    pixels[index] = 255;
                    pixels[index + 1] = 180;
                    pixels[index + 2] = 250;
                }
            }
        }

        return new RawTextureData(id, width, height, pixels);
    }

    /// <summary>
    /// Generates a glowing molten lava blockout texture with cracked crust and bright magma veins.
    /// </summary>
    public static RawTextureData GenerateLava(string id, int width, int height)
    {
        byte[] pixels = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * width + x) * 4;

                // Multi-frequency noise-like wave functions for lava crust & veins
                float v1 = MathF.Sin(x * 0.08f) + MathF.Cos(y * 0.08f);
                float v2 = MathF.Sin((x + y) * 0.15f) + MathF.Cos((x - y) * 0.15f);
                float val = (v1 + v2) * 0.25f + 0.5f;

                if (val > 0.65f) // Glowing veins
                {
                    pixels[index] = 255;                         // R
                    pixels[index + 1] = (byte)((val - 0.65f) * 600); // G (yellow-hot core)
                    pixels[index + 2] = 20;                          // B
                }
                else if (val > 0.45f) // Red magma
                {
                    pixels[index] = (byte)(180 + (val - 0.45f) * 350);
                    pixels[index + 1] = 30;
                    pixels[index + 2] = 10;
                }
                else // Dark cooling rock crust
                {
                    byte rock = (byte)(30 + val * 50);
                    pixels[index] = (byte)(rock + 20);
                    pixels[index + 1] = (byte)(rock / 2);
                    pixels[index + 2] = (byte)(rock / 3);
                }

                pixels[index + 3] = 255;
            }
        }

        return new RawTextureData(id, width, height, pixels);
    }

    /// <summary>
    /// Generates a deep blue water blockout texture with caustic surface wave reflections.
    /// </summary>
    public static RawTextureData GenerateWater(string id, int width, int height)
    {
        byte[] pixels = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * width + x) * 4;

                float w1 = MathF.Sin(x * 0.09f + y * 0.04f);
                float w2 = MathF.Cos(x * 0.05f - y * 0.08f);
                float caustic = MathF.Abs(w1 + w2) * 0.5f;

                byte r = (byte)(20 + caustic * 50);
                byte g = (byte)(80 + caustic * 100);
                byte b = (byte)(180 + caustic * 75);

                pixels[index] = r;
                pixels[index + 1] = g;
                pixels[index + 2] = b;
                pixels[index + 3] = 230;
            }
        }

        return new RawTextureData(id, width, height, pixels);
    }
}
