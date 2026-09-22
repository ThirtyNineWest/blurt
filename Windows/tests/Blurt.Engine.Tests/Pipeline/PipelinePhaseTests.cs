using Blurt.Engine.Pipeline;
using FluentAssertions;

namespace Blurt.Engine.Tests.Pipeline;

public sealed class PipelinePhaseTests
{
    private static IEnumerable<TestCaseData> TerminalTable()
    {
        yield return new TestCaseData(PipelinePhase.Idle, true);
        yield return new TestCaseData(PipelinePhase.Connecting, false);
        yield return new TestCaseData(PipelinePhase.Recording, false);
        yield return new TestCaseData(PipelinePhase.Transcribing, false);
        yield return new TestCaseData(PipelinePhase.Injecting, false);
        yield return new TestCaseData(PipelinePhase.Failed(new BlurtError.ApiKeyMissing()), true);
        yield return new TestCaseData(PipelinePhase.Failed(new BlurtError.TargetAppLost()), true);
        yield return new TestCaseData(PipelinePhase.Cancelled, true);
        yield return new TestCaseData(PipelinePhase.Pasted, true);
        yield return new TestCaseData(PipelinePhase.NoTarget, true);
    }

    private static IEnumerable<TestCaseData> CapturingTable()
    {
        yield return new TestCaseData(PipelinePhase.Idle, false);
        yield return new TestCaseData(PipelinePhase.Connecting, true);
        yield return new TestCaseData(PipelinePhase.Recording, true);
        yield return new TestCaseData(PipelinePhase.Transcribing, false);
        yield return new TestCaseData(PipelinePhase.Injecting, false);
        yield return new TestCaseData(PipelinePhase.Failed(new BlurtError.ApiKeyMissing()), false);
        yield return new TestCaseData(PipelinePhase.Cancelled, false);
        yield return new TestCaseData(PipelinePhase.Pasted, false);
        yield return new TestCaseData(PipelinePhase.NoTarget, false);
    }

    [TestCaseSource(nameof(TerminalTable))]
    public void IsTerminalCoversEveryPhase(PipelinePhase phase, bool expected)
    {
        phase.IsTerminal.Should().Be(expected);
    }

    [TestCaseSource(nameof(CapturingTable))]
    public void ExactlyTheBringUpAndRecordingPhasesCountAsCapturing(PipelinePhase phase, bool expected)
    {
        phase.IsCapturing.Should().Be(expected);
    }

    [Test]
    public void SingletonPhasesCompareByCase()
    {
        PipelinePhase.Idle.Should().Be(PipelinePhase.Idle);
        PipelinePhase.Idle.Should().NotBe(PipelinePhase.Cancelled);
        PipelinePhase.Recording.Should().NotBe(PipelinePhase.Connecting);
    }

    [Test]
    public void FailedPhasesCompareByTheirError()
    {
        PipelinePhase.Failed(new BlurtError.ApiKeyMissing()).Should().Be(PipelinePhase.Failed(new BlurtError.ApiKeyMissing()));
        PipelinePhase.Failed(new BlurtError.ApiKeyMissing()).Should().NotBe(PipelinePhase.Failed(new BlurtError.TargetAppLost()));
        PipelinePhase.Failed(new BlurtError.ApiKeyMissing()).Should().NotBe(PipelinePhase.Idle);
    }

    [Test]
    public void ToStringNamesEveryCaseLikeTheSwiftCase()
    {
        PipelinePhase[] phases =
        [
            PipelinePhase.Idle, PipelinePhase.Connecting, PipelinePhase.Recording, PipelinePhase.Transcribing,
            PipelinePhase.Injecting, PipelinePhase.Cancelled, PipelinePhase.Pasted, PipelinePhase.NoTarget,
            PipelinePhase.Failed(new BlurtError.ApiKeyMissing()),
        ];

        var names = phases.Select(p => p.ToString());

        names.Should().Equal(
            "idle", "connecting", "recording", "transcribing", "injecting", "cancelled", "pasted", "noTarget",
            "failed(apiKeyMissing)");
    }
}
