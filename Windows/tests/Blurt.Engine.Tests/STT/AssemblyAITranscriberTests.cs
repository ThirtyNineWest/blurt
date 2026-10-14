using System.Net;
using System.Text;
using Blurt.Engine.STT;
using Blurt.Engine.Tests.Stubs;
using FluentAssertions;
using static Blurt.Engine.Tests.Stubs.TranscriberTestSupport;

namespace Blurt.Engine.Tests.STT;

public sealed class AssemblyAITranscriberTests
{
    [Test]
    public async Task PostsToTheDictationEndpointAndReturnsTheRewrittenText()
    {
        var transport = Answering("""{"text":"um hello world","llm_response":"Hello world."}""");

        var result = await CollectTranscript(MakeTranscriber("test-key", transport));

        result.Should().Be("Hello world.");
        transport.Requests.Should().ContainSingle();
        transport.Requests[0].Message.Method.Should().Be(HttpMethod.Post);
        transport.Requests[0].Message.RequestUri.Should().Be(new Uri("https://dictation.assemblyai.com/v1/transcribe/live"));
    }

    [Test]
    public async Task ACustomBaseUrlKeepsItsOwnHost()
    {
        var transport = Answering("""{"text":"ok"}""");

        await CollectTranscript(MakeTranscriber("k", transport, baseUrl: new Uri("https://eu.dictation.example")));

        transport.Requests[0].Message.RequestUri.Should().Be(new Uri("https://eu.dictation.example/v1/transcribe/live"));
    }

    private static IEnumerable<TestCaseData> RewriteSelectionCases()
    {
        yield return new TestCaseData("""{"text":"um hello","llm_response":"Hello."}""", "Hello.").SetName("UsableRewriteWins");
        yield return new TestCaseData("""{"text":"um hello","llm_response":"Hello.","llm_error":"timeout"}""", "Hello.")
            .SetName("UsableRewriteWinsEvenWithAnLlmError");
        yield return new TestCaseData("""{"text":"hello world","llm_response":null,"llm_error":"timeout"}""", "hello world")
            .SetName("NullRewriteFallsBackToVerbatim");
        yield return new TestCaseData("""{"text":"hello world"}""", "hello world").SetName("AbsentRewriteFallsBackToVerbatim");
        yield return new TestCaseData("""{"text":"hello world","llm_response":""}""", "hello world").SetName("EmptyRewriteFallsBackToVerbatim");
        yield return new TestCaseData("""{"text":"hello world","llm_response":"   \n "}""", "hello world")
            .SetName("BlankRewriteFallsBackToVerbatim");
    }

    [TestCaseSource(nameof(RewriteSelectionCases))]
    public async Task ReturnsAUsableRewriteElseTheVerbatimTranscript(string body, string expected)
    {
        var result = await CollectTranscript(MakeTranscriber("test-key", Answering(body)));

        result.Should().Be(expected);
    }

    [TestCase("""{"text":"hello","llm_response":"Hello.","session_id":"a-b-c","audio_duration_ms":1200,"request_time_ms":840.5,"sync_time_ms":610}""")]
    [TestCase("""{"text":"hello","llm_response":"Hello."}""")]
    public async Task TheResponsesMetadataDecodesAndAMissingFieldIsNotAFailure(string body)
    {
        var result = await CollectTranscript(MakeTranscriber("k", Answering(body)));

        result.Should().Be("Hello.");
    }

    [Test]
    public async Task ARealContextBuildsThePromptOnTheRequest()
    {
        var transport = Answering("""{"text":"hello world"}""");
        var context = Context(appName: "Slack", priorText: "Dear Sam,", recentTranscripts: ["Following up on yesterday."], keyTerms: ["Blurt"]);

        var result = await CollectTranscript(MakeTranscriber("test-key", transport), context: context);

        result.Should().Be("hello world");
        var config = ConfigPart(transport.UploadedBody);
        config.GetProperty("stt_prompt").GetString().Should().Be("Following up on yesterday.\nDear Sam,");
        config.GetProperty("keyterms_prompt")[0].GetString().Should().Be("Blurt");
    }

    [Test]
    public async Task SendsTheRawKeyTheBlurtAgentNoModelHeaderAndAChunkedMultipartBody()
    {
        var transport = Answering("""{"text":"ok"}""");

        (await CollectTranscript(MakeTranscriber("test-key", transport))).Should().Be("ok");

        var request = transport.Requests[0];
        request.Header("Authorization").Should().Be("test-key");
        request.Header("User-Agent").Should().Be(UserAgent.Current);
        request.Header("X-AAI-Model").Should().BeNull();
        request.ContentType!.ToString().Should().StartWith("multipart/form-data; boundary=");
        request.ContentLength.Should().BeNull();
        request.Message.Version.Should().Be(HttpVersion.Version11);
        request.Message.VersionPolicy.Should().Be(HttpVersionPolicy.RequestVersionOrHigher);
    }

    [Test]
    public void TheRequestTimeoutIsTheDocumentedNinetySeconds()
    {
        AssemblyAITranscriber.RequestTimeout.Should().Be(TimeSpan.FromSeconds(90));
    }

    [Test]
    public async Task WarmUpIssuesASingleUnauthenticatedGetToWarm()
    {
        var transport = new FakeHttpTransport { DefaultStatus = HttpStatusCode.NotFound };

        await MakeTranscriber("test-key", transport).WarmUpAsync();

        transport.Requests.Should().ContainSingle();
        var request = transport.Requests[0];
        request.Message.Method.Should().Be(HttpMethod.Get);
        request.Message.RequestUri.Should().Be(new Uri("https://dictation.assemblyai.com/warm"));
        request.Header("Authorization").Should().BeNull();
        request.Header("User-Agent").Should().Be(UserAgent.Current);
    }

    [Test]
    public async Task WarmUpSwallowsATransportFailure()
    {
        var transport = new FakeHttpTransport { Failure = new HttpRequestException("offline") };

        await FluentActions.Awaiting(() => MakeTranscriber("k", transport).WarmUpAsync()).Should().NotThrowAsync();
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task AMissingOrBlankKeyThrowsApiKeyMissingWithoutARequest(string? key)
    {
        var transport = Answering("""{"text":"never"}""");

        var thrown = await FluentActions.Awaiting(() => CollectTranscript(MakeTranscriber(key, transport)))
            .Should().ThrowAsync<BlurtException>();

        thrown.Which.Error.Should().Be(new BlurtError.ApiKeyMissing());
        transport.Requests.Should().BeEmpty();
    }

    [Test]
    public async Task AResponseWithoutTranscriptTextIsMalformed()
    {
        var thrown = await FluentActions.Awaiting(() => CollectTranscript(MakeTranscriber("test-key", Answering("""{"confidence":"0.9"}"""))))
            .Should().ThrowAsync<AssemblyAIError>();

        thrown.Which.Kind.Should().Be(AssemblyAIErrorKind.MalformedResponse);
        thrown.Which.Status.Should().BeNull();
    }

    [Test]
    public async Task ANonJsonSuccessBodyIsMalformed()
    {
        var thrown = await FluentActions.Awaiting(() => CollectTranscript(MakeTranscriber("test-key", Answering("<html>"))))
            .Should().ThrowAsync<AssemblyAIError>();

        thrown.Which.Kind.Should().Be(AssemblyAIErrorKind.MalformedResponse);
    }

    [Test]
    public async Task ANon2xxResponseThrowsAnHttpError()
    {
        var transport = Answering("""{"message":"Invalid API key"}""", HttpStatusCode.Unauthorized);

        var thrown = await FluentActions.Awaiting(() => CollectTranscript(MakeTranscriber("bad-key", transport)))
            .Should().ThrowAsync<AssemblyAIError>();

        thrown.Which.Kind.Should().Be(AssemblyAIErrorKind.Http);
        thrown.Which.Status.Should().Be(401);
    }

    [Test]
    public async Task ATransportFailurePropagatesUnwrapped()
    {
        var transport = new FakeHttpTransport { Failure = new HttpRequestException("offline") };

        await FluentActions.Awaiting(() => CollectTranscript(MakeTranscriber("k", transport)))
            .Should().ThrowExactlyAsync<HttpRequestException>();
    }

    [Test]
    public void ConfigCarriesThePromptAndTheAudioGeometry()
    {
        var config = ParseConfig(AssemblyAITranscriber.MakeConfigData(16_000, "Previous utterance.\nat the cursor", [], null));

        config.GetProperty("stt_prompt").GetString().Should().Be("Previous utterance.\nat the cursor");
        config.GetProperty("sample_rate").GetInt32().Should().Be(16_000);
        config.GetProperty("channels").GetInt32().Should().Be(1);
    }

    [Test]
    public async Task ConfigCarriesTheCleanupInstructionOnEveryRequest(
        [Values("at the cursor", "")] string prior,
        [Values(true, false)] bool enhancedTranscripts)
    {
        var transport = Answering("""{"text":"ok"}""");

        await CollectTranscript(MakeTranscriber("k", transport, enhancedTranscripts), context: Context(priorText: prior));

        var config = ConfigPart(transport.UploadedBody);
        config.GetProperty("llm_instruction").GetString().Should().Be(CleanupInstruction.Text);
        Keys(config).Should().NotContain("llm");
    }

    [Test]
    public async Task ConfigCarriesTheCustomStyleAppendedToTheCleanupInstruction()
    {
        const string Custom = "always write in lowercase";
        var transport = Answering("""{"text":"ok"}""");

        await CollectTranscript(MakeTranscriber("k", transport, customStyle: Custom));

        ConfigPart(transport.UploadedBody).GetProperty("llm_instruction").GetString().Should().Be(CleanupInstruction.Sendable(Custom));
    }

    [TestCase(null)]
    [TestCase("   \n")]
    public async Task ABlankCustomStyleLeavesTheCleanupInstructionAsShipped(string? custom)
    {
        var transport = Answering("""{"text":"ok"}""");

        await CollectTranscript(MakeTranscriber("k", transport, customStyle: custom));

        ConfigPart(transport.UploadedBody).GetProperty("llm_instruction").GetString().Should().Be(CleanupInstruction.Text);
    }

    [TestCase("""{"text":"um hello","llm_response":"Hello."}""")]
    [TestCase("""{"text":"um hello","llm_response":null,"llm_error":"timeout"}""")]
    [TestCase("""{"text":"um hello"}""")]
    public async Task EnhancedTranscriptsOffPastesTheVerbatimTranscript(string body)
    {
        var result = await CollectTranscript(MakeTranscriber("test-key", Answering(body), enhancedTranscripts: false));

        result.Should().Be("um hello");
    }

    [Test]
    public async Task AnHttpErrorCarriesTheDecodedServerMessage()
    {
        var transport = Answering("""{"error":"audio too long","error_code":"audio_too_large"}""", HttpStatusCode.RequestEntityTooLarge);

        var thrown = await FluentActions.Awaiting(() => CollectTranscript(MakeTranscriber("k", transport)))
            .Should().ThrowAsync<AssemblyAIError>();

        thrown.Which.Status.Should().Be(413);
        thrown.Which.ServerMessage.Should().Be("audio too long");
    }

    [Test]
    public async Task ADocumentedErrorShaped400SurfacesItsSentence()
    {
        var transport = Answering("""{"error":"audio part must not be empty","error_code":"bad_request"}""", HttpStatusCode.BadRequest);

        var thrown = await FluentActions.Awaiting(() => CollectTranscript(MakeTranscriber("k", transport)))
            .Should().ThrowAsync<AssemblyAIError>();

        thrown.Which.Status.Should().Be(400);
        thrown.Which.ServerMessage.Should().Be("audio part must not be empty");
    }

    [Test]
    public void AnUnknownErrorShapeFallsBackToTheRawBody()
    {
        AssemblyAITranscriber.ErrorMessage(Utf8("""{"unexpected":"shape"}""")).Should().Be("""{"unexpected":"shape"}""");
    }

    [Test]
    public void TheErrorMessageIsReadFromDetailToo()
    {
        AssemblyAITranscriber.ErrorMessage(Utf8("""{"detail":"audio required"}""")).Should().Be("audio required");
    }

    [Test]
    public void ErrorMessagePrecedenceIsErrorThenDetailAndNothingElse()
    {
        AssemblyAITranscriber.ErrorMessage(Utf8("""{"error":"a"}""")).Should().Be("a");
        AssemblyAITranscriber.ErrorMessage(Utf8("""{"detail":"c"}""")).Should().Be("c");
        AssemblyAITranscriber.ErrorMessage(Utf8("""{"error":"a","detail":"c"}""")).Should().Be("a");
        AssemblyAITranscriber.ErrorMessage(Utf8("""{"error":"invalid config part: llm: Extra inputs are not permitted"}"""))
            .Should().Be("invalid config part: llm: Extra inputs are not permitted");
        var messageShaped = AssemblyAITranscriber.ErrorMessage(Utf8("""{"message":"b"}"""));
        messageShaped.Should().NotBe("b");
        messageShaped.Should().Contain("message");
    }

    [Test]
    public void ANonStringDetailFallsBackToTheRawBody()
    {
        const string Body = """{"detail":[{"loc":["config"],"msg":"field required"}]}""";

        AssemblyAITranscriber.ErrorMessage(Utf8(Body)).Should().Be(Body);
    }

    [Test]
    public void ARawBodyErrorMessageIsCappedAt500Characters()
    {
        AssemblyAITranscriber.ErrorMessage(Utf8(new string('x', 600)))!.Length.Should().Be(500);
    }

    [Test]
    public void TheErrorMessageIsNullOnlyForAnEmptyBody()
    {
        AssemblyAITranscriber.ErrorMessage([]).Should().BeNull();
        AssemblyAITranscriber.ErrorMessage(Utf8("   \n")).Should().BeNull();
    }

    [Test]
    public void ConstructsWithProductionDefaults()
    {
        FluentActions.Invoking(() => new AssemblyAITranscriber(() => null, new FakeHttpTransport())).Should().NotThrow();
    }

    [Test]
    public void AssemblyAIErrorDescriptionsIncludeTheStatus()
    {
        AssemblyAIError.Http(500, "boom").Message.Should().Be("AssemblyAI error 500: boom");
        AssemblyAIError.Http(503, null).Message.Should().Be("AssemblyAI error 503");
        AssemblyAIError.MalformedResponse().Message.Should().NotBeNullOrEmpty();
    }

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);
}
