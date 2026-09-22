namespace Blurt.Engine.Audio;

/// <summary>
/// A selectable record-start/stop cue voice: an id, a display label, and the picker group it
/// belongs to. The id doubles as the cue stem a host resolves against its own bundle:
/// <c>{id}-start</c> / <c>{id}-stop</c>. The engine ships no voices and no audio; a host supplies
/// them and collects them into a <see cref="SoundPackCatalog"/>.
/// </summary>
/// <param name="Id">The persisted identifier and cue stem.</param>
/// <param name="Label">The voice's display name, shown in the picker.</param>
/// <param name="Group">The picker section this voice belongs to, or empty for the silent pack.</param>
public sealed record SoundPack(string Id, string Label, string Group)
{
    /// <summary>The "no sound" choice. Not part of any catalog; a catalog keeps it reachable regardless of the voices it was handed.</summary>
    public static readonly SoundPack None = new("none", "None", string.Empty);

    /// <summary>True for the pack with no cue files, marked by an empty group.</summary>
    public bool IsSilent => Group.Length == 0;

    /// <summary>Cue stem for the start cue, or null when no sound plays.</summary>
    public string? StartFileName => IsSilent ? null : $"{Id}-start";

    /// <summary>Cue stem for the stop cue, or null when no sound plays.</summary>
    public string? StopFileName => IsSilent ? null : $"{Id}-stop";
}
