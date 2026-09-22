using Blurt.Engine.Pipeline;
using Blurt.Engine.Tests.Stubs;
using FluentAssertions;

namespace Blurt.Engine.Tests.Pipeline;

public sealed class DictationSessionGuardTests
{
    [Test]
    public async Task PressWhileAlreadyRecordingIsASilentNoOp()
    {
        await using var harness = SessionHarness.WithTranscript("hi");

        await harness.Session.PressAsync();
        await harness.Session.PressAsync();

        harness.Mic.StartCalls.Should().Be(1);
        harness.Session.Phase.Should().Be(PipelinePhase.Recording);
    }

    [Test]
    public async Task ReleaseWithNothingRecordingIsASilentNoOp()
    {
        await using var harness = SessionHarness.WithTranscript("hi");

        await harness.Session.ReleaseAsync();

        harness.Session.Phase.Should().Be(PipelinePhase.Idle);
        harness.Mic.StopCalls.Should().Be(0);
    }

    [Test]
    public async Task CancelWithNothingRecordingIsASilentNoOp()
    {
        await using var harness = SessionHarness.WithTranscript("hi");

        await harness.Session.CancelAsync();

        harness.Session.Phase.Should().Be(PipelinePhase.Idle);
        harness.Mic.StopCalls.Should().Be(0);
    }

    [Test]
    public async Task TheKeyTermsProviderIsConsultedAfreshAtEachPress()
    {
        var reads = 0;
        await using var harness = new SessionHarness(keyTermsProvider: () =>
        {
            Interlocked.Increment(ref reads);
            return ["Blurt"];
        });

        await harness.Session.PressAsync();
        await harness.Session.CancelAsync();
        await harness.Session.PressAsync();
        await harness.Session.CancelAsync();

        Volatile.Read(ref reads).Should().Be(2);
    }

    [Test]
    public async Task EveryPhaseStreamSubscriberReceivesLaterTransitions()
    {
        await using var harness = SessionHarness.WithTranscript("hi");
        await harness.Session.PressAsync();
        var first = harness.Session.PhaseStream(CancellationToken.None).CollectUntilAsync(p => p.IsTerminal);
        var second = harness.Session.PhaseStream(CancellationToken.None).CollectUntilAsync(p => p.IsTerminal);

        await harness.Session.ReleaseAsync();

        (await first)[^1].Should().Be(PipelinePhase.Pasted);
        (await second)[^1].Should().Be(PipelinePhase.Pasted);
    }

    [Test]
    public async Task APhaseStreamEndsWhenItsTokenIsCancelled()
    {
        await using var harness = new SessionHarness();
        using var cts = new CancellationTokenSource();
        var stream = harness.Session.PhaseStream(cts.Token);
        var seen = new List<PipelinePhase>();

        var reading = Task.Run(async () =>
        {
            try
            {
                await foreach (var phase in stream)
                {
                    seen.Add(phase);
                }
            }
            catch (OperationCanceledException)
            {
            }
        });
        await SessionWaits.WaitUntilAsync(() => seen.Count == 1, "the initial phase");
        await cts.CancelAsync();
        await reading.WaitAsync(SessionWaits.Budget);

        seen.Should().Equal(PipelinePhase.Idle);
    }

    [Test]
    public async Task DisposingTheSessionCompletesEveryPhaseStream()
    {
        var harness = new SessionHarness();
        var stream = harness.Session.PhaseStream(CancellationToken.None);
        var seen = new List<PipelinePhase>();

        await harness.DisposeAsync();
        await foreach (var phase in stream.WithCancellation(new CancellationTokenSource(SessionWaits.Budget).Token))
        {
            seen.Add(phase);
        }

        seen.Should().Equal(PipelinePhase.Idle);
    }
}
