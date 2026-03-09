using System.Runtime.InteropServices;

namespace SapAbapProject.RfcExtractor;

/// <summary>
/// Manages the location and loading of SAP NetWeaver RFC SDK native libraries.
/// The SDK (sapnwrfc.dll + ICU DLLs) must be installed separately by the user.
/// </summary>
public static class SapRfcSdkManager
{
    private static readonly string SettingsDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SapAbapProject");

    private static readonly string SettingsFilePath =
        Path.Combine(SettingsDirectory, "sdk-path.txt");

    private static bool _isLoaded;

    /// <summary>
    /// Gets the currently configured SDK path, or null if not configured.
    /// </summary>
    public static string? GetConfiguredSdkPath()
    {
        if (!File.Exists(SettingsFilePath))
            return null;

        var path = File.ReadAllText(SettingsFilePath).Trim();
        return IsValidSdkPath(path) ? path : null;
    }

    /// <summary>
    /// Saves the SDK path to persistent settings.
    /// </summary>
    public static void SaveSdkPath(string sdkPath)
    {
        if (!Directory.Exists(SettingsDirectory))
            Directory.CreateDirectory(SettingsDirectory);

        File.WriteAllText(SettingsFilePath, sdkPath);
    }

    /// <summary>
    /// Validates that the given directory contains the required SDK files.
    /// </summary>
    public static bool IsValidSdkPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        return File.Exists(Path.Combine(path, "sapnwrfc.dll"))
            && File.Exists(Path.Combine(path, "icudt50.dll"))
            && File.Exists(Path.Combine(path, "icuin50.dll"))
            && File.Exists(Path.Combine(path, "icuuc50.dll"))
            && File.Exists(Path.Combine(path, "libsapucum.dll"));
    }

    /// <summary>
    /// Adds the SDK path to the DLL search path so sapnwrfc.dll can be loaded.
    /// Must be called before any RFC connection is attempted.
    /// </summary>
    public static bool EnsureSdkLoaded(string? sdkPath = null)
    {
        if (_isLoaded)
            return true;

        sdkPath ??= GetConfiguredSdkPath();
        if (sdkPath is null || !IsValidSdkPath(sdkPath))
            return false;

        // Add to PATH so the native loader can find all dependent DLLs
        var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (!currentPath.Contains(sdkPath, StringComparison.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("PATH", sdkPath + ";" + currentPath);
        }

        // Also use SetDllDirectory for the current process
        SetDllDirectory(sdkPath);

        _isLoaded = true;
        return true;
    }

    /// <summary>
    /// Returns whether the SDK has been successfully loaded.
    /// </summary>
    public static bool IsLoaded => _isLoaded;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDllDirectory(string lpPathName);
}
