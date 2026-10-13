using Blurt.Engine.Hotkey;

namespace Blurt.Engine.Windows.Hotkey;

/// <summary>Reduces one <c>WH_KEYBOARD_LL</c> delivery to the event shape <see cref="DictationKeyRouter"/> routes.</summary>
public static class HookEventReducer
{
    public const int WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105;

    public const uint LlkhfInjected = 0x10, LlkhfUp = 0x80;

    /// <summary>
    /// The <c>dwExtraInfo</c> value ("BLRT" in ASCII) Blurt's own <c>SendInput</c> events carry, so the
    /// hook never reads its own paste keystrokes as a combo.
    /// </summary>
    public const nuint InjectedTag = 0x424C5254;

    /// <summary>
    /// The scan code of the synthetic <c>VK_LCONTROL</c> down an AltGr layout emits with every Right
    /// Alt down, including auto-repeats: Control's <c>0x1D</c> with the <c>0x200</c> fake bit, which a
    /// physical key never carries (a physical key's extended bit travels in <c>flags</c>).
    /// </summary>
    public const int AltGrFakeControlScanCode = 0x21D;

    private const int VkLeftControl = 0xA2;

    /// <summary>
    /// Returns whether the delivery is a key-down of any key other than the trigger that Blurt did not
    /// inject, which is what counts as the user typing.
    /// </summary>
    public static bool IsHardwareKeyDown(int message, int vkCode, nuint extraInfo, int triggerKeyCode) =>
        extraInfo != InjectedTag && vkCode != triggerKeyCode && message is WmKeyDown or WmSysKeyDown;

    /// <summary>
    /// Returns a <see cref="DictationKeyRouter.Event.FlagsChanged"/> for a down or up of the trigger,
    /// a <see cref="DictationKeyRouter.Event.KeyDown"/> for a down of any other key, and null for an
    /// event carrying <see cref="InjectedTag"/>, AltGr's fake Left Control, a key-up of another key,
    /// or any other message.
    /// </summary>
    /// <remarks>
    /// The fake Left Control is recognized by the documented <see cref="AltGrFakeControlScanCode"/>
    /// signature; without that filter it reads as a combo and cancels every Right Alt hold. If that
    /// signature ever fails, the fallback signal is a Left Control down immediately preceding the
    /// Right Alt down with the same <c>time</c>.
    /// </remarks>
    public static DictationKeyRouter.Event? Reduce(
        int message, int vkCode, int scanCode, uint flags, nuint extraInfo, int triggerKeyCode)
    {
        if (extraInfo == InjectedTag)
        {
            return null;
        }

        if (vkCode == VkLeftControl && scanCode == AltGrFakeControlScanCode)
        {
            return null;
        }

        var isDown = message is WmKeyDown or WmSysKeyDown;
        var isUp = message is WmKeyUp or WmSysKeyUp;

        if (vkCode == triggerKeyCode)
        {
            return isDown || isUp ? new DictationKeyRouter.Event.FlagsChanged(vkCode, isDown) : null;
        }

        return isDown ? new DictationKeyRouter.Event.KeyDown(vkCode) : null;
    }
}
