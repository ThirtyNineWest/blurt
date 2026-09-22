using Blurt.Engine.Diagnostics;
using Blurt.Engine.Permissions;
using Blurt.Engine.Windows.Audio;
using Blurt.Engine.Windows.Audio.Interop;
using Microsoft.Win32;
using static Blurt.Engine.Windows.Audio.Interop.AudioClientConstants;

namespace Blurt.Engine.Windows.Permissions;

/// <summary>Determines whether the app may capture from the microphone.</summary>
public static class MicrophoneAccess
{
    private const string LogCategory = "MicrophoneAccess";

    private const string ConsentStore =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";

    private static readonly TimeSpan DeviceProbeTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Reads the microphone privacy setting from the Capability Access Manager consent store at the
    /// machine, user, and user non-packaged-app levels, without opening any device. Returns
    /// <see cref="Classify"/> of the three values, or <see cref="MicrophoneAccessState.Unknown"/> when
    /// the registry cannot be read. Never throws.
    /// </summary>
    public static MicrophoneAccessState Probe()
    {
        try
        {
            var machine = Registry.GetValue($@"HKEY_LOCAL_MACHINE\{ConsentStore}", "Value", null) as string;
            var user = Registry.GetValue($@"HKEY_CURRENT_USER\{ConsentStore}", "Value", null) as string;
            var nonPackaged = Registry.GetValue($@"HKEY_CURRENT_USER\{ConsentStore}\NonPackaged", "Value", null) as string;
            return Classify(machine, user, nonPackaged);
        }
        catch (Exception e)
        {
            BlurtLog.Error(LogCategory, $"consent store read failed: {e.Message}");
            return MicrophoneAccessState.Unknown;
        }
    }

    /// <summary>
    /// Maps the consent-store <c>Value</c> strings (null when absent) to an access state: any
    /// <c>"Deny"</c> is <see cref="MicrophoneAccessState.Denied"/>; <c>"Allow"</c> at every present
    /// level, or nothing present, is <see cref="MicrophoneAccessState.Granted"/>, since Windows treats
    /// an absent consent value as Allow; any other value is <see cref="MicrophoneAccessState.Unknown"/>.
    /// Comparison ignores case.
    /// </summary>
    public static MicrophoneAccessState Classify(string? machine, string? user, string? nonPackaged)
    {
        string[] present = [.. new[] { machine, user, nonPackaged }.OfType<string>()];
        if (present.Any(static v => string.Equals(v, "Deny", StringComparison.OrdinalIgnoreCase)))
        {
            return MicrophoneAccessState.Denied;
        }

        if (present.All(static v => string.Equals(v, "Allow", StringComparison.OrdinalIgnoreCase)))
        {
            return MicrophoneAccessState.Granted;
        }

        return MicrophoneAccessState.Unknown;
    }

    /// <summary>
    /// Opens, starts, and stops a stream on the default capture endpoint on a dedicated MTA thread,
    /// waiting at most 3 seconds. Returns <see cref="MicrophoneAccessState.Denied"/> for
    /// <c>E_ACCESSDENIED</c>, <see cref="MicrophoneAccessState.NoDevice"/> when no endpoint exists,
    /// <see cref="MicrophoneAccessState.Granted"/> on success, and
    /// <see cref="MicrophoneAccessState.Unknown"/> for a timeout or any other failure. Lights the
    /// microphone-in-use indicator briefly. Never throws.
    /// </summary>
    public static MicrophoneAccessState ProbeDevice()
    {
        var state = MicrophoneAccessState.Unknown;
        try
        {
            var thread = new Thread(() => state = ProbeDeviceOnCurrentThread())
            {
                IsBackground = true,
                Name = "Blurt mic probe",
            };
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
            if (!thread.Join(DeviceProbeTimeout))
            {
                BlurtLog.Error(LogCategory, $"device probe timed out after {DeviceProbeTimeout.TotalSeconds:F0} s");
                return MicrophoneAccessState.Unknown;
            }
        }
        catch (Exception e)
        {
            BlurtLog.Error(LogCategory, $"device probe failed: {e.Message}");
            return MicrophoneAccessState.Unknown;
        }

        return state;
    }

    private static MicrophoneAccessState ProbeDeviceOnCurrentThread()
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
            BlurtLog.Info(LogCategory, $"device probe: {state} (0x{hr:X8})");
            return state;
        }
        catch (Exception e)
        {
            BlurtLog.Error(LogCategory, $"device probe failed: {e.Message}");
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
