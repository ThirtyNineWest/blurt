using System.Diagnostics;
using Blurt.Engine.Permissions;

namespace Blurt.Engine.Windows.Permissions;

/// <summary>Reads the app's microphone permission via an injected probe, without prompting.</summary>
public sealed class PermissionsChecker(Func<MicrophoneAccessState> probe)
{
    /// <summary>The current grant state: microphone access is granted only when the probe reports <see cref="MicrophoneAccessState.Granted"/>.</summary>
    public PermissionStatus Check() => new(probe() == MicrophoneAccessState.Granted);

    /// <summary>Opens Settings to Privacy &gt; Microphone.</summary>
    public static void OpenMicrophonePrivacySettings() =>
        Process.Start(new ProcessStartInfo("ms-settings:privacy-microphone") { UseShellExecute = true });
}
