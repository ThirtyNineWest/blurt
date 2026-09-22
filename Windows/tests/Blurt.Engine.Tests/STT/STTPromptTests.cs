using System.Globalization;
using Blurt.Engine.STT;
using FluentAssertions;
using static Blurt.Engine.Tests.Stubs.TranscriberTestSupport;

namespace Blurt.Engine.Tests.STT;

public sealed class STTPromptTests
{
    private static IEnumerable<TestCaseData> Cases()
    {
        yield return new TestCaseData(null, "").SetName("NullContextSendsNoPrompt");
        yield return new TestCaseData(Context(), "").SetName("EmptyContextSendsNoPrompt");
        yield return new TestCaseData(Context(priorText: "  \n\t"), "").SetName("WhitespaceOnlyPriorTextSendsNoPrompt");
        yield return new TestCaseData(Context(priorText: "and then the build finished"), "and then the build finished")
            .SetName("PriorTextAloneIsSentVerbatim");
        yield return new TestCaseData(Context(priorText: "  hello  "), "hello").SetName("PriorTextIsTrimmed");
        yield return new TestCaseData(Context(priorText: "Dear Sam,\n\nthanks for"), "Dear Sam,\n\nthanks for")
            .SetName("NewlinesInsideThePriorChunkArePreserved");
        yield return new TestCaseData(Context(recentTranscripts: ["First one.", "Second one."]), "First one.\nSecond one.")
            .SetName("RecentDictationsAloneAreSentOldestFirst");
        yield return new TestCaseData(Context(priorText: "thanks for", recentTranscripts: ["First one."]), "First one.\nthanks for")
            .SetName("HistoryThenPriorChunkWithThePriorChunkLast");
        yield return new TestCaseData(Context(recentTranscripts: ["real", "   ", "\n", "also real"]), "real\nalso real")
            .SetName("BlankHistoryEntriesAreDropped");
        yield return new TestCaseData(
                Context(appName: "Slack", windowTitle: "#eng-backend", fieldLabel: "Message", selectedText: "the draft", keyTerms: ["Blurt"]),
                "")
            .SetName("EveryOtherSignalWithoutHistoryOrPriorTextSendsNoPrompt");
    }

    [TestCaseSource(nameof(Cases))]
    public void TextMapsTheHistoryAndThePriorChunkToOneOrderedPrompt(TranscriptionContext? context, string expected)
    {
        STTPrompt.Text(context).Should().Be(expected);
    }

    [Test]
    public void HistoryThePriorChunkAlreadyCarriesIsNotSentTwice()
    {
        var context = Context(appName: "Slack", priorText: "Let's ship Friday. ", recentTranscripts: ["Morning all.", "Let's ship Friday."]);

        STTPrompt.Text(context).Should().Be("Morning all.\nLet's ship Friday.");
    }

    [Test]
    public void AWholeRunOfDictationsIntoOneFieldCollapses()
    {
        var context = Context(appName: "Slack", priorText: "One. Two. Three.", recentTranscripts: ["Older elsewhere.", "One.", "Two.", "Three."]);

        STTPrompt.Text(context).Should().Be("Older elsewhere.\nOne. Two. Three.");
    }

    [Test]
    public void DedupeStopsAtTheFirstEntryThePriorChunkDoesNotCarry()
    {
        var context = Context(appName: "Slack", priorText: "Two. typed by hand. Three.", recentTranscripts: ["Two.", "Three."]);

        STTPrompt.Text(context).Should().Be("Two.\nTwo. typed by hand. Three.");
    }

    [Test]
    public void HistoryThatMerelyAppearsInsideThePriorChunkIsStillSent()
    {
        var context = Context(appName: "Slack", priorText: "Ship it. Then follow up.", recentTranscripts: ["Ship it."]);

        STTPrompt.Text(context).Should().Be("Ship it.\nShip it. Then follow up.");
    }

    [Test]
    public void AHistoryDeeperThanTheCapIsFittedKeepingTheNewest()
    {
        var history = Enumerable.Range(1, 40).Select(i => $"{i} " + string.Concat(Enumerable.Repeat("word ", 39)) + "word").ToArray();

        var prompt = STTPrompt.Text(Context(recentTranscripts: history));

        Runes(prompt).Should().BeLessThanOrEqualTo(STTPrompt.CharacterCap);
        var kept = prompt.Split(STTPrompt.TurnSeparator);
        kept[^1].Should().Be(history[^1]);
        kept.Should().Equal(history[^kept.Length..]);
    }

    [Test]
    public void TheWholeHistoryGoesWhenItFitsHoweverManyEntries()
    {
        var history = Enumerable.Range(1, 120).Select(i => $"u{i}").ToArray();

        var prompt = STTPrompt.Text(Context(priorText: "at the cursor", recentTranscripts: history));

        prompt.Split(STTPrompt.TurnSeparator).Should().HaveCount(121);
        prompt.Should().EndWith("\nat the cursor");
    }

    [Test]
    public void TheSeparatorsAreChargedAgainstTheCap()
    {
        var history = Enumerable.Range(1, 64).Select(_ => new string('a', 64)).ToArray();

        var prompt = STTPrompt.Text(Context(recentTranscripts: history));

        Runes(prompt).Should().BeLessThanOrEqualTo(STTPrompt.CharacterCap);
    }

    [Test]
    public void TheCapCountsScalarsRatherThanUtf16UnitsOrGraphemes()
    {
        const string Family = "\U0001F468‍\U0001F469‍\U0001F467";

        var prompt = STTPrompt.Text(Context(priorText: string.Concat(Enumerable.Repeat(Family, 820))));

        Runes(prompt).Should().BeLessThanOrEqualTo(STTPrompt.CharacterCap);
        new StringInfo(prompt).LengthInTextElements.Should().BeLessThan(STTPrompt.CharacterCap);
    }

    [Test]
    public void AccentedTextUsesTheFullScalarBudget()
    {
        var prompt = STTPrompt.Text(Context(priorText: new string('é', STTPrompt.CharacterCap + 10)));

        Runes(prompt).Should().Be(STTPrompt.CharacterCap);
    }

    [Test]
    public void ASingleOverLongEntryIsClippedToTheCapKeepingItsTail()
    {
        var longPrior = string.Concat(Enumerable.Repeat("word ", 2000)) + "end";

        var prompt = STTPrompt.Text(Context(priorText: longPrior));

        Runes(prompt).Should().Be(STTPrompt.CharacterCap);
        prompt.Should().EndWith("end");
        prompt.Should().NotContain(STTPrompt.TurnSeparator);
    }

    [Test]
    public void AClipNeverSplitsASurrogatePair()
    {
        var prior = "x" + string.Concat(Enumerable.Repeat("\U0001F600", STTPrompt.CharacterCap));

        var prompt = STTPrompt.Text(Context(priorText: prior));

        Runes(prompt).Should().Be(STTPrompt.CharacterCap);
        char.IsLowSurrogate(prompt[0]).Should().BeFalse();
    }

    [Test]
    public void AnOlderEntryThatDoesNotFitIsDroppedWhole()
    {
        var old = new string('a', STTPrompt.CharacterCap - 6);

        var prompt = STTPrompt.Text(Context(priorText: "newest", recentTranscripts: [old, "middle"]));

        prompt.Should().Be("middle\nnewest");
    }

    private static readonly TranscriptionContext RichContext = Context(
        appName: "Slack",
        windowTitle: "#eng-backend",
        fieldLabel: "Message",
        priorText: "thanks for",
        selectedText: "the draft",
        recentTranscripts: ["Shipping the release notes now."],
        keyTerms: ["Blurt"]);

    [TestCase("Slack")]
    [TestCase("#eng-backend")]
    [TestCase("Message")]
    [TestCase("the draft")]
    [TestCase("Blurt")]
    public void NoLocalOnlySignalReachesTheWire(string signal)
    {
        STTPrompt.Text(RichContext).Should().NotContain(signal);
    }

    [Test]
    public void ThePromptIsExactlyTheHistoryThenThePriorChunk()
    {
        STTPrompt.Text(RichContext).Should().Be("Shipping the release notes now.\nthanks for");
    }

    private static int Runes(string s) => s.EnumerateRunes().Count();
}
