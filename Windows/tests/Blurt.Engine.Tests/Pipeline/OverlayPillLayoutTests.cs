using Blurt.Engine.Pipeline;
using FluentAssertions;

namespace Blurt.Engine.Tests.Pipeline;

public sealed class OverlayPillLayoutTests
{
    private static IEnumerable<TestCaseData> Layouts()
    {
        yield return new TestCaseData(OverlayUIState.Idle, new OverlayPillLayout(false, false, null, false));
        yield return new TestCaseData(OverlayUIState.Connecting, new OverlayPillLayout(true, false, "Connecting", false));
        yield return new TestCaseData(OverlayUIState.Recording, new OverlayPillLayout(true, true, null, false));
        yield return new TestCaseData(OverlayUIState.Processing, new OverlayPillLayout(true, false, "Transcribing", false));
        yield return new TestCaseData(OverlayUIState.Pasted, new OverlayPillLayout(false, false, "Pasted", false));
        yield return new TestCaseData(OverlayUIState.NoTarget, new OverlayPillLayout(false, false, "Copied", false));
        yield return new TestCaseData(OverlayUIState.Error("Transcription failed: boom"), new OverlayPillLayout(false, false, "Error", true));
    }

    [TestCaseSource(nameof(Layouts))]
    public void EachStateMapsToItsPillLayout(OverlayUIState state, OverlayPillLayout expected)
    {
        var layout = OverlayPillLayout.For(state);

        layout.Should().Be(expected);
    }

    [Test]
    public void TheTwoWaitsShareTheOrbAndWordShape()
    {
        var connecting = OverlayPillLayout.For(OverlayUIState.Connecting);
        var processing = OverlayPillLayout.For(OverlayUIState.Processing);

        (connecting with { StatusWord = null }).Should().Be(processing with { StatusWord = null });
    }

    [Test]
    public void OnlyRecordingShowsTheMeter()
    {
        var states = new[]
        {
            OverlayUIState.Idle, OverlayUIState.Connecting, OverlayUIState.Processing,
            OverlayUIState.Pasted, OverlayUIState.NoTarget, OverlayUIState.Error("x"),
        };

        states.Select(OverlayPillLayout.For).Should().OnlyContain(layout => !layout.ShowsMeter);
    }
}
