using Blurt.Engine.Config;
using Blurt.Engine.STT;
using FluentAssertions;

namespace Blurt.Engine.Tests.STT;

public sealed class TranscriptionContextTests
{
    private static TranscriptionContext Context(
        string? appName = null,
        string? windowTitle = null,
        string? fieldLabel = null,
        string? priorText = null,
        string? selectedText = null,
        string[]? recentTranscripts = null,
        string[]? keyTerms = null,
        TextShortcut[]? textShortcuts = null,
        bool targetIsSecure = false) =>
        new(appName, windowTitle, fieldLabel, priorText, selectedText, recentTranscripts ?? [], keyTerms ?? [], textShortcuts ?? [], targetIsSecure);

    [Test]
    public void EveryFieldAbsentIsEmpty()
    {
        Context().IsEmpty.Should().BeTrue();
    }

    [Test]
    public void WhitespaceOnlyFieldsAreEmpty()
    {
        Context(appName: "   ", priorText: "\n\t ").IsEmpty.Should().BeTrue();
    }

    [Test]
    public void ARealAppNameMakesItNonEmpty()
    {
        Context(appName: "Slack").IsEmpty.Should().BeFalse();
    }

    [Test]
    public void RealPriorTextMakesItNonEmpty()
    {
        Context(priorText: "hello there").IsEmpty.Should().BeFalse();
    }

    [Test]
    public void RealSelectedTextMakesItNonEmpty()
    {
        Context(selectedText: "highlighted").IsEmpty.Should().BeFalse();
    }

    [Test]
    public void WhitespaceOnlySelectedTextStaysEmpty()
    {
        Context(selectedText: "  \n").IsEmpty.Should().BeTrue();
    }

    [Test]
    public void ARealWindowTitleOrFieldLabelMakesItNonEmpty()
    {
        Context(windowTitle: "Re: Q3 pricing").IsEmpty.Should().BeFalse();
        Context(fieldLabel: "To").IsEmpty.Should().BeFalse();
    }

    [Test]
    public void KeyTermsAloneMakeItNonEmpty()
    {
        Context(keyTerms: ["Blurt"]).IsEmpty.Should().BeFalse();
    }

    [Test]
    public void RecentDictationsAloneMakeItNonEmpty()
    {
        Context(recentTranscripts: ["Said this before."]).IsEmpty.Should().BeFalse();
    }

    [Test]
    public void ASecureTargetAloneMakesItNonEmpty()
    {
        Context(targetIsSecure: true).IsEmpty.Should().BeFalse();
    }

    [Test]
    public void EqualityComparesEveryField()
    {
        Context(appName: "Notes", priorText: "x").Should().Be(Context(appName: "Notes", priorText: "x"));
        Context(appName: "Notes", priorText: "x", keyTerms: ["a"], recentTranscripts: ["r"])
            .Should().Be(Context(appName: "Notes", priorText: "x", keyTerms: ["a"], recentTranscripts: ["r"]));
        Context(appName: "Notes", priorText: "x").Should().NotBe(Context(appName: "Notes", priorText: "y"));
        Context(appName: "Notes").Should().NotBe(Context());
        Context(appName: "Notes", priorText: "x", selectedText: "a").Should().NotBe(Context(appName: "Notes", priorText: "x", selectedText: "b"));
        Context(appName: "Notes", windowTitle: "a", priorText: "x").Should().NotBe(Context(appName: "Notes", windowTitle: "b", priorText: "x"));
        Context(appName: "Notes", fieldLabel: "To", priorText: "x").Should().NotBe(Context(appName: "Notes", fieldLabel: "Subject", priorText: "x"));
        Context(appName: "Notes", priorText: "x", keyTerms: ["a"]).Should().NotBe(Context(appName: "Notes", priorText: "x", keyTerms: ["b"]));
        Context(appName: "Notes", priorText: "x", recentTranscripts: ["a"]).Should().NotBe(Context(appName: "Notes", priorText: "x", recentTranscripts: ["b"]));
        Context(targetIsSecure: true).Should().NotBe(Context());
    }

    [Test]
    public void EqualContextsHashEqually()
    {
        var a = Context(appName: "Notes", keyTerms: ["a"], recentTranscripts: ["r"]);
        var b = Context(appName: "Notes", keyTerms: ["a"], recentTranscripts: ["r"]);

        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Test]
    public void TextShortcutsAloneDoNotMakeAContextNonEmpty()
    {
        Context(textShortcuts: [new TextShortcut("personal email", "me@example.com")]).IsEmpty.Should().BeTrue();
    }

    [Test]
    public void ContextsDifferingOnlyInTextShortcutsAreNotEqual()
    {
        var email = new TextShortcut("personal email", "me@example.com");

        Context(appName: "Notes", textShortcuts: [email]).Should().Be(Context(appName: "Notes", textShortcuts: [email]));
        Context(appName: "Notes", textShortcuts: [email]).Should().NotBe(Context(appName: "Notes"));
    }
}
