using System.Diagnostics;

namespace Blurt.Engine.Config;

/// <summary>
/// Outcome of an <see cref="ApiKeySubmission"/> attempt. <see cref="Invalid"/>/<see cref="Unreachable"/>
/// mirror <see cref="ApiKeyValidationResult"/> (nothing was saved); <see cref="SaveFailed"/> means the
/// key validated but could not be written to (or read back from) storage.
/// </summary>
public enum ApiKeySubmissionOutcome
{
    /// <summary>The key validated and was saved.</summary>
    Valid,

    /// <summary>AssemblyAI rejected the key; nothing was saved.</summary>
    Invalid,

    /// <summary>AssemblyAI could not be reached; nothing was saved.</summary>
    Unreachable,

    /// <summary>The key validated but the write did not survive a read-back.</summary>
    SaveFailed,
}

/// <summary>
/// How a failed submission should be reported. A problem the user can fix by editing the field is
/// inline; a system fault retyping can't touch is an alert.
/// </summary>
public abstract record ApiKeyFailureReport
{
    private ApiKeyFailureReport()
    {
    }

    /// <summary>Recoverable: show <paramref name="message"/> beside the field and let the user retry.</summary>
    public static ApiKeyFailureReport Inline(string message) => new InlineReport(message);

    /// <summary>A genuine fault retyping the key cannot fix: present <paramref name="title"/>/<paramref name="message"/> as an alert.</summary>
    public static ApiKeyFailureReport Alert(string title, string message) => new AlertReport(title, message);

    /// <summary>A recoverable, inline error.</summary>
    public sealed record InlineReport(string Message) : ApiKeyFailureReport;

    /// <summary>A genuine fault, reported as an alert.</summary>
    public sealed record AlertReport(string Title, string Message) : ApiKeyFailureReport;
}

/// <summary>
/// The validate-then-save flow behind the setup/settings screen's Save/Update button. Its central
/// invariant: an unverified key never persists. A key is written only after AssemblyAI actively
/// accepts it; a rejected or unreachable outcome leaves the stored key untouched.
/// </summary>
/// <param name="keyStore">Where the verified key is written.</param>
/// <param name="validate">The validation check, injected so tests can drive every outcome without a network call.</param>
public sealed class ApiKeySubmission(IApiKeyGateway keyStore, Func<string, Task<ApiKeyValidationResult>> validate)
{
    /// <summary>
    /// Writes <paramref name="key"/> to the store. Returns true only when a non-empty key is actually
    /// readable back after the write — a write that "succeeds" but leaves no key stored (e.g. a
    /// whitespace-only key, which the gateway treats as a delete) is a failure to save a key.
    /// </summary>
    public bool Save(string key) => keyStore.Save(key) && keyStore.HasKey;

    /// <summary>
    /// Verifies <paramref name="key"/> against AssemblyAI and saves it only when AssemblyAI actively
    /// accepts it. On an invalid or unreachable outcome the stored key is left untouched.
    /// <paramref name="ct"/> is checked after validation and before the save, so a cancelled
    /// submission never persists.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled before the save.</exception>
    public async Task<ApiKeySubmissionOutcome> SubmitAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var result = await validate(key).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return result switch
        {
            ApiKeyValidationResult.Valid => Save(key) ? ApiKeySubmissionOutcome.Valid : ApiKeySubmissionOutcome.SaveFailed,
            ApiKeyValidationResult.Invalid => ApiKeySubmissionOutcome.Invalid,
            ApiKeyValidationResult.Unreachable => ApiKeySubmissionOutcome.Unreachable,
            _ => throw new UnreachableException(),
        };
    }

    /// <summary>How <paramref name="outcome"/> should be reported, or null for <see cref="ApiKeySubmissionOutcome.Valid"/>, which has nothing to report.</summary>
    public static ApiKeyFailureReport? FailureReport(ApiKeySubmissionOutcome outcome) => outcome switch
    {
        ApiKeySubmissionOutcome.Valid => null,
        ApiKeySubmissionOutcome.Invalid =>
            ApiKeyFailureReport.Inline("AssemblyAI rejected that key. Double-check it and try again."),
        ApiKeySubmissionOutcome.Unreachable =>
            ApiKeyFailureReport.Inline("Couldn't reach AssemblyAI. Check your connection and try again."),
        ApiKeySubmissionOutcome.SaveFailed => ApiKeyFailureReport.Alert(
            "Couldn’t Save Your Key",
            "Blurt couldn’t write the key to Windows Credential Manager. Check your account permissions and try again."),
        _ => throw new UnreachableException(),
    };
}
