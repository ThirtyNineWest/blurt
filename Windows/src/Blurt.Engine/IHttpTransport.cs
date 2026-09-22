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

/// <summary>An <see cref="IHttpTransport"/> that forwards to an <see cref="HttpClient"/>.</summary>
public sealed class HttpClientTransport(HttpClient client) : IHttpTransport
{
    /// <summary>Forwards to <see cref="HttpClient.SendAsync(HttpRequestMessage, HttpCompletionOption, CancellationToken)"/> with <see cref="HttpCompletionOption.ResponseHeadersRead"/>.</summary>
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
}
