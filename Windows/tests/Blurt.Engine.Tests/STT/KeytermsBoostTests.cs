using System.Text;
using Blurt.Engine.STT;
using FluentAssertions;

namespace Blurt.Engine.Tests.STT;

public sealed class KeytermsBoostTests
{
    [Test]
    public void NoTermsSendsNoField()
    {
        KeytermsBoost.Fitted([]).Should().BeEmpty();
    }

    [Test]
    public void AnAllBlankListSendsNoField()
    {
        KeytermsBoost.Fitted(["", "   ", "\n\t"]).Should().BeEmpty();
    }

    [Test]
    public void TermsPassThroughTrimmedInTheUsersOrder()
    {
        KeytermsBoost.Fitted(["AssemblyAI", " LeMUR ", "Blurt"]).Should().Equal("AssemblyAI", "LeMUR", "Blurt");
    }

    [Test]
    public void BlankEntriesAreDroppedFromAmongRealTerms()
    {
        KeytermsBoost.Fitted(["AssemblyAI", "  ", "LeMUR"]).Should().Equal("AssemblyAI", "LeMUR");
    }

    [Test]
    public void AnOversizedListIsFittedToTheCapKeepingWholeLeadingTerms()
    {
        var terms = Enumerable.Range(0, 2000).Select(i => $"term{i}").ToArray();

        var fitted = KeytermsBoost.Fitted(terms);

        fitted.Sum(t => Encoding.UTF8.GetByteCount(t)).Should().BeLessThanOrEqualTo(KeytermsBoost.CharacterCap);
        fitted[0].Should().Be("term0");
        fitted.Should().HaveCountLessThan(terms.Length);
        fitted.Should().OnlyContain(t => terms.Contains(t));
    }

    [Test]
    public void TheByteCapCountsUtf8BytesNotCharacters()
    {
        var term = new string('é', 600);

        var fitted = KeytermsBoost.Fitted([term, term]);

        fitted.Should().ContainSingle();
    }

    [Test]
    public void ASingleTermLargerThanTheCapSendsNoFieldAtAll()
    {
        KeytermsBoost.Fitted([new string('k', KeytermsBoost.CharacterCap + 1)]).Should().BeEmpty();
    }

    [Test]
    public void ATermThatDoesNotFitStopsTheListWithoutDroppingEarlierOnes()
    {
        var huge = new string('k', KeytermsBoost.CharacterCap);

        KeytermsBoost.Fitted(["Blurt", huge, "LeMUR"]).Should().Equal("Blurt");
    }

    [Test]
    public void AListUnderTheByteCapIsStillCutToTheTermCap()
    {
        var terms = Enumerable.Range(0, 150).Select(i => $"t{i}").ToArray();

        var fitted = KeytermsBoost.Fitted(terms);

        terms.Sum(t => Encoding.UTF8.GetByteCount(t)).Should().BeLessThan(KeytermsBoost.CharacterCap);
        fitted.Should().HaveCount(KeytermsBoost.TermCap);
        fitted[0].Should().Be("t0");
        fitted[^1].Should().Be("t99");
    }

    [Test]
    public void AListAtExactlyTheTermCapIsSentWhole()
    {
        KeytermsBoost.Fitted(Enumerable.Range(0, 100).Select(i => $"t{i}")).Should().HaveCount(100);
        KeytermsBoost.Fitted(Enumerable.Range(0, 101).Select(i => $"t{i}")).Should().HaveCount(100);
    }

    [Test]
    public void TheBoostCapIsItsOwnNumber()
    {
        KeytermsBoost.CharacterCap.Should().Be(2048);
        KeytermsBoost.CharacterCap.Should().NotBe(STTPrompt.CharacterCap);
        KeytermsBoost.TermCap.Should().Be(100);
    }
}
