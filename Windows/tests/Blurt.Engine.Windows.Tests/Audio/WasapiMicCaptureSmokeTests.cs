using System.Buffers.Binary;
using Blurt.Engine.Diagnostics;
using Blurt.Engine.Permissions;
using Blurt.Engine.Windows.Audio;
using Blurt.Engine.Windows.Permissions;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace Blurt.Engine.Windows.Tests.Audio;

[Explicit("Opens the real default microphone.")]
[Category(Categories.Explicit)]
public sealed class WasapiMicCaptureSmokeTests
{
    private Action<string>? _previousSink;

    [SetUp]
    public void RouteLogToTestOutput()
    {
        _previousSink = BlurtLog.Sink;
        BlurtLog.Sink = static line => TestContext.Out.WriteLine(line);
    }

    [TearDown]
    public void RestoreLog() => BlurtLog.Sink = _previousSink;

    [Test]
    public void TheConsentStoreProbeReportsAState()
    {
        var state = MicrophoneAccess.Probe();

        TestContext.Out.WriteLine($"MicrophoneAccess.Probe() = {state}");
        state.Should().BeOneOf(Enum.GetValues<MicrophoneAccessState>());
    }

    [Test]
    public void TheDeviceProbeReportsAState()
    {
        var state = MicrophoneAccess.ProbeDevice();

        TestContext.Out.WriteLine($"MicrophoneAccess.ProbeDevice() = {state}");
        state.Should().BeOneOf(Enum.GetValues<MicrophoneAccessState>());
    }

    [Test]
    public async Task HalfASecondOfCaptureDeliversAudibleFrames()
    {
        var capture = new WasapiMicCapture();
        var nonZeroSeen = false;
        var packets = 0;

        var frames = await capture.StartAsync(CancellationToken.None);
        var reader = Task.Run(async () =>
        {
            await foreach (var packet in frames)
            {
                packets++;
                nonZeroSeen |= HasNonZeroSample(packet);
            }
        });
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        var bytes = await capture.StopAsync();
        await reader.WaitAsync(TimeSpan.FromSeconds(2));

        TestContext.Out.WriteLine($"bytes={bytes} packets={packets} nonZeroSeen={nonZeroSeen}");
        bytes.Should().BeGreaterThanOrEqualTo(16_000 * 2 / 2);
        nonZeroSeen.Should().BeTrue();
    }

    [Test]
    public async Task TheLevelsFeedMovesWhileCapturing()
    {
        var capture = new WasapiMicCapture();
        var levels = new List<float>();
        using var stopReading = new CancellationTokenSource();

        await capture.StartAsync(CancellationToken.None);
        var reader = Task.Run(async () =>
        {
            await foreach (var level in capture.Levels.WithCancellation(stopReading.Token))
            {
                levels.Add(level);
            }
        });
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        await capture.StopAsync();
        stopReading.Cancel();
        await FluentActions.Awaiting(() => reader).Should().ThrowAsync<OperationCanceledException>();

        TestContext.Out.WriteLine($"levels={levels.Count} max={(levels.Count > 0 ? levels.Max() : 0)}");
        levels.Should().NotBeEmpty();
        levels.Should().OnlyContain(l => l >= 0 && l <= 1);
    }

    [Test]
    public async Task ALivenessTimeoutFailsClosedAndLeavesNoCaptureRunning()
    {
        var time = new FakeTimeProvider();
        var capture = new WasapiMicCapture(time, MicLiveness.Timeout);

        var start = capture.StartAsync(CancellationToken.None);
        time.Advance(MicLiveness.Timeout);

        (await FluentActions.Awaiting(() => start).Should().ThrowAsync<MicCaptureException>())
            .Which.Failure.Should().Be(MicCaptureFailure.NotLive);
        (await capture.StopAsync()).Should().Be(0);
    }

    [Test]
    public async Task CancellationDuringTheLivenessWaitThrowsAndLeavesNoCaptureRunning()
    {
        var capture = new WasapiMicCapture(new FakeTimeProvider(), MicLiveness.Timeout);
        using var cts = new CancellationTokenSource();

        var start = capture.StartAsync(cts.Token);
        cts.Cancel();

        await FluentActions.Awaiting(() => start).Should().ThrowAsync<OperationCanceledException>();
        (await capture.StopAsync()).Should().Be(0);
    }

    [Test]
    public async Task WarmUpCompletesWithoutThrowing()
    {
        var capture = new WasapiMicCapture();

        await FluentActions.Awaiting(capture.WarmUpAsync).Should().NotThrowAsync();
    }

    private static bool HasNonZeroSample(byte[] packet)
    {
        for (var i = 0; i + 1 < packet.Length; i += 2)
        {
            if (BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(i)) != 0)
            {
                return true;
            }
        }

        return false;
    }
}
