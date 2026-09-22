namespace Blurt.Engine.Pipeline;

/// <summary>
/// What the overlay pill draws for an <see cref="OverlayUIState"/>: the brand orb, the live meter,
/// and the status word. The two waits (connecting, processing) share the orb-plus-word shape so
/// they read as one surface progressing; recording pairs the orb with the meter; the notices are
/// the word alone.
/// </summary>
/// <param name="ShowsOrb">Whether the brand orb is drawn at the leading edge.</param>
/// <param name="ShowsMeter">Whether the live level meter fills the space beside the orb.</param>
/// <param name="StatusWord">The status-line word, or null when the state shows none.</param>
/// <param name="IsError">Whether the word takes the error color rather than the brand green.</param>
public sealed record OverlayPillLayout(bool ShowsOrb, bool ShowsMeter, string? StatusWord, bool IsError)
{
    /// <summary>Returns the layout for <paramref name="state"/>; idle draws nothing.</summary>
    public static OverlayPillLayout For(OverlayUIState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state is OverlayUIState.ErrorState)
        {
            return new(ShowsOrb: false, ShowsMeter: false, "Error", IsError: true);
        }

        if (state == OverlayUIState.Connecting)
        {
            return new(ShowsOrb: true, ShowsMeter: false, "Connecting", IsError: false);
        }

        if (state == OverlayUIState.Recording)
        {
            return new(ShowsOrb: true, ShowsMeter: true, null, IsError: false);
        }

        if (state == OverlayUIState.Processing)
        {
            return new(ShowsOrb: true, ShowsMeter: false, "Transcribing", IsError: false);
        }

        if (state == OverlayUIState.Pasted)
        {
            return new(ShowsOrb: false, ShowsMeter: false, "Pasted", IsError: false);
        }

        if (state == OverlayUIState.NoTarget)
        {
            return new(ShowsOrb: false, ShowsMeter: false, "Copied", IsError: false);
        }

        return new(ShowsOrb: false, ShowsMeter: false, null, IsError: false);
    }
}
