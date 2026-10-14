using Blurt.Engine.Config;

namespace Blurt.Engine.Hotkey;

/// <summary>
/// Persists the chosen <see cref="TriggerActivation"/> as its raw value. Defaults to tap-or-hold when
/// unset or when the stored value isn't one of the known modes.
/// </summary>
public sealed class TriggerActivationStore(ISettingsStore store)
{
    private static readonly string DefaultsKey = Blurt.Engine.Config.DefaultsKey.TriggerActivation.Key();

    /// <summary>The persisted activation mode, decoded through <see cref="TriggerActivations.FromPersisted(string?)"/>.</summary>
    public TriggerActivation Activation
    {
        get => TriggerActivations.FromPersisted(store.GetString(DefaultsKey));
        set => store.Set(DefaultsKey, value.RawValue());
    }
}
