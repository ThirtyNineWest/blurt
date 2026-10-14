using System.Net;
using System.Net.Http;
using System.Text.Json;
using Blurt.Engine.Update;
using FluentAssertions;

namespace Blurt.Engine.Tests.Update;

/// <summary>
/// Checks GitHub for a newer release. The download URL is the first asset that names a Windows
/// installer, falling back to the release page when no such asset exists.
/// <see cref="AssetMatching.IsWindowsDownload"/> is exercised directly and through
/// <see cref="UpdateChecker.CheckAsync"/> so both the matcher and its wiring into the checker are
/// covered.
/// </summary>
public sealed class UpdateCheckerTests
{
    private static readonly SemanticVersion CurrentVersion = Version("0.1.30");

    [Test]
    public async Task ReportsAvailableWithTheMatchingWindowsAssetUrlWhenTheReleaseIsNewer()
    {
        // The dSYM-style asset comes first so this pins that the checker skips a non-matching asset
        // to find the one that matches.
        var checker = Checker(ReleaseJson("v0.2.0", "Blurt-Setup.msi"));

        var result = await checker.CheckAsync(CurrentVersion, CancellationToken.None);

        result.Should().Be(new UpdateCheckResult.Available(Version("0.2.0"), new Uri("https://example.com/Blurt-Setup.msi")));
    }

    [Test]
    public async Task ReportsUpToDateWhenTheReleaseMatchesTheCurrentVersion()
    {
        var checker = Checker(ReleaseJson("v0.1.30", "Blurt-Setup.msi"));

        var result = await checker.CheckAsync(CurrentVersion, CancellationToken.None);

        result.Should().Be(new UpdateCheckResult.UpToDate());
    }

    [Test]
    public async Task ReportsUpToDateWhenTheReleaseIsOlderThanTheCurrentBuild()
    {
        var checker = Checker(ReleaseJson("v0.1.0", "Blurt-Setup.msi"));

        var result = await checker.CheckAsync(CurrentVersion, CancellationToken.None);

        result.Should().Be(new UpdateCheckResult.UpToDate());
    }

    [Test]
    public async Task TheWindowsAssetIsMatchedCaseInsensitively()
    {
        var checker = Checker(ReleaseJson("v0.2.0", "Blurt-Setup.MSI"));

        var result = await checker.CheckAsync(CurrentVersion, CancellationToken.None);

        result.Should().Be(new UpdateCheckResult.Available(Version("0.2.0"), new Uri("https://example.com/Blurt-Setup.MSI")));
    }

    [Test]
    public async Task AnExeAssetMatches()
    {
        var checker = Checker(ReleaseJson("v0.2.0", "Blurt-Setup.exe"));

        var result = await checker.CheckAsync(CurrentVersion, CancellationToken.None);

        result.Should().Be(new UpdateCheckResult.Available(Version("0.2.0"), new Uri("https://example.com/Blurt-Setup.exe")));
    }

    [Test]
    public async Task AZipAssetNamingWinMatches()
    {
        var checker = Checker(ReleaseJson("v0.2.0", "Blurt-win-x64.zip"));

        var result = await checker.CheckAsync(CurrentVersion, CancellationToken.None);

        result.Should().Be(new UpdateCheckResult.Available(Version("0.2.0"), new Uri("https://example.com/Blurt-win-x64.zip")));
    }

    [Test]
    public async Task FallsBackToTheReleasePageWhenNoAssetNamesAWindowsDownload()
    {
        // "Blurt-x64.zip" is a zip, but it doesn't name "win" — not a match, so the release page
        // itself is the download.
        var checker = Checker(ReleaseJson("v0.2.0", "Blurt-x64.zip"));

        var result = await checker.CheckAsync(CurrentVersion, CancellationToken.None);

        result.Should().Be(new UpdateCheckResult.Available(Version("0.2.0"), new Uri("https://example.com/blurt/releases/tag/v0.2.0")));
    }

    [Test]
    public async Task ThrowsWhenNeitherAWindowsAssetNorAReleasePageUrlExists()
    {
        var checker = Checker(ReleaseJson("v0.2.0", windowsAssetName: "Blurt-x64.zip", htmlUrl: null));

        await FluentActions.Awaiting(() => checker.CheckAsync(CurrentVersion, CancellationToken.None))
            .Should().ThrowAsync<UpdateCheckException>();
    }

    [Test]
    public async Task ThrowsOnAnUnparseableTag()
    {
        var checker = Checker(ReleaseJson("nightly", "Blurt-Setup.msi"));

        await FluentActions.Awaiting(() => checker.CheckAsync(CurrentVersion, CancellationToken.None))
            .Should().ThrowAsync<UpdateCheckException>();
    }

    [Test]
    public async Task ThrowsOnMalformedJson()
    {
        var checker = Checker("not json"u8.ToArray());

        await FluentActions.Awaiting(() => checker.CheckAsync(CurrentVersion, CancellationToken.None))
            .Should().ThrowAsync<UpdateCheckException>();
    }

    [Test]
    public async Task PropagatesANetworkFailureFromTheTransport()
    {
        var checker = new UpdateChecker(FakeTransport.Failing(new HttpRequestException("offline")), new Uri("https://example.invalid/release"));

        await FluentActions.Awaiting(() => checker.CheckAsync(CurrentVersion, CancellationToken.None))
            .Should().ThrowAsync<HttpRequestException>();
    }

    [TestCase("Blurt-Setup.msi", true)]
    [TestCase("Blurt-Setup.MSI", true)]
    [TestCase("Blurt-Setup.exe", true)]
    [TestCase("Blurt-win-x64.zip", true)]
    [TestCase("Blurt-x64.zip", false)]
    [TestCase("Blurt-win-x64.tar.gz", false)]
    [TestCase("Blurt.app.dSYM.zip", false)]
    [TestCase("Blurt.dmg", false)]
    [TestCase("notes.txt", false)]
    public void IsWindowsDownloadMatchesMsiAndExeAlwaysAndZipOnlyWhenNamedWin(string assetName, bool expected)
    {
        AssetMatching.IsWindowsDownload(assetName).Should().Be(expected);
    }

    [Test]
    public async Task SendsABlurtUserAgentWithTheReleaseRequest()
    {
        var transport = FakeTransport.Returning(ReleaseJson("v0.1.30", "Blurt-Setup.msi"));
        var checker = new UpdateChecker(transport, new Uri("https://example.invalid/release"));

        await checker.CheckAsync(CurrentVersion, CancellationToken.None);

        transport.LastUserAgent.Should().StartWith("Blurt/");
    }

    private static UpdateChecker Checker(byte[] body) =>
        new(FakeTransport.Returning(body), new Uri("https://example.invalid/release"));

    private static readonly object DsymAsset = new { name = "Blurt.app.dSYM.zip", browser_download_url = "https://example.com/Blurt.app.dSYM.zip" };

    private static byte[] ReleaseJson(string tag, string? windowsAssetName, string? htmlUrl = "https://example.com/blurt/releases/tag/v0.2.0")
    {
        var payload = new
        {
            tag_name = tag,
            html_url = htmlUrl,
            assets = Assets(windowsAssetName),
        };

        return JsonSerializer.SerializeToUtf8Bytes(payload);
    }

    /// <summary>
    /// A non-matching asset first, pinning that the checker skips over it, followed by the Windows
    /// asset under test (or nothing, when <paramref name="windowsAssetName"/> is null).
    /// </summary>
    private static object[] Assets(string? windowsAssetName) =>
        windowsAssetName is null
            ? [DsymAsset]
            : [DsymAsset, new { name = windowsAssetName, browser_download_url = $"https://example.com/{windowsAssetName}" }];

    private static SemanticVersion Version(string text)
    {
        SemanticVersion.TryParse(text, out var version).Should().BeTrue();
        return version;
    }

    private sealed class FakeTransport : IHttpTransport
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        private FakeTransport(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public static FakeTransport Returning(byte[] body) =>
            new(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });

        public static FakeTransport Failing(Exception exception) =>
            new(_ => throw exception);

        public string? LastUserAgent { get; private set; }

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUserAgent = request.Headers.TryGetValues("User-Agent", out var values) ? string.Join(" ", values) : null;
            return Task.FromResult(_respond(request));
        }
    }
}
