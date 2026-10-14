using System.Runtime.InteropServices;
using System.Threading.Channels;
using Blurt.Engine.Audio;
using Blurt.Engine.Diagnostics;
using Blurt.Engine.STT;
using Blurt.Engine.Windows.Audio.Interop;
using static Blurt.Engine.Windows.Audio.Interop.AudioClientConstants;

namespace Blurt.Engine.Windows.Audio;

/// <summary>
/// Captures 16 kHz mono 16-bit PCM from the default capture endpoint through WASAPI shared mode.
/// Every <see cref="StartAsync"/> opens a fresh audio client on its own MTA thread, and returns only
/// once the device delivers a packet above <see cref="MicLiveness.SilenceFloorDb"/>.
/// </summary>
public sealed class WasapiMicCapture : IMicCapture
{
    private const string LogCategory = "MicCapture";

    private static readonly TimeSpan MeterInterval = TimeSpan.FromMilliseconds(50);

    private readonly TimeProvider _time;
    private readonly TimeSpan _livenessTimeout;
    private readonly Channel<float> _levels = Channel.CreateBounded<float>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleWriter = false });

    private readonly Lock _gate = new();
    private Capture? _active;

    /// <summary>Creates a capture that waits up to <see cref="MicLiveness.Timeout"/> for live input.</summary>
    public WasapiMicCapture()
        : this(TimeProvider.System, MicLiveness.Timeout)
    {
    }

    /// <summary>Creates a capture whose liveness wait runs on <paramref name="time"/> for up to <paramref name="livenessTimeout"/>.</summary>
    public WasapiMicCapture(TimeProvider time, TimeSpan livenessTimeout)
    {
        _time = time;
        _livenessTimeout = livenessTimeout;
    }

    /// <summary>
    /// The meter, 0 to 1, about every 50 ms while a confirmed capture runs. Holds only the newest
    /// value and stays open across captures for the life of this instance.
    /// </summary>
    public IAsyncEnumerable<float> Levels => _levels.Reader.ReadAllAsync();

    /// <summary>
    /// Opens the default capture endpoint and returns its feed once a live packet arrives. The feed
    /// includes every packet since the device opened and completes when capture stops.
    /// </summary>
    /// <exception cref="MicCaptureException">Access is denied, no device exists, the device failed to
    /// open, or no live packet arrived within the liveness timeout.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> fired, or a stop landed
    /// before the capture was confirmed live.</exception>
    public async Task<IAsyncEnumerable<byte[]>> StartAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var capture = new Capture(_time, _levels.Writer);
        Capture? previous;
        lock (_gate)
        {
            previous = _active;
            _active = capture;
        }

        if (previous is not null)
        {
            await previous.StopAsync().ConfigureAwait(false);
        }

        var startedAt = _time.GetTimestamp();
        capture.Start();
        try
        {
            await capture.Live.WaitAsync(_livenessTimeout, _time, ct).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            Detach(capture);
            await capture.StopAsync().ConfigureAwait(false);
            if (failure is TimeoutException)
            {
                BlurtLog.Error(
                    LogCategory,
                    $"input liveness unconfirmed after {_livenessTimeout.TotalMilliseconds:F0} ms, peakDb={capture.PeakDbFs:F1}");
                throw MicCaptureException.NotLive();
            }

            BlurtLog.Info(LogCategory, $"start failed: {failure.Message}");
            throw;
        }

        BlurtLog.Info(
            LogCategory,
            $"input live after {_time.GetElapsedTime(startedAt).TotalMilliseconds:F0} ms, peakDb={capture.PeakDbFs:F1}");
        return capture.Frames;
    }

    /// <summary>Stops the capture, waits for its thread to exit, and returns the bytes it produced; 0 when none is running.</summary>
    /// <exception cref="MicCaptureException">The device failed after the capture was confirmed live,
    /// so the recording is truncated. The frame feed completed with the same exception.</exception>
    public async Task<int> StopAsync()
    {
        var capture = Detach(null);
        if (capture is null)
        {
            return 0;
        }

        var bytes = await capture.StopAsync().ConfigureAwait(false);
        if (capture.Failure is { } failure)
        {
            BlurtLog.Error(LogCategory, $"stop after capture failure: {failure.Message}");
            throw failure;
        }

        BlurtLog.Info(
            LogCategory,
            $"stop samples={bytes / SyncSTTLimits.BytesPerSample} durationMs={SyncSTTLimits.DurationMs(bytes)}");
        return bytes;
    }

    /// <summary>Stops the capture and discards its audio, including any capture failure. Never throws a capture failure.</summary>
    public async Task CancelCaptureAsync()
    {
        var capture = Detach(null);
        if (capture is null)
        {
            return;
        }

        await capture.StopAsync().ConfigureAwait(false);
        BlurtLog.Info(LogCategory, "cancelled capture, discarded audio");
    }

    /// <summary>Activates and releases an audio client on the default endpoint without opening a stream. Never throws.</summary>
    public Task WarmUpAsync() => Task.Run(static () =>
    {
        try
        {
            var hr = CaptureEndpoint.ActivateDefault(out var client);
            CaptureEndpoint.Release(client);
            if (hr < 0)
            {
                BlurtLog.Error(LogCategory, $"warm-up failed: 0x{hr:X8}");
            }
            else
            {
                BlurtLog.Info(LogCategory, "warmed the capture stack");
            }
        }
        catch (Exception e)
        {
            BlurtLog.Error(LogCategory, $"warm-up failed: {e.Message}");
        }
    });

    /// <summary>Clears and returns the active capture when it is <paramref name="expected"/>, or any active capture when null.</summary>
    private Capture? Detach(Capture? expected)
    {
        lock (_gate)
        {
            var active = _active;
            if (active is null || (expected is not null && active != expected))
            {
                return null;
            }

            _active = null;
            return active;
        }
    }

    /// <summary>One capture: a dedicated MTA thread owning the audio client for its whole lifetime.</summary>
    private sealed class Capture
    {
        private static readonly TimeSpan WakeInterval = TimeSpan.FromMilliseconds(200);

        private readonly TimeProvider _time;
        private readonly ChannelWriter<float> _levels;
        private readonly Channel<byte[]> _frames = Channel.CreateUnbounded<byte[]>(
            new UnboundedChannelOptions { SingleWriter = true, SingleReader = true });

        private readonly TaskCompletionSource _live = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly AutoResetEvent _packetReady = new(false);
        private readonly Thread _thread;
        private readonly Lock _stopGate = new();
        private Task<int>? _stopped;
        private volatile bool _stopRequested;
        private long _bytesCaptured;
        private double _peakDbFs = double.NegativeInfinity;
        private MicCaptureException? _failure;

        public Capture(TimeProvider time, ChannelWriter<float> levels)
        {
            _time = time;
            _levels = levels;
            _thread = new Thread(Run) { IsBackground = true, Name = "Blurt mic capture" };
        }

        /// <summary>Completes once a live packet arrives; faults with <see cref="MicCaptureException"/> or cancels when the capture ends first.</summary>
        public Task Live => _live.Task;

        /// <summary>The feed of captured packets. Completes normally on stop, or with <see cref="Failure"/> when the device fails.</summary>
        public IAsyncEnumerable<byte[]> Frames => _frames.Reader.ReadAllAsync();

        /// <summary>The failure that ended the capture thread, or null when it ended on request. Read after <see cref="StopAsync"/>.</summary>
        public MicCaptureException? Failure => Volatile.Read(ref _failure);

        /// <summary>The loudest packet level seen before liveness was confirmed.</summary>
        public double PeakDbFs => Volatile.Read(ref _peakDbFs);

        public void Start() => _thread.Start();

        /// <summary>Requests the thread to stop and returns the bytes captured once it has exited. Idempotent.</summary>
        public Task<int> StopAsync()
        {
            lock (_stopGate)
            {
                return _stopped ??= StopCoreAsync();
            }
        }

        private async Task<int> StopCoreAsync()
        {
            _stopRequested = true;
            _packetReady.Set();
            await _exited.Task.ConfigureAwait(false);
            _thread.Join();
            _packetReady.Dispose();
            return (int)Interlocked.Read(ref _bytesCaptured);
        }

        private void Run()
        {
            var coInit = Ole32.CoInitializeEx(IntPtr.Zero, Ole32.CoinitMultithreaded);
            IAudioClient? client = null;
            IAudioCaptureClient? captureClient = null;
            try
            {
                var hr = Open(out client, out captureClient);
                if (hr < 0)
                {
                    Fail(MicCaptureException.FromHResult(hr));
                    return;
                }

                Pump(captureClient!);
                client!.Stop();
            }
            catch (Exception e)
            {
                BlurtLog.Error(LogCategory, $"capture thread failed: {e.Message}");
                Fail(e as MicCaptureException ?? MicCaptureException.FromHResult(e.HResult));
            }
            finally
            {
                CaptureEndpoint.Release(captureClient);
                CaptureEndpoint.Release(client);
                _frames.Writer.TryComplete(Failure);
                _live.TrySetCanceled();
                if (coInit >= 0)
                {
                    Ole32.CoUninitialize();
                }

                _exited.TrySetResult();
            }
        }

        private int Open(out IAudioClient? client, out IAudioCaptureClient? captureClient)
        {
            captureClient = null;
            var hr = CaptureEndpoint.ActivateDefault(out client);
            if (hr < 0)
            {
                return hr;
            }

            var format = WaveFormatEx.CaptureRequest;
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

            hr = client.SetEventHandle(_packetReady.SafeWaitHandle.DangerousGetHandle());
            if (hr < 0)
            {
                return hr;
            }

            var iid = typeof(IAudioCaptureClient).GUID;
            hr = client.GetService(ref iid, out var service);
            if (hr < 0)
            {
                return hr;
            }

            captureClient = (IAudioCaptureClient)service;
            return client.Start();
        }

        private void Pump(IAudioCaptureClient captureClient)
        {
            var blockAlign = WaveFormatEx.CaptureRequest.BlockAlign;
            var lastLevelAt = _time.GetTimestamp();
            byte[]? lastPacket = null;
            while (!_stopRequested)
            {
                _packetReady.WaitOne(WakeInterval);
                while (!_stopRequested)
                {
                    var hr = captureClient.GetNextPacketSize(out var pending);
                    if (hr < 0)
                    {
                        EndOnFailure("GetNextPacketSize", hr);
                        return;
                    }

                    if (pending == 0)
                    {
                        break;
                    }

                    hr = captureClient.GetBuffer(out var data, out var frames, out var flags, out _, out _);
                    if (hr == BufferEmpty)
                    {
                        break;
                    }

                    if (hr < 0)
                    {
                        EndOnFailure("GetBuffer", hr);
                        return;
                    }

                    var packet = new byte[frames * blockAlign];
                    if ((flags & BufferFlagsSilent) == 0 && packet.Length > 0)
                    {
                        Marshal.Copy(data, packet, 0, packet.Length);
                    }

                    hr = captureClient.ReleaseBuffer(frames);
                    if (hr < 0)
                    {
                        EndOnFailure("ReleaseBuffer", hr);
                        return;
                    }

                    Interlocked.Add(ref _bytesCaptured, packet.Length);
                    _frames.Writer.TryWrite(packet);
                    lastPacket = packet;
                    if (!_live.Task.IsCompleted)
                    {
                        ConfirmIfLive(packet);
                    }
                }

                if (lastPacket is not null && _live.Task.IsCompletedSuccessfully && _time.GetElapsedTime(lastLevelAt) >= MeterInterval)
                {
                    _levels.TryWrite(LevelMeter.Normalized(LevelMeter.DbFs(lastPacket)));
                    lastLevelAt = _time.GetTimestamp();
                }
            }
        }

        private void ConfirmIfLive(byte[] packet)
        {
            var db = LevelMeter.DbFs(packet);
            if (db > _peakDbFs)
            {
                Volatile.Write(ref _peakDbFs, db);
            }

            if (MicLiveness.IsLive(packet))
            {
                _live.TrySetResult();
            }
        }

        private void EndOnFailure(string call, int hr)
        {
            BlurtLog.Error(LogCategory, $"{call} failed: 0x{hr:X8}");
            Fail(MicCaptureException.FromHResult(hr));
        }

        /// <summary>
        /// Records the first failure, which fails a pending liveness wait, completes the frame feed
        /// with it, and makes the owner's stop rethrow it.
        /// </summary>
        private void Fail(MicCaptureException failure)
        {
            Interlocked.CompareExchange(ref _failure, failure, null);
            _live.TrySetException(failure);
        }
    }
}
