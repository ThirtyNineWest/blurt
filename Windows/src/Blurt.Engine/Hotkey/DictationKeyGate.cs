namespace Blurt.Engine.Hotkey;

/// <summary>
/// Pure state machine for a single lone-modifier dictation trigger that supports tap-to-toggle and
/// hold-to-talk.
///
/// Recording starts the instant the trigger modifier goes down; what releasing it means is
/// <see cref="Activation"/>'s call (<see cref="TriggerActivations.LatchesOnRelease"/>). Under the
/// default tap-or-hold mode it is decided on key-up by how long it was held: a release at or past
/// <see cref="HoldThreshold"/> is a hold (push-to-talk stop), a shorter release is a tap (latch
/// recording on; the next tap stops it). Tap-only latches on every release; hold-only stops on every
/// release. A press combined with any other key is a normal modifier shortcut, not dictation.
///
/// The gate reads no clock — callers pass monotonic timestamps as a <see cref="TimeSpan"/> from a
/// fixed reference, so every decision is deterministic and unit-testable.
/// </summary>
public struct DictationKeyGate
{
    /// <summary>What a driven event tells the caller to do.</summary>
    public enum Action
    {
        /// <summary>No dictation-affecting edge occurred.</summary>
        None,

        /// <summary>Start recording.</summary>
        Start,

        /// <summary>Stop recording and keep the transcript.</summary>
        Stop,

        /// <summary>Discard the recording just started: a combo, not a dictation press.</summary>
        Cancel,
    }

    private enum Phase
    {
        Idle,

        /// <summary>Recording is active; deciding whether this press is a tap or a hold.</summary>
        Armed,

        /// <summary>Recording, toggled on by a tap; waiting for the next tap to stop.</summary>
        Latched,
    }

    private Phase _phase;
    private TimeSpan _downAt;

    /// <summary>
    /// True when the in-progress <see cref="Phase.Armed"/> press started from idle (so a combo
    /// discards the fresh capture) rather than from <see cref="Phase.Latched"/> (where a combo is a
    /// real shortcut over an already-running toggle recording).
    /// </summary>
    private bool _fromIdle;

    /// <summary>
    /// A release held at least this long counts as a hold (push-to-talk stop); shorter is a tap. 1 s
    /// is a good default when one key does both jobs.
    /// </summary>
    public TimeSpan HoldThreshold { get; }

    /// <summary>What a release means — tap-to-toggle, hold-to-talk, or both.</summary>
    public TriggerActivation Activation { get; }

    /// <summary>Whether the gate holds no in-flight dictation (neither armed nor latched).</summary>
    public readonly bool IsIdle => _phase == Phase.Idle;

    public DictationKeyGate(TimeSpan? holdThreshold = null, TriggerActivation activation = TriggerActivation.TapOrHold)
    {
        HoldThreshold = holdThreshold ?? TimeSpan.FromSeconds(1);
        Activation = activation;
        _phase = Phase.Idle;
        _downAt = TimeSpan.Zero;
        _fromIdle = false;
    }

    /// <summary>Feeds a trigger-modifier press at <paramref name="now"/> through the state machine.</summary>
    public Action ModifierDown(TimeSpan now)
    {
        switch (_phase)
        {
            case Phase.Idle:
                _phase = Phase.Armed;
                _downAt = now;
                _fromIdle = true;
                return Action.Start;
            case Phase.Latched:
                // Pressing again over a latched (toggle) recording: decide on key-up whether it's a
                // tap-to-stop or a hold.
                _phase = Phase.Armed;
                _downAt = now;
                _fromIdle = false;
                return Action.None;
            default:
                return Action.None;
        }
    }

    /// <summary>Feeds a trigger-modifier release at <paramref name="now"/> through the state machine.</summary>
    public Action ModifierUp(TimeSpan now)
    {
        if (_phase != Phase.Armed)
        {
            return Action.None;
        }

        // A release from idle latches recording on (tap-to-toggle) when the activation mode says
        // this press was a tap; every other release — a held push-to-talk, or any release over a
        // latched recording — stops.
        if (_fromIdle && Activation.LatchesOnRelease(now - _downAt, HoldThreshold))
        {
            _phase = Phase.Latched;
            return Action.None;
        }

        _phase = Phase.Idle;
        return Action.Stop;
    }

    /// <summary>Feeds a non-trigger key press through the state machine (a combo over the held trigger).</summary>
    public Action OtherKeyDown()
    {
        switch (_phase)
        {
            case Phase.Armed:
                if (_fromIdle)
                {
                    _phase = Phase.Idle;
                    return Action.Cancel;
                }

                _phase = Phase.Latched;
                return Action.None;
            default:
                return Action.None;
        }
    }

    /// <summary>
    /// Clears state to idle without emitting — used when the event tap is disabled (timeout /
    /// system-initiated) and intervening events may have been missed, so the held state is no longer
    /// trustworthy.
    /// </summary>
    public void Reset()
    {
        _phase = Phase.Idle;
    }
}
