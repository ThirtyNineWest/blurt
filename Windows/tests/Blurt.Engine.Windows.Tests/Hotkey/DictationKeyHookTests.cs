using Blurt.Engine.Config;
using Blurt.Engine.Hotkey;
using Blurt.Engine.Windows.Hotkey;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace Blurt.Engine.Windows.Tests.Hotkey;

public sealed class DictationKeyHookTests
{
    private const int RightControl = 0xA3;
    private const int RightShift = 0xA1;
    private const int KeyC = 0x43;
    private const int KeyV = 0x56;

    private InMemorySettingsStore _settings = null!;
    private FakeTimeProvider _clock = null!;
    private List<string> _calls = null!;
    private DictationKeyHook _hook = null!;

    [SetUp]
    public void SetUp()
    {
        _settings = new InMemorySettingsStore();
        _clock = new FakeTimeProvider();
        _calls = [];
        _hook = CreateHook();
    }

    [TearDown]
    public void TearDown() => _hook.Dispose();

    private DictationKeyHook CreateHook() => new(
        new TriggerKeyStore(_settings),
        new TriggerActivationStore(_settings),
        () => _calls.Add("start"),
        () => _calls.Add("stop"),
        () => _calls.Add("cancel"),
        () => _calls.Add("discarded"),
        _clock);

    private static DictationKeyRouter.Event Down(int vk = RightControl) =>
        new DictationKeyRouter.Event.FlagsChanged(vk, true);

    private static DictationKeyRouter.Event Up(int vk = RightControl) =>
        new DictationKeyRouter.Event.FlagsChanged(vk, false);

    private static TimeSpan At(double seconds) => TimeSpan.FromSeconds(seconds);

    [Test]
    public void AHoldStartsOnPressAndStopsOnRelease()
    {
        _hook.Dispatch(Down(), At(0));
        _hook.Dispatch(Up(), At(1.5));

        _calls.Should().Equal("start", "stop");
    }

    [Test]
    public void ATapLatchesAndTheNextTapStops()
    {
        _hook.Dispatch(Down(), At(0));
        _hook.Dispatch(Up(), At(0.2));
        _hook.Dispatch(Down(), At(3));
        _hook.Dispatch(Up(), At(3.2));

        _calls.Should().Equal("start", "stop");
    }

    [Test]
    public void AComboCancelsTheFreshCapture()
    {
        _hook.Dispatch(Down(), At(0));
        _hook.Dispatch(new DictationKeyRouter.Event.KeyDown(KeyC), At(0.1));
        _hook.Dispatch(Up(), At(0.2));

        _calls.Should().Equal("start", "cancel");
    }

    [Test]
    public void RepeatedTriggerDownsFromAutoRepeatStartOnce()
    {
        _hook.Dispatch(Down(), At(0));
        _hook.Dispatch(Down(), At(0.5));
        _hook.Dispatch(Down(), At(0.6));

        _calls.Should().Equal("start");
    }

    [Test]
    public void DispatchReturnsTheGateAction()
    {
        var action = _hook.Dispatch(Down(), At(0));

        action.Should().Be(DictationKeyGate.Action.Start);
    }

    [Test]
    public void TheInitialBindingComesFromTheStores()
    {
        _hook.Dispose();
        new TriggerKeyStore(_settings).TriggerKey = TriggerKey.RightShift;
        new TriggerActivationStore(_settings).Activation = TriggerActivation.Hold;
        _hook = CreateHook();

        _hook.Dispatch(Down(RightControl), At(0));
        _hook.Dispatch(Down(RightShift), At(1));
        _hook.Dispatch(Up(RightShift), At(1.1));

        _calls.Should().Equal("start", "stop");
    }

    [Test]
    public void RebindingMidRecordingFiresRecordingDiscarded()
    {
        _hook.Dispatch(Down(), At(0));
        new TriggerKeyStore(_settings).TriggerKey = TriggerKey.RightShift;

        _hook.RefreshBinding();

        _calls.Should().Equal("start", "discarded");
    }

    [Test]
    public void RebindingWhileIdleFiresNothing()
    {
        new TriggerKeyStore(_settings).TriggerKey = TriggerKey.RightShift;

        _hook.RefreshBinding();

        _calls.Should().BeEmpty();
    }

    [Test]
    public void AfterRebindingTheNewKeyDrivesAndTheOldKeyIsIgnored()
    {
        new TriggerKeyStore(_settings).TriggerKey = TriggerKey.RightShift;
        _hook.RefreshBinding();

        _hook.Dispatch(Down(RightControl), At(0));
        _hook.Dispatch(Down(RightShift), At(1));
        _hook.Dispatch(Up(RightShift), At(2.5));

        _calls.Should().Equal("start", "stop");
    }

    [Test]
    public void RefreshBindingPicksUpANewActivationMode()
    {
        new TriggerActivationStore(_settings).Activation = TriggerActivation.Tap;
        _hook.RefreshBinding();

        _hook.Dispatch(Down(), At(0));
        _hook.Dispatch(Up(), At(5));

        _calls.Should().Equal("start");
    }

    [Test]
    public void SyncAfterTerminalPhaseClearsALatchedGate()
    {
        _hook.Dispatch(Down(), At(0));
        _hook.Dispatch(Up(), At(0.2));

        _hook.SyncAfterTerminalPhase();
        _hook.Dispatch(Down(), At(5));

        _calls.Should().Equal("start", "discarded", "start");
    }

    [Test]
    public void SyncAfterTerminalPhaseWhileIdleFiresNothing()
    {
        _hook.SyncAfterTerminalPhase();

        _calls.Should().BeEmpty();
    }

    [Test]
    public void ARawHoldIsTimedByTheClock()
    {
        _hook.HandleRawKey(HookEventReducer.WmKeyDown, RightControl, 0, 0);
        _clock.Advance(TimeSpan.FromSeconds(1.5));
        _hook.HandleRawKey(HookEventReducer.WmKeyUp, RightControl, HookEventReducer.LlkhfUp, 0);

        _calls.Should().Equal("start", "stop");
    }

    [Test]
    public void ARawTapIsTimedByTheClock()
    {
        _hook.HandleRawKey(HookEventReducer.WmKeyDown, RightControl, 0, 0);
        _clock.Advance(TimeSpan.FromMilliseconds(200));
        _hook.HandleRawKey(HookEventReducer.WmKeyUp, RightControl, HookEventReducer.LlkhfUp, 0);

        _calls.Should().Equal("start");
    }

    [Test]
    public void ARawKeyDownFromOurOwnPasteIsNotACombo()
    {
        _hook.HandleRawKey(HookEventReducer.WmKeyDown, RightControl, 0, 0);
        _hook.HandleRawKey(
            HookEventReducer.WmKeyDown, KeyV, HookEventReducer.LlkhfInjected, HookEventReducer.InjectedTag);

        _calls.Should().Equal("start");
    }

    [Test]
    public void ANewHookIsNotInstalled()
    {
        _hook.IsInstalled.Should().BeFalse();
    }
}
