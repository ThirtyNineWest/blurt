using Blurt.Engine.Config;
using Blurt.Engine.Pipeline;
using FluentAssertions;

namespace Blurt.Engine.Tests.Pipeline;

public sealed class TextShortcutExpanderTests
{
    private static readonly TextShortcut Email = new("personal email", "me@example.com");
    private static readonly TextShortcut Linktree = new("personal link tree", "https://linktr.ee/someone");
    private static readonly TextShortcut Cal = new("personal cal.com", "https://cal.com/someone");

    [Test]
    public void ReplacesATriggerMidSentenceIgnoringCase()
    {
        TextShortcutExpander.Expand("You can reach me at Personal Email anytime.", [Email])
            .Should().Be("You can reach me at me@example.com anytime.");
    }

    [Test]
    public void ADictationThatIsOnlyTheTriggerPastesTheBareExpansion()
    {
        TextShortcutExpander.Expand("Personal email.", [Email]).Should().Be("me@example.com");
        TextShortcutExpander.Expand(" personal email ", [Email]).Should().Be(" me@example.com ");
    }

    [Test]
    public void SeparatorsBetweenWordsAreLoose()
    {
        TextShortcutExpander.Expand("my personal Linktree is here", [Linktree]).Should().Be("my https://linktr.ee/someone is here");
        TextShortcutExpander.Expand("book at personal Cal.com.", [Cal]).Should().Be("book at https://cal.com/someone.");
        TextShortcutExpander.Expand("send it to personal-email", [Email]).Should().Be("send it to me@example.com");
    }

    [TestCase("I finished my work. Email me later.")]
    [TestCase("at work, email is slow")]
    [TestCase("work! Email")]
    public void NeverMatchesAcrossASentenceOrClauseBreak(string text)
    {
        var work = new TextShortcut("work email", "me@work.example");

        TextShortcutExpander.Expand(text, [work]).Should().Be(text);
    }

    [Test]
    public void APeriodInsideAPhraseIsNotABreak()
    {
        var work = new TextShortcut("work email", "me@work.example");

        TextShortcutExpander.Expand("my work.email", [work]).Should().Be("my me@work.example");
    }

    [Test]
    public void ATriggerWithAnApostropheOrOtherSymbolMatchesAsSpelled()
    {
        var mom = new TextShortcut("mom's address", "1 Main St");
        var rnd = new TextShortcut("R&D budget", "$2M");

        TextShortcutExpander.Expand("send to mom's address", [mom]).Should().Be("send to 1 Main St");
        TextShortcutExpander.Expand("send to Mom’s address", [mom]).Should().Be("send to 1 Main St");
        TextShortcutExpander.Expand("the R&D budget", [rnd]).Should().Be("the $2M");
    }

    [Test]
    public void RedactingPutsPastedExpansionsBackToTheirTriggers()
    {
        TextShortcutExpander.RedactingExpansions("Mail me@example.com.", [Email]).Should().Be("Mail personal email.");
        TextShortcutExpander.RedactingExpansions("Mail me@example.com ", [Email]).Should().Be("Mail personal email ");
        TextShortcutExpander.RedactingExpansions("nothing here", [Email]).Should().Be("nothing here");
    }

    [Test]
    public void RedactingCatchesAnExpansionWhoseHeadTheCaptureClipped()
    {
        TextShortcutExpander.RedactingExpansions("ample.com and more", [Email]).Should().Be("personal email and more");
        TextShortcutExpander.RedactingExpansions("com and more", [Email]).Should().Be("com and more");
    }

    [Test]
    public void WholeWords()
    {
        const string text = "That was impersonal emails, honestly.";

        TextShortcutExpander.Expand(text, [Email]).Should().Be(text);
    }

    [Test]
    public void ReplacesEveryOccurrenceAndTheLongestTriggerWins()
    {
        var work = new TextShortcut("personal email work", "me@work.example");

        TextShortcutExpander.Expand("personal email work or personal email", [Email, work])
            .Should().Be("me@work.example or me@example.com");
    }

    [Test]
    public void AnExpansionIsInsertedLiterallyAndNeverReExpanded()
    {
        var tricky = new TextShortcut("price tag", "$1 \\0 personal email");

        TextShortcutExpander.Expand("the price tag", [tricky, Email]).Should().Be("the $1 \\0 personal email");
    }

    [Test]
    public void NoShortcutsOrAPunctuationOnlyTriggerLeavesTheTextAlone()
    {
        var junk = new TextShortcut("...", "x");

        TextShortcutExpander.Expand("hello", []).Should().Be("hello");
        TextShortcutExpander.Expand("wait...", [junk]).Should().Be("wait...");
    }

    [Test]
    public void WordsAreTheLetterAndDigitRuns()
    {
        TextShortcutExpander.Words("personal cal.com").Should().Equal("personal", "cal", "com");
        TextShortcutExpander.Words("Mom’s address").Should().Equal("Mom", "s", "address");
        TextShortcutExpander.Words("...").Should().BeEmpty();
    }

    [Test]
    public void WordsCountEveryNumeralCategory()
    {
        TextShortcutExpander.Words("½ cup").Should().Equal("½", "cup");
    }

    [Test]
    public void ATriggerWithANumeralOnlyMatchesWithTheNumeral()
    {
        var half = new TextShortcut("½ cup", "half a cup");

        TextShortcutExpander.Expand("add a cup now", [half]).Should().Be("add a cup now");
        TextShortcutExpander.Expand("add ½ cup now", [half]).Should().Be("add half a cup now");
    }
}
