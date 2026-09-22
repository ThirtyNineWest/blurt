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
}
