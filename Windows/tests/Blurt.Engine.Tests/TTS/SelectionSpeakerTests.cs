using System.Net;
using System.Text.Json;
using Blurt.Engine.Config;
using Blurt.Engine.FocusCapture;
using Blurt.Engine.Tests.Stubs;
using Blurt.Engine.TTS;
using FluentAssertions;

namespace Blurt.Engine.Tests.TTS;

public sealed class SelectionSpeakerTests
{
    private static readonly ReadAloudStyle s_fastSkipping = new(2, SkipsJargon: true);

    private sealed class Rig
    {
        public RecordingSink Sink { get; } = new();

        public double? Rate { get; private set; }

        public int Connects { get; private set; }

        public SelectionSpeaker Speaker { get; }

        public Rig(FakeSpeechSocket socket, FakeHttpTransport? gateway = null, IHttpTransport? transport = null, string? key = "k")
        {
            var llm = new ReadAloudLLM(() => key, transport ?? gateway ?? new FakeHttpTransport());
            var synthesizer = new AssemblyAISpeechSynthesizer(
                () => key,
                (_, _, _) =>
                {
                    Connects++;
                    return Task.FromResult<ISpeechSocket>(socket);
                });
            Speaker = new SelectionSpeaker(synthesizer, llm, rate =>
            {
                Rate = rate;
                return Sink;
            });
        }
    }

    private sealed class FakeCopy(string? answer) : ISelectionCopy
    {
        public int Calls { get; private set; }

        public int? MaxCharacters { get; private set; }

        public Task<string?> CopySelectionAsync(int maxCharacters, CancellationToken ct)
        {
            Calls++;
            MaxCharacters = maxCharacters;
            return Task.FromResult(answer);
        }
    }

    private sealed class FakeReader(SelectionRead result) : ISelectionReader
    {
        public int? MaxChars { get; private set; }

        public SelectionRead CaptureSelectedText(int maxChars)
        {
            MaxChars = maxChars;
            return result;
        }
    }

    /// <summary>A gateway that ignores cancellation: it waits for the release, then answers 200 anyway.</summary>
    private sealed class LateAnsweringTransport(string content) : IHttpTransport
    {
        private readonly TaskCompletionSource _asked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Asked => _asked.Task;

        public void Release() => _released.TrySetResult();

        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            _asked.TrySetResult();
            await _released.Task;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Completion(content)), RequestMessage = request };
        }
    }

    private static string Completion(string? content, string? finishReason = "stop") =>
        JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content }, finish_reason = finishReason },
            },
        });

    /// <summary>The text of every <c>Generate</c> the speaker sent, in order.</summary>
    private static List<string> Generated(FakeSpeechSocket socket) =>
        [.. socket.Sent
            .Select(command => JsonDocument.Parse(command).RootElement)
            .Where(root => root.GetProperty("type").GetString() == "Generate")
            .Select(root => root.GetProperty("text").GetString()!)];

    [Test]
    public async Task PlaysEveryChunkInOrderThenDrains()
    {
        var rig = new Rig(new FakeSpeechSocket(FakeSpeechSocket.Service()));

        await rig.Speaker.SpeakAsync("The first sentence here. And the second one.", ReadAloudStyle.Standard, CancellationToken.None);

        rig.Sink.Chunks.Should().BeEquivalentTo(new[] { new byte[] { 1, 0 }, [2, 0] }, options => options.WithStrictOrdering());
        rig.Sink.Drained.Should().BeTrue();
    }

    [Test]
    public async Task ASynthesisFailureSilencesPlaybackAndSurfacesTheError()
    {
        var socket = new FakeSpeechSocket(_ => ["""{"type":"Error","error":"boom"}"""]);
        var rig = new Rig(socket);

        var act = () => rig.Speaker.SpeakAsync("Say something here.", ReadAloudStyle.Standard, CancellationToken.None);

        (await act.Should().ThrowAsync<SpeechSynthesisException>()).Which.Failure.Should().Be(new SpeechSynthesisFailure.Server("boom"));
        rig.Sink.Stopped.Should().BeTrue();
        rig.Sink.Drained.Should().BeFalse();
    }

    [Test]
    public async Task CancellingTheSpeakingTaskStopsPlayback()
    {
        var socket = new FakeSpeechSocket(_ => []);
        var rig = new Rig(socket);
        using var cts = new CancellationTokenSource();
        var speaking = rig.Speaker.SpeakAsync("Say something here.", ReadAloudStyle.Standard, cts.Token);
        while (socket.Sent.Count == 0)
        {
            await Task.Yield();
        }

        await cts.CancelAsync();
        try
        {
            await speaking;
        }
        catch (Exception)
        {
            // The outcome is asserted on the sink; the failure kind is the synthesizer's.
        }

        rig.Sink.Stopped.Should().BeTrue();
    }

    [Test]
    public async Task TheStandardStyleReadsTheSelectionVerbatimAtNaturalPaceWithNoGatewayCall()
    {
        var socket = new FakeSpeechSocket(FakeSpeechSocket.Service());
        var gateway = new FakeHttpTransport().Enqueue(HttpStatusCode.OK, Completion("unused"));
        var rig = new Rig(socket, gateway);

        await rig.Speaker.SpeakAsync("Run `npm ci` first.", ReadAloudStyle.Standard, CancellationToken.None);

        Generated(socket).Should().Equal("Run `npm ci` first.");
        rig.Rate.Should().Be(1);
        gateway.Requests.Should().BeEmpty();
    }

    [Test]
    public async Task SkippingJargonSpeaksTheGatewaysRewriteAtTheStylesRate()
    {
        var socket = new FakeSpeechSocket(FakeSpeechSocket.Service());
        var gateway = new FakeHttpTransport().Enqueue(HttpStatusCode.OK, Completion("Run the command first."));
        var rig = new Rig(socket, gateway);

        await rig.Speaker.SpeakAsync("Run `npm ci --prefer-offline` first.", s_fastSkipping, CancellationToken.None);

        Generated(socket).Should().Equal("Run the command first.");
        rig.Rate.Should().Be(2);
        rig.Sink.Drained.Should().BeTrue();
    }

    [Test]
    public async Task AFailedRewriteFallsBackToReadingTheSelectionVerbatim()
    {
        var socket = new FakeSpeechSocket(FakeSpeechSocket.Service());
        var gateway = new FakeHttpTransport().Enqueue(HttpStatusCode.ServiceUnavailable, string.Empty);
        var rig = new Rig(socket, gateway);

        await rig.Speaker.SpeakAsync("Email jo@example.com today.", s_fastSkipping, CancellationToken.None);

        Generated(socket).Should().Equal("Email jo@example.com today.");
    }

    [Test]
    public async Task ARewriteTransportFailureFallsBackToReadingTheSelectionVerbatim()
    {
        var socket = new FakeSpeechSocket(FakeSpeechSocket.Service());
        var gateway = new FakeHttpTransport { Failure = new HttpRequestException("offline") };
        var rig = new Rig(socket, gateway);

        await rig.Speaker.SpeakAsync("Email jo@example.com today.", s_fastSkipping, CancellationToken.None);

        Generated(socket).Should().Equal("Email jo@example.com today.");
    }

    [Test]
    public async Task AnAnswerIsSpokenAsTheGatewayWroteItAtTheStylesRate()
    {
        var socket = new FakeSpeechSocket(FakeSpeechSocket.Service());
        var gateway = new FakeHttpTransport().Enqueue(HttpStatusCode.OK, Completion("It reports a strong quarter."));
        var rig = new Rig(socket, gateway);

        await rig.Speaker.AnswerAsync("Summarize this.", "Revenue rose `12%` in Q3.", s_fastSkipping, CancellationToken.None);

        gateway.Requests.Should().ContainSingle();
        Generated(socket).Should().Equal("It reports a strong quarter.");
        rig.Rate.Should().Be(2);
    }

    [Test]
    public async Task AFailedAnswerSurfacesItsErrorAndOpensNoSocket()
    {
        var socket = new FakeSpeechSocket(FakeSpeechSocket.Service());
        var gateway = new FakeHttpTransport().Enqueue(HttpStatusCode.ServiceUnavailable, string.Empty);
        var rig = new Rig(socket, gateway);

        var act = () => rig.Speaker.AnswerAsync("Explain.", "x", ReadAloudStyle.Standard, CancellationToken.None);

        (await act.Should().ThrowAsync<ReadAloudLLMException>()).Which.Failure.Should().Be(new ReadAloudLLMFailure.Status(503));
        socket.Sent.Should().BeEmpty();
        rig.Connects.Should().Be(0);
    }

    [Test]
    public async Task AStopDuringTheRewritePlaysNothingRatherThanFallingBack()
    {
        var socket = new FakeSpeechSocket(FakeSpeechSocket.Service());
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateway = new FakeHttpTransport { Hold = hold };
        var rig = new Rig(socket, gateway);
        using var cts = new CancellationTokenSource();
        var speaking = rig.Speaker.SpeakAsync("Say something here.", s_fastSkipping, cts.Token);
        await gateway.Holding.Task;

        await cts.CancelAsync();
        hold.TrySetResult();

        await speaking.Awaiting(t => t).Should().ThrowAsync<OperationCanceledException>();
        rig.Connects.Should().Be(0);
        socket.Sent.Should().BeEmpty();
        rig.Sink.Chunks.Should().BeEmpty();
    }

    [Test]
    public async Task ARewriteThatAnswersAfterTheStopStillPlaysNothingAndOpensNoSocket()
    {
        var socket = new FakeSpeechSocket(FakeSpeechSocket.Service());
        var transport = new LateAnsweringTransport("Say something here.");
        var rig = new Rig(socket, transport: transport);
        using var cts = new CancellationTokenSource();
        var speaking = rig.Speaker.SpeakAsync("Say something here.", s_fastSkipping, cts.Token);
        await transport.Asked;

        await cts.CancelAsync();
        transport.Release();

        await speaking.Awaiting(t => t).Should().ThrowAsync<OperationCanceledException>();
        rig.Connects.Should().Be(0);
        rig.Sink.Chunks.Should().BeEmpty();
    }

    [Test]
    public async Task AReadWithNoKeyOpensNoSocket()
    {
        var socket = new FakeSpeechSocket(FakeSpeechSocket.Service());
        var rig = new Rig(socket, key: null);

        var act = () => rig.Speaker.SpeakAsync("Say something here.", ReadAloudStyle.Standard, CancellationToken.None);

        (await act.Should().ThrowAsync<BlurtException>()).Which.Error.Should().Be(new BlurtError.ApiKeyMissing());
        rig.Connects.Should().Be(0);
        rig.Sink.Stopped.Should().BeTrue();
    }

    [Test]
    public async Task TheFallbackRunsOnlyWhenTheReadWasUnreadableNeverOnABareCaret()
    {
        var copy = new FakeCopy("copied");

        (await SelectionSpeaker.ResolveAsync(new SelectionRead.Text("from AX"), copy, CancellationToken.None)).Should().Be("from AX");
        (await SelectionSpeaker.ResolveAsync(SelectionRead.None, copy, CancellationToken.None)).Should().BeNull();
        copy.Calls.Should().Be(0);
        (await SelectionSpeaker.ResolveAsync(SelectionRead.Unreadable, copy, CancellationToken.None)).Should().Be("copied");
        copy.Calls.Should().Be(1);
        copy.MaxCharacters.Should().Be(SelectionSpeaker.MaxCharacters);
    }

    [Test]
    public async Task TheFocusedSelectionReadsOffTheCallerAndFallsBackToTheCopyWhenUnreadable()
    {
        var reader = new FakeReader(SelectionRead.Unreadable);
        var copy = new FakeCopy("copied");

        var selection = await SelectionSpeaker.FocusedSelectionAsync(reader, copy, CancellationToken.None);

        selection.Should().Be("copied");
        reader.MaxChars.Should().Be(SelectionSpeaker.MaxCharacters);
    }

    [Test]
    public async Task TheFocusedSelectionReturnsTheReadTextWithoutCopying()
    {
        var reader = new FakeReader(new SelectionRead.Text("from AX"));
        var copy = new FakeCopy("copied");

        var selection = await SelectionSpeaker.FocusedSelectionAsync(reader, copy, CancellationToken.None);

        selection.Should().Be("from AX");
        copy.Calls.Should().Be(0);
    }
}
