using Blurt.Engine.Config;
using Blurt.Engine.Diagnostics;
using Blurt.Engine.FocusCapture;

namespace Blurt.Engine.TTS;

/// <summary>
/// Reads text aloud with the streaming TTS: the engine half of read-selection-aloud, with the host
/// deciding when to call it. <see cref="SpeakAsync"/> runs until the last sample has played. To stop
/// early, cancel the token it was given: playback is silenced at once and the socket closes.
/// </summary>
/// <param name="synthesizer">Streams the speech for the text.</param>
/// <param name="llm">Rewrites the text for listening and answers questions about a selection.</param>
/// <param name="makeSink">Creates the playback sink for one read, handed the style's rate.</param>
public sealed class SelectionSpeaker(AssemblyAISpeechSynthesizer synthesizer, ReadAloudLLM llm, Func<double, IPcmSink> makeSink)
{
    /// <summary>The most text one press reads, so a select-all of a long document cannot queue minutes of audio.</summary>
    public const int MaxCharacters = 4000;

    private const string LogCategory = "SelectionSpeech";

    private readonly AssemblyAISpeechSynthesizer _synthesizer = synthesizer ?? throw new ArgumentNullException(nameof(synthesizer));
    private readonly ReadAloudLLM _llm = llm ?? throw new ArgumentNullException(nameof(llm));
    private readonly Func<double, IPcmSink> _makeSink = makeSink ?? throw new ArgumentNullException(nameof(makeSink));

    /// <summary>
    /// The text <paramref name="read"/> found: the accessibility text for <see cref="SelectionRead.Text"/>,
    /// null for <see cref="SelectionRead.None"/> without touching <paramref name="copy"/>, and the
    /// copied selection for <see cref="SelectionRead.Unreadable"/>.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled during the copy.</exception>
    public static Task<string?> ResolveAsync(SelectionRead read, ISelectionCopy copy, CancellationToken ct) =>
        read switch
        {
            SelectionRead.Text text => Task.FromResult<string?>(text.Value),
            _ when ReferenceEquals(read, SelectionRead.Unreadable) => copy.CopySelectionAsync(MaxCharacters, ct),
            _ => Task.FromResult<string?>(null),
        };

    /// <summary>
    /// The selected text in the focused app, or null when there is none, the field is secure, or
    /// nothing can be read. The accessibility read runs on its own thread because it can block for
    /// the platform's timeout against a hung app; the copy fallback runs only when no focused
    /// element could be reached.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    public static async Task<string?> FocusedSelectionAsync(ISelectionReader reader, ISelectionCopy copy, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var read = await Task.Factory
            .StartNew(() => reader.CaptureSelectedText(MaxCharacters), ct, TaskCreationOptions.LongRunning, TaskScheduler.Default)
            .ConfigureAwait(false);
        return await ResolveAsync(read, copy, ct).ConfigureAwait(false);
    }

    /// <summary>Reads <paramref name="text"/> in <paramref name="style"/>: rewritten for listening first when it skips jargon, then played at its rate.</summary>
    /// <exception cref="BlurtException">No API key is saved (<see cref="BlurtError.ApiKeyMissing"/>). No socket opens.</exception>
    /// <exception cref="SpeechSynthesisException">The speech service reported an error or went silent. Playback is stopped.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled. Nothing plays after the cancellation.</exception>
    public async Task SpeakAsync(string text, ReadAloudStyle style, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(style);
        var spoken = style.SkipsJargon ? await ForListeningAsync(text, ct).ConfigureAwait(false) : text;

        // A stop that landed while the rewrite was on its way back: nothing plays, no socket opens.
        ct.ThrowIfCancellationRequested();
        var sink = _makeSink(style.Rate);
        using var stopOnCancel = ct.Register(sink.Stop);
        try
        {
            await foreach (var chunk in _synthesizer.Synthesize(spoken, ct).ConfigureAwait(false))
            {
                sink.Enqueue(chunk);
            }
        }
        catch (Exception)
        {
            sink.Stop();
            throw;
        }

        await sink.DrainAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Answers <paramref name="request"/> about <paramref name="selection"/> and reads the answer at <paramref name="style"/>'s rate; the answer is written to be heard, so it skips the rewrite. No fallback.</summary>
    /// <exception cref="BlurtException">No API key is saved (<see cref="BlurtError.ApiKeyMissing"/>).</exception>
    /// <exception cref="ReadAloudLLMException">The gateway failed to answer. Nothing is spoken and no socket opens.</exception>
    /// <exception cref="SpeechSynthesisException">The speech service reported an error or went silent.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    public async Task AnswerAsync(string request, string selection, ReadAloudStyle style, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(style);
        var reply = await _llm.AnswerAsync(request, selection, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        await SpeakAsync(reply, new ReadAloudStyle(style.Rate, SkipsJargon: false), ct).ConfigureAwait(false);
    }

    /// <summary>The rewrite, or <paramref name="text"/> itself when the rewrite fails; a cancellation is the one failure that does not fall back.</summary>
    private async Task<string> ForListeningAsync(string text, CancellationToken ct)
    {
        try
        {
            return await _llm.RewriteForListeningAsync(text, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ct.ThrowIfCancellationRequested();
            BlurtLog.Error(LogCategory, $"read-aloud rewrite failed, reading verbatim: {ex.GetType().Name}: {ex.Message}");
            return text;
        }
    }
}
