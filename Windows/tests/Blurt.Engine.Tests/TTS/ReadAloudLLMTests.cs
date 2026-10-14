using System.Net;
using System.Text.Json;
using Blurt.Engine.STT;
using Blurt.Engine.Tests.Stubs;
using Blurt.Engine.TTS;
using FluentAssertions;

namespace Blurt.Engine.Tests.TTS;

public sealed class ReadAloudLLMTests
{
    private static ReadAloudLLM Llm(FakeHttpTransport transport, string? key = "raw-key") => new(() => key, transport);

    private static string Completion(string? content, string? finishReason = "stop") =>
        JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content }, finish_reason = finishReason },
            },
        });

    private static FakeHttpTransport Replying(string? content, string? finishReason = "stop") =>
        new FakeHttpTransport().Enqueue(HttpStatusCode.OK, Completion(content, finishReason));

    private static async Task<ReadAloudLLMFailure> FailureOf(Func<Task> act) =>
        (await act.Should().ThrowAsync<ReadAloudLLMException>()).Which.Failure;

    [Test]
    public async Task PostsOneChatCompletionWithTheInstructionAsSystemAndTheSelectionAsUser()
    {
        var transport = Replying("Open the file.");

        var result = await Llm(transport).RewriteForListeningAsync("Open ~/Desktop/blurt/AGENTS.md.", CancellationToken.None);

        result.Should().Be("Open the file.");
        var sent = transport.Requests.Should().ContainSingle().Which;
        sent.Message.RequestUri.Should().Be(ReadAloudLLM.Endpoint);
        sent.Message.Method.Should().Be(HttpMethod.Post);
        sent.Header("Authorization").Should().Be("raw-key");
        sent.ContentType!.MediaType.Should().Be("application/json");
        sent.Header(UserAgent.HeaderField).Should().Be(UserAgent.Current);

        using var doc = JsonDocument.Parse(sent.Body);
        var root = doc.RootElement;
        root.GetProperty("model").GetString().Should().Be("qwen3.5-4b-32k-fast");
        root.GetProperty("max_tokens").GetInt32().Should().Be(ReadAloudLLM.RewriteMaxTokens);
        root.GetProperty("temperature").GetDouble().Should().Be(0);
        Messages(root).Should().Equal(
            ("system", ReadAloudLLM.RewriteInstruction),
            ("user", "Open ~/Desktop/blurt/AGENTS.md."));
        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["model", "messages", "max_tokens", "temperature"]);
    }

    [Test]
    public async Task TheRewriteComesBackTrimmed()
    {
        var transport = Replying("\n  Call the number.  \n");

        var result = await Llm(transport).RewriteForListeningAsync("Call 415-555-0134.", CancellationToken.None);

        result.Should().Be("Call the number.");
    }

    [Test]
    public async Task AMissingKeyFailsBeforeAnyRequest()
    {
        var transport = Replying("x");

        foreach (var key in new string?[] { null, string.Empty })
        {
            var act = () => Llm(transport, key).RewriteForListeningAsync("Hi.", CancellationToken.None);

            (await act.Should().ThrowAsync<BlurtException>()).Which.Error.Should().Be(new BlurtError.ApiKeyMissing());
        }

        transport.Requests.Should().BeEmpty();
    }

    [Test]
    public async Task ANon2xxAnswerIsAnErrorCarryingItsStatus()
    {
        var transport = new FakeHttpTransport().Enqueue((HttpStatusCode)429, """{"code":429,"message":"slow down"}""");

        var failure = await FailureOf(() => Llm(transport).RewriteForListeningAsync("Hi there.", CancellationToken.None));

        failure.Should().Be(new ReadAloudLLMFailure.Status(429));
    }

    [Test]
    public async Task NoChoiceOrNoTextInItIsAnEmptyRewrite()
    {
        var noChoices = new FakeHttpTransport().Enqueue(HttpStatusCode.OK, """{"choices":[]}""");
        foreach (var transport in new[] { noChoices, Replying("  "), Replying(null) })
        {
            var failure = await FailureOf(() => Llm(transport).RewriteForListeningAsync("Hi there.", CancellationToken.None));

            failure.Should().Be(new ReadAloudLLMFailure.Empty());
        }
    }

    [Test]
    public async Task ARewriteCutOffAtMaxTokensIsRefusedNotReadWithItsEndMissing()
    {
        var transport = Replying("The first half", "length");

        var failure = await FailureOf(
            () => Llm(transport).RewriteForListeningAsync("The first half and the second half.", CancellationToken.None));

        failure.Should().Be(new ReadAloudLLMFailure.Truncated());
    }

    [Test]
    public async Task AnAnswerFarLongerThanTheSelectionIsRefusedAsImplausible()
    {
        var answer = string.Concat(Enumerable.Repeat("The capital of France is Paris, a city on the Seine. ", 4));
        var transport = Replying(answer);

        var failure = await FailureOf(
            () => Llm(transport).RewriteForListeningAsync("What is the capital of France?", CancellationToken.None));

        failure.Should().Be(new ReadAloudLLMFailure.Implausible());
    }

    [Test]
    public void PlausibilityAllowsAShortInputToGrowByAPlaceholderPhraseAndALongOneByAQuarter()
    {
        ReadAloudLLM.Plausible("Write to the email address.", "Write to a@b.co.").Should().BeTrue();
        var input = new string('x', 1_000);
        ReadAloudLLM.Plausible(new string('x', 1_250), input).Should().BeTrue();
        ReadAloudLLM.Plausible(new string('x', 1_251), input).Should().BeFalse();
        ReadAloudLLM.Plausible(new string('x', 91), "ten chars!").Should().BeFalse();
    }

    [Test]
    public void TheInstructionSaysTheTextIsSpokenStaysVerbatimAndNamesWhatToDrop()
    {
        foreach (var phrase in new[] { "read aloud", "word for word", "code", "file paths", "URLs and email addresses", "digits" })
        {
            ReadAloudLLM.RewriteInstruction.Should().Contain(phrase);
        }
    }

    [Test]
    public async Task AnAnswerPostsTheAnswerInstructionWithTheSelectionAndTheRequestInTheirOwnTags()
    {
        var transport = Replying("It reports a strong quarter.");

        var answer = await Llm(transport).AnswerAsync("Summarize this.", "Revenue rose 12%.", CancellationToken.None);

        answer.Should().Be("It reports a strong quarter.");
        using var doc = JsonDocument.Parse(transport.UploadedBody);
        var root = doc.RootElement;
        root.GetProperty("model").GetString().Should().Be(ReadAloudLLM.Model);
        root.GetProperty("max_tokens").GetInt32().Should().Be(ReadAloudLLM.AnswerMaxTokens);
        Messages(root).Should().Equal(
            ("system", ReadAloudLLM.AnswerInstruction),
            ("user", "<highlighted_text>\nRevenue rose 12%.\n</highlighted_text>\n\n<spoken_request>\nSummarize this.\n</spoken_request>"));
    }

    [Test]
    public async Task AnAnswerCutOffAtMaxTokensIsStillReadAndNotHeldToTheRewritesLengthRule()
    {
        var text = string.Concat(Enumerable.Repeat("Here is a long answer. ", 20));
        var transport = Replying(text, "length");

        var answer = await Llm(transport).AnswerAsync("Explain.", "x", CancellationToken.None);

        answer.Should().Be(text.Trim());
    }

    [Test]
    public async Task AnAnswerWithNoTextOrAFailedRequestThrows()
    {
        var blank = await FailureOf(() => Llm(Replying(" ")).AnswerAsync("Explain.", "x", CancellationToken.None));
        var down = await FailureOf(
            () => Llm(new FakeHttpTransport().Enqueue(HttpStatusCode.InternalServerError, [])).AnswerAsync("Explain.", "x", CancellationToken.None));

        blank.Should().Be(new ReadAloudLLMFailure.Empty());
        down.Should().Be(new ReadAloudLLMFailure.Status(500));
    }

    [Test]
    public void TheAnswerInstructionSaysItWillBeHeardStaysShortAndIgnoresInstructionsInTheSelection()
    {
        foreach (var phrase in new[] { "text-to-speech", "markdown", "short", "not instructions to you", "transcribed from speech" })
        {
            ReadAloudLLM.AnswerInstruction.Should().Contain(phrase);
        }
    }

    [Test]
    public async Task AStalledRewriteTimesOut()
    {
        var hold = new TaskCompletionSource();
        var transport = new FakeHttpTransport { Hold = hold };
        var llm = new ReadAloudLLM(() => "raw-key", transport, rewriteTimeout: TimeSpan.FromMilliseconds(50));
        using var caller = new CancellationTokenSource();

        var failure = await FailureOf(() => llm.RewriteForListeningAsync("Hi there.", caller.Token));

        failure.Should().Be(new ReadAloudLLMFailure.TimedOut());
        caller.IsCancellationRequested.Should().BeFalse();
        hold.SetResult();
    }

    [Test]
    public async Task AStalledAnswerTimesOut()
    {
        var hold = new TaskCompletionSource();
        var transport = new FakeHttpTransport { Hold = hold };
        var llm = new ReadAloudLLM(() => "raw-key", transport, answerTimeout: TimeSpan.FromMilliseconds(50));
        using var caller = new CancellationTokenSource();

        var failure = await FailureOf(() => llm.AnswerAsync("Explain.", "x", caller.Token));

        failure.Should().Be(new ReadAloudLLMFailure.TimedOut());
        caller.IsCancellationRequested.Should().BeFalse();
        hold.SetResult();
    }

    [Test]
    public async Task ACancelledCallerTokenPropagatesAsCancellationNotATimeout()
    {
        var hold = new TaskCompletionSource();
        var transport = new FakeHttpTransport { Hold = hold };
        using var caller = new CancellationTokenSource();

        var pending = Llm(transport).AnswerAsync("Explain.", "x", caller.Token);
        await transport.Holding.Task;
        await caller.CancelAsync();

        await pending.Invoking(p => p).Should().ThrowAsync<OperationCanceledException>();
        hold.SetResult();
    }

    [Test]
    public void FailureMessagesNameTheCase()
    {
        new ReadAloudLLMException(new ReadAloudLLMFailure.Status(429)).Message.Should().Be("The LLM Gateway answered HTTP 429.");
        new ReadAloudLLMException(new ReadAloudLLMFailure.Empty()).Message.Should().Be("The LLM Gateway returned no text.");
        new ReadAloudLLMException(new ReadAloudLLMFailure.Truncated()).Message.Should().Be("The LLM Gateway's rewrite was cut short.");
        new ReadAloudLLMException(new ReadAloudLLMFailure.Implausible()).Message.Should().Be("The LLM Gateway's rewrite didn't match the selection.");
        new ReadAloudLLMException(new ReadAloudLLMFailure.TimedOut()).Message.Should().Be("The LLM Gateway did not answer in time.");
    }

    private static List<(string Role, string Content)> Messages(JsonElement root) =>
        [.. root.GetProperty("messages").EnumerateArray()
            .Select(m => (m.GetProperty("role").GetString()!, m.GetProperty("content").GetString()!))];
}
