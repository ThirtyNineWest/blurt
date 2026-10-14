using Blurt.Engine.FocusCapture;
using Blurt.Engine.TTS;
using Blurt.Engine.Windows.Injection.Interop;

namespace Blurt.Engine.Windows.Injection;

/// <summary>
/// The read-aloud fallback for apps whose selection UI Automation cannot reach: sends Ctrl+C, reads what
/// landed on the clipboard, and puts the user's clipboard back. Runs only on a
/// <see cref="SelectionRead.Unreadable"/> read, never on a bare caret, because some editors copy the whole
/// current line on a Ctrl+C with no selection.
/// </summary>
public sealed class SelectionCopy : ISelectionCopy
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(100);

    private readonly ICopyClipboard _clipboard;
    private readonly Func<bool> _postCopy;
    private readonly TimeSpan _timeout;
    private readonly TimeProvider _clock;

    /// <summary>Creates the production copy over the Win32 clipboard and a synthesized Ctrl+C.</summary>
    public SelectionCopy()
        : this(new Win32Clipboard(), User32Input.PostCtrlC)
    {
    }

    /// <summary>
    /// Creates a copy over <paramref name="clipboard"/> that sends the copy with <paramref name="postCopy"/>
    /// (which returns whether it was sent) and waits up to <paramref name="timeout"/>, 100 ms by default, on
    /// <paramref name="clock"/>, <see cref="TimeProvider.System"/> by default, for the copied text.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="clipboard"/> or <paramref name="postCopy"/> is null.</exception>
    public SelectionCopy(ICopyClipboard clipboard, Func<bool> postCopy, TimeSpan? timeout = null, TimeProvider? clock = null)
    {
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _postCopy = postCopy ?? throw new ArgumentNullException(nameof(postCopy));
        _timeout = timeout ?? DefaultTimeout;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// The copied text, trimmed and cut to <paramref name="maxCharacters"/>, or null when the copy produced
    /// nothing visible. Never sends the copy when the clipboard cannot be snapshotted. Polls every 10 ms up to
    /// the timeout for a sequence change that comes with a string (an app may clear the clipboard and write it
    /// a moment later). The clipboard is restored whenever the copy changed it and nothing wrote after the
    /// copy. Every clipboard call runs on a thread-pool thread.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    public Task<string?> CopySelectionAsync(int maxCharacters, CancellationToken ct) =>
        Task.Run(() => CopyAsync(maxCharacters, ct), ct);

    private async Task<string?> CopyAsync(int maxCharacters, CancellationToken ct)
    {
        var saved = _clipboard.Snapshot();
        if (saved is null)
        {
            return null;
        }

        var before = _clipboard.SequenceNumber;
        if (!_postCopy())
        {
            return null;
        }

        var started = _clock.GetTimestamp();
        string? copied = null;
        var seen = before;
        do
        {
            await Task.Delay(PollInterval, _clock, ct).ConfigureAwait(false);
            var sequence = _clipboard.SequenceNumber;
            if (sequence == before)
            {
                continue;
            }

            var text = _clipboard.CurrentString();

            // A write between the two reads would pair the string with a stale sequence, and the restore would
            // then skip; poll again instead.
            if (_clipboard.SequenceNumber == sequence)
            {
                seen = sequence;
                copied = text;
            }
        }
        while (copied is null && _clock.GetElapsedTime(started) < _timeout);

        if (seen == before)
        {
            return null;
        }

        _clipboard.Restore(saved, ifSequenceIs: seen);
        return FocusCaptureRules.VisibleTextOrNil(FocusCaptureRules.Clip(copied.TrimmedNonEmpty(), maxCharacters));
    }
}
