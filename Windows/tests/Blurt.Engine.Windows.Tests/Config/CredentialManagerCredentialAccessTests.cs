using Blurt.Engine.Windows.Config;
using FluentAssertions;

namespace Blurt.Engine.Windows.Tests.Config;

/// <summary>
/// Round-trips <see cref="CredentialManagerCredentialAccess"/> against an isolated Credential Manager
/// target so the real <c>blurt</c>/<c>blurt-dev</c> entry is never touched.
/// </summary>
public sealed class CredentialManagerCredentialAccessTests
{
    private readonly CredentialManagerCredentialAccess _access = new();
    private string _target = string.Empty;

    [SetUp]
    public void CreateIsolatedTarget() => _target = $"blurt-test-{Guid.NewGuid():N}";

    [TearDown]
    public void DeleteTheTestCredential() => _access.Delete(_target);

    [Test]
    public void ReadReturnsAbsentBeforeAnythingIsWritten()
    {
        _access.Read(_target).Should().Be(CredentialReadResult.Absent);
    }

    [Test]
    public void WriteThenReadRoundTripsTheValue()
    {
        var written = _access.Write(_target, "sk-abc123");

        written.Should().BeTrue();
        _access.Read(_target).Should().Be(new CredentialReadResult.Found("sk-abc123"));
    }

    [Test]
    public void WriteOverwritesAnExistingValue()
    {
        _access.Write(_target, "first");

        _access.Write(_target, "second");

        _access.Read(_target).Should().Be(new CredentialReadResult.Found("second"));
    }

    [Test]
    public void DeleteRemovesTheValueThenReadReturnsAbsent()
    {
        _access.Write(_target, "present");

        var deleted = _access.Delete(_target);

        deleted.Should().BeTrue();
        _access.Read(_target).Should().Be(CredentialReadResult.Absent);
    }

    [Test]
    public void DeletingAnAlreadyAbsentValueStillReportsSuccess()
    {
        var deleted = _access.Delete(_target);

        deleted.Should().BeTrue();
    }
}
