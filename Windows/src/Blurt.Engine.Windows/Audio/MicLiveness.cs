namespace Blurt.Engine.Windows.Audio;

/// <summary>
/// The capture's liveness gate: a start is confirmed only once the device delivers a packet whose
/// level is above <see cref="SilenceFloorDb"/>, and fails closed after <see cref="Timeout"/>.
/// </summary>
public static class MicLiveness
{
    /// <summary>The RMS level, in dBFS, a packet must exceed to count as live input.</summary>
    public const double SilenceFloorDb = -50;

    /// <summary>How long a start waits for a live packet before failing.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2.5);

    /// <summary>
    /// Returns true when the RMS level of <paramref name="s16le"/> is strictly above
    /// <see cref="SilenceFloorDb"/>. Empty and all-zero packets are never live.
    /// </summary>
    public static bool IsLive(ReadOnlySpan<byte> s16le) => LevelMeter.DbFs(s16le) > SilenceFloorDb;
}
