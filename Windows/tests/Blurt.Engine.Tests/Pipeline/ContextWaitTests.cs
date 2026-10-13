using Blurt.Engine.Pipeline;
using Blurt.Engine.STT;
using Blurt.Engine.Tests.Stubs;
using FluentAssertions;

namespace Blurt.Engine.Tests.Pipeline;

public sealed class ContextWaitTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(500);

    private static readonly TranscriptionContext Context = new("Mail", null, null, "Hi team,", null, [], [], [], false);

    private static readonly TranscriptionContext PressKnown = new(null, null, null, null, null, ["earlier"], ["Blurt"], [], false);

    [Test]
    public async Task AContextThatResolvedDuringSpeechIsReturnedWithoutWaiting()
    {
        var clock = new TestClock();
        var press = new PressContext(PressKnown);
        press.Store(Context);

        var got = await press.WaitAsync(Budget, clock, CancellationToken.None).WaitAsync(SessionWaits.Budget);

        got.Should().Be(Context);
        clock.HasArmedTimer(Budget).Should().BeFalse();
    }

    [Test]
    public async Task AnEmptyReadYieldsNullPromptlyNotATimeoutWait()
    {
        var press = new PressContext(PressKnown);
        var waiting = press.WaitAsync(Budget, new TestClock(), CancellationToken.None);

        press.Store(null);
        var got = await waiting.WaitAsync(SessionWaits.Budget);

        got.Should().BeNull();
    }

    [Test]
    public async Task AReadLandingWithinTheBudgetIsReturned()
    {
        var clock = new TestClock();
        var press = new PressContext(null);
        var waiting = press.WaitAsync(Budget, clock, CancellationToken.None);
        await clock.WaitUntilSleepingAsync(Budget);

        press.Store(Context);
        var got = await waiting.WaitAsync(SessionWaits.Budget);

        got.Should().Be(Context);
        clock.HasArmedTimer(Budget).Should().BeFalse();
    }

    [Test]
    public async Task AHungReadIsAbandonedOnceTheBudgetElapses()
    {
        var clock = new TestClock();
        var press = new PressContext(PressKnown);
        var waiting = press.WaitAsync(Budget, clock, CancellationToken.None);
        await clock.WaitUntilSleepingAsync(Budget);

        clock.Advance(Budget);
        var got = await waiting.WaitAsync(SessionWaits.Budget);

        got.Should().BeNull();
        press.PressKnown.Should().Be(PressKnown);
    }

    [Test]
    public async Task AReadThatMissedTheBudgetIsStillResolvedLater()
    {
        var clock = new TestClock();
        var press = new PressContext(PressKnown);
        var waiting = press.WaitAsync(Budget, clock, CancellationToken.None);
        await clock.WaitUntilSleepingAsync(Budget);
        clock.Advance(Budget);
        var missed = await waiting.WaitAsync(SessionWaits.Budget);
        var before = press.Resolved;

        press.Store(Context);

        missed.Should().BeNull();
        before.Should().BeNull();
        press.Resolved.Should().Be(Context);
    }

    [Test]
    public async Task ASecondWaiterGetsNullAndReadsResolvedInstead()
    {
        var press = new PressContext(null);
        press.Store(Context);

        var first = await press.WaitAsync(Budget, new TestClock(), CancellationToken.None);
        var second = await press.WaitAsync(Budget, new TestClock(), CancellationToken.None);

        first.Should().Be(Context);
        second.Should().BeNull();
        press.Resolved.Should().Be(Context);
    }

    [Test]
    public async Task ACancelledWaitReturnsNull()
    {
        using var cts = new CancellationTokenSource();
        var press = new PressContext(null);
        var waiting = press.WaitAsync(Budget, new TestClock(), cts.Token);

        await cts.CancelAsync();
        var got = await waiting.WaitAsync(SessionWaits.Budget);

        got.Should().BeNull();
    }
}
