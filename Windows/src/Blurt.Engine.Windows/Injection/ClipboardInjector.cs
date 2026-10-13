using Blurt.Engine.Diagnostics;
using Blurt.Engine.FocusCapture;
using Blurt.Engine.Injection;
using Blurt.Engine.Windows.FocusCapture;
using Blurt.Engine.Windows.Hotkey;
using Blurt.Engine.Windows.Injection.Interop;

namespace Blurt.Engine.Windows.Injection;

/// <summary>
/// Pastes transcripts by writing them to the clipboard and posting Ctrl+V, then restores the user's clipboard after a
/// settle delay. Inserts run one at a time: each waits for the previous insert's paste and restore to finish.
/// </summary>
public sealed class ClipboardInjector : IInjector
{
    /// <summary>The default wait between posting Ctrl+V and restoring the clipboard.</summary>
    public static readonly TimeSpan DefaultPasteSettle = TimeSpan.FromMilliseconds(400);

    private const string LogCategory = "Injector";

    private readonly TimeSpan _pasteSettle;
    private readonly Func<bool> _postPaste;
    private readonly Func<FocusTarget, Task<bool>> _activateTarget;
    private readonly Func<bool> _hasEditableTarget;
    private readonly Func<FocusTarget?, bool> _isOpaqueApp;
    private readonly Func<TimeSpan> _timeSinceUserInput;
    private readonly IClipboardAccess _clipboard;
    private readonly TimeProvider _clock;
    private readonly Lock _gate = new();
    private FocusTarget? _target;
    private (ResolvedInsert Resolved, long At)? _lastInserted;
    private Task _pendingSettle = Task.CompletedTask;

    /// <summary>
    /// Creates an injector over the Win32 clipboard and <c>SendInput</c> that judges editability with
    /// <paramref name="focus"/> and treats browser and Chromium hosts as pasteable without an editable signal.
    /// <paramref name="userInput"/> says when the user last typed or clicked.
    /// </summary>
    public ClipboardInjector(UiaFocusCapture focus, UserInputClock userInput, TimeSpan? pasteSettle = null)
        : this(
            pasteSettle ?? DefaultPasteSettle,
            User32Input.PostCtrlV,
            static target => ForegroundActivator.ActivateAsync(target, TimeProvider.System, CancellationToken.None),
            (focus ?? throw new ArgumentNullException(nameof(focus))).HasEditableFocusedElement,
            static target => target is not null && ChromiumHost.IsOpaque(target.ProcessPath, target.ProcessName),
            userInput is null ? throw new ArgumentNullException(nameof(userInput)) : () => userInput.TimeSinceInput,
            new Win32Clipboard(),
            TimeProvider.System)
    {
    }

    /// <summary>Creates an injector over the given seams.</summary>
    /// <param name="pasteSettle">The wait between posting the paste and running the clipboard restore.</param>
    /// <param name="postPaste">Posts the paste keystroke; returns false when it could not be posted.</param>
    /// <param name="activateTarget">Brings the target window to the foreground; returns false when it could not.</param>
    /// <param name="hasEditableTarget">Returns whether the focused element accepts text.</param>
    /// <param name="isOpaqueApp">Returns whether the target host exposes no editable signal for real text fields.</param>
    /// <param name="timeSinceUserInput">How long since the user last pressed a key or clicked; <see cref="TimeSpan.MaxValue"/> when never.</param>
    /// <param name="clipboard">The clipboard the paste writes and restores.</param>
    /// <param name="clock">The clock the settle delay runs on.</param>
    public ClipboardInjector(
        TimeSpan pasteSettle,
        Func<bool> postPaste,
        Func<FocusTarget, Task<bool>> activateTarget,
        Func<bool> hasEditableTarget,
        Func<FocusTarget?, bool> isOpaqueApp,
        Func<TimeSpan> timeSinceUserInput,
        IClipboardAccess clipboard,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(postPaste);
        ArgumentNullException.ThrowIfNull(activateTarget);
        ArgumentNullException.ThrowIfNull(hasEditableTarget);
        ArgumentNullException.ThrowIfNull(isOpaqueApp);
        ArgumentNullException.ThrowIfNull(timeSinceUserInput);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(clock);
        _pasteSettle = pasteSettle;
        _postPaste = postPaste;
        _activateTarget = activateTarget;
        _hasEditableTarget = hasEditableTarget;
        _isOpaqueApp = isOpaqueApp;
        _timeSinceUserInput = timeSinceUserInput;
        _clipboard = clipboard;
        _clock = clock;
    }

    /// <summary>
    /// Completes when the most recent insert's paste and clipboard restore have finished. Never faults.
    /// </summary>
    public Task PendingSettle
    {
        get
        {
            lock (_gate)
            {
                return _pendingSettle;
            }
        }
    }

    /// <inheritdoc />
    public Task SetTargetAsync(FocusTarget? target)
    {
        lock (_gate)
        {
            _target = target;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns once the paste is posted; the clipboard restore runs <see cref="PendingSettle"/> later. Empty text is a
    /// no-op.
    /// </remarks>
    /// <exception cref="OperationCanceledException">When <paramref name="ct"/> is cancelled before the paste posts.</exception>
    public Task InsertAsync(string text, string? priorText, string? windowTitle, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return Task.CompletedTask;
        }

        lock (_gate)
        {
            var previous = _pendingSettle;
            var paste = Task.Run(() => PasteAfterAsync(previous, text, priorText, windowTitle, ct), CancellationToken.None);
            _pendingSettle = SettleAsync(paste);
            return paste;
        }
    }

    private async Task<PendingRestore> PasteAfterAsync(
        Task previous, string text, string? priorText, string? windowTitle, CancellationToken ct)
    {
        await previous.ConfigureAwait(false);
        var restore = await PerformInsertAsync(text, priorText, windowTitle, ct).ConfigureAwait(false);
        return new PendingRestore(Task.Delay(_pasteSettle, _clock), restore);
    }

    private async Task<Action> PerformInsertAsync(string text, string? priorText, string? windowTitle, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        FocusTarget? target;
        ResolvedInsert? continuity = null;
        lock (_gate)
        {
            target = _target;

            // The last paste still precedes the caret only if the user has not typed or clicked
            // since it; otherwise it is forgotten, as a window change would be.
            if (_lastInserted is { } last && _timeSinceUserInput() >= _clock.GetElapsedTime(last.At))
            {
                continuity = last.Resolved;
            }
        }

        var resolved = InsertResolver.Resolve(text, priorText, windowTitle, target?.ProcessId, continuity);
        if (target is not null && !await _activateTarget(target).ConfigureAwait(false))
        {
            _clipboard.Write(resolved.Text);
            throw new BlurtException(new BlurtError.TargetAppLost());
        }

        // A cancel that landed during activation must not post a keystroke into the focused window.
        ct.ThrowIfCancellationRequested();
        if (!_hasEditableTarget() && !_isOpaqueApp(target))
        {
            _clipboard.Write(resolved.Text);
            throw new BlurtException(new BlurtError.NoEditableTarget());
        }

        var restore = _clipboard.WriteAndPrepareRestore(resolved.Text);
        if (!_postPaste())
        {
            throw new BlurtException(new BlurtError.TargetAppLost());
        }

        lock (_gate)
        {
            _lastInserted = (resolved, _clock.GetTimestamp());
        }

        return restore;
    }

    private static async Task SettleAsync(Task<PendingRestore> paste)
    {
        PendingRestore pending;
        try
        {
            pending = await paste.ConfigureAwait(false);
        }
        catch (Exception)
        {
            return;
        }

        await pending.Settle.ConfigureAwait(false);
        try
        {
            pending.Restore();
        }
        catch (Exception exception)
        {
            BlurtLog.Error(LogCategory, $"clipboard restore failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>A posted paste's settle delay, already running, and the clipboard restore to run after it.</summary>
    private readonly record struct PendingRestore(Task Settle, Action Restore);
}
