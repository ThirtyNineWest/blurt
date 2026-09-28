using Blurt.Engine.Diagnostics;
using Blurt.Engine.FocusCapture;
using Blurt.Engine.Pipeline;
using Blurt.Engine.Tests.Stubs;
using FluentAssertions;

namespace Blurt.Engine.Tests.Pipeline;

[NonParallelizable]
public sealed class DictationOutcomeLogTests
{
    private const string Transcript = "Ship the Windows build today.";

    private static readonly FocusTarget Terminal =
        new(WindowHandle: 7, ProcessId: 900, ProcessName: "WindowsTerminal", ProcessPath: null);

    private readonly List<string> _lines = [];
    private Action<string>? _originalSink;

    [SetUp]
    public void CaptureSink()
    {
        _lines.Clear();
        _originalSink = BlurtLog.Sink;
        BlurtLog.Sink = line =>
        {
            lock (_lines)
            {
                _lines.Add(line);
            }
        };
    }

    [TearDown]
    public void RestoreSink() => BlurtLog.Sink = _originalSink;

    [Test]
    public async Task APastedDictationLogsItsOutcomeLengthAndTarget()
    {
        await using var harness = SessionHarness.WithTranscript(Transcript, new StubFocusCapture { Foreground = Terminal });

        var phase = await harness.DictateAsync();
        await harness.Session.AwaitPipelineAsync();

        phase.Should().Be(PipelinePhase.Pasted);
        OutcomeLines().Should().Equal($"INFO Session: dictation ended: pasted, {Transcript.Length} chars, target=WindowsTerminal");
    }

    [Test]
    public async Task ANoTargetDictationLogsThatTheTranscriptWasLeftOnTheClipboard()
    {
        await using var harness = new SessionHarness(
            transcriber: new StubTranscriber { TranscriptToReturn = Transcript },
            injector: new StubInjector { ErrorToThrow = new BlurtException(new BlurtError.TargetAppLost()) },
            focus: new StubFocusCapture { Foreground = Terminal });

        var phase = await harness.DictateAsync();
        await harness.Session.AwaitPipelineAsync();

        phase.Should().Be(PipelinePhase.NoTarget);
        OutcomeLines().Should().Equal(
            $"INFO Session: dictation ended: copied to clipboard, {Transcript.Length} chars, target=WindowsTerminal");
    }

    [Test]
    public async Task AFailedPasteLogsTheErrorsDiagnosticName()
    {
        await using var harness = new SessionHarness(
            transcriber: new StubTranscriber { TranscriptToReturn = Transcript },
            injector: new StubInjector { ErrorToThrow = new InvalidOperationException("paste broke") },
            focus: new StubFocusCapture { Foreground = Terminal });

        await harness.DictateAsync();
        await harness.Session.AwaitPipelineAsync();

        OutcomeLines().Should().Equal(
            $"INFO Session: dictation ended: failed: targetAppLost, {Transcript.Length} chars, target=WindowsTerminal");
    }

    [Test]
    public async Task AnUnknownTargetIsOmittedAndTheTranscriptIsNeverLogged()
    {
        await using var harness = SessionHarness.WithTranscript(Transcript);

        await harness.DictateAsync();
        await harness.Session.AwaitPipelineAsync();

        OutcomeLines().Should().Equal($"INFO Session: dictation ended: pasted, {Transcript.Length} chars");
        Snapshot().Should().NotContain(line => line.Contains(Transcript, StringComparison.Ordinal));
    }

    [Test]
    public async Task ADictationThatNeverProducedATranscriptLogsNoOutcome()
    {
        await using var harness = SessionHarness.WithTranscriberError(new HttpRequestException("AssemblyAI error 500"));

        await harness.DictateAsync();
        await harness.Session.AwaitPipelineAsync();

        OutcomeLines().Should().BeEmpty();
    }

    private List<string> Snapshot()
    {
        lock (_lines)
        {
            return [.. _lines];
        }
    }

    private List<string> OutcomeLines() =>
        Snapshot().Where(line => line.StartsWith("INFO Session: dictation ended:", StringComparison.Ordinal)).ToList();
}
