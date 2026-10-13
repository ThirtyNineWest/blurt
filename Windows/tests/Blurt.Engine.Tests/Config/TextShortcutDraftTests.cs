using Blurt.Engine.Config;
using FluentAssertions;

namespace Blurt.Engine.Tests.Config;

public sealed class TextShortcutDraftTests
{
    private static readonly IReadOnlySet<string> Others = new HashSet<string> { TextShortcutStore.MatchKey("personal email") };

    [TestCase("work email", "me@work.example", true)]
    [TestCase("   ", "me@work.example", false)]
    [TestCase("...", "me@work.example", false)]
    [TestCase("work email", "   ", false)]
    [TestCase("Personal-Email", "other@example.com", false)]
    public void CanSaveNeedsAMatchablePhraseAReplacementAndNoDuplicate(string trigger, string expansion, bool expected)
    {
        TextShortcutDraft.CanSave(trigger, expansion, Others).Should().Be(expected);
    }

    [Test]
    public void DuplicatesAnotherComparesByMatchKey()
    {
        TextShortcutDraft.DuplicatesAnother("personalemail", Others).Should().BeTrue();
        TextShortcutDraft.DuplicatesAnother("work email", Others).Should().BeFalse();
        TextShortcutDraft.DuplicatesAnother("...", Others).Should().BeFalse();
    }

    [Test]
    public void ClampsToTheStoresCaps()
    {
        TextShortcutDraft.ClampTrigger(new string('a', 100)).Should().HaveLength(TextShortcutStore.TriggerLimit);
        TextShortcutDraft.ClampExpansion(new string('b', 3000)).Should().HaveLength(TextShortcutStore.ExpansionLimit);
        TextShortcutDraft.ClampTrigger("short").Should().Be("short");
    }
}
