using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Blurt.Engine.Diagnostics;
using Blurt.Engine.TTS;
using Blurt.Engine.Windows.Audio.Interop;
using static Blurt.Engine.Windows.Audio.Interop.AudioClientConstants;

namespace Blurt.Engine.Windows.Audio;

/// <summary>
/// Plays 16-bit little-endian mono PCM through the default render endpoint in WASAPI shared mode, at
/// <c>rate</c> times natural pace with the pitch held. The first <see cref="Enqueue"/> starts a dedicated
/// MTA thread that owns the audio client for its whole lifetime. A device that fails to open or fails
/// mid-stream is logged under <c>PcmPlayer</c> and ends playback; no member throws a device failure.
/// One player serves one read.
/// </summary>
public sealed class WasapiPcmPlayer : IPcmSink
{
    private const string LogCategory = "PcmPlayer";
    private const int BytesPerFrame = 2;

    /// <summary>
    /// Zero samples fed through the stretcher before its flush: one frame plus the search range, so the
    /// input the stretcher keeps back is padding rather than the end of the read.
    /// </summary>
    private const int StretchTailPaddingBytes = (480 + 120) * BytesPerFrame;

    private static readonly TimeSpan DrainSlack = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan WakeInterval = TimeSpan.FromMilliseconds(200);

    private readonly int _sampleRate;
    private readonly PcmTimeStretch _stretch;
    private readonly BlockingCollection<byte[]> _queue = [];
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _gate = new();
    private Thread? _thread;
    private volatile bool _stopped;
    private bool _draining;
    private long _queuedBytes;

    /// <summary>Creates a player for PCM at <paramref name="sampleRate"/> Hz played at <paramref name="rate"/> times natural pace.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate"/> is not positive, or <paramref name="rate"/> is not a positive finite number.</exception>
    public WasapiPcmPlayer(int sampleRate, double rate = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        _sampleRate = sampleRate;

        // Rate 1 is a passthrough that also joins a sample split across chunks, so the render thread only
        // ever sees whole samples.
        _stretch = new PcmTimeStretch(rate);
    }

    /// <summary>
    /// Queues <paramref name="pcm"/> to play after everything already queued, starting playback on the first
    /// call. Does nothing once <see cref="Stop"/> or <see cref="DrainAsync"/> has been called. Does not block on
    /// the device.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="pcm"/> is null.</exception>
    public void Enqueue(byte[] pcm)
    {
        ArgumentNullException.ThrowIfNull(pcm);
        lock (_gate)
        {
            if (_stopped || _draining)
            {
                return;
            }

            Add(_stretch.Process(pcm));
            if (_thread is null)
            {
                _thread = new Thread(Run) { IsBackground = true, Name = "Blurt PCM playback" };
                _thread.Start();
            }
        }
    }

    /// <summary>
    /// Ends the input, queues the stretcher's tail, and completes once everything queued has played, the device
    /// failed, or <see cref="Stop"/> was called. The wait is bounded by the queued audio's duration plus 2 s.
    /// Always ends with <see cref="Stop"/>, so on return playback is silenced and the render thread is exiting.
    /// Completes at once when nothing was enqueued or no render device could be opened.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled before playback finished.</exception>
    public async Task DrainAsync(CancellationToken ct)
    {
        Thread? thread;
        lock (_gate)
        {
            if (!_stopped && !_draining)
            {
                _draining = true;
                if (_thread is not null && _stretch.Rate != 1)
                {
                    Add(_stretch.Process(new byte[StretchTailPaddingBytes]));
                    Add(_stretch.Flush());
                }

                _queue.CompleteAdding();
            }

            thread = _thread;
        }

        try
        {
            if (thread is not null)
            {
                var queuedSeconds = Interlocked.Read(ref _queuedBytes) / (double)(_sampleRate * BytesPerFrame);
                await _exited.Task.WaitAsync(TimeSpan.FromSeconds(queuedSeconds) + DrainSlack, ct).ConfigureAwait(false);
            }
        }
        catch (TimeoutException)
        {
            BlurtLog.Warn(LogCategory, "drain timed out; stopping playback");
        }
        finally
        {
            Stop();
        }
    }

    /// <summary>
    /// Silences playback and discards what is queued: the render thread stops and resets the audio client on its
    /// next wake, at most 200 ms later, and exits. Idempotent; never blocks.
    /// </summary>
    public void Stop()
    {
        lock (_gate)
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
            if (!_queue.IsAddingCompleted)
            {
                _queue.CompleteAdding();
            }
        }
    }

    private void Add(byte[] pcm)
    {
        if (pcm.Length == 0)
        {
            return;
        }

        Interlocked.Add(ref _queuedBytes, pcm.Length);
        _queue.Add(pcm);
    }

    private void Run()
    {
        var coInit = Ole32.CoInitializeEx(IntPtr.Zero, Ole32.CoinitMultithreaded);
        IAudioClient? client = null;
        IAudioRenderClient? renderClient = null;
        try
        {
            using var ready = new AutoResetEvent(false);
            var hr = Open(ready, out client, out renderClient, out var bufferFrames);
            if (hr < 0)
            {
                BlurtLog.Error(LogCategory, $"render device failed to open: 0x{hr:X8}");
                return;
            }

            Pump(client!, renderClient!, bufferFrames, ready);
        }
        catch (Exception e)
        {
            BlurtLog.Error(LogCategory, $"playback thread failed: {e.Message}");
        }
        finally
        {
            if (client is not null)
            {
                _ = client.Stop();
                _ = client.Reset();
            }

            CaptureEndpoint.Release(renderClient);
            CaptureEndpoint.Release(client);
            Stop();
            if (coInit >= 0)
            {
                Ole32.CoUninitialize();
            }

            _exited.TrySetResult();
        }
    }

    private int Open(AutoResetEvent ready, out IAudioClient? client, out IAudioRenderClient? renderClient, out uint bufferFrames)
    {
        renderClient = null;
        bufferFrames = 0;
        var hr = RenderEndpoint.ActivateDefault(out client);
        if (hr < 0)
        {
            return hr;
        }

        var format = WaveFormatEx.RenderRequest(_sampleRate);
        var session = Guid.Empty;
        hr = client!.Initialize(
            ShareModeShared,
            StreamFlagsEventCallback | StreamFlagsAutoConvertPcm | StreamFlagsSrcDefaultQuality,
            BufferDuration100Ms,
            0,
            ref format,
            ref session);
        if (hr < 0)
        {
            return hr;
        }

        hr = client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle());
        if (hr < 0)
        {
            return hr;
        }

        hr = client.GetBufferSize(out bufferFrames);
        if (hr < 0)
        {
            return hr;
        }

        var iid = typeof(IAudioRenderClient).GUID;
        hr = client.GetService(ref iid, out var service);
        if (hr < 0)
        {
            return hr;
        }

        renderClient = (IAudioRenderClient)service;
        return 0;
    }

    /// <summary>Fills the device buffer on every wake until stopped, or until the input has ended and the device has played all of it.</summary>
    private void Pump(IAudioClient client, IAudioRenderClient renderClient, uint bufferFrames, AutoResetEvent ready)
    {
        byte[]? chunk = null;
        var offset = 0;
        var started = false;
        while (!_stopped)
        {
            var hr = client.GetCurrentPadding(out var padding);
            if (hr < 0)
            {
                BlurtLog.Error(LogCategory, $"GetCurrentPadding failed: 0x{hr:X8}");
                return;
            }

            var chunkDone = chunk is null || offset == chunk.Length;
            if (chunkDone && _queue.IsCompleted && padding == 0)
            {
                return;
            }

            var free = bufferFrames - padding;
            if (free > 0 && !Fill(renderClient, free, ref chunk, ref offset))
            {
                return;
            }

            if (!started)
            {
                hr = client.Start();
                if (hr < 0)
                {
                    BlurtLog.Error(LogCategory, $"Start failed: 0x{hr:X8}");
                    return;
                }

                started = true;
            }

            ready.WaitOne(WakeInterval);
        }
    }

    /// <summary>
    /// Writes up to <paramref name="free"/> frames from the queue, carrying a partly written chunk across calls.
    /// Pads the rest with silence while more input may come; once the input has ended, writes only what is left.
    /// Returns false when the device failed.
    /// </summary>
    private unsafe bool Fill(IAudioRenderClient renderClient, uint free, ref byte[]? chunk, ref int offset)
    {
        var hr = renderClient.GetBuffer(free, out var data);
        if (hr < 0)
        {
            BlurtLog.Error(LogCategory, $"GetBuffer failed: 0x{hr:X8}");
            return false;
        }

        var capacity = (int)free * BytesPerFrame;
        var written = 0;
        while (written < capacity)
        {
            if (chunk is null || offset == chunk.Length)
            {
                if (!_queue.TryTake(out chunk))
                {
                    chunk = null;
                    break;
                }

                offset = 0;
            }

            var count = Math.Min(capacity - written, chunk.Length - offset);
            Marshal.Copy(chunk, offset, data + written, count);
            offset += count;
            written += count;
            Interlocked.Add(ref _queuedBytes, -count);
        }

        uint frames;
        uint flags = 0;
        if (written == capacity || _queue.IsCompleted)
        {
            frames = (uint)(written / BytesPerFrame);
        }
        else if (written == 0)
        {
            frames = free;
            flags = BufferFlagsSilent;
        }
        else
        {
            new Span<byte>((void*)(data + written), capacity - written).Clear();
            frames = free;
        }

        hr = renderClient.ReleaseBuffer(frames, flags);
        if (hr < 0)
        {
            BlurtLog.Error(LogCategory, $"ReleaseBuffer failed: 0x{hr:X8}");
            return false;
        }

        return true;
    }
}
