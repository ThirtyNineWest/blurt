using System.Runtime.InteropServices;

namespace Blurt.Engine.Windows.FocusCapture.Interop;

/// <summary>The <c>user32</c> window queries focus capture needs.</summary>
internal static partial class User32Window
{
    /// <summary>Returns the foreground window, or zero when there is none.</summary>
    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    /// <summary>Returns the id of the thread that created <paramref name="window"/> and writes its process id.</summary>
    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW", SetLastError = true)]
    private static partial int GetWindowTextLength(nint window);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", SetLastError = true)]
    private static unsafe partial int GetWindowText(nint window, char* buffer, int maxCount);

    /// <summary>Returns the process id owning <paramref name="window"/>, or zero when it cannot be read.</summary>
    internal static int ProcessIdOf(nint window)
    {
        if (window == 0)
        {
            return 0;
        }

        _ = GetWindowThreadProcessId(window, out var processId);
        return (int)processId;
    }

    /// <summary>Returns the title of <paramref name="window"/>, or null when it has none or cannot be read.</summary>
    internal static unsafe string? TitleOf(nint window)
    {
        if (window == 0)
        {
            return null;
        }

        var length = GetWindowTextLength(window);
        if (length <= 0)
        {
            return null;
        }

        var buffer = new char[length + 1];
        fixed (char* pointer = buffer)
        {
            var copied = GetWindowText(window, pointer, buffer.Length);
            return copied > 0 ? new string(buffer, 0, copied) : null;
        }
    }
}
