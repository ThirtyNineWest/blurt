using System.Buffers.Binary;

namespace Blurt.Engine.Windows.Tests.Audio;

/// <summary>Synthetic S16LE buffers for the meter and liveness tests.</summary>
internal static class Pcm
{
    public static byte[] Silence(int samples) => new byte[samples * 2];

    public static byte[] Constant(short value, int samples)
    {
        var bytes = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), value);
        }

        return bytes;
    }

    /// <summary>A 440 Hz sine at <paramref name="amplitude"/> (0 to 1 of full scale), a whole number of cycles long.</summary>
    public static byte[] Sine(double amplitude, int samples = 16_000)
    {
        var bytes = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
        {
            var value = amplitude * short.MaxValue * Math.Sin(2 * Math.PI * 440 * i / 16_000.0);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), (short)Math.Round(value));
        }

        return bytes;
    }
}
