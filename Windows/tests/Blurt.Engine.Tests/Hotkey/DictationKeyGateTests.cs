using Blurt.Engine.Hotkey;
using FluentAssertions;

namespace Blurt.Engine.Tests.Hotkey;

public sealed class DictationKeyGateTests
{
    public enum EventKind
    {
        Down,
        Up,
        Other,
        Reset,
    }

    /// <summary>One driver event plus the action it must return; null skips the assertion, and a reset never asserts.</summary>
    public readonly record struct Step(EventKind Kind, TimeSpan At, DictationKeyGate.Action? Expect);

    private static Step Down(TimeSpan at, DictationKeyGate.Action? expect = null) => new(EventKind.Down, at, expect);

    private static Step Up(TimeSpan at, DictationKeyGate.Action? expect = null) => new(EventKind.Up, at, expect);

    private static Step Other(DictationKeyGate.Action? expect = null) => new(EventKind.Other, TimeSpan.Zero, expect);

    private static Step ResetStep() => new(EventKind.Reset, TimeSpan.Zero, null);

    private static IEnumerable<TestCaseData> Scenarios()
    {
        yield return new TestCaseData(
            new[] { Down(TimeSpan.Zero, DictationKeyGate.Action.Start) },
            TriggerActivation.TapOrHold).SetName("modifier down from idle starts recording");

        yield return new TestCaseData(
            new[]
            {
                Down(TimeSpan.Zero),
                Up(TimeSpan.FromMilliseconds(200), DictationKeyGate.Action.Latch),
                Down(TimeSpan.FromSeconds(5)),
                Up(TimeSpan.FromMilliseconds(5100), DictationKeyGate.Action.Stop),
            },
            TriggerActivation.TapOrHold).SetName("quick tap latches on — release does not stop (a later solo tap does)");

        yield return new TestCaseData(
            new[]
            {
                Down(TimeSpan.Zero),
                Up(TimeSpan.FromMilliseconds(200)),
                Down(TimeSpan.FromSeconds(5), DictationKeyGate.Action.None),
                Up(TimeSpan.FromMilliseconds(5100), DictationKeyGate.Action.Stop),
            },
            TriggerActivation.TapOrHold).SetName("a second tap while latched stops recording");

        yield return new TestCaseData(
            new[]
            {
                Down(TimeSpan.Zero),
                Up(TimeSpan.FromMilliseconds(1200), DictationKeyGate.Action.Stop),
            },
            TriggerActivation.TapOrHold).SetName("hold past threshold then release stops (push-to-talk)");

        yield return new TestCaseData(
            new[]
            {
                Down(TimeSpan.Zero),
                Up(TimeSpan.FromMilliseconds(1200)),
                Down(TimeSpan.FromSeconds(2), DictationKeyGate.Action.Start),
            },
            TriggerActivation.TapOrHold).SetName("after a hold stops, the next press starts fresh");

        yield return new TestCaseData(
            new[]
            {
                Down(TimeSpan.Zero),
                Up(TimeSpan.FromSeconds(1), DictationKeyGate.Action.Stop),
            },
            TriggerActivation.TapOrHold).SetName("release exactly at threshold counts as a hold (stop)");

        yield return new TestCaseData(
            new[]
            {
                Down(TimeSpan.Zero),
                Up(TimeSpan.FromMilliseconds(999), DictationKeyGate.Action.Latch),
                Down(TimeSpan.FromSeconds(5)),
                Up(TimeSpan.FromMilliseconds(5100), DictationKeyGate.Action.Stop),
            },
            TriggerActivation.TapOrHold).SetName("release just under threshold is a tap (latch, not stop)");

        yield return new TestCaseData(
            new[]
            {
                Down(TimeSpan.Zero),
                Other(DictationKeyGate.Action.Cancel),
                Up(TimeSpan.FromMilliseconds(100), DictationKeyGate.Action.None),
                Down(TimeSpan.FromSeconds(2), DictationKeyGate.Action.Start),
            },
            TriggerActivation.TapOrHold).SetName("a combo from idle cancels the just-started capture");

        yield return new TestCaseData(
            new[]
            {
                Down(TimeSpan.Zero),
                Up(TimeSpan.FromMilliseconds(200)),
                Down(TimeSpan.FromSeconds(5)),
                Other(DictationKeyGate.Action.None),
                Up(TimeSpan.FromMilliseconds(5100), DictationKeyGate.Action.None),
                Down(TimeSpan.FromSeconds(10)),
                Up(TimeSpan.FromMilliseconds(10100), DictationKeyGate.Action.Stop),
            },
            TriggerActivation.TapOrHold).SetName("a combo while latched keeps recording (does not cancel)");

        yield return new TestCaseData(
            new[]
            {
                Other(DictationKeyGate.Action.None),
                Down(TimeSpan.FromSeconds(1), DictationKeyGate.Action.Start),
            },
            TriggerActivation.TapOrHold).SetName("otherKeyDown while idle is ignored");

        yield return new TestCaseData(
            new[] { Up(TimeSpan.FromSeconds(1), DictationKeyGate.Action.None) },
            TriggerActivation.TapOrHold).SetName("modifier up without a prior down is ignored");

        yield return new TestCaseData(
            new[]
            {
                Down(TimeSpan.Zero),
                ResetStep(),
                Down(TimeSpan.FromSeconds(2), DictationKeyGate.Action.Start),
            },
            TriggerActivation.TapOrHold).SetName("reset clears state so the next press starts fresh");

        yield return new TestCaseData(
            new[]
            {
                Down(TimeSpan.Zero, DictationKeyGate.Action.Start),
                Down(TimeSpan.FromMilliseconds(100), DictationKeyGate.Action.None),
                Up(TimeSpan.FromMilliseconds(1200), DictationKeyGate.Action.Stop),
            },
            TriggerActivation.TapOrHold).SetName("a repeated modifier down while armed is ignored (no double start)");

        yield return new TestCaseData(
            new[]
            {
                Down(TimeSpan.Zero, DictationKeyGate.Action.Start),
                Up(TimeSpan.FromMilliseconds(80)),
                Down(TimeSpan.FromMilliseconds(200), DictationKeyGate.Action.None),
                Up(TimeSpan.FromMilliseconds(280), DictationKeyGate.Action.Stop),
                Down(TimeSpan.FromSeconds(5), DictationKeyGate.Action.Start),
            },
            TriggerActivation.TapOrHold).SetName("a rapid second tap while latched still stops (no multi-tap gesture)");

        yield return new TestCaseData(
            new[]
            {
                Down(TimeSpan.Zero, DictationKeyGate.Action.Start),
                Up(TimeSpan.FromMilliseconds(1200), DictationKeyGate.Action.Latch),
                Down(TimeSpan.FromSeconds(5), DictationKeyGate.Action.None),
                Up(TimeSpan.FromMilliseconds(5100), DictationKeyGate.Action.Stop),
            },
            TriggerActivation.Tap).SetName("tap mode: a release past the hold threshold still latches (no push-to-talk)");

        yield return new TestCaseData(
            new[]
            {
                Down(TimeSpan.Zero, DictationKeyGate.Action.Start),
                Up(TimeSpan.FromMilliseconds(200), DictationKeyGate.Action.Latch),
                Down(TimeSpan.FromSeconds(2), DictationKeyGate.Action.None),
                Up(TimeSpan.FromMilliseconds(2100), DictationKeyGate.Action.Stop),
            },
            TriggerActivation.Tap).SetName("tap mode: a quick tap latches and the next tap stops, as in tap-or-hold");

        yield return new TestCaseData(
            new[]
            {
                Down(TimeSpan.Zero, DictationKeyGate.Action.Start),
                Up(TimeSpan.FromMilliseconds(200), DictationKeyGate.Action.Stop),
                Down(TimeSpan.FromSeconds(2), DictationKeyGate.Action.Start),
                Up(TimeSpan.FromMilliseconds(3500), DictationKeyGate.Action.Stop),
            },
            TriggerActivation.Hold).SetName("hold mode: a release under the threshold stops instead of latching");

        yield return new TestCaseData(
            new[]
            {
                Down(TimeSpan.Zero, DictationKeyGate.Action.Start),
                Other(DictationKeyGate.Action.Cancel),
                Up(TimeSpan.FromMilliseconds(100), DictationKeyGate.Action.None),
                Down(TimeSpan.FromSeconds(2), DictationKeyGate.Action.Start),
            },
            TriggerActivation.Hold).SetName("hold mode: a combo from idle still cancels the fresh capture");
    }

    [TestCaseSource(nameof(Scenarios))]
    public void TapHoldLatchComboStateMachine(Step[] steps, TriggerActivation activation)
    {
        var gate = new DictationKeyGate(TimeSpan.FromSeconds(1), activation);

        for (var i = 0; i < steps.Length; i++)
        {
            var step = steps[i];
            DictationKeyGate.Action? action;

            switch (step.Kind)
            {
                case EventKind.Down:
                    action = gate.ModifierDown(step.At);
                    break;
                case EventKind.Up:
                    action = gate.ModifierUp(step.At);
                    break;
                case EventKind.Other:
                    action = gate.OtherKeyDown();
                    break;
                case EventKind.Reset:
                    gate.Reset();
                    action = null;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(steps), step.Kind, null);
            }

            if (step.Expect is { } expected)
            {
                action.Should().Be(expected, $"step {i} ({step.Kind}) expected {expected}");
            }
        }
    }

    /// <summary>
    /// <see cref="DictationKeyRouter.Reset"/> reads <see cref="DictationKeyGate.IsIdle"/> to decide whether it
    /// just discarded a live recording the host must cancel upstream, so it has to be true exactly when no
    /// dictation is in flight.
    /// </summary>
    [Test]
    public void IsIdleTracksArmedAndLatchedRecordings()
    {
        var gate = new DictationKeyGate(TimeSpan.FromSeconds(1));

        gate.IsIdle.Should().BeTrue();

        gate.ModifierDown(TimeSpan.Zero); // armed (push-to-talk in progress)

        gate.IsIdle.Should().BeFalse();

        gate.ModifierUp(TimeSpan.FromMilliseconds(200)); // quick tap -> latched

        gate.IsIdle.Should().BeFalse();

        gate.Reset();

        gate.IsIdle.Should().BeTrue();

        gate.ModifierDown(TimeSpan.FromSeconds(2));
        gate.ModifierUp(TimeSpan.FromMilliseconds(3500)); // hold -> stop

        gate.IsIdle.Should().BeTrue();
    }
}
