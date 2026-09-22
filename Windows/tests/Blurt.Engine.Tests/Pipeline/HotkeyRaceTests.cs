using Blurt.Engine.Pipeline;
using Blurt.Engine.Tests.Stubs;
using FluentAssertions;

namespace Blurt.Engine.Tests.Pipeline;

public sealed class HotkeyRaceTests
{
    [Test]
    public async Task AReleaseDuringMicStartIsHonoredNotDropped()
    {
        var mic = new GatedStartMic();
        await using var harness = new SessionHarness(mic: mic, transcriber: new StubTranscriber { TranscriptToReturn = "hi" });
        var pressed = harness.Session.PressAsync();
        await mic.WaitUntilStartEnteredAsync();

        var released = harness.Session.ReleaseAsync();
        mic.AllowStartToFinish();
        await pressed;
        await released;
        await harness.Session.AwaitPipelineAsync().WaitAsync(SessionWaits.Budget);

        mic.StopCalls.Should().Be(1);
        harness.Session.Phase.Should().Be(PipelinePhase.Pasted);
        harness.Injector.Inserted.Should().Equal("hi");
    }

    [Test]
    public async Task ACancelDuringMicStartIsHonoredNotDropped()
    {
        var mic = new GatedStartMic();
        await using var harness = new SessionHarness(mic: mic, transcriber: new StubTranscriber { TranscriptToReturn = "hi" });
        var pressed = harness.Session.PressAsync();
        await mic.WaitUntilStartEnteredAsync();

        var cancelled = harness.Session.CancelAsync();
        mic.AllowStartToFinish();
        await pressed;
        await cancelled;

        mic.StopCalls.Should().Be(1);
        harness.Session.Phase.Should().Be(PipelinePhase.Cancelled);
        harness.Injector.Inserted.Should().BeEmpty();
    }

    [Test]
    public async Task ASecondPressDuringMicStartIsDroppedNotDoubleStarted()
    {
        var mic = new GatedStartMic();
        await using var harness = new SessionHarness(mic: mic);
        var pressed = harness.Session.PressAsync();
        await mic.WaitUntilStartEnteredAsync();

        var secondPress = harness.Session.PressAsync();
        mic.AllowStartToFinish();
        await pressed;
        await secondPress;

        mic.StartCalls.Should().Be(1);
        harness.Session.Phase.Should().Be(PipelinePhase.Recording);
    }

    [Test]
    public async Task ACancelOverridesAPendingReleaseDuringMicStart()
    {
        var mic = new GatedStartMic();
        await using var harness = new SessionHarness(mic: mic, transcriber: new StubTranscriber { TranscriptToReturn = "hi" });
        var pressed = harness.Session.PressAsync();
        await mic.WaitUntilStartEnteredAsync();

        var released = harness.Session.ReleaseAsync();
        var cancelled = harness.Session.CancelAsync();
        mic.AllowStartToFinish();
        await pressed;
        await released;
        await cancelled;
        await harness.Session.AwaitPipelineAsync().WaitAsync(SessionWaits.Budget);

        harness.Session.Phase.Should().Be(PipelinePhase.Cancelled);
        mic.StopCalls.Should().Be(1);
        harness.Injector.Inserted.Should().BeEmpty();
    }
}
