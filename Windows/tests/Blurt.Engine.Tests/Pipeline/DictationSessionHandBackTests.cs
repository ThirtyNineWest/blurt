using Blurt.Engine.Config;
using Blurt.Engine.Pipeline;
using Blurt.Engine.Tests.Stubs;
using FluentAssertions;

namespace Blurt.Engine.Tests.Pipeline;

public sealed class DictationSessionHandBackTests
{
    [Test]
    public async Task TheTranscriptComesBackAsHandedBackAndNothingIsPasted()
    {
        await using var harness = SessionHarness.WithTranscript("Give me a high-level summary.");

        var phase = await harness.DictateAsync(handingBack: true);

        phase.Should().Be(PipelinePhase.HandedBack("Give me a high-level summary."));
        harness.Injector.Inserted.Should().BeEmpty();
    }

    [Test]
    public async Task ARequestIsNeverRememberedAsADictationNorReportedAsOne()
    {
        await using var harness = SessionHarness.WithTranscript("What does this mean?");

        await harness.DictateAsync(handingBack: true);
        await harness.DictateAsync();

        harness.Transcriber.ReceivedContexts[^1].Should().BeNull();
        harness.Deliveries.Should().ContainSingle().Which.Text.Should().Be("What does this mean?");
    }

    [Test]
    public async Task TheSpokenWordsComeBackAsSaidWithoutTextShortcutsExpanded()
    {
        await using var harness = new SessionHarness(
            transcriber: new StubTranscriber { TranscriptToReturn = "Email it to my personal email" },
            textShortcuts: [new TextShortcut("personal email", "me@example.com")]);

        var phase = await harness.DictateAsync(handingBack: true);

        phase.Should().Be(PipelinePhase.HandedBack("Email it to my personal email"));
    }

    [Test]
    public async Task NothingSaidEndsInIdle()
    {
        await using var harness = SessionHarness.WithTranscript("   ");

        var phase = await harness.DictateAsync(handingBack: true);

        phase.Should().Be(PipelinePhase.Idle);
    }

    [Test]
    public async Task TheNextOrdinaryPressPastesAgain()
    {
        await using var harness = SessionHarness.WithTranscript("Hello world.");

        await harness.DictateAsync(handingBack: true);
        var phase = await harness.DictateAsync();

        phase.Should().Be(PipelinePhase.Pasted);
        harness.Injector.Inserted.Should().Equal("Hello world.");
    }

    [Test]
    public async Task SubmittingPressHandingBackIsTheSamePressThroughTheCommandFeed()
    {
        await using var harness = SessionHarness.WithTranscript("Summarize this.");
        var phases = harness.Session.PhaseStream(CancellationToken.None);

        harness.Session.Submit(Command.PressHandingBack);
        await harness.Session.WaitForPhaseAsync(PipelinePhase.Recording);
        harness.Session.Submit(Command.Release);
        var seen = await phases.CollectUntilAsync(p => p is PipelinePhase.HandedBackPhase);

        seen[^1].Should().Be(PipelinePhase.HandedBack("Summarize this."));
    }

    [Test]
    public void AHandBackPhaseIsTerminalHidesThePillAndReadsAsIdleOnTheTray()
    {
        var phase = PipelinePhase.HandedBack("x");

        phase.IsTerminal.Should().BeTrue();
        phase.IsCapturing.Should().BeFalse();
        phase.OverlayState.Should().Be(OverlayUIState.Idle);
        phase.TrayStatus.Should().Be(TrayStatus.Idle);
        phase.SetupBlocker.Should().BeNull();
        phase.ToString().Should().Be("handedBack");
        PipelinePhase.HandedBack("x").Should().Be(PipelinePhase.HandedBack("x"));
    }
}
