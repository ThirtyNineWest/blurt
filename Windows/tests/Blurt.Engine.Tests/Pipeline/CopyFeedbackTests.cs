using Blurt.Engine.Pipeline;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace Blurt.Engine.Tests.Pipeline;

public sealed class CopyFeedbackTests
{
    private static readonly TimeSpan JustUnder = CopyFeedback.CopiedDwell - TimeSpan.FromMilliseconds(1);

    [Test]
    public void StartsShowingNothing()
    {
        using var feedback = new CopyFeedback(new FakeTimeProvider());

        feedback.State.Should().Be(CopyFeedbackState.None);
    }

    [Test]
    public void TheDwellIsOneAndAHalfSeconds()
    {
        CopyFeedback.CopiedDwell.Should().Be(TimeSpan.FromSeconds(1.5));
    }

    [Test]
    public void ACopyShowsCopiedForTheDwellThenClears()
    {
        var clock = new FakeTimeProvider();
        using var feedback = new CopyFeedback(clock);

        feedback.Report(copied: true);
        var during = feedback.State;
        clock.Advance(JustUnder);
        var justBefore = feedback.State;
        clock.Advance(TimeSpan.FromMilliseconds(1));

        during.Should().Be(CopyFeedbackState.Copied);
        justBefore.Should().Be(CopyFeedbackState.Copied);
        feedback.State.Should().Be(CopyFeedbackState.None);
    }

    [Test]
    public void AFailedCopyShowsFailedForTheSameDwell()
    {
        var clock = new FakeTimeProvider();
        using var feedback = new CopyFeedback(clock);

        feedback.Report(copied: false);
        var during = feedback.State;
        clock.Advance(CopyFeedback.CopiedDwell);

        during.Should().Be(CopyFeedbackState.Failed);
        feedback.State.Should().Be(CopyFeedbackState.None);
    }

    [Test]
    public void ARepeatCopyRestartsTheDwell()
    {
        var clock = new FakeTimeProvider();
        using var feedback = new CopyFeedback(clock);

        feedback.Report(copied: true);
        clock.Advance(TimeSpan.FromSeconds(1));
        feedback.Report(copied: true);
        clock.Advance(TimeSpan.FromSeconds(1));
        var pastTheFirstDwell = feedback.State;
        clock.Advance(TimeSpan.FromSeconds(0.5));

        pastTheFirstDwell.Should().Be(CopyFeedbackState.Copied);
        feedback.State.Should().Be(CopyFeedbackState.None);
    }

    [Test]
    public void ACopyAfterAFailureReplacesIt()
    {
        var clock = new FakeTimeProvider();
        using var feedback = new CopyFeedback(clock);

        feedback.Report(copied: false);
        feedback.Report(copied: true);

        feedback.State.Should().Be(CopyFeedbackState.Copied);
    }

    [Test]
    public void RaisesEachChangeInOrder()
    {
        var clock = new FakeTimeProvider();
        using var feedback = new CopyFeedback(clock);
        var seen = new List<CopyFeedbackState>();
        feedback.Changed += seen.Add;

        feedback.Report(copied: true);
        clock.Advance(TimeSpan.FromSeconds(1));
        feedback.Report(copied: false);
        clock.Advance(CopyFeedback.CopiedDwell);

        seen.Should().Equal(CopyFeedbackState.Copied, CopyFeedbackState.Failed, CopyFeedbackState.None);
    }

    [Test]
    public void DisposingStopsThePendingClear()
    {
        var clock = new FakeTimeProvider();
        var feedback = new CopyFeedback(clock);
        var seen = new List<CopyFeedbackState>();
        feedback.Changed += seen.Add;

        feedback.Report(copied: true);
        feedback.Dispose();
        clock.Advance(CopyFeedback.CopiedDwell);
        feedback.Report(copied: true);

        seen.Should().Equal(CopyFeedbackState.Copied);
    }

    [TestCase(CopyFeedbackState.Copied, "Copied")]
    [TestCase(CopyFeedbackState.Failed, "Couldn't copy")]
    [TestCase(CopyFeedbackState.None, null)]
    public void LabelsEachState(CopyFeedbackState state, string? expected)
    {
        CopyFeedback.Label(state).Should().Be(expected);
    }
}
