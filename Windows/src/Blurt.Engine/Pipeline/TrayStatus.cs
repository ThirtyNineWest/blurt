namespace Blurt.Engine.Pipeline;

/// <summary>
/// The dictation state shown by the tray icon. Coarser than <see cref="OverlayUIState"/>: a handled
/// failure reads as <see cref="Idle"/>, so the icon cannot be stranded on a state the pill reverts
/// out of on a timer.
/// </summary>
public enum TrayStatus
{
    /// <summary>At rest.</summary>
    Idle,

    /// <summary>Audio is being captured.</summary>
    Recording,

    /// <summary>The transcript is awaited.</summary>
    Transcribing,
}

/// <summary>Presentation of <see cref="TrayStatus"/>.</summary>
public static class TrayStatuses
{
    /// <summary>The screen-reader label for the tray icon.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="status"/> is not a defined value.</exception>
    public static string AccessibilityLabel(this TrayStatus status) => status switch
    {
        TrayStatus.Idle => "Blurt — idle",
        TrayStatus.Recording => "Blurt — recording",
        TrayStatus.Transcribing => "Blurt — transcribing",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    /// <summary>The icon resource name for the tray icon.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="status"/> is not a defined value.</exception>
    public static string IconName(this TrayStatus status) => status switch
    {
        TrayStatus.Idle => "idle",
        TrayStatus.Recording => "recording",
        TrayStatus.Transcribing => "transcribing",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
