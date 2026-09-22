using System.Net;
using FluentAssertions;

namespace Blurt.Engine.Tests;

public sealed class HttpClientTransportTests
{
    [Test]
    public async Task ForwardsTheRequestAndReturnsTheResponse()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var transport = new HttpClientTransport(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.invalid/v1");

        using var response = await transport.SendAsync(request, CancellationToken.None);

        handler.Received.Should().BeSameAs(request);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Test]
    public void AClientWithTheDefaultTimeoutIsRejected()
    {
        using var client = new HttpClient();

        FluentActions.Invoking(() => new HttpClientTransport(client))
            .Should().Throw<ArgumentException>()
            .WithParameterName("client")
            .WithMessage("*HttpClient.Timeout*");
    }

    [Test]
    public void AClientWithAnyFiniteTimeoutIsRejected()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

        FluentActions.Invoking(() => new HttpClientTransport(client)).Should().Throw<ArgumentException>();
    }

    [Test]
    public void TheParameterlessTransportHasAnInfiniteTimeout()
    {
        var transport = new HttpClientTransport();

        transport.Timeout.Should().Be(Timeout.InfiniteTimeSpan);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Received { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Received = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted));
        }
    }
}
