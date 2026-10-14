using Blurt.Engine.STT;

namespace Blurt.Engine.Pipeline;

/// <summary>
/// The press-time field-context read and the one place it is published. The upload waits for it
/// with a budget before opening the request (<see cref="WaitAsync"/>); the release path reads it
/// later without a deadline (<see cref="Resolved"/>), so a read that misses the budget still
/// decides the paste separator and whether the dictation is remembered as history. Thread-safe.
/// </summary>
/// <param name="pressKnown">
/// What the session knew at press without the read: key terms and history, no focus signals. The
/// request's fallback when the read misses the budget. Null when that is empty.
/// </param>
public sealed class PressContext(TranscriptionContext? pressKnown)
{
    private readonly TaskCompletionSource<TranscriptionContext?> _stored =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private TranscriptionContext? _resolved;
    private int _taken;

    /// <summary>What the session knew at press. Set once and never changed.</summary>
    public TranscriptionContext? PressKnown { get; } = pressKnown;

    /// <summary>The read once it has been stored; null until then, and null for an empty read.</summary>
    public TranscriptionContext? Resolved => Volatile.Read(ref _resolved);

    /// <summary>
    /// Publishes the finished read. The value is written before waiters are signalled, so a waiter
    /// that wakes always finds <see cref="Resolved"/> set. Later calls are ignored.
    /// </summary>
    public void Store(TranscriptionContext? resolved)
    {
        if (_stored.Task.IsCompleted)
        {
            return;
        }

        Volatile.Write(ref _resolved, resolved);
        _stored.TrySetResult(resolved);
    }

    /// <summary>
    /// Returns the read if it is stored within <paramref name="budget"/> on <paramref name="clock"/>,
    /// else null. Only the first caller waits; every later caller gets null and reads
    /// <see cref="Resolved"/> instead. A cancelled <paramref name="ct"/> returns null.
    /// </summary>
    public async Task<TranscriptionContext?> WaitAsync(TimeSpan budget, TimeProvider clock, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (Interlocked.Exchange(ref _taken, 1) == 1)
        {
            return null;
        }

        if (_stored.Task.IsCompleted)
        {
            return await _stored.Task;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var deadline = Task.Delay(budget, clock, timeout.Token);
        var winner = await Task.WhenAny(_stored.Task, deadline);
        await timeout.CancelAsync();
        return winner == _stored.Task ? await _stored.Task : null;
    }
}
