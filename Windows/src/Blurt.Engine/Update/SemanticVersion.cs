using System.Globalization;

namespace Blurt.Engine.Update;

/// <summary>
/// A dotted numeric version (<c>major.minor.patch…</c>) parsed for comparison — used to decide
/// whether a GitHub release is newer than the running app. Accepts an optional leading <c>v</c>
/// (GitHub tags are <c>v0.1.30</c>; the running build's version is the bare <c>0.1.30</c>).
/// Comparison is component-wise with missing trailing components treated as zero, so
/// <c>1.2 == 1.2.0</c>.
/// </summary>
public readonly struct SemanticVersion : IEquatable<SemanticVersion>, IComparable<SemanticVersion>
{
    private readonly int[] _components;

    private SemanticVersion(string description, int[] components)
    {
        Description = description;
        _components = components;
    }

    /// <summary>The <c>v</c>-stripped source string, kept for display in the UI.</summary>
    public string Description { get; }

    /// <summary>
    /// Parses <paramref name="value"/> (e.g. <c>"0.1.30"</c> or <c>"v0.1.30"</c>) into
    /// <paramref name="version"/> and returns true. Returns false — leaving
    /// <paramref name="version"/> as its default — when the string is empty or any dot-separated
    /// component isn't a non-negative integer, so a malformed tag reads as "can't determine" rather
    /// than throwing.
    /// </summary>
    public static bool TryParse(string value, out SemanticVersion version)
    {
        var text = value.Length > 0 && (value[0] == 'v' || value[0] == 'V') ? value[1..] : value;
        var parts = text.Split('.', StringSplitOptions.None);
        var components = new int[parts.Length];

        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var component) || component < 0)
            {
                version = default;
                return false;
            }

            components[i] = component;
        }

        version = new SemanticVersion(text, components);
        return true;
    }

    /// <inheritdoc/>
    public int CompareTo(SemanticVersion other)
    {
        var left = _components ?? [];
        var right = other._components ?? [];
        var width = Math.Max(left.Length, right.Length);

        for (var i = 0; i < width; i++)
        {
            var a = i < left.Length ? left[i] : 0;
            var b = i < right.Length ? right[i] : 0;
            var comparison = a.CompareTo(b);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    /// <summary>True when both versions' components, zero-extended to a common length, are identical.</summary>
    public bool Equals(SemanticVersion other) => CompareTo(other) == 0;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var components = _components ?? [];
        var length = components.Length;
        while (length > 0 && components[length - 1] == 0)
        {
            length--;
        }

        var hash = new HashCode();
        for (var i = 0; i < length; i++)
        {
            hash.Add(components[i]);
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    public override string ToString() => Description ?? string.Empty;

    /// <summary>True when both versions' components, zero-extended to a common length, are identical.</summary>
    public static bool operator ==(SemanticVersion left, SemanticVersion right) => left.Equals(right);

    /// <summary>True when the versions are not <see cref="Equals(SemanticVersion)"/>.</summary>
    public static bool operator !=(SemanticVersion left, SemanticVersion right) => !left.Equals(right);

    /// <summary>True when <paramref name="left"/> orders before <paramref name="right"/>, comparing zero-extended components in order.</summary>
    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;

    /// <summary>True when <paramref name="left"/> orders after <paramref name="right"/>, comparing zero-extended components in order.</summary>
    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;
}
