using Blurt.Engine.STT;
using FluentAssertions;

namespace Blurt.Engine.Tests.STT;

public sealed class SyncSTTLimitsTests
{
    private static IEnumerable<TestCaseData> PositiveLimits()
    {
        yield return new TestCaseData(SyncSTTLimits.MaxAudioSeconds).SetArgDisplayNames("MaxAudioSeconds");
        yield return new TestCaseData(SyncSTTLimits.AutoReleaseSeconds).SetArgDisplayNames("AutoReleaseSeconds");
        yield return new TestCaseData(SyncSTTLimits.AutoReleaseMargin).SetArgDisplayNames("AutoReleaseMargin");
    }

    [Test]
    public void AutoReleaseIsTheAudioCapMinusTheSafetyMargin()
    {
        SyncSTTLimits.AutoReleaseSeconds.Should().Be(SyncSTTLimits.MaxAudioSeconds - SyncSTTLimits.AutoReleaseMargin);
        SyncSTTLimits.AutoReleaseSeconds.Should().Be(115);
    }

    [Test]
    public void AutoReleaseFiresStrictlyBeforeTheHardAudioCap()
    {
        SyncSTTLimits.AutoReleaseMargin.Should().BePositive();
        SyncSTTLimits.AutoReleaseSeconds.Should().BeLessThan(SyncSTTLimits.MaxAudioSeconds);
    }

    [Test]
    public void MinSamplesIsTheMinimumDurationAtTheCaptureSampleRate()
    {
        SyncSTTLimits.MinSamples.Should().Be((int)(SyncSTTLimits.MinAudioSeconds * SyncSTTLimits.SampleRate));
        SyncSTTLimits.MinSamples.Should().Be(1600);
    }

    [Test]
    public void MinPcmBytesIsMinSamplesInTheCaptured16BitEncoding()
    {
        SyncSTTLimits.BytesPerSample.Should().Be(2);
        SyncSTTLimits.MinPcmBytes.Should().Be(SyncSTTLimits.MinSamples * SyncSTTLimits.BytesPerSample);
        SyncSTTLimits.MinPcmBytes.Should().Be(3200);
    }

    [Test]
    public void TheCaptureGeometryIsMono16Bit()
    {
        SyncSTTLimits.ChannelCount.Should().Be(1);
        SyncSTTLimits.BitDepth.Should().Be(SyncSTTLimits.BytesPerSample * 8);
    }

    [Test]
    public void DurationMsConvertsARawS16LeByteCountToMilliseconds()
    {
        SyncSTTLimits.DurationMs(SyncSTTLimits.MinPcmBytes).Should().Be(100);
        SyncSTTLimits.DurationMs(32_000).Should().Be(1000);
        SyncSTTLimits.DurationMs(0).Should().Be(0);
        SyncSTTLimits.DurationMs(16_000, rate: 8_000).Should().Be(1000);
        SyncSTTLimits.DurationMs(32_000, rate: 0).Should().Be(0);
    }

    [Test]
    public void PcmBytesForSecondsIsTheInverseOfDurationMs()
    {
        SyncSTTLimits.PcmBytesForSeconds(1).Should().Be(32_000);
        SyncSTTLimits.PcmBytesForSeconds(SyncSTTLimits.MinAudioSeconds).Should().Be(SyncSTTLimits.MinPcmBytes);
        SyncSTTLimits.DurationMs(SyncSTTLimits.PcmBytesForSeconds(2.5)).Should().Be(2500);
    }

    [Test]
    public void TheCaptureGeometryIsTheSyncApis16Khz()
    {
        SyncSTTLimits.SampleRate.Should().Be(16_000);
    }

    [Test]
    public void TheMinimumAudioFloorSitsStrictlyInsideTheAcceptedRange()
    {
        SyncSTTLimits.MinAudioSeconds.Should().BePositive();
        SyncSTTLimits.MinAudioSeconds.Should().BeLessThan(SyncSTTLimits.MaxAudioSeconds);
    }

    [TestCaseSource(nameof(PositiveLimits))]
    public void LimitsArePositiveDurations(double value)
    {
        value.Should().BePositive();
    }
}
