using Blurt.Engine.Windows.Hotkey.Interop;

namespace Blurt.Engine.Windows.Hotkey;

/// <summary>
/// A <c>WH_KEYBOARD_LL</c> hook that raises <see cref="RawKey"/> on its hook thread for every
/// keyboard event. Swallows nothing.
/// </summary>
public sealed class LowLevelKeyboardHook() : LowLevelHook(User32Hook.WhKeyboardLl, "Blurt keyboard hook")
{
    /// <summary>
    /// Raised on the hook thread with <c>(message, vkCode, scanCode, flags, extraInfo)</c> for every keyboard
    /// event. Handlers run inside the system's low-level hook timeout and must not block; an
    /// exception a handler throws is logged and does not reach the hook chain.
    /// </summary>
    public event Action<int, int, int, uint, nuint>? RawKey;

    /// <inheritdoc />
    protected override unsafe void OnHook(nint wParam, nint lParam)
    {
        var data = (User32Hook.KbdLlHookStruct*)lParam;
        RawKey?.Invoke((int)wParam, (int)data->VkCode, (int)data->ScanCode, data->Flags, data->ExtraInfo);
    }
}
