using Blurt.Engine.Hotkey;
using Blurt.Engine.Windows.Hotkey;
using FluentAssertions;

namespace Blurt.Engine.Windows.Tests.Hotkey;

public sealed class HookEventReducerTests
{
    private const int RightControl = 0xA3;
    private const int RightAlt = 0xA5;
    private const int KeyC = 0x43;

    [Test]
    public void TheTriggerKeyDownIsAFlagsChangeWithTheFlagOn()
    {
        var reduced = HookEventReducer.Reduce(HookEventReducer.WmKeyDown, RightControl, 0, 0, RightControl);

        reduced.Should().Be(new DictationKeyRouter.Event.FlagsChanged(RightControl, true));
    }

    [Test]
    public void TheTriggerKeyUpIsAFlagsChangeWithTheFlagOff()
    {
        var reduced = HookEventReducer.Reduce(
            HookEventReducer.WmKeyUp, RightControl, HookEventReducer.LlkhfUp, 0, RightControl);

        reduced.Should().Be(new DictationKeyRouter.Event.FlagsChanged(RightControl, false));
    }

    [Test]
    public void RightAltDownArrivesAsASysKeyDownAndIsAFlagsChangeWithTheFlagOn()
    {
        var reduced = HookEventReducer.Reduce(HookEventReducer.WmSysKeyDown, RightAlt, 0x21, 0, RightAlt);

        reduced.Should().Be(new DictationKeyRouter.Event.FlagsChanged(RightAlt, true));
    }

    [Test]
    public void RightAltUpArrivesAsASysKeyUpAndIsAFlagsChangeWithTheFlagOff()
    {
        var reduced = HookEventReducer.Reduce(HookEventReducer.WmSysKeyUp, RightAlt, 0xA1, 0, RightAlt);

        reduced.Should().Be(new DictationKeyRouter.Event.FlagsChanged(RightAlt, false));
    }

    [Test]
    public void AnotherKeyDownIsAKeyDown()
    {
        var reduced = HookEventReducer.Reduce(HookEventReducer.WmKeyDown, KeyC, 0, 0, RightControl);

        reduced.Should().Be(new DictationKeyRouter.Event.KeyDown(KeyC));
    }

    [Test]
    public void AnotherKeyDownAsASysKeyDownIsAKeyDown()
    {
        var reduced = HookEventReducer.Reduce(HookEventReducer.WmSysKeyDown, KeyC, 0x20, 0, RightAlt);

        reduced.Should().Be(new DictationKeyRouter.Event.KeyDown(KeyC));
    }

    [TestCase(HookEventReducer.WmKeyUp)]
    [TestCase(HookEventReducer.WmSysKeyUp)]
    public void AnotherKeyUpIsIgnored(int message)
    {
        var reduced = HookEventReducer.Reduce(message, KeyC, HookEventReducer.LlkhfUp, 0, RightControl);

        reduced.Should().BeNull();
    }

    [TestCase(HookEventReducer.WmKeyDown, RightControl)]
    [TestCase(HookEventReducer.WmKeyUp, RightControl)]
    [TestCase(HookEventReducer.WmKeyDown, KeyC)]
    [TestCase(HookEventReducer.WmSysKeyDown, KeyC)]
    public void AnEventCarryingTheInjectedTagIsIgnored(int message, int vkCode)
    {
        var reduced = HookEventReducer.Reduce(
            message, vkCode, HookEventReducer.LlkhfInjected, HookEventReducer.InjectedTag, RightControl);

        reduced.Should().BeNull();
    }

    [Test]
    public void AnInjectedEventWithoutTheTagIsStillReduced()
    {
        var reduced = HookEventReducer.Reduce(
            HookEventReducer.WmKeyDown, KeyC, HookEventReducer.LlkhfInjected, 0x1234, RightControl);

        reduced.Should().Be(new DictationKeyRouter.Event.KeyDown(KeyC));
    }

    [Test]
    public void AnUnrelatedMessageIsIgnored()
    {
        var reduced = HookEventReducer.Reduce(0x0200, RightControl, 0, 0, RightControl);

        reduced.Should().BeNull();
    }

    [Test]
    public void TheInjectedTagSpellsBlrtInAscii()
    {
        HookEventReducer.InjectedTag.Should().Be((nuint)0x424C5254);
    }
}
