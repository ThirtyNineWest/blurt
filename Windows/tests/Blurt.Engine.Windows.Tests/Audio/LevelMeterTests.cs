using Blurt.Engine.Windows.Audio;
using FluentAssertions;

namespace Blurt.Engine.Windows.Tests.Audio;

public sealed class LevelMeterTests
{
    [Test]
    public void DigitalSilenceReadsNegativeInfinity()
    {
        var silence = Pcm.Silence(1600);

        var db = LevelMeter.DbFs(silence);

        db.Should().Be(double.NegativeInfinity);
    }

    [Test]
    public void AnEmptyBufferReadsNegativeInfinity()
    {
        var db = LevelMeter.DbFs([]);

        db.Should().Be(double.NegativeInfinity);
    }

    [Test]
    public void AFullScaleSineReadsItsRmsOfMinusThreeDb()
    {
        var sine = Pcm.Sine(1.0);

        var db = LevelMeter.DbFs(sine);

        db.Should().BeApproximately(-3.01, 0.02);
    }

    [Test]
    public void ATenthScaleSineReadsTwentyDbLower()
    {
        var sine = Pcm.Sine(0.1);

        var db = LevelMeter.DbFs(sine);

        db.Should().BeApproximately(-23.01, 0.02);
    }

    [Test]
    public void SamplesAreReadLittleEndianAndSigned()
    {
        byte[] negativeFullScale = [0x00, 0x80, 0x00, 0x80];

        var db = LevelMeter.DbFs(negativeFullScale);

        db.Should().BeApproximately(0, 0.001);
    }

    [Test]
    public void ATrailingOddByteIsIgnored()
    {
        byte[] oneSampleAndAStrayByte = [0x00, 0x80, 0x7F];

        var db = LevelMeter.DbFs(oneSampleAndAStrayByte);

        db.Should().BeApproximately(0, 0.001);
    }

    [Test]
    public void FullScaleNormalizesToOne()
    {
        LevelMeter.Normalized(0).Should().Be(1);
    }

    [TestCase(-50.0)]
    [TestCase(-80.0)]
    [TestCase(double.NegativeInfinity)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void TheFloorAndBelowAndNonFiniteReadingsNormalizeToZero(double dbFs)
    {
        LevelMeter.Normalized(dbFs).Should().Be(0);
    }

    [Test]
    public void AboveFullScaleClampsToOne()
    {
        LevelMeter.Normalized(3).Should().Be(1);
    }

    [Test]
    public void MidScaleMapsLinearlyInDecibels()
    {
        LevelMeter.Normalized(-25).Should().BeApproximately(0.5f, 0.01f);
        LevelMeter.Normalized(-11).Should().BeGreaterThan(LevelMeter.Normalized(-22));
    }

    [Test]
    public void SilenceMetersAsAnEmptyBarAndASineAsAPartialOne()
    {
        var silent = LevelMeter.Normalized(LevelMeter.DbFs(Pcm.Silence(800)));
        var speech = LevelMeter.Normalized(LevelMeter.DbFs(Pcm.Sine(0.1)));

        silent.Should().Be(0);
        speech.Should().BeInRange(0.5f, 0.6f);
    }
}
