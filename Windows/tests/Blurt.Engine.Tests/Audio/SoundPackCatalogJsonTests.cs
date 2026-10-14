using System.Text.Json;
using Blurt.Engine.Audio;
using FluentAssertions;

namespace Blurt.Engine.Tests.Audio;

public sealed class SoundPackCatalogJsonTests
{
    private const string Catalog = """
        {
          "defaultVoiceId": "rom1a-1",
          "voices": [
            { "id": "rom1a-0", "label": "Brass 1", "group": "Yamaha DX7 · ROM1A" },
            { "id": "rom1a-1", "label": "Brass 2", "group": "Yamaha DX7 · ROM1A" },
            { "id": "juno-0", "label": "Brass", "group": "Roland Juno-106 · A" }
          ]
        }
        """;

    [Test]
    public void ParsesTheGroupsAndVoicesInFileOrder()
    {
        var catalog = SoundPackCatalogJson.Parse(Catalog);

        catalog.Groups.Should().Equal("Yamaha DX7 · ROM1A", "Roland Juno-106 · A");
        catalog.Voices("Yamaha DX7 · ROM1A").Select(voice => voice.Id).Should().Equal("rom1a-0", "rom1a-1");
        catalog.Voices("Roland Juno-106 · A").Should().Equal(new SoundPack("juno-0", "Brass", "Roland Juno-106 · A"));
    }

    [Test]
    public void TheDefaultVoiceIdResolvesAnUnsetSetting()
    {
        var catalog = SoundPackCatalogJson.Parse(Catalog);

        catalog.FromPersisted(null).Id.Should().Be("rom1a-1");
    }

    [Test]
    public void ADefaultNamingNoVoiceFallsBackToSilence()
    {
        var catalog = SoundPackCatalogJson.Parse("""{ "defaultVoiceId": "missing", "voices": [] }""");

        catalog.FromPersisted(null).Should().Be(SoundPack.None);
    }

    [TestCase("not json")]
    [TestCase("[]")]
    [TestCase("""{ "voices": [] }""")]
    [TestCase("""{ "defaultVoiceId": "a" }""")]
    [TestCase("""{ "defaultVoiceId": "a", "voices": [ { "id": "a", "label": "A" } ] }""")]
    [TestCase("""{ "defaultVoiceId": "a", "voices": [ { "id": 1, "label": "A", "group": "G" } ] }""")]
    [TestCase("""{ "defaultVoiceId": "a", "voices": [ "a" ] }""")]
    public void AMalformedCatalogThrowsJsonException(string json)
    {
        FluentActions.Invoking(() => SoundPackCatalogJson.Parse(json)).Should().Throw<JsonException>();
    }
}
