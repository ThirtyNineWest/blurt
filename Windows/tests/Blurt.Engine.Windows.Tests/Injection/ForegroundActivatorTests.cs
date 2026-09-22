using Blurt.Engine.FocusCapture;
using Blurt.Engine.Windows.Injection;
using FluentAssertions;

namespace Blurt.Engine.Windows.Tests.Injection;

public sealed class ForegroundActivatorTests
{
    [Test]
    public async Task AHandleThatIsNotAWindowIsNotActivated()
    {
        var target = new FocusTarget(0, 0, null, null);

        var activated = await ForegroundActivator.ActivateAsync(target, TimeProvider.System, CancellationToken.None);

        activated.Should().BeFalse();
    }
}
