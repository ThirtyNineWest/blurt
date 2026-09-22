namespace Blurt.Engine.FocusCapture;

/// <summary>What the focused field exposed at press time. Every text field is null when unreadable.</summary>
/// <param name="WindowTitle">The focused window's title.</param>
/// <param name="FieldLabel">A short label for the focused field.</param>
/// <param name="PriorText">Text immediately before the caret.</param>
/// <param name="SelectedText">The current selection.</param>
/// <param name="IsSecure">Whether the capture refused to read the field's contents.</param>
public sealed record FocusedFieldContext(
    string? WindowTitle,
    string? FieldLabel,
    string? PriorText,
    string? SelectedText,
    bool IsSecure)
{
    /// <summary>Nothing was read and the field was not secure.</summary>
    public static readonly FocusedFieldContext Empty = new(null, null, null, null, false);
}
