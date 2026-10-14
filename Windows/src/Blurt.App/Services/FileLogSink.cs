using System.IO;
using System.Text;

namespace Blurt.App.Services;

/// <summary>
/// Appends each <see cref="Engine.Diagnostics.BlurtLog"/> line to a file with a local timestamp.
/// Thread-safe. A file that cannot be opened or written drops lines rather than throwing, so
/// logging never takes the app down.
/// </summary>
public sealed class FileLogSink : IDisposable
{
    /// <summary>A log larger than this at startup is truncated before the first line is written.</summary>
    public const long StartupSizeLimitBytes = 5 * 1024 * 1024;

    private readonly Lock _gate = new();
    private StreamWriter? _writer;

    /// <summary>Opens <c>{directory}\blurt.log</c> for appending, creating the directory.</summary>
    public FileLogSink(string directory)
    {
        FilePath = Path.Combine(directory, "blurt.log");
        try
        {
            Directory.CreateDirectory(directory);
            var existing = new FileInfo(FilePath);
            var mode = existing.Exists && existing.Length > StartupSizeLimitBytes ? FileMode.Create : FileMode.Append;
            var stream = new FileStream(FilePath, mode, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true, NewLine = "\n" };
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            _writer = null;
        }
    }

    /// <summary>The log file's full path.</summary>
    public string FilePath { get; }

    /// <summary>Appends <paramref name="line"/> prefixed with the current local time.</summary>
    public void Write(string line)
    {
        lock (_gate)
        {
            if (_writer is null)
            {
                return;
            }

            try
            {
                _writer.WriteLine($"{DateTimeOffset.Now:yyyy-MM-ddTHH:mm:ss.fffzzz} {line}");
            }
            catch (Exception failure) when (failure is IOException or ObjectDisposedException)
            {
                // A write error drops the line; the next write retries.
            }
        }
    }

    /// <summary>Closes the file; later writes are dropped.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
