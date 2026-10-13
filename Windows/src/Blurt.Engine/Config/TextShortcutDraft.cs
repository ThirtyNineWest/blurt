namespace Blurt.Engine.Config;

/// <summary>The pure rules of the text shortcut editor: what can be saved, and the caps applied as the user types.</summary>
public static class TextShortcutDraft
{
    /// <summary>
    /// Whether the editor may save: the trigger has letters or digits, the expansion is non-blank,
    /// and no other shortcut (by <see cref="TextShortcutStore.MatchKey"/> in <paramref name="otherKeys"/>)
    /// already uses the phrase.
    /// </summary>
    public static bool CanSave(string trigger, string expansion, IReadOnlySet<string> otherKeys) =>
        TextShortcutStore.MatchKey(trigger).Length > 0
        && expansion.TrimmedNonEmpty() is not null
        && !DuplicatesAnother(trigger, otherKeys);

    /// <summary>Whether another shortcut's match key equals <paramref name="trigger"/>'s; an unmatchable trigger duplicates nothing.</summary>
    public static bool DuplicatesAnother(string trigger, IReadOnlySet<string> otherKeys)
    {
        var key = TextShortcutStore.MatchKey(trigger);
        return key.Length > 0 && otherKeys.Contains(key);
    }

    /// <summary>Returns <paramref name="trigger"/> cut to <see cref="TextShortcutStore.TriggerLimit"/> characters.</summary>
    public static string ClampTrigger(string trigger) =>
        trigger.Length > TextShortcutStore.TriggerLimit ? trigger[..TextShortcutStore.TriggerLimit] : trigger;

    /// <summary>Returns <paramref name="expansion"/> cut to <see cref="TextShortcutStore.ExpansionLimit"/> characters.</summary>
    public static string ClampExpansion(string expansion) =>
        expansion.Length > TextShortcutStore.ExpansionLimit ? expansion[..TextShortcutStore.ExpansionLimit] : expansion;
}
