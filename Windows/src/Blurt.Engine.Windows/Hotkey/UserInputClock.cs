namespace Blurt.Engine.Windows.Hotkey;

/// <summary>
/// When the user last pressed a key or clicked, as the low-level hooks report it. Synthetic input
/// never reaches it: the keyboard hook skips Blurt's own tagged keystrokes and the trigger key, and
/// the mouse hook skips injected buttons. Thread-safe.
/// </summary>
public sealed class UserInputClock(TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private long _lastInput;
    private int _touched;

    /// <summary>How long since the last hardware key-down or button-down, or <see cref="TimeSpan.MaxValue"/> before any.</summary>
    public TimeSpan TimeSinceInput =>
        Volatile.Read(ref _touched) == 0 ? TimeSpan.MaxValue : _clock.GetElapsedTime(Volatile.Read(ref _lastInput));

    /// <summary>Records that hardware input just happened.</summary>
    public void Touch()
    {
        Volatile.Write(ref _lastInput, _clock.GetTimestamp());
        Volatile.Write(ref _touched, 1);
    }
}
