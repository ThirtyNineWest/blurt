using System.Runtime.InteropServices;

namespace Blurt.Engine.Windows.Injection.Interop;

/// <summary>The <c>user32</c> clipboard calls and <c>kernel32</c> global memory calls the clipboard needs.</summary>
internal static partial class User32Clipboard
{
    internal const uint CfUnicodeText = 13;
    internal const uint FirstRegisteredFormat = 0xC000;
    internal const uint GmemMoveable = 0x0002;

    /// <summary>Opens the clipboard for the calling thread; <paramref name="owner"/> zero associates it with the thread.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool OpenClipboard(nint owner);

    /// <summary>Closes the clipboard opened by <see cref="OpenClipboard"/>.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseClipboard();

    /// <summary>Empties the open clipboard and frees its data.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EmptyClipboard();

    /// <summary>Returns the format after <paramref name="format"/>, or zero at the end or on error.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint EnumClipboardFormats(uint format);

    /// <summary>Returns the data handle for <paramref name="format"/>, or zero when it is absent or not rendered.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint GetClipboardData(uint format);

    /// <summary>Places <paramref name="data"/> on the open clipboard; on success the system owns the memory.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint SetClipboardData(uint format, nint data);

    /// <summary>Returns whether the clipboard holds <paramref name="format"/>.</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsClipboardFormatAvailable(uint format);

    /// <summary>Returns the clipboard sequence number, which changes on every clipboard write.</summary>
    [LibraryImport("user32.dll")]
    internal static partial uint GetClipboardSequenceNumber();

    /// <summary>Registers <paramref name="name"/>, or returns its existing id; zero on failure.</summary>
    [LibraryImport("user32.dll", EntryPoint = "RegisterClipboardFormatW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint RegisterClipboardFormat(string name);

    [LibraryImport("user32.dll", EntryPoint = "GetClipboardFormatNameW", SetLastError = true)]
    private static unsafe partial int GetClipboardFormatName(uint format, char* buffer, int maxCount);

    /// <summary>Allocates a global memory block; zero on failure.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint GlobalAlloc(uint flags, nuint bytes);

    /// <summary>Frees a global memory block; returns zero on success.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint GlobalFree(nint memory);

    /// <summary>Locks a global memory block and returns a pointer to it; zero on failure.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint GlobalLock(nint memory);

    /// <summary>Unlocks a global memory block locked by <see cref="GlobalLock"/>.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GlobalUnlock(nint memory);

    /// <summary>Returns the size of a global memory block in bytes; zero on failure.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nuint GlobalSize(nint memory);

    /// <summary>Returns the registered name of <paramref name="format"/>, or null when it has none.</summary>
    internal static unsafe string? FormatName(uint format)
    {
        const int capacity = 256;
        var buffer = stackalloc char[capacity];
        var length = GetClipboardFormatName(format, buffer, capacity);
        return length > 0 ? new string(buffer, 0, length) : null;
    }
}
