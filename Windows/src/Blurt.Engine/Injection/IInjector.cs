using Blurt.Engine.FocusCapture;

namespace Blurt.Engine.Injection;

/// <summary>Pastes a transcript into the captured focus target.</summary>
public interface IInjector
{
    /// <summary>Sets the window the next <see cref="InsertAsync"/> activates, or null for whatever has focus.</summary>
    Task SetTargetAsync(FocusTarget? target);

    /// <summary>
    /// Inserts <paramref name="text"/> into the focus target. <paramref name="priorText"/> is the
    /// text before the caret at press time and decides whether a separating space is needed;
    /// <paramref name="windowTitle"/> is the press-time window title, used to recognize a continuing
    /// dictation when <paramref name="priorText"/> is unreadable. Either is null when unavailable.
    /// </summary>
    /// <exception cref="BlurtException">
    /// With <see cref="BlurtError.NoEditableTarget"/> or <see cref="BlurtError.TargetAppLost"/> when
    /// the transcript was left on the clipboard instead.
    /// </exception>
    Task InsertAsync(string text, string? priorText, string? windowTitle, CancellationToken ct);
}
