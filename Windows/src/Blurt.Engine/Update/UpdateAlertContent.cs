namespace Blurt.Engine.Update;

/// <summary>
/// The user-facing content of the alert an update check ends in: title, body, button titles, and —
/// for an available update — the URL the default button downloads. Owned here rather than assembled
/// at the dialog call site, which has no test target.
/// </summary>
public sealed class UpdateAlertContent : IEquatable<UpdateAlertContent>
{
    /// <summary>How prominently the shell presents the alert.</summary>
    public enum Style
    {
        /// <summary>A result the user asked for; not a warning.</summary>
        Informational,

        /// <summary>The check could not complete.</summary>
        Warning,
    }

    /// <summary>Private so every value comes from one of the named results below.</summary>
    private UpdateAlertContent(string title, string message, IReadOnlyList<string> buttons, Uri? downloadUrl = null, Style style = Style.Informational)
    {
        Title = title;
        Message = message;
        Buttons = buttons;
        DownloadUrl = downloadUrl;
        AlertStyle = style;
    }

    /// <summary>Alert title.</summary>
    public string Title { get; }

    /// <summary>Explanatory body.</summary>
    public string Message { get; }

    /// <summary>Button titles in presentation order; the first is the default action. Never empty.</summary>
    public IReadOnlyList<string> Buttons { get; }

    /// <summary>What the default button downloads, or null when it only dismisses.</summary>
    public Uri? DownloadUrl { get; }

    /// <summary>How prominently the shell presents the alert.</summary>
    public Style AlertStyle { get; }

    /// <summary>
    /// "Blurt 0.1.31" — the running-version label, also shown as the title of the Settings window's
    /// Updates row. Null names the bare product name, for a version that couldn't be parsed.
    /// </summary>
    public static string AppVersionLabel(SemanticVersion? version) =>
        version is { } known ? $"{ProductName} {known}" : ProductName;

    private static string ProductName => HostIdentity.Current.ProductName;

    /// <summary>The reassuring result a user-initiated check always shows.</summary>
    public static UpdateAlertContent UpToDate(SemanticVersion current) =>
        new("You’re up to date", $"{AppVersionLabel(current)} is the latest version.", ["OK"]);

    /// <summary>A newer release exists. The first button downloads it; the second dismisses.</summary>
    public static UpdateAlertContent Available(SemanticVersion current, SemanticVersion latest, Uri downloadUrl) =>
        new(
            $"A new version of {ProductName} is available",
            $"{AppVersionLabel(latest)} is available—you have {current}. Download it now?",
            ["Download", "Later"],
            downloadUrl);

    /// <summary>A recoverable "couldn't check" — offline, GitHub unreachable, or a malformed response.</summary>
    public static readonly UpdateAlertContent CheckFailed = new(
        "Couldn’t check for updates",
        "Check your internet connection and try again.",
        ["OK"],
        style: Style.Warning);

    /// <summary>
    /// The alert an unprompted check should show, or null when the result isn't worth interrupting
    /// for. Only an available update speaks: "you're up to date" answers a question nobody asked,
    /// and a modal saying so at launch is a nuisance.
    /// </summary>
    public static UpdateAlertContent? ForUnpromptedCheck(UpdateCheckResult result, SemanticVersion current) =>
        result switch
        {
            UpdateCheckResult.Available available => Available(current, available.Version, available.DownloadUrl),
            _ => null,
        };

    /// <summary>The alert for a completed check. A throwing check maps to <see cref="CheckFailed"/> at the call site instead.</summary>
    public static UpdateAlertContent FromResult(UpdateCheckResult result, SemanticVersion current) =>
        result switch
        {
            UpdateCheckResult.Available available => Available(current, available.Version, available.DownloadUrl),
            _ => UpToDate(current),
        };

    /// <inheritdoc/>
    public bool Equals(UpdateAlertContent? other) =>
        other is not null
        && Title == other.Title
        && Message == other.Message
        && Buttons.SequenceEqual(other.Buttons)
        && DownloadUrl == other.DownloadUrl
        && AlertStyle == other.AlertStyle;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as UpdateAlertContent);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Title);
        hash.Add(Message);
        foreach (var button in Buttons)
        {
            hash.Add(button);
        }

        hash.Add(DownloadUrl);
        hash.Add(AlertStyle);
        return hash.ToHashCode();
    }

    /// <summary>True when both alerts have the same title, message, buttons, download URL, and style.</summary>
    public static bool operator ==(UpdateAlertContent? left, UpdateAlertContent? right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>True when the alerts are not <see cref="Equals(UpdateAlertContent)"/>.</summary>
    public static bool operator !=(UpdateAlertContent? left, UpdateAlertContent? right) => !(left == right);
}
