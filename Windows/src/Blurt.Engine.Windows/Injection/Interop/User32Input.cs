using System.Runtime.InteropServices;
using Blurt.Engine.Windows.Hotkey;

namespace Blurt.Engine.Windows.Injection.Interop;

/// <summary>Synthesizes the Ctrl+V paste keystroke through <c>SendInput</c>.</summary>
public static partial class User32Input
{
    /// <summary><c>INPUT_KEYBOARD</c>.</summary>
    public const uint InputKeyboard = 1;

    /// <summary><c>KEYEVENTF_KEYUP</c>.</summary>
    public const uint KeyEventKeyUp = 0x0002;

    private const ushort VkControl = 0x11;
    private const ushort VkV = 0x56;

    /// <summary>The native <c>INPUT</c> structure.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Input
    {
        /// <summary>The input kind; <see cref="InputKeyboard"/> for keystrokes.</summary>
        public uint Type;

        /// <summary>The kind-specific payload.</summary>
        public InputUnion Union;
    }

    /// <summary>The native <c>INPUT</c> union; the mouse member sets its size.</summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        /// <summary>The <c>MOUSEINPUT</c> member.</summary>
        [FieldOffset(0)]
        public MouseInput Mouse;

        /// <summary>The <c>KEYBDINPUT</c> member.</summary>
        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    /// <summary>The native <c>MOUSEINPUT</c> structure.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MouseInput
    {
        /// <summary><c>dx</c>.</summary>
        public int Dx;

        /// <summary><c>dy</c>.</summary>
        public int Dy;

        /// <summary><c>mouseData</c>.</summary>
        public uint MouseData;

        /// <summary><c>dwFlags</c>.</summary>
        public uint Flags;

        /// <summary><c>time</c>.</summary>
        public uint Time;

        /// <summary><c>dwExtraInfo</c>.</summary>
        public nuint ExtraInfo;
    }

    /// <summary>The native <c>KEYBDINPUT</c> structure.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct KeyboardInput
    {
        /// <summary><c>wVk</c>.</summary>
        public ushort VirtualKey;

        /// <summary><c>wScan</c>.</summary>
        public ushort ScanCode;

        /// <summary><c>dwFlags</c>.</summary>
        public uint Flags;

        /// <summary><c>time</c>.</summary>
        public uint Time;

        /// <summary><c>dwExtraInfo</c>.</summary>
        public nuint ExtraInfo;
    }

    /// <summary>
    /// Returns Ctrl down, V down, V up, Ctrl up, each tagged with <see cref="HookEventReducer.InjectedTag"/> so the
    /// dictation keyboard hook ignores them.
    /// </summary>
    public static Input[] CtrlVInputs() =>
    [
        Key(VkControl, 0),
        Key(VkV, 0),
        Key(VkV, KeyEventKeyUp),
        Key(VkControl, KeyEventKeyUp),
    ];

    /// <summary>Posts Ctrl+V to the foreground window. Returns whether all four events were inserted.</summary>
    public static unsafe bool PostCtrlV()
    {
        var inputs = CtrlVInputs();
        fixed (Input* pointer = inputs)
        {
            return SendInput((uint)inputs.Length, pointer, sizeof(Input)) == inputs.Length;
        }
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static unsafe partial uint SendInput(uint count, Input* inputs, int size);

    private static Input Key(ushort virtualKey, uint flags) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion
        {
            Keyboard = new KeyboardInput { VirtualKey = virtualKey, Flags = flags, ExtraInfo = HookEventReducer.InjectedTag },
        },
    };
}
