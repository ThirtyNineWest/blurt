using System.Globalization;
using System.Text;
using System.Text.Json;
using Blurt.Engine.STT;
using FluentAssertions;

namespace Blurt.Engine.Tests.STT;

public sealed class CleanupInstructionTests
{
    [Test]
    public void TheInstructionIsNonBlankAndCarriesNoSurroundingPadding()
    {
        CleanupInstruction.Text.Should().NotBeEmpty();
        CleanupInstruction.Text.Trim().Should().Be(CleanupInstruction.Text);
    }

    [Test]
    public void TheInstructionFitsTheCapOnLlmInstruction()
    {
        Encoding.UTF8.GetByteCount(CleanupInstruction.Text).Should().BeLessThan(2048);
        Encoding.UTF8.GetByteCount(CleanupInstruction.Text).Should().BeLessThanOrEqualTo(CleanupInstruction.CharacterCap);
    }

    [Test]
    public void TheInstructionIsTheShippedTextInFiveLfSeparatedParagraphs()
    {
        CleanupInstruction.Text.Should().StartWith(
            "You will receive a single dictated spoken-language transcript. Clean it by removing disfluencies only, then return just the cleaned text.");
        CleanupInstruction.Text.Should().EndWith("\n\nReturn only the cleaned transcript.");
        CleanupInstruction.Text.Should().NotContain("\r");
        CleanupInstruction.Text.Split("\n\n").Should().HaveCount(6);
        CleanupInstruction.Text.Split("\n\n").Should().OnlyContain(p => p.Length > 0 && !p.Contains('\n'));
    }

    [Test]
    public void AFittingInstructionIsTheOneActuallySent()
    {
        CleanupInstruction.Sendable().Should().Be(CleanupInstruction.Text);
    }

    [Test]
    public void TheInstructionsCapIsNotThePromptsCap()
    {
        CleanupInstruction.CharacterCap.Should().Be(2048);
        CleanupInstruction.CharacterCap.Should().BeLessThan(STTPrompt.CharacterCap);
    }

    [TestCase("answer")]
    [TestCase("translat")]
    [TestCase("rephrase")]
    public void TheInstructionKeepsItsSafeguards(string stem)
    {
        CleanupInstruction.Text.Should().Contain(stem);
    }

    [Test]
    public void CustomStyleInstructionsAreAppendedAfterTheUnchangedBaseInstruction()
    {
        var combined = CleanupInstruction.Sendable("  always write in lowercase \n");

        combined.Should().StartWith(CleanupInstruction.Text + CleanupInstruction.CustomStylePreamble);
        combined.Should().EndWith("always write in lowercase");
        Encoding.UTF8.GetByteCount(combined!).Should().BeLessThanOrEqualTo(CleanupInstruction.CharacterCap);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   \n ")]
    public void NilAndBlankCustomStyleLeaveTheInstructionExactlyAsShipped(string? custom)
    {
        CleanupInstruction.Sendable(custom).Should().Be(CleanupInstruction.Sendable());
    }

    [Test]
    public void AnOverBudgetCustomStyleIsTrimmedSoTheRequestStillFits()
    {
        var oversized = new string('x', CleanupInstruction.CustomStyleBudget + 100);

        var combined = CleanupInstruction.Sendable(oversized);

        Encoding.UTF8.GetByteCount(combined!).Should().Be(CleanupInstruction.CharacterCap);
        combined.Should().StartWith(CleanupInstruction.Text);
    }

    [Test]
    public void AMultiScalarEmojiCustomStyleIsTrimmedByBytesOnWholeGraphemes()
    {
        const string Emoji = "\U0001F469\U0001F3FD‍\U0001F4BB";
        var oversized = string.Concat(Enumerable.Repeat(Emoji, CleanupInstruction.CustomStyleBudget));
        var lead = CleanupInstruction.Text + CleanupInstruction.CustomStylePreamble;

        var combined = CleanupInstruction.Sendable(oversized)!;

        Encoding.UTF8.GetByteCount(combined).Should().BeLessThanOrEqualTo(CleanupInstruction.CharacterCap);
        combined.Should().StartWith(lead);
        var appended = combined[lead.Length..];
        appended.Should().NotBeEmpty();
        TextElements(appended).Should().OnlyContain(e => e == Emoji);
    }

    [Test]
    public void TheCustomStyleBudgetIsTheRealHeadroomUnderTheCap()
    {
        (Encoding.UTF8.GetByteCount(CleanupInstruction.Text)
            + Encoding.UTF8.GetByteCount(CleanupInstruction.CustomStylePreamble)
            + CleanupInstruction.CustomStyleBudget).Should().Be(CleanupInstruction.CharacterCap);
        CleanupInstruction.CustomStyleBudget.Should().BeGreaterThanOrEqualTo(200);
    }

    [Test]
    public void TheInstructionRoundTripsThroughJsonUnchanged()
    {
        var encoded = JsonSerializer.Serialize(new Dictionary<string, string> { ["instruction"] = CleanupInstruction.Text });

        var decoded = JsonSerializer.Deserialize<Dictionary<string, string>>(encoded);

        decoded!["instruction"].Should().Be(CleanupInstruction.Text);
    }

    private static IEnumerable<string> TextElements(string s)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(s);
        while (enumerator.MoveNext())
        {
            yield return enumerator.GetTextElement();
        }
    }
}
