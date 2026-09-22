using System.Text.Json;
using Blurt.Engine.STT;

namespace Blurt.Engine.Update;

/// <summary>The outcome of a successful update check. Every case is abstract-sealed, so a new one can't compile without deciding how it prints.</summary>
public abstract record UpdateCheckResult
{
    private UpdateCheckResult()
    {
    }

    /// <summary>The running app is the latest published release, or newer.</summary>
    public sealed record UpToDate : UpdateCheckResult;

    /// <summary>A newer release exists; <paramref name="DownloadUrl"/> downloads it in the browser.</summary>
    /// <param name="Version">The newer release's version.</param>
    /// <param name="DownloadUrl">Where the default action downloads it — a matching Windows asset, or the release page.</param>
    public sealed record Available(SemanticVersion Version, Uri DownloadUrl) : UpdateCheckResult;
}

/// <summary>
/// The check couldn't produce a result from an otherwise successful HTTP response: malformed JSON,
/// an unparseable release tag, or a newer release with neither a matching Windows asset nor a
/// release page URL.
/// </summary>
public sealed class UpdateCheckException : Exception
{
    /// <summary>Creates an exception with <paramref name="message"/>.</summary>
    public UpdateCheckException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with <paramref name="message"/>, wrapping <paramref name="innerException"/>.</summary>
    public UpdateCheckException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Checks GitHub for a newer Blurt release. Reports only whether a newer release exists and where
/// to download it; installing is left to the user. The network call goes through the engine's
/// <see cref="IHttpTransport"/> seam, so tests substitute a fake transport instead of hitting the
/// live API.
/// </summary>
public sealed class UpdateChecker
{
    private readonly Uri _releaseUrl;
    private readonly IHttpTransport _transport;

    /// <summary>
    /// <paramref name="releaseUrl"/> defaults to the configured host identity's release URL, so a
    /// host that called <see cref="HostIdentity.Configure"/> checks its own repo without passing
    /// one; tests substitute a local URL here.
    /// </summary>
    public UpdateChecker(IHttpTransport transport, Uri? releaseUrl = null)
    {
        _transport = transport;
        _releaseUrl = releaseUrl ?? HostIdentity.Current.ReleaseUrl;
    }

    /// <summary>Fetches the latest release and compares it to <paramref name="current"/>.</summary>
    /// <exception cref="UpdateCheckException">
    /// The response body isn't valid JSON, its tag doesn't parse as a <see cref="SemanticVersion"/>,
    /// or a newer release carries neither a matching Windows asset nor a release page URL.
    /// </exception>
    public async Task<UpdateCheckResult> CheckAsync(SemanticVersion current, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _releaseUrl);
        request.Headers.TryAddWithoutValidation(UserAgent.HeaderField, UserAgent.Current);
        using var response = await _transport.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);

        GitHubRelease release;
        try
        {
            release = JsonSerializer.Deserialize<GitHubRelease>(body) ?? throw new UpdateCheckException("The release response was empty.");
        }
        catch (JsonException ex)
        {
            throw new UpdateCheckException("The release response was not valid JSON.", ex);
        }

        if (release.TagName is null || !SemanticVersion.TryParse(release.TagName, out var latest))
        {
            throw new UpdateCheckException($"The release tag '{release.TagName}' could not be parsed as a version.");
        }

        if (!(current < latest))
        {
            return new UpdateCheckResult.UpToDate();
        }

        var downloadUrl = FindWindowsDownloadUrl(release.Assets)
            ?? release.HtmlUrl
            ?? throw new UpdateCheckException("The release has no Windows asset and no release page URL.");

        return new UpdateCheckResult.Available(latest, downloadUrl);
    }

    private static Uri? FindWindowsDownloadUrl(IReadOnlyList<GitHubReleaseAsset> assets)
    {
        foreach (var asset in assets)
        {
            if (asset.Name is not null && asset.BrowserDownloadUrl is not null && AssetMatching.IsWindowsDownload(asset.Name))
            {
                return asset.BrowserDownloadUrl;
            }
        }

        return null;
    }
}
