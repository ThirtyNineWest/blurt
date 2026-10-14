namespace Blurt.Engine.Config;

/// <summary>
/// Persists the developer-mode switch. While on, each completed dictation is appended to the
/// developer-mode log — that gate is the switch's only effect.
/// </summary>
public sealed class DeveloperModeStore(ISettingsStore store)
{
    /// <summary>The settings key holding the switch.</summary>
    public static string SettingsKey => DefaultsKey.DeveloperMode.Key();

    /// <summary>Whether developer mode is on. An unset key reads as off.</summary>
    public bool IsEnabled
    {
        get => store.GetBool(SettingsKey) ?? false;
        set => store.Set(SettingsKey, value);
    }
}
