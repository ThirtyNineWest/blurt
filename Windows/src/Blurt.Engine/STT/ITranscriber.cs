namespace Blurt.Engine.STT;

/// <summary>Transcribes one utterance while it is still being recorded.</summary>
public interface ITranscriber
{
    /// <summary>
    /// Uploads <paramref name="frames"/> (raw mono S16LE PCM at <paramref name="sampleRate"/>) in
    /// chunks as they arrive and returns the single final transcript. Completing
    /// <paramref name="frames"/> signals end of audio. <paramref name="context"/> is resolved before
    /// the request opens; null sends none. Cancelling <paramref name="ct"/> abandons the request.
    /// </summary>
    /// <exception cref="AudioTooShortException">The recording ended below <see cref="SyncSTTLimits.MinPcmBytes"/>.</exception>
    Task<string> TranscribeAsync(IAsyncEnumerable<byte[]> frames, int sampleRate, TranscriptionContext? context, CancellationToken ct);

    /// <summary>
    /// Optionally pre-opens the connection so the next request skips setup. Never throws; a failure
    /// means the real request pays setup. The default does nothing.
    /// </summary>
    Task WarmUpAsync() => Task.CompletedTask;
}
