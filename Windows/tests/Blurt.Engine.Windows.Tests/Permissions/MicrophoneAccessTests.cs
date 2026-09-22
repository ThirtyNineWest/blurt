using Blurt.Engine.Permissions;
using Blurt.Engine.Windows.Permissions;
using FluentAssertions;

namespace Blurt.Engine.Windows.Tests.Permissions;

public sealed class MicrophoneAccessTests
{
    [TestCase("Deny", "Allow", "Allow")]
    [TestCase("Allow", "Deny", "Allow")]
    [TestCase("Allow", "Allow", "Deny")]
    [TestCase(null, null, "Deny")]
    [TestCase("deny", null, null)]
    [TestCase("Deny", "Prompt", null)]
    public void ADenyAtAnyLevelIsDenied(string? machine, string? user, string? nonPackaged)
    {
        var state = MicrophoneAccess.Classify(machine, user, nonPackaged);

        state.Should().Be(MicrophoneAccessState.Denied);
    }

    [TestCase("Allow", "Allow", "Allow")]
    [TestCase("Allow", null, null)]
    [TestCase(null, "Allow", null)]
    [TestCase(null, null, "Allow")]
    [TestCase("allow", "ALLOW", null)]
    public void AllowAtEveryPresentLevelIsGranted(string? machine, string? user, string? nonPackaged)
    {
        var state = MicrophoneAccess.Classify(machine, user, nonPackaged);

        state.Should().Be(MicrophoneAccessState.Granted);
    }

    [Test]
    public void NothingPresentIsUnknown()
    {
        var state = MicrophoneAccess.Classify(null, null, null);

        state.Should().Be(MicrophoneAccessState.Unknown);
    }

    [TestCase("Allow", "Prompt", null)]
    [TestCase("", null, null)]
    [TestCase(null, "Unexpected", "Allow")]
    public void AnUnrecognizedValueWithoutADenyIsUnknown(string? machine, string? user, string? nonPackaged)
    {
        var state = MicrophoneAccess.Classify(machine, user, nonPackaged);

        state.Should().Be(MicrophoneAccessState.Unknown);
    }
}
