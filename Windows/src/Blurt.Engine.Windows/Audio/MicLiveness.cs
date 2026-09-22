namespace Blurt.Engine.Windows.Audio;

/// <summary>
/// The capture's liveness gate: a start is confirmed only once the device delivers a packet whose
/// level is above <see cref="SilenceFloorDb"/>, and fails closed after <see cref="Timeout"/>.
/// </summary>
/// <remarks>
/// The floor separates digital silence (zero-filled packets from a device that is not yet
/// delivering) from any analog input. One least-significant bit of a 16-bit sample already reads
/// about -90 dBFS, so a live mic in a silent room clears it. Raising it toward a speech level turns
/// the gate into voice detection and fails every press in a quiet room.
/// </remarks>
public static class MicLiveness
{
    /// <summary>The RMS level, in dBFS, a packet must exceed to count as live input. Distinct from <see cref="LevelMeter.FloorDb"/>.</summary>
    public const double SilenceFloorDb = -115;

    /// <summary>How long a start waits for a live packet before failing.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2.5);

    /// <summary>
    /// Returns true when the RMS level of <paramref name="s16le"/> is strictly above
    /// <see cref="SilenceFloorDb"/>. Empty and all-zero packets are never live.
    /// </summary>
    public static bool IsLive(ReadOnlySpan<byte> s16le) => LevelMeter.DbFs(s16le) > SilenceFloorDb;
}
