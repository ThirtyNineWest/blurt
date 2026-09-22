using Blurt.Engine.Config;
using FluentAssertions;

namespace Blurt.Engine.Tests.Config;

public sealed class EnhancedTranscriptsStoreTests
{
    [Test]
    public void DefaultsToOnWhenUnset()
    {
        var store = new EnhancedTranscriptsStore(new InMemorySettingsStore());

        store.IsEnabled.Should().BeTrue();
    }

    [Test]
    public void RoundTripsTheSwitch()
    {
        var settings = new InMemorySettingsStore();
        var store = new EnhancedTranscriptsStore(settings);

        store.IsEnabled = false;

        store.IsEnabled.Should().BeFalse();
        new EnhancedTranscriptsStore(settings).IsEnabled.Should().BeFalse();
    }

    [Test]
    public void ReadsThroughOnEveryAccess()
    {
        var settings = new InMemorySettingsStore();
        var store = new EnhancedTranscriptsStore(settings);

        settings.Set(EnhancedTranscriptsStore.SettingsKey, false);
        store.IsEnabled.Should().BeFalse();

        settings.Set(EnhancedTranscriptsStore.SettingsKey, true);
        store.IsEnabled.Should().BeTrue();
    }
}
