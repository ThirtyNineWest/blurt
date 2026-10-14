using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace Blurt.Engine.TTS;

/// <summary>One decoded server frame of the streaming TTS exchange.</summary>
public abstract record SpeechFrame
{
    private SpeechFrame()
    {
    }

    /// <summary>A chunk of PCM16 little-endian mono audio, never empty.</summary>
    public sealed record Audio(byte[] Pcm) : SpeechFrame;

    /// <summary>The acknowledgement of one <c>Flush</c>.</summary>
    public sealed record FlushDone : SpeechFrame;

    /// <summary>An in-band failure, with the server's code in parentheses when it sent one.</summary>
    public sealed record Error(string Message) : SpeechFrame;

    /// <summary><c>Begin</c>, <c>Warning</c>, <c>WordBoundaries</c>, <c>Cancelled</c>, anything newer, and anything unparseable.</summary>
    public sealed record Ignored : SpeechFrame;
}

/// <summary>
/// Streams speech for a piece of text from AssemblyAI's streaming TTS, as raw PCM16 little-endian
/// mono at <see cref="SampleRate"/>. It connects to <see cref="Url"/> with the raw API key, sends
/// <c>Generate</c> then <c>Flush</c> for each <see cref="SpeechSegmenter"/> segment, receives
/// <c>Audio</c> frames and one <c>FlushDone</c> per flush, and closes with <c>Terminate</c> once
/// every flush is acknowledged. A rejected key arrives in-band as an <c>Error</c> frame.
/// </summary>
/// <param name="apiKeyProvider">Returns the saved API key, or null when none is saved.</param>
/// <param name="connect">Opens the socket; production passes <see cref="ClientWebSocketSpeechSocket.ConnectAsync"/>.</param>
/// <param name="receiveTimeout">How long a single receive may wait before the exchange fails as timed out. Defaults to 10 seconds.</param>
public sealed class AssemblyAISpeechSynthesizer(
    Func<string?> apiKeyProvider,
    SpeechSocketConnector connect,
    TimeSpan? receiveTimeout = null)
{
    /// <summary>The sample rate of the PCM the service streams.</summary>
    public const int SampleRate = 24000;

    /// <summary>The voice every request asks for.</summary>
    public const string Voice = "jane";

    /// <summary>The streaming TTS endpoint, without query parameters.</summary>
    public static readonly Uri Endpoint = new("wss://streaming-tts.assemblyai.com/v1/ws/");

    private static readonly JsonSerializerOptions s_commandOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly string s_flush = Command("Flush");
    private static readonly string s_terminate = Command("Terminate");

    private readonly Func<string?> _apiKeyProvider = apiKeyProvider ?? throw new ArgumentNullException(nameof(apiKeyProvider));
    private readonly SpeechSocketConnector _connect = connect ?? throw new ArgumentNullException(nameof(connect));
    private readonly TimeSpan _receiveTimeout = receiveTimeout ?? TimeSpan.FromSeconds(10);

    /// <summary>The URL the synthesizer connects to: <see cref="Endpoint"/> with the voice and sample rate as query parameters.</summary>
    public static Uri Url { get; } = new($"{Endpoint.AbsoluteUri}?voice={Voice}&sample_rate={SampleRate}");

    /// <summary>
    /// PCM chunks for <paramref name="text"/> in playback order. Blank text yields nothing and opens
    /// no socket. The sequence ends once the service has acknowledged every segment, after which
    /// the socket is closed normally. Every failure, and cancelling <paramref name="ct"/> or
    /// abandoning the enumeration, closes the socket abruptly.
    /// </summary>
    /// <exception cref="BlurtException">No API key is saved (<see cref="BlurtError.ApiKeyMissing"/>), thrown before any socket opens.</exception>
    /// <exception cref="SpeechSynthesisException">The service reported an error, or went silent past the receive timeout.</exception>
    /// <exception cref="WebSocketException">The socket closed or failed before every segment was acknowledged.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    public async IAsyncEnumerable<byte[]> Synthesize(string text, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        var segments = SpeechSegmenter.Segments(text);
        if (segments.Count == 0)
        {
            yield break;
        }

        var apiKey = _apiKeyProvider();
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new BlurtException(new BlurtError.ApiKeyMissing());
        }

        var socket = await _connect(Url, apiKey, ct);
        using var registration = ct.Register(() => socket.Close(normally: false));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var chunks = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        var exchange = RunAsync(socket, segments, chunks.Writer, stop.Token);
        var finished = false;
        try
        {
            await foreach (var chunk in chunks.Reader.ReadAllAsync(ct))
            {
                yield return chunk;
            }

            finished = true;
        }
        finally
        {
            if (!finished)
            {
                socket.Close(normally: false);
                await stop.CancelAsync();
            }

            await exchange;
        }
    }

    /// <summary>The JSON for one client command: <c>text</c> (omitted when null) before <c>type</c>.</summary>
    public static string Command(string type, string? text = null) =>
        JsonSerializer.Serialize(new WireCommand(text, type), s_commandOptions);

    /// <summary>
    /// Decodes one server frame leniently: anything that is not a JSON object with a string
    /// <c>type</c>, an <c>Audio</c> frame without decodable non-empty audio, and every unknown type
    /// are <see cref="SpeechFrame.Ignored"/>. An <c>Error</c> frame's <c>error_code</c> may be a
    /// number or a string; a blank reason reads <c>unknown error</c>.
    /// </summary>
    public static SpeechFrame Frame(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String)
            {
                return new SpeechFrame.Ignored();
            }

            return type.GetString() switch
            {
                "Audio" => AudioFrame(root),
                "FlushDone" => new SpeechFrame.FlushDone(),
                "Error" => ErrorFrame(root),
                _ => new SpeechFrame.Ignored(),
            };
        }
        catch (JsonException)
        {
            return new SpeechFrame.Ignored();
        }
    }

    private static SpeechFrame AudioFrame(JsonElement root)
    {
        if (!root.TryGetProperty("audio", out var audio) || audio.ValueKind != JsonValueKind.String)
        {
            return new SpeechFrame.Ignored();
        }

        try
        {
            var pcm = Convert.FromBase64String(audio.GetString() ?? string.Empty);
            return pcm.Length == 0 ? new SpeechFrame.Ignored() : new SpeechFrame.Audio(pcm);
        }
        catch (FormatException)
        {
            return new SpeechFrame.Ignored();
        }
    }

    private static SpeechFrame.Error ErrorFrame(JsonElement root)
    {
        var reason = (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String
            ? error.GetString().TrimmedNonEmpty()
            : null) ?? "unknown error";
        if (!root.TryGetProperty("error_code", out var code) || code.ValueKind == JsonValueKind.Null)
        {
            return new SpeechFrame.Error(reason);
        }

        var codeText = code.ValueKind == JsonValueKind.String ? code.GetString() : code.GetRawText();
        return new SpeechFrame.Error($"({codeText}) {reason}");
    }

    /// <summary>
    /// Runs the exchange, then sends <c>Terminate</c> and closes normally. Never throws: chunks go
    /// to <paramref name="chunks"/>, which completes with the failure, if any, after the socket is
    /// closed.
    /// </summary>
    private async Task RunAsync(ISpeechSocket socket, IReadOnlyList<string> segments, ChannelWriter<byte[]> chunks, CancellationToken ct)
    {
        try
        {
            await ExchangeAsync(socket, segments, chunks, ct);
            try
            {
                await socket.SendAsync(s_terminate, ct);
            }
            catch (Exception)
            {
                // Every segment is already acknowledged; a failed Terminate leaves nothing unspoken.
            }

            socket.Close(normally: true);
            chunks.TryComplete();
        }
        catch (Exception exception)
        {
            socket.Close(normally: false);
            chunks.TryComplete(exception);
        }
    }

    private async Task ExchangeAsync(ISpeechSocket socket, IReadOnlyList<string> segments, ChannelWriter<byte[]> chunks, CancellationToken ct)
    {
        using var senderStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var sender = SendAllAsync(socket, segments, senderStop.Token);
        try
        {
            var acknowledged = 0;
            while (acknowledged < segments.Count)
            {
                string? message;
                try
                {
                    message = await socket.ReceiveAsync(ct).WaitAsync(_receiveTimeout, ct);
                }
                catch (TimeoutException)
                {
                    socket.Close(normally: false);
                    throw new SpeechSynthesisException(new SpeechSynthesisFailure.TimedOut());
                }

                if (message is null)
                {
                    throw new WebSocketException("The socket closed before every segment was acknowledged.");
                }

                switch (Frame(message))
                {
                    case SpeechFrame.Audio audio:
                        chunks.TryWrite(audio.Pcm);
                        break;
                    case SpeechFrame.FlushDone:
                        acknowledged++;
                        break;
                    case SpeechFrame.Error error:
                        socket.Close(normally: false);
                        throw new SpeechSynthesisException(new SpeechSynthesisFailure.Server(error.Message));
                }
            }
        }
        catch (Exception)
        {
            // A pending send fails only once the socket is closed, so close before awaiting the sender.
            socket.Close(normally: false);
            await senderStop.CancelAsync();
            await ObserveAsync(sender);
            throw;
        }

        await senderStop.CancelAsync();
        await ObserveAsync(sender);
    }

    /// <summary>Sends <c>Generate</c> then <c>Flush</c> per segment, stopping before the next segment once <paramref name="ct"/> is cancelled.</summary>
    private static async Task SendAllAsync(ISpeechSocket socket, IReadOnlyList<string> segments, CancellationToken ct)
    {
        foreach (var segment in segments)
        {
            ct.ThrowIfCancellationRequested();
            await socket.SendAsync(Command("Generate", segment), ct);
            await socket.SendAsync(s_flush, ct);
        }
    }

    /// <summary>Awaits the sender, discarding its failure: the receive loop reports how the exchange ended.</summary>
    private static async Task ObserveAsync(Task sender)
    {
        try
        {
            await sender;
        }
        catch (Exception)
        {
            // A rejected key or dropped socket is reported by the receive loop, not the sender.
        }
    }

    private sealed record WireCommand(
        [property: JsonPropertyName("text")][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text,
        [property: JsonPropertyName("type")] string Type);
}
