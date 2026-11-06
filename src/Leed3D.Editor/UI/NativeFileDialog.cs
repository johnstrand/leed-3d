using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Leed3D.Editor.UI;

/// <summary>
/// Cross-platform native OS system file dialog helper for opening <c>.level.json</c> files.
/// Uses Win32 <c>GetOpenFileNameW</c> on Windows, <c>osascript</c> on macOS, and <c>zenity</c>/<c>kdialog</c> on Linux.
/// </summary>
public static class NativeFileDialog
{
    private const int MaxFileBufferChars = 4096;

    // Win32 OPENFILENAME flags
    private const int OFN_READONLY = 0x00000001;
    private const int OFN_OVERWRITEPROMPT = 0x00000002;
    private const int OFN_HIDEREADONLY = 0x00000004;
    private const int OFN_NOCHANGEDIR = 0x00000008;
    private const int OFN_PATHMUSTEXIST = 0x00000800;
    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_EXPLORER = 0x00080000;

    /// <summary>
    /// Attempts to open the native OS file picker dialog for selecting a <c>.level.json</c> file.
    /// </summary>
    /// <param name="initialDirectory">Optional initial directory to start browsing in.</param>
    /// <param name="ownerWindowHandle">Optional native window handle (HWND on Windows).</param>
    /// <param name="selectedPath">Receives the chosen file path, or <c>null</c> if the user cancelled.</param>
    /// <returns><c>true</c> if a native system dialog was supported and shown (even if cancelled by the user); <c>false</c> if unavailable on the host environment.</returns>
    public static bool TryShowOpenLevelDialog(string? initialDirectory, nint ownerWindowHandle, out string? selectedPath)
    {
        selectedPath = null;

        string? resolvedInitialDir = ResolveExistingDirectory(initialDirectory);

        if (OperatingSystem.IsWindows())
        {
            return TryShowWindowsOpenDialog(
                title: "Open Level (.level.json)",
                filter: "Leed3D Level Files (*.level.json;*.json)\0*.level.json;*.json\0All Files (*.*)\0*.*\0\0",
                defaultExt: "json",
                initialDirectory: resolvedInitialDir,
                ownerWindowHandle: ownerWindowHandle,
                out selectedPath);
        }

        if (OperatingSystem.IsMacOS())
        {
            return TryShowMacOpenDialog("Open Level (.level.json)", resolvedInitialDir, out selectedPath);
        }

        if (OperatingSystem.IsLinux())
        {
            return TryShowLinuxOpenDialog("Open Level (.level.json)", resolvedInitialDir, out selectedPath);
        }

        return false;
    }

    /// <summary>
    /// Attempts to open the native OS Save As dialog for choosing a destination <c>.level.json</c> file.
    /// </summary>
    /// <param name="initialFilePath">Optional initial file path or filename to pre-populate.</param>
    /// <param name="ownerWindowHandle">Optional native window handle (HWND on Windows).</param>
    /// <param name="selectedPath">Receives the chosen destination file path, or <c>null</c> if the user cancelled.</param>
    /// <returns><c>true</c> if a native system dialog was supported and shown (even if cancelled by the user); <c>false</c> if unavailable on the host environment.</returns>
    public static bool TryShowSaveLevelDialog(string? initialFilePath, nint ownerWindowHandle, out string? selectedPath)
    {
        selectedPath = null;

        string? initialDir = null;
        string defaultFileName = "my_level.level.json";

        if (!string.IsNullOrWhiteSpace(initialFilePath))
        {
            try
            {
                string? dir = Path.GetDirectoryName(initialFilePath);
                initialDir = ResolveExistingDirectory(dir);
                string fileName = Path.GetFileName(initialFilePath);
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    defaultFileName = fileName;
                }
            }
            catch
            {
                // Ignore invalid initial path
            }
        }

        if (OperatingSystem.IsWindows())
        {
            return TryShowWindowsSaveDialog(
                title: "Save Level As (.level.json)",
                filter: "Leed3D Level Files (*.level.json)\0*.level.json\0JSON Files (*.json)\0*.json\0All Files (*.*)\0*.*\0\0",
                defaultExt: "level.json",
                initialDirectory: initialDir,
                defaultFileName: defaultFileName,
                ownerWindowHandle: ownerWindowHandle,
                out selectedPath);
        }

        if (OperatingSystem.IsMacOS())
        {
            return TryShowMacSaveDialog("Save Level As (.level.json)", initialDir, defaultFileName, out selectedPath);
        }

        if (OperatingSystem.IsLinux())
        {
            return TryShowLinuxSaveDialog("Save Level As (.level.json)", initialDir, defaultFileName, out selectedPath);
        }

        return false;
    }

    private static string? ResolveExistingDirectory(string? candidateDir)
    {
        if (!string.IsNullOrWhiteSpace(candidateDir))
        {
            try
            {
                string full = Path.GetFullPath(candidateDir);
                if (Directory.Exists(full))
                {
                    return full;
                }
            }
            catch
            {
                // Ignore invalid directory paths and fall back
            }
        }

        return null;
    }

    private static bool TryShowWindowsOpenDialog(
        string title,
        string filter,
        string defaultExt,
        string? initialDirectory,
        nint ownerWindowHandle,
        out string? selectedPath)
    {
        selectedPath = null;
        string savedCurrentDir = Environment.CurrentDirectory;
        nint fileBuffer = Marshal.AllocHGlobal(MaxFileBufferChars * sizeof(char));

        try
        {
            // Zero-initialize first character so the filename box starts empty
            Marshal.WriteInt16(fileBuffer, 0, 0);

            var ofn = new OPENFILENAMEW
            {
                lStructSize = Marshal.SizeOf<OPENFILENAMEW>(),
                hwndOwner = ownerWindowHandle,
                lpstrFilter = filter,
                nFilterIndex = 1,
                lpstrFile = fileBuffer,
                nMaxFile = MaxFileBufferChars,
                lpstrInitialDir = initialDirectory,
                lpstrTitle = title,
                lpstrDefExt = defaultExt,
                Flags = OFN_EXPLORER | OFN_PATHMUSTEXIST | OFN_FILEMUSTEXIST | OFN_HIDEREADONLY | OFN_NOCHANGEDIR | OFN_READONLY
            };

            bool accepted = GetOpenFileNameW(ref ofn);
            if (accepted)
            {
                string? result = Marshal.PtrToStringUni(fileBuffer);
                if (!string.IsNullOrWhiteSpace(result))
                {
                    selectedPath = result;
                }
            }

            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(fileBuffer);
            try
            {
                Environment.CurrentDirectory = savedCurrentDir;
            }
            catch
            {
                // Ignore if savedCurrentDir is no longer accessible
            }
        }
    }

    private static bool TryShowMacOpenDialog(string title, string? initialDirectory, out string? selectedPath)
    {
        selectedPath = null;
        try
        {
            string defaultLocationClause = !string.IsNullOrWhiteSpace(initialDirectory)
                ? $" default location (POSIX file \"{EscapeAppleScriptString(initialDirectory)}\")"
                : string.Empty;

            string script = $"POSIX path of (choose file of type {{\"json\"}} with prompt \"{EscapeAppleScriptString(title)}\"{defaultLocationClause})";
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "osascript",
                ArgumentList = { "-e", script },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (proc is null)
            {
                return false;
            }

            string output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit();

            if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                selectedPath = output;
            }

            // ExitCode 1 with user cancel (-128) is normal cancellation on macOS
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryShowLinuxOpenDialog(string title, string? initialDirectory, out string? selectedPath)
    {
        selectedPath = null;

        // 1. Try zenity
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "zenity",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--file-selection");
            startInfo.ArgumentList.Add($"--title={title}");
            if (!string.IsNullOrWhiteSpace(initialDirectory))
            {
                startInfo.ArgumentList.Add($"--filename={Path.TrimEndingDirectorySeparator(initialDirectory)}/");
            }
            startInfo.ArgumentList.Add("--file-filter=Leed3D Level Files (*.level.json *.json) | *.level.json *.json");
            startInfo.ArgumentList.Add("--file-filter=All Files | *");

            using var proc = Process.Start(startInfo);
            if (proc is not null)
            {
                string output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit();
                if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    selectedPath = output;
                }
                return true;
            }
        }
        catch
        {
            // Fall through to kdialog
        }

        // 2. Try kdialog
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "kdialog",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--getopenfilename");
            startInfo.ArgumentList.Add(!string.IsNullOrWhiteSpace(initialDirectory) ? initialDirectory : ".");
            startInfo.ArgumentList.Add("*.level.json *.json|Leed3D Level Files");
            startInfo.ArgumentList.Add($"--title={title}");

            using var proc = Process.Start(startInfo);
            if (proc is not null)
            {
                string output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit();
                if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    selectedPath = output;
                }
                return true;
            }
        }
        catch
        {
            // Neither zenity nor kdialog available
        }

        return false;
    }

    private static string EscapeAppleScriptString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static bool TryShowWindowsSaveDialog(
        string title,
        string filter,
        string defaultExt,
        string? initialDirectory,
        string defaultFileName,
        nint ownerWindowHandle,
        out string? selectedPath)
    {
        selectedPath = null;
        string savedCurrentDir = Environment.CurrentDirectory;
        nint fileBuffer = Marshal.AllocHGlobal(MaxFileBufferChars * sizeof(char));

        try
        {
            string seedName = string.IsNullOrWhiteSpace(defaultFileName) ? "my_level.level.json" : defaultFileName;
            if (seedName.Length >= MaxFileBufferChars)
            {
                seedName = seedName[..(MaxFileBufferChars - 1)];
            }

            char[] chars = seedName.ToCharArray();
            Marshal.Copy(chars, 0, fileBuffer, chars.Length);
            Marshal.WriteInt16(fileBuffer, chars.Length * sizeof(char), 0);

            var ofn = new OPENFILENAMEW
            {
                lStructSize = Marshal.SizeOf<OPENFILENAMEW>(),
                hwndOwner = ownerWindowHandle,
                lpstrFilter = filter,
                nFilterIndex = 1,
                lpstrFile = fileBuffer,
                nMaxFile = MaxFileBufferChars,
                lpstrInitialDir = initialDirectory,
                lpstrTitle = title,
                lpstrDefExt = defaultExt,
                Flags = OFN_EXPLORER | OFN_PATHMUSTEXIST | OFN_OVERWRITEPROMPT | OFN_HIDEREADONLY | OFN_NOCHANGEDIR
            };

            bool accepted = GetSaveFileNameW(ref ofn);
            if (accepted)
            {
                string? result = Marshal.PtrToStringUni(fileBuffer);
                if (!string.IsNullOrWhiteSpace(result))
                {
                    selectedPath = result;
                }
            }

            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(fileBuffer);
            try
            {
                Environment.CurrentDirectory = savedCurrentDir;
            }
            catch
            {
                // Ignore if savedCurrentDir is no longer accessible
            }
        }
    }

    private static bool TryShowMacSaveDialog(
        string title,
        string? initialDirectory,
        string defaultFileName,
        out string? selectedPath)
    {
        selectedPath = null;
        try
        {
            string defaultLocationClause = !string.IsNullOrWhiteSpace(initialDirectory)
                ? $" default location (POSIX file \"{EscapeAppleScriptString(initialDirectory)}\")"
                : string.Empty;

            string script =
                $"POSIX path of (choose file name with prompt \"{EscapeAppleScriptString(title)}\" default name \"{EscapeAppleScriptString(defaultFileName)}\"{defaultLocationClause})";
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "osascript",
                ArgumentList = { "-e", script },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (proc is null)
            {
                return false;
            }

            string output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit();

            if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                selectedPath = output;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryShowLinuxSaveDialog(
        string title,
        string? initialDirectory,
        string defaultFileName,
        out string? selectedPath)
    {
        selectedPath = null;
        string initialTarget = !string.IsNullOrWhiteSpace(initialDirectory)
            ? Path.Combine(initialDirectory, defaultFileName)
            : defaultFileName;

        // 1. Try zenity
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "zenity",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--file-selection");
            startInfo.ArgumentList.Add("--save");
            startInfo.ArgumentList.Add("--confirm-overwrite");
            startInfo.ArgumentList.Add($"--title={title}");
            startInfo.ArgumentList.Add($"--filename={initialTarget}");
            startInfo.ArgumentList.Add("--file-filter=Leed3D Level Files (*.level.json *.json) | *.level.json *.json");
            startInfo.ArgumentList.Add("--file-filter=All Files | *");

            using var proc = Process.Start(startInfo);
            if (proc is not null)
            {
                string output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit();
                if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    selectedPath = output;
                }
                return true;
            }
        }
        catch
        {
            // Fall through to kdialog
        }

        // 2. Try kdialog
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "kdialog",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--getsavefilename");
            startInfo.ArgumentList.Add(initialTarget);
            startInfo.ArgumentList.Add("*.level.json *.json|Leed3D Level Files");
            startInfo.ArgumentList.Add($"--title={title}");

            using var proc = Process.Start(startInfo);
            if (proc is not null)
            {
                string output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit();
                if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    selectedPath = output;
                }
                return true;
            }
        }
        catch
        {
            // Neither zenity nor kdialog available
        }

        return false;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAMEW
    {
        public int lStructSize;
        public nint hwndOwner;
        public nint hInstance;
        public string? lpstrFilter;
        public nint lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public nint lpstrFile;
        public int nMaxFile;
        public nint lpstrFileTitle;
        public int nMaxFileTitle;
        public string? lpstrInitialDir;
        public string? lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string? lpstrDefExt;
        public nint lCustData;
        public nint lpfnHook;
        public string? lpTemplateName;
        public nint pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileNameW(ref OPENFILENAMEW lpofn);

    [DllImport("comdlg32.dll", EntryPoint = "GetSaveFileNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSaveFileNameW(ref OPENFILENAMEW lpofn);
}
