using Blurt.Engine.Config;
using FluentAssertions;

namespace Blurt.Engine.Tests.Config;

public sealed class DeveloperModeStoreTests
{
    [Test]
    public void DefaultsToOffWhenUnset()
    {
        var store = new DeveloperModeStore(new InMemorySettingsStore());

        store.IsEnabled.Should().BeFalse();
    }

    [Test]
    public void RoundTripsTheSwitch()
    {
        var settings = new InMemorySettingsStore();
        var store = new DeveloperModeStore(settings);

        store.IsEnabled = true;

        store.IsEnabled.Should().BeTrue();
        new DeveloperModeStore(settings).IsEnabled.Should().BeTrue();
    }

    [Test]
    public void ReadsThroughOnEveryAccess()
    {
        var settings = new InMemorySettingsStore();
        var store = new DeveloperModeStore(settings);

        settings.Set(DeveloperModeStore.SettingsKey, true);
        store.IsEnabled.Should().BeTrue();

        settings.Set(DeveloperModeStore.SettingsKey, false);
        store.IsEnabled.Should().BeFalse();
    }
}
