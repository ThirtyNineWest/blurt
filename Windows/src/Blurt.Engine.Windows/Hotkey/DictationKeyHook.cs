using Blurt.Engine.Diagnostics;
using Blurt.Engine.Hotkey;

namespace Blurt.Engine.Windows.Hotkey;

/// <summary>
/// Drives the single lone-modifier dictation trigger from a <see cref="LowLevelKeyboardHook"/>.
/// Each delivery is reduced by <see cref="HookEventReducer"/> and routed through
/// <see cref="DictationKeyRouter"/>; the gate's actions invoke the start, stop, and cancel
/// callbacks. Swallows nothing, so shortcuts that use the trigger keep working in other apps.
/// </summary>
/// <remarks>
/// Key events arrive on the hook thread while binding changes arrive from the caller's thread, so
/// router state is guarded by a lock. Callbacks run outside the lock, on whichever thread produced
/// the action, and must not block: a keyboard-event callback runs inside the system's low-level
/// hook timeout.
/// </remarks>
public sealed class DictationKeyHook : IDisposable
{
    private const string LogCategory = "DictationKeyHook";

    private readonly TriggerKeyStore _keys;
    private readonly TriggerActivationStore _activation;
    private readonly Action _onStart;
    private readonly Action _onStop;
    private readonly Action _onCancel;
    private readonly Action _onRecordingDiscarded;
    private readonly TimeProvider _clock;
    private readonly long _reference;
    private readonly Lock _routerGate = new();
    private readonly Lock _lifecycle = new();
    private DictationKeyRouter _router;
    private LowLevelKeyboardHook? _hook;
    private bool _hasLoggedInstallFailure;

    /// <summary>Creates the hook seeded from the persisted binding; nothing is installed until <see cref="EnsureRunning"/>.</summary>
    /// <param name="onRecordingDiscarded">
    /// Invoked when a state reset (rebinding, terminal-phase sync) discards a live gate state whose
    /// ending key events can no longer arrive, so the owner must cancel the recording.
    /// </param>
    public DictationKeyHook(
        TriggerKeyStore keys,
        TriggerActivationStore activation,
        Action onStart,
        Action onStop,
        Action onCancel,
        Action onRecordingDiscarded,
        TimeProvider? clock = null)
    {
        _keys = keys;
        _activation = activation;
        _onStart = onStart;
        _onStop = onStop;
        _onCancel = onCancel;
        _onRecordingDiscarded = onRecordingDiscarded;
        _clock = clock ?? TimeProvider.System;
        _reference = _clock.GetTimestamp();
        _router = new DictationKeyRouter(keys.TriggerKey.KeyCode(), activation: activation.Activation);
    }

    /// <summary>Whether the low-level keyboard hook is installed.</summary>
    public bool IsInstalled
    {
        get
        {
            lock (_lifecycle)
            {
                return _hook?.IsInstalled == true;
            }
        }
    }

    /// <summary>
    /// Re-reads the binding and installs the hook if it is not installed, replacing a hook whose
    /// thread has exited. Idempotent while the hook is installed.
    /// </summary>
    /// <returns>Whether the hook is installed; false when installation failed, so the caller can retry.</returns>
    public bool EnsureRunning()
    {
        RefreshBinding();
        lock (_lifecycle)
        {
            if (_hook is not null)
            {
                if (_hook.IsInstalled)
                {
                    return true;
                }

                _hook.RawKey -= HandleRawKey;
                _hook.Dispose();
                _hook = null;
            }

            var hook = new LowLevelKeyboardHook();
            hook.RawKey += HandleRawKey;
            if (!hook.Install())
            {
                hook.Dispose();
                if (!_hasLoggedInstallFailure)
                {
                    _hasLoggedInstallFailure = true;
                    BlurtLog.Error(LogCategory, "keyboard hook install failed");
                }

                return false;
            }

            _hook = hook;
            BlurtLog.Info(LogCategory, "dictation key hook installed");
            return true;
        }
    }

    /// <summary>
    /// Re-reads the trigger key and activation mode into the router. Invokes the discarded-recording
    /// callback when the rebind reset a live gate state, since the old key's up-event can no longer
    /// end that dictation.
    /// </summary>
    public void RefreshBinding()
    {
        var keyCode = _keys.TriggerKey.KeyCode();
        var activation = _activation.Activation;
        bool discarded;
        lock (_routerGate)
        {
            discarded = _router.Rebind(keyCode, activation);
        }

        if (discarded)
        {
            _onRecordingDiscarded();
        }
    }

    /// <summary>
    /// Clears gate state left behind by a dictation that ended without a key event (auto-release cap,
    /// refused or failed start), so the next trigger press starts a dictation. Invokes the
    /// discarded-recording callback when the reset cleared a live gate state.
    /// </summary>
    public void SyncAfterTerminalPhase()
    {
        bool discarded;
        lock (_routerGate)
        {
            discarded = _router.Reset();
        }

        if (discarded)
        {
            _onRecordingDiscarded();
        }
    }

    /// <summary>
    /// Reduces one raw hook delivery and routes it at the current elapsed time. This is the handler
    /// the installed hook calls.
    /// </summary>
    public void HandleRawKey(int message, int vkCode, int scanCode, uint flags, nuint extraInfo)
    {
        DictationKeyGate.Action action;
        lock (_routerGate)
        {
            var routed = HookEventReducer.Reduce(message, vkCode, scanCode, flags, extraInfo, _router.TriggerKeyCode);
            if (routed is null)
            {
                return;
            }

            action = _router.Handle(routed, _clock.GetElapsedTime(_reference));
        }

        Invoke(action);
    }

    /// <summary>Routes <paramref name="routed"/> at <paramref name="now"/>, invokes the matching callback, and returns the gate's action.</summary>
    public DictationKeyGate.Action Dispatch(DictationKeyRouter.Event routed, TimeSpan now)
    {
        DictationKeyGate.Action action;
        lock (_routerGate)
        {
            action = _router.Handle(routed, now);
        }

        Invoke(action);
        return action;
    }

    /// <summary>Uninstalls the hook and waits for its thread to exit.</summary>
    public void Dispose()
    {
        LowLevelKeyboardHook? hook;
        lock (_lifecycle)
        {
            hook = _hook;
            _hook = null;
        }

        if (hook is not null)
        {
            hook.RawKey -= HandleRawKey;
            hook.Dispose();
        }
    }

    private void Invoke(DictationKeyGate.Action action)
    {
        switch (action)
        {
            case DictationKeyGate.Action.Start:
                _onStart();
                break;
            case DictationKeyGate.Action.Stop:
                _onStop();
                break;
            case DictationKeyGate.Action.Cancel:
                _onCancel();
                break;
            case DictationKeyGate.Action.None:
                break;
        }
    }
}
