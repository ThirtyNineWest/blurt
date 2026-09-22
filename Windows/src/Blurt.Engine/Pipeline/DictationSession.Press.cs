using Blurt.Engine.Diagnostics;
using Blurt.Engine.FocusCapture;
using Blurt.Engine.STT;

namespace Blurt.Engine.Pipeline;

public sealed partial class DictationSession
{
    private async Task PerformPressAsync(CancellationToken pressToken)
    {
        lock (_gate)
        {
            if (!_phase.IsTerminal)
            {
                return;
            }

            _pressContext = null;
        }

        if (_readinessCheck() is { } blocker)
        {
            SetPhase(PipelinePhase.Failed(blocker));
            return;
        }

        SetPhase(PipelinePhase.Connecting);
        WarmUpTranscriber();

        // The bring-up is started, not awaited, so the context capture below overlaps it.
        Task<IAsyncEnumerable<byte[]>> started;
        try
        {
            started = _mic.StartAsync(pressToken);
        }
        catch (Exception ex)
        {
            started = Task.FromException<IAsyncEnumerable<byte[]>>(ex);
        }

        try
        {
            await BeginContextCaptureAsync();
        }
        catch (Exception ex)
        {
            BlurtLog.Error(LogCategory, $"press-time context capture failed: {ex.GetType().Name}");
        }

        IAsyncEnumerable<byte[]> frames;
        try
        {
            frames = await started;
        }
        catch (Exception ex) when (ex is OperationCanceledException || IsCancelRequested(pressToken))
        {
            // The cancel arrived during the liveness wait; a mic may report the abort as any
            // exception. Consuming the request here matters: the Connecting branch of CancelAsync
            // queues no turn that would clear it, and a request left standing would cancel the next
            // press.
            if (!ConsumeCancelRequest())
            {
                SetPhase(PipelinePhase.Cancelled);
            }

            return;
        }
        catch (Exception ex)
        {
            SetPhase(PipelinePhase.Failed(new BlurtError.AudioCaptureFailed(ex)));
            return;
        }

        if (!TryClaimRecording(pressToken, frames))
        {
            try
            {
                await _mic.CancelCaptureAsync();
            }
            catch (Exception ex)
            {
                BlurtLog.Warn(LogCategory, $"mic teardown after a cancelled bring-up failed: {ex.GetType().Name}");
            }

            if (!ConsumeCancelRequest())
            {
                SetPhase(PipelinePhase.Cancelled);
            }

            return;
        }

        ArmAutoRelease();
    }

    /// <summary>
    /// Claims <see cref="PipelinePhase.Recording"/> and opens the upload, unless a cancel was
    /// requested, the press was preempted, or the phase already left <see cref="PipelinePhase.Connecting"/>.
    /// The check and the claim are one critical section, so a cancel can never be overwritten.
    /// </summary>
    private bool TryClaimRecording(CancellationToken pressToken, IAsyncEnumerable<byte[]> frames)
    {
        PhaseChange change;
        lock (_gate)
        {
            if (_cancelRequested || pressToken.IsCancellationRequested || _phase != PipelinePhase.Connecting)
            {
                return false;
            }

            change = AssignLocked(PipelinePhase.Recording);
            StartUploadLocked(frames);
        }

        Publish(change);
        return true;
    }

    /// <summary>
    /// Captures the paste target, reads the key terms and history, and starts the field-context read
    /// on a dedicated thread without waiting for it: the read is cross-process and can block for
    /// seconds against an unresponsive app.
    /// </summary>
    private async Task BeginContextCaptureAsync()
    {
        var captured = _focus.CaptureForeground();
        await _injector.SetTargetAsync(captured);
        var keyTerms = _keyTermsProvider();
        IReadOnlyList<string> recent;
        lock (_gate)
        {
            recent = _recent.TranscriptsOldestFirst;
        }

        var pressKnown = new TranscriptionContext(null, null, null, null, null, recent, keyTerms, TargetIsSecure: false);
        var press = new PressContext(pressKnown.IsEmpty ? null : pressKnown);
        lock (_gate)
        {
            _pressContext = press;
        }

        var appName = captured?.ProcessName;
        _ = Task.Factory.StartNew(
            () => ReadFieldContext(press, appName, recent, keyTerms),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private void ReadFieldContext(PressContext press, string? appName, IReadOnlyList<string> recent, IReadOnlyList<string> keyTerms)
    {
        FocusedFieldContext field;
        try
        {
            field = _focus.CaptureFieldContext();
        }
        catch (Exception ex)
        {
            BlurtLog.Error(LogCategory, $"field context read failed: {ex.GetType().Name}");

            // Fails closed: an unreadable field is treated as secure, so its dictation is never
            // remembered as history.
            press.Store(new TranscriptionContext(appName, null, null, null, null, recent, keyTerms, TargetIsSecure: true));
            return;
        }

        var context = new TranscriptionContext(
            appName, field.WindowTitle, field.FieldLabel, field.PriorText, field.SelectedText, recent, keyTerms, field.IsSecure);
        press.Store(context.IsEmpty ? null : context);
    }

    private void WarmUpTranscriber()
    {
        var transcriber = _transcriber;
        _ = Task.Run(async () =>
        {
            try
            {
                await transcriber.WarmUpAsync();
            }
            catch (Exception ex)
            {
                BlurtLog.Warn(LogCategory, $"transcriber warm-up failed: {ex.GetType().Name}");
            }
        });
    }

    /// <summary>Arms the timer that releases a held trigger after the recording cap.</summary>
    private void ArmAutoRelease()
    {
        var cts = new CancellationTokenSource();
        CancellationTokenSource? previous;
        lock (_gate)
        {
            previous = _autoReleaseCts;
            _autoReleaseCts = cts;
        }

        CancelQuietly(previous);
        _ = AutoReleaseAsync(cts.Token);
    }

    private async Task AutoReleaseAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(_maxRecordingSeconds), _clock, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Queued like a key-up; a release that already ran leaves the phase off Recording and this
        // one drops out.
        await ReleaseAsync();
    }

    private void CancelAutoRelease()
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            cts = _autoReleaseCts;
            _autoReleaseCts = null;
        }

        CancelQuietly(cts);
    }
}
