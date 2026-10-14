using FluentAssertions;

namespace Blurt.Engine.Tests;

public sealed class HostIdentityTests
{
    private static readonly HostIdentity Acme = new(
        "Acme Voice", "com.acme.voice", "acme-voice", "AcmeVoice", "Acme Voice",
        new Uri("https://api.github.com/repos/acme/voice/releases/latest"));

    [Test]
    public void TheReverseDnsIdentityIsPinned()
    {
        HostIdentity.Blurt.Subsystem.Should().Be("dev.alex.blurt");
    }

    [Test]
    public void TheCredentialTargetIsPinned()
    {
        HostIdentity.Blurt.CredentialTarget.Should().Be("blurt");
    }

    [Test]
    public void TheDebugBuildsCredentialTargetIsPinned()
    {
        HostIdentity.BlurtDev.CredentialTarget.Should().Be("blurt-dev");
    }

    [Test]
    public void TheDebugIdentityDiffersFromTheShippingOneOnlyInTheCredentialTarget()
    {
        HostIdentity.BlurtDev.CredentialTarget.Should().NotBe(HostIdentity.Blurt.CredentialTarget);

        (HostIdentity.BlurtDev with { CredentialTarget = HostIdentity.Blurt.CredentialTarget })
            .Should().Be(HostIdentity.Blurt);
    }

    [Test]
    public void TheShippingIdentityCarriesTheProductNameLogDirectoryAndReleaseUrl()
    {
        HostIdentity.Blurt.ProductName.Should().Be("Blurt");
        HostIdentity.Blurt.SettingsPrefix.Should().Be("Blurt");
        HostIdentity.Blurt.LogDirectoryName.Should().Be("Blurt");
        HostIdentity.Blurt.ReleaseUrl.Should().Be(new Uri("https://api.github.com/repos/AssemblyAI/blurt/releases/latest"));
    }

    [Test]
    public void AnUnconfiguredEngineIsBlurt()
    {
        HostIdentity.Current.Should().Be(HostIdentity.Blurt);
    }

    [Test]
    public void SettingsKeysAreThePrefixPlusTheSuffix()
    {
        HostIdentity.Blurt.SettingsKey("SoundPack").Should().Be("BlurtSoundPack");
        Acme.SettingsKey("SoundPack").Should().Be("AcmeVoiceSoundPack");
    }

    [Test]
    public void ConfigureRejectsNull()
    {
        FluentActions.Invoking(() => HostIdentity.Configure(null!)).Should().Throw<ArgumentNullException>();
    }
}
