using Blurt.Engine.Hotkey;
using Blurt.Engine.Pipeline;
using FluentAssertions;

namespace Blurt.Engine.Tests.Hotkey;

public sealed class SelectionSpeechRoutingTests
{
    [Test]
    public void WithTheSwitchOffEveryCommandGoesStraightToTheSession()
    {
        var routing = new SelectionSpeechRouting();

        foreach (var command in new[] { Command.Press, Command.Release, Command.Cancel, Command.CancelRecording })
        {
            routing.Submit(command, readAloudEnabled: false).Should().Equal(RoutingAction.Forward(command));
        }
    }

    [Test]
    public void APressWithNothingSelectedDictatesReplayingWhatArrivedDuringTheReadInOrder()
    {
        var routing = new SelectionSpeechRouting();

        routing.Submit(Command.Press, readAloudEnabled: true).Should().Equal(RoutingAction.ReadSelection);
        routing.Submit(Command.Release, readAloudEnabled: true).Should().BeEmpty();
        routing.SelectionResolved(null).Should().Equal(RoutingAction.Forward(Command.Press), RoutingAction.Forward(Command.Release));
        routing.Submit(Command.Press, readAloudEnabled: true).Should().Equal(RoutingAction.ReadSelection);
    }

    [Test]
    public void APressOverASelectionSpeaksItAndTheNextKeyCommandStopsIt()
    {
        var routing = new SelectionSpeechRouting();
        routing.Submit(Command.Press, readAloudEnabled: true);

        routing.SelectionResolved("Hello there.").Should().Equal(new RoutingAction.Speak("Hello there."));
        routing.Submit(Command.Release, readAloudEnabled: true).Should().Equal(RoutingAction.StopSpeech);
        routing.SpeechFinished().Should().BeEmpty();
    }

    [Test]
    public void AComboCancelDuringTheReadDropsThePress()
    {
        var routing = new SelectionSpeechRouting();
        routing.Submit(Command.Press, readAloudEnabled: true);
        routing.Submit(Command.Cancel, readAloudEnabled: true);

        routing.SelectionResolved("Hello there.").Should().BeEmpty();
        routing.Submit(Command.Press, readAloudEnabled: false).Should().Equal(RoutingAction.Forward(Command.Press));
    }

    [Test]
    public void ARecoveryResetWhileSpeakingStopsTheReadAndStillReachesTheSession()
    {
        var routing = new SelectionSpeechRouting();
        routing.Submit(Command.Press, readAloudEnabled: true);
        routing.SelectionResolved("Hello there.");

        routing.Submit(Command.CancelRecording, readAloudEnabled: true)
            .Should().Equal(RoutingAction.StopSpeech, RoutingAction.Forward(Command.CancelRecording));
    }

    [Test]
    public void ANaturalEndClearsTheGateSilently()
    {
        var routing = new SelectionSpeechRouting();
        routing.Submit(Command.Press, readAloudEnabled: true);
        routing.SelectionResolved("Hello there.");

        routing.SpeechFinished().Should().Equal(RoutingAction.ResetGate);
        routing.Submit(Command.Press, readAloudEnabled: false).Should().Equal(RoutingAction.Forward(Command.Press));
    }

    [Test]
    public void ADictationsTerminalPhaseSyncsTheGateOnlyWhileCommandsGoToTheSession()
    {
        var routing = new SelectionSpeechRouting();

        routing.SessionReachedTerminalPhase(PipelinePhase.Pasted).Should().Equal(RoutingAction.SyncGateAfterSession);
        routing.Submit(Command.Press, readAloudEnabled: true);
        routing.SessionReachedTerminalPhase(PipelinePhase.Pasted).Should().BeEmpty();
        routing.SelectionResolved("Hello there.");
        routing.SessionReachedTerminalPhase(PipelinePhase.Pasted).Should().BeEmpty();
    }

    [Test]
    public void APressThatLandsDuringASlowReadAndIsDroppedClearsTheGateItLatched()
    {
        var routing = new SelectionSpeechRouting();
        routing.Submit(Command.Press, readAloudEnabled: true);
        routing.Submit(Command.Cancel, readAloudEnabled: true);
        routing.Submit(Command.Press, readAloudEnabled: true);
        routing.KeyLatched();

        routing.SelectionResolved("Hello there.").Should().Equal(RoutingAction.ResetGate);

        routing.Submit(Command.Press, readAloudEnabled: true);
        routing.Submit(Command.Release, readAloudEnabled: true);
        routing.SelectionResolved("Hello there.").Should().BeEmpty();
    }

    [Test]
    public void AStaleSelectionResultAfterThePressResolvedIsIgnored()
    {
        new SelectionSpeechRouting().SelectionResolved("Hello there.").Should().BeEmpty();
    }
}

public sealed class SelectionSpeechRoutingAskTests
{
    private const string Selection = "The quarterly report.";

    private static SelectionSpeechRouting Selected()
    {
        var routing = new SelectionSpeechRouting();
        routing.Submit(Command.Press, readAloudEnabled: true, askEnabled: true);
        routing.SelectionResolved(Selection);
        return routing;
    }

    private static SelectionSpeechRouting Asking()
    {
        var routing = Selected();
        routing.HoldElapsed();
        return routing;
    }

    [Test]
    public void APressThatMayAskStartsTheHoldTimerBesideTheSelectionRead()
    {
        var routing = new SelectionSpeechRouting();

        routing.Submit(Command.Press, readAloudEnabled: true, askEnabled: true)
            .Should().Equal(RoutingAction.ReadSelection, RoutingAction.StartHoldTimer);
    }

    [Test]
    public void ATapOverASelectionReadsItWithoutEverOpeningTheMic()
    {
        var routing = Selected();

        routing.KeyLatched().Should().Equal(new RoutingAction.Speak(Selection));
        routing.Submit(Command.Release, readAloudEnabled: true, askEnabled: true).Should().Equal(RoutingAction.StopSpeech);
    }

    [Test]
    public void ATapThatLandsBeforeTheSelectionReadReturnsStillReadsIt()
    {
        var routing = new SelectionSpeechRouting();
        routing.Submit(Command.Press, readAloudEnabled: true, askEnabled: true);

        routing.KeyLatched().Should().BeEmpty();
        routing.SelectionResolved(Selection).Should().Equal(new RoutingAction.Speak(Selection));
    }

    [Test]
    public void AHoldOverASelectionOpensTheMicWithTheHandBackPress()
    {
        var routing = Selected();

        routing.HoldElapsed().Should().Equal(RoutingAction.Forward(Command.PressHandingBack));
    }

    [Test]
    public void AHoldThatOutlastsASlowSelectionReadAsksAsSoonAsTheReadReturns()
    {
        var routing = new SelectionSpeechRouting();
        routing.Submit(Command.Press, readAloudEnabled: true, askEnabled: true);

        routing.HoldElapsed().Should().BeEmpty();
        routing.SelectionResolved(Selection).Should().Equal(RoutingAction.Forward(Command.PressHandingBack));
    }

    [Test]
    public void LettingGoAfterTheGatesHoldThresholdReleasesTheRequest()
    {
        var routing = Asking();

        routing.Submit(Command.Release, readAloudEnabled: true, askEnabled: true)
            .Should().Equal(RoutingAction.Forward(Command.Release));
    }

    [Test]
    public void LettingGoBeforeTheGatesHoldThresholdAlsoEndsTheRequestAndClearsTheLatch()
    {
        var routing = Asking();

        routing.KeyLatched().Should().Equal(RoutingAction.Forward(Command.Release), RoutingAction.ResetGate);
    }

    [Test]
    public void TheTranscriptHandedBackIsAnsweredAloudAboutTheSelection()
    {
        var routing = Asking();
        routing.Submit(Command.Release, readAloudEnabled: true, askEnabled: true);

        routing.SessionReachedTerminalPhase(PipelinePhase.HandedBack("Summarize this."))
            .Should().Equal(new RoutingAction.Answer("Summarize this.", Selection));
        routing.SpeechFinished().Should().Equal(RoutingAction.ResetGate);
    }

    [Test]
    public void ATapThatStopsAnAnswerClearsTheGateItArmedSoTheNextPressIsNotSwallowed()
    {
        var routing = Asking();
        routing.Submit(Command.Release, readAloudEnabled: true, askEnabled: true);
        routing.SessionReachedTerminalPhase(PipelinePhase.HandedBack("Summarize this."));

        routing.Submit(Command.Press, readAloudEnabled: true, askEnabled: true)
            .Should().Equal(RoutingAction.StopSpeech, RoutingAction.ResetGate);
        routing.KeyLatched().Should().BeEmpty();
        routing.Submit(Command.Press, readAloudEnabled: true, askEnabled: true)
            .Should().Equal(RoutingAction.ReadSelection, RoutingAction.StartHoldTimer);
    }

    [Test]
    public void ARequestWithNothingSaidReadsTheSelectionInstead()
    {
        var routing = Asking();
        routing.Submit(Command.Release, readAloudEnabled: true, askEnabled: true);

        routing.SessionReachedTerminalPhase(PipelinePhase.Idle).Should().Equal(new RoutingAction.Speak(Selection));
    }

    [Test]
    public void ARequestThatFailsIsADictationEndingWithoutAKeyEvent()
    {
        var routing = Asking();
        routing.Submit(Command.Release, readAloudEnabled: true, askEnabled: true);
        var failed = PipelinePhase.Failed(new BlurtError.SttFailed(new TimeoutException()));

        routing.SessionReachedTerminalPhase(failed).Should().Equal(RoutingAction.SyncGateAfterSession);
        routing.Submit(Command.Press, readAloudEnabled: false).Should().Equal(RoutingAction.Forward(Command.Press));
    }

    [Test]
    public void ARecordingThatFailsWhileTheKeyIsStillDownSyncsTheGate()
    {
        var routing = Asking();
        var failed = PipelinePhase.Failed(new BlurtError.AudioCaptureFailed(new InvalidOperationException()));

        routing.SessionReachedTerminalPhase(failed).Should().Equal(RoutingAction.SyncGateAfterSession);
    }

    [Test]
    public void TheRecordingCapHandingBackWithTheKeyStillDownAnswersAndClearsTheGate()
    {
        var routing = Asking();

        routing.SessionReachedTerminalPhase(PipelinePhase.HandedBack("What is this?"))
            .Should().Equal(new RoutingAction.Answer("What is this?", Selection), RoutingAction.ResetGate);
    }

    [Test]
    public void AComboWhileAskingCancelsTheRecordingAsItWouldADictation()
    {
        var routing = Asking();

        routing.Submit(Command.Cancel, readAloudEnabled: true, askEnabled: true)
            .Should().Equal(RoutingAction.Forward(Command.Cancel));
    }

    [Test]
    public void PressingAgainWhileTheRequestIsTranscribingDropsItAndClearsTheNewPress()
    {
        var routing = Asking();
        routing.Submit(Command.Release, readAloudEnabled: true, askEnabled: true);

        routing.Submit(Command.Press, readAloudEnabled: true, askEnabled: true)
            .Should().Equal(RoutingAction.Forward(Command.Cancel), RoutingAction.ResetGate);
        routing.SessionReachedTerminalPhase(PipelinePhase.Cancelled).Should().Equal(RoutingAction.SyncGateAfterSession);
    }

    [Test]
    public void AQuickReleaseBeforeTheHoldDelayUnderAHoldOnlyTriggerDoesNothing()
    {
        var routing = Selected();

        routing.Submit(Command.Release, readAloudEnabled: true, askEnabled: true).Should().BeEmpty();
        routing.HoldElapsed().Should().BeEmpty();
    }

    [Test]
    public void AHoldTimerIsIgnoredOutsideAPressThatMayAsk()
    {
        var routing = new SelectionSpeechRouting();

        routing.HoldElapsed().Should().BeEmpty();
        routing.Submit(Command.Press, readAloudEnabled: true, askEnabled: false);
        routing.HoldElapsed().Should().BeEmpty();
        routing.SelectionResolved(Selection).Should().Equal(new RoutingAction.Speak(Selection));
    }

    [Test]
    public void WithNothingSelectedAPressThatMayAskDictatesAsUsual()
    {
        var routing = new SelectionSpeechRouting();
        routing.Submit(Command.Press, readAloudEnabled: true, askEnabled: true);
        routing.KeyLatched();

        routing.SelectionResolved(null).Should().Equal(RoutingAction.Forward(Command.Press));
        routing.HoldElapsed().Should().BeEmpty();
    }
}
