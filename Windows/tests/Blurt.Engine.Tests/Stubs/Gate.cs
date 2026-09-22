namespace Blurt.Engine.Tests.Stubs;

/// <summary>
/// A two-phase gate for wedging a suspension point open: the seam under test calls
/// <see cref="EnterAsync"/>, which records arrival and then waits for <see cref="AllowToFinish"/>,
/// while the test awaits <see cref="WaitUntilEnteredAsync"/>. Tolerates repeated entry and an
/// <see cref="AllowToFinish"/> that already happened.
/// </summary>
public sealed class Gate
{
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Records arrival and waits until <see cref="AllowToFinish"/> or <paramref name="ct"/> cancels.</summary>
    public async Task EnterAsync(CancellationToken ct = default)
    {
        _entered.TrySetResult();
        await _finished.Task.WaitAsync(ct);
    }

    /// <summary>Completes once <see cref="EnterAsync"/> has been called; throws <see cref="TimeoutException"/> after the wait budget.</summary>
    public Task WaitUntilEnteredAsync() => _entered.Task.WaitAsync(SessionWaits.Budget);

    /// <summary>Releases every caller waiting in <see cref="EnterAsync"/>.</summary>
    public void AllowToFinish() => _finished.TrySetResult();
}
