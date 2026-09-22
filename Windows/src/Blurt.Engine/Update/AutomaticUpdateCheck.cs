namespace Blurt.Engine.Update;

/// <summary>
/// The policy behind the automatic update check: whether Blurt may check on its own at launch, and
/// how long it waits before doing so. "Automatic" here means the check, never the install — a
/// launch check that finds a newer release shows the same alert the menu command does, and the user
/// still downloads and installs it themselves.
/// </summary>
public static class AutomaticUpdateCheck
{
    /// <summary>
    /// The floor between automatic checks. Quitting and relaunching several times in an afternoon
    /// must not fetch GitHub every time (the unauthenticated API is rate-limited per IP), and
    /// someone who never quits Blurt is served by the menu command instead.
    /// </summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromHours(24);

    /// <summary>
    /// How long after launch the automatic check waits before fetching. The launch path shouldn't
    /// share its first moments with a network request, and the main window may not exist yet at
    /// launch to host the result.
    /// </summary>
    public static readonly TimeSpan LaunchDelay = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Whether a launch check should run, given how far setup has got and when a check last
    /// completed.
    /// </summary>
    /// <param name="isConfigured">
    /// Whether setup has finished. An app still in setup doesn't get an update alert thrown over its
    /// wizard, and someone who hasn't finished onboarding hasn't got a working Blurt to update yet.
    /// </param>
    /// <param name="lastCheck">When a check last completed, or null if none ever has.</param>
    /// <param name="now">The current time.</param>
    public static bool ShouldRun(bool isConfigured, DateTimeOffset? lastCheck, DateTimeOffset now)
    {
        if (!isConfigured)
        {
            return false;
        }

        if (lastCheck is not { } last)
        {
            return true;
        }

        // A stamp in the future (clock correction, a restored backup, a machine that travelled back
        // across a timezone) would otherwise wedge the check off for as long as the skew lasts.
        // Treat it as due rather than trusting it.
        if (last > now)
        {
            return true;
        }

        return now - last >= MinimumInterval;
    }
}
