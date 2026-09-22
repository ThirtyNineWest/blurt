using Blurt.Engine.Pipeline;
using Blurt.Engine.Tests.Stubs;
using FluentAssertions;

namespace Blurt.Engine.Tests.Pipeline;

public sealed class DictationSessionBringUpTests
{
    [Test]
    public async Task ACancelLandingDuringTheBringUpNeverReachesRecording()
    {
        var mic = new GatedStartMic();
        await using var harness = new SessionHarness(mic: mic);
        var stream = harness.Session.PhaseStream(CancellationToken.None);
        var pressed = harness.Session.PressAsync();
        await mic.WaitUntilStartEnteredAsync();

        var cancelled = harness.Session.CancelAsync();
        mic.AllowStartToFinish();
        await pressed;
        await cancelled;
        var seen = await stream.CollectUntilAsync(p => p.IsTerminal && p != PipelinePhase.Idle);

        seen.Should().Equal(PipelinePhase.Idle, PipelinePhase.Connecting, PipelinePhase.Cancelled);
        seen.Should().NotContain(PipelinePhase.Recording);
        mic.CancelCaptureCalls.Should().Be(1);
    }

    [Test]
    public async Task ASubmittedCancelPreemptsTheBringUpInsteadOfQueueingBehindIt()
    {
        var mic = new GatedStartMic();
        await using var harness = new SessionHarness(mic: mic);
        var stream = harness.Session.PhaseStream(CancellationToken.None);
        harness.Session.Submit(Command.Press);
        await mic.WaitUntilStartEnteredAsync();

        harness.Session.Submit(Command.Cancel);
        var preempted = mic.ReceivedToken.IsCancellationRequested;
        mic.AllowStartToFinish();
        var seen = await stream.CollectUntilAsync(p => p.IsTerminal && p != PipelinePhase.Idle);

        preempted.Should().BeTrue();
        seen[^1].Should().Be(PipelinePhase.Cancelled);
        seen.Should().NotContain(PipelinePhase.Recording);
        mic.CancelCaptureCalls.Should().Be(1);
    }

    [Test]
    public async Task ASubmittedCancelUnblocksABringUpThatWouldOtherwiseNeverReturn()
    {
        var mic = new GatedStartMic { ThrowsIfCancelled = true };
        await using var harness = new SessionHarness(mic: mic);
        var stream = harness.Session.PhaseStream(CancellationToken.None);
        harness.Session.Submit(Command.Press);
        await mic.WaitUntilStartEnteredAsync();

        harness.Session.Submit(Command.Cancel);
        var seen = await stream.CollectUntilAsync(p => p.IsTerminal && p != PipelinePhase.Idle);

        seen[^1].Should().Be(PipelinePhase.Cancelled);
        seen.Should().NotContain(PipelinePhase.Recording);
    }

    [Test]
    public async Task ACancelThatAbortsTheBringUpDoesNotLeakIntoTheNextPress()
    {
        var mic = new GatedStartMic { ThrowsIfCancelled = true };
        await using var harness = new SessionHarness(mic: mic);
        var pressed = harness.Session.PressAsync();
        await mic.WaitUntilStartEnteredAsync();
        await harness.Session.CancelAsync();
        mic.AllowStartToFinish();
        await pressed;
        var afterCancel = harness.Session.Phase;

        await harness.Session.PressAsync();

        afterCancel.Should().Be(PipelinePhase.Cancelled);
        harness.Session.Phase.Should().Be(PipelinePhase.Recording);
    }
}
