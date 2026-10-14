using System.Net.WebSockets;
using System.Text;
using Blurt.Engine.Tests.Stubs;
using Blurt.Engine.TTS;
using FluentAssertions;

namespace Blurt.Engine.Tests.TTS;

public sealed class AssemblyAISpeechSynthesizerWireFormatTests
{
    [Test]
    public void ConnectsToTheStreamingHostWithVoiceAndSampleRateAsQueryParams()
    {
        AssemblyAISpeechSynthesizer.Url.AbsoluteUri
            .Should().Be("wss://streaming-tts.assemblyai.com/v1/ws/?voice=jane&sample_rate=24000");
    }

    [Test]
    public void CommandsAreTheServicesGenerateAndFlushFrames()
    {
        AssemblyAISpeechSynthesizer.Command("Generate", "Hi.").Should().Be("""{"text":"Hi.","type":"Generate"}""");
        AssemblyAISpeechSynthesizer.Command("Flush").Should().Be("""{"type":"Flush"}""");
    }

    [Test]
    public void AudioFramesDecodeTheirBase64Pcm()
    {
        byte[] pcm = [0x01, 0x00, 0xFF, 0x7F];

        var frame = AssemblyAISpeechSynthesizer.Frame($$"""{"type":"Audio","audio":"{{Convert.ToBase64String(pcm)}}"}""");

        frame.Should().BeOfType<SpeechFrame.Audio>().Which.Pcm.Should().Equal(pcm);
    }

    [Test]
    public void FlushDoneIsThePerSegmentAcknowledgement()
    {
        AssemblyAISpeechSynthesizer.Frame("""{"type":"FlushDone"}""").Should().Be(new SpeechFrame.FlushDone());
    }

    [Test]
    public void ErrorFramesCarryTheirCodeNumericOrString()
    {
        AssemblyAISpeechSynthesizer.Frame("""{"type":"Error","error_code":1008,"error":"Unauthorized: Invalid API key"}""")
            .Should().Be(new SpeechFrame.Error("(1008) Unauthorized: Invalid API key"));
        AssemblyAISpeechSynthesizer.Frame("""{"type":"Error","error_code":"x","error":" "}""")
            .Should().Be(new SpeechFrame.Error("(x) unknown error"));
        AssemblyAISpeechSynthesizer.Frame("""{"type":"Error"}""").Should().Be(new SpeechFrame.Error("unknown error"));
    }

    [TestCase("""{"type":"Begin"}""")]
    [TestCase("""{"type":"WordBoundaries","words":[]}""")]
    [TestCase("""{"type":"Audio"}""")]
    [TestCase("not json")]
    [TestCase("[1]")]
    public void EverythingElseIsIgnoredNotFatal(string message)
    {
        AssemblyAISpeechSynthesizer.Frame(message).Should().Be(new SpeechFrame.Ignored());
    }

    [Test]
    public void StringAndBinarySocketMessagesDecodeTheSameWay()
    {
        var payload = Encoding.UTF8.GetBytes("""{"type":"FlushDone"}""");

        var text = ClientWebSocketSpeechSocket.Decode(WebSocketMessageType.Text, payload);
        var binary = ClientWebSocketSpeechSocket.Decode(WebSocketMessageType.Binary, payload);

        AssemblyAISpeechSynthesizer.Frame(text!).Should().Be(new SpeechFrame.FlushDone());
        AssemblyAISpeechSynthesizer.Frame(binary!).Should().Be(new SpeechFrame.FlushDone());
        ClientWebSocketSpeechSocket.Decode(WebSocketMessageType.Close, payload).Should().BeNull();
    }

    [Test]
    public async Task AMissingKeyFailsBeforeAnySocketOpens()
    {
        var connects = 0;
        var synth = new AssemblyAISpeechSynthesizer(
            () => null,
            (_, _, _) =>
            {
                connects++;
                return Task.FromResult<ISpeechSocket>(new FakeSpeechSocket(FakeSpeechSocket.Service()));
            });

        var consume = async () => await Collect(synth.Synthesize("Hello there.", CancellationToken.None));

        (await consume.Should().ThrowAsync<BlurtException>()).Which.Error.Should().Be(new BlurtError.ApiKeyMissing());
        connects.Should().Be(0);
    }

    [Test]
    public async Task BlankTextFinishesImmediatelyWithNoAudio()
    {
        var connects = 0;
        var synth = new AssemblyAISpeechSynthesizer(
            () => null,
            (_, _, _) =>
            {
                connects++;
                return Task.FromResult<ISpeechSocket>(new FakeSpeechSocket(FakeSpeechSocket.Service()));
            });

        var chunks = await Collect(synth.Synthesize("   ", CancellationToken.None));

        chunks.Should().BeEmpty();
        connects.Should().Be(0);
    }

    internal static async Task<List<byte[]>> Collect(IAsyncEnumerable<byte[]> stream)
    {
        var chunks = new List<byte[]>();
        await foreach (var chunk in stream)
        {
            chunks.Add(chunk);
        }

        return chunks;
    }
}

public sealed class AssemblyAISpeechSynthesizerExchangeTests
{
    private const string TwoSentences = "The first sentence here. And the second one.";

    [Test]
    public async Task FlushesEachSegmentStreamsAudioInOrderAndTerminatesOnceEveryFlushIsAcked()
    {
        var socket = new FakeSpeechSocket(FakeSpeechSocket.Service());
        Uri? connectedUrl = null;
        string? connectedKey = null;
        var synth = new AssemblyAISpeechSynthesizer(
            () => "raw-key",
            (url, key, _) =>
            {
                connectedUrl = url;
                connectedKey = key;
                return Task.FromResult<ISpeechSocket>(socket);
            });

        var chunks = await Collect(synth.Synthesize(TwoSentences, CancellationToken.None));

        chunks.Should().HaveCount(2);
        chunks[0].Should().Equal(1, 0);
        chunks[1].Should().Equal(2, 0);
        socket.Sent.Should().Equal(
            """{"text":"The first sentence here.","type":"Generate"}""",
            """{"type":"Flush"}""",
            """{"text":"And the second one.","type":"Generate"}""",
            """{"type":"Flush"}""",
            """{"type":"Terminate"}""");
        socket.ClosedNormally.Should().BeTrue();
        connectedKey.Should().Be("raw-key");
        connectedUrl.Should().Be(AssemblyAISpeechSynthesizer.Url);
    }

    [Test]
    public async Task AnInBandErrorFrameFailsTheStreamAndReleasesTheSocket()
    {
        var socket = new FakeSpeechSocket(_ => ["""{"type":"Error","error_code":1008,"error":"Unauthorized: Invalid API key"}"""]);

        var consume = async () => await Collect(Synthesizer(socket).Synthesize(TwoSentences, CancellationToken.None));

        (await consume.Should().ThrowAsync<SpeechSynthesisException>())
            .Which.Failure.Should().Be(new SpeechSynthesisFailure.Server("(1008) Unauthorized: Invalid API key"));
        socket.ClosedNormally.Should().BeFalse();
    }

    [Test]
    public async Task AConnectionDroppedMidExchangeFailsTheStreamInsteadOfHanging()
    {
        var socket = new FakeSpeechSocket(_ => []);
        var stream = Synthesizer(socket).Synthesize(TwoSentences, CancellationToken.None);
        socket.Drop();

        var consume = async () => await Collect(stream);

        await consume.Should().ThrowAsync<WebSocketException>();
        socket.ClosedNormally.Should().BeFalse();
    }

    [Test]
    public async Task ASilentServerTimesOut()
    {
        var socket = new FakeSpeechSocket(_ => []);
        var synth = new AssemblyAISpeechSynthesizer(
            () => "k",
            (_, _, _) => Task.FromResult<ISpeechSocket>(socket),
            TimeSpan.FromMilliseconds(50));

        var consume = async () => await Collect(synth.Synthesize("Say something here.", CancellationToken.None));

        var thrown = await consume.Should().ThrowAsync<SpeechSynthesisException>();
        thrown.Which.Failure.Should().Be(new SpeechSynthesisFailure.TimedOut());
        thrown.Which.Message.Should().Be("Text-to-speech stopped responding.");
        socket.ClosedNormally.Should().BeFalse();
    }

    [Test]
    public async Task AConnectThatNeverCompletesTimesOut()
    {
        var synth = new AssemblyAISpeechSynthesizer(
            () => "k",
            async (_, _, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return new FakeSpeechSocket(FakeSpeechSocket.Service());
            },
            TimeSpan.FromMilliseconds(50));

        var consume = async () => await Collect(synth.Synthesize("Say something here.", CancellationToken.None));

        (await consume.Should().ThrowAsync<SpeechSynthesisException>())
            .Which.Failure.Should().Be(new SpeechSynthesisFailure.TimedOut());
    }

    [Test]
    public async Task BeginWordBoundariesAndEmptyAudioDoNotCountAsAudioOrAcks()
    {
        var socket = new FakeSpeechSocket(command =>
            !command.Contains("\"Flush\"", StringComparison.Ordinal)
                ? ["""{"type":"Begin"}"""]
                : ["""{"type":"Audio","audio":""}""", """{"type":"WordBoundaries"}""", FakeSpeechSocket.Audio([9, 9]), FakeSpeechSocket.FlushDone]);

        var chunks = await Collect(Synthesizer(socket).Synthesize("Just one sentence.", CancellationToken.None));

        chunks.Should().ContainSingle().Which.Should().Equal(9, 9);
    }

    [Test]
    public async Task CancellingTheConsumerClosesTheSocketAbruptly()
    {
        var socket = new FakeSpeechSocket(_ => []);
        using var cancel = new CancellationTokenSource();
        var consumer = Collect(Synthesizer(socket).Synthesize(TwoSentences, cancel.Token));
        while (socket.Sent.Count == 0)
        {
            await Task.Yield();
        }

        await cancel.CancelAsync();

        var consume = async () => await consumer;
        await consume.Should().ThrowAsync<OperationCanceledException>();
        socket.ClosedNormally.Should().BeFalse();
    }

    [Test]
    public async Task AbandoningTheEnumerationClosesTheSocketAbruptly()
    {
        var socket = new FakeSpeechSocket(command =>
            command.Contains("\"Flush\"", StringComparison.Ordinal) ? [FakeSpeechSocket.Audio([5, 0])] : []);

        await foreach (var _ in Synthesizer(socket).Synthesize(TwoSentences, CancellationToken.None))
        {
            break;
        }

        socket.ClosedNormally.Should().BeFalse();
    }

    [Test]
    public void AServerFailureMessageNamesTheServersReason()
    {
        new SpeechSynthesisException(new SpeechSynthesisFailure.Server("boom")).Message
            .Should().Be("Text-to-speech failed: boom");
    }

    private static Task<List<byte[]>> Collect(IAsyncEnumerable<byte[]> stream) =>
        AssemblyAISpeechSynthesizerWireFormatTests.Collect(stream);

    private static AssemblyAISpeechSynthesizer Synthesizer(FakeSpeechSocket socket) =>
        new(() => "raw-key", (_, _, _) => Task.FromResult<ISpeechSocket>(socket));
}
