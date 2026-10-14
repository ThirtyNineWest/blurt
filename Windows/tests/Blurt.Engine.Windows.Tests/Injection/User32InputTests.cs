using System.Runtime.InteropServices;
using Blurt.Engine.Windows.Hotkey;
using Blurt.Engine.Windows.Injection.Interop;
using FluentAssertions;

namespace Blurt.Engine.Windows.Tests.Injection;

public sealed class User32InputTests
{
    [Test]
    public void CtrlVIsControlDownVDownVUpControlUp()
    {
        var inputs = User32Input.CtrlVInputs();

        inputs.Select(i => i.Type).Should().AllBeEquivalentTo(User32Input.InputKeyboard);
        inputs.Select(i => i.Union.Keyboard.VirtualKey).Should().Equal(0x11, 0x56, 0x56, 0x11);
        inputs.Select(i => i.Union.Keyboard.Flags).Should().Equal(0u, 0u, User32Input.KeyEventKeyUp, User32Input.KeyEventKeyUp);
    }

    [Test]
    public void EveryPasteEventCarriesTheTagTheKeyboardHookIgnores()
    {
        var inputs = User32Input.CtrlVInputs();

        inputs.Select(i => i.Union.Keyboard.ExtraInfo).Should().AllBeEquivalentTo(HookEventReducer.InjectedTag);
    }

    [Test]
    public void CtrlCIsControlDownCDownCUpControlUp()
    {
        var inputs = User32Input.CtrlCInputs();

        inputs.Select(i => i.Type).Should().AllBeEquivalentTo(User32Input.InputKeyboard);
        inputs.Select(i => i.Union.Keyboard.VirtualKey).Should().Equal(0x11, 0x43, 0x43, 0x11);
        inputs.Select(i => i.Union.Keyboard.Flags).Should().Equal(0u, 0u, User32Input.KeyEventKeyUp, User32Input.KeyEventKeyUp);
    }

    [Test]
    public void EveryCopyEventCarriesTheTagTheKeyboardHookIgnores()
    {
        var inputs = User32Input.CtrlCInputs();

        inputs.Select(i => i.Union.Keyboard.ExtraInfo).Should().AllBeEquivalentTo(HookEventReducer.InjectedTag);
    }

    [Test]
    public void TheInputStructMatchesTheNativeX64Layout() =>
        Marshal.SizeOf<User32Input.Input>().Should().Be(40);
}
