using Blurt.Engine.STT;
using FluentAssertions;

namespace Blurt.Engine.Tests.STT;

public sealed class UserAgentTests
{
    [Test]
    public void TheAgentIsTheProductTheVersionAndTheDetailsAsAComment()
    {
        UserAgent.Value("Blurt", "0.1.54", ["Windows 10.0.26200", "dev"]).Should().Be("Blurt/0.1.54 (Windows 10.0.26200; dev)");
    }

    [Test]
    public void ASingleDetailNeedsNoSeparator()
    {
        UserAgent.Value("Blurt", "0.1.54", ["Windows 10.0.26200"]).Should().Be("Blurt/0.1.54 (Windows 10.0.26200)");
    }

    [Test]
    public void NoDetailsMeansNoEmptyParentheses()
    {
        UserAgent.Value("Blurt", "0.1.54", []).Should().Be("Blurt/0.1.54");
    }

    [Test]
    public void BlankDetailsDropOut()
    {
        UserAgent.Value("Blurt", "0.1.54", ["", "  ", "Windows 10.0.26200"]).Should().Be("Blurt/0.1.54 (Windows 10.0.26200)");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\n")]
    public void AMissingOrBlankVersionDegradesToTheBareProductName(string? version)
    {
        UserAgent.Value("Blurt", version, []).Should().Be("Blurt");
        UserAgent.Value("Blurt", version, ["Windows 10.0.26200"]).Should().Be("Blurt (Windows 10.0.26200)");
    }

    [Test]
    public void ASurroundingWhitespaceVersionIsTrimmed()
    {
        UserAgent.Value("Blurt", " 0.1.54\n", []).Should().Be("Blurt/0.1.54");
    }

    [Test]
    public void AMultiWordProductNameBecomesASingleToken()
    {
        UserAgent.Value("Acme Voice", "2.0", []).Should().Be("Acme-Voice/2.0");
    }

    [Test]
    public void TheCurrentAgentNamesTheProductTheBuildVersionAndTheRunningWindows()
    {
        var os = Environment.OSVersion.Version;

        UserAgent.Current.Should().StartWith($"{HostIdentity.Current.ProductName}/0.1.62 ");
        UserAgent.Current.Should().Contain($"(Windows {os.Major}.{os.Minor}.{os.Build}");
    }

    [Test]
    public void ANonReleaseBuildMarksItselfDev()
    {
#if DEBUG
        UserAgent.Current.Should().EndWith("; dev)");
#else
        UserAgent.Current.Should().NotContain("dev");
#endif
    }

    [Test]
    public void TheHeaderFieldIsSpelledOnce()
    {
        UserAgent.HeaderField.Should().Be("User-Agent");
    }
}
