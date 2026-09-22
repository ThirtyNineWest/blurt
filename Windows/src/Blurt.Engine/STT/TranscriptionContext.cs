namespace Blurt.Engine.STT;

/// <summary>
/// Everything captured at dictation start about where the user is typing. Only
/// <see cref="RecentTranscripts"/> and <see cref="PriorText"/> go on the wire (as <c>stt_prompt</c>),
/// and <see cref="KeyTerms"/> rides its own request field. Every other field stays on the machine;
/// its presence here is not permission to send it.
/// </summary>
/// <param name="AppName">The foreground application's display name. Local only.</param>
/// <param name="WindowTitle">The focused window's title. Local only; the injector uses it to recognize the captured window.</param>
/// <param name="FieldLabel">A short label for the focused field. Local only.</param>
/// <param name="PriorText">Text immediately before the caret. Sent as the last <c>stt_prompt</c> line; also drives the paste's leading separator.</param>
/// <param name="SelectedText">The current selection in the focused field. Local only.</param>
/// <param name="RecentTranscripts">The user's recent dictations, oldest first. Sent as the leading <c>stt_prompt</c> lines.</param>
/// <param name="KeyTerms">User-configured vocabulary. Sent as <c>keyterms_prompt</c>.</param>
/// <param name="TargetIsSecure">
/// Whether the capture refused to read the focused field (a password input or unreadable
/// element). Local only; a transcript dictated into a secure field is not remembered as history.
/// </param>
public sealed record TranscriptionContext(
    string? AppName,
    string? WindowTitle,
    string? FieldLabel,
    string? PriorText,
    string? SelectedText,
    IReadOnlyList<string> RecentTranscripts,
    IReadOnlyList<string> KeyTerms,
    bool TargetIsSecure)
{
    /// <summary>
    /// True when no text field carries non-blank content, there is no history, no key terms, and the
    /// target was not secure. A secure target counts, so collapsing an empty context to null never
    /// erases the flag that keeps its transcript out of history.
    /// </summary>
    public bool IsEmpty =>
        !TargetIsSecure
        && KeyTerms.Count == 0
        && RecentTranscripts.Count == 0
        && AppName.TrimmedNonEmpty() is null
        && WindowTitle.TrimmedNonEmpty() is null
        && FieldLabel.TrimmedNonEmpty() is null
        && PriorText.TrimmedNonEmpty() is null
        && SelectedText.TrimmedNonEmpty() is null;

    /// <summary>True when every field is equal, comparing the lists element by element.</summary>
    public bool Equals(TranscriptionContext? other) =>
        other is not null
        && AppName == other.AppName
        && WindowTitle == other.WindowTitle
        && FieldLabel == other.FieldLabel
        && PriorText == other.PriorText
        && SelectedText == other.SelectedText
        && TargetIsSecure == other.TargetIsSecure
        && RecentTranscripts.SequenceEqual(other.RecentTranscripts)
        && KeyTerms.SequenceEqual(other.KeyTerms);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(AppName);
        hash.Add(WindowTitle);
        hash.Add(FieldLabel);
        hash.Add(PriorText);
        hash.Add(SelectedText);
        hash.Add(TargetIsSecure);
        foreach (var transcript in RecentTranscripts)
        {
            hash.Add(transcript);
        }

        foreach (var term in KeyTerms)
        {
            hash.Add(term);
        }

        return hash.ToHashCode();
    }
}
