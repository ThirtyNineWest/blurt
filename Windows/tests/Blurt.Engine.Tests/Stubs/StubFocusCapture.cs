using Blurt.Engine.FocusCapture;

namespace Blurt.Engine.Tests.Stubs;

/// <summary>
/// An <see cref="IFocusCapture"/> that returns configured values and counts reads. When
/// <see cref="FieldContextGate"/> is set, <see cref="CaptureFieldContext"/> blocks its thread until the
/// gate is set, modelling an unresponsive foreground app.
/// </summary>
public sealed class StubFocusCapture : IFocusCapture
{
    private int _foregroundCalls;
    private int _fieldContextCalls;

    /// <summary>What <see cref="CaptureForeground"/> returns.</summary>
    public FocusTarget? Foreground { get; set; }

    /// <summary>What <see cref="CaptureFieldContext"/> returns.</summary>
    public FocusedFieldContext FieldContext { get; set; } = FocusedFieldContext.Empty;

    /// <summary>When set, <see cref="CaptureFieldContext"/> blocks until it is signalled.</summary>
    public ManualResetEventSlim? FieldContextGate { get; set; }

    /// <summary>How many times <see cref="CaptureForeground"/> was called.</summary>
    public int ForegroundCalls => Volatile.Read(ref _foregroundCalls);

    /// <summary>How many times <see cref="CaptureFieldContext"/> was called.</summary>
    public int FieldContextCalls => Volatile.Read(ref _fieldContextCalls);

    /// <inheritdoc/>
    public FocusTarget? CaptureForeground()
    {
        Interlocked.Increment(ref _foregroundCalls);
        return Foreground;
    }

    /// <inheritdoc/>
    public FocusedFieldContext CaptureFieldContext()
    {
        Interlocked.Increment(ref _fieldContextCalls);
        FieldContextGate?.Wait();
        return FieldContext;
    }
}
