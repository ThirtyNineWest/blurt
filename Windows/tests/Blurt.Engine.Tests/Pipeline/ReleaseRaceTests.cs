using Blurt.Engine.Pipeline;
using Blurt.Engine.Tests.Stubs;
using FluentAssertions;

namespace Blurt.Engine.Tests.Pipeline;

public sealed class ReleaseRaceTests
{
    [Test]
    public async Task ASecondReleaseDuringMicStopIsDroppedNotRunTwice()
    {
        var mic = new GatedStopMic();
        await using var harness = new SessionHarness(
            mic: mic, transcriber: new StubTranscriber { TranscriptToReturn = "hi" });
        await harness.Session.PressAsync();
        var first = harness.Session.ReleaseAsync();
        await mic.WaitUntilStopEnteredAsync();

        var second = harness.Session.ReleaseAsync();
        mic.AllowStopToFinish();
        await first;
        await second;
        var phase = await harness.Session.WaitForTerminalAsync();

        mic.StopCalls.Should().Be(1);
        harness.Injector.Inserted.Should().Equal("hi");
        phase.Should().Be(PipelinePhase.Pasted);
    }

    [Test]
    public async Task ReleaseClaimsTranscribingBeforeMicStopCompletes()
    {
        var mic = new GatedStopMic();
        await using var harness = new SessionHarness(
            mic: mic, transcriber: new StubTranscriber { TranscriptToReturn = "hi" });
        await harness.Session.PressAsync();
        var release = harness.Session.ReleaseAsync();
        await mic.WaitUntilStopEnteredAsync();

        var duringStop = harness.Session.Phase;
        mic.AllowStopToFinish();
        await release;
        var phase = await harness.Session.WaitForTerminalAsync();

        duringStop.Should().Be(PipelinePhase.Transcribing);
        phase.Should().Be(PipelinePhase.Pasted);
        harness.Injector.Inserted.Should().Equal("hi");
    }
}
