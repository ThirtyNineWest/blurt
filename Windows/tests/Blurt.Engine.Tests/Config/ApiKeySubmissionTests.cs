using Blurt.Engine.Config;
using FluentAssertions;

namespace Blurt.Engine.Tests.Config;

public sealed class ApiKeySubmissionTests
{
    private static ApiKeySubmission MakeSubmission(IApiKeyGateway store, ApiKeyValidationResult result) =>
        new(store, _ => Task.FromResult(result));

    [Test]
    public async Task AKeyAssemblyAiAcceptsIsSavedAndReportedValid()
    {
        var store = new InMemoryApiKeyStore();

        var outcome = await MakeSubmission(store, ApiKeyValidationResult.Valid).SubmitAsync("sk-good");

        outcome.Should().Be(ApiKeySubmissionOutcome.Valid);
        store.Current.Should().Be("sk-good");
    }

    [Test]
    public async Task ARejectedKeyIsNeverPersisted()
    {
        var store = new InMemoryApiKeyStore();

        var outcome = await MakeSubmission(store, ApiKeyValidationResult.Invalid).SubmitAsync("sk-bad");

        outcome.Should().Be(ApiKeySubmissionOutcome.Invalid);
        store.Current.Should().BeNull();
    }

    [Test]
    public async Task ARejectedKeyNeverOverwritesThePreviouslySavedOne()
    {
        var store = new InMemoryApiKeyStore();
        store.Save("sk-old");

        var outcome = await MakeSubmission(store, ApiKeyValidationResult.Invalid).SubmitAsync("sk-bad");

        outcome.Should().Be(ApiKeySubmissionOutcome.Invalid);
        store.Current.Should().Be("sk-old");
    }

    [Test]
    public async Task AnUnreachableServerNeverPersistsTheUnverifiedKey()
    {
        var store = new InMemoryApiKeyStore();

        var outcome = await MakeSubmission(store, ApiKeyValidationResult.Unreachable).SubmitAsync("sk-maybe");

        outcome.Should().Be(ApiKeySubmissionOutcome.Unreachable);
        store.Current.Should().BeNull();
    }

    [Test]
    public async Task AValidatedKeyWhoseWriteFailsReportsSaveFailed()
    {
        var outcome = await MakeSubmission(new RejectingKeyStore(), ApiKeyValidationResult.Valid).SubmitAsync("sk-good");

        outcome.Should().Be(ApiKeySubmissionOutcome.SaveFailed);
    }

    [Test]
    public void SaveRequiresTheKeyToBeReadableBackNotJustAnAcceptedWrite()
    {
        var store = new InMemoryApiKeyStore();
        var submission = MakeSubmission(store, ApiKeyValidationResult.Valid);

        submission.Save("   ").Should().BeFalse();
        submission.Save("sk-good").Should().BeTrue();
    }

    [Test]
    public void SaveForwardsWritesToTheStoreItWasHanded()
    {
        var store = new InMemoryApiKeyStore();
        var submission = MakeSubmission(store, ApiKeyValidationResult.Valid);

        submission.Save("sk-good").Should().BeTrue();
        store.Current.Should().Be("sk-good");
    }

    [Test]
    public void AStoredKeyHasNothingToReport()
    {
        ApiKeySubmission.FailureReport(ApiKeySubmissionOutcome.Valid).Should().BeNull();
    }

    [Test]
    public void ARejectedKeyIsInlineAndRecoverable()
    {
        ApiKeySubmission.FailureReport(ApiKeySubmissionOutcome.Invalid).Should().Be(
            ApiKeyFailureReport.Inline("AssemblyAI rejected that key. Double-check it and try again."));
    }

    [Test]
    public void AnUnreachableServerIsInlineAndRecoverable()
    {
        ApiKeySubmission.FailureReport(ApiKeySubmissionOutcome.Unreachable).Should().Be(
            ApiKeyFailureReport.Inline("Couldn't reach AssemblyAI. Check your connection and try again."));
    }

    [Test]
    public void ASaveFaultIsAnAlertNotInlineText()
    {
        var report = ApiKeySubmission.FailureReport(ApiKeySubmissionOutcome.SaveFailed);

        report.Should().BeOfType<ApiKeyFailureReport.AlertReport>();
        var alert = (ApiKeyFailureReport.AlertReport)report!;
        alert.Title.Should().Be("Couldn’t Save Your Key");
        alert.Message.Should().Contain("Windows Credential Manager");
    }

    [Test]
    public void EveryFailingOutcomeReportsSomethingTheUserCanRead()
    {
        foreach (var outcome in new[] { ApiKeySubmissionOutcome.Invalid, ApiKeySubmissionOutcome.Unreachable, ApiKeySubmissionOutcome.SaveFailed })
        {
            ApiKeySubmission.FailureReport(outcome).Should().NotBeNull();
        }
    }

    private sealed class RejectingKeyStore : IApiKeyGateway
    {
        public string? Current => null;

        public bool Save(string key) => false;
    }
}
