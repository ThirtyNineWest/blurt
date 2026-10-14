using Blurt.Engine.Pipeline;

namespace Blurt.Engine.Hotkey;

/// <summary>
/// Decides where each trigger command goes while the read-aloud switch is on: to the dictation
/// session, to reading the selection aloud, or to asking about the selection. A pure state machine;
/// the host carries out the actions it returns. A press first waits in the deciding state for the
/// selection read, holding the commands that arrive meanwhile and replaying them in order. Speech
/// follows the gate's tap/hold semantics: a tap latches the gate so speech runs until the next tap,
/// a hold speaks until the release, a combo cancels. With asking on, a hold over a selection asks
/// about it instead (the mic opens through the session's hand-back press once the key has been down
/// <see cref="AskHoldDelay"/>), and a tap still reads. This owns every "the gate is stale" decision.
/// Not thread-safe; the host serializes calls.
/// </summary>
public sealed class SelectionSpeechRouting
{
    /// <summary>How long the key must stay down over a selection to count as a hold.</summary>
    public static readonly TimeSpan AskHoldDelay = TimeSpan.FromMilliseconds(350);

    private enum Mode
    {
        Passthrough,
        Deciding,
        Selected,
        Asking,
        Transcribing,
        Speaking,
    }

    private readonly List<Command> _pending = [];
    private Mode _mode = Mode.Passthrough;
    private bool _asks;
    private bool _latched;
    private bool _held;
    private string _selection = string.Empty;

    /// <summary>Routes <paramref name="command"/>; returns the actions to carry out, in order.</summary>
    public IReadOnlyList<RoutingAction> Submit(Command command, bool readAloudEnabled, bool askEnabled = false)
    {
        switch (_mode)
        {
            case Mode.Passthrough:
                if (command != Command.Press || !readAloudEnabled)
                {
                    return [RoutingAction.Forward(command)];
                }

                _mode = Mode.Deciding;
                _pending.Clear();
                _asks = askEnabled;
                _latched = false;
                _held = false;
                return askEnabled ? [RoutingAction.ReadSelection, RoutingAction.StartHoldTimer] : [RoutingAction.ReadSelection];
            case Mode.Deciding:
                _pending.Add(command);
                return [];
            case Mode.Selected:
                // Let go before it was a hold, or a combo over the selection: nothing was asked for.
                _mode = Mode.Passthrough;
                return command == Command.CancelRecording ? [RoutingAction.Forward(command)] : [];
            case Mode.Asking:
                return Asking(command);
            case Mode.Transcribing:
                return Transcribing(command);
            default:
                _mode = Mode.Passthrough;
                return command switch
                {
                    // A recovery reset still reaches the session, which can only cancel a live recording.
                    Command.CancelRecording => [RoutingAction.StopSpeech, RoutingAction.Forward(command)],
                    // A press reaches speech only when the gate was idle under it; its key-up would
                    // latch a recording nobody started, so the gate is cleared now.
                    Command.Press or Command.PressHandingBack => [RoutingAction.StopSpeech, RoutingAction.ResetGate],
                    _ => [RoutingAction.StopSpeech],
                };
        }
    }

    /// <summary>The gate latched: the key came up quickly enough to be a tap.</summary>
    public IReadOnlyList<RoutingAction> KeyLatched()
    {
        switch (_mode)
        {
            case Mode.Deciding:
                _latched = true;
                return [];
            case Mode.Selected:
                _mode = Mode.Speaking;
                return [new RoutingAction.Speak(_selection)];
            case Mode.Asking:
                // Let go before the gate's own hold threshold. Letting go ends the request, and the
                // latch holds no recording the user knows about.
                _mode = Mode.Transcribing;
                return [RoutingAction.Forward(Command.Release), RoutingAction.ResetGate];
            default:
                return [];
        }
    }

    /// <summary><see cref="AskHoldDelay"/> passed since the press.</summary>
    public IReadOnlyList<RoutingAction> HoldElapsed()
    {
        switch (_mode)
        {
            case Mode.Deciding when _asks:
                _held = true;
                return [];
            case Mode.Selected:
                _mode = Mode.Asking;
                return [RoutingAction.Forward(Command.PressHandingBack)];
            default:
                return [];
        }
    }

    /// <summary>The selection read returned; null means nothing is selected, so the press dictates.</summary>
    public IReadOnlyList<RoutingAction> SelectionResolved(string? selection)
    {
        if (_mode != Mode.Deciding)
        {
            return [];
        }

        if (selection is null)
        {
            _mode = Mode.Passthrough;
            var replay = new List<RoutingAction> { RoutingAction.Forward(Command.Press) };
            replay.AddRange(_pending.Select(RoutingAction.Forward));
            return replay;
        }

        // The press was over before the read returned. A later press among the held commands still
        // holds the gate, and nothing will run for it, so the gate is cleared or the next press is swallowed.
        if (_pending.Count > 0)
        {
            _mode = Mode.Passthrough;
            return _pending[^1] == Command.Press ? [RoutingAction.ResetGate] : [];
        }

        _selection = selection;
        if (!_asks || _latched)
        {
            _mode = Mode.Speaking;
            return [new RoutingAction.Speak(selection)];
        }

        if (_held)
        {
            _mode = Mode.Asking;
            return [RoutingAction.Forward(Command.PressHandingBack)];
        }

        _mode = Mode.Selected;
        return [];
    }

    /// <summary>The read ended on its own, or failed. A read the host stopped itself must not be reported.</summary>
    public IReadOnlyList<RoutingAction> SpeechFinished()
    {
        if (_mode != Mode.Speaking)
        {
            return [];
        }

        _mode = Mode.Passthrough;
        return [RoutingAction.ResetGate];
    }

    /// <summary>
    /// A dictation reached a terminal phase. While commands go to the session, the gate is this
    /// router's to sync. While a read or a decision owns it, an earlier dictation's end changes
    /// nothing. A request's end is what an ask waits for: its transcript, nothing said, or a failure.
    /// </summary>
    public IReadOnlyList<RoutingAction> SessionReachedTerminalPhase(PipelinePhase phase)
    {
        ArgumentNullException.ThrowIfNull(phase);
        switch (_mode)
        {
            case Mode.Passthrough:
                return [RoutingAction.SyncGateAfterSession];
            case Mode.Asking:
            case Mode.Transcribing:
                var stillDown = _mode == Mode.Asking;
                if (phase is PipelinePhase.HandedBackPhase handedBack)
                {
                    _mode = Mode.Speaking;
                    // Handed back with the key still down means the recording cap released it; the
                    // eventual key-up would otherwise stop the answer.
                    return stillDown
                        ? [new RoutingAction.Answer(handedBack.Transcript, _selection), RoutingAction.ResetGate]
                        : [new RoutingAction.Answer(handedBack.Transcript, _selection)];
                }

                if (phase == PipelinePhase.Idle && !stillDown)
                {
                    _mode = Mode.Speaking;
                    return [new RoutingAction.Speak(_selection)];
                }

                _mode = Mode.Passthrough;
                return [RoutingAction.SyncGateAfterSession];
            default:
                return [];
        }
    }

    private IReadOnlyList<RoutingAction> Asking(Command command)
    {
        switch (command)
        {
            case Command.Release:
                _mode = Mode.Transcribing;
                return [RoutingAction.Forward(Command.Release)];
            case Command.Cancel:
            case Command.CancelRecording:
                _mode = Mode.Passthrough;
                return [RoutingAction.Forward(command)];
            default:
                // The key is down for the whole request, so the gate cannot press again.
                return [];
        }
    }

    private IReadOnlyList<RoutingAction> Transcribing(Command command)
    {
        switch (command)
        {
            case Command.Press:
            case Command.PressHandingBack:
                // A press while the request is transcribing drops it; the gate is armed for this
                // press and its key-up would latch a recording nobody started.
                _mode = Mode.Passthrough;
                return [RoutingAction.Forward(Command.Cancel), RoutingAction.ResetGate];
            case Command.Cancel:
                _mode = Mode.Passthrough;
                return [RoutingAction.Forward(Command.Cancel)];
            default:
                // Nothing is recording, so the session treats a release or a recovery reset as a no-op.
                return [RoutingAction.Forward(command)];
        }
    }
}

/// <summary>One action <see cref="SelectionSpeechRouting"/> asks the host to carry out.</summary>
public abstract record RoutingAction
{
    /// <summary>Start the selection read; its result comes back through <see cref="SelectionSpeechRouting.SelectionResolved"/>.</summary>
    public static readonly RoutingAction ReadSelection = new ReadSelectionAction();

    /// <summary>Call <see cref="SelectionSpeechRouting.HoldElapsed"/> after <see cref="SelectionSpeechRouting.AskHoldDelay"/>, unless a newer press restarts the timer.</summary>
    public static readonly RoutingAction StartHoldTimer = new StartHoldTimerAction();

    /// <summary>Stop the speech that is playing.</summary>
    public static readonly RoutingAction StopSpeech = new StopSpeechAction();

    /// <summary>A dictation ended without a key event: the hook's terminal-phase sync.</summary>
    public static readonly RoutingAction SyncGateAfterSession = new SyncGateAfterSessionAction();

    /// <summary>A read ended on its own: clear the gate with no recording discarded.</summary>
    public static readonly RoutingAction ResetGate = new ResetGateAction();

    private RoutingAction()
    {
    }

    /// <summary>Hand <paramref name="command"/> to the dictation session.</summary>
    public static RoutingAction Forward(Command command) => new ForwardAction(command);

    /// <summary>Hand <paramref name="Command"/> to the dictation session.</summary>
    public sealed record ForwardAction(Command Command) : RoutingAction;

    /// <summary>Read <paramref name="Text"/> aloud.</summary>
    public sealed record Speak(string Text) : RoutingAction;

    /// <summary>Answer the spoken <paramref name="Request"/> about <paramref name="Selection"/>, aloud.</summary>
    public sealed record Answer(string Request, string Selection) : RoutingAction;

    private sealed record ReadSelectionAction : RoutingAction;

    private sealed record StartHoldTimerAction : RoutingAction;

    private sealed record StopSpeechAction : RoutingAction;

    private sealed record SyncGateAfterSessionAction : RoutingAction;

    private sealed record ResetGateAction : RoutingAction;
}
