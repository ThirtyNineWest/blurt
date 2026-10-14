using Blurt.Engine.Windows.Audio;
using FluentAssertions;

namespace Blurt.Engine.Windows.Tests.Audio;

public sealed class MicLivenessTests
{
    [Test]
    public void TheFloorAndTimeoutMatchTheCaptureContract()
    {
        MicLiveness.SilenceFloorDb.Should().Be(-115);
        MicLiveness.Timeout.Should().Be(TimeSpan.FromSeconds(2.5));
    }

    [Test]
    public void TheLivenessFloorSitsFarBelowTheMeterFloor()
    {
        MicLiveness.SilenceFloorDb.Should().BeLessThan(LevelMeter.FloorDb);
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
    public void OneLeastSignificantBitInAShortPacketIsLive()
    {
        var packet = Pcm.OneSampleIn(160, 1);

        var live = MicLiveness.IsLive(packet);

        LevelMeter.DbFs(packet).Should().BeApproximately(-112.3, 0.1);
        live.Should().BeTrue();
    }

    [Test]
    public void OneLeastSignificantBitInALongPacketIsNotLive()
    {
        var packet = Pcm.OneSampleIn(1600, -1);

        var live = MicLiveness.IsLive(packet);

        LevelMeter.DbFs(packet).Should().BeApproximately(-122.3, 0.1);
        live.Should().BeFalse();
    }

    [Test]
    public void AQuietRoomNoiseFloorIsLive()
    {
        var quietRoom = Pcm.Constant(1, 1600);

        var live = MicLiveness.IsLive(quietRoom);

        LevelMeter.DbFs(quietRoom).Should().BeApproximately(-90.3, 0.1);
        live.Should().BeTrue();
    }

    [Test]
    public void ASignalBelowTheMeterFloorIsStillLive()
    {
        var belowMeter = Pcm.Sine(0.002);

        var live = MicLiveness.IsLive(belowMeter);

        LevelMeter.Normalized(LevelMeter.DbFs(belowMeter)).Should().Be(0);
        live.Should().BeTrue();
    }

    [Test]
    public void ASpeechLevelSineIsLive()
    {
        MicLiveness.IsLive(Pcm.Sine(0.05)).Should().BeTrue();
    }
}
