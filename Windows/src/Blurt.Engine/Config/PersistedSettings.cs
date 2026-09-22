namespace Blurt.Engine.Config;

/// <summary>The "reset to a clean state" sweep over every settings key an engine store persists.</summary>
public static class PersistedSettings
{
    /// <summary>Clears every <see cref="DefaultsKey"/> in <paramref name="store"/>, leaving unrelated keys untouched.</summary>
    public static void ResetAll(ISettingsStore store)
    {
        foreach (var key in DefaultsKeys.All)
        {
            store.Remove(key.Key());
        }
    }
}
