using System.IO;

namespace Blurt.Engine.Windows.Config;

/// <summary>The file-system locations the engine reads and writes settings, credentials, and logs at.</summary>
public static class WindowsPaths
{
    /// <summary>The per-host application data directory: <c>%LOCALAPPDATA%\{identity.LogDirectoryName}</c>.</summary>
    public static string AppDataDirectory(HostIdentity identity) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), identity.LogDirectoryName);

    /// <summary>The JSON settings file: <c>{AppDataDirectory}\settings.json</c>.</summary>
    public static string SettingsFile(HostIdentity identity) => Path.Combine(AppDataDirectory(identity), "settings.json");

    /// <summary>The developer-mode log directory: <c>{AppDataDirectory}\Logs</c>.</summary>
    public static string LogDirectory(HostIdentity identity) => Path.Combine(AppDataDirectory(identity), "Logs");
}
