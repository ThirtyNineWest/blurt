namespace Blurt.Engine.Pipeline;

/// <summary>A failure the release pipeline raises itself, as opposed to one from the transport or the service.</summary>
public sealed class DictationPipelineException : Exception
{
    private DictationPipelineException(string message)
        : base(message)
    {
    }

    /// <summary>A release reached the transcript step with no request in flight, so the recording was never uploaded.</summary>
    public static DictationPipelineException UploadNeverStarted() => new("The recording wasn't uploaded.");
}
