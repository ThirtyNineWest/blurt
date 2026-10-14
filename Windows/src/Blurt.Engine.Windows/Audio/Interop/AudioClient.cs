using System.Runtime.InteropServices;
using Blurt.Engine.STT;

namespace Blurt.Engine.Windows.Audio.Interop;

/// <summary>Native <c>WAVEFORMATEX</c>: 18 bytes, packed on 2-byte boundaries.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct WaveFormatEx
{
    public ushort FormatTag;
    public ushort Channels;
    public uint SamplesPerSec;
    public uint AvgBytesPerSec;
    public ushort BlockAlign;
    public ushort BitsPerSample;
    public ushort ExtraSize;

    /// <summary>The PCM format every capture requests: <see cref="SyncSTTLimits"/>'s 16 kHz mono 16-bit geometry.</summary>
    public static WaveFormatEx CaptureRequest => new()
    {
        FormatTag = 1,
        Channels = SyncSTTLimits.ChannelCount,
        SamplesPerSec = SyncSTTLimits.SampleRate,
        AvgBytesPerSec = SyncSTTLimits.SampleRate * SyncSTTLimits.BytesPerSample * SyncSTTLimits.ChannelCount,
        BlockAlign = SyncSTTLimits.BytesPerSample * SyncSTTLimits.ChannelCount,
        BitsPerSample = SyncSTTLimits.BitDepth,
        ExtraSize = 0,
    };

    /// <summary>The PCM format playback requests: mono 16-bit at <paramref name="sampleRate"/> Hz.</summary>
    public static WaveFormatEx RenderRequest(int sampleRate) => new()
    {
        FormatTag = 1,
        Channels = 1,
        SamplesPerSec = (uint)sampleRate,
        AvgBytesPerSec = (uint)sampleRate * 2,
        BlockAlign = 2,
        BitsPerSample = 16,
        ExtraSize = 0,
    };
}

/// <summary>Flags, codes, and durations of the WASAPI audio client.</summary>
internal static class AudioClientConstants
{
    public const int ShareModeShared = 0;

    public const uint StreamFlagsEventCallback = 0x00040000;
    public const uint StreamFlagsAutoConvertPcm = 0x80000000;
    public const uint StreamFlagsSrcDefaultQuality = 0x08000000;

    public const uint BufferFlagsSilent = 2;

    /// <summary><c>AUDCLNT_S_BUFFER_EMPTY</c>: a success code meaning no packet is ready.</summary>
    public const int BufferEmpty = 0x08890001;

    /// <summary>100 ms in 100-nanosecond units.</summary>
    public const long BufferDuration100Ms = 1_000_000;
}

/// <summary><c>IAudioClient</c>. Methods are in vtable order.</summary>
[ComImport]
[Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    [PreserveSig]
    int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, ref WaveFormatEx format, ref Guid audioSessionGuid);

    [PreserveSig]
    int GetBufferSize(out uint bufferFrames);

    [PreserveSig]
    int GetStreamLatency(out long latency);

    [PreserveSig]
    int GetCurrentPadding(out uint paddingFrames);

    [PreserveSig]
    int IsFormatSupported(int shareMode, ref WaveFormatEx format, out IntPtr closestMatch);

    [PreserveSig]
    int GetMixFormat(out IntPtr deviceFormat);

    [PreserveSig]
    int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);

    [PreserveSig]
    int Start();

    [PreserveSig]
    int Stop();

    [PreserveSig]
    int Reset();

    [PreserveSig]
    int SetEventHandle(IntPtr eventHandle);

    [PreserveSig]
    int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
}

/// <summary><c>IAudioCaptureClient</c>. Methods are in vtable order.</summary>
[ComImport]
[Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioCaptureClient
{
    [PreserveSig]
    int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);

    [PreserveSig]
    int ReleaseBuffer(uint framesRead);

    [PreserveSig]
    int GetNextPacketSize(out uint framesInNextPacket);
}

/// <summary>Opens the default console capture endpoint.</summary>
internal static class CaptureEndpoint
{
    /// <summary>
    /// Activates an <see cref="IAudioClient"/> on the default console capture endpoint, without
    /// initializing it. Returns 0 with a client the caller must release, or the failing HRESULT with
    /// <paramref name="client"/> null.
    /// </summary>
    /// <exception cref="COMException">The device enumerator could not be created.</exception>
    public static int ActivateDefault(out IAudioClient? client) => Activate(EDataFlow.Capture, out client);

    /// <summary>
    /// Activates an <see cref="IAudioClient"/> on the default console endpoint of <paramref name="flow"/>, without
    /// initializing it. Returns 0 with a client the caller must release, or the failing HRESULT with
    /// <paramref name="client"/> null.
    /// </summary>
    /// <exception cref="COMException">The device enumerator could not be created.</exception>
    public static int Activate(EDataFlow flow, out IAudioClient? client)
    {
        client = null;
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        try
        {
            enumerator = MmDeviceApi.CreateEnumerator();
            var hr = enumerator.GetDefaultAudioEndpoint(flow, ERole.Console, out device);
            if (hr < 0)
            {
                return hr;
            }

            var iid = typeof(IAudioClient).GUID;
            hr = device.Activate(ref iid, MmDeviceApi.ClsctxAll, IntPtr.Zero, out var instance);
            if (hr < 0)
            {
                return hr;
            }

            client = (IAudioClient)instance;
            return 0;
        }
        finally
        {
            Release(device);
            Release(enumerator);
        }
    }

    /// <summary>Releases <paramref name="com"/>'s runtime callable wrapper when it is a COM object.</summary>
    public static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            Marshal.ReleaseComObject(com);
        }
    }
}

/// <summary>Opens the default console render endpoint.</summary>
internal static class RenderEndpoint
{
    /// <summary>
    /// Activates an <see cref="IAudioClient"/> on the default console render endpoint, without initializing it.
    /// Returns 0 with a client the caller must release, or the failing HRESULT with <paramref name="client"/> null.
    /// </summary>
    /// <exception cref="COMException">The device enumerator could not be created.</exception>
    public static int ActivateDefault(out IAudioClient? client) => CaptureEndpoint.Activate(EDataFlow.Render, out client);
}

/// <summary>COM apartment initialization for capture and render threads.</summary>
internal static class Ole32
{
    public const uint CoinitMultithreaded = 0;

    [DllImport("ole32.dll")]
    public static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll")]
    public static extern void CoUninitialize();
}
