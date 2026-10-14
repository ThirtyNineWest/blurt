namespace Blurt.Engine.STT;

/// <summary>
/// The <c>User-Agent</c> every AssemblyAI request carries: the host's product name, the build's
/// <c>major.minor.build</c> version, and a comment naming the OS, plus <c>dev</c> for any
/// non-release build, e.g. <c>Blurt/0.1.62 (Windows 10.0.26200; dev)</c>. It carries nothing
/// per-install and no locale or language.
/// </summary>
public static class UserAgent
{
    /// <summary>The header this value is sent under.</summary>
    public const string HeaderField = "User-Agent";

    private static readonly Lazy<string> s_current = new(() =>
        Value(HostIdentity.Current.ProductName, BuildVersion(), [OsDetail(), ChannelDetail()]));

    /// <summary>
    /// The agent for this process, resolved once on first read. It reads
    /// <see cref="HostIdentity.Current"/>, so a host must configure its identity before its first
    /// AssemblyAI request.
    /// </summary>
    public static string Current => s_current.Value;

    /// <summary>
    /// Formats an agent: <paramref name="product"/> with spaces replaced by hyphens (a product token
    /// may not contain whitespace), then <c>/version</c> when <paramref name="version"/> is
    /// non-blank, then the non-blank <paramref name="details"/> as a parenthesized comment joined
    /// with <c>"; "</c>. No details means no parentheses.
    /// </summary>
    public static string Value(string product, string? version, IReadOnlyList<string> details)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(details);
        var token = product.Replace(' ', '-');
        var name = version.TrimmedNonEmpty() is { } v ? $"{token}/{v}" : token;
        var comment = details.Select(d => d.TrimmedNonEmpty()).OfType<string>().ToList();
        return comment.Count == 0 ? name : $"{name} ({string.Join("; ", comment)})";
    }

    private static string? BuildVersion() =>
        typeof(UserAgent).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}" : null;

    private static string OsDetail()
    {
        var v = Environment.OSVersion.Version;
        return $"Windows {v.Major}.{v.Minor}.{v.Build}";
    }

    /// <summary>
    /// <c>dev</c> for any build that is not a release, so local and test traffic stays out of the
    /// service-side baseline; empty (dropped) for a release.
    /// </summary>
    private static string ChannelDetail()
    {
#if DEBUG
        return "dev";
#else
        return "";
#endif
    }
}
