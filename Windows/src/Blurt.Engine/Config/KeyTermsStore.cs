namespace Blurt.Engine.Config;

/// <summary>
/// Storage for the user's dictation "key terms" — a comma-separated list of domain words sent as
/// the dictation request's keyterms list, so the model favors those exact spellings.
/// </summary>
public sealed class KeyTermsStore(ISettingsStore store)
{
    /// <summary>The settings key holding the raw, comma-separated string the user typed.</summary>
    public static string SettingsKey => DefaultsKey.KeyTerms.Key();

    /// <summary>
    /// The raw string exactly as typed (trimmed), or null when unset or blank. The setter stores
    /// exactly what it is given; normalization lives entirely on the read side, in <see cref="Terms"/>.
    /// </summary>
    public string? Raw
    {
        get => store.GetString(SettingsKey).TrimmedNonEmpty();
        set => store.Set(SettingsKey, value ?? string.Empty);
    }

    /// <summary>The stored terms parsed into a clean list: split on commas, trimmed, blanks and duplicates removed.</summary>
    public IReadOnlyList<string> Terms => Parse(Raw);

    /// <summary>
    /// Splits <paramref name="text"/> on commas, trims each piece, drops blanks, and removes
    /// duplicates case-insensitively while keeping the first spelling.
    /// </summary>
    public static IReadOnlyList<string> Parse(string? text)
    {
        if (text is null)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var piece in text.Split(','))
        {
            var term = piece.TrimmedNonEmpty();
            if (term is null || !seen.Add(term))
            {
                continue;
            }

            result.Add(term);
        }

        return result;
    }
}
