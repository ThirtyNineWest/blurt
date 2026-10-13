using System.Text.Json.Nodes;
using Blurt.Engine.Pipeline;
using Blurt.Engine.STT;
using Blurt.Engine.Tests.Stubs;
using FluentAssertions;

namespace Blurt.Engine.Tests.Pipeline;

public sealed class DictationLogEntryTests
{
    private static readonly DateTimeOffset Moment = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).AddMilliseconds(123);

    private static readonly TranscriptionContext Context = new(
        "Mail", "Re: Q3 pricing", "Body", "Hi Sam,", "the old plan", [], [], [], false);

    private static DictationLog Log => new(() => true, Path.GetTempPath(), TestLog.RenderPrompt, TestLog.FitKeyterms);

    [Test]
    public void CarriesTheTranscriptAndAnIso8601UtcTimestampWithMilliseconds()
    {
        var entry = Log.MakeEntry("Polished.", null, Moment);

        ((string?)entry["transcript"]).Should().Be("Polished.");
        ((string?)entry["ts"]).Should().Be("2023-11-14T22:13:20.123Z");
    }

    [Test]
    public void StampsTheTimestampInUtcWhateverTheOffset()
    {
        var entry = Log.MakeEntry("p", null, Moment.ToOffset(TimeSpan.FromHours(-7)));

        ((string?)entry["ts"]).Should().Be("2023-11-14T22:13:20.123Z");
    }

    [Test]
    public void ThreadsEveryFocusContextFieldIncludingTheSelectedText()
    {
        var entry = Log.MakeEntry("p", Context, Moment);

        ((string?)entry["app"]).Should().Be("Mail");
        ((string?)entry["window"]).Should().Be("Re: Q3 pricing");
        ((string?)entry["field"]).Should().Be("Body");
        ((string?)entry["prior"]).Should().Be("Hi Sam,");
        ((string?)entry["selected"]).Should().Be("the old plan");
    }

    [Test]
    public void OmitsEveryContextFieldWhenNothingWasCaptured()
    {
        var entry = Log.MakeEntry("p", null, Moment);

        entry.Select(kv => kv.Key).Should().Equal("transcript", "ts");
    }

    [Test]
    public void KeysAreSortedSoTheLineIsByteStable()
    {
        var withEverything = Context with { RecentTranscripts = ["Sent the deck over."], KeyTerms = ["AssemblyAI"] };

        var entry = Log.MakeEntry("p", withEverything, Moment);

        entry.Select(kv => kv.Key).Should().Equal(
            "app", "field", "keyterms", "prior", "selected", "stt_prompt", "transcript", "ts", "window");
    }

    [Test]
    public void LogsThePromptTheRendererBuildsForTheContext()
    {
        var withHistory = Context with { RecentTranscripts = ["Sent the deck over."] };

        var entry = Log.MakeEntry("p", withHistory, Moment);

        ((string?)entry["stt_prompt"]).Should().Be(TestLog.RenderPrompt(withHistory));
        ((string?)entry["stt_prompt"]).Should().Be("Sent the deck over.\nHi Sam,");
    }

    [Test]
    public void KeepsThePriorChunkRawWhereThePromptCarryingItIsTrimmed()
    {
        var context = new TranscriptionContext("Mail", null, null, "Hi Sam, ", null, [], [], [], false);

        var entry = Log.MakeEntry("p", context, Moment);

        ((string?)entry["prior"]).Should().Be("Hi Sam, ");
        ((string?)entry["stt_prompt"]).Should().Be("Hi Sam,");
    }

    [Test]
    public void ANullPriorTellsAHistoryOnlyPromptApartFromAPriorChunk()
    {
        var context = new TranscriptionContext("Mail", null, null, null, null, ["Said this before."], [], [], false);

        var entry = Log.MakeEntry("p", context, Moment);

        entry.ContainsKey("prior").Should().BeFalse();
        ((string?)entry["stt_prompt"]).Should().Be("Said this before.");
    }

    [Test]
    public void RecordsTheKeyTermsFittedTheWayTheRequestFitsThem()
    {
        var context = new TranscriptionContext("Mail", null, null, "Hi Sam,", null, [], ["AssemblyAI", "  ", "LeMUR"], [], false);

        var entry = Log.MakeEntry("p", context, Moment);

        entry["keyterms"]!.AsArray().Select(n => (string?)n).Should().Equal("AssemblyAI", "LeMUR");
    }

    [Test]
    public void OmitsTheKeyTermsAndPromptWhenEmpty()
    {
        var context = new TranscriptionContext("Mail", null, null, null, null, [], ["  "], [], false);

        var entry = Log.MakeEntry("p", context, Moment);

        entry.ContainsKey("keyterms").Should().BeFalse();
        entry.ContainsKey("stt_prompt").Should().BeFalse();
    }

    [Test]
    public void KeepsATranscriptVerbatimIncludingNonAscii()
    {
        const string transcript = "Café — 北京 🎙️.";

        var entry = Log.MakeEntry(transcript, null, Moment);

        ((string?)entry["transcript"]).Should().Be(transcript);
    }
}

public sealed class DictationLogTests
{
    private TestLog _log = null!;

    [SetUp]
    public void CreateLog() => _log = new TestLog();

    [TearDown]
    public void DeleteLog() => _log.Dispose();

    [Test]
    public void ThePathsLiveInTheDirectoryAndReadingThemTouchesNothing()
    {
        var paths = (_log.Log.DictationsPath, _log.Log.ErrorsPath);

        paths.Should().Be((Path.Combine(_log.Directory, "dictations.jsonl"), Path.Combine(_log.Directory, "errors.jsonl")));
        Directory.Exists(_log.Directory).Should().BeFalse();
    }

    [Test]
    public async Task CreatesTheFileOnFirstAppend()
    {
        var existedBefore = File.Exists(_log.Log.DictationsPath);

        _log.Log.Append("Hi.", null);
        await _log.Log.DrainAsync();

        existedBefore.Should().BeFalse();
        File.Exists(_log.Log.DictationsPath).Should().BeTrue();
    }

    [Test]
    public async Task WritesOneJsonObjectPerLineTerminatedByLineFeed()
    {
        _log.Log.Append("Polished.", null);
        await _log.Log.DrainAsync();

        var contents = await File.ReadAllTextAsync(_log.Log.DictationsPath);
        contents.Should().EndWith("\n").And.NotContain("\r");
        contents.Split('\n').Should().HaveCount(2);
        ((string?)JsonNode.Parse(contents)!["transcript"]).Should().Be("Polished.");
    }

    [Test]
    public async Task WritesNonAsciiUnescapedAndWithoutAByteOrderMark()
    {
        _log.Log.Append("Café 北京", null);
        await _log.Log.DrainAsync();

        var bytes = await File.ReadAllBytesAsync(_log.Log.DictationsPath);
        bytes.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
        System.Text.Encoding.UTF8.GetString(bytes).Should().Contain("Café 北京");
    }

    [Test]
    public async Task AppendsInOrderAndPreservesExistingEntries()
    {
        _log.Log.Append("A.", null);
        _log.Log.Append("B.", null);
        _log.Log.Append("C.", null);

        var entries = await _log.TranscriptsAsync();

        entries.Select(e => (string?)e["transcript"]).Should().Equal("A.", "B.", "C.");
    }

    [Test]
    public async Task ANullEntryFieldIsOmittedFromTheLineNotWrittenAsNull()
    {
        _log.Log.Append("p", null);
        await _log.Log.DrainAsync();

        var line = await File.ReadAllTextAsync(_log.Log.DictationsPath);
        ((string?)JsonNode.Parse(line)!["transcript"]).Should().Be("p");
        line.Should().NotContain("selected").And.NotContain("stt_prompt").And.NotContain("keyterms").And.NotContain("null");
    }

    [Test]
    public async Task ANonEmptyPromptAndKeyTermsAreWrittenWithTheirValues()
    {
        var context = new TranscriptionContext("Mail", null, null, "Hi Sam,", null, [], ["AssemblyAI", "LeMUR"], [], false);

        _log.Log.Append("p", context);
        var entry = (await _log.TranscriptsAsync()).Should().ContainSingle().Subject;

        entry["keyterms"]!.AsArray().Select(n => (string?)n).Should().Equal("AssemblyAI", "LeMUR");
        ((string?)entry["stt_prompt"]).Should().Be("Hi Sam,");
    }

    [Test]
    public async Task TheEntryIsStampedByTheClockWhenAppendIsCalled()
    {
        var clock = new TestClock();
        using var log = new TestLog(clock);
        var stamped = clock.GetUtcNow();

        log.Log.Append("p", null);
        clock.Advance(TimeSpan.FromMinutes(5));
        var entry = (await log.TranscriptsAsync()).Should().ContainSingle().Subject;

        ((string?)entry["ts"]).Should().Be(stamped.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture));
    }
}

public sealed class DictationLogGateTests
{
    [Test]
    public async Task WithDeveloperModeOffAppendWritesNothingToDisk()
    {
        using var log = new TestLog(isEnabled: () => false);

        log.Log.Append("private dictation", null);
        await log.Log.DrainAsync();

        Directory.Exists(log.Directory).Should().BeFalse();
    }

    [Test]
    public async Task WithDeveloperModeOnAppendWritesTheTranscript()
    {
        using var log = new TestLog();

        log.Log.Append("logged dictation", null);
        await log.Log.DrainAsync();

        (await File.ReadAllTextAsync(log.Log.DictationsPath)).Should().Contain("logged dictation");
    }

    [Test]
    public async Task TheGateIsCheckedBeforeTheContextIsTouched()
    {
        var context = new TranscriptionContext("1Password", "Vault", "Password", "hunter2", null, [], [], [], false);
        using var off = new TestLog(isEnabled: () => false);
        using var on = new TestLog();

        off.Log.Append("p", context);
        on.Log.Append("p", context);
        await off.Log.DrainAsync();
        await on.Log.DrainAsync();

        Directory.Exists(off.Directory).Should().BeFalse();
        (await File.ReadAllTextAsync(on.Log.DictationsPath)).Should().Contain("hunter2");
    }
}

public sealed class DictationLogRemovalTests
{
    private TestLog _log = null!;

    [SetUp]
    public void CreateLog() => _log = new TestLog();

    [TearDown]
    public void DeleteLog() => _log.Dispose();

    [Test]
    public async Task RemovesBothHalvesOfTheLog()
    {
        _log.Log.Append("hello", null);
        _log.Log.AppendError(new BlurtError.ApiKeyMissing(), null);
        await _log.Log.DrainAsync();
        var bothExisted = File.Exists(_log.Log.DictationsPath) && File.Exists(_log.Log.ErrorsPath);

        var removed = _log.Log.RemoveStoredLogs();

        bothExisted.Should().BeTrue();
        removed.Should().BeTrue();
        File.Exists(_log.Log.DictationsPath).Should().BeFalse();
        File.Exists(_log.Log.ErrorsPath).Should().BeFalse();
    }

    [Test]
    public void AnAbsentLogIsAlreadyInTheStateBeingAskedFor()
    {
        _log.Log.RemoveStoredLogs().Should().BeTrue();
    }

    [Test]
    public async Task RemovesTheLogDirectoryOnceItIsEmpty()
    {
        _log.Log.Append("hello", null);
        await _log.Log.DrainAsync();

        _log.Log.RemoveStoredLogs();

        Directory.Exists(_log.Directory).Should().BeFalse();
    }

    [Test]
    public async Task LeavesADirectoryHoldingAnythingElseAlone()
    {
        _log.Log.Append("hello", null);
        await _log.Log.DrainAsync();
        var bystander = Path.Combine(_log.Directory, "crash.log");
        await File.WriteAllTextAsync(bystander, "boom");

        _log.Log.RemoveStoredLogs();

        File.Exists(bystander).Should().BeTrue();
        File.Exists(_log.Log.DictationsPath).Should().BeFalse();
    }
}
