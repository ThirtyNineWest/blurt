using Blurt.Engine.Audio;
using Blurt.Engine.Config;
using Blurt.Engine.Pipeline;
using Blurt.Engine.Update;
using FluentAssertions;

namespace Blurt.Engine.Tests.Config;

public sealed class PersistedSettingsTests
{
    [Test]
    public void ResetAllRemovesEveryDefaultsKey()
    {
        var settings = new InMemorySettingsStore();
        foreach (var key in DefaultsKeys.All)
        {
            settings.Set(key.Key(), "stale");
        }

        PersistedSettings.ResetAll(settings);

        foreach (var key in DefaultsKeys.All)
        {
            settings.GetString(key.Key()).Should().BeNull($"{key} should have been cleared");
        }
    }

    [Test]
    public void ResetAllLeavesUnrelatedKeysAlone()
    {
        var settings = new InMemorySettingsStore();
        settings.Set("SomeUnrelatedKey", "keep-me");

        PersistedSettings.ResetAll(settings);

        settings.GetString("SomeUnrelatedKey").Should().Be("keep-me");
    }

    [Test]
    public void EveryStoreInThisTaskWritesUnderARosterKey()
    {
        SoundPackStore.SettingsKey.Should().Be(DefaultsKey.SoundPack.Key());
        KeyTermsStore.SettingsKey.Should().Be(DefaultsKey.KeyTerms.Key());
        DeveloperModeStore.SettingsKey.Should().Be(DefaultsKey.DeveloperMode.Key());
        EnhancedTranscriptsStore.SettingsKey.Should().Be(DefaultsKey.EnhancedTranscripts.Key());
        StyleProfileStore.SettingsKey.Should().Be(DefaultsKey.StyleProfiles.Key());
        StyleProfileStore.ActiveSettingsKey.Should().Be(DefaultsKey.ActiveStyleProfile.Key());
        StyleProfileStore.LegacySettingsKey.Should().Be(DefaultsKey.CustomStyle.Key());
        OverlayOriginStore.XSettingsKey.Should().Be(DefaultsKey.OverlayCustomOriginX.Key());
        OverlayOriginStore.YSettingsKey.Should().Be(DefaultsKey.OverlayCustomOriginY.Key());
        LastUpdateCheckStore.SettingsKey.Should().Be(DefaultsKey.LastUpdateCheck.Key());
        TextShortcutStore.SettingsKey.Should().Be(DefaultsKey.TextShortcuts.Key());
    }
}
