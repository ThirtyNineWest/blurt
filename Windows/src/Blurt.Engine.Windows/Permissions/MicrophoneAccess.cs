using Blurt.Engine.Diagnostics;
using Blurt.Engine.Permissions;
using Blurt.Engine.Windows.Audio;
using Blurt.Engine.Windows.Audio.Interop;
using static Blurt.Engine.Windows.Audio.Interop.AudioClientConstants;

namespace Blurt.Engine.Windows.Permissions;

/// <summary>Determines whether the app may capture from the default microphone.</summary>
public static class MicrophoneAccess
{
    private const string LogCategory = "MicrophoneAccess";

    /// <summary>
    /// Opens, starts, and stops a stream on the default capture endpoint on a dedicated MTA thread
    /// and blocks until it finishes. Returns <see cref="MicrophoneAccessState.Denied"/> for
    /// <c>E_ACCESSDENIED</c>, <see cref="MicrophoneAccessState.NoDevice"/> when no endpoint exists,
    /// <see cref="MicrophoneAccessState.Granted"/> on success, and
    /// <see cref="MicrophoneAccessState.Unknown"/> for any other failure. Never throws.
    /// </summary>
    public static MicrophoneAccessState Probe()
    {
        var state = MicrophoneAccessState.Unknown;
        try
        {
            var thread = new Thread(() => state = ProbeOnCurrentThread()) { IsBackground = true, Name = "Blurt mic probe" };
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
            thread.Join();
        }
        catch (Exception e)
        {
            BlurtLog.Error(LogCategory, $"probe failed: {e.Message}");
            return MicrophoneAccessState.Unknown;
        }

        return state;
    }

    private static MicrophoneAccessState ProbeOnCurrentThread()
    {
        IAudioClient? client = null;
        try
        {
            var hr = CaptureEndpoint.ActivateDefault(out client);
            if (hr >= 0)
            {
                var format = WaveFormatEx.CaptureRequest;
                var session = Guid.Empty;
                hr = client!.Initialize(
                    ShareModeShared,
                    StreamFlagsAutoConvertPcm | StreamFlagsSrcDefaultQuality,
                    BufferDuration100Ms,
                    0,
                    ref format,
                    ref session);
            }

            if (hr >= 0)
            {
                hr = client!.Start();
            }

            if (hr >= 0)
            {
                hr = client!.Stop();
            }

            var state = hr >= 0 ? MicrophoneAccessState.Granted : StateFor(hr);
            BlurtLog.Info(LogCategory, $"probe: {state} (0x{hr:X8})");
            return state;
        }
        catch (Exception e)
        {
            BlurtLog.Error(LogCategory, $"probe failed: {e.Message}");
            return MicrophoneAccessState.Unknown;
        }
        finally
        {
            CaptureEndpoint.Release(client);
        }
    }

    private static MicrophoneAccessState StateFor(int hresult) => MicCaptureException.FromHResult(hresult).Failure switch
    {
        MicCaptureFailure.AccessDenied => MicrophoneAccessState.Denied,
        MicCaptureFailure.NoDevice => MicrophoneAccessState.NoDevice,
        _ => MicrophoneAccessState.Unknown,
    };
}
