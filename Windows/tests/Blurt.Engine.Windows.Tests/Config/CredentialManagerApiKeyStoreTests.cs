using Blurt.Engine.Config;
using Blurt.Engine.Windows.Config;
using FluentAssertions;

namespace Blurt.Engine.Windows.Tests.Config;

/// <summary>
/// Round-trips <see cref="CredentialManagerApiKeyStore"/> against an isolated Credential Manager
/// target so the real <c>blurt</c>/<c>blurt-dev</c> entry is never touched.
/// </summary>
public sealed class CredentialManagerApiKeyStoreTests
{
    private string _target = string.Empty;

    [SetUp]
    public void CreateIsolatedTarget() => _target = $"blurt-test-{Guid.NewGuid():N}";

    [TearDown]
    public void DeleteTheTestCredential() => new CredentialManagerApiKeyStore(_target).Save(string.Empty);

    [Test]
    public void CurrentIsNullBeforeAnythingIsSaved()
    {
        var store = new CredentialManagerApiKeyStore(_target);

        store.Current.Should().BeNull();
    }

    [Test]
    public void SaveThenCurrentRoundTripsTheValue()
    {
        var store = new CredentialManagerApiKeyStore(_target);

        var saved = store.Save("sk-abc123");

        saved.Should().BeTrue();
        store.Current.Should().Be("sk-abc123");
    }

    [Test]
    public void SaveTrimsSurroundingWhitespace()
    {
        var store = new CredentialManagerApiKeyStore(_target);

        store.Save("  sk-trim  \n");

        store.Current.Should().Be("sk-trim");
    }

    [Test]
    public void SaveOverwritesAnExistingValue()
    {
        var store = new CredentialManagerApiKeyStore(_target);
        store.Save("first");

        store.Save("second");

        store.Current.Should().Be("second");
    }

    [TestCase("")]
    [TestCase("   \n")]
    public void ABlankSaveDeletesTheStoredValue(string blank)
    {
        var store = new CredentialManagerApiKeyStore(_target);
        store.Save("present");

        var saved = store.Save(blank);

        saved.Should().BeTrue();
        store.Current.Should().BeNull();
    }

    [Test]
    public void DeletingAnAlreadyAbsentValueStillReportsSuccess()
    {
        var store = new CredentialManagerApiKeyStore(_target);

        var saved = store.Save("");

        saved.Should().BeTrue();
        store.Current.Should().BeNull();
    }

    [Test]
    public void HasKeyReflectsWhetherAKeyIsStored()
    {
        IApiKeyGateway store = new CredentialManagerApiKeyStore(_target);
        store.HasKey.Should().BeFalse();

        store.Save("sk-abc123");

        store.HasKey.Should().BeTrue();
    }

    [Test]
    public void CurrentIsServedFromTheMemoAfterTheFirstReadNotFromStorage()
    {
        var store = new CredentialManagerApiKeyStore(_target);
        store.Save("sk-memo");
        _ = store.Current;

        new CredentialManagerApiKeyStore(_target).Save(string.Empty);

        store.Current.Should().Be("sk-memo");
    }

    [Test]
    public void SaveRefreshesTheMemoWithoutARead()
    {
        var store = new CredentialManagerApiKeyStore(_target);
        _ = store.Current;

        store.Save("sk-new");

        store.Current.Should().Be("sk-new");
    }
}
