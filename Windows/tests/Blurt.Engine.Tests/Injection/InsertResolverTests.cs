using Blurt.Engine.Injection;
using FluentAssertions;

namespace Blurt.Engine.Tests.Injection;

public sealed class InsertResolverTests
{
    private static readonly WindowIdentity Editor = new(501, "notes.txt — Editor");

    [TestCase("Second.", "First.", " Second.")]
    [TestCase("Second.", "First. ", "Second.")]
    [TestCase("Second.", "First.\n", "Second.")]
    [TestCase("Second.", null, "Second.")]
    [TestCase("Second.", "", "Second.")]
    [TestCase(" Second.", "First.", " Second.")]
    [TestCase("", "First.", "")]
    [TestCase("Second.", "First.\t", "Second.")]
    [TestCase("Second.", "First.\r", "Second.")]
    [TestCase("Second.", "First. ", "Second.")]
    [TestCase("\tSecond.", "First.", "\tSecond.")]
    [TestCase(" Second.", "First.", " Second.")]
    [TestCase("Second.", "Wave \U0001F44B", " Second.")]
    public void WithLeadingSeparatorAddsOneSpaceOnlyBetweenNonWhitespace(string text, string? priorText, string expected)
    {
        var joined = InsertResolver.WithLeadingSeparator(text, priorText);

        joined.Should().Be(expected);
    }

    [TestCase("AX.", "Old.", false, "AX.")]
    [TestCase(null, "First.", true, "First.")]
    [TestCase(null, "First.", false, null)]
    [TestCase(null, null, true, null)]
    public void SeparatorBasisPrefersPriorTextThenTheSameWindowsLastPaste(
        string? priorText, string? lastInserted, bool sameWindow, string? expected)
    {
        var basis = InsertResolver.SeparatorBasis(priorText, lastInserted, sameWindow);

        basis.Should().Be(expected);
    }

    [Test]
    public void TheSamePidAndTitleRecoverTheSpacingFromTheLastPaste()
    {
        var resolved = SecondInsert(Editor.Pid, Editor.Title);

        resolved.Should().Be(new ResolvedInsert(" Second.", Editor));
    }

    [Test]
    public void AChangedTitleIsADifferentFieldButIsRemembered()
    {
        var resolved = SecondInsert(Editor.Pid, "Untitled document — Docs");

        resolved.Text.Should().Be("Second.");
        resolved.Window.Should().Be(new WindowIdentity(Editor.Pid, "Untitled document — Docs"));
    }

    [Test]
    public void NoReadableTitleMeansNoMatchAndNothingToRemember()
    {
        var resolved = SecondInsert(Editor.Pid, null);

        resolved.Should().Be(new ResolvedInsert("Second.", null));
    }

    [Test]
    public void ADifferentTargetProcessNeverInheritsThePreviousSpacing()
    {
        var resolved = SecondInsert(777, Editor.Title);

        resolved.Text.Should().Be("Second.");
        resolved.Window.Should().Be(new WindowIdentity(777, Editor.Title));
    }

    [Test]
    public void NoCapturedTargetMeansNothingToMatchOrRemember()
    {
        var resolved = SecondInsert(null, Editor.Title);

        resolved.Should().Be(new ResolvedInsert("Second.", null));
    }

    [Test]
    public void PriorTextWinsOverTheRememberedPaste()
    {
        var sameWindow = SecondInsert(Editor.Pid, Editor.Title, priorText: "Hello ");
        var otherWindow = SecondInsert(777, "Other", priorText: "Hello");

        sameWindow.Text.Should().Be("Second.");
        otherWindow.Text.Should().Be(" Second.");
    }

    [Test]
    public void AFirstInsertHasNoMemoryToFallBackOn()
    {
        var resolved = InsertResolver.Resolve("First.", null, Editor.Title, Editor.Pid, null);

        resolved.Should().Be(new ResolvedInsert("First.", Editor));
    }

    private static ResolvedInsert SecondInsert(int? targetPid, string? windowTitle, string? priorText = null) =>
        InsertResolver.Resolve("Second.", priorText, windowTitle, targetPid, new ResolvedInsert("First.", Editor));
}
