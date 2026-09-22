using Blurt.Engine.STT;
using FluentAssertions;

namespace Blurt.Engine.Tests.Stubs;

public sealed class StubBehaviorTests
{
    [Test]
    public async Task MicFeedPublishesThePcmAndEndsOnStop()
    {
        var mic = new StubMicCapture();
        var transcriber = new StubTranscriber();
        var feed = await mic.StartAsync(CancellationToken.None);
        var request = transcriber.TranscribeAsync(feed, SyncSTTLimits.SampleRate, null, CancellationToken.None);

        var byteCount = await mic.StopAsync();
        var transcript = await request;

        transcript.Should().Be("hello world");
        byteCount.Should().Be(SyncSTTLimits.MinPcmBytes * 2);
        transcriber.ReceivedFrames.Should().ContainSingle().Which.Should().HaveCount(byteCount);
        mic.StartCalls.Should().Be(1);
        mic.StopCalls.Should().Be(1);
    }

    [Test]
    public async Task MicCancelCountsAndStops()
    {
        var mic = new StubMicCapture();
        await mic.StartAsync(CancellationToken.None);

        await mic.CancelCaptureAsync();

        mic.CancelCaptureCalls.Should().Be(1);
        mic.StopCalls.Should().Be(1);
    }

    [Test]
    public async Task MicStartAndStopErrorsSurface()
    {
        var mic = new StubMicCapture { StartError = new InvalidOperationException("start") };

        await FluentActions.Awaiting(() => mic.StartAsync(CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>();

        mic.StartError = null;
        mic.StopError = new TimeoutException("stop");
        await mic.StartAsync(CancellationToken.None);
        await FluentActions.Awaiting(mic.StopAsync).Should().ThrowAsync<TimeoutException>();
    }

    [Test]
    public async Task TranscriberHoldsTheRequestOpenUntilTheGateOpens()
    {
        var gate = new TaskCompletionSource();
        var transcriber = new StubTranscriber { Gate = gate, TranscriptToReturn = "held" };
        var context = new TranscriptionContext("Notes", null, null, "x", null, [], [], false);

        var request = transcriber.TranscribeAsync(AsyncEnumerable.Empty<byte[]>(), 16_000, context, CancellationToken.None);
        await Task.Yield();
        var heldOpen = request.IsCompleted;
        gate.SetResult();

        heldOpen.Should().BeFalse();
        (await request).Should().Be("held");
        transcriber.ReceivedContext.Should().Be(context);
        transcriber.ReceivedSampleRate.Should().Be(16_000);
    }

    [Test]
    public async Task TranscriberThrowsTheConfiguredError()
    {
        var transcriber = new StubTranscriber { ErrorToThrow = new AudioTooShortException() };

        await FluentActions.Awaiting(() => transcriber.TranscribeAsync(AsyncEnumerable.Empty<byte[]>(), 16_000, null, CancellationToken.None))
            .Should().ThrowAsync<AudioTooShortException>();
    }

    [Test]
    public async Task InjectorRecordsInsertsWithTheirCaretContext()
    {
        var injector = new StubInjector();

        await injector.InsertAsync("text", "prior", "Window", CancellationToken.None);

        injector.Insertions.Should().Equal(new StubInjector.Insertion("text", "prior", "Window"));
    }

    [Test]
    public async Task InjectorErrorRecordsNothing()
    {
        var injector = new StubInjector { ErrorToThrow = new BlurtException(new BlurtError.NoEditableTarget()) };

        await FluentActions.Awaiting(() => injector.InsertAsync("text", null, null, CancellationToken.None))
            .Should().ThrowAsync<BlurtException>();

        injector.Inserted.Should().BeEmpty();
    }

    [Test]
    public void PcmFixturesStraddleTheFloor()
    {
        StubPcm.AboveMinimum.Length.Should().BeGreaterThan(SyncSTTLimits.MinPcmBytes);
        StubPcm.BelowMinimum.Length.Should().BeLessThan(SyncSTTLimits.MinPcmBytes);
        StubPcm.EveryByteValueAboveMinimum.Length.Should().BeGreaterThan(SyncSTTLimits.MinPcmBytes);
        StubPcm.EveryByteValueAboveMinimum.Distinct().Should().HaveCount(256);
    }
}
