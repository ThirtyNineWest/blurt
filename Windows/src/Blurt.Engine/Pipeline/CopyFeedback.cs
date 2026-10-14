namespace Blurt.Engine.Pipeline;

/// <summary>What a recent row's copy affordance shows after a copy.</summary>
public enum CopyFeedbackState
{
    /// <summary>No copy is being acknowledged.</summary>
    None,

    /// <summary>The last copy reached the clipboard.</summary>
    Copied,

    /// <summary>The last copy could not be written to the clipboard.</summary>
    Failed,
}

/// <summary>
/// The transient acknowledgement of a recent row's copy: <see cref="Report"/> shows
/// <see cref="CopyFeedbackState.Copied"/> or <see cref="CopyFeedbackState.Failed"/> for
/// <see cref="CopiedDwell"/>, and a repeat report restarts the dwell. Thread-safe.
/// </summary>
public sealed class CopyFeedback : IDisposable
{
    /// <summary>How long a copy's acknowledgement stays up.</summary>
    public static readonly TimeSpan CopiedDwell = TimeSpan.FromSeconds(1.5);

    private readonly TimeProvider _clock;
    private readonly Lock _gate = new();
    private CopyFeedbackState _state;
    private ITimer? _timer;
    private long _generation;
    private bool _disposed;

    /// <summary>Creates feedback showing nothing; <paramref name="clock"/> drives the dwell (system clock by default).</summary>
    public CopyFeedback(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Raised with the new state after each change: on the reporting thread for <see cref="Report"/>,
    /// on a timer thread when the dwell ends.
    /// </summary>
    public event Action<CopyFeedbackState>? Changed;

    /// <summary>The acknowledgement currently shown.</summary>
    public CopyFeedbackState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <summary>Returns the text shown for <paramref name="state"/>: "Copied", "Couldn't copy", or null for none.</summary>
    public static string? Label(CopyFeedbackState state) => state switch
    {
        CopyFeedbackState.Copied => "Copied",
        CopyFeedbackState.Failed => "Couldn't copy",
        _ => null,
    };

    /// <summary>
    /// Shows the outcome of a copy (<paramref name="copied"/> true when it reached the clipboard) and
    /// restarts the dwell after which the state returns to <see cref="CopyFeedbackState.None"/>.
    /// Does nothing once disposed.
    /// </summary>
    public void Report(bool copied)
    {
        var next = copied ? CopyFeedbackState.Copied : CopyFeedbackState.Failed;
        ITimer? superseded;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _state = next;
            superseded = _timer;

            // A superseded timer whose callback is already running sees a stale generation and
            // leaves the newer acknowledgement standing.
            _generation++;
            _timer = _clock.CreateTimer(Revert, _generation, CopiedDwell, Timeout.InfiniteTimeSpan);
        }

        superseded?.Dispose();
        Changed?.Invoke(next);
    }

    /// <summary>Stops the pending dwell; no later change is raised.</summary>
    public void Dispose()
    {
        ITimer? timer;
        lock (_gate)
        {
            _disposed = true;
            timer = _timer;
            _timer = null;
        }

        timer?.Dispose();
    }

    private void Revert(object? generation)
    {
        ITimer? finished;
        lock (_gate)
        {
            if (_disposed || (long)generation! != _generation)
            {
                return;
            }

            _state = CopyFeedbackState.None;
            finished = _timer;
            _timer = null;
        }

        finished?.Dispose();
        Changed?.Invoke(CopyFeedbackState.None);
    }
}
