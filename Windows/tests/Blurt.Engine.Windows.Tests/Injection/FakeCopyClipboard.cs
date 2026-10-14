using Blurt.Engine.Windows.Injection;

namespace Blurt.Engine.Windows.Tests.Injection;

/// <summary>An in-memory clipboard. <see cref="AnswerCopy"/> stands in for the target app answering Ctrl+C.</summary>
public sealed class FakeCopyClipboard(bool readable = true) : ICopyClipboard
{
    private readonly Lock _gate = new();
    private uint _sequenceNumber;
    private string? _text = "user's clipboard";
    private int _restoreCount;

    public bool Readable { get; } = readable;

    public uint SequenceNumber
    {
        get
        {
            lock (_gate)
            {
                return _sequenceNumber;
            }
        }
    }

    public string? Text
    {
        get
        {
            lock (_gate)
            {
                return _text;
            }
        }
    }

    public int RestoreCount
    {
        get
        {
            lock (_gate)
            {
                return _restoreCount;
            }
        }
    }

    public ClipboardSnapshot? Snapshot() => Readable ? new ClipboardSnapshot([], Text) : null;

    public string? CurrentString() => Text;

    public void Restore(ClipboardSnapshot saved, uint ifSequenceIs)
    {
        lock (_gate)
        {
            if (_sequenceNumber != ifSequenceIs)
            {
                return;
            }

            _restoreCount++;
            _text = saved.PlainText;
            _sequenceNumber++;
        }
    }

    /// <summary>What the target app does on Ctrl+C: writes <paramref name="copied"/>, or nothing when it is null.</summary>
    public Func<bool> AnswerCopy(string? copied) => () =>
    {
        if (copied is not null)
        {
            Write(copied);
        }

        return true;
    };

    public void Clear() => Write(null);

    public void Write(string? text)
    {
        lock (_gate)
        {
            _text = text;
            _sequenceNumber++;
        }
    }
}
