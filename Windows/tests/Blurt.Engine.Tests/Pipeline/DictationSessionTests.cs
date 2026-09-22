using Blurt.Engine.Pipeline;
using Blurt.Engine.STT;
using Blurt.Engine.Tests.Stubs;
using FluentAssertions;

namespace Blurt.Engine.Tests.Pipeline;

public sealed class DictationSessionTests
{
    [Test]
    public async Task PressFromIdleTransitionsToRecording()
    {
        await using var harness = SessionHarness.WithTranscript("hello");

        await harness.Session.PressAsync();

        harness.Session.Phase.Should().Be(PipelinePhase.Recording);
        harness.Mic.StartCalls.Should().Be(1);
    }

    [Test]
    public async Task HappyPathWalksEveryPhaseAndPastes()
    {
        await using var harness = SessionHarness.WithTranscript("Hello world.");
        var stream = harness.Session.PhaseStream(CancellationToken.None);

        await harness.Session.PressAsync();
        await harness.Session.ReleaseAsync();
        var seen = await stream.CollectUntilAsync(p => p.IsTerminal && p != PipelinePhase.Idle);

        seen.Should().Equal(
            PipelinePhase.Idle,
            PipelinePhase.Connecting,
            PipelinePhase.Recording,
            PipelinePhase.Transcribing,
            PipelinePhase.Injecting,
            PipelinePhase.Pasted);
        harness.Injector.Insertions.Should().Equal(new StubInjector.Insertion("Hello world.", null, null));
    }

    [Test]
    public async Task ATooShortClipIsDroppedAsASilentNoOp()
    {
        await using var harness = SessionHarness.WithTranscript("should not be used");
        harness.Mic.PcmToReturn = new byte[6];

        var phase = await harness.DictateAsync();

        phase.Should().Be(PipelinePhase.Idle);
        harness.Injector.Inserted.Should().BeEmpty();
        (await harness.Log.FailureNamesAsync()).Should().BeEmpty();
    }

    [Test]
    public async Task ATooShortClipReportedByTheTranscriberIdlesQuietly()
    {
        await using var harness = SessionHarness.WithTranscriberError(new AudioTooShortException());

        var phase = await harness.DictateAsync();

        phase.Should().Be(PipelinePhase.Idle);
        harness.Injector.Inserted.Should().BeEmpty();
        (await harness.Log.FailureNamesAsync()).Should().BeEmpty();
    }

    [Test]
    public async Task SttFailureSurfacesFailedAndSkipsInjection()
    {
        await using var harness = SessionHarness.WithTranscriberError(new InvalidOperationException("boom"));

        var phase = await harness.DictateAsync();

        phase.Should().BeOfType<PipelinePhase.FailedPhase>().Which.Error.Should().BeOfType<BlurtError.SttFailed>();
        harness.Injector.Inserted.Should().BeEmpty();
    }

    [Test]
    public async Task ABlurtErrorFromTheTranscriberSurfacesVerbatim()
    {
        await using var harness = SessionHarness.WithTranscriberError(new BlurtException(new BlurtError.ApiKeyMissing()));

        var phase = await harness.DictateAsync();

        phase.Should().Be(PipelinePhase.Failed(new BlurtError.ApiKeyMissing()));
        harness.Injector.Inserted.Should().BeEmpty();
    }

    [Test]
    public async Task PressAfterFailureSucceeds()
    {
        await using var harness = SessionHarness.WithTranscriberError(new InvalidOperationException("boom"));
        var failed = await harness.DictateAsync();

        await harness.Session.PressAsync();

        failed.Should().BeOfType<PipelinePhase.FailedPhase>();
        harness.Session.Phase.Should().Be(PipelinePhase.Recording);
    }

    [Test]
    public async Task MicStartFailureSurfacesAudioCaptureFailedAndStaysOutOfRecording()
    {
        await using var harness = SessionHarness.WithTranscript("never");
        harness.Mic.StartError = new InvalidOperationException("no device");

        await harness.Session.PressAsync();

        harness.Session.Phase.Should().BeOfType<PipelinePhase.FailedPhase>()
            .Which.Error.Should().BeOfType<BlurtError.AudioCaptureFailed>();
    }

    [Test]
    public async Task AnEmptyTranscriptReturnsToIdleWithoutInjecting()
    {
        await using var harness = SessionHarness.WithTranscript("   ");

        var phase = await harness.DictateAsync();

        phase.Should().Be(PipelinePhase.Idle);
        harness.Injector.Inserted.Should().BeEmpty();
    }

    [Test]
    public async Task AnUntypedInjectorFailureSurfacesTargetAppLost()
    {
        await using var harness = SessionHarness.WithTranscript("Hello world.");
        harness.Injector.ErrorToThrow = new InvalidOperationException("boom");

        var phase = await harness.DictateAsync();

        phase.Should().Be(PipelinePhase.Failed(new BlurtError.TargetAppLost()));
    }

    [Test]
    public async Task ANonQuietInjectorBlurtErrorSurfacesVerbatim()
    {
        await using var harness = SessionHarness.WithTranscript("Hello world.");
        var error = new BlurtError.AudioCaptureFailed(new InvalidOperationException("device gone"));
        harness.Injector.ErrorToThrow = new BlurtException(error);

        var phase = await harness.DictateAsync();

        phase.Should().Be(PipelinePhase.Failed(error));
    }

    [Test]
    public async Task NoEditableTargetSurfacesTheQuietNoTargetPhase()
    {
        await using var harness = SessionHarness.WithTranscript("Hello world.");
        harness.Injector.ErrorToThrow = new BlurtException(new BlurtError.NoEditableTarget());

        var phase = await harness.DictateAsync();

        phase.Should().Be(PipelinePhase.NoTarget);
    }

    [Test]
    public async Task ALostTargetSurfacesTheQuietNoTargetPhase()
    {
        await using var harness = SessionHarness.WithTranscript("Hello world.");
        harness.Injector.ErrorToThrow = new BlurtException(new BlurtError.TargetAppLost());

        var phase = await harness.DictateAsync();

        phase.Should().Be(PipelinePhase.NoTarget);
    }

    [Test]
    public async Task MicStopFailureSurfacesAudioCaptureFailedWithoutInjecting()
    {
        await using var harness = SessionHarness.WithTranscript("never");
        harness.Mic.StopError = new InvalidOperationException("stop failed");

        var phase = await harness.DictateAsync();

        phase.Should().BeOfType<PipelinePhase.FailedPhase>().Which.Error.Should().BeOfType<BlurtError.AudioCaptureFailed>();
        harness.Injector.Inserted.Should().BeEmpty();
    }

    [Test]
    public async Task AHeldTriggerAutoReleasesAtTheCap()
    {
        var clock = new TestClock();
        var cap = TimeSpan.FromSeconds(SyncSTTLimits.AutoReleaseSeconds);
        await using var harness = new SessionHarness(
            transcriber: new StubTranscriber { TranscriptToReturn = "Timed out text." }, clock: clock);
        await harness.Session.PressAsync();
        await clock.WaitUntilSleepingAsync(cap);

        clock.Advance(cap - TimeSpan.FromMilliseconds(100));
        var beforeCap = harness.Session.Phase;
        clock.Advance(TimeSpan.FromMilliseconds(100));
        var phase = await harness.Session.WaitForPhaseAsync(PipelinePhase.Pasted);

        beforeCap.Should().Be(PipelinePhase.Recording);
        phase.Should().Be(PipelinePhase.Pasted);
        harness.Injector.Inserted.Should().Equal("Timed out text.");
    }

    [Test]
    public async Task PhaseStreamYieldsTheCurrentPhaseThenTransitionsToTerminal()
    {
        await using var harness = SessionHarness.WithTranscript("Hi.");
        await harness.Session.PressAsync();
        var stream = harness.Session.PhaseStream(CancellationToken.None);

        var terminal = stream.CollectUntilAsync(p => p.IsTerminal);
        await harness.Session.ReleaseAsync();
        var seen = await terminal;

        seen[0].Should().Be(PipelinePhase.Recording);
        seen[^1].Should().Be(PipelinePhase.Pasted);
    }

    [Test]
    public async Task PhaseChangedIsRaisedForEveryTransition()
    {
        await using var harness = SessionHarness.WithTranscript("Hi.");
        var raised = new List<PipelinePhase>();
        var gate = new Lock();
        harness.Session.PhaseChanged += phase =>
        {
            lock (gate)
            {
                raised.Add(phase);
            }
        };

        await harness.DictateAsync();
        await SessionWaits.WaitUntilAsync(
            () =>
            {
                lock (gate)
                {
                    return raised.Count == 5;
                }
            },
            "five events");

        lock (gate)
        {
            raised.Should().Equal(
                PipelinePhase.Connecting,
                PipelinePhase.Recording,
                PipelinePhase.Transcribing,
                PipelinePhase.Injecting,
                PipelinePhase.Pasted);
        }
    }

    [Test]
    public async Task PressPublishesConnectingBeforeRecording()
    {
        await using var harness = new SessionHarness();
        var stream = harness.Session.PhaseStream(CancellationToken.None);

        harness.Session.Submit(Command.Press);
        var seen = await stream.CollectUntilAsync(p => p == PipelinePhase.Recording);

        seen.Should().Equal(PipelinePhase.Idle, PipelinePhase.Connecting, PipelinePhase.Recording);
    }

    [Test]
    public async Task CancelDuringRecordingStopsTheMicAndDiscardsTheAudio()
    {
        await using var harness = SessionHarness.WithTranscript("Hello");
        await harness.Session.PressAsync();
        var recording = harness.Session.Phase;

        await harness.Session.CancelAsync();

        recording.Should().Be(PipelinePhase.Recording);
        harness.Session.Phase.Should().Be(PipelinePhase.Cancelled);
        harness.Mic.StopCalls.Should().Be(1);
        harness.Mic.CancelCaptureCalls.Should().Be(1);
        harness.Deliveries.Should().BeEmpty();
        harness.Injector.Inserted.Should().BeEmpty();
    }

    [Test]
    public async Task ACancelTearsTheMicDownThroughCancelCaptureAndAReleaseThroughStop()
    {
        await using var cancelled = new SessionHarness();
        await using var released = new SessionHarness();

        await cancelled.Session.PressAsync();
        await cancelled.Session.CancelAsync();
        await released.DictateAsync();

        cancelled.Mic.CancelCaptureCalls.Should().Be(1);
        released.Mic.CancelCaptureCalls.Should().Be(0);
        released.Mic.StopCalls.Should().Be(1);
    }
}
