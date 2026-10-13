using Blurt.Engine.Windows.Hotkey;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace Blurt.Engine.Windows.Tests.Hotkey;

public sealed class UserInputClockTests
{
    [Test]
    public void BeforeAnyInputTheElapsedTimeIsInfinite()
    {
        new UserInputClock(new FakeTimeProvider()).TimeSinceInput.Should().Be(TimeSpan.MaxValue);
    }

    [Test]
    public void ATouchStartsTheClockAndLaterTouchesRestartIt()
    {
        var time = new FakeTimeProvider();
        var clock = new UserInputClock(time);

        clock.Touch();
        time.Advance(TimeSpan.FromSeconds(3));
        var afterFirst = clock.TimeSinceInput;
        clock.Touch();
        var afterSecond = clock.TimeSinceInput;

        afterFirst.Should().Be(TimeSpan.FromSeconds(3));
        afterSecond.Should().Be(TimeSpan.Zero);
    }
}
