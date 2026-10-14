using Blurt.Engine.Pipeline;
using FluentAssertions;

namespace Blurt.Engine.Tests.Pipeline;

public sealed class RecordingCueGateTests
{
    [Test]
    public void EnteringRecordingFiresTheStartCue()
    {
        var gate = new RecordingCueGate();

        gate.Cue(PipelinePhase.Recording).Should().Be(RecordingCue.Start);
    }

    [Test]
    public void TheMicBringUpIsSilentAndTheChimeWaitsForRecording()
    {
        var gate = new RecordingCueGate();

        gate.Cue(PipelinePhase.Connecting).Should().BeNull();
        gate.Cue(PipelinePhase.Recording).Should().Be(RecordingCue.Start);
    }

    [Test]
    public void APressWhoseMicNeverComesUpNeverChimes()
    {
        var gate = new RecordingCueGate();
        var failure = PipelinePhase.Failed(new BlurtError.AudioCaptureFailed(new InvalidOperationException("no mic")));

        gate.Cue(PipelinePhase.Connecting).Should().BeNull();
        gate.Cue(failure).Should().BeNull();
        gate.Cue(PipelinePhase.Connecting).Should().BeNull();
        gate.Cue(PipelinePhase.Recording).Should().Be(RecordingCue.Start);
    }

    [Test]
    public void LeavingRecordingFiresTheStopCue()
    {
        var gate = new RecordingCueGate();
        gate.Cue(PipelinePhase.Recording);

        gate.Cue(PipelinePhase.Transcribing).Should().Be(RecordingCue.Stop);
    }

    [Test]
    public void StayingInRecordingDoesNotRefireTheStartCue()
    {
        var gate = new RecordingCueGate();

        gate.Cue(PipelinePhase.Recording).Should().Be(RecordingCue.Start);
        gate.Cue(PipelinePhase.Recording).Should().BeNull();
    }

    [Test]
    public void TransitionsBetweenTwoNonRecordingPhasesAreSilent()
    {
        var gate = new RecordingCueGate();

        gate.Cue(PipelinePhase.Idle).Should().BeNull();
        gate.Cue(PipelinePhase.Transcribing).Should().BeNull();
        gate.Cue(PipelinePhase.Injecting).Should().BeNull();
        gate.Cue(PipelinePhase.Pasted).Should().BeNull();
        gate.Cue(PipelinePhase.Failed(new BlurtError.ApiKeyMissing())).Should().BeNull();
    }

    [Test]
    public void AFullPressRecordStopPressCycleChimesStartStopStart()
    {
        var gate = new RecordingCueGate();

        gate.Cue(PipelinePhase.Connecting).Should().BeNull();
        gate.Cue(PipelinePhase.Recording).Should().Be(RecordingCue.Start);
        gate.Cue(PipelinePhase.Injecting).Should().Be(RecordingCue.Stop);
        gate.Cue(PipelinePhase.Idle).Should().BeNull();
        gate.Cue(PipelinePhase.Connecting).Should().BeNull();
        gate.Cue(PipelinePhase.Recording).Should().Be(RecordingCue.Start);
    }
}
