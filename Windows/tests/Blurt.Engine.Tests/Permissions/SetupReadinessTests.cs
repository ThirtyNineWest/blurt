using Blurt.Engine.Permissions;
using FluentAssertions;

namespace Blurt.Engine.Tests.Permissions;

public sealed class SetupReadinessTests
{
    private static readonly PermissionStatus Granted = new(Microphone: true);
    private static readonly PermissionStatus Denied = new(Microphone: false);

    [Test]
    public void ReadyRequiresThePermissionAndAKey()
    {
        SetupReadiness.IsReady(Granted, hasApiKey: true).Should().BeTrue();
    }

    [Test]
    public void AnyMissingInputBlocksReadiness()
    {
        SetupReadiness.IsReady(Granted, hasApiKey: false).Should().BeFalse();
        SetupReadiness.IsReady(Denied, hasApiKey: true).Should().BeFalse();
        SetupReadiness.IsReady(Denied, hasApiKey: false).Should().BeFalse();
    }

    [Test]
    public void ThePollCoastsOnceConfiguredAndStaysBriskWhileSettingUp()
    {
        SetupReadiness.PollInterval(isReady: false).Should().Be(SetupReadiness.SettingUpPollInterval);
        SetupReadiness.PollInterval(isReady: true).Should().Be(SetupReadiness.ReadyPollInterval);

        (SetupReadiness.ReadyPollInterval > SetupReadiness.SettingUpPollInterval).Should().BeTrue();
    }

    [Test]
    public void LosingAGrantedPermissionIsDetected()
    {
        Denied.LostGrant(since: Granted).Should().BeTrue();
    }

    [Test]
    public void ASteadyStateIsNotARevocation()
    {
        Granted.LostGrant(since: Granted).Should().BeFalse();
        Denied.LostGrant(since: Denied).Should().BeFalse();
    }

    [Test]
    public void AGrantIsNotARevocation()
    {
        Granted.LostGrant(since: Denied).Should().BeFalse();
    }
}
