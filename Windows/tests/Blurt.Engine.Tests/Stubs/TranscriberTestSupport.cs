using System.Net;
using System.Text;
using System.Text.Json;
using Blurt.Engine.STT;

namespace Blurt.Engine.Tests.Stubs;

/// <summary>Scaffolding shared by the transcriber suites.</summary>
public static class TranscriberTestSupport
{
    /// <summary>
    /// A transcriber with every setting pinned. The default transport answers 500, so a test that
    /// should never reach the wire fails rather than succeeding on an empty body.
    /// </summary>
    public static AssemblyAITranscriber MakeTranscriber(
        string? apiKey,
        FakeHttpTransport? transport = null,
        bool enhancedTranscripts = true,
        string? customStyle = null,
        Uri? baseUrl = null) =>
        new(() => apiKey, transport ?? new FakeHttpTransport(), baseUrl, () => enhancedTranscripts, () => customStyle);

    /// <summary>A transport that answers the next request with <paramref name="json"/>.</summary>
    public static FakeHttpTransport Answering(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new FakeHttpTransport().Enqueue(status, json);

    /// <summary>Runs one request off a single-frame feed of <paramref name="pcm"/> (<see cref="StubPcm.AboveMinimum"/> by default).</summary>
    public static Task<string> CollectTranscript(
        AssemblyAITranscriber transcriber,
        byte[]? pcm = null,
        TranscriptionContext? context = null,
        CancellationToken ct = default) =>
        transcriber.TranscribeAsync(OneShot(pcm ?? StubPcm.AboveMinimum), SyncSTTLimits.SampleRate, context, ct);

    /// <summary>A feed that yields <paramref name="frames"/> and ends.</summary>
    public static async IAsyncEnumerable<byte[]> OneShot(params byte[][] frames)
    {
        foreach (var frame in frames)
        {
            await Task.Yield();
            yield return frame;
        }
    }

    /// <summary>A context with every field defaulted to absent.</summary>
    public static TranscriptionContext Context(
        string? appName = null,
        string? windowTitle = null,
        string? fieldLabel = null,
        string? priorText = null,
        string? selectedText = null,
        string[]? recentTranscripts = null,
        string[]? keyTerms = null) =>
        new(appName, windowTitle, fieldLabel, priorText, selectedText, recentTranscripts ?? [], keyTerms ?? [], false);

    /// <summary>The JSON payload of the <c>config</c> part of a multipart dictation body, parsed.</summary>
    public static JsonElement ConfigPart(byte[] body)
    {
        var marker = Encoding.ASCII.GetBytes("Content-Type: application/json\r\n\r\n");
        var start = IndexOf(body, marker) + marker.Length;
        var length = body.AsSpan(start).IndexOf("\r\n--"u8);
        using var document = JsonDocument.Parse(body.AsMemory(start, length));
        return document.RootElement.Clone();
    }

    /// <summary>The property names of a JSON object, sorted ordinally.</summary>
    public static string[] Keys(JsonElement element) =>
        [.. element.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)];

    /// <summary>The index of <paramref name="needle"/> in <paramref name="haystack"/>, or -1.</summary>
    public static int IndexOf(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle);

    /// <summary>The index of the UTF-8 encoding of <paramref name="needle"/> in <paramref name="haystack"/>, or -1.</summary>
    public static int IndexOf(byte[] haystack, string needle) => IndexOf(haystack, Encoding.UTF8.GetBytes(needle));

    /// <summary>The config JSON <see cref="AssemblyAITranscriber.MakeConfigData"/> encodes, parsed.</summary>
    public static JsonElement ParseConfig(byte[] config)
    {
        using var document = JsonDocument.Parse(config);
        return document.RootElement.Clone();
    }
}
