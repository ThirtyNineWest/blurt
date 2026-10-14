namespace Blurt.Engine.Hotkey;

/// <summary>
/// Routes raw trigger-key events into <see cref="DictationKeyGate"/> and owns the three decisions
/// that would otherwise sit untested in the app's hook shim:
///
/// <list type="bullet">
/// <item>
/// <b>Edge dedup.</b> A flags delivery re-reports the bound key's state whether or not it changed, so
/// the router tracks the modifier's current physical state and only a genuine down/up edge reaches
/// the gate — repeated same-state deliveries must not double-fire a dictation.
/// </item>
/// <item>
/// <b>Relevance.</b> Only the bound keycode's flag changes drive the modifier; a key-down for any
/// other key marks a combo, and the trigger's own keycode never counts as a combo.
/// </item>
/// <item>
/// <b>Dropped-event recovery.</b> After the host's hook is disabled and re-enabled, whether the
/// gate's state survives depends on the trigger still being held — see
/// <see cref="RecoverFromDroppedEvents(bool)"/>.
/// </item>
/// </list>
///
/// Like the gate, the router reads no clock — callers pass monotonic timestamps — so every decision
/// is deterministic and unit-testable.
/// </summary>
public struct DictationKeyRouter
{
    /// <summary>A keyboard event reduced to exactly what the routing decision needs.</summary>
    public abstract record Event
    {
        /// <summary>A flags delivery: the keycode it reports and whether the bound trigger's state bit is set.</summary>
        /// <param name="KeyCode">The keycode the delivery reports.</param>
        /// <param name="TriggerFlagIsOn">Whether the bound trigger's device-state bit is set.</param>
        public sealed record FlagsChanged(int KeyCode, bool TriggerFlagIsOn) : Event;

        /// <summary>A key-down for a keycode.</summary>
        /// <param name="KeyCode">The keycode that went down.</param>
        public sealed record KeyDown(int KeyCode) : Event;

        private Event()
        {
        }
    }

    /// <summary>The virtual keycode of the bound trigger modifier.</summary>
    public int TriggerKeyCode { get; private set; }

    private DictationKeyGate _gate;

    /// <summary>
    /// The bound modifier's current physical state, so repeated flags deliveries with an unchanged
    /// bit don't re-fire the gate.
    /// </summary>
    private bool _modifierIsDown;

    public DictationKeyRouter(
        int triggerKeyCode, TimeSpan? holdThreshold = null, TriggerActivation activation = TriggerActivation.TapOrHold)
    {
        TriggerKeyCode = triggerKeyCode;
        _gate = new DictationKeyGate(holdThreshold, activation);
        _modifierIsDown = false;
    }

    /// <summary>Feeds one event through the relevance/edge filters into the gate and returns its decision.</summary>
    public DictationKeyGate.Action Handle(Event e, TimeSpan now)
    {
        switch (e)
        {
            case Event.FlagsChanged(var keyCode, var triggerFlagIsOn):
                if (keyCode != TriggerKeyCode)
                {
                    return DictationKeyGate.Action.None;
                }

                if (triggerFlagIsOn && !_modifierIsDown)
                {
                    _modifierIsDown = true;
                    return _gate.ModifierDown(now);
                }

                if (!triggerFlagIsOn && _modifierIsDown)
                {
                    _modifierIsDown = false;
                    return _gate.ModifierUp(now);
                }

                return DictationKeyGate.Action.None;

            case Event.KeyDown(var keyCode):
                return keyCode == TriggerKeyCode ? DictationKeyGate.Action.None : _gate.OtherKeyDown();

            default:
                throw new ArgumentOutOfRangeException(nameof(e), e, null);
        }
    }

    /// <summary>
    /// Rebinds the trigger — key and activation mode, the two halves of the binding Settings can
    /// change — and resets: events already tracked belong to the old binding, whose up-event can no
    /// longer match (or would resolve under the old mode). The gate is rebuilt because its
    /// <see cref="DictationKeyGate.Activation"/> does not change after construction.
    /// </summary>
    /// <returns>Whether the reset discarded a live recording (see <see cref="Reset"/>).</returns>
    public bool Rebind(int triggerKeyCode, TriggerActivation activation)
    {
        TriggerKeyCode = triggerKeyCode;
        var discardedRecording = Reset();
        _gate = new DictationKeyGate(_gate.HoldThreshold, activation);
        return discardedRecording;
    }

    /// <summary>
    /// Recovery after the host's event hook was disabled (by timeout, or by user input while it was
    /// down) and re-enabled: events may have been dropped, so the gate's state may no longer match
    /// the keyboard.
    ///
    /// <paramref name="triggerStillHeld"/> is the caller's read of whether the trigger modifier is
    /// physically down right now. If it is, nothing that matters was lost: the key-up is still coming
    /// and the gate is coherent, so the state is kept — resetting would discard speech the user is
    /// mid-sentence on. If it isn't, the trigger's key-up may have been among the dropped events, so
    /// the gate is reset.
    /// </summary>
    /// <returns>Whether that reset discarded a live recording the caller must cancel upstream.</returns>
    public bool RecoverFromDroppedEvents(bool triggerStillHeld)
    {
        if (triggerStillHeld)
        {
            return false;
        }

        return Reset();
    }

    /// <summary>
    /// Clears the gate (and the modifier-down tracker) because the events it was tracking can no
    /// longer be trusted — the binding changed, or the host's event hook was disabled and events were
    /// dropped.
    /// </summary>
    /// <returns>
    /// True when the reset discarded a live gate state (armed or latched): no future key event can
    /// end that dictation, so the caller must cancel the recording upstream.
    /// </returns>
    public bool Reset()
    {
        var discardedRecording = !_gate.IsIdle;
        _gate.Reset();
        _modifierIsDown = false;
        return discardedRecording;
    }
}
