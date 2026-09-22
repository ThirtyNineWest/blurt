using System.Runtime.InteropServices;

namespace Blurt.Engine.Windows.Hotkey.Interop;

/// <summary>The <c>user32</c> and <c>kernel32</c> calls a low-level keyboard hook and its message loop need.</summary>
internal static partial class User32Hook
{
    internal const int WhKeyboardLl = 13;
    internal const uint WmQuit = 0x0012;
    internal const uint WmUser = 0x0400;
    internal const uint PmNoRemove = 0x0000;

    /// <summary>The <c>KBDLLHOOKSTRUCT</c> a <c>WH_KEYBOARD_LL</c> callback receives through <c>lParam</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct KbdLlHookStruct
    {
        internal uint VkCode;
        internal uint ScanCode;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }

    /// <summary>The <c>MSG</c> structure the message loop reads.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Msg
    {
        internal nint Hwnd;
        internal uint Message;
        internal nuint WParam;
        internal nint LParam;
        internal uint Time;
        internal int PointX;
        internal int PointY;
        internal uint Private;
    }

    /// <summary>The <c>LowLevelKeyboardProc</c> signature.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint HookProc(int nCode, nint wParam, nint lParam);

    /// <summary>Installs a hook procedure; returns the hook handle, or zero on failure.</summary>
    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    internal static partial nint SetWindowsHookEx(int idHook, nint proc, nint module, uint threadId);

    /// <summary>Removes a hook installed by <see cref="SetWindowsHookEx"/>.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnhookWindowsHookEx(nint hook);

    /// <summary>Passes the hook information to the next hook procedure in the chain.</summary>
    [LibraryImport("user32.dll")]
    internal static partial nint CallNextHookEx(nint hook, int nCode, nint wParam, nint lParam);

    /// <summary>Retrieves the next message for the calling thread; returns 0 on <c>WM_QUIT</c> and -1 on error.</summary>
    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    internal static partial int GetMessage(out Msg message, nint hwnd, uint filterMin, uint filterMax);

    /// <summary>Checks the calling thread's queue without blocking, creating the queue if it does not exist.</summary>
    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PeekMessage(out Msg message, nint hwnd, uint filterMin, uint filterMax, uint remove);

    /// <summary>Translates virtual-key messages into character messages.</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TranslateMessage(in Msg message);

    /// <summary>Dispatches a message to a window procedure.</summary>
    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    internal static partial nint DispatchMessage(in Msg message);

    /// <summary>Posts a message to a thread's queue.</summary>
    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);

    /// <summary>Returns the calling thread's native id.</summary>
    [LibraryImport("kernel32.dll")]
    internal static partial uint GetCurrentThreadId();

    /// <summary>Returns the module handle of the calling process's executable when <paramref name="moduleName"/> is null.</summary>
    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint GetModuleHandle(string? moduleName);
}
