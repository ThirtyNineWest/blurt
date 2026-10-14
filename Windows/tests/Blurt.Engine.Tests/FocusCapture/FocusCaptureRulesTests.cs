using Blurt.Engine.FocusCapture;
using FluentAssertions;

namespace Blurt.Engine.Tests.FocusCapture;

public sealed class FocusCaptureRulesTests
{
    private const string Grin = "\U0001F600";

    [Test]
    public void SelectLabelPrefersPlaceholderWhenPresent()
    {
        var label = FocusCaptureRules.SelectLabel(() => "Search", () => "desc", () => "title", () => "text field");

        label.Should().Be("Search");
    }

    [Test]
    public void SelectLabelFallsThroughBlanksInPriorityOrder()
    {
        var label = FocusCaptureRules.SelectLabel(() => "  ", () => null, () => "Subject", () => "text field");

        label.Should().Be("Subject");
    }

    [Test]
    public void SelectLabelUsesRoleDescriptionOnlyAsALastResort()
    {
        var label = FocusCaptureRules.SelectLabel(() => null, () => null, () => null, () => "text entry area");

        label.Should().Be("text entry area");
    }

    [Test]
    public void SelectLabelReturnsNullWhenEveryCandidateIsBlank()
    {
        var label = FocusCaptureRules.SelectLabel(() => " ", () => "", () => null, () => null);

        label.Should().BeNull();
    }

    [Test]
    public void SelectLabelTrimsTheChosenCandidate()
    {
        var label = FocusCaptureRules.SelectLabel(() => "  To  ", () => null, () => null, () => null);

        label.Should().Be("To");
    }

    [Test]
    public void SelectLabelStopsReadingCandidatesOnceOneAnswers()
    {
        var reads = new List<string>();
        Func<string?> Read(string name, string? value) => () =>
        {
            reads.Add(name);
            return value;
        };

        var label = FocusCaptureRules.SelectLabel(
            Read("placeholder", "Search"),
            Read("description", "desc"),
            Read("title", "title"),
            Read("roleDescription", "text field"));

        label.Should().Be("Search");
        reads.Should().Equal("placeholder");
    }

    [Test]
    public void PriorSliceReturnsTextUpToTheCaret()
    {
        FocusCaptureRules.PriorSlice("hello world", 5, 320).Should().Be("hello");
    }

    [Test]
    public void PriorSliceKeepsOnlyTheTrailingMaxCharsBeforeTheCaret()
    {
        FocusCaptureRules.PriorSlice("abcdef", 6, 3).Should().Be("def");
    }

    [TestCase(-1, 3, "def")]
    [TestCase(-5, 3, "def")]
    [TestCase(99, 10, "abcdef")]
    public void PriorSliceWithACaretOutOfRangeFallsBackToTheValuesTail(int caret, int maxChars, string expected)
    {
        FocusCaptureRules.PriorSlice("abcdef", caret, maxChars).Should().Be(expected);
    }

    [TestCase("", 0)]
    [TestCase("hello", 0)]
    public void PriorSliceReturnsNullForAnEmptyValueOrAZeroLengthPrefix(string full, int caret)
    {
        FocusCaptureRules.PriorSlice(full, caret, 320).Should().BeNull();
    }

    [TestCase("Hello ", 6, "Hello ")]
    [TestCase("  Hello world", 8, "  Hello ")]
    [TestCase("   ", 2, "  ")]
    public void PriorSliceKeepsSurroundingWhitespaceSoCaretOffsetsStayValid(string full, int caret, string expected)
    {
        FocusCaptureRules.PriorSlice(full, caret, 320).Should().Be(expected);
    }

    [Test]
    public void PriorSliceTreatsTheCaretAsAUtf16Offset()
    {
        var full = Grin + Grin + "ab";

        FocusCaptureRules.PriorSlice(full, 4, 320).Should().Be(Grin + Grin);
        FocusCaptureRules.PriorSlice(full, 2, 320).Should().Be(Grin);
    }

    [Test]
    public void PriorSliceFallsBackToTheWholeValueWhenTheCaretSplitsASurrogatePair()
    {
        var full = Grin + "b";

        FocusCaptureRules.PriorSlice(full, 1, 320).Should().Be(full);
        FocusCaptureRules.PriorSlice(full, 99, 320).Should().Be(full);
    }

    [Test]
    public void PriorSliceNeverStartsInsideASurrogatePair()
    {
        var full = "a" + Grin + "b";

        var slice = FocusCaptureRules.PriorSlice(full, full.Length, 2);

        slice.Should().Be("b");
    }

    [Test]
    public void ClipCapsOverlongTextAndPassesShortTextThrough()
    {
        FocusCaptureRules.Clip("abcdefghij", 4).Should().Be("abcd");
        FocusCaptureRules.Clip("abc", 4).Should().Be("abc");
        FocusCaptureRules.Clip(null, 4).Should().BeNull();
    }

    [Test]
    public void ClipNeverEndsInsideASurrogatePair()
    {
        FocusCaptureRules.Clip("a" + Grin + "b", 2).Should().Be("a");
    }

    [TestCase("​")]
    [TestCase("​​")]
    [TestCase("﻿‎")]
    [TestCase("­⁠͏")]
    [TestCase("️")]
    [TestCase("\U000E0100")]
    [TestCase("")]
    [TestCase(null)]
    public void VisibleTextOrNilCollapsesAnAllInvisibleReadToNull(string? text)
    {
        FocusCaptureRules.VisibleTextOrNil(text).Should().BeNull();
    }

    [TestCase("Hello")]
    [TestCase("Hello​")]
    public void VisibleTextOrNilKeepsTextWithAnyVisibleContentUnchanged(string text)
    {
        FocusCaptureRules.VisibleTextOrNil(text).Should().Be(text);
    }

    [TestCase("   ")]
    [TestCase("hello ")]
    [TestCase("\t\n")]
    public void VisibleTextOrNilPreservesPlainWhitespace(string text)
    {
        FocusCaptureRules.VisibleTextOrNil(text).Should().Be(text);
    }

    [Test]
    public void AnEditControlTypeIsEditable()
    {
        FocusCaptureRules.IsEditableTarget(isEditControlType: true, valueSettable: false, hasInsertionPoint: false)
            .Should().BeTrue();
    }

    [Test]
    public void ASettableValueIsEditableWithAnUnknownControlType()
    {
        FocusCaptureRules.IsEditableTarget(isEditControlType: false, valueSettable: true, hasInsertionPoint: false)
            .Should().BeTrue();
    }

    [Test]
    public void AnInsertionPointIsEditableWithAnUnknownControlType()
    {
        FocusCaptureRules.IsEditableTarget(isEditControlType: false, valueSettable: false, hasInsertionPoint: true)
            .Should().BeTrue();
    }

    [Test]
    public void AControlWithNoEditableSignalIsNotEditable()
    {
        FocusCaptureRules.IsEditableTarget(isEditControlType: false, valueSettable: false, hasInsertionPoint: false)
            .Should().BeFalse();
    }

    [Test]
    public void TheCapsMatchTheContextBudget()
    {
        FocusCaptureRules.MaxPriorChars.Should().Be(320);
        FocusCaptureRules.MaxSelectedChars.Should().Be(320);
        FocusCaptureRules.MaxWindowTitleChars.Should().Be(120);
        FocusCaptureRules.MaxFieldLabelChars.Should().Be(80);
    }
}
