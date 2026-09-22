using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Blurt.Engine.Tests.Stubs;

/// <summary>A request <see cref="FakeHttpTransport"/> received, with its content drained to bytes.</summary>
/// <param name="Message">The request as sent. Its content is consumed, and disposed once the client finishes.</param>
/// <param name="Body">Every byte the request content produced, or empty when it had none.</param>
/// <param name="ContentType">The content's <c>Content-Type</c> at send time, or null.</param>
/// <param name="ContentLength">The content's <c>Content-Length</c> at send time, or null when unknown.</param>
public sealed record RecordedRequest(HttpRequestMessage Message, byte[] Body, MediaTypeHeaderValue? ContentType, long? ContentLength)
{
    /// <summary>The raw value of the request header <paramref name="name"/> as the client set it, or null when absent.</summary>
    public string? Header(string name) =>
        Message.Headers.NonValidated.TryGetValues(name, out var values) ? values.ToString() : null;
}

/// <summary>
/// Per-instance <see cref="IHttpTransport"/> fake. Records each request, drains its content fully
/// before answering (so a streamed body's closing half is exercised), then returns the next queued
/// response, or <see cref="DefaultStatus"/> with an empty body when the queue is empty.
/// </summary>
public sealed class FakeHttpTransport : IHttpTransport
{
    private readonly Lock _gate = new();
    private readonly Queue<(HttpStatusCode Status, byte[] Body)> _responses = new();
    private readonly List<RecordedRequest> _requests = [];

    /// <summary>The status returned once the queue is empty.</summary>
    public HttpStatusCode DefaultStatus { get; init; } = HttpStatusCode.InternalServerError;

    /// <summary>When set, every request fails with this exception after its content is drained.</summary>
    public Exception? Failure { get; set; }

    /// <summary>Completes when a request is first entered, before its content is drained.</summary>
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>When set, each request waits for this after draining its content, or until the token cancels.</summary>
    public TaskCompletionSource? Hold { get; set; }

    /// <summary>Completes once a request has drained its content and is waiting on <see cref="Hold"/>.</summary>
    public TaskCompletionSource Holding { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Every request received, in order.</summary>
    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>The most recent request's drained body.</summary>
    public byte[] UploadedBody => Requests[^1].Body;

    /// <summary>Queues a response with a UTF-8 <paramref name="body"/>.</summary>
    public FakeHttpTransport Enqueue(HttpStatusCode status, string body) => Enqueue(status, Encoding.UTF8.GetBytes(body));

    /// <summary>Queues a response with a raw <paramref name="body"/>.</summary>
    public FakeHttpTransport Enqueue(HttpStatusCode status, byte[] body)
    {
        lock (_gate)
        {
            _responses.Enqueue((status, body));
        }

        return this;
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Entered.TrySetResult();
        var contentType = request.Content?.Headers.ContentType;
        var contentLength = request.Content?.Headers.ContentLength;
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(ct);
        lock (_gate)
        {
            _requests.Add(new RecordedRequest(request, body, contentType, contentLength));
        }

        if (Hold is { } hold)
        {
            Holding.TrySetResult();
            await hold.Task.WaitAsync(ct);
        }

        if (Failure is { } failure)
        {
            throw failure;
        }

        (HttpStatusCode Status, byte[] Body) next;
        lock (_gate)
        {
            next = _responses.Count > 0 ? _responses.Dequeue() : (DefaultStatus, []);
        }

        return new HttpResponseMessage(next.Status) { Content = new ByteArrayContent(next.Body), RequestMessage = request };
    }
}
