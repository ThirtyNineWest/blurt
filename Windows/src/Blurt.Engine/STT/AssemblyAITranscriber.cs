using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using Blurt.Engine.Diagnostics;

namespace Blurt.Engine.STT;

/// <summary>
/// <see cref="ITranscriber"/> backed by AssemblyAI's dictation API. One
/// <c>POST /v1/transcribe/live</c> per utterance carries a JSON <c>config</c> part and the raw
/// S16LE PCM as it is captured, and the response carries both the verbatim transcript and a
/// server-side rewrite produced with <see cref="CleanupInstruction"/>. Every request asks for the
/// rewrite; the enhanced-transcripts setting chooses between the two transcripts on the response.
/// Every path, header, part, and key this sends is one the reference documents, plus a
/// <c>User-Agent</c> naming Blurt.
/// </summary>
public sealed partial class AssemblyAITranscriber : ITranscriber
{
    /// <summary>
    /// The bound on a request making no progress. It restarts whenever an audio frame is handed to
    /// the upload and once more when the feed ends, so it bounds a stall rather than a long
    /// recording. 90 s is the client timeout the dictation API documents, generous over the STT
    /// upstream's inference deadline plus the rewrite's budget.
    /// </summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(90);

    private const string LogCategory = "Transcriber";

    /// <summary>
    /// The streaming route, relative to the base URL. It opens its upstream STT call as soon as the
    /// <c>config</c> part arrives, so inference overlaps the recording.
    /// </summary>
    private const string TranscribePath = "v1/transcribe/live";

    private static readonly Uri s_defaultBaseUrl = new("https://dictation.assemblyai.com");
    private static readonly UTF8Encoding s_strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly Func<string?> _apiKeyProvider;
    private readonly IHttpTransport _transport;
    private readonly Uri _baseUrl;
    private readonly Func<bool> _enhancedTranscripts;
    private readonly Func<string?> _customStyle;

    /// <summary>
    /// Creates a transcriber. <paramref name="enhancedTranscripts"/> (default true) and
    /// <paramref name="customStyle"/> (default none) are read at every request, so a settings
    /// change applies to the next dictation. <paramref name="baseUrl"/> defaults to
    /// <c>https://dictation.assemblyai.com</c>.
    /// </summary>
    public AssemblyAITranscriber(
        Func<string?> apiKeyProvider,
        IHttpTransport transport,
        Uri? baseUrl = null,
        Func<bool>? enhancedTranscripts = null,
        Func<string?>? customStyle = null)
    {
        ArgumentNullException.ThrowIfNull(apiKeyProvider);
        ArgumentNullException.ThrowIfNull(transport);
        _apiKeyProvider = apiKeyProvider;
        _transport = transport;
        _baseUrl = baseUrl ?? s_defaultBaseUrl;
        _enhancedTranscripts = enhancedTranscripts ?? (static () => true);
        _customStyle = customStyle ?? (static () => null);
    }

    /// <summary>The no-progress bound applied to each request. <see cref="RequestTimeout"/> unless a test shortens it.</summary>
    internal TimeSpan IdleTimeout { get; set; } = RequestTimeout;

    /// <inheritdoc/>
    /// <exception cref="BlurtException">No API key is configured (<see cref="BlurtError.ApiKeyMissing"/>). Thrown before any request.</exception>
    /// <exception cref="AudioTooShortException">The feed ended below <see cref="SyncSTTLimits.MinPcmBytes"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    /// <exception cref="TimeoutException">The request made no progress for <see cref="RequestTimeout"/>, or the transport timed out.</exception>
    /// <exception cref="AssemblyAIError">The API answered non-2xx, or a 2xx body carried no transcript.</exception>
    /// <exception cref="HttpRequestException">The transport failed.</exception>
    public async Task<string> TranscribeAsync(
        IAsyncEnumerable<byte[]> frames, int sampleRate, TranscriptionContext? context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (_apiKeyProvider().TrimmedNonEmpty() is not { } apiKey)
        {
            throw new BlurtException(new BlurtError.ApiKeyMissing());
        }

        // Encoded before the request opens: the config part leads the body, so an unencodable
        // config must fail here rather than abort a request whose upstream call is already open.
        var config = MakeConfigData(
            sampleRate,
            STTPrompt.Text(context),
            KeytermsBoost.Fitted(context?.KeyTerms ?? []),
            CleanupInstructionFor(_customStyle()));
        var boundary = $"blurt-{Guid.NewGuid():D}";
        var idleTimeout = IdleTimeout;
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
        idle.CancelAfter(idleTimeout);
        var progress = new UploadProgress();
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint(TranscribePath))
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
            Content = new StreamedMultipartContent(config, boundary, RestartingIdle(frames, idle, idleTimeout), progress),
        };

        // The raw key, no scheme: a bare token fails AuthenticationHeaderValue parsing.
        request.Headers.TryAddWithoutValidation("Authorization", apiKey);
        request.Headers.TryAddWithoutValidation(UserAgent.HeaderField, UserAgent.Current);

        var start = Stopwatch.GetTimestamp();
        HttpStatusCode status;
        byte[] body;
        try
        {
            using var response = await _transport.SendAsync(request, idle.Token);
            status = response.StatusCode;
            body = await response.Content.ReadAsByteArrayAsync(idle.Token);
        }
        catch (OperationCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("The dictation request made no progress before its timeout.", e);
        }
        catch (Exception e) when (e is not AudioTooShortException && InnerOfType<AudioTooShortException>(e) is { } tooShort)
        {
            throw Rethrow(tooShort);
        }

        LogRoundTrip(progress, sampleRate, start, Stopwatch.GetTimestamp());
        if ((int)status is < 200 or > 299)
        {
            throw AssemblyAIError.Http((int)status, ErrorMessage(body));
        }

        var decoded = DictationResponse.Decode(body) ?? throw AssemblyAIError.MalformedResponse();
        LogServerMetrics(decoded);
        return Transcript(decoded, _enhancedTranscripts());
    }

    /// <summary>
    /// Returns the UTF-8 JSON <c>config</c> part: the audio geometry (mono, per
    /// <see cref="SyncSTTLimits.ChannelCount"/>), <c>stt_prompt</c> only when
    /// <paramref name="sttPrompt"/> is non-empty, <c>keyterms_prompt</c> only when
    /// <paramref name="keytermsPrompt"/> is non-empty, and <c>llm_instruction</c> only when
    /// <paramref name="llmInstruction"/> is non-null.
    /// </summary>
    internal static byte[] MakeConfigData(int sampleRate, string sttPrompt, IReadOnlyList<string> keytermsPrompt, string? llmInstruction) =>
        new DictationConfig(sampleRate, SyncSTTLimits.ChannelCount, sttPrompt, keytermsPrompt, llmInstruction).Encode();

    /// <summary>
    /// Returns the best human-readable explanation of a non-2xx body: the documented
    /// <c>error</c>, then <c>detail</c>, then the raw UTF-8 body trimmed and capped at 500
    /// characters (so a proxy's HTML error page is still diagnosable). Returns null for an empty,
    /// blank, or non-UTF-8 body.
    /// </summary>
    internal static string? ErrorMessage(byte[] body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (DictationErrorResponse.Message(body) is { } message)
        {
            return message;
        }

        string raw;
        try
        {
            raw = s_strictUtf8.GetString(body);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }

        if (raw.TrimmedNonEmpty() is not { } trimmed)
        {
            return null;
        }

        var info = new StringInfo(trimmed);
        return info.LengthInTextElements <= 500 ? trimmed : info.SubstringByTextElements(0, 500);
    }

    /// <summary>
    /// Returns the transcript to paste. With <paramref name="enhanced"/> off, the verbatim
    /// <c>text</c>. With it on, the trimmed <c>llm_response</c> when non-blank, else the verbatim
    /// <c>text</c>: the rewrite is best-effort, and a blank one would drop the utterance with
    /// nothing pasted.
    /// </summary>
    internal static string Transcript(DictationResponse response, bool enhanced)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (!enhanced)
        {
            return response.Text;
        }

        if (response.LlmResponse.TrimmedNonEmpty() is { } rewrite)
        {
            return rewrite;
        }

        if (response.LlmError is { } error)
        {
            BlurtLog.Warn(LogCategory, $"llm rewrite unavailable ({error}); using verbatim transcript");
        }

        return response.Text;
    }

    private static string? CleanupInstructionFor(string? customStyle)
    {
        if (CleanupInstruction.Sendable(customStyle) is { } instruction)
        {
            return instruction;
        }

        // The failure is silent on the wire: the service's default cleanup replaces the user's
        // style with a 200 and a plausible transcript.
        BlurtLog.Error(
            LogCategory,
            $"cleanup instruction is {Encoding.UTF8.GetByteCount(CleanupInstruction.Text)} UTF-8 bytes, over the {CleanupInstruction.CharacterCap} cap; falling back to the service default");
        return null;
    }

    /// <summary>
    /// Passes <paramref name="frames"/> through, restarting <paramref name="idle"/>'s timer as each
    /// frame is handed to the upload and once more when the feed ends, so the response wait gets a
    /// full window of its own.
    /// </summary>
    private static async IAsyncEnumerable<byte[]> RestartingIdle(
        IAsyncEnumerable<byte[]> frames,
        CancellationTokenSource idle,
        TimeSpan timeout,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var frame in frames.WithCancellation(ct))
        {
            Restart(idle, timeout);
            yield return frame;
        }

        Restart(idle, timeout);
    }

    private static void Restart(CancellationTokenSource idle, TimeSpan timeout)
    {
        try
        {
            idle.CancelAfter(timeout);
        }
        catch (ObjectDisposedException)
        {
            // A transport that returns before the body finishes lets the request end first.
        }
    }

    private static T? InnerOfType<T>(Exception e)
        where T : Exception
    {
        for (var inner = e.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is T match)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>Rethrows <paramref name="e"/> with its original stack trace. Never returns.</summary>
    [DoesNotReturn]
    private static Exception Rethrow(Exception e)
    {
        ExceptionDispatchInfo.Throw(e);
        return e;
    }

    private Uri Endpoint(string path) => new($"{_baseUrl.AbsoluteUri.TrimEnd('/')}/{path}");

    /// <summary>
    /// Logs the client's view of the round trip. <c>wallMs</c> spans the recording too, since the
    /// request opens at press; <c>postSpeechMs</c> is the wait the user feels, from the last frame
    /// handed to the upload until the response arrived.
    /// </summary>
    private static void LogRoundTrip(UploadProgress progress, int sampleRate, long start, long finished)
    {
        var audioMs = SyncSTTLimits.DurationMs(progress.AudioBytes, sampleRate);
        var postSpeechMs = progress.LastFrameTimestamp is { } last ? Stopwatch.GetElapsedTime(last, finished).TotalMilliseconds : -1;
        var wallMs = Stopwatch.GetElapsedTime(start, finished).TotalMilliseconds;
        BlurtLog.Info(
            LogCategory,
            string.Create(CultureInfo.InvariantCulture, $"dictation round-trip audioMs={audioMs} postSpeechMs={postSpeechMs:F0} wallMs={wallMs:F0}"));
    }

    /// <summary>
    /// Logs the server's account of the round trip. <c>session</c> is the id the reference asks
    /// callers to quote when reporting a problem. A missing field logs <c>n/a</c> rather than
    /// skipping the line, so a field the service stops sending is visible.
    /// </summary>
    private static void LogServerMetrics(DictationResponse response)
    {
        static string Ms(double? value) => value?.ToString("F0", CultureInfo.InvariantCulture) ?? "n/a";
        BlurtLog.Info(
            LogCategory,
            $"dictation server session={response.SessionId ?? "n/a"} audioMs={Ms(response.AudioDurationMs)} requestMs={Ms(response.RequestTimeMs)} sttMs={Ms(response.SyncTimeMs)}");
    }
}
