using System.Runtime.InteropServices;
using Blurt.Engine.Diagnostics;
using Blurt.Engine.Windows.Hotkey.Interop;

namespace Blurt.Engine.Windows.Hotkey;

/// <summary>
/// Owns a low-level (<c>WH_KEYBOARD_LL</c> or <c>WH_MOUSE_LL</c>) hook installed on a dedicated
/// background STA thread that pumps its message loop, and calls <see cref="OnHook"/> on that thread
/// for every delivery. Swallows nothing: every delivery is passed on through <c>CallNextHookEx</c>.
/// </summary>
public abstract class LowLevelHook : IDisposable
{
    private const string LogCategory = "LowLevelHook";

    private static int s_hasLoggedInstallFailure;

    /// <summary>
    /// Holds the callback delegate for the hook's lifetime; the native side keeps only a function
    /// pointer, so a collected delegate would crash the process on the next event.
    /// </summary>
    private readonly User32Hook.HookProc _proc;
    private readonly int _idHook;
    private readonly string _threadName;
    private readonly Lock _lifecycle = new();
    private Thread? _thread;
    private uint _threadId;
    private nint _hook;
    private bool _disposed;

    /// <summary>Creates the hook; nothing is installed until <see cref="Install"/>.</summary>
    /// <param name="idHook">The <c>SetWindowsHookEx</c> hook type.</param>
    /// <param name="threadName">The name of the hook thread.</param>
    protected LowLevelHook(int idHook, string threadName)
    {
        _idHook = idHook;
        _threadName = threadName;
        _proc = Callback;
    }

    /// <summary>Whether the hook is currently installed.</summary>
    public bool IsInstalled => Volatile.Read(ref _hook) != 0;

    /// <summary>
    /// Starts the hook thread and installs the hook on it. Idempotent while installed.
    /// </summary>
    /// <returns>Whether the hook is installed; false when <c>SetWindowsHookExW</c> failed or the hook was disposed.</returns>
    public bool Install()
    {
        lock (_lifecycle)
        {
            if (_disposed)
            {
                return false;
            }

            if (_thread is not null)
            {
                return IsInstalled;
            }

            using var ready = new ManualResetEventSlim();
            var thread = new Thread(() => Run(ready)) { IsBackground = true, Name = _threadName };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.Wait();

            if (!IsInstalled)
            {
                thread.Join();
                return false;
            }

            _thread = thread;
            return true;
        }
    }

    /// <summary>Uninstalls the hook, ends the message loop, and waits for the hook thread to exit.</summary>
    public void Dispose()
    {
        Thread? thread;
        lock (_lifecycle)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            thread = _thread;
            _thread = null;
            if (thread is not null)
            {
                _ = User32Hook.PostThreadMessage(_threadId, User32Hook.WmQuit, 0, 0);
            }
        }

        if (thread is not null && thread != Thread.CurrentThread)
        {
            thread.Join();
        }
    }

    /// <summary>
    /// Handles one hook delivery on the hook thread. Runs inside the system's low-level hook timeout
    /// and must not block; an exception it throws is logged and does not reach the hook chain.
    /// </summary>
    /// <param name="wParam">The delivery's message identifier.</param>
    /// <param name="lParam">A pointer to the delivery's hook structure, valid only for this call.</param>
    protected abstract void OnHook(nint wParam, nint lParam);

    private void Run(ManualResetEventSlim ready)
    {
        _threadId = User32Hook.GetCurrentThreadId();

        // The queue must exist before Install returns so Dispose's WM_QUIT post cannot be lost.
        _ = User32Hook.PeekMessage(out _, 0, User32Hook.WmUser, User32Hook.WmUser, User32Hook.PmNoRemove);

        var hook = User32Hook.SetWindowsHookEx(
            _idHook,
            Marshal.GetFunctionPointerForDelegate(_proc),
            User32Hook.GetModuleHandle(null),
            0);
        if (hook == 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (Interlocked.Exchange(ref s_hasLoggedInstallFailure, 1) == 0)
            {
                BlurtLog.Error(LogCategory, $"SetWindowsHookExW failed for hook {_idHook} (error {error})");
            }

            ready.Set();
            return;
        }

        Volatile.Write(ref _hook, hook);
        ready.Set();

        try
        {
            while (User32Hook.GetMessage(out var message, 0, 0, 0) > 0)
            {
                _ = User32Hook.TranslateMessage(in message);
                _ = User32Hook.DispatchMessage(in message);
            }
        }
        finally
        {
            _ = User32Hook.UnhookWindowsHookEx(hook);
            Volatile.Write(ref _hook, 0);
        }
    }

    private nint Callback(int nCode, nint wParam, nint lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                OnHook(wParam, lParam);
            }
        }
        catch (Exception exception)
        {
            // An exception escaping a native callback ends the process, and the log sink is replaceable.
            try
            {
                BlurtLog.Error(LogCategory, $"hook {_idHook} handler threw: {exception}");
            }
            catch
            {
            }
        }

        return User32Hook.CallNextHookEx(0, nCode, wParam, lParam);
    }
}
