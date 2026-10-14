using System.ComponentModel;
using System.Diagnostics;
using Blurt.Engine.Diagnostics;

namespace Blurt.App.Services;

/// <summary>The web pages Blurt links to, and the one place that opens them in the default browser.</summary>
public static class BlurtLinks
{
    /// <summary>The AssemblyAI page behind the "Powered by AssemblyAI" footer link.</summary>
    public static readonly Uri PoweredBy = new("https://www.assemblyai.com/blurt");

    /// <summary>The issue tracker of the Windows port's repository.</summary>
    public static readonly Uri ReportBug = new("https://github.com/ThirtyNineWest/blurt/issues");

    /// <summary>LinkedIn's share dialog, prefilled with the Blurt page.</summary>
    public static readonly Uri ShareOnLinkedIn =
        new("https://www.linkedin.com/sharing/share-offsite/?url=https%3A%2F%2Fwww.assemblyai.com%2Fblurt");

    /// <summary>
    /// Opens <paramref name="url"/> in the default browser. A shell that refuses is logged under
    /// <paramref name="logCategory"/> and never thrown.
    /// </summary>
    public static void Open(Uri url, string logCategory)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception failure) when (failure is Win32Exception or InvalidOperationException)
        {
            BlurtLog.Error(logCategory, $"could not open {url.Host}: {failure.Message}");
        }
    }
}
