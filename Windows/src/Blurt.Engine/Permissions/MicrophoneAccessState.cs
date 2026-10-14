namespace Blurt.Engine.Permissions;

/// <summary>Whether the app may capture from a microphone.</summary>
public enum MicrophoneAccessState
{
    /// <summary>Access is allowed and a capture device exists.</summary>
    Granted,

    /// <summary>Privacy settings deny microphone access.</summary>
    Denied,

    /// <summary>No capture device is present.</summary>
    NoDevice,

    /// <summary>The state could not be determined.</summary>
    Unknown,
}

/// <summary>Presentation of <see cref="MicrophoneAccessState"/>.</summary>
public static class MicrophoneAccessStates
{
    /// <summary>The sentence the setup screen shows for a microphone test result.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="state"/> is not a defined value.</exception>
    public static string Description(this MicrophoneAccessState state) => state switch
    {
        MicrophoneAccessState.Granted => "Your microphone is working. Blurt can record.",
        MicrophoneAccessState.Denied =>
            "Windows is blocking the microphone. Turn on “Let desktop apps access your microphone” in Privacy settings.",
        MicrophoneAccessState.NoDevice => "No microphone was found. Connect one and test again.",
        MicrophoneAccessState.Unknown => "Blurt couldn’t open the microphone. Check Privacy settings and test again.",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };
}
