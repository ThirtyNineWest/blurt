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
