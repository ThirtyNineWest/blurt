using Blurt.Engine.Config;
using FluentAssertions;

namespace Blurt.Engine.Tests.Config;

public sealed class KeyTermsStoreParseTests
{
    [Test]
    public void NilAndBlankInputYieldNoTerms()
    {
        KeyTermsStore.Parse(null).Should().BeEmpty();
        KeyTermsStore.Parse("").Should().BeEmpty();
        KeyTermsStore.Parse("   ,  , \n").Should().BeEmpty();
    }

    [Test]
    public void CommaSeparatedInputSplitsAndTrimsEachTerm()
    {
        KeyTermsStore.Parse("AssemblyAI, Kubernetes ,  Anthropic").Should().Equal("AssemblyAI", "Kubernetes", "Anthropic");
    }

    [Test]
    public void BlankEntriesBetweenCommasAreDropped()
    {
        KeyTermsStore.Parse("foo,,bar, ,baz").Should().Equal("foo", "bar", "baz");
    }

    [Test]
    public void DuplicatesAreRemovedCaseInsensitivelyKeepingTheFirstSpelling()
    {
        KeyTermsStore.Parse("Blurt, blurt, BLURT, Slack").Should().Equal("Blurt", "Slack");
    }

    [Test]
    public void MultiWordTermsSurvive()
    {
        KeyTermsStore.Parse("San Francisco, New York").Should().Equal("San Francisco", "New York");
    }
}

public sealed class KeyTermsStoreTests
{
    private static KeyTermsStore MakeStore(string? stored)
    {
        var settings = new InMemorySettingsStore();
        if (stored is not null)
        {
            settings.Set(KeyTermsStore.SettingsKey, stored);
        }

        return new KeyTermsStore(settings);
    }

    [Test]
    public void RawTrimsTheStoredStringTermsParsesIt()
    {
        var store = MakeStore("  AssemblyAI, Slack  ");

        store.Raw.Should().Be("AssemblyAI, Slack");
        store.Terms.Should().Equal("AssemblyAI", "Slack");
    }

    [Test]
    public void AnUnsetKeyReadsAsNoTerms()
    {
        var store = MakeStore(null);

        store.Raw.Should().BeNull();
        store.Terms.Should().BeEmpty();
    }

    [TestCase("")]
    [TestCase("   \n")]
    public void ABlankFieldReadsAsNoTermsRatherThanAnEmptyTerm(string stored)
    {
        var store = MakeStore(stored);

        store.Raw.Should().BeNull();
        store.Terms.Should().BeEmpty();
    }

    [Test]
    public void EachStoreReadsItsOwnSettingsSoAStoredListCannotLeakBetweenThem()
    {
        MakeStore("Blurt").Terms.Should().Equal("Blurt");
        MakeStore(null).Terms.Should().BeEmpty();
    }

    [Test]
    public void TheSetterWritesTheRawStringForTheReaderToNormalize()
    {
        var settings = new InMemorySettingsStore();
        var store = new KeyTermsStore(settings);

        store.Raw = "  AssemblyAI, Slack  ";

        store.Raw.Should().Be("AssemblyAI, Slack");
    }
}
