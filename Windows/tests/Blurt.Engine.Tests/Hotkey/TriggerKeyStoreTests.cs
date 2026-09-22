using Blurt.Engine.Config;
using Blurt.Engine.Hotkey;
using FluentAssertions;

namespace Blurt.Engine.Tests.Hotkey;

public sealed class TriggerKeyStoreTests
{
    [Test]
    public void DefaultsToRightControlWhenUnset()
    {
        var store = new TriggerKeyStore(new InMemorySettingsStore());

        store.TriggerKey.Should().Be(TriggerKey.RightControl);
    }

    [Test]
    public void PersistsAndReadsBackAChosenKey()
    {
        var settings = new InMemorySettingsStore();
        var store = new TriggerKeyStore(settings);

        store.TriggerKey = TriggerKey.RightAlt;

        new TriggerKeyStore(settings).TriggerKey.Should().Be(TriggerKey.RightAlt);
    }

    [Test]
    public void AnUnknownStoredCodeFallsBackToTheDefault()
    {
        var settings = new InMemorySettingsStore();
        settings.Set(TriggerKeyStore.DefaultsKey, 123);

        new TriggerKeyStore(settings).TriggerKey.Should().Be(TriggerKey.RightControl);
    }
}
