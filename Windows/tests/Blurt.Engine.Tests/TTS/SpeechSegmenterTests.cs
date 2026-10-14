using Blurt.Engine.TTS;
using FluentAssertions;

namespace Blurt.Engine.Tests.TTS;

public sealed class SpeechSegmenterTests
{
    [Test]
    public void CutsAtSentenceEndsKeepingThePunctuation()
    {
        SpeechSegmenter.Segments("It works. Does it? Yes it does!")
            .Should().Equal("It works.", "Does it?", "Yes it does!");
    }

    [Test]
    public void AClosingCurlyQuoteStillEndsTheSentence()
    {
        SpeechSegmenter.Segments("She said “you uploaded me.” The clock ticks.")
            .Should().Equal("She said “you uploaded me.”", "The clock ticks.");
    }

    [Test]
    public void DecimalsAndVersionsAreNotSentenceEnds()
    {
        SpeechSegmenter.Segments("Pi is 3.14 in v1.2 today.").Should().Equal("Pi is 3.14 in v1.2 today.");
    }

    [Test]
    public void OneWordSentencesJoinTheNext()
    {
        SpeechSegmenter.Segments("Dr. Smith is in. Go.").Should().Equal("Dr. Smith is in. Go.");
    }

    [Test]
    public void AShortTrailingSentenceJoinsThePreviousOne()
    {
        SpeechSegmenter.Segments("That went well. Thanks.").Should().Equal("That went well. Thanks.");
        SpeechSegmenter.Segments("Hi.").Should().Equal("Hi.");
    }

    [Test]
    public void LineBreaksEndASegmentEvenWithoutPunctuation()
    {
        SpeechSegmenter.Segments("Release notes here\n\nFixed the paste bug")
            .Should().Equal("Release notes here", "Fixed the paste bug");
    }

    [Test]
    public void BlankInputYieldsNothingToSend()
    {
        SpeechSegmenter.Segments("  \n ").Should().BeEmpty();
    }

    [Test]
    public void ARunawaySentenceSplitsAtWhitespaceUnderTheCap()
    {
        var pieces = SpeechSegmenter.Segments("alpha beta gamma delta epsilon", 12);

        pieces.Should().Equal("alpha beta", "gamma delta", "epsilon");
        pieces.Should().OnlyContain(piece => piece.Length <= 12);
    }

    [Test]
    public void ARunWithNoWhitespaceIsCutHardAtTheCap()
    {
        SpeechSegmenter.Split("abcdefghij", 4).Should().Equal("abcd", "efgh", "ij");
    }
}
