namespace Leed3D.Editor;

public sealed class EditorSettings
{
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
}
