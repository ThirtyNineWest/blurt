using System.Net;
using Blurt.Engine.Config;
using Blurt.Engine.STT;
using FakeItEasy;
using FluentAssertions;

namespace Blurt.Engine.Tests.Config;

public sealed class ApiKeyValidatorTests
{
    private static IHttpTransport MakeTransport(HttpStatusCode status) =>
        MakeTransport(() => new HttpResponseMessage(status));

    private static IHttpTransport MakeTransport(Func<HttpResponseMessage> respond)
    {
        var transport = A.Fake<IHttpTransport>();
        A.CallTo(() => transport.SendAsync(A<HttpRequestMessage>._, A<CancellationToken>._))
            .ReturnsLazily(respond);

        return transport;
    }

    [Test]
    public async Task ValidatorReturnsValidOnA2xxResponseWithTheKeyAndAgentInTheHeaders()
    {
        HttpRequestMessage? captured = null;
        var transport = A.Fake<IHttpTransport>();
        A.CallTo(() => transport.SendAsync(A<HttpRequestMessage>._, A<CancellationToken>._))
            .Invokes((HttpRequestMessage request, CancellationToken _) => captured = request)
            .ReturnsLazily(() => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await new ApiKeyValidator(transport).ValidateAsync("good-key");

        result.Should().Be(ApiKeyValidationResult.Valid);
        captured.Should().NotBeNull();
        captured!.RequestUri!.AbsolutePath.Should().Be("/v2/transcript");
        captured.RequestUri!.Query.Should().Contain("limit=1");
        captured.Headers.GetValues("Authorization").Should().Equal("good-key");
        string.Join(" ", captured.Headers.GetValues("User-Agent")).Should().Be(UserAgent.Current);
    }

    [Test]
    public async Task ValidatorReturnsInvalidWhenAssemblyAiRejectsTheKey()
    {
        var validator = new ApiKeyValidator(MakeTransport(HttpStatusCode.Unauthorized));

        var result = await validator.ValidateAsync("bad-key");

        result.Should().Be(ApiKeyValidationResult.Invalid);
    }

    [Test]
    public async Task ValidatorReturnsUnreachableOnAnUnexpectedStatus()
    {
        var validator = new ApiKeyValidator(MakeTransport(HttpStatusCode.InternalServerError));

        var result = await validator.ValidateAsync("any-key");

        result.Should().Be(ApiKeyValidationResult.Unreachable);
    }

    [Test]
    public async Task ValidatorTreatsA400MalformedRequestAsInvalid()
    {
        var validator = new ApiKeyValidator(MakeTransport(HttpStatusCode.BadRequest));

        var result = await validator.ValidateAsync("malformed-key");

        result.Should().Be(ApiKeyValidationResult.Invalid);
    }

    [TestCase(HttpStatusCode.NotFound)]
    [TestCase(HttpStatusCode.MethodNotAllowed)]
    [TestCase(HttpStatusCode.Gone)]
    [TestCase((HttpStatusCode)451)]
    public async Task A4xxThatSaysNothingAboutTheKeyIsUnreachableNotInvalid(HttpStatusCode status)
    {
        var validator = new ApiKeyValidator(MakeTransport(status));

        var result = await validator.ValidateAsync("good-key");

        result.Should().Be(ApiKeyValidationResult.Unreachable, $"status {(int)status} must not reject the key");
    }

    [TestCase(HttpStatusCode.Forbidden)]
    [TestCase((HttpStatusCode)422)]
    public async Task TheRestOfTheCredentialRejectingStatusesAlsoReportInvalid(HttpStatusCode status)
    {
        var validator = new ApiKeyValidator(MakeTransport(status));

        var result = await validator.ValidateAsync("bad-key");

        result.Should().Be(ApiKeyValidationResult.Invalid, $"status {(int)status} must reject the key");
    }

    [Test]
    public async Task ValidatorTreats429RateLimitAsUnreachable()
    {
        var validator = new ApiKeyValidator(MakeTransport((HttpStatusCode)429));

        var result = await validator.ValidateAsync("good-key");

        result.Should().Be(ApiKeyValidationResult.Unreachable);
    }

    [Test]
    public async Task ValidatorTreatsA408TimeoutAsUnreachable()
    {
        var validator = new ApiKeyValidator(MakeTransport(HttpStatusCode.RequestTimeout));

        var result = await validator.ValidateAsync("good-key");

        result.Should().Be(ApiKeyValidationResult.Unreachable);
    }

    [Test]
    public async Task ValidatorTreatsAnUnexpectedNon4xxStatusAsUnreachable()
    {
        var validator = new ApiKeyValidator(MakeTransport(HttpStatusCode.MovedPermanently));

        var result = await validator.ValidateAsync("good-key");

        result.Should().Be(ApiKeyValidationResult.Unreachable);
    }

    [Test]
    public async Task ValidatorReturnsUnreachableWhenTheRequestFailsInTransport()
    {
        var transport = A.Fake<IHttpTransport>();
        A.CallTo(() => transport.SendAsync(A<HttpRequestMessage>._, A<CancellationToken>._))
            .Throws(new HttpRequestException("offline"));

        var result = await new ApiKeyValidator(transport).ValidateAsync("good-key");

        result.Should().Be(ApiKeyValidationResult.Unreachable);
    }

    [Test]
    public async Task ValidatorTreatsBlankInputAsInvalidWithoutMakingARequest()
    {
        var transport = A.Fake<IHttpTransport>();

        var result = await new ApiKeyValidator(transport).ValidateAsync("   ");

        result.Should().Be(ApiKeyValidationResult.Invalid);
        A.CallTo(() => transport.SendAsync(A<HttpRequestMessage>._, A<CancellationToken>._)).MustNotHaveHappened();
    }
}
