using Blurt.Engine.Hotkey;
using Blurt.Engine.Permissions;
using Blurt.Engine.Pipeline;
using FluentAssertions;

namespace Blurt.Engine.Tests.Pipeline;

public sealed class ReadyReadoutTests
{
    [Test]
    public void TheTapModesNameTheKeyInATapSentence()
    {
        var headline = ReadyReadout.Headline(TriggerKey.RightControl, TriggerActivation.TapOrHold);

        headline.Should().Be(new ReadoutSentence("Tap ", "Right Control (Ctrl)", " to start and stop."));
        ReadyReadout.Headline(TriggerKey.RightShift, TriggerActivation.Tap).ToString()
            .Should().Be("Tap Right Shift to start and stop.");
    }

    [Test]
    public void HoldNamesTheKeyInAHoldSentence()
    {
        ReadyReadout.Headline(TriggerKey.RightShift, TriggerActivation.Hold).ToString()
            .Should().Be("Hold Right Shift to talk, then release.");
    }

    [Test]
    public void TheSubtitleIsTheActivationGuidance()
    {
        foreach (var activation in TriggerActivations.All)
        {
            ReadyReadout.Subtitle(activation).Should().Be(activation.Guidance());
        }
    }

    [Test]
    public void TheListeningLineNamesAnActiveStyle()
    {
        ReadyReadout.ListeningSubtitle("Casual")
            .Should().Be("Blurting in Casual. Tap again or release to finish — Esc cancels.");
    }

    [Test]
    public void TheListeningLineWithNoStyleIsJustTheWayOut()
    {
        ReadyReadout.ListeningSubtitle(null).Should().Be("Tap again or release to finish — Esc cancels.");
    }

    [Test]
    public void RelativeLabelsRefreshAtHalfTheJustNowThreshold()
    {
        RecentDictations.Entry.LabelRefreshInterval.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Test]
    public void EveryMicrophoneStateHasADescription()
    {
        foreach (var state in Enum.GetValues<MicrophoneAccessState>())
        {
            state.Description().Should().NotBeNullOrWhiteSpace();
        }

        MicrophoneAccessState.Denied.Description().Should().Contain("Let desktop apps access your microphone");
    }
}
