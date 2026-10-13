using Blurt.Engine.Config;
using FluentAssertions;

namespace Blurt.Engine.Tests.Config;

public sealed class TextShortcutStoreTests
{
    [Test]
    public void AnUntouchedInstallHasNoShortcuts()
    {
        new TextShortcutStore(new InMemorySettingsStore()).Shortcuts.Should().BeEmpty();
    }

    [Test]
    public void ShortcutsRoundTripInOrder()
    {
        var store = new TextShortcutStore(new InMemorySettingsStore());
        var written = new List<TextShortcut>
        {
            new("personal email", "me@example.com"),
            new("personal github", "https://github.com/me"),
        };

        store.Shortcuts = written;

        store.Shortcuts.Should().Equal(written);
    }

    [Test]
    public void NormalizationTrimsDropsBlanksAndDedupesCaseInsensitively()
    {
        var store = new TextShortcutStore(new InMemorySettingsStore());

        store.Shortcuts =
        [
            new("  personal email ", " me@example.com\n"),
            new("Personal Email", "other@example.com"),
            new("   ", "orphan"),
            new("empty", "  "),
        ];

        store.Shortcuts.Select(s => s.Trigger).Should().Equal("personal email");
        store.Shortcuts.Select(s => s.Expansion).Should().Equal("me@example.com");
    }

    [Test]
    public void DedupeTreatsTriggersThatDifferOnlyInSeparatorsAsOnePhrase()
    {
        var store = new TextShortcutStore(new InMemorySettingsStore());

        store.Shortcuts =
        [
            new("personal email", "a@example.com"),
            new("Personal-Email", "b@example.com"),
            new("personalemail", "c@example.com"),
            new("...", "unmatchable"),
        ];

        store.Shortcuts.Select(s => s.Expansion).Should().Equal("a@example.com");
        TextShortcutStore.MatchKey("Cal.com").Should().Be(TextShortcutStore.MatchKey("cal com"));
    }

    [Test]
    public void CapsAreAppliedOnTheWayIn()
    {
        var store = new TextShortcutStore(new InMemorySettingsStore());

        store.Shortcuts = [new(new string('a', 70), new string('b', 2500))];

        store.Shortcuts.Single().Trigger.Should().HaveLength(TextShortcutStore.TriggerLimit);
        store.Shortcuts.Single().Expansion.Should().HaveLength(TextShortcutStore.ExpansionLimit);
    }

    [Test]
    public void AtMostShortcutLimitEntriesAreKept()
    {
        var store = new TextShortcutStore(new InMemorySettingsStore());

        store.Shortcuts = Enumerable.Range(0, TextShortcutStore.ShortcutLimit + 5).Select(i => new TextShortcut($"phrase {i}", "x")).ToList();

        store.Shortcuts.Should().HaveCount(TextShortcutStore.ShortcutLimit);
    }

    [Test]
    public void AnUndecodableSlotReadsAsNoShortcuts()
    {
        var settings = new InMemorySettingsStore();
        settings.Set(TextShortcutStore.SettingsKey, "not json");

        new TextShortcutStore(settings).Shortcuts.Should().BeEmpty();
    }

    [Test]
    public void AnExpansionCutInsideAnAstralCharacterIsStoredWithoutIt()
    {
        var store = new TextShortcutStore(new InMemorySettingsStore());

        var act = () => store.Shortcuts = [new("x", new string('b', TextShortcutStore.ExpansionLimit - 1) + "😀")];

        act.Should().NotThrow();
        store.Shortcuts.Single().Expansion.Should().HaveLength(TextShortcutStore.ExpansionLimit - 1);
    }

    [Test]
    public void AMatchKeyKeepsNumerals()
    {
        TextShortcutStore.MatchKey("²").Should().NotBeEmpty();
    }

    [Test]
    public void TheStoreWritesUnderItsRosterKey()
    {
        TextShortcutStore.SettingsKey.Should().Be(DefaultsKey.TextShortcuts.Key());
    }
}
