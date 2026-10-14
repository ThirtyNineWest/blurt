using Blurt.Engine.Hotkey;
using Blurt.Engine.Windows.Hotkey;
using FluentAssertions;

namespace Blurt.Engine.Windows.Tests.Hotkey;

public sealed class HookEventReducerTests
{
    private const int RightControl = 0xA3;
    private const int LeftControl = 0xA2;
    private const int RightAlt = 0xA5;
    private const int KeyC = 0x43;
    private const int ControlScanCode = 0x1D;
    private const int AltScanCode = 0x38;
    private const int KeyCScanCode = 0x2E;
    private const uint LlkhfExtended = 0x01;

    [Test]
    public void TheTriggerKeyDownIsAFlagsChangeWithTheFlagOn()
    {
        var reduced = HookEventReducer.Reduce(
            HookEventReducer.WmKeyDown, RightControl, ControlScanCode, LlkhfExtended, 0, RightControl);

        reduced.Should().Be(new DictationKeyRouter.Event.FlagsChanged(RightControl, true));
    }

    [Test]
    public void TheTriggerKeyUpIsAFlagsChangeWithTheFlagOff()
    {
        var reduced = HookEventReducer.Reduce(
            HookEventReducer.WmKeyUp,
            RightControl,
            ControlScanCode,
            LlkhfExtended | HookEventReducer.LlkhfUp,
            0,
            RightControl);

        reduced.Should().Be(new DictationKeyRouter.Event.FlagsChanged(RightControl, false));
    }

    [Test]
    public void RightAltDownArrivesAsASysKeyDownAndIsAFlagsChangeWithTheFlagOn()
    {
        var reduced = HookEventReducer.Reduce(HookEventReducer.WmSysKeyDown, RightAlt, AltScanCode, 0x21, 0, RightAlt);

        reduced.Should().Be(new DictationKeyRouter.Event.FlagsChanged(RightAlt, true));
    }

    [Test]
    public void RightAltUpArrivesAsASysKeyUpAndIsAFlagsChangeWithTheFlagOff()
    {
        var reduced = HookEventReducer.Reduce(HookEventReducer.WmSysKeyUp, RightAlt, AltScanCode, 0xA1, 0, RightAlt);

        reduced.Should().Be(new DictationKeyRouter.Event.FlagsChanged(RightAlt, false));
    }

    [Test]
    public void AnotherKeyDownIsAKeyDown()
    {
        var reduced = HookEventReducer.Reduce(HookEventReducer.WmKeyDown, KeyC, KeyCScanCode, 0, 0, RightControl);

        reduced.Should().Be(new DictationKeyRouter.Event.KeyDown(KeyC));
    }

    [Test]
    public void AnotherKeyDownAsASysKeyDownIsAKeyDown()
    {
        var reduced = HookEventReducer.Reduce(HookEventReducer.WmSysKeyDown, KeyC, KeyCScanCode, 0x20, 0, RightAlt);

        reduced.Should().Be(new DictationKeyRouter.Event.KeyDown(KeyC));
    }

    [TestCase(HookEventReducer.WmKeyUp)]
    [TestCase(HookEventReducer.WmSysKeyUp)]
    public void AnotherKeyUpIsIgnored(int message)
    {
        var reduced = HookEventReducer.Reduce(
            message, KeyC, KeyCScanCode, HookEventReducer.LlkhfUp, 0, RightControl);

        reduced.Should().BeNull();
    }

    [TestCase(HookEventReducer.WmKeyDown, RightControl)]
    [TestCase(HookEventReducer.WmKeyUp, RightControl)]
    [TestCase(HookEventReducer.WmKeyDown, KeyC)]
    [TestCase(HookEventReducer.WmSysKeyDown, KeyC)]
    public void AnEventCarryingTheInjectedTagIsIgnored(int message, int vkCode)
    {
        var reduced = HookEventReducer.Reduce(
            message, vkCode, 0, HookEventReducer.LlkhfInjected, HookEventReducer.InjectedTag, RightControl);

        reduced.Should().BeNull();
    }

    [Test]
    public void AnInjectedEventWithoutTheTagIsStillReduced()
    {
        var reduced = HookEventReducer.Reduce(
            HookEventReducer.WmKeyDown, KeyC, KeyCScanCode, HookEventReducer.LlkhfInjected, 0x1234, RightControl);

        reduced.Should().Be(new DictationKeyRouter.Event.KeyDown(KeyC));
    }

    [TestCase(HookEventReducer.WmKeyDown)]
    [TestCase(HookEventReducer.WmSysKeyDown)]
    public void AltGrsFakeLeftControlDownIsIgnored(int message)
    {
        var reduced = HookEventReducer.Reduce(
            message, LeftControl, HookEventReducer.AltGrFakeControlScanCode, 0x20, 0, RightAlt);

        reduced.Should().BeNull();
    }

    [Test]
    public void AGenuineLeftControlDownIsAKeyDown()
    {
        var reduced = HookEventReducer.Reduce(
            HookEventReducer.WmKeyDown, LeftControl, ControlScanCode, 0, 0, RightAlt);

        reduced.Should().Be(new DictationKeyRouter.Event.KeyDown(LeftControl));
    }

    [Test]
    public void AnUnrelatedMessageIsIgnored()
    {
        var reduced = HookEventReducer.Reduce(0x0200, RightControl, ControlScanCode, 0, 0, RightControl);

        reduced.Should().BeNull();
    }

    [Test]
    public void TheInjectedTagSpellsBlrtInAscii()
    {
        HookEventReducer.InjectedTag.Should().Be((nuint)0x424C5254);
    }

    [Test]
    public void TheFakeControlScanCodeIsControlWithTheFakeBit()
    {
        HookEventReducer.AltGrFakeControlScanCode.Should().Be(0x21D);
    }

    [TestCase(HookEventReducer.WmKeyDown, 0x41, 0u, true)]
    [TestCase(HookEventReducer.WmSysKeyDown, 0x41, 0u, true)]
    [TestCase(HookEventReducer.WmKeyUp, 0x41, 0u, false)]
    [TestCase(HookEventReducer.WmKeyDown, 0xA3, 0u, false)]
    public void HardwareKeyDownIsADownOfAnyKeyButTheTriggerThatBlurtDidNotInject(int message, int vk, uint unused, bool expected)
    {
        HookEventReducer.IsHardwareKeyDown(message, vk, 0, triggerKeyCode: 0xA3).Should().Be(expected);
    }

    [Test]
    public void AnInjectedKeyDownIsNotHardwareInput()
    {
        HookEventReducer.IsHardwareKeyDown(HookEventReducer.WmKeyDown, 0x56, HookEventReducer.InjectedTag, triggerKeyCode: 0xA3).Should().BeFalse();
    }
}
