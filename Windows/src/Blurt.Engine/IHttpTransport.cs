namespace Blurt.Engine;

/// <summary>The HTTP calls the AssemblyAI clients make, behind a seam tests substitute per instance.</summary>
public interface IHttpTransport
{
    /// <summary>
    /// Sends <paramref name="request"/> and returns the response once its headers arrive. A request
    /// whose content is a stream of unknown length is uploaded chunked as the stream produces it.
    /// </summary>
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct);
}

/// <summary>
/// An <see cref="IHttpTransport"/> that forwards to an <see cref="HttpClient"/> whose
/// <see cref="HttpClient.Timeout"/> is infinite. A dictation request stays open for the whole
/// recording, so a finite client-wide total bound would fail long dictations; every per-request
/// bound rides the cancellation token instead.
/// </summary>
public sealed class HttpClientTransport : IHttpTransport
{
    /// <summary>Creates a transport over its own <see cref="HttpClient"/> with an infinite timeout.</summary>
    public HttpClientTransport()
        : this(new HttpClient { Timeout = Timeout.InfiniteTimeSpan })
    {
    }

    /// <summary>Creates a transport over <paramref name="client"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="client"/>'s <see cref="HttpClient.Timeout"/> is not <see cref="Timeout.InfiniteTimeSpan"/>.</exception>
    public HttpClientTransport(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (client.Timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentException(
                $"HttpClient.Timeout must be Timeout.InfiniteTimeSpan, not {client.Timeout}. Bound each request with its cancellation token.",
                nameof(client));
        }

        Client = client;
    }

    /// <summary>The client requests are forwarded to. Its timeout is always infinite.</summary>
    public HttpClient Client { get; }

    /// <summary>Forwards to <see cref="HttpClient.SendAsync(HttpRequestMessage, HttpCompletionOption, CancellationToken)"/> with <see cref="HttpCompletionOption.ResponseHeadersRead"/>.</summary>
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
}
