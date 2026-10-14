using Blurt.Engine.Update;
using FluentAssertions;

namespace Blurt.Engine.Tests.Update;

/// <summary>
/// The gate on the launch-time update check. It lives in the engine so these cases are covered by
/// something: the shell that applies it has no test target, and its failure modes are quiet ones (a
/// check that never runs, or one that runs on every relaunch).
/// </summary>
public sealed class AutomaticUpdateCheckTests
{
    /// <summary>A fixed "now" so the arithmetic is deterministic; only the distances from it matter.</summary>
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset Ago(TimeSpan span) => Now - span;

    [Test]
    public void AConfiguredAppThatHasNeverCheckedChecks()
    {
        AutomaticUpdateCheck.ShouldRun(isConfigured: true, lastCheck: null, now: Now).Should().BeTrue();
    }

    [Test]
    public void SetupStillUnfinishedNeverChecks()
    {
        // No update modal thrown over the setup wizard, and nothing to update to before the app even
        // works. True regardless of when a check last ran.
        AutomaticUpdateCheck.ShouldRun(isConfigured: false, lastCheck: null, now: Now).Should().BeFalse();
        AutomaticUpdateCheck.ShouldRun(isConfigured: false, lastCheck: Ago(AutomaticUpdateCheck.MinimumInterval * 10), now: Now).Should().BeFalse();
    }

    [Test]
    public void ACheckWithinTheIntervalIsSkipped()
    {
        // Quit-and-relaunch a few times in an afternoon and GitHub is fetched once.
        AutomaticUpdateCheck.ShouldRun(isConfigured: true, lastCheck: Now, now: Now).Should().BeFalse();
        AutomaticUpdateCheck.ShouldRun(isConfigured: true, lastCheck: Ago(TimeSpan.FromSeconds(60)), now: Now).Should().BeFalse();
        AutomaticUpdateCheck.ShouldRun(isConfigured: true, lastCheck: Ago(AutomaticUpdateCheck.MinimumInterval - TimeSpan.FromSeconds(1)), now: Now).Should().BeFalse();
    }

    [Test]
    public void ACheckOlderThanTheIntervalRunsAgain()
    {
        AutomaticUpdateCheck.ShouldRun(isConfigured: true, lastCheck: Ago(AutomaticUpdateCheck.MinimumInterval), now: Now).Should().BeTrue();
        AutomaticUpdateCheck.ShouldRun(isConfigured: true, lastCheck: Ago(AutomaticUpdateCheck.MinimumInterval * 3), now: Now).Should().BeTrue();
    }

    [Test]
    public void ATimestampInTheFutureDoesNotWedgeTheCheckOff()
    {
        // Clock correction, a restored backup, or a machine that travelled back a timezone can leave
        // a stamp ahead of now. Naive subtraction would suppress the check until real time caught up
        // with the skew.
        AutomaticUpdateCheck.ShouldRun(isConfigured: true, lastCheck: Now + TimeSpan.FromDays(365), now: Now).Should().BeTrue();
    }

    [Test]
    public void TheThrottleIsADayAndTheLaunchWaitIsShort()
    {
        AutomaticUpdateCheck.MinimumInterval.Should().Be(TimeSpan.FromHours(24));
        AutomaticUpdateCheck.LaunchDelay.Should().BeGreaterThan(TimeSpan.Zero);
        AutomaticUpdateCheck.LaunchDelay.Should().BeLessThan(TimeSpan.FromSeconds(30));
    }
}
