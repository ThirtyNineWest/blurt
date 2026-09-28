using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Blurt.Engine.Audio;
using Blurt.Engine.Diagnostics;
using Blurt.Engine.FocusCapture;
using Blurt.Engine.Injection;
using Blurt.Engine.STT;

namespace Blurt.Engine.Pipeline;

/// <summary>
/// One dictation pipeline: press → <see cref="PipelinePhase.Connecting"/> → mic live →
/// <see cref="PipelinePhase.Recording"/> (the request opens and audio streams into it) → release →
/// <see cref="PipelinePhase.Transcribing"/> → <see cref="PipelinePhase.Injecting"/> →
/// <see cref="PipelinePhase.Pasted"/> or <see cref="PipelinePhase.NoTarget"/>, with
/// <see cref="PipelinePhase.Cancelled"/> or a failure possible from any stage.
/// </summary>
/// <remarks>
/// Press, release, and the two cancels run one at a time in arrival order on a serial queue.
/// Session state is guarded by one lock, and every phase change goes through one funnel that
/// publishes it in order, abandons the upload on a terminal phase, and logs a failure. A cancel acts at once
/// on a bring-up, a transcribe, or a paste in flight, and deterministically wins over a release
/// racing the mic stop.
/// </remarks>
public sealed partial class DictationSession : IAsyncDisposable
{
    /// <summary>The longest the upload waits for the press-time field-context read before opening the request without it.</summary>
    public static readonly TimeSpan ContextWaitBudget = TimeSpan.FromMilliseconds(500);

    private const string LogCategory = "DictationSession";

    private readonly IMicCapture _mic;
    private readonly ITranscriber _transcriber;
    private readonly IInjector _injector;
    private readonly IFocusCapture _focus;
    private readonly DictationLog _log;
    private readonly TimeProvider _clock;
    private readonly double _maxRecordingSeconds;
    private readonly Func<IReadOnlyList<string>> _keyTermsProvider;
    private readonly Func<string?> _styleNameProvider;
    private readonly Func<BlurtError?> _readinessCheck;
    private readonly Action<string, RecentDictations>? _onTranscriptDelivered;
    private readonly Channel<Command> _commands = Channel.CreateUnbounded<Command>(new UnboundedChannelOptions { SingleReader = true });
    private readonly BlockingCollection<PipelinePhase> _phaseEvents = [];

    // Every field below is guarded by _gate.
    private readonly Lock _gate = new();
    private readonly List<Channel<PipelinePhase>> _subscribers = [];
    private readonly RecentDictations _recent = new();
    private PipelinePhase _phase = PipelinePhase.Idle;
    private Task _queueTail = Task.CompletedTask;
    private bool _cancelRequested;
    private CancellationTokenSource? _inFlightPressCts;
    private CancellationTokenSource? _autoReleaseCts;
    private PressContext? _pressContext;
    private Task<string>? _upload;
    private CancellationTokenSource? _uploadCts;
    private PipelineRun? _pipelineRun;
    private Task _pipeline = Task.CompletedTask;
    private DeliveredTranscript? _delivered;
    private bool _disposed;

    /// <summary>
    /// Creates a session over the given seams. <paramref name="clock"/> drives the auto-release timer,
    /// the context-wait budget, and history timestamps (system clock by default). The providers are
    /// read afresh at each press: <paramref name="keyTermsProvider"/> for the request's key terms,
    /// <paramref name="styleNameProvider"/> for the custom style a transcript was made with,
    /// <paramref name="readinessCheck"/> for a blocker that refuses the press before capture.
    /// <paramref name="onTranscriptDelivered"/> receives each non-empty trimmed transcript with a
    /// snapshot of the history it joined, before the paste.
    /// </summary>
    public DictationSession(
        IMicCapture mic,
        ITranscriber transcriber,
        IInjector injector,
        IFocusCapture focus,
        DictationLog log,
        TimeProvider? clock = null,
        double maxRecordingSeconds = SyncSTTLimits.AutoReleaseSeconds,
        Func<IReadOnlyList<string>>? keyTermsProvider = null,
        Func<string?>? styleNameProvider = null,
        Func<BlurtError?>? readinessCheck = null,
        Action<string, RecentDictations>? onTranscriptDelivered = null)
    {
        ArgumentNullException.ThrowIfNull(mic);
        ArgumentNullException.ThrowIfNull(transcriber);
        ArgumentNullException.ThrowIfNull(injector);
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(log);
        _mic = mic;
        _transcriber = transcriber;
        _injector = injector;
        _focus = focus;
        _log = log;
        _clock = clock ?? TimeProvider.System;
        _maxRecordingSeconds = maxRecordingSeconds;
        _keyTermsProvider = keyTermsProvider ?? (static () => []);
        _styleNameProvider = styleNameProvider ?? (static () => null);
        _readinessCheck = readinessCheck ?? (static () => null);
        _onTranscriptDelivered = onTranscriptDelivered;
        _ = Task.Run(ConsumeCommandsAsync);
        _ = Task.Factory.StartNew(DispatchPhaseEvents, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    /// <summary>
    /// Raised after every phase change, in the order the phase took them, from one dispatcher thread;
    /// never on the caller's thread. Changes made before the session is disposed are still raised.
    /// </summary>
    public event Action<PipelinePhase>? PhaseChanged;

    /// <summary>The current phase.</summary>
    public PipelinePhase Phase
    {
        get
        {
            lock (_gate)
            {
                return _phase;
            }
        }
    }

    /// <summary>
    /// The latest press's field-context read once it has landed, or null before then (or when it was
    /// empty). The paste separator and the remember-this-dictation decision read this value.
    /// </summary>
    public TranscriptionContext? CapturedContext
    {
        get
        {
            lock (_gate)
            {
                return _pressContext?.Resolved;
            }
        }
    }

    /// <summary>
    /// Subscribes immediately and returns the current phase followed by every later change, in order.
    /// Any number of subscriptions may be live. The sequence ends when <paramref name="ct"/> cancels
    /// or the session is disposed.
    /// </summary>
    public IAsyncEnumerable<PipelinePhase> PhaseStream(CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<PipelinePhase>(new UnboundedChannelOptions { SingleReader = true });
        lock (_gate)
        {
            channel.Writer.TryWrite(_phase);
            if (_disposed)
            {
                channel.Writer.TryComplete();
            }
            else
            {
                _subscribers.Add(channel);
            }
        }

        var registration = ct.Register(() => Unsubscribe(channel));
        return ReadPhasesAsync(channel, registration, ct);
    }

    /// <summary>
    /// Completes when the transcribe-and-inject work spawned by the most recent release has finished,
    /// including after a cancel tore it down; completes at once when none was spawned. Never faults.
    /// </summary>
    public Task AwaitPipelineAsync()
    {
        lock (_gate)
        {
            return _pipeline;
        }
    }

    /// <summary>
    /// Stops accepting commands, cancels any in-flight press, upload, auto-release timer, and pipeline,
    /// and ends every phase stream. The microphone is left to its owner.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        CancellationTokenSource?[] toCancel;
        Channel<PipelinePhase>[] subscribers;
        lock (_gate)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            _phaseEvents.CompleteAdding();
            toCancel = [_uploadCts, _pipelineRun?.Cts, _inFlightPressCts, _autoReleaseCts];
            _uploadCts = null;
            ObserveFaults(_upload);
            _upload = null;
            _pipelineRun = null;
            _autoReleaseCts = null;
            subscribers = [.. _subscribers];
            _subscribers.Clear();
        }

        _commands.Writer.TryComplete();
        foreach (var cts in toCancel)
        {
            CancelQuietly(cts);
        }

        foreach (var subscriber in subscribers)
        {
            subscriber.Writer.TryComplete();
        }

        return ValueTask.CompletedTask;
    }

    private static void CancelQuietly(CancellationTokenSource? cts)
    {
        try
        {
            cts?.Cancel();
        }
        catch (AggregateException ex)
        {
            BlurtLog.Error(LogCategory, $"a cancellation callback failed: {ex.InnerException?.GetType().Name}");
        }
    }

    /// <summary>Marks a detached upload's fault as observed, so abandoning it never raises <see cref="TaskScheduler.UnobservedTaskException"/>.</summary>
    private static void ObserveFaults(Task? task) =>
        task?.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    // Starts op once every previously chained op has finished. The forced yield keeps op off the
    // caller's thread, so chaining under _gate never runs op while the lock is held.
    private static async Task RunAfterAsync(Task previous, Func<Task> op)
    {
        await previous.ConfigureAwait(ConfigureAwaitOptions.ForceYielding | ConfigureAwaitOptions.SuppressThrowing);
        try
        {
            await op();
        }
        catch (Exception ex)
        {
            BlurtLog.Error(LogCategory, $"command failed: {ex.GetType().Name}");
        }
    }

    private Task Enqueue(Func<Task> op)
    {
        lock (_gate)
        {
            return ChainLocked(op);
        }
    }

    private Task ChainLocked(Func<Task> op)
    {
        var task = RunAfterAsync(_queueTail, op);
        _queueTail = task;
        return task;
    }

    /// <summary>Sets the phase unconditionally and publishes it.</summary>
    private void SetPhase(PipelinePhase next)
    {
        PhaseChange change;
        lock (_gate)
        {
            change = AssignLocked(next);
        }

        Publish(change);
    }

    /// <summary>
    /// The only writer of <see cref="_phase"/>. Queues a failure to the error log before the phase is
    /// visible to any observer, so one that sees it and then drains the log finds the entry. Queues
    /// the phase to every stream and to the <see cref="PhaseChanged"/> dispatcher, both in assignment
    /// order, and on a terminal phase detaches the in-flight upload for <see cref="Publish"/> to
    /// cancel. The caller must hold <see cref="_gate"/> and then call <see cref="Publish"/> after
    /// releasing it.
    /// </summary>
    private PhaseChange AssignLocked(PipelinePhase next)
    {
        _phase = next;
        if (next is PipelinePhase.FailedPhase failed)
        {
            _log.AppendError(failed.Error, _pressContext?.Resolved);
        }

        foreach (var subscriber in _subscribers)
        {
            subscriber.Writer.TryWrite(next);
        }

        if (!_disposed)
        {
            _phaseEvents.Add(next);
        }

        CancellationTokenSource? upload = null;
        string? outcome = null;
        if (next.IsTerminal)
        {
            upload = _uploadCts;
            _uploadCts = null;
            ObserveFaults(_upload);
            _upload = null;
            if (_delivered is { } delivered)
            {
                outcome = OutcomeLine(next, delivered);
                _delivered = null;
            }
        }

        return new PhaseChange(next, upload, outcome);
    }

    /// <summary>
    /// The outside-the-lock half of a phase change: abandon the detached upload and log the finished
    /// dictation's outcome line.
    /// </summary>
    private void Publish(PhaseChange change)
    {
        CancelQuietly(change.UploadToCancel);
        if (change.OutcomeLine is { } line)
        {
            BlurtLog.Info(LogCategory, line);
        }
    }

    /// <summary>
    /// Returns the one-line outcome of a dictation whose transcript was delivered, e.g.
    /// <c>dictation ended: pasted, 212 chars, target=WindowsTerminal</c>. Carries the transcript's
    /// length and the target's process name only, never the transcript, window title, or field text.
    /// </summary>
    private static string OutcomeLine(PipelinePhase terminal, DeliveredTranscript delivered)
    {
        var outcome = terminal switch
        {
            _ when terminal == PipelinePhase.Pasted => "pasted",
            _ when terminal == PipelinePhase.NoTarget => "copied to clipboard",
            _ when terminal == PipelinePhase.Cancelled => "cancelled",
            PipelinePhase.FailedPhase failed => $"failed: {failed.Error.DiagnosticName}",
            _ => terminal.ToString(),
        };
        var target = delivered.AppName.TrimmedNonEmpty() is { } app ? $", target={app}" : "";
        return $"dictation ended: {outcome}, {delivered.Length} chars{target}";
    }

    /// <summary>Raises <see cref="PhaseChanged"/> for each queued phase, in order, until the session is disposed.</summary>
    private void DispatchPhaseEvents()
    {
        foreach (var phase in _phaseEvents.GetConsumingEnumerable())
        {
            try
            {
                PhaseChanged?.Invoke(phase);
            }
            catch (Exception ex)
            {
                BlurtLog.Error(LogCategory, $"a PhaseChanged handler threw {ex.GetType().Name}");
            }
        }
    }

    private async IAsyncEnumerable<PipelinePhase> ReadPhasesAsync(
        Channel<PipelinePhase> channel,
        CancellationTokenRegistration registration,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        try
        {
            await foreach (var phase in channel.Reader.ReadAllAsync(ct))
            {
                yield return phase;
            }
        }
        finally
        {
            await registration.DisposeAsync();
            Unsubscribe(channel);
        }
    }

    private void Unsubscribe(Channel<PipelinePhase> channel)
    {
        lock (_gate)
        {
            _subscribers.Remove(channel);
        }

        channel.Writer.TryComplete();
    }

    private readonly record struct PhaseChange(PipelinePhase Phase, CancellationTokenSource? UploadToCancel, string? OutcomeLine);

    /// <summary>The delivered transcript's length in UTF-16 code units and the target's process name, if known.</summary>
    private readonly record struct DeliveredTranscript(int Length, string? AppName);

    /// <summary>
    /// One spawned transcribe-and-inject run. A cancel revokes it by clearing
    /// <see cref="_pipelineRun"/> under the lock, after which none of its phase changes apply.
    /// </summary>
    private sealed class PipelineRun(CancellationTokenSource cts, Task<string>? upload)
    {
        public CancellationTokenSource Cts { get; } = cts;

        public Task<string>? Upload { get; } = upload;
    }
}
