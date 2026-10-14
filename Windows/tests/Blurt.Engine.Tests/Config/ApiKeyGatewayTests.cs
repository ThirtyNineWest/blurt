using Blurt.Engine.Config;
using FluentAssertions;

namespace Blurt.Engine.Tests.Config;

public sealed class ApiKeyGatewayTests
{
    [Test]
    public void InMemoryStoreRoundTripsAKeyTrimmed()
    {
        IApiKeyGateway store = new InMemoryApiKeyStore();
        store.Current.Should().BeNull();
        store.HasKey.Should().BeFalse();

        var saved = store.Save("  sk-123  ");

        saved.Should().BeTrue();
        store.Current.Should().Be("sk-123");
        store.HasKey.Should().BeTrue();
    }

    [TestCase("")]
    [TestCase("   \n")]
    public void BlankWritesClearTheStoredKey(string clearing)
    {
        IApiKeyGateway store = new InMemoryApiKeyStore();
        store.Save("sk-123");

        var saved = store.Save(clearing);

        saved.Should().BeTrue();
        store.Current.Should().BeNull();
        store.HasKey.Should().BeFalse();
    }

    [Test]
    public void HasKeyIsDerivedFromCurrentForAnyImplementation()
    {
        IApiKeyGateway withKey = new FixedGateway("sk-123");
        IApiKeyGateway withoutKey = new FixedGateway(null);
        IApiKeyGateway blank = new FixedGateway("   ");

        withKey.HasKey.Should().BeTrue();
        withoutKey.HasKey.Should().BeFalse();
        blank.HasKey.Should().BeFalse();
    }

    private sealed class FixedGateway(string? value) : IApiKeyGateway
    {
        public string? Current => value;

        public bool Save(string key) => false;
    }
}
