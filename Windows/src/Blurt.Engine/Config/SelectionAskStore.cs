namespace Blurt.Engine.Config;

/// <summary>
/// Persists the ask-about-the-selection switch. It only acts once read-aloud is on, so an unset
/// key reads as on.
/// </summary>
public sealed class SelectionAskStore(ISettingsStore store)
{
    /// <summary>What an unset key reads as.</summary>
    public const bool DefaultValue = true;

    /// <summary>The settings key holding the switch.</summary>
    public static string SettingsKey => DefaultsKey.SelectionAsk.Key();

    /// <summary>Whether a hold over a selection asks about it. An unset key reads as <see cref="DefaultValue"/>.</summary>
    public bool IsEnabled
    {
        get => store.GetBool(SettingsKey) ?? DefaultValue;
        set => store.Set(SettingsKey, value);
    }
}
