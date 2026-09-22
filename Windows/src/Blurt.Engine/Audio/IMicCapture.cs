namespace Blurt.Engine.Audio;

/// <summary>Captures 16 kHz mono 16-bit PCM from the microphone.</summary>
public interface IMicCapture
{
    /// <summary>
    /// Begins capture and returns its live feed of raw S16LE chunks in arrival order, which
    /// completes when capture stops. May not return until the device actually delivers frames, so
    /// the "speak now" cues that follow the return never precede live audio.
    /// </summary>
    /// <exception cref="Exception">Permission or device failure.</exception>
    Task<IAsyncEnumerable<byte[]>> StartAsync(CancellationToken ct);

    /// <summary>Stops capture and returns how many bytes of audio it produced.</summary>
    Task<int> StopAsync();

    /// <summary>
    /// Stops capture and discards the audio, taking effect immediately. The default calls
    /// <see cref="StopAsync"/> and drops its result.
    /// </summary>
    Task CancelCaptureAsync() => StopAsync();

    /// <summary>Loudness feed for a meter, 0 to 1, while recording. The default completes immediately.</summary>
    IAsyncEnumerable<float> Levels => AsyncEnumerable.Empty<float>();

    /// <summary>
    /// Optionally pays set-up cost ahead of the first <see cref="StartAsync"/>. Never opens the input
    /// device and never throws. The default does nothing.
    /// </summary>
    Task WarmUpAsync() => Task.CompletedTask;
}
