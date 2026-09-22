using System.Text.Json.Nodes;
using Blurt.Engine.STT;

namespace Blurt.Engine.Pipeline;

public sealed partial class DictationLog
{
    /// <summary>
    /// Appends a failure with its press-time context when developer mode is on; returns without
    /// effect otherwise. The entry is stamped now and written later off the caller's thread.
    /// </summary>
    public void AppendError(BlurtError error, TranscriptionContext? context)
    {
        ArgumentNullException.ThrowIfNull(error);
        Gated(now => AppendLine(MakeErrorEntry(error, context, now), ErrorsPath));
    }

    /// <summary>
    /// Returns one error entry: <c>kind</c> (the stable <see cref="BlurtError.DiagnosticName"/>),
    /// <c>error</c> (the full description), <c>ts</c>, and the context's <c>app</c>, <c>window</c>,
    /// and <c>field</c> when present, keys in ordinal order. It has no field for prior text, selected
    /// text, history, or the prompt, so the text the user was editing never reaches this log.
    /// </summary>
    public static JsonObject MakeErrorEntry(BlurtError error, TranscriptionContext? context, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(error);
        var fields = new SortedDictionary<string, JsonNode?>(StringComparer.Ordinal)
        {
            ["kind"] = error.DiagnosticName,
            ["error"] = error.Description,
            ["ts"] = FormatTimestamp(now),
        };
        AddIfPresent(fields, "app", context?.AppName);
        AddIfPresent(fields, "window", context?.WindowTitle);
        AddIfPresent(fields, "field", context?.FieldLabel);
        return new JsonObject(fields);
    }
}
