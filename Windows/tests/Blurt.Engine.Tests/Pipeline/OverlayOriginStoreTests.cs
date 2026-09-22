using Blurt.Engine.Config;
using Blurt.Engine.Pipeline;
using FluentAssertions;

namespace Blurt.Engine.Tests.Pipeline;

public sealed class OverlayOriginStoreTests
{
    [Test]
    public void UnsetReadsAsNilSoTheDefaultPlacementIsUsed()
    {
        var store = new OverlayOriginStore(new InMemorySettingsStore());

        store.Origin.Should().BeNull();
    }

    [Test]
    public void RoundTripsADraggedOrigin()
    {
        var store = new OverlayOriginStore(new InMemorySettingsStore());

        store.Origin = (120.5, 340.25);

        store.Origin.Should().Be((120.5, 340.25));
    }

    [Test]
    public void AHalfWrittenPairReadsAsNilRatherThanAnImpliedZero()
    {
        var xOnly = new InMemorySettingsStore();
        xOnly.Set(OverlayOriginStore.XSettingsKey, 120.0);

        new OverlayOriginStore(xOnly).Origin.Should().BeNull();

        var yOnly = new InMemorySettingsStore();
        yOnly.Set(OverlayOriginStore.YSettingsKey, 340.0);

        new OverlayOriginStore(yOnly).Origin.Should().BeNull();
    }

    [Test]
    public void NilClearsBothKeys()
    {
        var settings = new InMemorySettingsStore();
        var store = new OverlayOriginStore(settings);
        store.Origin = (10, 20);

        store.Origin = null;

        store.Origin.Should().BeNull();
        settings.GetDouble(OverlayOriginStore.XSettingsKey).Should().BeNull();
        settings.GetDouble(OverlayOriginStore.YSettingsKey).Should().BeNull();
    }
}
