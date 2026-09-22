namespace Blurt.Engine.STT;

/// <summary>
/// Limits of the Sync STT model behind AssemblyAI's dictation API, and the 16 kHz mono 16-bit PCM
/// geometry the capture and upload agree on. The session auto-releases a held trigger at
/// <see cref="AutoReleaseSeconds"/>, strictly before the hard cap.
/// </summary>
public static class SyncSTTLimits
{
    /// <summary>Sample rate the capture records at and the request declares.</summary>
    public const int SampleRate = 16_000;

    /// <summary>Maximum audio duration the model accepts per request, in seconds.</summary>
    public const double MaxAudioSeconds = 120;

    /// <summary>
    /// Minimum audio duration worth sending, in seconds. A shorter clip transcribes to nothing, so
    /// it is dropped as a silent no-op rather than paid for.
    /// </summary>
    public const double MinAudioSeconds = 0.1;

    /// <summary><see cref="MinAudioSeconds"/> in samples at <see cref="SampleRate"/>.</summary>
    public const int MinSamples = (int)(MinAudioSeconds * SampleRate);

    /// <summary>Bytes per sample of the 16-bit PCM geometry.</summary>
    public const int BytesPerSample = 2;

    /// <summary>
    /// Channels in the geometry. Every byte-count-to-duration conversion here assumes mono, so a
    /// stereo capture would report half the duration it recorded.
    /// </summary>
    public const int ChannelCount = 1;

    /// <summary>Bit depth of the geometry, derived from <see cref="BytesPerSample"/>.</summary>
    public const int BitDepth = BytesPerSample * 8;

    /// <summary>The fewest raw S16LE bytes worth sending: <see cref="MinSamples"/> × <see cref="BytesPerSample"/>.</summary>
    public const int MinPcmBytes = MinSamples * BytesPerSample;

    /// <summary>Safety margin subtracted from the cap for the auto-release, in seconds.</summary>
    public const double AutoReleaseMargin = 5;

    /// <summary>When a held trigger auto-releases: <see cref="MaxAudioSeconds"/> − <see cref="AutoReleaseMargin"/>.</summary>
    public const double AutoReleaseSeconds = MaxAudioSeconds - AutoReleaseMargin;

    /// <summary>Returns the raw mono S16LE byte count that <paramref name="seconds"/> of audio occupies at <see cref="SampleRate"/>.</summary>
    public static int PcmBytesForSeconds(double seconds) => (int)(seconds * SampleRate) * BytesPerSample;

    /// <summary>
    /// Returns the milliseconds of audio a raw mono S16LE <paramref name="byteCount"/> represents at
    /// <paramref name="rate"/>, or 0 when <paramref name="rate"/> is not positive.
    /// </summary>
    public static int DurationMs(int byteCount, int rate = SampleRate)
    {
        if (rate <= 0)
        {
            return 0;
        }

        return (int)((double)byteCount / BytesPerSample / rate * 1000);
    }
}
