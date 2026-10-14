using System.IO;

namespace Blurt.Engine.Windows.FocusCapture;

/// <summary>
/// Classifies hosts whose focused text field can expose no editable UI Automation signal: web browsers and
/// Chromium/Electron apps. The injector pastes into these even when the focused element reports nothing editable.
/// </summary>
public static class ChromiumHost
{
    private const string ChromiumPak = "chrome_100_percent.pak";

    /// <summary>Process names, without <c>.exe</c>, of known web browsers.</summary>
    public static readonly IReadOnlyList<string> BrowserProcessNames =
        ["chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc"];

    /// <summary>
    /// Returns true when <paramref name="processName"/> (case-insensitive, with or without <c>.exe</c>) is a known
    /// browser, or when the directory of <paramref name="processPath"/> holds the Chromium resource pack beside the
    /// exe or one directory down. Returns false when neither is known.
    /// </summary>
    public static bool IsOpaque(string? processPath, string? processName)
    {
        if (IsBrowserName(processName))
        {
            return true;
        }

        var directory = string.IsNullOrEmpty(processPath) ? null : Path.GetDirectoryName(processPath);
        return !string.IsNullOrEmpty(directory) && HasChromiumPak(directory);
    }

    /// <summary>
    /// Returns whether <c>chrome_100_percent.pak</c> exists in <paramref name="exeDirectory"/> or in any immediate
    /// subdirectory. An unreadable or missing directory returns false.
    /// </summary>
    internal static bool HasChromiumPak(string exeDirectory)
    {
        try
        {
            if (File.Exists(Path.Combine(exeDirectory, ChromiumPak)))
            {
                return true;
            }

            if (!Directory.Exists(exeDirectory))
            {
                return false;
            }

            foreach (var subdirectory in Directory.EnumerateDirectories(exeDirectory))
            {
                if (File.Exists(Path.Combine(subdirectory, ChromiumPak)))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsBrowserName(string? processName)
    {
        if (string.IsNullOrEmpty(processName))
        {
            return false;
        }

        var name = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;
        return BrowserProcessNames.Contains(name, StringComparer.OrdinalIgnoreCase);
    }
}
