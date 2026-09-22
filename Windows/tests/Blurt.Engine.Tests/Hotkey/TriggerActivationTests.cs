using Blurt.Engine.Config;
using Blurt.Engine.Hotkey;
using FluentAssertions;

namespace Blurt.Engine.Tests.Hotkey;

/// <summary>
/// The activation mode's decode rule, store round-trip, and the Shortcut section's copy. The latch
/// decisions themselves are scenario-tested where they act, in <see cref="DictationKeyGateTests"/>.
/// </summary>
public sealed class TriggerActivationTests
{
    [Test]
    public void DefaultsToTapOrHoldWhenUnset()
    {
        var store = new TriggerActivationStore(new InMemorySettingsStore());

        store.Activation.Should().Be(TriggerActivation.TapOrHold);
        TriggerActivations.FromPersisted(null).Should().Be(TriggerActivation.TapOrHold);
    }

    [Test]
    public void PersistsAndReadsBackAChosenMode()
    {
        var settings = new InMemorySettingsStore();
        var store = new TriggerActivationStore(settings);

        store.Activation = TriggerActivation.Hold;

        new TriggerActivationStore(settings).Activation.Should().Be(TriggerActivation.Hold);
    }

    [Test]
    public void AnUnknownStoredValueFallsBackToTheDefault()
    {
        var settings = new InMemorySettingsStore();
        settings.Set(DefaultsKey.TriggerActivation.Key(), "DoubleTap");

        new TriggerActivationStore(settings).Activation.Should().Be(TriggerActivation.TapOrHold);
    }

    [Test]
    public void EveryModeNamesItselfAndExplainsItselfDistinctly()
    {
        TriggerActivations.All.Select(mode => mode.Label()).Should().OnlyHaveUniqueItems();
        TriggerActivations.All.Select(mode => mode.Guidance()).Should().OnlyHaveUniqueItems();

        TriggerActivation.TapOrHold.Guidance().Should().Be(
            "Tap to start and tap again to stop, or hold the key and release to dictate.");
    }
}
