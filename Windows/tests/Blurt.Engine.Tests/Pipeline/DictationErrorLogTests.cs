using System.Text.Json.Nodes;
using Blurt.Engine.Diagnostics;
using Blurt.Engine.Pipeline;
using Blurt.Engine.STT;
using Blurt.Engine.Tests.Stubs;
using FluentAssertions;

namespace Blurt.Engine.Tests.Pipeline;

public sealed class DictationErrorEntryTests
{
    private static readonly DateTimeOffset Moment = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    private static readonly TranscriptionContext Context = new(
        "1Password", "Vault", "Password", "hunter2", "s3cret", ["earlier dictation"], ["Term"], false);

    [Test]
    public void RecordsTheStableKindTheHumanDescriptionAndATimestamp()
    {
        var entry = DictationLog.MakeErrorEntry(new BlurtError.ApiKeyMissing(), null, Moment);

        ((string?)entry["kind"]).Should().Be("apiKeyMissing");
        ((string?)entry["error"]).Should().Be(new BlurtError.ApiKeyMissing().Description);
        ((string?)entry["ts"]).Should().Be("2023-11-14T22:13:20.000Z");
    }

    [Test]
    public void KeepsTheWrappedErrorsDescriptionForSttFailed()
    {
        var entry = DictationLog.MakeErrorEntry(
            new BlurtError.SttFailed(new HttpRequestException("AssemblyAI error 429")), null, Moment);

        ((string?)entry["kind"]).Should().Be("sttFailed");
        ((string?)entry["error"]).Should().Contain("AssemblyAI error 429");
    }

    [Test]
    public void ThreadsTheFocusContextsAppWindowAndField()
    {
        var entry = DictationLog.MakeErrorEntry(new BlurtError.TargetAppLost(), Context, Moment);

        ((string?)entry["app"]).Should().Be("1Password");
        ((string?)entry["window"]).Should().Be("Vault");
        ((string?)entry["field"]).Should().Be("Password");
    }

    [Test]
    public void HasNoFieldForPriorTextSelectedTextOrThePrompt()
    {
        var entry = DictationLog.MakeErrorEntry(new BlurtError.TargetAppLost(), Context, Moment);

        entry.Select(kv => kv.Key).Should().Equal("app", "error", "field", "kind", "ts", "window");
        entry.ToJsonString().Should().NotContain("hunter2").And.NotContain("s3cret").And.NotContain("earlier dictation");
    }

    [Test]
    public void OmitsTheContextFieldsWhenNothingWasCaptured()
    {
        var entry = DictationLog.MakeErrorEntry(new BlurtError.ApiKeyMissing(), null, Moment);

        entry.Select(kv => kv.Key).Should().Equal("error", "kind", "ts");
    }
}

[NonParallelizable]
public sealed class DictationErrorLogTests
{
    private TestLog _log = null!;
    private Action<string>? _originalSink;

    [SetUp]
    public void CreateLog()
    {
        _log = new TestLog();
        _originalSink = BlurtLog.Sink;
    }

    [TearDown]
    public void DeleteLog()
    {
        BlurtLog.Sink = _originalSink;
        _log.Dispose();
    }

    [Test]
    public async Task CreatesTheFileOnFirstAppend()
    {
        var existedBefore = File.Exists(_log.Log.ErrorsPath);

        _log.Log.AppendError(new BlurtError.TargetAppLost(), null);
        await _log.Log.DrainAsync();

        existedBefore.Should().BeFalse();
        File.Exists(_log.Log.ErrorsPath).Should().BeTrue();
    }

    [Test]
    public async Task WritesOneJsonObjectPerLineTerminatedByLineFeed()
    {
        _log.Log.AppendError(new BlurtError.ApiKeyMissing(), null);
        await _log.Log.DrainAsync();

        var contents = await File.ReadAllTextAsync(_log.Log.ErrorsPath);
        contents.Should().EndWith("\n");
        contents.Split('\n').Should().HaveCount(2);
        ((string?)JsonNode.Parse(contents)!["kind"]).Should().Be("apiKeyMissing");
    }

    [Test]
    public async Task AppendsInOrderAndPreservesExistingEntries()
    {
        _log.Log.AppendError(new BlurtError.ApiKeyMissing(), null);
        _log.Log.AppendError(new BlurtError.TargetAppLost(), null);
        _log.Log.AppendError(new BlurtError.NoEditableTarget(), null);

        var names = await _log.FailureNamesAsync();

        names.Should().Equal("apiKeyMissing", "targetAppLost", "noEditableTarget");
    }

    [Test]
    public async Task ANullEntryFieldIsOmittedFromTheLineNotWrittenAsNull()
    {
        _log.Log.AppendError(new BlurtError.ApiKeyMissing(), null);
        await _log.Log.DrainAsync();

        var line = await File.ReadAllTextAsync(_log.Log.ErrorsPath);
        ((string?)JsonNode.Parse(line)!["kind"]).Should().Be("apiKeyMissing");
        line.Should().NotContain("\"app\"").And.NotContain("\"window\"").And.NotContain("\"field\"");
    }

    [Test]
    public async Task ADestinationThatCannotBeCreatedIsLoggedNotThrown()
    {
        var lines = new List<string>();
        BlurtLog.Sink = line =>
        {
            lock (lines)
            {
                lines.Add(line);
            }
        };
        var blocker = Path.Combine(Path.GetTempPath(), $"BlurtErrorLogBlocker-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(blocker, "a file, not a directory");
        try
        {
            var log = new DictationLog(() => true, Path.Combine(blocker, "nope"), TestLog.RenderPrompt, TestLog.FitKeyterms);

            log.AppendError(new BlurtError.TargetAppLost(), null);
            await log.DrainAsync();

            File.Exists(log.ErrorsPath).Should().BeFalse();
            lock (lines)
            {
                lines.Should().ContainSingle(l => l.Contains("errors.jsonl") && !l.Contains(blocker));
            }
        }
        finally
        {
            File.Delete(blocker);
        }
    }
}

public sealed class DictationErrorLogGateTests
{
    [Test]
    public async Task WithDeveloperModeOffAppendErrorWritesNothingToDisk()
    {
        using var log = new TestLog(isEnabled: () => false);
        var context = new TranscriptionContext("1Password", "Vault", "Password", "hunter2", null, [], [], false);

        log.Log.AppendError(new BlurtError.TargetAppLost(), context);
        await log.Log.DrainAsync();

        Directory.Exists(log.Directory).Should().BeFalse();
    }

    [Test]
    public async Task WithDeveloperModeOnAppendErrorWritesTheFailure()
    {
        using var log = new TestLog();

        log.Log.AppendError(new BlurtError.TargetAppLost(), null);

        (await log.FailureNamesAsync()).Should().Equal("targetAppLost");
    }

    [Test]
    public async Task BothLogsAnswerToTheSameSwitchReadOnEveryCall()
    {
        var enabled = true;
        using var log = new TestLog(isEnabled: () => Volatile.Read(ref enabled));
        log.Log.AppendError(new BlurtError.TargetAppLost(), null);
        log.Log.Append("logged", null);
        await log.Log.DrainAsync();

        Volatile.Write(ref enabled, false);
        log.Log.AppendError(new BlurtError.ApiKeyMissing(), null);
        log.Log.Append("private", null);

        (await log.FailureNamesAsync()).Should().Equal("targetAppLost");
        (await log.TranscriptsAsync()).Select(e => (string?)e["transcript"]).Should().Equal("logged");
    }

    [Test]
    public void TheErrorLogIsASiblingFileNotTheDictationsLog()
    {
        using var log = new TestLog();

        log.Log.ErrorsPath.Should().NotBe(log.Log.DictationsPath);
        Path.GetDirectoryName(log.Log.ErrorsPath).Should().Be(Path.GetDirectoryName(log.Log.DictationsPath));
        Path.GetFileName(log.Log.ErrorsPath).Should().Be("errors.jsonl");
    }
}
