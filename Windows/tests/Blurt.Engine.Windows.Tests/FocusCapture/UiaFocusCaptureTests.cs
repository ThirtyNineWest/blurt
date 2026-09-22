using Blurt.Engine.Windows.FocusCapture;
using FluentAssertions;

namespace Blurt.Engine.Windows.Tests.FocusCapture;

public sealed class UiaFocusCaptureTests
{
    [Test]
    public void EveryRegisteredWindowIsOwn()
    {
        var ignored = new HashSet<nint> { 0x10, 0x20 };

        UiaFocusCapture.IsOwnWindow(0x10, ignored).Should().BeTrue();
        UiaFocusCapture.IsOwnWindow(0x20, ignored).Should().BeTrue();
    }

    [Test]
    public void AnUnregisteredWindowIsNotOwn()
    {
        UiaFocusCapture.IsOwnWindow(0x30, new HashSet<nint> { 0x10, 0x20 }).Should().BeFalse();
    }

    [Test]
    public void NoForegroundWindowIsTreatedAsOwn()
    {
        UiaFocusCapture.IsOwnWindow(0, new HashSet<nint>()).Should().BeTrue();
    }
}
