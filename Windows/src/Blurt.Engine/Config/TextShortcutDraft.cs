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

    /// <summary>
    /// Returns <paramref name="trigger"/> cut to at most <see cref="TextShortcutStore.TriggerLimit"/> UTF-16
    /// units, never ending inside a surrogate pair.
    /// </summary>
    public static string ClampTrigger(string trigger) => Clamp(trigger, TextShortcutStore.TriggerLimit);

    /// <summary>
    /// Returns <paramref name="expansion"/> cut to at most <see cref="TextShortcutStore.ExpansionLimit"/> UTF-16
    /// units, never ending inside a surrogate pair.
    /// </summary>
    public static string ClampExpansion(string expansion) => Clamp(expansion, TextShortcutStore.ExpansionLimit);

    private static string Clamp(string text, int limit)
    {
        if (text.Length <= limit)
        {
            return text;
        }

        return char.IsHighSurrogate(text[limit - 1]) ? text[..(limit - 1)] : text[..limit];
    }
}
