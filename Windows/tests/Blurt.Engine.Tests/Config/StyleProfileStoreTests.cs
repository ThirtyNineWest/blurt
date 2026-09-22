using System.Text;
using Blurt.Engine.Config;
using FluentAssertions;

namespace Blurt.Engine.Tests.Config;

public sealed class StyleProfileStoreTests
{
    private const int CharacterLimit = 200;

    private static StyleProfileStore MakeStore(ISettingsStore? settings = null) =>
        new(settings ?? new InMemorySettingsStore(), CharacterLimit);

    private static StyleProfileStore MakeLegacyStore(string legacy)
    {
        var settings = new InMemorySettingsStore();
        settings.Set(StyleProfileStore.LegacySettingsKey, legacy);

        return new StyleProfileStore(settings, CharacterLimit);
    }

    [Test]
    public void AnUntouchedInstallHasNoProfilesAndSendsNoInstructions()
    {
        var store = MakeStore();

        store.Profiles.Should().BeEmpty();
        store.Active.Should().BeNull();
        store.ActiveInstructions.Should().BeNull();
    }

    [Test]
    public void ProfilesRoundTripThroughSettingsInOrder()
    {
        var store = MakeStore();
        var written = new[]
        {
            new StyleProfile("Casual", "keep it breezy"),
            new StyleProfile("Formal", "no contractions"),
        };

        store.Profiles = written;

        store.Profiles.Should().Equal(written);
    }

    [Test]
    public void TheLegacyFieldReadsAsOneActiveProfileNamedCustom()
    {
        var store = MakeLegacyStore("  always write in lowercase \n");

        store.Profiles.Should().HaveCount(1);
        var profile = store.Profiles[0];
        profile.Name.Should().Be(StyleProfileStore.LegacyProfileName);
        profile.Id.Should().Be(StyleProfileStore.LegacyProfileId);
        profile.Instructions.Should().Be("always write in lowercase");
        store.Active.Should().Be(profile);
        store.ActiveInstructions.Should().Be("always write in lowercase");
    }

    [TestCase("")]
    [TestCase("   \n ")]
    public void ABlankLegacyFieldMigratesNothing(string legacy)
    {
        MakeLegacyStore(legacy).Profiles.Should().BeEmpty();
    }

    [Test]
    public void AStoredListShadowsTheLegacyFieldEvenWhenEmpty()
    {
        var settings = new InMemorySettingsStore();
        settings.Set(StyleProfileStore.LegacySettingsKey, "always write in lowercase");
        var store = new StyleProfileStore(settings, CharacterLimit);

        store.Profiles = [];

        store.Profiles.Should().BeEmpty();
        store.ActiveInstructions.Should().BeNull();
    }

    [Test]
    public void WritingMoreThanTheLimitKeepsTheFirstProfiles()
    {
        var store = MakeStore();

        store.Profiles = [.. Enumerable.Range(1, StyleProfileStore.ProfileLimit + 3)
            .Select(i => new StyleProfile($"Style {i}", $"instruction {i}"))];

        store.Profiles.Should().HaveCount(StyleProfileStore.ProfileLimit);
        store.Profiles[^1].Name.Should().Be($"Style {StyleProfileStore.ProfileLimit}");
    }

    [Test]
    public void EachProfilesInstructionsAreTrimmedToTheByteBudget()
    {
        var store = MakeStore();
        var oversized = new string('x', CharacterLimit + 100);

        store.Profiles = [new StyleProfile("One", oversized), new StyleProfile("Two", oversized)];

        foreach (var profile in store.Profiles)
        {
            Encoding.UTF8.GetByteCount(profile.Instructions).Should().Be(CharacterLimit);
        }
    }

    [Test]
    public void NamesAreTrimmedToTheNameLimitAndNeverLeftBlank()
    {
        var store = MakeStore();

        store.Profiles =
        [
            new StyleProfile(new string('n', StyleProfileStore.NameLimit + 10), "a"),
            new StyleProfile("   ", "b"),
        ];

        store.Profiles[0].Name.Length.Should().Be(StyleProfileStore.NameLimit);
        store.Profiles[1].Name.Should().Be("Style");
    }

    [Test]
    public void OnlyTheActiveProfilesInstructionsAreReturned()
    {
        var store = MakeStore();
        var profiles = new[]
        {
            new StyleProfile("Casual", "keep it breezy"),
            new StyleProfile("Formal", "no contractions"),
            new StyleProfile("Terse", "cut every adjective"),
        };
        store.Profiles = profiles;

        store.Activate(profiles[1]);

        store.Active.Should().Be(store.Profiles[1]);
        var sent = store.ActiveInstructions;
        sent.Should().Be("no contractions");
        sent.Should().NotContain("keep it breezy");
        sent.Should().NotContain("cut every adjective");
    }

    [Test]
    public void TheActiveChoiceIsStickyAcrossAListEdit()
    {
        var store = MakeStore();
        var profiles = new[]
        {
            new StyleProfile("Casual", "keep it breezy"),
            new StyleProfile("Formal", "no contractions"),
        };
        store.Profiles = profiles;
        store.Activate(profiles[1]);

        store.Profiles = [.. profiles, new StyleProfile("Terse", "cut adjectives")];

        store.ActiveInstructions.Should().Be("no contractions");
    }

    [Test]
    public void DefaultSendsNoInstructionsUntilAnotherStyleIsChosen()
    {
        var store = MakeStore();
        var profiles = new[]
        {
            new StyleProfile("Casual", "keep it breezy"),
            new StyleProfile("Formal", "no contractions"),
        };
        store.Profiles = profiles;
        store.Activate(profiles[1]);

        store.ActivateDefault();

        store.Active.Should().BeNull();
        store.ActiveInstructions.Should().BeNull();

        store.Profiles = [.. profiles, new StyleProfile("Terse", "cut adjectives")];
        store.ActiveInstructions.Should().BeNull();

        store.Activate(profiles[0]);
        store.ActiveInstructions.Should().Be("keep it breezy");
    }

    [Test]
    public void DefaultIsDistinctFromUnsetOnALegacyInstall()
    {
        var store = MakeLegacyStore("always write in lowercase");

        store.ActiveInstructions.Should().Be("always write in lowercase");

        store.ActivateDefault();

        store.ActiveInstructions.Should().BeNull();
    }

    [Test]
    public void AnUnsetOrStaleActivePointerFallsBackToTheFirstProfile()
    {
        var store = MakeStore();
        var profiles = new[]
        {
            new StyleProfile("Casual", "keep it breezy"),
            new StyleProfile("Formal", "no contractions"),
        };
        store.Profiles = profiles;

        store.ActiveInstructions.Should().Be("keep it breezy");

        store.Activate(profiles[1]);
        store.Profiles = [profiles[0]];

        store.ActiveInstructions.Should().Be("keep it breezy");
    }

    [TestCase("")]
    [TestCase("  \n ")]
    public void BlankInstructionsInTheActiveProfileSendNothing(string instructions)
    {
        var store = MakeStore();

        store.Profiles = [new StyleProfile("Empty", instructions)];

        store.ActiveInstructions.Should().BeNull();
    }

    [Test]
    public void AnUndecodableStoredListReadsAsNoProfiles()
    {
        var settings = new InMemorySettingsStore();
        settings.Set(StyleProfileStore.SettingsKey, "not json");
        settings.Set(StyleProfileStore.LegacySettingsKey, "always write in lowercase");

        new StyleProfileStore(settings, CharacterLimit).Profiles.Should().BeEmpty();
    }

    [Test]
    public void TheLegacyProfileIdIsDeterministicAndNeverCollidesWithARandomGuid()
    {
        var again = new Guid(Encoding.ASCII.GetBytes("blurtstylelegacy"));

        StyleProfileStore.LegacyProfileId.Should().Be(again);
        StyleProfileStore.LegacyProfileId.Should().NotBe(Guid.NewGuid());
    }
}
