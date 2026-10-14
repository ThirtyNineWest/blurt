using System.Text.RegularExpressions;

namespace Blurt.Engine.TTS;

/// <summary>
/// Cuts text into the segments the synthesizer flushes one at a time. Cuts fall at sentence ends
/// only (terminal punctuation plus any closing quotes or brackets, followed by whitespace or the
/// end), or at line breaks; never at commas. A segment needs at least <see cref="MinWords"/> words,
/// so a one-word "sentence" (an abbreviation) joins the sentence after it. <c>maxCharacters</c>
/// only splits a runaway sentence.
/// </summary>
public static partial class SpeechSegmenter
{
    /// <summary>The fewest words a segment may hold.</summary>
    public const int MinWords = 2;

    [GeneratedRegex(@"[.!?…][""'’”)\]]*(?=\s|$)|\n")]
    private static partial Regex Boundary();

    /// <summary>The segments of <paramref name="text"/>, trimmed, in order; empty for blank text.</summary>
    public static IReadOnlyList<string> Segments(string text, int maxCharacters = 400)
    {
        ArgumentNullException.ThrowIfNull(text);
        var sentences = new List<string>();
        var start = 0;
        foreach (Match match in Boundary().Matches(text))
        {
            var end = match.Index + match.Length;
            AppendTrimmed(text[start..end], sentences);
            start = end;
        }

        AppendTrimmed(text[start..], sentences);
        return MergingShort(sentences).SelectMany(segment => Split(segment, maxCharacters)).ToList();
    }

    /// <summary>Joins any sentence shorter than <see cref="MinWords"/> onto the sentence after it; a short sentence at the very end joins the one before it.</summary>
    public static IReadOnlyList<string> MergingShort(IReadOnlyList<string> sentences)
    {
        ArgumentNullException.ThrowIfNull(sentences);
        var merged = new List<string>();
        var pending = string.Empty;
        foreach (var sentence in sentences)
        {
            pending = pending.Length == 0 ? sentence : pending + " " + sentence;
            if (WordCount(pending) >= MinWords)
            {
                merged.Add(pending);
                pending = string.Empty;
            }
        }

        if (pending.Length > 0)
        {
            if (merged.Count > 0)
            {
                merged[^1] = merged[^1] + " " + pending;
            }
            else
            {
                merged.Add(pending);
            }
        }

        return merged;
    }

    /// <summary>Splits <paramref name="segment"/> at the last whitespace at or before <paramref name="maxCharacters"/>; a run with no whitespace in that span is cut hard at the limit. Throws <see cref="ArgumentOutOfRangeException"/> when <paramref name="maxCharacters"/> is not positive.</summary>
    public static IReadOnlyList<string> Split(string segment, int maxCharacters)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCharacters);
        var pieces = new List<string>();
        var rest = segment.AsSpan();
        while (rest.Length > maxCharacters)
        {
            var window = rest[..maxCharacters];
            var cut = LastWhitespace(window);
            if (cut < 0)
            {
                cut = maxCharacters;
            }

            AppendTrimmed(rest[..cut].ToString(), pieces);
            rest = rest[cut..].TrimStart();
        }

        AppendTrimmed(rest.ToString(), pieces);
        return pieces;
    }

    private static int LastWhitespace(ReadOnlySpan<char> span)
    {
        for (var i = span.Length - 1; i >= 0; i--)
        {
            if (char.IsWhiteSpace(span[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static void AppendTrimmed(string piece, List<string> into)
    {
        if (piece.TrimmedNonEmpty() is { } trimmed)
        {
            into.Add(trimmed);
        }
    }

    private static int WordCount(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
