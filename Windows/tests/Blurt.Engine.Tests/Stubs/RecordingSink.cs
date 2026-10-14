using Blurt.Engine.TTS;

namespace Blurt.Engine.Tests.Stubs;

/// <summary>An <see cref="IPcmSink"/> that records what it was given and whether it drained or was stopped.</summary>
public sealed class RecordingSink : IPcmSink
{
    private readonly Lock _gate = new();
    private readonly List<byte[]> _chunks = [];
    private bool _drained;
    private bool _stopped;

    /// <summary>Every chunk enqueued, in order.</summary>
    public IReadOnlyList<byte[]> Chunks
    {
        get
        {
            lock (_gate)
            {
                return [.. _chunks];
            }
        }
    }

    /// <summary>Whether <see cref="DrainAsync"/> has been called.</summary>
    public bool Drained
    {
        get
        {
            lock (_gate)
            {
                return _drained;
            }
        }
    }

    /// <summary>Whether <see cref="Stop"/> has been called.</summary>
    public bool Stopped
    {
        get
        {
            lock (_gate)
            {
                return _stopped;
            }
        }
    }

    /// <inheritdoc/>
    public void Enqueue(byte[] pcm)
    {
        lock (_gate)
        {
            _chunks.Add(pcm);
        }
    }

    /// <inheritdoc/>
    public Task DrainAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            _drained = true;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Stop()
    {
        lock (_gate)
        {
            _stopped = true;
        }
    }
}
