using System.IO;
using Blurt.Engine.Windows.FocusCapture;
using FluentAssertions;

namespace Blurt.Engine.Windows.Tests.FocusCapture;

public sealed class ChromiumHostTests
{
    private const string Pak = "chrome_100_percent.pak";

    private string _root = null!;

    [SetUp]
    public void CreateRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), "BlurtChromiumHostTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void DeleteRoot() => Directory.Delete(_root, recursive: true);

    [TestCase("chrome")]
    [TestCase("msedge")]
    [TestCase("firefox")]
    [TestCase("brave")]
    [TestCase("opera")]
    [TestCase("vivaldi")]
    [TestCase("arc")]
    public void AKnownBrowserProcessNameIsOpaque(string name)
    {
        ChromiumHost.IsOpaque(processPath: null, processName: name).Should().BeTrue();
    }

    [TestCase("Chrome")]
    [TestCase("MSEDGE.EXE")]
    [TestCase("firefox.exe")]
    public void TheBrowserNameMatchIgnoresCaseAndTheExeSuffix(string name)
    {
        ChromiumHost.IsOpaque(processPath: null, processName: name).Should().BeTrue();
    }

    [TestCase("notepad")]
    [TestCase("chromedriver")]
    [TestCase("")]
    public void AnyOtherProcessNameIsNotOpaque(string name)
    {
        ChromiumHost.IsOpaque(processPath: null, processName: name).Should().BeFalse();
    }

    [Test]
    public void NothingKnownIsNotOpaque()
    {
        ChromiumHost.IsOpaque(processPath: null, processName: null).Should().BeFalse();
    }

    [Test]
    public void AnExeWithTheChromiumPakBesideItIsOpaque()
    {
        File.WriteAllText(Path.Combine(_root, Pak), "");
        var exe = Path.Combine(_root, "Slack.exe");

        ChromiumHost.IsOpaque(exe, "slack").Should().BeTrue();
    }

    [Test]
    public void AnExeWithTheChromiumPakOneDirectoryDownIsOpaque()
    {
        var version = Directory.CreateDirectory(Path.Combine(_root, "1.2.3.4"));
        File.WriteAllText(Path.Combine(version.FullName, Pak), "");
        var exe = Path.Combine(_root, "app.exe");

        ChromiumHost.IsOpaque(exe, "app").Should().BeTrue();
    }

    [Test]
    public void AChromiumPakTwoDirectoriesDownIsNotFound()
    {
        var deep = Directory.CreateDirectory(Path.Combine(_root, "a", "b"));
        File.WriteAllText(Path.Combine(deep.FullName, Pak), "");
        var exe = Path.Combine(_root, "app.exe");

        ChromiumHost.IsOpaque(exe, "app").Should().BeFalse();
    }

    [Test]
    public void ANativeExeDirectoryIsNotOpaque()
    {
        Directory.CreateDirectory(Path.Combine(_root, "resources"));
        File.WriteAllText(Path.Combine(_root, "other.pak"), "");
        var exe = Path.Combine(_root, "notepad.exe");

        ChromiumHost.IsOpaque(exe, "notepad").Should().BeFalse();
    }

    [Test]
    public void AnExeInAMissingDirectoryIsNotOpaque()
    {
        var exe = Path.Combine(_root, "gone", "ghost.exe");

        ChromiumHost.IsOpaque(exe, "ghost").Should().BeFalse();
    }
}
