namespace Blurt.Engine.Windows.Injection;

/// <summary>The clipboard operations <see cref="SelectionCopy"/> needs. <see cref="Win32Clipboard"/> is the production implementation.</summary>
public interface ICopyClipboard
{
    /// <summary>A number that changes on every clipboard write by any process.</summary>
    uint SequenceNumber { get; }

    /// <summary>Captures the clipboard contents, or returns null when they cannot be read and so could not be restored.</summary>
    ClipboardSnapshot? Snapshot();

    /// <summary>Returns the clipboard's Unicode text, or null when it holds none or cannot be opened.</summary>
    string? CurrentString();

    /// <summary>
    /// Writes <paramref name="saved"/> back when <see cref="SequenceNumber"/> still equals
    /// <paramref name="ifSequenceIs"/>; does nothing when anything wrote since. A failed write is left unrestored
    /// rather than thrown.
    /// </summary>
    void Restore(ClipboardSnapshot saved, uint ifSequenceIs);
}
