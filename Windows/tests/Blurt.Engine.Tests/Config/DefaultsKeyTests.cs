using Blurt.Engine.Config;
using FluentAssertions;

namespace Blurt.Engine.Tests.Config;

public sealed class DefaultsKeyTests
{
    [Test]
    public void RawValuesAreUnique()
    {
        var raws = DefaultsKeys.All.Select(k => k.RawValue()).ToList();

        raws.Should().OnlyHaveUniqueItems();
    }

    [Test]
    public void AllListsEveryCase()
    {
        DefaultsKeys.All.Should().BeEquivalentTo(Enum.GetValues<DefaultsKey>());
    }

    [Test]
    public void RawValuesAreTheOnDiskNames()
    {
        DefaultsKeys.All.Select(k => k.RawValue()).Should().Equal(
            "TriggerKeyCode", "TriggerActivation", "SoundPack", "KeyTerms", "DeveloperMode", "EnhancedTranscripts",
            "CustomStyle", "StyleProfiles", "ActiveStyleProfile", "OverlayCustomOriginX", "OverlayCustomOriginY",
            "LastUpdateCheck");
    }

    [Test]
    public void EveryUserSettingIsWrittenUnderTheHostPrefix()
    {
        foreach (var key in DefaultsKeys.All)
        {
            key.Key(HostIdentity.Blurt).Should().Be("Blurt" + key.RawValue(), $"{key} should be Blurt-prefixed");
        }
    }

    [Test]
    public void AHostsPrefixReplacesBlurtsRatherThanBeingAppendedToIt()
    {
        var acme = new HostIdentity(
            "Acme Voice", "com.acme.voice", "acme-voice", "AcmeVoice", "Acme Voice", HostIdentity.Blurt.ReleaseUrl);

        DefaultsKey.SoundPack.Key(acme).Should().Be("AcmeVoiceSoundPack");
        DefaultsKey.SoundPack.Key(HostIdentity.Blurt).Should().Be("BlurtSoundPack");
    }

    [Test]
    public void TheParameterlessKeyReadsTheCurrentIdentity()
    {
        DefaultsKey.SoundPack.Key().Should().Be(DefaultsKey.SoundPack.Key(HostIdentity.Current));
    }
}
