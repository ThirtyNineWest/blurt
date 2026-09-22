using Blurt.Engine.Audio;

namespace Blurt.Engine.Tests.Stubs;

/// <summary>
/// A mic whose <see cref="StartAsync"/> waits until the test releases it, so a command can land
/// while the session sits in <c>Connecting</c>. With <see cref="ThrowsIfCancelled"/> set, the wait
/// honors the start token and throws <see cref="OperationCanceledException"/> when it cancels, as the
/// real capture's liveness wait does.
/// </summary>
public sealed class GatedStartMic : IMicCapture
{
    private readonly Gate _gate = new();
    private int _startCalls;
    private int _stopCalls;
    private int _cancelCaptureCalls;

    /// <summary>Whether the gated wait honors the start token.</summary>
    public bool ThrowsIfCancelled { get; init; }

    /// <summary>
    /// When set, a start token cancelled during the gated wait makes <see cref="StartAsync"/> throw
    /// this instead of <see cref="OperationCanceledException"/>, as a device layer that reports the
    /// abort in its own terms would.
    /// </summary>
    public Exception? ErrorWhenCancelled { get; init; }

    /// <summary>The token passed to the most recent <see cref="StartAsync"/>.</summary>
    public CancellationToken ReceivedToken { get; private set; }

    /// <summary>How many times <see cref="StartAsync"/> was called.</summary>
    public int StartCalls => Volatile.Read(ref _startCalls);

    /// <summary>How many times <see cref="StopAsync"/> was called, including through <see cref="CancelCaptureAsync"/>.</summary>
    public int StopCalls => Volatile.Read(ref _stopCalls);

    /// <summary>How many times <see cref="CancelCaptureAsync"/> was called.</summary>
    public int CancelCaptureCalls => Volatile.Read(ref _cancelCaptureCalls);

    /// <inheritdoc/>
    public async Task<IAsyncEnumerable<byte[]>> StartAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _startCalls);
        ReceivedToken = ct;
        try
        {
            await _gate.EnterAsync(ThrowsIfCancelled || ErrorWhenCancelled is not null ? ct : CancellationToken.None);
        }
        catch (OperationCanceledException) when (ErrorWhenCancelled is { } error)
        {
            throw error;
        }

        return StubFeeds.OneShot(StubPcm.AboveMinimum);
    }

    /// <inheritdoc/>
    public Task<int> StopAsync()
    {
        Interlocked.Increment(ref _stopCalls);
        return Task.FromResult(StubPcm.AboveMinimum.Length);
    }

    /// <summary>Counts the call, then delegates to <see cref="StopAsync"/>.</summary>
    public async Task CancelCaptureAsync()
    {
        Interlocked.Increment(ref _cancelCaptureCalls);
        await StopAsync();
    }

    /// <summary>Completes once a <see cref="StartAsync"/> is waiting at the gate.</summary>
    public Task WaitUntilStartEnteredAsync() => _gate.WaitUntilEnteredAsync();

    /// <summary>Lets every waiting <see cref="StartAsync"/> return.</summary>
    public void AllowStartToFinish() => _gate.AllowToFinish();
}
