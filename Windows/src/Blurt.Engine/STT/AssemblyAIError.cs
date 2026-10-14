namespace Blurt.Engine.STT;

/// <summary>The kinds of <see cref="AssemblyAIError"/>.</summary>
public enum AssemblyAIErrorKind
{
    /// <summary>The dictation API answered with a non-2xx status.</summary>
    Http,

    /// <summary>A 2xx body carried no transcript <c>text</c>.</summary>
    MalformedResponse,
}

/// <summary>
/// A failure reported by, or decoding, the AssemblyAI dictation API. The session wraps it in
/// <see cref="BlurtError.SttFailed"/> before it reaches the UI. Its message is the user-facing
/// description.
/// </summary>
public sealed class AssemblyAIError : Exception
{
    private AssemblyAIError(AssemblyAIErrorKind kind, int? status, string? serverMessage, string message)
        : base(message)
    {
        Kind = kind;
        Status = status;
        ServerMessage = serverMessage;
    }

    /// <summary>Which failure this is.</summary>
    public AssemblyAIErrorKind Kind { get; }

    /// <summary>The HTTP status for <see cref="AssemblyAIErrorKind.Http"/>; null otherwise.</summary>
    public int? Status { get; }

    /// <summary>The server's explanation for <see cref="AssemblyAIErrorKind.Http"/>, when the body carried one.</summary>
    public string? ServerMessage { get; }

    /// <summary>A non-2xx response: "AssemblyAI error {status}", followed by ": {message}" when <paramref name="message"/> is non-null.</summary>
    public static AssemblyAIError Http(int status, string? message) =>
        new(
            AssemblyAIErrorKind.Http,
            status,
            message,
            message is null ? $"AssemblyAI error {status}" : $"AssemblyAI error {status}: {message}");

    /// <summary>A 2xx response with no decodable transcript.</summary>
    public static AssemblyAIError MalformedResponse() =>
        new(AssemblyAIErrorKind.MalformedResponse, null, null, "Unexpected response from AssemblyAI.");
}
