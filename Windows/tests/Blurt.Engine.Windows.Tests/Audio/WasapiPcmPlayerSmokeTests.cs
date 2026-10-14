using System.Buffers.Binary;
using System.Diagnostics;
using Blurt.Engine.Diagnostics;
using Blurt.Engine.Windows.Audio;
using FluentAssertions;

namespace Blurt.Engine.Windows.Tests.Audio;

[Explicit("Plays a short tone through the real default output.")]
[Category(Categories.Explicit)]
public sealed class WasapiPcmPlayerSmokeTests
{
    private const int SampleRate = 24_000;

    private Action<string>? _previousSink;

    [SetUp]
    public void RouteLogToTestOutput()
    {
        _previousSink = BlurtLog.Sink;
        BlurtLog.Sink = static line => TestContext.Out.WriteLine(line);
    }

    [TearDown]
    public void RestoreLog() => BlurtLog.Sink = _previousSink;

    [TestCase(1.0, 3)]
    [TestCase(2.0, 2)]
    public async Task HalfASecondToneDrainsInTime(double rate, int limitSeconds)
    {
        var player = new WasapiPcmPlayer(SampleRate, rate);
        var tone = Tone(440, TimeSpan.FromSeconds(0.5));
        var clock = Stopwatch.StartNew();

        for (var start = 0; start < tone.Length; start += 4_800)
        {
            player.Enqueue(tone[start..Math.Min(tone.Length, start + 4_800)]);
        }

        await player.DrainAsync(CancellationToken.None);

        TestContext.Out.WriteLine($"rate {rate}: drained in {clock.ElapsedMilliseconds} ms");
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(limitSeconds));
    }

    private static byte[] Tone(double hertz, TimeSpan duration)
    {
        var samples = (int)(SampleRate * duration.TotalSeconds);
        var pcm = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
        {
            var value = (short)(3000 * Math.Sin(2 * Math.PI * hertz * i / SampleRate));
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2), value);
        }

        return pcm;
    }
}
