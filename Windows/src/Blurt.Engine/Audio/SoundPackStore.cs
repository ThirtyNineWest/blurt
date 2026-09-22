using Blurt.Engine.Config;

namespace Blurt.Engine.Audio;

/// <summary>
/// Persists the chosen <see cref="SoundPack"/> by its id. Defaults to the catalog's default voice
/// when unset or when the stored id isn't one of its voices.
/// </summary>
public sealed class SoundPackStore(ISettingsStore store, SoundPackCatalog catalog)
{
    /// <summary>The settings key holding the selected pack id.</summary>
    public static string SettingsKey => DefaultsKey.SoundPack.Key();

    /// <summary>The persisted pack, or the catalog's default voice when unset or unrecognized.</summary>
    public SoundPack SoundPack
    {
        get => catalog.FromPersisted(store.GetString(SettingsKey));
        set => store.Set(SettingsKey, value.Id);
    }
}
