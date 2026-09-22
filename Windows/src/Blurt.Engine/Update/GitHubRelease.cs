using System.Text.Json.Serialization;

namespace Blurt.Engine.Update;

/// <summary>
/// The subset of GitHub's "get the latest release" response the update check reads.
/// https://docs.github.com/en/rest/releases/releases#get-the-latest-release
/// Internal: only <see cref="UpdateChecker"/> decodes and inspects this.
/// </summary>
internal sealed record GitHubRelease
{
    /// <summary>The release's tag, e.g. <c>v0.1.30</c>.</summary>
    [JsonPropertyName("tag_name")]
    public string? TagName { get; init; }

    /// <summary>The release page — the fallback download when no asset names a Windows installer.</summary>
    [JsonPropertyName("html_url")]
    public Uri? HtmlUrl { get; init; }

    /// <summary>The release's published files.</summary>
    [JsonPropertyName("assets")]
    public IReadOnlyList<GitHubReleaseAsset> Assets { get; init; } = [];
}

/// <summary>One release asset: its file name and the URL that downloads it.</summary>
internal sealed record GitHubReleaseAsset
{
    /// <summary>The published file name, e.g. <c>Blurt-Setup.msi</c>.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Where the asset downloads from.</summary>
    [JsonPropertyName("browser_download_url")]
    public Uri? BrowserDownloadUrl { get; init; }
}

/// <summary>Matches a GitHub release asset's file name against Blurt's Windows installer naming.</summary>
public static class AssetMatching
{
    /// <summary>
    /// True when <paramref name="name"/> names a Windows download: its lowercased form ends with
    /// <c>.msi</c> or <c>.exe</c>, or contains <c>win</c> and ends with <c>.zip</c>.
    /// </summary>
    public static bool IsWindowsDownload(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower.EndsWith(".msi", StringComparison.Ordinal)
            || lower.EndsWith(".exe", StringComparison.Ordinal)
            || (lower.Contains("win", StringComparison.Ordinal) && lower.EndsWith(".zip", StringComparison.Ordinal));
    }
}
