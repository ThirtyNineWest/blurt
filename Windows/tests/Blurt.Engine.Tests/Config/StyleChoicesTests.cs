using Blurt.Engine.Config;
using FakeItEasy;
using FluentAssertions;

namespace Blurt.Engine.Tests.Config;

public sealed class StyleChoicesTests
{
    private const int CharacterLimit = 200;

    [Test]
    public void EntriesAreDefaultThenEachProfileWithTheActiveOneMarked()
    {
        var (store, _) = MakeStore();
        var casual = new StyleProfile("Casual", "lowercase");
        store.Profiles = [casual];
        store.Activate(store.Profiles[0]);

        var choices = new StyleChoices(store);

        choices.Entries.Select(entry => entry.Name).Should().Equal(StyleProfileStore.DefaultStyleName, "Casual");
        choices.Entries.Select(entry => entry.IsActive).Should().Equal(false, true);
    }

    [Test]
    public void ActivatingBDeactivatesAAndActivatesBExactlyOnce()
    {
        var (store, settings) = MakeStore();
        store.Profiles = [new StyleProfile("A", "a"), new StyleProfile("B", "b")];
        store.Activate(store.Profiles[0]);
        var choices = new StyleChoices(store);
        var a = choices.Entries[1];
        var b = choices.Entries[2];
        Fake.ClearRecordedCalls(settings);

        b.IsActive = true;

        A.CallTo(() => settings.Set(StyleProfileStore.ActiveSettingsKey, A<string>._)).MustHaveHappenedOnceExactly();
        store.Active!.Name.Should().Be("B");
        a.IsActive.Should().BeFalse();
        b.IsActive.Should().BeTrue();
        choices.Entries[0].IsActive.Should().BeFalse();
    }

    [Test]
    public void AFalseWriteChangesNothingAndDoesNotNotify()
    {
        var (store, settings) = MakeStore();
        store.Profiles = [new StyleProfile("A", "a")];
        store.Activate(store.Profiles[0]);
        var choices = new StyleChoices(store);
        var a = choices.Entries[1];
        var notified = 0;
        a.PropertyChanged += (_, _) => notified++;
        Fake.ClearRecordedCalls(settings);

        a.IsActive = false;

        a.IsActive.Should().BeTrue();
        notified.Should().Be(0);
        A.CallTo(() => settings.Set(A<string>._, A<string>._)).MustNotHaveHappened();
    }

    [Test]
    public void ActivatingDefaultStoresTheSentinel()
    {
        var (store, _) = MakeStore();
        store.Profiles = [new StyleProfile("A", "a")];
        var choices = new StyleChoices(store);

        choices.Entries[0].IsActive = true;

        store.Active.Should().BeNull();
        choices.Active.Should().BeNull();
        choices.Entries[1].IsActive.Should().BeFalse();
    }

    [Test]
    public void AChangedProfileListRebuildsTheEntries()
    {
        var (store, _) = MakeStore();
        var choices = new StyleChoices(store);
        store.Profiles = [new StyleProfile("New", "n")];

        choices.Reload();

        choices.Entries.Select(entry => entry.Name).Should().Equal(StyleProfileStore.DefaultStyleName, "New");
    }

    private static (StyleProfileStore Store, ISettingsStore Settings) MakeStore()
    {
        var settings = A.Fake<ISettingsStore>(options => options.Wrapping(new InMemorySettingsStore()));
        return (new StyleProfileStore(settings, CharacterLimit), settings);
    }
}
