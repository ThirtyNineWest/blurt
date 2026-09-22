using Blurt.Engine.Windows.Injection;

namespace Blurt.Engine.Windows.Tests.Injection;

/// <summary>
/// An in-memory <see cref="IClipboardAccess"/> holding one string and a write counter. The restore closure puts the
/// saved string back only when no other write happened after its own, matching the sequence-number gate of
/// <see cref="Win32Clipboard"/>.
/// </summary>
public sealed class FakeClipboard(string? text) : IClipboardAccess
{
    private readonly Lock _gate = new();
    private string? _text = text;
    private int _count;

    /// <summary>The current contents.</summary>
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

    /// <inheritdoc />
    public void Write(string text)
    {
        lock (_gate)
        {
            _text = text;
            _count++;
        }
    }

    /// <inheritdoc />
    public Action WriteAndPrepareRestore(string text)
    {
        string? saved;
        int mark;
        lock (_gate)
        {
            saved = _text;
            _text = text;
            mark = ++_count;
        }

        return () =>
        {
            lock (_gate)
            {
                if (_count != mark)
                {
                    return;
                }

                _text = saved;
                _count++;
            }
        };
    }

    /// <summary>Simulates another process writing the clipboard.</summary>
    public void ExternalWrite(string text) => Write(text);
}
