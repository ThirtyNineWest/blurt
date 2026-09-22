using System.Runtime.InteropServices;

namespace Blurt.Engine.Windows.Injection.Interop;

/// <summary>The <c>user32</c> calls that bring a window to the foreground.</summary>
internal static partial class User32Foreground
{
    /// <summary>Returns whether <paramref name="window"/> identifies an existing window.</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindow(nint window);

    /// <summary>Asks the system to bring <paramref name="window"/> to the foreground; the system may refuse.</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint window);
}
