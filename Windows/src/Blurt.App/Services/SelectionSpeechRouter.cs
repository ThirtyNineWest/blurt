using System.Media;
using Blurt.Engine.Diagnostics;
using Blurt.Engine.Hotkey;
using Blurt.Engine.Pipeline;
using Blurt.Engine.TTS;

namespace Blurt.App.Services;

/// <summary>
/// Sits between the trigger hook and the dictation session. With the read-aloud switch off every
/// command goes straight to <see cref="DictationSession.Submit"/>; with it on, a press over selected
/// text reads it aloud, and a hold with asking on records a request about it and reads the answer.
/// The decisions are <see cref="SelectionSpeechRouting"/>'s; this class carries out its actions.
/// Thread-safe: the routing state sits behind one lock and every action runs outside it, so a
/// hook-thread callback never blocks.
/// </summary>
public sealed class SelectionSpeechRouter : IAsyncDisposable
{
    private const string LogCategory = "SelectionSpeech";

    private readonly DictationSession _session;
    private readonly SettingsStores _settings;
    private readonly SelectionSpeaker _speaker;
    private readonly Func<CancellationToken, Task<string?>> _readSelection;
    private readonly Lock _gate = new();
    private readonly SelectionSpeechRouting _routing = new();
    private CancellationTokenSource? _speech;
    private CancellationTokenSource? _holdTimer;
    private int _generation;

    /// <summary>Routes commands for <paramref name="session"/>, reading the switches from <paramref name="settings"/> at every press.</summary>
    /// <param name="session">Receives every command the routing forwards.</param>
    /// <param name="settings">The read-aloud, asking, and style stores.</param>
    /// <param name="speaker">Reads text and answers aloud.</param>
    /// <param name="readSelection">Returns the focused app's selected text, or null when there is none.</param>
    public SelectionSpeechRouter(
        DictationSession session, SettingsStores settings, SelectionSpeaker speaker, Func<CancellationToken, Task<string?>> readSelection)
    {
        _session = session;
        _settings = settings;
        _speaker = speaker;
        _readSelection = readSelection;
    }

    /// <summary>The hook's terminal-phase sync: a dictation ended without a key event.</summary>
    public Action SyncGateAfterSession { get; set; } = static () => { };

    /// <summary>The hook's silent gate reset: a read ended without a key event.</summary>
    public Action ResetGate { get; set; } = static () => { };

    /// <summary>Routes one trigger command.</summary>
    public void Submit(Command command)
    {
        var readAloud = _settings.SelectionSpeech.IsEnabled;
        var ask = _settings.SelectionAsk.IsEnabled;
        Perform(Route(routing => routing.Submit(command, readAloud, ask)));
    }

    /// <summary>The hook's latch: the press was a tap.</summary>
    public void KeyLatched() => Perform(Route(routing => routing.KeyLatched()));

    /// <summary>A dictation reached a terminal phase.</summary>
    public void SessionReachedTerminalPhase(PipelinePhase phase) => Perform(Route(routing => routing.SessionReachedTerminalPhase(phase)));

    /// <summary>Stops any speech and hold timer.</summary>
    public ValueTask DisposeAsync()
    {
        StopSpeech();
        CancellationTokenSource? hold;
        lock (_gate)
        {
            hold = _holdTimer;
            _holdTimer = null;
        }

        hold?.Cancel();
        return ValueTask.CompletedTask;
    }

    private IReadOnlyList<RoutingAction> Route(Func<SelectionSpeechRouting, IReadOnlyList<RoutingAction>> decide)
    {
        lock (_gate)
        {
            return decide(_routing);
        }
    }

    private void Perform(IReadOnlyList<RoutingAction> actions)
    {
        foreach (var action in actions)
        {
            switch (action)
            {
                case RoutingAction.ForwardAction forward:
                    _session.Submit(forward.Command);
                    break;
                case RoutingAction.Speak speak:
                    var style = _settings.ReadAloudStyle.Style;
                    Run(ct => _speaker.SpeakAsync(speak.Text, style, ct));
                    break;
                case RoutingAction.Answer answer:
                    var answerStyle = _settings.ReadAloudStyle.Style;
                    Run(ct => _speaker.AnswerAsync(answer.Request, answer.Selection, answerStyle, ct));
                    break;
                case var _ when ReferenceEquals(action, RoutingAction.ReadSelection):
                    TaskObserver.Observe(ReadSelectionAsync(), LogCategory, "selection read");
                    break;
                case var _ when ReferenceEquals(action, RoutingAction.StartHoldTimer):
                    StartHoldTimer();
                    break;
                case var _ when ReferenceEquals(action, RoutingAction.StopSpeech):
                    StopSpeech();
                    break;
                case var _ when ReferenceEquals(action, RoutingAction.SyncGateAfterSession):
                    SyncGateAfterSession();
                    break;
                case var _ when ReferenceEquals(action, RoutingAction.ResetGate):
                    ResetGate();
                    break;
                default:
                    break;
            }
        }
    }

    private async Task ReadSelectionAsync()
    {
        string? selection;
        try
        {
            selection = await Task.Run(() => _readSelection(CancellationToken.None)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            BlurtLog.Error(LogCategory, $"selection read failed: {ex.GetType().Name}");
            selection = null;
        }

        Perform(Route(routing => routing.SelectionResolved(selection)));
    }

    /// <summary>One timer at a time: a newer press cancels the older one's, so a timer left over from a quick tap cannot mark the next press as a hold.</summary>
    private void StartHoldTimer()
    {
        var cts = new CancellationTokenSource();
        CancellationTokenSource? previous;
        lock (_gate)
        {
            previous = _holdTimer;
            _holdTimer = cts;
        }

        previous?.Cancel();
        previous?.Dispose();
        TaskObserver.Observe(HoldAsync(cts.Token), LogCategory, "hold timer");
    }

    private async Task HoldAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(SelectionSpeechRouting.AskHoldDelay, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Perform(Route(routing => routing.HoldElapsed()));
    }

    /// <summary>Runs one read or answer as the current speech; only a speech that ends on its own reports back.</summary>
    private void Run(Func<CancellationToken, Task> work)
    {
        var cts = new CancellationTokenSource();
        CancellationTokenSource? previous;
        int generation;
        lock (_gate)
        {
            previous = _speech;
            _speech = cts;
            generation = ++_generation;
        }

        previous?.Cancel();
        TaskObserver.Observe(SpeakAsync(work, cts, generation), LogCategory, "read-aloud");
    }

    private async Task SpeakAsync(Func<CancellationToken, Task> work, CancellationTokenSource cts, int generation)
    {
        try
        {
            await Task.Run(() => work(cts.Token)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            BlurtLog.Error(LogCategory, $"read-aloud failed: {ex.GetType().Name}: {ex.Message}");
            SystemSounds.Beep.Play();
        }

        IReadOnlyList<RoutingAction> actions;
        lock (_gate)
        {
            // A read this router stopped was cancelled, and a newer one may already be playing.
            if (cts.IsCancellationRequested || generation != _generation)
            {
                return;
            }

            _speech = null;
            actions = _routing.SpeechFinished();
        }

        Perform(actions);
    }

    private void StopSpeech()
    {
        CancellationTokenSource? speech;
        lock (_gate)
        {
            speech = _speech;
            _speech = null;
        }

        speech?.Cancel();
    }
}
