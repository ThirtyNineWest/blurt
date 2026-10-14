using System.IO;
using Blurt.Engine;
using Blurt.Engine.Windows.Config;
using FluentAssertions;

namespace Blurt.Engine.Windows.Tests.Config;

public sealed class WindowsPathsTests
{
    [Test]
    public void AppDataDirectoryIsUnderLocalAppDataNamedForTheHost()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), HostIdentity.Blurt.LogDirectoryName);

        WindowsPaths.AppDataDirectory(HostIdentity.Blurt).Should().Be(expected);
    }

    [Test]
    public void SettingsFileIsSettingsJsonUnderTheAppDataDirectory()
    {
        var expected = Path.Combine(WindowsPaths.AppDataDirectory(HostIdentity.Blurt), "settings.json");

        WindowsPaths.SettingsFile(HostIdentity.Blurt).Should().Be(expected);
    }

    [Test]
    public void LogDirectoryIsLogsUnderTheAppDataDirectory()
    {
        var expected = Path.Combine(WindowsPaths.AppDataDirectory(HostIdentity.Blurt), "Logs");

        WindowsPaths.LogDirectory(HostIdentity.Blurt).Should().Be(expected);
    }

    [Test]
    public void TheDebugIdentitySharesTheReleaseAppDataDirectory()
    {
        // BlurtDev only overrides the credential target, so debug and release builds read and
        // write the same settings file and log directory.
        WindowsPaths.AppDataDirectory(HostIdentity.BlurtDev).Should().Be(WindowsPaths.AppDataDirectory(HostIdentity.Blurt));
    }
}
