using Blurt.Engine.FocusCapture;
using Blurt.Engine.Injection;
using Blurt.Engine.Pipeline;
using Blurt.Engine.STT;
using Blurt.Engine.Tests.Stubs;
using FluentAssertions;

namespace Blurt.Engine.Tests.Pipeline;

public sealed class CancelRaceTests
{
    [Test]
    public async Task CancelDuringTranscribingDiscardsTheTranscriptAndEndsCancelled()
    {
        var transcriber = new GatedTranscriber("Hello world.");
        await using var harness = new SessionHarness(transcriber: transcriber);
        await harness.Session.PressAsync();
        await harness.Session.ReleaseAsync();
        await transcriber.WaitUntilStartedAsync();
        var transcribing = harness.Session.Phase;

        await harness.Session.CancelAsync();
        var afterCancel = harness.Session.Phase;
        transcriber.AllowToFinish();
        await harness.Session.AwaitPipelineAsync().WaitAsync(SessionWaits.Budget);

        transcribing.Should().Be(PipelinePhase.Transcribing);
        afterCancel.Should().Be(PipelinePhase.Cancelled);
        harness.Injector.Inserted.Should().BeEmpty();
        harness.Deliveries.Should().BeEmpty();
        harness.Session.Phase.Should().Be(PipelinePhase.Cancelled);
    }

    [Test]
    public async Task CancelDuringTranscribingWithATranscriberThatThrowsOnAbortStaysCancelled()
    {
        var transcriber = new GatedTranscriber("Hello world.", throwsWhenCancelled: true);
        await using var harness = new SessionHarness(transcriber: transcriber);
        await harness.Session.PressAsync();
        await harness.Session.ReleaseAsync();
        await transcriber.WaitUntilStartedAsync();

        await harness.Session.CancelAsync();
        transcriber.AllowToFinish();
        await harness.Session.AwaitPipelineAsync().WaitAsync(SessionWaits.Budget);

        harness.Session.Phase.Should().Be(PipelinePhase.Cancelled);
        harness.Injector.Inserted.Should().BeEmpty();
        (await harness.Log.FailuresAsync()).Should().BeEmpty();
    }

    [Test]
    public async Task CancelDuringInjectingRecordsNoPasteAndEndsCancelled()
    {
        var injector = new GatedInjector();
        await using var harness = new SessionHarness(injector: injector);
        await harness.Session.PressAsync();
        await harness.Session.ReleaseAsync();
        await injector.WaitUntilInsertEnteredAsync();
        var injecting = harness.Session.Phase;

        await harness.Session.CancelAsync();
        var afterCancel = harness.Session.Phase;
        injector.AllowInsertToFinish();
        await harness.Session.AwaitPipelineAsync().WaitAsync(SessionWaits.Budget);

        injecting.Should().Be(PipelinePhase.Injecting);
        afterCancel.Should().Be(PipelinePhase.Cancelled);
        injector.RecordedPastes.Should().Be(0);
        harness.Session.Phase.Should().Be(PipelinePhase.Cancelled);
    }

    [Test]
    public async Task CancelWhileReleaseIsStoppingTheMicDiscardsTheRecording()
    {
        var mic = new GatedStopMic();
        await using var harness = new SessionHarness(mic: mic);
        await harness.Session.PressAsync();
        var release = harness.Session.ReleaseAsync();
        await mic.WaitUntilStopEnteredAsync();

        await harness.Session.CancelAsync();
        var afterCancel = harness.Session.Phase;
        mic.AllowStopToFinish();
        await release;
        await harness.Session.AwaitPipelineAsync().WaitAsync(SessionWaits.Budget);

        afterCancel.Should().Be(PipelinePhase.Cancelled);
        harness.Session.Phase.Should().Be(PipelinePhase.Cancelled);
        harness.Injector.Inserted.Should().BeEmpty();
        harness.Deliveries.Should().BeEmpty();
    }

    [Test]
    public async Task CancelWhileReleasesMicStopFailsStillWinsOverTheAudioError()
    {
        var mic = new GatedStopMic(new IOException("stop failed"));
        await using var harness = new SessionHarness(mic: mic);
        await harness.Session.PressAsync();
        var release = harness.Session.ReleaseAsync();
        await mic.WaitUntilStopEnteredAsync();

        await harness.Session.CancelAsync();
        mic.AllowStopToFinish();
        await release;

        harness.Session.Phase.Should().Be(PipelinePhase.Cancelled);
        harness.Injector.Inserted.Should().BeEmpty();
        (await harness.Log.FailuresAsync()).Should().BeEmpty();
    }

    [Test]
    public async Task ACancelRecordedWhileAReleaseIsQueuedIsConsumedByThatReleaseAfterTheMicStop()
    {
        var clock = new TestClock();
        var cap = TimeSpan.FromSeconds(SyncSTTLimits.AutoReleaseSeconds);
        using var pressHeld = new ManualResetEventSlim(false);
        clock.HoldTimerCreation(cap, pressHeld);
        await using var harness = new SessionHarness(clock: clock);
        var pressed = harness.Session.PressAsync();
        await SessionWaits.WaitUntilAsync(() => clock.HeldCreations == 1, "the press to arm its auto-release");
        var released = harness.Session.ReleaseAsync();
        var recordingWhileQueued = harness.Session.Phase;

        var cancelled = harness.Session.CancelAsync();
        pressHeld.Set();
        await pressed;
        await released;
        await cancelled;
        await harness.Session.AwaitPipelineAsync().WaitAsync(SessionWaits.Budget);

        recordingWhileQueued.Should().Be(PipelinePhase.Recording);
        harness.Session.Phase.Should().Be(PipelinePhase.Cancelled);
        harness.Mic.StopCalls.Should().Be(1);
        harness.Mic.CancelCaptureCalls.Should().Be(0);
        harness.Injector.Inserted.Should().BeEmpty();
        harness.Deliveries.Should().BeEmpty();
    }

    [Test]
    public async Task CancelOfALiveRecordingSurvivesAFailingMicTeardown()
    {
        await using var harness = new SessionHarness();
        harness.Mic.StopError = new IOException("stop failed");
        await harness.Session.PressAsync();
        var recording = harness.Session.Phase;

        await harness.Session.CancelAsync();

        recording.Should().Be(PipelinePhase.Recording);
        harness.Session.Phase.Should().Be(PipelinePhase.Cancelled);
        harness.Mic.StopCalls.Should().Be(1);
        harness.Injector.Inserted.Should().BeEmpty();
    }

    [Test]
    public async Task CancelRecordingDuringTranscribingLeavesThePipelineAlone()
    {
        var transcriber = new GatedTranscriber("Hello world.");
        await using var harness = new SessionHarness(transcriber: transcriber);
        await harness.Session.PressAsync();
        await harness.Session.ReleaseAsync();
        await transcriber.WaitUntilStartedAsync();

        await harness.Session.CancelRecordingAsync();
        var afterCancelRecording = harness.Session.Phase;
        transcriber.AllowToFinish();
        await harness.Session.AwaitPipelineAsync().WaitAsync(SessionWaits.Budget);

        afterCancelRecording.Should().Be(PipelinePhase.Transcribing);
        harness.Session.Phase.Should().Be(PipelinePhase.Pasted);
        harness.Injector.Inserted.Should().Equal("Hello world.");
    }

    [Test]
    public async Task CancelRecordingDuringRecordingCancelsLikeCancel()
    {
        await using var harness = new SessionHarness();
        await harness.Session.PressAsync();

        await harness.Session.CancelRecordingAsync();

        harness.Session.Phase.Should().Be(PipelinePhase.Cancelled);
        harness.Mic.StopCalls.Should().Be(1);
        harness.Injector.Inserted.Should().BeEmpty();
    }

    [Test]
    public async Task CancelLandingInTheInsertsNonCancellableTailStaysCancelledNotPasted()
    {
        var injector = new GatedInjector(honorsCancellation: false);
        await using var harness = new SessionHarness(injector: injector);
        await harness.Session.PressAsync();
        await harness.Session.ReleaseAsync();
        await injector.WaitUntilInsertEnteredAsync();

        await harness.Session.CancelAsync();
        injector.AllowInsertToFinish();
        await harness.Session.AwaitPipelineAsync().WaitAsync(SessionWaits.Budget);

        injector.RecordedPastes.Should().Be(1);
        harness.Session.Phase.Should().Be(PipelinePhase.Cancelled);
    }

    [Test]
    public async Task CancelDisarmsTheAutoReleaseTimer()
    {
        var clock = new TestClock();
        var cap = TimeSpan.FromSeconds(0.05);
        await using var harness = new SessionHarness(
            transcriber: new StubTranscriber { TranscriptToReturn = "Timed out text." }, clock: clock, maxRecordingSeconds: cap.TotalSeconds);
        await harness.Session.PressAsync();
        await clock.WaitUntilSleepingAsync(cap);

        await harness.Session.CancelAsync();
        var armedAfterCancel = clock.HasArmedTimer(cap);
        clock.Advance(TimeSpan.FromSeconds(1));
        await harness.Session.AwaitPipelineAsync().WaitAsync(SessionWaits.Budget);

        armedAfterCancel.Should().BeFalse();
        harness.Session.Phase.Should().Be(PipelinePhase.Cancelled);
        harness.Injector.Inserted.Should().BeEmpty();
        harness.Mic.StopCalls.Should().Be(1);
    }

    [Test]
    public async Task ReleaseDisarmsTheAutoReleaseTimer()
    {
        var clock = new TestClock();
        var cap = TimeSpan.FromSeconds(SyncSTTLimits.AutoReleaseSeconds);
        await using var harness = new SessionHarness(clock: clock);
        await harness.Session.PressAsync();
        await clock.WaitUntilSleepingAsync(cap);

        await harness.Session.ReleaseAsync();

        clock.HasArmedTimer(cap).Should().BeFalse();
    }

    /// <summary>
    /// A transcriber that drains the feed, signals entry, and then waits (ignoring cancellation)
    /// until released, so a cancel can land while the session is in <c>Transcribing</c>.
    /// </summary>
    private sealed class GatedTranscriber(string text, bool throwsWhenCancelled = false) : ITranscriber
    {
        private readonly Gate _gate = new();

        public async Task<string> TranscribeAsync(
            IAsyncEnumerable<byte[]> frames, int sampleRate, TranscriptionContext? context, CancellationToken ct)
        {
            try
            {
                await foreach (var _ in frames.WithCancellation(ct))
                {
                }
            }
            catch (OperationCanceledException)
            {
            }

            await _gate.EnterAsync();
            if (throwsWhenCancelled && ct.IsCancellationRequested)
            {
                throw new IOException("The request was aborted.");
            }

            return text;
        }

        public Task WaitUntilStartedAsync() => _gate.WaitUntilEnteredAsync();

        public void AllowToFinish() => _gate.AllowToFinish();
    }

    /// <summary>
    /// An injector that waits inside <see cref="InsertAsync"/> until released and then, when it
    /// honors cancellation, throws for a cancelled token before recording the paste.
    /// </summary>
    private sealed class GatedInjector(bool honorsCancellation = true) : IInjector
    {
        private readonly Gate _gate = new();
        private int _recordedPastes;

        public int RecordedPastes => Volatile.Read(ref _recordedPastes);

        public Task SetTargetAsync(FocusTarget? target) => Task.CompletedTask;

        public async Task InsertAsync(string text, string? priorText, string? windowTitle, CancellationToken ct)
        {
            await _gate.EnterAsync();
            if (honorsCancellation)
            {
                ct.ThrowIfCancellationRequested();
            }

            Interlocked.Increment(ref _recordedPastes);
        }

        public Task WaitUntilInsertEnteredAsync() => _gate.WaitUntilEnteredAsync();

        public void AllowInsertToFinish() => _gate.AllowToFinish();
    }
}
