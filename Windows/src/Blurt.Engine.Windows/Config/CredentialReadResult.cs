namespace Blurt.Engine.Windows.Config;

/// <summary>
/// Why a credential read produced no value. Collapsing this to a bare <see langword="null"/> is
/// unsafe for a caller that memoizes the answer: "no credential exists" is a durable fact, while any
/// other read failure (a locked profile, a DPAPI error, access denied) is transient and must not be
/// cached as "no key".
/// </summary>
public abstract record CredentialReadResult
{
    /// <summary>No credential exists at the target — a durable fact.</summary>
    public static readonly CredentialReadResult Absent = new AbsentResult();

    private CredentialReadResult()
    {
    }

    /// <summary>The stored value.</summary>
    public sealed record Found(string Key) : CredentialReadResult;

    /// <summary>
    /// The read itself failed for a reason other than "not found", so whether a value exists is
    /// unknown. Carries the Win32 error code.
    /// </summary>
    public sealed record Unavailable(int Win32Error) : CredentialReadResult;

    private sealed record AbsentResult : CredentialReadResult;
}
