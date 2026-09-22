using System.Threading.Channels;
using Blurt.Engine.Audio;

namespace Blurt.Engine.Tests.Stubs;

/// <summary>
/// An <see cref="IMicCapture"/> whose feed publishes <see cref="PcmToReturn"/> once on start and
/// stays open until <see cref="StopAsync"/> or <see cref="CancelCaptureAsync"/>, like a real
/// recorder. <see cref="StopAsync"/> reports <see cref="PcmToReturn"/>'s length, so one setting moves
/// both the uploaded payload and the release path's length check.
/// </summary>
public sealed class StubMicCapture : IMicCapture
{
    private readonly Lock _gate = new();
    private Channel<byte[]>? _feed;
    private int _startCalls;
    private int _stopCalls;
    private int _cancelCaptureCalls;

    /// <summary>The audio the feed publishes and whose length <see cref="StopAsync"/> reports.</summary>
    public byte[] PcmToReturn { get; set; } = StubPcm.AboveMinimum;

    /// <summary>When set, <see cref="StartAsync"/> throws it after counting the call.</summary>
    public Exception? StartError { get; set; }

    /// <summary>When set, <see cref="StopAsync"/> throws it after completing the feed.</summary>
    public Exception? StopError { get; set; }

    /// <summary>How many times <see cref="StartAsync"/> was called.</summary>
    public int StartCalls => Volatile.Read(ref _startCalls);

    /// <summary>How many times <see cref="StopAsync"/> was called, including through <see cref="CancelCaptureAsync"/>.</summary>
    public int StopCalls => Volatile.Read(ref _stopCalls);

    /// <summary>How many times <see cref="CancelCaptureAsync"/> was called.</summary>
    public int CancelCaptureCalls => Volatile.Read(ref _cancelCaptureCalls);

    /// <inheritdoc/>
    public Task<IAsyncEnumerable<byte[]>> StartAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _startCalls);
        if (StartError is { } startError)
        {
            return Task.FromException<IAsyncEnumerable<byte[]>>(startError);
        }

        var feed = Channel.CreateUnbounded<byte[]>();
        var pcm = PcmToReturn;
        if (pcm.Length > 0)
        {
            feed.Writer.TryWrite(pcm);
        }

        lock (_gate)
        {
            _feed = feed;
        }

        return Task.FromResult(feed.Reader.ReadAllAsync());
    }

    /// <summary>Completes the feed, then throws <see cref="StopError"/> or returns <see cref="PcmToReturn"/>'s length.</summary>
    public Task<int> StopAsync()
    {
        Interlocked.Increment(ref _stopCalls);
        FinishFeed();
        return StopError is { } stopError ? Task.FromException<int>(stopError) : Task.FromResult(PcmToReturn.Length);
    }

    /// <summary>Counts the call, then delegates to <see cref="StopAsync"/>.</summary>
    public async Task CancelCaptureAsync()
    {
        Interlocked.Increment(ref _cancelCaptureCalls);
        await StopAsync();
    }

    private void FinishFeed()
    {
        Channel<byte[]>? feed;
        lock (_gate)
        {
            feed = _feed;
            _feed = null;
        }

        feed?.Writer.TryComplete();
    }
}
