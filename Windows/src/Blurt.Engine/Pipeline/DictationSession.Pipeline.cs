using Blurt.Engine.Config;
using Blurt.Engine.Diagnostics;
using Blurt.Engine.STT;

namespace Blurt.Engine.Pipeline;

public sealed partial class DictationSession
{
    /// <summary>
    /// Opens the request and streams <paramref name="frames"/> into it. The context is settled first,
    /// because the request's config part leads the body: the press-time read if it lands within
    /// <see cref="ContextWaitBudget"/>, else the press-known half. The caller must hold <see cref="_gate"/>.
    /// </summary>
    private void StartUploadLocked(IAsyncEnumerable<byte[]> frames)
    {
        var press = _pressContext;
        var transcriber = _transcriber;
        var clock = _clock;
        var cts = new CancellationTokenSource();
        _uploadCts = cts;
        _upload = Task.Run(async () =>
        {
            var resolved = press is null ? null : await press.WaitAsync(ContextWaitBudget, clock, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            return await transcriber.TranscribeAsync(frames, SyncSTTLimits.SampleRate, resolved ?? press?.PressKnown, cts.Token);
        });
    }

    /// <summary>Abandons the in-flight request.</summary>
    private void CancelUpload()
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            cts = _uploadCts;
            _uploadCts = null;
            ObserveFaults(_upload);
            _upload = null;
        }

        CancelQuietly(cts);
    }

    private async Task PerformReleaseAsync()
    {
        PhaseChange change;
        CancellationTokenSource? autoRelease;
        lock (_gate)
        {
            if (_phase != PipelinePhase.Recording)
            {
                return;
            }

            autoRelease = _autoReleaseCts;
            _autoReleaseCts = null;

            // Claimed before the mic stop, so the stop cue fires at key-up and a second release
            // arriving during the stop fails the Recording guard.
            change = AssignLocked(PipelinePhase.Transcribing);
        }

        CancelQuietly(autoRelease);
        Publish(change);

        int recordedBytes;
        try
        {
            recordedBytes = await _mic.StopAsync();
        }
        catch (Exception ex)
        {
            FinishRelease(ex, 0);
            return;
        }

        FinishRelease(null, recordedBytes);
    }

    /// <summary>
    /// Decides the release once the mic has stopped. A cancel wins: either one recorded before this
    /// release's turn (consumed here) or one that already claimed the phase. Otherwise a stop failure
    /// fails the dictation, a clip under <see cref="SyncSTTLimits.MinPcmBytes"/> idles quietly, and
    /// anything else spawns the transcribe-and-inject run. One critical section, so a cancel cannot
    /// land between the check and the outcome.
    /// </summary>
    private void FinishRelease(Exception? stopFailure, int recordedBytes)
    {
        PhaseChange? change = null;
        lock (_gate)
        {
            if (_cancelRequested)
            {
                _cancelRequested = false;
                change = AssignLocked(PipelinePhase.Cancelled);
            }
            else if (_phase != PipelinePhase.Transcribing)
            {
                return;
            }
            else if (stopFailure is not null)
            {
                change = AssignLocked(PipelinePhase.Failed(new BlurtError.AudioCaptureFailed(stopFailure)));
            }
            else if (recordedBytes < SyncSTTLimits.MinPcmBytes)
            {
                change = AssignLocked(PipelinePhase.Idle);
            }
            else
            {
                var run = new PipelineRun(new CancellationTokenSource(), _upload);
                _pipelineRun = run;
                _pipeline = Task.Run(() => RunTranscribeInjectAsync(run));
            }
        }

        if (change is { } published)
        {
            Publish(published);
        }
    }

    private async Task RunTranscribeInjectAsync(PipelineRun run)
    {
        try
        {
            await TranscribeAndInjectAsync(run);
        }
        catch (Exception ex)
        {
            BlurtLog.Error(LogCategory, $"transcribe-and-inject failed: {ex.GetType().Name}");
        }
    }

    /// <summary>The user's shortcuts, or none when the provider throws (logged, never onto the dictation path).</summary>
    private IReadOnlyList<TextShortcut> ReadShortcuts()
    {
        try
        {
            return _textShortcutsProvider();
        }
        catch (Exception ex)
        {
            BlurtLog.Error(LogCategory, $"textShortcutsProvider threw {ex.GetType().Name}");
            return [];
        }
    }

    private async Task TranscribeAndInjectAsync(PipelineRun run)
    {
        var text = await AwaitUploadAsync(run);
        if (text is null)
        {
            return;
        }

        // The log keeps what the service returned; the paste and the Recent list get the expanded
        // text, and the ring also keeps the spoken version.
        var expanded = TextShortcutExpander.Expand(text, ReadShortcuts());
        var spoken = text.TrimmedNonEmpty();
        var trimmed = expanded.TrimmedNonEmpty();
        if (spoken is null || trimmed is null)
        {
            TryAdvance(run, PipelinePhase.Transcribing, PipelinePhase.Idle);
            return;
        }

        string? style;
        try
        {
            style = _styleNameProvider();
        }
        catch (Exception ex)
        {
            BlurtLog.Error(LogCategory, $"styleNameProvider threw {ex.GetType().Name}");
            style = null;
        }

        var now = _clock.GetUtcNow();
        TranscriptionContext? context;
        RecentDictations? snapshot = null;
        bool handsBack;
        lock (_gate)
        {
            if (!IsLiveLocked(run, PipelinePhase.Transcribing))
            {
                return;
            }

            context = _pressContext?.Resolved;
            handsBack = _handsBackTranscript;
            // A hand-back transcript was never typed anywhere, so it never joins the ring that primes later dictations.
            if (!handsBack)
            {
                // A dictation into a secure field is pasted but never remembered: history is replayed
                // as request context on every later dictation, in any app.
                if (context?.TargetIsSecure != true)
                {
                    _recent.Record(trimmed, spoken, style, now);
                }

                snapshot = _recent.Snapshot();
            }

            _delivered = new DeliveredTranscript(text.Length, context?.AppName);
        }

        _log.Append(text, context);
        if (handsBack)
        {
            TryAdvance(run, PipelinePhase.Transcribing, PipelinePhase.HandedBack(spoken));
            return;
        }

        try
        {
            _onTranscriptDelivered?.Invoke(trimmed, snapshot!);
        }
        catch (Exception ex)
        {
            BlurtLog.Error(LogCategory, $"onTranscriptDelivered threw {ex.GetType().Name}");
        }

        await InjectAsync(run, expanded, context);
    }

    /// <summary>
    /// Waits for the request opened at press and returns its transcript, or null after settling the
    /// phase for a failure. A failure that arrives after a cancel claimed the phase changes nothing.
    /// </summary>
    private async Task<string?> AwaitUploadAsync(PipelineRun run)
    {
        if (run.Upload is null)
        {
            TryAdvance(
                run,
                PipelinePhase.Transcribing,
                PipelinePhase.Failed(new BlurtError.SttFailed(DictationPipelineException.UploadNeverStarted())));
            return null;
        }

        try
        {
            return await run.Upload;
        }
        catch (Exception ex)
        {
            var outcome = ex switch
            {
                AudioTooShortException => PipelinePhase.Idle,
                BlurtException blurt => PipelinePhase.Failed(blurt.Error),
                _ => PipelinePhase.Failed(new BlurtError.SttFailed(ex)),
            };
            TryAdvance(run, PipelinePhase.Transcribing, outcome);
            return null;
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_upload, run.Upload))
                {
                    _upload = null;
                    _uploadCts = null;
                }
            }
        }
    }

    private async Task InjectAsync(PipelineRun run, string text, TranscriptionContext? context)
    {
        if (!TryAdvance(run, PipelinePhase.Transcribing, PipelinePhase.Injecting))
        {
            return;
        }

        try
        {
            await _injector.InsertAsync(text, context?.PriorText, context?.WindowTitle, run.Cts.Token);
            TryAdvance(run, PipelinePhase.Injecting, PipelinePhase.Pasted);
        }
        catch (Exception ex)
        {
            var outcome = ex is BlurtException blurt
                ? blurt.Error.IsQuietDegradation ? PipelinePhase.NoTarget : PipelinePhase.Failed(blurt.Error)
                : PipelinePhase.Failed(new BlurtError.TargetAppLost());
            TryAdvance(run, PipelinePhase.Injecting, outcome);
        }
    }

    /// <summary>
    /// Moves the phase from <paramref name="expected"/> to <paramref name="next"/> for a run that is
    /// still live; returns false, changing nothing, once a cancel has revoked the run.
    /// </summary>
    private bool TryAdvance(PipelineRun run, PipelinePhase expected, PipelinePhase next)
    {
        PhaseChange change;
        lock (_gate)
        {
            if (!IsLiveLocked(run, expected))
            {
                return false;
            }

            change = AssignLocked(next);
        }

        Publish(change);
        return true;
    }

    private bool IsLiveLocked(PipelineRun run, PipelinePhase expected) =>
        ReferenceEquals(_pipelineRun, run) && !run.Cts.IsCancellationRequested && _phase == expected;
}
