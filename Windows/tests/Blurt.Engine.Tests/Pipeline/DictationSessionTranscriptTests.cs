using Blurt.Engine.Pipeline;
using Blurt.Engine.Tests.Stubs;
using FluentAssertions;

namespace Blurt.Engine.Tests.Pipeline;

public sealed class DictationSessionTranscriptTests
{
    [Test]
    public async Task TheTranscriptIsDeliveredOnThePastedOutcome()
    {
        await using var harness = SessionHarness.WithTranscript("Hello world.");

        var phase = await harness.DictateAsync();

        phase.Should().Be(PipelinePhase.Pasted);
        harness.Deliveries.Select(d => d.Text).Should().Equal("Hello world.");
    }

    [Test]
    public async Task TheTranscriptIsDeliveredOnTheCopiedOutcome()
    {
        await using var harness = SessionHarness.WithTranscript("Copied text.");
        harness.Injector.ErrorToThrow = new BlurtException(new BlurtError.NoEditableTarget());

        var phase = await harness.DictateAsync();

        phase.Should().Be(PipelinePhase.NoTarget);
        harness.Deliveries.Select(d => d.Text).Should().Equal("Copied text.");
    }

    [Test]
    public async Task TheTranscriptIsDeliveredEvenWhenThePasteHardFails()
    {
        await using var harness = SessionHarness.WithTranscript("Spoken but unpasted.");
        var error = new BlurtError.AudioCaptureFailed(new InvalidOperationException("gone"));
        harness.Injector.ErrorToThrow = new BlurtException(error);

        var phase = await harness.DictateAsync();

        phase.Should().Be(PipelinePhase.Failed(error));
        harness.Deliveries.Select(d => d.Text).Should().Equal("Spoken but unpasted.");
    }

    [Test]
    public async Task TheDeliveredTranscriptIsTrimmed()
    {
        await using var harness = SessionHarness.WithTranscript("  Padded.\n");

        await harness.DictateAsync();

        harness.Deliveries.Select(d => d.Text).Should().Equal("Padded.");
        harness.Injector.Inserted.Should().Equal("  Padded.\n");
    }

    [Test]
    public async Task DeliveryCarriesTheHistoryTheTranscriptJustJoined()
    {
        await using var harness = SessionHarness.WithTranscript("Hello world.");

        await harness.DictateAsync();
        await harness.DictateAsync();

        harness.Deliveries.Select(d => string.Join(" | ", d.Recent.Entries.Select(e => e.Text)))
            .Should().Equal("Hello world.", "Hello world. | Hello world.");
    }

    [Test]
    public async Task EachDeliveryIsASnapshotThatLaterDictationsDoNotChange()
    {
        await using var harness = SessionHarness.WithTranscript("Hello world.");

        await harness.DictateAsync();
        await harness.DictateAsync();

        harness.Deliveries[0].Recent.Entries.Should().HaveCount(1);
    }

    [Test]
    public async Task TheHistoryEntryCarriesTheStyleNameAndTheClocksTime()
    {
        var clock = new TestClock();
        await using var harness = new SessionHarness(clock: clock, styleNameProvider: () => "Formal");

        await harness.DictateAsync();

        var entry = harness.Deliveries.Should().ContainSingle().Which.Recent.Entries.Should().ContainSingle().Subject;
        entry.Style.Should().Be("Formal");
        entry.Timestamp.Should().Be(clock.GetUtcNow());
    }

    [Test]
    public async Task NothingIsDeliveredWhenSttFails()
    {
        await using var harness = SessionHarness.WithTranscriberError(new InvalidOperationException("boom"));

        await harness.DictateAsync();

        harness.Deliveries.Should().BeEmpty();
    }

    [Test]
    public async Task NothingIsDeliveredWhenTheTranscriptIsBlank()
    {
        await using var harness = SessionHarness.WithTranscript("   ");

        var phase = await harness.DictateAsync();

        phase.Should().Be(PipelinePhase.Idle);
        harness.Deliveries.Should().BeEmpty();
    }

    [Test]
    public async Task NothingIsDeliveredWhenTheDictationIsCancelled()
    {
        await using var harness = SessionHarness.WithTranscript("Hello world.");

        await harness.Session.PressAsync();
        await harness.Session.CancelAsync();

        harness.Session.Phase.Should().Be(PipelinePhase.Cancelled);
        harness.Deliveries.Should().BeEmpty();
    }
}
