using System.Runtime.CompilerServices;
using Blurt.Engine.Pipeline;
using Blurt.Engine.STT;
using Blurt.Engine.Tests.Stubs;
using FluentAssertions;

namespace Blurt.Engine.Tests.Pipeline;

[NonParallelizable]
public sealed class DictationSessionRobustnessTests
{
    [Test]
    public async Task AThrowingStyleProviderIsTreatedAsNoStyle()
    {
        await using var harness = new SessionHarness(styleNameProvider: () => throw new InvalidOperationException("store broken"));

        var phase = await harness.DictateAsync();

        phase.Should().Be(PipelinePhase.Pasted);
        harness.Deliveries.Should().ContainSingle().Which.Recent.Entries.Should().ContainSingle().Which.Style.Should().BeNull();
    }

    [Test]
    public async Task AThrowingDeveloperModeSwitchStillLetsAFailureLand()
    {
        await using var harness = new SessionHarness(
            readinessCheck: () => new BlurtError.ApiKeyMissing(),
            logEnabled: () => throw new InvalidOperationException("settings unreadable"));

        await harness.Session.PressAsync();

        harness.Session.Phase.Should().Be(PipelinePhase.Failed(new BlurtError.ApiKeyMissing()));
        Directory.Exists(harness.Log.Directory).Should().BeFalse();
    }

    [Test]
    public async Task AThrowingDeveloperModeSwitchReadsAsOff()
    {
        using var log = new TestLog(isEnabled: () => throw new InvalidOperationException("settings unreadable"));

        FluentActions.Invoking(() => log.Log.Append("p", null)).Should().NotThrow();
        FluentActions.Invoking(() => log.Log.AppendError(new BlurtError.TargetAppLost(), null)).Should().NotThrow();
        await log.Log.DrainAsync();

        Directory.Exists(log.Directory).Should().BeFalse();
    }

    [Test]
    public async Task AnAbandonedUploadThatFaultsIsObserved()
    {
        var unobserved = 0;
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            if (e.Exception.InnerExceptions.Any(x => x is AbandonedUploadException))
            {
                Interlocked.Increment(ref unobserved);
            }
        }

        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            for (var i = 0; i < 3; i++)
            {
                await CancelARecordingWhoseUploadFaultsAsync();
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.WaitForPendingFinalizers();

            Volatile.Read(ref unobserved).Should().Be(0);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task CancelARecordingWhoseUploadFaultsAsync()
    {
        var transcriber = new FaultsOnAbortTranscriber();
        await using var harness = new SessionHarness(transcriber: transcriber);
        await harness.Session.PressAsync();
        await SessionWaits.WaitUntilAsync(() => transcriber.Started, "the upload to open");

        await harness.Session.CancelAsync();
        await SessionWaits.WaitUntilAsync(() => transcriber.Threw, "the upload to fault");
        await Task.Delay(20);
    }

    private sealed class AbandonedUploadException() : Exception("The request was abandoned.");

    /// <summary>A transcriber that reports an abandoned request as a non-cancellation fault.</summary>
    private sealed class FaultsOnAbortTranscriber : ITranscriber
    {
        private int _started;
        private int _threw;

        public bool Started => Volatile.Read(ref _started) == 1;

        public bool Threw => Volatile.Read(ref _threw) == 1;

        public async Task<string> TranscribeAsync(
            IAsyncEnumerable<byte[]> frames, int sampleRate, TranscriptionContext? context, CancellationToken ct)
        {
            Volatile.Write(ref _started, 1);
            try
            {
                await foreach (var _ in frames.WithCancellation(ct))
                {
                }
            }
            catch (OperationCanceledException)
            {
                Volatile.Write(ref _threw, 1);
                throw new AbandonedUploadException();
            }

            return "unreachable";
        }
    }
}
