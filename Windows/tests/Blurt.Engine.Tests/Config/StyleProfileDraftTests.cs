using System.Text;
using Blurt.Engine.Config;
using FluentAssertions;

namespace Blurt.Engine.Tests.Config;

public sealed class StyleProfileDraftTests
{
    [Test]
    public void AddingStopsAtTheProfileLimit()
    {
        StyleProfileDraft.CanAdd(StyleProfileStore.ProfileLimit - 1).Should().BeTrue();

        StyleProfileDraft.CanAdd(StyleProfileStore.ProfileLimit).Should().BeFalse();
    }

    [Test]
    public void ANameIsCutToTheNameLimit()
    {
        var longName = new string('n', StyleProfileStore.NameLimit + 5);

        StyleProfileDraft.ClampName(longName).Should().HaveLength(StyleProfileStore.NameLimit);
        StyleProfileDraft.ClampName("Casual").Should().Be("Casual");
    }

    [Test]
    public void InstructionsAreCutToTheByteLimitWithoutSplittingACharacter()
    {
        var text = "ab" + "é";

        var clamped = StyleProfileDraft.ClampInstructions(text, 3);

        clamped.Should().Be("ab");
        Encoding.UTF8.GetByteCount(clamped).Should().BeLessThanOrEqualTo(3);
    }

    [Test]
    public void TheCounterCountsUtf8BytesAgainstTheLimit()
    {
        StyleProfileDraft.Counter("héllo", 2048).Should().Be("6/2048");
        StyleProfileDraft.Counter(string.Empty, 2048).Should().Be("0/2048");
    }

    [Test]
    public void SavingNeedsANameAndInstructions()
    {
        StyleProfileDraft.CanSave("Casual", "lowercase").Should().BeTrue();

        StyleProfileDraft.CanSave("  ", "lowercase").Should().BeFalse();
        StyleProfileDraft.CanSave("Casual", "\n").Should().BeFalse();
    }

    [Test]
    public void AnEditReplacesTheProfileWithTheSameIdInPlace()
    {
        var first = new StyleProfile("One", "a");
        var second = new StyleProfile("Two", "b");
        var edited = second with { Name = "Renamed" };

        var updated = StyleProfileDraft.Upserted([first, second], edited);

        updated.Should().Equal(first, edited);
    }

    [Test]
    public void ANewProfileIsAppended()
    {
        var first = new StyleProfile("One", "a");
        var added = new StyleProfile("New", "c");

        var updated = StyleProfileDraft.Upserted([first], added);

        updated.Should().Equal(first, added);
    }

    [Test]
    public void RemovingDropsOnlyTheNamedProfile()
    {
        var first = new StyleProfile("One", "a");
        var second = new StyleProfile("Two", "b");

        var updated = StyleProfileDraft.Removed([first, second], first.Id);

        updated.Should().Equal(second);
    }
}
