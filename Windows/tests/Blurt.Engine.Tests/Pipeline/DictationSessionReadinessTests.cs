using Blurt.Engine.Pipeline;
using Blurt.Engine.Tests.Stubs;
using FluentAssertions;

namespace Blurt.Engine.Tests.Pipeline;

public sealed class DictationSessionReadinessTests
{
    [Test]
    public async Task PressIsRefusedBeforeCaptureWhenTheReadinessCheckBlocks()
    {
        await using var harness = new SessionHarness(readinessCheck: () => new BlurtError.ApiKeyMissing());

        await harness.Session.PressAsync();

        harness.Session.Phase.Should().Be(PipelinePhase.Failed(new BlurtError.ApiKeyMissing()));
        harness.Session.Phase.SetupBlocker.Should().Be(new BlurtError.ApiKeyMissing());
        harness.Mic.StartCalls.Should().Be(0);
    }

    [Test]
    public async Task PressSucceedsOnceTheBlockerClears()
    {
        var ready = false;
        await using var harness = new SessionHarness(
            readinessCheck: () => Volatile.Read(ref ready) ? null : new BlurtError.ApiKeyMissing());
        await harness.Session.PressAsync();
        var refused = harness.Session.Phase;

        Volatile.Write(ref ready, true);
        await harness.Session.PressAsync();

        refused.Should().Be(PipelinePhase.Failed(new BlurtError.ApiKeyMissing()));
        harness.Session.Phase.Should().Be(PipelinePhase.Recording);
        harness.Mic.StartCalls.Should().Be(1);
    }
}
