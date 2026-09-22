using Blurt.Engine.Pipeline;
using FluentAssertions;

namespace Blurt.Engine.Tests.Pipeline;

public sealed class OverlayUIStateTests
{
    private static IEnumerable<TestCaseData> Projections()
    {
        yield return new TestCaseData(PipelinePhase.Idle, OverlayUIState.Idle);
        yield return new TestCaseData(PipelinePhase.Connecting, OverlayUIState.Connecting);
        yield return new TestCaseData(PipelinePhase.Recording, OverlayUIState.Recording);
        yield return new TestCaseData(PipelinePhase.Transcribing, OverlayUIState.Processing);
        yield return new TestCaseData(PipelinePhase.Injecting, OverlayUIState.Processing);
        yield return new TestCaseData(PipelinePhase.Pasted, OverlayUIState.Pasted);
        yield return new TestCaseData(PipelinePhase.Cancelled, OverlayUIState.Idle);
        yield return new TestCaseData(PipelinePhase.NoTarget, OverlayUIState.NoTarget);
    }

    private static IEnumerable<TestCaseData> Labels()
    {
        yield return new TestCaseData(OverlayUIState.Idle, "Blurt.");
        yield return new TestCaseData(OverlayUIState.Connecting, "Connecting to the microphone.");
        yield return new TestCaseData(OverlayUIState.Recording, "Recording.");
        yield return new TestCaseData(OverlayUIState.Processing, "Processing.");
        yield return new TestCaseData(OverlayUIState.Pasted, "Your dictation was pasted.");
        yield return new TestCaseData(OverlayUIState.NoTarget, "No text field focused. Your dictation was copied to the clipboard.");
    }

    private static IEnumerable<PipelinePhase> NonFailedPhases()
    {
        yield return PipelinePhase.Idle;
        yield return PipelinePhase.Connecting;
        yield return PipelinePhase.Recording;
        yield return PipelinePhase.Transcribing;
        yield return PipelinePhase.Injecting;
        yield return PipelinePhase.Cancelled;
        yield return PipelinePhase.Pasted;
        yield return PipelinePhase.NoTarget;
    }

    [TestCaseSource(nameof(Projections))]
    public void EachPhaseProjectsToItsPillState(PipelinePhase phase, OverlayUIState expected)
    {
        phase.OverlayState.Should().Be(expected);
    }

    [Test]
    public void PastedIsATransientNotice()
    {
        OverlayUIState.Pasted.NoticeDwellSeconds.Should().NotBeNull();
    }

    [Test]
    public void FailedMapsToErrorCarryingTheReason()
    {
        var phase = PipelinePhase.Failed(new BlurtError.AudioCaptureFailed(new NoMicrophoneException()));

        phase.OverlayState.Should().Be(OverlayUIState.Error("Audio capture failed: No microphone is available."));
    }

    [Test]
    public void MissingKeyFailureMapsToIdle()
    {
        PipelinePhase.Failed(new BlurtError.ApiKeyMissing()).OverlayState.Should().Be(OverlayUIState.Idle);
    }

    [Test]
    public void SetupBlockerIsTheMissingKeyFailureOnly()
    {
        PipelinePhase.Failed(new BlurtError.ApiKeyMissing()).SetupBlocker.Should().Be(new BlurtError.ApiKeyMissing());
        PipelinePhase.Failed(new BlurtError.TargetAppLost()).SetupBlocker.Should().BeNull();
        PipelinePhase.Failed(new BlurtError.SttFailed(new InvalidOperationException("x"))).SetupBlocker.Should().BeNull();
        PipelinePhase.Failed(new BlurtError.ApiKeyMissing()).OverlayState.Should().Be(OverlayUIState.Idle);
    }

    [TestCaseSource(nameof(NonFailedPhases))]
    public void NonFailedPhasesHaveNoSetupBlocker(PipelinePhase phase)
    {
        phase.SetupBlocker.Should().BeNull();
    }

    [Test]
    public void FailedTargetLostCarriesANonEmptyMessage()
    {
        var state = PipelinePhase.Failed(new BlurtError.TargetAppLost()).OverlayState;

        state.Should().BeOfType<OverlayUIState.ErrorState>().Which.Message.Should().NotBeNullOrEmpty();
    }

    [TestCaseSource(nameof(Labels))]
    public void EachStateSpeaksItsFixedLabel(OverlayUIState state, string spoken)
    {
        state.AccessibilityLabel.Should().Be(spoken);
    }

    [Test]
    public void ErrorLabelIsTheMessageVerbatim()
    {
        OverlayUIState.Error("AssemblyAI error 401.").AccessibilityLabel.Should().Be("AssemblyAI error 401.");
    }

    [Test]
    public void PastedClearsFastest()
    {
        OverlayUIState.Pasted.NoticeDwellSeconds.Should().Be(0.8);
    }

    [Test]
    public void ErrorAndCopiedLingerLongEnoughToRead()
    {
        OverlayUIState.Error("boom").NoticeDwellSeconds.Should().Be(1.6);
        OverlayUIState.NoTarget.NoticeDwellSeconds.Should().Be(1.6);
    }

    [Test]
    public void SteadyStatesHaveNoDwell()
    {
        OverlayUIState.Idle.NoticeDwellSeconds.Should().BeNull();
        OverlayUIState.Connecting.NoticeDwellSeconds.Should().BeNull();
        OverlayUIState.Recording.NoticeDwellSeconds.Should().BeNull();
        OverlayUIState.Processing.NoticeDwellSeconds.Should().BeNull();
    }

    private sealed class NoMicrophoneException() : Exception("No microphone is available.");
}
