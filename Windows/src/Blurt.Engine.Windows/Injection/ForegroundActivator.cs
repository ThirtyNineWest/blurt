using Blurt.Engine.FocusCapture;
using Blurt.Engine.Windows.FocusCapture.Interop;
using Blurt.Engine.Windows.Injection.Interop;

namespace Blurt.Engine.Windows.Injection;

/// <summary>Returns a captured window to the foreground before a paste.</summary>
public static class ForegroundActivator
{
    /// <summary>How long a requested activation may take to become visible.</summary>
    public static readonly TimeSpan ActivationWait = TimeSpan.FromMilliseconds(350);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);

    /// <summary>
    /// Returns true when <paramref name="target"/>'s window is already foreground, or becomes foreground within
    /// <see cref="ActivationWait"/> after <c>SetForegroundWindow</c>. Returns false when the handle is no longer a
    /// window or the activation is never observed.
    /// </summary>
    /// <exception cref="OperationCanceledException">When <paramref name="ct"/> is cancelled during the wait.</exception>
    public static async Task<bool> ActivateAsync(FocusTarget target, TimeProvider clock, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(clock);
        var window = target.WindowHandle;
        if (!User32Foreground.IsWindow(window))
        {
            return false;
        }

        if (User32Window.GetForegroundWindow() == window)
        {
            return true;
        }

        _ = User32Foreground.SetForegroundWindow(window);
        var started = clock.GetTimestamp();
        while (clock.GetElapsedTime(started) < ActivationWait)
        {
            await Task.Delay(PollInterval, clock, ct).ConfigureAwait(false);
            if (User32Window.GetForegroundWindow() == window)
            {
                return true;
            }
        }

        return false;
    }
}
