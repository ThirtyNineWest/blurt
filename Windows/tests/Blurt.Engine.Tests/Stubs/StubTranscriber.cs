using Blurt.Engine.STT;

namespace Blurt.Engine.Tests.Stubs;

/// <summary>
/// An <see cref="ITranscriber"/> that records the context it receives, drains the frame feed to its
/// end into <see cref="ReceivedFrames"/>, waits for <see cref="Gate"/> when set, then throws
/// <see cref="ErrorToThrow"/> or returns <see cref="TranscriptToReturn"/>.
/// </summary>
public sealed class StubTranscriber : ITranscriber
{
    private readonly Lock _gate = new();
    private readonly List<byte[]> _receivedFrames = [];
    private readonly List<TranscriptionContext?> _receivedContexts = [];
    private int _transcribeCalls;

    /// <summary>The transcript returned when <see cref="ErrorToThrow"/> is unset.</summary>
    public string TranscriptToReturn { get; set; } = "hello world";

    /// <summary>When set, thrown after the feed is drained.</summary>
    public Exception? ErrorToThrow { get; set; }

    /// <summary>When set, the request stays open after draining until this completes (or the token cancels).</summary>
    public TaskCompletionSource? Gate { get; set; }

    /// <summary>How many times <see cref="TranscribeAsync"/> was called.</summary>
    public int TranscribeCalls => Volatile.Read(ref _transcribeCalls);

    /// <summary>Every chunk drained from every feed, in order.</summary>
    public IReadOnlyList<byte[]> ReceivedFrames
    {
        get
        {
            lock (_gate)
            {
                return [.. _receivedFrames];
            }
        }
    }

    /// <summary>The context passed to each call, in order.</summary>
    public IReadOnlyList<TranscriptionContext?> ReceivedContexts
    {
        get
        {
            lock (_gate)
            {
                return [.. _receivedContexts];
            }
        }
    }

    /// <summary>The context passed to the most recent call.</summary>
    public TranscriptionContext? ReceivedContext
    {
        get
        {
            lock (_gate)
            {
                return _receivedContexts.Count == 0 ? null : _receivedContexts[^1];
            }
        }
    }

    /// <summary>The sample rate passed to the most recent call.</summary>
    public int? ReceivedSampleRate { get; private set; }

    /// <inheritdoc/>
    public async Task<string> TranscribeAsync(
        IAsyncEnumerable<byte[]> frames, int sampleRate, TranscriptionContext? context, CancellationToken ct)
    {
        Interlocked.Increment(ref _transcribeCalls);
        lock (_gate)
        {
            _receivedContexts.Add(context);
            ReceivedSampleRate = sampleRate;
        }

        await foreach (var frame in frames.WithCancellation(ct))
        {
            lock (_gate)
            {
                _receivedFrames.Add(frame);
            }
        }

        ct.ThrowIfCancellationRequested();
        if (Gate is { } gate)
        {
            await gate.Task.WaitAsync(ct);
        }

        if (ErrorToThrow is { } error)
        {
            throw error;
        }

        return TranscriptToReturn;
    }
}
