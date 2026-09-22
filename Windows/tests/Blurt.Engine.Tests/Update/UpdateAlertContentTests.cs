using Blurt.Engine.Update;
using FluentAssertions;

namespace Blurt.Engine.Tests.Update;

/// <summary>
/// The wording an update check ends in. Owned here rather than assembled at the dialog call site,
/// which has no test target — so the version arithmetic in the copy, the button order, and "which
/// button downloads" are all covered by something.
/// </summary>
public sealed class UpdateAlertContentTests
{
    private static readonly Uri DownloadUrl = new("https://example.com/BlurtSetup.msi");

    [Test]
    public void UpToDateNamesTheRunningVersionAndOnlyDismisses()
    {
        var content = UpdateAlertContent.UpToDate(Version("0.1.31"));

        content.Title.Should().Be("You’re up to date");
        content.Message.Should().Be("Blurt 0.1.31 is the latest version.");
        content.Buttons.Should().Equal("OK");
        content.DownloadUrl.Should().BeNull();
        content.AlertStyle.Should().Be(UpdateAlertContent.Style.Informational);
    }

    [Test]
    public void AnAvailableUpdateNamesBothVersionsAndCarriesTheDownloadUrl()
    {
        var content = UpdateAlertContent.Available(Version("0.1.31"), Version("0.2.0"), DownloadUrl);

        content.Title.Should().Be("A new version of Blurt is available");
        content.Message.Should().Be("Blurt 0.2.0 is available—you have 0.1.31. Download it now?");
        content.DownloadUrl.Should().Be(DownloadUrl);
    }

    [Test]
    public void DownloadIsTheDefaultActionAndLaterDismisses()
    {
        var content = UpdateAlertContent.Available(Version("1.0"), Version("1.1"), DownloadUrl);

        // The shell opens `DownloadUrl` when the *first* button comes back, so the order here is
        // significant: flipping it would make "Later" download.
        content.Buttons.Should().Equal("Download", "Later");
        content.Buttons[0].Should().Be("Download");
    }

    [Test]
    public void CouldNotCheckIsAWarningWithNoDownload()
    {
        var content = UpdateAlertContent.CheckFailed;

        content.Title.Should().Be("Couldn’t check for updates");
        content.Message.Should().Be("Check your internet connection and try again.");
        content.Buttons.Should().Equal("OK");
        content.DownloadUrl.Should().BeNull();
        content.AlertStyle.Should().Be(UpdateAlertContent.Style.Warning);
    }

    [Test]
    public void EveryAlertOffersAtLeastOneWayOut()
    {
        UpdateAlertContent[] all =
        [
            UpdateAlertContent.UpToDate(Version("1.0")),
            UpdateAlertContent.Available(Version("1.0"), Version("2.0"), DownloadUrl),
            UpdateAlertContent.CheckFailed,
        ];

        all.Should().AllSatisfy(content => content.Buttons.Should().NotBeEmpty());
    }

    [Test]
    public void TheResultInitializerMapsBothCheckOutcomes()
    {
        var current = Version("1.0.0");
        var latest = Version("1.1.0");

        UpdateAlertContent.FromResult(new UpdateCheckResult.UpToDate(), current)
            .Should().Be(UpdateAlertContent.UpToDate(current));
        UpdateAlertContent.FromResult(new UpdateCheckResult.Available(latest, DownloadUrl), current)
            .Should().Be(UpdateAlertContent.Available(current, latest, DownloadUrl));
    }

    [Test]
    public void TheVersionLabelIsSharedByTheAlertsAndTheSettingsRow()
    {
        UpdateAlertContent.AppVersionLabel(Version("0.1.31")).Should().Be("Blurt 0.1.31");
        // A `v`-prefixed GitHub tag is stripped by SemanticVersion, so the label never reads "Blurt v0.1.31".
        UpdateAlertContent.AppVersionLabel(Version("v0.1.31")).Should().Be("Blurt 0.1.31");
    }

    [Test]
    public void AnUnparseableBundleVersionStillYieldsAUsableLabel()
    {
        UpdateAlertContent.AppVersionLabel(null).Should().Be("Blurt");
    }

    [Test]
    public void AnUnpromptedCheckSpeaksOnlyForAnAvailableUpdate()
    {
        var current = Version("1.0.0");
        var latest = Version("1.1.0");

        UpdateAlertContent.ForUnpromptedCheck(new UpdateCheckResult.UpToDate(), current).Should().BeNull();
        UpdateAlertContent.ForUnpromptedCheck(new UpdateCheckResult.Available(latest, DownloadUrl), current)
            .Should().Be(UpdateAlertContent.Available(current, latest, DownloadUrl));
    }

    private static SemanticVersion Version(string text)
    {
        SemanticVersion.TryParse(text, out var version).Should().BeTrue();
        return version;
    }
}
