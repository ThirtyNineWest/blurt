namespace Blurt.Engine.Pipeline;

/// <summary>Which recording chime to play.</summary>
public enum RecordingCue
{
    /// <summary>Recording began.</summary>
    Start,

    /// <summary>Recording ended.</summary>
    Stop,
}

/// <summary>
/// Edge detector for the recording chimes. The host passes every phase; <see cref="RecordingCue.Start"/>
/// fires only on the edge into <see cref="PipelinePhase.Recording"/> and <see cref="RecordingCue.Stop"/>
/// only on the edge out of it. The edge is recording, not the press: the press first claims
/// <see cref="PipelinePhase.Connecting"/>, so the chime sounds when audio actually flows.
/// </summary>
public struct RecordingCueGate
{
    private bool _wasRecording;

    /// <summary>Returns the cue to play for <paramref name="phase"/>, or null when the recording edge did not move.</summary>
    public RecordingCue? Cue(PipelinePhase phase)
    {
        var isRecording = phase == PipelinePhase.Recording;
        var wasRecording = _wasRecording;
        _wasRecording = isRecording;
        return (wasRecording, isRecording) switch
        {
            (false, true) => RecordingCue.Start,
            (true, false) => RecordingCue.Stop,
            _ => null,
        };
    }
}
