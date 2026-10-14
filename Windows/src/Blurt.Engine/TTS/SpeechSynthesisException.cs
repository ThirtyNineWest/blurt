namespace Blurt.Engine.TTS;

/// <summary>How a speech synthesis exchange failed.</summary>
public abstract record SpeechSynthesisFailure
{
    private SpeechSynthesisFailure()
    {
    }

    /// <summary>The socket went silent for longer than the synthesizer's receive timeout.</summary>
    public sealed record TimedOut : SpeechSynthesisFailure;

    /// <summary>An in-band <c>Error</c> frame, e.g. <c>(1008) Unauthorized: Invalid API key</c>.</summary>
    public sealed record Server(string Message) : SpeechSynthesisFailure;
}

/// <summary>
/// A speech synthesis exchange failed. <see cref="Exception.Message"/> is
/// <c>"Text-to-speech stopped responding."</c> for a timeout, or
/// <c>"Text-to-speech failed: {message}"</c> for a server error. Throws
/// <see cref="ArgumentNullException"/> when <c>failure</c> is null.
/// </summary>
public sealed class SpeechSynthesisException(SpeechSynthesisFailure failure) : Exception(Describe(failure))
{
    /// <summary>The failure this exception carries.</summary>
    public SpeechSynthesisFailure Failure { get; } = failure;

    private static string Describe(SpeechSynthesisFailure failure) => failure switch
    {
        SpeechSynthesisFailure.Server server => $"Text-to-speech failed: {server.Message}",
        SpeechSynthesisFailure.TimedOut => "Text-to-speech stopped responding.",
        _ => throw new ArgumentNullException(nameof(failure)),
    };
}
