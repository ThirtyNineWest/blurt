using System.Text;
using FluentAssertions;

namespace Blurt.Engine.Tests;

public sealed class StringNormalizationTests
{
    private const string Family = "\U0001F468‍\U0001F469‍\U0001F467";

    [Test]
    public void NullStaysNull()
    {
        string? value = null;

        value.TrimmedNonEmpty().Should().BeNull();
    }

    [Test]
    public void EmptyStringBecomesNull()
    {
        "".TrimmedNonEmpty().Should().BeNull();
    }

    [TestCase("   ")]
    [TestCase("\t")]
    [TestCase("\n")]
    [TestCase(" \t\n ")]
    public void BlankStringsBecomeNull(string value)
    {
        value.TrimmedNonEmpty().Should().BeNull();
    }

    [TestCase("  hello  ", "hello")]
    [TestCase("\nhello\t", "hello")]
    public void SurroundingWhitespaceAndNewlinesAreTrimmed(string value, string expected)
    {
        value.TrimmedNonEmpty().Should().Be(expected);
    }

    [Test]
    public void ACleanStringPassesThroughUnchanged()
    {
        "hello".TrimmedNonEmpty().Should().Be("hello");
    }

    [TestCase("  a b c  ", "a b c")]
    [TestCase("line one\nline two", "line one\nline two")]
    public void InteriorWhitespaceIsPreserved(string value, string expected)
    {
        value.TrimmedNonEmpty().Should().Be(expected);
    }

    [Test]
    public void AStringThatFitsIsReturnedWhole()
    {
        "hello".PrefixMaxUtf8Bytes(5).Should().Be("hello");
        "hello".PrefixMaxUtf8Bytes(100).Should().Be("hello");
    }

    [Test]
    public void AsciiIsCutToTheByteBudget()
    {
        "hello world".PrefixMaxUtf8Bytes(5).Should().Be("hello");
    }

    [Test]
    public void AMultiByteCharacterIsNeverSplit()
    {
        var result = "café".PrefixMaxUtf8Bytes(4);

        result.Should().Be("caf");
    }

    [Test]
    public void AFamilyEmojiAtTheBoundaryIsDroppedWhole()
    {
        var text = "hi " + Family;
        var familyBytes = Encoding.UTF8.GetByteCount(Family);

        var result = text.PrefixMaxUtf8Bytes(3 + familyBytes - 1);

        result.Should().Be("hi ");
    }

    [Test]
    public void AFamilyEmojiThatFitsExactlyIsKept()
    {
        var text = "hi " + Family + "!";

        var result = text.PrefixMaxUtf8Bytes(3 + Encoding.UTF8.GetByteCount(Family));

        result.Should().Be("hi " + Family);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void ANonPositiveBudgetYieldsEmpty(int maxBytes)
    {
        "hello".PrefixMaxUtf8Bytes(maxBytes).Should().BeEmpty();
    }
}
