using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Blurt.Engine.Diagnostics;
using Blurt.Engine.STT;

namespace Blurt.Engine.Pipeline;

/// <summary>
/// The developer-mode log: completed transcripts append to <see cref="DictationsPath"/> and failures
/// to <see cref="ErrorsPath"/>, one JSON object per line with sorted keys. Every append is gated on
/// <c>isEnabled</c> at call time, so with developer mode off nothing touches the disk. Writes run in
/// order on one background consumer and never throw onto the caller; a failed write is reported
/// through <see cref="BlurtLog"/> by file name only.
/// </summary>
public sealed partial class DictationLog
{
    private const string LogCategory = "DictationLog";

    private static readonly JsonSerializerOptions LineOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Func<bool> _isEnabled;
    private readonly Func<TranscriptionContext?, string> _renderPrompt;
    private readonly Func<IReadOnlyList<string>, IReadOnlyList<string>> _fitKeyterms;
    private readonly TimeProvider _clock;
    private readonly Channel<Action> _writes = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions { SingleReader = true });

    /// <summary>
    /// Creates a log writing <c>dictations.jsonl</c> and <c>errors.jsonl</c> under
    /// <paramref name="directory"/>, which is created on the first write. <paramref name="renderPrompt"/>
    /// and <paramref name="fitKeyterms"/> are the request's own builders, so an entry records what was
    /// sent; <paramref name="clock"/> stamps entries and defaults to the system clock.
    /// </summary>
    public DictationLog(
        Func<bool> isEnabled,
        string directory,
        Func<TranscriptionContext?, string> renderPrompt,
        Func<IReadOnlyList<string>, IReadOnlyList<string>> fitKeyterms,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(isEnabled);
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentNullException.ThrowIfNull(renderPrompt);
        ArgumentNullException.ThrowIfNull(fitKeyterms);
        _isEnabled = isEnabled;
        _renderPrompt = renderPrompt;
        _fitKeyterms = fitKeyterms;
        _clock = clock ?? TimeProvider.System;
        DictationsPath = Path.Combine(directory, "dictations.jsonl");
        ErrorsPath = Path.Combine(directory, "errors.jsonl");
        _ = Task.Run(ConsumeWritesAsync);
    }

    /// <summary>Where transcripts are appended. Reading it touches nothing.</summary>
    public string DictationsPath { get; }

    /// <summary>Where failures are appended, beside <see cref="DictationsPath"/>. Reading it touches nothing.</summary>
    public string ErrorsPath { get; }

    /// <summary>
    /// Appends a completed transcript with its press-time context when developer mode is on; returns
    /// without effect otherwise. The entry is stamped now and written later off the caller's thread.
    /// </summary>
    public void Append(string transcript, TranscriptionContext? context)
    {
        Gated(now => AppendLine(MakeEntry(transcript, context, now), DictationsPath));
    }

    /// <summary>
    /// Returns one transcript entry: <c>transcript</c>, <c>ts</c>, the context's <c>app</c>,
    /// <c>window</c>, <c>field</c>, <c>prior</c> (raw), and <c>selected</c> when present, the rendered
    /// <c>stt_prompt</c> and fitted <c>keyterms</c> when non-empty, keys in ordinal order.
    /// </summary>
    public JsonObject MakeEntry(string transcript, TranscriptionContext? context, DateTimeOffset now)
    {
        var fields = new SortedDictionary<string, JsonNode?>(StringComparer.Ordinal)
        {
            ["transcript"] = transcript,
            ["ts"] = FormatTimestamp(now),
        };
        AddIfPresent(fields, "app", context?.AppName);
        AddIfPresent(fields, "window", context?.WindowTitle);
        AddIfPresent(fields, "field", context?.FieldLabel);
        AddIfPresent(fields, "prior", context?.PriorText);
        AddIfPresent(fields, "selected", context?.SelectedText);
        var prompt = _renderPrompt(context);
        if (!string.IsNullOrEmpty(prompt))
        {
            fields["stt_prompt"] = prompt;
        }

        var keyterms = _fitKeyterms(context?.KeyTerms ?? []);
        if (keyterms.Count > 0)
        {
            fields["keyterms"] = new JsonArray(keyterms.Select(t => (JsonNode?)t).ToArray());
        }

        return new JsonObject(fields);
    }

    /// <summary>
    /// Deletes both log files, then the directory if nothing else is left in it. A file that is
    /// already absent counts as removed. Returns false when an existing file could not be deleted.
    /// </summary>
    public bool RemoveStoredLogs()
    {
        var allRemoved = true;
        foreach (var path in new[] { DictationsPath, ErrorsPath })
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                allRemoved = false;
                BlurtLog.Error(LogCategory, $"removing {Path.GetFileName(path)} failed: {Describe(ex)}");
            }
        }

        var directory = Path.GetDirectoryName(DictationsPath);
        try
        {
            if (directory is not null && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return allRemoved;
    }

    /// <summary>Completes once every write queued before this call has finished.</summary>
    public Task DrainAsync()
    {
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return _writes.Writer.TryWrite(drained.SetResult) ? drained.Task : Task.CompletedTask;
    }

    private static string FormatTimestamp(DateTimeOffset now) =>
        now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    // The exception type and HResult only: file-system messages embed the full path, which carries
    // the user's account name.
    private static string Describe(Exception ex) => $"{ex.GetType().Name} (0x{ex.HResult:X8})";

    private static void AddIfPresent(SortedDictionary<string, JsonNode?> fields, string key, string? value)
    {
        if (value is not null)
        {
            fields[key] = value;
        }
    }

    private static void AppendLine(JsonObject entry, string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var line = Utf8NoBom.GetBytes(entry.ToJsonString(LineOptions) + "\n");
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            stream.Write(line);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            BlurtLog.Error(LogCategory, $"append to {Path.GetFileName(path)} failed: {Describe(ex)}");
        }
    }

    private void Gated(Action<DateTimeOffset> write)
    {
        if (!_isEnabled())
        {
            return;
        }

        var now = _clock.GetUtcNow();
        _writes.Writer.TryWrite(() => write(now));
    }

    private async Task ConsumeWritesAsync()
    {
        await foreach (var write in _writes.Reader.ReadAllAsync())
        {
            try
            {
                write();
            }
            catch (Exception ex)
            {
                BlurtLog.Error(LogCategory, $"write failed: {ex.GetType().Name}");
            }
        }
    }
}
