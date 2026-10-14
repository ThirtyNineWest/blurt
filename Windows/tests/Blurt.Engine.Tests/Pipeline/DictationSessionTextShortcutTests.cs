using Blurt.Engine.Config;
using Blurt.Engine.FocusCapture;
using Blurt.Engine.STT;
using Blurt.Engine.Tests.Stubs;
using FluentAssertions;

namespace Blurt.Engine.Tests.Pipeline;

public sealed class DictationSessionTextShortcutTests
{
    private static readonly TextShortcut Email = new("personal email", "me@example.com");

    [Test]
    public async Task ThePastedTextAndTheDeliveredTranscriptAreExpandedButTheLogIsNot()
    {
        await using var harness = new SessionHarness(
            transcriber: new StubTranscriber { TranscriptToReturn = "Mail personal email." },
            textShortcuts: [Email]);

        await harness.DictateAsync();

        harness.Injector.Inserted.Should().Equal("Mail me@example.com.");
        harness.Deliveries.Select(d => d.Text).Should().Equal("Mail me@example.com.");
        var logged = (await harness.Log.TranscriptsAsync()).Should().ContainSingle().Subject;
        ((string?)logged["transcript"]).Should().Be("Mail personal email.");
    }

    [Test]
    public async Task TheNextRequestsHistoryCarriesTheSpokenTextNotTheExpansion()
    {
        await using var harness = new SessionHarness(
            transcriber: new StubTranscriber { TranscriptToReturn = "Mail personal email." },
            textShortcuts: [Email]);

        await harness.DictateAsync();
        await harness.DictateAsync();

        harness.Transcriber.ReceivedContexts[^1]!.RecentTranscripts.Should().Equal("Mail personal email.");
        harness.Deliveries[^1].Recent.Entries[0].Text.Should().Be("Mail me@example.com.");
        harness.Deliveries[^1].Recent.Entries[0].Spoken.Should().Be("Mail personal email.");
    }

    [Test]
    public async Task TheTextBeforeTheCaretGoesOutWithExpansionsPutBackToTriggers()
    {
        var focus = new StubFocusCapture
        {
            FieldContext = new FocusedFieldContext(null, null, "Mail me@example.com.", null, IsSecure: false),
        };
        await using var harness = new SessionHarness(focus: focus, textShortcuts: [Email]);

        await harness.DictateAsync();

        var context = harness.Transcriber.ReceivedContexts[^1]!;
        context.PriorText.Should().Be("Mail me@example.com.");
        context.TextShortcuts.Should().Equal(Email);
        STTPrompt.Text(context).Should().Be("Mail personal email.");
    }

    [Test]
    public async Task ShortcutsAreReadForTheContextOnlyWhenThereIsPriorTextToScrub()
    {
        var reads = 0;
        await using var harness = new SessionHarness(textShortcutsProvider: () =>
        {
            reads++;
            return [Email];
        });

        await harness.DictateAsync();

        // One read for the expansion before the paste, none for the context: the field had no prior text.
        reads.Should().Be(1);
        harness.Transcriber.ReceivedContexts.Should().Equal([null]);
    }
}
