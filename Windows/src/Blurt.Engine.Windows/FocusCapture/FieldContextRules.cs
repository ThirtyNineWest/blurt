using Blurt.Engine.FocusCapture;

namespace Blurt.Engine.Windows.FocusCapture;

/// <summary>Assembles a <see cref="FocusedFieldContext"/> from the reads taken off the focused element.</summary>
public static class FieldContextRules
{
    /// <summary>
    /// Builds the context. <paramref name="isPassword"/> is null when the element's role or password flag could not
    /// be read, which returns <see cref="FocusedFieldContext.Empty"/> marked secure and invokes no reader. A password
    /// field keeps only its window title and label. <paramref name="readText"/> is invoked only for a non-secure
    /// field and only when <paramref name="sameProcess"/> is true. Every value passes through
    /// <see cref="FocusCaptureRules.VisibleTextOrNil"/> and is clipped to its cap. Title, label, and selection are
    /// trimmed; prior text keeps its whitespace.
    /// </summary>
    public static FocusedFieldContext Compose(
        bool? isPassword,
        bool sameProcess,
        Func<string?> windowTitle,
        Func<string?> fieldLabel,
        Func<(string? Prior, string? Selected)> readText)
    {
        ArgumentNullException.ThrowIfNull(windowTitle);
        ArgumentNullException.ThrowIfNull(fieldLabel);
        ArgumentNullException.ThrowIfNull(readText);

        if (isPassword is null)
        {
            return FocusedFieldContext.Empty with { IsSecure = true };
        }

        var title = Visible(windowTitle().TrimmedNonEmpty(), FocusCaptureRules.MaxWindowTitleChars);
        var label = Visible(fieldLabel().TrimmedNonEmpty(), FocusCaptureRules.MaxFieldLabelChars);
        if (isPassword.Value)
        {
            return new FocusedFieldContext(title, label, null, null, IsSecure: true);
        }

        if (!sameProcess)
        {
            return new FocusedFieldContext(title, label, null, null, IsSecure: false);
        }

        var (prior, selected) = readText();
        var priorTail = prior is null ? null : FocusCaptureRules.PriorSlice(prior, prior.Length, FocusCaptureRules.MaxPriorChars);
        return new FocusedFieldContext(
            title,
            label,
            FocusCaptureRules.VisibleTextOrNil(priorTail),
            Visible(selected.TrimmedNonEmpty(), FocusCaptureRules.MaxSelectedChars),
            IsSecure: false);
    }

    private static string? Visible(string? text, int maxChars) =>
        FocusCaptureRules.Clip(FocusCaptureRules.VisibleTextOrNil(text), maxChars);
}
