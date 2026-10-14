using Blurt.Engine.Permissions;
using FluentAssertions;

namespace Blurt.Engine.Tests.Permissions;

public sealed class SetupWizardTests
{
    private static readonly PermissionStatus Granted = new(Microphone: true);
    private static readonly PermissionStatus Denied = new(Microphone: false);

    [Test]
    public void AFreshInstallStartsOnTheApiKeyStepAndIsNotCompleted()
    {
        var wizard = new SetupWizard(isReady: false);

        wizard.Step.Should().Be(SetupStep.ApiKey);
        wizard.IsCompleted.Should().BeFalse();
        wizard.StepNumber.Should().Be(1);
        wizard.CanGoBack.Should().BeFalse();
    }

    [Test]
    public void AnInstallReadyAtLaunchShowsTheReadyViewAtOnce()
    {
        var wizard = new SetupWizard(isReady: true);

        wizard.ShowsReadyView(isReady: true).Should().BeTrue();
    }

    [Test]
    public void TheApiKeyStepContinuesOnlyOnceAKeyIsStored()
    {
        var wizard = new SetupWizard(isReady: false);

        wizard.CanContinue(Granted, hasApiKey: false).Should().BeFalse();
        wizard.Continue(Granted, hasApiKey: false).Should().BeFalse();
        wizard.Step.Should().Be(SetupStep.ApiKey);

        wizard.Continue(Granted, hasApiKey: true).Should().BeTrue();
        wizard.Step.Should().Be(SetupStep.Microphone);
    }

    [Test]
    public void TheMicrophoneStepContinuesOnlyWithAccess()
    {
        var wizard = new SetupWizard(isReady: false);
        wizard.Continue(Denied, hasApiKey: true);

        wizard.CanContinue(Denied, hasApiKey: true).Should().BeFalse();

        wizard.CanContinue(Granted, hasApiKey: true).Should().BeTrue();
    }

    [Test]
    public void TheStepsRunInOrderAndTheLastCompletesTheWizard()
    {
        var wizard = new SetupWizard(isReady: false);
        List<SetupStep> seen = [wizard.Step];

        while (!wizard.IsLastStep)
        {
            wizard.Continue(Granted, hasApiKey: true);
            seen.Add(wizard.Step);
        }

        seen.Should().Equal(SetupStep.ApiKey, SetupStep.Microphone, SetupStep.Shortcut, SetupStep.Sound);
        wizard.ShowsReadyView(isReady: true).Should().BeFalse();

        wizard.Continue(Granted, hasApiKey: true).Should().BeTrue();

        wizard.IsCompleted.Should().BeTrue();
        wizard.ShowsReadyView(isReady: true).Should().BeTrue();
    }

    [Test]
    public void TheLastStepCannotFinishWhileSetupIsNotReady()
    {
        var wizard = OnSoundStep();

        wizard.Continue(Denied, hasApiKey: true).Should().BeFalse();

        wizard.IsCompleted.Should().BeFalse();
    }

    [Test]
    public void BackRetracesOneStepAndStopsAtTheFirst()
    {
        var wizard = OnSoundStep();

        wizard.Back().Should().BeTrue();
        wizard.Step.Should().Be(SetupStep.Shortcut);
        wizard.Back();
        wizard.Back();

        wizard.Back().Should().BeFalse();
        wizard.Step.Should().Be(SetupStep.ApiKey);
    }

    [Test]
    public void ACompletedWizardStillShowsItselfWhileSetupIsNotReady()
    {
        var wizard = new SetupWizard(isReady: true);

        wizard.ShowsReadyView(isReady: false).Should().BeFalse();
    }

    [Test]
    public void LosingTheKeyShowsTheWizardOnTheApiKeyStep()
    {
        var wizard = new SetupWizard(isReady: true);

        wizard.ReadinessLost(Denied, hasApiKey: false);

        wizard.ShowsReadyView(isReady: false).Should().BeFalse();
        wizard.Step.Should().Be(SetupStep.ApiKey);
    }

    [Test]
    public void LosingTheMicrophoneShowsTheWizardOnTheMicrophoneStep()
    {
        var wizard = new SetupWizard(isReady: true);

        wizard.ReadinessLost(Denied, hasApiKey: true);

        wizard.Step.Should().Be(SetupStep.Microphone);
        wizard.ShowsReadyView(isReady: false).Should().BeFalse();
    }

    [Test]
    public void ACompletedWizardReturnsToTheReadyViewOnceSetupIsReadyAgain()
    {
        var wizard = new SetupWizard(isReady: true);
        wizard.ReadinessLost(Denied, hasApiKey: true);

        var showsReady = wizard.ShowsReadyView(isReady: true);

        showsReady.Should().BeTrue();
    }

    [Test]
    public void ShowingAStepMovesTheWizardThere()
    {
        var wizard = new SetupWizard(isReady: false);
        wizard.Continue(Granted, hasApiKey: true);

        wizard.Show(SetupStep.ApiKey);

        wizard.Step.Should().Be(SetupStep.ApiKey);
        wizard.IsCompleted.Should().BeFalse();
    }

    private static SetupWizard OnSoundStep()
    {
        var wizard = new SetupWizard(isReady: false);
        wizard.Continue(Granted, hasApiKey: true);
        wizard.Continue(Granted, hasApiKey: true);
        wizard.Continue(Granted, hasApiKey: true);
        wizard.Step.Should().Be(SetupStep.Sound);
        return wizard;
    }
}
