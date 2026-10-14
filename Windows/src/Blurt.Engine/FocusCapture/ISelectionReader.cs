namespace Blurt.Engine.FocusCapture;

/// <summary>Reads the focused element's selected text through the platform's accessibility API.</summary>
public interface ISelectionReader
{
    /// <summary>
    /// Returns the selected text cut to <paramref name="maxChars"/>, <see cref="SelectionRead.None"/>
    /// when the element is readable but has no selection or is a secure field, or
    /// <see cref="SelectionRead.Unreadable"/> when no focused element can be reached. Blocks the
    /// calling thread for the duration of the cross-process read.
    /// </summary>
    SelectionRead CaptureSelectedText(int maxChars);
}
