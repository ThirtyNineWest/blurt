using Blurt.Engine.Audio;
using Blurt.Engine.Config;
using FluentAssertions;

namespace Blurt.Engine.Tests.Audio;

public sealed class SoundPackStoreTests
{
    private static readonly SoundPack Brass = new("rom1a-0", "Brass 1", "DX7");
    private static readonly SoundPack Clav = new("rom1a-19", "Clav 1", "DX7");

    private static SoundPackCatalog MakeCatalog() => new([Brass, Clav], "rom1a-0");

    [Test]
    public void DefaultsToTheCatalogsDefaultVoiceWhenUnset()
    {
        var store = new SoundPackStore(new InMemorySettingsStore(), MakeCatalog());

        store.SoundPack.Should().Be(Brass);
    }

    [Test]
    public void PersistsAndReadsBackAChosenPack()
    {
        var settings = new InMemorySettingsStore();

        new SoundPackStore(settings, MakeCatalog()).SoundPack = Clav;

        new SoundPackStore(settings, MakeCatalog()).SoundPack.Should().Be(Clav);
    }

    [Test]
    public void NoneIsAStorableDistinctValue()
    {
        var settings = new InMemorySettingsStore();

        new SoundPackStore(settings, MakeCatalog()).SoundPack = SoundPack.None;

        new SoundPackStore(settings, MakeCatalog()).SoundPack.Should().Be(SoundPack.None);
    }

    [Test]
    public void AnUnknownStoredValueFallsBackToTheDefault()
    {
        var settings = new InMemorySettingsStore();
        settings.Set(SoundPackStore.SettingsKey, "trombone");

        new SoundPackStore(settings, MakeCatalog()).SoundPack.Should().Be(Brass);
    }

    [Test]
    public void UsesTheRosterKey()
    {
        SoundPackStore.SettingsKey.Should().Be(DefaultsKey.SoundPack.Key());
    }
}
