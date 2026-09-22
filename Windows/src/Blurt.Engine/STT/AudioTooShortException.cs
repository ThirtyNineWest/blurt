namespace Blurt.Engine.STT;

/// <summary>
/// Thrown by a transcriber when the recording ended below <see cref="SyncSTTLimits.MinPcmBytes"/>;
/// the session maps it to a quiet <see cref="Pipeline.PipelinePhase.Idle"/>.
/// </summary>
public sealed class AudioTooShortException : Exception
{
    /// <summary>Creates the exception with its fixed message.</summary>
    public AudioTooShortException()
        : base("That recording was too short to transcribe.")
    {
    }
}
