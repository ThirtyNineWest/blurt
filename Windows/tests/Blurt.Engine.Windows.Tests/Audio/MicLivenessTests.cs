using Blurt.Engine.Windows.Audio;
using FluentAssertions;

namespace Blurt.Engine.Windows.Tests.Audio;

public sealed class MicLivenessTests
{
    [Test]
    public void TheFloorAndTimeoutMatchTheCaptureContract()
    {
        MicLiveness.SilenceFloorDb.Should().Be(-50);
        MicLiveness.Timeout.Should().Be(TimeSpan.FromSeconds(2.5));
    }

    [Test]
    public void DigitalSilenceIsNotLive()
    {
        MicLiveness.IsLive(Pcm.Silence(1600)).Should().BeFalse();
    }

    [Test]
    public void AnEmptyPacketIsNotLive()
    {
        MicLiveness.IsLive([]).Should().BeFalse();
    }

    [Test]
    public void ASignalJustAboveTheFloorIsLive()
    {
        var justAbove = Pcm.Constant(104, 1600);

        var live = MicLiveness.IsLive(justAbove);

        LevelMeter.DbFs(justAbove).Should().BeGreaterThan(MicLiveness.SilenceFloorDb);
        live.Should().BeTrue();
    }

    [Test]
    public void ASignalJustBelowTheFloorIsNotLive()
    {
        var justBelow = Pcm.Constant(103, 1600);

        var live = MicLiveness.IsLive(justBelow);

        LevelMeter.DbFs(justBelow).Should().BeLessThan(MicLiveness.SilenceFloorDb);
        live.Should().BeFalse();
    }

    [Test]
    public void ASpeechLevelSineIsLive()
    {
        MicLiveness.IsLive(Pcm.Sine(0.05)).Should().BeTrue();
    }

    [Test]
    public void ASineQuieterThanTheFloorIsNotLive()
    {
        MicLiveness.IsLive(Pcm.Sine(0.002)).Should().BeFalse();
    }
}
