using Blurt.Engine.Update;
using FluentAssertions;

namespace Blurt.Engine.Tests.Update;

public sealed class SemanticVersionTests
{
    [Test]
    public void ParsesABareDottedVersion()
    {
        SemanticVersion.TryParse("0.1.30", out var version).Should().BeTrue();

        version.Description.Should().Be("0.1.30");
    }

    [Test]
    public void ParsesAVPrefixedTag()
    {
        SemanticVersion.TryParse("v0.1.30", out var lower).Should().BeTrue();
        SemanticVersion.TryParse("V2.0.0", out var upper).Should().BeTrue();

        lower.Description.Should().Be("0.1.30");
        upper.Description.Should().Be("2.0.0");
    }

    [Test]
    public void OrdersByNumericComponentsNotLexically()
    {
        Parse("0.1.9").Should().BeLessThan(Parse("0.1.10"));
        Parse("0.1.30").Should().BeLessThan(Parse("0.2.0"));
        Parse("1.0.0").Should().BeLessThan(Parse("2.0.0"));
    }

    [Test]
    public void TreatsMissingTrailingComponentsAsZero()
    {
        var a = Parse("1.2");
        var b = Parse("1.2.0");

        a.Should().Be(b);
        (a < b).Should().BeFalse();
    }

    [Test]
    public void AShorterVersionSortsBelowALongerOneWhoseExtraComponentIsNonZero()
    {
        // `1.2 == 1.2.0` above is satisfied even by an ordering that only walks the *shorter*
        // version's components — and that shortcut would also call 1.2 and 1.2.1 equal, which would
        // make UpdateChecker's `current < latest` gate report "up to date" for a real published
        // release. Pin both directions and the inequality.
        var shortVersion = Parse("1.2");
        var longer = Parse("1.2.1");

        (shortVersion < longer).Should().BeTrue();
        (longer < shortVersion).Should().BeFalse();
        shortVersion.Should().NotBe(longer);
    }

    [Test]
    public void ComparisonKeepsGoingPastTheThirdComponent()
    {
        var release = Parse("1.2.3");
        var recut = Parse("1.2.3.4");
        var laterRecut = Parse("1.2.3.5");

        (release < recut).Should().BeTrue();
        (recut < laterRecut).Should().BeTrue();
        (laterRecut < recut).Should().BeFalse();
    }

    [Test]
    public void EqualVersionsAreNotLessThanEachOther()
    {
        var a = Parse("0.1.30");
        var b = Parse("0.1.30");

        (a < b).Should().BeFalse();
    }

    [Test]
    public void RejectsMalformedStrings()
    {
        SemanticVersion.TryParse("", out _).Should().BeFalse();
        SemanticVersion.TryParse("1.x.3", out _).Should().BeFalse();
        SemanticVersion.TryParse("-1.0", out _).Should().BeFalse();
        SemanticVersion.TryParse("abc", out _).Should().BeFalse();
    }

    [Test]
    public void ADoubledOrDanglingSeparatorIsMalformedNotSilentlyCollapsed()
    {
        // The split keeps empty subsequences, so an empty component is a component that isn't a
        // number — malformed — rather than one that quietly disappears. Collapsing them instead
        // would read "1.2." as 1.2 and "1..2" as 1.2, and UpdateChecker gates the update decision on
        // the parsed tag: it would then compare the running build against a version the release
        // never actually stated.
        SemanticVersion.TryParse("1..2", out _).Should().BeFalse();
        SemanticVersion.TryParse("1.2.", out _).Should().BeFalse();
        SemanticVersion.TryParse(".1.2", out _).Should().BeFalse();
        SemanticVersion.TryParse(".", out _).Should().BeFalse();
        SemanticVersion.TryParse("v", out _).Should().BeFalse();
    }

    private static SemanticVersion Parse(string text)
    {
        SemanticVersion.TryParse(text, out var version).Should().BeTrue();
        return version;
    }
}
