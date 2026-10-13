using System.Text.Json;
using Blurt.Engine.Pipeline;

namespace Blurt.Engine.Config;

/// <summary>
/// Storage for the user's text shortcuts, as one JSON list. The pipeline reads
/// <see cref="Shortcuts"/> at every transcript, so an edit applies to the next dictation.
/// </summary>
public sealed class TextShortcutStore(ISettingsStore store)
{
    /// <summary>How many shortcuts the user may define.</summary>
    public const int ShortcutLimit = 200;

    /// <summary>The most characters a trigger may hold.</summary>
    public const int TriggerLimit = 60;

    /// <summary>The most characters an expansion may hold.</summary>
    public const int ExpansionLimit = 2000;

    private static readonly JsonSerializerOptions DecodeOptions = new()
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };

    /// <summary>The settings key holding the JSON-encoded list.</summary>
    public static string SettingsKey => DefaultsKey.TextShortcuts.Key();

    /// <summary>
    /// The user's shortcuts, in their own order, normalized on the way out as well as in. Reads an
    /// unset or undecodable slot as no shortcuts.
    /// </summary>
    public IReadOnlyList<TextShortcut> Shortcuts
    {
        get => Decode(store.GetString(SettingsKey) ?? string.Empty);
        set => store.Set(SettingsKey, JsonSerializer.Serialize(Normalized(value)));
    }

    /// <summary>The shortcuts behind a raw slot value: the same decode and normalization the getter uses.</summary>
    public IReadOnlyList<TextShortcut> Decode(string raw)
    {
        if (raw.Length == 0)
        {
            return [];
        }

        List<TextShortcut?>? decoded;
        try
        {
            decoded = JsonSerializer.Deserialize<List<TextShortcut?>>(raw, DecodeOptions);
        }
        catch (JsonException)
        {
            return [];
        }

        if (decoded is null || decoded.Any(shortcut => shortcut is null))
        {
            return [];
        }

        return Normalized(decoded.Select(shortcut => shortcut!));
    }

    /// <summary>
    /// The identity two triggers share when the expander cannot tell them apart: letters and digits
    /// only, lowercased, separators dropped. Empty for a trigger with no letters or digits.
    /// </summary>
    public static string MatchKey(string trigger) =>
        string.Concat(TextShortcutExpander.Words(trigger)).ToLowerInvariant();

    /// <summary>
    /// The funnel every list passes through, on read and on write: trimmed fields within their caps,
    /// entries with a blank or unmatchable trigger or a blank expansion dropped, triggers deduplicated
    /// by <see cref="MatchKey"/> (first wins), at most <see cref="ShortcutLimit"/> kept. Idempotent.
    /// </summary>
    public static IReadOnlyList<TextShortcut> Normalized(IEnumerable<TextShortcut> shortcuts)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<TextShortcut>();
        foreach (var shortcut in shortcuts)
        {
            if (result.Count >= ShortcutLimit)
            {
                break;
            }

            var trigger = shortcut.Trigger.TrimmedNonEmpty();
            var expansion = shortcut.Expansion.TrimmedNonEmpty();
            if (trigger is null || expansion is null)
            {
                continue;
            }

            trigger = TextShortcutDraft.ClampTrigger(trigger);
            expansion = TextShortcutDraft.ClampExpansion(expansion);
            var key = MatchKey(trigger);
            if (key.Length == 0 || !seen.Add(key))
            {
                continue;
            }

            result.Add(new TextShortcut(shortcut.Id, trigger, expansion));
        }

        return result;
    }
}
