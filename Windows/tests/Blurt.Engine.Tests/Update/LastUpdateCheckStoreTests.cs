using System.Globalization;
using Blurt.Engine.Config;
using Blurt.Engine.Update;
using FluentAssertions;

namespace Blurt.Engine.Tests.Update;

public sealed class LastUpdateCheckStoreTests
{
    [Test]
    public void AnUnsetStoreReportsNoPreviousCheck()
    {
        var store = new LastUpdateCheckStore(new InMemorySettingsStore());

        store.LastCheck.Should().BeNull();
    }

    [Test]
    public void AStampRoundTrips()
    {
        var store = new LastUpdateCheckStore(new InMemorySettingsStore());
        var when = DateTimeOffset.Parse("2026-01-15T10:30:00Z", CultureInfo.InvariantCulture);

        store.LastCheck = when;

        store.LastCheck.Should().Be(when);
    }

    [Test]
    public void TheEpochIsARealStampNotAnUnsetStore()
    {
        var store = new LastUpdateCheckStore(new InMemorySettingsStore());

        store.LastCheck = DateTimeOffset.UnixEpoch;

        store.LastCheck.Should().Be(DateTimeOffset.UnixEpoch);
    }

    [Test]
    public void ClearingTheStampReturnsTheStoreToNeverChecked()
    {
        var store = new LastUpdateCheckStore(new InMemorySettingsStore());
        store.LastCheck = DateTimeOffset.UtcNow;

        store.LastCheck = null;

        store.LastCheck.Should().BeNull();
    }

    [Test]
    public void EachStoreReadsItsOwnSettings()
    {
        var settings = new InMemorySettingsStore();
        var when = DateTimeOffset.Parse("2026-01-15T10:30:00Z", CultureInfo.InvariantCulture);
        new LastUpdateCheckStore(settings).LastCheck = when;

        new LastUpdateCheckStore(settings).LastCheck.Should().Be(when);
    }

    [Test]
    public void TheKeyIsTheRostersKey()
    {
        LastUpdateCheckStore.SettingsKey.Should().Be(DefaultsKey.LastUpdateCheck.Key());
    }

    /// <summary>
    /// This getter sits on the app's launch path, so a corrupt stamp — a hand-edited or truncated
    /// settings file — must read as never-checked rather than throwing <see cref="FormatException"/>
    /// out of a launch-time read.
    /// </summary>
    [TestCase("not a date")]
    [TestCase("")]
    [TestCase("2026-13-40")]
    public void AMalformedStampReadsAsNeverCheckedRatherThanThrowing(string stored)
    {
        var settings = new InMemorySettingsStore();
        settings.Set(LastUpdateCheckStore.SettingsKey, stored);
        var store = new LastUpdateCheckStore(settings);

        store.LastCheck.Should().BeNull();
    }
}
