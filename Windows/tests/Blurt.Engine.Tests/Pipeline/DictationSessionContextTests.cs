using Blurt.Engine.FocusCapture;
using Blurt.Engine.Pipeline;
using Blurt.Engine.STT;
using Blurt.Engine.Tests.Stubs;
using FluentAssertions;

namespace Blurt.Engine.Tests.Pipeline;

public sealed class DictationSessionContextTests
{
    private static readonly FocusedFieldContext Field = new(
        WindowTitle: "Re: Q3 pricing", FieldLabel: "Body", PriorText: "Hi Sam,", SelectedText: "the old plan", IsSecure: false);

    private static readonly FocusedFieldContext SecureField = new(
        WindowTitle: "1Password", FieldLabel: "Password", PriorText: null, SelectedText: null, IsSecure: true);

    private static readonly FocusTarget Mail = new(WindowHandle: 42, ProcessId: 501, ProcessName: "Mail", ProcessPath: null);

    [Test]
    public async Task ThePressTimeCaptureReachesTheTranscriberTheInjectorAndTheLog()
    {
        var focus = new StubFocusCapture { Foreground = Mail, FieldContext = Field };
        await using var harness = SessionHarness.WithTranscript("Hello world.", focus, keyTerms: ["AssemblyAI"]);

        await harness.DictateAsync();

        var expected = new TranscriptionContext(
            "Mail", "Re: Q3 pricing", "Body", "Hi Sam,", "the old plan", [], ["AssemblyAI"], [], TargetIsSecure: false);
        harness.Transcriber.ReceivedContexts.Should().Equal(expected);
        harness.Injector.Target.Should().Be(Mail);
        harness.Injector.Insertions.Should().Equal(new StubInjector.Insertion("Hello world.", "Hi Sam,", "Re: Q3 pricing"));
        var logged = (await harness.Log.TranscriptsAsync()).Should().ContainSingle().Subject;
        ((string?)logged["transcript"]).Should().Be("Hello world.");
        ((string?)logged["app"]).Should().Be("Mail");
        ((string?)logged["window"]).Should().Be("Re: Q3 pricing");
        ((string?)logged["field"]).Should().Be("Body");
        ((string?)logged["prior"]).Should().Be("Hi Sam,");
        ((string?)logged["selected"]).Should().Be("the old plan");
        logged["keyterms"]!.AsArray().Select(n => (string?)n).Should().Equal("AssemblyAI");
    }

    [Test]
    public async Task AnEmptyCaptureTranscribesWithNoContextRatherThanAnEmptyOne()
    {
        await using var harness = SessionHarness.WithTranscript("Hello world.");

        await harness.DictateAsync();

        harness.Transcriber.ReceivedContexts.Should().Equal([null]);
        harness.Injector.Insertions.Should().Equal(new StubInjector.Insertion("Hello world.", null, null));
    }

    [Test]
    public async Task EachPressCapturesAfresh()
    {
        var focus = new StubFocusCapture { FieldContext = Field };
        await using var harness = SessionHarness.WithTranscript("Second.", focus);

        await harness.DictateAsync();
        await harness.DictateAsync();

        harness.Transcriber.ReceivedContexts.Should().HaveCount(2).And.OnlyContain(c => c != null && c.PriorText == "Hi Sam,");
        focus.FieldContextCalls.Should().Be(2);
    }

    [Test]
    public async Task EachDictationBecomesTheNextOnesHistory()
    {
        var focus = new StubFocusCapture { FieldContext = Field };
        await using var harness = SessionHarness.WithTranscript("First thought.", focus);

        for (var i = 0; i < 3; i++)
        {
            await harness.DictateAsync();
        }

        var contexts = harness.Transcriber.ReceivedContexts;
        contexts.Should().HaveCount(3);
        contexts[0]!.RecentTranscripts.Should().BeEmpty();
        contexts[1]!.RecentTranscripts.Should().Equal("First thought.");
        contexts[2]!.RecentTranscripts.Should().Equal("First thought.", "First thought.");
        contexts[2]!.PriorText.Should().Be("Hi Sam,");
    }

    [Test]
    public async Task ADictationIntoASecureFieldIsNeverRememberedAsHistory()
    {
        var focus = new StubFocusCapture { FieldContext = SecureField };
        await using var harness = SessionHarness.WithTranscript("hunter2", focus);

        await harness.DictateAsync();
        await harness.DictateAsync();

        harness.Injector.Inserted.Should().Equal("hunter2", "hunter2");
        harness.Deliveries.Should().HaveCount(2).And.OnlyContain(d => d.Recent.Entries.Count == 0);
        harness.Transcriber.ReceivedContexts.Should().OnlyContain(c => c != null && c.RecentTranscripts.Count == 0);
    }

    [Test]
    public async Task AMissedBudgetStillSendsTheKeyTermsAndTheRecentTurns()
    {
        var clock = new TestClock();
        var focus = new StubFocusCapture { FieldContextGate = new ManualResetEventSlim(false) };
        await using var harness = new SessionHarness(
            transcriber: new StubTranscriber { TranscriptToReturn = "spoken" }, focus: focus, clock: clock, keyTerms: ["Kubernetes"]);

        for (var i = 0; i < 2; i++)
        {
            await harness.Session.PressAsync();
            await clock.WaitUntilSleepingAsync(DictationSession.ContextWaitBudget);
            clock.Advance(DictationSession.ContextWaitBudget);
            await harness.Session.ReleaseAsync();
            await harness.Session.WaitForTerminalAsync();
        }

        var contexts = harness.Transcriber.ReceivedContexts;
        contexts.Should().HaveCount(2).And.OnlyContain(c => c != null && c.KeyTerms.SequenceEqual(new[] { "Kubernetes" }));
        var second = contexts[1]!;
        second.RecentTranscripts.Should().Equal("spoken");
        second.PriorText.Should().BeNull();
        second.WindowTitle.Should().BeNull();
        second.TargetIsSecure.Should().BeFalse();
    }

    [Test]
    public async Task ASecureCaptureThatLandsAfterTheUploadsBudgetIsStillHonored()
    {
        var clock = new TestClock();
        var fieldGate = new ManualResetEventSlim(false);
        var focus = new StubFocusCapture { FieldContext = SecureField, FieldContextGate = fieldGate };
        await using var harness = new SessionHarness(
            transcriber: new StubTranscriber { TranscriptToReturn = "hunter2" }, focus: focus, clock: clock);
        await harness.Session.PressAsync();
        await clock.WaitUntilSleepingAsync(DictationSession.ContextWaitBudget);
        clock.Advance(DictationSession.ContextWaitBudget);
        await SessionWaits.WaitUntilAsync(() => harness.Transcriber.TranscribeCalls == 1, "the request to open");
        var resolvedBeforeRequest = harness.Session.CapturedContext;

        fieldGate.Set();
        await SessionWaits.WaitUntilAsync(() => harness.Session.CapturedContext is not null, "the late read");
        await harness.Session.ReleaseAsync();
        await harness.Session.WaitForTerminalAsync();

        resolvedBeforeRequest.Should().BeNull();
        harness.Injector.Inserted.Should().Equal("hunter2");
        harness.Transcriber.ReceivedContexts.Should().Equal([null]);
        harness.Session.CapturedContext!.TargetIsSecure.Should().BeTrue();
        harness.Deliveries.Should().ContainSingle().Which.Recent.Entries.Should().BeEmpty();
    }

    [Test]
    public async Task ASecureCaptureWithNoReadableTextIsStillCarriedNotCollapsedToNull()
    {
        var focus = new StubFocusCapture { FieldContext = new FocusedFieldContext(null, null, null, null, IsSecure: true) };
        await using var harness = SessionHarness.WithTranscript("hunter2", focus);

        await harness.DictateAsync();

        new TranscriptionContext(null, null, null, null, null, [], [], [], TargetIsSecure: true).IsEmpty.Should().BeFalse();
        harness.Transcriber.ReceivedContexts.Should().ContainSingle().Which!.TargetIsSecure.Should().BeTrue();
        harness.Deliveries.Should().ContainSingle().Which.Recent.Entries.Should().BeEmpty();
    }

    [Test]
    public async Task HistoryAloneKeepsTheContextWorthCarryingWhenTheCaptureIsEmpty()
    {
        await using var harness = SessionHarness.WithTranscript("Said this.");

        await harness.DictateAsync();
        await harness.DictateAsync();

        var contexts = harness.Transcriber.ReceivedContexts;
        contexts[0].Should().BeNull();
        contexts[1]!.RecentTranscripts.Should().Equal("Said this.");
    }

    [Test]
    public async Task AContextReadThatNeverCompletesIsAbandonedOnceTheBudgetElapses()
    {
        var clock = new TestClock();
        var focus = new StubFocusCapture { FieldContextGate = new ManualResetEventSlim(false) };
        await using var harness = new SessionHarness(
            transcriber: new StubTranscriber { TranscriptToReturn = "Spoken anyway." }, focus: focus, clock: clock);

        await harness.Session.PressAsync();
        await harness.Session.ReleaseAsync();
        await clock.WaitUntilSleepingAsync(DictationSession.ContextWaitBudget);
        clock.Advance(DictationSession.ContextWaitBudget);
        var phase = await harness.Session.WaitForTerminalAsync();

        phase.Should().Be(PipelinePhase.Pasted);
        harness.Injector.Inserted.Should().Equal("Spoken anyway.");
        harness.Transcriber.ReceivedContexts.Should().Equal([null]);
    }
}

public sealed class DictationSessionFailureLogTests
{
    [Test]
    public async Task AFailureIsLoggedOnceWithTheCapturedContext()
    {
        var focus = new StubFocusCapture
        {
            FieldContext = new FocusedFieldContext("Vault", "Password", null, null, IsSecure: false),
        };
        await using var harness = SessionHarness.WithTranscriberError(new BlurtException(new BlurtError.ApiKeyMissing()), focus);

        await harness.DictateAsync();

        var failure = (await harness.Log.FailuresAsync()).Should().ContainSingle().Subject;
        ((string?)failure["kind"]).Should().Be("apiKeyMissing");
        ((string?)failure["window"]).Should().Be("Vault");
        (await harness.Log.TranscriptsAsync()).Should().BeEmpty();
    }

    [Test]
    public async Task APressRefusedAtTheReadinessCheckIsLoggedBeforeAnythingIsCaptured()
    {
        await using var harness = new SessionHarness(readinessCheck: () => new BlurtError.ApiKeyMissing());

        await harness.Session.PressAsync();

        harness.Session.Phase.Should().Be(PipelinePhase.Failed(new BlurtError.ApiKeyMissing()));
        harness.Mic.StartCalls.Should().Be(0);
        (await harness.Log.FailureNamesAsync()).Should().Equal("apiKeyMissing");
    }

    [Test]
    public async Task TheQuietCopiedNoticeIsNotAFailureSoNothingIsLogged()
    {
        await using var harness = SessionHarness.WithTranscript("Copied text.");
        harness.Injector.ErrorToThrow = new BlurtException(new BlurtError.NoEditableTarget());

        var phase = await harness.DictateAsync();

        phase.Should().Be(PipelinePhase.NoTarget);
        (await harness.Log.FailuresAsync()).Should().BeEmpty();
        (await harness.Log.TranscriptsAsync()).Select(e => (string?)e["transcript"]).Should().Equal("Copied text.");
    }

    [Test]
    public async Task ACancelledDictationLogsNeitherAFailureNorATranscript()
    {
        await using var harness = SessionHarness.WithTranscript("Hello world.");

        await harness.Session.PressAsync();
        await harness.Session.CancelAsync();

        harness.Session.Phase.Should().Be(PipelinePhase.Cancelled);
        (await harness.Log.FailuresAsync()).Should().BeEmpty();
        (await harness.Log.TranscriptsAsync()).Should().BeEmpty();
    }

    [Test]
    public async Task AFailingMicTeardownDuringCancelIsLoggedWithoutRepaintingThePhase()
    {
        await using var harness = SessionHarness.WithTranscript("Hello world.");
        harness.Mic.StopError = new IOException("teardown failed");

        await harness.Session.PressAsync();
        await harness.Session.CancelAsync();

        harness.Session.Phase.Should().Be(PipelinePhase.Cancelled);
        (await harness.Log.FailureNamesAsync()).Should().Equal("audioCaptureFailed");
    }
}
