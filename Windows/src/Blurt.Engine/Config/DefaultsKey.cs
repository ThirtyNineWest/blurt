namespace Blurt.Engine.Config;

/// <summary>
/// Every settings key an engine store persists. The name is the unprefixed half of the on-disk
/// key; renaming a member silently abandons every existing user's setting.
/// </summary>
public enum DefaultsKey
{
    /// <summary>The trigger key's virtual-key code.</summary>
    TriggerKeyCode,

    /// <summary>Whether the trigger activates on tap, hold, or both.</summary>
    TriggerActivation,

    /// <summary>The recording cue sound pack.</summary>
    SoundPack,

    /// <summary>The user's key terms.</summary>
    KeyTerms,

    /// <summary>Whether developer-mode logging is on.</summary>
    DeveloperMode,

    /// <summary>Whether enhanced transcripts are on.</summary>
    EnhancedTranscripts,

    /// <summary>The single custom-style field that predates style profiles; still read for migration.</summary>
    CustomStyle,

    /// <summary>The style profile list.</summary>
    StyleProfiles,

    /// <summary>The active style profile's id.</summary>
    ActiveStyleProfile,

    /// <summary>The overlay's custom origin, x.</summary>
    OverlayCustomOriginX,

    /// <summary>The overlay's custom origin, y.</summary>
    OverlayCustomOriginY,

    /// <summary>When the update check last ran.</summary>
    LastUpdateCheck,

    /// <summary>The JSON list of spoken-phrase → text expansions.</summary>
    TextShortcuts,

    /// <summary>The experimental read-selection-aloud switch.</summary>
    SelectionSpeech,

    /// <summary>Read-aloud's playback speed.</summary>
    ReadAloudSpeed,

    /// <summary>Whether read-aloud rewrites the text for listening first.</summary>
    ReadAloudSkipsJargon,

    /// <summary>Whether a hold over a selection asks about it.</summary>
    SelectionAsk,
}

/// <summary>Key composition for <see cref="DefaultsKey"/>.</summary>
public static class DefaultsKeys
{
    /// <summary>Every case, in declaration order.</summary>
    public static IReadOnlyList<DefaultsKey> All { get; } = Enum.GetValues<DefaultsKey>();

    /// <summary>Returns the unprefixed on-disk name: the member name.</summary>
    public static string RawValue(this DefaultsKey key) => key.ToString();

    /// <summary>Returns the key this case reads and writes under <see cref="HostIdentity.Current"/>.</summary>
    public static string Key(this DefaultsKey key) => HostIdentity.Current.SettingsKey(key.RawValue());

    /// <summary>Returns the key this case would write under <paramref name="identity"/>.</summary>
    public static string Key(this DefaultsKey key, HostIdentity identity) => identity.SettingsKey(key.RawValue());
}
