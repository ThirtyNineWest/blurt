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

        store.TriggerKey = TriggerKey.RightShift;

        new TriggerKeyStore(settings).TriggerKey.Should().Be(TriggerKey.RightShift);
    }

    [Test]
    public void AnUnknownStoredCodeFallsBackToTheDefault()
    {
        var settings = new InMemorySettingsStore();
        settings.Set(TriggerKeyStore.DefaultsKey, 123);

        new TriggerKeyStore(settings).TriggerKey.Should().Be(TriggerKey.RightControl);
    }

    [TestCase(0xA5)]
    [TestCase(0x5C)]
    public void AStoredRightAltOrRightWindowsCodeDecodesToRightControl(int code)
    {
        var settings = new InMemorySettingsStore();
        settings.Set(TriggerKeyStore.DefaultsKey, code);

        new TriggerKeyStore(settings).TriggerKey.Should().Be(TriggerKey.RightControl);
    }
}
