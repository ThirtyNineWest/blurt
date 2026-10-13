using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Blurt.Engine.Config;

namespace Blurt.Engine.Pipeline;

/// <summary>
/// Replaces each spoken shortcut trigger in a finished transcript with its expansion. Pure and
/// local: it runs on the text the dictation API returned, between the response and the paste.
/// Matching is case-insensitive; the separators between a trigger's words are loose (any run of
/// non-letter/digit characters, or none), except sentence punctuation followed by whitespace, which
/// ends the match; a trigger never matches inside a longer word. One pass, longest trigger first,
/// so an expansion is never itself re-expanded.
/// </summary>
public static class TextShortcutExpander
{
    /// <summary>
    /// The fewest characters of an expansion's tail <see cref="RedactingExpansions"/> treats as a
    /// clipped copy at the very start of the text. Below this an overlap is as likely to be
    /// coincidence (a shared "com" or ".") as a clip.
    /// </summary>
    public const int MinimumClippedOverlap = 4;

    private const string WordClass = @"\p{L}\p{M}\p{N}";
    private const string SentencePunctuation = ".,!?;:";
    private const string SeparatorRun = @"(?:[^\p{L}\p{M}\p{N}.,!?;:]|[.,!?;:](?!\s))*";
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>
    /// Returns <paramref name="text"/> with every trigger in <paramref name="shortcuts"/> replaced by
    /// its expansion. A text that is only a trigger, surrounding whitespace and trailing sentence
    /// punctuation aside, becomes exactly the expansion (the whitespace kept). Shortcuts whose
    /// trigger holds no letters or digits never match.
    /// </summary>
    public static string Expand(string text, IReadOnlyList<TextShortcut> shortcuts)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(shortcuts);
        var ordered = shortcuts
            .Select(shortcut => (Shortcut: shortcut, Pattern: Pattern(shortcut.Trigger)))
            .Where(pair => pair.Pattern is not null)
            .OrderByDescending(pair => pair.Pattern!.Length)
            .ToList();
        if (ordered.Count == 0)
        {
            return text;
        }

        var alternation = string.Join("|", ordered.Select(pair => $"({pair.Pattern})"));
        var lone = new Regex($"^(\\s*)(?:{alternation})[{SentencePunctuation}]*(\\s*)$", Options).Match(text);
        if (lone.Success && ShortcutIndex(lone, groupOffset: 2, ordered.Count) is { } loneIndex)
        {
            return lone.Groups[1].Value + ordered[loneIndex].Shortcut.Expansion + lone.Groups[ordered.Count + 2].Value;
        }

        var inline = new Regex($"(?<![{WordClass}])(?:{alternation})(?![{WordClass}])", Options);
        var result = new StringBuilder();
        var cursor = 0;
        foreach (Match match in inline.Matches(text))
        {
            if (ShortcutIndex(match, groupOffset: 1, ordered.Count) is not { } index)
            {
                continue;
            }

            result.Append(text, cursor, match.Index - cursor);
            result.Append(ordered[index].Shortcut.Expansion);
            cursor = match.Index + match.Length;
        }

        result.Append(text, cursor, text.Length - cursor);
        return result.ToString();
    }

    /// <summary>The trigger's word runs (letters, marks, and numerals of every kind), the only part of it the matcher reads.</summary>
    public static IReadOnlyList<string> Words(string trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var rune in trigger.EnumerateRunes())
        {
            if (IsWordRune(rune))
            {
                current.Append(rune.ToString());
            }
            else if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            words.Add(current.ToString());
        }

        return words;
    }

    /// <summary>
    /// Returns <paramref name="text"/> with every expansion in it put back to its trigger, literally
    /// and case-sensitively, longest expansion first. An expansion's tail of at least
    /// <see cref="MinimumClippedOverlap"/> characters at the very start of the text counts too, since
    /// the capture reads only the last few hundred characters before the caret.
    /// </summary>
    public static string RedactingExpansions(string text, IReadOnlyList<TextShortcut> shortcuts)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(shortcuts);
        var result = text;
        foreach (var shortcut in shortcuts.OrderByDescending(shortcut => shortcut.Expansion.Length))
        {
            var expansion = shortcut.Expansion;
            if (expansion.Length == 0)
            {
                continue;
            }

            result = result.Replace(expansion, shortcut.Trigger, StringComparison.Ordinal);
            var longest = Math.Min(expansion.Length - 1, result.Length);
            for (var length = longest; length >= MinimumClippedOverlap; length--)
            {
                if (result.StartsWith(expansion.AsSpan(expansion.Length - length), StringComparison.Ordinal))
                {
                    result = shortcut.Trigger + result[length..];
                    break;
                }
            }
        }

        return result;
    }

    private static bool IsWordRune(Rune rune) =>
        Rune.IsLetter(rune)
        || Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark
            or UnicodeCategory.DecimalDigitNumber
            or UnicodeCategory.LetterNumber
            or UnicodeCategory.OtherNumber;

    /// <summary>The regex body for one trigger, or null for a trigger with no letters or digits.</summary>
    private static string? Pattern(string trigger)
    {
        var words = Words(trigger);
        return words.Count == 0 ? null : string.Join(SeparatorRun, words.Select(Regex.Escape));
    }

    /// <summary>Which of the <paramref name="count"/> trigger groups, numbered from <paramref name="groupOffset"/>, took part in <paramref name="match"/>.</summary>
    private static int? ShortcutIndex(Match match, int groupOffset, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (match.Groups[i + groupOffset].Success)
            {
                return i;
            }
        }

        return null;
    }
}
