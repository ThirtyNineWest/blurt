using Blurt.Engine.Diagnostics;
using FluentAssertions;

namespace Blurt.Engine.Tests.Diagnostics;

[NonParallelizable]
public sealed class BlurtLogTests
{
    private Action<string>? _original;

    [SetUp]
    public void CaptureSink() => _original = BlurtLog.Sink;

    [TearDown]
    public void RestoreSink() => BlurtLog.Sink = _original;

    [Test]
    public void EveryLevelWritesLevelThenCategoryThenMessageToTheSink()
    {
        var lines = new List<string>();
        BlurtLog.Sink = lines.Add;

        BlurtLog.Info("Session", "started");
        BlurtLog.Warn("Mic", "slow route");
        BlurtLog.Error("STT", "401");

        lines.Should().Equal("INFO Session: started", "WARN Mic: slow route", "ERROR STT: 401");
    }

    [Test]
    public void ANullSinkDropsLinesWithoutThrowing()
    {
        BlurtLog.Sink = null;

        FluentActions.Invoking(() => BlurtLog.Info("Session", "dropped")).Should().NotThrow();
    }

    [Test]
    public void TheDefaultSinkIsSet()
    {
        _original.Should().NotBeNull();
    }
}
