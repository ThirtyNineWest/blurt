using Blurt.Engine.Audio;
using FluentAssertions;

namespace Blurt.Engine.Tests.Audio;

public sealed class SoundPackTests
{
    [Test]
    public void NonePlaysNothingAndIsLabelledNone()
    {
        SoundPack.None.Label.Should().Be("None");
        SoundPack.None.Group.Should().BeEmpty();

        SoundPack.None.StartFileName.Should().BeNull();
        SoundPack.None.StopFileName.Should().BeNull();
    }

    [Test]
    public void AVoiceExposesItsNameAndCueStems()
    {
        var harp = new SoundPack("rom1b-28", "Harp 1", "Yamaha DX7 · ROM1B");

        harp.Label.Should().Be("Harp 1");
        harp.StartFileName.Should().Be("rom1b-28-start");
        harp.StopFileName.Should().Be("rom1b-28-stop");
        harp.IsSilent.Should().BeFalse();
    }
}

/// <summary>
/// Exercises the catalog machinery against small fixtures rather than a real host catalog, since the
/// engine ships no voices of its own.
/// </summary>
public sealed class SoundPackCatalogTests
{
    private static readonly SoundPack Brass = new("rom1a-0", "Brass 1", "DX7");
    private static readonly SoundPack Strings = new("rom1a-3", "Strings 1", "DX7");
    private static readonly SoundPack Juno = new("juno-0", "Brass", "Juno");

    private static SoundPackCatalog MakeCatalog() => new([Brass, Strings, Juno], "rom1a-3");

    [Test]
    public void GroupsAreTheSectionsInCatalogOrderDeduped()
    {
        var catalog = MakeCatalog();

        catalog.Groups.Should().Equal("DX7", "Juno");
        catalog.Voices("DX7").Should().Equal(Brass, Strings);
        catalog.Voices("Juno").Should().Equal(Juno);
        catalog.Voices("Moog").Should().BeEmpty();
    }

    [Test]
    public void AnUnsetOrUnknownPersistedIdFallsBackToTheDefaultVoice()
    {
        var catalog = MakeCatalog();

        catalog.FromPersisted(null).Should().Be(Strings);
        catalog.FromPersisted("").Should().Be(Strings);
        catalog.FromPersisted("trombone").Should().Be(Strings);
        catalog.FromPersisted("juno-0").Should().Be(Juno);
        catalog.FromPersisted("none").Should().Be(SoundPack.None);
    }

    [Test]
    public void ADefaultIdNamingNoVoiceSilencesTheCuesInsteadOfTrapping()
    {
        var broken = new SoundPackCatalog([Brass], "rom1a-6");

        broken.FromPersisted(null).Should().Be(SoundPack.None);
        new SoundPackCatalog([], "rom1a-6").Groups.Should().BeEmpty();
    }

    [Test]
    public void AVoiceClaimingTheReservedNoneIdIsDropped()
    {
        var impostor = new SoundPack("none", "Impostor", "DX7");

        var catalog = new SoundPackCatalog([impostor], "none");

        catalog.Groups.Should().BeEmpty();
        catalog.Voices("DX7").Should().BeEmpty();
        catalog.FromPersisted("none").Should().Be(SoundPack.None);
    }
}
