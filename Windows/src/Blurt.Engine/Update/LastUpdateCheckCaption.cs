namespace Blurt.Engine.Update;

/// <summary>The Updates section's caption saying when an update check last completed.</summary>
public static class LastUpdateCheckCaption
{
    /// <summary>
    /// Returns "Last checked " followed by <paramref name="lastCheck"/> in <paramref name="culture"/>'s
    /// short date and time pattern, as the time appears in <paramref name="lastCheck"/>'s own offset,
    /// or "Not checked yet" when no check has completed.
    /// </summary>
    public static string Text(DateTimeOffset? lastCheck, IFormatProvider culture) =>
        lastCheck is { } stamp ? $"Last checked {stamp.ToString("g", culture)}" : "Not checked yet";
}
