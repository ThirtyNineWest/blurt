namespace Blurt.Engine.FocusCapture;

/// <summary>Reads the foreground window and the focused field at press time.</summary>
public interface IFocusCapture
{
    /// <summary>Returns the foreground window, or null when none can be identified.</summary>
    FocusTarget? CaptureForeground();

    /// <summary>Returns what the focused field exposes, or <see cref="FocusedFieldContext.Empty"/> when nothing is readable.</summary>
    FocusedFieldContext CaptureFieldContext();
}
