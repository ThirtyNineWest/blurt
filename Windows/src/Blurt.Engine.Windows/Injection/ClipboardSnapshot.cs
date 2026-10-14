namespace Blurt.Engine.Windows.Injection;

/// <summary>
/// The clipboard contents captured before a paste: one entry per format held in global memory, plus the
/// <c>CF_UNICODETEXT</c> string as a restore floor.
/// </summary>
/// <param name="Formats">The captured formats, in enumeration order. Empty when the clipboard held nothing.</param>
/// <param name="PlainText">The clipboard's Unicode text, or null when it held none.</param>
public sealed record ClipboardSnapshot(IReadOnlyList<ClipboardItem> Formats, string? PlainText)
{
    /// <summary>
    /// What a restore of this snapshot writes: the captured formats when there are any, otherwise the plain text when
    /// there is some, otherwise an empty clipboard.
    /// </summary>
    public ClipboardRestoreAction RestoreAction =>
        Formats.Count > 0 ? ClipboardRestoreAction.Formats
        : PlainText is not null ? ClipboardRestoreAction.PlainText
        : ClipboardRestoreAction.Empty;
}

/// <summary>One captured clipboard format.</summary>
/// <param name="Format">The format id.</param>
/// <param name="RegisteredName">The registered name for a format at or above <c>0xC000</c>; null otherwise or when unreadable.</param>
/// <param name="Data">The bytes of the format's global memory block.</param>
public sealed record ClipboardItem(uint Format, string? RegisteredName, byte[] Data);

/// <summary>What restoring a <see cref="ClipboardSnapshot"/> writes to the clipboard.</summary>
public enum ClipboardRestoreAction
{
    /// <summary>Empty the clipboard.</summary>
    Empty,

    /// <summary>Write every captured format.</summary>
    Formats,

    /// <summary>Write only the plain text.</summary>
    PlainText,
}

/// <summary>Classifies standard clipboard formats by whether their data is a copyable global memory block.</summary>
public static class ClipboardFormats
{
    /// <summary>
    /// Returns false for formats the system synthesizes from another (<c>CF_TEXT</c>, <c>CF_OEMTEXT</c>,
    /// <c>CF_LOCALE</c>), formats whose data is a GDI or metafile handle (<c>CF_BITMAP</c>, <c>CF_PALETTE</c>,
    /// <c>CF_METAFILEPICT</c>, <c>CF_ENHMETAFILE</c>, <c>CF_GDIOBJFIRST</c>–<c>CF_GDIOBJLAST</c>), owner-display
    /// formats (<c>CF_OWNERDISPLAY</c>, <c>CF_DSP*</c>), private formats (<c>CF_PRIVATEFIRST</c>–<c>CF_PRIVATELAST</c>),
    /// and zero. Returns true for every other format.
    /// </summary>
    public static bool IsRestorable(uint format) => format switch
    {
        0 or 1 or 2 or 3 or 7 or 9 or 14 or 16 => false,
        >= 0x80 and <= 0x8E => false,
        >= 0x200 and <= 0x3FF => false,
        _ => true,
    };
}
