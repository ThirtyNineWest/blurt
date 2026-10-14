using Blurt.Engine.Hotkey;
using FluentAssertions;

namespace Blurt.Engine.Tests.Hotkey;

public sealed class TriggerKeyTests
{
    [Test]
    public void KeyCodeMatchesTheWindowsVirtualKeyCode()
    {
        TriggerKey.RightControl.KeyCode().Should().Be(0xA3);
        TriggerKey.RightShift.KeyCode().Should().Be(0xA1);
    }

    [Test]
    public void EveryCaseHasANonEmptyLabel()
    {
        foreach (var key in TriggerKeys.All)
        {
            key.Label().Should().NotBeNullOrEmpty();
        }

        TriggerKey.RightControl.Label().Should().Be("right Ctrl");
        TriggerKey.RightShift.Label().Should().Be("right Shift");
    }

    [Test]
    public void EveryCaseHasASpelledOutFullName()
    {
        TriggerKey.RightControl.FullName().Should().Be("Right Control (Ctrl)");
        TriggerKey.RightShift.FullName().Should().Be("Right Shift");
    }

    [Test]
    public void FromPersistedRoundTripsEveryCuratedCode()
    {
        TriggerKeys.FromPersisted(0xA3).Should().Be(TriggerKey.RightControl);
        TriggerKeys.FromPersisted(0xA1).Should().Be(TriggerKey.RightShift);
    }

    [Test]
    public void AnUncuratedPersistedCodeFallsBackToRightControl()
    {
        TriggerKeys.FromPersisted(999).Should().Be(TriggerKey.RightControl);
    }

    [TestCase(0xA5)]
    [TestCase(0x5C)]
    public void APersistedRightAltOrRightWindowsCodeFallsBackToRightControl(int code)
    {
        TriggerKeys.FromPersisted(code).Should().Be(TriggerKey.RightControl);
    }

    [Test]
    public void AnUnsetPersistedCodeOfZeroFallsBackToRightControl()
    {
        TriggerKeys.FromPersisted(0).Should().Be(TriggerKey.RightControl);
    }

    [Test]
    public void AllListsEveryCuratedKeyExactlyOnce()
    {
        TriggerKeys.All.Should().Equal(TriggerKey.RightControl, TriggerKey.RightShift);

        TriggerKeys.All.Select(key => key.KeyCode()).Should().OnlyHaveUniqueItems();
    }
}
