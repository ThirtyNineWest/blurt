using Blurt.Engine.Config;
using FluentAssertions;

namespace Blurt.Engine.Tests.Config;

public sealed class InMemorySettingsStoreTests
{
    [Test]
    public void AnUnsetKeyReadsAsNullForEveryType()
    {
        var store = new InMemorySettingsStore();

        store.GetString("k").Should().BeNull();
        store.GetInt("k").Should().BeNull();
        store.GetBool("k").Should().BeNull();
        store.GetDouble("k").Should().BeNull();
    }

    [Test]
    public void EachTypeRoundTrips()
    {
        var store = new InMemorySettingsStore();

        store.Set("s", "value");
        store.Set("i", 42);
        store.Set("b", true);
        store.Set("d", 1.5);

        store.GetString("s").Should().Be("value");
        store.GetInt("i").Should().Be(42);
        store.GetBool("b").Should().BeTrue();
        store.GetDouble("d").Should().Be(1.5);
    }

    [Test]
    public void AValueOfAnotherTypeReadsAsNull()
    {
        var store = new InMemorySettingsStore();

        store.Set("k", "text");

        store.GetInt("k").Should().BeNull();
        store.GetBool("k").Should().BeNull();
        store.GetDouble("k").Should().BeNull();
    }

    [Test]
    public void RemoveClearsTheKey()
    {
        var store = new InMemorySettingsStore();
        store.Set("k", 1);

        store.Remove("k");

        store.GetInt("k").Should().BeNull();
    }
}
