using System.Text;

namespace Blurt.Engine.Injection;

/// <summary>
/// A window identified by its process id plus its title. Title equality is a heuristic: two tabs can share a title,
/// and the safe failure is a missing separator.
/// </summary>
/// <param name="Pid">The owning process id.</param>
/// <param name="Title">The window title.</param>
public sealed record WindowIdentity(int Pid, string Title);

/// <summary>What one insert resolves to: the exact text to write, and the window to remember it against.</summary>
/// <param name="Text">The text to paste, including any leading separator.</param>
/// <param name="Window">The target window, or null when the process or title is unknown.</param>
public sealed record ResolvedInsert(string Text, WindowIdentity? Window);

/// <summary>The pure separator and continuity rules an injector applies before pasting.</summary>
public static class InsertResolver
{
    /// <summary>
    /// Returns <paramref name="text"/> with one leading space when <paramref name="priorText"/> is non-empty and ends
    /// in a non-whitespace character and <paramref name="text"/> starts with a non-whitespace character; otherwise
    /// returns <paramref name="text"/> unchanged.
    /// </summary>
    public static string WithLeadingSeparator(string text, string? priorText)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0 || string.IsNullOrEmpty(priorText))
        {
            return text;
        }

        _ = Rune.DecodeLastFromUtf16(priorText, out var last, out _);
        _ = Rune.DecodeFromUtf16(text, out var first, out _);
        return Rune.IsWhiteSpace(last) || Rune.IsWhiteSpace(first) ? text : " " + text;
    }

    /// <summary>
    /// Returns the text treated as preceding the caret: <paramref name="priorText"/> when readable, otherwise
    /// <paramref name="lastInserted"/> when the insert targets the same window as the previous paste, otherwise null.
    /// </summary>
    public static string? SeparatorBasis(string? priorText, string? lastInserted, bool sameWindow) =>
        priorText ?? (sameWindow ? lastInserted : null);

    /// <summary>
    /// Resolves the text to paste and the window to remember. The window is known only when both
    /// <paramref name="targetPid"/> and <paramref name="windowTitle"/> are; an unknown window never matches
    /// <paramref name="lastInserted"/>.
    /// </summary>
    public static ResolvedInsert Resolve(
        string text, string? priorText, string? windowTitle, int? targetPid, ResolvedInsert? lastInserted)
    {
        var currentWindow = targetPid is { } pid && windowTitle is not null ? new WindowIdentity(pid, windowTitle) : null;
        var sameWindow = currentWindow is not null && currentWindow == lastInserted?.Window;
        var basis = SeparatorBasis(priorText, lastInserted?.Text, sameWindow);
        return new ResolvedInsert(WithLeadingSeparator(text, basis), currentWindow);
    }
}
