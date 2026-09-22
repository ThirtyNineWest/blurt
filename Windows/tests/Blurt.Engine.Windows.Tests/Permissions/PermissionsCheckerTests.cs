using Blurt.Engine.Permissions;
using Blurt.Engine.Windows.Permissions;
using FluentAssertions;

namespace Blurt.Engine.Windows.Tests.Permissions;

public sealed class PermissionsCheckerTests
{
    [TestCase(MicrophoneAccessState.Granted, true)]
    [TestCase(MicrophoneAccessState.Denied, false)]
    [TestCase(MicrophoneAccessState.NoDevice, false)]
    [TestCase(MicrophoneAccessState.Unknown, false)]
    public void CheckMapsTheProbeResultToMicrophoneGrant(MicrophoneAccessState probeResult, bool expectedMicrophone)
    {
        var checker = new PermissionsChecker(() => probeResult);

        var status = checker.Check();

        status.Should().Be(new PermissionStatus(expectedMicrophone));
    }

    [Test]
    public void CheckConsultsTheProbeExactlyOnce()
    {
        var calls = 0;
        var checker = new PermissionsChecker(() =>
        {
            calls++;
            return MicrophoneAccessState.Granted;
        });

        checker.Check();

        calls.Should().Be(1);
    }
}
