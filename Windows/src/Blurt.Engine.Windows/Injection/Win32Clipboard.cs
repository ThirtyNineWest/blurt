using System.ComponentModel;
using System.Runtime.InteropServices;
using Blurt.Engine.Diagnostics;
using static Blurt.Engine.Windows.Injection.Interop.User32Clipboard;

namespace Blurt.Engine.Windows.Injection;

/// <summary>
/// <see cref="IClipboardAccess"/> over the Win32 clipboard. Every operation opens and closes the clipboard on the
/// calling thread, retrying the open while another process holds it.
/// </summary>
public sealed class Win32Clipboard : IClipboardAccess
{
    private const string LogCategory = "Clipboard";
    private const int OpenRetries = 10;
    private static readonly TimeSpan OpenRetryDelay = TimeSpan.FromMilliseconds(10);

    /// <summary>The clipboard sequence number, which changes on every clipboard write by any process.</summary>
    public uint SequenceNumber => GetClipboardSequenceNumber();

    /// <inheritdoc />
    /// <exception cref="Win32Exception">When the clipboard cannot be opened or written.</exception>
    public void Write(string text) => SetString(text);

    /// <inheritdoc />
    /// <remarks>
    /// The restore does nothing when the snapshot could not be taken, so the written text stays on the clipboard.
    /// </remarks>
    /// <exception cref="Win32Exception">When the clipboard cannot be opened or written.</exception>
    public Action WriteAndPrepareRestore(string text)
    {
        var saved = Snapshot();
        SetString(text);
        var ours = SequenceNumber;
        return () =>
        {
            if (saved is null || SequenceNumber != ours)
            {
                return;
            }

            Restore(saved);
        };
    }

    /// <summary>
    /// Captures every restorable format whose data handle is non-null and non-empty, the registered name of each
    /// format at or above <c>0xC000</c>, and the <c>CF_UNICODETEXT</c> string. Returns an empty snapshot for an empty
    /// clipboard, and null when the clipboard cannot be opened or enumerated, or held formats of which none could be
    /// captured.
    /// </summary>
    public ClipboardSnapshot? Snapshot()
    {
        if (!TryOpen(out var error))
        {
            BlurtLog.Warn(LogCategory, $"snapshot skipped: the clipboard could not be opened (error {error})");
            return null;
        }

        try
        {
            var items = new List<ClipboardItem>();
            var heldAny = false;
            uint format = 0;
            while ((format = EnumClipboardFormats(format)) != 0)
            {
                heldAny = true;
                if (!ClipboardFormats.IsRestorable(format) || ReadGlobal(GetClipboardData(format)) is not { } data)
                {
                    continue;
                }

                var name = format >= FirstRegisteredFormat ? FormatName(format) : null;
                items.Add(new ClipboardItem(format, name, data));
            }

            var enumerationError = Marshal.GetLastPInvokeError();
            if (enumerationError != 0)
            {
                BlurtLog.Warn(LogCategory, $"snapshot skipped: format enumeration failed (error {enumerationError})");
                return null;
            }

            var plainText = ReadPlainText();
            if (heldAny && items.Count == 0 && plainText is null)
            {
                BlurtLog.Warn(LogCategory, "snapshot skipped: no clipboard format could be captured");
                return null;
            }

            return new ClipboardSnapshot(items, plainText);
        }
        finally
        {
            _ = CloseClipboard();
        }
    }

    /// <summary>Replaces the clipboard contents with <paramref name="text"/> as <c>CF_UNICODETEXT</c>.</summary>
    /// <exception cref="Win32Exception">When the clipboard cannot be opened or written.</exception>
    public void SetString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var block = AllocateText(text);
        try
        {
            Open();
            try
            {
                if (!EmptyClipboard())
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), "The clipboard could not be emptied.");
                }

                if (SetClipboardData(CfUnicodeText, block) == 0)
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), "The clipboard text could not be set.");
                }

                block = 0;
            }
            finally
            {
                _ = CloseClipboard();
            }
        }
        finally
        {
            if (block != 0)
            {
                _ = GlobalFree(block);
            }
        }
    }

    /// <summary>
    /// Writes <paramref name="saved"/> back per <see cref="ClipboardSnapshot.RestoreAction"/>. Every global memory
    /// block is built before the clipboard is emptied. When no captured format can be set, the plain text is written
    /// instead when there is some.
    /// </summary>
    /// <exception cref="Win32Exception">When the clipboard cannot be opened.</exception>
    public void Restore(ClipboardSnapshot saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        switch (saved.RestoreAction)
        {
            case ClipboardRestoreAction.Empty:
                Open();
                try
                {
                    _ = EmptyClipboard();
                }
                finally
                {
                    _ = CloseClipboard();
                }

                return;
            case ClipboardRestoreAction.PlainText:
                SetString(saved.PlainText!);
                return;
            default:
                if (RestoreFormats(saved.Formats) == 0 && saved.PlainText is not null)
                {
                    SetString(saved.PlainText);
                }

                return;
        }
    }

    private static int RestoreFormats(IReadOnlyList<ClipboardItem> items)
    {
        var blocks = new List<(uint Format, nint Block)>(items.Count);
        try
        {
            foreach (var item in items)
            {
                var block = AllocateBytes(item.Data);
                if (block != 0)
                {
                    blocks.Add((FormatId(item), block));
                }
            }

            if (blocks.Count == 0)
            {
                return 0;
            }

            var set = 0;
            Open();
            try
            {
                _ = EmptyClipboard();
                for (var i = 0; i < blocks.Count; i++)
                {
                    if (SetClipboardData(blocks[i].Format, blocks[i].Block) != 0)
                    {
                        blocks[i] = (blocks[i].Format, 0);
                        set++;
                    }
                    else
                    {
                        BlurtLog.Warn(LogCategory, $"restore dropped format {blocks[i].Format:X} (error {Marshal.GetLastPInvokeError()})");
                    }
                }
            }
            finally
            {
                _ = CloseClipboard();
            }

            return set;
        }
        finally
        {
            foreach (var (_, block) in blocks)
            {
                if (block != 0)
                {
                    _ = GlobalFree(block);
                }
            }
        }
    }

    private static uint FormatId(ClipboardItem item)
    {
        if (item.RegisteredName is null)
        {
            return item.Format;
        }

        var registered = RegisterClipboardFormat(item.RegisteredName);
        return registered != 0 ? registered : item.Format;
    }

    private static string? ReadPlainText()
    {
        if (!IsClipboardFormatAvailable(CfUnicodeText) || ReadGlobal(GetClipboardData(CfUnicodeText)) is not { } bytes)
        {
            return null;
        }

        var chars = MemoryMarshal.Cast<byte, char>(bytes.AsSpan(0, bytes.Length - (bytes.Length % 2)));
        var terminator = chars.IndexOf('\0');
        return new string(terminator >= 0 ? chars[..terminator] : chars);
    }

    private static byte[]? ReadGlobal(nint handle)
    {
        if (handle == 0)
        {
            return null;
        }

        var size = GlobalSize(handle);
        if (size == 0 || size > int.MaxValue)
        {
            return null;
        }

        var pointer = GlobalLock(handle);
        if (pointer == 0)
        {
            return null;
        }

        try
        {
            var data = new byte[(int)size];
            Marshal.Copy(pointer, data, 0, data.Length);
            return data;
        }
        finally
        {
            _ = GlobalUnlock(handle);
        }
    }

    private static nint AllocateText(string text)
    {
        var bytes = new byte[(text.Length + 1) * sizeof(char)];
        MemoryMarshal.AsBytes(text.AsSpan()).CopyTo(bytes);
        var block = AllocateBytes(bytes);
        return block != 0
            ? block
            : throw new Win32Exception(Marshal.GetLastPInvokeError(), "Clipboard memory could not be allocated.");
    }

    private static nint AllocateBytes(byte[] data)
    {
        var block = GlobalAlloc(GmemMoveable, (nuint)Math.Max(data.Length, 1));
        if (block == 0)
        {
            return 0;
        }

        var pointer = GlobalLock(block);
        if (pointer == 0)
        {
            _ = GlobalFree(block);
            return 0;
        }

        Marshal.Copy(data, 0, pointer, data.Length);
        _ = GlobalUnlock(block);
        return block;
    }

    private static void Open()
    {
        if (!TryOpen(out var error))
        {
            throw new Win32Exception(error, "The clipboard could not be opened.");
        }
    }

    private static bool TryOpen(out int error)
    {
        for (var attempt = 0; ; attempt++)
        {
            if (OpenClipboard(0))
            {
                error = 0;
                return true;
            }

            error = Marshal.GetLastPInvokeError();
            if (attempt == OpenRetries)
            {
                return false;
            }

            Thread.Sleep(OpenRetryDelay);
        }
    }
}
