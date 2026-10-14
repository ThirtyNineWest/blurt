using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using Blurt.App.Cues;
using Blurt.App.Overlay;
using Blurt.App.Services;
using Blurt.Engine.Diagnostics;
using Blurt.Engine.Pipeline;
using Blurt.Engine.Windows.Hotkey;
using Blurt.Engine.Windows.Permissions;

namespace Blurt.App;

/// <summary>
/// Wires the dictation session to the shell: the trigger-key hook feeds the session's command
/// queue through the read-aloud router, and every phase the session publishes is rendered to the
/// overlay pill, the tray status, the capture flag, and the recording cues. Phases, levels, and
/// delivered transcripts arrive on engine threads and are marshalled to the UI
/// <see cref="Dispatcher"/>; every public member must be called on that dispatcher's thread.
/// </summary>
public sealed class AppCoordinator : INotifyPropertyChanged, IAsyncDisposable
{
    private const string LogCategory = "AppCoordinator";

    private readonly Dispatcher _dispatcher;
    private readonly DictationComponents _components;
    private readonly SettingsStores _settings;
    private readonly PermissionsChecker _permissions;
    private readonly CueSoundPlayer _cues;
    private readonly CancellationTokenSource _lifetime = new();
    private DictationKeyHook? _keyHook;
    private SelectionSpeechRouter? _speechRouter;
    private LowLevelMouseHook? _mouseHook;
    private OverlayWindow? _overlay;
    private Task _levelsObserver = Task.CompletedTask;
    private PipelinePhase _phase = PipelinePhase.Idle;
    private TrayStatus _trayStatus = TrayStatus.Idle;
    private bool _isCapturing;
    private RecentDictations _recentDictations = new();

    /// <summary>Builds the session over <paramref name="components"/>; nothing runs until <see cref="Start"/>.</summary>
    public AppCoordinator(
        Dispatcher dispatcher,
        DictationComponents components,
        SettingsStores settings,
        ApiKeyModel apiKey,
        DictationLog log,
        PermissionsChecker permissions)
    {
        _dispatcher = dispatcher;
        _components = components;
        _settings = settings;
        _permissions = permissions;
        ApiKey = apiKey;
        _cues = new CueSoundPlayer(settings.SoundPack);
        Session = new DictationSession(
            components.Mic,
            components.Transcriber,
            components.Injector,
            components.Focus,
            log,
            keyTermsProvider: () => settings.KeyTerms.Terms,
            styleNameProvider: () => settings.EnhancedTranscripts.IsEnabled ? settings.Styles.Active?.Name : null,
            textShortcutsProvider: () => settings.TextShortcuts.Shortcuts,
            readinessCheck: apiKey.ReadinessCheck(),
            onTranscriptDelivered: (_, recents) => _dispatcher.BeginInvoke(() => RecentDictations = recents));
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raised when a press is refused because setup is unfinished (<see cref="PipelinePhase.SetupBlocker"/>),
    /// so the shell can bring the setup screen forward.
    /// </summary>
    public event Action? SetupBlocked;

    /// <summary>The dictation pipeline.</summary>
    public DictationSession Session { get; }

    /// <summary>The API key surface.</summary>
    public ApiKeyModel ApiKey { get; }

    /// <summary>The most recently rendered phase.</summary>
    public PipelinePhase Phase
    {
        get => _phase;
        private set => Set(ref _phase, value);
    }

    /// <summary>The tray icon's status, following the rendered phase.</summary>
    public TrayStatus TrayStatus
    {
        get => _trayStatus;
        private set => Set(ref _trayStatus, value);
    }

    /// <summary>Whether the microphone is being brought up or capturing.</summary>
    public bool IsCapturing
    {
        get => _isCapturing;
        private set => Set(ref _isCapturing, value);
    }

    /// <summary>The session's delivered transcripts, newest first. In memory only.</summary>
    public RecentDictations RecentDictations
    {
        get => _recentDictations;
        private set => Set(ref _recentDictations, value);
    }

    /// <summary>
    /// Warms the microphone when access is already granted, builds the (uninstalled) key hook, and
    /// starts observing phases and levels. The hook installs and the pill arms in <see cref="ShowOverlayArmed"/>.
    /// </summary>
    public void Start()
    {
        if (_permissions.Check().Microphone)
        {
            var mic = _components.Mic;
            TaskObserver.Observe(Task.Run(mic.WarmUpAsync), LogCategory, "microphone warm-up");
        }

        var router = new SelectionSpeechRouter(Session, _settings, _components.Speaker, _components.ReadSelection);
        _speechRouter = router;
        _keyHook = new DictationKeyHook(
            _settings.TriggerKey,
            _settings.TriggerActivation,
            onStart: () => router.Submit(Command.Press),
            onStop: () => router.Submit(Command.Release),
            onCancel: () => router.Submit(Command.Cancel),
            onLatch: router.KeyLatched,
            onRecordingDiscarded: () => router.Submit(Command.CancelRecording),
            onHardwareKeyDown: _components.UserInput.Touch);
        router.SyncGateAfterSession = () => _keyHook?.SyncAfterTerminalPhase();
        router.ResetGate = () => _keyHook?.ResetGate();
        Session.PhaseChanged += OnPhaseChanged;
        _levelsObserver = Task.Run(() => ObserveLevelsAsync(_lifetime.Token));
    }

    /// <summary>
    /// Arms dictation once setup is complete: installs the key hook, builds the hidden pill, and
    /// pre-loads the cues. The pill appears only while a dictation runs.
    /// </summary>
    public void ShowOverlayArmed()
    {
        _keyHook?.EnsureRunning();
        EnsureMouseHook();
        if (_overlay is null)
        {
            _overlay = new OverlayWindow(_settings.OverlayOrigin);
            _overlay.EnsureHandle();
        }

        _cues.Prime();
    }

    /// <summary>Hides the pill, for when setup stops being complete.</summary>
    public void HideOverlay() => _overlay?.HidePill();

    /// <summary>Retries a key or mouse hook install that failed; a no-op while both hooks are installed.</summary>
    public void RetryKeyHookInstallIfNeeded()
    {
        if (_keyHook is { IsInstalled: false } hook)
        {
            hook.EnsureRunning();
        }

        if (_keyHook is not null && _mouseHook is not { IsInstalled: true })
        {
            EnsureMouseHook();
        }
    }

    private void EnsureMouseHook()
    {
        if (_mouseHook is { IsInstalled: true })
        {
            return;
        }

        _mouseHook?.Dispose();
        var hook = new LowLevelMouseHook();
        hook.RawMouse += OnRawMouse;
        if (!hook.Install())
        {
            hook.Dispose();
            _mouseHook = null;
            return;
        }

        _mouseHook = hook;
    }

    // Injected buttons (LLMHF_INJECTED) are synthetic and never count as the user clicking.
    private void OnRawMouse(int message, uint flags, nuint extraInfo)
    {
        if ((flags & 0x01) == 0)
        {
            _components.UserInput.Touch();
        }
    }

    /// <summary>Re-reads the trigger key and activation after the user rebinds them.</summary>
    public void DictationBindingChanged() => _keyHook?.RefreshBinding();

    /// <summary>Reloads the cues for a newly selected sound pack and previews it.</summary>
    public void SoundPackChanged() => _cues.PackChanged();

    /// <summary>Uninstalls the hook, stops the session and the observers, and closes the pill.</summary>
    public async ValueTask DisposeAsync()
    {
        // The hook is uninstalled before the first await, so a caller that cannot wait for the
        // returned task still leaves no keyboard hook behind.
        Session.PhaseChanged -= OnPhaseChanged;
        _keyHook?.Dispose();
        _mouseHook?.Dispose();
        if (_speechRouter is not null)
        {
            await _speechRouter.DisposeAsync();
        }

        await _lifetime.CancelAsync();
        await Session.DisposeAsync();
        await _levelsObserver;
        _overlay?.Close();
        _cues.Dispose();
        _lifetime.Dispose();
    }

    private void OnPhaseChanged(PipelinePhase phase) => _dispatcher.BeginInvoke(() => Render(phase));

    private void Render(PipelinePhase phase)
    {
        // A setup blocker renders as calm idle; the only app-level effect is surfacing the fix.
        if (phase.SetupBlocker is not null)
        {
            SetupBlocked?.Invoke();
        }

        // The pill changes before the cue fires, so the sound never leads the visual state.
        _overlay?.Show(phase.OverlayState);
        Phase = phase;
        TrayStatus = phase.TrayStatus;
        IsCapturing = phase.IsCapturing;
        _cues.Transition(phase);

        // A dictation that ended without a key event leaves the trigger gate latched, which would
        // swallow the next press; the router decides whether the gate is the session's to sync.
        if (phase.IsTerminal)
        {
            _speechRouter?.SessionReachedTerminalPhase(phase);
        }
    }

    private async Task ObserveLevelsAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var level in _components.Mic.Levels.WithCancellation(ct).ConfigureAwait(false))
            {
                _ = _dispatcher.BeginInvoke(() => _overlay?.PushLevel(level));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception failure)
        {
            BlurtLog.Error(LogCategory, $"level observer stopped: {failure.GetType().Name}: {failure.Message}");
        }
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
