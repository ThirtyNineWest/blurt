using Blurt.Engine.Config;
using FluentAssertions;

namespace Blurt.Engine.Tests.Config;

public sealed class InstallResetReportTests
{
    [Test]
    public void ACleanResetReportsNothing()
    {
        InstallResetReport.FailureAlert(apiKeyCleared: true, logsCleared: true).Should().BeNull();
    }

    [Test]
    public void ASurvivingKeyIsNamed()
    {
        var alert = InstallResetReport.FailureAlert(apiKeyCleared: false, logsCleared: true);

        alert.Should().Be(new InstallResetAlert(
            "Blurt wasn’t fully reset",
            "Couldn’t clear your AssemblyAI API key. Everything else was reset. Quit Blurt and try again."));
    }

    [Test]
    public void EverySurvivorIsNamedInOneSentence()
    {
        var alert = InstallResetReport.FailureAlert(apiKeyCleared: false, logsCleared: false);

        alert!.Message.Should().StartWith("Couldn’t clear your AssemblyAI API key and the dictation logs.");
    }

    [Test]
    public void TheConfirmationSaysTheAppRestarts()
    {
        InstallResetReport.Confirmation.Title.Should().Be("Reset Blurt?");
        InstallResetReport.Confirmation.Message.Should().Contain("Blurt then restarts");
    }
}
