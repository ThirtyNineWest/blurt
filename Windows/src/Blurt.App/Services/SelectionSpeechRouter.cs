using System.Media;
using Blurt.Engine.Config;
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
/// Thread-safe: the routing state and a cached copy of the read-aloud settings sit behind one lock,
/// and every action runs outside it. A hook-thread callback never touches a settings store, which
/// can be held across a file write, so it never blocks; <see cref="RefreshSettings"/> re-reads them.
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
    private bool _disposed;
    private bool _readAloud;
    private bool _ask;
    private ReadAloudStyle _style;

    /// <summary>Routes commands for <paramref name="session"/>, reading the switches from <paramref name="settings"/> now and at each <see cref="RefreshSettings"/>.</summary>
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
        _readAloud = settings.SelectionSpeech.IsEnabled;
        _ask = settings.SelectionAsk.IsEnabled;
        _style = settings.ReadAloudStyle.Style;
    }

    /// <summary>The hook's terminal-phase sync: a dictation ended without a key event.</summary>
    public Action SyncGateAfterSession { get; set; } = static () => { };

    /// <summary>The hook's silent gate reset: a read ended without a key event.</summary>
    public Action ResetGate { get; set; } = static () => { };

    /// <summary>Re-reads the read-aloud switch, asking, and the style from the stores. Call off the hook thread after any of them changes.</summary>
    public void RefreshSettings()
    {
        var readAloud = _settings.SelectionSpeech.IsEnabled;
        var ask = _settings.SelectionAsk.IsEnabled;
        var style = _settings.ReadAloudStyle.Style;
        lock (_gate)
        {
            _readAloud = readAloud;
            _ask = ask;
            _style = style;
        }
    }

    /// <summary>Routes one trigger command.</summary>
    public void Submit(Command command) => Perform(Route(routing => routing.Submit(command, _readAloud, _ask)));

    /// <summary>The hook's latch: the press was a tap.</summary>
    public void KeyLatched() => Perform(Route(routing => routing.KeyLatched()));

    /// <summary>A dictation reached a terminal phase.</summary>
    public void SessionReachedTerminalPhase(PipelinePhase phase) => Perform(Route(routing => routing.SessionReachedTerminalPhase(phase)));

    /// <summary>Stops any speech and hold timer; afterwards every command and callback is ignored.</summary>
    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource? speech;
        CancellationTokenSource? hold;
        lock (_gate)
        {
            _disposed = true;
            speech = _speech;
            hold = _holdTimer;
            _speech = null;
            _holdTimer = null;
        }

        if (speech is not null)
        {
            await speech.CancelAsync().ConfigureAwait(false);
        }

        if (hold is not null)
        {
            await hold.CancelAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Cancels <paramref name="cts"/> on the thread pool, so its callbacks never run on the calling (hook) thread.</summary>
    private static void Stop(CancellationTokenSource? cts)
    {
        if (cts is not null)
        {
            TaskObserver.Observe(cts.CancelAsync(), LogCategory, "stop speech");
        }
    }

    private IReadOnlyList<RoutingAction> Route(Func<SelectionSpeechRouting, IReadOnlyList<RoutingAction>> decide)
    {
        lock (_gate)
        {
            return _disposed ? [] : decide(_routing);
        }
    }

    private ReadAloudStyle CurrentStyle()
    {
        lock (_gate)
        {
            return _style;
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
                    var style = CurrentStyle();
                    Run(ct => _speaker.SpeakAsync(speak.Text, style, ct));
                    break;
                case RoutingAction.Answer answer:
                    var answerStyle = CurrentStyle();
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
            if (_disposed)
            {
                cts.Dispose();
                return;
            }

            previous = _holdTimer;
            _holdTimer = cts;
        }

        Stop(previous);
        TaskObserver.Observe(HoldAsync(cts), LogCategory, "hold timer");
    }

    /// <summary>Delivers the hold only while <paramref name="cts"/> is still the current, uncancelled timer.</summary>
    private async Task HoldAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(SelectionSpeechRouting.AskHoldDelay, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        IReadOnlyList<RoutingAction> actions;
        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(_holdTimer, cts) || cts.IsCancellationRequested)
            {
                return;
            }

            _holdTimer = null;
            actions = _routing.HoldElapsed();
        }

        Perform(actions);
    }

    /// <summary>Runs one read or answer as the current speech; only a speech that ends on its own reports back.</summary>
    private void Run(Func<CancellationToken, Task> work)
    {
        var cts = new CancellationTokenSource();
        CancellationTokenSource? previous;
        int generation;
        lock (_gate)
        {
            if (_disposed)
            {
                cts.Dispose();
                return;
            }

            previous = _speech;
            _speech = cts;
            generation = ++_generation;
        }

        Stop(previous);
        TaskObserver.Observe(SpeakAsync(work, cts, generation), LogCategory, "read-aloud");
    }

    private async Task SpeakAsync(Func<CancellationToken, Task> work, CancellationTokenSource cts, int generation)
    {
        try
        {
            await Task.Run(() => work(cts.Token)).ConfigureAwait(false);
        }
        catch (Exception) when (cts.IsCancellationRequested)
        {
            // A stop can surface as a socket or disposed-object failure rather than a cancellation.
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
            if (_disposed || cts.IsCancellationRequested || generation != _generation)
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

        Stop(speech);
    }
}
