using System.Buffers.Binary;

namespace Blurt.Engine.Windows.Audio;

/// <summary>Loudness math over raw S16LE PCM, for the recording meter and the liveness gate.</summary>
public static class LevelMeter
{
    /// <summary>The dBFS reading at or below which the meter shows an empty bar.</summary>
    public const double FloorDb = -50;

    /// <summary>
    /// Returns the RMS level of <paramref name="s16le"/> in dBFS (full scale at 0), or negative
    /// infinity for an empty or all-zero buffer. A trailing odd byte is ignored.
    /// </summary>
    public static double DbFs(ReadOnlySpan<byte> s16le)
    {
        var samples = s16le.Length / 2;
        if (samples == 0)
        {
            return double.NegativeInfinity;
        }

        double sumOfSquares = 0;
        for (var i = 0; i < samples; i++)
        {
            double sample = BinaryPrimitives.ReadInt16LittleEndian(s16le.Slice(i * 2, 2));
            sumOfSquares += sample * sample;
        }

        var rms = Math.Sqrt(sumOfSquares / samples) / 32768.0;
        return 20 * Math.Log10(rms);
    }

    /// <summary>
    /// Maps <paramref name="dbFs"/> linearly from [<see cref="FloorDb"/>, 0] onto [0, 1], clamping
    /// above full scale. The floor, anything below it, and non-finite readings return 0.
    /// </summary>
    public static float Normalized(double dbFs)
    {
        if (!double.IsFinite(dbFs) || !(dbFs > FloorDb))
        {
            return 0;
        }

        return (float)Math.Min(1, (dbFs - FloorDb) / -FloorDb);
    }
}
