using System.Text.Json.Nodes;
using System.Threading.Channels;
using Blurt.Engine.Audio;
using Blurt.Engine.Injection;
using Blurt.Engine.Pipeline;
using Blurt.Engine.STT;
using Microsoft.Extensions.Time.Testing;

namespace Blurt.Engine.Tests.Stubs;

/// <summary>Canned capture feeds.</summary>
public static class StubFeeds
{
    /// <summary>Returns a feed that publishes <paramref name="pcm"/> once and completes.</summary>
    public static IAsyncEnumerable<byte[]> OneShot(byte[] pcm)
    {
        var feed = Channel.CreateUnbounded<byte[]>();
        feed.Writer.TryWrite(pcm);
        feed.Writer.TryComplete();
        return feed.Reader.ReadAllAsync();
    }
}

/// <summary>
/// A <see cref="TimeProvider"/> over <see cref="FakeTimeProvider"/> that also tracks the timers it
/// hands out, so a test can wait until a sleeper with a given due time is armed before advancing,
/// and can tell whether a timer was disarmed.
/// </summary>
public sealed class TestClock : TimeProvider
{
    private readonly FakeTimeProvider _fake = new(new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero));
    private readonly Lock _gate = new();
    private readonly List<TrackedTimer> _timers = [];
    private TimeSpan? _heldDueTime;
    private ManualResetEventSlim? _heldUntil;
    private int _heldCreations;

    /// <summary>How many <see cref="CreateTimer"/> calls have blocked on <see cref="HoldTimerCreation"/>.</summary>
    public int HeldCreations => Volatile.Read(ref _heldCreations);

    /// <inheritdoc/>
    public override TimeZoneInfo LocalTimeZone => _fake.LocalTimeZone;

    /// <inheritdoc/>
    public override long TimestampFrequency => _fake.TimestampFrequency;

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => _fake.GetUtcNow();

    /// <inheritdoc/>
    public override long GetTimestamp() => _fake.GetTimestamp();

    /// <inheritdoc/>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (_heldUntil is { } heldUntil && dueTime == _heldDueTime)
        {
            Interlocked.Increment(ref _heldCreations);
            heldUntil.Wait();
        }

        var tracked = new TrackedTimer(dueTime);
        lock (_gate)
        {
            _timers.Add(tracked);
        }

        tracked.Inner = _fake.CreateTimer(
            s =>
            {
                tracked.MarkFired();
                callback(s);
            },
            state,
            dueTime,
            period);
        return tracked;
    }

    /// <summary>
    /// Makes every later <see cref="CreateTimer"/> call for <paramref name="dueTime"/> block its thread
    /// until <paramref name="until"/> is set, holding whatever arms that timer at that point.
    /// </summary>
    public void HoldTimerCreation(TimeSpan dueTime, ManualResetEventSlim until)
    {
        _heldDueTime = dueTime;
        _heldUntil = until;
    }

    /// <summary>Whether a timer created with <paramref name="dueTime"/> is armed: neither fired nor disposed.</summary>
    public bool HasArmedTimer(TimeSpan dueTime)
    {
        lock (_gate)
        {
            return _timers.Any(t => t.DueTime == dueTime && t.IsArmed);
        }
    }

    /// <summary>Waits until a timer with <paramref name="dueTime"/> is armed; throws <see cref="TimeoutException"/> after the wait budget.</summary>
    public Task WaitUntilSleepingAsync(TimeSpan dueTime) =>
        SessionWaits.WaitUntilAsync(() => HasArmedTimer(dueTime), $"a {dueTime} sleeper");

    /// <summary>Moves virtual time forward, firing every timer whose due time has passed.</summary>
    public void Advance(TimeSpan by) => _fake.Advance(by);

    private sealed class TrackedTimer(TimeSpan dueTime) : ITimer
    {
        private int _fired;
        private int _disposed;

        public TimeSpan DueTime { get; } = dueTime;

        public ITimer? Inner { get; set; }

        public bool IsArmed => Volatile.Read(ref _fired) == 0 && Volatile.Read(ref _disposed) == 0;

        public void MarkFired() => Interlocked.Exchange(ref _fired, 1);

        public bool Change(TimeSpan dueTime, TimeSpan period) => Inner?.Change(dueTime, period) ?? false;

        public void Dispose()
        {
            Interlocked.Exchange(ref _disposed, 1);
            Inner?.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// A real <see cref="DictationLog"/> writing to a unique temp directory with developer mode on, so a
/// test can read back exactly what the session logged without touching the user's real logs.
/// </summary>
public sealed class TestLog : IDisposable
{
    /// <summary>Creates the log; <paramref name="isEnabled"/> defaults to always on.</summary>
    public TestLog(TimeProvider? clock = null, Func<bool>? isEnabled = null)
    {
        Directory = Path.Combine(Path.GetTempPath(), $"BlurtDictationLogTests-{Guid.NewGuid():N}");
        Log = new DictationLog(isEnabled ?? (() => true), Directory, RenderPrompt, FitKeyterms, clock);
    }

    /// <summary>The directory the log writes into. Not created until the first write.</summary>
    public string Directory { get; }

    /// <summary>The log under test.</summary>
    public DictationLog Log { get; }

    /// <summary>Stand-in prompt renderer: the history oldest first, then the trimmed prior text, newline-joined.</summary>
    public static string RenderPrompt(TranscriptionContext? context)
    {
        if (context is null)
        {
            return "";
        }

        var lines = context.RecentTranscripts.Append(context.PriorText.TrimmedNonEmpty()).OfType<string>();
        return string.Join("\n", lines);
    }

    /// <summary>Stand-in key-term fitter: drops blank terms.</summary>
    public static IReadOnlyList<string> FitKeyterms(IReadOnlyList<string> terms) =>
        terms.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();

    /// <summary>Reads every JSONL line of <paramref name="path"/>, or none when the file is absent.</summary>
    public static IReadOnlyList<JsonObject> ReadLines(string path) =>
        File.Exists(path)
            ? File.ReadAllLines(path).Where(l => l.Length > 0).Select(l => JsonNode.Parse(l)!.AsObject()).ToList()
            : [];

    /// <summary>Waits for pending writes, then returns every transcript entry.</summary>
    public async Task<IReadOnlyList<JsonObject>> TranscriptsAsync()
    {
        await Log.DrainAsync();
        return ReadLines(Log.DictationsPath);
    }

    /// <summary>Waits for pending writes, then returns every error entry.</summary>
    public async Task<IReadOnlyList<JsonObject>> FailuresAsync()
    {
        await Log.DrainAsync();
        return ReadLines(Log.ErrorsPath);
    }

    /// <summary>Waits for pending writes, then returns the <c>kind</c> of every error entry.</summary>
    public async Task<IReadOnlyList<string>> FailureNamesAsync() =>
        (await FailuresAsync()).Select(e => (string)e["kind"]!).ToList();

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>One <c>onTranscriptDelivered</c> call.</summary>
public sealed record Delivery(string Text, RecentDictations Recent);

/// <summary>
/// A <see cref="DictationSession"/> with every collaborator stubbed: the mic, transcriber, and
/// injector (stub classes by default), the focus capture, the key terms, and a temp-directory log.
/// Records every <c>onTranscriptDelivered</c> call.
/// </summary>
public sealed class SessionHarness : IAsyncDisposable
{
    private readonly Lock _gate = new();
    private readonly List<Delivery> _deliveries = [];

    /// <summary>Builds the session; every argument left null gets a default stub.</summary>
    public SessionHarness(
        IMicCapture? mic = null,
        ITranscriber? transcriber = null,
        IInjector? injector = null,
        StubFocusCapture? focus = null,
        TimeProvider? clock = null,
        double maxRecordingSeconds = SyncSTTLimits.AutoReleaseSeconds,
        IReadOnlyList<string>? keyTerms = null,
        Func<IReadOnlyList<string>>? keyTermsProvider = null,
        Func<string?>? styleNameProvider = null,
        Func<BlurtError?>? readinessCheck = null,
        Func<bool>? logEnabled = null)
    {
        MicCapture = mic ?? new StubMicCapture();
        TranscriberSeam = transcriber ?? new StubTranscriber { TranscriptToReturn = "Hello world." };
        InjectorSeam = injector ?? new StubInjector();
        Focus = focus ?? new StubFocusCapture();
        Log = new TestLog(clock, logEnabled);
        IReadOnlyList<string> terms = keyTerms ?? [];
        Session = new DictationSession(
            MicCapture,
            TranscriberSeam,
            InjectorSeam,
            Focus,
            Log.Log,
            clock,
            maxRecordingSeconds,
            keyTermsProvider ?? (() => terms),
            styleNameProvider,
            readinessCheck,
            (text, recent) =>
            {
                lock (_gate)
                {
                    _deliveries.Add(new Delivery(text, recent));
                }
            });
    }

    /// <summary>The session under test.</summary>
    public DictationSession Session { get; }

    /// <summary>The mic seam as passed.</summary>
    public IMicCapture MicCapture { get; }

    /// <summary>The transcriber seam as passed.</summary>
    public ITranscriber TranscriberSeam { get; }

    /// <summary>The injector seam as passed.</summary>
    public IInjector InjectorSeam { get; }

    /// <summary>The default mic stub; throws when a different mic was passed.</summary>
    public StubMicCapture Mic => (StubMicCapture)MicCapture;

    /// <summary>The default transcriber stub; throws when a different transcriber was passed.</summary>
    public StubTranscriber Transcriber => (StubTranscriber)TranscriberSeam;

    /// <summary>The default injector stub; throws when a different injector was passed.</summary>
    public StubInjector Injector => (StubInjector)InjectorSeam;

    /// <summary>The focus capture stub.</summary>
    public StubFocusCapture Focus { get; }

    /// <summary>The session's developer-mode log.</summary>
    public TestLog Log { get; }

    /// <summary>Every <c>onTranscriptDelivered</c> call, in order.</summary>
    public IReadOnlyList<Delivery> Deliveries
    {
        get
        {
            lock (_gate)
            {
                return [.. _deliveries];
            }
        }
    }

    /// <summary>Builds a harness whose transcriber returns <paramref name="transcript"/>.</summary>
    public static SessionHarness WithTranscript(
        string transcript, StubFocusCapture? focus = null, IReadOnlyList<string>? keyTerms = null) =>
        new(transcriber: new StubTranscriber { TranscriptToReturn = transcript }, focus: focus, keyTerms: keyTerms);

    /// <summary>Builds a harness whose transcriber throws <paramref name="error"/>.</summary>
    public static SessionHarness WithTranscriberError(Exception error, StubFocusCapture? focus = null) =>
        new(transcriber: new StubTranscriber { ErrorToThrow = error }, focus: focus);

    /// <summary>Runs one press, release, and wait for the terminal phase, returning that phase.</summary>
    public async Task<PipelinePhase> DictateAsync()
    {
        await Session.PressAsync();
        await Session.ReleaseAsync();
        return await Session.WaitForTerminalAsync();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        Focus.FieldContextGate?.Set();
        await Session.DisposeAsync();
        await Log.Log.DrainAsync();
        Log.Dispose();
    }
}

/// <summary>Bounded waits for session tests, so a hang fails as a timeout instead of stalling the run.</summary>
public static class SessionWaits
{
    /// <summary>The longest any single wait in these suites may take.</summary>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    /// <summary>Waits until the session reaches a terminal phase and returns it; the current phase counts.</summary>
    public static Task<PipelinePhase> WaitForTerminalAsync(this DictationSession session) =>
        session.WaitForPhaseAsync(p => p.IsTerminal);

    /// <summary>Waits until the session's phase equals <paramref name="phase"/>; the current phase counts.</summary>
    public static Task<PipelinePhase> WaitForPhaseAsync(this DictationSession session, PipelinePhase phase) =>
        session.WaitForPhaseAsync(p => p == phase);

    /// <summary>Waits until the session's phase satisfies <paramref name="match"/> and returns it; the current phase counts.</summary>
    public static async Task<PipelinePhase> WaitForPhaseAsync(this DictationSession session, Func<PipelinePhase, bool> match)
    {
        var seen = await session.PhaseStream(CancellationToken.None).CollectUntilAsync(match);
        return seen[^1];
    }

    /// <summary>
    /// Reads <paramref name="stream"/> until an element satisfies <paramref name="stop"/> and returns
    /// every element read, that one included. Throws <see cref="TimeoutException"/> after the budget.
    /// </summary>
    public static async Task<IReadOnlyList<PipelinePhase>> CollectUntilAsync(
        this IAsyncEnumerable<PipelinePhase> stream, Func<PipelinePhase, bool> stop)
    {
        using var cts = new CancellationTokenSource(Budget);
        var seen = new List<PipelinePhase>();
        try
        {
            await foreach (var phase in stream.WithCancellation(cts.Token))
            {
                seen.Add(phase);
                if (stop(phase))
                {
                    return seen;
                }
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            throw new TimeoutException($"No matching phase within {Budget}; saw [{string.Join(", ", seen)}].");
        }

        throw new InvalidOperationException($"The phase stream ended; saw [{string.Join(", ", seen)}].");
    }

    /// <summary>Polls <paramref name="condition"/> until it holds; throws <see cref="TimeoutException"/> naming <paramref name="what"/> after the budget.</summary>
    public static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + Budget;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for {what}.");
            }

            await Task.Delay(1);
        }
    }
}
