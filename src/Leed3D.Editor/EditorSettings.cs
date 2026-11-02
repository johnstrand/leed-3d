using System.Text.Json;

namespace Leed3D.Editor;

public sealed class EditorSettings
{
    public const float MinimumUiScaleMultiplier = 0.75f;
    public const float MaximumUiScaleMultiplier = 2.0f;

    public float UiScaleMultiplier { get; set; } = 1.0f;

    public static string ConfigDirectory
    {
        get
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root))
            {
                root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrWhiteSpace(root))
                {
                    throw new InvalidOperationException("Could not locate a per-user configuration directory.");
                }

                root = Path.Combine(root, ".local", "share");
            }

            return Path.Combine(root, "Leed3D");
        }
    }

    public static string ImGuiIniPath => Path.Combine(ConfigDirectory, "imgui.ini");

    public static string SettingsPath => Path.Combine(ConfigDirectory, "settings.json");

    public static EditorSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new EditorSettings();
        }

        using FileStream stream = File.OpenRead(SettingsPath);
        EditorSettings settings = JsonSerializer.Deserialize<EditorSettings>(stream)
            ?? throw new InvalidDataException($"Editor settings file '{SettingsPath}' is empty or invalid.");

        if (!float.IsFinite(settings.UiScaleMultiplier) ||
            settings.UiScaleMultiplier < MinimumUiScaleMultiplier ||
            settings.UiScaleMultiplier > MaximumUiScaleMultiplier)
        {
            throw new InvalidDataException(
                $"UI scale in '{SettingsPath}' must be between {MinimumUiScaleMultiplier:0.##} and {MaximumUiScaleMultiplier:0.##}.");
        }

        return settings;
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDirectory);
        string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        string temporaryPath = SettingsPath + ".tmp";
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, SettingsPath, overwrite: true);
    }
}
