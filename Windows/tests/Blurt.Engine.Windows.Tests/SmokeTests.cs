using FluentAssertions;
using NUnit.Framework;

namespace Blurt.Engine.Windows.Tests;

public sealed class SmokeTests
{
    [Test]
    public void TestProjectBuildsAndRuns()
    {
        var value = 1 + 1;

        value.Should().Be(2);
    }
}
