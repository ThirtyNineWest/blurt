using System.Globalization;
using System.Text;

namespace Blurt.Engine.FocusCapture;

/// <summary>The decisions focus capture applies to values read from the focused field, with no platform I/O.</summary>
public static class FocusCaptureRules
{
    /// <summary>The most text read from before the caret, in UTF-16 units.</summary>
    public const int MaxPriorChars = 320;

    /// <summary>The most selected text read, in UTF-16 units.</summary>
    public const int MaxSelectedChars = 320;

    /// <summary>The longest window title kept, in UTF-16 units.</summary>
    public const int MaxWindowTitleChars = 120;

    /// <summary>The longest field label kept, in UTF-16 units.</summary>
    public const int MaxFieldLabelChars = 80;

    /// <summary>
    /// Returns <paramref name="text"/> unchanged when it holds at least one visible code point, otherwise null.
    /// Default-ignorable code points (zero-width spaces, joiners, bidi marks, BOM, variation selectors) are
    /// invisible. Ordinary whitespace is visible, so a caret after a real space keeps its prior text.
    /// </summary>
    public static string? VisibleTextOrNil(string? text)
    {
        if (text is null)
        {
            return null;
        }

        foreach (var rune in text.EnumerateRunes())
        {
            if (!IsInvisible(rune))
            {
                return text;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the first candidate that is non-blank after trimming, in priority order
    /// placeholder → description → title → role description, or null when all are blank.
    /// A candidate is invoked only when every earlier one came back blank.
    /// </summary>
    public static string? SelectLabel(
        Func<string?> placeholder,
        Func<string?> description,
        Func<string?> title,
        Func<string?> roleDescription)
    {
        ArgumentNullException.ThrowIfNull(placeholder);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(roleDescription);

        return placeholder().TrimmedNonEmpty()
            ?? description().TrimmedNonEmpty()
            ?? title().TrimmedNonEmpty()
            ?? roleDescription().TrimmedNonEmpty();
    }

    /// <summary>
    /// Returns the last <paramref name="maxChars"/> UTF-16 units of <paramref name="full"/> before the UTF-16
    /// offset <paramref name="caretUtf16"/>, never starting inside a surrogate pair. A caret outside the string
    /// or inside a surrogate pair uses the whole string. Returns null when the slice is empty.
    /// </summary>
    public static string? PriorSlice(string full, int caretUtf16, int maxChars)
    {
        ArgumentNullException.ThrowIfNull(full);
        var caretIsBoundary = caretUtf16 >= 0
            && caretUtf16 <= full.Length
            && !(caretUtf16 > 0 && caretUtf16 < full.Length && char.IsSurrogatePair(full[caretUtf16 - 1], full[caretUtf16]));
        var end = caretIsBoundary ? caretUtf16 : full.Length;

        var start = Math.Max(0, end - Math.Max(0, maxChars));
        if (start > 0 && start < end && char.IsLowSurrogate(full[start]) && char.IsHighSurrogate(full[start - 1]))
        {
            start++;
        }

        return start < end ? full[start..end] : null;
    }

    /// <summary>
    /// Returns <paramref name="text"/> capped at <paramref name="maxChars"/> UTF-16 units, never ending inside
    /// a surrogate pair. Null stays null.
    /// </summary>
    public static string? Clip(string? text, int maxChars)
    {
        if (text is null || text.Length <= maxChars)
        {
            return text;
        }

        var length = Math.Max(0, maxChars);
        if (length > 0 && char.IsHighSurrogate(text[length - 1]) && char.IsLowSurrogate(text[length]))
        {
            length--;
        }

        return text[..length];
    }

    /// <summary>
    /// Returns whether the focused element accepts pasted text: it has a text control type, a settable value,
    /// or an insertion point. No positive signal means not editable.
    /// </summary>
    public static bool IsEditableTarget(bool isEditControlType, bool valueSettable, bool hasInsertionPoint) =>
        isEditControlType || valueSettable || hasInsertionPoint;

    private static bool IsInvisible(Rune rune)
    {
        if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format)
        {
            return true;
        }

        var value = rune.Value;
        return value is (>= 0x200B and <= 0x200F)
            or (>= 0x2028 and <= 0x202E)
            or (>= 0x2060 and <= 0x2064)
            or 0xFEFF
            or 0x00AD
            or 0x034F
            or 0x061C
            or 0x180E
            or (>= 0xFE00 and <= 0xFE0F)
            or (>= 0xE0100 and <= 0xE01EF);
    }
}
