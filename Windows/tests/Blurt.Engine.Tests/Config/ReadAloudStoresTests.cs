using System.Globalization;
using Blurt.Engine.Config;
using FluentAssertions;

namespace Blurt.Engine.Tests.Config;

public sealed class SelectionSpeechStoreTests
{
    [Test]
    public void DefaultsToOffWhenUnset()
    {
        new SelectionSpeechStore(new InMemorySettingsStore()).IsEnabled.Should().BeFalse();
    }

    [Test]
    public void ReadsBackTheSwitchTheSettingsToggleWrites()
    {
        var settings = new InMemorySettingsStore();
        var store = new SelectionSpeechStore(settings);

        settings.Set(SelectionSpeechStore.SettingsKey, true);
        store.IsEnabled.Should().BeTrue();

        settings.Set(SelectionSpeechStore.SettingsKey, false);
        store.IsEnabled.Should().BeFalse();
    }

    [Test]
    public void RoundTripsAWrite()
    {
        var settings = new InMemorySettingsStore();

        new SelectionSpeechStore(settings).IsEnabled = true;

        new SelectionSpeechStore(settings).IsEnabled.Should().BeTrue();
    }
}

public sealed class SelectionAskStoreTests
{
    [Test]
    public void DefaultsToOnWhenUnset()
    {
        new SelectionAskStore(new InMemorySettingsStore()).IsEnabled.Should().BeTrue();
        SelectionAskStore.DefaultValue.Should().BeTrue();
    }

    [Test]
    public void ReadsBackTheSwitchTheSettingsToggleWrites()
    {
        var settings = new InMemorySettingsStore();
        var store = new SelectionAskStore(settings);

        settings.Set(SelectionAskStore.SettingsKey, false);
        store.IsEnabled.Should().BeFalse();

        settings.Set(SelectionAskStore.SettingsKey, true);
        store.IsEnabled.Should().BeTrue();
    }

    [Test]
    public void RoundTripsAWrite()
    {
        var settings = new InMemorySettingsStore();

        new SelectionAskStore(settings).IsEnabled = false;

        new SelectionAskStore(settings).IsEnabled.Should().BeFalse();
    }
}

public sealed class ReadAloudStyleStoreTests
{
    [Test]
    public void UntouchedIsNaturalPaceWithTheSelectionVerbatim()
    {
        new ReadAloudStyleStore(new InMemorySettingsStore()).Style.Should().Be(ReadAloudStyle.Standard);
        ReadAloudStyleStore.DefaultSpeed.Should().Be(1);
        ReadAloudStyleStore.DefaultSkipsJargon.Should().BeFalse();
    }

    [Test]
    public void SpeedAndSkippingAreIndependentSettings()
    {
        var settings = new InMemorySettingsStore();
        var store = new ReadAloudStyleStore(settings);

        settings.Set(ReadAloudStyleStore.SpeedSettingsKey, 1.5);
        store.Style.Should().Be(new ReadAloudStyle(1.5, false));

        settings.Remove(ReadAloudStyleStore.SpeedSettingsKey);
        settings.Set(ReadAloudStyleStore.SkipsJargonSettingsKey, true);
        store.Style.Should().Be(new ReadAloudStyle(1, true));
    }

    [Test]
    public void ASpeedOffThePickersListSnapsToTheNearestChoiceAndANonNumberReadsAsTheDefault()
    {
        var settings = new InMemorySettingsStore();
        var store = new ReadAloudStyleStore(settings);

        settings.Set(ReadAloudStyleStore.SpeedSettingsKey, 9.0);
        store.Style.Rate.Should().Be(3);

        settings.Set(ReadAloudStyleStore.SpeedSettingsKey, 0.1);
        store.Style.Rate.Should().Be(1);

        settings.Set(ReadAloudStyleStore.SpeedSettingsKey, 1.6);
        store.Style.Rate.Should().Be(1.5);

        settings.Set(ReadAloudStyleStore.SpeedSettingsKey, 1.65);
        store.Style.Rate.Should().Be(1.75);

        settings.Set(ReadAloudStyleStore.SpeedSettingsKey, double.NaN);
        store.Style.Rate.Should().Be(ReadAloudStyleStore.DefaultSpeed);

        settings.Set(ReadAloudStyleStore.SpeedSettingsKey, "fast");
        store.Style.Rate.Should().Be(ReadAloudStyleStore.DefaultSpeed);
    }

    [Test]
    public void TheDefaultSpeedIsOneThePickerOffers()
    {
        ReadAloudStyleStore.SpeedChoices.Should().Contain(ReadAloudStyleStore.DefaultSpeed);
        ReadAloudStyleStore.SpeedChoices.Should().BeInAscendingOrder();
    }

    [Test]
    public void SpeedLabelsDropTrailingZerosAndFollowTheCulturesDecimalMark()
    {
        var english = CultureInfo.GetCultureInfo("en-US");

        ReadAloudStyleStore.SpeedLabel(2, english).Should().Be("2×");
        ReadAloudStyleStore.SpeedLabel(1.5, english).Should().Be("1.5×");
        ReadAloudStyleStore.SpeedLabel(1.25, english).Should().Be("1.25×");
        ReadAloudStyleStore.SpeedLabel(1.5, CultureInfo.GetCultureInfo("de-DE")).Should().Be("1,5×");
    }

    [Test]
    public void TheSettersPersistTheRawValuesAndTheGetterSnapsTheSpeed()
    {
        var settings = new InMemorySettingsStore();
        var store = new ReadAloudStyleStore(settings);

        store.Speed = 1.6;
        store.SkipsJargon = true;

        settings.GetDouble(ReadAloudStyleStore.SpeedSettingsKey).Should().Be(1.6);
        store.Speed.Should().Be(1.5);
        store.SkipsJargon.Should().BeTrue();
        store.Style.Should().Be(new ReadAloudStyle(1.5, true));
    }
}
