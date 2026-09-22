using FluentAssertions;

namespace Blurt.Engine.Tests;

public sealed class BlurtErrorTests
{
    private static IEnumerable<BlurtError> NonWrappingCases()
    {
        yield return new BlurtError.ApiKeyMissing();
        yield return new BlurtError.TargetAppLost();
        yield return new BlurtError.NoEditableTarget();
    }

    private static IEnumerable<BlurtError> AllCases()
    {
        var e = new InvalidOperationException("x");
        yield return new BlurtError.ApiKeyMissing();
        yield return new BlurtError.SttFailed(e);
        yield return new BlurtError.TargetAppLost();
        yield return new BlurtError.AudioCaptureFailed(e);
        yield return new BlurtError.NoEditableTarget();
    }

    [TestCaseSource(nameof(NonWrappingCases))]
    public void EachNonWrappingCaseHasANonEmptyDescription(BlurtError error)
    {
        error.Description.Should().NotBeNullOrEmpty();
    }

    [Test]
    public void WrappingCasesIncludeTheUnderlyingDescription()
    {
        var underlying = new InvalidOperationException("boom-marker-42");

        BlurtError[] wrapped = [new BlurtError.SttFailed(underlying), new BlurtError.AudioCaptureFailed(underlying)];

        wrapped.Should().AllSatisfy(w => w.Description.Should().Contain("boom-marker-42"));
    }

    [Test]
    public void EqualSingletonCasesCompareEqual()
    {
        new BlurtError.ApiKeyMissing().Should().Be(new BlurtError.ApiKeyMissing());
        new BlurtError.TargetAppLost().Should().Be(new BlurtError.TargetAppLost());
        new BlurtError.NoEditableTarget().Should().Be(new BlurtError.NoEditableTarget());
    }

    [Test]
    public void DifferentSingletonCasesCompareUnequal()
    {
        ((BlurtError)new BlurtError.ApiKeyMissing()).Should().NotBe(new BlurtError.TargetAppLost());
        ((BlurtError)new BlurtError.NoEditableTarget()).Should().NotBe(new BlurtError.TargetAppLost());
    }

    [Test]
    public void WrappingCasesCompareByUnderlyingTypeAndHResultNotMessage()
    {
        var a = new InvalidOperationException("same");
        var differentIdentity = new ArgumentException("same");
        var sameIdentityOtherMessage = new InvalidOperationException("different");

        new BlurtError.SttFailed(a).Should().NotBe(new BlurtError.SttFailed(differentIdentity));
        new BlurtError.SttFailed(a).Should().Be(new BlurtError.SttFailed(sameIdentityOtherMessage));
        new BlurtError.SttFailed(a).GetHashCode().Should().Be(new BlurtError.SttFailed(sameIdentityOtherMessage).GetHashCode());
        new BlurtError.AudioCaptureFailed(a).Should().Be(new BlurtError.AudioCaptureFailed(sameIdentityOtherMessage));
    }

    [Test]
    public void WrappedEqualityRequiresBothTypeAndHResultToMatch()
    {
        var baseError = new InvalidOperationException("x") { HResult = 1 };
        var otherHResult = new InvalidOperationException("x") { HResult = 2 };
        var otherType = new ArgumentException("x") { HResult = 1 };

        new BlurtError.SttFailed(baseError).Should().NotBe(new BlurtError.SttFailed(otherHResult));
        new BlurtError.SttFailed(baseError).Should().NotBe(new BlurtError.SttFailed(otherType));
        new BlurtError.AudioCaptureFailed(baseError).Should().NotBe(new BlurtError.AudioCaptureFailed(otherHResult));
        new BlurtError.AudioCaptureFailed(baseError).Should().NotBe(new BlurtError.AudioCaptureFailed(otherType));
    }

    [Test]
    public void WrappingCasesOfDifferentKindsNeverCompareEqual()
    {
        var e = new InvalidOperationException("same");

        ((BlurtError)new BlurtError.SttFailed(e)).Should().NotBe(new BlurtError.AudioCaptureFailed(e));
    }

    [Test]
    public void DiagnosticNameIsUniqueAndNonEmptyAcrossEveryCase()
    {
        var names = AllCases().Select(c => c.DiagnosticName).ToList();

        names.Should().AllSatisfy(n => n.Should().NotBeNullOrEmpty());
        names.Should().OnlyHaveUniqueItems();
        names.Should().HaveCount(5);
    }

    [Test]
    public void DiagnosticNameIsAStableCaseLabelNotDerivedFromTheDescription()
    {
        var underlying = new InvalidOperationException("boom");

        new BlurtError.ApiKeyMissing().DiagnosticName.Should().Be("apiKeyMissing");
        new BlurtError.SttFailed(underlying).DiagnosticName.Should().Be("sttFailed");
        new BlurtError.TargetAppLost().DiagnosticName.Should().Be("targetAppLost");
        new BlurtError.AudioCaptureFailed(underlying).DiagnosticName.Should().Be("audioCaptureFailed");
        new BlurtError.NoEditableTarget().DiagnosticName.Should().Be("noEditableTarget");
        new BlurtError.SttFailed(new ArgumentException("other")).DiagnosticName
            .Should().Be(new BlurtError.SttFailed(underlying).DiagnosticName);
    }

    [Test]
    public void OnlyTheTwoCopyFallbackErrorsAreQuietDegradations()
    {
        var e = new InvalidOperationException("x");

        new BlurtError.NoEditableTarget().IsQuietDegradation.Should().BeTrue();
        new BlurtError.TargetAppLost().IsQuietDegradation.Should().BeTrue();
        new BlurtError.ApiKeyMissing().IsQuietDegradation.Should().BeFalse();
        new BlurtError.SttFailed(e).IsQuietDegradation.Should().BeFalse();
        new BlurtError.AudioCaptureFailed(e).IsQuietDegradation.Should().BeFalse();
    }

    [Test]
    public void OnlyAMissingApiKeyIsASetupBlocker()
    {
        AllCases().Where(c => c.IsSetupBlocker).Should().ContainSingle()
            .Which.Should().BeOfType<BlurtError.ApiKeyMissing>();
    }

    [Test]
    public void BlurtExceptionCarriesTheErrorAndItsDescription()
    {
        var error = new BlurtError.ApiKeyMissing();

        var exception = new BlurtException(error);

        exception.Error.Should().Be(error);
        exception.Message.Should().Be(error.Description);
    }
}
