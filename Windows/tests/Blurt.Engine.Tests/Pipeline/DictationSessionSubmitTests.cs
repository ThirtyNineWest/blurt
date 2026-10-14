using Blurt.Engine.Pipeline;
using Blurt.Engine.Tests.Stubs;
using FluentAssertions;

namespace Blurt.Engine.Tests.Pipeline;

public sealed class DictationSessionSubmitTests
{
    [Test]
    public async Task SubmitRunsPressThenReleaseInEmitOrderThroughTheFullPipeline()
    {
        await using var harness = new SessionHarness();
        var stream = harness.Session.PhaseStream(CancellationToken.None);

        harness.Session.Submit(Command.Press);
        harness.Session.Submit(Command.Release);
        var seen = await stream.CollectUntilAsync(p => p.IsTerminal && p != PipelinePhase.Idle);

        seen.Should().Contain(PipelinePhase.Recording);
        seen[^1].Should().Be(PipelinePhase.Pasted);
        harness.Injector.Inserted.Should().Equal("Hello world.");
    }

    [Test]
    public async Task ACancelSubmittedAfterAPressFromTheSameThreadNeverStrandsARecording()
    {
        await using var harness = SessionHarness.WithTranscript("never");
        var stream = harness.Session.PhaseStream(CancellationToken.None);

        harness.Session.Submit(Command.Press);
        harness.Session.Submit(Command.Cancel);
        await stream.CollectUntilAsync(p => p == PipelinePhase.Cancelled);

        harness.Session.Phase.Should().Be(PipelinePhase.Cancelled);
        harness.Mic.StopCalls.Should().Be(1);
        harness.Injector.Inserted.Should().BeEmpty();
    }

    [Test]
    public async Task SubmitCancelRecordingTearsDownALiveRecording()
    {
        await using var harness = SessionHarness.WithTranscript("never");
        var stream = harness.Session.PhaseStream(CancellationToken.None);

        harness.Session.Submit(Command.Press);
        harness.Session.Submit(Command.CancelRecording);
        await stream.CollectUntilAsync(p => p == PipelinePhase.Cancelled);

        harness.Session.Phase.Should().Be(PipelinePhase.Cancelled);
        harness.Injector.Inserted.Should().BeEmpty();
    }

    [Test]
    public async Task ASubmittedCancelDoesNotPoisonTheNextPress()
    {
        await using var harness = new SessionHarness();
        var stream = harness.Session.PhaseStream(CancellationToken.None);
        var cancelled = false;

        harness.Session.Submit(Command.Press);
        harness.Session.Submit(Command.Cancel);
        harness.Session.Submit(Command.Press);
        var seen = await stream.CollectUntilAsync(p =>
        {
            cancelled |= p == PipelinePhase.Cancelled;
            return cancelled && p == PipelinePhase.Recording;
        });

        seen[^2].Should().Be(PipelinePhase.Connecting);
        seen[^1].Should().Be(PipelinePhase.Recording);
        harness.Mic.StartCalls.Should().Be(2);
    }
}
