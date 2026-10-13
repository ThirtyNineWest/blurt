using Blurt.Engine.Pipeline;

namespace Blurt.Engine.STT;

/// <summary>
/// Builds the dictation request's <c>config.stt_prompt</c>: the text that came before this
/// utterance, which the model reads as continuity (vocabulary, spelling, mid-sentence
/// continuation) while transcribing only the current audio.
/// <para>
/// Carries exactly two fields of <see cref="TranscriptionContext"/>, oldest first: the user's
/// recent dictations, then the text before the cursor. Every other field stays on the machine, and
/// the key terms ride their own field (<see cref="KeytermsBoost"/>). The prompt is prose with no
/// heading or directive: the service treats the field as a description of the audio, not a
/// command. <c>prompt</c> is this field's legacy alias and the route rejects a request carrying
/// both.
/// </para>
/// </summary>
public static class STTPrompt
{
    /// <summary>
    /// The cap a request's <c>stt_prompt</c> must fit, in Unicode scalars (<see cref="System.Text.Rune"/>s).
    /// Over it the route rejects the whole request with a 400 before reading the audio. The route
    /// validates the field twice, and the inner validator caps it at 4096 even though the
    /// reference documents 6000. Scalars are the unit the server counts: UTF-16 units over-count
    /// astral characters and graphemes under-count joined emoji into a 400.
    /// </summary>
    public const int CharacterCap = 4096;

    /// <summary>
    /// What joins the turns. A newline, because the pieces are separate utterances and a space
    /// would invent a sentence boundary. Each join costs a scalar out of <see cref="CharacterCap"/>.
    /// </summary>
    public const string TurnSeparator = "\n";

    /// <summary>
    /// Returns the prompt for <paramref name="context"/>: the non-blank recent dictations, then the
    /// trimmed prior text, joined by <see cref="TurnSeparator"/> and fitted to
    /// <see cref="CharacterCap"/>. Returns the empty string when there is nothing to send, which
    /// the encoder reads as "omit the field". History entries the prior text already ends with are
    /// sent once, as part of the prior text.
    /// </summary>
    public static string Text(TranscriptionContext? context)
    {
        if (context is null)
        {
            return "";
        }

        var recent = context.RecentTranscripts.Select(t => t.TrimmedNonEmpty()).OfType<string>().ToList();

        // Scrubbed where the wire string is built: the log's prior text and the paste separator
        // read the field text as captured.
        var scrubbed = context.PriorText is null
            ? null
            : TextShortcutExpander.RedactingExpansions(context.PriorText, context.TextShortcuts);
        if (scrubbed.TrimmedNonEmpty() is not { } prior)
        {
            return Fitted(recent);
        }

        var turns = WithoutTurnsAlreadyEnding(prior, recent);
        turns.Add(prior);
        return Fitted(turns);
    }

    /// <summary>
    /// <paramref name="recent"/> without the trailing entries <paramref name="prior"/> already ends
    /// with. Walks newest first, peeling each matched entry and any whitespace before it off the
    /// prior text, and stops at the first entry that does not match: an earlier entry the chunk
    /// does not end with is genuine history the chunk cannot speak for.
    /// </summary>
    private static List<string> WithoutTurnsAlreadyEnding(string prior, List<string> recent)
    {
        var kept = new List<string>(recent);
        var tail = prior;
        while (kept.Count > 0 && tail.EndsWith(kept[^1], StringComparison.Ordinal))
        {
            tail = tail[..^kept[^1].Length].TrimEnd();
            kept.RemoveAt(kept.Count - 1);
        }

        return kept;
    }

    /// <summary>
    /// <paramref name="turns"/> joined and cut to <see cref="CharacterCap"/> scalars, keeping a
    /// contiguous newest run: skipping an over-long entry to keep an older one would send a passage
    /// with a hole in it. A single entry longer than the whole budget, with nothing newer kept, is
    /// clipped to its tail, the end nearest the cursor.
    /// </summary>
    private static string Fitted(List<string> turns)
    {
        var separatorCost = RuneCount(TurnSeparator);
        var kept = new List<string>();
        var remaining = CharacterCap;
        for (var i = turns.Count - 1; i >= 0; i--)
        {
            var turn = turns[i];
            var cost = RuneCount(turn) + (kept.Count == 0 ? 0 : separatorCost);
            if (cost <= remaining)
            {
                kept.Add(turn);
                remaining -= cost;
            }
            else
            {
                if (kept.Count == 0)
                {
                    kept.Add(RuneSuffix(turn, remaining));
                }

                break;
            }
        }

        kept.Reverse();
        return string.Join(TurnSeparator, kept);
    }

    private static int RuneCount(string s) => s.EnumerateRunes().Count();

    /// <summary>The last <paramref name="count"/> scalars of <paramref name="s"/>. A surrogate pair is never split.</summary>
    private static string RuneSuffix(string s, int count)
    {
        var start = s.Length;
        for (var taken = 0; taken < count && start > 0; taken++)
        {
            var pair = start >= 2 && char.IsLowSurrogate(s[start - 1]) && char.IsHighSurrogate(s[start - 2]);
            start -= pair ? 2 : 1;
        }

        return s[start..];
    }
}
