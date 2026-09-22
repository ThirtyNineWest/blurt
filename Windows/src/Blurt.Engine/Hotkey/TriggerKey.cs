namespace Blurt.Engine.Hotkey;

/// <summary>
/// A lone momentary modifier key usable as the single dictation trigger. The value is the Windows
/// virtual-key code, so a persisted keycode decodes directly through <see cref="TriggerKeys.FromPersisted(int)"/>.
/// Curated to right-side modifiers, which are rarely bound in app shortcuts, so a solo press maps
/// cleanly to "dictate" and is distinct from its left-side counterpart's own VK code.
/// </summary>
public enum TriggerKey
{
    /// <summary>VK_RCONTROL.</summary>
    RightControl = 0xA3,

    /// <summary>VK_RMENU (right Alt).</summary>
    RightAlt = 0xA5,

    /// <summary>VK_RSHIFT.</summary>
    RightShift = 0xA1,

    /// <summary>VK_RWIN.</summary>
    RightWindows = 0x5C,
}

/// <summary>Decoding, presentation, and enumeration for <see cref="TriggerKey"/>.</summary>
public static class TriggerKeys
{
    /// <summary>Every curated trigger key, in declaration order.</summary>
    public static IReadOnlyList<TriggerKey> All { get; } =
        [TriggerKey.RightControl, TriggerKey.RightAlt, TriggerKey.RightShift, TriggerKey.RightWindows];

    /// <summary>Decodes a persisted keycode, falling back to right Control when the value isn't one of the curated options.</summary>
    public static TriggerKey FromPersisted(int code)
    {
        var candidate = (TriggerKey)code;
        return Enum.IsDefined(candidate) ? candidate : TriggerKey.RightControl;
    }

    /// <summary>The virtual-key code this case wraps.</summary>
    public static int KeyCode(this TriggerKey key) => (int)key;

    /// <summary>Inline sentence form, e.g. "Tap or hold right Ctrl to dictate".</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="key"/> is not a defined value.</exception>
    public static string Label(this TriggerKey key) => key switch
    {
        TriggerKey.RightControl => "right Ctrl",
        TriggerKey.RightAlt => "right Alt",
        TriggerKey.RightShift => "right Shift",
        TriggerKey.RightWindows => "right Win",
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };

    /// <summary>Spelled-out form, e.g. "Right Control (Ctrl)" — the abbreviation in parentheses names the key cap.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="key"/> is not a defined value.</exception>
    public static string FullName(this TriggerKey key) => key switch
    {
        TriggerKey.RightControl => "Right Control (Ctrl)",
        TriggerKey.RightAlt => "Right Alt",
        TriggerKey.RightShift => "Right Shift",
        TriggerKey.RightWindows => "Right Windows (Win)",
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };
}
