using System.Globalization;
using System.Text;

namespace Blurt.Engine;

/// <summary>The shared "usable text" and byte-budget truncation rules.</summary>
public static class StringNormalization
{
    /// <summary>
    /// Returns <paramref name="value"/> trimmed of surrounding whitespace and newlines, or
    /// <see langword="null"/> when it is absent or blank.
    /// </summary>
    public static string? TrimmedNonEmpty(this string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// Returns the longest prefix of whole text elements whose UTF-8 encoding fits
    /// <paramref name="maxBytes"/>. A multi-scalar grapheme at the boundary is dropped whole, never
    /// split into an invalid fragment. A non-positive budget returns an empty string.
    /// </summary>
    public static string PrefixMaxUtf8Bytes(this string value, int maxBytes)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (Encoding.UTF8.GetByteCount(value) <= maxBytes)
        {
            return value;
        }

        var bytes = 0;
        var length = 0;
        while (length < value.Length)
        {
            var elementLength = StringInfo.GetNextTextElementLength(value, length);
            var elementBytes = Encoding.UTF8.GetByteCount(value.AsSpan(length, elementLength));
            if (bytes + elementBytes > maxBytes)
            {
                break;
            }

            bytes += elementBytes;
            length += elementLength;
        }

        return value[..length];
    }
}
