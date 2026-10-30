namespace Leed3D.Kernel.Export;

/// <summary>
/// Shared color mapping utility for OBJ and glTF exporters.
/// </summary>
internal static class ExportMaterialUtils
{
    public static (float R, float G, float B) GetMaterialRgb(string materialId)
    {
        string normId = materialId?.Trim().ToLowerInvariant() ?? "dev_grid_gray";
        return normId switch
        {
            "dev_grid_gray" => (0.68f, 0.71f, 0.75f),
            "dev_grid_orange" => (0.90f, 0.52f, 0.22f),
            "dev_grid_blue" => (0.32f, 0.58f, 0.88f),
            "dev_grid_green" => (0.35f, 0.75f, 0.42f),
            "dev_grid_purple" => (0.65f, 0.35f, 0.85f),
            "dev_grid_red" => (0.85f, 0.25f, 0.25f),
            "dev_uv_checker" => (0.90f, 0.90f, 0.90f),
            "dev_trigger" => (0.85f, 0.45f, 0.10f),
            "dev_portal" => (0.20f, 0.70f, 0.90f),
            "dev_clip" => (0.80f, 0.20f, 0.20f),
            "dev_lava" => (0.95f, 0.35f, 0.05f),
            "dev_water" => (0.10f, 0.45f, 0.85f),
            _ => (0.75f, 0.75f, 0.75f)
        };
    }
}
