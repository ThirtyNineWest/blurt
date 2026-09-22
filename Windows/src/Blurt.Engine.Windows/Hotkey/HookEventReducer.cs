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
    /// Returns a <see cref="DictationKeyRouter.Event.FlagsChanged"/> for a down or up of the trigger,
    /// a <see cref="DictationKeyRouter.Event.KeyDown"/> for a down of any other key, and null for an
    /// event carrying <see cref="InjectedTag"/>, a key-up of another key, or any other message.
    /// </summary>
    public static DictationKeyRouter.Event? Reduce(
        int message, int vkCode, uint flags, nuint extraInfo, int triggerKeyCode)
    {
        if (extraInfo == InjectedTag)
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
