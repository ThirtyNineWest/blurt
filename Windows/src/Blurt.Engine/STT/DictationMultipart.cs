using System.Text;

namespace Blurt.Engine.STT;

/// <summary>
/// The dictation upload's multipart envelope: boundaries, part headers, the <c>audio.pcm</c>
/// filename, and CRLF placement, stated once. On the wire the order is <see cref="ConfigHead"/>,
/// <see cref="AudioPartHeader"/>, the raw PCM frames, then <see cref="ClosingBoundary"/>. The
/// streaming route rejects a body whose audio part precedes its config part.
/// </summary>
internal static class DictationMultipart
{
    /// <summary>
    /// The whole <c>config</c> part that opens the body: its boundary, part headers, the JSON
    /// <paramref name="config"/>, and the CRLF terminating the part.
    /// </summary>
    public static byte[] ConfigHead(byte[] config, string boundary) =>
        [
            .. Framed(
                $"--{boundary}\r\n",
                "Content-Disposition: form-data; name=\"config\"\r\n",
                "Content-Type: application/json\r\n\r\n"),
            .. config,
            .. Framed("\r\n"),
        ];

    /// <summary>
    /// The <c>audio</c> part's framing, everything before the PCM bytes. The part is not terminated
    /// here: the frames follow with no framing of their own, and its CRLF opens <see cref="ClosingBoundary"/>.
    /// </summary>
    public static byte[] AudioPartHeader(string boundary) =>
        Framed(
            $"--{boundary}\r\n",
            "Content-Disposition: form-data; name=\"audio\"; filename=\"audio.pcm\"\r\n",
            "Content-Type: audio/pcm\r\n\r\n");

    /// <summary>Everything after the last audio frame: the audio part's terminating CRLF and the closing boundary.</summary>
    public static byte[] ClosingBoundary(string boundary) => Framed("\r\n", $"--{boundary}--\r\n");

    private static byte[] Framed(params string[] parts) => Encoding.UTF8.GetBytes(string.Concat(parts));
}
