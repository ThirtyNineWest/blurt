using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Blurt.Engine.STT;
using Blurt.Engine.Tests.Stubs;
using FluentAssertions;
using static Blurt.Engine.Tests.Stubs.TranscriberTestSupport;

namespace Blurt.Engine.Tests.STT;

[CancelAfter(60_000)]
public sealed class ChunkedUploadTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [Test]
    public async Task TheRequestOpensBeforeTheFeedEnds()
    {
        var feed = Channel.CreateUnbounded<byte[]>();
        var transport = Answering("""{"text":"Hello world."}""");

        var upload = MakeTranscriber("k", transport).TranscribeAsync(feed.Reader.ReadAllAsync(), 16_000, null, default);
        await transport.Entered.Task.WaitAsync(Wait);

        upload.IsCompleted.Should().BeFalse();
        transport.Requests.Should().BeEmpty();
        feed.Writer.Complete();
        await FluentActions.Awaiting(() => upload).Should().ThrowAsync<AudioTooShortException>();
    }

    [Test]
    public async Task TheFeedEndingIsWhatCompletesTheBody()
    {
        var feed = Channel.CreateUnbounded<byte[]>();
        var transport = Answering("""{"text":"Hello world."}""");
        var upload = MakeTranscriber("k", transport).TranscribeAsync(feed.Reader.ReadAllAsync(), 16_000, null, default);
        await transport.Entered.Task.WaitAsync(Wait);

        await feed.Writer.WriteAsync(StubPcm.AboveMinimum);
        feed.Writer.Complete();
        var transcript = await upload.WaitAsync(Wait);

        transcript.Should().Be("Hello world.");
        Encoding.UTF8.GetString(transport.UploadedBody).Should().EndWith("--\r\n");
    }

    [Test]
    public async Task ACancelWhileRecordingAbandonsTheRequest()
    {
        var feed = Channel.CreateUnbounded<byte[]>();
        var transport = Answering("""{"text":"Hello world."}""");
        using var cts = new CancellationTokenSource();
        var upload = MakeTranscriber("k", transport).TranscribeAsync(feed.Reader.ReadAllAsync(), 16_000, null, cts.Token);
        await transport.Entered.Task.WaitAsync(Wait);
        await feed.Writer.WriteAsync(StubPcm.AboveMinimum);

        await cts.CancelAsync();

        await FluentActions.Awaiting(() => upload.WaitAsync(Wait)).Should().ThrowAsync<OperationCanceledException>();
        transport.Requests.Should().BeEmpty();
    }

    [Test]
    public async Task ACancelDuringTranscribingCancelsTheRequestNotJustTheWait()
    {
        var transport = Answering("""{"text":"Hello world."}""");
        transport.Hold = new TaskCompletionSource();
        using var cts = new CancellationTokenSource();
        var upload = CollectTranscript(MakeTranscriber("k", transport), ct: cts.Token);
        await transport.Holding.Task.WaitAsync(Wait);

        await cts.CancelAsync();

        await FluentActions.Awaiting(() => upload.WaitAsync(Wait)).Should().ThrowAsync<OperationCanceledException>();
    }

    [Test]
    public async Task ATooShortClipAbandonsTheRequestInsteadOfCompletingIt()
    {
        var transport = Answering("""{"text":"Hello world."}""");

        await FluentActions.Awaiting(() => CollectTranscript(MakeTranscriber("k", transport), new byte[8]))
            .Should().ThrowExactlyAsync<AudioTooShortException>();

        transport.Requests.Should().BeEmpty();
    }

    [Test]
    public async Task ATooShortFloorWrappedByTheTransportIsUnwrapped()
    {
        var transport = new FakeHttpTransport { Failure = new HttpRequestException("send failed", new IOException("copy", new AudioTooShortException())) };

        await FluentActions.Awaiting(() => CollectTranscript(MakeTranscriber("k", transport)))
            .Should().ThrowExactlyAsync<AudioTooShortException>();
    }

    [Test]
    public async Task AFeedWithNoFramesIsTooShort()
    {
        await FluentActions.Awaiting(() => MakeTranscriber("k", Answering("""{"text":"x"}""")).TranscribeAsync(OneShot(), 16_000, null, default))
            .Should().ThrowExactlyAsync<AudioTooShortException>();
    }

    [Test]
    public async Task TheFloorIsInclusiveAndCountsBytesAcrossFrames()
    {
        var half = new byte[SyncSTTLimits.MinPcmBytes / 2];
        var transport = Answering("""{"text":"ok"}""");

        var result = await MakeTranscriber("k", transport).TranscribeAsync(OneShot(half, half), 16_000, null, default);

        result.Should().Be("ok");
    }

    [Test]
    public async Task AStalledResponseTimesOutRatherThanReadingAsACancel()
    {
        var transport = Answering("""{"text":"ok"}""");
        transport.Hold = new TaskCompletionSource();
        var transcriber = new AssemblyAITranscriber(() => "k", transport, idleTimeout: TimeSpan.FromMilliseconds(200));

        await FluentActions.Awaiting(() => CollectTranscript(transcriber).WaitAsync(Wait)).Should().ThrowAsync<TimeoutException>();
    }

    [Test]
    public async Task FramesStillArrivingKeepTheRequestAlivePastTheIdleTimeout()
    {
        var transport = Answering("""{"text":"ok"}""");
        var transcriber = new AssemblyAITranscriber(() => "k", transport, idleTimeout: TimeSpan.FromMilliseconds(300));

        var result = await transcriber.TranscribeAsync(SlowFeed(8, TimeSpan.FromMilliseconds(100)), 16_000, null, default).WaitAsync(Wait);

        result.Should().Be("ok");
    }

    [Test]
    public async Task ARealHttpClientUploadsChunkedHttp11WithoutAContentLength()
    {
        await using var server = new LoopbackServer("""{"text":"over the wire"}""");
        var transcriber = new AssemblyAITranscriber(() => "k", new HttpClientTransport(), server.BaseUrl, () => true, () => null);
        var pcm = StubPcm.EveryByteValueAboveMinimum;

        var result = await transcriber.TranscribeAsync(OneShot(pcm[..100], pcm[100..]), 16_000, null, default).WaitAsync(Wait);

        result.Should().Be("over the wire");
        var (head, body) = await server.Received.WaitAsync(Wait);
        head.Should().StartWith("POST /v1/transcribe/live HTTP/1.1\r\n");
        head.Should().ContainEquivalentOf("Transfer-Encoding: chunked\r\n");
        head.Should().NotContainEquivalentOf("Content-Length");
        head.Should().Contain("Authorization: k\r\n");
        head.Should().Contain($"User-Agent: {UserAgent.Current}\r\n");
        IndexOf(body, pcm).Should().BeGreaterThan(IndexOf(body, "name=\"audio\""));
        IndexOf(body, "name=\"config\"").Should().BeLessThan(IndexOf(body, "name=\"audio\""));
        Encoding.UTF8.GetString(body).Should().EndWith("--\r\n");
    }

    [Test]
    public async Task ARealHttpClientSurfacesTheTooShortFloorUnwrapped()
    {
        await using var server = new LoopbackServer("""{"text":"never"}""");
        var transcriber = new AssemblyAITranscriber(() => "k", new HttpClientTransport(), server.BaseUrl, () => true, () => null);

        await FluentActions.Awaiting(() => transcriber.TranscribeAsync(OneShot(new byte[8]), 16_000, null, default).WaitAsync(Wait))
            .Should().ThrowExactlyAsync<AudioTooShortException>();
    }

    private static async IAsyncEnumerable<byte[]> SlowFeed(int frames, TimeSpan interval)
    {
        for (var i = 0; i < frames; i++)
        {
            await Task.Delay(interval);
            yield return new byte[SyncSTTLimits.MinPcmBytes];
        }
    }

    /// <summary>
    /// A one-request HTTP/1.1 server on loopback. Captures the request head and the de-chunked body,
    /// then answers 200 with a fixed JSON body and closes the connection.
    /// </summary>
    private sealed class LoopbackServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task _serve;
        private readonly TaskCompletionSource<(string Head, byte[] Body)> _received = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public LoopbackServer(string responseJson)
        {
            _listener.Start();
            BaseUrl = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}");
            _serve = ServeAsync(responseJson);
        }

        public Uri BaseUrl { get; }

        public Task<(string Head, byte[] Body)> Received => _received.Task;

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            try
            {
                await _serve;
            }
            catch (Exception)
            {
            }
        }

        private async Task ServeAsync(string responseJson)
        {
            using var socket = await _listener.AcceptTcpClientAsync();
            var stream = socket.GetStream();
            try
            {
                var head = new StringBuilder();
                string line;
                while ((line = await ReadLine(stream)).Length > 0)
                {
                    head.Append(line).Append("\r\n");
                }

                var body = new MemoryStream();
                while (true)
                {
                    var size = Convert.ToInt32(await ReadLine(stream), 16);
                    if (size == 0)
                    {
                        await ReadLine(stream);
                        break;
                    }

                    var chunk = new byte[size];
                    await stream.ReadExactlyAsync(chunk);
                    body.Write(chunk);
                    await ReadLine(stream);
                }

                _received.TrySetResult((head.ToString(), body.ToArray()));
                var payload = Encoding.UTF8.GetBytes(responseJson);
                var response = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(response);
                await stream.WriteAsync(payload);
            }
            catch (Exception e) when (e is IOException or EndOfStreamException or FormatException)
            {
                _received.TrySetException(e);
            }
        }

        private static async Task<string> ReadLine(NetworkStream stream)
        {
            var bytes = new List<byte>();
            var one = new byte[1];
            while (true)
            {
                await stream.ReadExactlyAsync(one);
                if (one[0] == '\n' && bytes.Count > 0 && bytes[^1] == '\r')
                {
                    bytes.RemoveAt(bytes.Count - 1);
                    return Encoding.ASCII.GetString([.. bytes]);
                }

                bytes.Add(one[0]);
            }
        }
    }
}
