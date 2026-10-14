namespace Blurt.Engine.Config;

/// <summary>
/// Persists the read-aloud switch. While on, a press over a selected span of text reads it aloud
/// instead of dictating. It is an experimental opt-in, so an unset key reads as off.
/// </summary>
public sealed class SelectionSpeechStore(ISettingsStore store)
{
    /// <summary>The settings key holding the switch.</summary>
    public static string SettingsKey => DefaultsKey.SelectionSpeech.Key();

    /// <summary>Whether read-aloud is on. An unset key reads as off.</summary>
    public bool IsEnabled
    {
        get => store.GetBool(SettingsKey) ?? false;
        set => store.Set(SettingsKey, value);
    }
}
