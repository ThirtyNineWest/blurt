namespace Blurt.Engine.TTS;

/// <summary>Plays PCM chunks as they arrive. One sink serves one read, so a cancelled read cannot bleed into the next.</summary>
public interface IPcmSink
{
    /// <summary>Queues <paramref name="pcm"/> to play after everything already queued. Does not block.</summary>
    void Enqueue(byte[] pcm);

    /// <summary>Completes once everything enqueued has played, or <see cref="Stop"/> was called.</summary>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled before playback finished.</exception>
    Task DrainAsync(CancellationToken ct);

    /// <summary>Silences playback at once and discards what is queued. Safe to call more than once.</summary>
    void Stop();
}
