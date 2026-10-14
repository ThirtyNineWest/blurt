namespace Blurt.Engine.Config;

/// <summary>
/// Persists the "enhanced transcripts" switch. On by default — every dictation request carries
/// <c>llm_instruction</c> regardless, and this flag decides which of the verbatim transcript or the
/// cleanup rewrite gets pasted.
/// </summary>
public sealed class EnhancedTranscriptsStore(ISettingsStore store)
{
    /// <summary>The settings key holding the switch.</summary>
    public static string SettingsKey => DefaultsKey.EnhancedTranscripts.Key();

    /// <summary>The value an unset key reads as.</summary>
    public const bool DefaultValue = true;

    /// <summary>Whether enhanced transcripts are on. An unset key reads as on.</summary>
    public bool IsEnabled
    {
        get => store.GetBool(SettingsKey) ?? DefaultValue;
        set => store.Set(SettingsKey, value);
    }
}
