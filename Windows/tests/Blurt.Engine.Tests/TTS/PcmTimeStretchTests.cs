using Blurt.Engine.TTS;
using FluentAssertions;

namespace Blurt.Engine.Tests.TTS;

public sealed class PcmTimeStretchTests
{
    private const int SampleRate = 24_000;

    [Test]
    public void RateOneIsAByteIdenticalPassthrough()
    {
        var stretch = new PcmTimeStretch(rate: 1);
        var input = Tone(440, seconds: 0.5);

        stretch.Process(input).Should().Equal(input);
        stretch.Flush().Should().BeEmpty();
    }

    [Test]
    public void RateTwoHalvesTheDurationWithinOneFrame()
    {
        var stretch = new PcmTimeStretch(rate: 2);
        var input = Tone(440, seconds: 2);

        var output = Concat(stretch.Process(input), stretch.Flush());

        output.Length.Should().BeCloseTo(input.Length / 2, delta: 480 * 2 * 2);
    }

    [Test]
    public void RateTwoKeepsThePitch()
    {
        var stretch = new PcmTimeStretch(rate: 2);
        var input = Tone(440, seconds: 2);

        var output = Concat(stretch.Process(input), stretch.Flush());

        ZeroCrossingsPerSecond(output).Should().BeInRange(440 * 0.9, 440 * 1.1);
    }

    [Test]
    public void ChunkedInputProducesTheSameOutputAsOneShot()
    {
        var input = Tone(300, seconds: 1.5);
        var oneShot = new PcmTimeStretch(rate: 1.5);
        var chunked = new PcmTimeStretch(rate: 1.5);

        var whole = Concat(oneShot.Process(input), oneShot.Flush());
        var pieces = new List<byte[]>();
        foreach (var chunk in input.Chunk(1_001))
        {
            pieces.Add(chunked.Process(chunk));
        }

        pieces.Add(chunked.Flush());

        Concat(pieces.ToArray()).Should().Equal(whole);
    }

    [Test]
    public void RateOneReassemblesSamplesSplitAcrossChunks()
    {
        var stretch = new PcmTimeStretch(rate: 1);
        var input = Tone(440, seconds: 0.5);

        var pieces = input.Chunk(1_001).Select(stretch.Process).ToList();
        pieces.Add(stretch.Flush());

        Concat(pieces.ToArray()).Should().Equal(input);
    }

    [Test]
    public void SilenceStaysSilent()
    {
        var stretch = new PcmTimeStretch(rate: 2);

        var output = Concat(stretch.Process(new byte[24_000 * 2]), stretch.Flush());

        output.Should().OnlyContain(b => b == 0);
    }

    [Test]
    public void ARateThatIsNotPositiveAndFiniteIsRefused()
    {
        FluentActions.Invoking(() => new PcmTimeStretch(rate: 0)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new PcmTimeStretch(rate: -1)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new PcmTimeStretch(rate: double.NaN)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new PcmTimeStretch(rate: double.PositiveInfinity)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void ABadGeometryNamesTheOffendingParameter()
    {
        FluentActions.Invoking(() => new PcmTimeStretch(rate: 2, frameSize: 480, synthesisHop: 300))
            .Should().Throw<ArgumentOutOfRangeException>().WithParameterName("synthesisHop");
        FluentActions.Invoking(() => new PcmTimeStretch(rate: 2, searchRange: 241))
            .Should().Throw<ArgumentOutOfRangeException>().WithParameterName("searchRange");
    }

    private static byte[] Tone(double hz, double seconds)
    {
        var samples = (int)(SampleRate * seconds);
        var bytes = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
        {
            var value = (short)(Math.Sin(2 * Math.PI * hz * i / SampleRate) * 12_000);
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 2), value);
        }

        return bytes;
    }

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    private static double ZeroCrossingsPerSecond(byte[] pcm)
    {
        var samples = pcm.Length / 2;
        var crossings = 0;
        var previous = BitConverter.ToInt16(pcm, 0);
        for (var i = 1; i < samples; i++)
        {
            var current = BitConverter.ToInt16(pcm, i * 2);
            if ((previous < 0 && current >= 0) || (previous >= 0 && current < 0))
            {
                crossings++;
            }

            previous = current;
        }

        // Two crossings per cycle.
        return crossings / 2.0 / (samples / (double)SampleRate);
    }
}
