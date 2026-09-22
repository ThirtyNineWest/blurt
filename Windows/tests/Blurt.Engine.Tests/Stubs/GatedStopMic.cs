using Blurt.Engine.Audio;

namespace Blurt.Engine.Tests.Stubs;

/// <summary>
/// A mic whose <see cref="StopAsync"/> signals entry and then waits until the test releases it, so
/// a second release or a cancel can land while the first caller is inside the stop. Throws the
/// configured stop error once released.
/// </summary>
public sealed class GatedStopMic(Exception? stopError = null) : IMicCapture
{
    private readonly Gate _gate = new();
    private int _startCalls;
    private int _stopCalls;

    /// <summary>How many times <see cref="StartAsync"/> was called.</summary>
    public int StartCalls => Volatile.Read(ref _startCalls);

    /// <summary>How many times <see cref="StopAsync"/> was called.</summary>
    public int StopCalls => Volatile.Read(ref _stopCalls);

    /// <inheritdoc/>
    public Task<IAsyncEnumerable<byte[]>> StartAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _startCalls);
        return Task.FromResult(StubFeeds.OneShot(StubPcm.AboveMinimum));
    }

    /// <inheritdoc/>
    public async Task<int> StopAsync()
    {
        Interlocked.Increment(ref _stopCalls);
        await _gate.EnterAsync();
        if (stopError is not null)
        {
            throw stopError;
        }

        return StubPcm.AboveMinimum.Length;
    }

    /// <summary>Completes once a <see cref="StopAsync"/> is waiting at the gate.</summary>
    public Task WaitUntilStopEnteredAsync() => _gate.WaitUntilEnteredAsync();

    /// <summary>Lets every waiting <see cref="StopAsync"/> finish.</summary>
    public void AllowStopToFinish() => _gate.AllowToFinish();
}
