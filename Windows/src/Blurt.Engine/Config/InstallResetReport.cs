namespace Blurt.Engine.Config;

/// <summary>Title and body for an alert the reset flow presents.</summary>
/// <param name="Title">The alert title.</param>
/// <param name="Message">The alert body.</param>
public sealed record InstallResetAlert(string Title, string Message);

/// <summary>
/// The wording of the "Reset Blurt" flow: the confirmation asked before anything is removed, and the
/// report shown when part of the sweep survived. Clearing settings cannot fail, so only the API key
/// and the dictation logs can be named as survivors.
/// </summary>
public static class InstallResetReport
{
    /// <summary>The Reset section's footer.</summary>
    public const string Footer = "Deletes your AssemblyAI API key, clears every setting, and removes the dictation logs.";

    /// <summary>The confirmation presented before the reset runs.</summary>
    public static InstallResetAlert Confirmation { get; } = new(
        "Reset Blurt?",
        "This can’t be undone. Your AssemblyAI API key, every setting, and the dictation logs are all removed.\n\n"
            + "Blurt then restarts and takes you back through setup.");

    /// <summary>
    /// The alert naming what survived the sweep, or null when every step landed and the app should
    /// restart without saying anything.
    /// </summary>
    public static InstallResetAlert? FailureAlert(bool apiKeyCleared, bool logsCleared)
    {
        List<string> survivors = [];
        if (!apiKeyCleared)
        {
            survivors.Add("your AssemblyAI API key");
        }

        if (!logsCleared)
        {
            survivors.Add("the dictation logs");
        }

        return survivors.Count == 0
            ? null
            : new InstallResetAlert(
                "Blurt wasn’t fully reset",
                $"Couldn’t clear {string.Join(" and ", survivors)}. Everything else was reset. Quit Blurt and try again.");
    }
}
