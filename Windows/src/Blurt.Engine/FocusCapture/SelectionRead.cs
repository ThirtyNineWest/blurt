namespace Blurt.Engine.FocusCapture;

/// <summary>What reading the focused element's selection found.</summary>
public abstract record SelectionRead
{
    /// <summary>A readable focused element with no selection, or a secure field: there is nothing to read aloud.</summary>
    public static readonly SelectionRead None = new NoneRead();

    /// <summary>No focused element could be reached: the only result for which the copy fallback may run.</summary>
    public static readonly SelectionRead Unreadable = new UnreadableRead();

    private SelectionRead()
    {
    }

    /// <summary>The selected text.</summary>
    public sealed record Text(string Value) : SelectionRead;

    private sealed record NoneRead : SelectionRead;

    private sealed record UnreadableRead : SelectionRead;
}
