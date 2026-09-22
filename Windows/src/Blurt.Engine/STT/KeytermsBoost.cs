using System.Text;

namespace Blurt.Engine.STT;

/// <summary>
/// The user's key terms as the dictation request's <c>config.keyterms_prompt</c>: a flat list of
/// spellings to bias recognition toward, sent beside <see cref="STTPrompt"/>'s prose. The route
/// accepts <c>keyterms</c> and <c>word_boost</c> as legacy aliases and rejects a request carrying
/// more than one of the three names, so only <c>keyterms_prompt</c> is ever sent.
/// </summary>
public static class KeytermsBoost
{
    /// <summary>
    /// The cap on <c>keyterms_prompt</c> summed across every term, counted in UTF-8 bytes: the
    /// documented unit is characters, and bytes can only overestimate a term's cost. A different
    /// number from <see cref="STTPrompt.CharacterCap"/>. Over it the whole request is a 400.
    /// </summary>
    public const int CharacterCap = 2048;

    /// <summary>
    /// The field's other cap: at most 100 terms however short. A comma-split free-text list can
    /// exceed it while far under <see cref="CharacterCap"/>, and over it the whole request is a 400.
    /// </summary>
    public const int TermCap = 100;

    /// <summary>
    /// Returns the terms to send: each term trimmed, blanks skipped, taken in the user's order
    /// while the running UTF-8 total fits <see cref="CharacterCap"/> and the list is under
    /// <see cref="TermCap"/>. The first term that breaches either cap ends the list. Terms are
    /// never split. Returns an empty list when nothing fits, which the encoder reads as "omit the
    /// field".
    /// </summary>
    public static IReadOnlyList<string> Fitted(IEnumerable<string> terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        var included = new List<string>();
        var remaining = CharacterCap;
        foreach (var raw in terms)
        {
            if (included.Count >= TermCap)
            {
                break;
            }

            if (raw.TrimmedNonEmpty() is not { } term)
            {
                continue;
            }

            var cost = Encoding.UTF8.GetByteCount(term);
            if (cost > remaining)
            {
                break;
            }

            included.Add(term);
            remaining -= cost;
        }

        return included;
    }
}
