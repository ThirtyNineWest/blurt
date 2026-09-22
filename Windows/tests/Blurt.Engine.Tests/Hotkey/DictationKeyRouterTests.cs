using Blurt.Engine.Hotkey;
using FluentAssertions;

namespace Blurt.Engine.Tests.Hotkey;

/// <summary>
/// The router's three jobs on top of <see cref="DictationKeyGate"/> (whose tap/hold semantics have
/// their own suite): only the bound keycode's flag edges reach the gate — a flags delivery re-reports
/// the state whether or not it changed, so a repeat must not double-fire — reset/rebind report
/// whether they discarded a live recording the host has to cancel upstream, and dropped-event
/// recovery decides whether a disabled-then-re-enabled hook keeps the gate's state.
/// </summary>
public sealed class DictationKeyRouterTests
{
    private static readonly int Trigger = TriggerKey.RightControl.KeyCode();
    private static readonly int OtherModifier = TriggerKey.RightShift.KeyCode();

    private static DictationKeyRouter.Event DownEvent(int keyCode) => new DictationKeyRouter.Event.FlagsChanged(keyCode, true);

    private static DictationKeyRouter.Event UpEvent(int keyCode) => new DictationKeyRouter.Event.FlagsChanged(keyCode, false);

    [Test]
    public void AHeldPressIsStartThenStop()
    {
        var router = new DictationKeyRouter(Trigger);

        router.Handle(DownEvent(Trigger), TimeSpan.Zero).Should().Be(DictationKeyGate.Action.Start);
        router.Handle(UpEvent(Trigger), TimeSpan.FromSeconds(2)).Should().Be(DictationKeyGate.Action.Stop);
    }

    [Test]
    public void ARepeatedDownStateDeliveryDoesNotRefireTheGate()
    {
        // While the trigger is held, another flags delivery can re-report its bit still set; re-arming
        // the gate on it would corrupt the tap/hold timing.
        var router = new DictationKeyRouter(Trigger);

        router.Handle(DownEvent(Trigger), TimeSpan.Zero).Should().Be(DictationKeyGate.Action.Start);
        router.Handle(DownEvent(Trigger), TimeSpan.FromMilliseconds(50)).Should().Be(DictationKeyGate.Action.None);

        // The eventual release still stops the (single) dictation.
        router.Handle(UpEvent(Trigger), TimeSpan.FromSeconds(2)).Should().Be(DictationKeyGate.Action.Stop);
    }

    [Test]
    public void AnUpStateDeliveryWithNoTrackedDownIsIgnored()
    {
        var router = new DictationKeyRouter(Trigger);

        router.Handle(UpEvent(Trigger), TimeSpan.Zero).Should().Be(DictationKeyGate.Action.None);
    }

    [Test]
    public void FlagChangesReportedForAnotherKeycodeNeverReachTheGate()
    {
        // E.g. right Alt going down while right Ctrl is bound: the delivery's flags may even carry the
        // trigger's bit, but the event isn't about the bound key.
        var router = new DictationKeyRouter(Trigger);

        router.Handle(DownEvent(OtherModifier), TimeSpan.Zero).Should().Be(DictationKeyGate.Action.None);
        router.Handle(UpEvent(OtherModifier), TimeSpan.FromSeconds(2)).Should().Be(DictationKeyGate.Action.None);
    }

    [Test]
    public void AnotherKeyOverAFreshPressIsAComboAndCancels()
    {
        var router = new DictationKeyRouter(Trigger);

        router.Handle(DownEvent(Trigger), TimeSpan.Zero).Should().Be(DictationKeyGate.Action.Start);
        router.Handle(new DictationKeyRouter.Event.KeyDown(8), TimeSpan.FromMilliseconds(100)).Should().Be(DictationKeyGate.Action.Cancel);
    }

    [Test]
    public void TheTriggersOwnKeyDownIsNotACombo()
    {
        var router = new DictationKeyRouter(Trigger);

        router.Handle(DownEvent(Trigger), TimeSpan.Zero).Should().Be(DictationKeyGate.Action.Start);
        router.Handle(new DictationKeyRouter.Event.KeyDown(Trigger), TimeSpan.FromMilliseconds(100)).Should().Be(DictationKeyGate.Action.None);
        router.Handle(UpEvent(Trigger), TimeSpan.FromSeconds(2)).Should().Be(DictationKeyGate.Action.Stop);
    }

    [Test]
    public void AShortTapLatchesTheNextTapStops()
    {
        var router = new DictationKeyRouter(Trigger);

        router.Handle(DownEvent(Trigger), TimeSpan.Zero).Should().Be(DictationKeyGate.Action.Start);
        router.Handle(UpEvent(Trigger), TimeSpan.FromMilliseconds(200)).Should().Be(DictationKeyGate.Action.None); // latched
        router.Handle(DownEvent(Trigger), TimeSpan.FromSeconds(5)).Should().Be(DictationKeyGate.Action.None);
        router.Handle(UpEvent(Trigger), TimeSpan.FromSeconds(5) + TimeSpan.FromMilliseconds(200)).Should().Be(DictationKeyGate.Action.Stop);
    }

    [Test]
    public void ResetWhileIdleReportsNothingDiscarded()
    {
        var router = new DictationKeyRouter(Trigger);

        var discarded = router.Reset();

        discarded.Should().BeFalse();
    }

    [Test]
    public void ResetMidRecordingReportsTheDiscardedRecording()
    {
        var router = new DictationKeyRouter(Trigger);
        router.Handle(DownEvent(Trigger), TimeSpan.Zero).Should().Be(DictationKeyGate.Action.Start);

        var discarded = router.Reset();

        discarded.Should().BeTrue();

        // The tracker cleared too: the stale key-up is ignored, a new press starts.
        router.Handle(UpEvent(Trigger), TimeSpan.FromSeconds(2)).Should().Be(DictationKeyGate.Action.None);
        router.Handle(DownEvent(Trigger), TimeSpan.FromSeconds(3)).Should().Be(DictationKeyGate.Action.Start);
    }

    [Test]
    public void ResetOverALatchedRecordingReportsTheDiscardedRecording()
    {
        var router = new DictationKeyRouter(Trigger);
        router.Handle(DownEvent(Trigger), TimeSpan.Zero).Should().Be(DictationKeyGate.Action.Start);
        router.Handle(UpEvent(Trigger), TimeSpan.FromMilliseconds(200)).Should().Be(DictationKeyGate.Action.None); // latched

        var discarded = router.Reset();

        discarded.Should().BeTrue();
    }

    [Test]
    public void ALatchLeftBehindByAKeylessDictationEndDoesNotSwallowTheNextPress()
    {
        // The bug this pins: when a dictation ends WITHOUT a key event — the auto-release cap fires, or
        // the press is refused/failed — the gate stays latched. A latched ModifierDown returns None and
        // the ModifierUp after it returns Stop, which no-ops on an already-terminal session, so the
        // user's whole next press does nothing. The host's terminal-phase sync calls Reset() to clear
        // it; this pins that a reset genuinely restores the next press.
        var router = new DictationKeyRouter(Trigger);
        router.Handle(DownEvent(Trigger), TimeSpan.Zero).Should().Be(DictationKeyGate.Action.Start);
        router.Handle(UpEvent(Trigger), TimeSpan.FromMilliseconds(200)).Should().Be(DictationKeyGate.Action.None); // latched

        // Without the reset, this next tap is swallowed — the exact dead press.
        var swallowed = router;

        swallowed.Handle(DownEvent(Trigger), TimeSpan.FromSeconds(5)).Should().Be(DictationKeyGate.Action.None);

        router.Reset();

        router.Handle(DownEvent(Trigger), TimeSpan.FromSeconds(5)).Should().Be(DictationKeyGate.Action.Start);
    }

    [Test]
    public void ResetAfterAKeylessEndIgnoresAStaleKeyUpThenStartsCleanly()
    {
        // The auto-release/failed-press case where the trigger is still physically held when the phase
        // goes terminal. The host resets anyway (the dictation is over), which clears the modifier
        // tracker — so the release that follows must route to None rather than emitting a spurious
        // Stop, and the press after that must start normally.
        var router = new DictationKeyRouter(Trigger);
        router.Handle(DownEvent(Trigger), TimeSpan.Zero).Should().Be(DictationKeyGate.Action.Start);

        router.Reset(); // terminal phase arrived while the key is still down

        router.Handle(UpEvent(Trigger), TimeSpan.FromMilliseconds(300)).Should().Be(DictationKeyGate.Action.None);
        router.Handle(DownEvent(Trigger), TimeSpan.FromSeconds(2)).Should().Be(DictationKeyGate.Action.Start);
    }

    [Test]
    public void RebindMidRecordingDiscardsItAndSwitchesKeycodes()
    {
        var router = new DictationKeyRouter(Trigger);
        router.Handle(DownEvent(Trigger), TimeSpan.Zero).Should().Be(DictationKeyGate.Action.Start);

        // Rebinding means the old key's up-event can never match — the caller must cancel the capture
        // rather than let the auto-release cap paste it.
        var discarded = router.Rebind(OtherModifier, TriggerActivation.TapOrHold);

        discarded.Should().BeTrue();
        router.TriggerKeyCode.Should().Be(OtherModifier);

        // The old key is now irrelevant; the new one drives dictation.
        router.Handle(DownEvent(Trigger), TimeSpan.FromSeconds(1)).Should().Be(DictationKeyGate.Action.None);
        router.Handle(DownEvent(OtherModifier), TimeSpan.FromSeconds(2)).Should().Be(DictationKeyGate.Action.Start);
    }

    [Test]
    public void RebindWhileIdleReportsNothingDiscarded()
    {
        var router = new DictationKeyRouter(Trigger);

        var discarded = router.Rebind(OtherModifier, TriggerActivation.TapOrHold);

        discarded.Should().BeFalse();
    }

    [Test]
    public void RebindSwapsTheActivationModeTheGateApplies()
    {
        var router = new DictationKeyRouter(Trigger);
        router.Rebind(Trigger, TriggerActivation.Hold);

        // Under hold-only a quick release stops instead of latching — the tap-or-hold gate this router
        // was built with would have returned None here.
        router.Handle(DownEvent(Trigger), TimeSpan.Zero).Should().Be(DictationKeyGate.Action.Start);
        router.Handle(UpEvent(Trigger), TimeSpan.FromMilliseconds(200)).Should().Be(DictationKeyGate.Action.Stop);
    }

    [Test]
    public void DroppedEventRecoveryKeepsARecordingWhoseTriggerIsStillHeld()
    {
        // The hook was disabled mid-sentence. The key-up hasn't happened yet, so it is still coming and
        // the gate is coherent — resetting here would throw away speech the user is in the middle of.
        var router = new DictationKeyRouter(Trigger);
        router.Handle(DownEvent(Trigger), TimeSpan.Zero).Should().Be(DictationKeyGate.Action.Start);

        var discarded = router.RecoverFromDroppedEvents(triggerStillHeld: true);

        discarded.Should().BeFalse();

        // The gate kept its state, so the eventual release still stops this dictation rather than
        // routing to None as it would after a reset.
        router.Handle(UpEvent(Trigger), TimeSpan.FromSeconds(2)).Should().Be(DictationKeyGate.Action.Stop);
    }

    [Test]
    public void DroppedEventRecoveryDiscardsARecordingWhoseKeyUpWasLost()
    {
        // The trigger is no longer held, so its key-up was among the dropped events and will never
        // arrive. Left latched, the session would sit in "recording" until the auto-release cap pasted
        // an unprompted transcript — so the reset must report the discarded recording for the host to
        // cancel upstream.
        var router = new DictationKeyRouter(Trigger);
        router.Handle(DownEvent(Trigger), TimeSpan.Zero).Should().Be(DictationKeyGate.Action.Start);

        var discarded = router.RecoverFromDroppedEvents(triggerStillHeld: false);

        discarded.Should().BeTrue();

        // The tracker was cleared with the gate, so a stale up can't emit a spurious Stop, and the next
        // press starts cleanly.
        router.Handle(UpEvent(Trigger), TimeSpan.FromMilliseconds(300)).Should().Be(DictationKeyGate.Action.None);
        router.Handle(DownEvent(Trigger), TimeSpan.FromSeconds(2)).Should().Be(DictationKeyGate.Action.Start);
    }

    [Test]
    public void DroppedEventRecoveryOverAnIdleGateDiscardsNothingEitherWay()
    {
        // The common case: the hook times out with no dictation in flight. Neither branch may claim a
        // recording was discarded, or the host cancels a session that was never recording.
        var router = new DictationKeyRouter(Trigger);

        var discardedAfterRelease = router.RecoverFromDroppedEvents(triggerStillHeld: false);

        discardedAfterRelease.Should().BeFalse();

        var discardedWhileHeld = router.RecoverFromDroppedEvents(triggerStillHeld: true);

        discardedWhileHeld.Should().BeFalse();

        // Still usable afterwards.
        router.Handle(DownEvent(Trigger), TimeSpan.FromSeconds(1)).Should().Be(DictationKeyGate.Action.Start);
    }

    [Test]
    public void DroppedEventRecoveryDiscardsALatchedTapToToggleRecording()
    {
        // A tapped recording has no key held by definition, so triggerStillHeld is false and the gate is
        // latched — the state most at risk of being stranded, since nothing is coming to close it.
        var router = new DictationKeyRouter(Trigger);
        router.Handle(DownEvent(Trigger), TimeSpan.Zero).Should().Be(DictationKeyGate.Action.Start);
        router.Handle(UpEvent(Trigger), TimeSpan.FromMilliseconds(100)).Should().Be(DictationKeyGate.Action.None); // latched

        var discarded = router.RecoverFromDroppedEvents(triggerStillHeld: false);

        discarded.Should().BeTrue();
    }
}
