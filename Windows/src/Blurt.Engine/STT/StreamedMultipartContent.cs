using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

namespace Blurt.Engine.STT;

/// <summary>
/// The dictation request body, streamed as the recording happens: the <c>config</c> part, the
/// <c>audio</c> part header, each frame as it arrives, then the closing boundary once the feed
/// ends. Its length is unknown, so the transport uploads it chunked with no <c>Content-Length</c>.
/// <para>
/// The body can be produced once. A recording already streamed to the server has no buffered copy
/// to replay, so a second serialization throws rather than resending an empty audio part.
/// </para>
/// </summary>
internal sealed class StreamedMultipartContent : HttpContent
{
    private readonly byte[] _configHead;
    private readonly byte[] _audioHeader;
    private readonly byte[] _closingBoundary;
    private readonly IAsyncEnumerable<byte[]> _frames;
    private readonly UploadProgress _progress;
    private int _serialized;

    /// <summary>Creates the body for <paramref name="config"/> framed by <paramref name="boundary"/>, streaming <paramref name="frames"/>.</summary>
    public StreamedMultipartContent(byte[] config, string boundary, IAsyncEnumerable<byte[]> frames, UploadProgress progress)
    {
        _configHead = DictationMultipart.ConfigHead(config, boundary);
        _audioHeader = DictationMultipart.AudioPartHeader(boundary);
        _closingBoundary = DictationMultipart.ClosingBoundary(boundary);
        _frames = frames;
        _progress = progress;
        var contentType = new MediaTypeHeaderValue("multipart/form-data");
        contentType.Parameters.Add(new NameValueHeaderValue("boundary", boundary));
        Headers.ContentType = contentType;
    }

    /// <inheritdoc/>
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SerializeToStreamAsync(stream, context, CancellationToken.None);

    /// <summary>
    /// Writes the body, flushing after the head and after each frame so audio moves as it is
    /// captured. Every frame's bytes are counted into the shared <see cref="UploadProgress"/>.
    /// </summary>
    /// <exception cref="AudioTooShortException">
    /// The feed ended below <see cref="SyncSTTLimits.MinPcmBytes"/>. The closing boundary is never
    /// written, so the server never receives a complete request to bill.
    /// </exception>
    /// <exception cref="InvalidOperationException">The body was already serialized once.</exception>
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _serialized, 1) != 0)
        {
            throw new InvalidOperationException("A streamed recording can't be replayed.");
        }

        await stream.WriteAsync(_configHead, cancellationToken);
        await stream.WriteAsync(_audioHeader, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        await foreach (var frame in _frames.WithCancellation(cancellationToken))
        {
            _progress.RecordFrame(frame.Length);
            await stream.WriteAsync(frame, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        if (_progress.AudioBytes < SyncSTTLimits.MinPcmBytes)
        {
            throw new AudioTooShortException();
        }

        await stream.WriteAsync(_closingBoundary, cancellationToken);
    }

    /// <summary>Always false: the body's length is unknown until the recording ends.</summary>
    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}

/// <summary>
/// How much audio the body producer has streamed and when the last frame went out, shared between
/// the producer and the request's log line. Thread-safe.
/// </summary>
internal sealed class UploadProgress
{
    private long _audioBytes;
    private long _lastFrameTimestamp;

    /// <summary>Total audio bytes handed to the upload.</summary>
    public int AudioBytes => (int)Interlocked.Read(ref _audioBytes);

    /// <summary>The <see cref="Stopwatch"/> timestamp of the last frame, or null when none arrived.</summary>
    public long? LastFrameTimestamp => Interlocked.Read(ref _lastFrameTimestamp) is var t && t != 0 ? t : null;

    /// <summary>Accounts one frame of <paramref name="bytes"/>, stamped at the moment it is handed to the upload.</summary>
    public void RecordFrame(int bytes)
    {
        Interlocked.Add(ref _audioBytes, bytes);
        Interlocked.Exchange(ref _lastFrameTimestamp, Stopwatch.GetTimestamp());
    }
}
