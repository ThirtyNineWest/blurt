using Blurt.Engine.Config;

namespace Blurt.Engine.Hotkey;

/// <summary>
/// Persists the chosen dictation <see cref="Hotkey.TriggerKey"/> as its keycode. Defaults to right
/// Control when unset or when the stored code isn't one of the curated options.
/// </summary>
public sealed class TriggerKeyStore(ISettingsStore store)
{
    /// <summary>The settings key holding the trigger keycode.</summary>
    public static string DefaultsKey => Blurt.Engine.Config.DefaultsKey.TriggerKeyCode.Key();

    /// <summary>The persisted trigger key, decoded through <see cref="TriggerKeys.FromPersisted(int)"/>.</summary>
    public TriggerKey TriggerKey
    {
        get => TriggerKeys.FromPersisted(store.GetInt(DefaultsKey) ?? 0);
        set => store.Set(DefaultsKey, value.KeyCode());
    }
}
