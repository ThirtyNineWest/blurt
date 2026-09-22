using Blurt.Engine.Pipeline;
using FluentAssertions;

namespace Blurt.Engine.Tests.Pipeline;

public sealed class TrayStatusTests
{
    private static IEnumerable<TestCaseData> Projections()
    {
        yield return new TestCaseData(PipelinePhase.Recording, TrayStatus.Recording);
        yield return new TestCaseData(PipelinePhase.Transcribing, TrayStatus.Transcribing);
        yield return new TestCaseData(PipelinePhase.Idle, TrayStatus.Idle);
        yield return new TestCaseData(PipelinePhase.Connecting, TrayStatus.Idle);
        yield return new TestCaseData(PipelinePhase.Injecting, TrayStatus.Idle);
        yield return new TestCaseData(PipelinePhase.Cancelled, TrayStatus.Idle);
        yield return new TestCaseData(PipelinePhase.Pasted, TrayStatus.Idle);
        yield return new TestCaseData(PipelinePhase.NoTarget, TrayStatus.Idle);
    }

    [TestCaseSource(nameof(Projections))]
    public void EachPhaseProjectsToItsTrayStatus(PipelinePhase phase, TrayStatus expected)
    {
        phase.TrayStatus.Should().Be(expected);
    }

    [Test]
    public void FailedMapsToIdle()
    {
        PipelinePhase.Failed(new BlurtError.TargetAppLost()).TrayStatus.Should().Be(TrayStatus.Idle);
    }

    [TestCase(TrayStatus.Idle, "idle")]
    [TestCase(TrayStatus.Recording, "recording")]
    [TestCase(TrayStatus.Transcribing, "transcribing")]
    public void IconNames(TrayStatus status, string expected)
    {
        status.IconName().Should().Be(expected);
    }

    [TestCase(TrayStatus.Idle, "Blurt — idle")]
    [TestCase(TrayStatus.Recording, "Blurt — recording")]
    [TestCase(TrayStatus.Transcribing, "Blurt — transcribing")]
    public void AccessibilityLabels(TrayStatus status, string expected)
    {
        status.AccessibilityLabel().Should().Be(expected);
    }
}
