using System.IO;
using Blurt.Engine.Diagnostics;
using Blurt.Engine.Windows.Config;
using FluentAssertions;

namespace Blurt.Engine.Windows.Tests.Config;

/// <summary>Exercises <see cref="JsonSettingsStore"/> against a scratch temp directory, deleted in teardown.</summary>
[NonParallelizable]
public sealed class JsonSettingsStoreTests
{
    private string _directory = string.Empty;
    private string _path = string.Empty;

    [SetUp]
    public void CreateTempDirectory()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"blurt-settings-test-{Guid.NewGuid():N}");
        _path = Path.Combine(_directory, "settings.json");
    }

    [TearDown]
    public void DeleteTempDirectory()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
        else if (File.Exists(_directory))
        {
            File.Delete(_directory);
        }
    }

    [Test]
    public void AMissingFileLoadsEmpty()
    {
        var store = new JsonSettingsStore(_path);

        store.GetString("k").Should().BeNull();
        store.GetInt("k").Should().BeNull();
        store.GetBool("k").Should().BeNull();
        store.GetDouble("k").Should().BeNull();
    }

    [Test]
    public void EachTypeRoundTrips()
    {
        var store = new JsonSettingsStore(_path);

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
        var store = new JsonSettingsStore(_path);

        store.Set("k", "text");

        store.GetInt("k").Should().BeNull();
        store.GetBool("k").Should().BeNull();
        store.GetDouble("k").Should().BeNull();
    }

    [Test]
    public void RemoveClearsTheKey()
    {
        var store = new JsonSettingsStore(_path);
        store.Set("k", 1);

        store.Remove("k");

        store.GetInt("k").Should().BeNull();
    }

    [Test]
    public void EveryMutationPersistsToDiskImmediately()
    {
        var store = new JsonSettingsStore(_path);

        store.Set("k", "value");

        var reloaded = new JsonSettingsStore(_path);
        reloaded.GetString("k").Should().Be("value");
    }

    [Test]
    public void SetCreatesTheSettingsDirectoryWhenMissing()
    {
        Directory.Exists(_directory).Should().BeFalse();
        var store = new JsonSettingsStore(_path);

        store.Set("k", "value");

        File.Exists(_path).Should().BeTrue();
    }

    [Test]
    public void ACorruptFileLoadsEmptyAndWarns()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_path, "{ not valid json");
        var original = BlurtLog.Sink;
        var lines = new List<string>();
        BlurtLog.Sink = lines.Add;

        try
        {
            var store = new JsonSettingsStore(_path);

            store.GetString("anything").Should().BeNull();
            lines.Should().NotBeEmpty();
        }
        finally
        {
            BlurtLog.Sink = original;
        }
    }

    [Test]
    public void GetIntOnAStoredDoubleConverts()
    {
        var store = new JsonSettingsStore(_path);
        store.Set("d", 3.7);

        store.GetInt("d").Should().Be(3);
    }

    [Test]
    public void GetDoubleOnAStoredIntConverts()
    {
        var store = new JsonSettingsStore(_path);
        store.Set("i", 3);

        store.GetDouble("i").Should().Be(3.0);
    }

    [Test]
    public void GetBoolOnAStringReturnsNull()
    {
        var store = new JsonSettingsStore(_path);
        store.Set("s", "true");

        store.GetBool("s").Should().BeNull();
    }

    [Test]
    public void SetDoesNotThrowWhenThePersistWriteFailsAndKeepsTheValueReadableFromMemory()
    {
        // A file occupies the path the store needs as its settings directory, so
        // Directory.CreateDirectory inside Persist fails every time.
        File.WriteAllText(_directory, "blocking the settings directory");
        var blockedPath = Path.Combine(_directory, "settings.json");
        var store = new JsonSettingsStore(blockedPath);
        var original = BlurtLog.Sink;
        var lines = new List<string>();
        BlurtLog.Sink = lines.Add;

        try
        {
            FluentActions.Invoking(() => store.Set("k", "value")).Should().NotThrow();

            store.GetString("k").Should().Be("value");
            lines.Should().NotBeEmpty();
        }
        finally
        {
            BlurtLog.Sink = original;
        }
    }
}
