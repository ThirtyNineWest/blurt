using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blurt.Engine.STT;

/// <summary>
/// The dictation request's <c>config</c> part. The route validates its config strictly and
/// accepts more names than the reference documents, so a 200 is no evidence a name is supported:
/// every key written here is one the reference documents, and nothing else is sent.
/// </summary>
/// <param name="SampleRate">The audio's sample rate, <c>sample_rate</c>.</param>
/// <param name="Channels">The audio's channel count, <c>channels</c>.</param>
/// <param name="SttPrompt">
/// The prior text from <see cref="STTPrompt"/>, <c>stt_prompt</c>. Always a string (the route
/// rejects an array). Empty omits the key. Its alias <c>prompt</c> is never sent beside it.
/// </param>
/// <param name="KeytermsPrompt">
/// The fitted key terms from <see cref="KeytermsBoost"/>, <c>keyterms_prompt</c>. Always an
/// array. Empty omits the key. Its aliases <c>keyterms</c> and <c>word_boost</c> are never sent.
/// </param>
/// <param name="LlmInstruction">
/// The cleanup instruction, <c>llm_instruction</c>. Null omits the key, which selects the
/// service's default cleanup, not "no rewrite": the route rewrites by default and documents no off
/// switch, so the enhanced-transcripts setting is applied to the response instead.
/// </param>
internal sealed record DictationConfig(
    int SampleRate,
    int Channels,
    string SttPrompt,
    IReadOnlyList<string> KeytermsPrompt,
    string? LlmInstruction)
{
    /// <summary>
    /// Returns the config as UTF-8 JSON, keys in declaration order with the audio geometry first.
    /// A property added to the record and not written here never reaches the wire.
    /// </summary>
    public byte[] Encode()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("sample_rate", SampleRate);
            writer.WriteNumber("channels", Channels);
            if (SttPrompt.Length > 0)
            {
                writer.WriteString("stt_prompt", SttPrompt);
            }

            if (KeytermsPrompt.Count > 0)
            {
                writer.WriteStartArray("keyterms_prompt");
                foreach (var term in KeytermsPrompt)
                {
                    writer.WriteStringValue(term);
                }

                writer.WriteEndArray();
            }

            if (LlmInstruction is not null)
            {
                writer.WriteString("llm_instruction", LlmInstruction);
            }

            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }
}

/// <summary>
/// A dictation success body. Every field but <see cref="Text"/> is optional even where the
/// reference marks it required: a field the service stops sending costs a diagnostic, never the
/// dictation. <see cref="Text"/> is the only field whose absence leaves nothing to paste.
/// </summary>
internal sealed class DictationResponse
{
    private static readonly JsonSerializerOptions s_options = new() { RespectNullableAnnotations = true };

    /// <summary>The verbatim transcript, never altered by the rewrite.</summary>
    [JsonPropertyName("text")]
    [JsonRequired]
    public required string Text { get; init; }

    /// <summary>The rewritten transcript, or null when the rewrite failed.</summary>
    [JsonPropertyName("llm_response")]
    public string? LlmResponse { get; init; }

    /// <summary><c>"timeout"</c> or <c>"error"</c> when the rewrite failed.</summary>
    [JsonPropertyName("llm_error")]
    public string? LlmError { get; init; }

    /// <summary>The request identifier the reference asks callers to quote when reporting a problem.</summary>
    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    /// <summary>
    /// The service's duration for the audio it received. Disagreeing with the client's byte count
    /// means the upload was truncated.
    /// </summary>
    [JsonPropertyName("audio_duration_ms")]
    public double? AudioDurationMs { get; init; }

    /// <summary>Total server-side processing time.</summary>
    [JsonPropertyName("request_time_ms")]
    public double? RequestTimeMs { get; init; }

    /// <summary>The transcription portion of <see cref="RequestTimeMs"/>; the remainder is roughly the rewrite.</summary>
    [JsonPropertyName("sync_time_ms")]
    public double? SyncTimeMs { get; init; }

    /// <summary>Decodes <paramref name="body"/>, or returns null when it is not a response carrying a string <c>text</c>.</summary>
    public static DictationResponse? Decode(byte[] body)
    {
        try
        {
            return JsonSerializer.Deserialize<DictationResponse>(body, s_options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Reads a dictation failure body. The reference documents exactly two shapes: <c>{error,
/// error_code}</c> for most failures and <c>{status, title, detail}</c> for failures relayed from
/// the transcription service. Only those two keys are read; a non-string value is ignored.
/// </summary>
internal static class DictationErrorResponse
{
    /// <summary>Returns the string <c>error</c>, else the string <c>detail</c>, else null (including for a body that is not a JSON object).</summary>
    public static string? Message(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return StringProperty(document.RootElement, "error") ?? StringProperty(document.RootElement, "detail");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? StringProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
