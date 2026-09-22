namespace Blurt.Engine.Pipeline;

/// <summary>
/// The in-memory history of recent dictations, newest first. It remembers far more than it shows:
/// <see cref="Capacity"/> bounds the history the request context draws from, and
/// <see cref="DisplayCapacity"/> is how many rows the ready window renders. Never written to disk.
/// Not thread-safe; the owner hands other threads a <see cref="Snapshot"/>.
/// </summary>
public sealed class RecentDictations
{
    /// <summary>How many dictations the history remembers.</summary>
    public const int Capacity = 100;

    /// <summary>How many rows the ready window's recent list renders.</summary>
    public const int DisplayCapacity = 3;

    private readonly List<Entry> _entries;

    /// <summary>Creates an empty history.</summary>
    public RecentDictations()
    {
        _entries = [];
    }

    private RecentDictations(IEnumerable<Entry> entries)
    {
        _entries = [.. entries];
    }

    /// <summary>Most recent first, at most <see cref="Capacity"/>.</summary>
    public IReadOnlyList<Entry> Entries => _entries.AsReadOnly();

    /// <summary>The newest <see cref="DisplayCapacity"/> entries.</summary>
    public IReadOnlyList<Entry> Displayed => _entries.Take(DisplayCapacity).ToList();

    /// <summary>Every remembered transcript, oldest first, which is the order the request context wants.</summary>
    public IReadOnlyList<string> TranscriptsOldestFirst => _entries.AsEnumerable().Reverse().Select(e => e.Text).ToList();

    /// <summary>
    /// Returns the height of a list showing <see cref="DisplayCapacity"/> rows: every row plus a
    /// separator between each adjacent pair.
    /// </summary>
    public static double ReservedHeight(double rowHeight, double separatorThickness) =>
        (DisplayCapacity * rowHeight) + ((DisplayCapacity - 1) * separatorThickness);

    /// <summary>
    /// Records <paramref name="text"/> made at <paramref name="at"/> with the custom style name
    /// <paramref name="style"/> (null for none) at the front, dropping the oldest entries beyond
    /// <see cref="Capacity"/>.
    /// </summary>
    public void Record(string text, string? style, DateTimeOffset at)
    {
        _entries.Insert(0, new Entry(Guid.NewGuid(), text, at, style));
        if (_entries.Count > Capacity)
        {
            _entries.RemoveRange(Capacity, _entries.Count - Capacity);
        }
    }

    /// <summary>Returns an independent copy; later records on either instance do not affect the other.</summary>
    public RecentDictations Snapshot() => new(_entries);

    /// <summary>One recorded dictation.</summary>
    /// <param name="Id">Stable identity assigned at creation.</param>
    /// <param name="Text">The transcript.</param>
    /// <param name="Timestamp">When the dictation landed.</param>
    /// <param name="Style">The custom style's display name, or null for the base styling.</param>
    public sealed record Entry(Guid Id, string Text, DateTimeOffset Timestamp, string? Style)
    {
        /// <summary>How long after a dictation <see cref="RelativeLabel"/> says "just now", in seconds.</summary>
        public const double JustNowThresholdSeconds = 60;

        /// <summary>
        /// Returns "just now" when less than <see cref="JustNowThresholdSeconds"/> has elapsed (including
        /// a timestamp slightly in the future), otherwise the largest whole unit of minutes, hours, or
        /// days, e.g. "1 minute ago" or "3 days ago".
        /// </summary>
        public string RelativeLabel(DateTimeOffset now)
        {
            var elapsed = (now - Timestamp).TotalSeconds;
            if (elapsed < JustNowThresholdSeconds)
            {
                return "just now";
            }

            var seconds = (long)elapsed;
            return seconds switch
            {
                >= 86_400 => Ago(seconds / 86_400, "day"),
                >= 3_600 => Ago(seconds / 3_600, "hour"),
                _ => Ago(seconds / 60, "minute"),
            };
        }

        private static string Ago(long count, string unit) => count == 1 ? $"1 {unit} ago" : $"{count} {unit}s ago";
    }
}
