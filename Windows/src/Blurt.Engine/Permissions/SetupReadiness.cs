namespace Blurt.Engine.Permissions;

/// <summary>The first-run setup gate: what counts as "fully configured", and how briskly to poll for it.</summary>
public static class SetupReadiness
{
    /// <summary>Poll cadence while the user is still setting up: brisk, since the window is on screen.</summary>
    public static readonly TimeSpan SettingUpPollInterval = TimeSpan.FromSeconds(1);

    /// <summary>Poll cadence once fully configured: coasting, only to catch a rare revocation.</summary>
    public static readonly TimeSpan ReadyPollInterval = TimeSpan.FromSeconds(5);

    /// <summary>Whether the app is fully configured: every permission granted and an API key saved.</summary>
    public static bool IsReady(PermissionStatus permissions, bool hasApiKey) => permissions.AllGranted && hasApiKey;

    /// <summary>How long to wait before the next permission poll, given where setup stands.</summary>
    public static TimeSpan PollInterval(bool isReady) => isReady ? ReadyPollInterval : SettingUpPollInterval;
}
