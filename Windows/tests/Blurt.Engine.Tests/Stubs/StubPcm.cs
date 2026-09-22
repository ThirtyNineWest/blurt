using Blurt.Engine.STT;

namespace Blurt.Engine.Tests.Stubs;

/// <summary>Canned capture audio sized against <see cref="SyncSTTLimits.MinPcmBytes"/>. Each read returns a fresh array.</summary>
public static class StubPcm
{
    /// <summary>Comfortably above the floor, so a press-release flow reaches transcribe.</summary>
    public static byte[] AboveMinimum => new byte[SyncSTTLimits.MinPcmBytes * 2];

    /// <summary>One sample short of the floor, so the session drops the clip as too short.</summary>
    public static byte[] BelowMinimum => new byte[SyncSTTLimits.MinPcmBytes - SyncSTTLimits.BytesPerSample];

    /// <summary>Every byte value 0 to 255, repeated past the floor, for byte-exactness assertions.</summary>
    public static byte[] EveryByteValueAboveMinimum
    {
        get
        {
            var repeats = (SyncSTTLimits.MinPcmBytes / 256) + 1;
            var bytes = new byte[repeats * 256];
            for (var i = 0; i < bytes.Length; i++)
            {
                bytes[i] = (byte)(i % 256);
            }

            return bytes;
        }
    }
}
