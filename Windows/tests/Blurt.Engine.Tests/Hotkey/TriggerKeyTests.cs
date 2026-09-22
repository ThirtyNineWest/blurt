using Blurt.Engine.Hotkey;
using FluentAssertions;

namespace Blurt.Engine.Tests.Hotkey;

public sealed class TriggerKeyTests
{
    [Test]
    public void KeyCodeMatchesTheWindowsVirtualKeyCode()
    {
        TriggerKey.RightControl.KeyCode().Should().Be(0xA3);
        TriggerKey.RightAlt.KeyCode().Should().Be(0xA5);
        TriggerKey.RightShift.KeyCode().Should().Be(0xA1);
        TriggerKey.RightWindows.KeyCode().Should().Be(0x5C);
    }

    [Test]
    public void EveryCaseHasANonEmptyLabel()
    {
        foreach (var key in TriggerKeys.All)
        {
            key.Label().Should().NotBeNullOrEmpty();
        }

        TriggerKey.RightControl.Label().Should().Be("right Ctrl");
        TriggerKey.RightAlt.Label().Should().Be("right Alt");
        TriggerKey.RightShift.Label().Should().Be("right Shift");
        TriggerKey.RightWindows.Label().Should().Be("right Win");
    }

    [Test]
    public void EveryCaseHasASpelledOutFullName()
    {
        TriggerKey.RightControl.FullName().Should().Be("Right Control (Ctrl)");
        TriggerKey.RightAlt.FullName().Should().Be("Right Alt");
        TriggerKey.RightShift.FullName().Should().Be("Right Shift");
        TriggerKey.RightWindows.FullName().Should().Be("Right Windows (Win)");
    }

    [Test]
    public void FromPersistedRoundTripsEveryCuratedCode()
    {
        TriggerKeys.FromPersisted(0xA3).Should().Be(TriggerKey.RightControl);
        TriggerKeys.FromPersisted(0xA5).Should().Be(TriggerKey.RightAlt);
        TriggerKeys.FromPersisted(0xA1).Should().Be(TriggerKey.RightShift);
        TriggerKeys.FromPersisted(0x5C).Should().Be(TriggerKey.RightWindows);
    }

    [Test]
    public void AnUncuratedPersistedCodeFallsBackToRightControl()
    {
        TriggerKeys.FromPersisted(999).Should().Be(TriggerKey.RightControl);
    }

    [Test]
    public void AnUnsetPersistedCodeOfZeroFallsBackToRightControl()
    {
        TriggerKeys.FromPersisted(0).Should().Be(TriggerKey.RightControl);
    }

    [Test]
    public void AllListsEveryCuratedKeyExactlyOnce()
    {
        TriggerKeys.All.Should().Equal(
            TriggerKey.RightControl, TriggerKey.RightAlt, TriggerKey.RightShift, TriggerKey.RightWindows);

        TriggerKeys.All.Select(key => key.KeyCode()).Should().OnlyHaveUniqueItems();
    }
}
