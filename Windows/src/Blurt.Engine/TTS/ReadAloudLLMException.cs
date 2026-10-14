namespace Blurt.Engine.TTS;

/// <summary>How a read-aloud LLM Gateway request failed.</summary>
public abstract record ReadAloudLLMFailure
{
    private ReadAloudLLMFailure()
    {
    }

    /// <summary>A non-2xx answer from the gateway.</summary>
    public sealed record Status(int Code) : ReadAloudLLMFailure;

    /// <summary>A 2xx with no text in it.</summary>
    public sealed record Empty : ReadAloudLLMFailure;

    /// <summary>The rewrite stopped at <c>max_tokens</c>, part-way through the selection.</summary>
    public sealed record Truncated : ReadAloudLLMFailure;

    /// <summary>The rewrite is too much longer than its input to be a rewrite of it.</summary>
    public sealed record Implausible : ReadAloudLLMFailure;

    /// <summary>The gateway did not answer within the request's time bound.</summary>
    public sealed record TimedOut : ReadAloudLLMFailure;
}

/// <summary>
/// A read-aloud LLM Gateway request failed. <see cref="Exception.Message"/> describes the failure:
/// <c>"The LLM Gateway answered HTTP {code}."</c>, <c>"The LLM Gateway returned no text."</c>,
/// <c>"The LLM Gateway's rewrite was cut short."</c>,
/// <c>"The LLM Gateway's rewrite didn't match the selection."</c>, or
/// <c>"The LLM Gateway did not answer in time."</c>. Throws <see cref="ArgumentNullException"/> when
/// <c>failure</c> is null.
/// </summary>
public sealed class ReadAloudLLMException(ReadAloudLLMFailure failure) : Exception(Describe(failure))
{
    /// <summary>The failure this exception carries.</summary>
    public ReadAloudLLMFailure Failure { get; } = failure;

    private static string Describe(ReadAloudLLMFailure failure) => failure switch
    {
        ReadAloudLLMFailure.Status status => $"The LLM Gateway answered HTTP {status.Code}.",
        ReadAloudLLMFailure.Empty => "The LLM Gateway returned no text.",
        ReadAloudLLMFailure.Truncated => "The LLM Gateway's rewrite was cut short.",
        ReadAloudLLMFailure.Implausible => "The LLM Gateway's rewrite didn't match the selection.",
        ReadAloudLLMFailure.TimedOut => "The LLM Gateway did not answer in time.",
        _ => throw new ArgumentNullException(nameof(failure)),
    };
}
