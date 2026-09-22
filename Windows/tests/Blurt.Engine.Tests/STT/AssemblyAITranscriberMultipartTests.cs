using System.Text;
using Blurt.Engine.STT;
using Blurt.Engine.Tests.Stubs;
using FluentAssertions;
using static Blurt.Engine.Tests.Stubs.TranscriberTestSupport;

namespace Blurt.Engine.Tests.STT;

public sealed class AssemblyAITranscriberMultipartTests
{
    [Test]
    public void TheMultipartPartsFrameTheConfigAndAudioTheDictationApiExpects()
    {
        var head = Encoding.UTF8.GetString(DictationMultipart.ConfigHead("""{"channels":1}"""u8.ToArray(), "BOUND"));
        var audio = Encoding.UTF8.GetString(DictationMultipart.AudioPartHeader("BOUND"));
        var close = Encoding.UTF8.GetString(DictationMultipart.ClosingBoundary("BOUND"));

        head.Should().StartWith("--BOUND\r\n");
        close.Should().Be("\r\n--BOUND--\r\n");
        head.Should().Contain("Content-Disposition: form-data; name=\"config\"\r\n");
        audio.Should().Contain("Content-Disposition: form-data; name=\"audio\"; filename=\"audio.pcm\"\r\n");
        head.Should().EndWith("Content-Type: application/json\r\n\r\n{\"channels\":1}\r\n");
        audio.Should().StartWith("--BOUND\r\n");
        audio.Should().EndWith("Content-Type: audio/pcm\r\n\r\n");
    }

    [Test]
    public async Task TheStreamedBodySendsConfigFirstAndAudioLastByteExact()
    {
        var pcm = StubPcm.EveryByteValueAboveMinimum;
        var transport = Answering("""{"text":"ok"}""");

        await CollectTranscript(MakeTranscriber("test-key", transport), pcm);

        var body = transport.UploadedBody;
        var configHeader = IndexOf(body, "name=\"config\"");
        var audioHeader = IndexOf(body, "name=\"audio\"; filename=\"audio.pcm\"\r\nContent-Type: audio/pcm\r\n\r\n");
        var pcmStart = IndexOf(body, pcm);
        configHeader.Should().BePositive();
        audioHeader.Should().BeGreaterThan(configHeader);
        pcmStart.Should().BeGreaterThan(audioHeader);
        var tail = Encoding.UTF8.GetString(body.AsSpan(pcmStart + pcm.Length));
        tail.Should().Be($"\r\n--{Boundary(body)}--\r\n");
    }

    [Test]
    public async Task TheBodyBoundaryMatchesTheContentTypeHeader()
    {
        var transport = Answering("""{"text":"ok"}""");

        await CollectTranscript(MakeTranscriber("test-key", transport));

        var contentType = transport.Requests[0].ContentType!;
        contentType.MediaType.Should().Be("multipart/form-data");
        contentType.Parameters.Single(p => p.Name == "boundary").Value.Should().Be(Boundary(transport.UploadedBody));
    }

    [Test]
    public async Task EveryFrameIsWrittenInOrder()
    {
        byte[] first = [1, .. new byte[SyncSTTLimits.MinPcmBytes]];
        byte[] second = [2, 3, 4];
        var transport = Answering("""{"text":"ok"}""");

        await MakeTranscriber("k", transport).TranscribeAsync(OneShot(first, second), 16_000, null, default);

        IndexOf(transport.UploadedBody, [.. first, .. second]).Should().BePositive();
    }

    private static string Boundary(byte[] body)
    {
        var firstCrlf = body.AsSpan().IndexOf("\r\n"u8);
        return Encoding.UTF8.GetString(body.AsSpan(2, firstCrlf - 2));
    }
}
