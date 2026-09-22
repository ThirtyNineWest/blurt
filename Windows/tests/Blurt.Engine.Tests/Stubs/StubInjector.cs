using Blurt.Engine.FocusCapture;
using Blurt.Engine.Injection;

namespace Blurt.Engine.Tests.Stubs;

/// <summary>
/// An <see cref="IInjector"/> that records each insert with the caret context forwarded alongside
/// it, and the last target set. When <see cref="ErrorToThrow"/> is set, inserts throw it and record
/// nothing.
/// </summary>
public sealed class StubInjector : IInjector
{
    private readonly Lock _gate = new();
    private readonly List<Insertion> _insertions = [];
    private FocusTarget? _target;
    private int _setTargetCalls;

    /// <summary>When set, <see cref="InsertAsync"/> throws it.</summary>
    public Exception? ErrorToThrow { get; set; }

    /// <summary>Every successful insert, in order.</summary>
    public IReadOnlyList<Insertion> Insertions
    {
        get
        {
            lock (_gate)
            {
                return [.. _insertions];
            }
        }
    }

    /// <summary>The inserted texts, in order.</summary>
    public IReadOnlyList<string> Inserted => Insertions.Select(i => i.Text).ToList();

    /// <summary>The value passed to the most recent <see cref="SetTargetAsync"/>.</summary>
    public FocusTarget? Target
    {
        get
        {
            lock (_gate)
            {
                return _target;
            }
        }
    }

    /// <summary>How many times <see cref="SetTargetAsync"/> was called.</summary>
    public int SetTargetCalls => Volatile.Read(ref _setTargetCalls);

    /// <inheritdoc/>
    public Task SetTargetAsync(FocusTarget? target)
    {
        Interlocked.Increment(ref _setTargetCalls);
        lock (_gate)
        {
            _target = target;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task InsertAsync(string text, string? priorText, string? windowTitle, CancellationToken ct)
    {
        if (ErrorToThrow is { } error)
        {
            return Task.FromException(error);
        }

        lock (_gate)
        {
            _insertions.Add(new Insertion(text, priorText, windowTitle));
        }

        return Task.CompletedTask;
    }

    /// <summary>One recorded insert.</summary>
    public sealed record Insertion(string Text, string? PriorText, string? WindowTitle);
}
