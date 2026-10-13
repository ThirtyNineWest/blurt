using Blurt.Engine.Windows.Hotkey.Interop;

namespace Blurt.Engine.Windows.Hotkey;

/// <summary>
/// A <c>WH_MOUSE_LL</c> hook that raises <see cref="RawMouse"/> on its hook thread for every button
/// press and nothing else. Swallows nothing.
/// </summary>
public sealed class LowLevelMouseHook() : LowLevelHook(User32Hook.WhMouseLl, "Blurt mouse hook")
{
    private const int WmLButtonDown = 0x0201;
    private const int WmRButtonDown = 0x0204;
    private const int WmMButtonDown = 0x0207;
    private const int WmXButtonDown = 0x020B;

    /// <summary>
    /// Raised on the hook thread with <c>(message, flags, extraInfo)</c> for each left, right, middle, or
    /// extra button-down. <c>flags</c> carries <c>LLMHF_INJECTED</c> (<c>0x01</c>) for synthetic input.
    /// Handlers run inside the system's low-level hook timeout and must not block; an exception a
    /// handler throws is logged and does not reach the hook chain.
    /// </summary>
    public event Action<int, uint, nuint>? RawMouse;

    /// <inheritdoc />
    protected override unsafe void OnHook(nint wParam, nint lParam)
    {
        var message = (int)wParam;
        if (message is not (WmLButtonDown or WmRButtonDown or WmMButtonDown or WmXButtonDown))
        {
            return;
        }

        var data = (User32Hook.MsLlHookStruct*)lParam;
        RawMouse?.Invoke(message, data->Flags, data->ExtraInfo);
    }
}
