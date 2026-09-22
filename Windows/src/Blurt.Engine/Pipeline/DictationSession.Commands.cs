using Blurt.Engine.Diagnostics;

namespace Blurt.Engine.Pipeline;

/// <summary>A host command for <see cref="DictationSession.Submit"/>, one per session method.</summary>
public enum Command
{
    /// <summary><see cref="DictationSession.PressAsync"/>.</summary>
    Press,

    /// <summary><see cref="DictationSession.ReleaseAsync"/>.</summary>
    Release,

    /// <summary><see cref="DictationSession.CancelAsync"/>.</summary>
    Cancel,

    /// <summary><see cref="DictationSession.CancelRecordingAsync"/>.</summary>
    CancelRecording,
}

public sealed partial class DictationSession
{
    /// <summary>
    /// Queues <paramref name="command"/> without blocking. Commands submitted from one thread run in
    /// submission order. A <see cref="Command.Cancel"/> records the cancel intent and preempts an
    /// in-flight press before it is queued, so it takes effect without waiting for its turn.
    /// Ignored after disposal.
    /// </summary>
    public void Submit(Command command)
    {
        if (command == Command.Cancel)
        {
            RequestCancel();
        }

        _commands.Writer.TryWrite(command);
    }

    /// <summary>
    /// Starts a dictation when the phase is terminal: refuses it with the readiness blocker, else
    /// claims <see cref="PipelinePhase.Connecting"/>, brings the mic up, and claims
    /// <see cref="PipelinePhase.Recording"/> once it is live. Completes when the press has run.
    /// </summary>
    public Task PressAsync()
    {
        var pressCts = new CancellationTokenSource();
        Task op;
        lock (_gate)
        {
            _inFlightPressCts = pressCts;
            op = ChainLocked(() => PerformPressAsync(pressCts.Token));
        }

        return FinishPressAsync(op, pressCts);
    }

    /// <summary>
    /// Ends a live recording: claims <see cref="PipelinePhase.Transcribing"/>, stops the mic, and
    /// spawns the transcribe-and-inject work. A no-op unless recording. Completes when the release
    /// has run, not when the transcript lands.
    /// </summary>
    public Task ReleaseAsync() => Enqueue(PerformReleaseAsync);

    /// <summary>
    /// The user's cancel. During <see cref="PipelinePhase.Transcribing"/>, <see cref="PipelinePhase.Injecting"/>,
    /// or <see cref="PipelinePhase.Connecting"/> it claims <see cref="PipelinePhase.Cancelled"/> at once and
    /// tears the work down. Otherwise it records the intent, which a queued release honors after its
    /// mic stop, and queues a turn that ends a live recording.
    /// </summary>
    public Task CancelAsync()
    {
        CancellationTokenSource? toCancel;
        PhaseChange? claimed = null;
        var queued = Task.CompletedTask;
        lock (_gate)
        {
            if (_phase == PipelinePhase.Transcribing || _phase == PipelinePhase.Injecting)
            {
                // The claim consumes the intent a submitted cancel recorded; left standing, it would
                // cancel the next press.
                _cancelRequested = false;
                toCancel = _pipelineRun?.Cts;
                _pipelineRun = null;
                claimed = AssignLocked(PipelinePhase.Cancelled);
            }
            else if (_phase == PipelinePhase.Connecting)
            {
                toCancel = RequestCancelLocked();
                claimed = AssignLocked(PipelinePhase.Cancelled);
            }
            else
            {
                // Recorded and chained in one critical section, so no press can queue between the
                // two and read the intent as its own.
                toCancel = RequestCancelLocked();
                queued = ChainLocked(PerformCancelAsync);
            }
        }

        CancelQuietly(toCancel);
        if (claimed is { } change)
        {
            Publish(change);
        }

        return queued;
    }

    /// <summary>
    /// Cancels only a live recording, for state-recovery callers. Never tears down a transcribe or
    /// paste and never overrides a queued release.
    /// </summary>
    public Task CancelRecordingAsync() => Enqueue(PerformCancelRecordingAsync);

    private async Task ConsumeCommandsAsync()
    {
        await foreach (var command in _commands.Reader.ReadAllAsync())
        {
            var op = command switch
            {
                Command.Press => PressAsync(),
                Command.Release => ReleaseAsync(),
                Command.Cancel => CancelAsync(),
                Command.CancelRecording => CancelRecordingAsync(),
                _ => Task.CompletedTask,
            };
            await op;
        }
    }

    private async Task FinishPressAsync(Task op, CancellationTokenSource pressCts)
    {
        await op;
        lock (_gate)
        {
            // A later press may own the slot; clearing it would leave that bring-up un-preemptable.
            if (ReferenceEquals(_inFlightPressCts, pressCts))
            {
                _inFlightPressCts = null;
            }
        }
    }

    /// <summary>Records the cancel intent and preempts the in-flight press.</summary>
    private void RequestCancel()
    {
        CancellationTokenSource? press;
        lock (_gate)
        {
            press = RequestCancelLocked();
        }

        CancelQuietly(press);
    }

    /// <summary>Records the cancel intent and returns the in-flight press to cancel once the lock is released.</summary>
    private CancellationTokenSource? RequestCancelLocked()
    {
        _cancelRequested = true;
        return _inFlightPressCts;
    }

    /// <summary>Whether a cancel was requested for the press running under <paramref name="pressToken"/>.</summary>
    private bool IsCancelRequested(CancellationToken pressToken)
    {
        lock (_gate)
        {
            return _cancelRequested || pressToken.IsCancellationRequested;
        }
    }

    /// <summary>Consumes a recorded cancel intent by claiming <see cref="PipelinePhase.Cancelled"/>; returns whether one was recorded.</summary>
    private bool ConsumeCancelRequest()
    {
        PhaseChange change;
        lock (_gate)
        {
            if (!_cancelRequested)
            {
                return false;
            }

            _cancelRequested = false;
            change = AssignLocked(PipelinePhase.Cancelled);
        }

        Publish(change);
        return true;
    }

    private async Task PerformCancelAsync()
    {
        lock (_gate)
        {
            _cancelRequested = false;
            if (_phase != PipelinePhase.Recording)
            {
                return;
            }
        }

        await StopAndCancelAsync();
    }

    private async Task PerformCancelRecordingAsync()
    {
        lock (_gate)
        {
            if (_phase != PipelinePhase.Recording)
            {
                return;
            }
        }

        await StopAndCancelAsync();
    }

    /// <summary>
    /// Tears a live recording down: disarms the auto-release, abandons the upload before the feed
    /// ends (so the request cannot read the end of audio as a finished utterance), discards the
    /// capture, and claims <see cref="PipelinePhase.Cancelled"/>. A failed mic teardown is logged,
    /// never shown.
    /// </summary>
    private async Task StopAndCancelAsync()
    {
        CancelAutoRelease();
        CancelUpload();
        try
        {
            await _mic.CancelCaptureAsync();
        }
        catch (Exception ex)
        {
            _log.AppendError(new BlurtError.AudioCaptureFailed(ex), CapturedContext);
            BlurtLog.Warn(LogCategory, $"mic teardown during cancel failed: {ex.GetType().Name}");
        }

        SetPhase(PipelinePhase.Cancelled);
    }
}
